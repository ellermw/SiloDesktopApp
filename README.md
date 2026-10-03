# Silo Desktop Player

Native Windows desktop client for [Silo Server](https://github.com/Silo-Server/silo-server), built with WinUI 3 and libmpv. The project aims to reproduce the Silo WebUI visually and functionally while providing broad native direct-play support for high-bitrate HEVC, HDR, Dolby Vision, lossless audio, and other formats browsers commonly transcode.

> **Project status:** active pre-release development. The application is usable, but WebUI parity and playback hardening are not yet complete. Silo Server is also evolving toward 1.0, so parity is continuously re-audited against its current `main` branch.

## Download

[**Download Silo Desktop Player 1.2.92**](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.92/SiloInstaller-Windows-x64.exe) — Windows 10/11 x64 installer from GitHub Releases.

SHA-256: `548CFCC694747FC230C03F05F23C3F6B768130A51D0D29167456515ABA50BC1B`

The installer is intentionally unsigned. Runtime Code Integrity testing found that the former self-signed local certificate caused Smart App Control to block files that launch successfully when unsigned. Windows may still display an unknown-publisher warning on other machines. The installer includes the .NET runtime, Windows App Runtime bootstrapper, and validated native libmpv runtime.

## License / private use

This repository and its contents are proprietary and private. All rights are reserved by the project owner. No license is granted to copy, distribute, publish, sublicense, sell, host, modify, or use this software, source code, installers, artwork, branding, documentation, or related assets except with explicit written permission from the owner.

Third-party components retain their own licenses. The externally loaded FSRCNNX shader is LGPL-3.0-or-later; its source, notices, and license texts are included under [libs/mpv/shaders](libs/mpv/shaders/README.md).

The reader includes Foliate and PDF.js; pinned sources, local adaptations and license texts are documented in [reader third-party notices](src/SiloPlayer/Assets/Reader/THIRD-PARTY-NOTICES.md).

## Current parity

**1.2.92 publishes the current installer checkpoint with 92 distinct verified corrections from the October 1 implementation pass.** The patch number follows the [correction ledger](docs/parity/2026-10-01-parity-implementation-ledger.md); it is not a claim that all parity work is complete. The release includes the earlier 1.2.0 feature batch and the OSD, detail-loading and notification-reconnect playback repairs tested locally.

The [14-package plan](docs/parity/2026-10-01-parity-implementation-plan.md) remains the objective. All packages still have final visual/end-to-end acceptance open. The table summarizes released implementation and specific remaining work, rather than treating implementation as completed parity.

| Area | Included in 1.2.92 | Remaining acceptance / known gaps |
|---|---|---|
| Login, profiles and navigation | Required password change, recovery, native OAuth, pairing, profile/PIN controls, branded navigation and Back | Provider routing, pairing cancellation/stale approval, session restoration, final layout/focus/DPI comparisons |
| Home, library, search and personal lists | Current sections/cards, scoped library state, typed filters, paging, quick-search keyboard behavior, external Watchlist, notifications and calendar | Latest rating/capability/paging checks and final rendered page/menu comparisons |
| Requests | Current hub/discovery/Yours, retained poster hover, corrected Back, season choices, request/watchlist actions and download polling | Final hub/detail/season visual comparisons and combined workflow sign-off |
| Media details, audiobooks and manga | Authoritative series Play targets, theme audio, logos/ratings, repaired first detail navigation/single-season loading, audio controls and manga resume | Populated long-series layout, latest logo/rating pairs and remaining responsive page comparisons |
| Video controls and native playback | Repaired episode-transition OSD, shared playback preferences, native direct/remux/HLS, session progress and reconnect recovery | Sustained playback confirmation; fullscreen/OSD/seek/resume and HDR/lossless audio on real hardware; historical buffering is not declared fully resolved |
| Ebook reader | Bundled EPUB/PDF renderers, CFI/page progress, bookmarks, annotations, settings and nested contents | Known reader toolbar/File button sizing mismatch; neighboring controls and final rendered comparisons |
| Collections and wizard | Typed grouped queries, templates/imports, retained error drafts, preview continuation, conditional writes and artwork retry baseline | Remaining wizard artwork actions and final gallery/editor/wizard comparisons |
| Settings and integrations | Device manifest/effective values, account sign-in identities, title-art scopes, Home editor/transfer, provider forms, subtitle appearance and theme settings | Complete title-art write/event/stale-authority checks, override/capability states and combined settings acceptance |
| Watch Party | Room permissions, sync/readiness, reconnect handling, staged selection, invitations, suggestions and member/activity surfaces | Latest responsive sizing/member views, paired visuals and real multi-client acceptance |
| Conditional dialogs | Metadata/person editing, matching/refresh, images, request seasons and file dialogs | Remaining field/permission/error-state and rendered dialog comparisons |
| Server administration | Opens the Silo WebUI for server management | Outside the user-only desktop scope; the newly identified admin Seek Previews dialog is tracked separately |

Release verification: **1,399 Release tests**, **15 checks against the actual published playback assembly**, and native first detail navigation/Back plus delayed single-season layout checks passed on the 1.2.92 rebuild. These checks cover the reproduced reconnect failure without certifying every page or every live playback condition. See the [release verification record](docs/releases/1.2.92.md).

The official public Silo Server reference last fetched for this checkpoint on October 3 is `478afa5257332df52f10650cd00b88596562d4c9`. Original October 1 visual acceptance is tied to `8e2e840474a085c6df6571a5a2850f7eb996810c`; the [account](docs/parity/2026-10-03-account-finalization.md), [browse](docs/parity/2026-10-03-browse-finalization.md) and [media](docs/parity/2026-10-03-media-finalization.md) reports distinguish later obligations and remaining evidence. These references do not establish the production server revision.

The [October 1 audit](docs/parity/2026-10-01-user-visual-functional-audit.md), [implementation ledger](docs/parity/2026-10-01-parity-implementation-ledger.md), and [September 30 queue record](docs/parity/2026-09-30-queue-progress.md) retain detailed scope and verification. Playback stability is the immediate priority while the reconnect repair is being tested.

## Latest release

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

