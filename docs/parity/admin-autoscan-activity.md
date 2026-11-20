# Autoscan Activity — source-only implementation candidate

## Identity and scope

- Prepared checkout: C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-1a1ebc57-1f3df4c36cc2df20.
- Branch: t3/admin-autoscan-activity; unchanged Beta base: 248aafd62ed65b868c0e7356926e4d83204649cd.
- App version remains 1.2.1181-beta.1. No version, release, installed-player, pipeline PR, or publication changes.
- Official Server/WebUI reference: 217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb, read as pinned Git objects from D:/SiloPlayer-work/server-upstream-nov14, not that checkout's current HEAD.
- Ownership is exclusively /admin/autoscan/activity. Sources and Settings remain independent; this candidate neither imports nor registers their unmerged pages.

This candidate includes the actual WinUI page, its independent API/models/controller/text, production registration, native XBF/source allowlists, and authored unit/native fixtures. It is not a helper-only or research-only handoff.

## Official references

Read the following paths at the exact official pin:

- web/src/pages/admin/autoscan/ActivityPanel.tsx; web/src/pages/AdminAutoscanActivity.tsx.
- web/src/api/v2/adminAutoscanEvents.ts; adminAutoscanScans.ts; adminAutoscanInspection.ts; adminAutoscanSources.ts; adminAutoscanConnections.ts; adminAutoscanAvailableSources.ts.
- internal/apiv2/admin_autoscan_events.go; admin_autoscan_scans.go; admin_autoscan_inspection.go; admin_autoscan_available_sources.go; internal/autoscan/repository.go.
- web/src/hooks/queries/useAutoscan.ts; web/src/hooks/queries/admin/scans.ts; web/src/components/RealtimeEventsProvider.tsx.
- web/src/lib/scanRuns.ts; web/src/lib/autoscanLabels.ts; web/src/pages/admin/autoscan/sourceDisplay.ts; sourceForm.ts; web/src/locales/en.json.

## Implemented surface and contracts

Scans is the initial history tab; Polls has its own retained query/status/page-size/cursor scope. Both include search with a 300 ms debounce, exact status choices, 25/50/100 row sizes, clear/reset, page summaries, previous/next, explicit restart, initial loading, filtered/unfiltered empty states, failure and retry. Wide rows become stacked cards below 1024 content DIPs. There is no slide transition added to the existing shell/Frame.

History GETs use /api/v2/admin/autoscan/scans or /events with limit, cursor, status and q; only events also accept source_id. No offset or sort parameter is invented. Scans preserve the official lifecycle-timestamp/id descending order, events completed-timestamp/id descending. The official page has no interactive history sort control. Live queue rows follow its running/accepted, mode and path ordering, with eight rows per queue page.

Scan rows expose scope/path, source/library navigation, results including skipped/protected-offline counters, error, identity, linked poll and timestamps. The linked-poll control searches by scan ID, which the official repository supports; it does not assume event IDs are searchable. Polls expose webhook delivery, counters, errors, duration/time, lazy path details, rewrites, all known reasons, opaque future outcome/reason values, linked-run counts, truncation and created run results. Source links are enabled only when that separately owned route is registered. Library links open the released Libraries route, not an invented editor route.

Live status comes from /api/v2/admin/autoscan/status. Active autoscan details come only from the released shared scans realtime channel and its cached snapshot. Status and the selected history refresh at the official 15-second interval; the inactive history is not polled. The one-shot scheduler waits for the current work, skips superseded generations and stops on hidden-window/unload/authority retirement. Terminal scan events update the queue and invalidate bounded label lookups, without directly refetching histories. Label invalidations coalesce during an in-flight read; remaining dirty work is picked up on the next normal refresh.

Cancellation POSTs /api/v2/scan/cancel with the exact string library_id. It is library-scoped, not falsely presented as a run-only cancellation. The existing authority-bound transport sends the write once without authentication replay. Unknown answers and 409/412 conflicts retain a per-library review gate across ordinary refresh and visibility changes. Explicit reconciliation is a single-flight status read, never a write. A newer complete shared snapshot, authoritative global zero, or a terminal target event with no remaining target-library rows allows the user to review and explicitly request another cancellation. Failed reconciliation stays unresolved with visible feedback; no success/completion is inferred from a request receipt.

## Bounds and native adaptations

- Reuses AdminMonitorAuthority/MonitorAuthorityLease, AdminCursorPager, AdminCollectionWalk and SiloApiClient.AdminLibrarySendAsync. Identity/server/profile retirement cancels and rejects late results; the page owns and releases its scans subscription.
- Existing transport bounds: 4 MiB response and 30-second deadline. Each history retains one current page and at most 100 signed cursor positions; duplicate identities, invalid states, unsafe numeric-string identities, malformed envelopes and repeated continuations are rejected. Live total counts are not represented as snapshots; a shrinking last page moves back and rereads a valid position.
- Source/connection/plugin lookups use limit 100, at most five pages/500 rows each. Libraries reuse the released one-response/1,000-library bound. Partial/failed names remain explicit and IDs remain available. No media-catalog enumeration occurs.
- The active queue retains at most 500 distinct autoscan rows after filtering other triggers; overflow remains visibly partial until a complete snapshot. Each event's run list is limited to 500 for native rendering safety; an over-limit response fails visibly rather than silently omitting runs. Change logs enforce the official maximum of 50, preserving changes_truncated and the original count.
- English admin ICU strings are isolated fallbacks for keys not in the released generated catalog; existing translations win when present. This source-only candidate does not rewrite generated localization output. New bound/access/reconciliation notices are explicitly native text. Paths, errors, labels and dynamic messages pass the existing credential/signed-URL redactors before display.
- The separate native status/source labels models contain only fields needed by this page. Running-poll/event/library IDs remain the published positive decimal string identities within the official JavaScript-safe boundary; run IDs are opaque strings.

## Authored versus observed verification

Authored, NOT EXECUTED:

- AdminAutoscanActivityTests and AdminAutoscanActivityLifecycleTests: exact envelopes and wire parameters, additive outcomes, unsafe IDs/states, duplicate/oversized/self-continuing pages, independent cursors/filters, 100-page depth, retired/hidden reads, 15-second scheduling, bounded/deduplicated realtime queue, source labels, result/plural/redaction behavior, uncertain/conflicting cancellation, terminal-target review and single-flight failed reconciliation.
- AdminAutoscanActivityNativeFixture, selector admin-autoscan-activity: real production registration through outer Frame, AdminShellPage and inner Frame; live queue/progress; both cursor chains; lazy rewrite/future-value/truncation details; related-poll filtering; debounced search; empty/history/status failure and retries; uncertain cancel/reconcile; authored 1440/1024/800 captures; held read released only after actual page and shell Unloaded barriers, before restoring services/title/window content.

Observed here: read-only pinned source inspection and Git diff/identity/whitespace inspection only. No .NET invocation, restore/build/test/native/browser/app launch, mutex acquisition, generated output, screenshot, installer, commit, push or PR occurred. The authored assertions and capture calls are not runtime evidence. Compilation, native rendering, full visual parity, all-DPI and live-server compatibility remain for the coordinator's authorized serialized lane.

## Coordinator acceptance — do not run in a source-only lane

Recheck the integrated source/version and bind the publish and fixture to those exact bytes first. Remeasure the inclusive generated-output budget (maximum 50 GiB, at least 50 GiB free on C and D). Use the approved containment/deadline harness and one nonblocking SiloOct18Build WaitOne(0) acquisition per authorized attempt, record actual ACQ/REL, release in finally, and do not wait/retry if held. Preserve prior payloads and the installed player.

From the final integrated checkout, the focused authored checks are:

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj --filter "FullyQualifiedName~AdminAutoscanActivityTests" -nologo
powershell -NoProfile -ExecutionPolicy Bypass -File installer/build.ps1 -Configuration Release -PublishDirectory <fresh-approved-install-layout-root> -InstallerOutputDirectory <fresh-approved-installer-root>
powershell -NoProfile -ExecutionPolicy Bypass -File tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory <exact-source-bound-published-version-directory> -Only admin-autoscan-activity
```

The installer command requires the existing signing prerequisites and does not authorize installing or publishing. The native runner itself builds its fixture host and generates captures: only the runtime coordinator may execute it. Review every capture, verify unload barriers and source/XBF acceptance, then run broader regression acceptance as appropriate. Integrate the eight production source allowlist additions and the single XBF addition without dropping other workers' entries. Do not infer a qualifying delivery or runtime pass from this source handoff.
