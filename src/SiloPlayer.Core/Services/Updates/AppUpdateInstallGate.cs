using System.Security.Cryptography;
using System.Text;

namespace SiloPlayer.Core.Services.Updates;

public static class AppUpdateInstallGate
{
    public static string FullInstallerNameFor(string executable) => GateName(executable, @"Global\SiloDesktopFullUpdate-");
    public static string NameFor(string executable) => GateName(executable, @"Global\SiloDesktopUpdate-");
    private static string GateName(string executable, string prefix)
    {
        var folder = (UpdateInstallation.FindRegisteredRoot(executable) ?? Path.GetDirectoryName(Path.GetFullPath(executable))!)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        return prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..16];
    }

    public static bool InstallationInProgress(string executable)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return IsHeld(NameFor(executable)) || IsHeld(FullInstallerNameFor(executable));
    }

    private static bool IsHeld(string name)
    {
        try
        {
            if (!Mutex.TryOpenExisting(name, out var gate)) return false;
            using (gate)
            {
                var acquired = false;
                try { acquired = gate.WaitOne(0); return !acquired; }
                catch (AbandonedMutexException) { acquired = true; return false; }
                finally { if (acquired) gate.ReleaseMutex(); }
            }
        }
        catch (UnauthorizedAccessException) { return true; }
    }
}
