# Subtitle identity implementation

Spec: ../specs/2026-11-02-server-parity-design.md (PR2 only).
Base: 6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af, freshly fetched origin/beta, unchanged 1.2.544-beta.1.
Official server: bf53b65450244751013a2965b39da4111d9a0f72.

- [x] Reproduce wire/adaptation, unavailable automatic choice, Lua and actual service/native failures.
- [x] Retain optional identity; match unique actual mpv IDs using container identity or trustworthy FFmpeg indices; legacy unique metadata fallback. Resolve absent/ambiguous identity with existing sidecars, preserving bitmap and authored ASS routes.
- [x] Refuse unavailable choices before selection, callbacks or preference writes; keep visible disabled rows and Off.
- [x] Run focused/full tests, Release publish and actual native/Lua fixtures with isolated outputs under SiloOct18Build.
- [x] Record receipts, source/artifact hashes and limitations, commit focused changes for parent review.

Ruling: Do not add capability advertisements; this PR uses only existing direct/native and advertised server sidecar/burn-in routes. Busy sidecar retries belong to PR3.

Execution record: final 3,074/3,074 full tests, 105/105 focused tests, Release publish, combined subtitle/room native fixture and 27 sidecar-ownership cases pass. Receipts and source fingerprints: D:/SiloPlayer/.codex-tmp/server-align-nov02/subtitles; complete handoff: ../subtitle-identity-report.md at that evidence root. Parent independent review and Beta delivery remain outstanding.

Ruling: Use the container ID with the builtin Matroska demuxer; its FFmpeg index is documented as approximate. An absolute FFmpeg index alone is accepted only with libavformat. Older-server rows use a unique metadata match or an existing sidecar; no native/HLS capability is advertised.

Ruling: A loaded subtitle with neither demonstrated native identity nor a usable sidecar is refused before active/preference mutation and restores the retained OSC highlight with actionable feedback. Busy503 retry remains PR3.

Ruling: Same-source unavailable filtering applies to the current inventory only while that source is loaded. Returning from an alternate edition retains the original requested index and lets the accepted source inventory resolve it. Full-suite and frozen native red evidence caught the initial overbroad filtering; corrected final source passes both.
