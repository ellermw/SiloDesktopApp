using System.Globalization;

namespace SiloPlayer.Tests;

// Production silo-osc.lua executed in the bundled libmpv. Reference: Silo
// Server 0b579aa6 (#3917 still-watching gate, #3993 desktop HUD).
public sealed partial class MpvOscRuntimeTests
{
    private const string AutoplayHudProbe = """
        handlers, sent, natives = {}, {}, {}
        local register = mp.register_script_message
        mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
        local command = mp.commandv
        mp.commandv = function(...)
            local args = {...}
            if args[1] == 'script-message' and tostring(args[2]):sub(1, 5) == 'silo-' then
                table.insert(sent, table.concat(args, '|', 2))
                return
            end
            return command(...)
        end
        local command_native = mp.command_native
        mp.command_native = function(cmd)
            if type(cmd) == 'table' and (cmd.name == 'overlay-add' or cmd.name == 'overlay-remove') then
                table.insert(natives, cmd)
                return true
            end
            return command_native(cmd)
        end
        speed, now, dims = 1, 1800000000, nil
        local number = mp.get_property_number
        mp.get_property_number = function(name, fallback)
            if name == 'speed' then return speed end
            return number(name, fallback)
        end
        local native = mp.get_property_native
        mp.get_property_native = function(name, fallback)
            if name == 'osd-dimensions' and dims then return dims end
            return native(name, fallback)
        end
        local real_time = os.time
        os.time = function(t) if t then return real_time(t) end return now end
        function hud_event(data, needle)
            for line in (data or ''):gmatch('[^\n]+') do
                local text = line:gsub('{[^}]*}', '')
                if text:find(needle, 1, true) then
                    local x, y = line:match('\\pos%(([%-%d%.]+),([%-%d%.]+)%)')
                    return tonumber(x), tonumber(y), tonumber(line:match('\\fs(%d+)')), text
                end
            end
        end
        function sent_matching(prefix)
            local result = {}
            for _, value in ipairs(sent) do
                if value:sub(1, #prefix) == prefix then table.insert(result, value) end
            end
            return result
        end
        """;

    [Fact]
    public void CreditsCountdownWaitsForTheViewerWhenGatedAndNamesItsTrigger()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled, s.current_alpha = false, 1
            osc_test_layout()
            s.duration, s.credits_start, s.credits_end = 100, 90, 99
            s.next_ep_detail = {season_number=1, episode_number=2, title='Next'}
            s.next_ep_available, s.time_pos = true, 91
            handlers['osc-set-autoplay-gate']('true')
            osc_test_check_countdown()
            assert(not s.next_ep_countdown_active, 'A gated credits region started the unattended countdown')
            handlers['osc-set-autoplay-gate']('false')
            osc_test_check_countdown()
            assert(s.next_ep_countdown_active, 'An ungated credits region did not start the countdown')
            handlers['osc-set-autoplay-gate']('true')
            assert(not s.next_ep_countdown_active, 'Becoming gated left the countdown running')
            assert(#sent_matching('silo-next-episode') == 0, 'Retiring the countdown advanced')
            handlers['osc-set-autoplay-gate']('false')
            s.next_ep_countdown_cancelled = false
            osc_test_check_countdown()
            s.next_ep_countdown_started_at = mp.get_time() - 11
            osc_test_check_countdown()
            local automatic = sent_matching('silo-next-episode')
            assert(#automatic == 1 and automatic[1] == 'silo-next-episode|automatic', 'Expiry was not an automatic start: ' .. table.concat(automatic, ','))
            sent = {}
            s.next_ep_available, s.next_ep_countdown_active = true, false
            osc_test_check_countdown()
            local play = s.next_ep_countdown_actions[1]
            s.mouse_x, s.mouse_y = play.x + play.w / 2, play.y + play.h / 2
            osc_test_mouse_down()
            local viewer = sent_matching('silo-next-episode')
            assert(#viewer == 1 and viewer[1] == 'silo-next-episode|viewer', 'Play Now was not a viewer start')
            sent = {}
            s.next_ep_available, s.time_pos, s.next_ep_countdown_cancelled = true, 99.5, true
            osc_test_check_next()
            osc_test_render_next()
            local pill = s.next_ep_rect
            s.mouse_x, s.mouse_y = pill.x + pill.w / 2, pill.y + pill.h / 2
            osc_test_mouse_down()
            local pill_sent = sent_matching('silo-next-episode')
            assert(#pill_sent == 1 and pill_sent[1] == 'silo-next-episode|viewer', 'Next Episode pill was not a viewer start')
            mp.set_property('user-data/autoplay-gate', 'gate, expiry, play-now and pill passed')
            """, rawAss: true, resultProperty: "user-data/autoplay-gate", beforeScript: AutoplayHudProbe);
        Assert.Equal("gate, expiry, play-now and pill passed", result);
    }

    [Fact]
    public void AutomaticMarkerSkipsAreNotViewerSeeks()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled, s.current_alpha = false, 1
            osc_test_layout()
            s.duration, s.credits_start, s.credits_end = 100, 90, 99
            s.auto_skip_credits, s.time_pos = true, 91
            osc_test_check()
            local automatic = sent_matching('silo-seek-absolute')
            assert(#automatic == 1 and automatic[1]:sub(-10) == '|automatic', 'Credits auto-skip looked like a viewer seek: ' .. table.concat(automatic, ','))
            sent = {}
            s.skip_visible, s.skip_label, s.skip_target, s.time_pos = true, 'Skip Intro', 40, 10
            osc_test_render_skip()
            local r = s.skip_rect
            s.mouse_x, s.mouse_y = r.x + r.w / 2, r.y + r.h / 2
            osc_test_mouse_down()
            local viewer = sent_matching('silo-seek-absolute')
            assert(#viewer == 1 and not viewer[1]:find('automatic', 1, true), 'Skip Intro press was marked automatic')
            mp.set_property('user-data/marker-seeks', 'automatic and viewer seeks distinguished')
            """, rawAss: true, resultProperty: "user-data/marker-seeks", beforeScript: AutoplayHudProbe);
        Assert.Equal("automatic and viewer seeks distinguished", result);
    }

    [Fact]
    public void EndTimeFollowsSpeedPausedClockScrubWatchPartyAndUnknownLength()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled, s.current_alpha, s.target_alpha = false, 1, 1
            handlers['osc-set-end-time-format']('24h', 'Ends {time}')
            s.content_title, s.content_subtitle = 'Show', 'S1:E2'
            s.duration, s.time_pos = 3600, 600
            local out = {}
            local function label()
                osc_test_render_hud()
                local data = s.osc_overlay and s.osc_overlay.data or ''
                return data:gsub('{[^}]*}', ''):match('Ends ([^\n]+)') or 'none'
            end
            speed = 1; table.insert(out, label())
            speed = 2; table.insert(out, label())
            s.watch_party, s.room_seek_position = {visible=false}, 600; table.insert(out, label())
            s.watch_party, speed, s.pause = nil, 1, true
            now = now + 125; table.insert(out, label())
            s.dragging_seek, s.seek_drag_pos = true, 0.5; table.insert(out, label())
            s.dragging_seek = false
            handlers['osc-set-end-time-format']('12h', 'Ends {time}'); table.insert(out, label())
            s.duration = 0
            osc_test_render_hud()
            local plain = s.osc_overlay.data:gsub('{[^}]*}', '')
            table.insert(out, (plain:find('Ends', 1, true) and 'shown' or 'hidden') .. ':'
                .. (plain:find('10:00 /', 1, true) and 'total' or (plain:find('10:00', 1, true) and 'elapsed' or 'none')))
            mp.set_property('user-data/end-time', table.concat(out, '|'))
            """, rawAss: true, resultProperty: "user-data/end-time", beforeScript: AutoplayHudProbe);

        const long now = 1800000000;
        static string At(long seconds, string format) =>
            DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString(format, CultureInfo.InvariantCulture);
        var expected = string.Join('|',
            At(now + 3000, "HH:mm"),
            At(now + 1500, "HH:mm"),
            At(now + 3000, "HH:mm"),
            At(now + 125 + 3000, "HH:mm"),
            At(now + 125 + 1800, "HH:mm"),
            At(now + 125 + 3000, "h:mm tt"),
            "hidden:elapsed");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void FullWidthTitleBlockCarriesTimesAndEndTimeBelowTheSeekTarget(int width, int height)
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, windowWidth: width, windowHeight: height, setup: Tokens("""
            local s = osc_test_state
            dims = {w=__W__, h=__H__}
            s.osc_disabled, s.current_alpha, s.target_alpha = false, 1, 1
            handlers['osc-set-end-time-format']('24h', 'Ends {time}')
            s.content_title, s.content_subtitle = 'Show', 'S1:E2'
            s.chapters = {
                {index=0, title='Cold open', start_seconds=0, end_seconds=300},
                {index=1, title='Opening', start_seconds=300, end_seconds=900},
                {index=2, title='Act one', start_seconds=900, end_seconds=3600},
            }
            s.duration, s.time_pos = 3600, 600
            osc_test_layout()
            osc_test_render_hud()
            local data, L = s.osc_overlay.data, s.layout
            local tx, ty, tfs = hud_event(data, 'Show')
            local mx, my, _, meta = hud_event(data, 'S1:E2')
            local ix, iy, ifs, times = hud_event(data, '10:00 / 1:00:00')
            local ex, ey = hud_event(data, 'Ends ')
            assert(ty and my and iy and ey, 'Title block lines missing')
            assert(meta:find('Opening', 1, true), 'Meta line did not carry the chapter: ' .. meta)
            assert(ty < my and my < iy, 'Title, meta and time lines are out of order')
            assert(math.abs(ey - iy) <= 1 and ex >= ix, 'End time does not follow the times on their line')
            assert(tx == ix and tx == mx, 'Title block lines are not left aligned')
            local seek_bottom = L.seek_bar.y + L.seek_bar.h
            assert(ty - tfs / 2 >= seek_bottom, 'Title overlaps the seek pointer target')
            assert(iy + ifs / 2 <= __H__, 'Times line left the frame')
            assert(L.seek_bar.x1 <= L.metadata.x + 16 and L.seek_bar.x2 >= __W__ - L.metadata.x - 16, 'Full seek bar does not span the row')
            mp.set_property('user-data/title-block', 'ok')
            """, width, height), rawAss: true, resultProperty: "user-data/title-block", beforeScript: AutoplayHudProbe);
        Assert.Equal("ok", result);
    }

    [Theory]
    [InlineData(960, 540)]
    [InlineData(640, 360)]
    public void CompactTimelineFlanksTheBarAndCarriesEndTimeInTheMeta(int width, int height)
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, windowWidth: width, windowHeight: height, setup: Tokens("""
            local s = osc_test_state
            dims = {w=__W__, h=__H__}
            s.osc_disabled, s.current_alpha, s.target_alpha = false, 1, 1
            handlers['osc-set-end-time-format']('24h', 'Ends {time}')
            s.content_title, s.content_subtitle = 'Show', 'S1:E2'
            s.duration, s.time_pos = 3600, 600
            osc_test_layout()
            osc_test_render_hud()
            local data, L = s.osc_overlay.data, s.layout
            local plain = data:gsub('{[^}]*}', '')
            assert(not plain:find('10:00 / ', 1, true), 'Compact layout kept the combined time line')
            local ex, ey = hud_event(data, '10:00')
            local rx, ry = hud_event(data, '-50:00')
            assert(ex and rx, 'Compact elapsed/remaining times missing')
            assert(L.time_elapsed and L.time_remaining, 'Compact time rects missing')
            assert(L.time_elapsed.x + L.time_elapsed.w <= L.seek_bar.x1 and L.time_remaining.x >= L.seek_bar.x2,
                'Times overlap the seek pointer target')
            assert(math.abs(ey - L.seek_bar.draw_y) <= 2 and math.abs(ry - L.seek_bar.draw_y) <= 2, 'Times are not beside the bar')
            local _, my, _, meta = hud_event(data, 'S1:E2')
            assert(meta and meta:find('Ends ', 1, true), 'Compact meta lacks the end time')
            mp.set_property('user-data/compact-times', 'ok')
            """, width, height), rawAss: true, resultProperty: "user-data/compact-times", beforeScript: AutoplayHudProbe);
        Assert.Equal("ok", result);
    }

    [Theory]
    [InlineData(1024, 576, false, false)]
    [InlineData(1280, 720, true, false)]
    [InlineData(1536, 864, true, false)]
    [InlineData(1920, 1080, true, true)]
    [InlineData(2560, 1440, true, true)]
    [InlineData(3840, 2160, true, true)]
    public void FullRailGroupsMenusAndMovesWindowActionsIntoMore(int width, int height, bool volumeSlider, bool labels)
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, windowWidth: width, windowHeight: height, setup: Tokens("""
            local s = osc_test_state
            dims = {w=__W__, h=__H__}
            s.osc_disabled, s.current_alpha, s.target_alpha = false, 1, 1
            s.series_context, s.prev_ep_available, s.next_ep_available = true, true, true
            s.audio_tracks = {{index=0, language='en', language_name='English', channels=6}, {index=1, language='fr', language_name='French', channels=2}}
            s.active_audio, s.active_subtitle = 0, -1
            s.chapters = {{index=0, title='Start', start_seconds=0, end_seconds=60}, {index=1, title='Two', start_seconds=60, end_seconds=120}}
            s.quality_info = {versions={{}, {}}, tiers={}}
            s.native_more_scope = 'more-owner'
            s.duration, s.time_pos = 120, 10
            osc_test_layout()
            local L = s.layout
            assert(not L.btn_pip and not L.btn_fill and not L.btn_stats, 'Window actions remained on the full rail')
            assert(L.btn_more and L.btn_audio and L.btn_cc and L.btn_chapters and L.btn_quality and L.btn_fullscreen and L.btn_volume, 'Rail control missing')
            local order = {'btn_volume', 'btn_audio', 'btn_cc', 'btn_chapters', 'btn_quality', 'btn_more', 'btn_fullscreen'}
            for i = 2, #order do
                assert(L[order[i - 1]].x + L[order[i - 1]].w <= L[order[i]].x, order[i - 1] .. ' is not before ' .. order[i])
            end
            assert((L.volume_bar ~= nil) == __VOLUME__, 'Inline volume slider visibility is wrong')
            assert(L.rail_dividers and #L.rail_dividers == 2, 'Rail groups are not divided')
            assert(L.rail_dividers[1] > L.btn_volume.x and L.rail_dividers[1] < L.btn_audio.x, 'First divider misplaced')
            assert(L.rail_dividers[2] > L.btn_chapters.x + L.btn_chapters.w and L.rail_dividers[2] < L.btn_quality.x, 'Second divider misplaced')
            local transport_right = L.btn_next_ep.x + L.btn_next_ep.w
            assert(L.btn_volume.x >= transport_right + 8, 'Rail overlaps the centred transport')
            assert(L.btn_play.cx == __W__ / 2, 'Transport left the centreline')
            osc_test_render_hud()
            local plain = s.osc_overlay.data:gsub('{[^}]*}', '')
            local labelled = plain:find('English 5.1', 1, true) ~= nil and plain:find('Chapters', 1, true) ~= nil
            assert(labelled == __LABELS__, 'Rail labels shown at the wrong width')
            osc_test_transport('btn_more', false)
            local opened = sent_matching('silo-native-more-open')
            assert(#opened == 1, 'More did not open its native menu')
            local layout = opened[1]:match('([^|]+)$')
            assert(layout == __MORE__, 'More did not name its layout: ' .. opened[1])
            mp.set_property('user-data/full-rail', 'ok')
            """, width, height, volumeSlider, labels), rawAss: true, resultProperty: "user-data/full-rail", beforeScript: AutoplayHudProbe);
        Assert.Equal("ok", result);
    }

    private static string Tokens(string lua, int width, int height, bool volumeSlider = false, bool labels = false)
        => lua.Replace("__W__", width.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__H__", height.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__VOLUME__", volumeSlider ? "true" : "false", StringComparison.Ordinal)
            .Replace("__LABELS__", labels ? "true" : "false", StringComparison.Ordinal)
            .Replace("__MORE__", volumeSlider ? "'full'" : "'full-novolume'", StringComparison.Ordinal);

    [Fact]
    public void LoadingCoverShowsTheOwnedBackdropUnderTheSpinnerOnlyWhileLoading()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            dims = {w=1920, h=1080}
            s.osc_disabled = false
            handlers['osc-set-loading-backdrop']('k1', 'C:/fixture/backdrop-1920x1080.bgra', '1920', '1080', '7680', '1920', '1080')
            handlers['osc-set-loading']('true')
            local add = natives[#natives]
            assert(add and add.name == 'overlay-add' and add.file == 'C:/fixture/backdrop-1920x1080.bgra', 'Loading did not show the backdrop')
            assert(add.x == 0 and add.y == 0 and add.dw == 1920 and add.dh == 1080 and add.fmt == 'bgra', 'Backdrop does not cover the frame')
            local cover_id = add.id
            assert(s.playback_wait_overlay.data:find('\\p1', 1, true), 'Spinner was not drawn over the cover')
            natives = {}
            dims = {w=1280, h=720}
            osc_test_render_wait()
            assert(natives[#natives] and natives[#natives].dw == 1280 and natives[#natives].dh == 720, 'Cover did not follow a resize')
            local request = sent_matching('silo-loading-backdrop-request')
            assert(#request == 1 and request[1] == 'silo-loading-backdrop-request|k1|1280|720', 'Resize did not request a matching bitmap')
            natives = {}
            handlers['osc-set-loading']('false')
            assert(natives[1] and natives[1].name == 'overlay-remove' and natives[1].id == cover_id, 'First frame left the cover up')
            natives = {}
            handlers['osc-set-buffering']('true')
            s.playback_buffering = true
            osc_test_render_wait()
            for _, cmd in ipairs(natives) do assert(cmd.name ~= 'overlay-add', 'Rebuffering covered the picture') end
            assert(s.playback_wait_overlay.data:find('\\1c&H000000&', 1, true), 'Rebuffer spinner lacks its dark disc')
            s.playback_buffering = false
            natives = {}
            handlers['osc-set-loading']('true')
            handlers['osc-clear-loading-backdrop']('other')
            assert(natives[#natives].name == 'overlay-add', 'A stale clear removed the current cover')
            handlers['osc-clear-loading-backdrop']('k1')
            assert(natives[#natives].name == 'overlay-remove', 'Clearing kept the cover')
            mp.set_property('user-data/loading-cover', 'ok')
            """, rawAss: true, resultProperty: "user-data/loading-cover", beforeScript: AutoplayHudProbe);
        Assert.Equal("ok", result);
    }
}
