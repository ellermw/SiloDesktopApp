# Home Page — Full Audit

Webui: Home.tsx (342) + HeroBanner.tsx (327) + SectionRow.tsx (106) + MediaCarousel.tsx (126) + ContinueWatchingCard.tsx (230) + SectionItemCard.tsx (115) + design-system.ts (new) + app.css (1585)
Desktop: HomePage.xaml (155) + .xaml.cs (219) + HeroCarousel.xaml/cs (405) + SectionRow.xaml/cs + PosterCard.xaml/cs + LandscapeCard.xaml/cs + BackdropImage.xaml/cs + HomeViewModel.cs

## Status: Strong parity — minor gaps only

### Already matching
- Section row layout: 48px side padding, 40px spacing between sections, 16px card gap
- Poster card dimensions: 178px×267px (2:3 aspect), CornerRadius=12, webui range 140-185px
- Landscape card dimensions: 280px×158px (16:9), webui range 260-315px
- Hero carousel: 72% viewport height, 8s auto-advance, crossfade (800ms), Ken Burns
- Hero metadata pills: year, IMDb rating (with star), genres (up to 3)
- Hover-reveal arrows: opacity 0→1 on pointer enter (180ms / 140ms)
- Edge fade gradients: 64px alpha-only fade at left/right scroll edges
- Explore all button: pill-style, visible on hover for library-backed sections
- Skeleton loading: 7 placeholder cards while section items load
- Two-phase loading: layout first (skeletons), then items in batches (max 5 concurrent)
- Undo dismissal banner: bottom-center overlay with Undo button
- Watch Tonight button: glass-style, right-aligned above sections
- Empty home state: adaptive text based on library access
- Poster card hover: scale + dim + play button + accent border
- Poster card overlays: corner badges (resolution, audio, year, etc.) from CardOverlayService
- Image loading: semaphore-limited (4), background thread, 250ms fade-in

### Fixed in this audit
1. **Hero title size**: 36→48px (webui uses clamp 36-72px)
2. **Hero eyebrow**: Added "FEATURED — No. 01" above title
3. **Two CTA buttons**: "Play" (accent pill) + "More Info" (glass pill) replacing single "Open details"
4. **Slide counter**: "01 / 04" monospace at bottom-right replacing center dots
5. **Progress rail**: Animated 8s width bar below counter showing auto-advance progress
6. **Pill button styling**: CornerRadius=20 (fully rounded)

### Remaining gaps (P2 polish)

| # | Gap | Webui | Desktop | Effort |
|---|-----|-------|---------|--------|
| 1 | Ambient glow | Radial gradient from backdrop's dominant color | Not implemented | large |
| 2 | Glass surfaces | `.glass` / `.glass-subtle` backdrop-filter blur | SurfaceBrush (no blur) | medium |
| 3 | Media card hover | translateY(-4px) + brightness(1.1) | scale(1.04) + dim overlay | small (style choice) |
| 4 | Hero pause/play toggle | Explicit pause button UI | Pauses on hover (no button) | small |
| 5 | 5 color themes | midnight-cinema, cobalt, oxblood, evergreen, cinema-light | Single dark theme | large |
| 6 | Ken Burns dual pattern | Two alternating 14s/18s patterns | Single 30s pattern | small |
| 7 | Responsive card sizing | 140/160/185px responsive breakpoints | Fixed 178px | small |

All remaining gaps are design-system-level changes (ambient glow, glass, themes) that would affect the entire app, not just the home page. The home page structure, layout, and interactions are at full parity.
