# Continuum Desktop Player

Native Windows 11 media player for Continuum media servers. Built for direct playback of HEVC/HDR content without server-side transcoding.

## Download

[**Download Installer (v1.1.3)**](https://transfers.taverncdn.com/5N9wE04pXr/ContinuumDesktopPlayer-1.1.3-Setup.exe) — Single-file setup, includes all dependencies. Windows 10/11 x64.

## Tech Stack

- **Framework:** WinUI 3 / .NET 8 / Windows App SDK
- **Player Engine:** libmpv with vo=gpu, D3D11 hardware rendering
- **Hardware Decode:** D3D11VA (full GPU pipeline, zero CPU frame copies)
- **Architecture:** MVVM with CommunityToolkit.Mvvm + dependency injection

## Playback

- **Direct play** of MKV/MP4 containers with HEVC, H.264, AV1, VP9
- **Audio passthrough** for DTS-HD MA, TrueHD, FLAC, AAC, EAC3, Opus
- **HDR10 support** with display colorspace hints
- **4K remux** with 800MB demuxer buffer and 16MB stream buffer
- **Subtitle rendering** (SRT, ASS, WebVTT) with external subtitle search/download
- **Fallback:** HLS transcode when direct play isn't possible (fMP4, 2-second segments)

## Features

- Server authentication with JWT token auto-refresh (never re-login)
- Multi-profile support with PIN protection
- Home screen with continue watching, recommendations, and curated sections
- Library browsing with genre/studio/rating filters and sort options
- Full-text search matching Continuum server search
- Custom on-screen controls (Lua OSC) with play/pause, seek, volume, fullscreen, minimize
- Video stats overlay (4-section layout matching Continuum web player)
- Subtitle selection menu with language names, source badges, and online search
- Mini-bar with live video thumbnail and transport controls
- Resume playback from last position
- Full admin panel (dashboard, users, libraries, tasks, logs, settings, collections, sections — admin role only)
- Watch progress tracking with server sync
- Favorites, watchlist, and rating management
- Win32 fullscreen with topmost flash z-ordering

## Project Structure

```
src/
  ContinuumPlayer/          WinUI 3 app (views, viewmodels, controls)
  ContinuumPlayer.Core/     Shared library (API clients, services, models)
  ContinuumPlayer.Player/   libmpv P/Invoke wrapper
```

## Build

Requires Visual Studio 2022 with the Windows App SDK workload, or .NET 8 SDK with WinUI tooling.

```
dotnet build ContinuumPlayer.sln
```

## Requirements

- Windows 10 1809+ / Windows 11
- `libmpv-2.dll` in the output directory (copied from `libs/mpv/`)

---

*This has been created with 100% vibe coding.*
