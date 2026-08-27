# Silo Desktop Player

Native Windows desktop client for [Silo Server](https://github.com/Silo-Server/silo-server), built with WinUI 3 and libmpv. The project aims to reproduce the Silo WebUI visually and functionally while providing broad native direct-play support for high-bitrate HEVC, HDR, Dolby Vision, lossless audio, and other formats browsers commonly transcode.

> **Project status:** active pre-release development. The application is usable, but WebUI parity and playback hardening are not yet complete. Silo Server is also evolving toward 1.0, so parity is continuously re-audited against its current `main` branch.

## Download

[**Download Silo Desktop Player 1.1.91**](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.1.91/SiloInstaller-Windows-x64.exe) — Windows 10/11 x64 installer from GitHub Releases.

SHA-256: `6723DB1233FC03363F2034ED4ABBD43C20B8FE868C67B4941C9BB561AB535276`

The QA installer is intentionally unsigned. Runtime Code Integrity testing found that the former self-signed local certificate caused Smart App Control to block files that launch successfully when unsigned. Windows may still display an unknown-publisher warning on other machines. The installer includes the .NET runtime, Windows App Runtime bootstrapper, and validated native libmpv runtime.

## License / private use

This repository and its contents are proprietary and private. All rights are reserved by the project owner. No license is granted to copy, distribute, publish, sublicense, sell, host, modify, or use this software, source code, installers, artwork, branding, documentation, or related assets except with explicit written permission from the owner.

## Current parity

Status meanings:

- **Functional; visual parity incomplete** — principal data and actions work, but the page is not considered complete until side-by-side visual and interaction verification matches the current WebUI.
- **Substantial** — the principal workflows exist, with additional visual or edge-case work remaining.
- **Partial** — usable foundation exists, but meaningful WebUI behavior is still missing.
- **Hardening** — implemented, with compatibility, performance, or reliability work still underway.

| Area | Status | Current state |
|---|---|---|
| Authentication and profiles | Substantial | Login, refresh tokens, profile selection, PINs, profile management, signup, setup, public server branding, QR/device login, and the complete administrator impersonation lifecycle with restart/stale-session recovery are implemented. Final installed visual comparison remains. |
| Shared shell and navigation | Substantial | The 260 px desktop sidebar, 64 px detail immersion rail with 150 ms hover expansion, current mobile header/drawer, active-route synchronization, dynamic libraries/pins/apps, profile avatar/fallback behavior, notification/activity chrome, Alt+Left/mouse/controller Back, cached route transitions, and playback-to-shell restoration are implemented. Installed comparison and the Playing Next close regression still require QA confirmation. |
| Catalog and library browsing | Substantial | Virtualized browsing, filters, sorting, search, collections, favorites, watchlist, history, calendar, recommendations, and requests are implemented. Exact layout details and literary-media browsing still need work. |
| Home screen | Substantial | Server-defined sections, hero content, continue watching, next up, recommendations, customization, incremental refresh, current card variants, and audiobook rows are implemented. Installed-build visual comparison and remaining responsive edge cases are still required. |
| Item and person details | Substantial | Movie, series, season, episode, cast/crew, versions, watched/favorite/watchlist/rating, trailers, extras, edition selection, current multi-version Media Info spec sheets, automatic split previews, marker workflows, and current More actions are implemented. Installed-build comparison and remaining edge cases are still required. |
| Audiobooks, ebooks, and manga | Substantial | Current server contracts, grouped browsing, literary detail surfaces, manga volume/chapter reading through the shared reader, next-chapter navigation, read progress, and the WebUI-style audiobook mini/expanded player are present. The reader recognizes the current EPUB/PDF/MOBI/AZW/AZW3/CBZ/CBR/FB2/FBZ contract; remaining format-specific compatibility and final installed visual validation are still required. |
| User collections | Substantial | Browse, create, edit, manual/smart rules, imports, and collection management exist. Current templates, guided rules, collage/scheduling details, and visual polish remain. |
| Notifications | Substantial | Notification center and user notification settings are implemented, including current delivery configuration foundations. Additional current-server edge cases remain to be audited. |
| Settings | Substantial | Playback, subtitles, appearance, theme editor, accessibility, date/time formats, home, card overlays, libraries, history import, webhook sync, watch providers, profiles, and notification controls are present. Date/time choices now flow through the current admin and operational surfaces. The server continues to add fields, so these routes remain subject to drift audits. |
| Admin: Libraries | Functional; visual parity incomplete | The current table, real drag reorder, separate live scan/metadata work rows, warning rows, scan queue, diagnostics, and seven-type editor are implemented. Primary rows now render before slower diagnostics. A final installed-build side-by-side pass and remaining fine visual tuning are still required. |
| Admin: Dashboard | Functional; visual parity incomplete | Incremental sections, live sessions, current stats, corrected Trakt 24h metrics, WebUI scan snapshot/progress states, users, activity links, and scan controls are implemented. A final installed-build side-by-side pass and remaining fine visual tuning are still required. |
| Admin: Activity | Functional; visual parity incomplete | Realtime stream state, search/filter/sort, client/profile/IP presentation, IP history lookup, playback position/state, grouped container/video/audio decisions, hardware transcode mode, session controls, log links, inline FFmpeg output, and the current Playback/Node/Time/Actions table structure are implemented. Installed-build comparison and fine visual tuning remain. |
| Admin: Logs | Functional; visual parity incomplete | Live application/audit streams, shared live playback-session filtering, playback summaries, cursor loading, reconnect, FFmpeg focus, and full operational-log detail sheets are implemented. Filter restarts are serialized and page-exit safe; installed-build comparison remains. |
| Admin: Collections | Functional; visual parity incomplete | The list follows the current WebUI's all-library sections and scoped group boards, including artwork, counts, visibility/featured/source badges, template entry, source selection, sync, edit, delete, and reorder actions. Create/edit now uses a dedicated full-page workspace; exact source-specific import layouts and live screenshot comparison remain incomplete. |
| Admin: Sections | Functional; visual parity incomplete | Live WebUI comparison now drives this page. The list has current recipe labels, drag-and-drop ordering, gallery creation, Home/Library scopes, badges, and the WebUI-style editor drawer with recipe-specific Continue Type controls. A final installed-build side-by-side pass is still required. |
| Admin: Requests | Functional; visual parity incomplete | The current Queue, Settings, Integrations, and User Overrides tabs are implemented. Queue rows include requester/library context, per-target quality/router status and errors, detail links, decline reasons, and action states. Global settings, plugin-backed router selection/configuration, and user limits call the current APIs; plugin-schema-specific form rendering and final visual tuning remain. |
| Admin: Automation | Functional; visual parity incomplete | Autoscan, Scheduled Tasks, Subtitles, Marker History, and Recommendations were re-audited against the current WebUI. Current source/activity structure, task metadata/actions, ten-column subtitle management, marker audit table, recommendation job controls, embedding presets, and connection checks are implemented. Fine responsive and installed-build visual verification remain. |
| Admin: Users and History | Functional; visual parity incomplete | Users now has the current Access Groups action, sortable seven-column table, invite-code workflow, and user actions. Playback History includes current filters, stats, polling/manual refresh, table and log links. History Import uses real API-key status, current source controls, discovery/mappings, runs, and current empty states. Fine visual tuning remains. |
| Admin: System | Functional; visual parity incomplete | Settings, Plugins, Nodes, API Keys, and Maintenance were re-audited against the current WebUI. Current grouped settings navigation, branding asset upload/preview/delete, Search diagnostics, safe Source/Changelog/Support links, authenticated plugin pages, plugin configuration/update policy, node capacity/load columns, API-key flows, and catalog import/export jobs are implemented. Installed-build visual verification remains. |
| Admin: Access Groups and Devices | Functional; visual parity incomplete | Access Groups now follows the current responsive cards and in-place editor sections. Devices now uses the current 1920px fleet console with pulse totals, grouping pivots, saved views, platform/override/recency facets, grouped device rows, and an in-place per-profile override editor. Installed-build side-by-side tuning and remaining keyboard/link refinements are still required. |
| Remaining admin work | Partial | Provider/policy edge cases and final installed-build visual verification remain across the admin suite. Dashboard, Libraries, Activity, Collections, Sections, Requests, and other pages retain explicit visual-parity-incomplete status until those checks pass. |
| Player on-screen controls | Substantial | Current cinema controls, recap/intro/credits actions, marker/chapter seek regions, real chapter thumbnails, rich audio/subtitle/quality menus, live AI subtitle translation, bounded playback-info sections, credits countdown, post-roll/finished screens, On Deck, PiP, keyboard shortcuts, and immediate fullscreen/PiP synchronization are implemented. Installed real-playback comparison and remaining edge-case geometry/focus validation are still required. |
| Native playback engine | Hardening | Direct play, remux, HLS fallback, D3D11VA, HEVC/AV1/VP9/H.264, HDR paths, subtitle rendering, live track switching, progress/session keepalive, seamless replacement sessions, byte-range stall recovery, upstream-idle recovery, and premature-EOF guards are implemented. High-bitrate 4K, Dolby Vision, HDR/tone mapping, TrueHD/Atmos/DTS passthrough, fastest startup/seek, and long-session reliability remain active real-media validation work. |
| Watch Party | Substantial | Create/join, room membership, suggestions, realtime synchronization, host/guest policy, transport controls, connection state, invite copy, end-room confirmation, and player sync overlay are implemented. Installed multi-client testing and remaining edge cases are still required. |

## Latest release

### 1.1.91

- Eliminated Home-page blank flashes caused by overlapping realtime refreshes.
- Reconciled Home-section items in place so playback progress, watched state, and newly scanned content update without rebuilding the page.
- Kept the featured hero synchronized when its item collection changes, including the stale-snapshot case identified during CodeRabbit review.
- Added regression coverage for realtime refresh gating, section reconciliation, and hero refresh behavior.
- Corrected the audited Windows App Runtime installer hook so the current Inno Setup compiler can package it successfully.
- Verified with 829 passing tests, CodeRabbit's clean review, native libmpv validation, and a successful x64 installer build.

See [CHANGELOG.md](CHANGELOG.md) for the complete release history.
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
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release
powershell -ExecutionPolicy Bypass -File installer/build.ps1
```

## Requirements

- Windows 10 version 1809 or newer, or Windows 11
- x64 processor
- A reachable Silo Server instance

## Reference source

Parity work is based on the public [Silo Server GitHub repository](https://github.com/Silo-Server/silo-server). Release-specific source revisions are recorded in [CHANGELOG.md](CHANGELOG.md) when a pass is tied to an exact server commit.
