# Admin User Detail Page — Full Audit (Deep Re-audit)
Webui: AdminUserDetail.tsx (673 lines) | Desktop: AdminUserDetailPage.xaml/.xaml.cs
## Status: Moderate parity — edit form is significantly less complete

### Already matching
- Breadcrumb/back nav, username + role/status badges
- Impersonate/Edit/Delete action buttons with confirm dialogs
- Tab bar: Overview/Profiles/Watch History/IP History
- Overview: account card (username/email/role/status/dates) + permissions card
- Profile list with profile cards
- Watch history table, IP history table

### Gaps
| # | Sev | Gap | Webui detail | Effort |
|---|-----|-----|-------------|--------|
| 1 | P2 | Playback quality descriptions | Webui PLAYBACK_QUALITY_OPTIONS shows description text per preset. Desktop has dropdown but no descriptions. | small |
| 2 | P2 | Watch history pagination | Webui may paginate; desktop shows all | small |

### Previously reported as P1 — actually already implemented:
- Edit form 3-tab dialog (Account/Access/Limits): DONE
- LibraryAccessSelector (All Libraries checkbox + per-library checkboxes): DONE
- Download permission toggles (downloadAllowed + downloadTranscodeAllowed): DONE
