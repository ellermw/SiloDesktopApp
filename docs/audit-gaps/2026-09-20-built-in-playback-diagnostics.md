# Built-in playback flight recorder — 2026-09-20

## Status and purpose

Candidate: `1.1.101-playback-diagnostics.4`. This adds evidence collection inside
the actual desktop player. It does not establish the root cause of the remaining
live buffering or claim that buffering is fixed. The installed `.3` build remains
unchanged until installation is authorized.

## Saved evidence

Directory: `%LOCALAPPDATA%\SiloPlayer\diagnostics` (or the diagnostics subdirectory
of `SILOPLAYER_LOG_DIRECTORY` when explicitly overridden for tests).

- `playback-current.jsonl`: structured timeline, rotated at about 4 MiB with one backup.
- `stall-<time>-<trace>.json`: automatic incident with up to 2,048 preceding events
  and up to 2,048 events during the following 30 seconds; eight reports retained.
- `recorder-status.json`: last write time, active trace, lost pending-event count,
  and previous write error type.

Reports start on cache buffering, reader retry/failure, native playback error, or
application recovery. Startup cache filling can also produce a report; the load,
seek, pause and file-loaded events distinguish this from interruption after playback.
`complete` means the capture window ended or the player was disposed, not that
playback recovered. Recovery is evidenced by `buffer_recovered`, advancing sampled
position and successful byte delivery. Stop/reload recovery preserves the recorder
and trace. Load numbers and process-wide unique request/pool IDs avoid ambiguity.

The recorder captures:

- UTC timestamps and monotonic elapsed times, application build and process ID.
- Request ID, connection pool ID, actual .NET connection ID and validated peer IP;
  reused requests retain the peer even if connection establishment has left history.
- Connect/TLS/queue events, negotiated HTTP version, response status, header wait,
  requested and returned byte ranges, content length and entity-tag presence.
- First-byte delay, bytes read, completed ranges, active operation, current pending
  read age, longest read, accumulated body wait, time since the last byte, consumer
  idle time, useful bytes, required rate and remaining progress deadline.
- Retry stage, reason, exact resume byte, attempt number, underlying exception
  types/HResults and socket/native error codes. Exception text is excluded.
- Signed stream token expiry when its URL contains a parseable JWT expiry claim.
  This is an unverified diagnostic claim, not authentication validation.
- Sampled playback position, duration, cache seconds, cache speed, dropped frames,
  pause/buffering state and native event-loop age; sampled GC pause totals, managed
  memory, thread-pool pressure and actual sampler interval.
- Recovery requests from PlayerService with fixed reason classifications.

## How to distinguish failures

| Observation | Evidence to inspect |
|---|---|
| Waiting for response headers | `phase=headers`, pending I/O age, connect/TLS/queue events, header duration |
| Body stops delivering bytes | `phase=body`, unchanged byte counters, pending I/O age, deadline failure |
| Body trickles too slowly | Advancing small byte totals against required rate; declining cache; useful-progress deadline |
| Connection reset | Nested socket error code, request/connection/peer, last exact delivered byte |
| Authentication rejection | HTTP 401/403 and `authorization_rejected`; compare expiry claim with event timestamp |
| Invalid or truncated response | Expected/returned ranges, status, length, `response_truncated` or rejection classification |
| Player stops consuming | Reader idle time while cache/position/event-loop samples explain whether consumption is expected |
| Native or runtime stall | Position/cache versus event-loop age, timer lateness, GC pause and thread-pool changes |
| Recovery does or does not help | Same trace through stop/load, replacement pool/connection, bytes and position after recovery |

An HTTP rejection alone does not prove an expired token. A stalled connection alone
does not prove why its peer stopped sending. These records identify the failing
stage and preserve measurable inputs for reproducing the desktop failure; causal
claims still require matching evidence and a reproducer.

## Playback safety and privacy

Playback callbacks append to bounded memory only. No disk access, flushing or waits
run on the native read or playback event threads. Byte reads update counters; they
do not serialize one log entry per packet. Background writes are serialized,
incident replacement is atomic, and temporary final-write failures retain the
incident for bounded retries. Non-finite native numeric values cannot break JSON
serialization. Logging failure cannot fail a media read.

No stream URLs, hostnames, request/response headers, bearer tokens, signed tokens,
credential values or exception messages are passed to this recorder. Network
events use an allowlist of numeric fields, protocol versions and validated IPs.

## Verification

- Fault-injection reports distinguish header stalls, body stalls, nested connection
  resets and authentication rejection, while excluding seeded URL/token/message secrets.
- Real TLS/HTTP2 trickle tests retain byte-for-byte correctness, deadline behavior
  and request peer evidence after connection establishment leaves the history ring.
- Real native playback tests verify the incident retains stop/reload and buffer
  recovery in the same trace across repeated stalls on a reused player.
- Recorder tests exercise bounded history, disk failure isolation, final-write
  retry and NaN/infinity from native telemetry.
- Published application service integration checks pass all six existing playback cases.

Full regression: `dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -v:minimal`
passed **957/957**, zero failures/skips. Published application passed **6/6** service
integration checks. Release publish succeeded, and an independent code review found
no remaining material diagnostics issues after the evidence-retention corrections.

Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.101-playback-diagnostics.4-Setup.exe`

Size: 167,546,567 bytes. SHA256:
`084EECE8A5FF344A5B3407A83A73244CDEB5A9E0F5607F519D8093FF5DDB9023`.

The running installed app is still `.3` (PID 34028 at verification). Installation
requires permission to interrupt that playback; a question was presented after
verification and packaging. The existing external watcher remains undisturbed.
