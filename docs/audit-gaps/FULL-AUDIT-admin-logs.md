# Admin Logs Page — Full Audit
Webui: AdminLogs.tsx | Desktop: AdminLogsPage.xaml/.xaml.cs
## Status: Good parity (fixed in prior session)
### Already matching (after fixes)
- Two-tab layout: Application / Audit
- Live-filter inputs with 500ms debounce (Search buttons removed)
- Proportional column widths (table fills full width)
- App logs: Time/Level/Component/Status/Duration/Message
- Audit logs: Time/Method/Path/Status/Client/User/Session/Playback/Request
- Playback session filter bar + summary card
- FFmpeg filter toggle
- Row click detail panel (desktop: inline; webui: side sheet)
- Color-coded status/level values
### Gaps
| # | Sev | Gap | Effort |
|---|-----|-----|--------|
| 1 | P2 | Detail panel style | Webui uses side Sheet; desktop uses inline collapsible panel | medium |
| 2 | P2 | Server-side cursor pagination | Webui notes it; desktop loads full result set | medium |
