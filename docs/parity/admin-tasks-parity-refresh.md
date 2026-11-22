# Configure > Scheduled Tasks: refresh against official `2972dc63c`

Scope: the list (`/admin/tasks`) and a task's page (`/admin/tasks/:key`) in
`Views/Admin/AdminScheduledTasksPage*.cs`, with `AdminTasksText`, the task models and the task API reader.
The earlier record, [`admin-tasks.md`](admin-tasks.md), settled the structure, operations, schedule
editing rules and history paging against official `92ee05187`; this file covers only what changed in the official
client and server since that was last compared, and what a source comparison found different.

## Baselines

- Native base: beta `beeb2b449` (`1.2.1393-beta.1`), branch `t3/admin-tasks-parity-refresh`.
- Official Silo Server/WebUI: `2972dc63c3291868ed5d452c9e95ad602d9ed022`, read only as exact `git show` / `git grep`
  objects from the blobless clone `D:/SiloPlayer-work/server-upstream-nov14`. The page was last compared against
  `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`.
- Files read at both commits: `web/src/pages/AdminTasks.tsx`, `AdminTaskDetail.tsx`, `AdminTaskTriggerForm.tsx`,
  `web/src/lib/taskText.ts`, `taskTrigger.ts`, `taskProgress.ts`, `itemTypeLabels.ts`,
  `web/src/components/admin/TaskStatusBadge.tsx`, `web/src/locales/en.json`,
  `contracts/api/v2/openapi.json` (every `AdminTask*` schema and `/admin/tasks` operation),
  `internal/apiv2/admin_tasks.go`, `admin_task_metrics.go`, `docs/admin-tasks-api.md`.
- Overlap: of the 12 pull requests open on beta (`gh pr list --base beta --state open --json number,files`), none
  edits `AdminScheduledTasksPage*`, `AdminTasksText`, the task models, the task API or the Scheduled Tasks fixture.
  #234 and #242 edit the shared `AdminText.cs` and #237 edits `AdminConfigureServices.cs`; this work edits neither
  (new text lives in `AdminTasksText.cs`). #244 edits the shared destructive theme and `DarkTheme.xaml`; this work does not.

## Upstream changes that reach these pages (`217ff25abc..2972dc63c`)

| Upstream change | What it does | Outcome |
| --- | --- | --- |
| `8a97ece87` #4677 | Category badge on a task's page shows `web.admin.tasks.category.<id>` instead of the key sent (`library`). | **Fix.** The badge and its accessible name use `AdminTasksText.Category`. An unknown category is shown as sent (the official map is typed to three categories and has no fallback; showing an empty badge is worse). |
| `0caa8b1ae` #4687 | Item types on the refresh task page map `movie`, `series`, `season`, `episode`, `ebook`, `audiobook`, `manga` to `web.search.type*`; an unknown type shows as sent; an empty type shows `web.admin.tasks.detail.itemFallback`. | **Fix.** `AdminTasksText.ItemType`. The seven `web.search.type*` keys are in the shared interface catalog, so the item type label follows the interface language. |
| `0d649bd32` #4688 | Failed step names in run history go through `taskText(step, "name")` (catalog entry keyed by the step key, else the name sent). | **Fix.** `HistoryError` uses the same lookup as task names (`AdminTasksText.TaskText`). |
| `ccd8b7390` #4749 | The server adds `series_title`, `season_number`, `episode_number` to refresh samples and sends the target's own title; the client composes the title (`sampleSeriesEpisode`, `sampleEpisode`, `sampleTitle`, season and episode codes) and shows the fixed `refreshFailed` sentence for a recent error; a failed run's badge tooltip and history cell show `taskStatus.failedDetails`; recent-error titles clamp to two lines and due-sample titles wrap. | **Fix.** Three optional fields read and bounded by the API reader, `AdminTasksText.SampleTitle`, catalog sentences, wrapping titles. Request URLs and bodies do not change (the metrics read is the same `GET`). |
| `927040075` #4627 | The task link on a row and the Libraries link in the `scan_libraries` note underline on hover. | **Fix.** Both links draw their underline only while the pointer is over them (row link: pointer handlers on the task row; note link: pointer-move hit test on its text). Keyboard focus is unchanged (the official rule is hover only). |
| `8e0441421` #4819, `43f488ecb` #4736, `58aacf65b` #4722, `190fd8dda` #4742, `e144acf20` #4891, `3d9634e05` #4920, `88680aee3` #4839 | Trigger fields stay inside their column; the type, weekday and time fields grow to their text but stop at the column; the whole time and the whole "None" placeholder show; the Max runtime label wraps and its box stays with its unit. | **Checked at 1440, 1024, 800 and 560 wide, fixed where it occurred** (see the layout table below). The time is a `TimePicker`, so #4722's browser-specific clipping does not apply. |
| `5a3156b9d` #4868, `379be8c75` #4904, `7746c542a` #4936 | The Add Trigger label wraps, a long word stays inside the button, and the schedule panel's buttons (Edit Schedule, Add Trigger, Review, Use this revision) wrap instead of running off screen. | **Checked and fixed where it occurred** (see the layout table). |
| `f124e246f` #4885 | Splits the realtime provider's connection state from its subscription context. | **Not applicable.** An internal React refactor; the native page keeps its own tasks channel lease, and the frames are unchanged. |
| `7ea46c825` #3551, `35b0ed7f0` #3611 | A plugin task without a saved schedule reports its default triggers at revision 0 under a validator that also changes when the default changes; cancel returns the task; a stale `PUT` against an unsaved schedule answers 412 with the current validator (or none). | **Already right.** The native editor treats the strong `ETag` as opaque, captures it when editing starts, refuses editing when it is weak, missing or `*`, keeps the draft on a 412, and re-reads before another save. A 412 without a validator is handled by the same review path (the review re-reads the schedule). No request changed. |
| `f18678fda` #4648, `165ab2b06` #4611 and the other commits in the range that touch the realtime provider, plugin lifecycle or other pages | Do not change `AdminTasks*`, the trigger form, the task models or the task text. | **Not applicable.** |
| `f6ed24869` #4700, `160a4425e` #4704 | Destructive buttons and badges use `--destructive-fill` (`#dc2626`) under `--destructive-foreground`; delete and disconnect confirms use the destructive style. | **Fix for the failed-run badge** (the official `TaskStatusBadge` uses the `destructive` badge variant for a failed run and for "Completed with errors"): a page-local solid `#DC2626` fill with white text, system colors in a contrast theme. **No task action has a destructive confirmation** (Run Now, Stop and Cancel send at once on both sides; the only dialog is the shared leave-with-unsaved-schedule discard dialog, which is not edited here). The Stop button is an outline button with `--destructive` text and border, unchanged by #4700. The shared destructive style and `DarkTheme.xaml` are not edited; pull request #244 owns them. |
| `en.json` | New keys `web.admin.common.taskStatus.failedDetails`, `web.admin.tasks.detail.refreshFailed`, `sampleEpisode`, `sampleSeriesEpisode`, `sampleTitle`, `backfill_attachment_bitrate.name` / `.description`, and `web.admin.tasks.detail.backupSchedule`. | **Added to the page's text table**, except `backupSchedule`: no official page uses it at `2972dc63c` (no reference outside the catalog), so it is left out rather than added unused. The mechanical comparison of the 257 existing table keys with the official catalog finds zero differences in wording. |

## Which text follows the interface language

Only the shared catalog keys follow the interface language: the item types (`web.search.type*`) and the season name and episode code used in a composed sample title (`web.detail.seasons.*`, `web.detail.episodeCode`). Everything else on these pages comes from the page's own English-only text table (`AdminTasksText`; the generated interface catalogs exclude `web.admin.*`): the category labels, task names and descriptions, step names, the fixed failure sentences, the sentence patterns that join series, code and title, the history and schedule wording. The official client translates all of these; the native pages show them in English whatever the interface language is. This was not reconciled. Where the table below says "translated" it describes the official client.

## Differences found between the official pages (`2972dc63c`) and the native pages on beta

| # | Where | Official | Native on beta | Resolution |
| --- | --- | --- | --- | --- |
| 1 | Task page, category badge | translated label | raw key (`library`) | text table lookup |
| 2 | Refresh task page, item type under a recent error and a due sample | translated label, unknown as sent, empty as "item" | raw value, empty as "item" | `ItemType` |
| 3 | Refresh task page, sample titles | composed from series, season and episode (translated season name or code) | `title`, else content id | `SampleTitle` |
| 4 | Refresh task page, recent error text | fixed catalog sentence | the reader's constant, drawn from a literal in the API reader | catalog key, shown for every recent error |
| 5 | Failed run, list badge tooltip and history Error cell | catalog sentence `failedDetails` | the server's `error_message` | catalog key for status `failed`; other statuses keep the trimmed server message |
| 6 | History Error cell, failed steps | translated step names | step names as sent | `TaskText` by step key |
| 7 | Links | underline on hover | Libraries link never underlined, task link never underlined | hover underline |
| 8 | Recent error and due sample titles | clamp to two lines / wrap | one trimmed line | wrap, two-line clamp for recent errors |
| 9 | History Error cell | single line with tooltip below 640 px, wraps from 640 px | always one trimmed line | wrap from 640 px |
| 10 | Failed and completed-with-errors badge | solid `#dc2626`, white text | 12% tint, red text | page-local solid fill |
| 11 | Schedule panel layout | fields stay in the column; Max runtime label wraps; panel buttons wrap | fixed widths in non-wrapping rows; buttons are one line | see layout table |
| 12 | Task catalog | `backfill_attachment_bitrate` has a name and description | shows the server's English | two keys |

## Layout of the schedule panel and task page (measured)

Captured by the native group at 1440, 1024, 800 and 560 device-independent pixels wide (list, a task's page with run history and queue health,
the schedule editor with weekly, daily and interval triggers, and the conflict review state). The fixture also asserts, at each width, that no
control or free text leaves its container sideways and that the container stays inside the page.

| Finding on beta (unchanged layout, English text) | Resolution |
| --- | --- |
| Schedule editor: weekday + time picker, interval + unit, type, Max runtime row, Add Trigger, Review and Use revision all fitted at 1440, 1024, 800 and 560. **No overflow fault occurred**; the weekly row had about 28 px to spare at 560. | Hardened without a visible fault to fix: the interval, weekly and Max runtime rows are wrapping rows (the label wraps above its box and the box stays with its unit), the type, unit and weekday selects have minimum widths and grow to their text, and the panel buttons wrap their label inside the button. This is for larger text sizes and longer labels; it was **not observed as a failure**. |
| History table at 560: the "Duration" header broke mid-word ("Duratio / n"). | **Fault, fixed.** Started, Duration and Status fit their content; Error takes the rest (minimum 120 px). Below 640 px the Error cell is one trimmed line with a tooltip, from 640 px it wraps (official `truncate sm:whitespace-normal`). |
| Due-samples table at 560: dates broke onto three lines. | **Fault, fixed twice.** First pass (superseded, see the review follow-up below): dates on one line and a sideways scroller below 640 px, which clipped the last column and could not be reached by keyboard. Current: dates stay on one line from a page width of 768 px (official `md:whitespace-nowrap`) and wrap at their spaces below it; the table is as wide as its panel at any width (the official table has no minimum width), with no scroller. Where the columns' minimums add to more than the panel they are given up in steps (second review follow-up below). |
| Recent-error titles ran as one trimmed line. | Wrap, clamped to two lines. |

Not changed (found, outside this unit): the official refresh page's counter grid switches at `sm` (two columns) and `xl` (five), and its reason-breakdown/recent-errors pair
splits only at `xl`; native switches between one column and the wide arrangement at a page width of 640 px. At 1024 px this leaves the recent-errors panel narrow, so titles
clamp early. The earlier record settled the structure; the breakpoint difference is listed here as not reconciled.

## Review follow-up (independent review of `e1404ae35`: no P1, two P2 and smaller items)

Nothing about running or cancelling tasks changed, and no controller, request body or URL was touched. Commits on top of `e1404ae35`: `9ca493e45` (reader and tests), `2580ca62c` (page), `c9b67356b` (native fixture); this record and the current-development entry follow in a docs-only commit.

| # | Finding | Resolution | Guard |
| --- | --- | --- | --- |
| 1 (P2) | The due-samples table was cut off below 640 px and not reachable by keyboard: both dates `noWrap` at every width, the table never narrower than 640 px inside a `ScrollViewer` (not a tab stop, nothing focusable inside). | Matches the official markup: dates `noWrap` only from a page width of 768 px (`DateNoWrapWidth`, `AdminScheduledTasksPage.cs`), wrapping below it; no 640 px floor and no scroller. Below 768 px the two date columns are star columns whose minimum is the longest date or header word at the heavier header weight plus the cell padding and 2 px (`DateColumnMinWidth`, `AdminScheduledTasksPage.Detail.cs`), the item column has the official minimum (128 plus padding). The earlier sentence that described the opposite is replaced in the layout table. | Native: at window widths 1440, 1024, 800, 640 and 560 (page widths 1200, 784, 800, 640, 560) the group checks no `ScrollViewer` in the panel, every text and the last column's right edge inside the panel and the page, the wrapping mode for the page width, and, when wrapping, that no date word is broken. `Overflowing` no longer treats a `ScrollViewer` as a control boundary. |
| 2 (P2) | Server text composed into the sample title with no neutralization; `Bound` could split a surrogate pair. | The reader applies the plugin-label rule (`AdminPluginText.Plain`: whitespace and line or paragraph separators become one space; control, format, private-use, unassigned characters removed; bounded at a whole text element) to the sample title, series title (so a season's own title too), item type, content ID (shown when there is no title) and step names. `Plain` gained one parameter, `keepJoiners`, which keeps U+200C and U+200D after a visible character (emoji sequences, Persian) and drops them at the start, after a space, doubled, or at the end; its default behavior is unchanged for plugin labels. `Bound` (used for the other fields) no longer cuts between the two units of a surrogate pair. A content ID that is empty after the reduction is an invalid sample, as an empty one was. Not neutralized (not in the finding): task name, task description, reason and attempt-bucket labels. | `AdminTasksApiTests`: one case per class (Cc, tab and line breaks, NEL, Zl, Zp, collapsed spaces, override, embeddings, isolates, marks and ALM, zero-width space, BOM, soft hyphen, word joiner, private use), the composed sentence, a content ID shown as the title, kept joiners (emoji family, Persian) and dropped stray ones, cuts at 300 text elements inside an emoji family and between emoji, `Bound` at 64 units; step names. `AdminPluginAppsTests`: default `Plain` still removes joiners. |
| 3 (P3) | A malformed optional field failed the whole metrics read. | `series_title`, `season_number`, `episode_number` are read as `JsonElement?`; another JSON kind, a fraction, an out-of-range exponent or a negative number counts as absent. | `AMalformedOptionalPartIsAbsentAndDoesNotFailTheMetricsRead` (12 cases). |
| 4 (P3) | `DestructiveBadge` allocated an `AccessibilitySettings` per failed badge per render. | Reads `ThemeService.IsSystemHighContrast` from the app services. The fixture now registers its `ThemeService`. | The existing badge color checks (solid `#dc2626` under white text); the contrast branch is still not exercised unless the host has high contrast on. |
| 5 (P3) | `Wrapping()` replaced string content with a text block. | The automation name is set explicitly (the button's own name, else its label). | Native: names `Edit Schedule`, `Add Trigger`, `Review current schedule`, `Use this revision and keep my draft` asserted with the wrapping content. |
| 6a (P3, confirmed) | `Edit Schedule` sat in an `Auto` column, which measures its child without a limit, so its label could not wrap. | The heading takes an `Auto` column (one line) and the button the star column. | Native: measured in 170 px the button's height exceeds one line (it was one line before: 32 px). |
| 6b (P3, not a defect) | History grid `Auto, Auto, Auto, Star(min 120)` between 640 and about 720 px. | Measured on the refresh task's history (with failed steps and badges): needs 634 px at 640, 661 at 680, 709 at 720: it fits each. No change. | Native: the three measurements are checks. |
| 7 | Record wording about the language. | The section "Which text follows the interface language" above; this record and the current-development entry no longer claim the whole page follows it. Two source comments still say "in the interface language" (`AdminTasksText.HistoryError`, `AdminTasksRefreshTextTests`); they were not edited because the docs commit must change no source. | n/a |
| 8 | Test gaps. | Unit: season 0 with its own title (with and without a series), `S00E07` with a title (with and without a series), episode 0, an episode number without a season number. Native: the hover check finds the callback through the page's own wiring record (`WireHover`/`HoverSetter`) for the task link (attached to the link, not the row's text or button) and for the Libraries link of the note, built through the page's `BuildScanNote` because Libraries is unregistered in the fixture; the note's hit test is checked on a laid-out note (on the link, above the line, past its end). The due-samples checks above cover what `Overflowing` could not see. | see rows 1 and 2 |

WinUI offers no way to raise a pointer event from code, so the hover check calls the callback the page attached; it does not prove the underline under a real mouse.

### Scaled displays (125% and 150%)

The development machine is at 100%, so no capture shows this. Reviewed against the rule: no second measure of a child at exactly its desired width, at least 1 px of tolerance in fit and wrap comparisons, no wrapping panel nested for a strip that must stay on one line, no zero-padding rounded button with text.

- Fixed: three buttons held text with zero padding and the default corner radius of 4: the task-name link on the list (the hover-underlined one), the breadcrumb links, and the history row's expand button. Each now has padding 4 with an equal negative margin (label position unchanged) and radius 4; the fixture asserts it for the task link and the breadcrumb (no result history row exists in the fixture, so the expand button is not asserted). The fixture's overflow check allows a box to reach past its container by exactly its negative margin.
- Fixed: the due-samples date columns' minimum width carries 2 px of tolerance over the measured word; the `Edit Schedule` column no longer relies on an `Auto` column for a wrapping label, and its heading is one line.
- Schedule editor wrapping rows (`Interval`, `Weekly`, Max runtime) use `DashboardWrapPanel`; they are measured at their column's width, never at their own desired width, and the interval box with its unit and the runtime box with its unit are already horizontal stack panels. The wrapping buttons keep side padding 12 against radius 10 (the rounded corners only reach x below 10, so the label is clear of them); the fixture asserts the side padding. Vertical padding is 5 for a label that may wrap: not asserted against the radius for the same reason.
- `DashboardWrapPanel` (shared, not edited here): it does not measure a child a second time (`DashboardFlowLayout.cs:32` measures once), but it decides wrapping with an exact comparison and no tolerance (`DashboardFlowLayout.cs:34`, `left + Spacing + size.Width > width`), and `DashboardWidgetHeader` does the same (`DashboardFlowLayout.cs:80`, `longest + 8 + actionWidth > width`). A parent that measures the panel at exactly its own rounded desired width would wrap a row that just fits. No such parent exists on these pages; it applies wherever the panel is placed in an auto-sized slot. `LogsWrapPanel` is covered by #246.

## Second review follow-up (two P2: a table wider than its panel loses its last column)

Both tables had column minimums that could add to more than the panel: due samples below about 500 px of page width, or at a larger text size (item 160, two date columns, attempts); history with a "Completed with errors" badge at 560 to 640 px, or at a larger text size (three content-sized columns and Error at 120). A Grid in that state is arranged wider than its panel and the last column is cut off, with no scroller to reach it. Beta's shared columns always fitted.

- Due samples (`FitDueSamples`, `AdminScheduledTasksPage.Detail.cs`): on every change of the panel's width the columns take the first arrangement that fits: one-line dates (from a page width of 768 px only), wrapping dates no narrower than their longest word, then beta's 2:1:1:1 shares with every header and cell wrapping. The widths of the content-sized columns are read from the laid-out table, so they follow the text size.
- History (`FitHistory`): content-sized Started, Duration and Status with Error at 120 or more while that fits; otherwise beta's shares (1.4, 0.8, content-sized Status, 1.6) with wrapping headers. In that arrangement a header can break inside a word and a Started date can be cut at its own cell's edge, as on beta; no column leaves the panel.
- Not changed: the item column's 160 px minimum does not grow with the text size (the table now steps down instead of clipping when it no longer fits).
- Guard (native, `CheckNarrowTableAsync`): each panel is held at 360 px; the table is no wider than the panel, the last header cell is inside it and wider than its padding, there is no `ScrollViewer`, and the table fits again at the full width. Written without a build: not run when this was recorded. A text size above 100% is still not exercised.

## Results

### Review follow-up run

- Focused unit run `tasksfix-focused-r3` (3 earlier labels: `-r1` a compile error in my new test file, `-r2` one wrong length in my own new assertion): 308/308.
- Final: `tasksfix-final-r1`, `-Stage final`, source `c9b67356b`: focused 308/308 and full 11,273/11,273 unit cases, zero failures; native PASS lines, zero FAIL, each with its completion line: admin-configure-scheduled-tasks 179, admin-configure-maintenance 50, admin-configure-general 188, admin-manage-foundation 36, admin-monitor 1,071. Payload and source seals verified.
- Stability: `tasksfix-stab-r1`, `tasksfix-stab-r2` (`-Stage native`, same build): admin-configure-scheduled-tasks 179 PASS, 0 FAIL each.
- Before the final run, `tasksfix-native-r1` (`-Stage publish-native`) failed only the overflow check for the first breadcrumb (its box reaches 4 px past the container by design: negative margin); the check was corrected to allow exactly that, in `c9b67356b`.
- No `0xC000027B` host exit and none of the known intermittent unit failures appeared in these runs. The groups known to crash on beta were not included.
- Captures were written at 1440, 1024, 800, 640 and 560 for the task page; none were opened in this pass. No visual or pixel parity is claimed.
- Not established: link underline under a real pointer; the contrast-theme badge colors; non-English wording; live-server interoperability; behavior at 125% and 150% (not run, the machine is at 100%); a due-samples table narrower than about 500 px of page width (the columns' minimums add to that; the official table would scroll there, this one clipped in this run and steps down since the second review follow-up); the history row's expand button corners.
- A test that fails without each fix was written for findings 1, 2, 3, 5, 6a and 8; they were not run against the reverted source.

### First pass (superseded where it differs from the follow-up above)

- Baseline (unchanged beta `beeb2b449`): `tasksref-baseline-r1`, `-Stage publish-native`, group `admin-configure-scheduled-tasks`: 55 PASS lines including "native regression run completed.", 0 FAIL, exit 0.
- Final: `tasksref-final-r1`, `-Stage final`, source `49b79d50d`: focused 218/218 and full 11,226/11,226 unit cases, zero failures; native PASS lines, zero FAIL, each with its completion line:
  admin-configure-scheduled-tasks 127, admin-configure-maintenance 50, admin-configure-general 188, admin-manage-foundation 36, admin-monitor 1,071. Payload and source seals verified.
- Stability: `tasksref-stab-r1` and `tasksref-stab-r2` (`-Stage native`, same build): admin-configure-scheduled-tasks 127 PASS, 0 FAIL each.
- No `0xC000027B` host exit and none of the known intermittent unit failures appeared in these runs.
- Captures opened (from `tasksref-cap-r4` to `-r7`, the same page source as the final build; none of the final run's own captures were opened):
  schedule editor 560 and 800, conflict review 560, task page with history 1440, 1024 and 560 (before and after each fix), list 560. The list at 1440, 1024 and 800, the editor at 1440 and 1024, and the
  review state at 1440, 1024 and 800 were captured and checked by the overflow assertions but not opened. These were compared with the official layout as read from source (class lists), not with a
  live WebUI; no visual or pixel parity is claimed.
- Request bodies and URLs: unchanged. The only wire change is reading three optional fields on refresh samples (`series_title`, `season_number`, `episode_number`).
- Not established: link underline under a real pointer (the fixture drives the same handler the pointer events call; the Libraries link in the `scan_libraries` note cannot be checked in the fixture because Libraries is
  unregistered there); the contrast-theme badge colors (a fixture check runs only when the host has high contrast on); non-English wording (the new `web.admin.*` task text is English only, as the rest of this page's table);
  live-server interoperability.
- Test change to an existing check: "detail shows the server category badge" asserted the raw key `library`; it now asserts the catalog label `Library` and the absence of the key, because the official client changed that behavior (#4677).
