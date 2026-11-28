namespace SiloPlayer.Tests;

public sealed class InstallerPrivilegeTests
{
    [Fact]
    public async Task ActualInnoGuardsAndIsolatedUpgradePreserveForeignData()
    {
        var script = FindRepoPath("tests", "installer-safety", "Verify-InstallDirectory.ps1");
        var configuredEvidence = Environment.GetEnvironmentVariable("SILO_INSTALLER_TEST_RESULT_DIRECTORY");
        var evidence = string.IsNullOrWhiteSpace(configuredEvidence) ? null : configuredEvidence;
        var root = evidence ?? Path.Combine(Path.GetTempPath(), "silo-inno-runtime-" + Guid.NewGuid().ToString("N"));
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-ResultDirectory", root }) start.ArgumentList.Add(arg);
        try
        {
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120));
            Assert.True(process.ExitCode == 0, await output + await error);
            using var receipt = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "install-upgrade-uninstall.json")));
            // Uninstall removes everything (owner decision, 2026-11-26); upgrades still keep additions.
            Assert.True(receipt.RootElement.GetProperty("foreignFilePreservedOnUpgrade").GetBoolean());
            Assert.True(receipt.RootElement.GetProperty("installFolderRemovedOnUninstall").GetBoolean());
            Assert.True(receipt.RootElement.GetProperty("linkTargetPreservedOnUninstall").GetBoolean());
            Assert.True(receipt.RootElement.GetProperty("installedOverLeftovers").GetBoolean());
            Assert.True(receipt.RootElement.GetProperty("uninstallLogClosedBeforeCleanup").GetBoolean());
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
    public void SideBySideMigrationRetainsUnknownFilesAndDropsLegacyUninstallWipes()
    {
        // A registered root is not proof that every later addition is ours.
        // The executable runtime fixture exercises retained legacy uninstall
        // directives, unknown root files and additions inside a version folder.
        var installer = ReadRepoFile("installer", "SiloInstaller.iss").ReplaceLineEndings("\n");
        Assert.DoesNotContain("Type: filesandordirs; Name: \"{app}\\*\"", installer);
        Assert.Contains("UninstallLogMode=overwrite", installer);
        Assert.Contains("--silo-initialize", installer);
        Assert.Contains("--silo-uninstall-owned", installer);
        var prepare = installer[installer.IndexOf("function PrepareToInstall(", StringComparison.Ordinal)..];
        Assert.Contains("DirectoryHasForeignContent(Dir)", prepare[..prepare.IndexOf("procedure InstallWindowsAppRuntime;", StringComparison.Ordinal)]);
        var guard = installer[installer.IndexOf("function DirectoryHasForeignContent(", StringComparison.Ordinal)..installer.IndexOf("function PrepareToInstall(", StringComparison.Ordinal)];
        Assert.Contains("RegisteredSiloDirectory(Dir)", guard);
        Assert.DoesNotContain("'unins000.dat'", guard);
        Assert.Contains("FindFirst(AddBackslash(Dir) + '*', FindRec)", guard);
        Assert.DoesNotContain("Type: filesandordirs; Name: \"{app}\"", installer);
    }

    [Fact]
    public void UninstallSweepAndLeftoverCleanupStayInsideAVerifiedDedicatedFolder()
    {
        // The runtime fixture above executes these paths; this pins their guards in the source.
        var installer = ReadRepoFile("installer", "SiloInstaller.iss").ReplaceLineEndings("\n");
        var sweep = installer[installer.IndexOf("procedure CurUninstallStepChanged(", StringComparison.Ordinal)..installer.IndexOf("procedure InstallWindowsAppRuntime;", StringComparison.Ordinal)];
        Assert.Contains("usPostUninstall", sweep);
        Assert.Contains("not UnsafeInstallDirectory(Dir)", sweep);
        Assert.Contains("not SharedWithAnotherUninstaller(Dir)", sweep);
        var prepare = installer[installer.IndexOf("function PrepareToInstall(", StringComparison.Ordinal)..installer.IndexOf("function SharedWithAnotherUninstaller(", StringComparison.Ordinal)];
        Assert.True(prepare.IndexOf("UnsafeInstallDirectory(Dir)", StringComparison.Ordinal) < prepare.IndexOf("DelTree(", StringComparison.Ordinal));
        Assert.Contains("if SiloLeftoverDirectory(Dir) then", prepare);
        var leftover = installer[installer.IndexOf("function SiloLeftoverDirectory(", StringComparison.Ordinal)..installer.IndexOf("function PrepareToInstall(", StringComparison.Ordinal)];
        Assert.Contains("HasLinkedInstallContent(Dir, 0)", leftover);
        Assert.Contains("'.silo-update'", leftover);
        // The elevated helper still runs before logged removal, but never unconditionally.
        Assert.Contains("(CurUninstallStep = usUninstall) and RunOwnedUninstall", sweep);
        Assert.Contains("Exec(Launcher, '--silo-uninstall-owned'", sweep);
        Assert.DoesNotContain("Parameters: \"--silo-uninstall-owned\"", installer);
    }

    [Fact]
    public void UninstallReportsAnUnsafePathBeforeAttemptingToLaunch()
    {
        var installer = ReadRepoFile("installer", "SiloInstaller.iss");
        var preflight = installer[installer.IndexOf("function InitializeUninstall:", StringComparison.Ordinal)..];
        var guard = preflight[preflight.IndexOf("if UnsafeInstallDirectory(Dir) or HasLinkedInstallPath(Launcher)", StringComparison.Ordinal)
            ..preflight.IndexOf("if not FileExists(Launcher)", StringComparison.Ordinal)];
        Assert.Contains("Log('Uninstall blocked:", guard);
        Assert.Contains("SuppressibleMsgBox('Silo cannot uninstall safely", guard);
        Assert.Contains("Result := False;", guard);
        Assert.DoesNotContain("Exec(", guard);
    }

    [Fact]
    public void LinkedPathGuardReadsTheErrorCapturedAfterItsOwnApiCall()
    {
        // A missing fresh-install folder must report "not found", not an error
        // left by Setup's own API calls after GetFileAttributesW returned.
        var installer = ReadRepoFile("installer", "SiloInstaller.iss").ReplaceLineEndings("\n");
        var linked = installer[installer.IndexOf("function HasLinkedInstallPath(", StringComparison.Ordinal)..installer.IndexOf("function SameInstallDirectory(", StringComparison.Ordinal)];
        Assert.Contains("ErrorCode := DLLGetLastError;", linked);
        Assert.DoesNotContain("GetLastError@kernel32", installer);
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
