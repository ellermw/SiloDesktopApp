namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    private const string PartyRenderProbe = PartyProbe + "\n" + """
        party_frame = ''
        local create = mp.create_osd_overlay
        mp.create_osd_overlay = function(kind)
            local overlay = create(kind)
            local update = overlay.update
            overlay.update = function(self)
                if self.data and self.data:find('WATCH PARTY',1,true) then party_frame = self.data end
                return update(self)
            end
            return overlay
        end
        """;

    [Theory]
    [InlineData(true, 1920, 1080, "Only you control playback")]
    [InlineData(true, 460, 700, "Only you control playback")]
    [InlineData(false, 1920, 1080, "Host controls playback")]
    public void NativePartyCardRendersPolicyAndOnlyItsNativeHitTarget(bool host, int width, int height, string policy)
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time=10
            local s=osc_test_state
            s.osc_disabled=false; s.visible=true; s.current_alpha=1; s.target_alpha=1
            s.watch_party={visible=true,is_host={{(host ? "true" : "false")}},code='TEST1234',member_count=2,connection_state='connected'}
            native_handlers['osc-native-party-scope']('11111111111111111111111111111111')
            osc_test_layout(); osc_test_tick()
            local legacy=0
            for _,item in ipairs(s.watch_party_actions) do if item.action~='native-party' then legacy=legacy+1 end end
            local policy=party_frame:find('{{policy}}',1,true)~=nil
            local card=s.layout.btn_party
            local old_end_x=card.x+card.w-32
            local old_end_y=card.y+106
            native_handlers['osc-mouse-down'](tostring(old_end_x),tostring(old_end_y))
            native_handlers['osc-mouse-up'](tostring(old_end_x),tostring(old_end_y))
            native_handlers['osc-video-click'](tostring(old_end_x),tostring(old_end_y))
            native_probe_time=11
            native_handlers['osc-mouse-down']('100','100')
            native_handlers['osc-mouse-up']('100','100')
            native_handlers['osc-video-click']('100','100')
            mp.set_property('user-data/party-review', tostring(legacy)..':'..tostring(policy)..':'..tostring(s.watch_party_end_confirm)..':'..tostring(#party_actions)..':'..tostring(#native_intents>0))
            """, rawAss:true, resultProperty:"user-data/party-review", beforeScript:PartyRenderProbe,
            windowWidth:width,windowHeight:height,scriptOverride:Environment.GetEnvironmentVariable("SILO_PARTY_REVIEW_OSC"));
        Assert.Equal("0:true:false:0:true",result);
    }

    [Fact]
    public void AdoptingNativePartyCapabilityRetiresLegacyConfirmationBeforeTheNextPaint()
    {
        var result=CaptureStats(1920,1080,1920,1080,false,setup:"""
            native_probe_time=10
            local s=osc_test_state
            s.osc_disabled=false; s.visible=true; s.current_alpha=1; s.target_alpha=1
            s.watch_party={visible=true,is_host=true,code='TEST1234',member_count=2,connection_state='connected'}
            osc_test_layout(); osc_test_tick()
            local ending
            for _,item in ipairs(s.watch_party_actions) do if item.action=='end' then ending=item end end
            assert(ending,'Legacy End control was not arranged')
            native_handlers['osc-mouse-down'](tostring(ending.x+ending.w/2),tostring(ending.y+ending.h/2))
            assert(s.watch_party_end_confirm,'Legacy confirmation did not open')
            native_handlers['osc-native-party-scope']('11111111111111111111111111111111')
            local cleared=not s.watch_party_end_confirm and #s.watch_party_actions==0
            native_probe_time=11
            native_handlers['osc-mouse-down']('100','100')
            native_handlers['osc-mouse-up']('100','100')
            native_handlers['osc-video-click']('100','100')
            mp.set_property('user-data/party-review',tostring(cleared)..':'..tostring(#party_actions)..':'..tostring(#native_intents))
            """,rawAss:true,resultProperty:"user-data/party-review",beforeScript:PartyRenderProbe,
            scriptOverride:Environment.GetEnvironmentVariable("SILO_PARTY_REVIEW_OSC"));
        Assert.Equal("true:0:1",result);
    }
}
