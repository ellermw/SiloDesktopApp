        1 # Continuum Desktop Player — Development Context
        2
        3 ## Project Goal
        4 Build a native Windows 11 desktop media player that connects to a Continuum media server instance. The app should authenticate, browse libraries, play media (direct play, remux, transcode/HLS), track progress, and provide a rich media browsing experience.
        5
        6 ## Continuum Server
        7 - **API Base**: `https://continuum.taverncdn.com/api/v1`
        8 - **Jellyfin Compat**: `https://jf-continuum.taverncdn.com` (Jellyfin-protocol endpoint — not needed for native client)
        9 - **Access Token Expiry**: 24 hours (refresh token: 30 days)
       10 - **Content-Type**: All requests/responses are `application/json`
       11
       12 ## Architecture Overview
       13 Continuum is a Go-based media server with:
       14 - **Integrated node** — API, UI, scanner, metadata
       15 - **Transcode nodes** — Dedicated GPU nodes for HLS transcoding (Intel Arc A380, VAAPI)
       16 - **CDN** — Requests arrive through CDN edge nodes (Cloudflare-style)
       17 - **S3** — Artwork/metadata stored in S3, served via presigned URLs with TTLs
       18 - **PostgreSQL** — Primary database
       19 - **Redis** — Caching and pub/sub
       20
       21 ### Playback Methods
       22 | Method | When Used | Delivery |
       23 |--------|-----------|----------|
       24 | **Direct** | Client supports container + all codecs natively | HTTP byte-range progressive download |
       25 | **Remux** | Client supports codecs but not container, or audio needs transcode | ffmpeg remux to MP4, streamed progressively |
       26 | **Transcode** | Client can't decode video codec (e.g., HEVC in Chrome) | HLS with fMP4 segments (2-second segments) |
       27
       28 ### Key Constraints
       29 - **4K transcode disabled** (`allow_4k_transcode = false`) — selecting a 4K HEVC file in a client that can't direct-play HEVC will fall back to a 1080p alternate version
       30 - **Transcode throttle** — transcodes buffer 10 minutes ahead, then pace to maintain that buffer
       31 - **Most content is HEVC** — a native Windows player with HEVC support can direct-play almost everything, avoiding transcoding entirely
       32 - **High bitrate remux** (30-40+ Mbps) — the player should handle large HTTP responses and have adequate buffering
       33
       34 ### Why Build a Native Player
       35 Chrome/web browsers cannot decode HEVC, forcing all HEVC content through transcoding. A native Windows player with HEVC/HDR support (via Windows Media Foundation, LAV Filters, or libmpv) can direct-play the vast majority of content without any server-side processing.
       36
       37 ---
       38
       39 ## Authentication
       40
       41 ### Login Flow
       42 ```
       43 POST /api/v1/auth/login
       44 {
       45   "username": "string",
       46   "password": "string"
       47 }
       48
       49 Response 200:
       50 {
       51   "access_token": "jwt-string",
       52   "refresh_token": "jwt-string",
       53   "expires_in": 86400,
       54   "user": {
       55     "id": 1,
       56     "username": "string",
       57     "email": "string",
       58     "role": "admin|user"
       59   }
       60 }
       61 ```
       62
       63 ### Token Usage
       64 - All authenticated requests: `Authorization: Bearer <access_token>`
       65 - For native video element src URLs (if applicable): append `?token=<access_token>` as query param
       66 - API keys (prefixed `sa_`) also accepted in Bearer header as alternative to JWT
       67
       68 ### Token Refresh
       69 ```
       70 POST /api/v1/auth/refresh
       71 { "refresh_token": "string" }
       72
       73 Response 200:
       74 {
       75   "access_token": "string",
       76   "refresh_token": "string",
       77   "expires_in": 86400
       78 }
       79 ```
       80
       81 **IMPORTANT**: Proactively refresh the access token before it expires. Do not wait for a 401 during playback — that kills the stream. Refresh when ~80% of `expires_in` has elapsed.
       82
       83 ### Profile Selection
       84 After login, select a user profile. Profile-scoped routes require the `X-Profile-Id` header.
       85
       86 ```
       87 GET /api/v1/profiles
       88 Response: { "profiles": [{ "id": "uuid", "name": "string", "has_pin": false, ... }] }
       89 ```
       90
       91 If profile has a PIN:
       92 ```
       93 POST /api/v1/profiles/{id}/verify-pin
       94 { "pin": "1234" }
       95 Response: { "valid": true, "profile_token": "jwt", "expires_at": "RFC3339" }
       96 ```
       97
       98 ### Required Headers (all authenticated requests)
       99 | Header | Required | Description |
      100 |--------|----------|-------------|
      101 | `Authorization: Bearer <token>` | Always | JWT access token or API key |
      102 | `X-Profile-Id: <uuid>` | Profile-scoped routes | Selected profile UUID |
      103 | `X-Profile-Token: <jwt>` | PIN-protected profiles | Token from verify-pin |
      104
      105 ---
      106
      107 ## Browsing API
      108
      109 ### Libraries
      110 ```
      111 GET /api/v1/user/libraries
      112 ```
      113 Returns libraries visible to the current user/profile.
      114
      115 ### Catalog (primary browse endpoint)
      116 ```
      117 GET /api/v1/catalog?source=library&source_id=1&type=movie&sort=title&order=asc&limit=20&offset=0
      118 ```
      119
      120 Query parameters:
      121 - `source` — `library`, `genre`, `studio`, `network`, `country`, `recently_added`, `search`
      122 - `source_id` — library ID, genre name, etc.
      123 - `type` — `movie`, `series`
      124 - `genre`, `studio`, `network`, `country` — filter values
      125 - `content_rating` — filter by rating
      126 - `sort` — `title`, `year`, `rating_imdb`, `created_at`, `added_at`
      127 - `order` — `asc`, `desc`
      128 - `q` — search query (when source=search)
      129 - `limit`, `offset` — pagination (default 20, max 100)
      130
      131 Response includes `items[]` with `content_id`, `type`, `title`, `year`, `genres`, `poster_url`, `backdrop_url`, `user_state`, etc.
      132
      133 ### Catalog Filters
      134 ```
      135 GET /api/v1/catalog/filters?source=library&source_id=1
      136 ```
      137 Returns available filter values (genres, studios, resolutions, audio languages, etc.) for the current scope.
      138
      139 ### Advanced Query
      140 ```
      141 POST /api/v1/catalog/query
      142 { "library_id": 1, "filter_config": { ... }, "sort": "title", "order": "asc", "limit": 20, "offset": 0 }
      143 ```
      144
      145 ### Item Detail
      146 ```
      147 GET /api/v1/catalog/items/{content_id}
      148 ```
      149 Returns full detail: metadata, cast/crew, file versions (with codec/resolution/track info), subtitles, intro/credits markers, user progress.
      150
      151 Key fields in response:
      152 - `versions[]` — available file versions with `file_id`, `resolution`, `codec_video`, `codec_audio`, `hdr`, `bitrate`, `video_tracks[]`, `audio_tracks[]`, `subtitle_tracks[]`
      153 - `user_data` — `position_seconds`, `played`, `last_file_id`, etc.
      154 - `intro` / `credits` — skip markers with `start` and `end` seconds
      155
      156 ### Series Navigation
      157 ```
      158 GET /api/v1/catalog/series/{id}/seasons
      159 GET /api/v1/catalog/series/{id}/seasons/{num}/episodes
      160 GET /api/v1/catalog/items/{id}/episodes    (for a season content_id)
      161 ```
      162
      163 ### Watch Detail (playback-oriented)
      164 ```
      165 GET /api/v1/watch/{content_id}
      166 ```
      167 Optimized for the player — returns file versions, subtitles, markers, user progress, and effective subtitle/audio preferences in one call.
      168
      169 ### Search
      170 ```
      171 GET /api/v1/catalog?source=search&q=war+machine&limit=20
      172 ```
      173
      174 ---
      175
      176 ## Home Screen
      177
      178 ### Layout (skeleton)
      179 ```
      180 GET /api/v1/home/layout
      181 ```
      182 Returns section metadata without items (for fast skeleton rendering).
      183
      184 ### Full Sections
      185 ```
      186 GET /api/v1/home/sections
      187 ```
      188 Returns all sections with items populated. Section types include:
      189 `continue_watching`, `next_up`, `recently_added`, `popular`, `genre`, `collection`, `recommendations_for_you`, `recommendations_because_watched`, etc.
      190
      191 ### Single Section Items
      192 ```
      193 GET /api/v1/home/sections/{section_id}/items
      194 ```
      195
      196 ### Dismiss from Continue Watching / Next Up
      197 ```
      198 PUT /api/v1/home/dismissals/{surface}/{item_id}
      199 ```
      200 - `surface`: `continue_watching` or `next_up`
      201 - Body: `{ "progress_updated_at": "RFC3339" }` for continue_watching, `{ "series_id": "string" }` for next_up
      202
      203 ---
      204
      205 ## Playback API
      206
      207 ### Start Playback
      208 ```
      209 POST /api/v1/playback/start
      210 {
      211   "file_id": 123,
      212   "profile_id": "uuid",
      213   "play_method": "direct",
      214   "start_position": 0.0,
      215   "audio_track_index": 0,
      216   "codecs_video": ["h264", "hevc", "av1", "vp9"],
      217   "codecs_audio": ["aac", "flac", "opus", "eac3", "ac3", "dts", "truehd"],
      218   "containers": ["mp4", "mkv", "webm"],
      219   "max_resolution": "2160p",
      220   "hdr": true
      221 }
      222 ```
      223
      224 **For a native Windows player** — declare full codec support so the server chooses direct play whenever possible:
      225 - `codecs_video`: `["h264", "hevc", "av1", "vp9"]`
      226 - `codecs_audio`: `["aac", "flac", "opus", "eac3", "ac3", "dts", "truehd"]`
      227 - `containers`: `["mp4", "mkv"]`
      228 - `hdr`: `true`
      229 - `max_resolution`: `"2160p"`
      230
      231 If `start_position` is omitted, the server restores saved progress.
      232
      233 Response (201):
      234 ```json
      235 {
      236   "session_id": "uuid",
      237   "play_method": "direct|remux|transcode",
      238   "position": 1234.5,
      239   "stream_url": "/stream/{session_id}",
      240   "duration_seconds": 7200.0,
      241   "audio_track_index": 0,
      242   "subtitle_urls": [
      243     {
      244       "index": 0,
      245       "language": "en",
      246       "codec": "srt",
      247       "label": "English",
      248       "source": "external|embedded|downloaded",
      249       "url": "/stream/{session_id}/subtitles/0",
      250       "forced": false
      251     }
      252   ],
      253   "playback_info": {
      254     "stream_type": "progressive|hls",
      255     "transcode_audio": false,
      256     "video_codec": "hevc",
      257     "audio_codec": "aac"
      258   }
      259 }
      260 ```
      261
      262 ### Stream URL Patterns
      263 - **Direct play**: `GET /api/v1/stream/{session_id}` — HTTP byte-range progressive download. Supports `Range` header for seeking.
      264 - **Remux**: Same URL, server remuxes on the fly. Supports `?seek=<seconds>` query param.
      265 - **Transcode (HLS)**: Use `manifest_url` from transcode/start response, NOT the stream endpoint.
      266
      267 ### Start/Restart Transcode (for quality switch or seek)
      268 ```
      269 POST /api/v1/playback/transcode/start
      270 {
      271   "session_id": "uuid",
      272   "seek_seconds": 600.0,
      273   "target_resolution": "1080p",
      274   "target_codec_video": "h264",
      275   "target_codec_audio": "aac",
      276   "target_bitrate_kbps": 8000,
      277   "segment_duration": 2,
      278   "subtitle_track_index": -1,
      279   "subtitle_burn_in": false
      280 }
      281 ```
      282
      283 Response (202):
      284 ```json
      285 {
      286   "session_id": "uuid",
      287   "status": "started",
      288   "manifest_url": "/playback/transcode/{session_id}/master.m3u8",
      289   "duration_seconds": 7200.0,
      290   "player_start_seconds": 600.0,
      291   "can_seek_anywhere": true,
      292   "switched_file_id": null
      293 }
      294 ```
      295
      296 Note: `switched_file_id` is non-null when the server switched to a lower-res file (4K transcode guard).
      297
      298 ### HLS Endpoints (no auth needed — session UUID is the token)
      299 ```
      300 GET /api/v1/playback/transcode/{session_id}/master.m3u8
      301 GET /api/v1/playback/transcode/{session_id}/segment/{name}
      302 ```
      303 Segments are fMP4 format: `init.mp4` + `seg_NNNNN.m4s`
      304
      305 ### Progress Reporting
      306 ```
      307 POST /api/v1/playback/{session_id}/progress
      308 { "position": 1234.5, "is_paused": false }
      309 ```
      310 Call every **5-10 seconds**. Returns 204. This keeps the session alive and saves watch progress.
      311
      312 **CRITICAL**: If you stop sending progress, the session will be reaped after ~45 seconds (active) or ~2 minutes (paused). Always report progress while playing.
      313
      314 ### Stop Playback
      315 ```
      316 DELETE /api/v1/playback/{session_id}
      317 ```
      318 Returns 204. Persists final position and logs history.
      319
      320 ### Change Audio Track
      321 ```
      322 PATCH /api/v1/playback/{session_id}/audio
      323 { "audio_track_index": 1, "position": 1234.5 }
      324 ```
      325 Response includes `switch_mode: "reload"` — the client must reload the stream at the given position. Play method may change.
      326
      327 ### WebSocket Session Control
      328 ```
      329 GET /api/v1/playback/ws/{session_id}  (WebSocket upgrade)
      330 ```
      331 Used for realtime admin commands (pause, resume, stop, seek, message).
      332
      333 Client sends `hello` on connect:
      334 ```json
      335 { "type": "hello", "session_id": "uuid" }
      336 ```
      337
      338 Server sends commands, client responds with `ack` then `result`:
      339 ```json
      340 { "type": "command", "command_id": "uuid", "name": "pause|unpause|seek|stop|display_message", "payload": {} }
      341 ```
      342
      343 ---
      344
      345 ## Subtitles
      346
      347 ### In Playback Response
      348 `subtitle_urls[]` from playback/start contains all available subtitles with URLs.
      349
      350 ### Fetch Subtitle
      351 ```
      352 GET /api/v1/stream/{session_id}/subtitles/{index}
      353 ```
      354 Returns WebVTT format. Bitmap subtitles (PGS/VOBSUB) return 400 — those need burn-in via transcode.
      355
      356 ### Search External Subtitles
      357 ```
      358 POST /api/v1/subtitles/search
      359 { "media_file_id": 123, "languages": ["en"] }
      360 ```
      361
      362 ### Download External Subtitle
      363 ```
      364 POST /api/v1/subtitles/download
      365 { "media_file_id": 123, "provider": "opensubtitles", "subtitle_id": "...", "language": "en", "format": "srt" }
      366 ```
      367
      368 ### Subtitle Preferences (per-series sticky)
      369 ```
      370 PUT /api/v1/subtitle-prefs/{series_id}
      371 { "subtitle_language": "en", "subtitle_mode": "auto" }
      372 ```
      373
      374 ---
      375
      376 ## Images / Artwork
      377
      378 All images come as **presigned S3 URLs** in API responses. Fields:
      379 - `poster_url` — Movie/series poster
      380 - `backdrop_url` — Widescreen backdrop
      381 - `logo_url` — Transparent logo overlay
      382 - `still_url` — Episode still/screenshot
      383 - `photo_url` — Person photo
      384
      385 Each has a corresponding `*_thumbhash` field (base64 thumbhash for placeholder blur).
      386
      387 **URLs expire** — cache the image data, not the URL. Re-fetch item detail to get fresh URLs if needed.
      388
      389 ---
      390
      391 ## User State
      392
      393 ### Favorites
      394 ```
      395 PUT /api/v1/favorites/{item_id}        — Add (204)
      396 DELETE /api/v1/favorites/{item_id}     — Remove (204)
      397 GET /api/v1/favorites/{item_id}        — Check (204=yes, 404=no)
      398 GET /api/v1/favorites                  — List all
      399 ```
      400
      401 ### Watchlist
      402 ```
      403 PUT /api/v1/watchlist/{item_id}        — Add (204)
      404 DELETE /api/v1/watchlist/{item_id}     — Remove (204)
      405 GET /api/v1/watchlist                  — List all
      406 ```
      407
      408 ### Watched State
      409 ```
      410 POST /api/v1/watched/{id}             — Mark watched (200, returns affected_count)
      411 DELETE /api/v1/watched/{id}           — Mark unwatched (200)
      412 ```
      413
      414 ### Watch Progress
      415 ```
      416 GET /api/v1/progress?status=in_progress&limit=50
      417 ```
      418
      419 ### Ratings
      420 ```
      421 PUT /api/v1/ratings/{item_id}   — { "rating": 4 } (1-5 scale)
      422 GET /api/v1/ratings/{item_id}   — { "rating": 4, "rated_at": "..." }
      423 DELETE /api/v1/ratings/{item_id}
      424 ```
      425
      426 ---
      427
      428 ## Recommendations
      429 ```
      430 GET /api/v1/recommendations/for-you/rows      — Multiple "For You" rows
      431 GET /api/v1/recommendations/because-watched/{item_id}
      432 GET /api/v1/recommendations/similar/{item_id}
      433 GET /api/v1/recommendations/taste-profile      — User taste summary
      434 GET /api/v1/recommendations/popular?days=30
      435 ```
      436
      437 ---
      438
      439 ## Error Format
      440 All errors:
      441 ```json
      442 {
      443   "error": "error_code",
      444   "message": "Human-readable description"
      445 }
      446 ```
      447
      448 Common codes: `bad_request`, `unauthorized`, `forbidden`, `not_found`, `internal_error`, `invalid_credentials`, `user_disabled`, `too_many_streams`, `too_many_transcodes`
      449
      450 ---
      451
      452 ## Typical Client Flow
      453
      454 1. **Login**: `POST /auth/login` → store tokens
      455 2. **Select profile**: `GET /profiles` → set `X-Profile-Id` header
      456 3. **Home screen**: `GET /home/layout` (skeleton) then `GET /home/sections` (full)
      457 4. **Browse**: `GET /user/libraries` → `GET /catalog?source=library&source_id=N`
      458 5. **Search**: `GET /catalog?source=search&q=term`
      459 6. **Item detail**: `GET /catalog/items/{id}`
      460 7. **Prepare playback**: `GET /watch/{id}` → pick `file_id` from `versions[]`
      461 8. **Start playback**: `POST /playback/start` with full codec declarations
      462 9. **Stream**:
      463    - Direct: HTTP GET `stream_url` with byte-range support
      464    - HLS: `POST /playback/transcode/start` then play `manifest_url`
      465 10. **Report progress**: `POST /playback/{session_id}/progress` every 5-10s
      466 11. **Connect WebSocket**: `GET /playback/ws/{session_id}` for admin controls
      467 12. **Stop**: `DELETE /playback/{session_id}`
      468 13. **Token refresh**: Before expiry, call `POST /auth/refresh`
      469
      470 ---
      471
      472 ## Recommended Tech Stack (Windows)
      473
      474 ### Video Playback
      475 - **libmpv** (mpv player library) — best codec support, handles HEVC/HDR/DTS/TrueHD natively, HTTP byte-range seeking, HLS support, subtitle rendering. Embed via `mpv-2.dll`.
      476 - **Alternative**: Windows Media Foundation — native Windows HEVC support (requires HEVC Video Extensions from Microsoft Store)
      477
      478 ### App Framework
      479 - **WPF + .NET 8** or **WinUI 3** for native Windows UI
      480 - **Electron/Tauri** if web tech preferred (but defeats the purpose of native codec support)
      481 - **C# with LibMPVSharp** or **mpv.net** for mpv integration
      482
      483 ### Why libmpv
      484 - Decodes HEVC, HDR10, DTS-HD MA, TrueHD, FLAC natively
      485 - HTTP byte-range seeking for direct play streams
      486 - Built-in HLS support for transcode fallback
      487 - Subtitle rendering (SRT, ASS, VTT)
      488 - Hardware acceleration (D3D11VA, DXVA2, Vulkan)
      489 - No codec licensing concerns
      490
      491 ### Direct Play Advantage
      492 With full codec support declared, the server will choose `play_method: "direct"` for almost all content. This means:
      493 - No server-side processing (zero GPU/CPU load)
      494 - Full quality preservation (no re-encoding)
      495 - Instant start (no transcode warmup)
      496 - Works with 4K HDR content (bypasses the 4K transcode guard)
      497 - Supports DTS-HD MA, TrueHD, and other lossless audio codecs

---

## Codex Handoff - Current State

This section is the current working memory for the next Codex session. Read it before making changes.

### Local Paths

- Desktop app repo: `F:\ContinuumPlayer`
- Extra docs created during Claude Code work: `F:\ContinuumPlayerDocs`
- Continuum server/WebUI reference source: `F:\continuum-server`
- Published test build path: `F:\ContinuumPlayer\publish-test\ContinuumPlayer.exe`
- Runtime app data/logs: `%LOCALAPPDATA%\ContinuumPlayer`
- Claude project memories: `C:\Users\Mike\.claude\projects\F--ContinuumPlayer\memory`
- Claude project transcripts: `C:\Users\Mike\.claude\projects\F--ContinuumPlayer`
- Codex state: `C:\Users\Mike\.codex`

Do not copy live access tokens, refresh tokens, API keys, or credential JSON values into repo files. Use the existing local app config/state when needed. Known secret-bearing locations include Claude/Codex auth files, Claude local settings, Git remotes/credentials, and `%LOCALAPPDATA%\ContinuumPlayer\settings.json`. Inspect/redact carefully.

### Product Goals

The desktop app should match the Continuum WebUI visually and functionally as closely as possible, while using native Windows/libmpv playback for broad direct-play codec support. Core priorities are:

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

- `src/ContinuumPlayer/Views/LibraryPage.xaml.cs`
  - Custom virtualized grid path using `VirtualCatalogItems`.
  - Reduced overscan and card creation pressure.
  - Batches card binding.
  - Defers poster image and overlay creation while scrolling.
  - Cancels catalog loads and detaches events on navigation away.
  - Writes UI perf breadcrumbs via `App.SetPerfBreadcrumb(...)`.
- `src/ContinuumPlayer/Helpers/VirtualCatalogItems.cs`
  - New virtual list helper.
- `src/ContinuumPlayer/ViewModels/LibraryViewModel.cs`
  - Catalog query cancellation and sliding/current-load management.
- `src/ContinuumPlayer/Controls/PosterCard.xaml.cs`
  - Added deferred image loading, deferred overlay loading, and image load concurrency cap.
  - Still a key suspect if freezes persist: many WinUI elements/images may still be created on UI thread.

UI lag diagnostics:

- `src/ContinuumPlayer/App.xaml.cs`
  - Added a DispatcherTimer lag detector.
  - Logs UI stalls of about 900ms+ to `%LOCALAPPDATA%\ContinuumPlayer\ui_lag.txt`.
  - Each line includes delay, managed MB, and a breadcrumb such as library scroll/render/card binding.

Playback/subtitles:

- `src/ContinuumPlayer/Services/PlayerService.cs`
  - Avoid eager-loading every subtitle URL with mpv `sub-add`.
  - External/downloaded subtitle tracks are now loaded on demand.
  - Added helpers for subtitle selection/mapping.
- `src/ContinuumPlayer.Player/MpvPlayer.cs`
  - `AddSubtitle` supports `select`.
  - `eof-reached` property no longer fires `PlaybackEnded`; `MPV_EVENT_END_FILE` is authoritative.

WebSocket/session stability:

- `src/ContinuumPlayer.Core/Services/EventChannelClient.cs`
  - Removed blocking waits during unsubscribe/reconnect.
- `src/ContinuumPlayer/Services/PlaybackWebSocket.cs`
  - Disconnect now aborts active sockets instead of fire-and-forget close.
- `src/ContinuumPlayer/ViewModels/WatchTogetherViewModels.cs`
  - Removed `_wsRunTask?.Wait(500)` from stop path.

Home/mark watched:

- `src/ContinuumPlayer.Core/Models/Home/HomeSectionsResponse.cs`
  - `HomeSectionWithItems.Items` changed to `ObservableCollection<MediaItem>`.
- `src/ContinuumPlayer/Views/HomePage.xaml.cs`
  - Section updates are incremental instead of full rebuilds.

Admin pages:

- `src/ContinuumPlayer/ViewModels/Admin/AdminDashboardViewModel.cs`
  - Added `RefreshSessionsOnlyAsync`.
- `src/ContinuumPlayer/Views/Admin/AdminDashboardPage.xaml.cs`
  - Realtime session events refresh only sessions/stats/cards instead of full content rebuild.
- `src/ContinuumPlayer/Views/Admin/AdminMaintenancePage.xaml.cs`
  - Job realtime events refresh job lists directly instead of full page reload.

Warnings/dead code cleanup:

- Debug build warning count was reduced from 43 to 1. Remaining warning is Win2D AnyCPU debug warning; x64 publish uses RID and succeeds.
- Removed a dead restart flag, unused WndProc constants, warning-hiding null assignments, and a few misleading nullable issues.

### Current Verification State

Commands run successfully during the v1.0.205 release:

```powershell
dotnet publish F:\ContinuumPlayer\src\ContinuumPlayer\ContinuumPlayer.csproj -c Release -p:Platform=x64 -nologo
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" F:\ContinuumPlayer\installer\ContinuumDesktopPlayer.iss
```

Latest installer:

```text
F:\ContinuumPlayer\installer\output\ContinuumDesktopPlayer-1.0.205-Setup.exe
https://transfers.taverncdn.com/6NHwqes77Q/ContinuumDesktopPlayer-1.0.205-Setup.exe
```

The user tested the latest library-scroll changes before release:

- Hard, repeated fast scrolling in `Movies > Library` did not freeze.
- Poster art now loads after the fast text-card path and remains stable.
- Root cause for the immediate library-page freeze was heavyweight filter ComboBox item construction for large filter lists, plus earlier library/card realization pressure.

### Immediate Next Debugging Steps

1. Monitor `%LOCALAPPDATA%\ContinuumPlayer\ui_lag.txt` after real usage on v1.0.205.
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

v1.0.205 release changes are intended to be committed and pushed. Important changed/new files include:

- `libs/mpv/scripts/continuum-osc.lua`
- `src/ContinuumPlayer/Views/LibraryPage.xaml`
- `src/ContinuumPlayer/Views/LibraryPage.xaml.cs`
- `src/ContinuumPlayer/ViewModels/LibraryViewModel.cs`
- `src/ContinuumPlayer/Controls/PosterCard.xaml.cs`
- `src/ContinuumPlayer/Helpers/VirtualCatalogItems.cs`
- `src/ContinuumPlayer/Controls/LibraryGridCard.cs`
- `src/ContinuumPlayer.Core/Services/VirtualGridScrollGate.cs`
- `src/ContinuumPlayer.Core/Services/AsyncWorkThrottle.cs`
- `src/ContinuumPlayer.Core/Services/AsyncLoadVersionGate.cs`
- `src/ContinuumPlayer.Core/Services/MediaItemDisplayText.cs`
- `src/ContinuumPlayer/Services/PlayerService.cs`
- `src/ContinuumPlayer.Player/MpvPlayer.cs`
- `src/ContinuumPlayer/App.xaml.cs`
- `src/ContinuumPlayer.Core/Services/EventChannelClient.cs`
- `src/ContinuumPlayer/Services/PlaybackWebSocket.cs`
- `src/ContinuumPlayer/ViewModels/WatchTogetherViewModels.cs`
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
- `reference_gitlab.md`
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

Codex-side production memory is also recorded at:

```text
C:\Users\Mike\.codex\memories\production-infra-handoff.md
```

Important items captured there:

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
