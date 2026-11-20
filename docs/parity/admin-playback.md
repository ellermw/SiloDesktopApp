# Manage > Playback: Nodes and Playback Settings

November 20, 2026. Branch `t3/admin-playback-nodes-settings`, merged with Beta `db49c4c93` (stable 1.2.1194 merged; version
unchanged here). Official pin `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`.
Scope: `/admin/nodes` and `/admin/settings/playback` only. History and Skip Markers (#170), the installed
player, updater, local playback preferences and libmpv code are untouched.

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

## Review findings fixed (November 20)

1. **Enabled switch after a rejected update.** The official Nodes switch is controlled
   (`checked={node.enabled}`), so a rejected change shows the server value again. The native card now returns the
   switch to the saved value whenever `UpdateAsync` does not save (4xx rejection, conflict, unknown outcome, stale
   target). Playback Settings toggles also resynchronise to the staged value if the controller refused the change.
2. **Last marker task execution status.** `MarkerTasksCard`/`TaskStatusBadge` show the last execution as a badge
   (`completed`, `failed`, `cancelled`, `Completed with errors` for a `contribute_markers` run that failed items;
   unknown statuses as sent; the server's error text as tooltip). The rules live in
   `AdminPlaybackMarkerTaskStatus`, which also owns the official result line (never run / last run date / contribute
   result summary / rate-limit note).

## Second independent review (November 20), all seven findings fixed

Each was confirmed in the code and against the pinned official source before changing anything.

| # | Finding | Fix | Test |
| --- | --- | --- | --- |
| D1 | `mbps * 1000` threw `OverflowException` for about 26 or more digits and escaped the async-void Save handler | Compare `mbps <= int.MaxValue / 1000m` before scaling; the input is trimmed; out-of-range is the usual validation message. Egress is the only scaled numeric field; capacity is an `int.TryParse` | `AdminPlaybackSecondReviewTests` (26-digit, decimal maximum, 2147483.648, negative, exponent; the 2147483.647 edge) |
| D2 | Node override list offered VideoToolbox, which the server's `hwAccelOverrideValues` (`internal/nodepool/repository.go`) and the official `hwAccelOverrideOptions()` omit, so Save returned 422 | `AdminPlaybackNodeEditor.HwAccelOverrideValues` (inherit, auto, qsv, vaapi, nvenc, none) feeds both the dialog (`NodeHardwareOptions`, official labels `web.admin.nodes.hwAccel.*`) and `Capture`. The cluster-wide `playback.hw_accel` list keeps VideoToolbox (`admin_settings.go:942`) | Unit list/enum and refusal tests; native step checks the dialog's option tags and that the cluster-wide select still offers VideoToolbox |
| D3 | A stored address that fails the native check (credentials, query, no scheme) was blanked in the form and then saved as empty (`public_url` null) | The form still never shows such a value, but an untouched field is now left out of the request (`url`, `public_url`); a typed value or a typed-then-cleared value is an explicit choice and is validated and sent. Official sends the stored text back unchanged; showing it is withheld here because it may carry credentials | Unit tests: untouched leaves both alone, edited is sent, cleared sends null, invalid edit refused, displayable stored value is still sent exactly |
| D4 | A task poll sent before a run could land after it and flip the running row back to idle | The poll captures `_supportSequence` and drops its answer if it changed; `RunTask` bumps the sequence before it sends | Native step holds a poll in flight, runs the task, releases the older answer and checks the row stays running. Reverting only the sequence check made that step FAIL (run `playback-fix-red-r1`) |
| D5 | A hidden row's invalid draft kept Save disabled with no visible error (buffer ahead with throttling off, marker workers when mode is online or off, preview rows without trickplay) | `AdminPlaybackSettingsRules.HiddenInvalidKeys`; the controller resets such a draft to its saved value (not while saving) and learns capability visibility from `SetVisibilityContext`. A valid hidden draft is kept, as the official page keeps hidden values (its hidden inputs are simply not rendered) | Unit rules and controller tests; native step: invalid buffer ahead blocks Save, switching throttling off re-enables it |
| D6 | Any exception from Run now (including 409 or another 4xx) was treated as unconfirmed and locked the row | An `ApiException` with a 4xx status other than 408 is a refusal: a toast, a support/task refresh from the server, and no lock | Native step: a 409 leaves the task runnable and is not replayed |
| D7 | Any background node refresh failure set `ReviewRequired`, stopped polling and disabled every control; official `useAdminNodes` is a react-query `useQuery` that keeps `data` and keeps its `refetchInterval` | `ReviewRequired` is set on a reload failure only when nothing was ever loaded or a write outcome is unknown; otherwise the rows stay, the error and a reload action show, and polling continues | Unit tests: refresh failure keeps rows and recovers, first-load failure blocks, failure after an unknown write keeps writes blocked; native step: after a refresh failure the rows and the switch stay usable |

The reviewer's gap is closed: `PreconditionFailedThroughTheEnabledSwitchBlocksReplayUntilReload` (unit) and a native
step drive a 412 through the node enabled switch (switch restored to the saved value, writes blocked, message and Reload
shown, no replay, reload recovers).

Changed existing assertions: one. The native step "node list failure shows the error with a reload action" asserted that a
failed refresh of an already loaded page sets `ReviewRequired`; it now asserts the error and the Reload action are shown
while `ReviewRequired` is false and the loaded node and its switch stay usable (D7; official source above). A wait for the
created proxy's editor to close was added before the delete step, because the first run of this fixture lost a race
between the editor closing and the delete click.

## Third review (November 20), five findings fixed

Not built or run when written; the pipeline run is the evidence. `AdminPlaybackThirdReviewTests` is new.

| # | Finding | Fix |
| --- | --- | --- |
| T1 | The 30-second node poll disabled every card control and replaced the cards, so keyboard and Narrator focus left the list each cycle | The poll no longer disables the cards; only a foreground load, a write or a required review does. An action started during a poll waits for it and then targets the refreshed row with the same `config_etag`. The cards are still replaced on each refresh (relative times and stale markers depend on the clock), and focus returns to the control with the same automation id if it was in view. The shared-GPU set is computed once per window |
| T2 | The one-time node key and its startup settings entered clipboard history and the cloud clipboard | Both copies use `Clipboard.SetContentWithOptions` with `IsAllowedInHistory = false` and `IsRoamable = false`. The clipboard itself is not cleared on close, because the key is pasted after the dialog closes |
| T3 | An unconfirmed marker run was released by any support refresh, including a failed one or the one a `playback.hw_accel` change triggers | `AdminPlaybackSupport.TasksRead` says whether the task list was read. Only the Refresh button raises the review, and only an applied read with `TasksRead` clears the lock |
| T4 | `physical_gpu_keys: null` threw in card rendering | `ListNodesAsync` normalises null to an empty list and the card guards the read |
| T5 | A stored empty `hw_accel_override` left the editor without a choice and refused every edit | The editor treats an empty or blank stored override as inherit. An unknown stored value is still refused with the usual message, not silently replaced |

## Fourth review (November 20), one finding fixed

Not built or run when written; the pipeline run is the evidence.

The shared `HttpClient` has the default 100-second timeout, so a re-probe that took longer ended before its 180-second
deadline and was reported as an unconfirmed change. Re-probe now uses the client without an `HttpClient` timeout that
Backups use; the 180-second deadline is its only limit. Every other node call stays on the shared client. Tests:
`ReprobeOutlastsTheSharedClientTimeout` and `OnlyReprobeLeavesTheSharedClient` in `AdminPlaybackNodesTests`.

## Tests

Observed on this source (`playback-fix-final-r1`, commit `cd39c93df`, lane `D:/SiloPlayer-build/admin-completion-lane-a`, one
`-Stage final` run): focused unit filter 282 passed / 0 failed; full unit suite 9,795 passed / 0 failed; native selectors
`admin-playback-administration` (94 PASS, 0 FAIL), `admin-manage-foundation`, `admin-configure-general` and
`admin-monitor` all passed. Earlier runs of this review: `playback-fix-native-r1` failed at "delete confirmation opened"
(a race in the fixture, fixed above) and `playback-fix-red-r1` was the deliberate check that the D4 step fails without its fix.
`AdminPlaybackSecondReviewTests` is new (28 tests).

- `AdminPlaybackAdministrationTests`, `AdminPlaybackNodesTests`: registration/resource approval, key counts,
  rules, editor null/omission semantics, transport bounds, unknown outcomes and no replay.
- `AdminPlaybackReviewFixTests`: rejected update keeps the saved node enabled and reports no review state;
  badge label/tone/tooltip, never-run, completed-with-errors, rate-limited and result text.
- `AdminPlaybackAdministrationNativeFixture` (`admin-playback-administration`): real DI registration, Admin shell and nested
  Frame. Nodes: populated, edit, key rotation and erasure, proxy create, delete, health/reprobe/drain/resume,
  routing preset save, **rejected enabled switch restored, lost-acknowledgement switch restored and blocked until reload (no replay)**,
  list failure with reload, empty state. Playback Settings: conditional rows, **marker-task badges, result lines and tooltip**,
  run-now once, lost task acknowledgement not replayed, preview-regeneration confirmation, strong-ETag save, conflict
  keeping the draft, reload and review, authority retirement clearing inputs. Seven PNG captures were written.

## Not verified / limits

- No live Silo server, real phone, real GPU/transcode node, real network enrollment or all-DPI/Narrator/IME run.
- Captures of single elements (the marker-task list) render light text on a transparent background, so their
  text looks faint in the PNG; this is a capture artefact of the fixture, not an on-screen defect. Visual parity
  against the official page was not compared side by side beyond the shell captures.
- Cross-links to Providers (`/admin/settings/providers`), Libraries and task history are real registered routes;
  a link stays disabled with an unavailable tooltip while that page is absent in a build.
- `MarkerProviderTiles` belongs to the Providers page and is not part of this family.
