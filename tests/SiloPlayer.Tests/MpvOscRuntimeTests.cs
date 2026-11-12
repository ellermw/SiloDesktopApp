using System.Runtime.InteropServices;

namespace SiloPlayer.Tests;

public sealed partial class MpvOscRuntimeTests
{
    // Match the application's P/Invoke library lifetime. Keep the module loaded
    // while still creating and destroying a fresh mpv context for every case.
    private static readonly Lazy<IntPtr> SharedLibrary = new(() =>
        NativeLibrary.Load(Path.Combine(FindRepositoryRoot(), "libs", "mpv", "libmpv-2.dll")));

    [Theory]
    [InlineData(960, 550)]
    [InlineData(1920, 1080)]
    public void CaptionsSkipRouteDisabledRowsUntilTheHostRepublishesAnAvailableRoute(int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.osc_disabled, state.current_alpha = false, 1
            handlers['osc-set-subtitles']('[{"index":7,"language":"en","unplayable":true},{"index":12,"language":"ja","unplayable":false}]', '42')
            handlers['osc-set-active-subtitle']('-1')
            state.subtitle_menu_visible = true
            state.keyboard_menu_kind, state.keyboard_menu_index = 'subtitles', 2
            osc_test_layout(); osc_test_render_subtitles()
            local row
            for _, item in ipairs(state.subtitle_menu_items) do if item.index == 7 then row = item end end
            assert(row and row.disabled and state.subtitle_menu_overlay.data:find('Unavailable', 1, true), 'Route-disabled row was hidden or actionable')
            osc_test_menu_activate()
            state.mouse_x, state.mouse_y = row.x + 5, row.y + 5
            osc_test_mouse_down()
            assert(#intents == 0, 'Route-disabled pointer/keyboard selection dispatched')
            keys['silo-captions-toggle']()
            assert(table.concat(intents, ',') == '12', 'CC selected the route-disabled row before a playable row')
            handlers['osc-set-active-subtitle']('-1')
            handlers['osc-set-subtitles']('[{"index":7,"language":"en","unplayable":true}]', '42')
            keys['silo-captions-toggle']()
            assert(#intents == 1 and state.active_subtitle == -1, 'CC selected the only route-disabled row')
            -- FILE_LOADED/host inventory publication can establish a route;
            -- the row must become selectable without mutating the saved choice.
            handlers['osc-set-subtitles']('[{"index":7,"language":"en","unplayable":false}]', '42')
            keys['silo-captions-toggle']()
            mp.set_property('user-data/subtitle-route-refresh', table.concat(intents, ','))
            """, rawAss: true, windowWidth: width, windowHeight: height,
            resultProperty: "user-data/subtitle-route-refresh", beforeScript: """
            handlers, keys, intents = {}, {}, {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local add = mp.add_forced_key_binding
            mp.add_forced_key_binding = function(key, name, fn, flags) keys[name] = fn; return add(key, name, fn, flags) end
            local native = mp.get_property_native
            mp.get_property_native = function(name, fallback)
                if name == 'mouse-pos' then return {x=-1, y=-1} end
                return native(name, fallback)
            end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-select' then table.insert(intents, args[3]); return end
                return command(...)
            end
            """);
        Assert.Equal("12,7", output);
    }

    [Theory]
    [InlineData("track_id")]
    [InlineData("sync_key")]
    public void CaptionShortcutRebindsRememberedIdentityAfterSameFileReindex(string identityField)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local state = osc_test_state
            handlers['osc-set-subtitles']('[{"index":8,"{{identityField}}":"english","language":"en"},{"index":3,"{{identityField}}":"spanish","language":"es"}]', '42')
            handlers['osc-set-active-subtitle']('3')
            keys['silo-captions-toggle']()
            handlers['osc-set-subtitles']('[{"index":8,"{{identityField}}":"english","language":"en"},{"index":3,"{{identityField}}":"japanese","language":"ja"},{"index":13,"{{identityField}}":"spanish","language":"es"}]', '42')
            assert(state.last_subtitle == 13, 'Remembered identity did not follow its new combined index')
            keys['silo-captions-toggle']()
            keys['silo-captions-toggle']()
            handlers['osc-set-subtitles']('[{"index":8,"{{identityField}}":"english","language":"en"},{"index":13,"{{identityField}}":"japanese","language":"ja"}]', '42')
            assert(state.last_subtitle == -1, 'Removed identity survived index reuse')
            keys['silo-captions-toggle']()
            mp.set_property('user-data/caption-reindex', table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/caption-reindex", beforeScript: """
            handlers, keys, intents = {}, {}, {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local add = mp.add_forced_key_binding
            mp.add_forced_key_binding = function(key, name, fn, flags) keys[name] = fn; return add(key, name, fn, flags) end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-select' then table.insert(intents, args[3]); return end
                return command(...)
            end
            """);
        Assert.Equal("-1,13,-1,8", output);
    }

    [Theory]
    [InlineData("[{\"index\":13,\"track_id\":\"spanish\",\"unplayable\":true}]")]
    [InlineData("[{\"index\":13,\"track_id\":\"spanish\"},{\"index\":14,\"track_id\":\"spanish\"}]")]
    [InlineData("[{\"index\":3,\"language\":\"ja\"}]")]
    public void CaptionShortcutRetiresUnavailableAmbiguousOrReplacedLegacyRows(string replacement)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local state = osc_test_state
            handlers['osc-set-subtitles']('[{"index":3,"track_id":"spanish","language":"es"}]', '42')
            handlers['osc-set-active-subtitle']('3')
            handlers['osc-set-active-subtitle']('-1')
            handlers['osc-set-subtitles']('{{replacement}}', '42')
            local retired = state.last_subtitle == -1 and state.last_subtitle_track == nil
            -- Identityless inventories must also reject metadata at a reused ordinal.
            handlers['osc-set-subtitles']('[{"index":3,"language":"es"}]', '42')
            handlers['osc-set-active-subtitle']('3')
            handlers['osc-set-active-subtitle']('-1')
            handlers['osc-set-subtitles']('[{"index":3,"language":"ja"}]', '42')
            mp.set_property('user-data/caption-retired', tostring(retired and state.last_subtitle == -1))
            """, rawAss: true, resultProperty: "user-data/caption-retired", beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            """);
        Assert.Equal("true", output);
    }

    [Fact]
    public void CaptionShortcutRemembersARowOnlyWithinItsMediaFile()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            handlers['osc-set-subtitles']('[{"index":0,"language":"en"},{"index":1,"language":"es"}]', '42')
            handlers['osc-set-active-subtitle']('0')
            handlers['osc-set-active-subtitle']('-1')
            handlers['osc-set-subtitles']('[{"index":1,"language":"es"},{"index":0,"language":"en"}]', '42')
            keys['silo-captions-toggle']()
            handlers['osc-set-active-subtitle']('-1')
            -- The same indices now describe the opposite languages on episode B.
            handlers['osc-set-subtitles']('[{"index":1,"language":"en"},{"index":0,"language":"es"}]', '44')
            local reset = state.last_subtitle == -1 and state.active_subtitle == -1
            keys['silo-captions-toggle']()
            handlers['osc-set-subtitles']('[]', '45')
            reset = reset and state.last_subtitle == -1 and state.active_subtitle == -1
            mp.set_property('user-data/caption-file', tostring(reset)..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/caption-file", beforeScript: """
            handlers, keys, intents = {}, {}, {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local add = mp.add_forced_key_binding
            mp.add_forced_key_binding = function(key, name, fn, flags) keys[name] = fn; return add(key, name, fn, flags) end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-select' then table.insert(intents, args[3]); return end
                return command(...)
            end
            """);
        Assert.Equal("true:0,1", output);
    }

    [Fact]
    public void ActualSubtitleCycleKeysUseHostDirectionsInsteadOfNativeCycling()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            mp.set_property('input-default-bindings', 'yes')
            mp.commandv('keypress', 'j')
            mp.commandv('keypress', 'J')
            mp.add_timeout(0.1, function()
                osc_test_state.playback_failure_visible = true
                mp.commandv('keypress', 'j')
                mp.commandv('keypress', 'J')
                mp.add_timeout(0.1, function()
                    mp.set_property('user-data/cycle-keys', table.concat(intents, ','))
                end)
            end)
            """, rawAss: true, resultProperty: "user-data/cycle-keys", beforeScript: """
            intents = {}
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-cycle' then
                    table.insert(intents, args[3] or 'backward')
                end
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-select' then table.insert(intents, 'persist') end
                if args[1] == 'cycle' and args[2] == 'sub' then table.insert(intents, 'native') end
                return command(...)
            end
            """);
        Assert.Equal("forward,backward", output);
    }

    [Fact]
    public void SubtitleRightClickUsesLocalHostCycleWithoutPersistedSelections()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.osc_disabled, state.current_alpha = false, 1
            state.subtitle_tracks = {{index=3, unplayable=true}, {index=8}, {index=12}}
            state.active_subtitle = -1
            osc_test_layout()
            state.mouse_x, state.mouse_y = state.layout.btn_cc.x + 2, state.layout.btn_cc.y + 2
            right(); right()
            mp.set_property('user-data/cycle-subtitles', tostring(state.active_subtitle)..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/cycle-subtitles", beforeScript: """
            intents = {}
            local add = mp.add_key_binding
            mp.add_key_binding = function(key, name, fn, flags)
                if key == 'mbtn_right' then right = fn end
                return add(key, name, fn, flags)
            end
            local native = mp.get_property_native
            mp.get_property_native = function(name, fallback)
                if name == 'mouse-pos' then return {x=-1, y=-1} end
                return native(name, fallback)
            end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-cycle' then table.insert(intents, 'local-cycle') end
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-select' then table.insert(intents, 'persist:'..args[3]) end
                if args[1] == 'cycle' and args[2] == 'sub' then table.insert(intents, 'native-cycle') end
                return command(...)
            end
            """);
        Assert.Equal("-1:local-cycle,local-cycle", output);
    }

    [Fact]
    public void CaptionShortcutTurnsOffExecutedTrackAfterSameFileInventoryRefresh()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            handlers['osc-set-subtitles']('[{"index":8,"language":"en"}]', '42')
            handlers['osc-set-active-subtitle']('8')
            handlers['osc-set-subtitles']('[{"index":8,"language":"en"},{"index":12,"language":"es"}]', '42')
            handlers['osc-set-active-subtitle']('8') -- host republishes executed identity
            local highlighted = state.active_subtitle
            keys['silo-captions-toggle']()
            mp.set_property('user-data/refreshed-caption', tostring(highlighted)..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/refreshed-caption", beforeScript: """
            handlers, keys, intents = {}, {}, {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local add = mp.add_forced_key_binding
            mp.add_forced_key_binding = function(key, name, fn, flags) keys[name] = fn; return add(key, name, fn, flags) end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-select' then table.insert(intents, args[3]); return end
                return command(...)
            end
            """);
        Assert.Equal("8:-1", output);
    }

    [Theory]
    [InlineData(960, 550)]
    [InlineData(1920, 1080)]
    public void SubtitleInventoryForAnotherFileRetiresOpenMenuAndItsHitTargets(int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.osc_disabled, state.current_alpha = false, 1
            for _, replacement in ipairs({
                {id='44', json='[{"index":1,"language":"es"}]'},
                {id='45', json='[]'}
            }) do
                handlers['osc-set-subtitles']('[{"index":1,"language":"en"}]', '42')
                state.subtitle_menu_visible = true
                state.keyboard_menu_kind, state.keyboard_menu_index = 'subtitles', 2
                osc_test_layout(); osc_test_render_subtitles()
                local stale
                for _, item in ipairs(state.subtitle_menu_items) do if item.index == 1 then stale = item end end
                assert(stale and state.subtitle_menu_overlay.data:find('English', 1, true), 'Missing original painted row')
                handlers['osc-set-subtitles'](replacement.json, replacement.id)
                assert(not state.subtitle_menu_visible and #state.subtitle_menu_items == 0
                    and state.subtitle_menu_overlay.data == '' and state.keyboard_menu_kind == nil
                    and state.keyboard_menu_index == 0, 'Previous file menu remained active')
                osc_test_menu_activate()
                state.mouse_x, state.mouse_y = stale.x + 5, stale.y + 5
                osc_test_mouse_down()
            end
            mp.set_property('user-data/subtitle-file-menu', tostring(selections))
            """, rawAss: true, windowWidth: width, windowHeight: height,
            resultProperty: "user-data/subtitle-file-menu", beforeScript: """
            handlers, selections = {}, 0
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local native = mp.get_property_native
            mp.get_property_native = function(name, fallback)
                if name == 'mouse-pos' then return {x=-1, y=-1} end
                return native(name, fallback)
            end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-select' then selections = selections + 1; return end
                return command(...)
            end
            """);
        Assert.Equal("0", output);
    }

    [Theory]
    [InlineData(960, 550)]
    [InlineData(1920, 1080)]
    public void SameFileSubtitleRefreshRepaintsRowsAndClampsKeyboardFocus(int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.osc_disabled, state.current_alpha = false, 1
            handlers['osc-set-subtitles']('[{"index":1,"language":"en"}]', '42')
            state.subtitle_menu_visible = true
            state.keyboard_menu_kind, state.keyboard_menu_index = 'subtitles', 2
            osc_test_layout(); osc_test_render_subtitles()
            handlers['osc-set-subtitles']('[{"index":1,"language":"es","unplayable":true}]', '42')
            assert(state.subtitle_menu_visible and state.subtitle_menu_overlay.data:find('Spanish', 1, true)
                and state.subtitle_menu_overlay.data:find('Unavailable', 1, true)
                and not state.subtitle_menu_overlay.data:find('English', 1, true), 'Same-file refresh kept old paint')
            local row
            for _, item in ipairs(state.subtitle_menu_items) do if item.index == 1 then row = item end end
            assert(row and row.disabled, 'Same-file refresh kept enabled hit targets')
            osc_test_menu_activate()
            state.mouse_x, state.mouse_y = row.x + 5, row.y + 5
            osc_test_mouse_down()
            state.keyboard_menu_index = 99
            handlers['osc-set-subtitles']('[]', '42')
            local count = #osc_test_menu_items('subtitles', state.subtitle_menu_items)
            assert(state.subtitle_menu_visible and state.keyboard_menu_index == count
                and count > 0 and not state.subtitle_menu_overlay.data:find('Spanish', 1, true), 'Focus or paint survived shrinking inventory')
            mp.set_property('user-data/subtitle-refresh-menu', tostring(selections))
            """, rawAss: true, windowWidth: width, windowHeight: height,
            resultProperty: "user-data/subtitle-refresh-menu", beforeScript: """
            handlers, selections = {}, 0
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local native = mp.get_property_native
            mp.get_property_native = function(name, fallback)
                if name == 'mouse-pos' then return {x=-1, y=-1} end
                return native(name, fallback)
            end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-select' then selections = selections + 1; return end
                return command(...)
            end
            """);
        Assert.Equal("0", output);
    }

    [Theory]
    [InlineData(960, 550)]
    [InlineData(1920, 1080)]
    public void UnavailableSubtitleRemainsVisibleButRefusesKeyboardAndPointer(int width, int height)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            handlers['osc-set-subtitles']('[{"index":7,"language":"en","codec":"webvtt","source":"embedded","unplayable":true},{"index":12,"language":"ja","source":"embedded"}]')
            state.subtitle_menu_visible = true
            state.keyboard_menu_kind, state.keyboard_menu_index = 'subtitles', 2
            osc_test_layout(); osc_test_render_subtitles()
            local unavailable
            for _, item in ipairs(state.subtitle_menu_items) do if item.index == 7 then unavailable = item end end
            assert(unavailable, 'Unavailable track disappeared')
            local visible = state.subtitle_menu_overlay.data:find('Unavailable', 1, true) ~= nil
            osc_test_menu_activate()
            state.subtitle_menu_visible = true; osc_test_render_subtitles()
            state.mouse_x, state.mouse_y = unavailable.x + 5, unavailable.y + 5
            osc_test_mouse_down()
            mp.set_property('user-data/subtitle-unavailable', tostring(visible) .. ':' .. tostring(unavailable.disabled == true) .. ':' .. tostring(selections))
            """, rawAss: true, windowWidth: width, windowHeight: height,
            resultProperty: "user-data/subtitle-unavailable", beforeScript: """
            handlers, selections = {}, 0
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-select' then selections = selections + 1 end
                return command(...)
            end
            """);
        Assert.Equal("true:true:0", output);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("file")]
    [InlineData("load")]
    [InlineData("preference")]
    public void OldMarkerDetailsCannotPaintAfterTheirOwnerWasReplaced(string departure)
    {
        var content = departure == "content" ? "old" : "current";
        var file = departure == "file" ? 7 : 8;
        var load = departure == "load" ? 1 : 2;
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            handlers['osc-set-markers']('{"content_id":"current","file_id":8,"load_generation":2,"intro_start":5,"intro_end":30}')
            handlers['osc-set-marker-details']('{"content_id":"current","file_id":8,"load_generation":2,"preference_generation":2,"intro":"CURRENT"}')
            handlers['osc-set-marker-details']('{"content_id":"{{content}}","file_id":{{file}},"load_generation":{{load}},"preference_generation":1,"intro":"OLD"}')
            mp.set_property('user-data/details-owner', tostring(osc_test_state.marker_details.intro))
            """, rawAss: true, resultProperty: "user-data/details-owner", beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            """);
        Assert.Equal("CURRENT", output);
    }

    [Fact]
    public void SameTimingRepublishPreservesUndoWhenInitialResetFlagIsConsumed()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            handlers['osc-set-markers']('{"content_id":"episode","intro_start":5,"intro_end":30,"reset_auto_skip":true}')
            local pending = {target=30}
            state.intro_pending, state.intro_undo_origin = pending, 7
            state.intro_prompt_remaining, state.intro_prompt_key = 3.25, 'owner-key'
            handlers['osc-set-markers']('{"content_id":"episode","intro_start":5,"intro_end":30,"reset_auto_skip":false}')
            local kept = state.intro_pending == pending and state.intro_undo_origin == 7
                and state.intro_prompt_remaining == 3.25 and state.intro_prompt_key == 'owner-key'
            mp.set_property('user-data/timing-identity', tostring(kept))
            """, rawAss: true, resultProperty: "user-data/timing-identity", beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            """);
        Assert.Equal("true", output);
    }

    [Theory]
    [InlineData(35)]
    [InlineData(45)]
    public void SegmentProjectionDoesNotChangeRepublishedTimingIdentity(int position)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local state = osc_test_state
            local payload = {content_id='episode', intro_start=5, intro_end=30,
                marker_segments={ {kind='intro', start_seconds=5, end_seconds=30},
                    {kind='intro', start_seconds=40, end_seconds=50} } }
            local utils = require('mp.utils')
            handlers['osc-set-markers'](utils.format_json(payload))
            state.duration, state.time_pos = 100, {{position}}
            osc_test_check()
            assert(state.intro_start ~= 5, 'Active segment must differ from the singular payload')
            local pending = {target=50}
            state.intro_pending, state.intro_undo_origin = pending, 7
            state.intro_prompt_remaining, state.intro_prompt_key = 3.25, 'owner-key'
            handlers['osc-set-markers'](utils.format_json(payload))
            local kept = state.intro_pending == pending and state.intro_undo_origin == 7
                and state.intro_prompt_remaining == 3.25 and state.intro_prompt_key == 'owner-key'
            payload.intro_end = 31
            handlers['osc-set-markers'](utils.format_json(payload))
            local range_reset = state.intro_pending == nil and state.intro_undo_origin == nil and state.intro_prompt_key == nil
            state.intro_pending, state.intro_undo_origin, state.intro_prompt_key = pending, 7, 'owner-key'
            payload.marker_segments[2].end_seconds = 51
            handlers['osc-set-markers'](utils.format_json(payload))
            local segment_reset = state.intro_pending == nil and state.intro_undo_origin == nil and state.intro_prompt_key == nil
            mp.set_property('user-data/timing-identity', tostring(kept)..':'..tostring(range_reset)..':'..tostring(segment_reset))
            """, rawAss: true, resultProperty: "user-data/timing-identity", beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            """);
        Assert.Equal("true:true:true", output);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"kind\":\"intro\",\"start_seconds\":5,\"end_seconds\":30}]")]
    public void SegmentProjectionRetainsSingularRangesForUnrepresentedKinds(string segments)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local state = osc_test_state
            handlers['osc-set-markers']([==[{"content_id":"episode","intro_start":5,"intro_end":30,
                "recap_start":1,"recap_end":4,"credits_start":90,"credits_end":100,
                "preview_start":70,"preview_end":75,"marker_segments":{{segments}}}]==])
            state.duration, state.osc_disabled, state.next_ep_available = 100, false, false
            state.intro_mode, state.auto_skip_recap, state.auto_skip_credits = 'ask', false, false
            for _, marker in ipairs({
                {'intro', 5, 30, 10, 'Skip Intro'},
                {'recap', 1, 4, 2, 'Skip Recap'}, {'credits', 90, 100, 95, 'Skip Credits'},
                {'preview', 70, 75, 72}
            }) do
                state.time_pos = marker[4]
                osc_test_check()
                assert(state[marker[1]..'_start'] == marker[2] and state[marker[1]..'_end'] == marker[3],
                    marker[1]..' lost its range')
                if marker[5] then
                    assert(state.skip_visible and state.skip_label == marker[5] and state.skip_target == marker[3],
                        marker[1]..' lost its skip pill')
                end
            end
            state.time_pos = 80
            osc_test_check()
            assert(not state.skip_visible, 'No pill should appear outside a marker window')
            state.intro_mode, state.time_pos = 'always', 10
            osc_test_check()
            assert(state.intro_pending and state.intro_pending.target == 30, 'Intro auto-skip was lost')
            state.intro_pending = nil
            state.auto_skip_recap, state.time_pos = true, 2
            osc_test_check()
            assert(state.recap_auto_skipped, 'Recap auto-skip was lost')
            state.auto_skip_credits, state.time_pos = true, 95
            osc_test_check()
            assert(state.credits_auto_skipped, 'Credits auto-skip was lost')
            mp.set_property('user-data/marker-fallback', 'preserved')
            """, rawAss: true, resultProperty: "user-data/marker-fallback", beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            """);
        Assert.Equal("preserved", output);
    }

    [Theory]
    [InlineData("intro")]
    [InlineData("recap")]
    [InlineData("credits")]
    public void MarkerDetailsAreEscapedAndDoNotResetPendingSkipState(string kind)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        { [kind] = "92% · TheIntroDB {\\fs99}provider\\Nname" });
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local state = osc_test_state
            state.osc_disabled = false
            state.duration = 100
            state.{{kind}}_start = {{(kind == "credits" ? "90" : "0")}}
            state.{{kind}}_end = {{(kind == "credits" ? "100" : "30")}}
            state.time_pos = {{(kind == "credits" ? "95" : "5")}}
            state.next_ep_available = false
            osc_test_check()
            local pending = {target=30}
            state.intro_pending = pending
            state.intro_prompt_remaining = 3.25
            state.intro_undo_origin = 7
            local key = state.intro_prompt_key
            handlers['osc-set-marker-details']([==[{{json}}]==])
            local preserved = state.intro_pending == pending and state.intro_prompt_remaining == 3.25
                and state.intro_undo_origin == 7 and state.intro_prompt_key == key
            local filled = marker_overlay or 'NO OVERLAY'
            handlers['osc-set-marker-details']('{}')
            local cleared = not marker_overlay:find('92%', 1, true) and state.intro_pending == pending
                and state.intro_prompt_remaining == 3.25 and state.intro_undo_origin == 7
            mp.set_property('user-data/marker-test', 'PRESERVED:'..tostring(preserved)..':CLEARED:'..tostring(cleared)..':'..filled)
            """, rawAss: true, resultProperty: "user-data/marker-test", beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local create = mp.create_osd_overlay
            mp.create_osd_overlay = function(kind)
                local overlay = create(kind)
                overlay.update = function(self)
                    if self.data and self.data:find('Skip ', 1, true) then marker_overlay = self.data end
                end
                return overlay
            end
            """);
        Assert.Contains("PRESERVED:true", output);
        Assert.Contains("CLEARED:true", output);
        Assert.Contains("92%", output); Assert.Contains("TheIntroDB", output);
        Assert.DoesNotContain("{\\fs99}", output);
    }

    [Theory]
    [InlineData("off", false, false)]
    [InlineData("select", false, false)]
    [InlineData("off", true, false)]
    [InlineData("select", true, false)]
    [InlineData("off", false, true)]
    [InlineData("select", false, true)]
    [InlineData("off", true, true)]
    [InlineData("select", true, true)]
    [InlineData("dismiss", false, false)]
    [InlineData("dismiss", true, false)]
    public void SubtitleMouseSelectionConsumesTheFollowingVideoClick(string action, bool paused, bool room)
    {
        // The native window sends mouse-down, mouse-up, then video-click for
        // one quick click. A subtitle row closes its popup on mouse-down;
        // video-click must still belong to that row after the popup closes.
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local state = osc_test_state
            state.osc_disabled = false
            state.visible, state.current_alpha, state.target_alpha = true, 1, 1
            state.subtitle_tracks = {
                {index=0, language='en', source='external', codec='srt'},
                {index=1, language='es', source='external', codec='srt'}
            }
            state.active_subtitle = 1
            state.watch_party = {{(room ? "{visible=true, is_host=false, guest_control_policy='host_only'}" : "nil")}}
            mp.set_property_bool('pause', {{paused.ToString().ToLowerInvariant()}})
            osc_test_layout()
            probe_time = 100
            for i = 1, 3 do
                state.subtitle_menu_visible = true
                osc_test_render_subtitles()
                local row
                for _, item in ipairs(state.subtitle_menu_items) do
                    if item.action == '{{(action == "dismiss" ? "select" : action)}}' then row = item; break end
                end
                assert(row, 'subtitle row missing')
                local x, y = {{(action == "dismiss" ? "100, 100" : "row.x + row.w/2, row.y + row.h/2")}}
                handlers['osc-mouse-down'](tostring(x), tostring(y))
                probe_time = probe_time + .1
                handlers['osc-mouse-up'](tostring(x), tostring(y))
                handlers['osc-video-click'](tostring(x), tostring(y))
            end
            local result = table.concat(intents, ',')..':'..tostring(state.subtitle_menu_visible)..':'..tostring(mp.get_property_bool('pause'))
            -- Once the menu click expires, a new click on empty video still
            -- requests the normal host-controlled pause action.
            probe_time = probe_time + .6
            intents = {}
            handlers['osc-video-click']('100', '100')
            mp.set_property('user-data/probe-result', result..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            handlers, intents = {}, {}
            local get_time = mp.get_time
            mp.get_time = function() return probe_time or get_time() end
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and (args[2] == 'silo-subtitle-select' or args[2] == 'silo-pause-toggle') then
                    table.insert(intents, args[2]..(args[3] and '='..args[3] or ''))
                    return
                end
                return command(...)
            end
            """);
        var selection = $"silo-subtitle-select={(action == "off" ? -1 : 0)}";
        var expected = action == "dismiss" ? "" : $"{selection},{selection},{selection}";
        Assert.Equal($"{expected}:false:{paused.ToString().ToLowerInvariant()}:silo-pause-toggle", output);
    }

    [Theory]
    [InlineData("off", false)]
    [InlineData("select", false)]
    [InlineData("off", true)]
    [InlineData("select", true)]
    public void SubtitleKeyboardSelectionPreservesPauseIntent(string action, bool paused)
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local state = osc_test_state
            state.subtitle_menu_visible = true
            state.subtitle_tracks = { {index=1, language='es', source='external', codec='srt'} }
            state.active_subtitle = 0
            state.keyboard_menu_kind = 'subtitles'
            mp.set_property_bool('pause', {{paused.ToString().ToLowerInvariant()}})
            osc_test_layout()
            osc_test_render_subtitles()
            for i, item in ipairs(osc_test_menu_items('subtitles', state.subtitle_menu_items)) do
                if item.action == '{{action}}' then state.keyboard_menu_index = i; break end
            end
            assert(osc_test_menu_activate(), 'keyboard menu was not activated')
            mp.set_property('user-data/probe-result', table.concat(intents, ',')..':'..tostring(state.subtitle_menu_visible)..':'..tostring(mp.get_property_bool('pause')))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            intents = {}
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and (args[2] == 'silo-subtitle-select' or args[2] == 'silo-pause-toggle') then
                    table.insert(intents, args[2]..(args[3] and '='..args[3] or ''))
                    return
                end
                return command(...)
            end
            """);
        Assert.Equal($"silo-subtitle-select={(action == "off" ? -1 : 1)}:false:{paused.ToString().ToLowerInvariant()}", output);
    }

    [Fact]
    public void SubtitleDelayKeysSendBoundedExplicitIntentOnlyAfterPreferencesLoad()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            keys['silo-sub-delay-fwd']()
            local unloaded = #intents == 0
            if handlers['osc-subtitle-preferences'] then handlers['osc-subtitle-preferences']('true', '') end
            mp.set_property_number('sub-delay', 9.95)
            keys['silo-sub-delay-fwd']()
            mp.set_property('user-data/probe-result', tostring(unloaded)..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            handlers, keys, intents = {}, {}, {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local add = mp.add_key_binding
            mp.add_key_binding = function(key, name, fn, flags) keys[name] = fn; return add(key, name, fn, flags) end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-offset' then table.insert(intents, args[3]); return end
                return command(...)
            end
            """);
        Assert.Equal("true:10000", output);
    }

    [Fact]
    public void SubtitleDelayMouseAndKeyboardRailsShareTheExplicitPreferenceIntent()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            osc_test_layout()
            state.subtitle_menu_visible = true
            state.active_subtitle = 0
            state.subtitle_tracks = {{index=0, language='en', source='external', codec='srt'}}
            mp.set_property_number('sub-delay', .5)
            handlers['osc-subtitle-preferences']('true', '')
            state.keyboard_menu_kind = 'subtitles'
            for i, item in ipairs(osc_test_menu_items('subtitles', state.subtitle_menu_items)) do
                if item.action == 'delay' and item.delta < 0 then state.keyboard_menu_index = i; break end
            end
            osc_test_menu_activate()
            state.keyboard_menu_kind = nil
            for _, item in ipairs(state.subtitle_menu_items) do
                if item.action == 'delay' and item.delta > 0 then state.mouse_x = item.x + item.w/2; state.mouse_y = item.y + item.h/2; break end
            end
            osc_test_mouse_down()
            for _, item in ipairs(state.subtitle_menu_items) do
                if item.action == 'delay_reset' then state.mouse_x = item.x + item.w/2; state.mouse_y = item.y + item.h/2; break end
            end
            osc_test_mouse_down()
            mp.set_property('user-data/probe-result', table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            handlers, intents = {}, {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-subtitle-offset' then table.insert(intents, args[3]); return end
                return command(...)
            end
            """);
        Assert.Equal("400,500,0", output);
    }

    [Fact]
    public void RoomHeldSeekChangesDisplayedTimelineWhileMediaAndMarkersRemainActual()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.watch_party = {visible=true}
            state.time_pos = 100
            state.duration = 300
            state.visible = true
            state.current_alpha = 1
            handlers['osc-room-seek-position']('130')
            osc_test_layout()
            osc_test_tick()
            local shown = state.osc_overlay.data:find('2:10', 1, true) ~= nil
            local actual = state.time_pos == 100
            handlers['osc-room-seek-position']('')
            osc_test_tick()
            local released = state.osc_overlay.data:find('1:40', 1, true) ~= nil
            mp.set_property('user-data/room-seek-display', tostring(shown)..':'..tostring(actual)..':'..tostring(released))
            """, rawAss: true, resultProperty: "user-data/room-seek-display", beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            """);
        Assert.Equal("true:true:true", output);
    }

    [Theory]
    [InlineData(360)]
    [InlineData(500)]
    [InlineData(720)]
    public void SubtitleSyncActionsRemainAboveTheTimelineOnShortWindows(int height)
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            osc_test_layout()
            state.subtitle_menu_visible = true
            state.subtitle_ai_available = true
            state.active_subtitle = 0
            state.subtitle_tracks = {
                {index=0, language='en', source='downloaded', codec='srt'},
                {index=1, language='fr', source='external', codec='srt'},
                {index=2, language='de', source='embedded', codec='srt'}
            }
            handlers['osc-set-subtitle-sync']('[{"index":0,"key":"stored-9","status":"Timing changed, but the new cues could not load. Reload to try again.","sync":true,"reset":true,"reload":true}]')
            local actions, max_bottom, min_top = {}, 0, math.huge
            for _, row in ipairs(state.subtitle_menu_items) do
                actions[row.action] = true
                max_bottom = math.max(max_bottom, row.y + row.h)
                min_top = math.min(min_top, row.y)
            end
            mp.set_property('user-data/probe-result', tostring(actions.sync and actions.reset_timing and actions.sync_reload)
                .. ':' .. tostring(min_top >= 10) .. ':' .. tostring(max_bottom < state.layout.seek_bar.y))
            """, rawAss: true, windowWidth: 640, windowHeight: height, resultProperty: "user-data/probe-result", beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            """);
        Assert.Equal("true:true:true", result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoplayIntroPreferenceRefreshPreservesTheLiveControlsScript(bool refreshWhileSeekPending)
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local state = osc_test_state
            handlers['osc-set-intro-mode']('always')
            handlers['osc-set-markers']('{"content_id":"episode-2","reset_auto_skip":true,"marker_segments":[{"kind":"intro","start_seconds":0,"end_seconds":113.032}]}')
            state.time_pos = 0
            osc_test_check()
            local original_key = state.intro_prompt_key
            if not {{(refreshWhileSeekPending ? "true" : "false")}} then
                state.time_pos = 113.032
                osc_test_check()
            end
            -- Replay the logged delayed host preference response after the
            -- successor episode's intro seek has already started/completed.
            handlers['osc-set-intro-mode']('always')
            state.time_pos = 113.032
            osc_test_check()
            local retained = state.intro_prompt_key == original_key
            state.intro_prompt_remaining = 0
            osc_test_check()
            handlers['osc-mouse-move']('100', '100')
            state.current_alpha = 1
            osc_test_tick()
            keys['silo-fs-override']()
            keys['silo-menu-escape']()
            mp.set_property('user-data/probe-result', tostring(retained)..':'..tostring(state.visible)..':'..tostring(state.osc_overlay.data ~= '')..':'..table.concat(intents, ','))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            handlers, keys, intents = {}, {}, {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local add = mp.add_forced_key_binding
            mp.add_forced_key_binding = function(key, name, fn, flags) keys[name] = fn; return add(key, name, fn, flags) end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2]:find('silo-', 1, true) == 1 then
                    if args[2] == 'silo-fullscreen-toggle' or args[2] == 'silo-escape-unhandled' then table.insert(intents, args[2]) end
                    return
                end
                return command(...)
            end
            """);
        Assert.True(result == "true:true:true:silo-fullscreen-toggle,silo-escape-unhandled", result);
    }

    [Theory]
    [InlineData("ask", false)]
    [InlineData("ask", true)]
    [InlineData("never", false)]
    [InlineData("never", true)]
    public void ChangedIntroPreferenceCancelsItsPreviousActionTogetherWithItsKey(string mode, bool pending)
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
            local state = osc_test_state
            handlers['osc-set-intro-mode']('always')
            handlers['osc-set-markers']('{"content_id":"episode","intro_start":0,"intro_end":30}')
            state.time_pos = 0; osc_test_check()
            if not {{(pending ? "true" : "false")}} then state.time_pos = 30; osc_test_check() end
            handlers['osc-set-intro-mode']('{{mode}}')
            local cancelled = state.intro_pending == nil and state.intro_undo_origin == nil
            state.time_pos = 30; osc_test_check()
            mp.set_property('user-data/probe-result', tostring(cancelled)..':'..tostring(state.intro_prompt_key ~= nil)..':'..tostring(not state.skip_visible))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            """);
        Assert.Equal("true:true:true", result);
    }

    [Fact]
    public void FillControlTogglesNativeContainCoverWithoutChangingTransport()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.controller_focus_name = 'btn_fill'
            osc_test_activate()
            local fill = mp.get_property_number('panscan', 0)
            osc_test_activate()
            mp.set_property('user-data/probe-result', tostring(fill) .. ':' .. tostring(mp.get_property_number('panscan', 0)))
            """, rawAss: true, resultProperty: "user-data/probe-result");
        Assert.Equal("1:0", result);
    }
    [Fact]
    public void CompactControlLayoutKeepsUtilitiesReachableThroughOverflow()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            osc_test_layout()
            local layout = osc_test_state.layout
            mp.set_property('user-data/probe-result', tostring(layout.btn_more ~= nil) .. ':' .. tostring(layout.btn_audio == nil) .. ':' .. tostring(layout.btn_pip == nil))
            """, rawAss: true, windowWidth: 500, windowHeight: 450, resultProperty: "user-data/probe-result");
        Assert.Equal("true:true:true", result);
    }

    [Fact]
    public void QualityChoicesRespectEmptyServerTiersAndHidePartyVersionSwitching()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.quality_info = {qualities={}, versions={{file_id=1}}}
            local empty = not osc_test_quality()
            state.quality_info.versions = {{file_id=1}, {file_id=2}}
            local versions = osc_test_quality()
            state.watch_party = true
            local party = not osc_test_quality()
            state.quality_info.qualities = {{id='original'}, {id='720p'}}
            local tiers = osc_test_quality()
            mp.set_property('user-data/probe-result', tostring(empty)..':'..tostring(versions)..':'..tostring(party)..':'..tostring(tiers))
            """, rawAss: true, resultProperty: "user-data/probe-result");
        Assert.Equal("true:true:true:true", result);
    }
    [Fact]
    public void ExpiredAskOfferReturnsOnReentryWithoutPretendingTheIntroWasSkipped()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.intro_mode = 'ask'; state.marker_content_id = 'episode'; state.intro_start = 0; state.intro_end = 30
            state.time_pos = 5; osc_test_check()
            state.intro_prompt_remaining = 0; osc_test_check()
            local expired = not state.skip_visible
            state.time_pos = 35; osc_test_check()
            state.time_pos = 5; osc_test_check()
            mp.set_property('user-data/probe-result', tostring(expired) .. ':' .. tostring(state.skip_visible))
            """, rawAss: true, resultProperty: "user-data/probe-result");
        Assert.Equal("true:true", result);
    }
    [Fact]
    public void BundledSubRipDecoderPreservesOriginalAlignmentAndTextStyle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"silo-original-subrip-{Guid.NewGuid():N}.srt");
        File.WriteAllText(path, "1\n00:00:00,000 --> 00:00:04,000\n{\\an8}<b>Bold</b> <i>Italic</i>\n");
        try
        {
            var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: $$"""
                mp.register_event('file-loaded', function()
                    mp.commandv('sub-add', [[{{path.Replace('\\', '/')}}]], 'select')
                end)
                mp.commandv('loadfile', 'av://lavfi:color=c=black:s=16x16:r=10:d=5')
                mp.add_periodic_timer(0.05, function()
                    local text = mp.get_property('sub-text-ass', '')
                    if text:find('Bold', 1, true) then mp.set_property('user-data/subrip-probe', text) end
                end)
                """, rawAss: true, resultProperty: "user-data/subrip-probe");
            Assert.Contains("\\an8", result); Assert.Contains("\\b1", result); Assert.Contains("\\i1", result);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void SharedVideoSeekIntervalsReachTheRealKeyboardTransport()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            handlers['osc-set-seek-intervals']('45', '90')
            keys['silo-seek-back']()
            keys['silo-seek-fwd']()
            mp.set_property('user-data/probe-result', table.concat(seeks, ','))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            handlers, keys, seeks = {}, {}, {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            local add = mp.add_forced_key_binding
            mp.add_forced_key_binding = function(key, name, fn, flags) keys[name] = fn; return add(key, name, fn, flags) end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[2] == 'silo-seek-relative' then table.insert(seeks, args[3]); return end
                return command(...)
            end
            """);
        Assert.Equal("-45,90", result);
    }

    [Fact]
    public void IntroUndoWaitsForMovementAndSurvivesStructuredRangeExit()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.intro_mode = 'always'
            state.marker_content_id = 'episode'
            state.marker_segments = {{kind='intro', start_seconds=0, end_seconds=30}}
            state.time_pos = 5
            osc_test_check()
            local waiting = state.intro_pending ~= nil and state.intro_undo_origin == nil
            state.time_pos = 30
            osc_test_check()
            local undo = state.skip_visible and state.skip_label == 'Watch Intro' and state.skip_target == 0
            state.marker_segments = {}
            state.intro_pending = nil; state.intro_undo_origin = nil; state.intro_prompt_key = nil
            osc_test_check()
            mp.set_property('user-data/probe-result', tostring(waiting) .. ':' .. tostring(undo) .. ':' .. tostring(state.skip_visible))
            """, rawAss: true, resultProperty: "user-data/probe-result");
        Assert.Equal("true:true:false", result);
    }
    [Fact]
    public void WatchPartyKeyboardTransportEmitsOneIntentWithoutMutatingPause()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local previous = mp.get_property_bool('input-default-bindings')
            mp.set_property_bool('input-default-bindings', false)
            room_keys['silo-play-pause-space']()
            room_keys['silo-play-pause-k']()
            local disabled = mp.get_property_bool('input-default-bindings') == false
            mp.set_property_bool('input-default-bindings', previous)
            mp.set_property('user-data/room-probe-output', tostring(room_intents) .. ':' .. tostring(room_direct_pauses) .. ':' .. tostring(disabled))
            """, rawAss: true, resultProperty: "user-data/room-probe-output", beforeScript: """
            room_keys = {}
            room_intents = 0
            room_direct_pauses = 0
            local add_key = mp.add_forced_key_binding
            mp.add_forced_key_binding = function(key, name, fn, flags)
                room_keys[name] = fn
                return add_key(key, name, fn, flags)
            end
            local command = mp.commandv
            mp.commandv = function(...)
                local args = {...}
                if args[1] == 'script-message' and args[2] == 'silo-pause-toggle' then
                    room_intents = room_intents + 1
                    return
                end
                if args[1] == 'cycle' and args[2] == 'pause' then room_direct_pauses = room_direct_pauses + 1 end
                return command(...)
            end
            """);
        Assert.Equal("2:0:true", output);
    }

    [Theory]
    [InlineData(960, 720)]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    public void LongEnhancementStatusUsesSeparateLinesWithoutLosingText(int width, int height)
    {
        const string status = "Requested driver enhancement is unavailable on the current video output";
        var ass = CaptureStats(1920, 1080, 1920, 1080, false,
            "mp.set_property('user-data/silo-upscaling-status', '" + status + "')",
            rawAss: true, windowWidth: width, windowHeight: height);
        var entries = System.Text.RegularExpressions.Regex.Matches(ass,
            @"\\pos\(([\d.]+),([\d.]+)\).*?\}([^\{\r\n]+)");
        var label = entries.Cast<System.Text.RegularExpressions.Match>()
            .First(m => m.Groups[3].Value == "Enhancement status");
        var value = entries.Cast<System.Text.RegularExpressions.Match>()
            .First(m => m.Groups[3].Value.StartsWith("Requested driver"));
        Assert.True(double.Parse(value.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
            > double.Parse(label.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        var text = System.Text.RegularExpressions.Regex.Replace(ass, @"\{[^}]*\}|\\N", "")
            .Replace("\r", "").Replace("\n", "");
        Assert.Contains(status, text);
    }
    [Fact]
    public void IntelStatsIdentifyRequestedDriverProcessing()
    {
        var text = CaptureStats(1920, 1080, 3840, 2160, false, processor: "intel");
        Assert.Contains("UpscalerIntel VSR (requested)", text);
        Assert.DoesNotContain("RTX", text);
    }

    [Fact]
    public void NeuralStatsSeparateRendererReconstructionFromDecodedVideo()
    {
        var text = CaptureStats(1920, 1080, 1920, 1080, false, neural: true);
        Assert.Contains("Processed video1920x1080", text);
        Assert.Contains("UpscalerFSRCNNX AI (requested)", text);
        Assert.Contains("AI luma reconstruction (requested)3840x2160 (2x)", text);
        Assert.DoesNotContain("VSR active", text);
    }

    [Theory]
    [InlineData(1920, 1080, 3840, 2160, true, "3840x2160 (4K)", "2.00x", "4.00x", "RTX VSR (requested)")]
    [InlineData(1920, 1080, 1920, 1080, false, "1920x1080", "None", "1.00x", "Normal renderer")]
    [InlineData(3840, 2160, 3840, 2160, false, "3840x2160 (4K)", "None", "1.00x", "Normal renderer")]
    [InlineData(1920, 800, 3840, 1600, true, "3840x1600", "2.00x", "4.00x", "RTX VSR (requested)")]
    [InlineData(1920, 1080, 2880, 1620, true, "2880x1620", "1.50x", "2.25x", "RTX VSR (requested)")]
    public void PlaybackInfoDistinguishesProcessedVideoFromFullscreenWindow(int sourceWidth, int sourceHeight,
        int outputWidth, int outputHeight, bool rtx, string processed, string factor, string pixels, string upscaler)
    {
        var text = CaptureStats(sourceWidth, sourceHeight, outputWidth, outputHeight, rtx);
        Assert.Contains($"Source video{sourceWidth}x{sourceHeight}", text);
        Assert.Contains($"Processed video{processed}", text);
        Assert.Contains($"Scale per dimension{factor}", text);
        Assert.Contains($"Pixel count{pixels}", text);
        Assert.Contains($"Upscaler{upscaler}", text);
        Assert.DoesNotContain("VSR active", text);
    }

    [Theory]
    [InlineData(2000, 1800000, 128000, "2.0 Mbps", "1.8 Mbps", "128 kbps")]
    [InlineData(1500, 1200000, 96000, "1.5 Mbps", "1.2 Mbps", "96 kbps")]
    [InlineData(0, 8400000, 192000, "Not applicable", "8.4 Mbps", "192 kbps")]
    [InlineData(2000, 0, 0, "2.0 Mbps", "Unavailable", "Unavailable")]
    [InlineData(-1, 0, 0, "Unavailable", "Unavailable", "Unavailable")]
    public void PlaybackInfoSeparatesTargetAndMeasuredBitratesFromSource(int targetKbps,
        int videoBps, int audioBps, string target, string video, string audio)
    {
        var setup = $$"""
            mp.commandv('script-message', 'osc-set-media-info', '{"bitrate":8400,"video_bitrate":0}')
            mp.commandv('script-message', 'osc-set-stream-info', 'Transcode', 'HLS', 'https', 'H264', 'AAC', '6000')
            mp.commandv('script-message', 'osc-set-stream-info', '{{(targetKbps != 0 ? "Transcode" : "Direct Play")}}', 'HLS', 'https', 'H264', 'AAC', '{{targetKbps}}')
            """;
        var text = CaptureStats(1280, 720, 2560, 1440, true, setup, videoBps, audioBps);
        var stream = text.Split("PLAYBACK STREAM INFO")[1].Split("CURRENT SOURCE FILE")[0];
        var source = text.Split("CURRENT SOURCE FILE")[1];
        Assert.Contains($"Target video bitrate{target}", stream);
        Assert.Contains($"Video bitrate (measured){video}", stream);
        Assert.Contains($"Audio bitrate (measured){audio}", stream);
        Assert.Contains("Bitrate8.4 Mbps", source);
        Assert.Contains("Video bitrateNot supplied", source);
    }

    [Fact]
    public void MediaAndServerTrackTextCannotInjectAssOverrideTags()
    {
        // Track titles come from media-file metadata and tier labels from the
        // server. Neither may restyle the HUD or break lines through ASS tags.
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            osc_test_layout()
            local hostile = '{\\fs400\\c&H0000FF&}Evil\\NLine\\h'
            state.active_subtitle = 0
            state.subtitle_tracks = {
                {index=0, language=hostile, label=hostile, source='external', codec=hostile},
                {index=1, language='en', label=string.rep('é', 60), source='external', codec='srt'},
            }
            state.subtitle_menu_visible = true
            osc_test_render_subtitles()
            state.subtitle_menu_visible = false
            state.audio_tracks = {{index=0, language=hostile, title='Main', codec='aac', layout=hostile}}
            state.active_audio = 0
            state.audio_menu_visible = true
            osc_test_render_audio()
            state.audio_menu_visible = false
            state.quality_info = {qualities={{id='t1', label=hostile}}, versions={{file_id=1, label=hostile}, {file_id=2, label='Other'}}, active_file_id=1}
            state.active_quality = 't1'
            state.quality_menu_visible = true
            osc_test_render_quality()
            local rendered = table.concat(captured, '\n')
            local function count(needle)
                local n, from = 0, 1
                while true do
                    local i = rendered:find(needle, from, true)
                    if not i then return n end
                    n, from = n + 1, i + 1
                end
            end
            local raw_tag = rendered:find('{\\fs400', 1, true) ~= nil
            local raw_break = rendered:find('Evil\\NLine', 1, true) ~= nil
            local raw_hard_space = rendered:find('Line\\h', 1, true) ~= nil
            -- Escaped override block, literal break and hard space, in each of
            -- the subtitle, audio and quality menus (at least three copies).
            local escaped_tags = count('\\{\\\239\187\191fs400')
            local literal_breaks = count('Evil\\\239\187\191NLine')
            local split_utf8 = rendered:find('\195…', 1, true) ~= nil
            local whole_utf8 = rendered:find('é…', 1, true) ~= nil
            mp.set_property('user-data/probe-result', table.concat({tostring(raw_tag), tostring(raw_break),
                tostring(raw_hard_space), tostring(escaped_tags >= 3), tostring(literal_breaks >= 3),
                tostring(split_utf8), tostring(whole_utf8)}, ':'))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            captured = {}
            local create = mp.create_osd_overlay
            mp.create_osd_overlay = function(kind)
                local overlay = create(kind)
                local update = overlay.update
                overlay.update = function(self)
                    if self.data and self.data ~= '' then table.insert(captured, self.data) end
                    return update(self)
                end
                return overlay
            end
            """);
        Assert.Equal("false:false:false:true:true:false:true", output);
    }

    [Fact]
    public void UnchangedControlsDoNotResendTheOverlayEveryTick()
    {
        // The 30 Hz tick re-renders the controls. Identical frames must not
        // issue another synchronous osd-overlay command, while every real
        // change (first show, resize, hide, show again) still reaches mpv.
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.osc_disabled = false
            local function count() return updates[state.osc_overlay] or 0 end
            local function ticks(n) for _ = 1, n do osc_test_tick() end end
            local results = {}
            local function record(value) table.insert(results, tostring(value)) end

            state.target_alpha, state.current_alpha = 1, 1
            osc_test_tick()
            record(count() > 0 and state.osc_overlay.data ~= '')       -- first visible frame is sent
            local before = count(); ticks(10); record(count() - before) -- steady visible: no resend

            dims = {w = 1280, h = 720}
            before = count(); osc_test_tick(); record(count() - before) -- resize is sent once
            record(state.osc_overlay.res_x)
            before = count(); ticks(10); record(count() - before)

            state.target_alpha, state.current_alpha = 0, 0
            before = count(); osc_test_tick(); record(count() - before) -- hide is sent once
            record(state.osc_overlay.data == '' and 'empty' or 'visible')
            before = count(); ticks(10); record(count() - before)       -- steady hidden: no resend

            state.target_alpha, state.current_alpha = 1, 1
            before = count(); osc_test_tick(); record(count() - before) -- show again is sent
            record(state.osc_overlay.data ~= '' and 'nonempty' or 'empty')
            mp.set_property('user-data/probe-result', table.concat(results, ':'))
            """, rawAss: true, resultProperty: "user-data/probe-result", beforeScript: """
            updates, dims = {}, nil
            local native = mp.get_property_native
            mp.get_property_native = function(name, fallback)
                if name == 'osd-dimensions' and dims then return dims end
                return native(name, fallback)
            end
            local create = mp.create_osd_overlay
            mp.create_osd_overlay = function(kind)
                local overlay = create(kind)
                local update = overlay.update
                overlay.update = function(self)
                    updates[self] = (updates[self] or 0) + 1
                    return update(self)
                end
                return overlay
            end
            """);
        Assert.Equal("true:0:1:1280:0:1:empty:0:1:nonempty", output);
    }

    [Fact]
    public void TransportReloadingShowsSpinnerWithoutBlackRectangleAndKeepsMenusAccessible()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.osc_disabled = false
            state.visible, state.current_alpha, state.target_alpha = true, 1, 1
            state.duration, state.time_pos, state.volume = 120, 10, 50
            mp.set_property_number('volume', 50)
            state.pause = false
            local results = {}

            -- 1. Transport reloading: spinner but no black rect
            handlers['osc-set-transport-reloading']('true')
            assert(state.transport_reloading, 'transport_reloading not set')
            assert(not state.playback_loading, 'playback_loading should be false during transport reload')
            local wait_data = state.playback_wait_overlay and state.playback_wait_overlay.data or ''
            local has_spinner = wait_data:find('draw_circle', 1, true) ~= nil or #wait_data > 10
            table.insert(results, 'spinner:' .. tostring(has_spinner))
            -- Black rect uses draw_rect with full-screen 000000 fill; transport reloading should not have it
            -- The rect is rendered as {\\p1\\bord0\\shad0\\1c&H000000& in ASS
            local has_black_rect = wait_data:find('1c&H000000&', 1, true) ~= nil
            table.insert(results, 'black_rect:' .. tostring(has_black_rect))

            -- 2. Native menu should NOT be blocked during transport reload
            state.native_audio_capable = true
            state.native_audio_legacy = false
            state.native_audio_scope = 'test_scope'
            state.audio_tracks = {{}, {}}
            state.native_menu_request = nil
            state.native_menu_sequence = 0
            state.post_roll_active = false
            state.playback_failure_visible = false
            osc_test_layout()
            local menu_blocked_reload = state.playback_loading
            table.insert(results, 'menu_blocked_reload:' .. tostring(menu_blocked_reload))

            -- 3. Clear transport reloading
            handlers['osc-set-transport-reloading']('false')
            assert(not state.transport_reloading, 'transport_reloading not cleared')
            wait_data = state.playback_wait_overlay and state.playback_wait_overlay.data or ''
            table.insert(results, 'cleared:' .. tostring(#wait_data == 0))

            -- 4. Startup loading: HAS black rect and blocks menus
            handlers['osc-set-loading']('true')
            assert(state.playback_loading, 'playback_loading not set')
            assert(not state.transport_reloading, 'transport_reloading should be cleared by loading')
            wait_data = state.playback_wait_overlay and state.playback_wait_overlay.data or ''
            local loading_has_black = wait_data:find('1c&H000000&', 1, true) ~= nil
            table.insert(results, 'loading_black:' .. tostring(loading_has_black))
            local menu_blocked_loading = state.playback_loading
            table.insert(results, 'menu_blocked_loading:' .. tostring(menu_blocked_loading))

            -- 5. Mutual exclusion: transport reloading clears loading
            handlers['osc-set-transport-reloading']('true')
            assert(not state.playback_loading, 'loading should be cleared by transport reloading')
            assert(state.transport_reloading, 'transport_reloading not set after mutual clear')
            table.insert(results, 'mutual_excl:true')

            handlers['osc-set-transport-reloading']('false')
            handlers['osc-set-loading']('false')

            mp.set_property('user-data/transport-reload-test', table.concat(results, ','))
            """, beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            """, resultProperty: "user-data/transport-reload-test");
        Assert.Contains("spinner:true", output);
        Assert.Contains("black_rect:false", output);
        Assert.Contains("menu_blocked_reload:false", output);
        Assert.Contains("cleared:true", output);
        Assert.Contains("loading_black:true", output);
        Assert.Contains("menu_blocked_loading:true", output);
        Assert.Contains("mutual_excl:true", output);
    }

    [Fact]
    public void TransportReloadingHidesThePauseBadgeAndTracksTheHostPauseIntent()
    {
        var output = CaptureStats(1920, 1080, 1920, 1080, false, setup: """
            local state = osc_test_state
            state.osc_disabled = false
            local results = {}
            local function badge()
                return state.pause_indicator_overlay ~= nil and #state.pause_indicator_overlay.data > 0
            end

            -- 1. A pause outside a reload keeps its centre Play badge and mpv's own state
            state.pause = true
            handlers['osc-set-transport-reloading']('false')
            table.insert(results, 'idle_badge:' .. tostring(badge()))
            handlers['osc-set-transport-reload-pause']('false')
            table.insert(results, 'idle_intent:' .. tostring(state.transport_reload_paused))

            -- 2. The load freeze pauses mpv: no badge under the spinner, host intent kept
            handlers['osc-set-transport-reloading']('true')
            table.insert(results, 'reload_badge:' .. tostring(badge()))
            handlers['osc-set-transport-reload-pause']('false')
            table.insert(results, 'reload_intent:' .. tostring(state.transport_reload_paused))
            handlers['osc-set-transport-reload-pause']('true')
            table.insert(results, 'reload_paused_intent:' .. tostring(state.transport_reload_paused))

            -- 3. First output retires the reload and its intent; the pause badge returns
            handlers['osc-set-transport-reloading']('false')
            table.insert(results, 'retired_badge:' .. tostring(badge()))
            table.insert(results, 'retired_intent:' .. tostring(state.transport_reload_paused))

            -- 4. An opaque startup cover also retires the reload's intent
            handlers['osc-set-transport-reloading']('true')
            handlers['osc-set-transport-reload-pause']('true')
            handlers['osc-set-loading']('true')
            table.insert(results, 'cover_intent:' .. tostring(state.transport_reload_paused))

            handlers['osc-set-loading']('false')
            state.pause = false

            mp.set_property('user-data/transport-reload-pause-test', table.concat(results, ','))
            """, beforeScript: """
            handlers = {}
            local register = mp.register_script_message
            mp.register_script_message = function(name, fn) handlers[name] = fn; return register(name, fn) end
            """, resultProperty: "user-data/transport-reload-pause-test");
        Assert.Contains("idle_badge:true", output);
        Assert.Contains("idle_intent:nil", output);
        Assert.Contains("reload_badge:false", output);
        Assert.Contains("reload_intent:false", output);
        Assert.Contains("reload_paused_intent:true", output);
        Assert.Contains("retired_badge:true", output);
        Assert.Contains("retired_intent:nil", output);
        Assert.Contains("cover_intent:nil", output);
    }

    private static string CaptureStats(int sourceWidth, int sourceHeight, int outputWidth, int outputHeight,
        bool rtx, string setup = "", int videoBps = 0, int audioBps = 0, string? processor = null, bool neural = false,
        bool rawAss = false, int windowWidth = 3840, int windowHeight = 2160,
        string beforeScript = "", string resultProperty = "user-data/stats-test-output", string? scriptOverride = null)
    {
        var root = FindRepositoryRoot();
        var probe = Path.Combine(Path.GetTempPath(), $"silo-stats-{Guid.NewGuid():N}.lua");
        var script = (scriptOverride ?? Path.Combine(root, "libs", "mpv", "scripts", "silo-osc.lua")).Replace('\\', '/');
        // Run the real OSC inside bundled mpv with controlled input/output
        // properties. Capture the rendered ASS, not the Lua source text.
        File.WriteAllText(probe, $$"""
            local mp = require 'mp'
            local get_number = mp.get_property_number
            local get_native = mp.get_property_native
            local create_overlay = mp.create_osd_overlay
            local values = {
                ['osd-width']={{windowWidth}}, ['osd-height']={{windowHeight}},
                ['video-params/w']={{sourceWidth}}, ['video-params/h']={{sourceHeight}},
                ['video-out-params/w']={{outputWidth}}, ['video-out-params/h']={{outputHeight}},
                ['video-bitrate']={{videoBps}}, ['audio-bitrate']={{audioBps}}
            }
            mp.get_property_number = function(name, fallback)
                if values[name] ~= nil then return values[name] end
                return get_number(name, fallback)
            end
            mp.get_property_native = function(name, fallback)
                if name == 'osd-dimensions' then return {w={{windowWidth}}, h={{windowHeight}}} end
                if name == 'vf' then return {{(rtx || processor != null ? "{{name='d3d11vpp',label='silo-rtx',enabled=true,params={['scaling-mode']='" + (processor ?? "nvidia") + "'}}}" : "{}")}} end
                if name == 'glsl-shaders' then return {{(neural ? "{'C:/fixture/FSRCNNX_x2_8-0-4-1.glsl'}" : "{}")}} end
                return get_native(name, fallback)
            end
            mp.create_osd_overlay = function(kind)
                local overlay = create_overlay(kind)
                overlay.update = function(self)
                    if self.data and self.data:find('Playback Info', 1, true) then
                        mp.set_property('user-data/stats-test-output', self.data)
                    end
                end
                return overlay
            end
            {{beforeScript}}
            local source = assert(io.open([[{{script}}]], 'r'))
            local code = source:read('*a'); source:close()
            assert(loadstring(code .. '\nosc_test_state = state; osc_test_check = check_skip_markers; osc_test_quality = quality_choices_available; osc_test_activate = activate_controller_focus; osc_test_layout = compute_layout; osc_test_tick = tick; osc_test_menu_activate = activate_keyboard_menu_item; osc_test_menu_items = keyboard_menu_items; osc_test_mouse_down = handle_mouse_down; osc_test_render_subtitles = render_subtitle_menu; osc_test_render_audio = render_audio_menu; osc_test_render_quality = render_quality_menu; osc_test_render_stats = render_stats; osc_test_render_hud = render_osc'))()
            mp.add_timeout(0.05, function()
                local ok, err = pcall(function()
                    {{setup}}
                end)
                if not ok then
                    mp.set_property('{{resultProperty}}', 'Lua error: ' .. tostring(err))
                    return
                end
                mp.commandv('script-message', 'osc-toggle-stats')
            end)
            """);
        var library = SharedLibrary.Value;
        var handle = LoadDelegate<MpvCreate>(library, "mpv_create")();
        try
        {
            var option = LoadDelegate<MpvSetOptionString>(library, "mpv_set_option_string");
            foreach (var (key, value) in new[] { ("vo", "null"), ("ao", "null"), ("idle", "yes"), ("osc", "no"), ("scripts", probe) })
                Assert.True(option(handle, key, value) >= 0);
            Assert.True(LoadDelegate<MpvInitialize>(library, "mpv_initialize")(handle) >= 0);
            var get = LoadDelegate<MpvGetPropertyString>(library, "mpv_get_property_string");
            var free = LoadDelegate<MpvFree>(library, "mpv_free");
            string? rendered = null;
            for (var attempt = 0; attempt < 100 && rendered == null; attempt++)
            {
                var ptr = get(handle, resultProperty);
                try { if (ptr != IntPtr.Zero) rendered = Marshal.PtrToStringUTF8(ptr); }
                finally { if (ptr != IntPtr.Zero) free(ptr); }
                if (rendered == null) Thread.Sleep(10);
            }
            Assert.NotNull(rendered);
            var ass = rendered!.StartsWith('"') ? System.Text.Json.JsonSerializer.Deserialize<string>(rendered)! : rendered;
            Assert.False(ass.StartsWith("Lua error:", StringComparison.Ordinal), ass);
            if (rawAss) return ass;
            var text = System.Text.RegularExpressions.Regex.Replace(ass, @"\{[^}]*\}|\\N", "")
                .Replace("\r", "").Replace("\n", "");
            return text;
        }
        finally
        {
            LoadDelegate<MpvTerminateDestroy>(library, "mpv_terminate_destroy")(handle);
            File.Delete(probe);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MpvGetPropertyString(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MpvFree(IntPtr value);

    [Fact]
    public void BundledLibMpv_LoadsOscWithoutLuaErrors()
    {
        var root = FindRepositoryRoot();
        var libraryPath = Path.Combine(root, "libs", "mpv", "libmpv-2.dll");
        var scriptPath = Path.Combine(root, "libs", "mpv", "scripts", "silo-osc.lua");
        var logPath = Path.Combine(Path.GetTempPath(), $"silo-osc-probe-{Guid.NewGuid():N}.log");

        Assert.True(File.Exists(libraryPath), $"Missing bundled libmpv: {libraryPath}");
        Assert.True(File.Exists(scriptPath), $"Missing OSC script: {scriptPath}");

        var library = IntPtr.Zero;
        var handle = IntPtr.Zero;
        MpvTerminateDestroy? terminate = null;

        try
        {
            library = SharedLibrary.Value;
            var create = LoadDelegate<MpvCreate>(library, "mpv_create");
            var setOption = LoadDelegate<MpvSetOptionString>(library, "mpv_set_option_string");
            var initialize = LoadDelegate<MpvInitialize>(library, "mpv_initialize");
            var commandString = LoadDelegate<MpvCommandString>(library, "mpv_command_string");
            terminate = LoadDelegate<MpvTerminateDestroy>(library, "mpv_terminate_destroy");
            handle = create();
            Assert.NotEqual(IntPtr.Zero, handle);

            void Set(string name, string value) =>
                Assert.True(setOption(handle, name, value) >= 0, $"libmpv rejected {name}={value}");

            Set("vo", "null");
            Set("ao", "null");
            Set("idle", "yes");
            Set("osc", "no");
            Set("log-file", logPath);
            Set("msg-level", "all=v");
            Set("scripts", scriptPath);

            Assert.True(initialize(handle) >= 0, "The bundled libmpv instance did not initialize.");
            Thread.Sleep(350);
            Assert.True(
                commandString(handle, "script-message osc-set-visibility false") >= 0,
                "The OSC rejected its Playing Next hide transition.");
            Assert.True(
                commandString(handle, "script-message osc-set-visibility true") >= 0,
                "The OSC rejected its autoplay re-enable transition.");
            Assert.True(
                commandString(handle, "script-message osc-mouse-move 100 100") >= 0,
                "The OSC rejected its first pointer wake after autoplay.");
            Thread.Sleep(150);
        }
        finally
        {
            if (handle != IntPtr.Zero)
                terminate?.Invoke(handle);
        }

        try
        {
            var log = File.ReadAllText(logPath);
            Assert.Contains("silo-osc.lua", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("syntax error", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("error loading script", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("stack traceback", log, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    private static T LoadDelegate<T>(IntPtr library, string export) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, export));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the SiloPlayer repository root.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr MpvCreate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpvSetOptionString(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpvInitialize(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MpvCommandString(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string command);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MpvTerminateDestroy(IntPtr handle);
}
