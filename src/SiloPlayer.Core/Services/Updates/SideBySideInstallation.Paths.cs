using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace SiloPlayer.Core.Services.Updates;

public static partial class SideBySideInstallation
{
    public const string MetadataDirectoryName = ".silo-update";
    public const string InventoryFileName = ".silo-owned.json";
    private const int MaximumRecordBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string CanonicalRoot(string root)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(path) || Path.GetPathRoot(path) == path)
            throw new InvalidDataException("Installation root must be an existing application directory.");
        using var lease = LockDirectories(path);
        return path;
    }

    public static string GetMutexName(string root) => @"Global\SiloDesktopUpdate-" + RootHash(CanonicalRoot(root))[..16];
    public static string RootHash(string root) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant())));
    private static string Metadata(string root, string name) => Path.GetFullPath(Path.Combine(root, MetadataDirectoryName, name));
    private static string VersionPath(string root, string version) => Path.Combine(root, "versions", CheckVersion(version));
    private static string CheckVersion(string version)
    {
        if (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision < 0 || parsed.ToString(4) != version)
            throw new InvalidDataException("A normalized four-component version is required.");
        return version;
    }

    private static string CheckTransaction(string id) => Guid.TryParseExact(id, "N", out _) ? id :
        throw new InvalidDataException("Invalid update transaction.");

    private static string RelativeFile(string directory, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 240 || Path.IsPathRooted(relative) || relative.Contains('\\'))
            throw new InvalidDataException("Invalid update path.");
        foreach (var segment in relative.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.Any(c => c < 32 || c is ':' or '<' or '>' or '"' or '|' or '?' or '*') ||
                segment.Equals(InventoryFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsafe update path.");
            var stem = segment.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && (char.IsDigit(stem[3]) || "¹²³".Contains(stem[3]))))
                throw new InvalidDataException("Reserved update path.");
        }
        var path = Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update path escaped its directory.");
        return path;
    }

    private static void ValidateBootstrapPath(string path)
    {
        var first = path.Split('/')[0];
        if (first.Equals("versions", StringComparison.OrdinalIgnoreCase) || first.Equals(MetadataDirectoryName, StringComparison.OrdinalIgnoreCase) ||
            first.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bootstrap path overlaps installation metadata.");
    }

    private static T ReadRecord<T>(string path)
    {
        using var directories = LockDirectories(Path.GetDirectoryName(path)!);
        using var file = OpenFile(path, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > MaximumRecordBytes) throw new InvalidDataException("Invalid update record length.");
        return JsonSerializer.Deserialize<T>(file, JsonOptions) ?? throw new InvalidDataException("Empty update record.");
    }

    private static void WriteRecord<T>(string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaximumRecordBytes) throw new InvalidDataException("Update record exceeds its limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var directories = LockDirectories(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            { output.Write(bytes); output.Flush(true); }
            if (File.Exists(path))
            {
                using (OpenFile(path, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { }
                File.Move(temporary, path, true);
            }
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Handles prohibit parent renames and opening final files through junctions/symlinks.
    // Source handles deny writes/deletion for the entire hash-and-copy operation.
    private static FileStream OpenFile(string path, FileAccess access, FileShare share, bool deleteAccess = false)
    {
        var desired = access == FileAccess.Read ? 0x80000000u : 0xC0000000u;
        if (deleteAccess) desired |= 0x10000;
        var handle = CreateFile(path, desired, (uint)share, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException("Cannot lock update file.", new Win32Exception(error)); }
        try
        {
            if (!GetFileInformationByHandle(handle, out var info)) throw new IOException("Cannot inspect update file.");
            if ((info.Attributes & 0x410) != 0 || info.NumberOfLinks != 1)
                throw new InvalidDataException("Linked update files are not supported.");
            return new FileStream(handle, access);
        }
        catch { handle.Dispose(); throw; }
    }

    private static DirectoryLease LockDirectories(string directory)
    {
        var lease = new DirectoryLease();
        try
        {
            var full = Path.GetFullPath(directory);
            var current = Path.GetPathRoot(full)!;
            foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                var handle = CreateFile(current, 0x80, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException("Cannot lock update directory.", new Win32Exception(error)); }
                lease.Handles.Add(handle);
                if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0 || (info.Attributes & 0x10) == 0)
                    throw new InvalidDataException("Reparse-point update directories are not supported.");
            }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    private sealed class DirectoryLease : IDisposable
    {
        public List<SafeFileHandle> Handles { get; } = [];
        public void Dispose() { foreach (var handle in Handles.AsEnumerable().Reverse()) handle.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string path, uint desiredAccess, uint shareMode, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref int information, uint size);

    private static void DeleteLockedFile(FileStream file)
    {
        var delete = 1;
        if (!SetFileInformationByHandle(file.SafeFileHandle, 4, ref delete, sizeof(int)))
            throw new IOException("Cannot remove owned update file.", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    private static void RemoveEmptyOwnedDirectory(string directory)
    {
        using var parents = LockDirectories(Path.GetDirectoryName(directory)!);
        using var handle = CreateFile(directory, 0x10080, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Cannot lock owned directory for removal.", new Win32Exception(Marshal.GetLastWin32Error()));
        if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0 || (info.Attributes & 0x10) == 0)
            throw new InvalidDataException("Linked owned directory cannot be removed.");
        if (Directory.EnumerateFileSystemEntries(directory).Any()) return;
        var delete = 1;
        if (!SetFileInformationByHandle(handle, 4, ref delete, sizeof(int)))
            throw new IOException("Cannot remove empty owned directory.", new Win32Exception(Marshal.GetLastWin32Error()));
    }
}
