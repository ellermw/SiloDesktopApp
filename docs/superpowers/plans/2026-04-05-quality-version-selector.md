# Quality/Version Selector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a quality/version selector to the player OSC matching the Continuum web player's QualityMenu — versions at top, separator, transcode quality tiers below, with Auto and Original options.

**Architecture:** Gear button added to OSC bar triggers a Lua-drawn popup menu. Version data flows from C# to Lua via `osc-set-quality-info` script-message. User selections flow back via `continuum-version-select` and `continuum-quality-select`. Transcode switching uses `POST /playback/transcode/start`. Same proven bidirectional script-message pattern as fullscreen/exit/subtitle.

**Tech Stack:** C# / .NET 8 (WinUI 3), Lua 5.1 (mpv scripting, ASS drawing), libmpv C API

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `libs/mpv/scripts/continuum-osc.lua` | Modify | Gear button layout, draw_gear_icon(), render_quality_menu(), click handlers, osc-set-quality-info handler, mutual exclusion with subtitle menu |
| `src/ContinuumPlayer/Services/PlayerService.cs` | Modify | SendQualityInfoToOsc() on file load, handle continuum-version-select and continuum-quality-select in OnScriptMessage, SwitchQualityTierAsync() |

---

### Task 1: Add gear button to OSC layout and drawing

**Files:**
- Modify: `libs/mpv/scripts/continuum-osc.lua`

- [ ] **Step 1: Add quality menu state to the state table**

In the state table (after the subtitle_menu fields), add:

```lua
    -- Quality menu
    quality_info        = nil,      -- parsed JSON from osc-set-quality-info
    active_quality      = "auto",   -- current quality tier ID
    quality_menu_visible = false,
    quality_menu_overlay = nil,
    quality_menu_items  = {},
```

- [ ] **Step 2: Add draw_gear_icon function**

After the `draw_minimize_icon` function, add a gear icon drawing function. A simple gear can be drawn as a circle with 6 rectangular notches:

```lua
-- Gear/settings icon (circle with notches)
local function draw_gear_icon(ass, cx, cy, size, color, alpha, master_alpha)
    local r_outer = size * 0.35
    local r_inner = size * 0.2
    local notch_w = size * 0.1
    local notch_h = size * 0.12
    -- Center circle
    draw_circle(ass, cx, cy, r_inner, color, alpha, master_alpha)
    -- 6 notches around the circle
    for i = 0, 5 do
        local angle = i * math.pi / 3
        local nx = cx + math.cos(angle) * r_outer
        local ny = cy + math.sin(angle) * r_outer
        draw_rect(ass, nx - notch_w, ny - notch_w, nx + notch_w, ny + notch_w,
            color, alpha, master_alpha)
    end
end
```

- [ ] **Step 3: Add btn_quality to compute_layout**

In `compute_layout()`, insert `btn_quality` between `btn_minimize` and the volume bar. After the minimize button block:

```lua
    -- Quality/settings button
    rx_cursor = rx_cursor - config.small_button_size
    L.btn_quality = {
        x = rx_cursor, y = controls_y - config.small_button_size / 2,
        w = config.small_button_size, h = config.small_button_size,
        cx = rx_cursor + config.small_button_size / 2,
        cy = controls_y
    }
    rx_cursor = rx_cursor - 12
```

- [ ] **Step 4: Add gear icon draw call in render_osc**

In the render function, after the minimize icon draw and before the exit button draw:

```lua
    -- Quality/settings button
    local bq = L.btn_quality
    draw_gear_icon(ass, bq.cx, bq.cy, config.small_button_size,
        config.text_color, "00", ma)
```

- [ ] **Step 5: Add click handler for gear button**

In `handle_mouse_down`, before the minimize check, add:

```lua
    -- Check quality menu toggle
    if L.btn_quality and point_in_rect(mx, my, L.btn_quality) then
        -- Close subtitle menu if open (mutual exclusion)
        if state.subtitle_menu_visible then
            state.subtitle_menu_visible = false
            render_subtitle_menu()
        end
        state.quality_menu_visible = not state.quality_menu_visible
        render_quality_menu()
        return
    end
```

Also update the CC button click handler to close the quality menu:

```lua
    -- Check CC (subtitle menu toggle)
    if L.btn_cc and point_in_rect(mx, my, L.btn_cc) then
        -- Close quality menu if open (mutual exclusion)
        if state.quality_menu_visible then
            state.quality_menu_visible = false
            render_quality_menu()
        end
        state.subtitle_menu_visible = not state.subtitle_menu_visible
        render_subtitle_menu()
        return
    end
```

- [ ] **Step 6: Add quality menu click handling at top of handle_mouse_down**

In `handle_mouse_down`, after the subtitle menu click handling block and before the alpha check, add:

```lua
    -- Quality menu click handling
    if state.quality_menu_visible then
        local handled = false
        for _, item in ipairs(state.quality_menu_items) do
            if point_in_rect(mx, my, item) then
                if item.action == "version" then
                    mp.commandv("script-message", "continuum-version-select", tostring(item.file_id))
                elseif item.action == "quality" then
                    mp.commandv("script-message", "continuum-quality-select", item.tier_id)
                end
                state.quality_menu_visible = false
                render_quality_menu()
                return
            end
        end
        -- Click outside menu — close it
        state.quality_menu_visible = false
        render_quality_menu()
        return
    end
```

- [ ] **Step 7: Commit**

```bash
git add libs/mpv/scripts/continuum-osc.lua
git commit -m "feat: gear button on OSC bar with click handling for quality menu"
```

---

### Task 2: Quality menu rendering in Lua

**Files:**
- Modify: `libs/mpv/scripts/continuum-osc.lua`

- [ ] **Step 1: Define transcode tiers table**

After the state table, add a constant table for the transcode quality tiers:

```lua
local quality_tiers = {
    { id = "auto",      label = "Auto" },
    { id = "original",  label = "Original" },
    { id = "1080p-high", label = "1080p High",  sublabel = "~10 Mbps" },
    { id = "1080p",      label = "1080p",        sublabel = "~6 Mbps" },
    { id = "720p-high",  label = "720p High",    sublabel = "~4 Mbps" },
    { id = "720p",       label = "720p",          sublabel = "~2 Mbps" },
    { id = "480p",       label = "480p",          sublabel = "~1.5 Mbps" },
    { id = "420p",       label = "420p",          sublabel = "~720 kbps" },
}
```

- [ ] **Step 2: Add render_quality_menu function**

After `render_subtitle_menu`, add `render_quality_menu`:

```lua
local function render_quality_menu()
    if not state.quality_menu_visible then
        if state.quality_menu_overlay then
            state.quality_menu_overlay.data = ""
            state.quality_menu_overlay:update()
        end
        state.quality_menu_items = {}
        return
    end

    update_osd_dimensions()
    local ass = assdraw.ass_new()
    local W = state.osd_width
    local H = state.osd_height

    local fs = config.stats_font_size
    local fs_small = math.max(fs - 2, 10)
    local padding = config.stats_padding
    local item_h = config.stats_line_height + 4
    local menu_w = 280

    -- Get version and quality info
    local qi = state.quality_info
    local versions = (qi and qi.versions) or {}
    local active_file_id = (qi and qi.active_file_id) or 0

    -- Calculate menu height
    local num_versions = #versions
    local num_tiers = #quality_tiers
    -- header + versions + separator + tiers
    local menu_h = padding * 2 + item_h  -- header
    if num_versions > 0 then
        menu_h = menu_h + (num_versions * item_h) + 8  -- versions + separator gap
    end
    menu_h = menu_h + (num_tiers * item_h)  -- quality tiers

    -- Position above the quality button
    compute_layout()
    local L = state.layout
    local menu_x = W / 2 - menu_w / 2
    local menu_y = H - config.bar_height - menu_h - 10
    if L.btn_quality then
        menu_x = L.btn_quality.x - menu_w / 2
    end
    if menu_x < 10 then menu_x = 10 end
    if menu_x + menu_w > W - 10 then menu_x = W - menu_w - 10 end
    if menu_y < 10 then menu_y = 10 end

    -- Background
    draw_rounded_rect(ass, menu_x, menu_y, menu_x + menu_w, menu_y + menu_h,
        8, config.bar_bg_color, config.stats_bg_alpha, 1.0)

    local cy = menu_y + padding
    state.quality_menu_items = {}

    -- Header
    draw_text(ass, menu_x + padding, cy + item_h / 2, "Quality",
        fs, config.text_color, "00", 1.0, 4, nil, true)
    cy = cy + item_h

    -- Versions section
    if num_versions > 0 then
        for _, ver in ipairs(versions) do
            local is_active = (ver.file_id == active_file_id)
            local text_color = is_active and config.text_color or config.dim_text_color

            if is_active then
                draw_text(ass, menu_x + padding, cy + item_h / 2, "✓",
                    fs, config.text_color, "00", 1.0, 4)
            end

            local label = ver.label or ver.resolution or "Unknown"
            draw_text(ass, menu_x + padding + 24, cy + item_h / 2, label,
                fs, text_color, "00", 1.0, 4)

            -- Resolution badge right-aligned
            if ver.resolution and ver.resolution ~= "" then
                draw_text(ass, menu_x + menu_w - padding, cy + item_h / 2, ver.resolution,
                    fs_small, config.dim_text_color, "40", 1.0, 6)
            end

            table.insert(state.quality_menu_items, {
                x = menu_x, y = cy, w = menu_w, h = item_h,
                action = "version", file_id = ver.file_id
            })
            cy = cy + item_h
        end

        -- Separator
        cy = cy + 4
        draw_rect(ass, menu_x + padding, cy, menu_x + menu_w - padding, cy + 1,
            config.dim_text_color, "60", 1.0)
        cy = cy + 4
    end

    -- Quality tiers
    for _, tier in ipairs(quality_tiers) do
        local is_active = (tier.id == state.active_quality)
        local text_color = is_active and config.text_color or config.dim_text_color

        if is_active then
            draw_text(ass, menu_x + padding, cy + item_h / 2, "✓",
                fs, config.text_color, "00", 1.0, 4)
        end

        draw_text(ass, menu_x + padding + 24, cy + item_h / 2, tier.label,
            fs, text_color, "00", 1.0, 4)

        -- Sublabel right-aligned
        if tier.sublabel then
            draw_text(ass, menu_x + menu_w - padding, cy + item_h / 2, tier.sublabel,
                fs_small, config.dim_text_color, "40", 1.0, 6)
        end

        table.insert(state.quality_menu_items, {
            x = menu_x, y = cy, w = menu_w, h = item_h,
            action = "quality", tier_id = tier.id
        })
        cy = cy + item_h
    end

    -- Update overlay
    if not state.quality_menu_overlay then
        state.quality_menu_overlay = mp.create_osd_overlay("ass-events")
    end
    state.quality_menu_overlay.data = ass.text
    state.quality_menu_overlay.res_x = W
    state.quality_menu_overlay.res_y = H
    state.quality_menu_overlay.z = 70
    state.quality_menu_overlay:update()
end
```

- [ ] **Step 3: Register script-message handlers for quality data**

In the script-message registration section:

```lua
    mp.register_script_message("osc-set-quality-info", function(json_str)
        local ok, data = pcall(require("mp.utils").parse_json, json_str)
        if ok and data then
            state.quality_info = data
            if data.active_quality then
                state.active_quality = data.active_quality
            end
            if data.active_file_id then
                state.quality_info.active_file_id = data.active_file_id
            end
        end
    end)

    mp.register_script_message("osc-set-active-quality", function(tier_id)
        state.active_quality = tier_id or "auto"
    end)
```

- [ ] **Step 4: Clear quality menu on OSC hide and osc-set-visibility false**

In the `osc-set-visibility` handler, add quality menu cleanup:

```lua
    -- In the osc-set-visibility handler, inside if state.osc_disabled:
            if state.quality_menu_overlay then
                state.quality_menu_overlay.data = ""
                state.quality_menu_overlay:update()
            end
            state.quality_menu_visible = false
```

- [ ] **Step 5: Commit**

```bash
git add libs/mpv/scripts/continuum-osc.lua
git commit -m "feat: quality menu rendering — versions, separator, transcode tiers"
```

---

### Task 3: C# quality data and switching logic

**Files:**
- Modify: `src/ContinuumPlayer/Services/PlayerService.cs`

- [ ] **Step 1: Add SendQualityInfoToOsc method**

After `SendSubtitleListToOsc()`:

```csharp
private void SendQualityInfoToOsc()
{
    if (_mpv == null || _playbackManager?.WatchDetail == null || _playbackManager.CurrentSession == null) return;

    var wd = _playbackManager.WatchDetail;
    var session = _playbackManager.CurrentSession;

    var versions = (wd.Versions ?? []).Select(v => new Dictionary<string, object?>
    {
        ["file_id"] = v.FileId,
        ["label"] = v.FileName ?? $"{v.Resolution} {v.CodecVideo}",
        ["resolution"] = v.Resolution ?? ""
    }).ToArray();

    var info = new Dictionary<string, object?>
    {
        ["versions"] = versions,
        ["active_file_id"] = session.MediaFileId,
        ["active_quality"] = "auto"
    };

    var json = System.Text.Json.JsonSerializer.Serialize(info);
    _mpv.SendScriptMessage("osc-set-quality-info", json);
}
```

- [ ] **Step 2: Call SendQualityInfoToOsc in FileLoaded handler**

In the FileLoaded handler, after `SendSubtitleListToOsc()`:

```csharp
            SendQualityInfoToOsc();
```

- [ ] **Step 3: Handle continuum-version-select in OnScriptMessage**

Add to the switch in `OnScriptMessage`:

```csharp
            case "continuum-version-select":
                if (args.Length > 1 && int.TryParse(args[1], out var fileId))
                {
                    var version = Versions.FirstOrDefault(v => v.FileId == fileId);
                    if (version != null)
                        dispatch.TryEnqueue(() => _ = SwitchVersionAndNotifyAsync(version));
                }
                break;
            case "continuum-quality-select":
                if (args.Length > 1)
                    dispatch.TryEnqueue(() => _ = SwitchQualityTierAsync(args[1]));
                break;
```

- [ ] **Step 4: Add SwitchVersionAndNotifyAsync**

```csharp
private async Task SwitchVersionAndNotifyAsync(FileVersion version)
{
    await SwitchVersionAsync(version);
    // Re-send quality info with new active file ID
    SendQualityInfoToOsc();
    SendMediaInfoToOsc();
    _mpv?.SendScriptMessage("osc-set-active-quality", "auto");
}
```

- [ ] **Step 5: Add SwitchQualityTierAsync**

```csharp
private async Task SwitchQualityTierAsync(string tierId)
{
    if (_mpv == null || _playbackManager == null) return;

    var currentPos = _mpv.Position;

    if (tierId is "auto" or "original")
    {
        // Switch back to direct play — reload the current version
        var currentFileId = _playbackManager.CurrentSession?.MediaFileId;
        var version = Versions.FirstOrDefault(v => v.FileId == currentFileId);
        if (version != null)
        {
            _switchingContent = true;
            try
            {
                await _playbackManager.StopSessionAsync();
                var session = await _playbackManager.StartSessionAsync(version.FileId, currentPos);
                PlayMethod = session.PlayMethod;

                var token = _apiClient.AccessToken;
                var authHeader = token != null ? $"Bearer {token}" : null;
                var streamUrl = _playbackManager.StreamUrl ?? "";
                if (token != null)
                    streamUrl += (streamUrl.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";

                _resumePosition = currentPos;
                _mpv.LoadFile(streamUrl, authHeader);
                _mpv.Play();
                SendMediaInfoToOsc();
            }
            catch (Exception ex)
            {
                LogToFile("player_quality_switch_error.txt", ex.ToString());
                _mpv.ShowOsdText("Quality switch failed", 3000);
            }
            finally
            {
                _switchingContent = false;
            }
        }
    }
    else
    {
        // Transcode tier — map tier ID to settings
        var (resolution, bitrate) = tierId switch
        {
            "1080p-high" => ("1080p", 10000),
            "1080p"      => ("1080p", 6000),
            "720p-high"  => ("720p", 4000),
            "720p"       => ("720p", 2000),
            "480p"       => ("480p", 1500),
            "420p"       => ("420p", 720),
            _ => ("1080p", 6000)
        };

        _switchingContent = true;
        try
        {
            var transcodeResponse = await _playbackApi.StartTranscodeAsync(new TranscodeStartRequest
            {
                SessionId = _playbackManager.SessionId!,
                SeekSeconds = currentPos,
                TargetResolution = resolution,
                TargetCodecVideo = "h264",
                TargetCodecAudio = "aac",
                TargetBitrateKbps = bitrate,
                SegmentDuration = 2,
                SubtitleTrackIndex = -1,
                SubtitleBurnIn = false
            });

            var baseUrl = _apiClient.BaseUrl;
            var manifestPath = transcodeResponse.ManifestUrl;
            if (!manifestPath.StartsWith("http") && !manifestPath.StartsWith("/api/v1"))
                manifestPath = "/api/v1" + manifestPath;
            var streamUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";

            PlayMethod = "transcode";
            _resumePosition = transcodeResponse.PlayerStartSeconds;
            _mpv.LoadFile(streamUrl);
            _mpv.Play();
            SendMediaInfoToOsc();
        }
        catch (Exception ex)
        {
            LogToFile("player_quality_switch_error.txt", ex.ToString());
            _mpv.ShowOsdText("Transcode failed", 3000);
        }
        finally
        {
            _switchingContent = false;
        }
    }

    _mpv?.SendScriptMessage("osc-set-active-quality", tierId);
}
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -p:Platform=x64 -c Release`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add src/ContinuumPlayer/Services/PlayerService.cs
git commit -m "feat: quality switching — version select, transcode tiers, auto/original"
```

---

### Task 4: Build, publish, verify

- [ ] **Step 1: Full build**

Run: `dotnet publish src/ContinuumPlayer/ContinuumPlayer.csproj -c Release -p:Platform=x64`
Expected: Build succeeded

- [ ] **Step 2: Manual test checklist**

1. Play content — OSC shows gear icon on hover (between minimize and volume)
2. Click gear — quality menu opens with versions at top, separator, then quality tiers
3. Verify "Auto" has checkmark (default)
4. If content has multiple versions, verify they appear with file names
5. Click a different version — player reloads with new file, menu closes, version checkmark updates
6. Click gear → select "1080p" — player switches to HLS transcode, resumes at same position
7. Click gear → select "Auto" — player switches back to direct play
8. Click gear while subtitle menu is open — subtitle menu closes, quality menu opens
9. Click CC while quality menu is open — quality menu closes, subtitle menu opens
10. Minimize to mini-bar — quality menu closes, no OSC visible
11. Exit and replay — quality menu works on second play

- [ ] **Step 3: Commit if fixups needed**

```bash
git add -A
git commit -m "fix: quality selector polish"
```
