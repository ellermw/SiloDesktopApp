# Monster recurring buffering — September 20, 2026

**Latest status: unresolved recurrence on the installed fix at 23:04 UTC.**
See the final recurrence section. The preceding fix and its passing checks did
not eliminate buffering. Original-app request timing is now being collected by
a read-only observer; do not present the earlier result as overall resolution.

## Current result after the September 20 live capture

The 22:14 UTC live incident supplied the missing evidence: the active desktop
and an independent fresh native reader both stalled while receiving tiny amounts
of data. Account and stream tokens were valid. The desktop's per-read inactivity
timeout did not treat sustained insufficient delivery as failure, and its bounded
FFmpeg requests opened a new connection for each range. A local implementation
now retains healthy connections and replaces slow connections using a bounded
useful-progress deadline, resuming at the exact next byte inside the decoder's
current direct stream. Details and verification are recorded at the end.

The installed app has not been replaced. The historical restart-dependent Monster
incidents cannot all be declared explained by this one capture; this fix targets
the failure reproduced live and in native HTTP/HTTPS regression cases.

## Initial state, before the live capture

The reported failure was unresolved. The user identifies Monster (2022),
season 4 episodes 1 and 2: buffering recurs shortly after recovery, closing and
reopening playback does not reliably help, and restarting the desktop app does.
Do not describe preserving direct play as resolving this buffering defect.

## Captured incident

The retained `%LOCALAPPDATA%\SiloPlayer\mpv_log.txt.1` matches the September 18
evening playback in `state_trace.txt`. The native log starts with the app restart
around 21:10 local America/Chicago. Episode 2 is file 37364436, content
`episode-tvdb-389492-4-2`, beginning around 22:29:39. Signed URLs were decoded only
in memory to read issued/expiry times; their values were not copied here.

- Its stream token was issued September 19 at 03:29:38 UTC and expires September
  20 at 03:29:38 UTC, a 24-hour lifetime. It was still valid for roughly 23.5 hours
  at the first captured episode-2 stall. This rules out expiry of that token for
  this incident, not every possible authentication failure.
- Native time 6553.073: HTTPS/TLS read error -138, followed by a byte-offset
  reconnect at 4,484,031,084.
- Native time 6563.414: another TLS error while reading HTTP response headers;
  another reconnect follows. No HTTP 401/403 appears in the captured sequence.
- Native time 6573.135 (22:59:46 local): playable cache falls to 0.064 seconds.
  Playback buffers for 20.888 seconds before reaching the two-second resume
  threshold. Intermittent packet arrival is visible during that wait.
- Around 23:05:01, the buffer empties again. At 23:05:30, the app reloads the
  same direct plan; subsequent buffering occurs at 23:09:10.
- Several later premature-response/EOF messages occur during **intentional
  demuxer cancellation** for reload/close. They must not be misclassified as the
  original cause of those stalls.

Redacted network/cache excerpts are preserved in the ignored diagnostic location
`.codex-tmp/monster-2026-09-18-network-redacted.txt` before app log rotation can
overwrite the source. No production configuration changes or app shutdowns were
performed.

## Player lifetime and tests

`PlayerService.CloseAsync` stops playback and releases the server session but
retains `_mpv` and its video window. `EnsureMpvInitialized` reuses that native
instance. An app exit destroys it. This is a concrete difference consistent
with the user's workaround, not proof that reusing mpv causes the fault.

The native fixture now covers silent body stalls over HTTP and HTTPS, HTTPS
response-header stalls, and four successive stalled HTTPS loads followed by
stop/reopen on the same `MpvPlayer`. The fixture uses the actual bundled native
DLL. The reuse test deliberately interrupts a read before its ten-second timeout,
then verifies resumed advancement from 80 seconds past 110 seconds.

These controlled failures recover successfully in isolation. They do not
reproduce the persistent application state reported by the user. The synthetic
fixture uses PCM audio and the offscreen initialization path, so it does not
validate the windowed HEVC/HDR rendering path or its full application lifecycle.

## Real-source comparison

A separate diagnostic starts episode 2 with `progress_persistence=client`, an
explicit start position, and original delivery. It sends keepalives and closes
its own session, without saving watch progress or controlling the user's player.
Only numeric playback/cache/throughput and token expiry metadata are emitted;
the signed URL and account credentials remain in memory.

The native trial uses a fresh mpv instance with audio-only output at 3x speed,
starting at source position 1700 seconds. This exercises the actual MKV network
reader without a second GPU video workload. In the completed 240-second trial,
source position advanced from 1700 to 2410.1 seconds without a sampled buffering
state. After startup, the forward cache stayed approximately 26.4–30 seconds.
This crossed the captured stall positions 1805.3, 2099.5, and 2314.9. Its temporary
session was closed successfully. This is transport evidence, not full windowed
playback acceptance, and does not reproduce the unhealthy long-running instance.

All nine `NativeDirectStreamingTests` passed in the final targeted run. No
production application source change, new installer, or installed-app replacement
was made during this investigation.

## Remaining decisive comparison

The current app had already restarted and had no active playback when inspected,
so the original failing native state was unavailable. When the loop next occurs,
leave that process and playback running long enough to compare it with a fresh
reader using **the same signed URL and current position**. The prepared helper is:

```powershell
dotnet run --project .codex-tmp/current-stream-diagnostic -- <SiloPlayer PID> native
```

It reads the active transport from the running process without suspending it or
controlling its playback and runs a separate
60-second native reader starting near the original position. It creates no new
server session in this mode. Credentials and URLs remain in memory. Compare
the original app's contemporaneous cache/network log with that independent reader.
Successful fresh reading while the original remains stuck narrows the problem to
state specific to the existing instance; failure in both requires a different
transport comparison. Do not assume either result in advance.

## Continued investigation after the request to implement a proven fix

The installed application still has no active media transport. A new controlled
`https-body-trickle` native test supplies 4096 bytes every 200 ms after the first
16 MiB, with a healthy full-speed response available on the next request. It
fails the existing 20-second playback-completion assertion: occasional data keeps
the native inactivity timeout alive, and the reader does not abandon that slow
response. Command (one failure, zero passes):

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj --filter 'FullyQualifiedName~SignedDirectPlaybackBoundsRequestsAndRecoversInterruptedMediaWithoutRelay&DisplayName~https-body-trickle' -v:minimal
```

This is a demonstrated transport recovery gap, **not proof of the cause of the
restart-dependent live incident**. The new case is intentionally failing while
investigation continues; the previous nine-pass result does not include it.
Do not ship a speculative transport rewrite or claim an implemented fix.

Independent HTTP/1.1 probes using the diagnostic session's signed URL fetched
the same 8 MiB range starting at byte 5,300,000,000 through each of the seven
edge addresses observed in the historical native log. All returned HTTP 206
and completed in 0.737–1.552 seconds. TLS validation and hostname/SNI remained
unchanged; only the connection endpoint was selected. No edge was persistently
slow at test time. The temporary session was closed. This says nothing about
those addresses' performance at the historical incident time.

A separate real-source experiment now includes `InitializeWithWindow`, actual
D3D11 hardware HEVC decoding, audio-clock pacing at 1x, and an isolated hidden
640x360 window. It starts at 1700 seconds and runs for 720 seconds. Numeric cache,
position, dropped-frame and active decoder observations are captured along with
redacted native connection/error events via a secondary read-only native client.
The helper's `gpu` mode enables this experiment. Completed result: 720.1 seconds
elapsed, media position 2416.5, no sampled cache pause, and zero dropped frames;
`hwdec-current=d3d11va`. After startup the cache remained approximately 29.4–30.3
seconds. It crossed all three historical failure positions. The diagnostic
session closed successfully and the process exited with code 0. This narrows
the investigation but does not reproduce or fix the unhealthy app state.

### Automatic live capture and follow-up

`.codex-tmp/current-stream-diagnostic/Watch-LiveBuffering.ps1` observes new buffering entries in the app's state
trace every two seconds. It runs the independent same-URL native diagnostic as
soon as buffering starts, without closing, pausing, reloading, or changing the
original app or its server session. The native comparison now starts immediately;
it no longer waits for an unrelated byte-zero HTTP probe first.

Captures and watcher status are under
`.codex-tmp/current-stream-diagnostic/captures`. Only redacted selected events
and numeric observations are persisted. The watcher runs for 24 hours, at most
six captures, with at least ten minutes between captures. It can be stopped by
creating `.codex-tmp/current-stream-diagnostic/stop-watching`.

Started September 20 at 08:23 UTC, watcher PID 12112; status verified `watching`.
Heartbeat automation `resolve-silo-recurring-buffering` checks hourly, returns
quietly immediately when there is no new evidence, and continues diagnosis/fix
work when a capture is available. It must pause when completed or when the
watcher expires/fails without the necessary evidence, avoiding repeated empty
runs. No production application source, installed binaries, or infrastructure
configuration has been changed by this continuation.

## Live incident at September 20 22:14 UTC and implemented repair

Capture: `.codex-tmp/current-stream-diagnostic/captures/20260920-221423.txt`.
Original installed app PID 15444, content `movie-tmdb-1892`, original file length
50,649,860,738 bytes, duration 8088 seconds. At 17:14:22 local it buffered at
881.1 seconds. It kept receiving tiny packets for the next 30 seconds; the
existing watchdog reloaded the direct stream at 17:14:52 and the cache pause
cleared at 17:14:54.255. This was approximately 32 seconds of interrupted
playback, not a two-second interruption. The premature-response message during
that forced reload is a consequence of cancellation, not the initial cause.

The automatically started independent native reader used the same signed URL
and media position 879.1. It connected to 135.148.26.56 and remained essentially
stationary for 36 seconds, then reached only 879.3 and remained cache-paused
through the 60-second capture, receiving roughly 65–70 KB/s. A fresh native
instance was also affected, so this event is not evidence of a uniquely poisoned
long-lived player instance.

Contemporaneous independent range probes used exactly bytes
5,546,456,960–5,580,011,391 of the same URL, with TLS hostname/SNI and certificate
validation preserved. Endpoint selection was diagnostic only:

| Protocol and endpoint | Bytes received | Time | Result |
| --- | ---: | ---: | --- |
| HTTP/1.1, 135.148.26.56 | 6,962,816 | 25.008 s | Deadline expired |
| HTTP/1.1, 15.204.223.214 | 33,554,432 | 1.467 s | Complete 206 |
| HTTP/2, 135.148.26.56 | 15,171,200 | 25 s | Deadline expired |

The account token was issued 22:04:55 and expired 23:04:55. The stream token was
issued 22:00:03 and expired September 21 22:00:03. Neither had expired. These
facts rule out token expiration for this capture, not all authentication faults.
No token or signed URL is persisted in this document.

### Responsible desktop behavior and change

The desktop was continuously accepting data too slowly to sustain playback.
Small reads kept its FFmpeg inactivity timeout alive until the decoded buffer
ran out; eventual recovery depended on the 30-second playback watchdog. Its
32 MiB ranges also discarded healthy TCP connections between requests. Native
regression tests reproduced both behaviors before the change: the TLS trickle
case failed its 20-second playback limit; the keepalive fixture failed its
12-second limit when subsequent connections trickled.

`DirectHttpReader` now reads signed, seekable original files via mpv's existing
stream callback ABI. This is an in-process byte reader, without a loopback
listener or relay, and does not replace mpv decoding or media selection.

- Requests remain bounded at 32 MiB and reuse a healthy HTTP/2 or HTTP/1.1 pool.
- A five-second active-read budget requires useful delivery at the file's average
  bitrate. Time when mpv is paused or its cache is full is excluded. Tiny reads
  cannot perpetually renew the budget. Before duration is known, the floor is
  64 KiB/s; afterwards it is at least 16 KiB/s.
- Recovery closes the response and replaces the connection pool, including for
  HTTP/2 where cancelling a stream alone leaves its TCP connection reusable.
  It resumes at the exact next byte, preserving decoder/cache and direct play.
- Range start/end/total and strong ETags are checked before accepting bytes.
  Changed entities, ignored ranges, and malformed range responses are rejected.
  Recovery is bounded; cancellation interrupts pending I/O.
- Exhausted reads have a dedicated transport-failure event. Both native end-file
  and keep-open EOF-property paths route to recovery rather than movie/audiobook
  completion. Startup failure, terminal state, and viewer retry retain the direct
  transport classification instead of requesting remux.
- Legacy bearer-authenticated streams, remux and HLS keep their existing paths.
  No production server, CDN, DNS configuration, or installed app was changed.
  Production recovery uses normal DNS; the diagnostic IP choices are not shipped.

### Verification

- Full `SiloPlayer.Tests`: **949 passed, 0 failed, 0 skipped**, 73 seconds.
- Native streaming cases include connection reuse, disconnects, temporary HTTP
  errors, stalled headers/bodies, continuing tiny TLS reads, repeated stop/resume
  on the same mpv instance, seek/pause and startup/midstream terminal failures.
- Real TLS HTTP/2 test: the first TCP connection continually trickles; recovery
  opens a second connection and returns all randomized 4 MiB bytes identically,
  including the bytes received before recovery. Range/ETag rejection, finite
  failures, and cancellation cases also pass.
- Published WinUI `PlayerService` integration: **six checks passed**, including
  audiobook/startup transport failure and terminal viewer retry classification.
- Release publish completed successfully for `1.1.101-streaming-fix.2`.
- The new reader fetched the actual incident's 32 MiB range in 1.697 seconds.
  SHA-256 matched a separate read via the other endpoint:
  `118443D1B34A4BA033646D86109ED62B41BDBF992CAE139434DA7D7422591E71`.
  The previously slow endpoint had recovered by this time; this confirms byte
  correctness against the live file, not a live slow-endpoint recovery.
- Independent native audio playback of the same signed URL from 879.1 seconds
  ran 120 seconds through position 996.1 without a sampled cache pause. After
  startup, buffer depth was approximately 29.3–30 seconds. This was an independent
  reader; the user's original playback was never controlled or interrupted.

No claim is made that a short successful run proves every historical stall fixed
or prevents buffering when no viable connection can deliver the required data.
The previously unproven trickle failure now has matching live evidence and a
verified repair, rather than only synthetic support.

### Delivered local build and follow-up state

A second independent 120.3-second playback check used the hidden native video
window and the final reader settings, starting at 879.1 and ending at 997.3
seconds. No sampled cache pause occurred; after initial fill, buffer depth stayed
between 27.7 and 30.2 seconds. The hidden swap chain reported inability to query
its output information; this is not a display/HDR certification test.

Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.101-streaming-fix.2-Setup.exe`

- Size: 167,526,139 bytes.
- SHA-256: `B52B39211BFA39A516E47B0B88D4F95D1838016B2590CA3CDD8BDBB819F22E8C`.
- Published product: `1.1.101-streaming-fix.2`, file version `1.1.101.2`.
- Independent review findings were fixed: HTTP/2 connection-pool replacement,
  dedicated failure rather than EOF, and startup terminal retry classification.
- Existing unrelated working-tree edits were preserved; no commit, push, public
  release, installation, or production infrastructure change was performed.
- Original app remains PID 15444 with its original 15:28:55 local start time.
  The user can install the replacement after finishing current playback.

The completed capture is marked reviewed. The watcher stop marker was written and the hourly follow-up was paused after verification, preventing further scheduled investigation runs for this capture.

### Monitoring resumed after installation
The user installed streaming-fix.2 and resumed playback. Installed executable version 1.1.101.2 was verified at 22:34 UTC; app PID 33980 started 17:34:11 local. At the user's request, monitoring restarted at 22:35 UTC for 24 hours (watcher PID 23568), using the final reader diagnostic helper. New captures are written as .pending and published as .txt only when completed; the earlier capture remains marked reviewed. The hourly follow-up is active with an updated prompt describing the installed fix and instructions to remain quiet unless actionable evidence appears. The running app was not controlled or modified.

## Recurrence on installed streaming-fix.2 — September 20 23:04 UTC

**Overall buffering issue remains unresolved.** The user reported another stall
while running the installed `1.1.101-streaming-fix.2`, PID 33980. Playback started
at 17:34:16 local. At 18:04:10.109 it buffered at media position 3776.5, and the
cache-pause flag cleared at 18:04:17.309. Native output measured 7.179745 seconds
of cache pause. This does not measure additional user-visible delay outside the
cache-pause interval and does not minimize the report. No player restart or
server-plan replacement occurred in the captured interval.

Completed capture: `.codex-tmp/current-stream-diagnostic/captures/20260920-230410.txt`.
The updated independent reader started at 3774.5 using the same active signed
URL. It also briefly buffered near 3776 seconds (native pause 1.403106 seconds),
then filled to roughly 30 seconds ahead by 12 seconds and ran to position 3830.5
without another sampled cache pause. Both readers experienced slow acquisition
at the same media region/time; this does not establish the responsible transport
stage or prove that a fresh app instance resolves this event.

A subsequent read-only managed-memory snapshot found the original reader at
byte 24,298,992,572, response end 24,329,723,702, failures=0, active-read wait
0.157 seconds, useful bytes 2,770,440. These are post-recovery values, not a
history of what caused the buffer to drain. Established app TCP endpoints then
included 15.204.172.94, 15.204.223.214, and 192.34.101.21. Their roles and health
at the start of the stall cannot be inferred from that later socket listing.

The capture lacks the request timing needed to distinguish these hypotheses:
1. Repeated response-header waits consume playable time outside the reader's
   active-body progress budget.
2. Recovery reconnects to the same temporarily slow endpoint.
3. Sustained read delivery remains insufficient even across retries.

No product change or new installer was made on these unproven hypotheses.
A read-only EventPipe observer now listens to System.Net.Http, Sockets and
Security in the installed process. It writes only an explicit allowlist of
connection IDs, endpoints/hosts, protocol/status and event timestamps. URL paths,
query strings, authorization, exception messages and raw EventPipe streams are
never persisted. Files are bounded to two 4 MiB timing logs under
`.codex-tmp/current-stream-diagnostic/network`; a status file records observer
health. Event source timestamps (rather than observer delivery time) are used
by the corrected observer; entries before its second monitor-started marker
used event delivery time and must not be used for duration conclusions.

The buffering watcher now includes timing before and after the comparison in
completed captures. Both observers run hidden and are bounded to 24 hours.
The native app remains untouched; only diagnostic observers were restarted.
The reviewed marker distinguishes this examined but unresolved recurrence from
new evidence. Future automated work must not mark the issue resolved using the
previous 949-test result or replay this capture without new timing evidence.

Active observers after the recurrence: network PID 4092, buffering watcher PID 14000, both verified watching at 23:08 UTC. The network observer is `.codex-tmp/stream-network-monitor/bin-monitor-v2/stream-network-monitor.dll`, arguments: app PID, diagnostic network directory. Stop it with `network/stop-network-monitor`. Both observers expire around September 21 23:08 UTC. Sanitized live events verify that capture is working; post-recovery range requests reused connection 15 and returned 206, with approximately 124–197 ms response-header waits in the inspected examples. These healthy later requests do not explain the earlier stall.

### Follow-up repair prepared after the user's request to continue

See `2026-09-20-stream-reader-header-budget.md`. A separate reproducible desktop
recovery defect was identified and corrected: header waits were excluded from
the five-second progress deadline, producing a measured 10.450-second wait before
retry. The corrected reader retries at 5.024 seconds. Candidate streaming-fix.3
passed 950 tests and six published-service checks. This establishes the deadline
repair, not the exact cause of the 23:04 recurrence. The user clarified that the
visible interruption was only a couple of seconds; retain that report separately
from the internal cache-pause measurement.
