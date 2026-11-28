using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace SiloPlayer.Core.Services;

// Holds the ordinary path components open without delete sharing, and opens
// mutation targets relative to the verified directory rather than by path.
internal sealed class WindowsCacheDirectory : IDisposable
{
    private const uint ReadAttributes = 0x80, Traverse = 0x20, Synchronize = 0x100000, DeleteAccess = 0x10000;
    private const uint ReadControl = 0x20000, WriteDac = 0x40000;
    private const int FileObject = 1; // SE_FILE_OBJECT
    private const uint ShareRead = 1, ShareWrite = 2;
    private const uint OpenExisting = 1, CreateNew = 2, OpenOrCreate = 3; // NT create dispositions
    private const uint DirectoryFile = 1, NonDirectoryFile = 0x40, SyncIo = 0x20, OpenReparsePoint = 0x200000;
    private const int ReparsePointEncountered = 4395;
    private const FileAttributes DirectoryOrLink = FileAttributes.Directory | FileAttributes.ReparsePoint;
    private const string OwnershipKey = @"Software\SiloPlayer\PrivateCacheDirectories";
    private static readonly object OwnershipGate = new();

    private readonly List<SafeFileHandle> _components = [];
    private SafeFileHandle Handle => _components[^1];
    internal string Path { get; }
    internal FileIdentity Identity { get; private set; }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct FileIdentity
    {
        private readonly ulong _volume, _low, _high;
        public override string ToString() => $"{_volume:X16}{_low:X16}{_high:X16}";
    }

    private WindowsCacheDirectory(string path) => Path = path;

    internal static WindowsCacheDirectory Open(string path, bool createMissing = false)
        => OpenCore(path, createMissing, requireIdentity: true);

    // A read-only review still pins ordinary directories, but doesn't require
    // FileIdInfo support on the volume being inventoried.
    internal static WindowsCacheDirectory OpenForReview(string path)
        => OpenCore(path, createMissing: false, requireIdentity: false);

    private static WindowsCacheDirectory OpenCore(string path, bool createMissing, bool requireIdentity)
    {
        path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        var directory = new WindowsCacheDirectory(path);
        var names = new Stack<string>();
        var root = new DirectoryInfo(path);
        while (root.Parent is { } parent)
        {
            names.Push(root.Name);
            root = parent;
        }

        try
        {
            var handle = CreateFileW(root.FullName, ReadAttributes | Traverse, ShareRead | ShareWrite,
                IntPtr.Zero, 3, 0x02200000, IntPtr.Zero); // OPEN_EXISTING, BACKUP_SEMANTICS | OPEN_REPARSE_POINT
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw IoError(error);
            }
            directory.Pin(handle);
            while (names.TryPop(out var name))
                directory.Pin(OpenChild(directory.Handle, name, ReadAttributes | Traverse, ShareRead | ShareWrite,
                    createMissing ? OpenOrCreate : OpenExisting, DirectoryFile));
            if (requireIdentity) directory.Identity = ReadIdentity(directory.Handle);
            return directory;
        }
        catch
        {
            directory.Dispose();
            throw;
        }
    }

    private void Pin(SafeFileHandle handle)
    {
        _components.Add(handle);
        if ((Metadata(handle).Attributes & DirectoryOrLink) != FileAttributes.Directory)
            throw new IOException("Cache path contains a reparse point or a non-directory component.");
    }

    internal static WindowsCacheDirectory? OpenPrivate(string parentPath, string prefix, bool create)
    {
        // Concurrent first writers must not each create and register a different directory.
        lock (OwnershipGate) return OpenPrivateCore(parentPath, prefix, create);
    }

    private static WindowsCacheDirectory? OpenPrivateCore(string parentPath, string prefix, bool create)
    {
        using var parent = Open(parentPath, createMissing: create);
        // A private child is unsafe on filesystems that cannot enforce its ACL.
        if (!GetVolumeInformationByHandleW(parent.Handle, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero,
                out var flags, IntPtr.Zero, 0))
            throw IoError(Marshal.GetLastWin32Error());
        const uint persistentAcls = 0x8;
        if ((flags & persistentAcls) == 0) throw new IOException("Private cache storage requires persistent ACLs.");
        var valueName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parent.Path.ToUpperInvariant() + "\0" + prefix)));
        using var key = create ? Registry.CurrentUser.CreateSubKey(OwnershipKey) : Registry.CurrentUser.OpenSubKey(OwnershipKey);
        if (key is null) return null;
        // Only a receipt in the user's registry can authorize reuse. The volume
        // and file ID also reject another object substituted at the same path.
        if (key.GetValue(valueName) is string receipt && receipt.Split(':') is [var savedSuffix, var savedIdentity]
            && Guid.TryParseExact(savedSuffix, "N", out _))
        {
            try
            {
                var name = $"{prefix}-{savedSuffix}";
                // Inspect the receipt's leaf without following links or requiring it
                // to be a directory. A file or link is left intact, never adopted.
                using var leaf = OpenChild(parent.Handle, name, ReadAttributes, ShareRead | ShareWrite, OpenExisting, 0);
                if ((Metadata(leaf).Attributes & DirectoryOrLink) == FileAttributes.Directory
                    && ReadIdentity(leaf).ToString() == savedIdentity)
                {
                    var existing = Open(System.IO.Path.Combine(parent.Path, name));
                    if (existing.Identity.ToString() == savedIdentity) return existing;
                    existing.Dispose();
                }
            }
            // A missing leaf or refused reparse can get a fresh sibling. Keep the
            // receipt on access or sharing failures, which may be temporary.
            catch (IOException error) when ((error.HResult & 0xffff) is 2 or 3 or ReparsePointEncountered) { }
        }
        if (!create) return null;

        using var user = WindowsIdentity.GetCurrent();
        var sid = user.User?.Value ?? throw new IOException("Cannot identify the cache owner.");
        // Do not inherit permissions from a potentially shared destination.
        // SYSTEM and the current user are the only trustees; children inherit.
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW($"O:{sid}D:P(A;OICI;FA;;;{sid})(A;OICI;FA;;;SY)",
                1, out var descriptor, IntPtr.Zero))
            throw IoError(Marshal.GetLastWin32Error());
        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var name = $"{prefix}-{suffix}";
            // FILE_CREATE returns the newly created object and its handle in one
            // operation; a pre-existing directory is never treated as ours.
            using var created = OpenChild(parent.Handle, name, ReadAttributes | Traverse, ShareRead | ShareWrite,
                CreateNew, DirectoryFile, descriptor);
            var identity = ReadIdentity(created);
            var directory = Open(System.IO.Path.Combine(parent.Path, name));
            try
            {
                if (directory.Identity != identity) throw new IOException("Cache directory was replaced during creation.");
                key.SetValue(valueName, $"{suffix}:{identity}");
                return directory;
            }
            catch
            {
                directory.Dispose();
                throw;
            }
        }
        finally { LocalFree(descriptor); }
    }

    internal static bool IsUnavailable(Exception error) => error is IOException or UnauthorizedAccessException or SecurityException;

    internal SafeFileHandle OpenForDeletion(string name) =>
        OpenChild(Handle, name, DeleteAccess | ReadAttributes, ShareRead, OpenExisting, 0);

    // Takes over one file from the directory that holds this private directory, where
    // builds before the private cache kept artwork for a linked cache. The caller names
    // a file it is about to request; the name never comes from listing that directory.
    // The file must be an ordinary one of acceptable size, with no other hard link and
    // owned by the current user. It is opened relative to the pinned parent without
    // following links, moved here without replacing anything, and then given this
    // directory's permissions in place of the ones it had.
    internal bool TryAdoptFromParent(string name, long maxBytes)
    {
        if (_components.Count < 2) return false;
        SafeFileHandle source;
        try
        {
            source = OpenChild(_components[^2], name, DeleteAccess | ReadAttributes | ReadControl | WriteDac,
                ShareRead, OpenExisting, NonDirectoryFile);
        }
        catch (Exception error) when (IsUnavailable(error)) { return false; }
        using (source)
        {
            if (!GetFileInformationByHandle(source, out var info)) return false;
            var size = ((long)info.SizeHigh << 32) | info.SizeLow;
            if ((info.Attributes & DirectoryOrLink) != 0 || info.LinkCount != 1 || size <= 0 || size > maxBytes
                || !IsOwnedByCurrentUser(source))
                return false;
            try { Rename(source, Handle, name, replace: false); }
            catch (IOException) { return false; } // for one, the name is already taken here
            try
            {
                InheritPermissions(source);
                return true;
            }
            catch (IOException)
            {
                // It is in the private directory but still open to whoever could reach it
                // before. It was established as this user's cache file, so drop it; the
                // caller downloads the artwork again.
                try { Delete(source); }
                catch (IOException) { /* Maintenance removes what it cannot validate. */ }
                return false;
            }
        }
    }

    private static bool IsOwnedByCurrentUser(SafeFileHandle handle)
    {
        const uint ownerInformation = 0x1;
        if (GetSecurityInfo(handle, FileObject, ownerInformation, out var owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                out var descriptor) != 0)
            return false;
        try
        {
            using var user = WindowsIdentity.GetCurrent();
            return owner != IntPtr.Zero && user.User is { } sid && sid.Equals(new SecurityIdentifier(owner));
        }
        finally { LocalFree(descriptor); }
    }

    private static unsafe void InheritPermissions(SafeFileHandle handle)
    {
        // An empty access list that is not protected from inheritance: the file keeps
        // no rule of its own and takes the rules of the directory it is now in.
        const uint daclInformation = 0x4, unprotectedDacl = 0x20000000;
        const int aclSize = 8;
        byte* empty = stackalloc byte[aclSize];
        new Span<byte>(empty, aclSize).Clear();
        empty[0] = 2; // ACL_REVISION
        *(ushort*)(empty + 2) = aclSize;
        var error = SetSecurityInfo(handle, FileObject, daclInformation | unprotectedDacl, IntPtr.Zero, IntPtr.Zero,
            empty, IntPtr.Zero);
        if (error != 0) throw IoError((int)error);
    }

    // Renames an open file into `directory` under `name`, both identified by handle.
    private static unsafe void Rename(SafeFileHandle file, SafeFileHandle directory, string name, bool replace)
    {
        ValidateName(name);
        // FILE_RENAME_INFORMATION: BOOLEAN, aligned HANDLE, ULONG, WCHAR[].
        var header = IntPtr.Size * 2 + sizeof(uint);
        var nameBytes = name.Length * sizeof(char);
        var length = header + nameBytes;
        byte* buffer = stackalloc byte[length];
        new Span<byte>(buffer, length).Clear();
        buffer[0] = replace ? (byte)1 : (byte)0; // ReplaceIfExists
        *(IntPtr*)(buffer + IntPtr.Size) = directory.DangerousGetHandle();
        *(uint*)(buffer + IntPtr.Size * 2) = (uint)nameBytes;
        name.AsSpan().CopyTo(new Span<char>(buffer + header, name.Length));
        SetInformation(file, buffer, (uint)length, 10); // FileRenameInformation
        GC.KeepAlive(directory);
    }

    internal static (FileAttributes Attributes, long Size, long Written) Metadata(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw IoError(Marshal.GetLastWin32Error());
        return (info.Attributes, ((long)info.SizeHigh << 32) | info.SizeLow,
            DateTime.FromFileTimeUtc(((long)info.WriteHigh << 32) | info.WriteLow).Ticks);
    }

    private static FileIdentity ReadIdentity(SafeFileHandle handle)
    {
        if (!TryReadIdentity(handle, out var identity))
            throw IoError(Marshal.GetLastWin32Error());
        return identity;
    }

    internal bool MatchesIdentity(FileIdentity identity)
        => TryReadIdentity(Handle, out var actual) && actual == identity;

    private static bool TryReadIdentity(SafeFileHandle handle, out FileIdentity identity)
        => GetFileInformationByHandleEx(handle, 18, out identity, 24); // FileIdInfo

    internal static unsafe void Delete(SafeFileHandle handle)
    {
        byte delete = 1;
        SetInformation(handle, &delete, 1, 13); // FileDispositionInformation
    }

    internal StagedFile Stage(string name)
    {
        const uint genericReadWrite = 0xC0000000;
        var handle = OpenChild(Handle, name, genericReadWrite | DeleteAccess, 0, CreateNew, NonDirectoryFile);
        try { return new StagedFile(this, new FileStream(handle, FileAccess.ReadWrite)); }
        catch { handle.Dispose(); throw; }
    }

    internal sealed class StagedFile(WindowsCacheDirectory directory, FileStream stream) : IDisposable
    {
        private bool _published;
        internal FileStream Stream { get; } = stream;

        internal void Publish(string name)
        {
            ValidateName(name);
            Stream.Flush();
            Rename(Stream.SafeFileHandle, directory.Handle, name, replace: true);
            GC.KeepAlive(directory);
            _published = true;
        }

        public void Dispose()
        {
            try { if (!_published) Delete(Stream.SafeFileHandle); }
            catch (IOException) { /* A later pass can remove an abandoned staged file. */ }
            finally { Stream.Dispose(); }
        }
    }

    public void Dispose()
    {
        for (var i = _components.Count - 1; i >= 0; i--) _components[i].Dispose();
    }

    private static void ValidateName(string name)
    {
        if (name.Length is 0 or > 255 || name is "." or ".." || name.IndexOfAny(['\\', '/', ':', '\0']) >= 0)
            throw new IOException("Invalid cache entry name.");
    }

    private static unsafe SafeFileHandle OpenChild(SafeFileHandle parent, string name, uint access, uint share,
        uint disposition, uint options, IntPtr descriptor = default)
    {
        ValidateName(name);
        fixed (char* text = name)
        {
            var length = (ushort)(name.Length * sizeof(char));
            var ntName = new UnicodeString { Length = length, MaximumLength = length, Buffer = text };
            var attributes = new ObjectAttributes
            {
                Length = (uint)sizeof(ObjectAttributes), RootDirectory = parent.DangerousGetHandle(),
                // OBJ_CASE_INSENSITIVE | OBJ_DONT_REPARSE; never resolve a link in a relative open.
                ObjectName = &ntName, Attributes = 0x1040, SecurityDescriptor = descriptor
            };
            var status = NtCreateFile(out var handle, access | Synchronize, ref attributes, out _, IntPtr.Zero,
                (uint)FileAttributes.Normal, share, disposition,
                options | SyncIo | (disposition == CreateNew ? 0 : OpenReparsePoint), IntPtr.Zero, 0);
            GC.KeepAlive(parent);
            if (status >= 0) return handle;
            handle?.Dispose();
            throw IoError((int)RtlNtStatusToDosError(status));
        }
    }

    private static unsafe void SetInformation(SafeFileHandle handle, void* buffer, uint size, int kind)
    {
        var status = NtSetInformationFile(handle, out _, buffer, size, kind);
        if (status < 0) throw IoError((int)RtlNtStatusToDosError(status));
    }

    private static IOException IoError(int error) => new($"Cache filesystem operation failed (Windows error {error}).",
        unchecked((int)(0x80070000u | (uint)error)));

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInformation
    {
        public FileAttributes Attributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct UnicodeString { public ushort Length, MaximumLength; public char* Buffer; }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ObjectAttributes
    {
        public uint Length;
        public IntPtr RootDirectory;
        public UnicodeString* ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor, SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { public IntPtr Status, Information; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint sharing, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInformation info);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out FileIdentity info, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandleW(SafeFileHandle handle, IntPtr volumeName, uint volumeNameSize,
        IntPtr serial, IntPtr maxComponentLength, out uint flags, IntPtr fileSystemName, uint fileSystemNameSize);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision,
        out IntPtr securityDescriptor, IntPtr size);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern uint GetSecurityInfo(SafeFileHandle handle, int objectType, uint securityInfo,
        out IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl, out IntPtr securityDescriptor);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern unsafe uint SetSecurityInfo(SafeFileHandle handle, int objectType, uint securityInfo,
        IntPtr owner, IntPtr group, void* dacl, IntPtr sacl);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("ntdll.dll")]
    private static extern unsafe int NtCreateFile(out SafeFileHandle handle, uint access, ref ObjectAttributes attributes,
        out IoStatusBlock status, IntPtr allocationSize, uint fileAttributes, uint sharing, uint disposition,
        uint options, IntPtr eaBuffer, uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern unsafe int NtSetInformationFile(SafeFileHandle handle, out IoStatusBlock status,
        void* info, uint length, int kind);

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);
}
