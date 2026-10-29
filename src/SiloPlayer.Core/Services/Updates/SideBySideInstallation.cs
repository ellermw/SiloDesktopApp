using System.Security.Cryptography;
using System.Text.Json;
using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

/// <summary>
/// Synchronous installation operations. The trusted host holds GetMutexName(root) on this
/// thread; no API establishes release authority or grants elevation to a caller's package.
/// </summary>
public static partial class SideBySideInstallation
{
    /// <summary>Full installer only, after its logged payload has been placed and verified.</summary>
    public static void InitializeFromInstaller(string root, UpdateManifest manifest)
    {
        root = CanonicalRoot(root);
        ValidateManifest(manifest);
        var previous = ReadState(root);
        if (ReadJournal(root) is { } transaction)
        {
            if (transaction.Phase == "pending") Rollback(root, transaction.TransactionId);
            else RecoverInterruptedTransaction(root);
            previous = ReadState(root);
        }
        var directory = VersionPath(root, manifest.Version);
        VerifyTree(directory, manifest.Files.Where(f => f.Area == "client"), default);
        VerifyBootstrapCompatible(root, manifest);
        WriteRecord(Path.Combine(directory, InventoryFileName), new Inventory(Guid.NewGuid().ToString("N"), manifest));
        var priorVersion = previous?.ActiveVersion == manifest.Version ? previous.PreviousVersion : previous?.ActiveVersion;
        WriteRecord(Metadata(root, "state.json"), new SideBySideState(manifest.Version, priorVersion, null, null));
    }

    public static void RemoveOwnedVersionsForUninstall(string root, Func<string, bool> isVersionInUse)
    {
        root = CanonicalRoot(root);
        var journal = ReadJournal(root);
        CleanupVersions(root, isVersionInUse, uninstall: true);
        var versions = Path.Combine(root, "versions");
        // A retained inventory means an owned file was modified, locked or in use.
        // Leave control records for repair rather than pretending uninstall completed.
        if (Directory.Exists(versions) && Directory.EnumerateDirectories(versions).Any(d => File.Exists(Path.Combine(d, InventoryFileName)))) return;
        if (journal is not null) CleanupTransactionStaging(root, journal);
        RemoveOwnedAuthorityCooldown(root);
        foreach (var name in new[] { "transaction.json", "state.json" })
        {
            var path = Metadata(root, name);
            if (!File.Exists(path)) continue;
            using var parents = LockDirectories(Path.GetDirectoryName(path)!);
            using var file = OpenFile(path, FileAccess.Read, FileShare.Read, deleteAccess: true);
            DeleteLockedFile(file);
        }
        if (Directory.Exists(versions) && !Directory.EnumerateFileSystemEntries(versions).Any()) Directory.Delete(versions);
    }

    private static void RemoveOwnedAuthorityCooldown(string root)
    {
        var path = Metadata(root, AuthorityCooldownFileName);
        if (!File.Exists(path)) return;
        try
        {
            using var parents = LockDirectories(Path.GetDirectoryName(path)!);
            using var file = OpenFile(path, FileAccess.Read, FileShare.Read, deleteAccess: true);
            if (file.Length is <= 0 or > 1024) return;
            var record = JsonSerializer.Deserialize<UpdateAuthorityCooldown.StoredCooldown>(file, JsonOptions);
            if (record is null || record.Schema != 1 || record.Until - record.RecordedAt < TimeSpan.FromMinutes(5) ||
                record.Until - record.RecordedAt > TimeSpan.FromHours(6)) return;
            DeleteLockedFile(file);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { }
    }
    private sealed record Inventory(string TransactionId, UpdateManifest Manifest);
    private sealed record Journal(string TransactionId, string Version, string PriorVersion, string? PriorPreviousVersion,
        string Phase, UpdateManifest Manifest);

    public static SideBySideState? ReadState(string root)
    {
        root = CanonicalRoot(root);
        var path = Metadata(root, "state.json");
        if (!File.Exists(path)) return null;
        var state = ReadRecord<SideBySideState>(path);
        CheckVersion(state.ActiveVersion);
        if (state.PreviousVersion is not null) CheckVersion(state.PreviousVersion);
        if (state.PendingVersion is not null) CheckVersion(state.PendingVersion);
        if (state.TransactionId is not null) CheckTransaction(state.TransactionId);
        if ((state.PendingVersion is null) != (state.TransactionId is null) ||
            (state.PendingVersion is not null && state.PendingVersion != state.ActiveVersion))
            throw new InvalidDataException("Inconsistent installation state.");
        return state;
    }

    public static string ResolveClientExecutable(string root)
    {
        root = CanonicalRoot(root);
        var state = ReadState(root) ?? throw new InvalidOperationException("Side-by-side installation is not initialized.");
        return VerifiedExecutable(root, state.ActiveVersion);
    }

    public static void Initialize(string root, UpdateManifest manifest)
    {
        root = CanonicalRoot(root);
        ValidateManifest(manifest);
        if (ReadState(root) is not null) throw new InvalidOperationException("Installation is already initialized.");
        var directory = VersionPath(root, manifest.Version);
        VerifyTree(directory, manifest.Files.Where(f => f.Area == "client"), default);
        VerifyBootstrapCompatible(root, manifest);
        WriteRecord(Path.Combine(directory, InventoryFileName), new Inventory(Guid.NewGuid().ToString("N"), manifest));
        WriteRecord(Metadata(root, "state.json"), new SideBySideState(manifest.Version, null, null, null));
    }

    public static StagedUpdate Stage(string root, PreparedUpdatePackage package, Action<IncrementalUpdateProgress> progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        root = CanonicalRoot(root);
        using var rootLease = LockDirectories(root);
        var state = ReadState(root) ?? throw new InvalidOperationException("A full installer migration is required first.");
        if (state.PendingVersion is not null || ReadJournal(root) is not null)
            throw new InvalidOperationException("Another update transaction requires completion or recovery.");
        VerifyOwnedVersion(root, state.ActiveVersion);
        var manifest = ReadPreparedManifest(package);
        ValidateManifest(manifest);
        VerifyBootstrapCompatible(root, manifest);
        if (Version.Parse(manifest.Version) <= Version.Parse(state.ActiveVersion))
            throw new InvalidOperationException("Update version must be newer than the active version.");
        var versionDirectory = VersionPath(root, manifest.Version);
        if (Directory.Exists(versionDirectory) || File.Exists(versionDirectory))
            throw new IOException("The target version directory already exists.");
        var transaction = Guid.NewGuid().ToString("N");
        var stageDirectory = Metadata(root, "staging/" + transaction);
        var clientDirectory = Path.Combine(stageDirectory, "client");
        var bootstrapDirectory = Path.Combine(stageDirectory, "bootstrap");
        Directory.CreateDirectory(clientDirectory);
        Directory.CreateDirectory(bootstrapDirectory);
        using var stagingLease = LockDirectories(stageDirectory);
        var journal = new Journal(transaction, manifest.Version, state.ActiveVersion, state.PreviousVersion, "staging", manifest);
        WriteRecord(Metadata(root, "transaction.json"), journal);
        var completed = 0L;
        var files = 0;
        var total = manifest.Files.Sum(f => f.Size);
        try
        {
            foreach (var entry in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                var source = RelativeFile(Path.Combine(package.Directory, entry.Area), entry.Path);
                var destination = RelativeFile(entry.Area == "client" ? clientDirectory : bootstrapDirectory, entry.Path);
                CopyVerified(source, destination, entry.Size, entry.Sha256, ct);
                completed += entry.Size;
                progress(new("Installing alongside current version", completed, total, ++files, manifest.Files.Count));
            }
            VerifyTree(clientDirectory, manifest.Files.Where(f => f.Area == "client"), ct);
            VerifyTree(bootstrapDirectory, manifest.Files.Where(f => f.Area == "bootstrap"), ct);
            ct.ThrowIfCancellationRequested();
            WriteRecord(Path.Combine(clientDirectory, InventoryFileName), new Inventory(transaction, manifest));
            Directory.CreateDirectory(Path.Combine(root, "versions"));
            using (LockDirectories(Path.Combine(root, "versions"))) Directory.Move(clientDirectory, versionDirectory);
            WriteRecord(Metadata(root, "transaction.json"), journal with { Phase = "staged" });
            return new(root, transaction, manifest.Version, versionDirectory, bootstrapDirectory, manifest);
        }
        catch
        {
            // Journal survives an interruption; explicit Rollback can release it. The active pointer never moved.
            WriteRecord(Metadata(root, "transaction.json"), journal with { Phase = "failed-stage" });
            throw;
        }
    }

    public static void Activate(StagedUpdate update)
    {
        var root = CanonicalRoot(update.InstallationRoot);
        var journal = RequireJournal(root, update.TransactionId);
        if (journal.Phase != "staged" || journal.Version != update.Version ||
            !Path.GetFullPath(update.VersionDirectory).Equals(VersionPath(root, journal.Version), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(update.BootstrapDirectory).Equals(Metadata(root, "staging/" + journal.TransactionId + "/bootstrap"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Staged update does not match the installation transaction.");
        var state = ReadState(root)!;
        if (state.ActiveVersion != journal.PriorVersion || state.PendingVersion is not null)
            throw new InvalidOperationException("The active installation changed since staging.");
        VerifyOwnedVersion(root, journal.Version);
        VerifyBootstrapCompatible(root, journal.Manifest);
        WriteRecord(Metadata(root, "state.json"), new SideBySideState(journal.Version, journal.PriorVersion, journal.Version, journal.TransactionId));
        WriteRecord(Metadata(root, "transaction.json"), RequireJournal(root, journal.TransactionId) with { Phase = "pending" });
    }

    public static void Confirm(string root, string transactionId)
    {
        root = CanonicalRoot(root);
        var journal = RequireJournal(root, transactionId);
        var state = ReadState(root)!;
        // The caller has authenticated the actual process's local UI-readiness signal.
        if (state.TransactionId != transactionId || state.PendingVersion != journal.Version || journal.Phase != "pending")
            throw new InvalidOperationException("Update is not waiting for boot confirmation.");
        VerifyOwnedVersion(root, journal.Version);
        WriteRecord(Metadata(root, "transaction.json"), journal with { Phase = "confirmed" });
        WriteRecord(Metadata(root, "state.json"), state with { PendingVersion = null, TransactionId = null });
        DeleteJournal(root);
    }

    public static void Rollback(string root, string transactionId)
    {
        root = CanonicalRoot(root);
        var journal = RequireJournal(root, transactionId);
        VerifyOwnedVersion(root, journal.PriorVersion);
        if (journal.Phase == "confirmed")
            throw new InvalidOperationException("A confirmed update cannot be rolled back by its old transaction.");
        WriteRecord(Metadata(root, "state.json"), new SideBySideState(journal.PriorVersion, journal.PriorPreviousVersion, null, null));
        DeleteJournal(root);
        CleanupTransactionStaging(root, journal);
    }

    /// <summary>Explicit privileged crash recovery, called under the root mutex before beginning new work.</summary>
    public static void RecoverInterruptedTransaction(string root)
    {
        root = CanonicalRoot(root);
        var journal = ReadJournal(root);
        if (journal is null) return;
        if (journal.Phase == "confirmed")
        {
            VerifyOwnedVersion(root, journal.Version);
            WriteRecord(Metadata(root, "state.json"), new SideBySideState(journal.Version, journal.PriorVersion, null, null));
            DeleteJournal(root);
        }
        else if (journal.Phase != "pending")
        {
            Rollback(root, journal.TransactionId);
            CleanupOwnedVersions(root, IsVersionDirectoryInUse);
        }
    }

    /// <summary>The caller supplies an exact-process inventory check; uncertainty must return true.</summary>
    public static void CleanupOwnedVersions(string root, Func<string, bool> isVersionInUse)
        => CleanupVersions(root, isVersionInUse, uninstall: false);

    private static void CleanupVersions(string root, Func<string, bool> isVersionInUse, bool uninstall)
    {
        root = CanonicalRoot(root);
        var state = ReadState(root) ?? throw new InvalidOperationException("Installation is not initialized.");
        var versions = Path.Combine(root, "versions");
        if (!Directory.Exists(versions)) return;
        using var lease = LockDirectories(versions);
        foreach (var directory in Directory.EnumerateDirectories(versions))
        {
            var owned = false;
            var version = Path.GetFileName(directory);
            if (!uninstall && (version == state.ActiveVersion || version == state.PreviousVersion || version == state.PendingVersion)) continue;
            if ((!uninstall && ReadJournal(root)?.Version == version) || isVersionInUse(directory)) continue;
            try
            {
                CheckVersion(version);
                using var directoryLease = LockDirectories(directory);
                var inventory = ReadRecord<Inventory>(Path.Combine(directory, InventoryFileName));
                ValidateManifest(inventory.Manifest);
                if (inventory.Manifest.Version != version) continue;
                owned = true;
                foreach (var entry in inventory.Manifest.Files.Where(f => f.Area == "client"))
                {
                    var path = RelativeFile(directory, entry.Path);
                    if (!File.Exists(path)) continue;
                    using var parents = LockDirectories(Path.GetDirectoryName(path)!);
                    using var file = OpenFile(path, FileAccess.Read, FileShare.Read, deleteAccess: true);
                    if (Matches(file, entry.Size, entry.Sha256)) DeleteLockedFile(file);
                }
                // Unknown additions and modified owned files stay. Only empty owned directories can disappear.
                RemoveEmptyOwnedDirectories(directory, inventory.Manifest.Files.Where(f => f.Area == "client"));
                var inventoryPath = Path.Combine(directory, InventoryFileName);
                if (inventory.Manifest.Files.Where(f => f.Area == "client").All(f => !File.Exists(RelativeFile(directory, f.Path))))
                {
                    using (var file = OpenFile(inventoryPath, FileAccess.Read, FileShare.Read, deleteAccess: true)) DeleteLockedFile(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { }
            if (owned && Directory.Exists(directory)) RemoveEmptyOwnedDirectory(directory);
        }
    }

    private static UpdateManifest ReadPreparedManifest(PreparedUpdatePackage package)
    {
        using var parents = LockDirectories(Path.GetDirectoryName(Path.GetFullPath(package.ManifestPath))!);
        using var input = OpenFile(package.ManifestPath, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > UpdateManifestValidation.MaximumManifestBytes) throw new InvalidDataException("Invalid manifest size.");
        if (!Convert.ToHexString(SHA256.HashData(input)).Equals(package.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prepared manifest was changed.");
        input.Position = 0;
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(input, JsonOptions) ?? throw new InvalidDataException("Missing manifest.");
        if (!JsonSerializer.Serialize(manifest).Equals(JsonSerializer.Serialize(package.Manifest), StringComparison.Ordinal))
            throw new InvalidDataException("Prepared manifest identity does not match its bytes.");
        return manifest;
    }

    private static void ValidateManifest(UpdateManifest manifest)
    {
        if (manifest is null) throw new InvalidDataException("Missing installed payload manifest.");
        UpdateManifestValidation.Validate(manifest);
        CheckVersion(manifest.Version);
        foreach (var entry in manifest.Files)
        {
            RelativeFile(Path.GetTempPath(), entry.Path);
            if (entry.Area == "bootstrap") ValidateBootstrapPath(entry.Path);
        }
    }

    private static void CopyVerified(string source, string destination, long size, string hash, CancellationToken ct)
    {
        using var sourceParents = LockDirectories(Path.GetDirectoryName(source)!);
        using var input = OpenFile(source, FileAccess.Read, FileShare.Read);
        if (!Matches(input, size, hash)) throw new InvalidDataException("Update source content failed verification.");
        input.Position = 0;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var destinationParents = LockDirectories(Path.GetDirectoryName(destination)!);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough);
        var buffer = new byte[65536];
        int read;
        while ((read = input.Read(buffer)) != 0) { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
        output.Flush(true);
    }

    private static bool Matches(FileStream file, long size, string hash) => file.Length == size &&
        Convert.ToHexString(SHA256.HashData(file)).Equals(hash, StringComparison.OrdinalIgnoreCase);

    private static void VerifyTree(string directory, IEnumerable<UpdateFileEntry> entries, CancellationToken ct)
    {
        using var lease = LockDirectories(directory);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var path = RelativeFile(directory, entry.Path);
            using var parents = LockDirectories(Path.GetDirectoryName(path)!);
            using var file = OpenFile(path, FileAccess.Read, FileShare.Read);
            if (!Matches(file, entry.Size, entry.Sha256)) throw new InvalidDataException("Installed version failed content verification.");
        }
    }

    private static Inventory VerifyOwnedVersion(string root, string version)
    {
        var directory = VersionPath(root, version);
        var inventory = ReadRecord<Inventory>(Path.Combine(directory, InventoryFileName));
        CheckTransaction(inventory.TransactionId);
        ValidateManifest(inventory.Manifest);
        if (inventory.Manifest.Version != version) throw new InvalidDataException("Version inventory mismatch.");
        VerifyTree(directory, inventory.Manifest.Files.Where(f => f.Area == "client"), default);
        return inventory;
    }
    private static string VerifiedExecutable(string root, string version)
    { VerifyOwnedVersion(root, version); return Path.Combine(VersionPath(root, version), "SiloPlayer.exe"); }

    private static Journal? ReadJournal(string root)
    {
        var path = Metadata(root, "transaction.json");
        if (!File.Exists(path)) return null;
        var journal = ReadRecord<Journal>(path);
        CheckTransaction(journal.TransactionId);
        CheckVersion(journal.Version); CheckVersion(journal.PriorVersion);
        if (journal.PriorPreviousVersion is not null) CheckVersion(journal.PriorPreviousVersion);
        ValidateManifest(journal.Manifest);
        if (journal.Manifest.Version != journal.Version ||
            journal.Phase is not ("staging" or "failed-stage" or "staged" or "pending" or "confirmed"))
            throw new InvalidDataException("Invalid update transaction record.");
        return journal;
    }
    private static Journal RequireJournal(string root, string transactionId)
    {
        CheckTransaction(transactionId);
        var journal = ReadJournal(root);
        return journal?.TransactionId == transactionId ? journal : throw new InvalidOperationException("Update transaction is stale or missing.");
    }
    private static void DeleteJournal(string root)
    {
        var path = Metadata(root, "transaction.json");
        using var parents = LockDirectories(Path.GetDirectoryName(path)!);
        using (OpenFile(path, FileAccess.Read, FileShare.Read | FileShare.Delete)) { }
        File.Delete(path);
    }
    private static void RemoveEmptyOwnedDirectories(string root, IEnumerable<UpdateFileEntry> entries)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var directory = Path.GetDirectoryName(RelativeFile(root, entry.Path))!;
            while (!directory.Equals(root, StringComparison.OrdinalIgnoreCase))
            { directories.Add(directory); directory = Path.GetDirectoryName(directory)!; }
        }
        foreach (var directory in directories.OrderByDescending(p => p.Length))
        {
            if (!Directory.Exists(directory)) continue;
            RemoveEmptyOwnedDirectory(directory);
        }
    }

    private static void CleanupTransactionStaging(string root, Journal journal)
    {
        var transaction = Metadata(root, "staging/" + journal.TransactionId);
        if (!Directory.Exists(transaction)) return;
        foreach (var area in new[] { "client", "bootstrap" })
        {
            var directory = Path.Combine(transaction, area);
            if (!Directory.Exists(directory)) continue;
            try
            {
                using (LockDirectories(directory))
                {
                    var entries = journal.Manifest.Files.Where(f => f.Area == area).ToArray();
                    foreach (var entry in entries)
                    {
                        var path = RelativeFile(directory, entry.Path);
                        if (!File.Exists(path)) continue;
                        using var parents = LockDirectories(Path.GetDirectoryName(path)!);
                        using var file = OpenFile(path, FileAccess.Read, FileShare.Read, deleteAccess: true);
                        if (Matches(file, entry.Size, entry.Sha256)) DeleteLockedFile(file);
                    }
                    RemoveEmptyOwnedDirectories(directory, entries);
                }
                if (Directory.Exists(directory)) RemoveEmptyOwnedDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
        RemoveEmptyOwnedDirectory(transaction);
    }

    public static bool IsVersionDirectoryInUse(string directory)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        foreach (var name in new[] { "SiloPlayer", "SiloPlayer.UpdateHost" })
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
        {
            using (process)
            {
                try { if (!process.HasExited && Path.GetFullPath(process.MainModule?.FileName ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true; }
                catch { return true; }
            }
        }
        return false;
    }
}
