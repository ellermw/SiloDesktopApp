using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Enumeration;
using System.Security.Cryptography;

namespace SiloPlayer.Core.Services;

// Owns the disk ledger. Only the maintenance worker inventories artwork,
// writes the index or evicts files. Foreground callers update individual keys.
internal sealed class ArtworkDiskCache : IDisposable
{
    internal const string IndexName = "artwork-index.bin";
    internal const int ProtectionLimit = 1024;
    internal static readonly TimeSpan RequestGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan Expiry = TimeSpan.FromDays(60);
    private static readonly TimeSpan ProtectionLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan ExpiryInterval = TimeSpan.FromHours(1);
    private const int IndexMagic = 0x31414353; // SCA1
    private const int DigestLength = 32;
    private const int MaxPendingChanges = 4096;
    private const int MaxDirectoryLinks = 40;
    private const string RelocatedCacheDirectoryName = "SiloPlayer.ArtworkCache";

    private readonly string _configuredDirectory;
    private readonly object _directoryGate = new();
    private volatile VerifiedDirectory? _verifiedDirectory;
    private long _nextDirectoryRetry;
    private readonly long _cap;
    private readonly long _target;
    private readonly long _maxImageBytes;
    private readonly TimeProvider _clock;
    private readonly bool _automaticMaintenance;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskCompletionSource> _deleting = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LinkedListNode<Protection>> _protected = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Protection> _protectionOrder = new();
    private HashSet<string> _changes = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _maintenance = new(1);
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _stopping;
    private readonly ITimer? _timer;
    private FileSystemWatcher? _watcher;
    private bool _started, _rescan = true, _headroom, _disposed;
    private long _bytes, _revision, _savedRevision, _nextExpiry, _nextPressure, _nextSave;
    private int _queued;

    private sealed record VerifiedDirectory(string Path, WindowsCacheDirectory.FileIdentity Identity);
    private sealed record Entry(long Size, long Written, long Used);
    private sealed record Protection(string Name, long Seen);
    private readonly record struct DiskFile(string Name, long Size, long Written, long Accessed, FileAttributes Attributes)
    {
        public bool IsRegular => (Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;

        // Reading a file can change its access time without changing the file we inspected.
        public bool MatchesMetadata(DiskFile other) => Name == other.Name && Size == other.Size
            && Written == other.Written && Attributes == other.Attributes;
    }

    internal readonly record struct MaintenanceResult(int FilesRemoved, long BytesRemoved, int FilesRemaining,
        long BytesRemaining, bool Rescanned, bool IndexLoaded, bool Trimmed);

    internal ArtworkDiskCache(string directory, long cap, long maxImageBytes, TimeProvider clock, bool automaticMaintenance)
    {
        _configuredDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        _cap = cap;
        _target = (long)(cap * 0.85m);
        _maxImageBytes = maxImageBytes;
        _clock = clock;
        _automaticMaintenance = automaticMaintenance;
        _stopping = _stop.Token;
        VerifyDirectory()?.Dispose();
        if (automaticMaintenance)
            _timer = clock.CreateTimer(_ => Schedule(), null, TimeSpan.Zero, MaintenanceInterval);
    }

    internal string DirectoryPath => _verifiedDirectory?.Path ?? _configuredDirectory;

    private WindowsCacheDirectory? VerifyDirectory()
    {
        try
        {
            Directory.CreateDirectory(_configuredDirectory);
            var linksRemaining = MaxDirectoryLinks;
            var resolved = ResolveCacheDirectory(_configuredDirectory, ref linksRemaining);
            var verified = linksRemaining == MaxDirectoryLinks
                ? WindowsCacheDirectory.Open(resolved)
                : WindowsCacheDirectory.OpenPrivate(resolved, RelocatedCacheDirectoryName, create: true);
            if (verified != null)
            {
                _verifiedDirectory = new VerifiedDirectory(verified.Path, verified.Identity);
                return verified;
            }
        }
        catch (Exception error) when (WindowsCacheDirectory.IsUnavailable(error)) { }
        Volatile.Write(ref _nextDirectoryRetry, Now + MaintenanceInterval.Ticks);
        return null;
    }

    private static string ResolveCacheDirectory(string directory, ref int linksRemaining)
    {
        // Walk from the root so links above the cache (or above a link's target)
        // cannot hide behind an ordinary leaf. Keep using the resolved path thereafter.
        var components = new Stack<string>();
        var root = new DirectoryInfo(directory);
        while (root.Parent is { } parent)
        {
            components.Push(root.Name);
            root = parent;
        }

        var path = root.FullName;
        while (true)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0)
                throw new IOException($"Artwork cache path is not a directory: {path}");
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if (linksRemaining == 0 || Directory.ResolveLinkTarget(path, returnFinalTarget: false) is not { } target)
                    throw new IOException($"Cannot resolve artwork cache directory: {path}");
                linksRemaining--;
                path = ResolveCacheDirectory(target.FullName, ref linksRemaining);
            }
            if (!components.TryPop(out var component)) return path;
            path = Path.Combine(path, component);
        }
    }

    private long Now => _clock.GetUtcNow().UtcTicks;

    internal WindowsCacheDirectory? OpenDirectory()
    {
        var expected = _verifiedDirectory;
        if (expected == null)
        {
            // Only one retry touches the filesystem. Other requests can use
            // the memory fallback rather than waiting for slow storage.
            if (Now < Volatile.Read(ref _nextDirectoryRetry) || !Monitor.TryEnter(_directoryGate)) return null;
            try
            {
                expected = _verifiedDirectory;
                if (expected == null)
                    return Now >= _nextDirectoryRetry && !_stopping.IsCancellationRequested ? VerifyDirectory() : null;
            }
            finally { Monitor.Exit(_directoryGate); }
        }
        try
        {
            // After the first success, never adopt a replacement directory.
            var verified = WindowsCacheDirectory.Open(expected.Path);
            if (verified.Identity == expected.Identity) return verified;
            verified.Dispose();
        }
        catch (Exception error) when (WindowsCacheDirectory.IsUnavailable(error)) { }
        return null;
    }

    internal Use BeginUse(string path)
    {
        var name = Path.GetFileName(path);
        lock (_gate)
        {
            _active[name] = _active.GetValueOrDefault(name) + 1;
            TouchLocked(name);
            return new Use(this, name, _deleting.GetValueOrDefault(name)?.Task ?? Task.CompletedTask);
        }
    }

    internal sealed class Use(ArtworkDiskCache owner, string name, Task ready) : IDisposable
    {
        // A request arriving after eviction commits can start its transfer at
        // once. Publishing a replacement waits for that one deletion syscall.
        internal Task Ready { get; } = ready;

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (owner._active[name] == 1) owner._active.Remove(name);
                else owner._active[name]--;
            }
        }
    }

    internal void Touch(string name)
    {
        lock (_gate) TouchLocked(name);
    }

    private void TouchLocked(string name)
    {
        if (_entries.TryGetValue(name, out var known))
        {
            _entries[name] = known with { Used = Now };
            _revision++;
        }
    }

    internal void Protect(string name)
    {
        lock (_gate)
        {
            var now = Now;
            if (_protected.TryGetValue(name, out var node))
            {
                // Home can project a row several times in one load.
                if (now - node.Value.Seen < TimeSpan.TicksPerMinute) return;
                _protectionOrder.Remove(node);
            }
            SetProtection(new Protection(name, now));
        }
    }

    private void SetProtection(Protection protection)
    {
        _protected[protection.Name] = _protectionOrder.AddLast(protection);
        if (_protected.Count > ProtectionLimit)
        {
            _protected.Remove(_protectionOrder.First!.Value.Name);
            _protectionOrder.RemoveFirst();
        }
        _revision++;
    }

    internal bool TryUseFile(string path)
    {
        using var directory = OpenDirectory();
        return directory != null && TryUseFile(directory, Path.GetFileName(path));
    }

    internal bool TryUseFile(WindowsCacheDirectory directory, string name)
    {
        var file = ReadFile(name);
        try
        {
            if (file is { } candidate && IsUsable(candidate, Path.Combine(directory.Path, name)))
            {
                Record(candidate, used: true);
                return true;
            }
        }
        catch (IOException error) when (IsSharingViolation(error)) { return false; }
        if (file.HasValue) QueueChange(name);
        else Forget(name);
        return false;
    }

    internal void RecordDownload(string path)
    {
        if (ReadFile(Path.GetFileName(path)) is { } file)
            Record(file, used: true);
    }

    private void Record(DiskFile file, bool used)
    {
        bool pressure;
        lock (_gate)
        {
            var now = Now;
            _entries.TryGetValue(file.Name, out var previous);
            // Use legacy access times only when there is no matching app record.
            var seen = used ? now : previous is not null && previous.Written == file.Written && previous.Size == file.Size
                ? previous.Used : Math.Min(now, Math.Max(file.Written, file.Accessed));
            var updated = new Entry(file.Size, file.Written, seen);
            if (updated == previous) return;
            _entries[file.Name] = updated;
            _bytes += file.Size - (previous?.Size ?? 0);
            _revision++;
            pressure = _cap > 0 && _bytes > _cap && now >= _nextPressure;
        }
        if (pressure) Schedule();
    }

    private void Forget(string name)
    {
        lock (_gate)
        {
            if (!_entries.TryRemove(name, out var entry)) return;
            _bytes -= entry.Size;
            _revision++;
        }
    }

    private bool IsUsable(DiskFile file, string path, bool verifyAccess = false)
    {
        if (!file.IsRegular || file.Size <= 0 || file.Size > _maxImageBytes) return false;
        var validated = _entries.TryGetValue(file.Name, out var known)
            && known.Size == file.Size && known.Written == file.Written;
        if (validated && !verifyAccess) return true;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1);
            return validated || !ImageService.LooksLikeDocument(stream);
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException error) when (IsSharingViolation(error)) { throw; }
        catch (IOException) { return false; }
    }

    private DiskFile? ReadFile(string name)
    {
        try
        {
            var file = new FileInfo(Path.Combine(DirectoryPath, name));
            var attributes = file.Attributes;
            if (attributes == (FileAttributes)(-1)) return null;
            var regular = (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
            return new DiskFile(name, regular ? file.Length : 0, file.LastWriteTimeUtc.Ticks,
                file.LastAccessTimeUtc.Ticks, attributes);
        }
        catch (Exception error) when (IsFileError(error)) { return null; }
    }

    private static bool IsCacheName(ReadOnlySpan<char> name) => name.Length == 20 && name.EndsWith(".img", StringComparison.OrdinalIgnoreCase)
        && ulong.TryParse(name[..16], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);

    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException;
    private static bool IsSharingViolation(IOException error) => (error.HResult & 0xffff) is 32 or 33;

    private void QueueChange(string name)
    {
        if (name.Length == 0 || name is "." or ".." || Path.GetFileName(name) != name
            || string.Equals(name, IndexName, StringComparison.OrdinalIgnoreCase)) return;
        lock (_gate)
        {
            if (_rescan) return;
            _changes.Add(name);
            if (_changes.Count >= MaxPendingChanges) _rescan = true;
        }
    }

    private void StartWatcher()
    {
        try
        {
            var watcher = new FileSystemWatcher(DirectoryPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size
                    | NotifyFilters.LastWrite | NotifyFilters.Security,
                InternalBufferSize = 32768
            };
            watcher.Created += (_, args) => QueueChange(args.Name ?? "");
            watcher.Deleted += (_, args) => QueueRemoval(args.Name);
            watcher.Changed += (_, args) => QueueChange(args.Name ?? "");
            watcher.Renamed += (_, args) => { QueueRemoval(args.OldName); QueueChange(args.Name ?? ""); };
            watcher.Error += (_, _) => { lock (_gate) _rescan = true; };
            lock (_gate)
            {
                if (_disposed) { watcher.Dispose(); return; }
                _watcher = watcher;
                watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception error) when (IsFileError(error) || error is ArgumentException)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void QueueRemoval(string? name)
    {
        if (name == null) return;
        lock (_gate)
            if (!_deleting.ContainsKey(name) && (_active.ContainsKey(name) || _entries.ContainsKey(name))) QueueChange(name);
    }

    private void Schedule()
    {
        if (!_automaticMaintenance || _stopping.IsCancellationRequested || Interlocked.Exchange(ref _queued, 1) == 1) return;
        _ = RunScheduledPassAsync();
    }

    private async Task RunScheduledPassAsync()
    {
        try { await MaintainAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        finally { Volatile.Write(ref _queued, 0); }
        // A download can cross the cap while a persistence-only pass runs.
        // Recheck after clearing _queued so that crossing is not lost.
        if (_cap > 0 && Interlocked.Read(ref _bytes) > _cap && Now >= Interlocked.Read(ref _nextPressure)) Schedule();
    }

    internal async Task<MaintenanceResult> MaintainAsync()
    {
        await _maintenance.WaitAsync(_stopping).ConfigureAwait(false);
        try { return await Task.Run(Maintain, _stopping).ConfigureAwait(false); }
        finally { _maintenance.Release(); }
    }

    private MaintenanceResult Maintain()
    {
        var start = Stopwatch.GetTimestamp();
        var now = Now;
        var removed = new Removals();
        var scanned = false;
        var loaded = false;
        var trimmed = false;
        try
        {
            _stopping.ThrowIfCancellationRequested();
            using var directory = OpenDirectory();
            if (directory == null)
            {
                Interlocked.Exchange(ref _nextPressure, now + MaintenanceInterval.Ticks);
                return default;
            }
            if (!_started)
            {
                loaded = LoadIndex(now);
                if (!loaded && ReadFile(IndexName) != null) _savedRevision = -1;
                _started = true;
            }
            if (_watcher == null) StartWatcher();
            HashSet<string> changes;
            lock (_gate)
            {
                scanned = _rescan;
                _rescan = _watcher == null;
                changes = _changes;
                _changes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            if (scanned) Reconcile(directory, removed);
            else foreach (var name in changes)
            {
                _stopping.ThrowIfCancellationRequested();
                ReconcileFile(directory, name, ReadFile(name), removed, verifyAccess: true);
            }

            if (now >= _nextExpiry)
            {
                foreach (var pair in _entries)
                {
                    _stopping.ThrowIfCancellationRequested();
                    if (pair.Value.Used <= now - Expiry.Ticks)
                        Delete(directory, pair.Key, pair.Value, removed);
                }
                _nextExpiry = now + ExpiryInterval.Ticks;
            }

            lock (_gate)
            {
                while (_protectionOrder.First is { Value: var oldest } && oldest.Seen <= now - ProtectionLifetime.Ticks)
                {
                    _protected.Remove(oldest.Name);
                    _protectionOrder.RemoveFirst();
                    _revision++;
                }
                _headroom |= _cap > 0 && _bytes > _cap;
                trimmed = _headroom;
                if (trimmed) _nextPressure = now + MaintenanceInterval.Ticks;
            }
            if (trimmed) Trim(directory, removed);

            if (now >= _nextSave)
            {
                SaveIndex(directory);
                _nextSave = now + MaintenanceInterval.Ticks;
            }
        }
        catch (Exception error) when (IsFileError(error))
        {
            // The directory can be removed or temporarily unavailable. Keep
            // the live ledger and retry reconciliation on the next pass.
            lock (_gate)
            {
                _rescan = true;
                _nextPressure = now + MaintenanceInterval.Ticks;
            }
        }
        finally
        {
            if (trimmed || removed.Count > 0)
                LocalLog.AppendLine("library_performance.txt", $"artwork_trim | ms={Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} | files_removed={removed.Count} | bytes_removed={removed.Bytes} | files_remaining={_entries.Count} | bytes_remaining={Interlocked.Read(ref _bytes)} | rescan={scanned}");
        }
        return new MaintenanceResult(removed.Count, removed.Bytes, _entries.Count, Interlocked.Read(ref _bytes), scanned, loaded, trimmed);
    }

    private void Reconcile(WindowsCacheDirectory directory, Removals removed)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var before = _entries.ToArray();
        var files = new FileSystemEnumerable<DiskFile>(directory.Path,
            (ref FileSystemEntry file) => new(file.FileName.ToString(), file.Length, file.LastWriteTimeUtc.UtcTicks,
                file.LastAccessTimeUtc.UtcTicks, file.Attributes),
            new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false });
        foreach (var file in files)
        {
            _stopping.ThrowIfCancellationRequested();
            seen.Add(file.Name);
            ReconcileFile(directory, file.Name, file, removed, verifyAccess: false);
        }
        foreach (var pair in before)
        {
            _stopping.ThrowIfCancellationRequested();
            if (!seen.Contains(pair.Key)) ReconcileFile(directory, pair.Key, ReadFile(pair.Key), removed, verifyAccess: false);
        }
    }

    private void ReconcileFile(WindowsCacheDirectory directory, string name, DiskFile? file, Removals removed, bool verifyAccess)
    {
        if (string.Equals(name, IndexName, StringComparison.OrdinalIgnoreCase)) return;
        Entry? observed;
        lock (_gate)
        {
            if (_active.ContainsKey(name))
            {
                QueueChange(name);
                return;
            }
            if (file == null) { Forget(name); return; }
            _entries.TryGetValue(name, out observed);
        }

        try
        {
            var isCacheName = IsCacheName(name);
            var path = Path.Combine(directory.Path, name);
            if (isCacheName && IsUsable(file.Value, path, verifyAccess))
            {
                // An enumeration entry may predate a replacement committed by
                // a download. Re-read the metadata only when it disagrees.
                if (observed != null && (observed.Written != file.Value.Written || observed.Size != file.Value.Size)
                    && (ReadFile(name) is not { } actual || !actual.MatchesMetadata(file.Value)))
                {
                    QueueChange(name);
                    return;
                }
                lock (_gate)
                {
                    if (!_active.ContainsKey(name) && ReferenceEquals(_entries.GetValueOrDefault(name), observed))
                        Record(file.Value, used: false);
                    else QueueChange(name);
                }
                return;
            }
            Delete(directory, name, expected: null, removed, file);
        }
        catch (IOException error) when (IsSharingViolation(error)) { QueueChange(name); }
    }

    private void Trim(WindowsCacheDirectory directory, Removals removed)
    {
        HashSet<string> protectedNames;
        lock (_gate) protectedNames = new HashSet<string>(_protected.Keys, StringComparer.OrdinalIgnoreCase);
        var order = _entries.OrderBy(pair => pair.Value.Used).ToArray();
        foreach (var pair in order)
        {
            _stopping.ThrowIfCancellationRequested();
            if (Interlocked.Read(ref _bytes) <= _target) break;
            if (!protectedNames.Contains(pair.Key)) Delete(directory, pair.Key, pair.Value, removed, respectProtection: true);
        }

        if (Interlocked.Read(ref _bytes) > _cap && !_entries.Any(pair => !protectedNames.Contains(pair.Key)))
        {
            foreach (var pair in order)
            {
                _stopping.ThrowIfCancellationRequested();
                if (Interlocked.Read(ref _bytes) <= _target) break;
                if (protectedNames.Contains(pair.Key)) Delete(directory, pair.Key, pair.Value, removed);
            }
        }

        var unprotectedRemain = Interlocked.Read(ref _bytes) > _target && _entries.Any(pair => !protectedNames.Contains(pair.Key));
        lock (_gate) _headroom = _bytes > _cap || unprotectedRemain;
    }

    private sealed class Removals
    {
        public int Count;
        public long Bytes;
    }

    private void Delete(WindowsCacheDirectory directory, string name, Entry? expected, Removals removed,
        DiskFile? stray = null, bool respectProtection = false)
    {
        _stopping.ThrowIfCancellationRequested();
        TaskCompletionSource completion;
        lock (_gate)
        {
            _entries.TryGetValue(name, out var current);
            if (_active.ContainsKey(name) || (respectProtection && _protected.ContainsKey(name))
                || (expected != null && (!ReferenceEquals(current, expected) || current.Used > Now - RequestGrace.Ticks)))
                return;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _deleting.Add(name, completion);
        }
        try
        {
            using var handle = directory.OpenForDeletion(name);
            var (attributes, size, written) = WindowsCacheDirectory.Metadata(handle);
            var regular = (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
            var actual = new DiskFile(name, regular ? size : 0, written, 0, attributes);
            if ((stray is { } reviewed && !actual.MatchesMetadata(reviewed))
                || (expected != null && (actual.Written != expected.Written || actual.Size != expected.Size || !actual.IsRegular)))
            {
                QueueChange(name);
                return;
            }
            _stopping.ThrowIfCancellationRequested();
            WindowsCacheDirectory.Delete(handle);
            Forget(name);
            removed.Count++;
            removed.Bytes += actual.Size;
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 2 or 3) { Forget(name); }
        catch (Exception error) when (IsFileError(error))
        {
            // Sharing violations, including an external decoder, are retried.
            if (stray != null) QueueChange(name);
        }
        finally
        {
            lock (_gate) _deleting.Remove(name);
            completion.SetResult();
        }
    }

    private bool LoadIndex(long now)
    {
        var path = Path.Combine(DirectoryPath, IndexName);
        try
        {
            if (ReadFile(IndexName) is not { IsRegular: true, Size: >= 44 and <= 64 * 1024 * 1024 }) return false;
            var data = File.ReadAllBytes(path);
            var end = data.Length - DigestLength;
            if (end < 12 || !SHA256.HashData(data.AsSpan(0, end)).AsSpan().SequenceEqual(data.AsSpan(end))) return false;
            using var reader = new BinaryReader(new MemoryStream(data, 0, end));
            if (reader.ReadInt32() != IndexMagic) return false;
            var count = reader.ReadInt32();
            var protectedCount = reader.ReadInt32();
            if (count < 0 || protectedCount is < 0 or > ProtectionLimit || end != 12L + count * 32L + protectedCount * 16L)
                return false;

            for (var i = 0; i < count; i++)
            {
                _stopping.ThrowIfCancellationRequested();
                var name = IndexFileName(reader.ReadUInt64());
                var size = reader.ReadInt64();
                var written = reader.ReadInt64();
                var seen = Math.Min(now, reader.ReadInt64());
                if (size <= 0 || size > _maxImageBytes || written <= 0 || seen <= 0) continue;
                lock (_gate)
                {
                    // Only validated artwork is recorded; matching size and write time reuse that check.
                    if (_entries.TryAdd(name, new Entry(size, written, seen))) _bytes += size;
                }
            }
            var restoredProtection = new List<Protection>(protectedCount);
            for (var i = 0; i < protectedCount; i++)
            {
                _stopping.ThrowIfCancellationRequested();
                var name = IndexFileName(reader.ReadUInt64());
                var seen = Math.Min(now, reader.ReadInt64());
                if (seen > now - ProtectionLifetime.Ticks) restoredProtection.Add(new Protection(name, seen));
            }
            lock (_gate)
            {
                var merged = restoredProtection.Concat(_protectionOrder)
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.MaxBy(item => item.Seen)!)
                    .OrderBy(item => item.Seen).TakeLast(ProtectionLimit).ToArray();
                _protected.Clear();
                _protectionOrder.Clear();
                foreach (var item in merged) SetProtection(item);
            }
            return true;
        }
        catch (Exception error) when (IsFileError(error)) { return false; }
    }

    private static string IndexFileName(ulong hash) => $"{hash:X16}.img";

    private void SaveIndex(WindowsCacheDirectory directory)
    {
        long revision;
        Protection[] protection;
        lock (_gate)
        {
            revision = _revision;
            if (revision == _savedRevision) return;
            protection = [.. _protectionOrder];
        }
        // Concurrent enumeration avoids stopping readers for a full dictionary copy.
        var entries = _entries.Select(static pair => pair).ToArray();
        using var data = new MemoryStream(12 + entries.Length * 32 + protection.Length * 16 + DigestLength);
        using var writer = new BinaryWriter(data);
        writer.Write(IndexMagic);
        writer.Write(entries.Length);
        writer.Write(protection.Length);
        foreach (var pair in entries)
        {
            _stopping.ThrowIfCancellationRequested();
            writer.Write(ulong.Parse(pair.Key.AsSpan(0, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
            writer.Write(pair.Value.Size);
            writer.Write(pair.Value.Written);
            writer.Write(pair.Value.Used);
        }
        foreach (var item in protection)
        {
            writer.Write(ulong.Parse(item.Name.AsSpan(0, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
            writer.Write(item.Seen);
        }
        writer.Write(SHA256.HashData(data.GetBuffer().AsSpan(0, (int)data.Length)));
        _stopping.ThrowIfCancellationRequested();
        using var staged = directory.Stage($"{IndexName}.{Guid.NewGuid():N}.tmp");
        staged.Stream.Write(data.GetBuffer(), 0, (int)data.Length);
        _stopping.ThrowIfCancellationRequested();
        staged.Publish(IndexName);
        _savedRevision = revision;
    }

    public void Dispose()
    {
        FileSystemWatcher? watcher;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            watcher = _watcher;
        }
        _stop.Cancel();
        _timer?.Dispose();
        watcher?.Dispose();
        _stop.Dispose();
    }
}
