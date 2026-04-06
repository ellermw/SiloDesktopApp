# Phase 2: Quality/Version Selector — Design Spec

**Date:** 2026-04-05
**Status:** Approved

## Scope

Add a quality/version selector to the OSC matching the Continuum web player's QualityMenu. Supports version switching (different file versions) and transcode quality tiers with manual selection.

## Menu Structure

Gear icon button on the OSC bar (between stats and fullscreen). Click toggles the popup menu.

### Layout
- Header: "Quality"
- Versions section: one row per file version from WatchDetail.Versions[]. Label = file name or resolution. Active version has checkmark.
- Separator line
- Quality tiers section:
  - Auto (default — direct play, server decides play method)
  - Original (direct play / remux at full quality)
  - 1080p High (~10 Mbps)
  - 1080p (~6 Mbps)
  - 720p High (~4 Mbps)
  - 720p (~2 Mbps)
  - 480p (~1.5 Mbps)
  - 420p (~720 kbps)
- Active tier has checkmark

### Visual Style
Same as subtitle menu: semi-transparent black (85% opacity), white text, checkmarks, positioned above the gear button. Separate overlay z=70.

## Data Flow

### Version Switching
1. Lua sends `continuum-version-select <file_id>`
2. C# calls existing `PlayerService.SwitchVersionAsync(version)`
3. On completion, C# sends `osc-set-quality-info` with updated active_file_id
4. Lua updates checkmark

### Quality Tier Switching
1. Lua sends `continuum-quality-select <tier_id>`
2. C# handles in OnScriptMessage:
   - "auto" / "original": if currently transcoding, stop transcode and reload as direct play/remux. If already direct, no-op.
   - Transcode tier: call `POST /playback/transcode/start` with tier settings, switch mpv to HLS manifest, resume at current position
3. C# sends `osc-set-active-quality <tier_id>` back to Lua

### Data Sent on File Load
```
script-message osc-set-quality-info <json>
```
```json
{
  "versions": [
    {"file_id": 123, "label": "Movie.2160p.mkv", "resolution": "2160p"},
    {"file_id": 456, "label": "Movie.1080p.mkv", "resolution": "1080p"}
  ],
  "active_file_id": 123,
  "active_quality": "auto"
}
```

### Transcode Tier Definitions (hardcoded, matching web player)
| ID | Label | Resolution | Bitrate kbps |
|---|---|---|---|
| auto | Auto | — | — |
| original | Original | — | — |
| 1080p-high | 1080p High | 1080p | 10000 |
| 1080p | 1080p | 1080p | 6000 |
| 720p-high | 720p High | 720p | 4000 |
| 720p | 720p | 720p | 2000 |
| 480p | 480p | 480p | 1500 |
| 420p | 420p | 420p | 720 |

## OSC Button Layout

Right-side buttons (right to left): Exit, Fullscreen, Minimize, **Quality (gear)**, Volume bar, Volume icon, Stats, CC

## Menu Behavior
- Click gear → toggle menu
- Click outside → close
- Click version/tier → switch, close menu
- Mutual exclusion: opening quality menu closes subtitle menu and vice versa
- Brief loading indicator during switch, resume at same position

## Files Changed
| File | Changes |
|---|---|
| `continuum-osc.lua` | Gear button layout, render_quality_menu(), click handlers, osc-set-quality-info handler, mutual exclusion |
| `PlayerService.cs` | SendQualityInfoToOsc(), handle continuum-version-select and continuum-quality-select, SwitchQualityTierAsync() |

## What's NOT in This Phase
- Adaptive bitrate / auto quality switching (Phase 4)
- WebSocket session control (Phase 3)
