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
| #4626 Activity row links and hover visible (`ROW_LINK_CLASS`, `ROW_HOVER_CLASS`, expand button `surface-raised`) | Fixed: links underline on hover/focus (`AdminMonitorUi.LinkButton`), row highlight and lit chevron held back over links and non-toggle buttons, expand button hover. `LinkButton` later got its own template with no hover fill (review round). |
| #4650/#4632 links whose only hover style was the primary colour get an underline | Fixed for Activity, Now Playing title, bar-list items, Server Activity task/preparation rows. |
| #4703 server activity count readable; #4729 live badge contrast, no pulse | Fixed: count is `bg-foreground`/`text-background` with a 2px background ring and no pulse (`ServerActivityButton`; also seen by the main shell, which hosts the same control); live badge label `#fca5a5`. |
| #4674, #4691, #4709 phone header | Title `leading-normal` without top margin, eyebrow ellipsis. The sticky behaviour (#4709) is inherent: the native header sits outside the scrolling frame. `scroll-mt-24` has no native equivalent (no anchor jumps). |
| #4768, #4700 label and fill tokens | Method tags use the `-label` text colour (destructive mixes 25% toward white). Red fills under white text (Diagnostics Delete, the Users widget "Disabled" pill, the API client sign-out confirmation) use `#dc2626`. |
| #4671 `dark:` follows the Silo theme | Web-only; the native theme service already follows the server theme. |
| #4629 dashboard auto-refresh fixed 60 s cadence | Native already reads `LastUpdatedAt` on a one-second tick, so data arrivals cannot postpone it. No code changed; the store test that was added for it tested behaviour this branch did not change (and ended in a real delay) and was removed in the review round. |
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

Viewed: `admin-dashboard-1440x900-step0`, `-800x900-step4`, `admin-dashboard-customize-1440x900` (run `monitorref-vis-r4`), and `-560x900-step5` again in `monitorref-final-r2` after the stretched-actions fix. The Concurrent captures were written but not individually opened; the fixture assertions cover them.

### Activity

| Official element | Difference found | Result |
| --- | --- | --- |
| Realtime status | Two-line "REALTIME / Live" block | fixed (dot + one line, warning when not live, heading as tooltip; polite live region, see the review round) |
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
| Search box | Fixed 320 wide | fixed (review round): `w-full sm:w-80`, the whole filter row below 640, 320 from 640 up; also matches the profile name |
| Poster | Radius 2 | fixed (review round): `rounded-sm` = `--radius` (0.75rem) - 4px = 8px, as a brush so the image follows the corners |
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

Viewed: `admin-shell-ClientDiagnostics-1440x900`, `-560x900`, `-detail-1440x900` (re-opened in `monitorref-final-r2`, showing the `#dc2626` Delete), `admin-diagnostics-error-800`, `-empty-1024`.

## Review round (independent review of this branch: no P1, fourteen findings)

Each finding was checked against the code and the official source (`e130f262d`) before it was changed. Request bodies and URLs are unchanged.

| # | Finding | Result | Guard |
| --- | --- | --- | --- |
| 1 | `CanOpenHref` accepted the Dashboard catch-all, so a task key with "/", control characters or over 256 characters, or an unvalidated user id, rendered as a link that did nothing | fixed: `AdminRouteResolution.OpensRoute` (route, not catch-all, has a request) gates the link; an unopenable target is plain text. `.` and `..` are no longer valid segments (a browser normalises them away) | `AdminMonitorParityRefreshTests.AHrefOpensARouteOnlyWhenItNamesAPageOfItsOwn`, `AnOverlongSegmentIsNotALinkTarget` |
| 1a | Can a task key `..` reach Scheduled Tasks and change the API path? | Before the fix `..` passed `IsValidSegment` and reached the page as a segment. The page never used it: `AdminTaskDetailController` checks `AdminTasksApi.IsTaskKey` (pattern, and no `..`) before any read, and `TaskPath` throws before a request, so no different API path was possible. The link now refuses it too. The Scheduled Tasks page is unchanged | `ADotSegmentTaskKeyNeverReachesTheTasksApiPath` |
| 2 | `ImageService`: the 30 s deadline started before the 16-slot wait, so a queued poster returned null uncached | fixed: the deadline starts after the slot is held and bounds the transfer only. A caller's own cancellation already abandoned its wait in `GetImageDiskPathAsync` (`WaitAsync(ct)`); the shared download never took a caller token, and that is unchanged | `ImageServiceTests.SharedDownloadDeadline_DoesNotCountTimeSpentQueuedForASlot` |
| 3 | Concurrent native fixture never asserted the stored view; "capability advertised, points without the split" never ran | fixed: asserts `EffectiveView` and the entry's view stay Stacked and the server-stored layout still says Stacked, for the older replica and for the advertised-capability case (`Server.AdvertiseMethodSplit`) | `admin-dashboard` native |
| 4 | Hover fill behind every new link | fixed: `LinkButton` has its own template with no pointer states (system focus visual and automation name kept); the shared ghost style is untouched | `admin-monitor` native runs the links; the hover itself was not captured |
| 5 | Recent activity link underlined on hover | fixed: `underlineOnHover: false` for the history link (`hover:text-primary` only) | none (visual) |
| 6 | Search missed the profile name | fixed. Route labels: the native text catalog is English only, so matching the shown route text is matching official's shown plus English text | `AdminActivityListStateTests.SearchMatchesTheProfileNameAndTheRouteLabelText` |
| 7 | Search box fixed at 320 | fixed (see Activity table) | `admin-monitor` native: 560 and 639 fill the row (528 of 528, 607 of 607), 640 and up are 320 |
| 8 | Poster radius 2 | fixed, 8px | `admin-monitor` native checks the radius at every width |
| 9 | Realtime status not announced | fixed: polite live region on the label; the change is raised on the new element once it is loaded, only when the status text differs from the last one shown. Re-using one status element across rebuilt headers (the first attempt) crashed the native host, so none is kept | `admin-monitor` native: live setting present; a re-render does not announce an unchanged status |
| 10 | `MonitorSourceProof.Verify()` did nothing unless `SILO_MONITOR_SOURCE_ROOT` was set | fixed: falls back to `SILO_ACCEPTANCE_SOURCE_ROOT` (set by the acceptance script), and logs a SKIP line if neither is set. In `monitorfix-final-r3` it ran and passed: 1,841 documents for `SiloPlayer` and 287 for the host | `admin-monitor` native |
| 11 | Dashboard store test for unchanged behaviour with a fixed `Task.Delay(50)` | removed | n/a |
| 12 | `AdminShrinkPanel` overflow unclipped, `_shrinkers` never pruned | fixed: clips to its arranged size (set after layout, not inside it) and prunes removed children on measure | none (no unit seam; visual) |
| 13 | Per-row `CreateRegistry()` in the Server Activity popover; `RowHover` walked the subtree on every pointer move | fixed: `AdminShellPage.CreateHrefProbe()` resolves the registry once per render; `RowHover` takes the row's expand button when the row is built | none (cost) |
| 14 | Count badge fill snapshot after a theme switch | established: `ThemeService.ApplyTheme` changes the colours of the shared brushes in place and does not rebuild the main window; `MainServerActivityButton` is declared in `MainWindow.xaml` and lives for the session, so the snapshot fill (`A.Solid`) would not have followed while the text (`AppBackgroundBrush`) did. Fixed: the fill is the live `PrimaryTextBrush` | `admin-monitor` native (fill is the resource brush) |

Not changed: the household streams panel (`SessionDisplayText.cs`, `PlaybackSessionSummary`).

## Acceptance

One `-Stage final` run on the final source: label `monitorref-final-r2` (lane `D:/SiloPlayer-build/admin-completion-lane-a`), commit `538187b80`: focused 450 and full 10,308 unit cases, zero failures. Native groups all passed: admin-dashboard 334, admin-monitor 1,013, admin-monitor-logs 268, admin-manage-foundation 36, admin-configure-general 188, admin-playback-administration 99, admin-requests-management 37 PASS lines (PASS lines count assertions and captures, not unit tests), zero FAIL lines. `monitorref-final-r1` failed only the source-guard unit test that lists approved Monitor surface files (`AdminShrinkPanel.cs` was not yet registered); it was registered in `admin-monitor-surfaces.json` and the whole run repeated.

### Review round acceptance

Merged Beta `65e9ac880` (normal merge; the only conflict was `docs/current-development.md`, resolved by keeping this branch's entry on top of Beta's; `admin-monitor-surfaces.json` merged cleanly and parses). Final run `monitorfix-final-r3` (lane `D:/SiloPlayer-build/admin-completion-lane-a`, source `4e50b3a90`): focused 793 and full 10,387 unit cases, zero failures. Native, PASS lines with zero FAIL: admin-monitor 1,036, admin-dashboard 339, admin-monitor-logs 268, admin-manage-foundation 36, admin-configure-general 188, admin-playback-administration 99, admin-requests-management 37. The poster-loading viewer selector `artwork-first-mount` in the same run failed five overlay/badge assertions ("custom PosterCard applies loaded overlay preferences without rebind" and four like it); with all review fixes reverted on top of the merge (`monitorfix-exp-r6`) it fails the same way, so it is a baseline failure and is not attributed to the fixes. `retained-library` (70 PASS) and `poster-captions` (2 PASS) passed on the final source (`monitorfix-viewer-r1`). `monitorfix-final-r1` and `-r2` ended with a native host crash in `admin-monitor` (see finding 9); `monitorfix-exp-r1` to `r6` were bisection runs.

Captures opened and looked at (`monitorfix-final-r3-admin-monitor`): `admin-activity-560x900` (the search box spans the row), `admin-activity-1440x900` (320 wide; Live status line), `admin-indicator-active` (count badges). Not seen: link hover (no capture of a hovered state), poster corners with an image (fixture posters do not load; only the empty placeholder, which keeps the radius, was drawn), the live region in a screen reader.

## Limits

* Synthetic fixtures only; no live server, no other theme, locale or DPI, no Narrator/IME, no long-running traffic.
* Captures not opened were not used for any claim above; the fixture also writes many extra size sweeps that were not individually inspected.
* The Server Activity count badge change is shared with the main shell; the household streams contract change (#4561) remains in Settings.
* Right-to-left mirroring, document-level web behaviours and the app-wide destructive style are not part of this change.

## Merge with beta after #207 merged (November 21, 2026)

Local branch `local/monitor-232-with-beta` (never pushed): this branch (`0a856b9b9`) merged with Beta `8cf35edad` by a normal merge
(`a8ba38ee1`). #207 had been squash-merged into Beta as `e047e8d54`, so Beta and this branch held two descendants of `4565dcce1`.
Beta's side of every conflicting file was `4565dcce1` itself except for two pipeline repairs (`05635ee88`, "fix: address review
findings (pipeline attempt 1)", written without building or running anything; the pipeline's later merge commit `3d13cbbcb` only
merged Beta). This branch's side is the reviewed, built and tested descendant, so it was kept for every conflict.

| File | Resolution |
| --- | --- |
| `docs/current-development.md` | Both sides' entries kept once: this branch's entry above Beta's. The #207 entry that Beta carries at its top and this branch carried lower down was identical; the lower copy was removed. |
| `DashboardChartMath.cs`, `DashboardWidgets.Insights.cs`, `AdminActivityPage.cs`, `AdminActivityPage.Details.cs`, `AdminDashboardConcurrentNativeFixture.cs`, `AdminDashboardNativeFixture.cs`, `MonitorSourceProof.cs` | Beta's side was unchanged `4565dcce1`; this branch's side kept (`CanStackConcurrent`, `HasMethodSplit` needing a point, the reworked Activity). |
| `ImageService.cs` | Beta's side was `4565dcce1`; this branch's side kept (the 30 second deadline starts only after a download slot is held). |
| `AdminActivityPage.Rows.cs`, `AdminShellPage.Branding.cs`, `AdminActivityCorrectionNativeFixture.cs`, `AdminMonitorShellNativeFixture.cs`, `AdminShellBrandingNativeFixture.cs`, `AdminMonitorCorrectionsTests.cs` | This branch's side kept, then Beta's repairs ported (next table). |
| `ImageServiceTests.cs` (merged without a textual conflict) | The merge left two byte-identical copies of `SharedDownloadDeadline_CancelsStalledTransfersAndReleasesEverySlot`, a compile error (CS0111). One copy removed; every other test kept. |

| Pipeline repair of #207 | Review finding | Where its intent lives now |
| --- | --- | --- |
| SVG wordmarks: the cached body (always `.img`, URL possibly extensionless) is inspected by content, SVG loads through `SvgImageSource`, a malformed SVG keeps the bundled default, the lease and version are re-checked after the new await | The wordmark of a server with a vector logo fell back to bundled Silo because `BitmapImage` cannot decode SVG | Not handled by this branch; ported in `e8690854d`: `SvgArtwork.IsSvgContent` (arrived from Beta unchanged), `AdminShellPage.WordmarkSourceAsync` and the second lease check in `LoadBrandingAsync`. Tests: `CachedBrandingBodiesSelectTheSvgDecoderByContent`; native `AdminShellBrandingNativeFixture` serves an extensionless `image/svg+xml` wordmark (5,376 matching pixels) and `AdminMonitorShellNativeFixture` serves it |
| Compact bitrate: stacked Activity cards show the measured rate with the limit/source reference between the method line and the progress line | The stacked layout had no Bitrate column, so the rate was unreadable below 62rem (reported twice, same defect) | Not handled by this branch; ported in `e8690854d`: `BitrateReference` (shared with the wide `BitrateCell`) and `BitrateLine` in `StackedRow`. Tests: `StackedActivityCardsKeepTheMeasuredBitrateReading`; native `CheckCompactBitrate` at 1120, 800 and 1311 |

The two other findings that #207's review raised are not pipeline repairs and were already fixed by this branch: Stacked offered without the advertised
capability (`CanStackConcurrent`) and the shared image loader's deadline counting queue time (`ImageService`).

Assertions dropped: none. Every assertion of both sides is present (the only removal is the identical duplicated test above). The ported native check
`CheckCompactBitrate` ran against this branch's Activity with the fixture's 16000 and 3500 kbps sessions.

Runs (lane `D:/SiloPlayer-build/admin-completion-lane-lead`):
* `monitormerge-beta-r1` (`-Stage publish-native`, unchanged Beta `8cf35edad`): admin-monitor 113, admin-dashboard 311, admin-monitor-logs 232 PASS lines, zero FAIL. Beta's Monitor groups work today. Beta's `admin-monitor` default run does not execute the Activity corrections or branding fixtures (here they are reachable from the selector), so Beta's two repairs were never exercised natively there.
* `monitormerge-focused-r1` failed to compile (the duplicated test); `monitormerge-focused-r2` after the fix: 419 passed.
* `monitormerge-final-r1` (`-Stage final`, source `57014c7cb`): focused 419 and full 11,048 unit cases passed, zero failures; native PASS lines, zero FAIL: admin-monitor 1,071, admin-dashboard 339, admin-monitor-logs 268, admin-manage-foundation 36, admin-configure-general 188, admin-playback-administration 99, admin-requests-management 37, retained-library 70, poster-captions 2. Against `monitorfix-final-r3` (10,387 unit; 1,036, 339, 268) no count went down: unit +661 from Beta's tests, admin-monitor +35 (the ten ported compact-bitrate and SVG checks, plus the text-line-height checks that now also cover the compact bitrate lines; a diff of the two result files shows no assertion missing apart from clock-dependent elapsed-time labels), the others equal.
* `monitormerge-native-r1` (`-Stage native`, same build): admin-monitor 1,071, admin-dashboard 339, zero FAIL.
Not run: the native groups known to fail on Beta; no live server, no phone, no installer.
