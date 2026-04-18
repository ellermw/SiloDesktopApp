# Item Detail Page — Full Audit

Webui: ItemDetail/ folder with MovieContent, SeriesContent, SeasonContent, EpisodeContent + ActionBar + VersionDropdown + CastCarousel + CrewList
Desktop: ItemDetailPage.xaml (~600) + ItemDetailPage.xaml.cs (~2800) + ItemDetailViewModel.cs (~500)

## Status: Strong functional parity — minor cosmetic gaps

### Already matching
- Full hero backdrop with Ken Burns animation + gradient overlays
- Poster image display (left side of hero)
- Title, tagline, overview text
- Metadata row: year, content rating, runtime, IMDb/TMDB scores
- Cast carousel: portrait cards (110x165, 2:3 aspect), horizontal scroll, photo fallback to initials
- Crew section: grouped by Director/Writer/Producer, comma-separated
- Version selector: split button dropdown, resolution/codec/HDR/audio summary
- Audio track selector with auto-resolution
- Subtitle selector with auto-resolution, search online, embedded/downloaded tracks
- Play/Resume button with progress overlay
- Play from Start in dropdown
- Favorites toggle (heart icon, color change)
- Watched toggle (checkmark + text)
- Watchlist in kebab menu
- Star rating widget (5-star, hover preview, toggle-off on re-click)
- Episode grid for series: single-season collapse, multi-season carousel
- Episode cards: still image, title, overview, quality badges, progress bar, watched checkmark
- Season cards: poster, title, progress text, watched overlay
- Similar items section (More Like This)
- Download button (movies/episodes with versions)
- Refresh metadata (admin, in More menu)
- Match item dialog (admin)
- Quality badges (resolution, HDR, audio format)
- Overlay badge system from CardOverlayService

### Gaps

| # | Sev | Gap | Webui | Desktop | Effort |
|---|-----|-----|-------|---------|--------|
| 1 | P1 | Ambient glow | Color extraction from backdrop, radial glow tinted by dominant color | Not implemented | large |
| 2 | P1 | Season detail page | Dedicated /item/{seasonId} view with own hero | Seasons inline in series detail only | large |
| 3 | P1 | Sibling episode carousel | Below episode detail, shows other episodes in same season | Not present | medium |
| 4 | P2 | Edit Metadata dialog | Admin inline form for editing metadata | Not implemented | medium |
| 5 | P2 | Crew line in hero | Directed by X, Written by Y above genres in hero | Crew below overview in separate section | small |
| 6 | P2 | Genres as styled badges | Clickable genre badges with pill styling | Plain comma-separated text | small |
| 7 | P2 | Media Locations | Admin read-only file paths display | Not present (paths shown in Match dialog only) | small |

### Notes
- Ambient glow (gap 1) is a design-system-level change that would affect the entire app theme.
- Season detail page (gap 2) requires a new page type + navigation route.
- Sibling episode carousel (gap 3) is the most impactful missing feature for episode browsing UX.
- Desktop has features webui doesn't: subtitle deletion UI, downloaded subtitles listing below overview.
