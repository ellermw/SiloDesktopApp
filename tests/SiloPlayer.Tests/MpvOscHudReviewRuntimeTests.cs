namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    [Fact]
    public void HudControllerOnlyPauseResumeReleasesFocusAfterTheInputIdleTimeout()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.mouse_in_bar = false
            handlers['osc-controller-activate']() -- First A focuses Play.
            assert(s.controller_focus_name == 'btn_play')
            handlers['osc-controller-activate']() -- Host pauses.
            assert(s.pause)
            hud_time = 13
            assert(hide_check)()
            assert(s.target_alpha == 1, 'Pause must retain the HUD')
            hud_time = 14
            handlers['osc-controller-activate']() -- Host resumes; refresh idle period.
            assert(not s.pause)
            hud_time = 16
            hide_check()
            assert(s.target_alpha == 1, 'Recent controller input still owns focus')
            hud_time = 17
            hide_check()
            mp.set_property('user-data/hud-result', tostring(s.target_alpha)..':'
                ..tostring(s.controller_focus_name == nil)..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local timeout = mp.add_timeout
            mp.add_timeout = function(seconds, callback)
                if seconds == 3 then hide_check = callback end
                return timeout(seconds, callback)
            end
            -- Model only the native host's response to pause-toggle intents.
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-pause-toggle' then
                    osc_test_state.pause = not osc_test_state.pause
                end
                return command(...)
            end
            """);
        Assert.Equal("0:true:silo-pause-toggle,silo-pause-toggle", output);
    }

    [Theory]
    [InlineData("file", "none")]
    [InlineData("file", "subtitles")]
    [InlineData("file", "audio")]
    [InlineData("disable", "none")]
    [InlineData("disable", "subtitles")]
    [InlineData("disable", "audio")]
    public void HudSurfaceReplacementRetiresControllerFocusAndKeyboardMenus(string retirement, string menu)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.mouse_in_bar = false
            s.audio_tracks = { {index=1,name='First'}, {index=2,name='Second'} }
            handlers['osc-controller-nav']('right')
            assert(s.controller_focus_name)
            local overlay
            if '{{menu}}' ~= 'none' then
                local button = '{{menu}}' == 'audio' and 'btn_audio' or 'btn_cc'
                for i = 1, 30 do
                    if s.controller_focus_name == button then break end
                    handlers['osc-controller-nav']('right')
                end
                handlers['osc-controller-activate']()
                assert(s.keyboard_menu_kind == '{{menu}}')
                overlay = '{{menu}}' == 'audio' and s.audio_menu_overlay or s.subtitle_menu_overlay
                assert(overlay and overlay.data ~= '')
            end
            if '{{retirement}}' == 'file' then events['file-loaded']()
            else handlers['osc-set-visibility']('false') end
            local retired = s.controller_focus_name == nil and s.keyboard_menu_kind == nil
                and s.keyboard_menu_index == -1 and not s.subtitle_menu_visible and not s.audio_menu_visible
                and (not overlay or overlay.data == '')
            if '{{retirement}}' == 'disable' then handlers['osc-set-visibility']('true') end
            assert(hide_check)()
            mp.set_property('user-data/hud-result', tostring(retired)..':'..tostring(s.target_alpha))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local timeout = mp.add_timeout
            mp.add_timeout = function(seconds, callback)
                if seconds == 3 then hide_check = callback end
                return timeout(seconds, callback)
            end
            """);
        Assert.Equal("true:0", output);
    }

    [Theory]
    [InlineData("prompt")]
    [InlineData("undo")]
    public void HudCompactPointerControlsDoNotActivateTheBufferingPrompt(string pill)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = { {index=1,name='First'}, {index=2,name='Second'} }
            osc_test_layout()
            handlers['osc-buffering-quality']('{{pill}}')
            assert(#s.buffering_quality_actions > 0)
            local r = assert(s.layout.btn_more)
            local x, y = tostring(r.cx), tostring(r.cy)
            handlers['osc-mouse-down'](x, y)
            handlers['osc-mouse-up'](x, y)
            handlers['osc-video-click'](x, y)
            assert(s.compact_menu_visible, 'More must open the compact sheet')
            osc_test_render_hud()
            for _, item in ipairs(s.compact_menu_items) do
                if item.name == 'btn_audio' then r = item; break end
            end
            assert(r.name == 'btn_audio')
            x, y = tostring(r.x + r.w / 2), tostring(r.y + r.h / 2)
            handlers['osc-mouse-down'](x, y)
            handlers['osc-mouse-up'](x, y)
            handlers['osc-video-click'](x, y)
            mp.set_property('user-data/hud-result', tostring(s.audio_menu_visible)..':'
                ..tostring(not s.compact_menu_visible)..':'..tostring(s.controller_focus_name == nil)
                ..':'..tostring(s.buffering_quality_pill)..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal($"true:true:true:{pill}:", output);
    }

    [Theory]
    [InlineData("btn_exit")]
    [InlineData("btn_minimize")]
    public void HudTopChromePressDuringFadeOwnsTheSurfaceThroughRelease(string button)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.last_fade_time = hud_time
            osc_test_layout()
            local r = assert(s.layout['{{button}}'])
            local x, y = tostring(r.cx), tostring(r.cy)
            handlers['osc-hide']()
            hud_time = 10.15
            osc_test_tick()
            assert(s.current_alpha >= .1 and s.current_alpha < 1 and s.target_alpha == 0)
            -- No mouse-move wakeup: the press itself must reverse the fade.
            handlers['osc-mouse-down'](x, y)
            assert(s.pressed_control == '{{button}}' and #intents == 0)
            hud_time = 13.5
            assert(hide_check)()
            osc_test_tick()
            assert(s.target_alpha == 1 and s.current_alpha >= .1)
            handlers['osc-mouse-up'](x, y)
            handlers['osc-video-click'](x, y)
            handlers['osc-mouse-up'](x, y)
            mp.set_property('user-data/hud-result', table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local timeout = mp.add_timeout
            mp.add_timeout = function(seconds, callback)
                if seconds == 3 then hide_check = callback end
                return timeout(seconds, callback)
            end
            """);
        Assert.Equal(button == "btn_exit" ? "silo-exit" : "silo-minimize", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HudPointerFitDoesNotKeepEitherHideTimerOwnedByControllerFocus(bool leave)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true,1,1
            osc_test_layout()
            local r = assert(s.layout.btn_fill)
            mp.set_property_number('panscan',0)
            handlers['osc-mouse-move'](tostring(r.cx),tostring(r.cy))
            handlers['osc-mouse-down'](tostring(r.cx),tostring(r.cy))
            handlers['osc-mouse-up'](tostring(r.cx),tostring(r.cy))
            local pointer_only = s.controller_focus_name == nil
            handlers['osc-mouse-move']('400','200')
            if {{leave.ToString().ToLowerInvariant()}} then handlers['osc-mouse-leave'](); assert(leave_check)()
            else assert(hide_check)() end
            mp.set_property('user-data/hud-result',tostring(mp.get_property_number('panscan'))..':'..tostring(pointer_only)..':'..tostring(s.target_alpha))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local timeout = mp.add_timeout
            mp.add_timeout = function(seconds, callback)
                if seconds == .5 then leave_check = callback end
                if seconds == 3 then hide_check = callback end
                return timeout(seconds, callback)
            end
            """);
        Assert.Equal("1:true:0", output);
    }

    [Fact]
    public void HudNativePointerMovementRetiresActualControllerFocus()
    {
        var output = CaptureStats(1920,1080,1920,1080,false, setup: """
            local s = osc_test_state
            s.osc_disabled,s.pause = false,false
            s.visible,s.current_alpha,s.target_alpha = true,1,1
            handlers['osc-controller-nav']('right')
            local focused = s.controller_focus_name ~= nil
            handlers['osc-mouse-move']('400','200')
            mp.set_property('user-data/hud-result',tostring(focused)..':'..tostring(s.controller_focus_name == nil))
            """, rawAss:true,resultProperty:"user-data/hud-result",beforeScript:HudProbe);
        Assert.Equal("true:true",output);
    }

    [Theory]
    [InlineData("btn_exit")]
    [InlineData("btn_minimize")]
    public void HudTopChromeDefersItsOnlyIntentUntilMatchingRelease(string button)
    {
        var output = CaptureStats(1920,1080,1920,1080,false,setup: $$"""
            hud_time,intents = 10,{}
            local s=osc_test_state
            s.osc_disabled,s.pause=false,false
            s.visible,s.current_alpha,s.target_alpha=true,1,1
            osc_test_layout()
            local r=assert(s.layout['{{button}}'])
            local x,y=tostring(r.cx),tostring(r.cy)
            handlers['osc-mouse-move'](x,y); osc_test_render_hud()
            local hover=s.osc_overlay.data
            handlers['osc-mouse-down'](x,y); osc_test_render_hud()
            local before=#intents
            local held=s.osc_overlay.data~=hover and s.pressed_control=='{{button}}'
            hud_time=12 -- A long hold must still consume its release.
            handlers['osc-mouse-up'](x,y)
            handlers['osc-video-click'](x,y)
            handlers['osc-mouse-up'](x,y)
            mp.set_property('user-data/hud-result',tostring(before)..':'..tostring(held)..':'..table.concat(intents,','))
            """,rawAss:true,resultProperty:"user-data/hud-result",beforeScript:HudProbe);
        Assert.Equal($"0:true:{(button=="btn_exit"?"silo-exit":"silo-minimize")}",output);
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("other-button")]
    [InlineData("leave")]
    [InlineData("hidden")]
    [InlineData("file")]
    [InlineData("menu")]
    [InlineData("failure")]
    public void HudTopChromeCanceledPressCannotDispatchOnRelease(string retirement)
    {
        var output=CaptureStats(1920,1080,1920,1080,false,setup: $$"""
            hud_time,intents=10,{}
            local s=osc_test_state
            s.osc_disabled,s.pause=false,false
            s.visible,s.current_alpha,s.target_alpha=true,1,1
            osc_test_layout()
            local r=s.layout.btn_exit
            local x,y=tostring(r.cx),tostring(r.cy)
            handlers['osc-mouse-down'](x,y)
            if '{{retirement}}'=='outside' then x,y='400','200'
            elseif '{{retirement}}'=='other-button' then r=s.layout.btn_minimize; x,y=tostring(r.cx),tostring(r.cy)
            elseif '{{retirement}}'=='leave' then handlers['osc-mouse-leave']()
            elseif '{{retirement}}'=='hidden' then handlers['osc-set-visibility']('false'); handlers['osc-set-visibility']('true')
            elseif '{{retirement}}'=='file' then events['file-loaded']()
            elseif '{{retirement}}'=='menu' then s.subtitle_menu_visible=true
            elseif '{{retirement}}'=='failure' then s.playback_failure_visible=true end
            handlers['osc-mouse-up'](x,y)
            mp.set_property('user-data/hud-result',tostring(#intents)..':'..tostring(s.pressed_control==nil))
            """,rawAss:true,resultProperty:"user-data/hud-result",beforeScript:HudProbe);
        Assert.Equal("0:true",output);
    }
}
