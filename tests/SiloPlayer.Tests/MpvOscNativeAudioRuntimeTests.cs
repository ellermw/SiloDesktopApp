namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    [Fact]
    public void NativeAudioPendingBlocksNativeWindowMinimizeShortcut()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local minimize = native_keys['silo-window-minimize']
            if not minimize then mp.set_property('user-data/native-audio-result','unsupported'); return end
            local calls = 0
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-minimize' then calls = calls + 1; return end
                return command(...)
            end
            local s = osc_test_state
            s.native_menu_request = { owner = '11111111111111111111111111111111', sequence = 1 }
            minimize()
            local blocked = calls == 0
            s.native_menu_request = nil
            minimize()
            mp.set_property('user-data/native-audio-result', tostring(blocked)..':'..tostring(calls))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("true:1", output);
    }

    private const string NativeAudioProbe = """
        native_handlers, native_requests, native_intents, native_keys, native_inputs, native_timers = {}, {}, {}, {}, {}, {}
        local register = mp.register_script_message
        mp.register_script_message = function(name, callback)
            native_handlers[name] = callback
            return register(name, callback)
        end
        local key = mp.add_key_binding
        mp.add_key_binding = function(binding, name, callback, flags)
            native_keys[name] = callback
            return key(binding, name, callback, flags)
        end
        local forced_key = mp.add_forced_key_binding
        mp.add_forced_key_binding = function(binding, name, callback, flags)
            native_keys[name] = callback
            return forced_key(binding, name, callback, flags)
        end
        local timeout = mp.add_timeout
        mp.add_timeout = function(delay, callback)
            table.insert(native_timers, {delay=delay, callback=callback})
            return timeout(delay, callback)
        end
        local time = mp.get_time
        mp.get_time = function() return native_probe_time or time() end
        local command = mp.commandv
        mp.commandv = function(...)
            local args = {...}
            if args[1] == 'script-message' and args[2] == 'silo-native-audio-open' then
                table.insert(native_requests, args)
                return
            end
            if args[1] == 'script-message' and args[2] == 'silo-native-menu-input' then
                table.insert(native_inputs, args)
                return
            end
            if args[1] == 'script-message' and (args[2] == 'silo-pause-toggle' or args[2] == 'silo-volume-changed'
                or args[2] == 'silo-audio-select' or args[2] == 'silo-seek-relative') then
                table.insert(native_intents, args[2])
                return
            end
            return command(...)
        end
        """;

    [Theory]
    [InlineData("pointer", 1920, 1080)]
    [InlineData("controller", 1920, 1080)]
    [InlineData("compact", 460, 700)]
    public void NativeAudioCapabilityOpensOneScopedHostInsteadOfTheLegacyMenu(string route, int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = { {index=0,title='English'}, {index=1,title='Japanese'} }
            if native_handlers['osc-native-audio-scope'] then native_handlers['osc-native-audio-scope']('11111111111111111111111111111111') end
            osc_test_layout()
            if '{{route}}' == 'pointer' then
                local r = s.layout.btn_audio
                native_handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            elseif '{{route}}' == 'controller' then
                s.controller_focus_name = 'btn_audio'; osc_test_activate()
            else
                s.controller_focus_name = 'btn_more'; osc_test_activate()
                s.controller_focus_name = 'overflow_btn_audio'; osc_test_activate()
            end
            local request = native_requests[1]
            local scoped = request and request[3] == '11111111111111111111111111111111'
                and tonumber(request[4]) > 0 and tonumber(request[7]) > 0 and tonumber(request[8]) > 0
            mp.set_property('user-data/native-audio-result', tostring(#native_requests)..':'..tostring(s.audio_menu_visible)..':'..tostring(scoped))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("1:false:true", output);
    }

    [Fact]
    public void NativeAudioPendingGuardRejectsStaleCloseAndBlocksTheVideoClick()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time = 10
            local scope = native_handlers['osc-native-audio-scope']
            if not scope then mp.set_property('user-data/native-audio-result','unsupported'); return end
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {{index=0,title='English'}, {index=1,title='Japanese'}}
            scope('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name = 'btn_audio'; osc_test_activate()
            local request = assert(native_requests[1])
            native_handlers['osc-native-menu-closed'](request[3], '0', 'false')
            local retained = s.native_menu_request ~= nil
            native_handlers['osc-video-click']('100','100')
            local blocked = #native_intents
            native_handlers['osc-native-menu-closed'](request[3], request[4], 'false')
            local closed = s.native_menu_request == nil
            native_probe_time = 11
            native_handlers['osc-video-click']('100','100')
            mp.set_property('user-data/native-audio-result', tostring(retained)..':'..tostring(blocked)..':'..tostring(closed)..':'..tostring(#native_intents))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("true:0:true:1", output);
    }

    [Fact]
    public void NativeAudioPendingInputDoesNotReachUnderlyingVolumeOrTransport()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time = 10
            local scope = native_handlers['osc-native-audio-scope']
            if not scope then mp.set_property('user-data/native-audio-result','unsupported'); return end
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {{index=0,title='English'}, {index=1,title='Japanese'}}
            scope('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name = 'btn_audio'; osc_test_activate()
            local volume = mp.get_property_number('volume')
            native_probe_time = 11
            local r = s.layout.btn_play
            native_handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            native_handlers['osc-mouse-up'](tostring(r.cx), tostring(r.cy))
            native_handlers['osc-video-click']('100','100')
            native_keys['silo-osc-wheel-down']()
            native_handlers['osc-controller-nav']('right')
            native_handlers['osc-controller-activate']()
            mp.set_property('user-data/native-audio-result', tostring(#native_intents)..':'..tostring(mp.get_property_number('volume') == volume))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("0:true", output);
    }

    [Fact]
    public void NativeAudioPendingMenuRetainsVisibleOpenerAcrossHideAndLeaveAndBlocksStats()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause, s.mouse_in_window, s.mouse_in_bar = false, false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {{index=0,title='English'}, {index=1,title='Japanese'}}
            native_handlers['osc-native-audio-scope']('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name = 'btn_audio'; osc_test_activate()
            native_handlers['osc-hide']()
            local after_hide = s.visible and s.target_alpha == 1
            native_handlers['osc-mouse-leave']()
            native_timers[#native_timers].callback()
            local after_leave = s.visible and s.target_alpha == 1
            native_keys['silo-osc-toggle-stats']()
            local blocked_stats = not s.stats_visible
            mp.set_property('user-data/native-audio-result', tostring(after_hide)..':'..tostring(after_leave)..':'..tostring(blocked_stats))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("true:true:true", output);
    }

    [Theory]
    [InlineData("loading")]
    [InlineData("hidden")]
    [InlineData("postroll")]
    public void NativeAudioCannotOpenForAnUnavailablePlaybackSurface(string unavailable)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading, s.post_roll_active = false, false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = { {index=0,title='English'}, {index=1,title='Japanese'} }
            native_handlers['osc-native-audio-scope']('11111111111111111111111111111111')
            if '{{unavailable}}' == 'loading' then native_handlers['osc-set-loading']('true')
            elseif '{{unavailable}}' == 'hidden' then native_handlers['osc-set-visibility']('false')
            else native_handlers['osc-set-post-roll']('true') end
            osc_test_layout(); s.controller_focus_name = 'btn_audio'; osc_test_activate()
            mp.set_property('user-data/native-audio-result', tostring(#native_requests)..':'..tostring(s.audio_menu_visible))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("0:false", output);
    }

    [Fact]
    public void NativeAudioScopeReplacementRejectsOldCloseAndRoutesCurrentScopedNavigationOnly()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading = false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {{index=0,title='English'}, {index=1,title='Japanese'}}
            local scope, closed = native_handlers['osc-native-audio-scope'], native_handlers['osc-native-menu-closed']
            scope('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name = 'btn_audio'; osc_test_activate()
            local first = assert(native_requests[1])
            scope('11111111111111111111111111111111')
            local retained = s.native_menu_request ~= nil
            scope('22222222222222222222222222222222')
            local retired = s.native_menu_request == nil
            s.controller_focus_name = 'btn_audio'; osc_test_activate()
            local second = assert(native_requests[2])
            closed(first[3], first[4], 'false')
            local successor = s.native_menu_request ~= nil
            native_handlers['osc-controller-nav']('down')
            native_keys['silo-menu-home']()
            native_keys['silo-menu-end']()
            native_handlers['osc-controller-activate']()
            native_keys['silo-menu-escape']()
            local routed = #native_inputs == 5
            for _, input in ipairs(native_inputs) do
                routed = routed and input[3] == second[3] and input[4] == second[4]
            end
            closed(second[3], second[4], 'false')
            s.controller_focus_name = 'btn_audio'; osc_test_activate()
            local third = assert(native_requests[3])
            closed(second[3], second[4], 'false')
            local replay_blocked = s.native_menu_request and tonumber(third[4]) > tonumber(second[4])
            scope('')
            local revoked = s.native_menu_request == nil
            mp.set_property('user-data/native-audio-result', tostring(retained)..':'..tostring(retired)..':'..tostring(successor)..':'..tostring(routed)..':'..tostring(replay_blocked)..':'..tostring(revoked))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("true:true:true:true:true:true", output);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void NativeAudioWithNoAlternativeTrackStaysUnavailable(int count)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading = false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {}
            for index=1,{{count}} do s.audio_tracks[index] = {index=index-1,title='Only track'} end
            native_handlers['osc-native-audio-scope']('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name = 'btn_audio'; osc_test_activate()
            mp.set_property('user-data/native-audio-result', tostring(#native_requests)..':'..tostring(s.audio_menu_visible)..':'..tostring(s.native_menu_request == nil))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("0:false:true", output);
    }

    [Fact]
    public void NativeAudioWithoutHostCapabilityKeepsTheExistingAudioMenu()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading = false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {{index=0,title='English'}, {index=1,title='Japanese'}}
            osc_test_layout(); s.controller_focus_name = 'btn_audio'; osc_test_activate()
            mp.set_property('user-data/native-audio-result', tostring(#native_requests)..':'..tostring(s.audio_menu_visible)..':'..tostring(#s.audio_menu_items))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("0:true:2", output);
    }

    [Fact]
    public void NativeAudioPendingMenuOwnsAllPlaybackShortcutsAndPointerRoutes()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading = false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {{index=0,title='English'}, {index=1,title='Japanese'}}
            s.subtitle_preferences_loaded, s.active_subtitle = true, 0
            native_handlers['osc-native-audio-scope']('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name = 'btn_audio'; osc_test_activate()
            local volume, mute, delay, panscan = mp.get_property('volume'), mp.get_property('mute'),
                mp.get_property('sub-delay'), mp.get_property('panscan')
            native_probe_time = 11
            for _, name in ipairs({
                'silo-seek-back','silo-seek-fwd','silo-vol-up','silo-vol-down','silo-menu-home','silo-menu-end',
                'silo-menu-activate','silo-control-focus-next','silo-menu-escape','silo-play-pause-space',
                'silo-play-pause-k','silo-play-pause-k-shift','silo-mute-toggle','silo-mute-toggle-shift',
                'silo-captions-toggle','silo-captions-toggle-shift','silo-sub-delay-back','silo-sub-delay-fwd',
                'silo-fs-override','silo-fs-override-shift','silo-pip-override','silo-pip-override-shift',
                'silo-osc-toggle-stats','silo-osc-toggle-stats-shift','silo-osc-mbtn-right',
                'silo-osc-mbtn-left-dbl','silo-osc-wheel-up','silo-osc-wheel-down'}) do
                assert(native_keys[name], name)()
            end
            native_handlers['osc-toggle-stats']()
            native_handlers['osc-mouse-down-right']('100','100')
            native_handlers['osc-mouse-move']('100','100')
            native_handlers['osc-video-click']('100','100')
            local unchanged = mp.get_property('volume') == volume and mp.get_property('mute') == mute
                and mp.get_property('sub-delay') == delay and mp.get_property('panscan') == panscan
            mp.set_property('user-data/native-audio-result', tostring(#native_intents)..':'..tostring(unchanged)..':'..tostring(not s.stats_visible)..':'..tostring(#native_inputs))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("0:true:true:9", output);
    }

    [Theory]
    [InlineData("controller")]
    [InlineData("pointer")]
    public void NativeAudioTransfersHudPointerOwnershipAndRestoresOnlyTheLogicalKeyboardOpener(string route)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading = false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.mouse_in_window, s.mouse_in_bar = true, true
            s.audio_tracks = { {index=0,title='English'}, {index=1,title='Japanese'} }
            native_handlers['osc-native-audio-scope']('11111111111111111111111111111111')
            osc_test_layout()
            if '{{route}}' == 'controller' then
                -- The pointer may remain over Play when the controller opens Audio.
                local play = s.layout.btn_play
                s.mouse_x, s.mouse_y = play.cx, play.cy
                s.controller_focus_name = 'btn_audio'
                osc_test_activate()
            else
                local audio = s.layout.btn_audio
                native_handlers['osc-mouse-down'](tostring(audio.cx), tostring(audio.cy))
            end
            local request = assert(native_requests[1])
            local transferred = s.mouse_x == -1 and s.mouse_y == -1 and not s.mouse_in_window
                and not s.mouse_in_bar and s.controller_focus_name == nil
            osc_test_tick()
            local actual = s.osc_overlay.data
            -- Compare actual rendered ASS with the neutral owner state. Audio's
            -- active indicator remains in both; stale hover/focus is the difference.
            s.mouse_x, s.mouse_y, s.mouse_in_window, s.mouse_in_bar = -1, -1, false, false
            s.controller_focus_name = nil
            osc_test_tick()
            local neutral = s.osc_overlay.data
            native_handlers['osc-native-menu-closed'](request[3], request[4], 'false')
            local restored = '{{route}}' == 'controller' and s.controller_focus_name == 'btn_audio'
                or '{{route}}' == 'pointer' and s.controller_focus_name == nil
            mp.set_property('user-data/native-audio-result', tostring(actual == neutral)..':'..tostring(transferred)..':'..tostring(restored))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("true:true:true", output);
    }
}
