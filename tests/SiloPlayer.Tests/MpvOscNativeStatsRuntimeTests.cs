namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void StatsRetainsReadonlyFallbackDuringLoadingAndPostroll(bool loading, bool hostToggle)
    {
        var setup = """
            native_probe_time=10
            local s=osc_test_state
            s.osc_disabled,s.visible,s.pause=false,true,true
            s.current_alpha,s.target_alpha=1,1
            native_handlers['osc-native-stats-scope']('11111111111111111111111111111111')
            s.playback_loading=LOADING
            s.post_roll_active=not LOADING
            local toggle=HOST and native_handlers['osc-toggle-stats'] or native_keys['silo-osc-toggle-stats']
            toggle()
            local opened=s.stats_visible and s.native_menu_request==nil
            toggle()
            mp.set_property('user-data/native-stats-result',tostring(opened)..':'..tostring(s.stats_visible)..':'..tostring(#native_requests)..':'..tostring(#native_intents))
            """.Replace("LOADING", loading ? "true" : "false").Replace("HOST", hostToggle ? "true" : "false");
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: setup, rawAss: true,
            resultProperty: "user-data/native-stats-result",
            beforeScript: NativeAudioProbe.Replace("== 'silo-native-audio-open'", "== 'silo-native-stats-open'"));
        Assert.Equal("true:false:0:0", output);
    }

    [Fact]
    public void NativeStatsPublishesDisplayedSectionsOnlyWhileTheMatchingPresentationIsOpen()
    {
        var probe = NativeAudioProbe.Replace("== 'silo-native-audio-open'", "== 'silo-native-stats-open'") + """

            native_stats_samples = {}
            local native_stats_command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1]=='script-message' and args[2]=='silo-native-stats-data' then
                    table.insert(native_stats_samples,args); return
                end
                return native_stats_command(...)
            end
            """;
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            native_probe_time=10
            local s=osc_test_state
            s.osc_disabled,s.visible,s.pause=false,true,true
            s.current_alpha,s.target_alpha=1,1
            local scope=native_handlers['osc-native-stats-scope']
            if not scope then mp.set_property('user-data/native-stats-result','unsupported'); return end
            scope('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name='btn_stats'; osc_test_activate()
            local request=assert(native_requests[1])
            osc_test_render_stats()
            local first=native_stats_samples[#native_stats_samples]
            local sections=first and require('mp.utils').parse_json(first[5]) or {}
            local fields=sections[1] and sections[1].rows or {}
            local routed=first and first[3]==request[3] and first[4]==request[4]
            native_keys['silo-osc-toggle-stats']()
            local close=native_inputs[#native_inputs]
            local toggle_closes=close and close[3]==request[3] and close[4]==request[4] and close[5]=='cancel'
            native_handlers['osc-native-menu-closed'](request[3],request[4],'false')
            local count=#native_stats_samples
            osc_test_render_stats()
            mp.set_property('user-data/native-stats-result',tostring(routed)..':'..tostring(toggle_closes)..':'..tostring(#sections)..':'..tostring(fields[1] and fields[1].label)..':'..tostring(count==#native_stats_samples)..':'..tostring(s.stats_visible)..':'..tostring(#native_intents))
            """, rawAss: true, resultProperty: "user-data/native-stats-result", beforeScript: probe);
        Assert.Equal("true:true:4:Player:true:false:0", output);
    }
}
