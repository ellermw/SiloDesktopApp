# Library / Catalog Page — Full Audit

Webui: LibraryPage.tsx (152) + LibraryHeader.tsx (72) + LibraryRecommended.tsx + LibraryBrowse.tsx + LibraryCollections.tsx + CatalogFiltersPanel + libraryPageSearchParams.ts
Desktop: LibraryPage.xaml (471) + LibraryPage.xaml.cs (~800) + LibraryViewModel.cs (~350)

## Status: Good structural parity — cosmetic/interaction gaps

### Already matching
- Three-tab layout: Recommended / Library / Collections
- Pill tab bar with active/inactive styling
- Recommended tab: hero carousel + section rows (reuses same SectionRow/HeroCarousel as home)
- Library tab: poster grid with UniformGridLayout virtualizer
- Collections tab: poster grid for collection cards + empty state
- Filter bar: media type, genre, content rating, year range, studio, country, resolution, audio language
- Sort controls: title/recently added/year/IMDb rating + ascending/descending toggle
- Active filter badges with clear-all button
- Pill-shaped ComboBox filter controls (CornerRadius=16)
- Item count panel (AVAILABLE NOW + count)
- Infinite scroll pagination (ViewChanged at threshold)
- Per-library view state persistence (in-memory dict)
- Explore all button on section rows
- Year filter with debounce

### Gaps

| # | Sev | Gap | Webui | Desktop | Effort |
|---|-----|-----|-------|---------|--------|
| 1 | P1 | Sticky overlay header | LibraryHeader: transparent over hero, glass on scroll (160px threshold) | Static header, no overlay/glass effect | large |
| 2 | P1 | Eyebrow breadcrumb | Library / {name} eyebrow above tabs | No eyebrow, just title + subtitle | small |
| 3 | P1 | Header in hero | Header overlays hero banner in Recommended tab | Header separate from hero (gap between) | medium |
| 4 | P2 | Loading skeleton | 21 poster skeletons in responsive grid | ProgressRing only | medium |
| 5 | P2 | Scroll-to-top button | Floating button at bottom-right | Not present | small |
| 6 | P2 | URL-based state | Filters/sort in URL search params | In-memory dictionary (not sharable) | medium |
| 7 | P2 | Collection card pin button | Pin/unpin to sidebar on hover | Not present | medium |
| 8 | P2 | Per-section retry | Each section row has error + retry button | Recommended tab all-or-nothing | small |

### Notes
- Desktop UniformGridLayout adapts column count via MinItemWidth — functionally equivalent to responsive breakpoints.
- Desktop shows all filters inline (no Filters button), so badge count is unnecessary.
- Sticky overlay header (gap 1) is the biggest visual difference but architecturally complex in WinUI.
