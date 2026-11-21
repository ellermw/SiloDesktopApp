# Manage > Playback > Nodes: parity refresh against official 8ae4f4f5a

November 21, 2026. Branch `t3/admin-nodes-parity-refresh` (from Beta `aca3dcb21`, version unchanged). Official reference:
`Silo-Server/silo-server` commit `8ae4f4f5a0b4a35503048a882695b7170a1604a3`, read with `git show`/`git grep` from the blobless clone
`D:/SiloPlayer-work/server-upstream-nov14`. For the review fixes the reference was re-read at `a84b2551b23646380e1eecef45fdbfdbeecdfed3`: `git log 8ae4f4f5a..a84b2551b` over
`AdminNodes.tsx`, `adminNodesPresentation.ts`, `en.json` and `openapi.json` is empty, so nothing on this page changed and every statement below holds at `a84b2551b`. The page was built against `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`
([admin-playback.md](admin-playback.md)); this record covers `217ff25abc..8ae4f4f5a` for the Nodes page only. The Playback settings page, node routing
settings (`NodeRoutingSettings.tsx`), the Dashboard's node widgets and every other page are out of scope and unchanged.

## Upstream changes that reach this page (217ff25abc..8ae4f4f5a)

Found with `git log` over `web/src/pages/AdminNodes.tsx`, `adminNodesPresentation.ts`, `hooks/queries/admin/nodes.ts`, `api/types.ts`, `api/v2/request.ts`,
the shared `ui/` components the page imports, `contracts/api/v2/openapi.json`, the node handlers under `internal/apiv2` and `internal/nodepool`, and `locales/en.json`.

| Commit | Change | Reaches the page as |
| --- | --- | --- |
| `2f5a14b8b` #4499 | Proxy and transcode nodes declare configuration from their environment | `StreamNode.env_managed` (update-body field names); the editor shows those fields read-only with `web.admin.nodes.form.envManaged`; `restoreEnvManagedFields` restates stored values in a full-form save; the server refuses a change with 422 `managed_by_environment` and accepts a restatement; node capability `env_declarations` |
| `ee1925336` #4654 | Transcode nodes and their keys created from the API servers' configuration | `StreamNode.deployment_managed`; the key line reads `web.admin.nodes.key.deploymentManaged` and offers no Issue/Rotate; Delete stays focusable (`aria-disabled`) and a press shows `web.admin.nodes.unit.deleteDeploymentManaged`; the server refuses `issueAdminNodeKey` and `deleteAdminNode` with 409 `node_deployment_managed`; node capability `deployment_nodes` |
| `d9515d397` #4727, `5537df46a` #4766 | Paths and URLs left to right | node form URL `dir="ltr"`, public URL and device override `dir="ltr"` only while filled (translated placeholder), device picker path in a `dir="ltr"` span |
| `4b1713ec1` #4753 | Copy URLs, device paths and IDs without invisible characters | `mono` rich text renders `<code>` (no FSI/PDI isolates) so a copy is exactly what is shown |
| `e2e4ab8b5` #4859, `30184e77d` #4792 | Status labels readable | backend and Drift badges use the `--*-label` tokens; group chip count loses its opacity |
| `ee1925336` #4654 (`nodes.ts`) | A 409 on a node write shows the server message | `useNodeWrite.onError` (a 409 shows the server text, anything else the unconfirmed text) |
| shared `ui/` (dialog, alert-dialog, badge, button, input, select) | `wrap-anywhere` titles and descriptions, `grid-cols-1` dialog column, `--destructive-fill` (#dc2626) for destructive buttons and badges, min-height from line height | long node names wrap in dialog titles/descriptions; destructive buttons use the solid fill |
| `request.ts` (#4577, #4529) | `v2ErrorText` | only a server problem or the session layer's notice is shown, never developer English from a transport failure |

Other upstream commits in the range (plugins, downloads, ratings, backups, history) do not touch the page, its imports or its operations. The openapi diff
for `/admin/nodes*` is exactly: `AdminNode.deployment_managed` and `AdminNode.env_managed` (both listed as required by the server), capabilities
`deployment_nodes` and `env_declarations`, a 409 on `deleteAdminNode`, and the updated `updateAdminNode` summary.

## Contract differences and how each is handled

| # | Official at 8ae4f4f5a | Native before | Now |
| --- | --- | --- | --- |
| C1 | `deployment_managed` (bool) | not read | `AdminPlaybackNode.DeploymentManaged` (`bool?`); absent on an older server means not managed. A non-boolean value refuses the whole page |
| C2 | `env_managed` (string[]) | not read | `EnvManaged` (`string[]?`); absent, null or **empty means nothing is managed and is accepted** (the server sends `[]` for a node that declares nothing). Only a non-array, a non-string or null item, an empty or control-character name, or more than 16 names refuses the page. The 16-name cap is left as it is: the official `declaration.go` names 11 declarable fields in all (a transcode node can declare at most 9, a proxy 4), so a legitimate server cannot reach 16, and a larger bound would protect nothing more |
| C3 | capabilities `env_declarations`, `deployment_nodes` | not read | read (default false); the page follows the node's own fields, as the official page does, so a failed capability read never hides a managed marker |
| C4 | detected backend `reason`, device `pci_address` | model read `Error` (not an official field) | `Reason` and `PciAddress`; the probe-failure titles and the live-GPU match by PCI address now work |
| C5 | reprobe result `node_name`, `error` | not read | read; the re-probe outcome message is the official `describeReprobeOutcome` |
| C6 | a 409 or 422 problem carries a reason (`detail`, field `detail`) | every 4xx showed one generic line | the server's reason (field detail, else problem detail, else title) is shown, redacted, plain, one line, at most 300 characters; the generic line stays only when the server sent none |
| C7 | update body fields are optional pointers; a declared field may be restated but not changed | n/a | a declared field is **omitted** from the PUT body (the official page restates the stored value). Omission is a no-change on the server, it cannot be normalised into a change (`""` against null, `0` against unlimited), and it satisfies "a managed node's read-only configuration is never sent". An older server has no `env_managed`, so nothing is omitted and the body is the same as before |
| C8 | `deleteAdminNode`/`issueAdminNodeKey` refuse a deployment-managed node | would send and show a generic refusal | never sent: `AdminPlaybackAdministrationApi` throws `AdminPlaybackNodeActionNotOfferedException` before any request, and the controller reports the official note without busy or review state |
| C9 | `enabled` toggle sends `{enabled}` only | same | unchanged: `enabled` is not a declarable field, so the switch stays available on a managed node |

Request bodies: create (`POST /api/v2/admin/nodes`), delete, key, command and `enabled` requests are unchanged. The edit body (`PUT /api/v2/admin/nodes/{id}`) is
unchanged for a node with no `env_managed`; for a node that declares fields it now omits exactly those fields. Both older and newer servers keep working.

## What managed nodes allow on this page

* **Environment-managed fields** (`env_managed`): read-only controls with the official note under each, device switches disabled and "Use cluster default"
  hidden for a declared `hw_device_override`; a declared field is never validated or sent; everything else on the node stays editable. If every editable field is declared,
  Save closes without a request.
* **Deployment-managed transcode node**: key line says "Key set by the server's deployment configuration"; no Issue/Rotate; the delete button stays enabled for focus
  but looks inactive, carries the note as tooltip and automation help text, and a press shows the note instead of a confirmation (nothing is sent). Edit, enable/disable, health check,
  re-probe and drain/resume stay as for any node. A server that still answers 409 `node_deployment_managed` (a node adopted after the page loaded) shows the server's reason
  and needs no review.

## Difference table (official 8ae4f4f5a against the native page)

Legend: **fixed** = changed in this pass; **already right** = verified equal; **deliberate** = intentionally different, reason given; **cannot** = not possible in WinUI.


| # | Area | Official 8ae4f4f5a | Native before | Outcome |
| --- | --- | --- | --- | --- |
| 1 | Header | `PageHeader` title and subtitle 8 px apart; sections `space-y-8` (32 px) | 28 px gaps; empty status and filter rows added extra gaps | **fixed**: title/subtitle grouped at 8, sections 32, empty status/filter rows collapse |
| 2 | Loading | whole page replaced by the loading text | status line under the header | **deliberate**: the shell and header stay |
| 3 | Group filter | eyebrow "Group", All chip with `allTitle`, chips with count, degraded dot, per-group routing title; hidden under two groups | plain buttons "label · count", no heading, titles or degraded marker | **fixed** (`AdminPlaybackNodePresentation.Groups`; filled chip when active; 28 px pill) |
| 4 | Sections | h2 + count badge + Add (primary); proxy banner (info colour, icon), transcode banner (warning colour, icon, `strong` sentence) | heading text, button, muted sentence | **fixed**; the strong sentence is located inside the English catalog text (the isolated adapter is English-only) |
| 5 | Empty section | boxed message, "in group" variants, Add (outline) or Show all groups | message and Show all groups only | **fixed** (Add added to the empty state) |
| 6 | Node unit | rounded-2xl (20 px), `p-5 ps-6`, 3 px status rail (healthy 70%, draining warning, drained info, unhealthy destructive, disabled 30%), 65% opacity when disabled | 16 px radius, left border colour | **fixed** |
| 7 | Unit header | name 15 px semibold, state dot + label, group badge (mono 10 px), URL mono truncated (title = full URL), public URL line (title `publicUrlTitle`) | name, state text, group text, URL text | **fixed**; URLs and device paths always read left to right |
| 8 | Key line | status + Issue/Rotate link; warning colour when missing; deployment-managed wording and no link | text + button | **fixed** (deployment-managed row included) |
| 9 | Actions | switch, divider, 28 px ghost icons: check, drain/resume (titles), re-probe (transcode only; titles), edit, delete | 32 px buttons; resume icon drew a plus (icon missing from the shared set) | **fixed** (`AdminPlaybackNodeIcons` draws circle-play, key and info) |
| 10 | Delete, deployment-managed | `aria-disabled`, tooltip, `aria-describedby`, a press shows the note | request the server refuses | **fixed**: button stays enabled for focus (UI Automation Invoke on a disabled button throws), 50% opacity, tooltip and automation help text; a press shows the note in the status line and sends nothing |
| 11 | Drain line | `statusLine`: strong state colour, muted detail, title = detail | one plain sentence without detail | **fixed** (rich runs from the dashboard text engine) |
| 12 | Disabled note | `unit.disabledNote` | same | **already right** |
| 13 | Acceleration block | backend badge (verified/failed/unverified/software with probe title), Drift badge, Shared GPU badge, probe-failure triangle, Stale label with reason title, device summary, override line, live per-device engine meters | override/drift/stale as plain English lines, device paths listed | **fixed** (`Gpu`, `Drift`, `SharedGpu`, `Override`, `Staleness`) |
| 14 | Load block | CPU, RAM, Disk (worst mount), Net, process memory with meters; unreported/stale labels with titles | CPU, memory, disks, network as English lines; a build line | **fixed**; the build line is not in the official block and was removed |
| 15 | Capacity block | Streams/Transcodes meter (warning at the cap), Egress on a proxy, "Checked" relative time with the exact time as title | text only | **fixed**; time through `DateTimeDisplay` |
| 16 | Layout | 1 column below 640, 2 from 640, 3 (1.1/1/0.85) from xl (1280) | single stack | **fixed** (window width) |
| 17 | Feedback | sonner toasts | status line | **deliberate**: the page reports through its live status line |
| 18 | Network provider lines | not on the official page | provider status and a link to Network Access | **deliberate** (kept from #214) |
| 19 | Editor | label above control, placeholders, Type badge, helps, url `dir=ltr`, public URL/device `dir=ltr` only while filled, `Saving...`, device picker with description + path + switch, effective acceleration prose | no placeholders, plain Type text, no effective prose | **fixed**; the full-width Save button **cannot** be matched (a ContentDialog owns its footer: Save/Cancel) |
| 20 | Editor, managed fields | disabled controls + note; stored values restated | n/a | **fixed**: disabled + note, and omitted from the body (C7) |
| 21 | Editor URL check | server validates only | client refuses credentials, queries, fragments | **deliberate**: an address with credentials is never shown or re-sent |
| 22 | Device switches | `onCheckedChange` | `Toggled` re-reported the starting state after realization | **fixed**: a switch stages a draft only for a change the admin made |
| 23 | Delete confirmation | ConfirmDialog, destructive variant, wrapped long names | shared red style, raw name | **fixed**: page-local `#dc2626` style (hover `#b91c1c`, pressed `#991b1b`, system colours in high contrast); name neutralized (control, line/paragraph separator and direction characters removed, 200 characters) |
| 24 | Key dialog | issue vs rotate (destructive), "Not now"/Cancel, issuing text, refusal reason + Close, unconfirmed text, `dir=ltr` | one generic failure text, no Close | **fixed** (refusal vs unknown outcome, Close, issuing text, Not now) |
| 25 | Key display and copy | key shown in clear; Copy puts the visible value on the clipboard | masked field | **deliberate** (kept): the key is masked and copied exactly; the startup settings show the key masked and Copy settings puts `SILO_CORE_URL=<url>` and `SILO_NODE_KEY=<key>` (two lines) on the clipboard (no history, not roaming) only when the address is a plain HTTP(S) URL and neither value holds an invisible character |
| 26 | Routing section | `h2` + max-w-3xl wrapper | panel without heading | **fixed** |
| 27 | Poll and focus | 30 s interval, keeps data on failure | same | **already right** |
| 28 | Native-only English | none | status and error lines (re-probe running, first 1600 nodes, load/refresh failure, validation messages, generic refusal, "could not be displayed") | **deliberate**: no official string exists for these reliability notes |
| 29 | Dialog and entrance motion | zoom/fade | none | **deliberate** (owner rule) |

String check: the page's text table matches the official `en.json` at `8ae4f4f5a` mechanically: 460 keys decoded from the ICU AST, 460 identical, 0 changed, 0 missing
for `web.admin.nodes.`. Keys added in this pass: `web.admin.nodes.form.envManaged`, `web.admin.nodes.key.deploymentManaged`, `web.admin.nodes.unit.deleteDeploymentManaged`.

## Safety kept and added

Kept from the earlier reviews: strong `If-Match` per captured row, one acknowledged write at a time, no replay, an unknown outcome blocks writes until the rows are read again,
the 30 s poll never disables cards, per-node re-probe without a page lock, a re-probe client without the shared timeout, `null` clears, hidden proxy fields omitted,
enabled-switch restore. Added or verified here: the key lives only in its dialog (never in state, a tooltip, an automation property or a log; the fixture asserts this over the
whole page) and is erased on close, sign-out and page leave; a refused write keeps the draft and shows the server's reason; a refused key or delete shows its reason and needs
no review; a lost answer to a key issue or a delete blocks a repeat; a repeated activation while a request is in flight sends nothing; nothing the page does not offer
(deployment-managed delete/key, an env-managed field) is ever sent. A card that cannot be drawn is logged and reported instead of ending the application.

## Intermittent fixture step

The flake at "key rotation confirmation opened" had two causes: the click arrived while the editor dialog was still closing and was dropped, and the button reference was taken
before a refresh replaced the cards. Product fix found on the way: a dialog that follows another one (the key offer after creating a node) is retried by `ShowDialog` while the
first is still closing instead of silently never appearing. The first fixture answer to the flake (re-clicking until a dialog opened) was replaced after review: it could hide a
regression of that product retry. `OpenDialog` now waits until the page has no dialog and the button is enabled, invokes **once**, waits 8 s (the product's 2 s retry window plus margin)
and fails when no dialog opened; it logs `DIALOG-CLICKS` for each step. All 15 dialog steps needed exactly one click in every run listed below, so no step needed a second-click helper and
`ShowDialog` needed no change.

## Review of `72e3d0c08` and the fixes (commits `87be653d3` source and unit tests, `c4842d505` native fixture)

An independent review found no P1 (no key exposed or kept, no rotate or delete on the wrong node, no managed field writable; the body that omits environment-managed fields is
correct, because `Repository.Update` leaves an omitted field unchanged for every declarable field). Its other items, and what was done. Request bodies and URLs are unchanged.

| # | Finding | Fix | Guard |
| --- | --- | --- | --- |
| 1 | Redaction ran before cleaning (`https\u200E://user:pw@host`, `Bearer` + control character + token, `pass\u200Bword=`) | `AdminPlaybackNodeText.Reason` removes format and direction characters, cleans (2000 characters), redacts, and cuts to 300 last. It is the only caller of the redaction in the touched files | unit: `ReasonCleansBeforeItRedacts…` (5 rows), `ACredentialThatStraddlesTheCut…`; 6 of them fail on the old order (`nodesfix-mutation-unit-r1`) |
| 2 | A card that fails to render left the page half built and erased the warning | `Render` catches a failed draw: cards and groups cleared, `_rendered` reset, a page-level render error kept with Reload until a draw succeeds; later renders and refreshes do not erase it; Reload redraws | native: one card made to throw (the date formatter's service withheld; only n-plain keeps a parsable health time): error and Reload shown, no `NodeEdit_`/`NodeDelete_` left, a later refresh keeps the error, Reload recovers |
| 3 | Key-erasure checks could not fail | the open dialog is searched (text, text boxes, tooltips, button labels, dialog title, automation name/help/description/status) outside the masked box; the `PasswordBox` and both `TextBox` objects are kept and asserted empty after close and after sign-out; Copy settings and Copy key are read back from the clipboard and the clipboard is cleared; the old Playback step keeps its box too. Product fix found by it: erasing the address re-derived and re-filled the settings box, so the order is now address first and nothing is derived once the key is forgotten | native (see the mutation run below) |
| 4 | `OpenDialog` re-clicked | single invocation, see above | native, `DIALOG-CLICKS` |
| 5 | Managed delete not announced as unavailable | item status and full description carry the note (besides tooltip and help text); the name stays "Delete {name}"; focusable | native, through the automation peer |
| 6 | A managed Delete press cleared an existing error | `NotOffered` sets only the feedback; the status line shows the error, then the note | unit `PressingAManagedDeleteKeepsAnErrorThatIsAlreadyShowing` (fails on the old code) |
| 7 | Refused copies gave no reason; Copy key was unchecked | `CheckCopy` returns the reason (missing or invisible or not plain address, missing or invisible key); a reason line is shown, Copy key is checked too; comment corrected. Native-only English | unit `ACopyIsRefused…` (8 rows), native (invisible address, ftp address, no address, invisible key) |
| 8 | Raw device path and backend labels | picker path through the plain-text helper; backend label bounded to 64 characters and plain in the badge, probe title, failure list and re-probe message | unit `ABackendNameTheNodeReports…` |
| 9 | Raw timestamps in the stale-inventory title | `StaleTitle` takes the app's `DateTimeDisplay.FormatDateTime`; unreadable text shows as plain text. A missing settings service fails the render visibly (the page then shows its render error); the Nodes and Playback fixtures both register `SettingsService` | unit `TheStaleInventoryTitle…` |
| 10 | The key dialog could be closed while still being built | the dialog stays pending until built and its close button already reads "I saved the key" | native: the address read is held; a close is refused and the key stays |
| 11 | "Could not confirm a new key" when nothing was sent | `IssueKeyOutcomeAsync` returns Done / NotSent / Refused / Unknown; NotSent says the node changed and offers Retry (reads the nodes, closes); Unknown keeps the unconfirmed text; a capability guard is NotSent and no longer locks the page | unit `AKeyOutcomeTellsNothingSentFromRefusedFromUnknown`; native (row changed under the confirmation: no key request, no review, Retry) |
| 12 | Title after a failure and the drain hint while the request runs | title becomes the plain key title; the hint is hidden while the request runs | native (the key request is held while the hint is checked) |
| 13 | Doc comments on the API class | the class summary sits on the class, the exception has its own | none needed |
| 14 | Record | the C2 row states what the review verified (above) | none needed |

Mutation run `nodesfix-mutation-r1` (temporary, not committed): the three lines that erase the key box, the settings box and the address box were removed from the dialog's close path
and the fixture's end-of-block failure was neutralised so the run reached the old Playback step. Each erasure check failed on its own: "the masked key box is empty after the dialog
closes", "the startup settings box is empty…", "the server address box is empty…", "sign-out erases the masked key box", "sign-out erases the settings and address boxes", and the old step
"key erased after close". The source was then restored (`git diff` empty) before `nodesfix-final-r1`. A second temporary run, `nodesfix-mutation-unit-r1`, restored the old redaction order
and the old `NotOffered`: 6 unit tests failed.

## Shared files touched

| File | What changed | Other users |
| --- | --- | --- |
| `Core/Models/Admin/AdminPlaybackAdministrationModels.cs` | additive fields (`deployment_managed`, `env_managed`, capability flags, `pci_address`, reprobe `node_name`/`error`); backend `Error` renamed `Reason` (the official field; nothing read `Error`) | Playback settings page (`AdminPlaybackSettingsPanel.Conditions.cs`) reads the same hardware types: compiled and its group passes, unchanged source |
| `Core/Services/Admin/AdminPlaybackAdministrationText.cs` | three official keys added, header comment | Playback settings page and Nodes page (isolated catalog); the 457 earlier values are unchanged |
| `Core/Api/AdminPlaybackAdministrationApi.cs` | node refusals carry the server's reason, managed-field checks, client-side refusal of actions not offered | Playback settings support reads share `Send`; their behaviour is unchanged (the extra problem fields are only read by the Nodes controller) |
| `AdminNodesPage.HardwareOptions()` | kept with its signature | used by `AdminPlaybackSettingsPanel.Conditions.cs` |
| `tests/.../AdminPlaybackAdministrationNativeFixture.cs` | class made `partial`; one call to `NodesRefreshAsync` at the top of `RunAsync`; the `SettingsService` the date formatter reads added to the service collection; `OpenDialog` (one invocation, fails if no dialog opens) replaces four click-and-wait steps (the flaky one included); the old key-erasure step keeps the box it checks; `DrainLabel` reads the automation name (the tooltip is now the official drain title) | the Playback settings steps are untouched |
| `docs/parity/admin-monitor-surfaces.json` | three new source files listed | architecture test |

No shared control, `DarkTheme.xaml` or shared destructive style was edited. `installer/build.ps1` needed no change (no XAML added or renamed; `AdminNodesPage.xbf` was already approved).

## Where the new fixture steps are (relative to #234)

All Nodes steps are in the new file `tests/SiloPlayer.NativeRegressionTests/AdminPlaybackAdministrationNativeFixture.Nodes.cs`, with their own server, client, services and shell,
run first by one added line at the top of `RunAsync`. #234's Playback-settings steps (the Advanced section opened before the device list, the device switches disabled while a key is
managed) sit after the line `AdminShellPage.TryOpenHref("/admin/settings/playback")` in the existing file, which this branch does not edit. The two changes touch different lines;
the only shared lines are the `partial` keyword and the service-collection line.

## Tests

* `AdminNodesParityRefreshTests` (new, 51 cases at `72e3d0c08`, 70 after the review fixes): managed fields present, empty and absent; malformed answers refused (non-boolean, non-array, null/empty/control/oversized names);
  capabilities default false on an older server; official `reason`/`pci_address`; every locked field never in a body, all-locked leaves nothing to send, a stored value the form
  would refuse does not block a node-owned field, an unmanaged node sends the whole form; update/delete/key are never sent when the page does not offer them; refusal reason shown
  (409, 422 with field detail, none), not replayed, no review lock; a lost key answer blocks a repeat until a re-read; plain text (control, separators, direction characters, length,
  emoji); key settings exactly the visible values and refused for invisible characters or credentials; presentation rules (re-probe outcome, drain, group degradation, staleness, override,
  backend badge, device picker, CUDA syntax, live samples) and a smoke test over a fully reported node.
* Native `admin-playback-administration` (`AdminPlaybackAdministrationNativeFixture.Nodes.cs`, 107 new checks at `72e3d0c08`, 148 after the review fixes): see the list below and the review section.

Native Nodes block: every node kind listed; deployment-managed key line and delete (note, tooltip, help text, nothing sent); env-managed editor (read-only fields, notes, device switches
disabled, Use cluster default hidden, URL left to right) and the body omitting them; env-managed proxy; legacy node (no managed fields: whole form editable and sent); refused edit keeps the
draft and shows the server's reason; create with the key offered, shown once, in no text/tooltip/automation property, erased after close; rotate with the destructive fill (`#dc2626`),
a doubled activation sends one request, refused (reason, no review) and unknown (blocks, not replayed, reload releases); delete with neutralized name, destructive fill, refused,
unknown and a doubled confirmation sending one delete with the captured validator; a repeated health check while one is in flight sends nothing; empty state; failed read with a working Retry;
sign-out closing an open key dialog and erasing the key; leaving the page dropping controller, dialogs and cards.

## Runs (lane `D:/SiloPlayer-build/admin-completion-lane-b`, task id `admin-families-nov20`)

| Label | Stage | Source | Result |
| --- | --- | --- | --- |
| `nodesref-baseline-r1` | publish-native | unchanged Beta `aca3dcb21` | `admin-playback-administration` 99 PASS, 0 FAIL (the known flaky step did not occur; one run) |
| `nodesref-focused-r1..r3` | focused | work in progress | r1 compile error in a test, r2 one wrong expectation ("Net" label), r3 one test row without `healthy` |
| `nodesref-pn-r1..r4`, `nodesref-hn-r1..r4` | publish-native / host-native | work in progress | r1 compile error (usings), r2 crashed the host (the date formatter needs `SettingsService`, absent from the fixture's services), hn-r1 stale button reference, hn-r2 a doubled check was not in flight, hn-r3/r4 206 PASS |
| `nodesref-final-r1` | final | `606836ff0` | passed; superseded because reviewing its captures showed the key dialog's address fields were not full width (fixed in `72e3d0c08`) |
| `nodesref-native-r1`, `-r2` | native | `606836ff0` | 206 PASS, 0 FAIL each |
| **`nodesref-final-r2`** | final | **`72e3d0c08`** | focused filter 211 passed / 0 failed; full unit suite 10,884 passed / 0 failed / 0 skipped; app build, publish, native host; native `admin-playback-administration` 206 PASS / 0 FAIL, `admin-dashboard` 311 / 0, `admin-configure-general` 188 / 0, `admin-manage-foundation` 36 / 0, `admin-monitor` 113 / 0; source and payload seals unchanged |
| `nodesref-native-r3`, `-r4` | native | `72e3d0c08` | `admin-playback-administration` 206 PASS, 0 FAIL each (the formerly intermittent step passed in both) |
| `nodesfix-focused-r1` | focused | review fixes | 133 passed / 0 failed (filter: `AdminNodesParityRefresh`, `AdminPlaybackNodes`, `AdminPlaybackAdministration`) |
| `nodesfix-pn-r1`, `nodesfix-hn-r1` | publish-native / host-native | review fixes | pn: app build and publish passed, host build failed (missing `using` in the fixture); hn-r1 failed the drain-hint check, which raced the answer (the key request is now held) |
| `nodesfix-hn-r2` | host-native | review fixes | 247 PASS, 0 FAIL |
| `nodesfix-mutation-unit-r1`, `nodesfix-mutation-r1` | focused, publish-native | temporary mutations, restored | 6 unit tests failed; the six erasure checks failed (see above) |
| **`nodesfix-final-r1`** | final | **`c4842d505`** | focused 133 passed / 0 failed; full unit suite 10,903 passed / 0 failed / 0 skipped; app build, publish, host; native `admin-playback-administration` 247 PASS / 0 FAIL, `admin-dashboard` 311 / 0, `admin-configure-general` 188 / 0, `admin-manage-foundation` 36 / 0, `admin-monitor` 113 / 0; source and payload seals unchanged. The known intermittent unit tests were not hit |
| `nodesfix-native-r1`, `-r2` | native | `c4842d505` build | `admin-playback-administration` 247 PASS, 0 FAIL each; every one of the 15 dialog steps needed exactly 1 click in both |

The known intermittent unit tests were not hit in the final run.

## Captures opened

From `D:/SiloPlayer-build/admin-completion-lane-b/results/nodesref-final-r2-admin-playback-administration/` (and the identical earlier `hn-r4` set), opened and compared with the official
layout as read from source: `nodes-refresh-list-1440.png`, `nodes-refresh-list-560.png`, `nodes-refresh-full-1440.png`, `nodes-refresh-full-800.png`, `nodes-refresh-full-560.png`,
`nodes-refresh-failed-560.png`, `nodes-refresh-editor-env.png`, `nodes-refresh-rotate.png`, `nodes-refresh-key.png`. Written but not opened: `list-1024`, `full-1024`, `empty-1440`, `empty-560`,
`failed-1440`. The `full-*` captures render the page content element, so some text (light on transparent) is faint in the PNG; that is a capture artefact. Popup layers (dialogs)
are captured as their own element, not as the shell with the popup over it; the checks on dialogs run through automation.

## Deliberate differences, not established, limits

* Deliberate: see the table (rows 2, 17, 18, 21, 25, 28, 29) and C7 (managed fields are omitted, not restated).
* The Copy settings and Copy key checks read the real Windows clipboard of the test host (written with `IsAllowedInHistory=false`, `IsRoamable=false`, not read back); the flags themselves were not observed.
* Not established: any live Silo server, a real proxy or transcode node, a node that really declares its environment or is created by `SILO_TRANSCODE_NODES`, a phone, assistive technology,
  high contrast (the style switches to system colours; it was not run in high contrast), right-to-left interfaces (the English catalog has no RTL text; left-to-right layout is
  asserted on the controls), DPI other than the fixture's, and no side-by-side comparison with a running official page. No claim of visual or pixel parity is made.
* The official server's `required` list makes `deployment_managed` and `env_managed` mandatory; the native client treats them as optional so an older server keeps working.
* Re-probe progress text, the 1600-node bound and the load/refresh failure lines remain native-only English.
* No push, pull request, installer or release was made; the installed player was not touched.
