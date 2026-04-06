# Phase 1: Mini-Bar Fixes, Stats Overlay, Subtitle Menu — Design Spec

**Date:** 2026-04-05
**Status:** Approved

## Scope

Fix mini-bar thumbnail size and OSC overlay, redesign video stats to match Continuum web player exactly, add subtitle selection menu with online search.

## Mini-Bar Changes

### Thumbnail Size
- 200x112 logical pixels (16:9 aspect ratio)
- DPI-scaled in `PositionVideoForMiniBar()` using `GetDpiForWindow`
- MiniPlayerBar.xaml Grid column width: 200, height: 112

### Bar Height
- Increase from 100 to 132 (112 thumbnail + 10px top + 10px bottom margin)

### OSC Hiding
- On `SetState(Minimized)`: C# sends `script-message osc-set-visibility false`
- Lua sets `state.osc_disabled = true`, skips all rendering and input handling
- On `SetState(Expanded)`: C# sends `script-message osc-set-visibility true`
- Mini-bar thumbnail shows only live video, no OSC overlay

## Video Stats Overlay

Replaces the existing simple stats display entirely. Matches the Continuum web player's `PlaybackInfoOverlay.tsx` exactly.

### Layout
- Position: top-left of player
- Fixed width: ~320px in OSD coordinates
- Background: semi-transparent black (85% opacity)
- Header: "Playback Info" with close button (X)
- Sections separated by spacing, labels uppercase small text

### Section 1: "Player"
| Field | Source |
|---|---|
| Player | Static: "libmpv (GPU)" |
| Play method | From `osc-set-play-method` script-message |
| Protocol | Parsed from stream URL (https/http) |
| Stream type | From play method: "Progressive" for direct/remux, "HLS" for transcode |

### Section 2: "Video Info" (live, updated every 1s)
| Field | Source |
|---|---|
| Player dimensions | OSD width x height |
| Video resolution | mpv `video-params/w` x `video-params/h` |
| Dropped frames | mpv `vo-delayed-frame-count` + `decoder-frame-drop-count` |
| Corrupted frames | "0" (not exposed by mpv) |

### Section 3: "Playback Stream Info"
| Field | Source |
|---|---|
| Video codec | From media info + play method suffix: "(direct)" / "(transcoded)" / "(copy)" |
| Audio codec | Same pattern |

### Section 4: "Original Media Info"
| Field | Source |
|---|---|
| Container | From `osc-set-media-info` JSON |
| Size | Formatted from bytes (e.g., "7.1 GiB") |
| Bitrate | Formatted (e.g., "22.5 Mbps") |
| Video codec | Codec + profile (e.g., "HEVC Main 10") |
| Video bitrate | Formatted |
| Video range type | HDR/DV/SDR with profile detail |
| Audio codec | Full name with title (e.g., "EAC3 Dolby Digital Plus + Dolby Atmos") |
| Audio bitrate | Formatted |
| Audio channels | Channel count |
| Audio sample rate | In Hz |

### Data Flow
- C# sends `osc-set-media-info <json>` on file load with all Section 4 fields from WatchDetailResponse
- Sections 1-3 use mix of script-messages and mpv property reads
- Toggle: `i` key and stats button on OSC

## Subtitle Menu

### Toggle
- Click CC button on OSC opens popup menu anchored above the button

### Menu Structure
- Header: "Subtitles"
- "Off" option with checkmark when no subtitle active
- Divider
- Track list sorted by source priority: External > Downloaded > Embedded
- Each track: language name + source badge (right-aligned)
- Secondary line if label differs from language name
- Active track has checkmark
- Divider
- "Search Online..." footer button

### Visual Style (matching web player)
- Background: semi-transparent black (85% opacity)
- Active text: white/90, inactive: white/70
- Source badge: small text with subtle background
- Checkmark: "checkmark" character
- Menu width: ~240px OSD coords

### Data Flow

**Host -> Lua:**
- `osc-set-subtitles <json>` — array of `{index, language, label, source, codec, forced}`
- `osc-set-active-subtitle <index>` — currently selected track

**Lua -> Host:**
- `continuum-subtitle-select <index>` — user picked a track (-1 = off)
- `continuum-subtitle-search` — user clicked "Search Online..."

### Search Online Flow
1. Lua sends `continuum-subtitle-search`
2. C# calls `POST /subtitles/search` with media file ID and configured languages
3. C# calls `POST /subtitles/download` for the best result
4. C# sends updated `osc-set-subtitles` with new tracks
5. Lua refreshes menu

## Files Changed

| File | Changes |
|---|---|
| `MiniPlayerBar.xaml` | Thumbnail 200x112, bar height 132 |
| `PlayerService.cs` | Thumbnail sizing, send media info/subtitles/visibility to Lua, handle subtitle select/search |
| `continuum-osc.lua` | Stats overlay (4 sections), subtitle popup menu, OSC disable for mini-bar |

## What's NOT in This Phase
- Quality/version selector (Phase 2)
- WebSocket session control (Phase 3)
- API audit (Phase 3)
