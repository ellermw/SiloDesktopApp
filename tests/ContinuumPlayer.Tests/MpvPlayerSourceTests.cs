namespace ContinuumPlayer.Tests;

public sealed class MpvPlayerSourceTests
{
    [Fact]
    public void CommandFailureLoggingRedactsQuerySecrets()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer.Player",
            "MpvPlayer.cs"));

        Assert.Contains("RedactCommandArgument", source);
        Assert.Contains("room_token", source);
        Assert.Contains("api_key", source);
        Assert.DoesNotContain(@"(?:token|access_token|refresh_token|profile_token)", source);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "ContinuumPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
