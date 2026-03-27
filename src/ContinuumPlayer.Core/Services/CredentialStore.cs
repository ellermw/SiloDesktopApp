using System.Runtime.InteropServices;
using System.Text;

namespace ContinuumPlayer.Core.Services;

public class CredentialStore
{
    private const string CredentialPrefix = "ContinuumPlayer:";

    public void SaveCredential(string serverUrl, string key, string value)
    {
        var targetName = $"{CredentialPrefix}{serverUrl}:{key}";
        var credentialBlob = Encoding.UTF8.GetBytes(value);

        var credential = new NativeMethods.CREDENTIAL
        {
            Type = NativeMethods.CRED_TYPE_GENERIC,
            TargetName = targetName,
            CredentialBlobSize = (uint)credentialBlob.Length,
            CredentialBlob = Marshal.AllocHGlobal(credentialBlob.Length),
            Persist = NativeMethods.CRED_PERSIST_LOCAL_MACHINE,
            UserName = key
        };

        try
        {
            Marshal.Copy(credentialBlob, 0, credential.CredentialBlob, credentialBlob.Length);
            NativeMethods.CredWrite(ref credential, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(credential.CredentialBlob);
        }
    }

    public string? LoadCredential(string serverUrl, string key)
    {
        var targetName = $"{CredentialPrefix}{serverUrl}:{key}";
        if (!NativeMethods.CredRead(targetName, NativeMethods.CRED_TYPE_GENERIC, 0, out var credentialPtr))
            return null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlobSize == 0)
                return null;
            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.UTF8.GetString(blob);
        }
        finally
        {
            NativeMethods.CredFree(credentialPtr);
        }
    }

    public void DeleteCredential(string serverUrl, string key)
    {
        var targetName = $"{CredentialPrefix}{serverUrl}:{key}";
        NativeMethods.CredDelete(targetName, NativeMethods.CRED_TYPE_GENERIC, 0);
    }

    public void DeleteAllForServer(string serverUrl)
    {
        DeleteCredential(serverUrl, "access_token");
        DeleteCredential(serverUrl, "refresh_token");
    }

    private static class NativeMethods
    {
        public const int CRED_TYPE_GENERIC = 1;
        public const int CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredWrite(ref CREDENTIAL credential, int flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredRead(string targetName, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern void CredFree(IntPtr credential);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredDelete(string targetName, int type, int flags);
    }
}
