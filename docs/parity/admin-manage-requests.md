# Admin Manage Requests — coordinator acceptance, November 17, 2026

The three-route Requests family has compiled and completed scoped unit/native
acceptance. This is not a full visual-parity or live-server certification. The
separate viewer permissions suite has two recorded ancillary failures described
below; neither failure was silently fixed or reported as passing. Final
independent source review precedes the authorized Beta submission.

Desktop base: `248aafd62ed65b868c0e7356926e4d83204649cd`, branch
`t3/admin-request-management`, prepared worktree
`C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-add8c545-bce2b1ccdf324159`.
Version remains `1.2.1181-beta.1` / `1.2.1180.1`; stamping belongs to the pipeline.
The candidate changes 25 tracked/new paths, excluding ignored acceptance
artifacts. It is one independent family and needs no dependency split.

Official contract/review pin: `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`,
read through local `git show`. A direct GitHub main fetch on November 17 resolved
`13dec5cc2d7f7de31c019a0de51faba0f66a980a`. The scoped Requests WebUI, API,
server, plugin-schema and ancillary collection paths have no diff from the pin.
All 723 catalog keys used by `AdminRequestsText` also remain unchanged. Upstream
checkout: `D:/SiloPlayer-work/server-upstream-nov14`; no additional clone was made.

## Contract and implementation map

All three leaves register through `AddAdminRequests()` and the real
`AdminShellPage`/Frame. `AdminRequestsPage` owns its route parameter, dialogs,
staged General draft and `AdminAuthorityLifetime`. There are no placeholder
route registrations. Navigation keeps the shell's suppressed transitions and
document-scroller gutters.

| Route / workflow | Official pinned sources | Native source |
| --- | --- | --- |
| `/admin/requests`: four counted views, title/TMDB and media/account filters, bounded Load more, request/target/download state, events, title/library links, approve/decline/retry/cancel, selected-row approve/decline and progress/failures | `web/src/pages/AdminRequests.tsx`, `pages/admin-requests/{RequestQueue,RequestQueueDialog,requestQueueModel,queueParts}.tsx` (model is `.ts`), `hooks/queries/admin/requests.ts`, `internal/apiv2/{admin_requests,admin_request_queue}.go` | `AdminRequestsPage.Queue.cs`, `AdminRequestsController`, `AdminRequestCountStore` |
| `/admin/requests/discover`: complete layout replacement, enable, order, delete, restore, stock rows, chart/filter/list/brand rows, first-page preview, MDBList search/popular templates/public link, list order and source state | `web/src/pages/AdminRequestDiscover.tsx`, `pages/admin-requests/{DiscoverRowDialog,DiscoverFilterFields}.tsx`, `discoverLayoutModel.ts`, `internal/apiv2/admin_request_discover.go`, `internal/requests/discoverrows/*` | `AdminRequestsPage.Discover.cs`, shared field controls |
| `/admin/settings/requests`: General approval/default limits/window/watchlist/dual quality, server connections/probes/plugin fields and guarded delete, Standard/Advanced mode, ordered rules/presets/include/exclude conditions, HD/4K destinations/overrides, Everything else, test-title/requester and evaluation trace | `web/src/pages/admin-settings/{RequestsSettings,RequestServers,RequestRouting,RequestRoutingMode,RequestRuleEditor,RequestRuleConditions,RequestRulePresets,RequestRouteFields,RequestRoutePreview}.tsx`, `requestServerModel.ts`, `requestRoutingModel.ts`, `requestRoutingPresets.ts`, `requestIntegrationMediaTypes.ts`, `internal/apiv2/admin_request_routes.go` | `AdminRequestsPage.{Settings,Servers,Routing,Fields}.cs` |
| Exact transport/editor shapes | `web/src/api/v2/adminRequests.ts`, `internal/apiv2/{admin_requests,admin_request_discover,admin_request_routes}.go`, official v2 fixtures | `AdminRequestsApi`, `SiloApiClient.AdminRequests.cs`, `AdminRequestDocument`, `AdminRequestRules` |
| Admin English messages | `web/src/locales/en.json` compiled message AST at the pin | `AdminRequestsText.cs` (verbatim patterns restored from that AST; shared ICU formatter) |

Endpoint families are `/api/v2/admin/requests{,/counts,/capabilities,/{id}/events,
/{id}/approve,/{id}/decline,/{id}/retry,/{id}/cancel}`, `/admin/request-settings`,
`/admin/request-discover/{layout,preview,watch-providers}`,
`/admin/request-integrations{,/{id},/{id}/options}`,
`/admin/request-routing`, and `/admin/request-routes{,/{id},/order,/titles,/preview}`.
Ancillary bounded reads use Admin users/plugin installations, Admin collection
capabilities/templates, and the same public-list search endpoint used by the
official Discover editor. Account/group override editors remain with Users/
Access Groups; this family edits their server-wide defaults only.

## Safety and resource behavior

- Read/editor documents and route-order snapshots belong to the exact authority
  instance, epoch and account that read them. Every request checks a captured
  lease before sending and after reads. Retiring/suspending a page hides its
  dialog, clears staged values and secret controls, and discards late results.
- Strong ETags are required for canonical settings/layout/routing/integration/
  rule reads and their guarded writes/deletes. A 412 retains the current draft
  and blocks another save until explicit current-value review. Fallback rules
  cannot be deleted. Integration deletion explains known route blockers and
  still relies on the server's guarded decision.
- Administrative mutations, probes and previews are sent once with token-refresh
  replay disabled. Unknown outcomes require reconciliation/review. Bulk runs at
  most four requests concurrently, keeps each failure and never automatically
  retries it. Queue/count reconciliation follows an action or completed batch.
- Each success/problem body is bounded to 4 MiB and 30 seconds. Queue pages are
  50 rows; the visible window stops at 500. Cursor, missing/duplicate IDs and
  repeated continuations are refused. Integrations/users stop at ten pages/500
  records; plugin lookup stops at 200; routing stops at 202 canonical records;
  history stops at 200. No media-catalog walk is performed.
- The registered Queue badge shares counts with its page, coalesces concurrent
  reads, caches for ten seconds and polls at most once per minute while the
  Admin sidebar listens. It clears on authority retirement/failure and stops
  polling when the last listener leaves.
- Saved credentials never populate password boxes. Blank secrets preserve saved
  values; replacement values are cleared when their dialog/owner retires. The
  transport diagnostic flag suppresses exception text for this domain, including
  a refreshed read. Inline errors use safe messages and field locations rather
  than echoing credentials or signed addresses.

## Coordinator corrections and observed RED

The worker test-only checkpoints under `.codex-tmp/admin-requests-20261117/`
are historical source checkpoints, not patches for the final accepted source.
No checkout was reset to obtain RED. All receipts and earlier captures remain
under `D:/SiloPlayer-build/admin-completion-requests`.

- `review-red-r1` exposed two missing JSON body fields. Requests now serializes
  its `JsonObject` to a wire `JsonElement` at the domain transport boundary,
  rather than reflecting the object through the shared request serializer.
- `review-red-r4` exposed a real retirement-time WinRT callback-marshalling
  failure. Dialog cancellation now queues a managed closure which validates
  the captured dialog identity and actual loaded state before hiding it.
- `review-red-r5` observed all seven targeted behavioral regressions failing:
  staged plugin fields lost by Test; disabled numeric bounds still enforced;
  Anime override not selected; stale genre chips after media change; Add rule
  enabled without an HD fallback; downloading rows not refreshing; and retired
  action capture escaping its exception boundary.
- The fixes separate live draft capture from Save validation, refresh dynamic
  choices in place without replacing mounted dirty/secret controls, honor
  numeric enable flags, seed presets before control construction, replace
  media-dependent filters, explain/gate the fallback prerequisite, and poll
  active downloads once per settled thirty-second interval under the visit owner.
- `review-green-r6` passed all seven correction scenarios; the older management
  fixture then raced the queue refresh after cancelling its dialog. One
  fixture-only readiness barrier fixed that sequencing; `review-green-r7`
  passed the five selected admin groups on the preserved r6 product bytes.
- Capture inspection found a clipped wide-row Details button. `layout-red-r8`
  failed the added real-layout assertion on the preserved product. Intrinsic
  action-column sizing fixes it; the assertion passes on the final payload.

## Historical scoped acceptance, r9 through r11

`final-green-r9` freshly rebuilt the main unit project nonincrementally, then
passed 125 focused and 8,057 full unit cases with zero failures/skips. Both
counts include one ignored coordinator-only physical-root/Core portable-PDB
checksum proof; that proof is not a committed test addition. The same chain
passed a nonincremental x64 app build, self-contained publish, nonincremental
native host build and the five scoped admin groups. Its overall receipt is
failed because the additional viewer permissions group failed, as recorded below.

`scoped-final-r11` reused those exact published and host bytes, verified their
identity, and passed all six selected native groups:

```text
admin-requests-corrections
admin-requests-management
admin-manage-foundation
admin-configure-general
admin-monitor
request-discover-current
```

The production-shell fixtures exercise queue/details/decline, guarded Discover
editing, settings/412 retention, masked credentials and close/retirement scrub,
explicit connection probing, fallback destinations, title preview, capability
absence, real Frame departure, all seven corrective scenarios and wide action
visibility. Nine primary Requests PNGs cover wide/compact queue/settings and
actual editor/decline dialogs. They were inspected; final repeat images were
either byte-identical to inspected images or inspected again. This establishes
those synthetic states at the tested DPI, not complete visual parity.

Final proof:

- Receipt: `receipt-scoped-final-r11.json`, status `passed`.
- PID 32596 acquired at `2026-11-17T16:34:24.1166067Z` and released at
  `2026-11-17T16:36:10.9936831Z`, with exactly one nonblocking `WaitOne(0)`.
- Inclusive preflight: 31.91411493718624 GiB generated; both drive free-space
  floors passed. No cleanup occurred. Each child was contained, deadline-bound
  and fail-stopped; release occurred in `finally`.
- Source: 2,327 build-input entries, SHA-256
  `e732d6a3380e051d64dc0dec183c0f4fa7feb72c1154fa8a944bb1fb4178e051`.
- Published payload: 582 files, manifest SHA-256
  `b2f875a4fdca7e6e0118460f2fe23d246f907b612969c344e20d83426701e4d0`.
- Published, reference and host app DLL/PDB/PRI bytes matched. Source and full
  published-file manifests reverified at the successful end of r11. The unit
  proof covers the physical test root/Core PDB, not every app/host PDB document.
- The earlier product/host baselines remain in `frozen-review-red-r5` and
  `frozen-review-green-r6`; receipts, logs and captures retain their original labels.

Documentation-only acceptance updates occur after that source seal; the seal
deliberately covers build inputs rather than these Markdown records.

## Final review corrections and acceptance, r12 through r14

Both frozen reviews confirmed five distinct findings: queued progress did not
poll; plugin numbers rejected fractions/lost fractional bounds; both previews
omitted effective overrides; blank plugin passwords dropped saved credentials;
and failed canonical reconciliation reopened unresolved writes.

`review-red-r12` rebuilt only the host against sealed r11 product bytes. Seven
new scenarios failed at those behaviors; the original seven corrections passed.
The fixes preserve a blank saved plugin secret only for an unchanged connection,
keep password controls empty, honor numeric schema types/finite decimal bounds,
poll request/target download presence (including queued rows), and show resolved
effective overrides on both preview surfaces. Preview metadata reads remain
bounded and owner/cancellation-scoped; secret fields are excluded. A separate
canonical-review barrier now survives failed reads, exposes recovery and blocks
another mutation until its matching reconciliation succeeds, without replay.

`review-green-r13` nonincrementally rebuilt units, the x64 app, self-contained
publish and host. All 125 focused / 8,057 full unit cases passed (zero failures/
skips, including one ignored source-root/Core-PDB proof), followed by all six
selectors listed above. All fourteen corrective scenarios passed: real thirty-
second queued request/target refresh, fractional Test/Save and bounds, saved and
changed-connection credentials, named previews and failed-read write admission.

r13 capture inspection found its Settings preview offscreen. Only the fixture
then changed to bring the preview and masked draft credential into view.
`capture-final-r14` rebuilt the host and passed the same six selectors on the
identical sealed r13 product. All eleven primary captures were inspected or
byte-matched to inspected images; both preview captures visibly show the folder,
named quality profile and series type. This is not complete visual parity.

- r13 PID 23680: ACQ `2026-11-17T17:46:13.9847250Z`, REL
  `2026-11-17T17:53:45.2144309Z`; inclusive preflight 32.28527884092182 GiB.
- r14 PID 27040: ACQ `2026-11-17T17:55:34.0002725Z`, REL
  `2026-11-17T17:59:06.9558427Z`; inclusive preflight 32.6739758271724 GiB.
- Each attempt used one nonblocking `WaitOne(0)`, drive floors, containment,
  deadlines, fail-stop and final release. No cleanup or installed-player action.
- r13 source seal: 2,327 build inputs,
  `8303f0dae6e5f9db8fe109e4286ebfd863fcec18fe691198b99c056b2975ffa8`.
- r14 source seal: 2,327 build inputs,
  `9956fbac00235e7e81983310187457f19b7fecb1b7c14178f2f2bc83dc2bb167`.
  The sole r13-to-r14 build-input delta is the capture-positioning fixture.
- Identical r13/r14 payload: 582 files,
  `072282513a1e4cc2c6c8b4b4fd44b3c669ee1eda46c10c5446385b86284e40e2`.
  App DLL/PDB/PRI equality across publish/reference/host and complete source/
  payload rechecks passed; no all-app/host-PDB document proof is claimed.
- Frozen r11, r12 and r13 payloads/hosts join the preserved r5/r6 baselines.
  Historical failed receipts and original captures remain intact.

## Review corrections r16 and r17, Beta merge r18 and recheck r19 and r20

A further frozen review after r14 reported four defects. `review-red-r16`
rebuilt only the host against the r14 product and observed all four new native
scenarios failing: a hidden saved plugin credential crossing to a replacement
connection's Test, a held earlier title overwriting the latest route preview,
the same for the metadata preview, and a closed unknown create dropping the
canonical review barrier. `review-green-r17` corrected five page files and
passed 125 focused and the full unit suite plus all six native groups on base
`248aafd62ed65b868c0e7356926e4d83204649cd`.

On November 20 the family was committed and current Beta
`f35bb7e59d14f8e7f2e53bac982953fa3d5dc7da` (`1.2.1194-beta.1`) was merged
normally, with no rebase. Two documentation conflicts were resolved by keeping
both sides. `beta-merge-r18` then passed 122 focused and 9,731 full unit cases
and the same six native groups on the merged source. The focused count fell by
three because Beta removed three "unsupported page" navigation cases for pages
that now exist.

An independent read-only recheck of the four corrections confirmed three and
reported two P2 defects, both now fixed:

- A create that the server accepted but answered without a usable record
  identity surfaced as `ArgumentException`, which the editor treats as "nothing
  was sent": the review barrier was cleared and Save re-enabled, so a second
  click could create a duplicate. `AdminRequestsApi.CreateAsync` now reports it
  as an unconfirmed result, which keeps the barrier until canonical review.
- A superseded title whose slow preview read later failed raised a page notice
  over the valid preview on screen. That late failure is now ignored when the
  selection is no longer current.

`recheck-red-r19` ran the two new native scenarios against the r18 published
app and both failed. `recheck-green-r20` nonincrementally rebuilt everything on
the corrected source and passed 125 focused and 9,734 full unit cases with no
failures or skips, the app build, publish, host build and all six native
groups, including all 16 corrective scenarios.

- r18 PID 4660: ACQ `2026-11-20T13:53:06.8636243Z`, REL
  `2026-11-20T14:01:13.5685624Z`.
- r19 PID 43600: ACQ `2026-11-20T14:03:45.5098246Z`, REL
  `2026-11-20T14:06:48.1379699Z`; failed as intended.
- r20 PID 45508: ACQ `2026-11-20T14:10:50.5933076Z`, REL
  `2026-11-20T14:19:07.6824466Z`.
- r20 source seal: 2,492 build inputs,
  `9f3521a2079c4df3fdf0b643198adab18ddcc3bea48d806734b354d0821f9e08`.
- r20 payload: 582 files,
  `a4cf1e8477a1a892cce426abddcaf8ec53f0d328f612da1e26008de96c4805af`.

The recheck also noted, without counting it as a defect, that the saved server
API key is not required again when only the URL changes; whether a stored key is
reused for a changed URL is decided by the server and was not observed here.
No captures were re-inspected after r14.

## Ancillary failures and remaining limits

The broader `request-permissions-review` selector is not accepted:

- `final-green-r9` failed in the unchanged `RequestRemovalNativeFixture.cs:567`
  primary-button lookup, reached from route-refresh coverage at line 351.
- One bounded same-payload continuation, `ancillary-r10`, passed viewer
  `request-discover-current` but failed the unchanged
  `ProfileRequestsNativeFixture.cs:267` editor-viewport assertion instead.
- Neither affected viewer fixture nor viewer production source changed in this
  family. These are observed ancillary failures, not proof that the entire
  broader suite passed or proof of their behavior on an unmodified baseline.
  They remain disclosed and were not expanded into unrelated source fixes.

Native adaptations include explicit Preview/Test/search actions, chip/code
entry alongside pickers, Up/Down reorder and WinUI dialogs rather than WebUI
popovers. The server remains the final validator for plugin schemas, filters
and connection/routing compatibility. English Admin messages do not certify
all locales. Live router/plugin interoperability, real large-server timings,
Narrator/IME, pointer-device behavior, all-DPI accessibility and complete
rendered parity remain unverified.

The installed player, its settings, Requests977 and all pipeline-owned PRs remain
untouched. A read-only r12 observation showed installed PID 31132 rather than
the earlier PID 39668; no process or installation action was taken.
No installer ran, version was stamped, branch rebased,
merge into Beta performed or stable release published. Review, QA and merge of the
Beta pull request belong to the pipeline.
