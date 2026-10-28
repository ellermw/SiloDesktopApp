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
