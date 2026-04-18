# Search Page — Full Audit

Webui: Catalog.tsx (search mode via ?source=search)
Desktop: SearchPage.xaml + SearchPage.xaml.cs + SearchViewModel.cs

## Status: At parity (desktop has extras)

### Already matching
- Empty state: centered icon + title + subtitle + prominent search bar
- Results grid with poster cards (shared PosterCard component)
- Result count display
- Clear button visible when text present
- Loading/error states
- Debounced search input (400ms)
- Infinite scroll for results

### Desktop extras (not in webui)
- People results section: shows matching actors/directors with photo cards
- Dual search boxes: one in empty state, one in results view

### Gaps: None actionable
Search page is at full functional parity. Desktop actually exceeds webui with people results.
