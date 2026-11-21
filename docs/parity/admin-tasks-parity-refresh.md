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
| `0caa8b1ae` #4687 | Item types on the refresh task page map `movie`, `series`, `season`, `episode`, `ebook`, `audiobook`, `manga` to `web.search.type*`; an unknown type shows as sent; an empty type shows `web.admin.tasks.detail.itemFallback`. | **Fix.** `AdminTasksText.ItemType`. The seven `web.search.type*` keys are in the shared interface catalog, so they already follow the app language. |
| `0d649bd32` #4688 | Failed step names in run history go through `taskText(step, "name")` (catalog entry keyed by the step key, else the name sent). | **Fix.** `HistoryError` uses the same lookup as task names (`AdminTasksText.TaskText`). |
| `ccd8b7390` #4749 | The server adds `series_title`, `season_number`, `episode_number` to refresh samples and sends the target's own title; the client composes the title (`sampleSeriesEpisode`, `sampleEpisode`, `sampleTitle`, season and episode codes) and shows the fixed `refreshFailed` sentence for a recent error; a failed run's badge tooltip and history cell show `taskStatus.failedDetails`; recent-error titles clamp to two lines and due-sample titles wrap. | **Fix.** Three optional fields read and bounded by the API reader, `AdminTasksText.SampleTitle`, catalog sentences, wrapping titles. Request URLs and bodies do not change (the metrics read is the same `GET`). |
| `927040075` #4627 | The task link on a row and the Libraries link in the `scan_libraries` note underline on hover. | **Fix.** Both links draw their underline only while the pointer is over them (`AdminTasksHoverUnderline`). Keyboard focus is unchanged (the official rule is hover only). |
| `8e0441421` #4819, `43f488ecb` #4736, `58aacf65b` #4722, `190fd8dda` #4742, `e144acf20` #4891, `3d9634e05` #4920, `88680aee3` #4839 | Trigger fields stay inside their column; the type, weekday and time fields grow to their text but stop at the column; the whole time and the whole "None" placeholder show; the Max runtime label wraps and its box stays with its unit. | **Checked at 1440, 1024, 800 and 560 wide, fixed where it occurred** (see the layout table below). The time is a `TimePicker`, so #4722's browser-specific clipping does not apply. |
| `5a3156b9d` #4868, `379be8c75` #4904, `7746c542a` #4936 | The Add Trigger label wraps, a long word stays inside the button, and the schedule panel's buttons (Edit Schedule, Add Trigger, Review, Use this revision) wrap instead of running off screen. | **Checked and fixed where it occurred** (see the layout table). |
| `f124e246f` #4885 | Splits the realtime provider's connection state from its subscription context. | **Not applicable.** An internal React refactor; the native page keeps its own tasks channel lease, and the frames are unchanged. |
| `7ea46c825` #3551, `35b0ed7f0` #3611 | A plugin task without a saved schedule reports its default triggers at revision 0 under a validator that also changes when the default changes; cancel returns the task; a stale `PUT` against an unsaved schedule answers 412 with the current validator (or none). | **Already right.** The native editor treats the strong `ETag` as opaque, captures it when editing starts, refuses editing when it is weak, missing or `*`, keeps the draft on a 412, and re-reads before another save. A 412 without a validator is handled by the same review path (the review re-reads the schedule). No request changed. |
| `f18678fda` #4648, `165ab2b06` #4611 and the other commits in the range that touch the realtime provider, plugin lifecycle or other pages | Do not change `AdminTasks*`, the trigger form, the task models or the task text. | **Not applicable.** |
| `f6ed24869` #4700, `160a4425e` #4704 | Destructive buttons and badges use `--destructive-fill` (`#dc2626`) under `--destructive-foreground`; delete and disconnect confirms use the destructive style. | **Fix for the failed-run badge** (the official `TaskStatusBadge` uses the `destructive` badge variant for a failed run and for "Completed with errors"): a page-local solid `#DC2626` fill with white text, system colors in a contrast theme. **No task action has a destructive confirmation** (Run Now, Stop and Cancel send at once on both sides; the only dialog is the shared leave-with-unsaved-schedule discard dialog, which is not edited here). The Stop button is an outline button with `--destructive` text and border, unchanged by #4700. The shared destructive style and `DarkTheme.xaml` are not edited; pull request #244 owns them. |
| `en.json` | New keys `web.admin.common.taskStatus.failedDetails`, `web.admin.tasks.detail.refreshFailed`, `sampleEpisode`, `sampleSeriesEpisode`, `sampleTitle`, `backfill_attachment_bitrate.name` / `.description`, and `web.admin.tasks.detail.backupSchedule`. | **Added to the page's text table**, except `backupSchedule`: no official page uses it at `2972dc63c` (no reference outside the catalog), so it is left out rather than added unused. The mechanical comparison of the 257 existing table keys with the official catalog finds zero differences in wording. |

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

Filled in by the capture run; see "Results" below.

## Results

Filled in after the verification runs.
