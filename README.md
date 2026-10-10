# Silo Desktop Player

Native Windows desktop client for [Silo Server](https://github.com/Silo-Server/silo-server), built with WinUI 3 and libmpv. The project aims to reproduce the Silo WebUI visually and functionally while providing broad native direct-play support for high-bitrate HEVC, HDR, Dolby Vision, lossless audio, and other formats browsers commonly transcode.

> **Project status:** active pre-release development. Version 1.2.241 completes the current 14-area implementation and specified verification pass, including the owner's final installer check. Ongoing playback and hardware testing continues; Silo Server changes are re-audited against its official public `main` branch.

## Download

[**Download Silo Desktop Player 1.2.241**](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.241/SiloInstaller-Windows-x64.exe) — Windows 10/11 x64 installer from GitHub Releases.

SHA-256: `B778381FEAB926E325F041FA62060805E89588A611ED9F05D1213480BE4943F0`

The installer is intentionally unsigned. Runtime Code Integrity testing found that the former self-signed local certificate caused Smart App Control to block files that launch successfully when unsigned. Windows may still display an unknown-publisher warning on other machines. The installer includes the .NET runtime, Windows App Runtime bootstrapper, and validated native libmpv runtime.

## License / private use

This repository and its contents are proprietary and private. All rights are reserved by the project owner. No license is granted to copy, distribute, publish, sublicense, sell, host, modify, or use this software, source code, installers, artwork, branding, documentation, or related assets except with explicit written permission from the owner.

Third-party components retain their own licenses. The externally loaded FSRCNNX shader is LGPL-3.0-or-later; its source, notices, and license texts are included under [libs/mpv/shaders](libs/mpv/shaders/README.md).

The reader includes Foliate and PDF.js; pinned sources, local adaptations and license texts are documented in [reader third-party notices](src/SiloPlayer/Assets/Reader/THIRD-PARTY-NOTICES.md).

## Current parity

**1.2.241 includes 241 distinct verified correction groups tracked since the October 1 implementation pass.** The patch number follows the [correction ledger](docs/parity/2026-10-01-parity-implementation-ledger.md). Seventy-six groups were newly reconciled during the final implementation and verification cycle; overlapping refinements count once.

The current [14-area checklist](docs/parity/2026-10-07-easiest-first.md) has completed its specified implementation and finite acceptance pass. The owner installed and approved the final candidate. The [combined verification report](docs/parity/2026-10-09-combined-verification.md) records rendered comparisons, physical interactions, original failures, isolated API checks and deliberate native differences.

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

