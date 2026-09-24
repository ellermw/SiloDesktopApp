# Silo Desktop Player

Native Windows desktop client for [Silo Server](https://github.com/Silo-Server/silo-server), built with WinUI 3 and libmpv. The project aims to reproduce the Silo WebUI visually and functionally while providing broad native direct-play support for high-bitrate HEVC, HDR, Dolby Vision, lossless audio, and other formats browsers commonly transcode.

> **Project status:** active pre-release development. The application is usable, but WebUI parity and playback hardening are not yet complete. Silo Server is also evolving toward 1.0, so parity is continuously re-audited against its current `main` branch.

## Download

[**Download Silo Desktop Player 1.1.104**](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.1.104/SiloInstaller-Windows-x64.exe) — Windows 10/11 x64 installer from GitHub Releases.

SHA-256: `0E54B19C01707B0C4C864752E7667C87FFCB22F6204B3A9079E64B9E0EA3D6AB`

The installer is intentionally unsigned. Runtime Code Integrity testing found that the former self-signed local certificate caused Smart App Control to block files that launch successfully when unsigned. Windows may still display an unknown-publisher warning on other machines. The installer includes the .NET runtime, Windows App Runtime bootstrapper, and validated native libmpv runtime.

## License / private use

This repository and its contents are proprietary and private. All rights are reserved by the project owner. No license is granted to copy, distribute, publish, sublicense, sell, host, modify, or use this software, source code, installers, artwork, branding, documentation, or related assets except with explicit written permission from the owner.

Third-party components retain their own licenses. The externally loaded FSRCNNX shader is LGPL-3.0-or-later; its source, notices, and license texts are included under [libs/mpv/shaders](libs/mpv/shaders/README.md).

## Current parity

**1.1.104 implements the main user workflows and the first six finalization packages, but complete visual and behavioral parity has not been verified.** The remaining work includes specific feature gaps, playback reliability investigation, and installed-app comparisons; those are different kinds of work.

The table describes the released build. **Known gap** means a concrete mismatch or reported defect. **Verification outstanding** means the implementation exists but current runtime evidence is insufficient; it does not mean the feature is absent. No area below is certified complete.

| Area | Implemented in 1.1.104 | Known gaps / verification outstanding |
|---|---|---|
| Login, profiles and navigation | Login/signup, token refresh, PINs, profile management, device login, branding, sidebar/drawer, Back navigation and playback return | **Verify:** installed layouts, profile/permission changes, keyboard/controller focus, narrow windows and DPI scaling. |
| Home, library, search and personal lists | Home sections, hero, continue watching/next up, virtualized libraries, filters/sorting, search, favorites, watchlist, history, recommendations, requests and calendar | **Verify:** side-by-side cards/spacing, loading/empty/error states, refresh and cached navigation, large-library scrolling. Recheck older Home/scroll reports against the released build instead of assuming either recurrence or resolution. |
| Movie, series, episode and person details | Versions, seasons/episodes, cast, media information, trailers/extras, watched/favorite/watchlist/rating actions, person metadata refresh and permission-gated maintenance | **Fixed in this release:** stale watched button after playback, verified with native UI regression checks. **Verify:** completion updates across cards and Home, plus installed layouts and permission states. |
| Video controls and playback settings | Native OSC, track/quality/subtitle menus, markers, chapters, credits/next episode, PiP, shortcuts and failure/retry controls | **Known upstream gap:** profile-wide video/audiobook seek intervals added after the release reference are not integrated; video skips remain fixed and audiobook intervals remain local. **Verify:** current control geometry, focus, autoplay/close and subtitle-provider behavior. |
| Native playback reliability | Direct/remux/HLS, codec/HDR support, live tracks, progress/session keepalive, protocol-v3 recovery, native direct reader and diagnostics | **Open investigation:** recurring buffering is not proven fully resolved. **Verify:** sustained high-bitrate 4K, seek/resume and recovery, HDR/Dolby Vision, TrueHD/Atmos/DTS on real hardware. Intel/AMD upscaling validation remains incomplete. |
| Audiobooks, ebooks and manga | Grouped browsing, literary details, mini/expanded audiobook player, reader navigation and progress | **Verify:** supported document formats with real files, resume/next chapter, audiobook speed/sleep timer, layout and input behavior. Shared seek settings are the confirmed gap noted above. |
| Collections | Manual/smart collections, editor, imports, grouping/order and API v2 conditional updates | **Verify:** current templates/guided rules, preservation of complex rules through edit/preview/save, collage/scheduling presentation and current WebUI layouts. Older checklist items need rechecking before being called missing. |
| Settings, notifications, downloads and integrations | Playback/subtitles, appearance/theme/accessibility, Home/cards/libraries, profiles, Account password settings, notifications, safer download saves, corrected history import state/progress and watch-provider/webhook settings; API v2 integration | **Verify:** live password changes, imports and provider operations, effective profile values and permission/error states. API v2 migration itself is already shipped. |
| Watch Party | Create/join, membership, suggestions, sync, host/guest controls, invitations and player overlay | **Verify:** multi-client playback, disconnect/reconnect, buffering/catch-up and quality-change behavior against the newer upstream policy. |
| Server administration | Opens the Silo WebUI for server setup/management | **Outside desktop scope:** dashboards, users, scanning, nodes, policies, tasks and other server administration were intentionally removed in 1.1.94. They are not unfinished desktop features. |

Evidence: **1.1.104 passed 1,137 Release tests, native WinUI regression checks and six published playback-service checks**. Native checks cover calendar navigation, Account and import fields, subtitle-provider states, watched-button updates and artwork reattachment. These checks do not establish every-page visual parity or sustained playback reliability. The finalization reference is official Silo `main` at `d4e35ba9df416e747822c6c9f2193b89c6b7e9fb`, fetched before implementation.

See [the current remaining-work and evidence record](docs/parity/2026-09-24-status.md) for priorities, exact references and verification criteria. Historical audit counts include removed administration and superseded findings, so they are not a current completion measure.

The [first finalization audit and work-package list](docs/parity/2026-09-24-finalization-audit.md) records 20 specific findings against the fetched reference, including two reproduced defects. The [first six selected packages](docs/parity/2026-09-24-first-six-implementation.md) ship in 1.1.104; that record distinguishes automated/native verification from live installed acceptance. The five remaining packages are smart-collection rule preservation, playback preferences, search, reader progress compatibility and Watch Party recovery. Recurring buffering remains a separate open investigation.

## Latest release

### 1.1.104

- Refresh the watched action after playback completes and final progress saves, including return navigation and episode transitions.
- Keep calendar week navigation visible while scrolling and wrap long Playback Info values without overlapping labels.
- Respect subtitle-provider availability while retaining subtitle upload, and refresh person details as background metadata work completes.
- Preserve download filenames and formats, stage transfers until complete, and show save/delete failures.
- Correct history import progress and clear consumed Connect authorization after successful submission.
- Add capability-aware Account password settings with validation and safe clearing of password fields.
- Verified **1,137 Release tests**, native WinUI regression checks, **six published playback-service checks**, and clean installer packaging with native libmpv load/hash checks. Live account/import/provider acceptance and the remaining parity packages are still outstanding.

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
