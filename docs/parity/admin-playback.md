# Manage > Playback: Nodes and Playback Settings

November 20, 2026. Branch `t3/admin-playback-nodes-settings`, merged with Beta `f35bb7e59` (version
`1.2.1194-beta.1`, unchanged here). Official pin `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`.
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

## Tests

Observed on this source (`playback-final-r1`, lane `D:/SiloPlayer-build/admin-completion-lane-a`, one `-Stage final`
run): focused unit filter 254 passed / 0 failed; full unit suite 9,767 passed / 0 failed; native selectors
`admin-playback-administration` (73 PASS, 0 FAIL), `admin-manage-foundation`, `admin-configure-general` and
`admin-monitor` all passed.

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
