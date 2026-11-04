namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    private static string NativeSubtitleProbe => NativeAudioProbe.Replace("args[2] == 'silo-native-audio-open'", "args[2] == 'silo-native-subtitle-open'");

    [Theory]
    [InlineData("pointer", 1920, 1080)]
    [InlineData("controller", 1920, 1080)]
    [InlineData("compact", 460, 700)]
    public void NativeSubtitleOpensFromEveryActualCcEntryEvenWithoutTracks(string route, int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.subtitle_tracks = {}
            local scope = native_handlers['osc-native-subtitle-scope']
            if not scope then mp.set_property('user-data/native-subtitle-result','unsupported'); return end
            scope('11111111111111111111111111111111')
            osc_test_layout()
            if '{{route}}' == 'pointer' then
                local r = s.layout.btn_cc
                native_handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            elseif '{{route}}' == 'controller' then
                s.controller_focus_name = 'btn_cc'; osc_test_activate()
            else
                s.controller_focus_name = 'btn_more'; osc_test_activate()
                s.controller_focus_name = 'overflow_btn_cc'; osc_test_activate()
            end
            local request = native_requests[1]
            local scoped = request and request[3] == '11111111111111111111111111111111'
                and tonumber(request[4]) > 0 and tonumber(request[7]) > 0
            mp.set_property('user-data/native-subtitle-result', tostring(#native_requests)..':'..tostring(s.subtitle_menu_visible)..':'..tostring(scoped))
            """, rawAss: true, resultProperty: "user-data/native-subtitle-result", beforeScript: NativeSubtitleProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("1:false:true", output);
    }

    [Fact]
    public void RevokingAnotherMenuKindCannotReleaseSubtitleInputOrResetItsSequence()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            local subtitle = native_handlers['osc-native-subtitle-scope']
            if not subtitle then mp.set_property('user-data/native-subtitle-result','unsupported'); return end
            local token = '11111111111111111111111111111111'
            native_handlers['osc-native-audio-scope'](token); subtitle(token)
            osc_test_layout(); s.controller_focus_name = 'btn_cc'; osc_test_activate()
            local request = assert(native_requests[1])
            native_handlers['osc-native-audio-scope']('')
            local retained = s.native_menu_request ~= nil
            native_handlers['osc-video-click']('100','100')
            local blocked = #native_intents == 0
            native_handlers['osc-native-menu-closed'](token, request[4], 'true')
            s.controller_focus_name = 'btn_cc'; osc_test_activate()
            local successor = native_requests[2]
            mp.set_property('user-data/native-subtitle-result', tostring(retained)..':'..tostring(blocked)..':'..tostring(successor and tonumber(successor[4]) > tonumber(request[4])))
            """, rawAss: true, resultProperty: "user-data/native-subtitle-result", beforeScript: NativeSubtitleProbe);
        Assert.Equal("true:true:true", output);
    }

    [Fact]
    public void NativeSubtitleDialogHandoffRestoresOnlyItsMatchingCurrentOpener()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time = 10
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            local opening = native_handlers['osc-native-subtitle-dialog-opening']
            if not opening then mp.set_property('user-data/native-subtitle-result','unsupported'); return end
            local token = '11111111111111111111111111111111'
            native_handlers['osc-native-subtitle-scope'](token)
            osc_test_layout(); s.controller_focus_name = 'btn_cc'; osc_test_activate()
            local request = native_requests[1]
            opening(token, request[4])
            native_handlers['osc-native-menu-closed'](token, request[4], 'true')
            local deferred = s.controller_focus_name == nil
            native_handlers['osc-subtitle-dialog-closed'](token, request[4], 'true')
            local restored = s.controller_focus_name == 'btn_cc'
            s.controller_focus_name = nil
            native_handlers['osc-native-subtitle-scope']('22222222222222222222222222222222')
            native_handlers['osc-subtitle-dialog-closed'](token, request[4], 'true')
            mp.set_property('user-data/native-subtitle-result', tostring(deferred)..':'..tostring(restored)..':'..tostring(s.controller_focus_name == nil))
            """, rawAss: true, resultProperty: "user-data/native-subtitle-result", beforeScript: NativeSubtitleProbe);
        Assert.Equal("true:true:true", output);
    }

    [Fact]
    public void NativeMenuProtocolPreservesSubtitleKindAndGeometry()
    {
        Assert.True(SiloPlayer.Services.PlaybackMenus.PlaybackMenuRequest.TryParse(
            ["silo-native-subtitle-open", "11111111111111111111111111111111", "1", "10", "20", "30", "40", "100", "100"], out var request));
        Assert.Equal(.4, request!.Anchor.Right);
    }
}
