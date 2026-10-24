# Silo Desktop Player

> **Current release: 1.2.310.** Updates the native Dolby Vision engine, verifies Profile 7 enhancement processing and Profile 8 playback, and repairs personal Settings, connection speed tests, profile hover and media-button feedback. See [the release record and limits](docs/releases/1.2.309.md) and [SiloCast setup](docs/silocast-testing.md). Local development starts in `D:\SiloPlayer`; read [the current handoff](docs/current-development.md) before editing or building.

Native Windows desktop client for [Silo Server](https://github.com/Silo-Server/silo-server), built with WinUI 3 and libmpv. The project aims to reproduce the Silo WebUI visually and functionally while providing broad native direct-play support for high-bitrate HEVC, HDR, Dolby Vision, lossless audio, and other formats browsers commonly transcode.

> **Project status:** active pre-release development. Version 1.2.310 preserves the prior parity work and SiloCast receiver. Phone interoperability, broader hardware testing and the separate intermittent Next Up removal investigation continue. Dolby Vision source processing is verified for the tested samples; physical Dolby Vision display transport and every legacy container are not certified. The cause of every historical live buffering incident is not established. Targeted verification does not certify complete visual/functional parity. Silo Server changes are re-audited against its official public `main` branch.

## Download

[**Download Silo Desktop Player 1.2.310**](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.310/SiloInstaller-Windows-x64.exe) — Windows 10/11 x64 installer from GitHub Releases.

SHA-256: `BC30DA1E4C8BF09B3DC25A15315158074A5D7A0F18DD9520F90BC6A006A78FF3`

SiloCast is built in and enabled for fresh/missing preferences. The installer automatically adds its Private-network/local-subnet firewall rule, with no extra SiloCast checkbox. A saved opt-out remains respected; enable or disable it under **Settings → Playback → Desktop playback → Allow SiloCast control**. Keep the PC and phone on the same private local network and the desktop app running. See [the testing guide](docs/silocast-testing.md) for setup and troubleshooting. Actual iPhone/Android interoperability remains under testing.

The installer is intentionally unsigned. Runtime Code Integrity testing found that the former self-signed local certificate caused Smart App Control to block files that launch successfully when unsigned. Windows may still display an unknown-publisher warning on other machines. The installer includes the .NET runtime, Windows App Runtime bootstrapper, and validated native libmpv runtime.

## License / private use

This repository and its contents are proprietary and private. All rights are reserved by the project owner. No license is granted to copy, distribute, publish, sublicense, sell, host, modify, or use this software, source code, installers, artwork, branding, documentation, or related assets except with explicit written permission from the owner.

Third-party components retain their own licenses. The externally loaded FSRCNNX shader is LGPL-3.0-or-later; its source, notices, and license texts are included under [libs/mpv/shaders](libs/mpv/shaders/README.md).

The reader includes Foliate and PDF.js; pinned sources, local adaptations and license texts are documented in [reader third-party notices](src/SiloPlayer/Assets/Reader/THIRD-PARTY-NOTICES.md).

## Current parity

Version **1.2.309** adds five correction groups beyond 1.2.304: media-button feedback, Dolby Vision engine/profile support, profile hover geometry, personal Settings/font packaging and connection-test behavior/presentation. The focused comparison uses official Silo Server/WebUI [`d2c12db`](https://github.com/Silo-Server/silo-server/commit/d2c12dbb0f8f40f0a33304f3032f2104c5ee9674). Passed **2,132 main tests**, **nine exact-payload native groups** and **24 packaged playback checks**. See [the current correction ledger and limits](docs/releases/1.2.309.md). This preserves [1.2.304's upstream alignment and stability repairs](docs/releases/1.2.304.md); the table below records the earlier 1.2.241 acceptance pass.

**1.2.241 includes 241 distinct verified correction groups tracked since the October 1 implementation pass.** The patch number follows the [correction ledger](docs/parity/2026-10-01-parity-implementation-ledger.md). Seventy-six groups were newly reconciled during the final implementation and verification cycle; overlapping refinements count once.

Version 1.2.241 completed the recorded [14-area checklist](docs/parity/2026-10-07-easiest-first.md) implementation and finite acceptance pass. Subsequent reports found additional defects; that historical pass is not a claim of complete current parity. The [combined verification report](docs/parity/2026-10-09-combined-verification.md) records its rendered comparisons, physical interactions, original failures, isolated API checks and deliberate native differences.

| Area | Included and verified in 1.2.241 | Continuing coverage / native differences |
|---|---|---|
| Search, personal lists, Notifications, Person and Calendar | Responsive layout, paging, focus/hover, current captions, filters, read-state and recovery behavior | Native focus and raster rounding; Calendar keeps focused headings readable |
| Typed filters and conditional dialogs | Current scopes/operators/suggestions, lossless rules, measured drawers, metadata/image/match and permission/retry flows | Native virtualized choices and file picker; metadata mutations tested on isolated APIs |
| Requests | Current hub/status/detail/season presentation, artwork retention, Back and exact season submission | Production requests were not submitted during verification |
| Collections | Current boards/chooser/manual/Smart/Synced forms, source/artwork/draft preservation, preview and actual mouse/keyboard ordering | Production create/share/delete/sync/reorder writes were exercised through isolated APIs |
| Home and Library | Current heroes/cards/loading states, refresh ownership, scoped browsing, responsive geometry and bounded grids | Saved poster sizing remains a user preference |
| Media details, audiobooks and manga | Initial episode captions, measured navigation, recommendations, media actions/tools and native audio/reader workflows | Native playback and reader surfaces; no production metadata jobs submitted |
| Shared controls and shell | Theme/icon/typography/artwork rules, menus, dialog action order, hover and responsive layout | Windows titlebar, native compositor effects and raster rounding |
| Authentication, profiles and Settings | Login/PIN/network/provider lifecycle, devices/sessions, responsive settings, Home rows and guarded saves | Native pairing differs from browser fallback; no production account changes |
| Watch Party | Selection/resume/risk/permission states, bounded picker and two real local WebSocket clients | Two physical remote viewers remain a separate live test |
| Playback, Shuffle, multipart, reader and subtitles | Published direct/remux/recovery checks, scoped sequencing and subtitle ownership; live 1080p/direct 4K, natural autoplay, successor OSD/fullscreen/Escape/Exit | Broader HDR/audio/display hardware and real provider-job coverage continue; historical midstream buffering is not declared eliminated |
| Server administration | Opens the Silo WebUI for management | Outside the user-only desktop scope |

Release verification: **1,605 unit tests**, **51 unique exact-payload native cases** and **15 published playback-service checks** passed. The initial native Requests fixture failure and its stronger natural-navigation rerun remain documented. The application Release publish completed with no warnings/errors; the installer contains the frozen verified payload. See the [release record](docs/releases/1.2.241.md).

The official public Silo Server reference fetched for the final pass is [1a7a3970a9928efb0157460c10888832a8a74eb1](https://github.com/Silo-Server/silo-server/commit/1a7a3970a9928efb0157460c10888832a8a74eb1). This identifies the source comparison, not the production server revision. Future upstream changes and broader hardware testing remain ongoing.


## Latest release

### 1.2.309 — Dolby Vision and personal Settings

- Upgrade the pinned native playback engine and verify Profile 7 MEL/FEL enhancement processing, Profile 8.1/8.4 and Profile 5 playback, including hardware rendering, pause and seek/resume. Add sanitized video-pipeline diagnostics.
- Align personal Settings layout, navigation, labels and fonts with the current WebUI. Ship and validate all five indexed fonts; center the profile-icon hover backdrop.
- Repair connection speed-test redirects, cancellation, saving and rate-limit handling, with current connection and Auto-budget controls.
- Correct media-page Play/Resume and Watched hover feedback across the full button.
- Publish the exact owner-tested installer. Verification: **2,132 main tests**, **nine exact-payload native groups**, **24 packaged playback checks**, six Settings widths and 16 final screenshots. See [scope, engine provenance and display/container limits](docs/releases/1.2.309.md).

### 1.2.304 — Home stability, streaming recovery and upstream alignment

- Keep cached Home rows, cards, decoded artwork and scroll across return navigation and reconnect catch-up. Guard retired callbacks and defer sidebar pane transitions outside WinUI opening callbacks.
- Recover direct-read deadline cancellation and late bytes at the exact unread offset; preserve caller cancellation and terminal handling for unrelated disposal. Correct false-outage detection so slow-but-live playback can accumulate real stall history.
- Add capability-gated connection speed/learned Auto limits and buffering preferences, explicit audio save/reset, current subtitle behavior, complete Server Collections, episode Show title rules and provider-aware Connect Apps guidance.
- Align metadata editing, Play History labels/durations/layout, Home layout reset/import, hero synopsis limits and Dolby Vision/HDR10+ compatibility wording; remove the obsolete unwatched-sync toggle.
- Publish the unchanged owner-tested installer. Verification: **2,120 main tests**, **32 native runs**, **24 packaged playback checks** and three repeated real libmpv constrained-link scenarios. See [all eighteen correction groups, evidence and limits](docs/releases/1.2.304.md).

### 1.2.286 — upstream alignment, catalog recovery and watched state

- Retire rejected catalog cursors and their cached cards, then retry the visible range once. Add sanitized query/cursor correlation for future diagnostics; the initial rejection trigger remains unproven.
- Update show, season and episode watched actions, descendant badges and parent counts without reloading, including delayed-response and sibling-card cases.
- Apply twelve update groups from the current official Silo source: delivered quality across native/OSC/SiloCast, Watch Party startup, inherited defaults, all-row catalog navigation, Seasonal Picks, detail captions/extras, calendar dates, navigation/provider alignment, synced-list help and primary-profile Discord controls.
- Preserve the episode-poster caption repair across Home, TV/Anime sections and virtualized grids.
- Publish the exact installer the owner installed and approved. Verification passed **2,005 main tests**, **16 exact-payload native groups** and **24 published-service playback groups**. See [the release record](docs/releases/1.2.286.md) and [the 41-commit upstream comparison](docs/parity/2026-10-20-upstream-delta.md) for scope and limits.

### 1.2.271 — current quality contracts and menu updates

- Align Auto and fixed quality choices with current server contracts, preserving canonical preferences, bitrate caps and requested intent across native playback, fullscreen controls, SiloCast and replans.
- Group media actions with the current Manage submenu and move Search for Subtitles to the fixed Subs footer while preserving permissions and selected-file behavior.
- Place Forgot password below the password input, right aligned, with existing provider and capability gates.
- Publish the exact installer the owner installed and approved. Verification passed **1,920 main tests**, **nine exact-payload native groups** and **24 published-service playback groups**. See [the release record](docs/releases/1.2.271.md) for the official upstream reference, evidence and limits.

### 1.2.268 — server alignment, playback preferences and rounded backgrounds

- Match the October 15–16 server contracts: current branding/Settings navigation, metadata-language refreshes, translation provenance, episode spoilers, Home row deduplication, Cast & Crew, Atmos labels and recommendation presentation.
- Add supported Reset Progress and device names. Retire obsolete built-in Trakt routes while preserving existing content and supported imports.
- Add profile subtitle appearance/timing, Not Interested actions and list management, notification-series mute controls, and Show new episodes first.
- Improve Watch Together clock/command synchronization and guard profile-picker Add/Manage with the primary PIN when required.
- Reanchor an idle, forward-only progressive remux on resume after a long pause; preserve direct play and playback ownership.
- Correct oversized oval backgrounds at 18 badge/button sites across Add Row, Requests, Collections, person details, subtitle options, Watch Together and the player shell.
- Publish the exact installer the owner installed and approved. Fresh publication verification passed **1,910 main tests**; the exact 1.2.268 payload passed **five targeted native groups**, including responsive Add Row states and existing poster/detail badge checks. The preceding 1.2.267 candidate passed **58 native scenario runs** and **24 packaged playback groups**; those earlier results are identified separately in [the release record](docs/releases/1.2.268.md).

### 1.2.247 — Library paging, server branding and refresh repairs

- Keep the signed catalog page size stable across Library scrolling, prefetch, count reads and large-filter POST requests. Fix the reproduced malformed/different-query cursor error.
- Use each connected server's favicon for the window icon, separately from its sidebar logo, with a bundled Silo fallback.
- Present Settings' proper layout immediately and preserve a tab selected during loading. Keep enabled toggle thumbs contrasting in normal, hover and pressed states.
- Refresh Home data after notification reconnection while retaining unchanged mounted poster cards; preserve strict permission/profile changes and playback ownership.
- Enable SiloCast for fresh preferences and install its existing Private-LAN rule automatically. Preserve saved opt-outs and the Settings toggle.
- Retain complete redacted exception, HTTP and UI-action diagnostics. Use current removal labels; the separate intermittent Next Up removal exception remains under investigation.
- Publish the verified 1.2.247 installer without rebuilding it. Verification includes **1,669 main tests**, **23 packaged playback checks**, actual Library-tab scrolling at 1080p/ultrawide/4K sizes and native favicon format/color checks. See [the release record](docs/releases/1.2.247.md).

### 1.2.243 — Windows SiloCast receiver and badge fixes

- Add opt-in private-LAN discovery, server-approved phone handoff, native fullscreen playback, remote controls, status and disconnect actions.
- Keep temporary phone playback identity separate from the saved desktop account; guard local takeover, progress and cleanup with captured playback ownership.
- Include the complete 1.2.242 poster and media-detail badge repair, preserving straight pill edges with circular ends.
- Publish the same installer the owner installed. Verification includes 1,687 unit/receiver/discovery tests, 23 published playback groups, settings rendering and 42 badge cases. Real-phone interoperability remains unverified; the broader default native suite stops on a preexisting series-button assertion. See [the release record](docs/releases/1.2.243.md).

### 1.2.241 — completed checklist verification and episode-end repair

- Finalize the current 14-area parity pass across search, personal lists, Calendar, filters, Requests, Collections, Home/Library, media details, shared controls, authentication, Settings, Watch Party and playback/reader behavior.
- Correct initial episode caption visibility, empty navigation space, responsive grids, clipped toolbar labels, recommendation carousels, notification presentation, filter choices and Settings icons.
- Preserve Collection drafts, source configuration and artwork; verify actual keyboard and mouse reordering. Add current permission-gated media tools with paged, virtualized file selection.
- Accept healthy episode endings despite rounded catalog durations, avoiding the reproduced backward-seek recovery loop. Display next-episode runtime in the correct units. Live natural autoplay retained OSD, pause, fullscreen, Escape and Exit.
- Verify 1,605 unit tests, 51 unique native cases and 15 published playback checks. The owner installed and approved this exact installer on October 10. See the [release record](docs/releases/1.2.241.md) and [combined verification](docs/parity/2026-10-09-combined-verification.md).
- Broad hardware/HDR coverage and the separate intermittent midstream-buffering investigation remain ongoing.

### 1.2.94 — detail rating alignment and visible TMDB mark

- Keep movie and series ratings above the description. Measure rating rows within the hero width so wrapped entries reserve enough height, including at fractional display scaling.
- Render the TMDB provider mark with its original green/cyan gradient instead of black against the dark background.
- Verify 32 native rating-layout scenarios at 100%/150% scaling, existing detail/loading/title-art regressions, 1,399 Release tests and 15 published playback checks. See the [rating-layout diagnosis](docs/audit-gaps/2026-10-07-detail-rating-overlap.md).
- Includes all 1.2.92 changes. Final WebUI parity and sustained-playback confirmation remain open.

### 1.2.92 — current checkpoint and playback reconnect repair

- Publish the current 1.2.0 installer checkpoint with clean version 1.2.92, reflecting 92 verified distinct corrections.
- Prevent notification-socket reconnects from falsely invalidating playback, rejecting its progress updates and repeatedly restarting a healthy stream. Real account/profile/server/PIN changes still invalidate old playback.
- Include the episode-transition OSD repair and first-navigation/single-season detail-loading repairs, together with the accumulated Requests, browse, collections, settings, auth, reader and Watch Party work.
- Verify 1,399 Release tests, 15 published playback checks and native detail/loading regressions. See the [root-cause record](docs/audit-gaps/2026-10-03-notification-reconnect-playback.md).
- Final WebUI parity and sustained-playback confirmation remain open. This publishes the checkpoint explicitly requested by the user, rather than declaring the 14-package plan complete.

### 1.1.105

- Fix repeated Home/Continue Watching flashing during bulk library metadata refreshes.
- Keep unchanged cards mounted when refreshed responses omit their display surface or renew image URL signatures. Preserve loaded artwork and retry missing images with fresh URLs.
- Coalesce metadata-event bursts through the existing 30-second catalog cooldown while retaining prompt playback and watched-state updates.
- Verified **1,140 Release tests**, native WinUI regression checks, and clean installer packaging. The native reproduction dropped from **nine unnecessary card preparations to zero** across three refreshes, while genuine progress updates still worked.
- Includes all 1.1.104 fixes. Remaining parity work and the separate playback-buffering investigation remain open. See the [Home refresh diagnosis](docs/audit-gaps/2026-09-26-home-metadata-refresh-flashing.md) for evidence and scope.

See [CHANGELOG.md](CHANGELOG.md) for the complete release history.

## AI video upscaling (experimental)

Version **1.1.102** includes Intel and AMD options alongside NVIDIA RTX VSR. Select **Settings → Playback → Desktop playback → AI video upscaling (experimental)** and restart **Silo for Windows Desktop App**. Preferences are local and off by default; existing NVIDIA opt-ins are preserved.

| Mode | Processing | Verification |
|---|---|---|
| NVIDIA RTX VSR | Driver video enhancement; enable Video Super Resolution in NVIDIA App | Existing RTX path; the NVIDIA indicator verifies driver activation |
| Intel VSR | mpv's Intel D3D11 video-processing extension | Experimental; detected Intel hardware is a candidate, not a guarantee of VSR support |
| FSRCNNX AI | Bundled trained neural-network shader; used for AMD and selectable on NVIDIA/Intel | Actual shader execution verified on RTX 5080; AMD/Intel performance and compatibility need testers |
| Automatic | Prefer RTX VSR, then FSRCNNX on AMD, then Intel VSR | Same experimental limitations apply |

FSRCNNX is a portable neural shader, not AMD driver VSR or FSR. It reconstructs luma at 2× when both display/source dimension ratios exceed 1.3, then mpv fits the result to the display. Enhancement applies to eligible SDR video up to 1080p. HDR, unknown color transfers, rotated/anamorphic content, and native 4K bypass it. Missing hardware, missing shader assets, and detected processing errors use normal playback.

The maximum is **2× per dimension**: 1080p can produce 4K frames; 720p can produce 1440p frames, followed by normal rendering to a 4K display. This creates four times the pixels, not native-4K source detail. Upscaling does not increase the streamed bitrate or convert SDR to HDR.

Press **I** during playback to compare **Source video**, **Processed video**, **Upscaler**, and **Enhancement status**. Intel/NVIDIA statuses say **requested**, because installing a video filter does not prove driver AI activation. FSRCNNX runs after video-filter output, so **AI luma reconstruction (requested)** is shown separately; **Processed video** may remain 1080p while a renderer shader reconstructs it. **Target video bitrate** and measured rates describe the stream, not the local enhancement.

See [implementation and verification notes](docs/ai-upscaling-feasibility.md) for preview limits and test coverage.

See [the multi-GPU test guide](docs/multigpu-upscaling-testing.md) for the tester checklist and remaining hardware validation.

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
pwsh -NoProfile -File installer/build.ps1
```

## Requirements

- Windows 10 version 1809 or newer, or Windows 11
- x64 processor
- A reachable Silo Server instance

## Reference source

Parity work is based on the public [Silo Server GitHub repository](https://github.com/Silo-Server/silo-server). Release-specific source revisions are recorded in [CHANGELOG.md](CHANGELOG.md) when a pass is tied to an exact server commit.

