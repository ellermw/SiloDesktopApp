# Profile Requests permission: official #2612

Base: Desktop Beta `40c5133bea92cb56587f36aa3224725225636827`
(`1.2.1165-beta.1`), which merges cleanly with Beta `bdd033c1c`. Tested upstream:
official main `abd85d0cadac28f4ab7db6821dad9634fc114790`; official main
`8f93594ce` adds no Requests or profile changes. Version numbers are left to the
pipeline.

Reference: official `91611b428f9cd0ead8f84886222ce254cd6487ee` (#2612),
plus current `useCanRequest`, `RequestActionBar` and title-route contracts,
fetched directly from Silo-Server/silo-server.

## Supported behavior

An omitted profile `requests_allowed` defaults true; omitted collection capability
defaults false. Unsupported servers hide Allow requests and receive no new field.
The household editor preserves the saved value, puts the switch after library
access, and applies the existing kids/touched-switch presets. Existing manager
entry permissions remain. Settings summarize blocked profiles as No requests.

Blocked profiles retain their own bounded history, status, eligible cancel/remove
and library links. The sidebar appears only with the feature on and an own-history
`mine?limit=1` result. Discovery, search, outside watchlist actions, new requests
and season submission are guarded by current profile/status authority. Blocked or
disabled title/browse routes return Home, including a current status update.
Access-change refresh rereads the current profile metadata and commits it only
under the existing shell owner/revision guards. Remote block and unblock changes
therefore reach Requests without profile reselection; the PIN grant and selected
identity are retained. Bookmark clicks recheck discovery ownership at invocation,
including retained cards. No polling was added.

Account `allowed=false` still permits discovery, matching the WebUI. A title that
can be followed keeps its Follow/Unfollow toggle regardless of that account flag.
Follow uses the ordinary authenticated API and reports the server's refusal;
it neither bypasses the server nor changes entitlement. Unfollow stays
identity-bound and ungated after denial/feature disable. Creation still observes
submission permission. Current active write cancellation reports feedback;
unloaded, superseded or truly retired actions stay quiet.

`RequestDiscoveryOwner(ApiIdentityContext Identity, long AccessGeneration)`
survives routine transport catch-up and retires on real identity/access changes.
Cache validation does not recursively consult discovery permission. Season
confirmation captures this owner before reading and rechecks it before submit
and success feedback. A retired current title load stages data locally and shows
Retry without another fetch; stale/unloaded loads cannot paint. Retained typed
Search refreshes only its stale outside-results page on Back, preserves query/page,
and reuses valid owners after reconnect. Routed-query replay remains unchanged.
Local no-slide/retained-state behavior, cancellation and paging limits remain.

## Verification

Regression tests reproduced each defect before its fix: season reconnect, retired
title load, active cancelled write, disabled title, denied follow, typed Search
Back, remote profile access refresh and retained bookmark ownership.

Local results on this candidate's product source:

- All 7,500 unit tests passed.
- All eight isolated native Requests review cases passed.
- The combined `request-permissions-review` native selector passed 70 checks, then
  failed one: a pending removal button lost its ProgressRing. The button registers
  a localized Content binding, and its Loaded refresh wrote the label back over
  the spinner. `RequestsPage` now clears that binding while removal is pending,
  and the fixture waits for the rebuilt button to load before asserting.

That final two-file correction has not run locally. Generated build output on
the development machine exceeds the owner's 10 GiB limit, and cleanup is pending
owner approval. The Beta pipeline's Windows QA VM runs the unit and matching
native suites on the PR head.

Synthetic fixtures do not certify live households, physical input/Narrator,
every DPI or full WebUI pixel parity.

The only L5 integration overlap is narrow Requests logic in
`ItemDetailPage.xaml.cs` (season capability/confirmation). `LibraryPage.xaml.cs`
is untouched; later integration must retain the stable season-owner checks.
