# Silo Desktop Player

Native Windows desktop client for [Silo Server](https://github.com/Silo-Server/silo-server), built with WinUI 3 and libmpv. The project aims to reproduce the Silo WebUI visually and functionally while providing broad native direct-play support for high-bitrate HEVC, HDR, Dolby Vision, lossless audio, and other formats browsers commonly transcode.

> **Project status:** active pre-release development. The application is usable, but WebUI parity and playback hardening are not yet complete. Silo Server is also evolving toward 1.0, so parity is continuously re-audited against its current `main` branch.

## Download

[**Download Silo Desktop Player 1.1.102**](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.1.102/SiloInstaller-Windows-x64.exe) — Windows 10/11 x64 installer from GitHub Releases.

SHA-256: `CEC056DED1B56219A319C01C38B80BD2CCDA9A25541AB6C560BF4DC97B9BCEBC`

The installer is intentionally unsigned. Runtime Code Integrity testing found that the former self-signed local certificate caused Smart App Control to block files that launch successfully when unsigned. Windows may still display an unknown-publisher warning on other machines. The installer includes the .NET runtime, Windows App Runtime bootstrapper, and validated native libmpv runtime.

## License / private use

This repository and its contents are proprietary and private. All rights are reserved by the project owner. No license is granted to copy, distribute, publish, sublicense, sell, host, modify, or use this software, source code, installers, artwork, branding, documentation, or related assets except with explicit written permission from the owner.

Third-party components retain their own licenses. The externally loaded FSRCNNX shader is LGPL-3.0-or-later; its source, notices, and license texts are included under [libs/mpv/shaders](libs/mpv/shaders/README.md).

## Current parity

Status meanings:

- **Functional; visual parity incomplete** — principal data and actions work, but the page is not considered complete until side-by-side visual and interaction verification matches the current WebUI.
- **Substantial** — the principal workflows exist, with additional visual or edge-case work remaining.
- **Partial** — usable foundation exists, but meaningful WebUI behavior is still missing.
- **Hardening** — implemented, with compatibility, performance, or reliability work still underway.

| Area | Status | Current state |
|---|---|---|
| Authentication and profiles | Substantial | Login, refresh tokens, profile selection, PINs, profile management, signup, public server branding, and QR/device login are implemented. A server that still requires initial setup directs the user to finish setup in the Silo WebUI. Final installed visual comparison remains. |
| Shared shell and navigation | Substantial | The 260 px desktop sidebar, 64 px detail immersion rail with 150 ms hover expansion, current mobile header/drawer, active-route synchronization, dynamic libraries/pins/apps, profile avatar/fallback behavior, notification chrome, Alt+Left/mouse/controller Back, cached route transitions, and playback-to-shell restoration are implemented. Installed comparison and the Playing Next close regression still require QA confirmation. |
| Catalog and library browsing | Substantial | Virtualized browsing, filters, sorting, focused/cancelable search, collections, favorites, watchlist, history, calendar, recommendations, and requests are implemented. Shared cards now include current action, progress, episode-state, input, and personal-source presentation behavior. Installed comparison and the post-`8164fd59` WebUI drift pass remain. |
| Home screen | Substantial | Server-defined sections, hero content, continue watching, next up, recommendations, customization, incremental refresh, current card variants, and audiobook rows are implemented. Mounted sections reconcile in place during refresh instead of blanking loaded content. Installed-build visual comparison and remaining responsive edge cases are still required. |
| Item and person details | Substantial | Movie, series, season, episode, and person surfaces include cast/crew, versions, watched/favorite/watchlist/rating, trailers, extras, edition selection, current multi-version Media Info spec sheets, prefetched detail navigation, and current More actions. Profiles with metadata-curation permission retain Match/Fix Match for movies and series plus Quick/Complete Refresh Metadata. Navigation-aware mutations prevent an earlier item from repainting a reused detail page. Installed comparison and the current WebUI drift pass remain. |
| Audiobooks, ebooks, and manga | Substantial | Current server contracts, grouped browsing, literary detail surfaces, manga volume/chapter reading through the shared reader, next-chapter navigation, read progress, and the WebUI-style audiobook mini/expanded player are present. The reader recognizes the current EPUB/PDF/MOBI/AZW/AZW3/CBZ/CBR/FB2/FBZ contract; remaining format-specific compatibility and final installed visual validation are still required. |
| User collections | Substantial | Browse, create, edit, manual/smart rules, imports, and collection management exist. Current templates, guided rules, collage/scheduling details, and visual polish remain. |
| Notifications | Substantial | Notification center and user notification settings are implemented, including current delivery configuration foundations. Additional current-server edge cases remain to be audited. |
| Settings | Substantial | Playback, subtitles, appearance, theme editor, accessibility, date/time formats, home, card overlays, libraries, history import, webhook sync, watch providers, profiles, and notification controls are present. The server continues to add fields, so these routes remain subject to drift audits. |
| Server administration | WebUI only | The desktop application is intentionally user-facing. Server setup, dashboards, libraries/scanning, users, activity/logs, nodes, plugins, policies, tasks, request moderation, and all other server-management workflows belong to the Silo WebUI and are not shipped in the Windows client. |
| Player on-screen controls | Substantial | Current cinema controls, recap/intro/credits actions, marker/chapter seek regions, real chapter thumbnails, rich audio/subtitle/quality menus, live AI subtitle translation, bounded playback-info sections, credits countdown, post-roll/finished screens, On Deck, PiP, keyboard shortcuts, post-autoplay OSC restoration, fullscreen reconciliation, and an in-player Retry/Exit failure surface are implemented. Installed real-playback comparison and remaining edge-case geometry/focus validation are still required. |
| Native playback engine | Hardening | Direct play, remux, HLS fallback, D3D11VA, HEVC/AV1/VP9/H.264, HDR paths, subtitle rendering, live track switching, progress/session keepalive, protocol-v3 route recovery, realtime plan invalidation, replacement sessions after keepalive loss, byte-range stall recovery, upstream-idle recovery, and premature-EOF guards are implemented. High-bitrate 4K, Dolby Vision, HDR/tone mapping, TrueHD/Atmos/DTS passthrough, fastest startup/seek, and long-session reliability remain active real-media validation work. |
| Watch Party | Substantial | Create/join, room membership, suggestions, realtime synchronization, host/guest policy, transport controls, connection state, invite copy, end-room confirmation, and player sync overlay are implemented. Installed multi-client testing and remaining edge cases are still required. |

## Latest release

### 1.1.102

- Updated authentication, browsing, settings, collections, notifications, downloads, and playback for the current Silo API v2 contracts, retaining native direct play and playback protocol v3.
- Added a native direct-stream reader, bounded stall recovery, and detailed playback diagnostics that redact credentials and signed URLs. Long-session buffering remains under investigation.
- Fixed clipped year and media-type labels beneath search results and other shared poster grids.
- Added experimental Intel VSR and portable FSRCNNX upscaling alongside NVIDIA RTX VSR, with fallback to normal rendering.
- Corrected audiobook progress units and playback marker ranges, and retained the recent library scrolling and playback-quality improvements.
- Removed the test-build version suffix. This release is **1.1.102**; complete WebUI visual parity and broader GPU validation remain ongoing.
- Verified **1,045 Release tests**, **six published playback-service checks**, and clean installer packaging with native libmpv load/hash checks.

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
