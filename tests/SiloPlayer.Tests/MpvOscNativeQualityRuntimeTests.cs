namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    [Fact]
    public void NativeQualityArrivalRecomputesTheExistingHudWithoutRequiringResize()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s=osc_test_state
            s.quality_info=nil
            osc_test_layout()
            local before=s.layout.btn_quality==nil
            native_handlers['osc-set-quality-info']('{"versions":[{"file_id":41},{"file_id":71}],"qualities":[{"id":"auto"},{"id":"720p"}]}')
            osc_test_layout()
            mp.set_property('user-data/native-quality-result',tostring(before)..':'..tostring(s.layout.btn_quality~=nil))
            """, rawAss:true, resultProperty:"user-data/native-quality-result", beforeScript:NativeAudioProbe);
        Assert.Equal("true:true", output);
    }

    [Theory]
    [InlineData("pointer")]
    [InlineData("controller")]
    public void NativeQualityOpensScopedHostWithoutLegacyOverlayOrPlaybackIntent(string route)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause = false, true
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            native_handlers['osc-set-quality-info']('{"versions":[{"file_id":41,"label":"1080p"},{"file_id":71,"label":"4K"}],"active_file_id":41,"active_quality":"auto","qualities":[{"id":"auto","label":"Auto"},{"id":"720p","label":"720p"}]}')
            local scope = native_handlers['osc-native-quality-scope']
            if not scope then mp.set_property('user-data/native-quality-result','unsupported'); return end
            scope('11111111111111111111111111111111')
            osc_test_layout()
            if '{{route}}' == 'pointer' then
                local r = assert(s.layout.btn_quality)
                native_handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            else
                s.controller_focus_name = 'btn_quality'; osc_test_activate()
            end
            local request = native_requests[1]
            local scoped = request and request[3] == '11111111111111111111111111111111'
                and tonumber(request[4]) > 0 and tonumber(request[7]) > 0
            mp.set_property('user-data/native-quality-result', tostring(#native_requests)..':'..tostring(s.quality_menu_visible)..':'..tostring(scoped)..':'..tostring(#native_intents)..':'..tostring(s.pause))
            """, rawAss: true, resultProperty: "user-data/native-quality-result",
            beforeScript: NativeAudioProbe.Replace("== 'silo-native-audio-open'", "== 'silo-native-quality-open'"));
        Assert.Equal("1:false:true:0:true", output);
    }
}
