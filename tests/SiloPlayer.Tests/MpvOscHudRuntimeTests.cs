namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    private const string HudProbe = """
        handlers, intents, events = {}, {}, {}
        local register = mp.register_script_message
        mp.register_script_message = function(name, callback)
            handlers[name] = callback
            return register(name, callback)
        end
        local native_time = mp.get_time
        mp.get_time = function() return hud_time or native_time() end
        local register_event = mp.register_event
        mp.register_event = function(name, callback)
            events[name] = callback
            return register_event(name, callback)
        end
        local command = mp.commandv
        mp.commandv = function(...)
            local args = {...}
            if args[1] == 'script-message' and args[2]:match('^silo%-') then
                if args[2] ~= 'silo-cursor-visible' and args[2] ~= 'silo-cursor-hidden'
                    and args[2] ~= 'silo-buffering-obscured'
                    -- Playback Info's accessible rows are host state, not a user intent.
                    and args[2] ~= 'silo-stats-panel-rows' then
                    table.insert(intents, args[2])
                end
                return
            end
            return command(...)
        end
        """;

    [Theory]
    [InlineData("btn_exit", 960, 540)]
    [InlineData("btn_minimize", 960, 540)]
    [InlineData("btn_exit", 1920, 1080)]
    [InlineData("btn_minimize", 1920, 1080)]
    [InlineData("btn_exit", 3840, 2160)]
    [InlineData("btn_minimize", 3840, 2160)]
    public void HudTopChromeRendersDistinctHoverPressAndRelease(string button, int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.mouse_x, s.mouse_y = -1, -1
            osc_test_layout(); osc_test_render_hud()
            local normal = s.osc_overlay.data
            local r = s.layout['{{button}}']
            local x, y = tostring(r.cx), tostring(r.cy)
            handlers['osc-mouse-move'](x, y); osc_test_render_hud()
            local hover = s.osc_overlay.data
            handlers['osc-mouse-down'](x, y); osc_test_render_hud()
            local pressed = s.osc_overlay.data
            handlers['osc-mouse-up'](x, y); osc_test_render_hud()
            mp.set_property('user-data/hud-result', table.concat({
                tostring(normal ~= hover), tostring(hover ~= pressed),
                tostring(s.osc_overlay.data == hover), table.concat(intents, ',')}, ':'))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal($"true:true:true:{(button == "btn_exit" ? "silo-exit" : "silo-minimize")}", output);
    }

    [Theory]
    [InlineData("btn_exit", false, false)]
    [InlineData("btn_exit", true, false)]
    [InlineData("btn_exit", false, true)]
    [InlineData("btn_exit", true, true)]
    [InlineData("btn_minimize", false, false)]
    [InlineData("btn_minimize", true, false)]
    [InlineData("btn_minimize", false, true)]
    [InlineData("btn_minimize", true, true)]
    public void HudTopChromeOwnsItsCompleteNativeClickWithoutPausing(string button, bool paused, bool room)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled = false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.watch_party = {{(room ? "{visible=true,is_host=false}" : "nil")}}
            mp.set_property_bool('pause', {{paused.ToString().ToLowerInvariant()}})
            osc_test_layout()
            local r = s.layout['{{button}}']
            local x, y = tostring(r.cx), tostring(r.cy)
            handlers['osc-mouse-down'](x, y)
            handlers['osc-mouse-up'](x, y)
            handlers['osc-video-click'](x, y)
            mp.set_property('user-data/hud-result', table.concat(intents, ',')..':'..tostring(mp.get_property_bool('pause')))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe);
        Assert.Equal($"{(button == "btn_exit" ? "silo-exit" : "silo-minimize")}:{paused.ToString().ToLowerInvariant()}", output);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void HudDisabledAudioDoesNotAdvertiseHoverOrDispatchAnAction(int tracks)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {{(tracks == 0 ? "{}" : "{{index=1,name='Only audio'}}")}}
            s.mouse_x, s.mouse_y = -1, -1
            osc_test_layout(); osc_test_render_hud()
            local normal = s.osc_overlay.data
            local r = s.layout.btn_audio
            if not r then
                mp.set_property('user-data/hud-result', 'absent:'..tostring(s.audio_menu_visible)..':'..table.concat(intents, ','))
                return
            end
            local x, y = tostring(r.cx), tostring(r.cy)
            handlers['osc-mouse-move'](x, y); osc_test_render_hud()
            local unchanged = s.osc_overlay.data == normal
            handlers['osc-mouse-down'](x, y); handlers['osc-mouse-up'](x, y); handlers['osc-video-click'](x, y)
            mp.set_property('user-data/hud-result', tostring(unchanged)..':'..tostring(s.audio_menu_visible)..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe);
        Assert.Equal(tracks == 0 ? "absent:false:" : "true:false:", output);
    }

    [Theory]
    [InlineData("btn_play")]
    [InlineData("btn_skip_back")]
    [InlineData("btn_skip_fwd")]
    [InlineData("btn_fullscreen")]
    public void HudTransportAndUtilityPressIsVisibleUntilRelease(string button)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            osc_test_layout()
            local r = s.layout['{{button}}']
            local x, y = tostring(r.cx), tostring(r.cy)
            handlers['osc-mouse-move'](x, y); osc_test_render_hud()
            local hover = s.osc_overlay.data
            handlers['osc-mouse-down'](x, y); osc_test_render_hud()
            local pressed = s.osc_overlay.data
            handlers['osc-mouse-up'](x, y); osc_test_render_hud()
            mp.set_property('user-data/hud-result', tostring(hover ~= pressed)..':'..tostring(s.osc_overlay.data == hover)..':'..tostring(#intents))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe);
        Assert.Equal("true:true:1", output);
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("disable")]
    [InlineData("fade")]
    [InlineData("file")]
    [InlineData("menu")]
    public void HudHeldFeedbackRetiresWithItsInputSurface(string retirement)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            osc_test_layout()
            local r = s.layout.btn_play
            local x, y = tostring(r.cx), tostring(r.cy)
            handlers['osc-mouse-move'](x, y); osc_test_render_hud()
            local hover = s.osc_overlay.data
            handlers['osc-mouse-down'](x, y); osc_test_render_hud()
            local held_was_visible = s.osc_overlay.data ~= hover
            local action = '{{retirement}}'
            if action == 'leave' then handlers['osc-mouse-leave']()
            elseif action == 'disable' then handlers['osc-set-visibility']('false'); handlers['osc-set-visibility']('true')
            elseif action == 'fade' then handlers['osc-hide']()
            elseif action == 'file' then events['file-loaded']()
            elseif action == 'menu' then
                r = s.layout.btn_cc
                handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            end
            mp.set_property('user-data/hud-result', tostring(held_was_visible)..':'..tostring(s.pressed_control == nil))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe);
        Assert.Equal("true:true", output);
    }

    [Fact]
    public void HudFocusOnlyPaintsAndOpenMenusDoNotHighlightUnderlyingButtons()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.mouse_x, s.mouse_y = -1, -1
            osc_test_layout(); osc_test_render_hud()
            local normal = s.osc_overlay.data
            s.controller_focus_name = 'btn_play'; osc_test_render_hud()
            local focus_visible = s.osc_overlay.data ~= normal
            s.controller_focus_name = nil
            s.subtitle_menu_visible = true
            osc_test_render_hud(); local covered = s.osc_overlay.data
            local r = s.layout.btn_play
            handlers['osc-mouse-move'](tostring(r.cx), tostring(r.cy)); osc_test_render_hud()
            mp.set_property('user-data/hud-result', tostring(focus_visible)..':'..tostring(s.osc_overlay.data == covered)..':'..tostring(#intents))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe);
        Assert.Equal("true:true:0", output);
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("press")]
    [InlineData("menu")]
    public void HudAutoHideRetainsAnOwnedFocusOrPressUntilItIsReleased(string ownership)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.mouse_in_bar, s.mouse_in_window = false, false
            osc_test_layout()
            if '{{ownership}}' == 'focus' then handlers['osc-controller-nav']('right')
            elseif '{{ownership}}' == 'menu' then
                local r = s.layout.btn_cc
                handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
                handlers['osc-show']()
            else
                local r = s.layout.btn_play
                handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
                handlers['osc-show']()
            end
            assert(hide_check, 'Expected the real OSC hide timer')
            hide_check()
            local kept = s.target_alpha == 1
            s.controller_focus_name, s.pressed_control = nil, nil
            s.subtitle_menu_visible = false
            hide_check()
            mp.set_property('user-data/hud-result', tostring(kept)..':'..tostring(s.target_alpha))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local timeout = mp.add_timeout
            mp.add_timeout = function(seconds, callback)
                if seconds == 3 then hide_check = callback end
                return timeout(seconds, callback)
            end
            """);
        Assert.Equal("true:0", output);
    }

    [Fact]
    public void HudFillRetainsPressedFeedbackUntilRelease()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            osc_test_layout()
            -- #3993 moved Fit into More; the mute button is the rail's utility control.
            local r = assert(s.layout.btn_volume)
            handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            local held = s.pressed_control == 'btn_volume'
            handlers['osc-mouse-up'](tostring(r.cx), tostring(r.cy))
            mp.set_property('user-data/hud-result', tostring(held)..':'..tostring(s.pressed_control == nil))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe);
        Assert.Equal("true:true", output);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void HudAudioKeyboardPolicyMatchesItsDisabledAppearance(int count, bool canOpen)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {}
            for i = 1, {{count}} do table.insert(s.audio_tracks, {index=i,name='Synthetic audio '..i}) end
            osc_test_layout()
            s.controller_focus_name = 'btn_audio'
            handlers['osc-controller-activate']()
            local opened = s.audio_menu_visible
            s.audio_menu_visible, s.controller_focus_name = false, nil
            local navigable = false
            for i = 1, 30 do
                handlers['osc-controller-nav']('right')
                if s.controller_focus_name == 'btn_audio' then navigable = true end
            end
            mp.set_property('user-data/hud-result', tostring(opened)..':'..tostring(navigable)..':'..tostring(#intents))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe);
        var expected = canOpen.ToString().ToLowerInvariant();
        Assert.Equal($"{expected}:{expected}:0", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HudHiddenOrCoveredFillDoesNotDispatchUnderlayAction(bool menu)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = {{(menu ? "true, 1, 1" : "false, 0, 0")}}
            s.subtitle_menu_visible = {{menu.ToString().ToLowerInvariant()}}
            osc_test_layout()
            local r = assert(s.layout.btn_volume)
            mp.set_property_bool('mute', false)
            handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            mp.set_property('user-data/hud-result', tostring(mp.get_property_bool('mute'))..':'..tostring(s.pressed_control == nil))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe);
        Assert.Equal("false:true", output);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void HudCompactAudioKeepsDisabledRowsInert(int count, bool canOpen)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = {}
            for i = 1, {{count}} do table.insert(s.audio_tracks, {index=i,name='Synthetic audio '..i}) end
            osc_test_layout()
            local r = assert(s.layout.btn_more)
            handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            osc_test_render_hud()
            s.controller_focus_name = 'overflow_btn_audio'
            handlers['osc-controller-activate']()
            mp.set_property('user-data/hud-result', tostring(s.audio_menu_visible)..':'..tostring(s.compact_menu_visible)..':'..tostring(#intents))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe, windowWidth: 460, windowHeight: 700);
        Assert.Equal($"{canOpen.ToString().ToLowerInvariant()}:{(!canOpen).ToString().ToLowerInvariant()}:0", output);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void HudCompactChapterChoicesUseTheVisibleMoreAnchor(int count, bool canOpen)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.chapters = {}
            for i = 1, {{count}} do table.insert(s.chapters, {index=i,time=i*10,title='Synthetic chapter '..i}) end
            osc_test_layout()
            local r = assert(s.layout.btn_more)
            handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            osc_test_render_hud()
            s.controller_focus_name = 'overflow_btn_chapters'
            handlers['osc-controller-activate']()
            mp.set_property('user-data/hud-result', tostring(s.chapter_menu_visible)..':'..tostring(s.compact_menu_visible)..':'..tostring(#intents))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe, windowWidth: 460, windowHeight: 700);
        Assert.Equal($"{canOpen.ToString().ToLowerInvariant()}:{(!canOpen).ToString().ToLowerInvariant()}:0", output);
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("menu")]
    public void HudPointerLeaveUsesTheSameVisibilityOwnershipAsNormalAutoHide(string ownership)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.mouse_in_bar = false
            osc_test_layout()
            if '{{ownership}}' == 'focus' then handlers['osc-controller-nav']('right')
            else
                local r = s.layout.btn_cc
                handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
                handlers['osc-show']()
            end
            handlers['osc-mouse-leave']()
            assert(leave_check, 'Expected native pointer-leave hide timer')
            leave_check()
            local kept = s.target_alpha == 1
            s.controller_focus_name, s.subtitle_menu_visible = nil, false
            assert(hide_check, 'Expected a normal hide timer after ownership ends')
            hide_check()
            mp.set_property('user-data/hud-result', tostring(kept)..':'..tostring(s.target_alpha))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local timeout = mp.add_timeout
            mp.add_timeout = function(seconds, callback)
                if seconds == .5 then leave_check = callback end
                if seconds == 3 then hide_check = callback end
                return timeout(seconds, callback)
            end
            """);
        Assert.Equal("true:0", output);
    }
}
