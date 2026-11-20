# Configure > Server > Backups — parity record

Status: **In progress** for pipeline and visual acceptance. The source candidate has passed
local unit, build and scoped native checks; this is not a full visual-parity certification.
An independent source-only review completed before the corrections recorded below.

- Official reference: Silo Server/WebUI `92ee051878c2a791940ec9598e2bdd0f04890b3e`
  (`web/src/pages/AdminBackups.tsx`, `AdminBackupRestore.tsx`, `pages/admin-backups/*`,
  `hooks/queries/admin/backup*.ts`, `api/v2/adminBackup*.ts`, `lib/profileDataTypes.ts`,
  `internal/apiv2/admin_backup*.go`, `internal/backup/{downloads,upload,settings}.go`,
  `internal/config/admin_settings.go`, `contracts/api/v2/openapi.json`, `web/src/locales/en.json`).
- Desktop base: Beta `542dec13c7d45c7520116c559166023ecb4ee987`; version stays pipeline-owned.
- Official main rechecked directly at `94797c3e2e39ef12bf2796fa08df12e3b3525b3e`: changes
  since the implementation pin affect metadata/CI, not these Backups contracts.
- Desktop `origin/beta` at final submission preparation: `3d9b642242b0539684fcb055b00690dbd242a93e`.
  This candidate keeps its deliberately selected, tested base; the pipeline owns reconciliation.
- Contract notes and design: [plan](../plans/2026-11-16-admin-backups.md).

## Routes

| Route | Native | Notes |
| --- | --- | --- |
| `/admin/backups` (`?backup=`) | `AdminBackupsPage` list view | Linked backup is selected and scrolled to. |
| `/admin/backups/:id/restore` (`?job=`) | `AdminBackupsPage` restore view | Job address replaced in place (`TryReplace`). |
| `/admin/backups/uploads/:uploadId/restore` | same | Upload previews always need consent. |

The three patterns were removed from `AdminRouteCatalog.UnsupportedPatterns`; Maintenance and
Plugins stay unsupported. The old unsupported assertion moved into positive routing tests.

## Implemented surfaces

- Header: Back up now (with the official blocker sentence), Upload (only when the server reports
  `upload`), status line, active operation with progress, last-run problem, warnings with the
  key confirmation action.
- Saved backups table: kind, size, version, status (with warning details: omitted accounts,
  missing files, plain-text copy), Restore… (selective restore capability, other-key reason),
  Download (signed links dialog, restore set for encrypted backups), confirmed delete, Show all.
- Uploaded backups list: restore, check again (idempotent completion), confirmed delete.
- Settings: schedule and content groups through the shared guarded settings API (strong ETag,
  412 keeps the draft, Reload rebases), location group (default/S3 radio, storage link,
  bucket setup), full-restore command with copy, save bar and leave guard.
- Dialogs: S3 setup (test, key box, save after acknowledging the key; secret write-only and
  cleared on close), Confirm key, Upload (sniff, parts, check, failures with resume), Download.
- Restore: step indicator, choose (sections, difference bar, all/filter, library groups,
  blocked groups, same items, accounts with profiles, data types, never-restored settings,
  omitted accounts, plan summary), review (plan, left out, refusals, unverified consent),
  run (progress, stop with confirmation), report (items, accounts, data, recovery point,
  restart keys, JSON report save, choose again).

## Safety invariants

- GET reads may refresh an expired access token; non-idempotent commands never refresh or replay.
  Unknown backup outcomes require an explicit refresh. Restore uncertainty is retained in the
  singleton API per source and authority epoch, surviving page departure/replacement. A pending
  original request cannot be acknowledged; after it settles, a fresh preview and review are
  required. The old preview is cleared while reloading. Only the response's own `Location` job
  or a running job of this source is adopted; a foreign-source 409 leaves the chooser usable.
- Signed downloads use a separate unauthenticated transport, no redirects, exact-path or https
  presigned URLs only, bounded streaming, exact size and SHA-256 where recorded, atomic replace
  that keeps any existing file on failure, and one refresh of an expired link for the same object.
- Upload parts are bounded buffers; only the naturally idempotent part PUT may be re-authenticated
  and is retried at most three times for retryable failures; creating an upload is never resent.
- Known unfinished uploads are discarded before disposal using a page-independent lease of the
  same authority epoch. Page departure can delete that known upload without reviving retired
  credentials. Cancelled stream disposal does not escape the asynchronous upload handler.
- A per-attempt guard drops late download progress after completion, keeping the verified result.
- Polling: list 3 s/30 s, capabilities after 60 s, uploads 3 s while checking, jobs 2 s until
  terminal; paused when hidden or suspended; background ticks never queue; foreground refreshes
  coalesce; stale generations are dropped.

## Verification and corrections — November 16 local / November 17 UTC

Evidence remains under the task worktree's ignored
`.codex-tmp/admin-backups-verification`; generated binaries reuse
`D:/SiloPlayer-build/admin-backups-active`. Every runtime used one nonblocking
`SiloOct18Build` acquisition, fail-stop stages and release in finally.

| Evidence | Observed result |
| --- | --- |
| `backups-review-red-r4` | Six review regressions RED (0/6); source proof 1/1. |
| `backups-preview-red-r6` | Original six GREEN; stale-preview regression RED. |
| `backups-full-r7` | Nonincremental unit build and PDB/source binding; source proof 1/1, focused 226/226, full 7852/7852. Later app stage failed on missing UI imports; the receipt is not an all-stage PASS. |
| `backups-app-r11` | Nonincremental app/host builds, Release publish, source/PDB binding, host/app assembly identity, pinned libmpv and Backups XBF presence passed. Builds completed with warnings: app 516, host 45. |
| `backups-host-r12` | Own `admin-configure-backups` native: 30 PASS records including completion; known-upload departure DELETE exactly once. |
| `backups-foundation-r13` | `admin-manage-foundation`: 36 PASS records including completion; retained product hashes unchanged. |

All seven unit review/preview regressions are GREEN in r7. Reused unit evidence is guarded
by every source hash: the only later code changes are three UI namespace imports, the
download terminal-status guard, and the native fixture. No Core/unit source changed.
Native fixture fixes use reflection for existing internal picker seams, route identity and
loaded/enabled barriers, and await actual dialog Closed plus page/shell Unloaded before
restoring services/pickers. They do not weaken product assertions or expose public test APIs.

The native download assertion was RED in r9/r10: exact verified bytes were saved, but late
queued progress replaced the terminal message with "0 of 0 KB". R11/r12 prove the guard
keeps "Saved and checked against the backup's checksum." after queued progress drains. R11 then stopped on
a fixture readiness error; r12 corrected only that loaded/enabled barrier.

**Neighbor limit:** r12 fail-stopped at unchanged
`AdminDownloadSettingsNativeFixture.cs:316`, timing out after unchecking
`download.server_bandwidth_mbps` because the fixture expects empty after earlier finite
250 is remembered. Those product/fixture files are unchanged by this candidate, and the
fixture matches current Beta. No clean runtime baseline was run here, so this is not
certified as pre-existing and the Downloads group is not claimed passing. Foundation
was run separately after that stop. No Backups access violation was observed.

Final runtime release: `2026-11-17T01:25:12.2338806Z`. Storage measurement before it:
16.658 GiB under the owner-approved 50 GiB generated-output cap; C/D free 169.91/461.815 GiB.
No cleanup, installed player/updater operation, installer launch or release publication ran.

## Known gaps and limits

- Live-server behavior, visual comparison, DPI, IME and Narrator are unverified.
- Closing during upload creation before a server ID is known may leave an unidentified upload
  for the server's 24-hour sweep (also present in the official dialog); no guessed deletion or
  automatic create replay is attempted.
- OS file pickers and drag-and-drop are not automated; the fixture uses page seams confined to its
  own result directory.
- No playback-start signal exists in the app; `DeferBackground` is wired but defaults to never.
- Toasts combine the official title and description into one native message.
- Layout follows the official structure with native controls; pixel parity is unverified.
- Upload authority change closes the dialog (the WebUI can resume as whoever signs in again).
