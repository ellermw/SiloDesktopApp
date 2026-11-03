namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    private const string PartyProbe = NativeAudioProbe + "\n" + """
        party_requests, party_actions = {}, {}
        local party_command = mp.commandv
        mp.commandv = function(...)
            local args = {...}
            if args[1] == 'script-message' and args[2] == 'silo-native-party-open' then table.insert(party_requests,args); return end
            if args[1] == 'script-message' and args[2] == 'silo-watch-party-action' then table.insert(party_actions,args); return end
            return party_command(...)
        end
        """;

    [Theory]
    [InlineData("pointer", 1920, 1080)]
    [InlineData("controller", 1920, 1080)]
    [InlineData("pointer", 460, 700)]
    public void NativePartyOpenerUsesCommonScopedInputWithoutRoomMutation(string route, int width, int height)
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.watch_party = { visible=true, code='TEST1234', member_count=2, is_host=true, connection_state='connected' }
            local scope = native_handlers['osc-native-party-scope']
            if not scope then mp.set_property('user-data/party-result','unsupported'); return end
            scope('11111111111111111111111111111111'); osc_test_layout()
            local r = s.layout.btn_party
            if not r then mp.set_property('user-data/party-result','no-opener'); return end
            if '{{route}}' == 'controller' then s.controller_focus_name = 'btn_party'; osc_test_activate()
            else native_handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy)) end
            local request = party_requests[1]
            local bounded = request and #request == 10 and request[3] == '11111111111111111111111111111111'
                and tonumber(request[5]) >= 0 and tonumber(request[6]) >= 0
                and tonumber(request[5])+tonumber(request[7]) <= tonumber(request[9])
            native_handlers['osc-video-click']('100','100')
            native_keys['silo-osc-wheel-down']()
            mp.set_property('user-data/party-result', tostring(#party_requests)..':'..tostring(bounded)..':'..tostring(#party_actions)..':'..tostring(#native_intents))
            """, rawAss: true, resultProperty: "user-data/party-result", beforeScript: PartyProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("1:true:0:0", result);
    }

    [Fact]
    public void NativePartyConfirmationRestoresOnlyItsExactCurrentOpener()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            s.osc_disabled=false; s.visible=true; s.current_alpha=1; s.target_alpha=1
            s.watch_party={visible=true,is_host=true,code='TEST1234'}
            native_handlers['osc-native-party-scope']('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name='btn_party'; osc_test_activate()
            local request=party_requests[1]
            native_handlers['osc-native-dialog-opening'](request[3],request[4])
            native_handlers['osc-native-menu-closed'](request[3],request[4],'true')
            local held=s.controller_focus_name==nil and s.native_dialog_request~=nil
            native_handlers['osc-native-dialog-closed'](request[3],'9999','true')
            local stale=s.controller_focus_name==nil and s.native_dialog_request~=nil
            native_handlers['osc-native-dialog-closed'](request[3],request[4],'true')
            local restored=s.controller_focus_name=='btn_party' and s.native_dialog_request==nil
            osc_test_activate(); request=party_requests[2]
            native_handlers['osc-native-dialog-opening'](request[3],request[4])
            native_handlers['osc-native-menu-closed'](request[3],request[4],'true')
            native_handlers['osc-native-party-scope']('22222222222222222222222222222222')
            s.controller_focus_name='btn_play'
            native_handlers['osc-native-dialog-closed'](request[3],request[4],'true')
            mp.set_property('user-data/party-result',tostring(held)..':'..tostring(stale)..':'..tostring(restored)..':'..tostring(s.controller_focus_name=='btn_play'))
            """, rawAss: true, resultProperty: "user-data/party-result", beforeScript: PartyProbe);
        Assert.Equal("true:true:true:true", result);
    }

    [Fact]
    public void NativePartyScopeDepartureRemovesOpenerAndPendingHitTarget()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s=osc_test_state
            s.osc_disabled=false; s.visible=true; s.current_alpha=1; s.target_alpha=1
            s.watch_party={visible=true,is_host=false,code='TEST1234'}
            native_handlers['osc-native-party-scope']('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name='btn_party'; osc_test_activate()
            native_handlers['osc-native-party-scope']('')
            s.watch_party=nil; osc_test_layout(); osc_test_tick()
            mp.set_property('user-data/party-result',tostring(s.native_menu_request==nil)..':'..tostring(s.layout.btn_party==nil)..':'..tostring(#party_actions))
            """, rawAss: true, resultProperty: "user-data/party-result", beforeScript: PartyProbe);
        Assert.Equal("true:true:0", result);
    }
}
