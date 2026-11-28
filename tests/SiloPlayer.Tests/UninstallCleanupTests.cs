using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

// Uninstall removes everything (owner decision, 2026-11-26). Every case here uses a temporary
// profile or a test-only credential name; nothing touches this machine's real Silo data.
public sealed class UninstallCleanupTests : IDisposable
{
    private readonly string sandbox = Path.Combine(Path.GetTempPath(), "silo-uninstall-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ProfileRemovalTakesTheAppsFoldersAndSubtitleTempsOnly()
    {
        var local = Path.Combine(sandbox, "Local");
        var temp = Path.Combine(local, "Temp");
        string Write(string path, string content = "x")
        { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); return path; }
        Write(Path.Combine(local, "SiloPlayer", "settings.json"));
        Write(Path.Combine(local, "SiloPlayer", "ImageCache", "poster.jpg"));
        Write(Path.Combine(local, "SiloPlayer", "updates", "incremental-" + new string('a', 64), "prepared", "client", "SiloPlayer.dll"));
        Write(Path.Combine(local, "SiloPlayer", "reader-webview", "EBWebView", "Local State"));
        var readOnly = Write(Path.Combine(local, "SiloPlayer", "diagnostics", "stall.json"));
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        Write(Path.Combine(local, "SiloPlayer.exe.WebView2", "EBWebView", "Local State"));
        Write(Path.Combine(temp, "SiloPlayer.exe.WebView2", "EBWebView", "Local State"));
        var subtitle = Write(Path.Combine(temp, "silo-subtitle-" + Guid.NewGuid().ToString("N") + ".srt"));
        var sync = Write(Path.Combine(temp, "silo-subtitle-sync-" + Guid.NewGuid().ToString("N") + ".ass"));
        var unrelatedTemp = Write(Path.Combine(temp, "silo-subtitle-notes.txt"), "not ours");
        var otherApp = Write(Path.Combine(local, "SiloPlayerTools", "keep.txt"), "another program");
        var outside = Write(Path.Combine(sandbox, "Documents", "keep.txt"), "outside the profile folder");
        CreateJunction(Path.Combine(local, "SiloPlayer", "linked-documents"), Path.GetDirectoryName(outside)!);

        var report = new UninstallReport();
        UninstallUserData.Remove(new(local, temp, true), report);

        Assert.Empty(report.Retained);
        Assert.False(Directory.Exists(Path.Combine(local, "SiloPlayer")));
        Assert.False(Directory.Exists(Path.Combine(local, "SiloPlayer.exe.WebView2")));
        Assert.False(Directory.Exists(Path.Combine(temp, "SiloPlayer.exe.WebView2")));
        Assert.False(File.Exists(subtitle)); Assert.False(File.Exists(sync));
        Assert.Equal("not ours", File.ReadAllText(unrelatedTemp));
        Assert.Equal("another program", File.ReadAllText(otherApp));
        Assert.Equal("outside the profile folder", File.ReadAllText(outside));
    }

    [Fact]
    public void ProfileRemovalNeverWaitsForAFileInUse()
    {
        var local = Path.Combine(sandbox, "Local");
        var log = Path.Combine(local, "SiloPlayer", "mpv_log.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!); File.WriteAllText(log, "open");
        File.WriteAllText(Path.Combine(local, "SiloPlayer", "settings.json"), "{}");
        var report = new UninstallReport();
        var clock = Stopwatch.StartNew();
        using (new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.None))
            UninstallUserData.Remove(new(local, Path.Combine(local, "Temp"), true), report);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Contains(log, report.Retained, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, report.QueuedForRestart);
        Assert.False(File.Exists(Path.Combine(local, "SiloPlayer", "settings.json")));
    }

    [Fact]
    public void UninstallingUsersAlwaysIncludesTheRunningAccount()
    {
        // Same-account elevation is the normal case here; another session user is added only when it differs.
        var profiles = UninstallUserData.UninstallingUsers();
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), profiles[0].LocalAppData);
        Assert.True(profiles[0].CurrentAccount);
        Assert.All(profiles.Skip(1), profile => Assert.False(profile.CurrentAccount));
    }

    [Fact]
    public void SignInRemovalDeletesEveryEntryWithTheSiloPrefixOnly()
    {
        var prefix = "SiloPlayerUninstallTest" + Guid.NewGuid().ToString("N") + ":";
        var mine = new[] { prefix + "https://synthetic.invalid:access_token", prefix + "https://synthetic.invalid:refresh_token" };
        var lookalike = prefix.TrimEnd(':') + "x:https://synthetic.invalid:access_token";
        try
        {
            foreach (var target in mine.Append(lookalike)) WriteGenericCredential(target, "synthetic value");
            Assert.Equal(2, CredentialStore.DeleteAllWithPrefix(prefix));
            Assert.All(mine, target => Assert.False(CredentialExists(target)));
            Assert.True(CredentialExists(lookalike));
            Assert.Equal(0, CredentialStore.DeleteAllWithPrefix(prefix));
        }
        finally { foreach (var target in mine.Append(lookalike)) CredDelete(target, 1, 0); }
        Assert.Throws<ArgumentException>(() => CredentialStore.DeleteAllWithPrefix("Silo*:"));
        Assert.Throws<ArgumentException>(() => CredentialStore.DeleteAllWithPrefix("SiloPlayer"));
    }

    [Fact]
    public void LauncherTouchesProfileDataOnlyForTheRegisteredMachineInstallation()
    {
        var source = ReadRepoFile("src", "SiloPlayer.Launcher", "Program.cs").ReplaceLineEndings("\n");
        var uninstall = source[source.IndexOf("private static int UninstallOwned(", StringComparison.Ordinal)..];
        var gate = uninstall.IndexOf("FindRegisteredRoot(Path.Combine(root, \"SiloPlayer.exe\"), machineOnly: true)", StringComparison.Ordinal);
        Assert.True(gate > 0);
        Assert.True(gate < uninstall.IndexOf("UninstallUserData.Remove(", StringComparison.Ordinal));
        Assert.True(gate < uninstall.IndexOf("CredentialStore.DeleteAllSiloCredentials()", StringComparison.Ordinal));
        Assert.Contains("RemoveOwnedVersionsForUninstall(root, queueLockedForRestart: true)", uninstall);
    }

    private static void CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, ArgumentList = { "/d", "/c", "mklink", "/J", link, target } })!;
        process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(15000)); Assert.Equal(0, process.ExitCode);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }

    private static void WriteGenericCredential(string target, string value)
    {
        var blob = Encoding.UTF8.GetBytes(value);
        var credential = new Credential { Type = 1, TargetName = target, CredentialBlobSize = (uint)blob.Length, CredentialBlob = Marshal.AllocHGlobal(blob.Length), Persist = 2, UserName = "synthetic" };
        try
        {
            Marshal.Copy(blob, 0, credential.CredentialBlob, blob.Length);
            Assert.True(CredWrite(ref credential, 0), "Could not write a test credential.");
        }
        finally { Marshal.FreeHGlobal(credential.CredentialBlob); }
    }

    private static bool CredentialExists(string target)
    {
        if (!CredRead(target, 1, 0, out var pointer)) return false;
        CredFree(pointer); return true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags; public int Type; public string TargetName; public string Comment; public long LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob; public int Persist; public int AttributeCount;
        public IntPtr Attributes; public string TargetAlias; public string UserName;
    }
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref Credential credential, int flags);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, int type, int flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);

    public void Dispose()
    {
        try
        {
            // Unlink any junction first so the cleanup itself can never follow one.
            if (Directory.Exists(sandbox))
                foreach (var link in Directory.EnumerateDirectories(sandbox, "*", SearchOption.AllDirectories)
                             .Where(path => new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)).ToArray())
                    Directory.Delete(link);
            foreach (var file in Directory.Exists(sandbox) ? Directory.EnumerateFiles(sandbox, "*", SearchOption.AllDirectories) : [])
                File.SetAttributes(file, FileAttributes.Normal);
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
