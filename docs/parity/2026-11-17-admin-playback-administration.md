# Manage > Playback: Nodes and Playback Settings

Source-only candidate, November 17, 2026. Branch `t3/admin-playback-nodes-settings`, sole checkout
`C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-bb1c70bd-8bdb7288eeb60a12`, exact base
`248aafd62ed65b868c0e7356926e4d83204649cd`. Version `1.2.1181-beta.1` is unchanged.
No commit, push, PR, installer, publication, mutex acquisition or runtime execution occurred.

## Official reference

All upstream reads used pinned Git objects, through `git -C D:/SiloPlayer-work/server-upstream-nov14 show
217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb:<path>`. No extra checkout or fetch was needed for this pinned assignment.

- `web/src/pages/AdminNodes.tsx`, `adminNodesPresentation.ts`: separate proxy/transcode sections,
  group filters, node cards, editor, delete confirmation, health/reprobe, drain/resume, one-time key dialog.
- `web/src/hooks/queries/admin/nodes.ts`: configuration ETag versus observations, bounded desktop
  adaptation of the 200-item cursor collection, capability gates and non-replayed writes.
- `web/src/pages/admin-settings/NodeRoutingSettings.tsx`: three presets, five independently staged
  routing policies, preferred-path preview, worker/proxy-only warnings and a separate SaveBar.
- `web/src/pages/admin-settings/PlaybackSettings.tsx`, `playbackSettings.keys.ts`,
  `playbackSettings.utils.ts`, `TrickplayNodeCapacityField.tsx`, `MarkerTasksCard.tsx`:
  watch progress, transcoding, hardware/GPU selection, speed tests, preview images and skip-marker settings.
- `web/src/components/settings/PathSettingField.tsx`, `web/src/lib/hwDevices.ts`,
  `web/src/pages/admin-settings/settingsPathDefaults.ts`: blank path reset semantics and device ordering.
- `contracts/api/v2/openapi.json`, `internal/config/admin_settings.go`, `internal/api/handlers/nodes.go`:
  wire fields, guarded mutations, defaults, enums, numeric constraints and command acknowledgements.
- `web/src/locales/en.json`: 457 exact English ICU patterns copied into the isolated domain catalog;
  rich text tags become native text. Additional reliability/status text is native-only English.

The pinned playback form owns **33 writable keys**. Its search index has 36 labels, including headings;
that is not a count of wire settings. The existing navigation/search catalog is preserved.
`playback.chapter_thumbnail_node_capacity` stays hidden and the `download.*` family stays separate.
Routing owns exactly five `playback.routing.*` settings.

## Implemented production entry points

`AdminConfigureServices.Add` registers `/admin/nodes` and `/admin/settings/playback`, the domain API,
and the keys read by the existing authority-scoped settings store. New XBF resources and all new Admin
source files are in the existing installer and architecture allowlists. History and Skip Markers #170,
the installed player, updater, local playback preferences and libmpv code are untouched.

Nodes includes group-aware add, edit, enable/disable, delete, health check, re-probe, capability-gated
drain/resume and issue/rotate-key workflows. Proxy Public URL clearing sends null; proxy edits omit
hidden GPU/source fields; transcode inheritance sends null; create omits unsupported acceleration
fields. Source access is local/core only when advertised. Egress Mbps becomes integer kbps. GPU
device overrides retain configured but missing devices and distinguish CUDA identities from render
paths, including inheritance from the cluster policy. New transcode nodes offer key issuance after
their acknowledged creation and immediate health check. A key remains in its owning dialog only,
is masked on screen and cleared on close/retirement; copying it or its startup settings is explicit.

Node cards expose health, jobs/caps/egress, checked time, hardware inventory, drift/shared GPU,
stale inventory reasons, CPU/memory/disk/network/process samples, build and GPU engine readings.
Old/unhealthy samples are not presented as live. Network-provider observations link to the existing
registered Network Access page, which already owns provider discovery, per-host Connect/Disconnect
and enrollment workflows. No provider slug/host identifier or new network endpoint is guessed here.
The pinned Nodes WebUI itself does not embed those provider command forms.

Routing has Silo Defaults, GPU offload and Central egress presets; all five execution/egress controls
remain editable under Advanced, with preferred paths and stranded/proxy-only warnings. Routing and
Playback Settings have separate dirty drafts, environment management, restart badges, sticky save
bars, discard/leave guards, strong ETag saves and explicit reload/review after conflicts or unknown
outcomes. Advanced choices use the existing disclosure preference helper, with separate playback
keys; a user choice can override a forced-open dirty/warning state.

Playback implements every pinned writable key, exact enums/ranges, lower-version/4K and throttle
conditions, HEVC encoding and hardware/software tone mapping, paths with real server defaults,
back buffer, keyframe playlist, verified hardware detection and GPU toggles, speed-test settings,
preview width/interval/workers/node capacity/execution/HDR controls, and marker mode, detection-kind
capability, online-storage/lazy-playback and workers. On-demand storage stages lazy playback true.
Node-only image modes remain selectable when saved or node discovery is unknown; known unavailable
modes are disabled. Preview placement moves to Advanced without usable transcode nodes. Width and
affected interval saves ask for preview regeneration, using ready + running + pending counts, or an
explicit unknown count. Marker task rows include results, run-now and history links. Uncertain run
outcomes remain disabled until manual review/refresh, and a run acknowledgement is not completion.

Cross-links to Providers or task history use real registered routes. They show an unavailable tooltip
and stay disabled if that other family's page is absent in this independently reviewable base. Those
pages belong to other workers/the submitted pipeline, and are not reimplemented or depended on here.

## Transport and lifetime limits

| Operation | Contract |
| --- | --- |
| List nodes | `GET /api/v2/admin/nodes?limit=200&cursor=...`; at most eight pages/1600 rows, 2 MiB per response; continuation disclosed, duplicate IDs/cursors rejected |
| Create/update | `POST /api/v2/admin/nodes`, `PUT /api/v2/admin/nodes/{id}`; update uses captured strong `config_etag`; response ETag must equal body tag and update must acknowledge the same opaque ID |
| Delete | `DELETE /api/v2/admin/nodes/{id}` with captured strong `If-Match`; accepts the official empty 204 |
| Commands | `POST .../{id}/check`, `/reprobe`, `/drain`, `/resume`; validate operation-specific receipt and target identity |
| Key | `POST .../{id}/key`; one response-only key, no query/controller cache and no replay |
| Settings | existing effective/sensitive-status/restart-keys reads and `PUT /api/v2/admin/settings` with strong `If-Match`, dirty string-valued keys only |
| Support | `/api/v2/libraries/capabilities`, `/api/v2/admin/markers/capabilities`, `/api/v2/admin/system/hw-accel`, `/api/v2/admin/trickplay/libraries`, `/api/v2/admin/tasks` |
| Marker commands | `POST /api/v2/admin/tasks/{key}/run`, allowlisted to sync/detect/contribute; validates returned task identity/state |

Every request is tied to captured account/server/profile authority, including post-response checks.
Writes cannot refresh authentication or retry. Unknown node outcomes stop further mutations until
an explicit reload; unknown settings outcomes retain the draft and require explicit reconciliation.
Reads have cancellation and deadlines; re-probe allows 180 seconds, other domain calls at most 45.
Node polling is 30 seconds, page/window activity gated, paused for dialogs and deferred during player
loading. It is serialized with writes. Marker task observations use a separate bounded, foreground-only
30-second task read, without repeating hardware probes or node enumeration. Support reads cancel when superseded or retired. No catalog walk,
installed-player restart, decoder modification or shared/generated output is part of this candidate.

## Tests authored; runtime entirely unobserved

The source-only slot ran no .NET restore/build/test, app, native host or browser. It acquired no mutex
and changed no generated outputs. No RED, GREEN, native capture or visual parity result is claimed.
Observed checks are limited to source inspection, manifest/key/count checks, `git diff --check` and
patch applicability checks.

- `AdminPlaybackAdministrationTests`: baseline-compilable reflection-backed RED cases for missing
  page registration/resources and actual rules/API/controller behavior. The coordinator can run these
  before applying production changes; missing types fail assertions, not type resolution at compilation.
- `AdminPlaybackNodesTests`: typed editor/transport/controller cases, including null versus omission,
  numeric units, address validation, cursor bounds, wrong-node receipts, no auth replay, late key
  retirement, unknown outcomes, defaults, width validation and disclosure preferences.
- `AdminPlaybackAdministrationNativeFixture`, selector `admin-playback-administration`: real production
  DI registration, shell and nested Frame. Synthetic health/reprobe/drain/resume, editor save, key
  rotation and erasure, proxy create/delete, routing preset save, conditional playback controls,
  preview regeneration confirmation, ETag saves and retirement. Dialogs/pages close/unload before
  restoring globals. The fixture requests five PNGs, including the dialog visual roots; none exists yet.

The coordinator must inspect those rendered captures against the pinned WebUI. Source tests alone
do not establish layout parity. Live server compatibility, complete all-DPI behavior, Narrator/IME,
physical pointer behavior, real network enrollment and real GPU/transcode behavior are unverified.

## Coordinator acceptance commands

Use the coordinator's existing deadline/containment and exact-source receipt harness for **every**
external command below, after storage inventory and the single nonblocking `SiloOct18Build` acquisition
required by the execution contract. Use one coordinator-owned output root, releasing the mutex in
finally. Do not run these commands in a source-only worker or use the old unbounded `run.ps1` wrapper.

1. Apply `.codex-tmp/playback-administration-20261117/tests-only.patch` to the exact base in the
   coordinator's prepared acceptance checkout. Before production, run:

   ```powershell
   dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path D:/SiloPlayer-build/artifacts --filter FullyQualifiedName~AdminPlaybackAdministrationTests --logger "trx;LogFileName=playback-admin-red.trx" --results-directory D:/SiloPlayer-build/playback-admin-results
   ```

   Expected RED includes absent production type assertions and absent page registrations. This is an
   expectation, not an observed result. The initial pre-production patch is preserved separately;
   the current tests-only patch includes subsequent fixture/import corrections, still baseline compilable.

2. Apply adjacent `production.patch` on top of the test checkpoint (it includes the typed additional
   tests and docs). Run focused and preserved foundations:

   ```powershell
   dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path D:/SiloPlayer-build/artifacts --filter "FullyQualifiedName~AdminPlaybackAdministrationTests|FullyQualifiedName~AdminPlaybackNodesTests|FullyQualifiedName~AdminPlaybackAuditTests|FullyQualifiedName~AdminManageNavigationTests|FullyQualifiedName~AdminSettingsFormTests|FullyQualifiedName~AdminServerSettingsStoreTests|FullyQualifiedName~UserOnlyClientArchitectureTests" --logger "trx;LogFileName=playback-admin-green.trx" --results-directory D:/SiloPlayer-build/playback-admin-results
   dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path D:/SiloPlayer-build/artifacts --logger "trx;LogFileName=playback-admin-full.trx" --results-directory D:/SiloPlayer-build/playback-admin-results
   dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true --artifacts-path D:/SiloPlayer-build/artifacts -o D:/SiloPlayer-build/publish
   ```

3. Bind that exact publish and PDB/source inventory to the native reference using the coordinator's
   existing receipt procedure. Stage `SiloPlayer.*`, source `Assets` and published `Updates` into
   `D:/SiloPlayer-build/native-reference`, as the repository native wrapper does. Build the host:

   ```powershell
   dotnet build tests/SiloPlayer.NativeRegressionTests/SiloPlayer.NativeRegressionTests.csproj -c Release --artifacts-path D:/SiloPlayer-build/artifacts -p:SiloPlayerAssemblyDirectory=D:/SiloPlayer-build/native-reference -o D:/SiloPlayer-build/playback-admin-host
   ```

   Under task-owned containment and a 180-second native deadline, launch the built host with:

   ```powershell
   $env:SILO_NATIVE_TEST_APP_DIRECTORY = 'D:/SiloPlayer-build/publish'
   $env:SILO_NATIVE_TEST_RESULT_DIRECTORY = 'D:/SiloPlayer-build/playback-admin-results/native'
   $env:SILO_NATIVE_TEST_THEME_PATH = Join-Path (Get-Location) 'src/SiloPlayer/Themes/DarkTheme.xaml'
   $env:SILO_NATIVE_TEST_READER_FIXTURES = Join-Path (Get-Location) 'tests/reader-fixtures'
   $env:SILO_NATIVE_TEST_ONLY = 'admin-playback-administration'
   # The coordinator's contained process runner executes:
   # D:/SiloPlayer-build/playback-admin-host/SiloPlayer.NativeRegressionTests.exe
   ```

   Ensure the result directory exists. Require numeric exit 0, the final completion marker, zero FAIL
   lines, inspected PNGs and matching source/publish/host receipts. Repeat the existing
   `admin-playback-audit` and `admin-manage-foundation` selectors on the same bound payload. Restore
   environment variables after execution. Any failure requires fixing and re-verifying final source.

4. Review the uncommitted candidate and current file count before accepting. This family is below
   200 files and needs no split. No runtime success or visual/live compatibility claim may be inferred
   from the source-only handoff. The coordinator owns acceptance and any subsequent submission.
