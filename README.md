# Silo Desktop Player

Native Windows desktop client for [Silo Server](https://github.com/Silo-Server/silo-server), built with WinUI 3 and libmpv. The project aims to reproduce the Silo WebUI visually and functionally while providing broad native direct-play support for high-bitrate HEVC, HDR, Dolby Vision, lossless audio, and other formats browsers commonly transcode.

> **Project status:** active pre-release development. The application is usable, but WebUI parity and playback hardening are not yet complete. Silo Server is also evolving toward 1.0, so parity is continuously re-audited against its current `main` branch.

## Download

[**Download Silo Desktop Player 1.1.31**](https://transfers.taverncdn.com/j6pnS0jSmc/SiloInstaller-1.1.31-Setup.exe) — Windows 10/11 x64 installer, available for 180 days.

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
| Admin: Libraries | Functional; visual parity incomplete | Current provider-chain contract, remote server folder browser, library types, metadata language, AI translation, trailer kinds, chapter/intro settings, posters, scan queue/progress/cancel, reorder, unmatched items, ambiguous/skipped roots, stale IDs, and collapsed diagnostics are implemented. Layout and styling still require a strict side-by-side WebUI parity pass. |
| Admin: Dashboard | Functional; visual parity incomplete | Incremental section loading, active sessions, session controls, current stats, Trakt activity, library scan state/progress, users, activity links, manual refresh, and active-page refresh are implemented. Layout and styling still require a strict side-by-side WebUI parity pass. |
| Admin: Activity | Functional; visual parity incomplete | Realtime stream state, search/filter/sort, client/profile/IP presentation, IP history lookup, playback position/state, container/video/audio decision details, hardware transcode mode, session controls, log links, and inline FFmpeg output are implemented. Layout and styling still require a strict side-by-side WebUI parity pass. |
| Remaining admin pages | Partial | Users, tasks, logs, history, maintenance, collections, sections, providers, plugins, nodes, API keys, invites, subtitles, requests, recommendations, autoscan, devices, policy, access groups, marker history, and settings have foundations of varying depth. Each still needs a fresh current-WebUI page audit. |
| Player on-screen controls | Substantial | Current visual control foundation, play/pause, seek, volume, fullscreen state sync, quality/audio/subtitle menus, intro/credits actions, next episode, keyboard shortcuts, and stats are present. Chapter thumbnails, subtitle actions, playing-next/postroll, PiP/mini-player details, and final state polish remain. |
| Native playback engine | Hardening | Direct play, remux, HLS fallback, D3D11VA, HEVC/AV1/VP9/H.264, HDR paths, subtitle rendering, track switching, progress reporting, and stall recovery foundations exist. High-bitrate 4K, Dolby Vision, HDR/tone mapping, TrueHD/Atmos/DTS passthrough, fastest startup/seek, and long-session reliability remain active work. |
| Watch Party | Partial | Join/create, room membership, suggestions, and realtime foundations exist. Content search, series drill-down, spotlight/now-playing UI, auto-start, and complete synchronization remain. |

## Recent release work

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

Parity work is based on the public [Silo Server GitHub repository](https://github.com/Silo-Server/silo-server). Release 1.1.28 was compared against Silo Server commit `28c6ddc237b9a3ef0102a9ec7514e5654865a3fe` from July 11, 2026.
