# Admin History Import Page — Full Audit (Deep Re-audit)
Webui: AdminHistoryImport.tsx (1371 lines) | Desktop: AdminHistoryImportPage.xaml/.xaml.cs (173 lines)
## Status: Very low parity — desktop is a simplified read-only list. Webui is a full management system.

### Webui features (1371 lines, 6 major components):
1. **SourceDialog** (240 lines) — Create/edit source form: type selector (emby/jellyfin/plex), name, URL, enabled toggle, source-specific hints
2. **TokenDialog** (170 lines) — API token management: eye toggle visibility, clear/save flow, Plex OAuth login button (opens popup, polls for auth token)
3. **DiscoverDialog** (245 lines) — External user discovery: search users on remote server, multi-step wizard (select external user → select Continuum user → select profile → confirm mapping)
4. **SourceBar** (130 lines) — Source selector dropdown + token status indicator (badge: configured/not configured) + action buttons (Discover Users, Edit Source, Delete Source, Set Token)
5. **MappingsSection** (150 lines) — User mapping table: source user → Continuum user/profile with last-imported date, run/delete per-mapping, create mapping button
6. **RunsSection** (190 lines) — Import runs with status badges, expandable metric cards (fetched/matched/unmatched/updated/created/skipped), warning list, unmatched samples, cancel running, realtime event channel refresh

### Desktop has:
- Simple source list as read-only cards
- Add Source button
- Recent imports as text labels
- No token management, no discovery, no mappings, no run details

### Gaps
| # | Sev | Gap | Effort |
|---|-----|-----|--------|
| 1 | P0 | SourceDialog (create/edit) | Full form with type/name/URL/enabled. Desktop has basic "Add Source" only | large |
| 2 | P0 | TokenDialog | API token set/clear with eye toggle + Plex OAuth login popup | large |
| 3 | P0 | DiscoverDialog | Multi-step user discovery wizard (search → select user → map to continuum user+profile) | large |
| 4 | P0 | MappingsSection | Full user mapping CRUD table with run-per-mapping | large |
| 5 | P0 | RunsSection with metrics | Expandable run details: 6 metric counters + warnings + unmatched samples + cancel | large |
| 6 | P1 | SourceBar | Source selector dropdown + token status badge + action buttons | medium |
| 7 | P1 | Realtime event channel | Auto-refresh when import events arrive | small |
| 8 | P2 | Status badges | Colored status badges (queued/running/completed/failed/cancelled) with icons | small |
