namespace SiloPlayer.Tests;

public sealed class FullscreenStateSyncSourceTests
{
    [Fact]
    public void EveryPlayerStateTransitionPublishesFullscreenVisualState()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs");
        var overlay = ReadRepoFile("src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs");

        Assert.Contains("PublishFullscreenVisualState(newState == PlayerState.Fullscreen)", service);
        Assert.Contains("SendScriptMessage(\"osc-fullscreen-state\"", service);
        Assert.Contains("PublishFullscreenVisualState(false);", service);
        Assert.Contains("SyncFullscreenIcon();", overlay);
    }

    [Fact]
    public void OscFullscreenIconIsStateDriven()
    {
        var osc = ReadRepoFile("libs", "mpv", "scripts", "silo-osc.lua");
        Assert.Contains("mp.register_script_message(\"osc-fullscreen-state\"", osc);
        Assert.Contains("state.fullscreen = (val == \"true\")", osc);
        Assert.Contains("draw_fullscreen_icon", osc);
    }

    [Fact]
    public void PictureInPictureStateAndEpisodeActionsRoundTripThroughHost()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs");

        Assert.Contains("PublishPictureInPictureVisualState", service);
        Assert.Contains("case \"silo-pip-toggle\"", service);
        Assert.Contains("case \"silo-prev-episode\"", service);
        Assert.Contains("PlayPreviousEpisodeAsync", service);
        Assert.Contains("osc-set-episode-navigation", service);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? "";
        if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("Could not find repository root.");
        return File.ReadAllText(Path.Combine([dir, .. parts]));
    }
}
