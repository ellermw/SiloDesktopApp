namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    [Fact]
    public void NativeAudioCompactPointerCloseAllowsImmediateFreshVideoClick()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {{index=0,title='English'}, {index=1,title='Japanese'}}
            native_handlers['osc-native-audio-scope']('11111111111111111111111111111111')
            osc_test_layout()
            local more = assert(s.layout.btn_more)
            native_handlers['osc-mouse-down'](tostring(more.cx), tostring(more.cy))
            native_handlers['osc-mouse-up'](tostring(more.cx), tostring(more.cy))
            osc_test_tick()
            native_probe_time = 11 -- More's unrelated opening gesture has expired.
            local audio
            for _, item in ipairs(s.compact_menu_items) do if item.name == 'btn_audio' then audio = item end end
            assert(audio, 'compact Audio row missing')
            native_handlers['osc-mouse-down'](tostring(audio.x + audio.w / 2), tostring(audio.y + audio.h / 2))
            local request = assert(native_requests[1])
            native_handlers['osc-native-menu-closed'](request[3], request[4], 'false')
            native_probe_time = 11.01
            native_handlers['osc-mouse-up']('100','100')
            native_handlers['osc-video-click']('100','100')
            local release = #native_intents
            native_handlers['osc-mouse-down']('100','100')
            native_handlers['osc-mouse-up']('100','100')
            native_handlers['osc-video-click']('100','100')
            mp.set_property('user-data/native-audio-result', tostring(release)..':'..tostring(#native_intents))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.True(!output.StartsWith("Lua error:", StringComparison.Ordinal), output);
        Assert.Equal("0:1", output);
    }

    [Theory]
    [InlineData("opening")]
    [InlineData("outside")]
    [InlineData("unrelated")]
    public void NativeAudioPointerBarrierRetainsOnlyItsGesture(string origin)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = { {index=0,title='English'}, {index=1,title='Japanese'} }
            native_handlers['osc-native-audio-scope']('11111111111111111111111111111111')
            osc_test_layout()
            local r = s.layout.btn_audio
            native_handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            local request = assert(native_requests[1])
            if '{{origin}}' == 'unrelated' then s.ignore_video_click_until = 11 end
            native_handlers['osc-native-menu-closed'](request[3], request[4], '{{origin}}' == 'outside' and 'true' or 'false')
            native_probe_time = 10.01
            native_handlers['osc-mouse-up']('100','100')
            native_handlers['osc-video-click']('100','100')
            local release = #native_intents
            native_handlers['osc-mouse-down']('100','100')
            native_handlers['osc-mouse-up']('100','100')
            native_handlers['osc-video-click']('100','100')
            mp.set_property('user-data/native-audio-result', tostring(release)..':'..tostring(#native_intents))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal(origin == "unrelated" ? "0:0" : "0:1", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeAudioKeyboardCloseAllowsImmediateFreshVideoClick(bool pointerOpen)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = { {index=0,title='English'}, {index=1,title='Japanese'} }
            native_handlers['osc-native-audio-scope']('11111111111111111111111111111111')
            osc_test_layout()
            if {{pointerOpen.ToString().ToLowerInvariant()}} then
                local r = s.layout.btn_audio
                native_handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            else
                s.controller_focus_name = 'btn_audio'; osc_test_activate()
            end
            local request = assert(native_requests[1])
            native_handlers['osc-native-menu-closed'](request[3], request[4], 'false')
            native_probe_time = 10.01
            native_handlers['osc-mouse-down']('100','100')
            native_handlers['osc-mouse-up']('100','100')
            native_handlers['osc-video-click']('100','100')
            mp.set_property('user-data/native-audio-result', tostring(#native_intents))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("1", output);
    }

    [Fact]
    public void UnrelatedNativeCapabilityRevocationRetainsAudioInputAndSequence()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {{index=0,title='English'},{index=1,title='Japanese'}}
            local quality = native_handlers['osc-native-quality-scope']
            if not quality then mp.set_property('user-data/native-audio-result','unsupported'); return end
            local token = '11111111111111111111111111111111'
            quality(token); native_handlers['osc-native-audio-scope'](token)
            osc_test_layout(); s.controller_focus_name='btn_audio'; osc_test_activate()
            local first = assert(native_requests[1])
            quality('')
            local retained = s.native_menu_request ~= nil
            native_handlers['osc-video-click']('100','100')
            local blocked = #native_intents == 0
            native_handlers['osc-native-menu-closed'](token,first[4],'true')
            s.controller_focus_name='btn_audio'; osc_test_activate()
            local next = native_requests[2]
            mp.set_property('user-data/native-audio-result',tostring(retained)..':'..tostring(blocked)..':'..tostring(next and tonumber(next[4])>tonumber(first[4])))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("true:true:true", output);
    }

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
    [InlineData("retired")]
    [InlineData("retired-loading")]
    public void NativeAudioCannotOpenForAnUnavailablePlaybackSurface(string unavailable)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading, s.post_roll_active = false, false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = { {index=0,title='English'}, {index=1,title='Japanese'} }
            native_handlers['osc-native-audio-scope']('11111111111111111111111111111111')
            if '{{unavailable}}' == 'retired' or '{{unavailable}}' == 'retired-loading' then
                native_handlers['osc-native-audio-scope']('')
                if '{{unavailable}}' == 'retired-loading' then native_handlers['osc-set-loading']('true') end
            elseif '{{unavailable}}' == 'loading' then native_handlers['osc-set-loading']('true')
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

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(480, 270)]
    public void NativeAudioExplicitLegacyScopePreservesLocalSelectionUntilRevoked(int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading = false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {{index=0,title='English'}, {index=5,title='Japanese'}}
            local selected
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-audio-select' then selected = args[3] end
                return command(...)
            end
            local scope = native_handlers['osc-native-audio-scope']
            scope('11111111111111111111111111111111')
            scope('legacy')
            osc_test_layout()
            local focus = s.layout.btn_audio and 'btn_audio' or 'overflow_btn_audio'
            s.controller_focus_name = focus; osc_test_activate()
            assert(s.audio_menu_visible and #native_requests == 0, 'Host fallback did not open legacy Audio')
            native_keys['silo-menu-end']()
            native_keys['silo-menu-activate']()
            assert(not s.audio_menu_visible and selected == '5'
                and #native_intents == 1 and native_intents[1] == 'silo-audio-select',
                'Legacy Audio did not preserve the source-index selection')
            scope('')
            s.controller_focus_name = focus; osc_test_activate()
            assert(not s.audio_menu_visible and #native_requests == 0, 'Revocation retained the legacy fallback')
            scope('22222222222222222222222222222222')
            s.controller_focus_name = focus; osc_test_activate()
            assert(not s.audio_menu_visible and #native_requests == 1, 'A new native scope did not replace fallback')
            mp.set_property('user-data/native-audio-result', 'passed')
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("passed", output);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(480, 270)]
    public void NativeAudioInitialEmptyScopeAdvertisesCapabilityWithoutLegacyFallback(int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading = false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {{index=0,title='English'}, {index=1,title='Japanese'}}
            native_handlers['osc-native-audio-scope']('')
            osc_test_layout()
            s.controller_focus_name = s.layout.btn_audio and 'btn_audio' or 'overflow_btn_audio'
            osc_test_activate()
            mp.set_property('user-data/native-audio-result', tostring(s.native_audio_capable)..':'..tostring(#native_requests)..':'..tostring(s.audio_menu_visible))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("true:0:false", output);
    }

    [Theory]
    [InlineData(460, 700, 0)]
    [InlineData(460, 700, 1)]
    [InlineData(460, 700, 2)]
    [InlineData(480, 270, 0)]
    [InlineData(480, 270, 1)]
    [InlineData(480, 270, 2)]
    public void CompactLegacyAudioAndChaptersUseTheOverflowAnchor(int width, int height, int trackCount)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading = false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.duration, s.time_pos = 60, 0
            s.audio_tracks = {}
            for i=1,{{trackCount}} do s.audio_tracks[i] = {index=i-1,title='Audio '..i} end
            s.chapters = { {index=0,title='Opening',start_seconds=0,end_seconds=30},
                {index=1,title='Ending',start_seconds=30,end_seconds=60} }
            osc_test_layout()
            assert(not s.layout.btn_audio and not s.layout.btn_chapters and s.layout.btn_more)
            s.controller_focus_name = 'btn_more'; osc_test_activate(); osc_test_tick()
            local audio
            for _, row in ipairs(s.compact_menu_items) do if row.name == 'btn_audio' then audio = row end end
            if {{trackCount}} > 1 then
                assert(audio, 'Multiple-track Audio must remain reachable')
                native_handlers['osc-mouse-down'](tostring(audio.x+audio.w/2), tostring(audio.y+audio.h/2))
                assert(s.audio_menu_visible and #s.audio_menu_items > 0)
                assert(s.audio_menu_overlay.data:find('AUDIO', 1, true))
                native_keys['silo-menu-escape']()
            else
                assert(not audio, 'Audio without alternatives must not be offered')
                s.controller_focus_name = 'overflow_btn_audio'; osc_test_activate()
                assert(not s.audio_menu_visible, 'A stale Audio focus must remain inert')
            end
            s.compact_menu_visible = false
            s.controller_focus_name = 'btn_more'; osc_test_activate(); osc_test_tick()
            local chapters
            for _, row in ipairs(s.compact_menu_items) do if row.name == 'btn_chapters' then chapters = row end end
            assert(chapters, 'Chapters must remain reachable')
            native_handlers['osc-mouse-down'](tostring(chapters.x+chapters.w/2), tostring(chapters.y+chapters.h/2))
            assert(s.chapter_menu_visible and #s.chapter_menu_items > 0)
            assert(s.chapter_menu_overlay.data:find('CHAPTERS', 1, true))
            mp.set_property('user-data/native-audio-result', 'available-without-crash')
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("available-without-crash", output);
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
            -- The opening press still has an up event on the captured video
            -- HWND. Pending button messages must not reclaim its pointer.
            local play = s.layout.btn_play
            native_handlers['osc-mouse-up'](tostring(play.cx), tostring(play.cy))
            native_handlers['osc-mouse-down'](tostring(play.cx), tostring(play.cy))
            native_handlers['osc-mouse-down-right'](tostring(play.cx), tostring(play.cy))
            native_keys['silo-osc-mouse-move']()
            native_keys['silo-osc-mouse-enter']()
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

    [Fact]
    public void NativeAudioPendingTimersRetainOwnershipAndMatchingCloseRestartsNormalIdle()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause, s.playback_loading = false, false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.mouse_in_window, s.mouse_in_bar = false, false
            s.audio_tracks = {{index=0,title='English'}, {index=1,title='Japanese'}}
            native_handlers['osc-native-audio-scope']('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name = 'btn_audio'; osc_test_activate()
            local request = assert(native_requests[1])
            assert(s.controller_focus_name == nil, 'Native Audio must own focus before timer checks')
            local idle = assert(native_timers[#native_timers])
            assert(idle.delay == 3, 'Expected normal idle deadline')
            idle.callback()
            local kept_idle = s.native_menu_request ~= nil and s.visible and s.target_alpha == 1
            native_handlers['osc-mouse-leave']()
            local leave = assert(native_timers[#native_timers])
            assert(leave.delay == .5, 'Expected pointer-leave deadline')
            leave.callback()
            local kept_leave = s.native_menu_request ~= nil and s.visible and s.target_alpha == 1
            local timer_count = #native_timers
            native_handlers['osc-native-menu-closed'](request[3], request[4], 'false')
            local restarted = #native_timers > timer_count and native_timers[#native_timers].delay == 3
            s.controller_focus_name, s.mouse_in_window, s.mouse_in_bar = nil, false, false
            native_timers[#native_timers].callback()
            local hidden = s.native_menu_request == nil and not s.visible and s.target_alpha == 0
            native_probe_time = 11
            native_handlers['osc-video-click']('100','100')
            mp.set_property('user-data/native-audio-result', tostring(kept_idle)..':'..tostring(kept_leave)..':'..tostring(restarted)..':'..tostring(hidden)..':'..tostring(#native_intents == 1))
            """, rawAss: true, resultProperty: "user-data/native-audio-result", beforeScript: NativeAudioProbe);
        Assert.Equal("true:true:true:true:true", output);
    }

}
