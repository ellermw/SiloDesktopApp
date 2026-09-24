# Official upstream refresh — September 22, 2026

## Provenance

The latest documented upstream compatibility review located in the prior audit is
September 7–8, with official main commit
`aeb82e1c935336eba7a4a7b22233134dbee13c4c` (see the September 8 PROJECT-AUDIT.md).
That was targeted contract checking, not complete feature or visual parity.

On September 22, fetched `main` directly from
`https://github.com/Silo-Server/silo-server.git`.
Current fetched commit: `c80c5169f8e58f354fba35551e0c2bbcabb70b8a`, committed
September 22 at 17:31:32 -04:00. It has 289 intervening commits, including merges,
relative to the September 8 audit reference.

Pinned clean source checkout:
`D:\SiloPlayer\.codex-tmp\silo-server-20260922-c80c5169`.
The older `silo-server-current` working checkout remains at `658be10e` from
September 4; use the new pinned checkout for this review, not that stale tree.
Fetching source does not establish what version is deployed on the user's server.

## Verified playback contract changes

Upstream commit `a0ab8f0b` migrated the server/WebUI to API v2 on September 13.
Current `docs/playback-api.md` describes:

- `/api/v2/playback/capabilities`, installation identity and revision.
- Installation identity on mutations; sequenced progress/stop with mutation receipts.
- A ticket-authenticated playback control socket using `silo.playback-control.v2`.
- `fixed_media_file_v1` and `allow_alternate_versions=false` to preserve the
  selected source file through recovery and other replans.
- Repeated `marker_segments` for intros, credits, recaps and previews.

Desktop PlaybackApi still calls `/api/v1`, with `{position,is_paused}` progress,
and PlaybackWebSocket still uses the v1 control URL. The playback DTO/API/socket
files do not implement the listed v2 capability/identity/sequence fields.
This is a verified migration/feature gap, not proof that those v1 calls currently
fail or cause the observed buffering.

Crucially, upstream's documented v1 bridge keeps playback start/progress/stop/
replan and stream handlers with their frozen request/response bodies. Apple and
Android also use that surface until v2 adoption. Changing the desktop URL prefix
alone would be incorrect because the v2 envelopes, identifiers and responses differ.

Broader upstream changes include watch-party coordination, subtitle handling,
storage and scoped search. Full addition/removal/behavior parity has not been
reviewed in this focused refresh and must not be marked complete.

## Buffering and installer

The existing verified diagnostic installer is available at
`D:\SiloPlayer\installer\output\SiloInstaller-1.1.101-playback-diagnostics.4-Setup.exe`.
Its SHA256 was reverified as
`084EECE8A5FF344A5B3407A83A73244CDEB5A9E0F5607F519D8093FF5DDB9023`.
This recorder can identify the direct-reader exception preceding the captured
29-second buffer drain. It does not contain the v2 migration or claim to fix
the still-unproven underlying read failure.

Continue against the pinned current source. Correlate a `.4` direct-reader
incident before attributing buffering to API drift. API v2 migration requires
contract tests for start, progress, stop, recovery, authentication and control
sockets; do not treat it as a string replacement or an established buffering fix.
No app installation, production change, or automatic follow-up resumption was
performed in this refresh.
