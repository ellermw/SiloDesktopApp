using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using SiloPlayer.Core.Models.Updates;

namespace SiloPlayer.Core.Services.Updates;

public static partial class SideBySideInstallation
{
    private const string LaunchVerificationFileName = "verified-launch.json";
    private const uint FileReadAttributes = 0x80;
    private const int FileBasicInfo = 0;
    private const int FileIdInfo = 18;

    private sealed record LaunchVerification(int Schema, string RootHash, string Version, string InventorySha256, LaunchFileStamp[] Files);
    private readonly record struct LaunchFileStamp(long Size, long Created, long Written, long Changed, ulong Volume, ulong IdLow, ulong IdHigh);

    private static string VerifyConfirmedLaunch(string root, string version, string? receiptDirectory,
        Func<string, string, bool> canReuse, Action? onFullVerification)
    {
        var directory = VersionPath(root, version);
        if (!canReuse(root, directory))
        {
            ForgetLaunchVerification(root, receiptDirectory);
            return VerifiedExecutable(root, version, onFullVerification);
        }

        var inventory = ReadOwnedInventory(directory, version);
        var files = inventory.Manifest.Files.Where(file => file.Area == "client").ToArray();
        var inventoryHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(inventory, JsonOptions)));
        var rootHash = RootHash(root);
        var receiptPath = TryGetLaunchVerificationPath(root, receiptDirectory);
        var receipt = receiptPath is null ? null : TryReadLaunchVerification(receiptPath);
        var executable = Path.Combine(directory, "SiloPlayer.exe");
        if (receipt is { Schema: 1, Files: not null } && receipt.RootHash == rootHash && receipt.Version == version &&
            receipt.InventorySha256 == inventoryHash && receipt.Files.Length == files.Length &&
            LaunchMetadataMatches(directory, files, receipt.Files))
            return executable;

        if (receiptPath is not null) DeleteLaunchVerification(receiptPath);
        onFullVerification?.Invoke();
        var stamps = new List<LaunchFileStamp>(files.Length);
        VerifyTree(directory, files, default, receiptPath is null ? null : file =>
        {
            // Capture the identity from the same locked handle that supplied the verified bytes.
            if (TryReadLaunchFileStamp(file.SafeFileHandle, file.Length) is { } stamp) stamps.Add(stamp);
        });
        if (receiptPath is not null && stamps.Count == files.Length)
            TryWriteLaunchVerification(receiptPath, new(1, rootHash, version, inventoryHash, stamps.ToArray()));
        return executable;
    }

    private static bool IsRegisteredLaunchRoot(string root) =>
        string.Equals(UpdateInstallation.FindRegisteredRoot(Path.Combine(root, "SiloPlayer.exe"), machineOnly: true),
            root, StringComparison.OrdinalIgnoreCase);

    private static bool CanReuseLaunchVerification(string root, string directory)
    {
        try
        {
            // The per-user receipt is mutable. Only use it for local NTFS installations
            // whose directories, control records and payload are protected from that user.
            if (!IsRegisteredLaunchRoot(root)) return false;
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            if (drive.DriveType != DriveType.Fixed || !drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                return false;
            UpdateInstallation.RequireProtectedLaunchInstallation(root, directory);
            return true;
        }
        catch (Exception ex) when (IsLaunchVerificationError(ex)) { return false; }
    }

    private static bool LaunchMetadataMatches(string directory, IReadOnlyList<UpdateFileEntry> files, LaunchFileStamp[] expected)
    {
        try
        {
            using var lease = LockDirectories(directory);
            for (var index = 0; index < files.Count; index++)
            {
                var entry = files[index];
                var path = RelativeFile(directory, entry.Path);
                using var parents = LockDirectories(Path.GetDirectoryName(path)!, lease);
                // Request attributes only. This handle cannot read the file's data stream.
                using var handle = OpenOrdinaryFile(path, FileReadAttributes, FileShare.Read, out var info);
                var size = ((long)info.FileSizeHigh << 32) | info.FileSizeLow;
                if (size != entry.Size || TryReadLaunchFileStamp(handle, size) != expected[index]) return false;
            }
            return true;
        }
        catch (Exception ex) when (IsLaunchVerificationError(ex)) { return false; }
    }

    private static LaunchFileStamp? TryReadLaunchFileStamp(SafeFileHandle file, long size)
    {
        if (!GetFileBasicInformation(file, FileBasicInfo, out var basic, (uint)Marshal.SizeOf<LaunchFileBasicInformation>()) ||
            !GetFileIdInformation(file, FileIdInfo, out var id, (uint)Marshal.SizeOf<LaunchFileIdInformation>()) ||
            basic.ChangeTime <= 0 || (id.Low == 0 && id.High == 0))
            return null;
        return new(size, basic.CreationTime, basic.LastWriteTime, basic.ChangeTime, id.VolumeSerialNumber, id.Low, id.High);
    }

    private static string? TryGetLaunchVerificationPath(string root, string? receiptDirectory)
    {
        try { return Path.Combine(Path.GetDirectoryName(BootReceiptPath(root, receiptDirectory))!, LaunchVerificationFileName); }
        catch (Exception ex) when (IsLaunchVerificationError(ex)) { return null; }
    }

    private static LaunchVerification? TryReadLaunchVerification(string path)
    {
        try { return File.Exists(path) ? ReadRecord<LaunchVerification>(path) : null; }
        catch (Exception ex) when (IsLaunchVerificationError(ex)) { return null; }
    }

    private static void TryWriteLaunchVerification(string path, LaunchVerification receipt)
    {
        try
        {
            UpdateManifestValidation.EnsureOrdinaryPath(path);
            WriteRecord(path, receipt);
        }
        catch (Exception ex) when (IsLaunchVerificationError(ex)) { }
    }

    private static void ForgetLaunchVerification(string root, string? receiptDirectory)
    {
        if ((receiptDirectory is not null || IsRegisteredLaunchRoot(root)) &&
            TryGetLaunchVerificationPath(root, receiptDirectory) is { } path)
            DeleteLaunchVerification(path);
    }

    private static void DeleteLaunchVerification(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var parents = LockDirectories(Path.GetDirectoryName(path)!);
            using var file = OpenFile(path, FileAccess.Read, FileShare.Read, deleteAccess: true);
            DeleteLockedFile(file);
        }
        catch (Exception ex) when (IsLaunchVerificationError(ex)) { }
    }

    private static bool IsLaunchVerificationError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or
            System.Security.SecurityException or System.ComponentModel.Win32Exception or NotSupportedException;

    [StructLayout(LayoutKind.Sequential)]
    private struct LaunchFileBasicInformation
    {
        public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LaunchFileIdInformation
    {
        public ulong VolumeSerialNumber, Low, High;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileBasicInformation(SafeFileHandle file, int informationClass, out LaunchFileBasicInformation info, uint size);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileIdInformation(SafeFileHandle file, int informationClass, out LaunchFileIdInformation info, uint size);
}
