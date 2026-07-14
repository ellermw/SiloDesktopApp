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
    public void EpisodeNavigationReservesCurrentWebUiSeriesControls()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("series_context", script);
        Assert.Contains("prev_ep_available", script);
        Assert.Contains("local function draw_prev_episode_icon", script);
        Assert.Contains("osc-set-episode-navigation", script);
        Assert.Contains("silo-prev-episode", script);
        Assert.Contains("silo-next-episode", script);
    }

    [Fact]
    public void PictureInPictureMatchesCurrentWebUiUtilityActionAndShortcut()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("local function draw_pip_icon", script);
        Assert.Contains("L.btn_pip = place_utility()", script);
        Assert.Contains("silo-pip-toggle", script);
        Assert.Contains("osc-pip-state", script);
        Assert.Contains("silo-pip-override", script);
    }

    [Fact]
    public void KeyboardShortcutsMatchCurrentWebUiPlayer()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("silo-play-pause-space", script);
        Assert.Contains("silo-play-pause-k", script);
        Assert.Contains("silo-captions-toggle", script);
        Assert.Contains("state.last_subtitle", script);
        Assert.Contains("silo-mute-toggle", script);
        Assert.Contains("silo-fs-override", script);
        Assert.Contains("silo-pip-override", script);
        Assert.Contains("silo-seek-back", script);
        Assert.Contains("silo-seek-fwd", script);
        Assert.Contains("silo-vol-up", script);
        Assert.Contains("silo-vol-down", script);
    }

    [Fact]
    public void SubtitleMenuExposesCurrentWebUiActionsWithoutOverflowing()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("Search Online…", script);
        Assert.Contains("Appearance…", script);
        Assert.Contains("Translate with AI…", script);
        Assert.Contains("local action_count = state.subtitle_ai_available and 4 or 3", script);
        Assert.Contains("\"DELAY\"", script);
        Assert.Contains("delta = -0.1", script);
        Assert.Contains("delta = 0.1", script);
        Assert.Contains("subtitle_menu_offset", script);
        Assert.Contains("max_visible_tracks", script);
    }

    [Fact]
    public void MarkerEditorIsAvailableInActiveNativeOsc()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("osc-set-marker-edit-available", script);
        Assert.Contains("L.btn_marker_edit", script);
        Assert.Contains("draw_marker_tags_icon", script);
        Assert.Contains("silo-marker-edit", script);
    }

    [Fact]
    public void QualityMenuMatchesCurrentWebUiStructureAndActiveLabel()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("visible_quality_tiers", script);
        Assert.Contains("Original (", script);
        Assert.Contains("requested_file_id", script);
        Assert.Contains("local has_versions = #versions > 1", script);
        Assert.Contains("active_quality_label()", script);
        Assert.Contains("state.quality_switching", script);
        Assert.Contains("table.concat(details, \" · \")", script);
        Assert.Contains("L.btn_quality.show_label", script);
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
        Assert.DoesNotContain("Delayed frames", script);
        Assert.Contains("CURRENT SOURCE FILE", script);
        Assert.Contains("Audio sample rate", script);
        Assert.Contains("Auto-switched from", script);
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

        Assert.Contains("{ key = \"intro\", label = \"Intro\"", script);
        Assert.Contains("{ key = \"recap\", label = \"Recap\"", script);
        Assert.Contains("{ key = \"credits\", label = \"Credits / Outro\"", script);
        Assert.Contains("{ key = \"preview\", label = \"Preview\"", script);
        Assert.Contains("data.recap_start", script);
        Assert.Contains("data.preview_start", script);
    }

    [Fact]
    public void SeekRail_MatchesCurrentWebUiChapterAndMarkerPreview()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("chapter_at_time", script);
        Assert.Contains("marker_at_time", script);
        Assert.Contains("Credits / Outro", script);
        Assert.Contains("Chapter boundaries are one-pixel", script);
        Assert.Contains("hover_chapter.title", script);
        Assert.Contains("hover_marker.start", script);
        Assert.Contains("silo-chapter-thumbnail-request", script);
        Assert.Contains("osc-set-chapter-thumbnail", script);
        Assert.Contains("overlay-add", script);
        Assert.Contains("fmt = \"bgra\"", script);
    }

    [Fact]
    public void AudioMenu_MatchesCurrentWebUiRichTrackRows()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("local badges = {}", script);
        Assert.Contains("codec_label = \"TRUEHD\"", script);
        Assert.Contains("codec_label = \"DTS-HD\"", script);
        Assert.Contains("channel_label = \"7.1\"", script);
        Assert.Contains("table.insert(badges, \"DEFAULT\")", script);
        Assert.Contains("track.sample_rate", script);
        Assert.Contains("track.bit_depth", script);
    }

    [Fact]
    public void WatchPartyPanelMatchesCurrentWebUiPlayerSurface()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("osc-set-watch-party", script);
        Assert.Contains("WATCH PARTY", script);
        Assert.Contains("Guests can pause & resume", script);
        Assert.Contains("Only you control playback", script);
        Assert.Contains("Allow Pause", script);
        Assert.Contains("Host Only", script);
        Assert.Contains("End watch party?", script);
        Assert.Contains("Syncing playback", script);
        Assert.Contains("silo-watch-party-action", script);
    }

    [Fact]
    public void CreditsRegionMatchesCurrentWebUiNextEpisodeCountdown()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("osc-set-next-episode-detail", script);
        Assert.Contains("next_ep_countdown_remaining = 10", script);
        Assert.Contains("UP NEXT IN ", script);
        Assert.Contains("Play Now", script);
        Assert.Contains("next_ep_countdown_cancelled = true", script);
        Assert.Contains("state.time_pos < state.credits_start", script);
        Assert.Contains("state.next_ep_countdown_overlay.z = 57", script);
    }

    [Fact]
    public void LiveSubtitleTranslationMatchesCurrentWebUiBufferingSurface()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("osc-set-translation-buffering", script);
        Assert.Contains("Preparing ", script);
        Assert.Contains("translation_buffering_overlay", script);
        Assert.Contains("translation_spinner_frame", script);
        Assert.Contains("not state.translation_buffering", script);
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
