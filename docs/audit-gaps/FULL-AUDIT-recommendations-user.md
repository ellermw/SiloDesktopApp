# Recommendations Page (User-facing) — Full Audit (Deep Re-audit)

Webui: Recommendations.tsx (190 lines) | Desktop: RecommendationsPage.xaml + .xaml.cs

## Status: Good parity — styling differences

### Already matching
- Taste profile card (genres, directors)
- Discover carousel rows using MediaCarousel/SectionRow
- Empty state (Sparkles icon + "Not enough data yet")

### Gaps

| # | Sev | Gap | Webui | Desktop | Effort |
|---|-----|-----|-------|---------|--------|
| 1 | P2 | Taste profile styling | `glass-subtle` card with signal_counts total ("X signals"), genres as accent-bg pills, directors as border-only pills | May use different card/badge styling | small |
| 2 | P2 | Error retry button | RefreshCw icon + "Retry" link | Error text only | small |
| 3 | P2 | Skeleton loading | 4 carousel skeletons with 12 poster skeletons each | ProgressRing | small |
| 4 | P2 | Title sizing | `text-2xl font-bold sm:text-3xl` responsive | Fixed size | small |
