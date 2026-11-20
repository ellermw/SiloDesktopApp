# Admin Manage > Downloads > Prepared Files

Source-only candidate prepared November 17, 2026 from Desktop Beta
`248aafd62ed65b868c0e7356926e4d83204649cd`, version `1.2.1181-beta.1` unchanged.
The exact official reference is Silo Server/WebUI
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`, read from the pinned objects in
`D:/SiloPlayer-work/server-upstream-nov14`. The native route is `/admin/downloads`
(`AdminRoute.PreparedFiles`) and is registered only through `AddAdminPreparedFiles()`.
Viewer Downloads and Manage > Downloads > Settings are separate surfaces and are unchanged; the
server-location dialog only navigates to the existing Download Settings page.

## Scope

The page follows `pages/AdminDownloads.tsx` and the five components under
`pages/admin-downloads`: `StorageTab`, `PreparationTab`, `PreparedFilesTab`,
`DeviceCopiesTab`, `HistoryTab`, and `EditLocationDialog`. It has the official tab order and
URL state (`tab`, prepared-file `location`, and stale-device entry), loading/error/empty states,
bounded Load more controls, and compact horizontal filter scrolling.

- Storage: decimal storage totals, official-priority warnings, server/node online and disk state,
  directory/budget source, prepared/untracked/waiting counts, browse-by-location, cleanup,
  guarded node directory/budget editing, and confirmed untracked deletion.
- Preparation: active-state counts; state, worker, and text filters; source/output/progress,
  requester and worker facts; row and selected pause/resume/cancel/remove controls. Cancel copy
  names lost encoding progress and affected waiting downloads.
- Prepared files: title search; location, state, format, and official sort choices; recipe,
  location, disk size, last-use and usage state; selected deletion. In-use files are excluded by
  default and require an explicit opt-in explaining that they will be prepared again.
- Device copies: person/device search, stale-only and official sort choices; expandable bounded
  download lists; selected, single, and whole-device revocation; optional reason; default-on
  monitor pausing for whole-device revocation; next-connection/cache/stale-device consequences.
- History: official reason/location/1, 7, 30 or all-kept filters, storage policy summary, event
  item/location/actor/bytes details, and bounded paging.

## Wire and ownership contracts

- Reads use `GET /api/v2/admin/downloads/storage`, `/preparations?limit=200`,
  `/storage/files`, `/devices`, `/entries`, and `/storage/events`. Cursor lists request 50 rows and
  retain at most 10 pages (500 rows); repeated/missing cursors, oversized pages, duplicate or
  missing identities, invalid locations/states, and malformed values fail closed.
- Actions use the exact official POST routes: `/preparations/{pause|resume|cancel}`,
  `/storage/files/delete`, `/storage/locations/{location}/cleanup`,
  `/storage/locations/{location}/untracked/delete`, and `/revoke`. IDs are distinct and batched at
  500. Prepared-artifact IDs use the official `[A-Za-z0-9_-]{1,128}` alphabet before send.
  Every distinct requested ID must receive exactly one result; truncated, duplicate, or unrequested
  acknowledgements fail closed.
- Node edits read bounded `/api/v2/admin/nodes` pages and `PUT /api/v2/admin/nodes/{id}` with the
  exact strong `config_etag` in `If-Match`. A response must return the same node identity and the
  same new strong validator in its ETag and body. Server directory policy remains informational
  here and is edited by the existing Download Settings page.
- Every write is single-send with no authentication retry/replay. Transport/server uncertainty is
  reported as unknown and followed by authoritative reads. No workflow reads, guesses, or deletes
  a local Windows path; prepared-file deletion is exclusively server-side.
- The controller owns one visible visit. Reads have independent cancellation and sequence guards;
  query changes, account/profile/server authority retirement, unload, and shell departure discard
  late results and clear retained rows, selections, expansions, and dialogs. Page re-entry creates a
  fresh controller and authority lease.

## Integration

- `App.xaml.cs`: `services.AddAdminPreparedFiles();`
- `docs/parity/admin-monitor-surfaces.json`: all 15 new Admin source/XAML files.
- `installer/build.ps1`: `Views/Admin/AdminPreparedFilesPage.xbf` is approved.
- Native dispatch: `SILO_NATIVE_TEST_ONLY=admin-prepared-files` enters the real registered Admin
  shell/Frame with a synthetic server, covers all tabs and destructive dialogs, checks exact node
  `If-Match`, retires authority, and authors wide/compact captures.
- `.codex-tmp/admin-prepared-files/tests-only.patch` preserves the pre-production tests-only
  checkpoint for coordinator-observed RED evidence.

## Authored verification and acceptance

No .NET, build, test, app, native, browser, mutex, or generated-output command was run in this
source-only slot. All results are unobserved. The coordinator must first remeasure output/free-space
limits, then run each .NET stage under the required single nonblocking `SiloOct18Build` acquisition
and release it in `finally`. From this worktree, the stage commands are:

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --filter "FullyQualifiedName~AdminPreparedFiles|FullyQualifiedName~AdminManageNavigationTests|FullyQualifiedName~UserOnlyClientArchitectureTests"
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishDir=<fresh-absolute-publish-directory>
& tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory <fresh-absolute-publish-directory> -Only admin-prepared-files
& tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory <fresh-absolute-publish-directory> -Only admin-manage-foundation
& tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory <fresh-absolute-publish-directory> -Only admin-manage-download-settings
```

Acceptance also requires inspecting `admin-prepared-files-wide.png` and
`admin-prepared-files-compact.png`, source/PDB binding to this exact candidate, the literal native
completion sentinel, and confirmation that Viewer Downloads and Download Settings behavior remain
unchanged.

Limits: synthetic HTTP and authored source only. Compilation, runtime behavior, live-server
interoperability, actual filesystem permission failures, 412 conflict rendering, Narrator/IME,
light theme, all DPI/width combinations, and complete pixel parity are not certified. No installer,
installed-player/settings, credentials, production, commit, push, or PR action occurred.
