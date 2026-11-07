namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    private const string NativeChapterProbe = """
        chapter_handlers, chapter_requests, chapter_intents, chapter_keys = {}, {}, {}, {}
        local register = mp.register_script_message
        mp.register_script_message = function(name, callback)
            chapter_handlers[name] = callback
            return register(name, callback)
        end
        local key = mp.add_key_binding
        mp.add_key_binding = function(binding, name, callback, flags)
            chapter_keys[name] = callback
            return key(binding, name, callback, flags)
        end
        local command = mp.commandv
        mp.commandv = function(...)
            local args = {...}
            if args[1] == 'script-message' and args[2] == 'silo-native-chapters-open' then
                table.insert(chapter_requests, args); return
            end
            if args[1] == 'script-message' and (args[2] == 'silo-pause-toggle'
                or args[2] == 'silo-seek-absolute' or args[2] == 'silo-volume-changed') then
                table.insert(chapter_intents, args); return
            end
            return command(...)
        end
        """;

    [Theory]
    [InlineData("pointer", 1920, 1080)]
    [InlineData("controller", 1920, 1080)]
    [InlineData("compact", 460, 700)]
    public void NativeChapterInventoryOpensScopedHostThroughEveryLiveOpener(string route, int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            s.osc_disabled, s.pause = false, true
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.chapters = { {index=4,title='Opening',start_seconds=0,end_seconds=30},
                {index=93,title='Finale',start_seconds=30,end_seconds=60} }
            if chapter_handlers['osc-native-chapters-scope'] then
                chapter_handlers['osc-native-chapters-scope']('11111111111111111111111111111111')
            end
            osc_test_layout()
            if '{{route}}' == 'pointer' then
                local r = s.layout.btn_chapters
                chapter_handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            elseif '{{route}}' == 'controller' then
                s.controller_focus_name = 'btn_chapters'; osc_test_activate()
            else
                s.controller_focus_name = 'btn_more'; osc_test_activate()
                s.controller_focus_name = 'overflow_btn_chapters'; osc_test_activate()
            end
            local request = chapter_requests[1]
            local scoped = request and request[3] == '11111111111111111111111111111111'
                and tonumber(request[4]) > 0 and tonumber(request[7]) > 0 and tonumber(request[8]) > 0
            mp.set_property('user-data/native-chapter-result',
                tostring(#chapter_requests)..':'..tostring(s.chapter_menu_visible)..':'..tostring(scoped)..':'..tostring(#chapter_intents))
            """, rawAss: true, resultProperty: "user-data/native-chapter-result", beforeScript: NativeChapterProbe,
            windowWidth: width, windowHeight: height);
        Assert.Equal("1:false:true:0", output);
    }

    [Fact]
    public void NativeChaptersCannotOpenWithoutAChapterOrWhileAnotherRequestOwnsInput()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local scope = chapter_handlers['osc-native-chapters-scope']
            if not scope then mp.set_property('user-data/native-chapter-result','unsupported'); return end
            local s = osc_test_state
            s.osc_disabled = false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            scope('11111111111111111111111111111111')
            s.chapters = {}
            osc_test_layout(); s.controller_focus_name = 'overflow_btn_chapters'; osc_test_activate()
            local empty = #chapter_requests == 0 and not s.chapter_menu_visible
            s.chapters = { {index=93,title='Finale',start_seconds=30,end_seconds=60} }
            s.native_menu_request = {owner='22222222222222222222222222222222', sequence=12, kind='audio'}
            osc_test_layout(); s.controller_focus_name = 'btn_chapters'; osc_test_activate()
            mp.set_property('user-data/native-chapter-result',tostring(empty)..':'..tostring(#chapter_requests))
            """, rawAss: true, resultProperty: "user-data/native-chapter-result", beforeScript: NativeChapterProbe);
        Assert.Equal("true:0", output);
    }

    [Fact]
    public void ChapterReadyPatchKeepsExistingTimelineThumbnailsAndMarkerOwner()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local s = osc_test_state
            local cached = {index=4,file='retained.bgra',width=80,height=48,stride=320}
            s.chapters = { {index=4,title='Opening',start_seconds=0,end_seconds=30,thumbnail_url='first'},
                {index=93,title='Finale',start_seconds=30,end_seconds=60} }
            s.chapter_thumbnails[4] = cached
            s.chapter_thumbnail_requested[93] = true
            local marker = s.marker_details
            chapter_handlers['osc-patch-chapter-thumbnail-url']('93','ready.webp')
            local retained = s.chapter_thumbnails[4] == cached and s.marker_details == marker
            mp.set_property('user-data/native-chapter-result',tostring(retained)..':'..s.chapters[2].thumbnail_url..':'..tostring(s.chapter_thumbnail_requested[93]))
            """, rawAss: true, resultProperty: "user-data/native-chapter-result", beforeScript: NativeChapterProbe);
        Assert.Equal("true:ready.webp:nil", output);
    }

    [Theory]
    [InlineData(true, "false")]
    [InlineData(false, "true")]
    public void ChapterSeekPreservesRealNativePauseIntent(bool paused, string forceResume)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local s = osc_test_state
            local get_bool = mp.get_property_bool
            mp.get_property_bool = function(name, fallback)
                if name == 'pause' then return {{paused.ToString().ToLowerInvariant()}} end
                return get_bool(name, fallback)
            end
            s.chapters = { {index=93,title='Finale',start_seconds=30,end_seconds=60} }
            s.chapter_menu_visible, s.keyboard_menu_kind, s.keyboard_menu_index = true, 'chapters', 1
            s.chapter_menu_items = { {start_seconds=30} }
            osc_test_menu_activate()
            local request = chapter_intents[1]
            mp.set_property('user-data/native-chapter-result', request and request[2]..':'..request[3]..':'..request[4] or 'no-seek')
            """, rawAss: true, resultProperty: "user-data/native-chapter-result", beforeScript: NativeChapterProbe);
        Assert.Equal("silo-seek-absolute:30:" + forceResume, output);
    }
}

