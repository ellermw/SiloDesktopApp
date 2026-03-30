# Continuum Desktop Player

Native Windows 11 media player for Continuum media servers. Built for direct playback of HEVC/HDR content without server-side transcoding.

## Download

[**Download Installer**](https://transfers.taverncdn.com/eLwlqbEyBz/ContinuumDesktopPlayer-Setup.exe) -- Single-file setup, includes all dependencies. Windows 10/11 x64.

## Tech Stack

- **Framework:** WinUI 3 / .NET 8 / Windows App SDK
- **Player Engine:** libmpv (software render API into WriteableBitmap)
- **Hardware Decode:** D3D11VA-copy (GPU decodes, copies to RAM for compositing)
- **Architecture:** MVVM with CommunityToolkit.Mvvm + dependency injection

## Playback

- **Direct play** of MKV/MP4 containers with HEVC, H.264, AV1, VP9
- **Audio passthrough** for DTS-HD MA, TrueHD, FLAC, AAC, EAC3, Opus
- **HDR10 support** with display colorspace hints
- **4K remux** with 800MB demuxer buffer and 16MB stream buffer
- **Subtitle rendering** (SRT, ASS, WebVTT) with external subtitle search/download
- **Fallback:** HLS transcode when direct play isn't possible (fMP4, 2-second segments)

## Features

- Server authentication with JWT token refresh
- Multi-profile support with PIN protection
- Home screen with continue watching, recommendations, and curated sections
- Library browsing with genre/studio/rating filters and sort options
- Full admin panel (dashboard, users, libraries, tasks, logs, settings, collections, sections)
- Watch progress tracking with server sync
- Favorites, watchlist, and rating management
- Keyboard shortcuts for player controls
- Win32 fullscreen (borderless, covers taskbar)

## Project Structure

```
src/
  ContinuumPlayer/          WinUI 3 app (views, viewmodels, controls)
  ContinuumPlayer.Core/     Shared library (API clients, services, models)
  ContinuumPlayer.Player/   libmpv P/Invoke wrapper
tests/
  ContinuumPlayer.Core.Tests/
```

## Build

Requires Visual Studio 2022 with the Windows App SDK workload, or .NET 8 SDK with WinUI tooling.

```
dotnet build ContinuumPlayer.sln
```

## Requirements

- Windows 10 1809+ / Windows 11
- `libmpv-2.dll` in the output directory (copied from `libs/mpv/`)
