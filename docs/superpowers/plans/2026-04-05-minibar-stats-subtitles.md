# Phase 1: Mini-Bar, Stats Overlay, Subtitle Menu — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix mini-bar thumbnail size/OSC overlay, redesign stats to match Continuum web player exactly, add subtitle selection menu with online search.

**Architecture:** All OSC features (stats, subtitle menu) are drawn in Lua using ASS primitives. Data flows from C# to Lua via script-messages (JSON payloads). User actions flow from Lua to C# via `continuum-*` script-messages. Same proven pattern as fullscreen/exit/minimize.

**Tech Stack:** C# / .NET 8 (WinUI 3), Lua 5.1 (mpv scripting, ASS drawing), libmpv C API

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `src/ContinuumPlayer/Controls/MiniPlayerBar.xaml` | Modify | Thumbnail 200x112, bar height 132 |
| `src/ContinuumPlayer/Services/PlayerService.cs` | Modify | Thumbnail DPI sizing, send media info/subtitles/visibility to Lua, handle subtitle select/search messages |
| `src/ContinuumPlayer.Core/Api/PlaybackApi.cs` | Modify | Add subtitle search + download API methods |
| `libs/mpv/scripts/continuum-osc.lua` | Modify | Stats overlay (4-section), subtitle popup menu, OSC disable flag, new script-message handlers |

---

### Task 1: Mini-bar XAML and thumbnail sizing

**Files:**
- Modify: `src/ContinuumPlayer/Controls/MiniPlayerBar.xaml`
- Modify: `src/ContinuumPlayer/Services/PlayerService.cs`

- [ ] **Step 1: Update MiniPlayerBar.xaml**

Change the UserControl Height from 100 to 132. Change the thumbnail Grid from `Width="100" Height="80"` to `Width="200" Height="112"`:

```xml
<!-- In MiniPlayerBar.xaml -->
<!-- Line 9: Change Height="100" to Height="132" -->
    Height="132">

<!-- Line 24-25: Change thumbnail Grid -->
            <Grid Grid.Column="0" Width="200" Height="112" Margin="0,10"
                  Tapped="VideoThumbnail_Tapped" Background="Transparent" CornerRadius="6" />
```

- [ ] **Step 2: Update PlayerService.PositionVideoForMiniBar DPI calculations**

In `PlayerService.cs`, update `PositionVideoForMiniBar()` to use the new dimensions:

```csharp
private void PositionVideoForMiniBar()
{
    if (_videoWindow == null) return;
    var mw = App.MainWindowInstance;
    if (mw == null) { _videoWindow.Hide(); return; }

    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);
    GetWindowRect(hwnd, out var windowRect);

    double dpi = GetDpiForWindow(hwnd);
    double scale = dpi / 96.0;

    // Mini-bar thumbnail: 200x112 logical pixels (16:9)
    int thumbW = (int)(200 * scale);
    int thumbH = (int)(112 * scale);
    int thumbX = windowRect.Left + (int)(12 * scale);
    int thumbY = windowRect.Bottom - (int)(132 * scale) + (int)(10 * scale);

    _videoWindow.PositionAt(thumbX, thumbY, thumbW, thumbH);
}
```

- [ ] **Step 3: Update MainWindow.xaml.cs NavView margin**

In `MainWindow.xaml.cs`, line 236, change the bottom margin from 100 to 132:

```csharp
case PlayerState.Minimized:
    // ...
    NavView.Margin = new Thickness(0, 0, 0, 132);
    // ...
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -p:Platform=x64 -c Release`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/ContinuumPlayer/Controls/MiniPlayerBar.xaml src/ContinuumPlayer/Services/PlayerService.cs src/ContinuumPlayer/MainWindow.xaml.cs
git commit -m "fix: mini-bar thumbnail 200x112, bar height 132"
```

---

### Task 2: OSC visibility control for mini-bar

**Files:**
- Modify: `src/ContinuumPlayer/Services/PlayerService.cs`
- Modify: `libs/mpv/scripts/continuum-osc.lua`

- [ ] **Step 1: Send visibility messages from PlayerService**

In `PlayerService.SetState()`, after the video window show/hide/position logic, send OSC visibility:

```csharp
// In SetState, after the existing if/else block for window visibility:
if (newState == PlayerState.Minimized)
    _mpv?.SendScriptMessage("osc-set-visibility", "false");
else if (newState == PlayerState.Expanded || newState == PlayerState.Fullscreen)
    _mpv?.SendScriptMessage("osc-set-visibility", "true");
```

- [ ] **Step 2: Add osc_disabled state and handler in Lua**

In the `state` table (after line ~127), add:

```lua
    osc_disabled    = false,  -- true when mini-bar is active (no OSC rendering)
```

In the script-message registration section (after `osc-fullscreen-state` handler), add:

```lua
    mp.register_script_message("osc-set-visibility", function(val)
        state.osc_disabled = (val == "false")
        if state.osc_disabled then
            -- Clear both overlays when hiding
            if state.osc_overlay then
                state.osc_overlay.data = ""
                state.osc_overlay:update()
            end
            if state.stats_overlay then
                state.stats_overlay.data = ""
                state.stats_overlay:update()
            end
            state.stats_visible = false
        end
    end)
```

- [ ] **Step 3: Guard tick() and input handlers**

At the top of `tick()` function, add:

```lua
local function tick()
    if state.osc_disabled then return end
    -- ... existing code
```

At the top of `handle_mouse_down()`, add the same guard:

```lua
local function handle_mouse_down()
    if state.osc_disabled then return end
    -- ... existing code
```

Same for `handle_mouse_down_right()` and `handle_mouse_up()`.

- [ ] **Step 4: Commit**

```bash
git add src/ContinuumPlayer/Services/PlayerService.cs libs/mpv/scripts/continuum-osc.lua
git commit -m "feat: hide OSC when in mini-bar mode via osc-set-visibility"
```

---

### Task 3: Stats overlay redesign (4 sections matching web player)

**Files:**
- Modify: `src/ContinuumPlayer/Services/PlayerService.cs`
- Modify: `libs/mpv/scripts/continuum-osc.lua`

- [ ] **Step 1: Add media info state variables in Lua**

In the `state` table, add after the existing fields:

```lua
    -- Media info (from host via osc-set-media-info)
    media_info      = nil,     -- parsed JSON table with original media details
    play_method_str = "",      -- "Direct Play" / "Direct Streaming" / "Transcode"
    stream_type_str = "",      -- "Progressive" / "HLS"
    protocol_str    = "",      -- "https" / "http"
    stream_codec_video = "",   -- e.g. "HEVC (direct)"
    stream_codec_audio = "",   -- e.g. "AAC (transcoded)"
```

- [ ] **Step 2: Add osc-set-media-info handler in Lua**

Register the script-message handler to parse the JSON payload:

```lua
    mp.register_script_message("osc-set-media-info", function(json_str)
        local ok, data = pcall(require("mp.utils").parse_json, json_str)
        if ok and data then
            state.media_info = data
        end
    end)

    mp.register_script_message("osc-set-stream-info", function(play_method, stream_type, protocol, video_codec, audio_codec)
        state.play_method_str = play_method or ""
        state.stream_type_str = stream_type or ""
        state.protocol_str = protocol or ""
        state.stream_codec_video = video_codec or ""
        state.stream_codec_audio = audio_codec or ""
    end)
```

- [ ] **Step 3: Send media info from C# on file load**

In `PlayerService.cs`, in the `FileLoaded` handler (after `ContentLoaded?.Invoke()`), add a method call to send media info:

```csharp
// In the FileLoaded handler, after ContentLoaded?.Invoke():
SendMediaInfoToOsc();
```

Add the method:

```csharp
private void SendMediaInfoToOsc()
{
    if (_mpv == null || _playbackManager?.WatchDetail == null || _playbackManager.CurrentSession == null) return;

    var wd = _playbackManager.WatchDetail;
    var session = _playbackManager.CurrentSession;
    var version = wd.Versions?.FirstOrDefault(v => v.FileId == session.MediaFileId);
    if (version == null) return;

    // Get the active audio track info
    var audioTrack = version.AudioTracks?.ElementAtOrDefault(session.AudioTrackIndex);

    var info = new
    {
        container = version.Container ?? "",
        file_size = version.FileSize,
        bitrate = version.Bitrate,
        codec_video = version.CodecVideo ?? "",
        codec_audio = version.CodecAudio ?? "",
        hdr = version.Hdr,
        audio_channels = audioTrack?.Channels ?? version.AudioChannels ?? 0,
        audio_title = audioTrack?.Title ?? audioTrack?.EmbeddedTitle ?? "",
        audio_sample_rate = 0, // Not in our model yet
        resolution = version.Resolution ?? ""
    };

    var json = System.Text.Json.JsonSerializer.Serialize(info,
        new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower });
    _mpv.SendScriptMessage("osc-set-media-info", json);

    // Send stream info
    var pi = session.PlaybackInfo;
    var playMethodDisplay = session.PlayMethod switch
    {
        "direct" => "Direct Play",
        "remux" => "Direct Streaming",
        "transcode" => "Transcode",
        _ => session.PlayMethod
    };
    var streamType = pi?.StreamType switch
    {
        "hls" => "HLS",
        _ => "Progressive"
    };
    var streamUrl = _playbackManager.StreamUrl ?? "";
    var protocol = streamUrl.StartsWith("https") ? "https" : "http";

    // Build codec display strings with suffix
    var vcSuffix = session.PlayMethod == "direct" ? "(direct)" : session.PlayMethod == "remux" ? "(copy)" : "(transcoded)";
    var acSuffix = (pi?.TranscodeAudio == true) ? "(transcoded)" : (session.PlayMethod == "direct" ? "(direct)" : "(copy)");
    var vcDisplay = $"{(pi?.VideoCodec ?? version.CodecVideo ?? "").ToUpper()} {vcSuffix}";
    var acDisplay = $"{(pi?.AudioCodec ?? version.CodecAudio ?? "").ToUpper()} {acSuffix}";

    _mpv.SendScriptMessage("osc-set-stream-info", playMethodDisplay, streamType, protocol, vcDisplay, acDisplay);
}
```

- [ ] **Step 4: Rewrite render_stats() in Lua**

Replace the entire `render_stats()` function with the 4-section web player layout. This is a complete rewrite (~150 lines). The function draws:
- Header bar with "Playback Info" title and X close button
- Section 1: "PLAYER" — Player, Play method, Protocol, Stream type
- Section 2: "VIDEO INFO" — Player dimensions, Video resolution, Dropped frames, Corrupted frames
- Section 3: "PLAYBACK STREAM INFO" — Video codec, Audio codec
- Section 4: "ORIGINAL MEDIA INFO" — Container, Size, Bitrate, Video codec, Video bitrate, Video range type, Audio codec, Audio bitrate, Audio channels, Audio sample rate

Each section has an uppercase header label, and rows with left-aligned labels (white/60) and right-aligned values (white/90).

The key drawing helpers needed:
- `draw_stats_section_header(ass, x, y, w, text, alpha)` — uppercase section label
- `draw_stats_row(ass, x, y, w, label, value, alpha)` — label/value pair
- `format_file_size(bytes)` — returns "7.1 GiB" / "1.2 GiB" etc.
- `format_bitrate(bps)` — returns "22.5 Mbps" / "640 kbps"

Live data (Section 2) reads from mpv properties on each render:
- `video-params/w`, `video-params/h` for resolution
- `vo-delayed-frame-count` + `decoder-frame-drop-count` for dropped frames

- [ ] **Step 5: Build and verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -p:Platform=x64 -c Release`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/ContinuumPlayer/Services/PlayerService.cs libs/mpv/scripts/continuum-osc.lua
git commit -m "feat: stats overlay redesign — 4 sections matching Continuum web player"
```

---

### Task 4: Subtitle API additions

**Files:**
- Modify: `src/ContinuumPlayer.Core/Api/PlaybackApi.cs`

- [ ] **Step 1: Add search and download methods to PlaybackApi**

```csharp
// Add to PlaybackApi class:

public Task<SubtitleSearchResponse> SearchSubtitlesAsync(int mediaFileId, string[] languages, CancellationToken ct = default)
    => client.PostAsync<SubtitleSearchResponse>("/api/v1/subtitles/search",
        new { media_file_id = mediaFileId, languages }, ct);

public Task<SubtitleDownloadResponse> DownloadSubtitleAsync(int mediaFileId, string provider, string subtitleId, string language, string format, CancellationToken ct = default)
    => client.PostAsync<SubtitleDownloadResponse>("/api/v1/subtitles/download",
        new { media_file_id = mediaFileId, provider, subtitle_id = subtitleId, language, format }, ct);
```

- [ ] **Step 2: Add response models**

```csharp
// Add after SubtitleEntry class:

public class SubtitleSearchResponse
{
    public List<SubtitleSearchResult> Results { get; set; } = [];
}

public class SubtitleSearchResult
{
    public string Provider { get; set; } = "";
    public string SubtitleId { get; set; } = "";
    public string Language { get; set; } = "";
    public string? ReleaseName { get; set; }
    public string Format { get; set; } = "srt";
    public double Score { get; set; }
    public bool HearingImpaired { get; set; }
}

public class SubtitleDownloadResponse
{
    public int Id { get; set; }
    public string Language { get; set; } = "";
    public string Format { get; set; } = "";
}
```

- [ ] **Step 3: Build and commit**

```bash
dotnet build src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj
git add src/ContinuumPlayer.Core/Api/PlaybackApi.cs
git commit -m "feat: add subtitle search + download API methods"
```

---

### Task 5: Send subtitle data to Lua + handle selections

**Files:**
- Modify: `src/ContinuumPlayer/Services/PlayerService.cs`
- Modify: `libs/mpv/scripts/continuum-osc.lua`

- [ ] **Step 1: Send subtitle list to Lua on file load**

In `PlayerService.cs`, add a method to send subtitle track data:

```csharp
private void SendSubtitleListToOsc()
{
    if (_mpv == null || _playbackManager?.CurrentSession == null) return;

    var tracks = _playbackManager.CurrentSession.SubtitleUrls ?? [];
    var jsonTracks = tracks.Select(t => new
    {
        index = t.Index,
        language = t.Language ?? "",
        label = t.Label ?? "",
        source = t.Source ?? "embedded",
        codec = t.Codec ?? "",
        forced = t.Forced
    }).ToArray();

    var json = System.Text.Json.JsonSerializer.Serialize(jsonTracks,
        new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower });
    _mpv.SendScriptMessage("osc-set-subtitles", json);
    _mpv.SendScriptMessage("osc-set-active-subtitle", "-1"); // default: off
}
```

Call `SendSubtitleListToOsc()` in the FileLoaded handler after `SendMediaInfoToOsc()`.

- [ ] **Step 2: Handle continuum-subtitle-select in OnScriptMessage**

Add cases to `OnScriptMessage`:

```csharp
case "continuum-subtitle-select":
    if (args.Length > 1 && int.TryParse(args[1], out var subIdx))
    {
        _mpv?.SetSubtitleTrack(subIdx <= 0 ? 0 : subIdx);
        _mpv?.SendScriptMessage("osc-set-active-subtitle", args[1]);
    }
    break;
case "continuum-subtitle-search":
    dispatch.TryEnqueue(() => _ = SearchAndDownloadSubtitlesAsync());
    break;
```

- [ ] **Step 3: Add SearchAndDownloadSubtitlesAsync method**

```csharp
private async Task SearchAndDownloadSubtitlesAsync()
{
    if (_playbackManager?.CurrentSession == null) return;
    var fileId = _playbackManager.CurrentSession.MediaFileId;

    try
    {
        var results = await _playbackApi.SearchSubtitlesAsync(fileId, ["en"]);
        if (results.Results.Count == 0)
        {
            _mpv?.ShowOsdText("No subtitles found", 3000);
            return;
        }

        // Download the best result
        var best = results.Results[0];
        await _playbackApi.DownloadSubtitleAsync(fileId, best.Provider, best.SubtitleId, best.Language, best.Format);
        _mpv?.ShowOsdText($"Downloaded: {best.Language} subtitle", 3000);

        // Refresh subtitle list — re-fetch watch detail and re-send to Lua
        // The downloaded subtitle will now appear in the session's subtitle URLs
        // For now, add it to mpv directly
        var baseUrl = _apiClient.BaseUrl;
        var token = _apiClient.AccessToken;
        var sessionId = _playbackManager.SessionId;
        // Reload subtitles by re-fetching and re-sending the list
        Task.Run(() => LoadSubtitles());
        SendSubtitleListToOsc();
    }
    catch (Exception ex)
    {
        LogToFile("player_subtitle_error.txt", ex.ToString());
        _mpv?.ShowOsdText("Subtitle search failed", 3000);
    }
}
```

- [ ] **Step 4: Register Lua handlers for subtitle data**

In Lua, add state fields:

```lua
    -- Subtitle menu
    subtitle_tracks = {},      -- array from osc-set-subtitles JSON
    active_subtitle = -1,      -- current active index (-1 = off)
    subtitle_menu_visible = false,
```

Register script-message handlers:

```lua
    mp.register_script_message("osc-set-subtitles", function(json_str)
        local ok, data = pcall(require("mp.utils").parse_json, json_str)
        if ok and data then
            state.subtitle_tracks = data
        end
    end)

    mp.register_script_message("osc-set-active-subtitle", function(idx)
        state.active_subtitle = tonumber(idx) or -1
    end)
```

- [ ] **Step 5: Commit**

```bash
git add src/ContinuumPlayer/Services/PlayerService.cs libs/mpv/scripts/continuum-osc.lua
git commit -m "feat: subtitle data flow — C# sends tracks to Lua, handles select/search"
```

---

### Task 6: Subtitle menu UI in Lua

**Files:**
- Modify: `libs/mpv/scripts/continuum-osc.lua`

- [ ] **Step 1: Add subtitle menu rendering function**

Add `render_subtitle_menu()` function that draws:
- Background panel (~240px wide) anchored above CC button position
- Header: "Subtitles" text
- "Off" item with checkmark when `state.active_subtitle == -1`
- Divider line
- Track items sorted by source priority (external=0, downloaded=1, embedded=2):
  - Language name (from `LanguageCodeToName` equivalent in Lua)
  - Source badge text right-aligned ("External"/"Downloaded"/"Embedded")
  - Checkmark when active
- Divider line
- "Search Online..." footer item

Each item has a stored hit rect for click detection.

- [ ] **Step 2: Toggle subtitle menu on CC button click**

In `handle_mouse_down()`, change the CC button handler from `cycle sub` to toggle the subtitle menu:

```lua
    -- Check CC (subtitle menu toggle)
    if L.btn_cc and point_in_rect(mx, my, L.btn_cc) then
        state.subtitle_menu_visible = not state.subtitle_menu_visible
        if state.subtitle_menu_visible then
            render_subtitle_menu()
        else
            clear_subtitle_menu()
        end
        return
    end
```

- [ ] **Step 3: Handle clicks inside the subtitle menu**

When `state.subtitle_menu_visible` is true, check mouse clicks against the stored menu item rects before checking other OSC buttons. On match:
- "Off" item: send `script-message continuum-subtitle-select -1`
- Track item: send `script-message continuum-subtitle-select <index>`
- "Search Online...": send `script-message continuum-subtitle-search`
- Close menu after selection

- [ ] **Step 4: Clear menu on outside click or Escape**

If the menu is visible and the click is outside the menu rect, close it. Also close when the OSC hides.

- [ ] **Step 5: Render menu on top of OSC**

Use a separate overlay with z=70 (above OSC at z=50, above stats at z=60) so it doesn't interfere with other overlays.

- [ ] **Step 6: Commit**

```bash
git add libs/mpv/scripts/continuum-osc.lua
git commit -m "feat: subtitle selection menu — language, source badges, search online"
```

---

### Task 7: Build, publish, and verify

- [ ] **Step 1: Full build**

Run: `dotnet publish src/ContinuumPlayer/ContinuumPlayer.csproj -c Release -p:Platform=x64`
Expected: Build succeeded

- [ ] **Step 2: Verify no stale references**

```bash
grep -rn "cycle.*sub" libs/mpv/scripts/continuum-osc.lua  # should only be in right-click handler
```

- [ ] **Step 3: Manual test checklist**

1. Play content — OSC shows on hover, stats button works
2. Press `i` — stats overlay shows 4 sections matching web player layout
3. Verify Section 1: Player = "libmpv (GPU)", Play method, Protocol, Stream type
4. Verify Section 2: Player dimensions, Video resolution, Dropped frames
5. Verify Section 3: Video codec with (direct)/(copy)/(transcoded), Audio codec same
6. Verify Section 4: Container, Size, Bitrate, Video codec, HDR info, Audio details
7. Click CC button — subtitle menu opens above CC button
8. Menu shows "Off" (checked), track list with language names + source badges
9. Click a subtitle track — subtitles appear on video, menu closes, checkmark moves
10. Click CC → "Off" — subtitles disappear
11. Click CC → "Search Online..." — OSD shows "Downloaded: en subtitle" or "No subtitles found"
12. Minimize to mini-bar — OSC disappears, only live video shows in 200x112 thumbnail
13. Expand from mini-bar — OSC works again
14. Exit and replay — everything still works

- [ ] **Step 4: Final commit if needed**

```bash
git add -A
git commit -m "fix: Phase 1 complete — mini-bar, stats overlay, subtitle menu"
```
