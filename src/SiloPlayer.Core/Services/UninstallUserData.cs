using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Per-user data removed at uninstall: the app's folder under Local AppData (settings, profile
/// state, logs, caches, downloaded updates, boot receipts, reader WebView2 data), WebView2's
/// default folder for SiloPlayer.exe and subtitle temporary files. Sign-in tokens live in Windows
/// Credential Manager and are removed by <see cref="CredentialStore.DeleteAllSiloCredentials"/>.
/// </summary>
public static partial class UninstallUserData
{
    public sealed record Profile(string LocalAppData, string Temp, bool CurrentAccount);

    public static void Remove(Profile profile, UninstallReport report)
    {
        // Exact names only. Every walk is link-safe: the uninstaller is elevated and these are user-writable.
        foreach (var directory in new[]
        {
            Path.Combine(profile.LocalAppData, "SiloPlayer"),
            Path.Combine(profile.LocalAppData, "SiloPlayer.exe.WebView2"),
            Path.Combine(profile.Temp, "SiloPlayer.exe.WebView2"),
        })
        {
            try { SideBySideInstallation.DeleteTreeWithoutFollowingLinks(directory, report, queueLockedForRestart: false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { report.Retained.Add(directory); }
        }
        try
        {
            if (!Directory.Exists(profile.Temp)) return;
            foreach (var file in Directory.GetFiles(profile.Temp, "silo-subtitle-*"))
                if (SubtitleTemporaryFile().IsMatch(Path.GetFileName(file)))
                    SideBySideInstallation.DeleteTreeWithoutFollowingLinks(file, report, queueLockedForRestart: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { }
    }

    // PlayerService and PlayerService.SubtitleSync: silo-subtitle-<guid>.<ext> and silo-subtitle-sync-<guid>.<ext>.
    [GeneratedRegex(@"^silo-subtitle-(?:sync-)?[0-9a-f]{32}\.[A-Za-z0-9]{1,16}$", RegexOptions.CultureInvariant)]
    private static partial Regex SubtitleTemporaryFile();

    /// <summary>
    /// The account running uninstall, plus the signed-in user of this session when an administrator
    /// elevated the uninstall for them with another account ("over the shoulder"). The elevated
    /// account's Local AppData and Inno's {localappdata} are the administrator's in that case.
    /// </summary>
    public static IReadOnlyList<Profile> UninstallingUsers()
    {
        var profiles = new List<Profile>
        {
            new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.GetTempPath(), true),
        };
        // Best effort: an account that cannot be resolved exactly keeps its data rather than risk another's.
        try { if (SessionUserProfile() is { } other) profiles.Add(other); }
        catch (Exception) { }
        return profiles;
    }

    private static Profile? SessionUserProfile()
    {
        if (!ProcessIdToSessionId((uint)Environment.ProcessId, out var session) || session == 0) return null;
        var user = QuerySession(session, 5);
        if (string.IsNullOrEmpty(user)) return null;
        var sid = (SecurityIdentifier)new NTAccount(QuerySession(session, 7) ?? "", user).Translate(typeof(SecurityIdentifier));
        using (var current = WindowsIdentity.GetCurrent()) if (sid.Equals(current.User)) return null;
        // That account's own profile and Local AppData, from its loaded registry hive. Anything
        // unusual (redirection outside the profile, unexpanded variables) is left alone.
        using var list = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid.Value);
        if (list?.GetValue("ProfileImagePath") is not string image) return null;
        image = Environment.ExpandEnvironmentVariables(image);
        if (image.Contains('%') || !Path.IsPathFullyQualified(image)) return null;
        var profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(image));
        using var folders = Registry.Users.OpenSubKey(sid.Value + @"\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        var local = folders?.GetValue("Local AppData", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? @"%USERPROFILE%\AppData\Local";
        local = local.Replace("%USERPROFILE%", profile, StringComparison.OrdinalIgnoreCase);
        if (local.Contains('%') || !Path.IsPathFullyQualified(local)) return null;
        local = Path.TrimEndingDirectorySeparator(Path.GetFullPath(local));
        return local.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? new(local, Path.Combine(local, "Temp"), false) : null;
    }

    private static string? QuerySession(uint session, int information)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, session, information, out var buffer, out _)) return null;
        try { return Marshal.PtrToStringUni(buffer); }
        finally { WTSFreeMemory(buffer); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WTSQuerySessionInformationW")]
    private static extern bool WTSQuerySessionInformation(IntPtr server, uint session, int information, out IntPtr buffer, out uint bytes);
    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
