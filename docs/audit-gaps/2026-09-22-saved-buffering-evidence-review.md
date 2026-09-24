# Saved buffering evidence review — September 22, 2026

Reviewed the preserved snapshot at
`C:\Users\Michael\AppData\Local\SiloPlayer-DiagnosticArchives\buffering-paused-20260922-022228`
and the newer local state-trace tail. No player controls, installation, production
changes, tests, or scheduled automation resumption were performed.

## New concrete sequence

The September 21 21:20 local interruption was on American Hostage S01E01
(`episode-tvdb-462907-1-1`, duration approximately 2484 seconds), during direct play.

| Evidence | Timestamp | Observation |
|---|---|---|
| Archived mpv_log.txt:64586 | mpv elapsed 16670.877 | Matroska reader reports EOF well before the title's actual end |
| Archived mpv_log.txt:64587–64595 | elapsed 16700.128–16700.169 | Queued audio/video drain and rendering runs out, about 29.25 seconds later |
| Archived state_trace.txt:14115 | local 21:20:02.409 | Application reports `Direct stream could not recover its byte reads` |
| Archived state_trace.txt:14117 | local 21:20:02.613 | Application reopens the same direct plan at position 413.7 seconds |
| Archived state_trace.txt:14121 | local 21:20:03.802 | Replacement file loaded |
| Archived state_trace.txt:14125 | local 21:20:04.803 | PlaybackRestarted/video-output-ready event |
| Archived state_trace.txt:14127 | local 21:20:34.802 | Recovery confirmed by 30 seconds of position advancement |

The error-to-output-ready interval is 2.394 seconds. This is an internal event
interval, not a measurement of the user's exact visible freeze. Its timing and
sequence fit the reported one-to-two-second interruption. The stream remained
direct; no remux fallback or new server plan is shown during this recovery.

This narrows the observed failure to premature termination of the app's direct
read path followed by consumption of already-buffered media and a full stream
reload. It does not identify the exception/status that terminated the reader.
In particular, it is not evidence that an account token expired, nor evidence
that the server or internet was the cause.

An earlier 21:14:58 read failure followed a user seek past the reported end of
the title. Keep that event separate from the later mid-title 21:20 failure.
The 18:54:58 cache pause lasted 200 ms and followed repeated seeks; do not
substitute it for the user's reported spontaneous interruption.

## Other interruptions and coverage

Older records show session restarts after three failed progress reports at
19:16 and 23:41. These are distinct from the 21:20 direct-reader failure.
They must not be treated as its cause merely because they are in the same files.

The external network observer's saved events end September 21 at 05:27:26 UTC,
when its original process exits. The app starts again at 16:41 local, so this
observer does not cover the evening 21:20 failure. The saved status is `finished`.
No new completed independent capture exists for that evening event.

Registry verification identifies the installed application as
`1.1.101-streaming-fix.3` at `C:\Program Files\Silo Desktop Player`.
The in-app diagnostics directory is absent. The prepared `.4` recorder was not
installed, so its detailed request failure records do not exist for this event.
No SiloPlayer process was running at this review.

## Next bounded step

Activate the already-built and tested `1.1.101-playback-diagnostics.4` installer
for a subsequent playback session. Its direct-reader incident records can identify
whether the premature termination was a deadline/retry exhaustion, HTTP rejection,
inconsistent range/entity response, truncation or a socket exception. Retain the
same-trace reader events before the approximately 30-second cache drain.

Do not repeat the old synthetic deadline fix or claim the remaining root cause
resolved from this review. Automatic investigation remains paused; this was a
user-requested examination of existing evidence.
