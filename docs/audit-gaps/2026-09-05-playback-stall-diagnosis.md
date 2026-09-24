# Playback starvation: initial-interruption investigation

Scope: diagnose the first media-data interruption, not only subsequent recovery.
No application playback code, installed binaries, server configuration, or running
player controls were changed during this investigation.

## Reference and evidence

- Official GitHub server `origin/main` fetched at
  `dc3f7fa5b955b6351fe6e0089ed78ec0edb110ac`.
- Installed player process: 1.1.100. Runtime logs under `%LOCALAPPDATA%/SiloPlayer`.
- The original player continues writing to the rotated `mpv_log.txt.1`; the
  newer `mpv_log.txt` belongs to another diagnostic. File timestamp alone was
  insufficient to identify the active player log.
- mpv entered buffering with 0.084 seconds cached and accumulated only about
  1.2 seconds of media over the following 34 seconds. This is data starvation,
  not merely an OSC/rendering freeze.
- Progressive downloads repeatedly ended prematurely after approximately
  another 1.0–1.1 GB. Successful byte-range retries did not prevent later stalls.

## Controlled live transport tests

The diagnostic reads the active transport URL in memory, without suspending the
player or persisting credentials. It discards media bytes. It does not start,
stop, replan, or report progress for a playback session. TLS validation remains
enabled. Test helpers are isolated under `.codex-tmp/stream-origin-diagnostic`.

| Test | Result |
| --- | --- |
| HTTP/1.1 open-ended range, 8 MiB/s, 1,200 MiB target | Reached target in 150 seconds; maximum individual read 9.4 ms. Initial helper subsequently failed during duplicate diagnostic-runtime disposal; that cleanup defect was removed. |
| Repeat fast open-ended read without bearer on signed URL | Reached target in 150 seconds; maximum read 22.5 ms; successful process exit. |
| HTTP/1.1 open-ended range, 3 MiB/s, run 1 | Premature response end at 1,098,633,808 bytes / 349.3 seconds. |
| Same playback-paced test, run 2 | Premature response end at 1,098,826,704 bytes / 349.3 seconds. |
| Short 32 MiB bounded range, 3 MiB/s | Completed in 10.7 seconds. |
| Short open-ended read stopped locally after 32 MiB, 3 MiB/s | Completed in 10.7 seconds; short tests alone do not reproduce the failure. |
| Independent curl HTTP/1.1, 3 MiB/s, 90-second budget | Read 279,969,792 bytes at approximately 3 MiB/s until the deliberate time limit; too short to reach the failure boundary. |
| Independent curl long-response reproduction | Failed with curl error 18 at 1,089,888,400 bytes / 346.494 seconds, with 2,894,791,807 bytes missing. HTTP 206; approximately 3 MiB/s. No relay, mpv, or desktop playback code was involved. |
| Sequential 32 MiB ranges, 1,200 MiB total, 3 MiB/s | Passed: 1,258,291,200 bytes in 400.0 seconds across 38 requests; successful process exit. Each response was checked for HTTP 206, exact range boundaries, and stable ETag. |

The two long-response failures occurred without the application relay and
without mpv. They reproduce the approximate byte boundary seen in actual
playback. Removing the relay alone therefore cannot be claimed to fix them.
The curl and bounded-range tests used the subsequent active episode (Parks and
Recreation S6:E4, source length 3,984,680,207 bytes). The earlier .NET comparisons
used S6:E3 (3,983,709,044 bytes). Do not label all experiments as one identical
file. DNS selected different edge IPs across tests; no DNS/system settings were
changed. A short pinned-edge read also passed, so a permanently bad edge was
not established.

The serving node exposes `silo_direct_stream_ends_total`. Its `stalled_reap`
counter increased during these tests. Server source defines this outcome as a
write timeout, distinct from a client cancellation. This is aggregate evidence,
not a per-request attribution; do not overstate it as a correlated server log.
During the long curl/bounded comparison the counter rose from 39 to 40 at
23:59:41 and 41 at 23:59:57, then stayed at 41 through 00:03:47. The server's
default write-stall window in source is 180 seconds. Native playback recorded
another premature response end at 00:01:34, byte 1,108,629,488. This timing fits
an upstream response being closed while downstream buffered bytes remain.

## Working causal model — not fully confirmed configuration diagnosis

An open-ended multi-gigabyte response is consumed gradually by the player. The
delivery path includes an OpenResty edge. A buffering/backpressure interaction
can leave the serving node blocked writing while the edge still holds media for
the client. A serving-node write timeout can then truncate the response; the
client notices only after consuming the buffered bytes.

This fits the reproducible size/rate dependence and the serving-node timeout
counter, but the actual deployed edge configuration and a correlated node log
have not been inspected. The specific edge setting must not be stated as proven.

NGINX documents response buffering and a default 1,024 MiB temporary-file limit:
https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_buffering
https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_max_temp_file_size
Those defaults are supporting context, not evidence of this deployment's values.

## Separate previously confirmed defect

The application's relay bounds body reads but not reconnect response-header
waiting. The separate `.codex-tmp/relay-stall-diagnostic` reproduces that defect.
It can worsen recovery, but is not the explanation for the independently
reproduced initial response truncation.

## Diagnostic conclusion and remaining verification

- The initial failure is reproducible long-response truncation at playback-paced
  consumption, independently of the app's relay and decoder. It is not merely
  a recovery-timer or visual-stutter bug.
- Bounded-range delivery crossed the failure boundary at the same rate without
  changing the encoded media. This supports fixing the request pattern, not
  automatically downgrading quality or switching every stream to HLS.
- Obtain per-request serving-node timeout evidence and inspect the edge's actual
  streaming-buffer configuration before claiming the exact infrastructure cause.
- Verify any chosen native mpv/FFmpeg bounded-request implementation against
  authentication, seeking, direct playback and non-seekable/remux behavior.
  Current upstream documentation describes `request_size` (FFmpeg) and
  `curl-max-request-size` (mpv's optional curl backend); availability and behavior
  in the bundled build have not yet been tested:
  https://ffmpeg.org/ffmpeg-protocols.html#http
  https://mpv.io/manual/master/#network
- Do not silently change CDN/server settings, remove the relay, or ship a fix
  based solely on this diagnostic report.
