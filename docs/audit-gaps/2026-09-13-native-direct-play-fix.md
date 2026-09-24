# Native direct-play correction — September 13, 2026

## Incident evidence

The installed desktop client's local logs recorded:

- 06:40:25: direct playback began.
- 06:45:37 and 06:51:26: the open-ended response ended prematurely; the local relay resumed by byte offset.
- 06:51:27: the relay terminated with `DirectStreamEntityChangedException`.
- 06:51:57: the client submitted protocol-v3 `failure_recovery`, adopting progressive remux.
- 06:54:39: buffering resumed; the next recovery selected remux HLS.

This change addresses the desktop request pattern and recovery decisions. No
production server changes were made. The error response headers from the live
06:51:27 request were not captured; an error-page ETag is a reproduced client
classification defect, not a proven attribution of that specific response.

## Implementation

Signed, seekable `/stream/direct/{token}` playback now opens the original URL
in mpv without `DirectStreamProxy`. The bundled FFmpeg HTTP implementation uses
32 MiB ranged requests and reconnects at the last byte. It retries temporary
404/408/429/5xx statuses, bounds network waits, and does not forward an account
bearer to a signed URL. Per-load options reset when leaving this transport.

Protocol-v3 cache stalls and premature EOF reopen the same direct plan instead
of reporting the original delivery route as failed and escalating to remux.
Recovery uses the current media position rather than the plan's initial start.
Three reloads without 30 seconds of advancement stop with a retryable failure;
explicit Retry starts a fresh direct session. Other terminal retry paths retain
their previous replanning behavior and quality selection.

Account-token progressive URLs and sequential remux still use their existing
relay; HLS retains its own transport. The remaining relay now bounds response
header waits and rejects error documents before processing media ETags or
appending bytes to a media response. Codec declarations and quality selection
were not reduced.

## Verification

- New regression cases failed on the original code for unbounded native reads,
  missing `eof-reached` recovery, header hangs, error-page ETag classification,
  direct-plan escalation, transient 404/429, and unbounded same-position reloads.
- Main suite: 932 passed, zero failures or skips.
- Native tests use the real `MpvPlayer` and bundled DLL to play a 96 MiB WAV,
  cut the active response at byte 16,777,216, verify exact byte continuation,
  and cover stalled reconnect headers plus 404/429/503 responses. They also
  reload at 180 seconds, seek to 300 while paused, complete playback, and verify
  that a later legacy HTTP load restores bearer auth and its original options.
- Published-service integration verifies proxy bypass, direct positions
  0/687.3/3600, remux timeline preservation, legacy authentication transport,
  and terminal retry routing without opening the app or using live credentials.
- Windows x64 candidate: `1.1.101-direct-play-fix.1`, file version `1.1.101.1`.

Long-duration playback of the user's actual HEVC/HDR content on the installed
candidate remains live acceptance work. These fixture and integration results
do not claim that every network or decoder failure has been eliminated.

No existing player process was closed by this work. No commit, push, or public
release was performed. Pre-existing working-tree changes were preserved.

## Installed result

The old player had exited before installation. The local installer completed
successfully with exit code 0 and no Windows restart required. The installed
EXE, application DLL, and libmpv hashes match the verified publish. The updated
app launched from `C:\Program Files\Silo Desktop Player\SiloPlayer.exe`, remained
responsive, and reached `Home · The Tavern - Silo`. Crash-log timestamps did not
advance during startup.

Installer: `installer/output/SiloInstaller-1.1.101-direct-play-fix.1-Setup.exe`.
SHA-256: `741D14CF81AA843C6F0526D64969DBD7FA0D98D7432559D6E71E7509633AF083`.
