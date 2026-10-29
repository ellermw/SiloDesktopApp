using System.Diagnostics;
using System.Text.Json;
using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

public static partial class SideBySideInstallation
{
    public static void BeginLaunch(string root, string executable, string? receiptDirectory = null)
    {
        root = CanonicalRoot(root);
        var state = ReadState(root);
        if (state?.PendingVersion is null || state.TransactionId is null) return;
        var expected = Path.Combine(VersionPath(root, state.PendingVersion), "SiloPlayer.exe");
        if (!Path.GetFullPath(executable).Equals(expected, StringComparison.OrdinalIgnoreCase)) return;
        var existing = ReadBootReceipt(root, receiptDirectory);
        if (existing is { Failed: false } && (existing.Starting ? IsStartingObserverAlive(root, existing) : IsObservedProcessAlive(existing))) return;
        using var observer = Process.GetCurrentProcess();
        WriteRecord(BootReceiptPath(root, receiptDirectory), new SideBySideBootReceipt(RootHash(root), state.TransactionId,
            state.PendingVersion, 0, 0, expected, false)
        { Starting = true, ObserverProcessId = observer.Id, ObserverStartUtcTicks = observer.StartTime.ToUniversalTime().Ticks });
    }
    public static string GetBootReceiptDirectory(string root) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer", "updates", "boots", RootHash(CanonicalRoot(root)));

    /// <summary>Per-user observation only; it never modifies or authorizes changes to the protected installation.</summary>
    public static void RecordBoot(string root, Process process, string? receiptDirectory = null)
        => RecordProcessObservation(root, process, false, receiptDirectory);

    public static bool RecordReady(string root, string? receiptDirectory = null)
    {
        if (ReadState(root)?.PendingVersion is null) return false;
        using var process = Process.GetCurrentProcess();
        RecordReady(root, process, receiptDirectory);
        return true;
    }

    public static void RecordReady(string root, Process process, string? receiptDirectory = null)
        => RecordProcessObservation(root, process, true, receiptDirectory);

    private static void RecordProcessObservation(string root, Process process, bool ready, string? receiptDirectory)
    {
        root = CanonicalRoot(root);
        var state = ReadState(root) ?? throw new InvalidOperationException("Installation is not initialized.");
        if (state.PendingVersion is null || state.TransactionId is null) throw new InvalidOperationException("There is no pending boot.");
        var executable = Path.Combine(VersionPath(root, state.PendingVersion), "SiloPlayer.exe");
        process.Refresh();
        if (process.HasExited || !Path.GetFullPath(process.MainModule?.FileName ?? "").Equals(executable, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Boot observation is not from the pending version.");
        var ticks = process.StartTime.ToUniversalTime().Ticks;
        var receipt = new SideBySideBootReceipt(RootHash(root), state.TransactionId, state.PendingVersion,
            process.Id, ticks, executable, ready);
        // The launcher may return after its child has already signaled readiness. Do not
        // overwrite that signal with the earlier start observation for the same identity.
        var existing = ReadBootReceipt(root, receiptDirectory);
        if (!ready && existing is { Failed: false, Starting: false } && IsObservedProcessAlive(existing)) return;
        WriteRecord(BootReceiptPath(root, receiptDirectory), receipt);
    }

    public static SideBySideBootReceipt? ReadBootReceipt(string root, string? receiptDirectory = null)
    {
        root = CanonicalRoot(root);
        var state = ReadState(root);
        if (state?.PendingVersion is null || state.TransactionId is null) return null;
        var path = BootReceiptPath(root, receiptDirectory);
        try
        {
            if (!File.Exists(path)) return null;
            using var parents = LockDirectories(Path.GetDirectoryName(path)!);
            using var input = OpenFile(path, FileAccess.Read, FileShare.Read);
            if (input.Length is <= 0 or > 65536) return null;
            var receipt = JsonSerializer.Deserialize<SideBySideBootReceipt>(input, JsonOptions);
            var executable = Path.Combine(VersionPath(root, state.PendingVersion), "SiloPlayer.exe");
            return receipt is not null && receipt.RootHash == RootHash(root) && receipt.TransactionId == state.TransactionId &&
                receipt.Version == state.PendingVersion && ((receipt.ProcessId > 0 && receipt.ProcessStartUtcTicks > 0) ||
                    ((receipt.Failed || (receipt.Starting && receipt.ObserverProcessId > 0 && receipt.ObserverStartUtcTicks > 0)) &&
                     receipt.ProcessId == 0 && receipt.ProcessStartUtcTicks == 0)) &&
                string.Equals(receipt.Executable, executable, StringComparison.OrdinalIgnoreCase) ? receipt : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException) { return null; }
    }

    /// <summary>The host must additionally authenticate its IPC peer; an observation is not release authority.</summary>
    public static bool IsBootReady(string root, string? receiptDirectory = null)
        => ReadBootReceipt(root, receiptDirectory) is { Ready: true, Failed: false, Starting: false } receipt && IsObservedProcessAlive(receipt);

    public static string ResolveLaunchExecutable(string root, string? receiptDirectory = null)
    {
        root = CanonicalRoot(root);
        var state = ReadState(root) ?? throw new InvalidOperationException("Installation is not initialized.");
        var receipt = ReadBootReceipt(root, receiptDirectory);
        if (state.PendingVersion is not null && state.PreviousVersion is not null && receipt is not null &&
            (receipt.Failed || (receipt.Starting ? !IsStartingObserverAlive(root, receipt) : !receipt.Ready && !IsObservedProcessAlive(receipt))))
            return VerifiedExecutable(root, state.PreviousVersion);
        try { return VerifiedExecutable(root, state.ActiveVersion); }
        catch (Exception ex) when (state.PendingVersion is not null && state.PreviousVersion is not null &&
            ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Pending files can disappear before a child can create any observation.
            // Select only a fully verified prior payload; confirmed-active failures
            // remain repair errors rather than silently selecting a historic version.
            return VerifiedExecutable(root, state.PreviousVersion);
        }
    }

    public static void MarkBootFailed(string root, string transactionId, string? receiptDirectory = null)
    {
        var receipt = ReadBootReceipt(root, receiptDirectory);
        if (receipt is null || receipt.TransactionId != transactionId) throw new InvalidOperationException("Boot observation is stale or missing.");
        WriteRecord(BootReceiptPath(root, receiptDirectory), receipt with { Failed = true, Ready = false });
    }

    /// <summary>Records inability to create/observe a child. This is a per-user fallback hint only.</summary>
    public static void RecordFailedLaunch(string root, string executable, string? receiptDirectory = null)
    {
        root = CanonicalRoot(root);
        var state = ReadState(root);
        if (state?.PendingVersion is null || state.TransactionId is null) return;
        var expected = Path.Combine(VersionPath(root, state.PendingVersion), "SiloPlayer.exe");
        if (!Path.GetFullPath(executable).Equals(expected, StringComparison.OrdinalIgnoreCase)) return;
        var existing = ReadBootReceipt(root, receiptDirectory);
        if (existing is { Starting: false, Failed: false } && IsObservedProcessAlive(existing)) return;
        WriteRecord(BootReceiptPath(root, receiptDirectory), new SideBySideBootReceipt(RootHash(root), state.TransactionId,
            state.PendingVersion, 0, 0, expected, false, true));
    }

    private static string BootReceiptPath(string root, string? receiptDirectory)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(receiptDirectory ?? GetBootReceiptDirectory(root)));
        root = CanonicalRoot(root);
        if (directory.Equals(root, StringComparison.OrdinalIgnoreCase) || directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Boot observations must remain outside the installation.");
        return Path.Combine(directory, "boot.json");
    }

    private static bool IsObservedProcessAlive(SideBySideBootReceipt receipt)
    {
        try
        {
            using var process = Process.GetProcessById(receipt.ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == receipt.ProcessStartUtcTicks &&
                Path.GetFullPath(process.MainModule?.FileName ?? "").Equals(receipt.Executable, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { return false; }
    }

    private static bool IsStartingObserverAlive(string root, SideBySideBootReceipt receipt)
    {
        try
        {
            using var process = Process.GetProcessById(receipt.ObserverProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == receipt.ObserverStartUtcTicks &&
                Path.GetFullPath(process.MainModule?.FileName ?? "").Equals(Path.Combine(root, "SiloPlayer.exe"), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { return false; }
    }

    /// <summary>Checks an installed helper against the protected version inventory before it is started/elevated.</summary>
    public static string VerifyInstalledClientFile(string root, string version, string relativePath)
    {
        root = CanonicalRoot(root);
        var inventory = VerifyOwnedVersion(root, version);
        var entry = inventory.Manifest.Files.SingleOrDefault(f => f.Area == "client" && f.Path.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The helper is absent from the installed inventory.");
        return RelativeFile(VersionPath(root, version), entry.Path);
    }
}
