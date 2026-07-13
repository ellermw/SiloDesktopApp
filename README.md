# Silo Desktop Player

Native Windows desktop client for [Silo Server](https://github.com/Silo-Server/silo-server), built with WinUI 3 and libmpv. The project aims to reproduce the Silo WebUI visually and functionally while providing broad native direct-play support for high-bitrate HEVC, HDR, Dolby Vision, lossless audio, and other formats browsers commonly transcode.

> **Project status:** active pre-release development. The application is usable, but WebUI parity and playback hardening are not yet complete. Silo Server is also evolving toward 1.0, so parity is continuously re-audited against its current `main` branch.

## Download

[**Download the latest Silo Desktop Player**](https://github.com/ellermw/SiloDesktopApp/releases/latest/download/SiloInstaller-Windows-x64.exe) — Windows 10/11 x64 installer from GitHub Releases.

The installer is currently unsigned, so Windows may display a SmartScreen warning. It includes the .NET runtime, Windows App Runtime bootstrapper, and the validated native libmpv runtime.

## Current parity

Status meanings:

- **Functional; visual parity incomplete** — principal data and actions work, but the page is not considered complete until side-by-side visual and interaction verification matches the current WebUI.
- **Substantial** — the principal workflows exist, with additional visual or edge-case work remaining.
- **Partial** — usable foundation exists, but meaningful WebUI behavior is still missing.
- **Hardening** — implemented, with compatibility, performance, or reliability work still underway.

| Area | Status | Current state |
|---|---|---|
| Authentication and profiles | Substantial | Login, refresh tokens, profile selection, PINs, profile management, signup, and setup flows exist. Device login and the complete impersonation lifecycle remain to be finished. |
| Catalog and library browsing | Substantial | Virtualized browsing, filters, sorting, search, collections, favorites, watchlist, history, calendar, recommendations, and requests are implemented. Exact layout details and literary-media browsing still need work. |
| Home screen | Partial | Server-defined sections, hero content, continue watching, recommendations, customization, and incremental loading exist. Exact current card/section behavior and cross-surface refresh remain under review. |
| Item and person details | Partial | Movie, series, season, episode, cast/crew, versions, watched/favorite/watchlist/rating, and metadata actions exist. Current pre-play selectors, inline episode rules, extras, trailers, and all media-type variants remain incomplete. |
| Audiobooks, ebooks, and manga | Partial | Models, ebook reading foundation, and portions of detail/browse support exist. Current audiobook player, manga presentation, reader behavior, grouping, and literary metadata flows need a dedicated parity pass. |
| User collections | Substantial | Browse, create, edit, manual/smart rules, imports, and collection management exist. Current templates, guided rules, collage/scheduling details, and visual polish remain. |
| Notifications | Substantial | Notification center and user notification settings are implemented, including current delivery configuration foundations. Additional current-server edge cases remain to be audited. |
| Settings | Substantial | Playback, subtitles, appearance, theme editor, accessibility, home, card overlays, libraries, history import, webhook sync, watch providers, profiles, and notification controls are present. The server continues to add fields, so these routes remain subject to drift audits. |
| Admin: Libraries | Functional; visual parity incomplete | The current table, real drag reorder, separate live scan/metadata work rows, warning rows, scan queue, diagnostics, and seven-type editor are implemented. Primary rows now render before slower diagnostics. A final installed-build side-by-side pass and remaining fine visual tuning are still required. |
| Admin: Dashboard | Functional; visual parity incomplete | Incremental sections, live sessions, current stats, corrected Trakt 24h metrics, WebUI scan snapshot/progress states, users, activity links, and scan controls are implemented. A final installed-build side-by-side pass and remaining fine visual tuning are still required. |
| Admin: Activity | Functional; visual parity incomplete | Realtime stream state, search/filter/sort, client/profile/IP presentation, IP history lookup, playback position/state, grouped container/video/audio decisions, hardware transcode mode, session controls, log links, inline FFmpeg output, and the current Playback/Node/Time/Actions table structure are implemented. Installed-build comparison and fine visual tuning remain. |
| Admin: Logs | Functional; visual parity incomplete | Live application/audit streams, shared live playback-session filtering, playback summaries, cursor loading, reconnect, FFmpeg focus, and full operational-log detail sheets are implemented. Filter restarts are serialized and page-exit safe; installed-build comparison remains. |
| Admin: Collections | Functional; visual parity incomplete | The list follows the current WebUI's all-library sections and scoped group boards, including artwork, counts, visibility/featured/source badges, template entry, source selection, sync, edit, delete, and reorder actions. Create/edit now uses a dedicated full-page workspace; exact source-specific import layouts and live screenshot comparison remain incomplete. |
| Admin: Sections | Functional; visual parity incomplete | Live WebUI comparison now drives this page. The list has current recipe labels, drag-and-drop ordering, gallery creation, Home/Library scopes, badges, and the WebUI-style editor drawer with recipe-specific Continue Type controls. A final installed-build side-by-side pass is still required. |
| Admin: Requests | Functional; visual parity incomplete | The current Queue, Settings, Integrations, and User Overrides tabs are implemented. Queue rows include requester/library context, per-target quality/router status and errors, detail links, decline reasons, and action states. Global settings, plugin-backed router selection/configuration, and user limits call the current APIs; plugin-schema-specific form rendering and final visual tuning remain. |
| Remaining admin pages | Partial | Users, tasks, history, maintenance, providers, plugins, nodes, API keys, invites, subtitles, recommendations, autoscan, devices, policy, access groups, marker history, and settings have foundations of varying depth. Each still needs a fresh current-WebUI page audit. |
| Player on-screen controls | Substantial | Current visual control foundation, play/pause, seek, volume, fullscreen state sync, quality/audio/subtitle menus, intro/credits actions, next episode, keyboard shortcuts, and stats are present. Chapter thumbnails, subtitle actions, playing-next/postroll, PiP/mini-player details, and final state polish remain. |
| Native playback engine | Hardening | Direct play, remux, HLS fallback, D3D11VA, HEVC/AV1/VP9/H.264, HDR paths, subtitle rendering, track switching, progress reporting, and stall recovery foundations exist. High-bitrate 4K, Dolby Vision, HDR/tone mapping, TrueHD/Atmos/DTS passthrough, fastest startup/seek, and long-session reliability remain active work. |
| Watch Party | Partial | Join/create, room membership, suggestions, and realtime foundations exist. Content search, series drill-down, spotlight/now-playing UI, auto-start, and complete synchronization remain. |

## Recent release work

### 1.1.40

- Re-audited Admin Activity, Logs, Collections, Sections, and Requests against public Silo Server commit `28c6ddc237b9a3ef0102a9ec7514e5654865a3fe` and the signed-in live WebUI.
- Rebuilt Activity around the current User / Stream / Playback / Node / Time / Actions table, including grouped container/video/audio delivery summaries and current inline actions.
- Fixed Logs so playback-session and FFmpeg filters consistently restart the live WebSocket stream, serialize reconnects, and cannot reopen after leaving the page.
- Added stable skeleton loading, parallel requests, and latest-selection-wins guards to Collections and Sections, eliminating blank/empty flashes and unnecessary sequential waits.
- Replaced the obsolete single-list Media Requests admin page with the current Queue, Settings, Integrations, and User Overrides experience, including per-target fulfillment failures and plugin-backed routing configuration.
- Expanded source-parity regression coverage; the release passes 240 tests and a zero-warning x64 build.

### 1.1.39

- Re-audited Admin Dashboard against public Silo Server commit `28c6ddc237b9a3ef0102a9ec7514e5654865a3fe` and the live WebUI.
- Added WebUI-style client/version badges to Now Playing cards and client context to Recent Activity.
- Corrected episode cards to show the episode title with `Sx · Ex — Series` underneath.
- Added paused poster treatment, neutral link colors, exact Activity/Scan Line icon geometry, and richer live scan phase/progress summaries.
- Added immediate Dashboard skeletons for stats, Now Playing, libraries, and users so navigation paints useful structure before network requests finish.

### 1.1.38

- Fixed the overlapping top-right activity indicator by ensuring only the Admin shell owns that control while Admin pages are active.
- Restored the current WebUI's Stale External IDs diagnostic by updating the desktop client to the current public Silo API route.
- Prevented the Libraries table from appearing blank during its initial request by rendering WebUI-style loading rows immediately.
- Corrected the Add/Edit Library editor's 768 px layout, section rail, type-card grid, and right-aligned footer actions.
- Aligned library-row scan, metadata refresh, mount verification, edit, delete, and guarded empty-root actions with the current WebUI order and labels.

### 1.1.37

- Reworked Admin Libraries as a complete surface: live scan and metadata activity now occupy WebUI-style full-width rows beneath each library, while empty-root warnings remain in their own block below normal rows.
- Rebuilt Add/Edit Library General with visible seven-type cards, the WebUI Enabled setting, active section-rail states, matching dialog headers/actions, and validation that keeps the editor open and focuses the invalid section.
- Removed the multi-second blank Libraries page by rendering its primary table immediately and loading diagnostics, providers, and refresh-job data independently.
- Corrected Dashboard scan snapshots/events, active-scan copy, amber status state, stop controls, recent activity refresh, stat-card radius, and header action geometry.
- Fixed Trakt 24-hour counters that incorrectly displayed zero because numeric-suffix JSON fields were not mapped to the server contract.
- Corrected Server Activity's false disconnected marker when it loads after the shared event channel is already connected, and matched the WebUI's 36px trigger and 18px badge geometry.

### 1.1.36

- Corrected the visibly broken top-right server-activity control with the WebUI pulse icon, red count badge, and stable geometry instead of a Windows-version-dependent font glyph.
- Updated Cinema Dark's stale muted-text token to the current WebUI value, improving navigation, subtitles, metadata, and secondary button text across the application.
- Fixed Admin Libraries initial loading so Unmatched Items, Troubleshooting, Stale External IDs, and refresh-job data are present in the first rendered page instead of arriving after the only rebuild.
- Fixed the realtime scan snapshot/event subscription so per-library scan queues and progress rows appear and update like the WebUI.
- Replaced desktop-only move arrows with actual drag-and-drop library ordering, corrected action order/icons, tightened panel radii, and fixed header wrapping/button glyphs.

### 1.1.35

- Began the live Chrome-to-desktop parity workflow and corrected Admin Sections against the signed-in current WebUI rather than treating functional coverage as visual completion.
- Added server-provided admin wordmark and dynamic server naming, while retaining bundled Silo branding as the offline fallback.
- Matched the Sections table's current title scale, labels, row density, recipe names, badges, drag ordering, gallery entry point, and 512px right-side editor geometry.
- Added the WebUI editor field order, Featured helper card, Enabled card, stacked footer actions, audiobook/ebook scopes, and Watching/Listening recipe configuration.
- Rebuilt the Dashboard Trakt summary into the WebUI's three-column footer instead of the compressed desktop-only sentence.
- Sections and Dashboard remain explicitly visual-parity-incomplete until version 1.1.35 is installed and compared side by side with the live WebUI.

### 1.1.34

- Replaced the remaining Admin Collections create/edit dialog with a dedicated full-page workspace inside the Admin route.
- Added WebUI-style back navigation, source-aware editor titles, change-source handling, a wide form surface, persistent save/cancel actions, and visible save errors.
- Preserved collection artwork upload, multi-library assignment, group placement, source configuration, scheduling, visibility, and featured controls.

### 1.1.33

- Replaced the desktop-only Admin Collections table with the current WebUI's library-section and scoped group-board structure.
- Added poster rows, library/count/source/visibility/featured metadata, conditional sync actions, current empty states, and compact WebUI-style actions.
- Restored the actual Browse Templates entry point and added the WebUI source-type chooser for smart/manual, MDBList, TMDB, Trakt, and template flows.
- Kept Collections marked visual-parity-incomplete: its dedicated full-page create/edit/import workspace still needs the next parity pass.

### 1.1.32

- Rebuilt Admin Logs page geometry, typography, tabs, filters, tables, and playback-summary layout against the current WebUI source.
- Added the missing disconnected-stream Reconnect action.
- Replaced the desktop-only inline log detail panel with the WebUI-style light-dismiss 640px right-side detail sheet.
- Preserved live application/audit streaming and added usable cursor loading beyond the WebUI's current server-side-only cursor notice.

### 1.1.31

- Reworked Admin Activity against the current WebUI desktop layout: page width, 52px title, 24px rhythm, red live badge, compact refresh/realtime header, and 20px summary/IP surfaces.
- Matched the current six-column stream table proportions, row gutters, header spacing, and viewport-relative scroll height.
- Preserved the current client/profile/IP presentation, expandable playback decisions, session controls, logs, and FFmpeg inspection from 1.1.29.
- Activity remains marked visual-parity-incomplete until a live side-by-side screenshot pass is available.

### 1.1.30

- Removed the desktop-only Admin header strip and repositioned Server Activity to match the WebUI's floating desktop control.
- Rebuilt the shared Admin sidebar footer to use the WebUI build-card and Back to App structure.
- Corrected Dashboard page width, gutters, vertical rhythm, title/subtitle scale, card radii, action icons, number formatting, and stream-card geometry.
- Corrected Libraries page width, gutters, title/subtitle scale, table surface/row/action sizing, and unified all collapsed diagnostics with the WebUI icon/count/chevron presentation.
- Dashboard, Libraries, and Activity remain explicitly marked visual-parity-incomplete until side-by-side validation is complete.

### 1.1.29

- Updated Admin Activity to the current session contract, including client identity and playback-position fields.
- Expanded search and IP lookup behavior to match the current WebUI.
- Added playing/paused position presentation, expandable container/video/audio decision details, hardware transcode mode, and preserved inline FFmpeg inspection and session controls.
- Removed Claude workspace and instruction artifacts from the public repository and added ignore rules to prevent them from returning.

### 1.1.28

- Rebuilt Admin Dashboard loading so stats, sessions, libraries, and users render independently instead of waiting for the slowest request.
- Added current Trakt activity metrics and accurate movie/show file counts.
- Added active library scan state, progress, scan-to-stop behavior, working navigation links, manual refresh feedback, and active-page refresh.
- Completed the current Admin Libraries milestone introduced in 1.1.27.

### 1.1.27

- Updated Admin Libraries for the current plugin provider-chain contract.
- Replaced the incorrect local Windows folder picker with the remote Silo server filesystem browser.
- Added current metadata fields, manga type, provider defaults, poster actions, stateful scan/refresh controls, immediate Scan All feedback, server-wide unmatched search, and collapsed diagnostics.

## Playback goals

- Prefer direct play whenever libmpv can decode the source.
- Preserve 4K HDR and high-bitrate remux quality without unnecessary server transcoding.
- Support HEVC, H.264, AV1, VP9, HDR10, Dolby Vision where the render path permits it, TrueHD/Atmos, DTS families, FLAC, EAC3, AAC, and Opus.
- Start playback quickly, seek responsively, maintain server sessions reliably, and recover from network/cache stalls without silently stopping.
- Fall back to remux or HLS only when the selected media, tracks, subtitles, server policy, or device capability requires it.

## Tech stack

- WinUI 3 / .NET 8 / Windows App SDK
- libmpv embedded through native P/Invoke
- D3D11 hardware decoding and rendering
- CommunityToolkit.Mvvm and dependency injection
- Inno Setup packaging

## Project structure

```text
src/
  SiloPlayer/          WinUI application, views, view models, controls, and services
  SiloPlayer.Core/     API clients, contracts, and shared services
  SiloPlayer.Player/   Native libmpv wrapper
tests/
  SiloPlayer.Tests/    Unit, contract, and source-parity regression tests
libs/mpv/              Native player runtime and Silo OSC script
installer/             Publish and Inno Setup packaging
docs/                  Page audits and parity tracking
```

## Build and test

Visual Studio 2022 with Windows App SDK tooling or a compatible .NET 8 SDK is required.

```powershell
dotnet build SiloPlayer.sln -c Release -p:Platform=x64
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release -p:Platform=x64
powershell -ExecutionPolicy Bypass -File installer/build.ps1
```

## Requirements

- Windows 10 version 1809 or newer, or Windows 11
- x64 processor
- A reachable Silo Server instance

## Reference source

Parity work is based on the public [Silo Server GitHub repository](https://github.com/Silo-Server/silo-server). Release 1.1.40 was compared against Silo Server commit `28c6ddc237b9a3ef0102a9ec7514e5654865a3fe` from July 11, 2026.
