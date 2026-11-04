namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    [Fact]
    public void PointerSettingsHandoffRetainsPointerOrigin()
    {
        var output=CaptureStats(1920,1080,1920,1080,false,setup:"""
            local s=osc_test_state
            s.osc_disabled=false;s.visible=true;s.current_alpha=1;s.target_alpha=1
            local token='11111111111111111111111111111111'
            native_handlers['osc-native-quality-scope'](token);native_handlers['osc-native-speed-scope'](token)
            osc_test_layout();local r=s.layout.btn_quality
            native_handlers['osc-mouse-down'](tostring(r.cx),tostring(r.cy))
            local first=native_requests[1]
            native_handlers['osc-native-menu-closed'](token,first[4],'true')
            native_handlers['osc-native-speed-handoff'](token,first[4])
            local child=s.native_menu_request
            native_handlers['osc-native-menu-closed'](token,tostring(child.sequence),'false')
            mp.set_property('user-data/speed-result',tostring(#native_requests)..':'..tostring(child.kind)..':'..tostring(child.focus_name==nil)..':'..tostring(s.controller_focus_name==nil))
            """,rawAss:true,resultProperty:"user-data/speed-result",beforeScript:NativeSpeedProbe);
        Assert.Equal("2:speed:true:true",output);
    }

    [Fact]
    public void BlockedSpeedHandoffConsumesItsExactClosedCapability()
    {
        var output=CaptureStats(1920,1080,1920,1080,false,setup:"""
            local s=osc_test_state
            s.osc_disabled=false;s.visible=true;s.current_alpha=1;s.target_alpha=1
            local token='11111111111111111111111111111111'
            native_handlers['osc-native-quality-scope'](token);native_handlers['osc-native-speed-scope'](token)
            osc_test_layout();s.controller_focus_name='btn_quality';osc_test_activate()
            local first=native_requests[1]
            native_handlers['osc-native-menu-closed'](token,first[4],'false')
            s.playback_loading=true;native_handlers['osc-native-speed-handoff'](token,first[4])
            s.playback_loading=false;native_handlers['osc-native-speed-handoff'](token,first[4])
            mp.set_property('user-data/speed-result',tostring(#native_requests)..':'..tostring(s.native_menu_request==nil))
            """,rawAss:true,resultProperty:"user-data/speed-result",beforeScript:NativeSpeedProbe);
        Assert.Equal("1:true",output);
    }

    [Fact]
    public void ClosedAudioRequestCannotAuthorizeSpeedHandoff()
    {
        var output=CaptureStats(1920,1080,1920,1080,false,setup:"""
            local s=osc_test_state
            s.osc_disabled=false;s.visible=true;s.current_alpha=1;s.target_alpha=1
            s.audio_tracks={{index=0,title='A'},{index=1,title='B'}}
            local token='11111111111111111111111111111111'
            native_handlers['osc-native-quality-scope'](token);native_handlers['osc-native-speed-scope'](token);native_handlers['osc-native-audio-scope'](token)
            osc_test_layout();s.controller_focus_name='btn_audio';osc_test_activate()
            local request=s.native_menu_request
            native_handlers['osc-native-menu-closed'](token,tostring(request.sequence),'false')
            native_handlers['osc-native-speed-handoff'](token,tostring(request.sequence))
            mp.set_property('user-data/speed-result',tostring(#native_requests)..':'..tostring(s.native_menu_request==nil))
            """,rawAss:true,resultProperty:"user-data/speed-result",beforeScript:NativeSpeedProbe);
        Assert.Equal("0:true",output);
    }

    private static readonly string NativeSpeedProbe = NativeAudioProbe.Replace("args[2] == 'silo-native-audio-open'",
        "(args[2] == 'silo-native-quality-open' or args[2] == 'silo-native-speed-open')") + "\n" + """
        speed_unrelated={}
        local speed_command=mp.commandv
        mp.commandv=function(...)
            local args={...}
            if args[1]=='script-message' and (args[2]=='silo-buffering-keep' or args[2]=='silo-playback-retry') then
                table.insert(speed_unrelated,args); return
            end
            return speed_command(...)
        end
        """;

    [Theory]
    [InlineData("pointer", 1920, 1080)]
    [InlineData("controller", 1920, 1080)]
    [InlineData("pointer", 460, 700)]
    public void SpeedKeepsTheRealSettingsGearReachableWithoutQualityOptions(string route, int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s=osc_test_state
            s.osc_disabled=false; s.visible=true; s.current_alpha=1; s.target_alpha=1; s.quality_info=nil
            native_handlers['osc-native-quality-scope']('11111111111111111111111111111111')
            native_handlers['osc-native-speed-scope']('11111111111111111111111111111111')
            osc_test_layout()
            local r=s.layout.btn_quality
            if not r then mp.set_property('user-data/speed-result','no-gear'); return end
            if '{{route}}'=='pointer' then native_handlers['osc-mouse-down'](tostring(r.cx),tostring(r.cy))
            else s.controller_focus_name='btn_quality'; osc_test_activate() end
            local request=native_requests[1]
            mp.set_property('user-data/speed-result',tostring(request and request[2]=='silo-native-quality-open')..':'..
                tostring(request and #request==10)..':'..tostring(#native_intents))
            """, rawAss: true, resultProperty: "user-data/speed-result", beforeScript: NativeSpeedProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("true:true:0", output);
    }

    [Fact]
    public void SpeedHandoffUsesExactCurrentSettingsRequestAndRestoresItsGear()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s=osc_test_state
            s.osc_disabled=false; s.visible=true; s.current_alpha=1; s.target_alpha=1
            local token='11111111111111111111111111111111'
            native_handlers['osc-native-quality-scope'](token); native_handlers['osc-native-speed-scope'](token)
            local handoff=native_handlers['osc-native-speed-handoff']
            if not handoff then mp.set_property('user-data/speed-result','unsupported'); return end
            osc_test_layout(); s.controller_focus_name='btn_quality'; osc_test_activate()
            local first=native_requests[1]
            handoff(token,first[4]); local blocked=#native_requests==1
            native_handlers['osc-native-menu-closed'](token,first[4],'false')
            handoff('22222222222222222222222222222222',first[4]); handoff(token,'99999')
            local stale=#native_requests==1
            handoff(token,first[4]); local second=native_requests[2]
            local anchor=second and second[5]==first[5] and second[6]==first[6] and second[7]==first[7]
            local fresh=second and tonumber(second[4])>tonumber(first[4]) and second[2]=='silo-native-speed-open'
            native_handlers['osc-native-menu-closed'](token,second[4],'false')
            handoff(token,first[4]); local retired=#native_requests==2
            mp.set_property('user-data/speed-result',tostring(blocked)..':'..tostring(stale)..':'..tostring(anchor)..':'..
                tostring(fresh)..':'..tostring(s.controller_focus_name=='btn_quality')..':'..tostring(retired)..':'..tostring(#native_intents))
            """, rawAss: true, resultProperty: "user-data/speed-result", beforeScript: NativeSpeedProbe);
        Assert.Equal("true:true:true:true:true:true:0", output);
    }

    [Theory]
    [InlineData("buffering", "1:0")]
    [InlineData("failure", "0:0")]
    public void SettingsIntentWithSpeedNeverDispatchesUnrelatedPriorityAction(string overlay, string expected)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s=osc_test_state
            s.osc_disabled=false; s.visible=true; s.current_alpha=1; s.target_alpha=1
            native_handlers['osc-native-quality-scope']('11111111111111111111111111111111')
            native_handlers['osc-native-speed-scope']('11111111111111111111111111111111')
            osc_test_layout()
            if '{{overlay}}'=='buffering' then
                s.buffering_quality_pill=true; s.buffering_quality_actions={ {action='keep'} }; s.buffering_quality_focus=1
            else s.playback_failure_visible=true; s.playback_failure_can_retry=true; s.playback_failure_focus='retry' end
            s.controller_focus_name='btn_quality'; osc_test_activate()
            mp.set_property('user-data/speed-result',tostring(#native_requests)..':'..tostring(#speed_unrelated))
            """, rawAss: true, resultProperty: "user-data/speed-result", beforeScript: NativeSpeedProbe);
        Assert.Equal(expected, output);
    }
}
