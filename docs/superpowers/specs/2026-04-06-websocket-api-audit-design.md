# Phase 3: WebSocket Session Control + API Audit — Design Spec

**Date:** 2026-04-06
**Status:** Approved

## Scope

Add WebSocket real-time session control matching the Continuum web player protocol. Full API compatibility audit with fixes.

## WebSocket Session Control

### Connection Lifecycle
- Connect to `wss://{server}/api/v1/playback/ws/{session_id}` immediately after playback starts
- Send `hello` with client name `"continuum-desktop"`, version `"1"`, supported commands
- Keep alive for entire playback session
- Disconnect on CloseAsync

### Hello Message
```json
{
  "type": "hello",
  "session_id": "uuid",
  "client": { "name": "continuum-desktop", "version": "1" },
  "capabilities": {
    "commands": ["pause", "unpause", "play_pause", "seek", "set_volume",
                 "stop", "terminate", "display_message",
                 "server_restarting", "server_shutting_down"]
  }
}
```

### Command Protocol
1. Receive JSON command from server
2. Send `ack` immediately: `{"type":"ack", "command_id":"...", "session_id":"...", "status":"accepted"}`
3. Execute command
4. Send `result`: `{"type":"result", "command_id":"...", "session_id":"...", "status":"completed|rejected", "error":"..."}`

### Supported Commands

| Command | Action |
|---|---|
| `pause` | `_mpv.Pause()` |
| `unpause` | `_mpv.Play()` |
| `play_pause` | `_mpv.TogglePause()` |
| `seek` | `_mpv.Seek(payload.position)` |
| `set_volume` | `_mpv.SetVolume(payload.volume * 100)` |
| `stop` / `terminate` | Show notice if message in payload, then `CloseAsync()` |
| `display_message` | Show notice overlay: payload.title + payload.message, tone=info, 8s dismiss |
| `server_restarting` | Show notice "Server restarting" + payload.message, tone=warning, 8s dismiss |
| `server_shutting_down` | Show notice "Server shutting down" + payload.message, tone=warning, 8s dismiss |

### Notice Overlay (Lua ASS)
- Position: top-center of player
- Style: semi-transparent rounded box, title (bold) + message text
- Two tones: info (blue/sky tint), warning (amber tint)
- Auto-dismiss after 8 seconds, non-blocking
- C# sends `osc-show-notice <json>` to Lua

## API Audit Results

### Coverage: 167/200+ endpoints (84%)

### BROKEN — Must Fix
1. **`PUT /admin/plugins/installations/{id}/analyzer-bindings/{id}`** — endpoint does NOT exist in server. PluginsApi.SaveAnalyzerBindingsAsync() will 404. Remove or fix.

### MISSING — Should Add
1. **Admin Playback Session Control** — Server has `POST /admin/sessions/{id}/pause|resume|stop|terminate|message` but desktop has no API class for these. Admin dashboard should be able to control active sessions.
2. **Subtitle Preferences** — `PUT /subtitle-prefs/{series_id}` saves per-series subtitle language. Should call when user selects subtitle during series playback.
3. **Home Dismissals** — `PUT /home/dismissals/{surface}/{item_id}` for dismissing Continue Watching / Next Up items.

### CORRECT — Verified (all other endpoints)
- Auth (10 endpoints) ✓
- Profiles (5) ✓
- Catalog/Browse (15) ✓
- Favorites/Watchlist/History (10) ✓
- Ratings (4) ✓
- Playback (8) ✓
- Home/Sections (6) ✓
- Recommendations (8) ✓
- People (3) ✓
- Collections (7) ✓
- Downloads (4) ✓
- Settings (11) ✓
- Subtitles (4) ✓
- History Import (7) ✓
- API Keys (3) ✓
- Admin Users (11) ✓
- Admin Libraries (9) ✓
- Admin Tasks (6) ✓
- Admin Nodes (6) ✓
- Admin Sessions/Stats (3) ✓
- Admin Collections (6) ✓
- Admin Sections (6) ✓
- Admin Recommendations (5) ✓
- Admin Logs (2) ✓
- Admin Settings (3) ✓
- Admin API Keys (4) ✓
- Admin Providers (5) ✓
- Admin Plugins (15) ✓ (except analyzer-bindings broken)
- Admin Invite Codes (4) ✓
- Admin Subtitle Providers (3) ✓
- Admin Catalog/Seed (7) ✓
- Admin Jobs (2) ✓
- Admin Rate Limits (2) ✓

### NOT ADDING (low priority)
- Advanced catalog query (POST /catalog/query) — current GET approach works
- Theme/branding endpoints — cosmetic
- Filesystem browse — only useful for setup wizard
- Admin WebSocket streaming for logs/sessions — HTTP polling works
- Node force reload — rarely needed

## Files Changed

| File | Action | Responsibility |
|---|---|---|
| `PlaybackWebSocket.cs` (new) | Create | WebSocket client, protocol, command dispatch |
| `PlayerService.cs` | Modify | Connect/disconnect WebSocket, execute commands |
| `continuum-osc.lua` | Modify | Notice overlay with auto-dismiss |
| `PlaybackApi.cs` | Modify | Add subtitle prefs, home dismissals |
| `PluginsApi.cs` | Modify | Remove broken analyzer-bindings endpoint |
| `PlaybackManager.cs` | Modify | Verify progress interval = 10s |
