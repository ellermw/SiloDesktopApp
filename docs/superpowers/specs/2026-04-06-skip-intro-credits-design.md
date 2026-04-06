# Skip Intro/Credits Buttons — Design Spec

**Date:** 2026-04-06
**Status:** Approved

## Scope

Add floating "Skip Intro" and "Skip Credits" buttons that appear during intro/credits time ranges, matching streaming app UX.

## Data Flow

C# sends markers to Lua on file load:
```
script-message osc-set-markers <json>
```
```json
{"intro_start":10,"intro_end":90,"credits_start":5400,"credits_end":5520}
```
Null/zero values mean no marker for that range.

## Lua Behavior

- On each `tick()`, check `state.time_pos` against marker ranges
- When position enters intro range: show "Skip Intro" button
- When position enters credits range: show "Skip Credits" button
- When position exits range or user clicks: hide button
- Button click: `mp.commandv("seek", end_time, "absolute")` — direct seek, no C# round-trip

## Button Appearance

- Position: bottom-right of player, above the OSC bar
- Style: semi-transparent rounded button, white text, matching OSC theme
- Text: "Skip Intro" or "Skip Credits"
- Separate overlay at z=55 (above OSC z=50, below menus z=70)
- Stays visible for entire range, disappears when past end time

## Click Handling

- Check click in `handle_mouse_down` before other controls
- On hit: seek to marker end time, hide button
- Lua handles seek directly via mpv command

## Files Changed

| File | Changes |
|---|---|
| `continuum-osc.lua` | Marker state, tick check, button draw, click handler, osc-set-markers handler |
| `PlayerService.cs` | SendMarkersToOsc() called from FileLoaded handler |
