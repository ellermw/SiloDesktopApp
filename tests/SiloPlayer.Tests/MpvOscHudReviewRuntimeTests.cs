namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
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
