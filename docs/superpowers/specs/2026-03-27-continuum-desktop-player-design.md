# Continuum Desktop Player -- Design Specification

## Overview

A native Windows 11 desktop media player that connects to Continuum media server instances. The app authenticates users, browses libraries, and plays media with full codec support (HEVC, AV1, HDR10, Dolby Vision, DTS-HD MA, TrueHD) to maximize direct play and minimize server-side transcoding.

**Framework:** WinUI 3 + .NET 8 (C#)
**Playback engine:** libmpv via LibMPVSharp
**Target platform:** Windows 11

---

## Architecture

Four layers with clear separation of concerns:

```
+---------------------------------------------+
|              WinUI 3 UI Layer               |
|  (Pages, Controls, Navigation, Theming)     |
+---------------------------------------------+
|            Application Services             |
|  (Auth, Playback Manager, Library Browser,  |
|   Image Cache, Settings, Progress Tracker)  |
+---------------------------------------------+
|             API Client Layer                |
|  (HTTP client, WebSocket, Token Manager,    |
|   Request/Response models)                  |
+---------------------------------------------+
|           libmpv (via LibMPVSharp)          |
|  (Video/Audio decode, HDR, Subtitle render, |
|   HW accel, HLS, HTTP byte-range)          |
+---------------------------------------------+
```

- **UI Layer** -- WinUI 3 pages and controls. MVVM pattern (ViewModels bind to Views). Handles navigation, theming, user interaction.
- **Application Services** -- Business logic. Coordinates between UI and API. Manages playback sessions, token refresh, image caching, progress reporting.
- **API Client** -- Typed HTTP client wrapping all Continuum API endpoints. Handles auth headers, token refresh, error parsing. Single source of truth for server communication.
- **libmpv** -- Embedded player. Receives stream URLs and subtitle files from the Playback Manager. Handles all decoding, rendering, and hardware acceleration.

---

## UI Structure & Navigation

### Sidebar (always visible)

- "Continuum" branding at top
- Navigation items with icons, pulled from `GET /api/v1/user/libraries`:
  - Home (hardcoded, always first)
  - One entry per library (Movies, TV Shows, Anime, Sports, etc.)
- Below the libraries: Search, Favorites, Watchlist
- Bottom: Settings, profile switcher (shows current profile name)

### Home Page

- **Hero carousel** -- Sections where `featured: true` in the `/home/layout` response. Displays large backdrop image, title, metadata (year, genres), overview text, and a Play button. Navigation dots for cycling between items. Auto-advances on a timer.
- **Section rows** -- Each non-featured section renders as a horizontal scrollable row with a title header. Each item shows poster art with `overlay_summary` badges (resolution, audio codec, release type).
- **Loading flow:**
  1. `GET /api/v1/home/layout` -- get section metadata, render skeleton with thumbhash placeholders immediately
  2. `GET /api/v1/home/sections` -- populate all rows with actual items
  3. Sections with `featured: true` render as the hero carousel at the top
  4. Remaining sections render as horizontal poster rows beneath

### Library Page

- Grid of poster cards with sorting and filtering via `GET /api/v1/catalog` and `GET /api/v1/catalog/filters`
- Sort by: title, year, IMDB rating, date added
- Filter by: genre, studio, resolution, audio language, content rating, etc.
- Pagination with `limit`/`offset`

### Item Detail Page

- Full backdrop with title, logo overlay if available
- Metadata: year, genres, rating, overview, cast/crew
- Version selector (if multiple file versions with different resolutions/codecs)
- Play button, Favorite/Watchlist toggles, rating (1-5 scale)
- For series: season/episode navigation via `GET /api/v1/catalog/series/{id}/seasons` and episode endpoints
- Intro/credits skip markers displayed during playback

### Player Page

- libmpv renders the video (fullscreen or windowed)
- Overlay controls: play/pause, seek bar, volume, subtitle selector, audio track selector, quality/version selector
- Skip Intro / Skip Credits buttons appear when current position is within marker ranges
- Progress reporting every 5-10 seconds
- WebSocket connection for admin commands

### Search

- Search bar triggers `GET /api/v1/catalog?source=search&q=term`
- Results displayed as poster grid

---

## Playback Engine (libmpv Integration)

### Setup

App bundles `mpv-2.dll` and initializes via LibMPVSharp. The mpv render context attaches to a native window handle hosted inside the WinUI layout via `SwapChainPanel` or `WindowNative` interop. Hardware acceleration via D3D11VA/Vulkan.

### Playback Flow

1. User hits Play -- app calls `GET /api/v1/watch/{content_id}` to get file versions, subtitles, user progress
2. App picks the best file version (user preference or highest quality)
3. App calls `POST /api/v1/playback/start` declaring full codec support:
   - `codecs_video`: `["h264", "hevc", "av1", "vp9"]`
   - `codecs_audio`: `["aac", "flac", "opus", "eac3", "ac3", "dts", "truehd"]`
   - `containers`: `["mp4", "mkv"]`
   - `hdr`: `true`
   - `max_resolution`: `"2160p"`
4. Server responds with `session_id`, `play_method`, `stream_url`, `subtitle_urls[]`
5. Based on `play_method`:
   - **Direct:** Feed `stream_url` to mpv -- handles HTTP byte-range seeking natively
   - **Remux:** Same URL, mpv handles identically. Seeking uses `?seek=<seconds>` query param
   - **Transcode:** Call `POST /api/v1/playback/transcode/start`, feed `manifest_url` (HLS) to mpv
6. Load subtitle tracks from `subtitle_urls[]` as external subs (WebVTT). For embedded subs in MKV direct play, mpv extracts and renders them directly.
7. Resume from `position` returned by server

### During Playback

- **Progress reporting:** Fires every 5-10 seconds via `POST /api/v1/playback/{session_id}/progress`. This keeps the session alive (sessions are reaped after ~45s active / ~2min paused without progress).
- **WebSocket:** Connects to `/playback/ws/{session_id}` for admin commands (pause, seek, stop, display message). Sends `hello` on connect, responds with `ack` then `result` to commands.
- **Skip markers:** Skip Intro/Credits buttons appear when current position is within `intro.start`-`intro.end` or `credits.start`-`credits.end` ranges.
- **Audio track switching:** `PATCH /api/v1/playback/{session_id}/audio` -- client reloads stream at the returned position. Play method may change.
- **Stop:** `DELETE /api/v1/playback/{session_id}` -- persists final position.

### HDR & Dolby Vision

libmpv with D3D11VA hardware acceleration passes HDR metadata through to the display. Windows handles HDR-to-SDR tone mapping if the display isn't HDR capable. Dolby Vision Profile 5/8 works on supported hardware.

### Subtitle Rendering

- **Text subtitles (SRT, ASS, VTT):** libmpv renders as overlays natively. Server provides WebVTT via `/stream/{session_id}/subtitles/{index}`. No transcoding required.
- **Bitmap subtitles (PGS, VOBSUB):** During direct play of MKV files, mpv renders these from embedded streams. If the server returns 400 for bitmap subtitle fetch, fall back to requesting burn-in via transcode as a last resort.
- **External subtitle search:** `POST /api/v1/subtitles/search` and `POST /api/v1/subtitles/download` for fetching from external providers (OpenSubtitles).
- **Per-series preferences:** `PUT /api/v1/subtitle-prefs/{series_id}` for sticky subtitle language/mode.

---

## Authentication & Token Management

### Server Management

Stored in `%APPDATA%/ContinuumPlayer/servers.json`:
- List of saved servers: `{ url, name, lastUsed }`
- On launch: if one server, auto-connect. If multiple, show server picker.

### Login Flow

1. User enters server URL -- app validates connectivity
2. Login screen: username + password
3. `POST /api/v1/auth/login` -- receive `access_token`, `refresh_token`, `expires_in`, `user` object
4. Store tokens in Windows Credential Manager, keyed by server URL + username
5. `GET /api/v1/profiles` -- show profile selector
6. If profile has PIN, prompt and verify via `POST /api/v1/profiles/{id}/verify-pin`
7. Set `X-Profile-Id` (and `X-Profile-Token` if PIN-protected) on all subsequent requests

### Token Refresh

- Timer fires at 80% of `expires_in` (~19 hours for 24-hour tokens)
- Calls `POST /api/v1/auth/refresh` proactively -- never waits for a 401 during playback
- On refresh failure: prompt user to re-login
- Refresh token (30-day lifetime) also stored in Credential Manager

### API Key Support

For development/testing, API keys (prefixed `sa_`) can be used in place of JWT tokens via the same `Authorization: Bearer` header. No refresh cycle needed.

### Required Headers

| Header | When |
|---|---|
| `Authorization: Bearer <token>` | Always |
| `X-Profile-Id: <uuid>` | After profile selection |
| `X-Profile-Token: <jwt>` | PIN-protected profiles only |

---

## Image Handling & Caching

All images arrive as presigned S3 URLs with ~4-hour TTL. Cache the image data, not the URL.

- **In-memory LRU cache** -- Recently viewed posters/backdrops kept in memory, capped at ~200MB.
- **Disk cache** -- Images saved to `%APPDATA%/ContinuumPlayer/cache/images/` with content-ID-based filenames. Avoids re-downloading previously viewed content.
- **Thumbhash placeholders** -- Every item includes `*_thumbhash` fields (base64). Decode instantly into blurred placeholder images while real images load.
- **Lazy loading** -- Only fetch images for items currently visible or about to scroll into view.
- **Stale URL handling** -- If a cached image needs re-fetch and the URL has expired, call the item detail endpoint for a fresh presigned URL.

---

## Data Flow & State Management

### MVVM with Dependency-Injected Services

ViewModels hold UI state and bind to Views. No direct API calls from ViewModels -- all go through services.

**Services (singletons via .NET DI):**
- `AuthService` -- login, token storage, refresh timer, profile management
- `ApiClient` -- typed HTTP client, auth headers, error parsing, base URL per server
- `PlaybackManager` -- full playback lifecycle (start, progress reporting, WebSocket, stop)
- `LibraryService` -- catalog browsing, filtering, search
- `HomeService` -- home sections, dismissals
- `ImageService` -- fetch, cache, thumbhash decode
- `UserStateService` -- favorites, watchlist, watched, ratings
- `SettingsService` -- local preferences (last server, last profile, UI state)

### Navigation

- WinUI 3's built-in `NavigationView` for sidebar
- Frame-based page navigation with back stack
- Player page defaults to filling the app window (replacing the current view). User can toggle to true fullscreen (borderless, hides taskbar). Pressing Escape exits fullscreen back to windowed player; pressing Escape again (or back button) exits the player and returns to the previous page.

### Reactive Updates

- Favorites/ratings/watched changes propagate immediately to all visible UI showing that item
- Continue Watching row updates after playback stops

---

## Project Structure

```
ContinuumPlayer/
├── src/
│   ├── ContinuumPlayer/                # Main WinUI 3 app project
│   │   ├── App.xaml                     # App entry, DI container setup
│   │   ├── Views/                       # XAML pages
│   │   │   ├── ServerSelectPage.xaml
│   │   │   ├── LoginPage.xaml
│   │   │   ├── ProfileSelectPage.xaml
│   │   │   ├── HomePage.xaml
│   │   │   ├── LibraryPage.xaml
│   │   │   ├── ItemDetailPage.xaml
│   │   │   ├── PlayerPage.xaml
│   │   │   ├── SearchPage.xaml
│   │   │   └── SettingsPage.xaml
│   │   ├── ViewModels/                  # MVVM ViewModels
│   │   ├── Controls/                    # Reusable UI controls
│   │   │   ├── HeroCarousel.xaml
│   │   │   ├── SectionRow.xaml
│   │   │   ├── PosterCard.xaml
│   │   │   ├── PlayerOverlay.xaml
│   │   │   └── SubtitleSelector.xaml
│   │   ├── Themes/                      # Dark theme, brushes, styles
│   │   └── Assets/                      # App icon, static resources
│   ├── ContinuumPlayer.Core/            # Class library (no UI dependency)
│   │   ├── Services/                    # AuthService, PlaybackManager, etc.
│   │   ├── Models/                      # API response models, DTOs
│   │   └── Api/                         # ApiClient, endpoint wrappers
│   └── ContinuumPlayer.Player/          # libmpv integration library
│       ├── MpvPlayer.cs                 # LibMPVSharp wrapper
│       ├── MpvInterop.cs               # Native window handle hosting
│       └── SubtitleManager.cs           # Subtitle loading/switching
├── libs/
│   └── mpv/                             # mpv-2.dll and dependencies
├── docs/
├── tests/
└── ContinuumPlayer.sln
```

Three projects in the solution:
- **ContinuumPlayer** -- WinUI 3 app (Views, ViewModels, Controls, Themes)
- **ContinuumPlayer.Core** -- Business logic and API client, no UI references. Testable independently.
- **ContinuumPlayer.Player** -- libmpv wrapper, isolated so the rest of the app doesn't touch native interop directly.

---

## Phased Implementation

### Phase 1 -- Foundation & Browsing
- Project scaffolding (WinUI 3, .NET 8, solution structure)
- Server management (add/remove/select server)
- Auth flow (login, token storage, refresh, profile selection)
- Home page (hero carousel, section rows, poster cards, thumbhash placeholders)
- Library browsing (grid view, sorting, filtering, pagination)
- Image caching (memory + disk, lazy loading)
- Dark theme matching Continuum's web UI

### Phase 2 -- Playback
- libmpv integration (embed in WinUI, D3D11VA hardware acceleration)
- Direct play and remux streaming (HTTP byte-range)
- HLS transcode fallback
- Player overlay controls (play/pause, seek, volume)
- Subtitle rendering (text subs via mpv, bitmap sub fallback)
- Audio track switching
- Progress reporting (5-10 second interval)
- Resume from saved position
- Skip Intro / Skip Credits buttons

### Phase 3 -- User Features
- Search
- Favorites, Watchlist, Ratings
- Mark watched/unwatched
- Item detail page (full metadata, cast/crew, version selector)
- Series navigation (seasons/episodes)
- Subtitle preferences (per-series sticky)
- External subtitle search and download

### Phase 4 -- Polish
- WebSocket admin controls
- Continue Watching / Next Up dismissals
- Recommendations sections
- Keyboard shortcuts and media key support
- Error handling and offline/reconnection behavior
- Performance optimization (virtualized lists, prefetching)

---

## Key Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Framework | WinUI 3 + .NET 8 | Modern Windows 11 Fluent Design, native feel |
| Playback engine | libmpv via LibMPVSharp | Unmatched codec support, HDR/DV, HW accel, subtitle rendering |
| Subtitle strategy | libmpv overlay (default), server burn-in (bitmap fallback) | Eliminates transcoding for text subs |
| Credential storage | Windows Credential Manager | OS-level encryption for tokens/passwords |
| Non-sensitive config | JSON in %APPDATA% | Simple, readable, no security concern |
| Multi-server | Supported, auto-connect if single server | Matches Jellyfin/Emby UX pattern |
| Auto-update | Deferred | Manual installs for now |
| Image caching | Memory LRU + disk cache + thumbhash placeholders | Handles expiring S3 URLs, smooth loading UX |
| Navigation | Left sidebar matching Continuum web UI | Familiar layout, library-driven nav |
