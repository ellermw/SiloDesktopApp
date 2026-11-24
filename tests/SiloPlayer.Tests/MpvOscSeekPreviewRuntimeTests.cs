namespace SiloPlayer.Tests;

// Production silo-osc.lua executed in the bundled libmpv: the seek tooltip's preview
// (trickplay) image. Official silo-server 3b2b32168 web/src/player/trickplay.ts and
// components/SeekBar.tsx: the tile under the pointer, above the time and chapter label,
// in the wide tooltip clamped to the bar's ends; the tooltip is unchanged without one.
public sealed partial class MpvOscRuntimeTests
{
    private const string SeekPreviewSetup = """
        local s = osc_test_state
        s.osc_disabled, s.current_alpha, s.target_alpha, s.visible = false, 1, 1, true
        s.duration, s.time_pos = 3000, 40
        osc_test_layout()
        local sb = s.layout.seek_bar
        local sc = osc_test_scale()
        manifest = '{"key":"k1","interval_ms":10000,"width":300,"height":168,"columns":10,"rows":10,"count":250,"sheets":3}'
        function hover(seconds)
            s.mouse_in_bar, s.mouse_in_window = true, true
            s.mouse_x, s.mouse_y = sb.x1 + (sb.x2 - sb.x1) * seconds / s.duration, sb.draw_y
            osc_test_render_hud()
            local t = math.floor((s.mouse_x - sb.x1) / (sb.x2 - sb.x1) * s.duration)
            last_label = string.format('%d:%02d', math.floor(t / 60), t % 60)
            return s.osc_overlay.data
        end
        function leave()
            s.mouse_in_bar, s.mouse_x, s.mouse_y = false, -100, -100
            osc_test_render_hud()
        end
        function adds()
            local result = {}
            for _, cmd in ipairs(natives) do if cmd.name == 'overlay-add' and cmd.id == 62 then table.insert(result, cmd) end end
            return result
        end
        function removes(id)
            local count = 0
            for _, cmd in ipairs(natives) do if cmd.name == 'overlay-remove' and cmd.id == id then count = count + 1 end end
            return count
        end
        function ready(sheet)
            handlers['osc-set-seek-preview-sheet']('k1', tostring(sheet), 'ready', 'C:/fixture/sheet' .. sheet .. '.bgra', '256', '143', '10240')
        end
        function expected_box(seconds)
            local w = math.floor(176 * sc)
            local dh = math.floor(w * 168 / 300)
            local mouse_x = sb.x1 + (sb.x2 - sb.x1) * seconds / s.duration
            local cx = math.max(sb.x1 + w / 2, math.min(sb.x2 - w / 2, mouse_x))
            local bottom = sb.draw_y - math.floor(12 * sc)
            return math.floor(cx - w / 2), math.floor(bottom - math.floor(28 * sc) - dh), w, dh
        end
        """;

    [Fact]
    public void SeekPreviewDrawsTheHoveredTileAboveTheTimeAtTheWideTooltipSize()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: SeekPreviewSetup + "\n" + """
            handlers['osc-set-seek-preview'](manifest)
            -- 105 s is thumbnail 10: sheet 0, row 1, column 0. The sheet is not decoded yet.
            hover(105)
            assert(#adds() == 0, 'A preview was drawn before its sheet loaded')
            local focus = sent_matching('silo-seek-preview-focus')
            assert(#focus == 1 and focus[1] == 'silo-seek-preview-focus|k1|0', 'Hovering did not report the sheet under the pointer: ' .. table.concat(focus, ','))
            ready(0)
            local data = hover(105)
            local add = adds()[1]
            assert(add, 'The decoded sheet was not drawn')
            local x, y, w, h = expected_box(105)
            assert(add.file == 'C:/fixture/sheet0.bgra' and add.fmt == 'bgra', 'Wrong sheet file')
            assert(add.offset == 143 * 10240 and add.w == 256 and add.h == 143 and add.stride == 10240, 'Wrong tile offset/size: ' .. tostring(add.offset))
            assert(add.x == x and add.y == y and add.dw == w and add.dh == h, 'Preview not at the tooltip image area: ' .. add.x .. ',' .. add.y .. ' ' .. add.dw .. 'x' .. add.dh)
            local _, label_y = hud_event(data, last_label)
            assert(label_y and label_y > add.y + add.dh, 'The time label is not below the preview')
            -- 1234 s is thumbnail 123: sheet 1, row 2, column 3 once sheet 1 loads.
            hover(1234)
            assert(sent_matching('silo-seek-preview-focus')[2] == 'silo-seek-preview-focus|k1|1', 'Next sheet was not reported')
            assert(removes(62) == 1, 'A loading sheet kept the previous tile on screen')
            ready(1)
            hover(1234)
            local next_add = adds()[2]
            assert(next_add and next_add.file == 'C:/fixture/sheet1.bgra' and next_add.offset == 2 * 143 * 10240 + 3 * 256 * 4, 'Wrong tile on the next sheet')
            mp.set_property('user-data/seek-preview', 'tile above label')
            """, rawAss: true, resultProperty: "user-data/seek-preview", beforeScript: AutoplayHudProbe);
        Assert.Equal("tile above label", result);
    }

    [Fact]
    public void SeekPreviewMovesWithinASheetWithoutRemovingOrRedrawingItsBitmap()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: SeekPreviewSetup + "\n" + """
            handlers['osc-set-seek-preview'](manifest)
            ready(0)
            hover(105)
            hover(105)
            assert(#adds() == 1, 'Redrawing the same frame issued another overlay-add')
            -- The pointer moving within one tile only moves the same mapping along.
            hover(106)
            assert(#adds() == 2 and adds()[2].offset == adds()[1].offset and adds()[2].x > adds()[1].x, 'Moving within a tile did not follow the pointer with the same tile')
            -- Another tile of the same sheet replaces the bitmap in place.
            hover(500)
            assert(#adds() == 3 and adds()[3].file == 'C:/fixture/sheet0.bgra' and adds()[3].offset == 5 * 143 * 10240, 'Same-sheet tile change did not map the new tile')
            assert(removes(62) == 0, 'Moving within a sheet removed the preview (flicker)')
            assert(#sent_matching('silo-seek-preview-focus') == 1, 'Moving within a sheet asked the host again')
            leave()
            assert(removes(62) == 1, 'Hiding the tooltip left the preview on screen')
            local focus = sent_matching('silo-seek-preview-focus')
            assert(focus[#focus] == 'silo-seek-preview-focus|k1|-1', 'Leaving the bar did not stop sheet loading')
            mp.set_property('user-data/seek-preview', 'steady')
            """, rawAss: true, resultProperty: "user-data/seek-preview", beforeScript: AutoplayHudProbe);
        Assert.Equal("steady", result);
    }

    [Fact]
    public void SeekPreviewClampsToTheSeekBarEnds()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: SeekPreviewSetup + "\n" + """
            handlers['osc-set-seek-preview'](manifest)
            ready(0); ready(2)
            hover(1)
            local left = adds()[1]
            assert(left and left.x == math.floor(sb.x1), 'Preview not clamped to the bar start: ' .. tostring(left and left.x))
            hover(2999)
            local right = adds()[2]
            assert(right and right.x + right.dw == math.floor(sb.x2), 'Preview not clamped to the bar end: ' .. tostring(right and right.x + right.dw))
            -- Past the last thumbnail the last one stays (official clamp to count - 1).
            assert(right.offset == 4 * 143 * 10240 + 9 * 256 * 4, 'Last thumbnail is not the clamped tile')
            mp.set_property('user-data/seek-preview', 'clamped')
            """, rawAss: true, resultProperty: "user-data/seek-preview", beforeScript: AutoplayHudProbe);
        Assert.Equal("clamped", result);
    }

    [Fact]
    public void SeekPreviewLeavesTheTooltipUnchangedWhileASheetLoadsOrFailsOrWithoutPreviews()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: SeekPreviewSetup + "\n" + """
            local baseline = hover(105)
            handlers['osc-set-seek-preview'](manifest)
            local loading = hover(105)
            assert(loading == baseline, 'The tooltip changed while its sheet loads')
            handlers['osc-set-seek-preview-sheet']('k1', '0', 'failed', '', '0', '0', '0')
            assert(hover(105) == baseline, 'A failed sheet changed the tooltip')
            assert(#adds() == 0, 'A failed sheet was drawn')
            -- A stale key (an older manifest) is ignored.
            handlers['osc-set-seek-preview-sheet']('k0', '0', 'ready', 'C:/fixture/old.bgra', '256', '143', '10240')
            hover(105); assert(#adds() == 0, 'A sheet for another manifest was drawn')
            ready(0); hover(105); assert(#adds() == 1, 'Ready sheet not drawn')
            handlers['osc-set-seek-preview']('')
            assert(removes(62) == 1, 'Withdrawn previews stayed on screen')
            assert(hover(105) == baseline, 'Tooltip without previews differs from today')
            mp.set_property('user-data/seek-preview', 'unchanged')
            """, rawAss: true, resultProperty: "user-data/seek-preview", beforeScript: AutoplayHudProbe);
        Assert.Equal("unchanged", result);
    }

    [Fact]
    public void SeekPreviewTakesTheChapterImageAreaAndKeepsTheChapterTitle()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: SeekPreviewSetup + "\n" + """
            handlers['osc-set-chapters']('[{"index":0,"start_seconds":0,"end_seconds":3000,"title":"Opening","thumbnail_url":"c0"}]')
            s.chapter_thumbnails[0] = {index=0,file='C:/fixture/chapter.bgra',width=160,height=90,stride=640}
            hover(105)
            local chapter_shown = false
            for _, cmd in ipairs(natives) do if cmd.name == 'overlay-add' and cmd.id == 63 then chapter_shown = true end end
            assert(chapter_shown, 'Fixture chapter image did not draw without previews')
            handlers['osc-set-seek-preview'](manifest)
            ready(0)
            local data = hover(105)
            assert(#adds() == 1, 'Preview not drawn over a chapter')
            assert(removes(63) >= 1, 'The chapter image kept the image area under the preview')
            assert(data:find('Opening', 1, true), 'The chapter title left the tooltip')
            mp.set_property('user-data/seek-preview', 'preview over chapter')
            """, rawAss: true, resultProperty: "user-data/seek-preview", beforeScript: AutoplayHudProbe);
        Assert.Equal("preview over chapter", result);
    }

    [Fact]
    public void SeekPreviewIsRemovedOnLoadingHiddenControlsEvictionAndANewFile()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: SeekPreviewSetup + "\n" + """
            handlers['osc-set-seek-preview'](manifest)
            ready(0); hover(105)
            handlers['osc-set-loading']('true')
            assert(removes(62) == 1, 'The loading cover kept the preview')
            hover(105)
            assert(#adds() == 1, 'A preview was drawn over the loading cover')
            handlers['osc-set-loading']('false')
            hover(105); assert(#adds() == 2, 'Preview did not return after loading')
            -- Evicting the mapped sheet removes the overlay so its file can be deleted.
            handlers['osc-set-seek-preview-sheet']('k1', '0', 'evicted', '', '0', '0', '0')
            assert(removes(62) == 2, 'An evicted sheet stayed mapped')
            ready(0); hover(105)
            handlers['osc-set-visibility']('false')
            assert(removes(62) == 3, 'Hidden controls kept the preview')
            handlers['osc-set-visibility']('true')
            s.current_alpha, s.target_alpha = 1, 1
            hover(105)
            -- A new file's manifest (new key) drops every sheet of the old one.
            handlers['osc-set-seek-preview']((manifest:gsub('"k1"', '"k2"')))
            assert(removes(62) == 4, 'A new file kept the previous preview')
            assert(next(s.seek_preview_sheets) == nil, 'Old sheets survived a new manifest')
            -- A refreshed manifest with the same key keeps decoded sheets and asks again.
            handlers['osc-set-seek-preview-sheet']('k2', '0', 'ready', 'C:/fixture/k2.bgra', '256', '143', '10240')
            handlers['osc-set-seek-preview']((manifest:gsub('"k1"', '"k2"')))
            assert(s.seek_preview_sheets[0] and s.seek_preview_focus == nil, 'A URL refresh dropped decoded sheets')
            mp.set_property('user-data/seek-preview', 'removed')
            """, rawAss: true, resultProperty: "user-data/seek-preview", beforeScript: AutoplayHudProbe);
        Assert.Equal("removed", result);
    }

    [Fact]
    public void SeekPreviewShowsAboveTheDragTimeWhileScrubbing()
    {
        var result = CaptureStats(1920, 1080, 1920, 1080, false, setup: SeekPreviewSetup + "\n" + """
            handlers['osc-set-seek-preview'](manifest)
            ready(0)
            s.dragging_seek, s.seek_drag_pos = true, 105 / s.duration
            s.mouse_in_bar = true
            local data = (function() osc_test_render_hud(); return s.osc_overlay.data end)()
            last_label = '1:45'
            local add = adds()[1]
            assert(add and add.offset == 143 * 10240, 'Dragging did not show the preview of the drag position')
            local _, label_y = hud_event(data, last_label)
            assert(label_y and add.y + add.dh < label_y, 'Drag preview overlaps the drag time')
            s.dragging_seek = false; leave()
            assert(removes(62) == 1, 'Preview stayed after the drag ended')
            mp.set_property('user-data/seek-preview', 'scrub')
            """, rawAss: true, resultProperty: "user-data/seek-preview", beforeScript: AutoplayHudProbe);
        Assert.Equal("scrub", result);
    }
}
