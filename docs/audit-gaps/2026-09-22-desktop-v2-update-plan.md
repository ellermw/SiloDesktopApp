# Desktop update against Silo c80c5169

User authorized updating the desktop app after the upstream refresh. Keep the
native player, direct-play preference, lightweight browsing, and current UI.
Replace old API contracts explicitly; do not silently change URL prefixes or
claim that API migration proves the buffering root cause.

Implementation order:
1. Shared transport: v2 Problem Details, string numeric IDs, explicit JSON bodies,
   ETags/conditional writes, and account/profile generation guards.
2. Authentication/profile/session/device flows and strict request/response maps.
3. Playback capabilities/installation identity, sequenced progress/stop, recovery,
   control tickets, subtitle/watch-party routes, and current marker ranges.
4. Browse/home/search/person/recommendation queries, collection pagination and
   conditional mutations; preserve existing view-model interfaces where possible.
5. Remaining settings, download, notification, request, ebook and integration
   surfaces: map every existing route against the pinned OpenAPI and migration
   ledger, adapting renamed/removed routes rather than leaving invisible breakage.
6. Run fixture-backed HTTP tests, existing regressions, release publish and
   published-service checks; inspect any remaining v1 routes explicitly. Prepare
   an installer after verification. Preserve unrelated dirty files and do not
   install over running playback without authorization.

Parallel ownership: authentication, playback, and browsing are independent API
domains; the root handles shared transport, collections, and remaining APIs.
Integration tests must cover stale context responses, token refresh, ETag replay,
exact playback sequencing/retry identity, pagination boundaries, and removed
contract fields. No commit/push or production changes are authorized by this plan.

Implementation and local installer completed September 22; see
`2026-09-22-desktop-v2-update-result.md` for the delivered scope, 1,045 passing
tests, six published-service checks, installer path/hash and remaining live
buffering verification limits.
