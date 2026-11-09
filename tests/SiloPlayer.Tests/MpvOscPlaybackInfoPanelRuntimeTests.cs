namespace SiloPlayer.Tests;

// Playback Info is the OSC's own panel in the top-left corner. It must not
// move into a native menu anchored to the bottom-right utility rail, and it
// must show every row at common window sizes without wheel scrolling.
public sealed partial class MpvOscRuntimeTests
{
    private const string PlaybackInfoScope = "11111111111111111111111111111111";

    // Records every native menu request; publishes every native capability the
    // host could offer (including a stats scope, if a script still accepts one).
    private static readonly string PlaybackInfoProbe = NativeAudioProbe.Replace(
        "args[2] == 'silo-native-audio-open'", "args[2]:match('^silo%-native%-.+%-open$')") + "\n" + """
        function playback_info_publish_scopes()
            for _, kind in ipairs({'audio','subtitle','quality','chapters','speed','stats','more','party'}) do
                local handler = native_handlers['osc-native-' .. kind .. '-scope']
                if handler then handler('SCOPE') end
            end
        end
        function playback_info_native_stats_requests()
            local count = 0
            for _, request in ipairs(native_requests) do
                if request[2] == 'silo-native-stats-open' then count = count + 1 end
            end
            return count
        end
        """.Replace("SCOPE", PlaybackInfoScope);

    // Fail-before: on beta 87a21f38 every route with a published stats scope
    // opened the bottom-right native panel instead (stats_visible false, one
    // silo-native-stats-open request), so each case returned "false:1:...".
    [Theory]
    [InlineData("key")]
    [InlineData("host")]
    [InlineData("pointer")]
    [InlineData("controller")]
    [InlineData("more")]
    public void PlaybackInfoOpensTheTopLeftOscPanelFromEveryRoute(string route)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading, s.post_roll_active = false, false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            playback_info_publish_scopes()
            osc_test_layout()
            local route = '{{route}}'
            if route == 'key' then native_keys['silo-osc-toggle-stats']()
            elseif route == 'host' then native_handlers['osc-toggle-stats']()
            elseif route == 'pointer' then
                local r = assert(s.layout.btn_stats, 'no Playback info button')
                native_handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            elseif route == 'controller' then
                s.controller_focus_name = 'btn_stats'; osc_test_activate()
            else
                s.controller_focus_name = 'btn_more'; osc_test_activate()
                local more = assert(native_requests[1], 'More did not open')
                native_handlers['osc-native-menu-closed'](more[3], more[4], 'true')
                native_handlers['osc-native-more-action'](more[3], more[4], 'stats')
            end
            local r = s.stats_panel_rect or {}
            mp.set_property('user-data/playback-info-route', tostring(s.stats_visible) .. ':'
                .. tostring(playback_info_native_stats_requests()) .. ':' .. tostring(s.native_menu_request == nil) .. ':'
                .. tostring(r.x) .. ':' .. tostring(r.y))
            """, rawAss: true, resultProperty: "user-data/playback-info-route", beforeScript: PlaybackInfoProbe,
            windowWidth: route == "more" ? 460 : 1920, windowHeight: route == "more" ? 700 : 1080);
        Assert.Equal("true:0:true:16:48", output);
    }

    // Fail-before: on beta 87a21f38 the panel was always one 380px column
    // bounded at H - 96, so 1600x900, 1366x768, 1280x720 and both 1024-wide
    // cases needed wheel scrolling (stats_scroll_max 100-280) and the last
    // source-file rows were not drawn. The 1920-wide cases already fit and
    // guard that the single column stays unchanged there.
    [Theory]
    [InlineData(1920, 1080, false)]
    [InlineData(1920, 1080, true)]
    [InlineData(1920, 1009, true)]
    [InlineData(1600, 900, true)]
    [InlineData(1366, 768, true)]
    [InlineData(1280, 720, true)]
    [InlineData(1024, 768, true)]
    [InlineData(1024, 600, false)]
    public void PlaybackInfoShowsEveryRowWithoutScrollingAtCommonWindowSizes(int width, int height, bool optionalRows)
    {
        var output = CaptureStats(1920, 1080, 3840, 2160, true, neural: optionalRows, setup: $$"""
            local s = osc_test_state
            s.osc_disabled = false
            s.play_method_str, s.protocol_str, s.stream_type_str = 'Direct Play', 'HTTP', 'Progressive'
            s.stream_codec_video, s.stream_codec_audio = 'HEVC Main 10', 'TrueHD Atmos 7.1'
            s.media_info = {
                container = 'mkv', file_size = 64 * 1024 * 1024 * 1024, bitrate = 72000,
                codec_video = 'hevc', video_profile = 'Main 10', video_bitrate = 68000,
                video_range = 'HDR10', color_range = 'tv', audio_title = 'TrueHD Atmos 7.1',
                audio_bitrate = 4500, audio_channels = 8, audio_sample_rate = 48000,
            }
            if {{(optionalRows ? "true" : "false")}} then
                s.media_info.requested_source = '4K HDR Remux'
                s.media_info.buffering_limit = '8 Mbps until playback is steady'
                mp.set_property_native('user-data/silo-upscaling-status', 'RTX VSR active on the NVIDIA adapter')
            end
            native_handlers['osc-toggle-stats']()
            local r = assert(s.stats_panel_rect, 'Playback info did not render')
            local ass = s.stats_overlay.data
            local missing = {}
            for _, label in ipairs({'PLAYER', 'VIDEO INFO', 'PLAYBACK STREAM INFO', 'CURRENT SOURCE FILE',
                'Play method', 'Dropped frames', 'Audio bitrate (measured)', 'Audio channels', 'Audio sample rate'}) do
                if not ass:find(label, 1, true) then table.insert(missing, label) end
            end
            local fits = r.x + r.w <= {{width}} and r.y + r.h <= {{height}}
            mp.set_property('user-data/playback-info-fit', tostring(s.stats_scroll_max) .. ':' .. table.concat(missing, ',')
                .. ':' .. tostring(fits) .. ':' .. tostring(r.x) .. ':' .. tostring(r.y))
            """, rawAss: true, resultProperty: "user-data/playback-info-fit", beforeScript: PlaybackInfoProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("0::true:16:48", output);
    }

    // The restored panel keeps its original single 380px column whenever that
    // column fits, so full-screen 1080p looks exactly as it did before #92.
    [Fact]
    public void PlaybackInfoKeepsTheOriginalSingleColumnWhenItFits()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled = false
            native_handlers['osc-toggle-stats']()
            local r = assert(s.stats_panel_rect, 'Playback info did not render')
            mp.set_property('user-data/playback-info-column', tostring(r.w) .. ':' .. tostring(s.stats_scroll_max))
            """, rawAss: true, resultProperty: "user-data/playback-info-column", beforeScript: PlaybackInfoProbe,
            windowWidth: 1920, windowHeight: 1080);
        Assert.Equal("380:0", output);
    }

    // Only windows too small for two columns fall back to the bounded,
    // wheel-scrolled viewport; the panel never draws past the window.
    [Fact]
    public void PlaybackInfoScrollsOnlyWhenTwoColumnsCannotFit()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled = false
            native_handlers['osc-toggle-stats']()
            local r = assert(s.stats_panel_rect, 'Playback info did not render')
            mp.set_property('user-data/playback-info-small', tostring(r.w <= 380) .. ':' .. tostring(s.stats_scroll_max > 0)
                .. ':' .. tostring(r.y + r.h <= 500))
            """, rawAss: true, resultProperty: "user-data/playback-info-small", beforeScript: PlaybackInfoProbe,
            windowWidth: 700, windowHeight: 500);
        Assert.Equal("true:true:true", output);
    }

    // An overflowing panel scrolls from the keyboard and the controller, not
    // only the wheel, and those keys do not also change the volume.
    [Theory]
    [InlineData("key")]
    [InlineData("controller")]
    public void PlaybackInfoScrollsFromKeyboardAndControllerWhenItOverflows(string route)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled = false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            native_handlers['osc-toggle-stats']()
            local max = s.stats_scroll_max or 0
            local volume = mp.get_property('volume')
            local key = '{{route}}' == 'key'
            local function step(direction)
                if key then native_keys[direction == 'up' and 'silo-vol-up' or 'silo-vol-down']()
                else native_handlers['osc-controller-nav'](direction) end
            end
            step('down')
            local moved = (s.stats_scroll_offset or 0) > 0
            if key then native_keys['silo-menu-end']() else for _ = 1, 200 do step('down') end end
            local bottom = s.stats_scroll_offset == max
            if key then native_keys['silo-menu-home']() else for _ = 1, 200 do step('up') end end
            local top = s.stats_scroll_offset == 0
            mp.set_property('user-data/playback-info-keys', tostring(max > 0) .. ':' .. tostring(moved) .. ':'
                .. tostring(bottom) .. ':' .. tostring(top) .. ':' .. tostring(mp.get_property('volume') == volume))
            """, rawAss: true, resultProperty: "user-data/playback-info-keys", beforeScript: PlaybackInfoProbe,
            windowWidth: 700, windowHeight: 500);
        Assert.Equal("true:true:true:true:true", output);
    }

    // The two-column panel (z=60) can cover the top-right Watch Party opener
    // and card (z=50). Clicks on the visible panel, including its close
    // button, must never reach the hidden party targets beneath it.
    [Theory]
    [InlineData(960, 540)]
    [InlineData(900, 700)]
    public void PlaybackInfoPanelOwnsClicksOverTheWatchPartyCard(int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.watch_party = { visible=true, code='TEST1234', member_count=2, is_host=true, connection_state='connected' }
            native_handlers['osc-native-party-scope']('11111111111111111111111111111111')
            native_handlers['osc-toggle-stats']()
            osc_test_layout(); osc_test_tick()
            local panel = assert(s.stats_panel_rect, 'Playback info did not render')
            local opener = assert(s.layout.btn_party, 'no Watch Party opener')
            local close = assert(s.stats_close_rect)
            local function inside(r, x, y) return x >= r.x and x <= r.x + r.w and y >= r.y and y <= r.y + r.h end
            for _, item in ipairs(s.watch_party_actions or {}) do
                local x, y = (item.x or -1) + (item.w or 0) / 2, (item.y or -1) + (item.h or 0) / 2
                if inside(panel, x, y) then native_handlers['osc-mouse-down'](tostring(x), tostring(y)) end
            end
            local kept = s.stats_visible
            local cx, cy = close.x + close.w / 2, close.y + close.h / 2
            local covered = inside(opener, cx, cy)
            native_handlers['osc-mouse-down'](tostring(cx), tostring(cy))
            mp.set_property('user-data/playback-info-party', tostring(covered) .. ':' .. tostring(kept) .. ':'
                .. tostring(#party_requests) .. ':' .. tostring(#party_actions) .. ':' .. tostring(s.stats_visible))
            """, rawAss: true, resultProperty: "user-data/playback-info-party", beforeScript: PartyProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("true:true:0:0:false", output);
    }

    // At the scroll edge the arrow keys fall through to their normal volume
    // command instead of being swallowed by a panel that cannot move further.
    [Fact]
    public void PlaybackInfoReleasesVolumeKeysAtTheScrollEdge()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled = false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            mp.set_property_number('volume', 50)
            native_handlers['osc-toggle-stats']()
            local max = s.stats_scroll_max or 0
            native_keys['silo-vol-up']()
            local top_up = s.stats_scroll_offset == 0 and mp.get_property_number('volume') == 55
            native_keys['silo-menu-end']()
            native_keys['silo-vol-down']()
            local bottom_down = s.stats_scroll_offset == max and mp.get_property_number('volume') == 50
            mp.set_property('user-data/playback-info-edge', tostring(max > 0) .. ':' .. tostring(top_up) .. ':'
                .. tostring(bottom_down) .. ':' .. tostring(s.stats_visible))
            """, rawAss: true, resultProperty: "user-data/playback-info-edge", beforeScript: PlaybackInfoProbe,
            windowWidth: 960, windowHeight: 540);
        Assert.Equal("true:true:true:true", output);
    }

    // The two-column panel (z=60) can also cover the credits countdown card
    // (z=57) and the Next Episode button (z=56). A click on the visible panel
    // must never advance to the next episode.
    [Theory]
    [InlineData(960, 540)]
    [InlineData(1024, 600)]
    public void PlaybackInfoPanelOwnsClicksOverTheNextEpisodeSurfaces(int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            local advanced = 0
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-next-episode' then advanced = advanced + 1 end
                return command(...)
            end
            native_handlers['osc-toggle-stats']()
            local panel = assert(s.stats_panel_rect, 'Playback info did not render')
            local W, H = s.osd_width, s.osd_height
            -- render_next_episode_countdown geometry; ui_scale() is 1 at these sizes.
            local card_x, card_y = W - 24 - 360, H - 96 - 126
            local buttons_y = card_y + 126 - 16 - 32
            local cancel_x = card_x + 360 - 16 - 78
            s.next_ep_available, s.next_ep_visible = true, true
            s.next_ep_detail = { season_number = 1, episode_number = 2, title = 'Next' }
            s.next_ep_countdown_active, s.next_ep_countdown_remaining = true, 8
            s.next_ep_countdown_actions = {
                { action = 'play', x = card_x + 16, y = buttons_y, w = cancel_x - 8 - card_x - 16, h = 32 },
                { action = 'cancel', x = cancel_x, y = buttons_y, w = 78, h = 32 },
            }
            local function overlap(a, b)
                local x1, y1 = math.max(a.x, b.x), math.max(a.y, b.y)
                local x2, y2 = math.min(a.x + a.w, b.x + b.w), math.min(a.y + a.h, b.y + b.h)
                if x2 <= x1 or y2 <= y1 then return nil end
                return (x1 + x2) / 2, (y1 + y2) / 2
            end
            local px, py = overlap(panel, s.next_ep_countdown_actions[1])
            if px then native_handlers['osc-mouse-down'](tostring(px), tostring(py)) end
            local button = { x = W - 180 - 40, y = H - 96 - 44, w = 180, h = 44 }
            local bx, by = overlap(panel, button)
            if bx then native_handlers['osc-mouse-down'](tostring(bx), tostring(by)) end
            mp.set_property('user-data/playback-info-next', tostring(px ~= nil) .. ':' .. tostring(advanced) .. ':'
                .. tostring(s.next_ep_available) .. ':' .. tostring(s.stats_visible))
            """, rawAss: true, resultProperty: "user-data/playback-info-next", beforeScript: PlaybackInfoProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("true:0:true:true", output);
    }

    // The buffering-quality card (z=85) is drawn over the two-column panel
    // (z=60). Its visible Lower quality button keeps its own click.
    [Theory]
    [InlineData(960, 540)]
    [InlineData(1024, 600)]
    public void PlaybackInfoPanelLeavesClicksToTheBufferingQualityCardAboveIt(int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            local lowered = 0
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-buffering-lower' then lowered = lowered + 1 end
                return command(...)
            end
            native_handlers['osc-toggle-stats']()
            local panel = assert(s.stats_panel_rect, 'Playback info did not render')
            local W, H = s.osd_width, s.osd_height
            -- render_buffering_quality geometry; ui_scale() is 1 at these sizes.
            local card_w = math.min(440, W - 24)
            local card_x, card_y = W - card_w - 12, H - 106 - 105
            local button_w = math.floor((card_w - 44) / 2)
            s.buffering_quality_pill = 'prompt'
            s.buffering_quality_actions = {
                { action = 'lower', x = card_x + 16, y = card_y + 54, w = button_w, h = 36 },
                { action = 'keep', x = card_x + 28 + button_w, y = card_y + 54, w = button_w, h = 36 },
            }
            local lower = s.buffering_quality_actions[1]
            local x, y = lower.x + lower.w / 2, lower.y + lower.h / 2
            local covered = x >= panel.x and x <= panel.x + panel.w and y >= panel.y and y <= panel.y + panel.h
            native_handlers['osc-mouse-down'](tostring(x), tostring(y))
            mp.set_property('user-data/playback-info-buffering', tostring(covered) .. ':' .. tostring(lowered) .. ':'
                .. tostring(s.stats_visible))
            """, rawAss: true, resultProperty: "user-data/playback-info-buffering", beforeScript: PlaybackInfoProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("true:1:true", output);
    }

    // The panel's top edge overlaps the Exit pill and a tall single column
    // reaches the seek band. While the HUD takes input, those controls keep
    // their gestures; the rest of the panel still owns its clicks.
    [Fact]
    public void PlaybackInfoPanelLeavesWindowChromeAndSeekBandToTheHud()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            native_handlers['osc-toggle-stats']()
            local panel = assert(s.stats_panel_rect, 'Playback info did not render')
            osc_test_layout()
            local exit, sb = assert(s.layout.btn_exit), assert(s.layout.seek_bar)
            local function inside(r, x, y) return x >= r.x and x <= r.x + r.w and y >= r.y and y <= r.y + r.h end
            local ex, ey = exit.cx, (panel.y + exit.y + exit.h) / 2
            local sx, sy = panel.x + panel.w / 2, sb.y + sb.h / 2
            local covered = inside(panel, ex, ey) and inside(exit, ex, ey) and inside(panel, sx, sy)
            native_handlers['osc-mouse-down'](tostring(ex), tostring(ey))
            local exit_pressed = s.pressed_control == 'btn_exit'
            native_handlers['osc-mouse-down'](tostring(panel.x + panel.w / 2), tostring(panel.y + panel.h / 2))
            local panel_owned = not s.pressed_control and not s.dragging_seek and s.stats_visible
            native_handlers['osc-mouse-down'](tostring(sx), tostring(sy))
            mp.set_property('user-data/playback-info-chrome', tostring(covered) .. ':' .. tostring(exit_pressed) .. ':'
                .. tostring(panel_owned) .. ':' .. tostring(s.dragging_seek == true))
            """, rawAss: true, resultProperty: "user-data/playback-info-chrome", beforeScript: PlaybackInfoProbe,
            windowWidth: 1600, windowHeight: 900);
        Assert.Equal("true:true:true:true", output);
    }
}
