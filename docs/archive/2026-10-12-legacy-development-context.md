# Continuum Desktop Player — Development Context

## Project Goal
Build a native Windows 11 desktop media player that connects to a Continuum media server instance. The app should authenticate, browse libraries, play media (direct play, remux, transcode/HLS), track progress, and provide a rich media browsing experience.

## Continuum Server
- **API Base**: `https://continuum.taverncdn.com/api/v1`
- **Jellyfin Compat**: `https://jf-continuum.taverncdn.com` (Jellyfin-protocol endpoint — not needed for native client)
- **Access Token Expiry**: 24 hours (refresh token: 30 days)
- **Content-Type**: All requests/responses are `application/json`

## Architecture Overview
Continuum is a Go-based media server with:
- **Integrated node** — API, UI, scanner, metadata
- **Transcode nodes** — Dedicated GPU nodes for HLS transcoding (Intel Arc A380, VAAPI)
- **CDN** — Requests arrive through CDN edge nodes (Cloudflare-style)
- **S3** — Artwork/metadata stored in S3, served via presigned URLs with TTLs
- **PostgreSQL** — Primary database
- **Redis** — Caching and pub/sub

### Playback Methods
| Method | When Used | Delivery |
|--------|-----------|----------|
| **Direct** | Client supports container + all codecs natively | HTTP byte-range progressive download |
| **Remux** | Client supports codecs but not container, or audio needs transcode | ffmpeg remux to MP4, streamed progressively |
| **Transcode** | Client can't decode video codec (e.g., HEVC in Chrome) | HLS with fMP4 segments (2-second segments) |

### Key Constraints
- **4K transcode disabled** (`allow_4k_transcode = false`) — selecting a 4K HEVC file in a client that can't direct-play HEVC will fall back to a 1080p alternate version
- **Transcode throttle** — transcodes buffer 10 minutes ahead, then pace to maintain that buffer
- **Most content is HEVC** — a native Windows player with HEVC support can direct-play almost everything, avoiding transcoding entirely
- **High bitrate remux** (30-40+ Mbps) — the player should handle large HTTP responses and have adequate buffering

### Why Build a Native Player
Chrome/web browsers cannot decode HEVC, forcing all HEVC content through transcoding. A native Windows player with HEVC/HDR support (via Windows Media Foundation, LAV Filters, or libmpv) can direct-play the vast majority of content without any server-side processing.

---

## Authentication

### Login Flow
```
POST /api/v1/auth/login
{
  "username": "string",
  "password": "string"
}

Response 200:
{
  "access_token": "jwt-string",
  "refresh_token": "jwt-string",
  "expires_in": 86400,
  "user": {
    "id": 1,
    "username": "string",
    "email": "string",
    "role": "admin|user"
  }
}
```

### Token Usage
- All authenticated requests: `Authorization: Bearer <access_token>`
- For native video element src URLs (if applicable): append `?token=<access_token>` as query param
- API keys (prefixed `sa_`) also accepted in Bearer header as alternative to JWT

### Token Refresh
```
POST /api/v1/auth/refresh
{ "refresh_token": "string" }

Response 200:
{
  "access_token": "string",
  "refresh_token": "string",
  "expires_in": 86400
}
```

**IMPORTANT**: Proactively refresh the access token before it expires. Do not wait for a 401 during playback — that kills the stream. Refresh when ~80% of `expires_in` has elapsed.

### Profile Selection
After login, select a user profile. Profile-scoped routes require the `X-Profile-Id` header.

```
GET /api/v1/profiles
Response: { "profiles": [{ "id": "uuid", "name": "string", "has_pin": false, ... }] }
```

If profile has a PIN:
```
POST /api/v1/profiles/{id}/verify-pin
{ "pin": "1234" }
Response: { "valid": true, "profile_token": "jwt", "expires_at": "RFC3339" }
```

### Required Headers (all authenticated requests)
| Header | Required | Description |
|--------|----------|-------------|
| `Authorization: Bearer <token>` | Always | JWT access token or API key |
| `X-Profile-Id: <uuid>` | Profile-scoped routes | Selected profile UUID |
| `X-Profile-Token: <jwt>` | PIN-protected profiles | Token from verify-pin |

---

## Browsing API

### Libraries
```
GET /api/v1/user/libraries
```
Returns libraries visible to the current user/profile.

### Catalog (primary browse endpoint)
```
GET /api/v1/catalog?source=library&source_id=1&type=movie&sort=title&order=asc&limit=20&offset=0
```

Query parameters:
- `source` — `library`, `genre`, `studio`, `network`, `country`, `recently_added`, `search`
- `source_id` — library ID, genre name, etc.
- `type` — `movie`, `series`
- `genre`, `studio`, `network`, `country` — filter values
- `content_rating` — filter by rating
- `sort` — `title`, `year`, `rating_imdb`, `created_at`, `added_at`
- `order` — `asc`, `desc`
- `q` — search query (when source=search)
- `limit`, `offset` — pagination (default 20, max 100)

Response includes `items[]` with `content_id`, `type`, `title`, `year`, `genres`, `poster_url`, `backdrop_url`, `user_state`, etc.

### Catalog Filters
```
GET /api/v1/catalog/filters?source=library&source_id=1
```
Returns available filter values (genres, studios, resolutions, audio languages, etc.) for the current scope.

### Advanced Query
```
POST /api/v1/catalog/query
{ "library_id": 1, "filter_config": { ... }, "sort": "title", "order": "asc", "limit": 20, "offset": 0 }
```

### Item Detail
```
GET /api/v1/catalog/items/{content_id}
```
Returns full detail: metadata, cast/crew, file versions (with codec/resolution/track info), subtitles, intro/credits markers, user progress.

Key fields in response:
- `versions[]` — available file versions with `file_id`, `resolution`, `codec_video`, `codec_audio`, `hdr`, `bitrate`, `video_tracks[]`, `audio_tracks[]`, `subtitle_tracks[]`
- `user_data` — `position_seconds`, `played`, `last_file_id`, etc.
- `intro` / `credits` — skip markers with `start` and `end` seconds

### Series Navigation
```
GET /api/v1/catalog/series/{id}/seasons
GET /api/v1/catalog/series/{id}/seasons/{num}/episodes
GET /api/v1/catalog/items/{id}/episodes    (for a season content_id)
```

### Watch Detail (playback-oriented)
```
GET /api/v1/watch/{content_id}
```
Optimized for the player — returns file versions, subtitles, markers, user progress, and effective subtitle/audio preferences in one call.

### Search
```
GET /api/v1/catalog?source=search&q=war+machine&limit=20
```

---

## Home Screen

### Layout (skeleton)
```
GET /api/v1/home/layout
```
Returns section metadata without items (for fast skeleton rendering).

### Full Sections
```
GET /api/v1/home/sections
```
Returns all sections with items populated. Section types include:
`continue_watching`, `next_up`, `recently_added`, `popular`, `genre`, `collection`, `recommendations_for_you`, `recommendations_because_watched`, etc.

### Single Section Items
```
GET /api/v1/home/sections/{section_id}/items
```

### Dismiss from Continue Watching / Next Up
```
PUT /api/v1/home/dismissals/{surface}/{item_id}
```
- `surface`: `continue_watching` or `next_up`
- Body: `{ "progress_updated_at": "RFC3339" }` for continue_watching, `{ "series_id": "string" }` for next_up

---

## Playback API

### Start Playback
```
POST /api/v1/playback/start
{
  "file_id": 123,
  "profile_id": "uuid",
  "play_method": "direct",
  "start_position": 0.0,
  "audio_track_index": 0,
  "codecs_video": ["h264", "hevc", "av1", "vp9"],
  "codecs_audio": ["aac", "flac", "opus", "eac3", "ac3", "dts", "truehd"],
  "containers": ["mp4", "mkv", "webm"],
  "max_resolution": "2160p",
  "hdr": true
}
```

**For a native Windows player** — declare full codec support so the server chooses direct play whenever possible:
- `codecs_video`: `["h264", "hevc", "av1", "vp9"]`
- `codecs_audio`: `["aac", "flac", "opus", "eac3", "ac3", "dts", "truehd"]`
- `containers`: `["mp4", "mkv"]`
- `hdr`: `true`
- `max_resolution`: `"2160p"`

If `start_position` is omitted, the server restores saved progress.

Response (201):
```json
{
  "session_id": "uuid",
  "play_method": "direct|remux|transcode",
  "position": 1234.5,
  "stream_url": "/stream/{session_id}",
  "duration_seconds": 7200.0,
  "audio_track_index": 0,
  "subtitle_urls": [
    {
      "index": 0,
      "language": "en",
      "codec": "srt",
      "label": "English",
      "source": "external|embedded|downloaded",
      "url": "/stream/{session_id}/subtitles/0",
      "forced": false
    }
  ],
  "playback_info": {
    "stream_type": "progressive|hls",
    "transcode_audio": false,
    "video_codec": "hevc",
    "audio_codec": "aac"
  }
}
```

### Stream URL Patterns
- **Direct play**: `GET /api/v1/stream/{session_id}` — HTTP byte-range progressive download. Supports `Range` header for seeking.
- **Remux**: Same URL, server remuxes on the fly. Supports `?seek=<seconds>` query param.
- **Transcode (HLS)**: Use `manifest_url` from transcode/start response, NOT the stream endpoint.

### Start/Restart Transcode (for quality switch or seek)
```
POST /api/v1/playback/transcode/start
{
  "session_id": "uuid",
  "seek_seconds": 600.0,
  "target_resolution": "1080p",
  "target_codec_video": "h264",
  "target_codec_audio": "aac",
  "target_bitrate_kbps": 8000,
  "segment_duration": 2,
  "subtitle_track_index": -1,
  "subtitle_burn_in": false
}
```

Response (202):
```json
{
  "session_id": "uuid",
  "status": "started",
  "manifest_url": "/playback/transcode/{session_id}/master.m3u8",
  "duration_seconds": 7200.0,
  "player_start_seconds": 600.0,
  "can_seek_anywhere": true,
  "switched_file_id": null
}
```

Note: `switched_file_id` is non-null when the server switched to a lower-res file (4K transcode guard).

### HLS Endpoints (no auth needed — session UUID is the token)
```
GET /api/v1/playback/transcode/{session_id}/master.m3u8
GET /api/v1/playback/transcode/{session_id}/segment/{name}
```
Segments are fMP4 format: `init.mp4` + `seg_NNNNN.m4s`

### Progress Reporting
```
POST /api/v1/playback/{session_id}/progress
{ "position": 1234.5, "is_paused": false }
```
Call every **5-10 seconds**. Returns 204. This keeps the session alive and saves watch progress.

**CRITICAL**: If you stop sending progress, the session will be reaped after ~45 seconds (active) or ~2 minutes (paused). Always report progress while playing.

### Stop Playback
```
DELETE /api/v1/playback/{session_id}
```
Returns 204. Persists final position and logs history.

### Change Audio Track
```
PATCH /api/v1/playback/{session_id}/audio
{ "audio_track_index": 1, "position": 1234.5 }
```
Response includes `switch_mode: "reload"` — the client must reload the stream at the given position. Play method may change.

### WebSocket Session Control
```
GET /api/v1/playback/ws/{session_id}  (WebSocket upgrade)
```
Used for realtime admin commands (pause, resume, stop, seek, message).

Client sends `hello` on connect:
```json
{ "type": "hello", "session_id": "uuid" }
```

Server sends commands, client responds with `ack` then `result`:
```json
{ "type": "command", "command_id": "uuid", "name": "pause|unpause|seek|stop|display_message", "payload": {} }
```

---

## Subtitles

### In Playback Response
`subtitle_urls[]` from playback/start contains all available subtitles with URLs.

### Fetch Subtitle
```
GET /api/v1/stream/{session_id}/subtitles/{index}
```
Returns WebVTT format. Bitmap subtitles (PGS/VOBSUB) return 400 — those need burn-in via transcode.

### Search External Subtitles
```
POST /api/v1/subtitles/search
{ "media_file_id": 123, "languages": ["en"] }
```

### Download External Subtitle
```
POST /api/v1/subtitles/download
{ "media_file_id": 123, "provider": "opensubtitles", "subtitle_id": "...", "language": "en", "format": "srt" }
```

### Subtitle Preferences (per-series sticky)
```
PUT /api/v1/subtitle-prefs/{series_id}
{ "subtitle_language": "en", "subtitle_mode": "auto" }
```

---

## Images / Artwork

All images come as **presigned S3 URLs** in API responses. Fields:
- `poster_url` — Movie/series poster
- `backdrop_url` — Widescreen backdrop
- `logo_url` — Transparent logo overlay
- `still_url` — Episode still/screenshot
- `photo_url` — Person photo

Each has a corresponding `*_thumbhash` field (base64 thumbhash for placeholder blur).

**URLs expire** — cache the image data, not the URL. Re-fetch item detail to get fresh URLs if needed.

---

## User State

### Favorites
```
PUT /api/v1/favorites/{item_id}        — Add (204)
DELETE /api/v1/favorites/{item_id}     — Remove (204)
GET /api/v1/favorites/{item_id}        — Check (204=yes, 404=no)
GET /api/v1/favorites                  — List all
```

### Watchlist
```
PUT /api/v1/watchlist/{item_id}        — Add (204)
DELETE /api/v1/watchlist/{item_id}     — Remove (204)
GET /api/v1/watchlist                  — List all
```

### Watched State
```
POST /api/v1/watched/{id}             — Mark watched (200, returns affected_count)
DELETE /api/v1/watched/{id}           — Mark unwatched (200)
```

### Watch Progress
```
GET /api/v1/progress?status=in_progress&limit=50
```

### Ratings
```
PUT /api/v1/ratings/{item_id}   — { "rating": 4 } (1-5 scale)
GET /api/v1/ratings/{item_id}   — { "rating": 4, "rated_at": "..." }
DELETE /api/v1/ratings/{item_id}
```

---

## Recommendations
```
GET /api/v1/recommendations/for-you/rows      — Multiple "For You" rows
GET /api/v1/recommendations/because-watched/{item_id}
GET /api/v1/recommendations/similar/{item_id}
GET /api/v1/recommendations/taste-profile      — User taste summary
GET /api/v1/recommendations/popular?days=30
```

---

## Error Format
All errors:
```json
{
  "error": "error_code",
  "message": "Human-readable description"
}
```

Common codes: `bad_request`, `unauthorized`, `forbidden`, `not_found`, `internal_error`, `invalid_credentials`, `user_disabled`, `too_many_streams`, `too_many_transcodes`

---

## Typical Client Flow

1. **Login**: `POST /auth/login` → store tokens
2. **Select profile**: `GET /profiles` → set `X-Profile-Id` header
3. **Home screen**: `GET /home/layout` (skeleton) then `GET /home/sections` (full)
4. **Browse**: `GET /user/libraries` → `GET /catalog?source=library&source_id=N`
5. **Search**: `GET /catalog?source=search&q=term`
6. **Item detail**: `GET /catalog/items/{id}`
7. **Prepare playback**: `GET /watch/{id}` → pick `file_id` from `versions[]`
8. **Start playback**: `POST /playback/start` with full codec declarations
9. **Stream**:
   - Direct: HTTP GET `stream_url` with byte-range support
   - HLS: `POST /playback/transcode/start` then play `manifest_url`
10. **Report progress**: `POST /playback/{session_id}/progress` every 5-10s
11. **Connect WebSocket**: `GET /playback/ws/{session_id}` for admin controls
12. **Stop**: `DELETE /playback/{session_id}`
13. **Token refresh**: Before expiry, call `POST /auth/refresh`

---

## Recommended Tech Stack (Windows)

### Video Playback
- **libmpv** (mpv player library) — best codec support, handles HEVC/HDR/DTS/TrueHD natively, HTTP byte-range seeking, HLS support, subtitle rendering. Embed via `mpv-2.dll`.
- **Alternative**: Windows Media Foundation — native Windows HEVC support (requires HEVC Video Extensions from Microsoft Store)

### App Framework
- **WPF + .NET 8** or **WinUI 3** for native Windows UI
- **Electron/Tauri** if web tech preferred (but defeats the purpose of native codec support)
- **C# with LibMPVSharp** or **mpv.net** for mpv integration

### Why libmpv
- Decodes HEVC, HDR10, DTS-HD MA, TrueHD, FLAC natively
- HTTP byte-range seeking for direct play streams
- Built-in HLS support for transcode fallback
- Subtitle rendering (SRT, ASS, VTT)
- Hardware acceleration (D3D11VA, DXVA2, Vulkan)
- No codec licensing concerns

### Direct Play Advantage
With full codec support declared, the server will choose `play_method: "direct"` for almost all content. This means:
- No server-side processing (zero GPU/CPU load)
- Full quality preservation (no re-encoding)
- Instant start (no transcode warmup)
- Works with 4K HDR content (bypasses the 4K transcode guard)
- Supports DTS-HD MA, TrueHD, and other lossless audio codecs

---

## Codex Handoff - Current State

This section is the current working memory for the next Codex session. Read it before making changes.

### Local Paths

- Desktop app repo: `D:\SiloPlayer`
- Extra legacy docs: `D:\SiloPlayerDocs`
- Current Silo server/WebUI reference worktree: `D:\SiloPlayer\.codex-tmp\silo-server-current`
- Published test build path: `D:\SiloPlayer\publish-test\SiloPlayer.exe`
- Runtime app data/logs: `%LOCALAPPDATA%\SiloPlayer`
- Claude project memories: `C:\Users\Mike\.claude\projects\F--ContinuumPlayer\memory`
- Claude project transcripts: `C:\Users\Mike\.claude\projects\F--ContinuumPlayer`
- Codex state: `C:\Users\Michael\.codex`

### Source-of-Truth and Parity Rules (Critical)

- The only authoritative Silo server/WebUI source is the public GitHub repository: `https://github.com/Silo-Server/silo-server`.
- Never fetch, inspect, compare against, cite, or use the legacy private GitLab Continuum repository for Silo desktop parity work.
- Before each parity pass, fetch the current GitHub `main` branch directly and record the exact commit used.
- Latest parity reference fetched for this audit: `20ae82ae05edcfef151a02738e323cf1a97034ef` from official GitHub `main`.
- "Implemented" or "functional" does not mean parity is complete.
- A page is complete only after side-by-side verification confirms visual layout, spacing, typography, colors, responsive behavior, states, interactions, data, and functionality match the current WebUI as closely as native WinUI permits.
- Admin Dashboard, Admin Libraries, and Admin Activity currently have substantial functional coverage but are **not** visually 1:1 and must remain marked visual-parity-incomplete.

Do not copy live access tokens, refresh tokens, API keys, or credential JSON values into repo files. Use the existing local app config/state when needed. Known secret-bearing locations include Claude/Codex auth files, Claude local settings, Git remotes/credentials, and `%LOCALAPPDATA%\SiloPlayer\settings.json`. Inspect/redact carefully.

### Product Goals

The desktop app should match the current Silo WebUI visually and functionally as closely as possible, while using native Windows/libmpv playback for broad direct-play codec support. Core priorities are:

- WebUI parity for browse/home/library/player behavior.
- Direct play whenever possible for HEVC, HDR, Dolby Vision where mpv supports it, Atmos/TrueHD/DTS, VC-1, etc.
- Fast page loads, fast playback start, fast seek/resume.
- Lightweight, stable, optimized UI. Scrolling and navigation should not freeze.

### User-Reported Issues In This Workstream

- Persistent freeze while scrolling `Movies > Library`. This still reproduces after the latest fixes.
- Previous blank/flashing poster cards and launch failures occurred during earlier changes.
- Skip Intro originally skipped but left playback paused; this was reported fixed by the user.
- Credits/outro detection was too early and showed both `Next Episode` and `Skip Credits`; prior work added guards/UI behavior, but keep watching this area.
- Playback could randomly stop/pause during 4K HDR movies and not resume until closing the player. This may relate to server/session/progress, mpv EOF/cache events, or network stalls.
- Admin `Libraries > Scan All` froze the app for 10+ seconds; some admin realtime rebuild pressure was reduced, but retest.

### Important Recent Changes

Library scrolling and catalog:

- `src/SiloPlayer/Views/LibraryPage.xaml.cs`
  - Custom virtualized grid path using `VirtualCatalogItems`.
  - Reduced overscan and card creation pressure.
  - Batches card binding.
  - Defers poster image and overlay creation while scrolling.
  - Cancels catalog loads and detaches events on navigation away.
  - Writes UI perf breadcrumbs via `App.SetPerfBreadcrumb(...)`.
- `src/SiloPlayer/Helpers/VirtualCatalogItems.cs`
  - New virtual list helper.
- `src/SiloPlayer/ViewModels/LibraryViewModel.cs`
  - Catalog query cancellation and sliding/current-load management.
- `src/SiloPlayer/Controls/PosterCard.xaml.cs`
  - Added deferred image loading, deferred overlay loading, and image load concurrency cap.
  - Still a key suspect if freezes persist: many WinUI elements/images may still be created on UI thread.

UI lag diagnostics:

- `src/SiloPlayer/App.xaml.cs`
  - Added a DispatcherTimer lag detector.
  - Logs UI stalls of about 900ms+ to `%LOCALAPPDATA%\SiloPlayer\ui_lag.txt`.
  - Each line includes delay, managed MB, and a breadcrumb such as library scroll/render/card binding.

Playback/subtitles:

- `src/SiloPlayer/Services/PlayerService.cs`
  - Avoid eager-loading every subtitle URL with mpv `sub-add`.
  - External/downloaded subtitle tracks are now loaded on demand.
  - Added helpers for subtitle selection/mapping.
- `src/SiloPlayer.Player/MpvPlayer.cs`
  - `AddSubtitle` supports `select`.
  - `eof-reached` property no longer fires `PlaybackEnded`; `MPV_EVENT_END_FILE` is authoritative.

WebSocket/session stability:

- `src/SiloPlayer.Core/Services/EventChannelClient.cs`
  - Removed blocking waits during unsubscribe/reconnect.
- `src/SiloPlayer/Services/PlaybackWebSocket.cs`
  - Disconnect now aborts active sockets instead of fire-and-forget close.
- `src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs`
  - Removed `_wsRunTask?.Wait(500)` from stop path.

Home/mark watched:

- `src/SiloPlayer.Core/Models/Home/HomeSectionsResponse.cs`
  - `HomeSectionWithItems.Items` changed to `ObservableCollection<MediaItem>`.
- `src/SiloPlayer/Views/HomePage.xaml.cs`
  - Section updates are incremental instead of full rebuilds.

Admin pages:

- `src/SiloPlayer/ViewModels/Admin/AdminDashboardViewModel.cs`
  - Added `RefreshSessionsOnlyAsync`.
- `src/SiloPlayer/Views/Admin/AdminDashboardPage.xaml.cs`
  - Realtime session events refresh only sessions/stats/cards instead of full content rebuild.
- `src/SiloPlayer/Views/Admin/AdminMaintenancePage.xaml.cs`
  - Job realtime events refresh job lists directly instead of full page reload.

Warnings/dead code cleanup:

- Debug build warning count was reduced from 43 to 1. Remaining warning is Win2D AnyCPU debug warning; x64 publish uses RID and succeeds.
- Removed a dead restart flag, unused WndProc constants, warning-hiding null assignments, and a few misleading nullable issues.

### Current Verification State

Commands used to verify the current v1.1.90 release:

```powershell
dotnet publish D:\SiloPlayer\src\SiloPlayer\SiloPlayer.csproj -c Release -p:Platform=x64 -nologo
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" D:\SiloPlayer\installer\SiloInstaller.iss
```

Latest installer:

```text
D:\SiloPlayer\installer\output\SiloInstaller-1.1.90-Setup.exe
https://github.com/ellermw/SiloDesktopApp/releases/download/v1.1.90/SiloInstaller-Windows-x64.exe
```

The user tested the latest library-scroll changes before release:

- Hard, repeated fast scrolling in `Movies > Library` did not freeze.
- Poster art now loads after the fast text-card path and remains stable.
- Root cause for the immediate library-page freeze was heavyweight filter ComboBox item construction for large filter lists, plus earlier library/card realization pressure.

### Immediate Next Debugging Steps

1. Monitor `%LOCALAPPDATA%\SiloPlayer\ui_lag.txt` after real usage on v1.1.90.
2. If library lag returns, start from the latest breadcrumb. The current library path is a bounded active-window grid with 40 realized cards and a two-at-a-time poster queue.
3. If UI lag points at filters again, cap or virtualize the filter dropdown option lists instead of constructing thousands of dropdown entries.
4. If UI lag points at poster loading, lower `LibraryGridCard` poster queue concurrency or increase its cancellation delay.
5. Continue watching older non-library issues separately: 4K playback stopping/pausing, admin scan-all stalls, and any remaining WebSocket/session churn.

### Audit Findings To Keep In Mind

- `rg.exe` exists but may fail with `Access is denied`; PowerShell `Get-ChildItem | Select-String` worked reliably.
- Obvious UI-blocking waits were removed. Remaining waits found:
  - `PlaybackManager`: `_progressGuard.Wait(0)` is a nonblocking skip.
  - `MpvPlayer`: `_frameUpdateEvent.Wait(100)` is in render loop/disposal behavior, not the library UI path.
- Many `.Result` usages are after `await Task.WhenAll(...)`; not urgent unless they appear on a UI path without prior await.
- Several direct `new BitmapImage(new Uri(...))` remote-image usages remain outside the library path. They may be future optimization targets but are not the current library-scroll freeze suspect unless those pages are open.

### Working Tree Notes

Current v1.1.90 follow-up changes are kept in the working tree until explicitly approved for commit and push. Important changed/new files include:

- `libs/mpv/scripts/silo-osc.lua`
- `src/SiloPlayer/Views/LibraryPage.xaml`
- `src/SiloPlayer/Views/LibraryPage.xaml.cs`
- `src/SiloPlayer/ViewModels/LibraryViewModel.cs`
- `src/SiloPlayer/Controls/PosterCard.xaml.cs`
- `src/SiloPlayer/Helpers/VirtualCatalogItems.cs`
- `src/SiloPlayer/Controls/LibraryGridCard.cs`
- `src/SiloPlayer.Core/Services/VirtualGridScrollGate.cs`
- `src/SiloPlayer.Core/Services/AsyncWorkThrottle.cs`
- `src/SiloPlayer.Core/Services/AsyncLoadVersionGate.cs`
- `src/SiloPlayer.Core/Services/MediaItemDisplayText.cs`
- `src/SiloPlayer/Services/PlayerService.cs`
- `src/SiloPlayer.Player/MpvPlayer.cs`
- `src/SiloPlayer/App.xaml.cs`
- `src/SiloPlayer.Core/Services/EventChannelClient.cs`
- `src/SiloPlayer/Services/PlaybackWebSocket.cs`
- `src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs`
- admin/home related files listed by `git status --short`

Local-only support folders/files:

- `.claude/worktrees/` contains old Claude git worktrees with uncommitted changes; do not delete or commit it.
- Build outputs such as `publish-test/`, `installer/publish/`, `installer/output/`, and temporary screenshots are ignored.

Do not delete untracked Claude/Codex artifacts unless the user explicitly asks.

### Claude Memory Pointers

Claude Code memories for this project are in:

```text
C:\Users\Mike\.claude\projects\F--ContinuumPlayer\memory
```

Especially relevant files:

- `MEMORY.md`
- `feedback_one_to_one_clone.md`
- `feedback_speed_priority.md`
- `feedback_quality_over_speed.md`
- `feedback_no_close_running_app.md`
- `feedback_no_skipping.md`
- `reference_web_player.md`
- `reference_gitlab.md` (historical only; never use as a current Silo source)
- `reference_continuum_sync.md`
- `reference_parity_docs.md`
- `reference_audit_workflow.md`
- `project_architecture.md`
- `user_profile.md`

Use those as read-only context unless the user asks to update Claude Code memory too.

### Codex Production Infra Handoff

Codex is taking over Claude Code responsibilities over time. Do not delete Claude
Code memories, transcripts, worktrees, or artifacts; keep using them as read-only
reference if context seems missing.

Codex-side production memory is organized under:

```text
C:\Users\Mike\.codex\memories\
```

For production infrastructure tasks, new Codex chats must read this first:

```text
C:\Users\Mike\.codex\memories\00-read-me-first.md
```

Then open the focused runbook for the task:

- Silo updates: `silo-update-runbook.md`
- Production access/PVE/VMIDs: `access-runbook.md`
- Discord changelogs: `discord-changelog-runbook.md`
- LibraryManager: `librarymanager-runbook.md`
- ORM/CDN: `orm-cdn-runbook.md`
- Emby/Jellyfin: `emby-jellyfin-runbook.md`

The old `production-infra-handoff.md` is now historical archive/context, not the
primary procedure file.

Important items captured in the current runbooks:

- Production rule: announce intent before actions; no code/config/DB/service/log/data
  changes without explicit approval.
- Stable remote pattern: local tunnel through FW to management, then fan out from
  management to PVE/appboxes/LXCs.
- PVE access: key is stored in LibraryManager `proxmox_servers`; fetch via base64,
  materialize only to a temporary 0600 key file, remove immediately, never print it.
- Management cleanup completed 2026-05-09: Docker build cache, expired transfer-sh
  data, and oversized Docker JSON logs. Root filesystem ended around 64% used.
- LibraryManager overview: production media-service management platform for
  Plex/Emby/Jellyfin users, appboxes, CDN/OpenResty, autoscan, enforcement, and
  WHMCS/provisioning.
- Kometa incident for service `1665-2334` / display `390`: central Kometa VMID 102,
  config at `/opt/kometa/390/config.yml`, bad line
  `custom_repo: overlay_artwork_filetype: webp_lossy`, causing YAML parse failure.
  No fix was applied.
