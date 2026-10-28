namespace SiloPlayer.Tests;

public sealed class InstallerPrivilegeTests
{
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
        // [InstallDelete] {app}\* and [UninstallDelete] {app} run elevated. A
        // typed, already populated folder must be refused before either runs.
        var installer = ReadRepoFile("installer", "SiloInstaller.iss").ReplaceLineEndings("\n");
        Assert.Contains("Type: filesandordirs; Name: \"{app}\\*\"", installer);
        var prepare = installer[installer.IndexOf("function PrepareToInstall(", StringComparison.Ordinal)..];
        Assert.Contains("DirectoryHasForeignContent(Dir)", prepare[..prepare.IndexOf("end;", StringComparison.Ordinal)]);
        var guard = installer[installer.IndexOf("function DirectoryHasForeignContent(", StringComparison.Ordinal)..installer.IndexOf("function PrepareToInstall(", StringComparison.Ordinal)];
        // An existing Silo installation (upgrades, the in-app updater's /DIR) stays allowed.
        Assert.Contains("'{#MyAppExeName}'", guard);
        Assert.Contains("'unins000.dat'", guard);
        Assert.Contains("FindFirst(AddBackslash(Dir) + '*', FindRec)", guard);
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
