namespace ContinuumPlayer.Tests;

public sealed class PlayerServiceSourceTests
{
    [Fact]
    public void RequiredTranscodeStartupFailuresDoNotFallThroughToStreamEndpoint()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("Failed to start transcode playback.", source);
        Assert.DoesNotContain(
            """
            LogToFile("player_transcode_error.txt", ex.ToString());
                        return (null, null);
            """,
            source);
    }

    [Fact]
    public void LogicalEndFallbackUsesNaturalEndPath()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("PlaybackNaturalEndDetector", source);
        Assert.Contains("Logical natural end detected", source);
        Assert.Contains("HandleNaturalPlaybackEnded", source);
        Assert.Contains("HandleNaturalPlaybackEnded(naturalEndDecision.Reason);", source);
        Assert.Contains("ShowPlayingNextRequested?.Invoke();", source);
        Assert.Contains("PlaybackEnded?.Invoke();", source);
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
