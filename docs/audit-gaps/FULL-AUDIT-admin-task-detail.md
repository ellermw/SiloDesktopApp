# Admin Task Detail Page — Full Audit (Deep Re-audit)
Webui: AdminTaskDetail.tsx (639 lines) | Desktop: AdminTaskDetailPage.xaml/.xaml.cs
## Status: Moderate parity — significant missing features

### Already matching
- Back navigation with breadcrumb (Admin > Scheduled Tasks > {name})
- Task name with category badge + description
- Run/Cancel action button
- Progress bar + message while running (including "Cancelling..." state)
- Trigger list display (read-only, with describeTrigger formatting)
- Execution history table (Started/Duration/Status/Error)
- Auto-refresh while running

### Gaps
| # | Sev | Gap | Webui detail | Effort |
|---|-----|-----|-------------|--------|
| 1 | P0 | RefreshMetricsPanel | 5 metric cards (Queue/Due Now/Leased/Oldest Due/Oldest Lease) + reason breakdown badges (episode_incomplete, stale_provider_id, etc.) + attempt buckets grid (5 columns) + recent errors list (title/type/attempts/last error) + due samples table (Item/Next refresh/Attempts/Last attempt). Only shown for refresh_metadata task. ~120 lines of UI. | large |
| 2 | P1 | Full trigger editor | TriggerFormRow with: type selector (Interval/Daily/Weekly/Startup), interval value+unit conversion (seconds/minutes/hours), day-of-week picker, time-of-day input, max runtime input. Add/remove triggers. Save/Cancel buttons. ~140 lines. | large |
| 3 | P1 | Expandable history rows | Each execution row expands on click to show result_data as formatted JSON. Chevron indicator, cursor:pointer, bg highlight on failed rows. | medium |
| 4 | P2 | scan_libraries notice | Special info panel explaining scan work continues in background, with link to Admin Libraries | small |
| 5 | P2 | Cancelling state styling | Progress bar turns yellow (bg-yellow-500) during cancelling | small |
