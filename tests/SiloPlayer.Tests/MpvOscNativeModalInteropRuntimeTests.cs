namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    private static string? ModalInteropSource => Environment.GetEnvironmentVariable("SILO_OSC_NATIVE_MODAL_SOURCE_OVERRIDE");

    private static readonly string ModalInteropProbe = NativeAudioProbe.Replace(
        "args[2] == 'silo-native-audio-open'", "args[2]:match('^silo%-native%-.+%-open$')") + "\n" + """
        modal_actions = {}
        local modal_command = mp.commandv
        mp.commandv = function(...)
            local a={...}
            local name=a[2]
            if a[1]=='script-message' and (name=='silo-pause-toggle' or name=='silo-seek-relative'
                or name=='silo-seek-absolute' or name=='silo-volume-changed' or name=='silo-exit'
                or name=='silo-minimize' or name=='silo-fullscreen-toggle' or name=='silo-pip-toggle'
                or name=='silo-watch-party-action') or a[1]=='seek' or a[1]=='cycle'
                or (a[1]=='set' and name=='volume') then table.insert(modal_actions,a) end
            return modal_command(...)
        end
        local modal_timeout=mp.add_timeout
        mp.add_timeout=function(delay,callback)
            if delay==3 then
                modal_idle_hide=callback
                modal_idle_arms=(modal_idle_arms or 0)+1
            end
            if delay==.5 then modal_leave_hide=callback end
            return modal_timeout(delay,callback)
        end

        -- Extract actual ASS vector events whose first point is in this exact
        -- control rectangle. Separate the small status marker in its bottom
        -- quarter from the complete glyph; no source-text/icon-name heuristic.
        function modal_region(data,r)
            local all,glyph,dots={},{},0
            for tags,path in data:gmatch('(%b{})(m [^{}]+)%b{}') do
                local x,y=path:match('^m%s+(%-?%d+)%s+(%-?%d+)')
                x,y=tonumber(x),tonumber(y)
                if x and x>=r.x and x<=r.x+r.w and y>=r.y and y<=r.y+r.h then
                    local values={};for v in path:gmatch('%-?%d+') do table.insert(values,tonumber(v)) end
                    local left,right,top,bottom=math.huge,-math.huge,math.huge,-math.huge
                    for i=1,#values,2 do
                        left,right=math.min(left,values[i]),math.max(right,values[i])
                        top,bottom=math.min(top,values[i+1]),math.max(bottom,values[i+1])
                    end
                    local event=tags..path
                    table.insert(all,event)
                    if top>r.y+r.h*.75 and right-left<=8 and bottom-top<=8 then dots=dots+1
                    else table.insert(glyph,event) end
                end
            end
            assert(#all>0,'empty actual control ASS region')
            return table.concat(all,'\n'),table.concat(glyph,'\n'),dots
        end
        """;

    [Theory]
    [InlineData("video-click")]
    [InlineData("pause-key")]
    [InlineData("seek-key")]
    [InlineData("wheel")]
    [InlineData("controller-nav")]
    [InlineData("controller-activate")]
    public void NativeModalInteropPartyOwnsTransportInputUntilMatchingClose(string route)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time=10
            local s=osc_test_state
            s.osc_disabled,s.playback_loading,s.pause=false,false,false
            s.visible,s.current_alpha,s.target_alpha=true,1,1
            s.duration,s.time_pos,s.volume=120,10,50
            mp.set_property_number('volume',50)
            s.watch_party={visible=true,is_host=true,code='TEST1234'}
            local token='11111111111111111111111111111111'
            native_handlers['osc-native-party-scope'](token)
            osc_test_layout();s.controller_focus_name='btn_party';osc_test_activate()
            local request=assert(native_requests[1],'actual Party request missing')
            native_handlers['osc-native-dialog-opening'](request[3],request[4])
            native_handlers['osc-native-menu-closed'](request[3],request[4],'false')
            assert(s.native_dialog_request and not s.native_menu_request,'Party modal did not inherit input')
            native_probe_time=12
            modal_actions,native_inputs,native_requests={},{},{}
            local function input()
                if '{{route}}'=='video-click' then native_handlers['osc-video-click']('100','100')
                elseif '{{route}}'=='pause-key' then native_keys['silo-play-pause-space']()
                elseif '{{route}}'=='seek-key' then native_keys['silo-seek-fwd']()
                elseif '{{route}}'=='wheel' then native_keys['silo-osc-wheel-down']()
                elseif '{{route}}'=='controller-nav' then native_handlers['osc-controller-nav']('right')
                else s.controller_focus_name='btn_play';native_handlers['osc-controller-activate']() end
            end
            input()
            local held=#modal_actions==0 and #native_inputs==0 and #native_requests==0
                and ({{(route == "controller-nav" ? "true" : "false")}} and s.controller_focus_name==nil
                    or {{(route != "controller-nav" ? "true" : "false")}})
            if '{{route}}'=='wheel' then held=held and mp.get_property_number('volume')==50 end
            native_handlers['osc-native-dialog-closed'](request[3],'9999','false')
            local stale=s.native_dialog_request~=nil
            native_handlers['osc-native-dialog-closed'](request[3],request[4],'false')
            modal_actions,native_inputs,native_requests={},{},{}
            s.controller_focus_name=nil
            input()
            local released=not s.native_dialog_request and (#modal_actions>0 or #native_requests>0
                or ('{{route}}'=='controller-nav' and s.controller_focus_name~=nil))
            mp.set_property('user-data/modal-interop',tostring(held)..':'..tostring(stale)..':'..tostring(released))
            """, rawAss: true, resultProperty: "user-data/modal-interop", beforeScript: ModalInteropProbe,
            windowWidth: 1920, windowHeight: 1080, scriptOverride: ModalInteropSource);
        Assert.Equal("true:true:true", output);
    }

    [Theory]
    [InlineData("native-menu", false)]
    [InlineData("native-menu", true)]
    [InlineData("party-modal", false)]
    [InlineData("party-modal", true)]
    [InlineData("subtitle-modal", false)]
    [InlineData("subtitle-modal", true)]
    public void NativeModalInteropIdleAndLeaveHideTimersHoldWhileOwnedThenRelease(string owner, bool leave)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time=10
            local s=osc_test_state
            s.osc_disabled,s.playback_loading,s.pause=false,false,false
            s.visible,s.current_alpha,s.target_alpha=true,1,1
            s.mouse_x,s.mouse_y,s.mouse_in_window,s.mouse_in_bar=-1,-1,false,false
            s.controller_focus_name,s.pressed_control=nil,nil
            osc_test_layout()
            -- These are the real protocol's accepted ownership records. Keep
            -- only the timer's owner controlled; run its actual Lua callback.
            local request={owner='11111111111111111111111111111111',sequence=1,kind='party'}
            if '{{owner}}'=='native-menu' then s.native_menu_request=request
            elseif '{{owner}}'=='party-modal' then s.native_dialog_request=request
            else s.native_subtitle_dialog_request=request end
            native_handlers['osc-show']()
            if {{leave.ToString().ToLowerInvariant()}} then native_handlers['osc-mouse-leave']() end
            local timer={{(leave ? "modal_leave_hide" : "modal_idle_hide")}}
            local prior_arms=modal_idle_arms or 0
            assert(timer,'actual hide callback missing')()
            local held=s.target_alpha==1 and (modal_idle_arms or 0)>prior_arms
            s.native_menu_request,s.native_dialog_request,s.native_subtitle_dialog_request=nil,nil,nil
            s.controller_focus_name,s.pressed_control=nil,nil
            assert(modal_idle_hide,'owned timer did not re-arm')()
            mp.set_property('user-data/modal-interop',tostring(held)..':'..tostring(s.target_alpha==0))
            """, rawAss: true, resultProperty: "user-data/modal-interop", beforeScript: ModalInteropProbe,
            windowWidth: 1920, windowHeight: 1080, scriptOverride: ModalInteropSource);
        Assert.Equal("true:true", output);
    }

    [Theory]
    [InlineData("speed", "btn_quality")]
    [InlineData("subtitle", "btn_cc")]
    public void NativeModalInteropActiveUtilityRenderingRetainsNativeMenuState(string kind, string control)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s=osc_test_state
            s.osc_disabled,s.playback_loading,s.pause=false,false,false
            s.visible,s.current_alpha,s.target_alpha=true,1,1
            s.mouse_x,s.mouse_y,s.controller_focus_name=-1,-1,nil
            s.active_subtitle=-1
            s.stats_visible,s.quality_menu_visible=false,false
            s.native_menu_request=nil
            -- Ensure the real settings gear is laid out without depending on
            -- a Subtitle handler that is absent in the preserved red source.
            native_handlers['osc-native-quality-scope']('11111111111111111111111111111111')
            native_handlers['osc-native-speed-scope']('11111111111111111111111111111111')
            osc_test_layout();local r=assert(s.layout['{{control}}'])
            osc_test_render_hud()
            local normal,off_glyph,normal_dots=modal_region(s.osc_overlay.data,r)
            if '{{kind}}'=='stats' then s.stats_visible=true
            elseif '{{kind}}'=='speed' then s.native_menu_request={kind='quality',opener='btn_quality',sequence=1}
            else s.active_subtitle=0 end
            osc_test_render_hud()
            local expected,on_glyph,expected_dots=modal_region(s.osc_overlay.data,r)
            s.stats_visible,s.active_subtitle=false,-1
            s.native_menu_request={owner='11111111111111111111111111111111',sequence=2,kind='{{kind}}',opener='{{control}}'}
            osc_test_render_hud()
            local current,glyph,dots=modal_region(s.osc_overlay.data,r)
            local active=normal_dots==0 and expected_dots==1 and dots==1 and current~=normal
            local correct='{{kind}}'=='subtitle' and glyph==off_glyph and glyph~=on_glyph
                or '{{kind}}'~='subtitle' and current==expected
            mp.set_property('user-data/modal-interop',tostring(active)..':'..tostring(correct))
            """, rawAss: true, resultProperty: "user-data/modal-interop", beforeScript: ModalInteropProbe,
            windowWidth: 1920, windowHeight: 1080, scriptOverride: ModalInteropSource);
        Assert.Equal("true:true", output);
    }
}
