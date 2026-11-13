# November 13, 2026 server-alignment reconciliation

Official upstream: 0387303d96618a9b8bda44333a6c2e60de37a6fb.
Desktop Beta: 02d007297c923f529f4ebf282429dd3a62301ec1.
Submitted localization PR #145: 81bc4c955096fee1941f3aa9e8d67e9ed3e5e1f4.
Uncommitted localization changes were excluded from this comparison.

## Independent download correction

The official downloads contract (docs/downloads-api.md, section 1) states that
profile-hidden ephemeral rows disappear from listing and their delete routes
return 404. Desktop retained those stale rows and displayed a connection error.
The delete command now removes only the matching local row on ApiException 404,
updates the empty state and makes no claim that the server record was deleted.
Other errors and explicit cancellation retain their existing behavior.

Verification: both new last-row/multiple-row regressions failed before the fix;
all 10 delete-command cases passed afterward. The final related download suite
passed 67/67, zero failed/skipped. It covers 401/403/409/500/503 retention,
cancellation, timeout, existing success, 404 removal, unrelated-row preservation,
clearing a previous error, and one DELETE request without a follow-up list read.
Tests compile the actual production view model and call the normal generated
command through DownloadsApi/SiloApiClient with an injected HTTP transport.
No native UI or live-account test was run for this command-only change.
Text logs: D:/SiloPlayer-build/download-delete-{red,green,final}.log.

## Original findings and submitted work

- PR #143 merged: primary-profile/household Webhook Sync authority, atomic provider
  reset and capability handling, content-growing Featured heroes.
- PR #144 merged: player HUD, finish time, loading artwork, unattended autoplay.
- PR #145 submitted: interface localization, RTL/formatting wiring, keyed server
  titles, structured API problem display, v1 comment/fixture cleanup and guard.
  The Beta pipeline owns its review, fixes, QA, merge and packaging.
- Relative legacy delivery projection/redaction remains intentional input
  compatibility; it does not create outgoing retired API calls.
- Origin-sensitive retirement-guard relaxation has no demonstrated affected
  producer. Optional card external IDs and server diagnostics upload remain
  separate feature proposals, not required fixes from this audit.

## New upstream delta

Compared fb165fa931ea0f9e99b9a3a6f339af3322bc5524 through the upstream pin above:

- 0b0aa221: contributor rules for other-client parity issues; process only.
- 0b3467ab: download registration fenced against file/title ownership races;
  existing Desktop batch refusal/reconciliation handles the existing 404 result.
- ac54052c: preserve keyframe playlist during server-to-node transcode rehome;
  no native client request/response change.
- 0387303d: Matroska Cues copy-playlist planning and fallback; no native client
  request/response change. A completed plan can return segment 404; HlsProxy
  already bounds retries and propagates it. Early termination of those retries
  remains an optional investigation requiring a reproduced user-visible stall.

No additional Desktop implementation was warranted by those four commits.
This is a source reconciliation, not complete visual-parity or interoperability
certification. Live tombstone, progressive-remux/node-loss, provider-job behavior,
physical-phone interoperability and complete visual/RTL layout remain unverified.

Codex reconciled the original reports against Beta and the submitted PR; Claude
independently audited the four-commit upstream delta. The lead checked the
reported download contract and reproduced/corrected the remaining behavior.
Version remains pipeline-owned: 1.2.1015-beta.1 (numeric 1.2.1014.1).
