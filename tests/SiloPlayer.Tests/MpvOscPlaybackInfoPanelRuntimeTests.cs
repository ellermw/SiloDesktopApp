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
}
