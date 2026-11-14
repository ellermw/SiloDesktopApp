# November 13, 2026 - Metered quality and upstream 4448e401 handoff

Desktop baseline: 6f9f87f70439a327677e851cc64afbf717417a73, version 1.2.1017-beta.1 / 1.2.1016.1. Branch: codex/metered-quality-4448-nov13. Version stamping remains pipeline-owned.

## Behavior

The shared effective-settings reader selects cellular preferred quality and bitrate on metered connections, using the contract defaults of 720p / 2000 kbps when rows are absent. An explicit null bitrate remains uncapped. Main settings remain effective on unmetered connections. Playback resolves again if the metering class changes while reading settings, with a bounded retry count. Unknown/offline network classification keeps the main quality settings; speed-test detection retains its separate conservative behavior. Older servers returning 422 for cellular keys use 720p/2000 defaults and read buffering independently.

The mounted detail page observes network and cellular-setting changes. Reads retain content, profile, navigation, revision and network ownership. Changes recompute Auto within the chosen edition and retain an explicit file pin. A response for a departed network cannot replace the current quality.

## Verification

Three new cases failed against the original behavior before implementation. Before review corrections, 64 focused and 6,111 full units passed. After corrections, 60 focused quality/settings/connection cases passed; the final full suite passed 6,113 tests, zero failed/skipped, in 2 minutes 23 seconds. Release publish and native host build succeeded. Actual oct24-track-choices and settings-visual-oct24 native fixtures passed, including network/settings handlers, retained edition/pin and a departed-network response. Evidence is under D:/SiloPlayer-build with prefix 4448-metered; native results are results/oct24-track-choices/results.txt.

These are deterministic fixtures, not physical-network or live-server interoperability certification. Independent Fable review found older-server 422 handling, unknown-network classification and missing native controls. All three findings were corrected by the lead. Settings exposes separate metered quality and bitrate controls when contract revision 20 is available, with older-server explanatory text. The actual Settings fixture verifies default display, quality writes and explicit null (No limit), and checks responsive geometry from 600 to 3840 pixels. No second independent review is claimed. Each increment uses its own baseline; no claim is made that the four combined PRs were integration-tested locally.

## Complete upstream audit disposition

Official source: https://github.com/Silo-Server/silo-server. Audited range: 7edef3ddc4cdd5c12f26c23a895b41f31782f286..4448e401b30a1828a34a1e2676f83b9b0c04d8e0 (43 commits). The official main ref was fetched and pinned; source comparisons used pinned Git objects because the upstream working checkout is older.

Four independent increments cover applicable native gaps:

- PR148: Auto ordering within/across editions, later-part Resume, part-one Start Over with portable track intent, and ordered multipart downloads.
- PR149: successful marker save/redetect invalidates stale watch-prefetch snapshots through the existing scoped media-change notification.
- PR150: coalesced mounted catalog refresh, retained authorized rows, letter/native landing ownership, failure handback and lifecycle retirement.
- This branch: effective connection quality shared by detail Auto and playback.

No native API operations were added or removed. Required admin plugin signature metadata is outside Desktop's settings-plugin endpoint. New source_frame_rate is server-to-node traffic. Monitor-null validation and admin collection_type validation affect routes Desktop does not use. Identity-provider errors already map to native sign-in handling. Search, scanner, notification, backup, transcode/GPU/restart and Jellyfin-compat corrections are server-owned; existing native calls benefit without a contract change.

Browser HDR probing, CORS and font-bundle changes do not require native transport changes: mpv is not constrained by browser CORS, and Desktop advertises no font attachment support. Native seeks are gated during quality/track switches, so the web queued-seek race is not reproduced. Native marker editing already fetches fresh markers when opened.

Locale additions in this range are admin-only (14 English additions; 75 additions and one modification per other changed locale). No viewer catalog import is required. Submitted PR145 and the original checkout's six uncommitted localization review fixes remain outside these increments. Complete visual parity, physical-phone interoperability and live-server scan/playback behavior remain unverified.

## Storage

One reusable artifact/publish/native payload was used. Before the full test run, free space was C: 192.51 GiB and D: 480.36 GiB. Prior cleanup was automatically rejected with "blocked by policy"; deletion was not retried or bypassed. Recovered space remains 0 GiB on both drives. Source/history/evidence are preserved; the installed player was not installed, restarted or modified. Final measured output: 1.569 GiB; free C: 192.290 GiB, D: 480.387 GiB.