namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    [Theory]
    [InlineData("pointer", false)]
    [InlineData("controller", true)]
    public void MoreStatsDispatchForwardsItsOriginalInputMode(string route, bool keyboard)
    {
        var probe = NativeMoreProbe + """

            local original_loadstring = loadstring
            loadstring = function(code, ...)
                if code:find('local function activate_utility_control', 1, true) then
                    code = code .. '\n toggle_stats = function(keyboard) more_stats_origin = keyboard; more_stats_calls = (more_stats_calls or 0) + 1 end'
                end
                return original_loadstring(code, ...)
            end
            """;
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled, s.playback_loading = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            more_handlers['osc-native-more-scope']('11111111111111111111111111111111')
            osc_test_layout()
            if '{{route}}' == 'pointer' then
                local r = s.layout.btn_more
                more_handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            else s.controller_focus_name = 'btn_more'; osc_test_activate() end
            local request = assert(more_requests[1])
            more_handlers['osc-native-menu-closed'](request[3], request[4], 'true')
            more_handlers['osc-native-more-action'](request[3], request[4], 'stats')
            mp.set_property('user-data/more-stats-origin', tostring(more_stats_calls) .. ':' .. tostring(more_stats_origin))
            """, rawAss: true, resultProperty: "user-data/more-stats-origin", beforeScript: probe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal("1:" + (keyboard ? "true" : "false"), output);
    }
}
