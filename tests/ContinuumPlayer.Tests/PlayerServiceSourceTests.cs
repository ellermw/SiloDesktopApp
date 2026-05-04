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

    [Fact]
    public void PlaybackWebSocketHandlesRealtimeMarkerUpdates()
    {
        var playerService = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer",
            "Services",
            "PlayerService.cs"));
        var websocket = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer",
            "Services",
            "PlaybackWebSocket.cs"));

        Assert.Contains("markers_updated", playerService);
        Assert.Contains("EventReceived", websocket);
        Assert.Contains("MarkersChanged", playerService);
        Assert.Contains("ApplyRealtimeMarkersUpdated", playerService);
    }

    [Fact]
    public void PlayerOverlayUsesActiveVersionMarkers()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer",
            "Controls",
            "PlayerOverlay.xaml.cs"));

        Assert.Contains("ActiveIntro", source);
        Assert.Contains("ActiveCredits", source);
        Assert.Contains("MarkersChanged", source);
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
