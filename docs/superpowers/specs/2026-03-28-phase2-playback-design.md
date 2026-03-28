# Phase 2: Playback -- Design Specification

## Overview

Add media playback to the Continuum Desktop Player using libmpv. The player handles local decoding of all supported codecs (HEVC, AV1, H.264, VP9, DTS-HD MA, TrueHD, FLAC, EAC3, AC3, AAC, etc.) with HDR/Dolby Vision passthrough. All transcoding/remuxing decisions are made server-side.

## Architecture

```
ItemDetailPage (Play button) or EpisodeRow (Play button)
    |
    v
PlaybackManager.StartPlaybackAsync(contentId, fileId?)
    |-- GET /api/v1/watch/{contentId}  → get versions, subtitles, user progress
    |-- User selects version (or auto-select best match)
    |-- POST /api/v1/playback/start    → declare codec capabilities, get session
    |
    v
PlayerPage (fullscreen or fills app window)
    |-- MpvPlayer loads stream URL
    |-- Subtitle tracks loaded from server URLs
    |-- Progress reported every 5-10s
    |-- WebSocket connected for admin commands
    |-- Skip Intro/Credits buttons shown at marker times
    |
    v
On exit: DELETE /api/v1/playback/{sessionId}
```

## Components

### 1. MpvPlayer (libmpv wrapper)
- P/Invoke wrapper around `libmpv-2.dll`
- Initializes mpv with hardware acceleration (D3D11VA)
- Renders video into a WinUI native window handle
- Exposes: Play, Pause, Seek, SetVolume, LoadSubtitle, SetAudioTrack
- Events: PositionChanged, DurationChanged, PlaybackEnded, Error

### 2. PlaybackManager (orchestrator)
- Calls watch detail API, starts playback session
- Manages progress reporting timer (every 7 seconds)
- Handles audio track switching (PATCH API → reload stream)
- Manages WebSocket for admin commands
- Stops session on exit

### 3. PlayerPage (UI)
- Full window or true fullscreen mode
- Overlay controls: play/pause, seek bar, volume, subtitle picker, audio track picker
- Skip Intro / Skip Credits buttons at marker times
- Version/quality selector
- Escape to exit

### 4. Codec Capabilities Declaration
Send to server on playback start:
```json
{
    "codecs_video": ["h264", "hevc", "av1", "vp9"],
    "codecs_audio": ["aac", "flac", "opus", "eac3", "ac3", "dts", "truehd"],
    "containers": ["mp4", "mkv"],
    "hdr": true,
    "max_resolution": "2160p"
}
```

### 5. Playback Flow
1. User hits Play → PlaybackManager gets watch detail
2. Auto-select best version or show version picker
3. POST /playback/start with codec capabilities
4. Server returns: session_id, play_method, stream_url, subtitle_urls[]
5. Based on play_method:
   - **direct**: feed stream_url to mpv (HTTP byte-range)
   - **remux**: same URL, mpv handles it
   - **transcode**: POST /playback/transcode/start, feed HLS manifest_url to mpv
6. Load subtitle tracks from subtitle_urls[]
7. Start progress reporting timer
8. Connect WebSocket for admin commands

### 6. Subtitle Handling
- Text subs (SRT/ASS/VTT): loaded as external tracks in mpv from server WebVTT URLs
- Bitmap subs (PGS/VOBSUB): mpv renders from embedded MKV streams during direct play
- If server returns 400 for bitmap sub fetch: fall back to burn-in via transcode

### 7. Player Controls
- Play/Pause (Space key)
- Seek bar with time display
- Volume slider + mute toggle
- Subtitle track selector
- Audio track selector
- Version/quality selector
- Fullscreen toggle (F key)
- Skip Intro button (visible during intro marker range)
- Skip Credits button (visible during credits marker range)
- Escape to exit player

## Key Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Video rendering | Native window handle via P/Invoke | Only reliable way to embed mpv in WinUI 3 |
| libmpv binding | Direct P/Invoke (not Mpv.NET) | More control, fewer dependencies |
| Subtitle rendering | mpv native | Handles SRT, ASS, VTT without transcoding |
| Progress interval | 7 seconds | Within 5-10s requirement, sessions reaped at 45s |
| HW acceleration | D3D11VA | Best Windows support for HEVC/AV1/HDR |
