# Watch / Player Page — Full Audit

Webui: VideoPlayer.tsx (1957) + PlayerControls.tsx (232) + QualityMenu/SubtitleMenu/AudioTrackMenu + hls.js
Desktop: PlayerService.cs (2278) + MpvPlayer.cs (994) + PlayerOverlay.xaml/cs + continuum-osc.lua (1000+)

## Status: Strong parity — architecture differs (libmpv vs HTML5) but features align

### Already matching
- Play/pause, seek, volume, mute controls
- Quality switching (auto/original/480p/720p/1080p tiers)
- Audio track selection menu
- Subtitle track selection with embedded/external/downloaded sources
- Skip intro button (appears in intro range)
- Skip credits button (desktop has, webui implicit via next-episode countdown)
- Chapters menu (conditional on version data)
- Auto-hide controls (3s timer)
- Buffering indicator (debounced 500ms)
- Loading overlay (preparing playback)
- Error overlay with close/retry
- Stats/debug overlay (resolution, codec, bitrate, HDR, session)
- Keyboard shortcuts (Space/K, F, M, C, arrows for seek/volume)
- Volume + mute persistence
- Fullscreen toggle
- Pause indicator (center play icon)
- 15+ keyboard shortcuts (desktop exceeds webui's 9)

### Gaps

| # | Sev | Gap | Effort |
|---|-----|-----|--------|
| 1 | P1 | Watch Together sync | Phase 4 planned, not implemented | large |
| 2 | P2 | Picture-in-Picture | Webui uses HTML5 PiP API | medium |
| 3 | P2 | Postroll mini-player | Webui has resizable mini-player at top-left | medium |
| 4 | P2 | Subtitle search online in player | Webui has SubtitleSearchModal in player | small |
| 5 | P2 | Playback info frame rate + buffer level | Webui shows more detailed stats | small |
| 6 | P2 | ASS/SSA subtitle rendering | Webui uses JASSUB, desktop uses mpv decoder | n/a (mpv handles natively) |

### Notes
- Desktop EXCEEDS webui in: keyboard shortcuts (15+ vs 9), skip credits button, chapter navigation (< / >)
- ASS rendering gap is n/a: mpv natively renders ASS/SSA subtitles better than JASSUB
- Watch Together is the only P1 gap, planned for Phase 4
