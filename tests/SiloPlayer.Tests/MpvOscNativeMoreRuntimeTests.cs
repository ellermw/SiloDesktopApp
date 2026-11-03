namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    private const string NativeMoreProbe = """
        more_handlers, more_requests, more_intents = {}, {}, {}
        local register = mp.register_script_message
        mp.register_script_message = function(name, callback)
            more_handlers[name] = callback
            return register(name, callback)
        end
        local command = mp.commandv
        mp.commandv = function(...)
            local args = {...}
            if args[1] == 'script-message' and args[2]:match('^silo%-native%-.+%-open$') then
                table.insert(more_requests, args); return
            end
            if args[1] == 'script-message' and (args[2] == 'silo-volume-changed' or args[2] == 'silo-pause-toggle'
                or args[2] == 'silo-pip-toggle' or args[2] == 'silo-seek-absolute') then
                table.insert(more_intents, args); return
            end
            return command(...)
        end
        """;

    [Theory]
    [InlineData("pointer")]
    [InlineData("controller")]
    public void NativeMoreOwnsTheLiveCompactOpenerWithoutOpeningLegacyOverflow(string route)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled = false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            if more_handlers['osc-native-more-scope'] then more_handlers['osc-native-more-scope']('11111111111111111111111111111111') end
            osc_test_layout()
            if '{{route}}' == 'pointer' then
                local r = s.layout.btn_more
                more_handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            else
                s.controller_focus_name = 'btn_more'; osc_test_activate()
            end
            local request = more_requests[1]
            mp.set_property('user-data/native-more-result',tostring(#more_requests)..':'..tostring(s.compact_menu_visible)
                ..':'..tostring(request and request[2] == 'silo-native-more-open')..':'..tostring(#more_intents))
            """, rawAss: true, resultProperty: "user-data/native-more-result", beforeScript: NativeMoreProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal("1:false:true:0", output);
    }

    [Fact]
    public void NativeMoreActionRequiresItsOwnClosedSequenceAndIsConsumedOnce()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local scope, action = more_handlers['osc-native-more-scope'], more_handlers['osc-native-more-action']
            if not scope or not action then mp.set_property('user-data/native-more-result','unsupported'); return end
            local s = osc_test_state
            s.osc_disabled = false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            mp.set_property_number('volume', 50)
            scope('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name='btn_more'; osc_test_activate()
            local request = more_requests[1]
            action(request[3],request[4],'volume-up')
            local blocked_open = mp.get_property_number('volume') == 50
            more_handlers['osc-native-menu-closed'](request[3],request[4],'true')
            action(request[3],'999','volume-up')
            local blocked_wrong = mp.get_property_number('volume') == 50
            action(request[3],request[4],'volume-up')
            local accepted = mp.get_property_number('volume')
            action(request[3],request[4],'volume-up')
            mp.set_property('user-data/native-more-result',tostring(blocked_open)..':'..tostring(blocked_wrong)
                ..':'..tostring(accepted)..':'..tostring(mp.get_property_number('volume'))..':'..tostring(#more_intents))
            """, rawAss: true, resultProperty: "user-data/native-more-result", beforeScript: NativeMoreProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal("true:true:60:60:1", output);
    }

    [Theory]
    [InlineData("audio", "silo-native-audio-open")]
    [InlineData("chapters", "silo-native-chapters-open")]
    public void NativeMoreHandoffCannotBeUnlockedByTheParentsLateClose(string kind, string message)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local scope, action = more_handlers['osc-native-more-scope'], more_handlers['osc-native-more-action']
            if not scope or not action then mp.set_property('user-data/native-more-result','unsupported'); return end
            local s = osc_test_state
            s.osc_disabled = false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = { {index=0,title='English'}, {index=1,title='Japanese'} }
            s.chapters = { {index=93,title='Finale',start_seconds=30,end_seconds=60} }
            scope('11111111111111111111111111111111')
            more_handlers['osc-native-{{kind}}-scope']('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name='btn_more'; osc_test_activate()
            local parent = more_requests[1]
            more_handlers['osc-native-menu-closed'](parent[3],parent[4],'true')
            action(parent[3],parent[4],'{{kind}}')
            local child = more_requests[2]
            more_handlers['osc-native-menu-closed'](parent[3],parent[4],'true')
            action(parent[3],parent[4],'volume-up')
            local retained = child and s.native_menu_request and tonumber(child[4]) == s.native_menu_request.sequence
            mp.set_property('user-data/native-more-result',tostring(#more_requests)..':'..tostring(child and child[2])..':'..tostring(retained)..':'..tostring(#more_intents))
            """, rawAss: true, resultProperty: "user-data/native-more-result", beforeScript: NativeMoreProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal("2:" + message + ":true:0", output);
    }

    [Theory]
    [InlineData("fill", "true")]
    [InlineData("mute", "true")]
    [InlineData("stats", "true")]
    [InlineData("pip", "silo-pip-toggle")]
    public void NativeMoreActionsUseTheExistingLiveControlPolicies(string actionId, string expected)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local scope, action = more_handlers['osc-native-more-scope'], more_handlers['osc-native-more-action']
            if not scope or not action then mp.set_property('user-data/native-more-result','unsupported'); return end
            local s = osc_test_state
            s.osc_disabled = false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            mp.set_property_number('panscan',0); mp.set_property('mute','no')
            scope('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name='btn_more'; osc_test_activate()
            local request = more_requests[1]
            more_handlers['osc-native-menu-closed'](request[3],request[4],'true')
            action(request[3],request[4],'{{actionId}}')
            local observed = '{{actionId}}' == 'fill' and tostring(mp.get_property_number('panscan') > 0)
                or '{{actionId}}' == 'mute' and tostring(mp.get_property_bool('mute'))
                or '{{actionId}}' == 'stats' and tostring(s.stats_visible)
                or more_intents[1] and more_intents[1][2] or 'missing'
            mp.set_property('user-data/native-more-result',observed)
            """, rawAss: true, resultProperty: "user-data/native-more-result", beforeScript: NativeMoreProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal(expected, output);
    }
}

