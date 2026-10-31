using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Services;

public sealed partial class WindowsAppUpdateInstaller(string cacheDirectory) : IAppUpdateInstaller, IStagedAppUpdateInstaller
{
    private string? _closeConfirmation;
    private string? _handoffToken;
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B8E2F4A1-3C5D-4E6F-9A1B-2D3E4F5A6B7C}_is1";
    public bool IsSupported
    {
        get
        {
            var executable = Environment.ProcessPath;
            if (executable is null || !Path.GetFileName(executable).Equals("SiloPlayer.exe", StringComparison.OrdinalIgnoreCase)) return false;
            if(UpdateInstallation.FindRegisteredRoot(executable) is not null)return true;
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                try
                {
                    using var registry = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    using var key = registry.OpenSubKey(UninstallKey);
                    if (key?.GetValue("InstallLocation") is string location && SameDirectory(location, Path.GetDirectoryName(executable)!)) return true;
                }
                catch (Exception ex) { LocalLog.AppendLine("updates.txt", "installation_lookup_failed kind=" + ex.GetType().Name); }
            }
            return false;
        }
    }

    public async Task<bool> ScheduleAsync(AppRelease release, string installerPath, bool restart, CancellationToken ct)
    {
        try
        {
            if (!IsSupported) throw new InvalidOperationException("Install updates from the registered Silo desktop installation.");
            if (!GitHubReleaseReader.TryReleaseIdentity(release.Tag, release.IsBeta, out var numeric, out var semantic)
                || numeric != GitHubReleaseReader.Normalize(release.Version)
                || (release.SemanticVersion is { } declared && declared.Text != semantic?.Text)
                || numeric.Major != typeof(App).Assembly.GetName().Version!.Major
                || numeric <= GitHubReleaseReader.Normalize(typeof(App).Assembly.GetName().Version!))
                throw new InvalidDataException("The full update release identity is invalid or not newer.");
            var root = Path.GetFullPath(cacheDirectory);
            var expected = Path.Combine(root, GitHubReleaseReader.Normalize(release.Version).ToString(), GitHubReleaseReader.InstallerName);
            if (!Path.GetFullPath(installerPath).Equals(expected, StringComparison.OrdinalIgnoreCase)
                || !await AppUpdateDownloader.VerifyAsync(expected, release, ct))
                throw new InvalidDataException("The staged installer could not be verified.");
            var pe = FileVersionInfo.GetVersionInfo(expected);
            if (new Version(pe.FileMajorPart, pe.FileMinorPart, pe.FileBuildPart, pe.FilePrivatePart) != numeric)
                throw new InvalidDataException("The installer version does not match the verified release.");
            var installationRoot = UpdateInstallation.FindRegisteredRoot(Environment.ProcessPath!)
                ?? throw new InvalidOperationException("The registered installation was not found.");
            var versioned = !SameDirectory(installationRoot, AppContext.BaseDirectory);
            var helper = Path.Combine(AppContext.BaseDirectory, "Updates", "UpdateOnExit.ps1");
            if (versioned)
            {
                await Task.Run(() => UpdateInstallation.RequireProtectedMachineInstallation(installationRoot, AppContext.BaseDirectory), ct);
                var sourceVersion = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
                if (SideBySideInstallation.ReadState(installationRoot)?.ActiveVersion != sourceVersion
                    || !SideBySideInstallation.VerifyInstalledClientFile(installationRoot, sourceVersion, "SiloPlayer.exe").Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The original installed client changed.");
                helper = SideBySideInstallation.VerifyInstalledClientFile(installationRoot, sourceVersion, "Updates/UpdateOnExit.ps1");
            }
            AppUpdateDownloader.EnsureOrdinaryPath(helper);
            var script = Path.Combine(Path.GetDirectoryName(expected)!, "UpdateOnExit-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.Copy(helper, script, overwrite: false);
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(powershell)
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            using var current = Process.GetCurrentProcess();
            var handoffToken = Guid.NewGuid().ToString("N");
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                "-ParentPid", current.Id.ToString(CultureInfo.InvariantCulture), "-ParentStartTicks", current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                "-Version", release.Version.ToString(), "-Sha256", release.Sha256, "-ExpectedSize", release.Size.ToString(CultureInfo.InvariantCulture), "-AppPath", Environment.ProcessPath!, "-HandoffToken", handoffToken })
                start.ArgumentList.Add(arg);
            if (versioned) { start.ArgumentList.Add("-InstallRoot"); start.ArgumentList.Add(installationRoot); }
            if (restart) start.ArgumentList.Add("-Restart");
            ct.ThrowIfCancellationRequested();
            await WriteResultAsync(root, "scheduled", "The update will install after Silo closes.", release.Version.ToString(), ct);
            using var process = Process.Start(start) ?? throw new IOException("The update helper could not start.");
            _handoffToken = handoffToken;
            _closeConfirmation = Path.Combine(Path.GetDirectoryName(expected)!, "close-" + handoffToken + ".confirmed");
            LocalLog.AppendLine("updates.txt", $"handoff_scheduled version={release.Version} restart={restart} helperPid={process.Id}");
            return true;
        }
        catch (Exception ex)
        {
            LocalLog.AppendException("updates.txt", ex, "update_handoff_failed");
            try { await WriteResultAsync(cacheDirectory, "failed", "The update could not start. Open update details and try again.", release.Version.ToString(), CancellationToken.None); }
            catch (Exception journal) { LocalLog.AppendLine("updates.txt", "handoff_journal_failed kind=" + journal.GetType().Name); }
            return false;
        }
    }

    public void ConfirmNormalClose()
    {
        ConfirmStagedClose();
        var path = Interlocked.Exchange(ref _closeConfirmation, null);
        if (path is null || _handoffToken is null) return;
        AppUpdateDownloader.EnsureOrdinaryPath(path);
        File.WriteAllText(path, _handoffToken);
    }

    private static bool SameDirectory(string first, string second) => Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar)
        .Equals(Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static async Task WriteResultAsync(string root, string state, string message, string version, CancellationToken ct)
    {
        AppUpdateDownloader.EnsureOrdinaryPath(root); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "last-result.json"); AppUpdateDownloader.EnsureOrdinaryPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { state, message, version, utc = DateTimeOffset.UtcNow }), ct);
        File.Move(temporary, path, overwrite: true);
    }
}
