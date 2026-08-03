namespace SiloPlayer.Tests;

public sealed class MpvOscScriptTests
{
    [Fact]
    public void PostRollCancelsTheCreditsCountdownOwnedByTheForegroundPlayer()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("post_roll_active = false", script);
        Assert.Contains("osc-set-post-roll", script);
        Assert.Contains("state.next_ep_countdown_cancelled = true", script);
        Assert.Contains("state.post_roll_active or state.next_ep_countdown_cancelled", script);
    }

    [Fact]
    public void DetachedPlaybackHidesEveryOverlayWithoutSuspendingCreditsAutoplay()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("if state.osc_disabled then", script);
        Assert.Contains("check_next_episode_countdown()", script);
        Assert.Contains("state.osc_disabled or not state.next_ep_countdown_active", script);
        Assert.Contains("state.osc_disabled or not state.skip_visible", script);
        Assert.Contains("state.osc_disabled or not state.next_ep_visible", script);
        Assert.Contains("render_playback_wait()", script);
        Assert.Contains("render_translation_buffering()", script);
    }

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
    public void TimedSkipButtonsUseCurrentWebUiCurvedArrowGlyphs()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("local function draw_skip_arrow_icon", script);
        Assert.Contains("direction == \"back\" and -1 or 1", script);
        Assert.Contains("\"back\", \"10\"", script);
        Assert.Contains("\"forward\", \"30\"", script);
        Assert.DoesNotContain("two left-pointing triangles", script);
        Assert.DoesNotContain("two right-pointing triangles", script);
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
    public void FileLoadClearsReusableEpisodeNavigationBeforeHostPublishesNewContext()
    {
        var script = File.ReadAllText(FindOscScriptPath()).Replace("\r\n", "\n");
        var start = script.IndexOf("mp.register_event(\"file-loaded\"", StringComparison.Ordinal);
        var end = script.IndexOf("mp.register_event(\"shutdown\"", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var fileLoaded = script[start..end];
        Assert.Contains("state.series_context = false", fileLoaded);
        Assert.Contains("state.prev_ep_available = false", fileLoaded);
        Assert.Contains("state.next_ep_available = false", fileLoaded);
        Assert.Contains("state.next_ep_detail = nil", fileLoaded);
        Assert.Contains("render_next_episode_button()", fileLoaded);
    }

    [Fact]
    public void OscReenableAfterPlayingNextRestoresAUsableControlSurface()
    {
        var script = File.ReadAllText(FindOscScriptPath()).Replace("\r\n", "\n");
        var start = script.IndexOf(
            "mp.register_script_message(\"osc-set-visibility\"",
            StringComparison.Ordinal);
        var end = script.IndexOf(
            "mp.register_script_message(\"osc-set-loading\"",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var handler = script[start..end];
        Assert.Contains("state.dragging_seek = false", handler);
        Assert.Contains("state.dragging_volume = false", handler);
        Assert.Contains("state.dragging_marker_edge = nil", handler);
        Assert.Contains("state.mouse_in_bar = false", handler);
        Assert.Contains("dismiss_transport_menus_for_fade()", handler);
        Assert.Contains("show_osc()", handler);
        Assert.Contains("state.mouse_in_window = true", script);
    }

    [Fact]
    public void FourKMetadataUsesDedicatedReadableScaleWithoutEnlargingEveryMenu()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("local function metadata_scale()", script);
        Assert.Contains("math.min(width_scale, height_scale)", script);
        Assert.Contains("if surface_scale > 1.75 then return 1.75 end", script);
        Assert.Contains("font_size_title     = 17", script);
        Assert.Contains("font_size_subtitle  = 12", script);
        Assert.Contains("font_size_time      = 13", script);
        Assert.Contains("local text_sc = metadata_scale()", script);
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
    public void SubtitleLanguageLabelsMatchTheCurrentWebUiCatalogAndRegionalCodes()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("local base = normalized:match(\"^([^%-_]+)\") or normalized", script);
        Assert.Contains("sk = \"Slovak\", slo = \"Slovak\", slk = \"Slovak\"", script);
        Assert.Contains("sl = \"Slovenian\", slv = \"Slovenian\"", script);
        Assert.Contains("ms = \"Malay\", may = \"Malay\", msa = \"Malay\"", script);
        Assert.Contains("ta = \"Tamil\", tam = \"Tamil\", te = \"Telugu\", tel = \"Telugu\"", script);
        Assert.Contains("bn = \"Bengali\", ben = \"Bengali\", fa = \"Persian\"", script);
    }

    [Fact]
    public void OscAccentTracksTheActiveApplicationTheme()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("osc-set-theme", script);
        Assert.Contains("WinUI sends RRGGBB; ASS colors are BGR.", script);
        Assert.Contains("config.accent_color = value:sub(5, 6)", script);
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
        Assert.Contains("silo-menu-home", script);
        Assert.Contains("silo-menu-end", script);
        Assert.Contains("silo-menu-activate", script);
        Assert.Contains("silo-menu-escape", script);
        Assert.Contains("silo-escape-unhandled", script);
        Assert.Contains("mp.add_forced_key_binding(\"ESC\"", script);
        Assert.Contains("move_keyboard_menu_focus", script);
        Assert.Contains("silo-control-focus-next", script);
        Assert.Contains("osc-controller-nav", script);
        Assert.Contains("osc-controller-activate", script);
        Assert.Contains("activate_controller_focus", script);
        Assert.Contains("state.controller_focus_name", script);
        Assert.Contains("state.keyboard_menu_index == keyboard_row", script);
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
        Assert.Contains("local menu_bottom = (L.seek_bar and L.seek_bar.y", script);
        Assert.Contains("local menu_y = menu_bottom - menu_h", script);
        Assert.Contains("math.floor((menu_bottom - math.floor(10 * sc) - fixed_height) / track_item_h)", script);
        Assert.Contains("8, \"000000\", \"1A\", 1.0)", script);
        Assert.DoesNotContain("local menu_y = (L.btn_cc.y - menu_h)", script);
    }

    [Fact]
    public void TransportMenusUseReadableWebUiSurfacesAndStayAboveTheSeekRail()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.True(
            System.Text.RegularExpressions.Regex.Matches(
                script,
                "8, \\\"000000\\\", \\\"1A\\\", 1\\.0\\)").Count >= 2);
        Assert.Contains(
            "local menu_bottom = (state.layout.seek_bar and state.layout.seek_bar.y",
            script);
        Assert.Contains("state.layout.btn_audio.y) - math.floor(8 * sc)", script);
        Assert.Contains("state.layout.btn_chapters.y) - math.floor(8 * sc)", script);
        Assert.True(
            System.Text.RegularExpressions.Regex.Matches(
                script,
                "local menu_y = math\\.max\\(10, menu_bottom - menu_h\\)").Count >= 2);
        Assert.DoesNotContain("anchor.y - menu_h - math.floor(8 * sc)", script);
    }

    [Fact]
    public void MarkerEditorIsAvailableInActiveNativeOsc()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("osc-set-marker-edit-available", script);
        Assert.Contains("L.btn_marker_edit", script);
        Assert.Contains("draw_marker_tags_icon", script);
        Assert.Contains("open_marker_editor", script);
        Assert.Contains("render_marker_editor_panel", script);
        Assert.Contains("Drag the timeline handles, or set points to the playhead.", script);
        Assert.Contains("state.marker_editor_handle_rects", script);
        Assert.Contains("state.dragging_marker_edge", script);
        Assert.Contains("state.dragging_marker_panel", script);
        Assert.Contains("update_marker_panel_drag", script);
        Assert.Contains("Embedded libmpv sends pointer motion through this script message", script);
        Assert.Contains("silo-marker-save", script);
        Assert.Contains("Reset all", script);
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
        Assert.Contains("Color range", script);
        Assert.Contains("Auto-switched from", script);
        Assert.Contains("format_mbps_from_kbps(mi.bitrate)", script);
        Assert.Contains("format_mbps_from_kbps(mi.video_bitrate)", script);
        Assert.Contains("format_kbps(mi.audio_bitrate)", script);
        Assert.Contains("format_sample_rate(mi.audio_sample_rate)", script);
        Assert.DoesNotContain("format_bitrate(mi.video_bitrate)", script);
    }

    [Fact]
    public void PlayerHud_MatchesCurrentWebUiThreeColumnCinemaLayout()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("hud_height          = 144", script);
        Assert.Contains("hide_timeout        = 3.0", script);
        Assert.Contains("fade_duration       = 0.30", script);
        Assert.Contains("x = W / 2 - main_size / 2", script);
        Assert.Contains("L.metadata =", script);
        Assert.Contains("Seek rail spans the frame", script);
        Assert.Contains("Top-left chrome matches VideoPlayer.tsx", script);
        Assert.Contains("Exact player-hud CSS stops", script);
        Assert.Contains("local hud_mid_y = L.gradient.y + L.gradient.h * 0.55", script);
        Assert.Contains("draw_secondary_disc", script);
        Assert.Contains("hovered and \"C7\" or \"E6\"", script);
        Assert.Contains("hovered and \"DB\" or \"EF\"", script);
        Assert.Contains("local breathe_phase = (mp.get_time() % 2.6) / 2.6", script);
        Assert.Contains("18 * sc * breathe_progress", script);
        Assert.Contains("state.next_ep_available", script);
        Assert.Contains("osc-set-audio-tracks", script);
        Assert.Contains("silo-audio-select", script);
        Assert.Contains("osc-set-chapters", script);
        Assert.Contains("render_chapter_menu", script);
        Assert.DoesNotContain("-- 2. Bar background", script);
    }

    [Fact]
    public void PlayerHud_UsesCurrentWebUiIconsScrimsAndResponsiveVolume()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("local top_mid = math.floor(H * 0.14)", script);
        Assert.Contains("local top_gh = math.floor(H * 0.28)", script);
        Assert.Contains("draw_minimize_chevron_icon", script);
        Assert.Contains("draw_captions_icon", script);
        Assert.Contains("draw_audio_lines_icon", script);
        Assert.Contains("draw_chapters_icon", script);
        Assert.Contains("draw_settings_icon", script);
        Assert.Contains("draw_info_icon", script);
        Assert.Contains("draw_circle_outline", script);
        Assert.DoesNotContain("\"\u2699\"", script);
        Assert.DoesNotContain("\"\u24D8\"", script);
        Assert.Contains("local compact = W < math.floor(640 * sc)", script);
        Assert.Contains("local show_volume_group = not compact", script);
        Assert.Contains("compact and 48 or config.button_size", script);
        Assert.Contains("compact and 40 or config.small_button_size", script);
        Assert.Contains("local divider_center_y = L.btn_play.cy", script);
        Assert.Contains("truncate_display_text", script);
        Assert.DoesNotContain("controls_y - divider_half_h", script);
    }

    [Fact]
    public void VolumeRailMatchesCurrentWebUiPersistentSliderAndDivider()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("volume_bar_width    = 96", script);
        Assert.Contains("volume_bar_height   = 3", script);
        Assert.Contains("local volume_hover = state.dragging_volume", script);
        Assert.Contains("if volume_hover then", script);
        Assert.Contains("L.utility_divider_x", script);
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
    public void SkipMarkerPillMatchesCurrentWebUiIntroAndRecapActions()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("if pos < 0 then return end", script);
        Assert.Contains("state.skip_label = \"Skip Intro\"", script);
        Assert.Contains("state.skip_label = \"Skip Recap\"", script);
        Assert.Contains("state.skip_target = state.recap_end", script);
        Assert.Contains("state.skip_label ~= previous_label", script);
        Assert.Contains("state.skip_target ~= previous_target", script);
        Assert.Contains("math.floor(24 * sc)", script);
        Assert.Contains("local btn_h = math.floor(36 * sc)", script);
        Assert.Contains("border-white/40 over bg-black/70", script);
        Assert.Contains("state.skip_hovered and \"CC\" or \"4D\"", script);
    }

    [Fact]
    public void AutoSkipMarkersUseTransportAwareSeekAndResetPerMediaLoad()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("osc-set-auto-skip", script);
        Assert.Contains("state.auto_skip_intro and not state.intro_auto_skipped", script);
        Assert.Contains("state.auto_skip_recap and not state.recap_auto_skipped", script);
        Assert.Contains("state.auto_skip_credits and not state.credits_auto_skipped", script);
        Assert.Contains("state.watch_party == nil or state.watch_party.is_host == true", script);
        Assert.Contains("seek_and_resume(state.intro_end", script);
        Assert.Contains("state.intro_auto_skipped = false", script);
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
    public void ChapterMenuRepaintsWhenAnAsyncThumbnailArrives()
    {
        var script = File.ReadAllText(FindOscScriptPath());
        var handlerStart = script.IndexOf(
            "mp.register_script_message(\"osc-set-chapter-thumbnail\"",
            StringComparison.Ordinal);
        var handlerEnd = script.IndexOf(
            "mp.register_script_message(\"osc-clear-chapter-thumbnail\"",
            handlerStart,
            StringComparison.Ordinal);

        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = script[handlerStart..handlerEnd];
        Assert.Contains("if state.chapter_menu_visible then", handler, StringComparison.Ordinal);
        Assert.Contains("render_chapter_menu()", handler, StringComparison.Ordinal);
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
        Assert.Contains("DOWNLOADED", script);
        Assert.Contains("string.format(\"%d Hz\"", script);
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

    [Fact]
    public void NativePopupShowsCurrentWebUiLoadingAndRebufferFeedback()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("render_playback_wait", script);
        Assert.Contains("osc-set-loading", script);
        Assert.Contains("osc-set-buffering", script);
        Assert.Contains("mp.add_timeout(0.5", script);
        Assert.Contains("state.playback_loading and 13 or 17", script);
        Assert.Contains("state.playback_wait_overlay.z = 90", script);
    }

    [Fact]
    public void PlaybackNoticeMatchesCurrentWebUiTintedWrappedCard()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("local function wrap_display_text", script);
        Assert.Contains("local box_y = math.floor(80 * sc)", script);
        Assert.Contains("math.floor(576 * sc)", script);
        Assert.Contains("amber-500/15 + amber-400/50", script);
        Assert.Contains("sky-500/15 + sky-400/50", script);
        Assert.Contains("local radius = math.max(2, math.floor(16 * sc))", script);
        Assert.Contains("wrap_display_text(state.notice_message, max_chars, 3)", script);
    }

    [Fact]
    public void ActivePlayerSurfacesReflowWhenTheHostWindowChangesSize()
    {
        var script = File.ReadAllText(FindOscScriptPath()).Replace("\r\n", "\n");
        var observerStart = script.IndexOf(
            "mp.observe_property(\"osd-dimensions\", \"native\", function(_, val)",
            StringComparison.Ordinal);
        Assert.True(observerStart >= 0);

        var observerEnd = script.IndexOf("    end)\nend", observerStart, StringComparison.Ordinal);
        Assert.True(observerEnd > observerStart);
        var observer = script[observerStart..observerEnd];

        Assert.Contains("render_stats()", observer);
        Assert.Contains("render_subtitle_menu()", observer);
        Assert.Contains("render_quality_menu()", observer);
        Assert.Contains("render_audio_menu()", observer);
        Assert.Contains("render_chapter_menu()", observer);
        Assert.Contains("render_notice()", observer);
        Assert.Contains("render_skip_button()", observer);
        Assert.Contains("render_next_episode_button()", observer);
        Assert.Contains("render_next_episode_countdown()", observer);
        Assert.Contains("render_translation_buffering()", observer);
        Assert.Contains("render_playback_wait()", observer);
        Assert.Contains("request_tick()", observer);
    }

    [Fact]
    public void HostWindowModeChangesImmediatelyRefreshTheirControlIcons()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("if state.fullscreen ~= fullscreen then", script);
        Assert.Contains("if state.picture_in_picture ~= picture_in_picture then", script);
        Assert.Matches(
            "(?s)osc-fullscreen-state.*?state\\.fullscreen = fullscreen.*?request_tick\\(\\)",
            script);
        Assert.Matches(
            "(?s)osc-pip-state.*?state\\.picture_in_picture = picture_in_picture.*?request_tick\\(\\)",
            script);
    }

    [Fact]
    public void PlaybackInfoUsesTheWebUiBoundedScrollablePanelBehavior()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("H - math.floor(96 * sc)", script);
        Assert.Contains("state.stats_scroll_max = math.max(0, total_h - box_h)", script);
        Assert.Contains("state.stats_panel_rect = { x = box_x, y = box_y, w = box_w, h = box_h }", script);
        Assert.Contains("point_in_rect(state.mouse_x, state.mouse_y, state.stats_panel_rect)", script);
        Assert.Contains("state.stats_scroll_offset / state.stats_scroll_max", script);
        Assert.Contains("point_in_rect(mx, my, state.stats_panel_rect)", script);
    }

    [Fact]
    public void TransportMenusSwitchAndToggleInOneClickWithoutLeakingToVideo()
    {
        var script = File.ReadAllText(FindOscScriptPath()).Replace("\r\n", "\n");

        Assert.Contains("local function toggle_transport_menu_from_button(mx, my)", script);
        Assert.Contains("set_keyboard_transport_menu(\"subtitles\", opening)", script);
        Assert.Contains("set_keyboard_transport_menu(\"quality\", opening)", script);
        Assert.Contains("set_keyboard_transport_menu(\"audio\", opening)", script);
        Assert.Contains("set_keyboard_transport_menu(\"chapters\", opening)", script);

        var handlerStart = script.IndexOf("local function handle_mouse_down()", StringComparison.Ordinal);
        var handlerEnd = script.IndexOf("local function handle_mouse_down_right()", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = script[handlerStart..handlerEnd];
        Assert.True(
            handler.Split("toggle_transport_menu_from_button(mx, my)", StringSplitOptions.None).Length >= 5,
            "Open menu outside-click branches should route transport-button clicks through the one-click switcher.");

        var videoClickStart = script.IndexOf("mp.register_script_message(\"osc-video-click\"", StringComparison.Ordinal);
        var videoClickEnd = script.IndexOf("-- Initialization", videoClickStart, StringComparison.Ordinal);
        Assert.True(videoClickStart >= 0 && videoClickEnd > videoClickStart);
        var videoClick = script[videoClickStart..videoClickEnd];
        Assert.Contains("state.subtitle_menu_visible or state.quality_menu_visible", videoClick);
        Assert.Contains("or state.audio_menu_visible or state.chapter_menu_visible", videoClick);
    }

    [Fact]
    public void SubtitleKeyboardNavigationCanOpenFixedSubtitleActions()
    {
        var script = File.ReadAllText(FindOscScriptPath()).Replace("\r\n", "\n");

        var itemsStart = script.IndexOf("local function keyboard_menu_items(kind, source)", StringComparison.Ordinal);
        var itemsEnd = script.IndexOf("local function render_keyboard_menu(kind)", itemsStart, StringComparison.Ordinal);
        Assert.True(itemsStart >= 0 && itemsEnd > itemsStart);
        var items = script[itemsStart..itemsEnd];
        Assert.Contains("item.action == \"search\"", items);
        Assert.Contains("item.action == \"appearance\"", items);
        Assert.Contains("item.action == \"ai\"", items);
        Assert.Contains("item.action == \"delay\"", items);
        Assert.Contains("item.action == \"delay_reset\"", items);

        var activateStart = script.IndexOf("local function activate_keyboard_menu_item()", StringComparison.Ordinal);
        var activateEnd = script.IndexOf("local function close_keyboard_surface()", activateStart, StringComparison.Ordinal);
        Assert.True(activateStart >= 0 && activateEnd > activateStart);
        var activate = script[activateStart..activateEnd];
        Assert.Contains("silo-subtitle-search", activate);
        Assert.Contains("silo-subtitle-appearance", activate);
        Assert.Contains("silo-subtitle-ai", activate);
        Assert.Contains("mp.set_property_number(\"sub-delay\"", activate);
        Assert.Contains("state.subtitle_menu_visible = false", activate);
    }

    [Fact]
    public void SubtitleDialogsRestoreControllerFocusToTheirTrigger()
    {
        var script = File.ReadAllText(FindOscScriptPath());

        Assert.Contains("restore_subtitle_focus_after_dialog", script);
        Assert.Contains(
            "state.restore_subtitle_focus_after_dialog = state.controller_focus_name == \"btn_cc\"",
            script);
        Assert.Contains("osc-subtitle-dialog-closed", script);
        Assert.Contains("state.controller_focus_name = \"btn_cc\"", script);
        Assert.Contains("show_osc()", script);
    }

    [Fact]
    public void ControllerNavigationTraversesBoundedMenusAndStartsOnTheFirstItem()
    {
        var script = File.ReadAllText(FindOscScriptPath()).Replace("\r\n", "\n");
        var moveStart = script.IndexOf("local function move_keyboard_menu_focus(direction)", StringComparison.Ordinal);
        var moveEnd = script.IndexOf("local function activate_keyboard_menu_item()", moveStart, StringComparison.Ordinal);
        Assert.True(moveStart >= 0 && moveEnd > moveStart);
        var move = script[moveStart..moveEnd];

        Assert.Contains("state.keyboard_menu_index < 1 and 1", move);
        Assert.Contains("state.subtitle_menu_offset = state.subtitle_menu_offset + 1", move);
        Assert.Contains("state.audio_menu_offset = state.audio_menu_offset + 1", move);
        Assert.Contains("state.chapter_menu_offset = state.chapter_menu_offset + 1", move);
        Assert.Contains("state.audio_menu_offset = #state.audio_tracks", move);
        Assert.Contains("state.chapter_menu_offset = #state.chapters", move);
    }

    [Fact]
    public void EscapeClosesOscSurfacesBeforeReturningControlToTheNativeWindow()
    {
        var script = File.ReadAllText(FindOscScriptPath()).Replace("\r\n", "\n");

        var closeStart = script.IndexOf("local function close_keyboard_surface()", StringComparison.Ordinal);
        var closeEnd = script.IndexOf("local function set_keyboard_transport_menu", closeStart, StringComparison.Ordinal);
        Assert.True(closeStart >= 0 && closeEnd > closeStart);
        var close = script[closeStart..closeEnd];
        Assert.Contains("close_transport_menus(nil)", close);
        Assert.Contains("close_marker_editor()", close);
        Assert.Contains("state.stats_visible = false", close);

        var escapeStart = script.IndexOf(
            "mp.add_forced_key_binding(\"ESC\", \"silo-menu-escape\"",
            StringComparison.Ordinal);
        Assert.True(escapeStart >= 0);
        var escape = script[escapeStart..Math.Min(script.Length, escapeStart + 320)];
        Assert.Contains("if not close_keyboard_surface() then", escape);
        Assert.Contains("silo-escape-unhandled", escape);
    }

    [Fact]
    public void TransportMenusCloseWhenTheOscFades()
    {
        var script = File.ReadAllText(FindOscScriptPath());
        var helperStart = script.IndexOf(
            "local function dismiss_transport_menus_for_fade()",
            StringComparison.Ordinal);
        var hideStart = script.IndexOf("hide_osc = function()", helperStart, StringComparison.Ordinal);
        var hideEnd = script.IndexOf("local last_cursor_visible", hideStart, StringComparison.Ordinal);
        Assert.True(helperStart >= 0 && hideStart > helperStart && hideEnd > hideStart);

        var helper = script[helperStart..hideStart];
        Assert.Contains("state.subtitle_menu_visible = false", helper, StringComparison.Ordinal);
        Assert.Contains("state.quality_menu_visible = false", helper, StringComparison.Ordinal);
        Assert.Contains("state.audio_menu_visible = false", helper, StringComparison.Ordinal);
        Assert.Contains("state.chapter_menu_visible = false", helper, StringComparison.Ordinal);
        Assert.Contains("state.keyboard_menu_kind = nil", helper, StringComparison.Ordinal);

        var hide = script[hideStart..hideEnd];
        Assert.Contains("dismiss_transport_menus_for_fade()", hide, StringComparison.Ordinal);
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
