# Desktop API v2 update — 1.1.102-api-v2.1

Reference: official Silo GitHub main fetched September 22, 2026,
`c80c5169f8e58f354fba35551e0c2bbcabb70b8a`. Contract fixtures in the new
ApiV2Auth, BrowseV2, PlaybackV2, and CollectionsV2 test folders come from that
checkout. This work updates existing desktop integrations; it does not establish
complete visual parity or adoption of every newly introduced upstream feature.

## Implemented

- Authentication, profiles, sessions, device activation and invitation response
  shapes; proactive refresh and authority-generation safeguards retained.
- API v2 Problem Details, numeric-string identifier reads, explicit JSON bodies,
  ETag conditional writes, cursor traversal and profile/account change guards.
- Playback installation identity and capabilities, stable retry identity,
  sequenced progress/stop receipts, fixed source selection, fresh control and room
  socket tickets, current subtitle contracts and disjoint active marker ranges.
- Native direct-play transport and detailed bounded playback diagnostics retained.
  Audiobook progress uses the required integer millisecond fields.
- Catalog seek/window cursors and filters, home, recommendations, people, requests,
  settings, collections, notifications, downloads, ebooks and user integrations.
- Collection artwork updates use the artwork endpoint. Collection group/order
  edits compare their displayed baseline before using a revision, avoiding silent
  overwrites of intervening changes. Watch-provider settings display the same
  snapshot protected by their revision.
- Download streaming carries device/profile identity without cross-origin
  credential forwarding. Webhook receiver URLs resolve against the server origin.
- Shared realtime events use fresh session-bound v2 ticket subprotocols instead
  of credentials in URL query strings.

The dynamic plugin-owned proxy intentionally retains `/api/v1/plugins/...`:
the official migration ledger classifies it as a documented exclusion. Legacy
playback start/transcode/audio APIs are retired in favor of negotiated protocol-v3
plans through v2 endpoints; no invented replacements or silent bridge fallback.

## Validation and delivery

Verified: **1,045 tests passed, zero failures/skips**; Release x64 publish
succeeded; **six published-service checks passed**. `git diff --check` passed.

Release publish directory:
`D:\SiloPlayer\.codex-tmp\api-v2-publish-1.1.102.1`.

Verification artifacts are under `.codex-tmp`: `api-v2-verified-tests.txt`,
`api-v2-publish.txt`, `api-v2-published-checks.txt`, and `api-v2-installer.txt`.
The published-service checks cover signed direct playback bypassing both proxies,
initial/resume/recovery positions, remux timeline, account-auth transport,
recovery exhaustion and repeated startup failures without completing content.

Installer delivery is local only. No installation, running playback replacement,
commit/push, production change, or diagnostic-monitor restart was performed.

Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.102-api-v2.1-Setup.exe`
(167,579,070 bytes). SHA-256:
`6C48D5CDC85EC760697F829C741AA53A642FAC28B4136ABC5E49EC22192FAC35`.
The compiler completed successfully; a sibling `.sha256` file records the hash.

## Remaining playback investigation

This API migration is not evidence that all recurring buffering is fixed.
The previously saved live-buffering evidence and diagnosis limits remain in
`2026-09-22-saved-buffering-evidence-review.md` and the earlier incident records.
The build includes the detailed request/read/cache/network diagnostic recorder
needed to identify another live recurrence. Automated fixture tests and published
service checks do not substitute for a sustained playback test on this candidate.

## Search caption follow-up — 1.1.102-api-v2.2

The user supplied a search-results screenshot with the year/type line clipped.
Search's UniformGridLayout allocated 44 pixels below each poster through
UICustomizationService, while PosterCard explicitly required 56. Title-only
mode also disagreed (28 versus 36). Both now use the same service value:
0 for artwork, 36 for title, 56 for title and metadata. Poster sizing is unchanged.
Other poster grids using this service receive the same correction.

Release x64 publish and 18 existing search/poster-sizing checks passed.
This is a layout calculation correction; the updated search screen has not been
visually inspected in a running app. No installed app was replaced.

## Stable release — 1.1.102

On September 23, the user reported the candidate was working well and authorized
publishing all accumulated desktop changes to GitHub main, updating the README
installer link, and removing the API-v2 version suffix. The stable application
and installer version is 1.1.102; file/assembly version is 1.1.102.0.

Fresh verification: 1,045 Release tests passed with zero failures/skips, all six
published playback-service checks passed, and the clean installer build passed
published-resource guards and native libmpv hash/load checks. The release review
found no blockers. Vendored shader whitespace is preserved to keep its upstream
source hash intact; the remaining staged files pass whitespace checks.

Installer: `installer/output/SiloInstaller-1.1.102-Setup.exe` (167,589,738 bytes).
The GitHub asset is `SiloInstaller-Windows-x64.exe`, with identical SHA-256:
`CEC056DED1B56219A319C01C38B80BD2CCDA9A25541AB6C560BF4DC97B9BCEBC`.

Local diagnostic captures, generated test results, and build outputs remain
excluded from Git. This release does not establish resolution of all recurring
buffering or complete WebUI visual parity.
