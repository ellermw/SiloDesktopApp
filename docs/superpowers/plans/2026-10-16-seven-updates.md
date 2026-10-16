# October 16 seven upstream updates — implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the ordered tasks and review gates. Preserve the current uncommitted baseline; do not commit or publish.

**Goal:** Implement all seven owner-approved update groups in their specified order, verify the integrated native desktop candidate and deliver one local installer for owner testing.

**Architecture:** Extend the existing WinUI/libmpv, typed Settings API, captured request/playback authorities and mounted media reconciliation. Reuse existing profile, notification and menu UI conventions, matching pinned WebUI layout/data/interaction states. Keep optional LAN and network-aware Auto quality outside this pass.

**Tech Stack:** .NET 8, WinUI 3, bundled libmpv, xUnit, isolated HTTP/socket/native fixtures, supported installer/build.ps1.

**Spec:** docs/parity/2026-10-16-upstream-delta.md (all seven groups explicitly approved by the owner). Detailed playback references: .codex-tmp/upstream-oct16-playback-audit.md.

## Global constraints

- Official source refetched immediately before this pass: `9cc3091d6ec19996ee4f7149c7b72bfad3a0323f`, unchanged from the approved audit. Use explicit Git objects; reference checkout HEAD is historical.
- Start from actual uncommitted 1.2.260 in canonical D:\SiloPlayer. Preserve its frozen installer/payload, previous evidence, account data and unrelated changes. Do not reset/checkout old main over it.
- No Git commit/push/publication, production changes, app installation or closing the running player. All mutations use isolated fixtures. Never expose settings values, tokens or signed URLs.
- Task order is 1 through 7. Each task records red/green behavior checks, implementation, source review and verification scope before advancing. Continue without additional authorization questions.
- Serialize dotnet builds/tests with named mutex `SiloOct16Build`; release before native fixture execution. Use unique test output/payload directories.
- Final intended version 1.2.267: seven completed update groups after 1.2.260. Do not claim completion from a version change or test count alone.
- No claim of complete historical visual parity, phone interoperability, historical recurring direct-play buffering repair or the intermittent Next Up removal root cause.

## Review focus

- A stale profile/server/playback owner must never publish state or submit a mutation for its replacement (Tasks 1,3–7).
- Older servers and absent optional capabilities must hide unsupported actions and never receive new revision-only setting scopes (Tasks 3–6).
- Paused commands, late transport arrival, stalled guests and repeated seeks must preserve current room intent without a replan loop (Task 1).
- Trakt retirement preserves existing/manual collection titles and unrelated provider plugins/history; stale imports must not resurrect unsupported rows (Task 2).
- Native pending/error/empty/paged states must be visible and usable at narrow/1080/ultrawide/4K sizes, with stable Home artwork (Tasks 3–7).

## Task 1 — Watch Together timing and convergence

Files: Core/Services/WatchPartySyncController.cs; new focused Core room-clock helper; Services/WatchTogetherCoordinator.cs; ViewModels/WatchTogetherViewModels.cs; existing WatchPartySync/TwoClient/V2 tests; published/native room fixtures.

Interfaces: preserve existing controller/native playback adapter. Add clock sampling with explicit ping send/receive timestamps; state reports gain optional applied command ID; queued monotonic execution times may be in the past so projection remains accurate. Existing readiness IDs and authority/session fences stay.

- [x] Write failing behavior checks for fastest of last eight RTT samples, late-playing projection, paused targets, 0.4/0.1-second start/settle bands, quantized 1–4% convergence through five seconds, no periodic-loop seeks, applied report ID and session replacement.
- [x] Run targeted tests and retain expected failures; implement current clock/ping cadence, scheduled command projection, convergence and report propagation through real VM/coordinator seams.
- [x] Preserve explicit-seek/reload budget/buffering behavior; verify burst seek intent, command replacement, long-latency host/guest/readiness and cancellation with deterministic multi-client tests and native service where applicable.
- [x] Review against pinned roomClock/roomSyncCatchup/useWatchTogetherPlaybackSync/spec; correct findings and record exact checks before Task 2.

## Task 2 — Trakt retirement

Files: Core/Api/CollectionsApi.cs; Models/Collections/CollectionImports.cs; HomeLayoutMerge/Transfer/SectionWritePolicy services; Collections/CollectionEditor view models and view partials; RecipeGalleryDialog; Settings labels; existing import/layout native tests.

Interfaces: retain generic provider APIs and supported MDBList/TMDB/manual imports. Remove obsolete builtin Trakt imports/templates/guards rather than inventing a server migration. Keep existing generic legacy-data parsing safe where unsupported payloads can still be loaded from files.

- [x] Write failing behavior checks for migrated manual collections and unsupported legacy Home import/reimport handling; preserve unrelated layout IDs, histories and provider registrations.
- [x] Remove obsolete routes/actions/copy, align source/schedule/editor logic and current Home transfer rules with upstream.
- [x] Run collection/import/layout regression tests and native editor/transfer checks; review no unsupported path can be offered or resurrected before Task 3.

## Task 3 — Profile subtitle appearance and timing

Files: Core typed Settings helpers/new focused subtitle preference service; SettingsViewModel; SubtitleAppearanceDialog; PlayerOverlay; PlayerService subtitle preference integration; corresponding Core/published/native tests.

Interfaces: shared captured-authority preference read/save/reset for appearance and offset. Appearance saves profile + clears current device shadow; reset profile/device. Offset profile scope only at manifest19+, device fallback below19. Keep explicit Your Devices overrides and downloaded-subtitle media sync distinct.

- [x] Red tests: typed object/profile cleanup/reset404 tolerance; offset ±10000 bounds, revision19/profile versus older/device, captured-owner cancellation; appearance/timing apply at load and remain consistent between Settings/player.
- [x] Implement shared writers and resolved playback application; update controls/copy/pending/error/realtime behavior without overwriting local versus SiloCast identity.
- [x] Green behavior/native UI and published playback regression checks; review then Task 4.

## Task 4 — Not Interested

Files: new Catalog API partial/models/policy as needed; shared UserState/reconciler; MediaItemMenu/CardActions; ItemDetail action path; Settings Personalize list; media surface messages/refresh paths; tests/native fixtures.

Interfaces: optional GET capability; PUT/DELETE item mark, GET paged marks. Only movie/series actions. Preserve unrelated UserState fields in optimistic writes. Mark removes only designated recommendation/discovery rows and supports undo; library/search/Favorites/Watchlist stay.

- [x] Red API/model/state tests: capability absence, idempotent204, wrong media type, stale owner/rollback, real reconciler publication and cursor limit/filter stability.
- [x] Implement current menu/detail/list UI, pending/error/retry and targeted local/realtime refresh; do not trigger whole-Home repaint.
- [x] Green behavioral/native action+management-list checks; review then Task 5.

## Task 5 — Notification series and mute

Files: NotificationsApi/models; NotificationsPage and Settings entry point; NotificationsViewModel or focused list state; new tests/native fixture.

Interfaces: capability series.available and series.mute; GET notification series with status/reason/q/sort/limit/cursor; PUT/DELETE per-series mute. Preserve inbox read cutoff and account-channel behavior. Muting changes only profile notifications, not Home or saved/history state.

- [x] Red tests for capability omission, series/reason/next-up DTOs, full cursor query preservation, profile-safe idempotent writes, mute rollback and read-cutoff invariance.
- [x] Implement Inbox/Series navigation, filters/sort/search/paging/reasons and muted undo; current Settings link and pending/error/empty states.
- [x] Green API/state/native list/mute/inbox regression checks; review then Task 6.

## Task 6 — New releases first

Files: SettingsViewModel Home effective-key/writer; SettingsPage Home group; Home refresh seam; tests/native fixture.

Interfaces: `ui.next_up_new_releases_first` profile boolean, introduced18, defaultfalse. Expose only when supported. Server remains ordering authority.

- [x] Red tests for revision17 omission/revision18 effectivefalse/true, profile save/no device scope, stale completion and one in-place Home refresh.
- [x] Implement visible toggle/current copy/pending/error and effective reload; preserve mounted Home/card identity.
- [x] Green behavior/native toggle checks; review then Task 7.

## Task 7 — Picker add/manage profiles

Files: ProfileSelectPage and VM; AuthApi/SettingsApi captured-profile helpers if needed; existing ProfileEditorDialog/context support; tests/native picker fixtures.

Interfaces: verify primary PIN for add/manage. Add uses captured primary context/libraries/avatar upload without selecting primary or newly created profile. Manage selects verified primary and opens existing Settings Profiles route. Empty-account creation remains valid.

- [x] Red tests for protected/unprotected primary, cancellation during proof/library/create, stale account, no accidental selected-profile mutation on add, manage routing and empty-account fallback.
- [x] Reuse existing editor/guards; add visible Add card and Manage button with pending/error accessibility states.
- [x] Green API/authority/native picker interactions at supported sizes; review all seven integrated changes.

## Final integration and installer

- [x] Fresh independent combined review; resolve correctness findings and preserve red/green evidence.
- [x] Complete main suite and relevant native/published groups on exact final payload. Compare screenshots/layouts for each newly changed surface; report synthetic/live limits precisely.
- [x] Set verified candidate version1.2.267; supported installer/build.ps1 with fresh uniquely named payload; native dependencies and QA signing policy unchanged.
- [x] Hash source/test/build inputs, payload and installer; write receipt/release scope/current-development handoff. Public README/download remain on the published version until authorized.
- [x] Deliver local installer for owner testing; no commit/push/publication/install.
