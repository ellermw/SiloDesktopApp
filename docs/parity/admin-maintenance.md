# Configure › Server › Maintenance (`/admin/maintenance`)

Status: **In progress — bounded verification complete; Beta pipeline acceptance remains.**
The page-specific native fixture and full units pass. Two neighboring native groups remain
unverified after failures described below; this is not a complete visual-parity certification.

- Desktop base: Beta `19a66e547a78bad9fdc88925c3deae308e05d4ba` (fast-forwarded in the isolated
  worktree from `3350f1b9`; no local edits were discarded). Version stamps are pipeline-owned and
  unchanged.
- Official reference: Silo-Server/silo-server `92ee051878c2a791940ec9598e2bdd0f04890b3e`, read as
  exact `git show` objects from the existing `D:/SiloPlayer-work/server-upstream-nov14` fetch.
- Separate from the Backups/uploads/restore candidate and every other Admin section. It changes no
  Task API, no Storage/Scheduled Tasks timers and no other route gate.

## Official scope

`web/src/pages/AdminMaintenance.tsx` renders exactly two parts under the page header
(`web.admin.maintenance.page.title|subtitle`):

1. `components/AdminCatalogMaintenance.tsx` — Catalog Import & Export header (Start Export,
   Import Catalog dialog), Recent Catalog Imports (10 per page) and Recent Catalog Exports
   (10 per page) with Download / Create seven-day link / Copy URL.
2. `components/AdminJobHistory.tsx` — read-only retained jobs of every kind (30 per page).

No restart, reboot, repair, updater, cancel, delete or run command exists on this page.

## Wire contracts (verified at 92ee0518)

| Operation | Contract |
| --- | --- |
| `GET /api/v2/admin/jobs?kind=&limit=&cursor=` | ActingAdmin; limit 1–200 (default 20); opaque cursor scoped to `kind`; `AdminTaskJob` rows; `page.has_more/next_cursor`. Page sizes: exports/imports 10, history 30 (`useCatalogExportJobs`, `useCatalogImportJobs`, `useAllAdminJobs`). |
| `GET /api/v2/admin/catalog/import-sources`, `/local-import-sources` | ActingAdmin; limit 1–200 default 50 (WebUI sends 50); `{key,size_bytes,last_modified?}`; separate cursor scopes per operation. |
| `POST /api/v2/admin/catalog/export-jobs` `{}` | 202 `AdminTaskJob`, `Location: /api/v2/admin/jobs/{id}`, `Retry-After: 5`. Not completion. 409 `conflict` with `Location` of the active catalog job when one is queued/running. |
| `POST /api/v2/admin/catalog/import-jobs` | Body: exactly one of `local_path|export_job_id|artifact_key|remote_url`, `conflict_mode` (`skip_existing|overwrite_existing`), `path_rewrites` (required array of `{from,to}`). 202 like export. |
| `POST /api/v2/admin/catalog/import` | Same body; 200 `ImportResult` means the catalog transaction committed. Server summary: “do not automatically retry a lost response.” |
| `POST /api/v2/admin/catalog/export-jobs/{id}/publish` | 200 `{job_id,url,expires_at?}`; seven-day signed URL; an existing URL is returned without renewing its expiry. 409 when the export is not a completed artifact or storage cannot presign. |

All five commands are `ClassActingAdmin`, `DemoRestricted`, `RetrySafetyNonRetryable`; none
uses ETag/If-Match. WebUI sends them with `retryAuthentication: false`.

Actual error mapping (`catalogTransferProblem`, `catalogImportSourceProblem`, `serviceProblem`):

- 422 `validation_failed`: not exactly one source, a rewrite with an empty side, invalid/unsupported
  bundle or conflict mode, and **path rewrites required** (`errors[]` with
  `location: body.path_rewrites`, `code: path_rewrite_required`, one per unmatched root).
- 400 `bad_request`: the selected source could not be loaded (local path missing/not `.json.gz`,
  remote download failure or over the size limit, export job/artifact not found as a source).
- 503 `dependency_unavailable`/service unavailable: catalog storage or jobs unavailable.
- 409 `conflict`: active catalog job (with `Location`), publish of a non-completed export, or
  `presign_unsupported` (still type `conflict`).
- 500: generic internal error with no detail.

Job projection: v2 `state` is `queued|running|canceling|succeeded|failed|canceled`; WebUI maps
`succeeded→completed`, `canceled→cancelled`, `canceling→running` with the message
“Cancellation requested”. `failure.detail` is a fixed safe sentence. Catalog rows carry
`library_ids`, `source_label`, `catalog_result` (completed only), `download_url`
(presigned or server-signed, self-authorizing), `public_url`, `public_link_supported`.
`image_cache_cleanup` keeps `library_result` when cancelled. `storage_transition` hides its
error message in WebUI; `template_bundle_apply` and future kinds have no result summary.

Polling/refetch: `useAdminTaskJobs(kind, limit, poll = false)` — this page never polls.
Query defaults: `refetchOnWindowFocus: false`, `refetchOnReconnect: true`, reads retried once
except authorization refusals, 404 and timeouts. Mutations: export → invalidate exports;
publish → invalidate exports; queued import → invalidate imports; synchronous import →
invalidate libraries only (nothing on this page). Export 409 also invalidates exports.

## Native design

- `AdminMaintenanceApi` (Core/Api) — domain-prefixed; reuses the bounded Admin library transport
  through one thin `SiloApiClient.AdminMaintenance.cs` partial that additionally exposes the
  response status and `Location` (and a 409 conflict's `Location` via
  `AdminCatalogConflictException : ApiException`). No new deadline, size bound or retry policy.
  Writes never refresh tokens and are sent once.
- `AdminCatalogImportDraft` (Core) — four mutually exclusive sources, execution, conflict mode,
  rewrite rows with stable ids. The wire body contains only the selected source. Wholly blank
  rewrite rows are dropped; partially filled rows are a field error (native safety addition:
  WebUI silently drops them). Server paths are never validated with Windows path APIs.
- `AdminMaintenanceController` (Core) — owner/generation per list chain; refresh coalescing,
  append-vs-refresh exclusion, cursor cycle/duplicate checks across pages, late-result drop,
  authority retirement clears everything. 202 receipts must name the job in `Location`; any
  other or missing acknowledgement, 408, 5xx, transport failure or deadline is
  “possibly accepted”: never resent, the matching list is refreshed. A 409 `Location` is read
  and only that job's catalog list is refreshed. An ambiguous import (either execution) leaves
  an explicit unknown-outcome block in the singleton API, scoped to authority and epoch. It
  survives dialog close/reopen, page departure/recreation and failed refreshes. Departure
  cancellation does not settle an on-wire import. Acknowledgement cannot clear an in-flight
  attempt; after it settles, the administrator must acknowledge checking the catalog before
  another import is allowed. A new authority epoch does not inherit the former account's block.
- `AdminMaintenancePresentation` (Core) — WebUI formatters: progress label/percent, kind
  labels, targets, per-kind result summaries (refresh, delete, cleanup incl. cancelled totals,
  export, import), unknown kinds shown generically. Remote-URL source labels are shown without
  user info, query or fragment.
- `AdminMaintenancePage` (App) — page header, catalog header, Import dialog (all official
  fields and states), imports list, exports list, Job History, `JobPageControls`
  (Restart / Load older). Download opens `download_url` (resolved against the connected
  server when relative) in the default browser through an injectable launcher; Copy URL copies
  `public_url` through an injectable clipboard. Neither attaches account credentials, and
  neither URL is logged or rendered. Callbacks act only for the current owner lease.
- Registration: `AdminConfigureServices.Add` (same entry as App and fixtures); `["maintenance"]`
  removed from `UnsupportedPatterns`. `/admin/maintenance/x` keeps resolving through the
  existing descendant rule to the Dashboard catch-all (verified in `AdminRouteCatalog.Resolve`;
  no new pattern added).

## Documented differences

- No automatic refetch on reconnect (native admin has no reconnect signal); Refresh, Restart
  and mutation reconciliation cover it.
- Partial rewrite rows block submission instead of being dropped.
- Unknown-outcome block and explicit acknowledgement after an ambiguous import (safety; the
  server forbids automatic retry and WebUI shows only a toast).
- Server/provider problem `detail` text is never shown; fixed localized categories are used.
  All command 5xx responses conservatively remain outcome-unknown, including a server 503
  storage refusal: an intermediary's matching status cannot prove the command was rejected.
  This can require acknowledgement even when the server definitely refused the request.
  `path_rewrite_required` details are rendered as the server's root list only after
  extracting the root path from the fixed server sentence.
- Remote-URL source labels are redacted to scheme, host and path.
- A catalog conflict refreshes the list of the job named by Location, rather than only the
  initiating command's list. The captured job is not shown as a separate conflict card.
- Job-list controls are rebuilt during loading, so focus retention and Narrator require further
  verification. A Restart race between refresh passes can produce an extra read, not a write.
- Copy URL for a published link uses the native clipboard; Download launches the default
  browser (WebUI opens a new tab).

## Verification evidence (UTC November 17, 2026)

Evidence root: `.codex-tmp/admin-maintenance-verification/` in the task worktree. All runs used
one nonblocking `SiloOct18Build` acquisition and released in `finally`; the installed player,
Requests payloads and other lane outputs were untouched. All source seals matched at release.

- `maintenance-departure-red-r2`: both page-departure unknown-import cases failed before the fix.
- `maintenance-ack-red-r3`: departure cases passed; all three incomplete result bodies failed
  before required result fields were added. Both regression families pass in the final full suite.
- `maintenance-full-r4` (00:18:49–00:23:51): nonincremental unit build, source binding, source-root
  proof 1/1, focused 323/323 and full 7,949/7,949 passed. App build, Release publish and native host
  build passed; app/host assembly hashes, engine hash and published Maintenance XBF were checked.
  The native fixture initially timed out editing a rewrite before its queued source-panel render.
- `maintenance-native-r6` and `maintenance-native-r7`: only the Maintenance fixture readiness waits
  changed from r4; retained product bytes were verified before host-only rebuilds. Maintenance
  passed 43 checks twice. R6 foundation completed its assertions but crashed during COM-wrapper
  finalization (0xC0000005); the unchanged r7 run passed foundation 36, General 188, Email 297,
  Security 374 and Network 346 checks. No environmental or product root cause is established.
- R7 stopped at Downloads fixture line 316 (`uncheck download.server_bandwidth_mbps`). Its earlier
  finite 250 draft is remembered by the unchanged page, while this check expects an empty value.
  Neither Downloads production nor fixture source differs from the base/current fetched Beta.
- `maintenance-libraries-r8` (00:40:25–00:40:58): the isolated Libraries host correctly reports
  Maintenance unavailable, but active-scans/Show-all assertions failed before line 368 timed out.
  Libraries source is unchanged. No clean-baseline runtime was performed for either neighbor,
  so neither is claimed proven pre-existing or passed; both remain for pipeline QA.
- The official source was freshly fetched at `94797c3e2e39ef12bf2796fa08df12e3b3525b3e`; only
  unrelated metadata/CI changes occurred after the implementation reference.

## Unverified limits

Visual parity, live-server behaviour, real clipboard/browser/OS dialogs, IME, Narrator and DPI
coverage remain unverified. No installer was built or run; versioning and packaging belong
to the Beta pipeline. The two neighboring native failures above remain outstanding.
