using System.Security.Cryptography;
using System.Text;

namespace SiloPlayer.Core.Services.Updates;

public static class AppUpdateInstallGate
{
    public static string NameFor(string executable)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(executable))!
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        return @"Global\SiloDesktopUpdate-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..16];
    }

    public static bool InstallationInProgress(string executable)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            if (!Mutex.TryOpenExisting(NameFor(executable), out var gate)) return false;
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
