using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SiloPlayer.Core.Services.Updates;

/// <summary>What a removal pass deleted, queued for deletion at the next restart, or had to leave.</summary>
public sealed class UninstallReport
{
    public int Removed { get; internal set; }
    public int QueuedForRestart { get; internal set; }
    public List<string> Retained { get; } = [];
}

public static partial class SideBySideInstallation
{
    private const string StagingDirectoryName = "staging", AuthorityDirectoryName = "authority";

    /// <summary>
    /// Uninstall removes everything (owner decision, 2026-11-26): every version folder and the
    /// updater's control folder, including what the app or updater created there. Inno then removes
    /// its own logged files and the uninstaller's last step removes the rest of the folder.
    /// Junctions and symbolic links are removed as links and never followed. A locked entry never
    /// makes uninstall wait; in a protected installation it is queued for deletion at restart.
    /// </summary>
    public static UninstallReport RemoveOwnedVersionsForUninstall(string root, bool queueLockedForRestart = false)
    {
        root = CanonicalRoot(root);
        RequireDedicatedRoot(root);
        // Only a Silo layout is emptied: a folder that merely holds a copied launcher is left alone.
        var metadata = Path.Combine(root, MetadataDirectoryName);
        if (!new[] { "state.json", "transaction.json", "initial.manifest.json" }.Any(name => File.Exists(Path.Combine(metadata, name))))
            throw new InvalidOperationException("Installation is not initialized.");
        queueLockedForRestart &= IsProtectedDirectory(root);
        var report = new UninstallReport();
        using var lease = LockDirectories(root);
        foreach (var name in new[] { "versions", MetadataDirectoryName })
            DeleteTreeWithoutFollowingLinks(Path.Combine(root, name), report, queueLockedForRestart);
        return report;
    }

    /// <summary>The elevated worker's new folder for this run's release-authority manifest. Earlier runs' copies are removed first.</summary>
    public static string PrepareAuthorityDirectory(string root)
    {
        root = CanonicalRoot(root);
        CleanupUpdateMetadata(root, staging: false, authority: true);
        return Metadata(root, AuthorityDirectoryName + "/" + Guid.NewGuid().ToString("N"));
    }

    // Updater control entries that no transaction needs: staging folders other than the current
    // journal's, and (when no run is using one) the release-authority manifests of finished runs.
    // Cleanup is best effort and never fails the operation that asked for it.
    private static void CleanupUpdateMetadata(string root, bool staging, bool authority)
    {
        try
        {
            var keep = staging ? ReadJournal(root)?.TransactionId : null;
            var report = new UninstallReport();
            foreach (var (name, wanted) in new[] { (StagingDirectoryName, staging), (AuthorityDirectoryName, authority) })
            {
                var directory = Metadata(root, name);
                if (!wanted || !Directory.Exists(directory)) continue;
                using (LockDirectories(directory))
                {
                    foreach (var entry in Directory.GetFileSystemEntries(directory))
                    {
                        var id = Path.GetFileName(entry);
                        if (!Guid.TryParseExact(id, "N", out _) || id.Equals(keep, StringComparison.OrdinalIgnoreCase)) continue;
                        DeleteTreeWithoutFollowingLinks(entry, report, queueLockedForRestart: false);
                    }
                }
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) RemoveEmptyOwnedDirectory(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or JsonException) { }
    }

    // The folders the installer also refuses as an installation directory (SiloInstaller.iss UnsafeInstallDirectory).
    private static void RequireDedicatedRoot(string root)
    {
        static string? Full(string? path) => string.IsNullOrEmpty(path) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        bool Same(string? path) => Full(path) is { } other && root.Equals(other, StringComparison.OrdinalIgnoreCase);
        bool Within(string? path) => Full(path) is { } other && (Same(other) || root.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Environment.SpecialFolder[] account =
        [
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.LocalApplicationData,
        ];
        if (account.Any(folder => Same(Environment.GetFolderPath(folder))) || Within(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ||
            Within(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiloPlayer")) ||
            Path.Exists(Path.Combine(root, ".git")) || Path.Exists(Path.Combine(root, ".codex-tmp")))
            throw new InvalidDataException("Uninstall cleanup requires a dedicated installation folder.");
    }

    private static bool IsProtectedDirectory(string directory)
    {
        // Uncertainty means "not protected": nothing is then queued for a privileged restart deletion.
        try { return UpdateInstallation.IsPrivilegedAcl(new DirectoryInfo(directory).GetAccessControl()); }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Deletes an entry and, when it is an ordinary folder, everything below it. Each entry is opened
    /// without following reparse points and held without delete sharing while its children go, so no
    /// part of the walk can be renamed or swapped for a link to somewhere else. A junction or symbolic
    /// link is deleted as a link; its target is never touched. A missing entry is not an error.
    /// </summary>
    internal static void DeleteTreeWithoutFollowingLinks(string path, UninstallReport report, bool queueLockedForRestart)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Path.GetDirectoryName(path) is not { } parent) return;
        using var parents = LockDirectories(parent);
        DeleteEntry(path, report, queueLockedForRestart, 0);
    }

    private static void DeleteEntry(string path, UninstallReport report, bool queue, int depth)
    {
        // DELETE | FILE_READ_ATTRIBUTES; share read and write, not delete; FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS.
        var handle = CreateFile(path, 0x00010080, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error is not (2 or 3)) Retain(path, report, queue);
            return;
        }
        using (handle)
        {
            if (!GetFileInformationByHandle(handle, out var info)) { Retain(path, report, queue); return; }
            if ((info.Attributes & 0x410) == 0x10)
            {
                string[] children;
                try { children = depth < 64 ? Directory.GetFileSystemEntries(path) : throw new IOException("Folder nesting is too deep."); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Retain(path, report, false); return; }
                foreach (var child in children) DeleteEntry(child, report, queue, depth + 1);
            }
            // Delete with POSIX semantics, ignoring read-only, so this file's attributes (shared by any
            // hard link) are never changed. Older file systems fall back to the classic disposition.
            var flags = 0x13;
            var classic = 1;
            if (SetFileInformationByHandle(handle, 21, ref flags, sizeof(int)) || SetFileInformationByHandle(handle, 4, ref classic, sizeof(int)))
            { report.Removed++; return; }
        }
        Retain(path, report, queue);
    }

    // In use or protected. Never wait for it: queue it for the next restart (administrator, protected
    // installation only; never a user-writable location) or report it.
    private static void Retain(string path, UninstallReport report, bool queue)
    {
        if (queue && MoveFileEx(path, null, 0x4)) report.QueuedForRestart++;
        else report.Retained.Add(path);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "MoveFileExW")]
    private static extern bool MoveFileEx(string existing, string? replacement, int flags);
}
