namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    [Theory]
    [InlineData("prompt", "activate", "silo-buffering-lower")]
    [InlineData("undo", "activate", "silo-buffering-restore")]
    [InlineData("prompt", "escape", "silo-buffering-keep")]
    [InlineData("undo", "escape", "silo-buffering-keep")]
    public void ExplicitBufferingControlsKeepTheirMeaningAfterAMoreUtility(string pill, string input, string intent)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled, s.playback_loading = false,false
            s.visible, s.current_alpha, s.target_alpha = true,1,1
            mp.set_property_number('volume',50)
            more_handlers['osc-native-more-scope']('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name='btn_more'; osc_test_activate()
            local request=assert(more_requests[1])
            more_handlers['osc-buffering-quality']('{{pill}}')
            more_handlers['osc-native-menu-closed'](request[3],request[4],'true')
            more_handlers['osc-native-more-action'](request[3],request[4],'volume-up')
            if '{{input}}' == 'escape' then more_keys['silo-menu-escape']() else osc_test_activate() end
            mp.set_property('user-data/more-review',tostring(mp.get_property_number('volume'))..':'..tostring(#more_intents)
                ..':'..tostring(more_intents[2] and more_intents[2][2]))
            """, rawAss: true, resultProperty: "user-data/more-review", beforeScript: NativeMoreProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal("60:2:" + intent, output);
    }

    [Theory]
    [InlineData("prompt", "volume-up")]
    [InlineData("undo", "volume-up")]
    [InlineData("prompt", "fill")]
    [InlineData("undo", "fill")]
    [InlineData("prompt", "chapters")]
    [InlineData("undo", "chapters")]
    public void MoreChosenUtilityCannotBecomeAnInterveningBufferingAction(string pill, string utility)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled, s.playback_loading = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.chapters = { {index=93,title='Finale',start_seconds=30,end_seconds=60} }
            mp.set_property_number('volume',50); mp.set_property_number('panscan',0)
            more_handlers['osc-native-more-scope']('11111111111111111111111111111111')
            more_handlers['osc-native-chapters-scope']('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name='btn_more'; osc_test_activate()
            local request = assert(more_requests[1])
            more_handlers['osc-buffering-quality']('{{pill}}')
            more_handlers['osc-native-menu-closed'](request[3],request[4],'true')
            more_handlers['osc-native-more-action'](request[3],request[4],'{{utility}}')
            local correct = '{{utility}}' == 'volume-up' and mp.get_property_number('volume') == 60
                or '{{utility}}' == 'fill' and mp.get_property_number('panscan') == 1
                or '{{utility}}' == 'chapters' and #more_requests == 2 and more_requests[2][2] == 'silo-native-chapters-open'
            local unrelated = 0
            for _, intent in ipairs(more_intents) do
                if intent[2]:match('^silo%-buffering%-') then unrelated = unrelated + 1 end
            end
            mp.set_property('user-data/more-review',tostring(correct)..':'..tostring(unrelated)..':'..tostring(s.buffering_quality_pill))
            """, rawAss: true, resultProperty: "user-data/more-review", beforeScript: NativeMoreProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal("true:0:" + pill, output);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("loading")]
    [InlineData("hidden")]
    [InlineData("postroll")]
    [InlineData("retired")]
    public void MoreLateUtilityIsConsumedWithoutActingOnAnUnavailablePlaybackSurface(string condition)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled, s.playback_loading = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            mp.set_property_number('volume',50)
            more_handlers['osc-native-more-scope']('11111111111111111111111111111111')
            osc_test_layout(); s.controller_focus_name='btn_more'; osc_test_activate()
            local request = assert(more_requests[1])
            more_handlers['osc-native-menu-closed'](request[3],request[4],'true')
            if '{{condition}}' == 'failure' then
                more_handlers['osc-show-playback-failure']('{"title":"Synthetic failure","message":"Synthetic","can_retry":true}')
            elseif '{{condition}}' == 'loading' then s.playback_loading = true
            elseif '{{condition}}' == 'hidden' then s.osc_disabled = true
            elseif '{{condition}}' == 'postroll' then s.post_roll_active = true
            else more_handlers['osc-native-more-scope']('22222222222222222222222222222222') end
            more_handlers['osc-native-more-action'](request[3],request[4],'volume-up')
            s.playback_failure_visible, s.playback_loading, s.osc_disabled, s.post_roll_active = false,false,false,false
            more_handlers['osc-native-more-action'](request[3],request[4],'volume-up')
            mp.set_property('user-data/more-review',tostring(mp.get_property_number('volume'))..':'..tostring(#more_intents))
            """, rawAss: true, resultProperty: "user-data/more-review", beforeScript: NativeMoreProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal("50:0", output);
    }

    [Theory]
    [InlineData("controller", "volume-up")]
    [InlineData("controller", "fill")]
    [InlineData("controller", "mute")]
    [InlineData("pointer", "volume-up")]
    [InlineData("pointer", "fill")]
    [InlineData("pointer", "mute")]
    public void MoreUtilityRestoresItsRealInputOriginWithoutLeavingAHiddenActionFocused(string route, string utility)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled, s.playback_loading = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            mp.set_property_number('volume',50); mp.set_property_number('panscan',0); mp.set_property('mute','no')
            more_handlers['osc-native-more-scope']('11111111111111111111111111111111')
            osc_test_layout()
            if '{{route}}' == 'pointer' then
                local r = s.layout.btn_more
                more_handlers['osc-mouse-down'](tostring(r.cx),tostring(r.cy))
            else s.controller_focus_name='btn_more'; osc_test_activate() end
            local request = assert(more_requests[1])
            more_handlers['osc-native-menu-closed'](request[3],request[4],'true')
            more_handlers['osc-native-more-action'](request[3],request[4],'{{utility}}')
            local restored = '{{route}}' == 'controller' and s.controller_focus_name == 'btn_more'
                or '{{route}}' == 'pointer' and s.controller_focus_name == nil
            local navigated, reopened, retained = true,true,true
            if '{{route}}' == 'controller' then
                more_handlers['osc-controller-nav']('right')
                navigated = s.controller_focus_name == 'btn_fullscreen'
                more_handlers['osc-controller-nav']('left')
                local volume, fill, mute = mp.get_property_number('volume'),mp.get_property_number('panscan'),mp.get_property_bool('mute')
                osc_test_activate()
                reopened = #more_requests == 2 and more_requests[2][2] == 'silo-native-more-open'
                retained = volume == mp.get_property_number('volume') and fill == mp.get_property_number('panscan') and mute == mp.get_property_bool('mute')
            end
            mp.set_property('user-data/more-review',tostring(restored)..':'..tostring(navigated)..':'..tostring(reopened)..':'..tostring(retained))
            """, rawAss: true, resultProperty: "user-data/more-review", beforeScript: NativeMoreProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal("true:true:true:true", output);
    }

    [Theory]
    [InlineData("pointer", "audio")]
    [InlineData("pointer", "chapters")]
    [InlineData("controller", "audio")]
    [InlineData("controller", "chapters")]
    public void MoreChildPresentationKeepsPointerOrControllerOrigin(string route, string kind)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled, s.playback_loading = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = { {index=0,title='English'}, {index=1,title='Japanese'} }
            s.chapters = { {index=93,title='Finale',start_seconds=30,end_seconds=60} }
            more_handlers['osc-native-more-scope']('11111111111111111111111111111111')
            more_handlers['osc-native-{{kind}}-scope']('11111111111111111111111111111111')
            osc_test_layout()
            if '{{route}}' == 'pointer' then
                local r=s.layout.btn_more; more_handlers['osc-mouse-down'](tostring(r.cx),tostring(r.cy))
            else s.controller_focus_name='btn_more'; osc_test_activate() end
            local parent = assert(more_requests[1])
            more_handlers['osc-native-menu-closed'](parent[3],parent[4],'true')
            more_handlers['osc-native-more-action'](parent[3],parent[4],'{{kind}}')
            local child = assert(more_requests[2])
            local focused = s.native_menu_request.focus_name
            local correct = '{{route}}' == 'pointer' and focused == nil or '{{route}}' == 'controller' and focused == 'btn_more'
            more_handlers['osc-native-menu-closed'](child[3],child[4],'true')
            local restored = '{{route}}' == 'pointer' and s.controller_focus_name == nil
                or '{{route}}' == 'controller' and s.controller_focus_name == 'btn_more'
            mp.set_property('user-data/more-review',tostring(correct)..':'..tostring(restored))
            """, rawAss: true, resultProperty: "user-data/more-review", beforeScript: NativeMoreProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal("true:true", output);
    }

    [Theory]
    [InlineData("audio", 1920, 1080, "true:false:false")]
    [InlineData("chapters", 1920, 1080, "false:true:false")]
    [InlineData("more", 460, 700, "false:false:true")]
    [InlineData("chapters", 460, 700, "false:false:true")]
    public void NativeMenuActivityIsDrawnOnItsActualHudOpener(string kind, int width, int height, string expected)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled, s.playback_loading, s.pause = false,false,false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.mouse_x,s.mouse_y,s.controller_focus_name = -1,-1,nil
            s.audio_tracks = { {index=0,title='English'}, {index=1,title='Japanese'} }
            s.chapters = { {index=93,title='Finale',start_seconds=30,end_seconds=60} }
            more_handlers['osc-native-{{kind}}-scope']('11111111111111111111111111111111')
            osc_test_layout()
            s.controller_focus_name = '{{kind}}' == 'more' and 'btn_more'
                or '{{kind}}' == 'audio' and 'btn_audio'
                or s.layout.btn_chapters and 'btn_chapters' or 'overflow_btn_chapters'
            osc_test_activate()
            osc_test_tick()
            local rendered = assert(s.osc_overlay.data)
            local function dot_at(name)
                local r=s.layout[name]
                if not r then return false end
                for offset=4,8 do
                    local needle=string.format('m %d %d b ',math.floor(r.cx),math.floor(r.y+r.h-offset))
                    if rendered:find(needle,1,true) then return true end
                end
                return false
            end
            mp.set_property('user-data/more-review',tostring(dot_at('btn_audio'))..':'..tostring(dot_at('btn_chapters'))..':'..tostring(dot_at('btn_more')))
            """, rawAss: true, resultProperty: "user-data/more-review", beforeScript: NativeMoreProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal(expected, output);
    }
}

