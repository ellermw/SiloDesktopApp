# Admin Pages -- Consolidated Backlog

Generated: 2026-04-15
Source: 11 full-audit files in `docs/audit-gaps/FULL-AUDIT-admin-*.md`

---

## Section 1: Cross-cutting patterns

Bugs or gaps that appear on 3+ admin pages and can be fixed once in a shared location.

### XC-1: Loading/error state hides entire page
**Pages:** Activity, Catalog Maintenance, Nodes, API Keys, Invite Codes, Providers, Subtitle Providers, Playback History, Settings (all tabs), Dashboard
**Description:** Desktop renders a full-page ProgressRing or centered error+Retry panel that hides the header, filters, and card chrome. Webui keeps page chrome visible and renders skeleton rows or inline error within each card body.
**Fix:** Move IsLoading / ErrorMessage visibility bindings off the root ScrollViewer. Render loading skeletons and error banners inside each card's body, leaving the page header and filter bar always visible.
**Severity:** P0

### XC-2: No realtime refresh / event-channel integration
**Pages:** Activity, Catalog Maintenance, Libraries, Playback History, Nodes (unverified), Settings (implicit)
**Description:** Desktop performs a single one-shot load on Page_Loaded and refreshes only after explicit user mutations. Webui uses React Query refetchInterval, refetchOnWindowFocus, or event-channel SSE subscriptions.
**Fix:** Subscribe to relevant EventChannelClient channels (sessions, scans, jobs) and invalidate/re-fetch. Add fallback DispatcherTimer (10-30s). EventChannelClient is already operational.
**Severity:** P0

### XC-3: Panel / card corner radius drift (26px vs 12-16px)
**Pages:** Activity (IP Lookup 26, summary 26), Nodes (table 26, banner 22), API Keys (table 26), Invite Codes (Public Signups 22, table 26), Providers (table 26), Subtitle Providers (card 20), Playback History (stat cards 8 vs 16, table card 8 vs 16)
**Fix:** Define RadiusXL = 12 and Radius2XL = 16 in DarkTheme.xaml. Audit every CornerRadius and map to correct Tailwind token.
**Severity:** P1

### XC-4: Table header typography drift
**Pages:** Activity, Playback History, Nodes, API Keys, Invite Codes, Providers
**Description:** Desktop uses FontSize=10-12, SemiBold, TertiaryTextBrush, often UPPERCASE. Webui uses FontSize=14, Medium, muted-foreground, Title Case.
**Fix:** Create shared AdminTableHeaderStyle. Apply uniformly.
**Severity:** P1

### XC-5: Inline status banner vs toast
**Pages:** Nodes, API Keys, Invite Codes, Providers, Subtitle Providers, Sections, Catalog Maintenance
**Description:** Desktop uses inline accent banner with 4s auto-hide. Webui uses sonner toast notifications.
**Fix:** Implement shared toast/InfoBar service and replace all inline banners.
**Severity:** P1

### XC-6: Delete/destructive confirmation buttons not styled red
**Pages:** Nodes, API Keys, Invite Codes, Activity (Terminate session)
**Fix:** Create DestructiveButtonStyle (red foreground/background). Ensure safety text present.
**Severity:** P1

### XC-7: Row hover state missing
**Pages:** Activity, Dashboard (libraries), Playback History, Nodes, API Keys, Invite Codes, Providers
**Fix:** Add PointerEntered/PointerExited handlers to row builders with subtle bg tint.
**Severity:** P2

### XC-8: Delete icon colored red in row actions
**Pages:** Nodes, API Keys, Providers
**Description:** Desktop colors trash icon red. Webui ghost button inherits neutral color.
**Fix:** Remove hardcoded red foreground from MakeIconButton delete icons.
**Severity:** P2

### XC-9: Dialog field typography drift
**Pages:** Nodes, API Keys, Invite Codes, Providers, Subtitle Providers
**Description:** Desktop labels FontSize=12 SemiBold SecondaryTextBrush. Webui FontSize=14 Medium default foreground.
**Fix:** Update shared AddField helper: label FontSize=14, FontWeight=Medium, PrimaryTextBrush.
**Severity:** P2

---

## Section 2: Flat master list sorted by severity

### P0 -- Critical (57 items)

| # | Page | Description | Effort | XC? |
|---|------|-------------|--------|-----|
| 1 | Settings (Storage) | Setting keys use s3.operational_* -- server expects s3.public_* / s3.private_*. Every save is a no-op. | large | no |
| 2 | Settings (Storage) | Missing Private Internal sub-tab (2 vs 3 sub-tabs). | large | no |
| 3 | Collections | TMDB Preset Import Form missing. | large | no |
| 4 | Collections | MDBList Import Form missing. | large | no |
| 5 | Collections | Migrate admin editing from dialogs to full-page editor. | large | no |
| 6 | Libraries | ScanQueuePopover completely absent. | large | no |
| 7 | Libraries | Unmatched Items Section completely missing. | large | no |
| 8 | Libraries | Stale Media IDs Section completely missing. | large | no |
| 9 | Libraries | Metadata Provider Configuration missing from LibraryForm. | large | no |
| 10 | Libraries | Ambiguous Roots Section with Override Dialog missing. | large | no |
| 11 | Sections | Form missing collection picker, library multi-select, media scope, rules editor. | large | no |
| 12 | Sections | BuildCreateBody doesn't construct config payload. | medium | no |
| 13 | Sections | Missing queryDefinition helpers. | medium | no |
| 14 | Sections | Drag-drop reordering absent (uses move buttons). | large | no |
| 15 | Sections | Collection badges show generic "Collection" text. | small | no |
| 16 | Settings (Theme) | Live theme preview card missing. | large | no |
| 17 | Settings (Theme) | Token editor with color pickers missing. | large | no |
| 18 | Settings | Missing restartRequired tracking + Restart Server button. | medium | no |
| 19 | Settings (Overlays) | edition overlay missing from registry. | small | no |
| 20 | Settings (Log Retention) | Default bucket policies differ (5 vs 4 rules). | small | no |
| 21 | Settings (Log Retention) | Level options include debug -- webui excludes it. | small | no |
| 22 | Settings (Database) | sensitiveManagedByEnv badge + disabled fields not implemented. | medium | no |
| 23 | Settings (Theme) | No CSS sanitizer -- security concern. | medium | no |
| 24 | Providers | AdminProvidersPage has NO webui counterpart -- fabricated page. | medium | no |
| 25 | Subtitle Providers | Promoted from subsection to standalone page. | medium | no |
| 26 | Activity | Sort headers on 5 columns completely absent. | medium | no |
| 27 | Activity | Realtime events not wired to page. | medium | XC-2 |
| 28 | Activity | User-name link missing. | small | no |
| 29 | Activity | Stream-title link missing. | small | no |
| 30 | Activity | IP Lookup user link missing. | small | no |
| 31 | Activity | Connection state indicator missing. | small | no |
| 32 | Activity | Auto-switched source hint missing. | small | no |
| 33 | Activity | Action overflow menu missing (4 inline buttons vs dropdown). | medium | no |
| 34 | Activity | View Logs / FFmpeg Logs deep links missing. | small | no |
| 35 | Activity | Clear-Filters must not clear search. | small | no |
| 36 | Dashboard | AdminSessionActions entirely missing in Recent Activity. | medium | no |
| 37 | Dashboard | Stream Card title not clickable. | small | no |
| 38 | Users | Impersonate User not implemented. | medium | no |
| 39 | Libraries | Drag-and-Drop marked TODO, non-functional. | medium | no |
| 40 | Playback History | Loading hides entire page. | small | XC-1 |
| 41 | Playback History | Error hides entire page. | small | XC-1 |
| 42 | Playback History | No auto-refresh -- label is a lie. | medium | XC-2 |
| 43 | Playback History | Method badge colors invented (per-method). | small | no |
| 44 | Playback History | Status badge colors wrong (green vs blue, gray vs outline). | small | no |
| 45 | Catalog Maintenance | Loading/error hides entire page. | small | XC-1 |
| 46 | Catalog Maintenance | No realtime refresh while jobs run. | medium | XC-2 |
| 47 | Invite Codes | Copy button missing from Code cell. | small | no |
| 48 | Invite Codes | Inline Switch missing in Status column. | small | no |
| 49 | Invite Codes | Structural: standalone page vs webui tab. | medium | no |
| 50 | Invite Codes | Code input not auto-uppercase. | small | no |
| 51 | Invite Codes | max_uses=0 allowed -- webui enforces min=1. | small | no |
| 52 | Invite Codes | Delete dialog missing safety text. | small | no |
| 53 | API Keys | Pagination completely missing. | medium | no |
| 54 | API Keys | No copy feedback. | small | no |
| 55 | Nodes | Count badge invisible (same bg as card). | small | no |
| 56 | Nodes | No spin animation on check-health pending. | small | no |
| 57 | Catalog Maintenance | Progress formatter missing thousand separators. | small | no |

### P1 -- Important (93 items)

| # | Page | Description | Effort | XC? |
|---|------|-------------|--------|-----|
| 58 | Collections | Sync Schedule field -- display only, no edit. | medium | no |
| 59 | Collections | Source config persistence. | medium | no |
| 60 | Collections | Image upload fields missing. | medium | no |
| 61 | Collections | library_ids multi-select (single picker only). | small | no |
| 62 | Libraries | Active scan count badges missing in Status. | small | no |
| 63 | Libraries | Inline refresh job progress missing. | small | no |
| 64 | Libraries | Inline active scans list missing. | small | no |
| 65 | Libraries | Cancel Scans button missing. | small | no |
| 66 | Libraries | Realtime event channel missing. | medium | XC-2 |
| 67 | Libraries | Skipped Roots: search/sort/pagination/expand. | medium | no |
| 68 | Libraries | Metadata Language selector missing. | small | no |
| 69 | Libraries | Chapter Thumbnails toggle missing. | small | no |
| 70 | Settings (Theme) | Reset all button missing. | small | no |
| 71 | Settings (Theme) | Debounced auto-save vs SaveBar. | medium | no |
| 72 | Settings (Playback) | HW-accel resolved live indicator. | medium | no |
| 73 | Settings (Playback) | Select labels auto-capitalized. | small | no |
| 74 | Settings (Rate Limit) | Per-tab save vs shared SaveBar. | medium | no |
| 75-78 | Settings (Log Retention) | Parse-error recovery, save-error text, subtitle sentence, restartRequired. | small | no |
| 79-80 | Settings (Nav) | Active tab not persisted, active pill indicator. | small | no |
| 81 | Settings (Integrations) | Eye/EyeOff toggle for API keys. | small | no |
| 82-83 | Settings (SaveBar/Downloads) | Saving... text, restartRequired tracking. | small | no |
| 84-91 | Subtitle Providers | Card radius, badge style, enabled toggle, test placement, test result, save pending, font size, max-width. | small | no |
| 92 | Activity | FFmpeg inline log panel. | large | no |
| 93-99 | Activity | IP collapsible, loading/empty states, table scroll, empty state contextual, refresh icon, live badge icon, column mins. | small | no |
| 100-102 | Dashboard | User row nav, activity title nav, overflow sessions link. | small | no |
| 103-113 | Playback History | media_item_id filter, empty state, logs link color, row hover, stat card, separator, profile sub, date seconds, combos, fonts, radii. | small | no/XC |
| 114-117 | Catalog Maintenance | job_type filter, export progress bars, button styling, deep links. | small-med | no |
| 118-150 | Nodes/API Keys/Invite Codes/Providers | Various typography, radius, badge, layout, dialog fixes. | small | XC |

### P2 -- Polish (88 items)

Items 151-238: eyebrow tracking, icon sizes, date formats, hover states, responsive sizing, max-width containers, field typography, badge padding, etc. All small effort.

---

## Section 3: Summary counts

| Severity | Count |
|----------|-------|
| **P0** | 57 |
| **P1** | 93 |
| **P2** | 88 |
| **Total** | **238** |

### P0 breakdown by page

| Page | P0 items |
|------|----------|
| Activity | 10 |
| Settings | 9 |
| Libraries | 6 |
| Invite Codes | 6 |
| Sections | 5 |
| Playback History | 5 |
| Collections | 3 |
| Catalog Maintenance | 3 |
| Dashboard | 2 |
| API Keys | 2 |
| Nodes | 2 |
| Users | 1 |
| Providers | 1 |
| Subtitle Providers | 1 |
