# User Collections Pages — Full Audit (Deep Re-audit)
Webui: Collections.tsx (151) + CollectionEditor.tsx (68) + userCollectionsShared.tsx | Desktop: CollectionsPage + CollectionBrowsePage + CollectionEditorPage
## Status: Good parity — layout paradigm differs

### Already matching
- Collection list with type badges (Smart/Manual) and shared indicator
- Browse view with poster grid
- Editor with Manual/Smart toggle, rule builder, manual item search
- New Collection button, delete confirmation dialog
- Empty state with icon + description + create button

### Gaps
| # | Sev | Gap | Webui | Desktop | Effort |
|---|-----|-----|-------|---------|--------|
| 1 | P1 | Card layout paradigm | Webui uses Card components in 1/2/3-col grid with name + badges (no poster images). surface-panel with hover:-translate-y-1 | Desktop uses poster-style grid (5-per-row with poster images) | medium |
| 2 | P2 | Hover edit/delete buttons | Webui ghost icon buttons appear on card hover (Pencil + Trash) | Desktop uses right-click context menu | style choice |
| 3 | P2 | Skeleton loading | Webui shows 6 skeleton cards (h-24 rounded-[1.6rem]) | Desktop uses ProgressRing | small |
| 4 | P2 | Card corner radius | Webui: rounded-[1.6rem] (25.6px). Desktop may differ | small |
