# Busy subtitle sidecar recovery

Approved spec: ../specs/2026-11-02-server-parity-design.md. Base: 6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af; upstream: bf53b65450244751013a2965b39da4111d9a0f72.

- [x] Reproduce real native busy-selection failure before product changes.
- [x] Add bounded async 503/Retry-After reader and behavioral transport tests.
- [x] Defer acceptance until download succeeds; retire operations on choice/load/owner changes including same-track ABA; preserve supported fallback behavior.
- [x] Run focused/full tests, fresh Release publish and native busy/ownership/continuity fixtures through shared mutex wrapper.
- [x] Record evidence and compatibility with independently frozen PR2, commit locally and hand off for independent review/integration.

PR2 touches the same selection/preload methods: parent owns integration; retain its unavailable/identity gates around this transaction. No merge/cherry-pick in this branch. No version changes or installer/publication.

Ruling: use this scoped ledger and shared Windows verification wrapper rather than shell skill scripts, preserving all evidence as required by the owner.

## Lifecycle trace and implementation decisions

- Native overlay closes its flyout and calls SetSubtitleTrackAndPersistAsync; OSC/Lua optimistically changes active_subtitle then calls SelectSubtitleByServerIndexAsync. Both need the same deferred acceptance gate and retained OSC feedback on busy exhaustion.
- FILE_LOADED initializer calls SelectSubtitleByServerIndexAsync(..., false); LoadSubtitles and StopSubtitleSync retire per-file downloads. BeginMpvLoad calls CancelPendingLoadedMediaInitialization before replacement load, covering its settings-await gap.
- PlaybackManager.RecordLocalSubtitleSelection changes replacement-plan intent immediately. Busy prefetch must precede this call, not roll it back to a previously pending value.
- The prefetch URL comes exclusively from manager.GetSubtitleUrls; it carries URL authority without an account header. Reader retains known text formats and the 16 MiB declared/streaming cap. Bitmap and other existing fallback routes remain outside busy retries.
- Ruling: use a per-selection cancellation owner plus captured API context. Awaiting read does not change the accepted index, manager intent or persisted preference. A newer selection retires the old owner even for equal indices. Load/close cancels the read; captured profile context retires it during wait as well.
- Independent PR2 review: its unavailable/route checks must execute before starting this transaction; its native identity resolver changes only when prefetch is needed. No PR2 source is imported here.

## Candidate verification and correction ledger

- Reader red: single-read baseline failed 12/14 cases; reader-green passed14/14. Expanded final transport16 cases and source checks passed in full-candidate3074/3074.
- Native corrected-red: frozen baseline replaced the accepted server selection after503. Initial native-red hung because remote sub-add also blocked the fixture's dispatcher-based listener; preserved as diagnostic only, listener moved to independent background acceptance.
- Busy candidate:13/13 native cases passed, including exact4attempt budget, retained actualSID/manager/prefs, pause/resume/seek and operation retirement/ABA.
- Candidate native ownership: early OSC publication during accepted pending loads is a real regression. Gate native feedback until the actual file owner is ready.
- Pending same-content reload intent must survive while its old read is canceled. Preserve existing latest-request invariant: after compatible initialization, re-resolve the latest request, apply it, and persist once. Do not revert expectations to losing the request. Accepted preference ownership is separate from per-file initializer/read ownership.
- Deterministic accepted-reload fixture holds only the original manual caller's continuation after BeginMpvLoad until actual FILE_LOADED initialization completes, with bitmap/Off/text and genuine newer-manual negative control. First fixture attempt held an earlier nested continuation before the load; corrected the hold predicate to observe file-load generation. Awaiting corrected baseline evidence before owner correction.
- Manual-reload-gap fixture extended to assert no stale application or preference before held initialization completes, then release settings and assert eventual latest-request application/persistence once. This preserves manual intent while enforcing acceptance before durable save.

## Final local verification and protocol ruling

Product commit: 9e05c6a2fa2b1ae5e19fb21eaa57757a51ef4e4e. Frozen Release DLL: 04512FDFB136C2B138506E1725662F206F011C9D8229179DF32F9E3085E4D5E8.

Focused95/95 and full3075/3075 passed. Final native groups passed: busy13, ownership29, accepted-reload4, burn-readiness/changed-grant4, plus the existing multi-step subtitle-adoption fixture. All target the same published bytes. See the external subtitle-busy-report.md for exact run receipts, fixture-control corrections and historical candidates.

Ruling: server replacement commits before response and may close/revoke the predecessor (official playback_v3.go5415–5494,3288–3338,4457–4490). Track-change can change file on a terminal refusal (planReplanV3 and replanAllowsAlternateFileV3). Preserve the valid new server revision and its existing recovery; do not invent rollback/lease guarantees. Cross-file busy leaves actual native captions Off with actionable retry guidance and no false preference success. Same-file carry is strictly scoped and consumed once.

The controlled newer-choice cross-file regression failed with invalid-index recovery and old-file preference1. ReplanRecordedSubtitleSelectionAsync submits the existing mapped intent without replacing its original source identity; the corrected path requests0→8 directly and persists only applied8. A Core regression also verifies that submitting an already recorded choice preserves its original exact variant on return from a reordered version.

Fixture controls now release held replacement media before awaiting application/persistence completion. The retired bitmap case issues newer Off before awaiting the retired task and asserts only applied Off persists; the old bitmap is absent from the replacement inventory. HeldContinuation preserves its synchronization context through the added preflight await, so the accepted-preference race remains deterministic.

Upstream drift observed after the bf53b65450244751013a2965b39da4111d9a0f72 playback pass:90f88f2d6076daefb8b5ae77893a41e9de6e20d9, calendar changes; playback/subtitle contracts unchanged per preserved root refresh receipt. Original playback references stay pinned.

Independent Standards/Spec review and reconciliation with the identity source that actually reaches Beta remain parent/pipeline gates. Assigned identity refs803cf6ab2f9ad94bd2ea4956fe59f50a73db395d/31abdab were reviewed read-only; observed pipeline head09572257348eb6237eca6e78e0fe48894f1c6683 is not imported. This branch is independently verified on original Beta6d3ac20, not a combined identity/busy readiness claim.
