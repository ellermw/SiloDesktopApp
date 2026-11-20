# Configure > Server > Scheduled Tasks (C8)

Status: **Verified for separate Beta submission** on deliberate Beta `6936ae5c956360358c454cb8af478f47e2dbd0ba` (pipeline-owned `1.2.1168-beta.1`). The list and complete detail/schedule/history/metrics workflows are implemented and verified. Original source-only authoring did not establish observed test-first development.

## Baselines

- Desktop source base: Beta `6936ae5c956360358c454cb8af478f47e2dbd0ba`
  (fast-forwarded from the worktree's inherited `3350f1b90`; clean, no local
  edits existed). Version stamps are untouched and stay pipeline-owned.
- Official Silo Server/WebUI: `92ee051878c2a791940ec9598e2bdd0f04890b3e`
  (read as exact `git show` objects from the existing upstream checkout; its
  working HEAD was not used). The earlier contract map at `5b3dcaa2f` still
  applies; `92ee` changed only server encryption diagnostics.
- Official files mirrored: `web/src/pages/AdminTasks.tsx`,
  `web/src/pages/AdminTaskDetail.tsx`, `web/src/pages/AdminTaskTriggerForm.tsx`,
  `web/src/hooks/queries/admin/tasks.ts`, `web/src/lib/taskTrigger.ts`,
  `web/src/lib/taskText.ts`, `web/src/lib/taskProgress.ts`,
  `web/src/components/admin/TaskStatusBadge.tsx`, the `tasks` branches of
  `web/src/components/RealtimeEventsProvider.tsx`,
  `internal/apiv2/admin_tasks.go`, `internal/apiv2/admin_task_metrics.go`,
  `docs/admin-tasks-api.md`, the `contracts/api/v2/fixtures/admin_task*.json`
  fixtures and the English catalog (`web/src/locales/en.json`, decompiled from
  its message AST to ICU text without changing wording).
- `web/src/api/v2/adminTasks.ts` is the retained-job adapter and is **not** the
  scheduled-task API; it is not used.

## Plan / spec

### Seams (files)

| Concern | File |
| --- | --- |
| Wire models | `src/SiloPlayer.Core/Models/Admin/AdminTaskModels.cs` |
| Domain API (8 operations) | `src/SiloPlayer.Core/Api/AdminTasksApi.cs` |
| Text, catalog and formatting | `src/SiloPlayer.Core/Services/Admin/AdminTasksText.cs` |
| Schedule draft rules | `src/SiloPlayer.Core/Services/Admin/AdminTaskScheduleDraft.cs` |
| List controller | `src/SiloPlayer.Core/Services/Admin/AdminTasksController.cs` |
| Detail controller | `src/SiloPlayer.Core/Services/Admin/AdminTaskDetailController.cs` |
| Shared realtime/clock seam | `src/SiloPlayer.Core/Services/Admin/AdminTasksRealtime.cs` |
| WinUI page (list + detail) | `src/SiloPlayer/Views/Admin/AdminScheduledTasksPage{.xaml,.cs,.List.cs,.Detail.cs,.Schedule.cs}` |
| Production registration | `src/SiloPlayer/Services/AdminConfigureServices.cs` (3 lines) |
| Installer XBF allowlist | `installer/build.ps1` (1 line) |
| Approved Admin sources | `docs/parity/admin-monitor-surfaces.json` |

### Operations (official `registerAdminTasks`)

1. `GET /api/v2/admin/tasks` (default list; hidden tasks omitted)
2. `GET /api/v2/admin/tasks/{key}` (any registered task, hidden included)
3. `POST /api/v2/admin/tasks/{key}/run` (non-retryable)
4. `POST /api/v2/admin/tasks/{key}/cancel` (non-retryable)
5. `GET /api/v2/admin/tasks/{key}/metrics` (`refresh_metadata` only)
6. `GET /api/v2/admin/tasks/{key}/triggers` (strong caller-bound ETag)
7. `PUT /api/v2/admin/tasks/{key}/triggers` (`If-Match` required, guarded)
8. `GET /api/v2/admin/tasks/{key}/history` (signed cursor, limit 20)

Every call is bound to a captured admin lease, sent once with
`allowTokenRefresh: false`, has a finite deadline that covers the bounded body
read, and validates the response before use. Keys are validated
(`[A-Za-z0-9_.:-]`, 1–256, no leading `.`, which admits
`plugin:<installation>:<capability>`) and escaped as one path segment; unsafe
keys are rejected before any request. Problem text is replaced by a safe code.
Run/cancel/PUT failures that may have reached the server (408, 5xx, transport,
malformed acknowledgement, foreign key in acknowledgement) become an unknown
outcome: the client reconciles with one read and never reissues the command.

### Behaviour

- List: groups `library`, `metadata`, `system` (non-manual) then a separate
  "On demand" group with its description; unknown categories are not grouped
  (as upstream). Row summary: short trigger list or "Manual only"/"No
  schedule", last run (relative, floored) with duration, "· Never run",
  overdue warning or "· Next: in Nm/Nh/Nd". Running/cancelling progress bar
  with "Running"/"Cancelling..." and percent; status badge of the last run
  when idle (with error tooltip, "Completed with errors" for
  `contribute_markers` with failures). Run Now → Starting... (shown at least
  1 s) → Stop → Stopping.... `refresh_metadata` shows inline queue metrics
  and non-zero reason chips.
- Detail: breadcrumb Admin › Scheduled Tasks › name, title/description,
  category badge, Run Now/Cancel; a 404 always replaces a cached task with
  "Task not found" and an "All tasks" link; other first-read failures show
  "Couldn't load this task" with Retry; transient refresh failures keep the
  last good task. `scan_libraries` note links to Libraries only when that
  route is registered (otherwise plain text). Running panel. Queue health for
  `refresh_metadata`: five counters, reason breakdown, attempt buckets,
  recent errors (server-safe text only), due samples.
- Schedule: read-only list or "No triggers configured."; manual-only tasks show
  their sentence and no editor. Edit Schedule captures `GET …/triggers` and
  its strong ETag; weak/missing/`*` ETags refuse editing with
  "Schedule revision unavailable. Reload before editing." Rows have stable
  identities; Add Trigger defaults to interval 3 600 000 ms (1 hour); switching
  type fills interval 60 000 ms, time 00:00 and Sunday; interval text keeps the
  typed unit; empty/zero/over-limit intervals and malformed max runtime block
  Save. Any save failure (412, unknown outcome, rejection) keeps the draft and
  original ETag and requires "Review current schedule" then "Use this revision
  and keep my draft" before another save. Writes are never replayed.
- History: first page, "Load older executions" by cursor, "History could not
  load. Restart history"; duplicate identities, foreign task keys, oversized
  pages, a repeated cursor or `has_more` without a cursor are rejected.
  Expanding a row shows only the typed marker-contribution result.
- Realtime: the page leases the `tasks` channel. `task.updated` frames are
  invalidation signals only (they omit `started_at`): the list refetches; the
  detail refetches its task, and on `idle` also history (restart) and metrics.
  Snapshots (subscribe/reconnect) refetch everything. Foreground invalidations
  coalesce behind an in-flight read into one trailing read. The 30 s
  metrics/relative-time tick runs only while the page and window are active
  and never queues behind an in-flight read. There is no list polling.
- Lifetime: `AdminAuthorityLifetime` owner per page instance; suspend/revoke
  cancels reads and commands, clears tasks, cursors, schedule ETags and draft,
  drops late results and stale events, stops timers and closes dialogs. A
  dirty schedule draft is protected by `AdminLeaveGuard`.

### Test coverage

- `tests/SiloPlayer.Tests/AdminTasksApiTests.cs` — wire paths/escaping, unsafe
  key rejection, fixture parsing, ETag capture/strength, `If-Match` on PUT,
  412 conflict, unknown outcomes, no auth replay, history cursor/identity
  validation, metrics restriction and safe error text.
- `tests/SiloPlayer.Tests/AdminTaskScheduleDraftTests.cs` — stable ids, add
  default, type defaults, unit preservation, invalid interval/runtime, wire
  output, dirty tracking.
- `tests/SiloPlayer.Tests/AdminTasksTextTests.cs` — trigger text (long/short),
  durations, next-run/overdue, progress clamp, task-name catalog lookup, status.
- `tests/SiloPlayer.Tests/AdminTasksControllerTests.cs` — grouping, event
  invalidation and coalescing, active-only ticks, run/stop feedback, unknown
  outcome reconciliation, retirement clearing.
- `tests/SiloPlayer.Tests/AdminTaskDetailControllerTests.cs` — 404 overriding
  cache, Retry, 412 review/adoption flow, unknown outcome, manual-only,
  history paging/restart, metrics, suspension clearing.
- `tests/SiloPlayer.Tests/AdminConfigureNavigationTests.cs` — production
  registration and hidden search-task detail resolution.
- `tests/SiloPlayer.NativeRegressionTests/AdminScheduledTasksNativeFixture.cs`
  (`SILO_NATIVE_TEST_ONLY=admin-configure-scheduled-tasks`) — production
  registration, shell, Frame, list/detail rendering, run/stop, schedule
  412 review, leave guard dialog, hidden search task detail, retirement, and
  Closed/Unloaded barriers before restoring the service provider.

## Differences and limits

- Native-only list read failure shows an alert with Retry (WebUI renders
  nothing); detail behaviour matches WebUI.
- Metrics are requested only when the list contains `refresh_metadata`
  (WebUI also asks, receiving 404, when it is absent).
- Detail history refresh after an `idle` event restarts at the first page
  rather than refetching every loaded page.
- Daily/weekly time uses a native 24-hour `TimePicker`.
- No live-server interoperability, live-WebUI visual comparison, installer, physical input, every-DPI, Narrator or comprehensive visual-parity certification. New task copy is English-only.

## Final verification

- `tasks-full-r5`: nonincremental unit build and source/PDB binding; source proof **1/1**, focused **250/250**, full **7724/7724**, zero failures/skips. App build/publish passed and the Scheduled Tasks XBF resource is present/allowlisted. Original receipt SHA256 `2CEFD7747BB8D2B52A4B5E37375A454A88228B2D89501D8B9A69463E62C82D63` remains failed at its original native-host fixture compile, not relabelled green.
- Final `tasks-native-r8` retained the exact R5 unit/product source and all **667** published app files. Only `AdminScheduledTasksNativeFixture.cs` changed afterward: corrected its raw JSON interpolation/import and awaited actual loaded/enabled/visible buttons before automation invocation. All **44** original Check lines remain unchanged. The final native host was rebuilt nonincrementally and unit/app/host PDB bindings passed.
- All six groups exited **0**, with one completion each and no failure markers or raw exceptions: Scheduled Tasks **45**, Monitor **113**, Manage foundation **36**, Playback audit **76**, Subtitles **64**, Network access **346** PASS markers (including completions).
- Actual final lease **2026-11-16T23:03:21.3835771Z** to **2026-11-16T23:06:18.3669255Z**. Receipt SHA256 `714164EA15DC5253378D2754D3E08FC68C32C7CAA67965909D5C2B91DC3D8260`.
- Released audit qualified **2614/2614** source entries, **667/667** app payload files and all **9** exact final inline/disk numeric captures with contained cleanup, plus the seven retained successful unit/app stages and all TRX counters. The sole source delta from R5 is the rebuilt native fixture. Evidence: `.codex-tmp/admin-tasks-verification/tasks-native-r8/root-released-audit.json`.

## Review and observed repairs

The bounded independent source review found no P0/P1 and one P2: a failed detail read left Retry enabled during its next read. A held-response unit regression was observed failing before the state predicate was corrected; it now passes, the unavailable panel stays visible, and Retry disables while fetching. No second review loop is claimed.

The initial routing tests also exposed a legacy blocklist: production registration alone did not unblock `/admin/tasks` and its descendants. Those two unsupported patterns were removed; their old unsupported test moved into positive route coverage. WinUI automation namespace imports and the fixture JSON/navigation imports were compile corrections. The first native runtime reached 412 Review/Adopt but invoked a button before its queued render enabled it; the fixture now waits for actual control readiness, without changing assertions or bypassing product state. Earlier failed runs remain preserved.

Only delivery documentation changed after final acceptance. The Scheduled Tasks group produces no screenshots: its native checks establish controls/data/actions/barriers, not complete visual parity. Fresh official main was fetched directly and remains `92ee051878c2a791940ec9598e2bdd0f04890b3e`.

This is one C8-only PR, independent of Libraries181, Users194, Search198, Storage199 and Dashboard200. Registering Scheduled Tasks activates Search198's already-preserved hidden task destinations when combined; Search itself is not edited. Pipeline owns reconciliation, review, QA and stamping. Installed player/settings and retained Requests output remained untouched.
