# Watch Together Page — Full Audit (Deep Re-audit)

Webui: WatchTogetherRoomPage.tsx (887 lines) + WatchTogetherJoin.tsx (260) + WatchTogetherSuggestionPanel.tsx
Desktop: WatchTogetherRoomPage.xaml + .xaml.cs + WatchTogetherViewModels.cs

## Status: Partially implemented — room management exists, content search + playback sync gaps

### Already matching
- Room creation and joining (WatchTogetherJoinPage)
- Room member list with status indicators
- Basic suggestion list with vote/delete
- Invite link sharing (clipboard copy)
- Room settings (host controls)
- WebSocket room connection

### Webui features (887 lines, 6 components):
1. **SearchPosterCard** — poster grid for content search results inside the room (poster image, title, year, type badge)
2. **CandidateSpotlight** — full confirmation card: backdrop image, poster, title, metadata (year/rating/duration), overview, "Play Now" + "Back to search" buttons
3. **NowPlayingHero** — shows active playback: backdrop + title + metadata, room state display
4. **Multi-step drill-down** — results → select series → select season → select episode (3-stage flow)
5. **Auto-start playback** — when room has active selection and revision changes, auto-starts via playback controller
6. **StatusDot** — colored dot per room state (green=playing, amber=waiting, gray=idle)

### Gaps

| # | Sev | Gap | Webui | Desktop | Effort |
|---|-----|-----|-------|---------|--------|
| 1 | P1 | Content search with poster grid | SearchPosterCard grid with 200ms debounced query, 12-item pages. Movies shown directly, series triggers drill-down | Not implemented (code comments: "coming in later pass") | large |
| 2 | P1 | Series drill-down flow | 3-stage: search results → season grid → episode grid. Each stage has back nav | Not implemented | large |
| 3 | P1 | CandidateSpotlight confirmation | Full card: backdrop, poster, metadata (year/rating/genres/runtime), overview, Play Now button | Not implemented | medium |
| 4 | P1 | Auto-start playback integration | Auto-starts via useWatchPlaybackController when room selection changes (lastAutoStartRevisionRef prevents re-trigger) | Stubbed/TODO in code-behind | medium |
| 5 | P1 | NowPlayingHero | Shows active playback backdrop + title + metadata when room is playing | Not implemented | medium |
| 6 | P2 | StatusDot colors | Green (playing), amber (waiting), gray (idle) | May differ | small |
