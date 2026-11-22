using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace SiloPlayer.Core.Services;

public class ImageService : IDisposable
{
    private readonly string _diskCacheDir;
    private readonly long _maxMemoryBytes;
    private readonly long _maxDiskBytes;
    private readonly int _maxNegativeCacheEntries;
    private readonly long _maxImageBytes;
    private readonly TimeSpan _downloadTimeout;
    // Entries written before downloads were checked may hold a document or an
    // oversized body. Each cached file is checked once per process.
    private readonly ConcurrentDictionary<string, byte> _checkedDiskFiles = new();
    private readonly ConcurrentDictionary<string, byte[]> _memoryCache = new();
    private readonly ConcurrentQueue<string> _evictionOrder = new();
    private long _currentMemoryBytes;
    private readonly SemaphoreSlim _downloadLock = new(16);
    private readonly ConcurrentDictionary<string, InflightDownload> _inflightDownloads = new();
    private readonly ConcurrentDictionary<string, long> _negativeCache = new();
    private readonly HashSet<string> _missingImages = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _negativeEvictionOrder = new();
    private readonly object _negativeCacheSync = new();
    private static readonly TimeSpan DiskTrimInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromSeconds(30);
    private long _lastDiskTrimTicks;
    private int _diskTrimQueued;

    public ImageService(
        string diskCacheDir,
        long maxMemoryCacheBytes = 200 * 1024 * 1024,
        long maxDiskCacheBytes = 2L * 1024 * 1024 * 1024,
        int maxNegativeCacheEntries = 2048,
        long maxImageBytes = 64 * 1024 * 1024,
        TimeSpan? downloadTimeout = null)
    {
        _diskCacheDir = diskCacheDir;
        _maxMemoryBytes = maxMemoryCacheBytes;
        _maxDiskBytes = maxDiskCacheBytes;
        _maxNegativeCacheEntries = Math.Max(0, maxNegativeCacheEntries);
        _maxImageBytes = maxImageBytes;
        _downloadTimeout = downloadTimeout ?? TimeSpan.FromSeconds(30);
        if (_downloadTimeout <= TimeSpan.Zero || _downloadTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(downloadTimeout));
        Directory.CreateDirectory(diskCacheDir);
    }

    public Task<byte[]?> GetImageAsync(string contentId, string imageType, string url, HttpClient http, CancellationToken ct = default)
    {
        if (NativeApiRetirement.IsRetiredResource(url)) return Task.FromResult<byte[]?>(null);
        var cacheKey = BuildCacheKey(contentId, imageType, url);
        var negativeCacheKey = BuildNegativeCacheKey(cacheKey, url);

        // 1. Memory cache hit — synchronous fast path. No I/O, safe to run
        //    from any thread including the UI thread.
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
            return Task.FromResult<byte[]?>(cached);

        // 2/3. Disk or network path — ALL sync I/O (File.Exists, disk reads,
        //      disk writes) is done inside a Task.Run so the UI thread never
        //      blocks on the filesystem. Under contention (many downloads
        //      completing concurrently), File.Exists alone can take 50-100ms
        //      per call; times N visible cards that's a multi-second freeze.
        return Task.Run(async () =>
        {
            var diskPath = GetDiskPath(cacheKey);
            if (IsUsableDiskFile(diskPath))
            {
                var diskBytes = await File.ReadAllBytesAsync(diskPath, ct).ConfigureAwait(false);
                TouchDiskFile(diskPath);
                AddToMemoryCache(cacheKey, diskBytes);
                return (byte[]?)diskBytes;
            }

            if (IsNegativeCacheHit(negativeCacheKey))
                return null;

            var downloadedPath = await GetOrStartDownloadAsync(negativeCacheKey, diskPath, url, http, ct)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(downloadedPath) || !File.Exists(downloadedPath))
                return null;

            var downloadedBytes = await File.ReadAllBytesAsync(downloadedPath, ct).ConfigureAwait(false);
            TouchDiskFile(downloadedPath);
            AddToMemoryCache(cacheKey, downloadedBytes);
            return downloadedBytes;
        }, ct);
    }

    /// <summary>
    /// Distinguishes a definitive missing image from a transient null download
    /// result, including a rejected proxy document. Uses exact-URL cache identity.
    /// </summary>
    public bool IsKnownUnavailable(string contentId, string imageType, string url)
    {
        if (NativeApiRetirement.IsRetiredResource(url)) return true;
        var key = BuildNegativeCacheKey(BuildCacheKey(contentId, imageType, url), url);
        lock (_negativeCacheSync)
            return IsNegativeCacheHit(key) && _missingImages.Contains(key);
    }

    /// <summary>
    /// Ensures the image for the given cache key exists on disk (downloading
    /// if necessary) and returns the local path. Lets callers point
    /// <c>BitmapImage.UriSource</c> at a file:// URI, which is significantly
    /// faster than <c>SetSourceAsync</c> on a <c>MemoryStream</c> because
    /// WinUI's native decoder does the work off the UI thread without the
    /// COM interop overhead of <c>AsRandomAccessStream</c>.
    /// Returns null when download fails. With <paramref name="waitForSharedDownload"/>,
    /// cancellation prevents starting a download but does not abandon an existing
    /// shared download's completion, allowing warmup callers to retain their throttle slot.
    /// </summary>
    public Task<string?> GetImageDiskPathAsync(string contentId, string imageType, string url, HttpClient http, CancellationToken ct = default, bool waitForSharedDownload = false)
    {
        if (NativeApiRetirement.IsRetiredResource(url)) return Task.FromResult<string?>(null);
        var cacheKey = BuildCacheKey(contentId, imageType, url);
        var negativeCacheKey = BuildNegativeCacheKey(cacheKey, url);

        // All sync I/O (File.Exists) goes on the thread pool so the UI thread
        // never blocks on disk ops. Fast-path: if download is already inflight,
        // reuse its Task.
        return Task.Run(async () =>
        {
            var diskPath = GetDiskPath(cacheKey);
            if (IsUsableDiskFile(diskPath))
            {
                TouchDiskFile(diskPath);
                return (string?)diskPath;
            }

            if (IsNegativeCacheHit(negativeCacheKey))
                return null;

            ct.ThrowIfCancellationRequested();
            return await GetOrStartDownloadAsync(negativeCacheKey, diskPath, url, http, ct, waitForSharedDownload)
                .ConfigureAwait(false);
        }, ct);
    }

    /// <summary>
    /// One shared download per exact URL. <see cref="Waiters"/> counts the callers still waiting for it;
    /// the state is guarded by a lock on the instance.
    /// </summary>
    private sealed class InflightDownload(string key)
    {
        public readonly string Key = key;
        public Task<string?>? Completion;
        public CancellationTokenSource? SlotWait;
        public int Waiters;
        public bool SlotHeld;
        // Abandoned or finished: later callers start their own download.
        public bool Closed;
    }

    private async Task<string?> GetOrStartDownloadAsync(
        string negativeCacheKey,
        string diskPath,
        string url,
        HttpClient http,
        CancellationToken ct,
        bool waitForSharedDownload = false)
    {
        var download = JoinInflightDownload(negativeCacheKey, diskPath, url, http);
        try
        {
            return await (waitForSharedDownload ? download.Completion! : download.Completion!.WaitAsync(ct)).ConfigureAwait(false);
        }
        finally
        {
            LeaveInflightDownload(download);
        }
    }

    private InflightDownload JoinInflightDownload(
        string negativeCacheKey,
        string diskPath,
        string url,
        HttpClient http)
    {
        // The disk identity intentionally ignores expiring signatures, but an
        // in-flight request may already be using a signature that the CDN has
        // rejected. Share only callers using the exact same URL. A refreshed
        // signature then waits on the common disk lock and either reuses the
        // old request's successful file or performs its own recovery request.
        var inflightKey = negativeCacheKey;
        while (true)
        {
            var download = _inflightDownloads.GetOrAdd(inflightKey, static key => new InflightDownload(key));
            lock (download)
            {
                if (!download.Closed)
                {
                    download.Waiters++;
                    if (download.Completion == null)
                    {
                        download.SlotWait = new CancellationTokenSource();
                        download.Completion = DownloadAndCacheToDiskAsync(
                            download,
                            download.SlotWait.Token,
                            diskPath,
                            url,
                            http,
                            negativeCacheKey);
                        download.Completion.ContinueWith(
                            _ => CloseInflightDownload(download),
                            CancellationToken.None,
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                    }
                    return download;
                }
            }

            // Closed, and its removal may not have run yet.
            RemoveInflightDownload(download);
        }
    }

    private void LeaveInflightDownload(InflightDownload download)
    {
        lock (download)
        {
            if (--download.Waiters > 0 || download.SlotHeld || download.Closed)
                return;

            // Nobody waits for it and its transfer has not started: take it out of
            // the slot queue, so posters that scrolled away do not hold back the
            // ones now on screen. A transfer that holds a slot runs to its deadline.
            download.Closed = true;
            RemoveInflightDownload(download);
            download.SlotWait!.Cancel();
            download.SlotWait.Dispose();
        }
    }

    private void CloseInflightDownload(InflightDownload download)
    {
        bool abandoned;
        lock (download)
        {
            abandoned = download.Closed;
            download.Closed = true;
        }

        // An abandoned download's slot wait is cancelled and disposed by the caller that left it.
        if (abandoned)
            return;
        RemoveInflightDownload(download);
        download.SlotWait!.Dispose();
    }

    private void RemoveInflightDownload(InflightDownload download)
    {
        ((ICollection<KeyValuePair<string, InflightDownload>>)_inflightDownloads)
            .Remove(new KeyValuePair<string, InflightDownload>(download.Key, download));
    }

    private async Task<string?> DownloadAndCacheToDiskAsync(
        InflightDownload download,
        CancellationToken slotWait,
        string diskPath,
        string url,
        HttpClient http,
        string negativeCacheKey)
    {
        // The slot wait has no deadline: a download queued behind a full poster grid or a slow server must still run while a
        // caller waits for it, so the deadline bounds only the transfer and starts once the slot is held. The wait ends early
        // only when every caller has left (LeaveInflightDownload); one caller's cancellation abandons its own wait, not this
        // shared download.
        try
        {
            await _downloadLock.WaitAsync(slotWait).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (slotWait.IsCancellationRequested)
        {
            return null;
        }
        using var deadline = new CancellationTokenSource(_downloadTimeout);
        var ct = deadline.Token;
        string? tempPath = null;
        try
        {
            lock (download)
            {
                // Abandoned as the slot was granted.
                if (download.Closed)
                    return null;
                download.SlotHeld = true;
            }

            // Double-check after acquiring lock
            if (IsUsableDiskFile(diskPath))
            {
                TouchDiskFile(diskPath);
                return diskPath;
            }

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
                    SetNegativeCache(negativeCacheKey, missing: true);
                return null;
            }

            // The disk file has no expiry, so a proxy or captive-portal page
            // returned with 200 would replace the artwork until the cache is
            // trimmed. An untyped body stays allowed; many CDNs omit the type.
            if (IsDocumentMediaType(response.Content.Headers.ContentType?.MediaType)
                || response.Content.Headers.ContentLength > _maxImageBytes)
            {
                SetNegativeCache(negativeCacheKey);
                return null;
            }

            RemoveNegativeCache(negativeCacheKey);

            tempPath = $"{diskPath}.{Guid.NewGuid():N}.tmp";
            var withinLimit = true;
            await using (var remoteStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var fileStream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                // Callers read the whole file into memory; bound bodies that
                // omit or misreport their length.
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await remoteStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    if (fileStream.Length + read > _maxImageBytes)
                    {
                        withinLimit = false;
                        break;
                    }
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
                await fileStream.FlushAsync(ct).ConfigureAwait(false);
            }

            if (!withinLimit || LooksLikeDocument(tempPath))
            {
                File.Delete(tempPath);
                tempPath = null;
                SetNegativeCache(negativeCacheKey);
                return null;
            }

            File.Move(tempPath, diskPath, overwrite: true);
            tempPath = null;
            _checkedDiskFiles.TryAdd(diskPath, 0);
            ScheduleTrimDiskCache();
            return diskPath;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { }
            }
            _downloadLock.Release();
        }
    }

    private static bool IsDocumentMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return false;
        mediaType = mediaType.Trim();
        // image/svg+xml is an image; text/* and JSON/XML/HTML documents are not.
        return mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || (mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
                && (mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
                    || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)));
    }

    private bool IsUsableDiskFile(string diskPath)
    {
        var file = new FileInfo(diskPath);
        if (!file.Exists) return false;
        if (_checkedDiskFiles.ContainsKey(diskPath)) return true;
        if (file.Length <= _maxImageBytes && !LooksLikeDocument(diskPath))
        {
            _checkedDiskFiles.TryAdd(diskPath, 0);
            return true;
        }
        try { File.Delete(diskPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return false;
    }

    /// <summary>
    /// True for an HTML page or JSON body, whatever its declared type. No image
    /// format starts with these bytes. XML is not sniffed: SVG starts the same way.
    /// </summary>
    internal static bool LooksLikeDocument(string path)
    {
        Span<byte> head = stackalloc byte[64];
        int length;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            length = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        var text = head[..length];
        if (text.Length >= 3 && text[0] == 0xEF && text[1] == 0xBB && text[2] == 0xBF) text = text[3..];
        while (text.Length > 0 && text[0] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') text = text[1..];
        if (text.Length == 0) return false;
        if (text[0] is (byte)'{' or (byte)'[') return true;
        var prefix = System.Text.Encoding.ASCII.GetString(text[..Math.Min(text.Length, 14)]).ToLowerInvariant();
        return prefix.StartsWith("<!doctype html", StringComparison.Ordinal)
            || prefix.StartsWith("<html", StringComparison.Ordinal);
    }

    private void AddToMemoryCache(string key, byte[] data)
    {
        if (data.Length > _maxMemoryBytes) return;

        while (Interlocked.Read(ref _currentMemoryBytes) + data.Length > _maxMemoryBytes)
        {
            if (!_evictionOrder.TryDequeue(out var oldKey)) break;
            if (_memoryCache.TryRemove(oldKey, out var removed))
                Interlocked.Add(ref _currentMemoryBytes, -removed.Length);
        }

        if (_memoryCache.TryAdd(key, data))
        {
            Interlocked.Add(ref _currentMemoryBytes, data.Length);
            _evictionOrder.Enqueue(key);
        }
        // If key already exists, don't enqueue again (prevents counter drift on eviction)
    }

    private string GetDiskPath(string cacheKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)))[..16];
        return Path.Combine(_diskCacheDir, $"{hash}.img");
    }

    private static string BuildCacheKey(string contentId, string imageType, string url)
    {
        var normalizedUrl = NormalizeImageUrlForCache(url);
        var urlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl)))[..16];
        return $"{contentId}_{imageType}_{urlHash}";
    }

    private static string BuildNegativeCacheKey(string cacheKey, string url)
    {
        var exactUrlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return $"{cacheKey}\u001F{exactUrlHash}";
    }

    private void SetNegativeCache(string negativeCacheKey, bool missing = false)
    {
        if (_maxNegativeCacheEntries <= 0)
            return;

        lock (_negativeCacheSync)
        {
            var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            var expiresAtTicks = nowTicks + NegativeCacheDuration.Ticks;
            if (_negativeCache.TryAdd(negativeCacheKey, expiresAtTicks))
                _negativeEvictionOrder.Enqueue(negativeCacheKey);
            else
                _negativeCache[negativeCacheKey] = expiresAtTicks;
            if (missing) _missingImages.Add(negativeCacheKey);
            else _missingImages.Remove(negativeCacheKey);

            PruneNegativeCache(nowTicks);
        }
    }

    private void PruneNegativeCache(long nowTicks)
    {
        while (_negativeEvictionOrder.TryPeek(out var candidate))
        {
            if (_negativeCache.TryGetValue(candidate, out var expiresAtTicks) && expiresAtTicks > nowTicks)
                break;

            _negativeEvictionOrder.TryDequeue(out _);
            _negativeCache.TryRemove(candidate, out _);
            _missingImages.Remove(candidate);
        }

        while (_negativeCache.Count > _maxNegativeCacheEntries &&
               _negativeEvictionOrder.TryDequeue(out var oldest))
        {
            _negativeCache.TryRemove(oldest, out _);
            _missingImages.Remove(oldest);
        }
    }

    private bool IsNegativeCacheHit(string negativeCacheKey)
    {
        lock (_negativeCacheSync)
        {
            if (!_negativeCache.TryGetValue(negativeCacheKey, out var expiresAtTicks))
                return false;

            if (expiresAtTicks > DateTimeOffset.UtcNow.UtcTicks)
                return true;

            _negativeCache.TryRemove(negativeCacheKey, out _);
            _missingImages.Remove(negativeCacheKey);
            return false;
        }
    }

    private void RemoveNegativeCache(string negativeCacheKey)
    {
        lock (_negativeCacheSync)
        {
            _negativeCache.TryRemove(negativeCacheKey, out _);
            _missingImages.Remove(negativeCacheKey);
        }
    }

    internal static string NormalizeImageUrlForCache(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.Trim();

        var builder = new UriBuilder(uri)
        {
            Fragment = "",
            Query = BuildStableQuery(uri.Query)
        };

        return builder.Uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
            UriFormat.UriEscaped);
    }

    private static string BuildStableQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "";

        var stablePairs = new List<(string Name, string Pair)>();
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            var rawName = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            var name = Uri.UnescapeDataString(rawName.Replace("+", " "));
            if (IsVolatileImageQueryParameter(name))
                continue;

            stablePairs.Add((name, pair));
        }

        stablePairs.Sort((left, right) =>
        {
            var nameCompare = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return nameCompare != 0
                ? nameCompare
                : StringComparer.Ordinal.Compare(left.Pair, right.Pair);
        });

        return string.Join('&', stablePairs.Select(pair => pair.Pair));
    }

    private static bool IsVolatileImageQueryParameter(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        return name.StartsWith("X-Amz-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("X-Goog-", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Signature", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Expires", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AWSAccessKeyId", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Key-Pair-Id", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Policy", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("token", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("access_token", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("jwt", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sig", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("se", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sp", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("spr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sr", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sv", StringComparison.OrdinalIgnoreCase);
    }

    private void ScheduleTrimDiskCache()
    {
        if (_maxDiskBytes <= 0)
            return;

        var nowTicks = DateTime.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastDiskTrimTicks);
        if (lastTicks != 0 && nowTicks - lastTicks < DiskTrimInterval.Ticks)
            return;

        if (Interlocked.Exchange(ref _diskTrimQueued, 1) == 1)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                TrimDiskCache();
                Interlocked.Exchange(ref _lastDiskTrimTicks, DateTime.UtcNow.Ticks);
            }
            finally
            {
                Interlocked.Exchange(ref _diskTrimQueued, 0);
            }
        });
    }

    private void TrimDiskCache()
    {
        try
        {
            if (_maxDiskBytes <= 0) return;

            var files = Directory.GetFiles(_diskCacheDir, "*.img")
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists)
                .OrderBy(file => file.LastAccessTimeUtc)
                .ToList();

            var total = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (total <= _maxDiskBytes) break;
                try
                {
                    var length = file.Length;
                    file.Delete();
                    total -= length;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void TouchDiskFile(string diskPath)
    {
        try { File.SetLastAccessTimeUtc(diskPath, DateTime.UtcNow); }
        catch { }
    }

    public void Dispose()
    {
        _downloadLock.Dispose();
        _memoryCache.Clear();
        lock (_negativeCacheSync)
        {
            _negativeCache.Clear();
            _missingImages.Clear();
            while (_negativeEvictionOrder.TryDequeue(out _)) { }
        }
    }
}
