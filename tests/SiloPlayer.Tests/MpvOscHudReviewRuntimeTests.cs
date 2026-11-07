namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    [Theory]
    [InlineData("audio", false, 1920)]
    [InlineData("audio", true, 1920)]
    [InlineData("chapters", false, 1920)]
    [InlineData("chapters", true, 1920)]
    [InlineData("quality", false, 1920)]
    [InlineData("quality", true, 1920)]
    [InlineData("audio", false, 460)]
    [InlineData("audio", true, 460)]
    [InlineData("chapters", false, 460)]
    [InlineData("chapters", true, 460)]
    public void HudMenuSelectionAndDismissalOwnTheCompleteNativeClick(string kind, bool dismiss, int width)
    {
        var expected = dismiss ? "" : kind == "audio" ? "silo-audio-select"
            : kind == "chapters" ? "silo-seek-absolute" : "silo-quality-select";
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time = 10
            local s = osc_test_state
            s.osc_disabled = false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = { {index=1,title='First'}, {index=2,title='Second'} }
            s.chapters = { {index=0,start_seconds=20,end_seconds=40,title='Chapter'} }
            s.quality_info = {qualities={ {id='original',label='Original'}, {id='720p',label='720p'} } }
            local prefix = '{{kind}}' == 'chapters' and 'chapter' or '{{kind}}'
            local button = '{{kind}}' == 'chapters' and 'btn_chapters' or 'btn_{{kind}}'
            local function click(r)
                local x, y = tostring(r.x + r.w / 2), tostring(r.y + r.h / 2)
                handlers['osc-mouse-down'](x, y)
                hud_time = hud_time + .1
                handlers['osc-mouse-up'](x, y)
                handlers['osc-video-click'](x, y)
            end
            for _, paused in ipairs({false, true}) do
                for _, room in ipairs({false, true}) do
                    s.pause = paused
                    mp.set_property_bool('pause', paused)
                    s.watch_party = room and {visible=true,is_host=false,guest_control_policy='host_only'} or nil
                    osc_test_layout()
                    local r = s.layout[button]
                    if not r then
                        click(assert(s.layout.btn_more))
                        osc_test_render_hud()
                        r = assert(s.layout['overflow_' .. button])
                    end
                    click(r)
                    assert(s[prefix .. '_menu_visible'], 'Pointer must open the menu')
                    r = assert(s[prefix .. '_menu_items'][1])
                    -- Do not let the opening click's suppression mask a broken row/dismissal.
                    hud_time = hud_time + .6
                    intents = {}
                    if {{dismiss.ToString().ToLowerInvariant()}} then r = {x=400,y=200,w=0,h=0} end
                    assert(r.y + r.h / 2 < s.layout.bar_hit.y, 'Click must be above the transport rail')
                    click(r)
                    assert(not s[prefix .. '_menu_visible'], 'Click must close the menu')
                    assert(table.concat(intents, ',') == '{{expected}}', table.concat(intents, ','))
                    assert(mp.get_property_bool('pause') == paused, 'Menu click changed pause')
                    hud_time = hud_time + .6
                    intents = {}
                    click({x=400,y=200,w=0,h=0})
                    assert(table.concat(intents, ',') == 'silo-pause-toggle', 'Fresh video click must still work')
                end
            end
            mp.set_property('user-data/hud-result', 'owned')
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe,
            windowWidth: width, windowHeight: 1080);
        Assert.Equal("owned", output);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HudStatsCloseOwnsTheCompleteNativeClick(bool paused, bool hidden)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, {{paused.ToString().ToLowerInvariant()}}
            mp.set_property_bool('pause', s.pause)
            s.visible, s.current_alpha, s.target_alpha = {{(hidden ? "false, 0, 0" : "true, 1, 1")}}
            handlers['osc-toggle-stats']()
            assert(s.stats_visible)
            local r = assert(s.stats_close_rect)
            local x, y = tostring(r.x + r.w / 2), tostring(r.y + r.h / 2)
            handlers['osc-mouse-down'](x, y)
            hud_time = 10.1
            handlers['osc-mouse-up'](x, y)
            handlers['osc-video-click'](x, y)
            mp.set_property('user-data/hud-result', tostring(s.stats_visible)..':'
                ..tostring(mp.get_property_bool('pause'))..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe,
            windowWidth: 1920, windowHeight: 1080);
        Assert.Equal($"false:{paused.ToString().ToLowerInvariant()}:", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HudCompactToDesktopResizeRetiresTheSheetAndReleasesInputAndAutoHide(bool leave)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.mouse_in_bar = false
            s.audio_tracks = { {index=1,title='First'}, {index=2,title='Second'} }
            osc_test_layout()
            local r = assert(s.layout.btn_more)
            handlers['osc-mouse-down'](tostring(r.cx), tostring(r.cy))
            handlers['osc-mouse-up'](tostring(r.cx), tostring(r.cy))
            handlers['osc-video-click'](tostring(r.cx), tostring(r.cy))
            osc_test_render_hud()
            handlers['osc-controller-nav']('right')
            assert(s.compact_menu_visible and #s.compact_menu_items > 0)
            assert(s.controller_focus_name:sub(1, 9) == 'overflow_')
            assert(s.layout.overflow_btn_audio)
            dims = {w=1920,h=1080}
            osc_test_layout()
            assert(not s.compact_menu_visible and s.controller_focus_name == nil)
            assert(#s.compact_menu_items == 0 and s.layout.btn_more == nil)
            for name in pairs(s.layout) do assert(name:sub(1, 9) ~= 'overflow_', 'Stale sheet rectangle') end
            -- The first video click after resizing must not dismiss an invisible sheet.
            hud_time = 11
            handlers['osc-mouse-down']('400', '200')
            handlers['osc-mouse-up']('400', '200')
            handlers['osc-video-click']('400', '200')
            assert(table.concat(intents, ',') == 'silo-pause-toggle')
            osc_test_render_hud()
            local normal = s.osc_overlay.data
            r = assert(s.layout.btn_fill)
            handlers['osc-mouse-move'](tostring(r.cx), tostring(r.cy))
            osc_test_render_hud()
            assert(s.osc_overlay.data ~= normal, 'Desktop controls must regain hover feedback')
            handlers['osc-mouse-move']('400', '200')
            hud_time = 15
            if {{leave.ToString().ToLowerInvariant()}} then handlers['osc-mouse-leave'](); assert(leave_check)()
            else assert(hide_check)() end
            assert(s.target_alpha == 0, 'Resized HUD must auto-hide')
            -- Even a stale flag cannot retain a sheet with no drawable anchor.
            s.compact_menu_visible = true
            handlers['osc-show']()
            assert(hide_check)()
            mp.set_property('user-data/hud-result', tostring(s.target_alpha)..':'..tostring(s.compact_menu_visible))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local native = mp.get_property_native
            mp.get_property_native = function(name, fallback)
                if name == 'osd-dimensions' and dims then return dims end
                return native(name, fallback)
            end
            local timeout = mp.add_timeout
            mp.add_timeout = function(seconds, callback)
                if seconds == .5 then leave_check = callback end
                if seconds == 3 then hide_check = callback end
                return timeout(seconds, callback)
            end
            """, windowWidth: 460, windowHeight: 700);
        Assert.Equal("0:false", output);
    }

    [Fact]
    public void HudControllerOnlyPauseResumeReleasesFocusAfterTheInputIdleTimeout()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.mouse_in_bar = false
            handlers['osc-controller-activate']() -- First A focuses Play.
            assert(s.controller_focus_name == 'btn_play')
            handlers['osc-controller-activate']() -- Host pauses.
            assert(s.pause)
            hud_time = 13
            assert(hide_check)()
            assert(s.target_alpha == 1, 'Pause must retain the HUD')
            hud_time = 14
            handlers['osc-controller-activate']() -- Host resumes; refresh idle period.
            assert(not s.pause)
            hud_time = 16
            hide_check()
            assert(s.target_alpha == 1, 'Recent controller input still owns focus')
            hud_time = 17
            hide_check()
            mp.set_property('user-data/hud-result', tostring(s.target_alpha)..':'
                ..tostring(s.controller_focus_name == nil)..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local timeout = mp.add_timeout
            mp.add_timeout = function(seconds, callback)
                if seconds == 3 then hide_check = callback end
                return timeout(seconds, callback)
            end
            -- Model only the native host's response to pause-toggle intents.
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-pause-toggle' then
                    osc_test_state.pause = not osc_test_state.pause
                end
                return command(...)
            end
            """);
        Assert.Equal("0:true:silo-pause-toggle,silo-pause-toggle", output);
    }

    [Theory]
    [InlineData("file", "none")]
    [InlineData("file", "subtitles")]
    [InlineData("file", "audio")]
    [InlineData("disable", "none")]
    [InlineData("disable", "subtitles")]
    [InlineData("disable", "audio")]
    public void HudSurfaceReplacementRetiresControllerFocusAndKeyboardMenus(string retirement, string menu)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.mouse_in_bar = false
            s.audio_tracks = { {index=1,name='First'}, {index=2,name='Second'} }
            handlers['osc-controller-nav']('right')
            assert(s.controller_focus_name)
            local overlay
            if '{{menu}}' ~= 'none' then
                local button = '{{menu}}' == 'audio' and 'btn_audio' or 'btn_cc'
                for i = 1, 30 do
                    if s.controller_focus_name == button then break end
                    handlers['osc-controller-nav']('right')
                end
                handlers['osc-controller-activate']()
                assert(s.keyboard_menu_kind == '{{menu}}')
                overlay = '{{menu}}' == 'audio' and s.audio_menu_overlay or s.subtitle_menu_overlay
                assert(overlay and overlay.data ~= '')
            end
            if '{{retirement}}' == 'file' then events['file-loaded']()
            else handlers['osc-set-visibility']('false') end
            local retired = s.controller_focus_name == nil and s.keyboard_menu_kind == nil
                and s.keyboard_menu_index == -1 and not s.subtitle_menu_visible and not s.audio_menu_visible
                and (not overlay or overlay.data == '')
            if '{{retirement}}' == 'disable' then handlers['osc-set-visibility']('true') end
            assert(hide_check)()
            mp.set_property('user-data/hud-result', tostring(retired)..':'..tostring(s.target_alpha))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local timeout = mp.add_timeout
            mp.add_timeout = function(seconds, callback)
                if seconds == 3 then hide_check = callback end
                return timeout(seconds, callback)
            end
            """);
        Assert.Equal("true:0", output);
    }

    [Theory]
    [InlineData("prompt")]
    [InlineData("undo")]
    public void HudCompactPointerControlsDoNotActivateTheBufferingPrompt(string pill)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.audio_tracks = { {index=1,name='First'}, {index=2,name='Second'} }
            osc_test_layout()
            handlers['osc-buffering-quality']('{{pill}}')
            assert(#s.buffering_quality_actions > 0)
            local r = assert(s.layout.btn_more)
            local x, y = tostring(r.cx), tostring(r.cy)
            handlers['osc-mouse-down'](x, y)
            handlers['osc-mouse-up'](x, y)
            handlers['osc-video-click'](x, y)
            assert(s.compact_menu_visible, 'More must open the compact sheet')
            osc_test_render_hud()
            for _, item in ipairs(s.compact_menu_items) do
                if item.name == 'btn_audio' then r = item; break end
            end
            assert(r.name == 'btn_audio')
            x, y = tostring(r.x + r.w / 2), tostring(r.y + r.h / 2)
            handlers['osc-mouse-down'](x, y)
            handlers['osc-mouse-up'](x, y)
            handlers['osc-video-click'](x, y)
            mp.set_property('user-data/hud-result', tostring(s.audio_menu_visible)..':'
                ..tostring(not s.compact_menu_visible)..':'..tostring(s.controller_focus_name == nil)
                ..':'..tostring(s.buffering_quality_pill)..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe,
            windowWidth: 460, windowHeight: 700);
        Assert.Equal($"true:true:true:{pill}:", output);
    }

    [Theory]
    [InlineData("btn_exit")]
    [InlineData("btn_minimize")]
    public void HudTopChromePressDuringFadeOwnsTheSurfaceThroughRelease(string button)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            s.last_fade_time = hud_time
            osc_test_layout()
            local r = assert(s.layout['{{button}}'])
            local x, y = tostring(r.cx), tostring(r.cy)
            handlers['osc-hide']()
            hud_time = 10.15
            osc_test_tick()
            assert(s.current_alpha >= .1 and s.current_alpha < 1 and s.target_alpha == 0)
            -- No mouse-move wakeup: the press itself must reverse the fade.
            handlers['osc-mouse-down'](x, y)
            assert(s.pressed_control == '{{button}}' and #intents == 0)
            hud_time = 13.5
            assert(hide_check)()
            osc_test_tick()
            assert(s.target_alpha == 1 and s.current_alpha >= .1)
            handlers['osc-mouse-up'](x, y)
            handlers['osc-video-click'](x, y)
            handlers['osc-mouse-up'](x, y)
            mp.set_property('user-data/hud-result', table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local timeout = mp.add_timeout
            mp.add_timeout = function(seconds, callback)
                if seconds == 3 then hide_check = callback end
                return timeout(seconds, callback)
            end
            """);
        Assert.Equal(button == "btn_exit" ? "silo-exit" : "silo-minimize", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HudPointerFitDoesNotKeepEitherHideTimerOwnedByControllerFocus(bool leave)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true,1,1
            osc_test_layout()
            local r = assert(s.layout.btn_fill)
            mp.set_property_number('panscan',0)
            handlers['osc-mouse-move'](tostring(r.cx),tostring(r.cy))
            handlers['osc-mouse-down'](tostring(r.cx),tostring(r.cy))
            handlers['osc-mouse-up'](tostring(r.cx),tostring(r.cy))
            local pointer_only = s.controller_focus_name == nil
            handlers['osc-mouse-move']('400','200')
            if {{leave.ToString().ToLowerInvariant()}} then handlers['osc-mouse-leave'](); assert(leave_check)()
            else assert(hide_check)() end
            mp.set_property('user-data/hud-result',tostring(mp.get_property_number('panscan'))..':'..tostring(pointer_only)..':'..tostring(s.target_alpha))
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local timeout = mp.add_timeout
            mp.add_timeout = function(seconds, callback)
                if seconds == .5 then leave_check = callback end
                if seconds == 3 then hide_check = callback end
                return timeout(seconds, callback)
            end
            """);
        Assert.Equal("1:true:0", output);
    }

    [Fact]
    public void HudNativePointerMovementRetiresActualControllerFocus()
    {
        var output = CaptureStats(1920,1080,1920,1080,false, setup: """
            local s = osc_test_state
            s.osc_disabled,s.pause = false,false
            s.visible,s.current_alpha,s.target_alpha = true,1,1
            handlers['osc-controller-nav']('right')
            local focused = s.controller_focus_name ~= nil
            handlers['osc-mouse-move']('400','200')
            mp.set_property('user-data/hud-result',tostring(focused)..':'..tostring(s.controller_focus_name == nil))
            """, rawAss:true,resultProperty:"user-data/hud-result",beforeScript:HudProbe);
        Assert.Equal("true:true",output);
    }

    [Theory]
    [InlineData("focus", false)]
    [InlineData("focus", true)]
    [InlineData("fullscreen", false)]
    [InlineData("fullscreen", true)]
    [InlineData("subtitle-dialog", false)]
    [InlineData("subtitle-dialog", true)]
    public void HudZeroDeltaMouseMessagesPreserveControllerOwnership(string scenario, bool mpvFirst)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            hud_time, intents = 10, {}
            local s = osc_test_state
            s.osc_disabled, s.pause = false, false
            s.visible, s.current_alpha, s.target_alpha = true, 1, 1
            local function move(x, y)
                pointer = {x=x, y=y}
                if {{mpvFirst.ToString().ToLowerInvariant()}} then
                    assert(mouse_move)()
                    handlers['osc-mouse-move'](tostring(x), tostring(y))
                else
                    handlers['osc-mouse-move'](tostring(x), tostring(y))
                    assert(mouse_move)()
                end
            end
            move(400, 200)
            handlers['osc-controller-nav']('right')
            handlers['osc-controller-nav']('right')
            assert(s.controller_focus_name == 'btn_skip_fwd')
            if '{{scenario}}' == 'fullscreen' then
                for i = 1, 30 do
                    if s.controller_focus_name == 'btn_fullscreen' then break end
                    handlers['osc-controller-nav']('right')
                end
                assert(s.controller_focus_name == 'btn_fullscreen')
                handlers['osc-controller-activate']()
                assert(table.concat(intents, ',') == 'silo-fullscreen-toggle')
                handlers['osc-fullscreen-state']('true')
                dims = {w=2560, h=1440}
                osc_test_layout()
            elseif '{{scenario}}' == 'subtitle-dialog' then
                s.restore_subtitle_focus_after_dialog = true
                handlers['osc-subtitle-dialog-closed']()
                assert(s.controller_focus_name == 'btn_cc')
            end
            local focus, deadline, timer = s.controller_focus_name, s.controller_focus_deadline, s.hide_timer
            osc_test_render_hud()
            local focused = s.osc_overlay.data
            -- Window changes may repeat WM_MOUSEMOVE, including the delayed fullscreen resize.
            for _, time in ipairs({10.01, 10.2}) do
                hud_time = time
                move(400, 200)
                assert(s.controller_focus_name == focus, 'Zero-delta message retired focus')
                assert(s.controller_focus_deadline == deadline and s.hide_timer == timer,
                    'Zero-delta message refreshed the input idle timeout')
            end
            osc_test_render_hud()
            assert(s.osc_overlay.data == focused, 'Zero-delta message changed focus feedback')
            handlers['osc-controller-nav']('right')
            assert(s.controller_focus_name ~= focus and s.controller_focus_name ~= 'btn_play',
                'Navigation must continue from the retained focus, not restart at Play')
            move(401, 200)
            assert(s.controller_focus_name == nil, 'Real movement must still retire focus')
            mp.set_property('user-data/hud-result', 'preserved')
            """, rawAss: true, resultProperty: "user-data/hud-result", beforeScript: HudProbe + """

            local bind = mp.add_key_binding
            mp.add_key_binding = function(key, name, callback, ...)
                if name == 'silo-osc-mouse-move' then mouse_move = callback end
                return bind(key, name, callback, ...)
            end
            local native = mp.get_property_native
            mp.get_property_native = function(name, fallback)
                if name == 'mouse-pos' and pointer then return pointer end
                if name == 'osd-dimensions' and dims then return dims end
                return native(name, fallback)
            end
            """, windowWidth: 1920, windowHeight: 1080);
        Assert.Equal("preserved", output);
    }

    [Theory]
    [InlineData("btn_exit")]
    [InlineData("btn_minimize")]
    public void HudTopChromeDefersItsOnlyIntentUntilMatchingRelease(string button)
    {
        var output = CaptureStats(1920,1080,1920,1080,false,setup: $$"""
            hud_time,intents = 10,{}
            local s=osc_test_state
            s.osc_disabled,s.pause=false,false
            s.visible,s.current_alpha,s.target_alpha=true,1,1
            osc_test_layout()
            local r=assert(s.layout['{{button}}'])
            local x,y=tostring(r.cx),tostring(r.cy)
            handlers['osc-mouse-move'](x,y); osc_test_render_hud()
            local hover=s.osc_overlay.data
            handlers['osc-mouse-down'](x,y); osc_test_render_hud()
            local before=#intents
            local held=s.osc_overlay.data~=hover and s.pressed_control=='{{button}}'
            hud_time=12 -- A long hold must still consume its release.
            handlers['osc-mouse-up'](x,y)
            handlers['osc-video-click'](x,y)
            handlers['osc-mouse-up'](x,y)
            mp.set_property('user-data/hud-result',tostring(before)..':'..tostring(held)..':'..table.concat(intents,','))
            """,rawAss:true,resultProperty:"user-data/hud-result",beforeScript:HudProbe);
        Assert.Equal($"0:true:{(button=="btn_exit"?"silo-exit":"silo-minimize")}",output);
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("other-button")]
    [InlineData("leave")]
    [InlineData("hidden")]
    [InlineData("file")]
    [InlineData("menu")]
    [InlineData("failure")]
    public void HudTopChromeCanceledPressCannotDispatchOnRelease(string retirement)
    {
        var output=CaptureStats(1920,1080,1920,1080,false,setup: $$"""
            hud_time,intents=10,{}
            local s=osc_test_state
            s.osc_disabled,s.pause=false,false
            s.visible,s.current_alpha,s.target_alpha=true,1,1
            osc_test_layout()
            local r=s.layout.btn_exit
            local x,y=tostring(r.cx),tostring(r.cy)
            handlers['osc-mouse-down'](x,y)
            if '{{retirement}}'=='outside' then x,y='400','200'
            elseif '{{retirement}}'=='other-button' then r=s.layout.btn_minimize; x,y=tostring(r.cx),tostring(r.cy)
            elseif '{{retirement}}'=='leave' then handlers['osc-mouse-leave']()
            elseif '{{retirement}}'=='hidden' then handlers['osc-set-visibility']('false'); handlers['osc-set-visibility']('true')
            elseif '{{retirement}}'=='file' then events['file-loaded']()
            elseif '{{retirement}}'=='menu' then s.subtitle_menu_visible=true
            elseif '{{retirement}}'=='failure' then s.playback_failure_visible=true end
            handlers['osc-mouse-up'](x,y)
            mp.set_property('user-data/hud-result',tostring(#intents)..':'..tostring(s.pressed_control==nil))
            """,rawAss:true,resultProperty:"user-data/hud-result",beforeScript:HudProbe);
        Assert.Equal("0:true",output);
    }
}
