# Admin Monitor parity refresh — November 20, 2026

Branch `t3/admin-monitor-parity-refresh`, started from the unmerged #207 commit `4565dcce1e9f5a23d0c843c57544ea807ed75d72`
(branch `origin/t3/admin-activity-branding-fixes`, written against official `217ff25abc`) and merged with Beta
`d51eb2d8528ece8c9b416612ac09f22607ab390a` (normal merge, no rebase). Versions are unchanged.

Official reference: Silo Server `e130f262d412cc7ffc4f0e040bdaba02c132515e`, read with `git show <commit>:<path>` from
`D:/SiloPlayer-work/server-upstream-nov14` (never checked out or modified). The earlier pins were `1a99f7be` and `217ff25abc`.

**What this record can and cannot claim.** There is no running official server and there are no official screenshots. Every row
below compares the official component source with the native source, and the captures named below were opened and looked at.
Nothing here claims pixel parity. Capture sets are synthetic fixture data under the dark theme at 1440, 1024, 800 and 560
device-independent pixels; other themes, locales, DPI scales and live-server traffic were not exercised.

## How the merge went

Two conflicts, both additive: `docs/current-development.md` (Beta's top entries first, then #207's November 17 entry) and
`src/SiloPlayer.Core/Services/Admin/AdminText.cs` (both sides added disjoint dictionary entries; no duplicate key). Git auto-merged
`AdminMonitorUi.cs` (Beta added Lucide icon paths for other pages; #207 added `film`/`audio-lines`/`globe`; they coexist) and
`admin-monitor-surfaces.json`. Beta had also merged #200 (Now Playing even rows and tag-row clipping) into
`DashboardWidgets.Operations.cs` after #207's base; it auto-merged, and Beta's tag rows were kept. `git grep "^<<<<<<< \|^>>>>>>> "`
printed nothing before the merge commit. #207's behaviour was kept wherever Beta had not touched the same code; where both sides
changed Dashboard widgets, Beta's behaviour won.

## Review finding on #207 (P2): "Gate Stacked on the advertised capability"

`HasMethodSplit` applied `All` to an empty point list and answered true, so an older server with no samples in the selected window
offered Stacked (and a click persisted the unsupported view), and the same happened while the timeseries request was still loading.

Fix: `HasMethodSplit` now requires at least one point; `DashboardChartMath.CanStackConcurrent(capabilities, series)` offers Stacked
only when the server advertises `streams_by_method` (the capability read is now one of the widget's keys, so Stacked appears or stays
away when it lands), withdraws it for a window an older replica answered without the split, and a saved Stacked on an unsupported
server still draws Lines without being rewritten. Tests: `AdminMonitorParityRefreshTests.AnEmptyWindowShowsNoMethodSplit` and
`StackedIsOfferedOnlyWhenTheServerAdvertisesTheSplit` (the first fails against the old `HasMethodSplit`); the native Concurrent
fixture's capabilities response now advertises `streams_by_method` only when the fixture sends the split, which also proves the
older-replica path end to end.

## What changed upstream since `217ff25abc` and what was done

| Upstream change | Native action |
| --- | --- |
| #4561 `fix(apiv2)!` live rows name an episode as history does (`media_type` episode, `media_title` the episode, `series_title` the show, `media_item_id`; `series_name`, `episode_name`, `content_id` removed) | Fixed. `AdminActivitySession` and `DashboardSession` read the new names; Activity/Now Playing/Recent titles, subtitles, search and item links follow. A server that still sends the old names (which the official client no longer reads) is folded into the current shape by `NormalizeLegacyNaming` so older servers keep working; this is a native-only courtesy, covered by a test. |
| #4561 Activity type filter: the Series chip selects `episode` and keeps show-level `series` rows; search matches `series_title` | Fixed (`AdminActivityListState`), tested. |
| #4561 household sessions (`GET` household sessions) use the same shape | Not touched: `PlaybackSessionSummary` feeds Settings › Household streams, outside this section. It still reads the old names. Reported, not fixed. |
| #4562 live rows carry a nullable `subtitle` object | Not read by the official WebUI at this commit; not shown. |
| #4626 Activity row links and hover visible (`ROW_LINK_CLASS`, `ROW_HOVER_CLASS`, expand button `surface-raised`) | Fixed: links underline on hover/focus (`AdminMonitorUi.LinkButton`), row highlight and lit chevron held back over links and non-toggle buttons, expand button hover. |
| #4650/#4632 links whose only hover style was the primary colour get an underline | Fixed for Activity, Now Playing title, bar-list items, Server Activity task/preparation rows. |
| #4703 server activity count readable; #4729 live badge contrast, no pulse | Fixed: count is `bg-foreground`/`text-background` with a 2px background ring and no pulse (`ServerActivityButton`; also seen by the main shell, which hosts the same control); live badge label `#fca5a5`. |
| #4674, #4691, #4709 phone header | Title `leading-normal` without top margin, eyebrow ellipsis. The sticky behaviour (#4709) is inherent: the native header sits outside the scrolling frame. `scroll-mt-24` has no native equivalent (no anchor jumps). |
| #4768, #4700 label and fill tokens | Method tags use the `-label` text colour (destructive mixes 25% toward white). Red fills under white text (Diagnostics Delete, the Users widget "Disabled" pill, the API client sign-out confirmation) use `#dc2626`. |
| #4671 `dark:` follows the Silo theme | Web-only; the native theme service already follows the server theme. |
| #4629 dashboard auto-refresh fixed 60 s cadence | Native already reads `LastUpdatedAt` on a one-second tick, so data arrivals cannot postpone it; a regression test pins that. |
| `RecentActivity` catalog description "Most recent plays across all accounts" | Fixed (`DashboardTextCatalog`). |
| `dragAutoScroll` pinned-header inset, export-only edits | Web-only drag behaviour; native uses its own drag handler. |
| #3909 and later right-to-left passes | The native shell cannot mirror: WinUI layouts here are not direction-aware. Not attempted. |
| Task/log text translations; new task `backfill_attachment_bitrate`; `messageUnconfirmed` toast key | Task catalog belongs to the Scheduled Tasks page (another family); `messageUnconfirmed` is unused by the web client at this commit. No Logs or Diagnostics string changed. |
| Logs / Diagnostics sources (`AdminLogs*`, `AdminDiagnostics*`, `logs.ts`, `diagnostics.ts`) | No structural change since `217ff25abc` (only an export removal). |

## Shared control and shell changes, and who else they touch

* `AdminMonitorUi` (Controls/AdminMonitorUi.cs): `LiveText` colour (every `LiveBadge`: sidebar Activity pill, Activity header), `LinkButton`,
  `Mix`, `LabelColor`, `DestructiveFill`. New `AdminShrinkPanel` (flex-row with truncating items; used by Activity only).
* `ServerActivityButton`: badge restyle affects the Admin shell **and** the main app shell; popover task/preparation rows and "View all"
  links now navigate when the target page is registered (Tasks, Libraries, Downloads preparations).
* `AdminShellPage`: narrow header typography; `CanOpenHref` (used by Activity, Server Activity, Dashboard).
* `DashboardUi.TextLink`: optional hover underline; `LogsPageHeader`: optional "stretch actions when wrapped" (Dashboard only; off elsewhere).
* Dashboard widget context gained `OpenHref` and `OpenItem`; the widgets that were plain text where the official page links now link
  whenever History, Users, Libraries, Autoscan, Nodes or Tasks is registered in the native shell (and stay plain text otherwise).
* No change to `DestructiveButtonStyle`, any theme dictionary, or any other family's page.

## Native fixtures and how each reaches a selector

* `admin-monitor` (`AdminMonitorShellNativeFixture`): its default run now also executes the #207 Activity corrections/IP Lookup fixture and the
  custom-branding fixture (both were reachable only through `SILO_NATIVE_ADMIN_CASE`). Activity empty, error and stale states are captured at the four widths.
* `admin-dashboard`: the default run now also runs the Concurrent streams fixture (previously `SILO_NATIVE_ADMIN_CASE=concurrent-correction` only).
* `admin-monitor-logs`: Server Log, Client Diagnostics and Logging Settings state captures at the four widths.
* Fixture changes forced by current behaviour: reasons are inline runs of one text block; the branding fixture counts reads relative to its start and uses its own wordmark URL; the Dashboard fixture's rows use the current wire names and the Series subtitle rule.

## Per-page difference tables

Legend: **fixed**; **not possible** (reason); **left** (reason). "Viewed" names captures from `D:/SiloPlayer-build/admin-completion-lane-a/results/` that I opened.

### Shell chrome (AdminLayout, AdminSidebar)

| Official element | Difference found | Result |
| --- | --- | --- |
| Phone header title, eyebrow | Title had a 4px top margin and natural line height; eyebrow could be cut | fixed (22.5px line, no margin, ellipsis) |
| Phone header position | Pinned while pages scroll | already true (outside the scrolling frame) |
| Live badge `.live-badge` | Label `#f87171` and pulse | fixed (`#fca5a5`, static) |
| Server Activity count | Red pill, pulsing | fixed (foreground fill, background ring, static) |
| Server Activity popover rows | Task/preparation titles and "View all" were text | fixed where the target page is registered |
| Sidebar sections, search field, build card, Back to App, active bar, 240px, 320px drawer, 1560px content frame, gutters 16/24/32/40 | Compared against `AdminSidebar.tsx`/`AdminLayout.tsx`; structure matches | no change |
| Skip link, `scroll-mt-24`, `data-adminShell` root attribute | Web document concepts | not possible |
| RTL (`rtl:` variants) | Not mirrored | not possible (see above) |

Viewed: `admin-activity-1440x900`, `-800x900`, `-560x900` (header, sidebar, drawer), `admin-server-branding-800`, `admin-indicator-active`.

### Dashboard

| Official element | Difference found | Result |
| --- | --- | --- |
| Header gap | `PageHeader` `mb-8` (32) wins over `space-y-6/8`; native added 8 at lg | fixed |
| Header actions below sm | `w-full` stack; native sat at the end | fixed (stretch once wrapped) |
| Now Playing / Recent Activity / Top Titles / Top Profiles / API clients / Libraries / Scanner / Scan activity / Users / Transcode nodes / Watch providers links | Rendered as plain text although the target pages are now registered | fixed (links; plain text when unregistered) |
| Bar-list item links | Colour only | fixed (hover underline) |
| Concurrent streams Stacked | Offered without the capability | fixed (review finding) |
| Live-row field names | `series_name`/`episode_name`/`content_id` | fixed |
| Red fill on the Users "Disabled" pill and sign-out confirmation | `#ef6b73` under white | fixed |
| Widget grid, stat tiles, charts, health strip, attention notice, customize bar, add-widget sheet | Matched in the earlier passes; recompared at the four widths | no change |
| Drag auto-scroll under a pinned header | Web drag implementation | not possible |
| Header `Updated … ago` and chips | Present | no change |

Viewed: `admin-dashboard-1440x900-step0`, `-800x900-step4`, `-560x900-step5`, `admin-dashboard-customize-1440x900`, the concurrent captures.

### Activity

| Official element | Difference found | Result |
| --- | --- | --- |
| Realtime status | Two-line "REALTIME / Live" block | fixed (dot + one line, warning when not live, heading as tooltip) |
| Header gap | 16 instead of `mb-8` | fixed (32) |
| Filter chips | Loose pills | fixed (segmented groups: 1px gaps, h-[30px], active `bg-primary/10`) |
| Node filter | Flat toggle list behind an icon button | fixed (labelled trigger with chevron, groups by kind, counts at the end) |
| "Clear" and filter status | Text only; 11px | fixed (x icon; 12px; 16px above) |
| Table header | `surface/50` | fixed (`bg-card`; `surface/50` kept for the stacked sort bar) |
| Rows | Striped; 0.3 borders | fixed (no striping, `border/40`, none on the last) |
| Row hover | None | fixed (hover highlight + lit chevron, held back over other links/buttons) |
| Username | Plain text | fixed (link to `/admin/users/{id}` when registered; also in IP Lookup) |
| Title, IP address | Plain / muted | fixed (row links with hover/focus underline) |
| Viewer cell | Wrapped; JF pill on the name line | fixed (three flex lines; only name and client truncate) |
| Stacked card line | Wrapped chips | fixed (one flex line with middle dot) |
| Profile pill | `bg-primary/15` | fixed (`/10`, max 7rem) |
| Method tags | Plain tone text | fixed (`-label` colour) |
| Bitrate bar | Fixed 104px | fixed (fills to 128px) |
| Expanded panel | Decision and reasons stacked; decision chip under the row label; tags as plain lines | fixed (inline, bordered tag chips, auto-sized label column) |
| Sticky table header while the page scrolls | Native keeps its nested scroller | left: WinUI has no sticky headers inside a scrolling parent without a nested scroller (kept deliberately by #207) |
| Progress line `flex` no-wrap | Wraps the play icon below when the cell is narrow | left: overflowing text would paint over the next column |
| Menu glyphs | Segoe Fluent glyphs instead of Lucide in the flyout | left |
| Error with no rows | Official shows "No active streams"; native shows the error and Retry | left on purpose (reachable recovery) |

Viewed: `admin-activity-1440x900`, `-1024x768`, `-800x900`, `-560x900`, `-expanded-1440x900`, `-expanded-560x900`, `-error-1024`, `-empty-560`, `activity-ip-lookup-800`.

### Logs (Server Log, Audit, Logging Settings)

No upstream change since `217ff25abc`. Recompared `AdminLogs.tsx` (header stream block, session panel, tabs, filter panels, tables, history bar, detail sheet) with `AdminLogsPage`: structure, column sets, labels and states match. Differences: none fixed; table rows keep the native hover/selection treatment of the shared `LogsNativeTable` (left, no official change); at 560 the application table scrolls horizontally as the official table does.

Viewed: `admin-logs-populated-1440x900`, `-560x900`, `admin-logs-detail-1440`, `admin-logs-error-800`, `admin-logs-audit-1024`.

### Client Diagnostics

No upstream change. Recompared `AdminDiagnostics.tsx`; one fix: the Delete button's red fill (`#dc2626`). The list-error row carries a Retry (official shows only the message; kept for recovery).

Viewed: `admin-shell-ClientDiagnostics-1440x900`, `-560x900`, `-detail-1440x900`, `admin-diagnostics-error-800`, `-empty-1024`.

## Limits

* Synthetic fixtures only; no live server, no other theme, locale or DPI, no Narrator/IME, no long-running traffic.
* Captures not opened were not used for any claim above; the fixture also writes many extra size sweeps that were not individually inspected.
* The Server Activity count badge change is shared with the main shell; the household streams contract change (#4561) remains in Settings.
* Right-to-left mirroring, document-level web behaviours and the app-wide destructive style are not part of this change.
