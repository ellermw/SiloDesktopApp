namespace SiloPlayer.Tests;

public sealed class MpvOscScriptTests
{
    [Fact]
    public void NextEpisodeUsesDistinctGlyphAndAction()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("local function draw_next_episode_icon", script);
        Assert.Contains("draw_next_episode_icon(ass, L.btn_next_ep.cx", script);
        Assert.DoesNotContain("draw_skip_fwd_icon(ass, L.btn_next_ep.cx", script);
        Assert.Contains("mp.commandv(\"script-message\", \"silo-next-episode\")", script);
    }

    [Fact]
    public void StatsOverlay_DoesNotCountDelayedFramesAsDroppedFrames()
    {
        var script = File.ReadAllText(FindOscScriptPath());
        var droppedLabelIndex = script.IndexOf("Dropped frames", StringComparison.Ordinal);
        Assert.True(droppedLabelIndex >= 0);

        var droppedCalculationStart = Math.Max(0, droppedLabelIndex - 500);
        var droppedCalculation = script[droppedCalculationStart..droppedLabelIndex];

        Assert.Contains("frame-drop-count", droppedCalculation);
        Assert.Contains("decoder-frame-drop-count", droppedCalculation);
        Assert.DoesNotContain("vo-delayed-frame-count", droppedCalculation);
        Assert.Contains("Delayed frames", script);
    }

    [Fact]
    public void PlayerHud_MatchesCurrentWebUiThreeColumnCinemaLayout()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("hud_height          = 172", script);
        Assert.Contains("x = W / 2 - main_size / 2", script);
        Assert.Contains("L.metadata =", script);
        Assert.Contains("Seek rail spans the frame", script);
        Assert.Contains("Top-left chrome matches VideoPlayer.tsx", script);
        Assert.Contains("Transparent cinema HUD gradient", script);
        Assert.Contains("draw_secondary_disc", script);
        Assert.Contains("state.next_ep_available", script);
        Assert.Contains("osc-set-audio-tracks", script);
        Assert.Contains("silo-audio-select", script);
        Assert.Contains("osc-set-chapters", script);
        Assert.Contains("render_chapter_menu", script);
        Assert.DoesNotContain("-- 2. Bar background", script);
    }

    [Fact]
    public void SeekRail_MapsEveryCurrentWebUiMarkerKind()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("state.intro_start, state.intro_end", script);
        Assert.Contains("state.recap_start, state.recap_end", script);
        Assert.Contains("state.credits_start, state.credits_end", script);
        Assert.Contains("state.preview_start, state.preview_end", script);
        Assert.Contains("data.recap_start", script);
        Assert.Contains("data.preview_start", script);
    }

    private static string FindOscScriptPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "libs", "mpv", "scripts", "silo-osc.lua");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate libs/mpv/scripts/silo-osc.lua from test output.");
    }
}
