namespace SiloPlayer.Tests;

public sealed class InstallerPrivilegeTests
{
    [Fact]
    public void PostInstallLaunchDropsSetupElevation()
    {
        var installer = ReadRepoFile("installer", "SiloInstaller.iss").ReplaceLineEndings("\n");
        Assert.Contains("PrivilegesRequired=admin", installer);

        // In admin install mode Inno Setup runs [Run] entries with Setup's
        // elevated token unless runasoriginaluser is set.
        var launch = installer.Split('\n').Single(line =>
            line.StartsWith("Filename: \"{app}\\{#MyAppExeName}\"", StringComparison.Ordinal) && line.Contains("postinstall"));
        Assert.Contains("runasoriginaluser", launch);
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
