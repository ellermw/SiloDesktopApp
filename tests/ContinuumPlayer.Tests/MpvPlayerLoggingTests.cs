namespace ContinuumPlayer.Tests;

public sealed class MpvPlayerLoggingTests
{
    [Fact]
    public void MpvPlayer_RotatesAndLimitsNoiseInMpvLog()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ContinuumPlayer.Player", "MpvPlayer.cs"));

        Assert.Contains("RotateLogIfNeeded(logPath", source);
        Assert.Contains("SetOption(\"msg-level\", \"all=warn\")", source);
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(pathParts)} from test output.");
    }
}
