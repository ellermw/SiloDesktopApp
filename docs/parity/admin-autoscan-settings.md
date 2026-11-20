# Autoscan › Settings — source candidate, November 17, 2026

## Source and scope

- Desktop base: Beta `248aafd62ed65b868c0e7356926e4d83204649cd`; prepared branch `t3/admin-autoscan-settings`. Version remains `1.2.1181-beta.1`. All changes are uncommitted.
- Official source: `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`, read with `git show` from `D:/SiloPlayer-work/server-upstream-nov14`. No clone, checkout setup or unmerged Sources dependency.
- Only `/admin/settings/autoscan`: polling defaults plus complete saved connections table, create/manual/reuse editor, edit, advisory check, Requests setup cross-link and delete confirmation. Global enable, Run now, Sources, Activity and webhook source setup remain with their separate writers.
- The app registration, required installer XBF and administration source allowlist include this finished page. No other submitted PR is changed.

## Exact reference trace

All references below are at the official pin:

- `web/src/pages/admin-settings/AutoscanSettings.tsx`: two-field draft, number coercion defaults (300/0), seconds units, Debounce explanation, dirty row/group markers and save/discard bar. No enable switch here.
- `web/src/pages/admin/autoscan/ConnectionsPanel.tsx`: Name/Kind/Source/URL columns; Own/Reused from Requests; default reuse when a Sonarr/Radarr Requests server exists; create-only mode choice; manual fields; saved-key mask and leave-blank hint; no-server setup link; test and save conditions; delete-source-binding warning.
- `web/src/hooks/queries/useAutoscan.ts`: settings GET/PUT, connections GET/POST/PUT/DELETE and connection test POST, captured identity, no mutation or authentication replay.
- `web/src/api/v2/adminAutoscanConnections.ts` and `adminAutoscanPagination.ts`: cursor-based connection reads.
- `web/src/api/v2/adminRequests.ts`: Requests list followed by canonical per-ID descriptor reads. Service kind comes only from `plugin_config.service_kind`, not legacy top-level kind. This page projects only id/name/service kind and never keeps Requests credential properties.
- `internal/api/handlers/autoscan.go`: name plus own URL or Requests link validation; normalized optional integration reference; blank API key retains a saved key on edit; `has_api_key` without credential disclosure; saved-ID/direct/integration check branches; polling interval 1–2147483647 and debounce 0–2147483647; persistence plus independent rescheduling result.
- `contracts/api/v2/openapi.json`: dedicated non-retryable settings PUT has **no ETag precondition**; delete confirms with 204; mutation responses are canonical credential-free connection views.
- `web/src/locales/en.json`: scoped English labels/descriptions copied into `AdminAutoscanSettingsText`. Native-only messages distinguish failed dependencies, explicit review and unknown outcomes. No raw server/plugin error text is displayed.

## Writes and ownership

Settings send precisely enabled/default_poll_interval_seconds/debounce_seconds to `/api/v2/admin/autoscan/settings`. Before dispatch, a new owned GET verifies that polling defaults have not changed and copies the latest enabled value. Changed defaults block with explicit Reload/review; the page never assumes it owns enabled. An acknowledged save separately reports applied, failed or not_configured rescheduling.

**Protocol limitation:** there is no server revision/If-Match contract here. A concurrent change between that GET and PUT cannot be made atomic by this client. Descriptor comparison likewise cannot detect an independent replacement of a write-only key that leaves all visible fields unchanged. This implementation does not invent a precondition or claim transactional protection.

All mutations, including advisory check and delete, are sent once with authentication refresh/replay disabled. Serialization prevents double-submit. Lost transport, 408/5xx, malformed 2xx acknowledgements and ambiguous results are not success. Save/delete require explicit readback before another submission. Rejected and changed-target writes also require review. Reload closes the editor, clears secret drafts and discards the prior polling draft; it never replays a command. A check is advisory and tied to the exact editor revision; changing fields suppresses its old result. A failed check does not block Save.

Own connection creation requires name, URL and key; own editing can omit a key only when has_api_key is true. Blank keys are omitted, never sent as an invented clear operation. Linked writes contain name/kind/request_integration_id only. Linked edits check the saved connection ID. Own edits check the saved ID unless both a URL and new key are present; then they check that explicit draft. New own checks may omit a key, matching the official advisory condition.

The shared AdminMonitorAuthority owns page/read/action leases. Authority loss, profile/server/account replacement, navigation or outer-shell suspension cancels and clears reads, editors, results and secrets. Deferred UI publication checks controller, owner and current state again. Dialog completion is exposed for the native fixture; fixture restoration waits for both page and shell Unloaded plus dialog completion.

## Bounds and intentional native safeguards

- JSON: 1 MiB per response, including failures; individual GET deadline 20 seconds, write/check deadline 45 seconds.
- Connections: 100 rows/page, at most 10 pages/1,000 rows and 45 seconds total. Requests: 50 summaries/page, at most four pages/200 canonical rows and 60 seconds including detail reads. Duplicate IDs, repeated/missing continuations, malformed metadata and excess pages fail closed without publishing a partial list. No catalog walk, polling or background scan is introduced.
- Name 512 characters, base URL 4,096, typed key 8,192. Native safety restriction beyond the WebUI: only HTTP(S) base URLs without userinfo, query or fragment are editable/testable. Unsafe server-returned URLs are not displayed; this avoids credential/signed-URL disclosure. Raw plugin connection errors are replaced with the official generic failure text; safe version text is bounded.
- Native WinUI adaptive settings rows, horizontal table overflow, stacked dialog fields and a vertically scrollable editor replace the HTML layout. Wide/narrow capture code is authored, not observed. Settings › Requests navigation truthfully reports unavailable if that independent page has not been registered yet.

## Authored regressions and runtime limits

Authored `AdminAutoscanSettingsTests` cover exact body/current enabled, validation, rejected/unknown single dispatch, malformed acknowledgements, connection descriptor changes, delete status semantics, omitted saved key, all credential conditions, canonical Requests kind projection, duplicate cursor refusal, oversized bodies, concurrent save, exact-revision test results and retired/late identity reads. `AdminAutoscanSettingsSourceTests` protect registration, XBF/approved sources, no generic settings or Sources dependency and native unload ordering.

The `admin-autoscan-settings` native selector enters through the production registration, AdminShellPage and nested Frame. It exercises official groups/table, polling save plus reschedule warning, reuse create/check, manual create/check with lost acknowledgement and explicit reconciliation, blank-key edit, missing-key state, linked edit/check, source-bound delete rejection and explicit successful delete, dependency failure versus empty Requests setup, authority retirement, and wide/narrow capture. Dialog and real Unloaded barriers precede restoration of global App.Services.

**No .NET, restore, build, unit tests, native host, browser, mutex acquisition, generated outputs or installer were run by this worker.** No runtime count, visual parity, accessibility, live-server or all-DPI claim is made. `git diff --check` is a source whitespace inspection only.

## Coordinator acceptance commands

Only the authorized serialized delivery lane may execute these after its required inventory/free-space checks, one nonblocking SiloOct18Build WaitOne(0), process containment/deadline and source/PDB binding. Use the lane's existing output root and QA signing policy; preserve all frozen artifacts and the installed player. These commands do not authorize installation or publication.

~~~powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj --filter 'FullyQualifiedName~AdminAutoscanSettings|FullyQualifiedName~UserOnlyClientArchitectureTests'
pwsh -NoProfile -File installer/build.ps1 -Configuration Release -PublishDirectory $LanePublishRoot -InstallerOutputDirectory $LaneInstallerRoot
pwsh -NoProfile -File tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory $BoundVersionedAppDirectory -Only admin-autoscan-settings
~~~

The lane supplies approved signing flags/environment, absolute lane-owned output directories and the exact bound application directory inside its versioned publish. After selected coverage, run its broader existing Admin/navigation/native regressions, inspect both authored captures and the dialog at narrow/wide widths, then record actual receipts. There is no external source blocker; compilation and runtime observation are pending coordinator acceptance, not certified here.

