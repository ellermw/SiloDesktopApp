# The Runner 4K buffering — 2026-09-07

Read-only diagnosis of user-reported buffering despite a streaming-server speed test above 2 Gbps. Installed app: 1.1.101-multigpu-preview.1, PID 32728. Times below are local America/Chicago on September 7. Sources: local state_trace.txt and direct_stream_proxy.txt. No player controls, app settings, server configuration, or credentials were changed.

## Findings

The movie is content `movie-tmdb-1386315`; the repeatedly failing source is file ID `32621347`. The direct stream advertises total length 9,429,308,854 bytes, inferred consistently from retry offset plus remaining expected bytes. Playback stalls around 2208 seconds (36:48), close to byte offset 4.31 GB.

| Local time | Evidence |
|---|---|
| 02:09:47 | Direct playback begins. |
| 02:18:35 | Upstream response ends prematurely at byte 1,083,448,568. |
| 02:27:50 | Another premature end, byte 2,168,556,864. |
| 02:37:08 | Another premature end, byte 3,260,000,120. |
| 02:46:05 | Another premature end, byte 4,307,789,688. |
| 02:46:27 / 02:46:48 | Two retries report no upstream media data for 20 seconds, near byte 4,309.8 MB. |
| 02:46:37 | mpv enters cache buffering at media position 2209.1 seconds. |
| 02:47:07 | App detects stalled playback and invokes recovery. |
| 02:47:10 onward | Server plan changes to progressive remux; this stream also buffers and fails, followed by an HLS remux plan. |
| 02:53:39 | Direct playback restarts on the same movie/source around 2206 seconds. |
| 02:53:44 | Cache buffering resumes at 2207.8 seconds. |
| 02:54:02 | Upstream idle timeout near byte 4,307,535,357. |
| 02:54:17 onward | Remux recovery repeats; buffering resumes shortly after output starts. |
| 02:58:35 | Direct playback restarts on file 32621347 near 2208 seconds. |
| 02:58:38 / 02:58:57 | Buffering recurs, followed by upstream idle timeout near byte 4,308,410,268. |
| 02:59:04 / 02:59:06 | Active source changes to file 32621349; cache buffering clears. Resolution of the replacement was not independently verified. |

The relay emits the idle-timeout message when an upstream body ReadAsync supplies no data for 20 seconds. That is different from insufficient GPU decoding performance. A fast speed test measures available throughput for that test; it does not establish continuity of the media response at the failing byte range.

The recurring roughly 1.0–1.1 GB premature response endings resemble the independent long-response truncation reproduced earlier with curl outside mpv and the relay (see 2026-09-05-playback-stall-diagnosis.md). That earlier reproduction supports investigating the delivery path, but it was not this movie and does not identify the exact cause of today's repeatable stall around 36:48.

## Limits and next verification

No independent range probe was run against the failing source in this turn. By the time active-read testing was prepared, playback had switched to a different source. Do not label a successful test on the replacement as a successful read of the failing 4K file.

Confirmed: upstream stream truncations/idle periods lead to mpv cache starvation, and app recovery does not immediately restore the same source. Not confirmed: a specific CDN buffering setting, serving-node timeout, storage failure, file corruption, or file-offset implementation defect. Playback-time bandwidth availability alone cannot distinguish these.

Next: correlate serving-node and edge request logs for file 32621347 at the timestamps above; test bounded byte ranges immediately before/at/after 4,308 MB on that exact file. Compare edge and origin only through already authorized access. The app's separate known reconnect-header timeout weakness should be addressed as recovery resilience, not mislabeled as proof of the original upstream failure.

## Follow-up: repeat stall and independent reproduction

The version switch was temporary relief, not a fix. File 32621349 continued until approximately 3681.3 seconds (1:01:21), then buffered again. Its advertised length is **9,429,312,854 bytes**, distinct from the earlier file's 9,429,308,854 bytes.

- 03:07:49: premature response end at byte 5,389,233,629.
- 03:17:06: premature response end at byte 6,477,874,920.
- 03:23:30: no upstream media data for 20 seconds at byte 7,189,022,055.
- 03:23:40: mpv cache buffering at 3681.3 seconds.
- 03:24:11 onward: application stall recovery and progressive-remux fallback; fallback also buffers.

The diagnostic inspected only DirectStreamProxy transport objects in the running process without suspending it or making a memory dump. Signed transport URLs remained in memory and were not printed or saved. No access/refresh tokens were extracted, no token refresh was performed, and no playback session was started, stopped, or replanned by the diagnostic. The latest retained direct transport was no longer the active plan after remux recovery, but remained usable for successful authenticated-by-signature HTTP 206 reads. Its content length matched file 32621349.

### Short range checks, outside mpv and the app relay

| Source range start | Result for 512 KiB | Total elapsed |
|---|---|---:|
| 0 | HTTP 206, exact requested range, complete body | 1164 ms |
| 7,189,000,000 | HTTP 206, exact requested range, complete body | 210 ms |
| 7,220,000,000 | HTTP 206, exact requested range, complete body | 202 ms |

A fresh GET to the active remux transport also supplied 512 KiB in 342 ms. These short reads rule out a *persistently* unreadable byte interval at test time; they do not prove a long-running transfer is healthy.

### Playback-paced long comparison

Both requests used the same signed direct URL, starting at byte **7,189,000,000**, HTTP/1.1, at **3 MiB/s**. They ran concurrently, independently of mpv and the app relay. Total combined target throughput was 6 MiB/s. No media bytes were saved. Bounded responses were checked for HTTP 206 and exact requested range boundaries, and a supplied entity tag was retained for If-Range validation.

| Request pattern | Result |
|---|---|
| One open-ended range | **Failed** after **1,107,341,792 bytes**, **352.0 seconds**, with `HttpIOException` during the body transfer. |
| Consecutive 32 MiB ranges | **Passed** the **1,258,291,200-byte** target in **400.0 seconds**, **38 requests**. |

This independently reproduces a long-response failure on the user's movie, and demonstrates a bounded-range workaround across that failure boundary at the same consumption rate. It does not establish that every future bounded request will succeed or that the player integration has been fixed.

### Server evidence and limits

Official Silo server GitHub main fetched directly for source inspection: `aeb82e1c935336eba7a4a7b22233134dbee13c4c`. The serving host exposed OpenResty in its Server header. Its aggregate `silo_direct_stream_ends_total{outcome="stalled_reap"}` counter increased from **495 to 496** during the comparison. Other traffic was present, so this is supporting evidence, not per-request attribution. Current official source defines that outcome as a write timeout and uses a default 180-second rolling write-stall window, overridable by deployment configuration. The deployed value was not inspected.

The causal model remains an upstream streaming timeout interacting with proxy buffering/backpressure: the origin can stop making write progress while the proxy still holds data for a slow-consuming client. The client then discovers the truncated response after draining buffered data. This is consistent with the tests; the exact component/configuration requires correlated edge and serving-node logs.

The production access runbook specified in AGENTS.md was absent at both the historical Mike path and the corresponding Michael path. The user was asked where to find the existing SSH connection/runbook; no server settings were changed.

### Separate app recovery defect, reconfirmed

`dotnet run --project .codex-tmp/relay-stall-diagnostic -c Release` reproduces a retry that waits for response headers beyond the configured upstream idle timeout. With a 50 ms idle timeout and a 2-second safety cancellation, the helper failed at 2010 ms after relaying only 4 of 8 bytes; the second response never delivered headers. The current relay applies the idle timeout to body reads but not `SendAsync(...ResponseHeadersRead...)`. The higher-level player may eventually cancel/replan; the relay itself does not bound that header wait.

Recommended desktop mitigation: bounded 32 MiB requests for seekable direct streams, preserving downstream range/length semantics and byte identity, plus a finite response-header timeout. Keep non-seekable remux handling separate. This requires implementation and integration testing; no production playback code, installed build, or server configuration was changed in this diagnosis.

Artifacts: `.codex-tmp/runner-buffering-diagnostic/Program.cs`, `comparison.txt`, and the existing relay-stall diagnostic. The helper is throwaway diagnostic code, not a shipped player change.
