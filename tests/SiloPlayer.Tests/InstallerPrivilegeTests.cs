namespace SiloPlayer.Tests;

public sealed class InstallerPrivilegeTests
{
    [Fact]
    public async Task ActualInnoGuardsAndIsolatedUpgradePreserveForeignData()
    {
        var script = FindRepoPath("tests", "installer-safety", "Verify-InstallDirectory.ps1");
        var evidence = Environment.GetEnvironmentVariable("SILO_INSTALLER_TEST_RESULT_DIRECTORY");
        var root = evidence ?? Path.Combine(Path.GetTempPath(), "silo-inno-runtime-" + Guid.NewGuid().ToString("N"));
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-ResultDirectory", root }) start.ArgumentList.Add(arg);
        try
        {
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.True(process.ExitCode == 0, await output + await error);
            using var receipt = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "install-upgrade-uninstall.json")));
            Assert.True(receipt.RootElement.GetProperty("foreignFilePreservedOnUninstall").GetBoolean());
            Assert.False(receipt.RootElement.GetProperty("productionAppChanged").GetBoolean());
        }
        finally { if (evidence is null && Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static string FindRepoPath(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Installer runtime fixture not found.");
    }

    [Fact]
    public void PostInstallLaunchExplicitlyUsesOriginalCallerCredentials()
    {
        var installer = ReadRepoFile("installer", "SiloInstaller.iss").ReplaceLineEndings("\n");
        Assert.Contains("PrivilegesRequired=admin", installer);

        // postinstall already defaults to runasoriginaluser. Keep the policy
        // explicit; this source check does not establish process-token behavior.
        var launch = installer.Split('\n').Single(line =>
            line.StartsWith("Filename: \"{app}\\{#MyAppExeName}\"", StringComparison.Ordinal) && line.Contains("postinstall"));
        Assert.Contains("runasoriginaluser", launch);
        Assert.DoesNotContain("runascurrentuser", launch);
    }

    [Fact]
    public void InstallFolderWipeIsLimitedToFoldersThatHoldOnlySilo()
    {
        // Clean-payload upgrades require Silo registration; uninstall removes
        // logged files rather than recursively wiping unrelated additions.
        var installer = ReadRepoFile("installer", "SiloInstaller.iss").ReplaceLineEndings("\n");
        Assert.Contains("Type: filesandordirs; Name: \"{app}\\*\"", installer);
        var prepare = installer[installer.IndexOf("function PrepareToInstall(", StringComparison.Ordinal)..];
        Assert.Contains("DirectoryHasForeignContent(Dir)", prepare[..prepare.IndexOf("procedure InstallWindowsAppRuntime;", StringComparison.Ordinal)]);
        var guard = installer[installer.IndexOf("function DirectoryHasForeignContent(", StringComparison.Ordinal)..installer.IndexOf("function PrepareToInstall(", StringComparison.Ordinal)];
        Assert.Contains("RegisteredSiloDirectory(Dir)", guard);
        Assert.DoesNotContain("'unins000.dat'", guard);
        Assert.Contains("FindFirst(AddBackslash(Dir) + '*', FindRec)", guard);
        Assert.DoesNotContain("Type: filesandordirs; Name: \"{app}\"", installer);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
