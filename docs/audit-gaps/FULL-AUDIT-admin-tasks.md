# Admin Tasks Page — Full Audit
Webui: AdminTasks.tsx | Desktop: AdminTasksPage.xaml/.xaml.cs
## Status: High parity
### Already matching
- Grouped task cards by category (Library/Metadata/System)
- Task rows: name, schedule description, last run, next run, status badge, run/cancel button
- Live progress bar while running
- Row click navigates to task detail
- Auto-refresh while tasks running
### Gaps
| # | Sev | Gap | Effort |
|---|-----|-----|--------|
| 1 | P1 | Refresh metadata metrics panel | Webui shows queue stats + reason breakdown badges | medium |
| 2 | P2 | Real-time event channel | Webui uses events; desktop uses 5s timer | small |
