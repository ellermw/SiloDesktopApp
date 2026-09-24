# Direct reader request deadline — September 20, 2026

The user reported more buffering on installed `1.1.101-streaming-fix.2`, then
clarified that the visible interruption lasted a couple of seconds. The internal
cache-pause interval was 7.18 seconds; it is not a substitute for the user's
description of visible playback. The independent same-URL reader also briefly
buffered. This capture did not include the request timing needed to identify
the exact remaining cause.

Active investigation found and reproduced another concrete reader defect:
`DirectHttpReader` counted body-read waits against its five-second useful-progress
budget, but excluded response-header waits. Repeated ranges could therefore
consume substantially more than five seconds of active network waiting before
recovery, allowing extra buffer depletion.

## Reproduction and repair

The ignored diagnostic harness uses the real production reader with deterministic
2.7-second response-header waits followed by paced response data. Its first
transport is slow and its replacement is healthy. It reads a complete 64 MiB file.

Before the fix:

```
dotnet .codex-tmp/header-budget-diagnostic/current-stream-diagnostic.dll header-budget
header-budget bytes=67108864 pools=2 firstRecoverySeconds=10.450 elapsedSeconds=10.564
FAIL active request deadline excluded header wait
```

After the fix:

```
dotnet .codex-tmp/header-budget-fixed/current-stream-diagnostic.dll header-budget
header-budget bytes=67108864 pools=2 firstRecoverySeconds=5.024 elapsedSeconds=5.139
PASS active request deadline includes header wait
```

Response-header and body waits now share one remaining deadline. Time when mpv
is paused or its cache is full remains excluded. Range validation, byte offsets,
connection reuse/replacement, bounded retries, and direct-play recovery remain
unchanged. No new relay, larger cache, codec fallback, or server change was added.

The real TLS HTTP/2 randomized-byte regression test now covers both immediate
headers and delayed headers. It verifies that the replacement connection arrives
within 6.5 seconds and that all 4 MiB of recovered content matches exactly.
All eight reader tests passed. Independent review found no material issue in
this incremental deadline correction.

## Limits of the evidence

A separately instrumented replay of the affected movie section (3740–3807.9
seconds) ran for 70.1 seconds without buffering. Its range headers took roughly
62–319 ms after initial probing, and its healthy connection remained in use.
That later healthy replay does not establish what happened during the earlier
stall. The deadline defect is reproduced and repaired; its contribution to the
latest reported interruption is not established. Do not label all recurring
buffering resolved on the strength of this correction.

The running app and production infrastructure were not modified during tests.
The new local installer is version `1.1.101-streaming-fix.3`.

## Verification of candidate .3

- Full test suite: **950 passed, 0 failed, 0 skipped**, 78 seconds.
- Published WinUI service integration: **six checks passed**, including direct
  recovery, startup failures, audiobook handling, and viewer retry.
- Release publish succeeded with product `1.1.101-streaming-fix.3`, file version
  `1.1.101.3`.
- Existing unrelated working-tree edits are preserved. No commit, push, or
  production infrastructure change was performed.
Installer: D:\SiloPlayer\installer\output\SiloInstaller-1.1.101-streaming-fix.3-Setup.exe
Size: 167529200 bytes.
SHA-256: F5904F9407065A93E34F7EB065B3F726C95C4CA2B83F6DD35EAD050E77E2306B
Installer compilation completed successfully. Installation is pending; the original app remains running.

## Installation completed with explicit user approval

The user selected "Install and restart now." Installer exit code was 0.
Installed executable file version is 1.1.101.3; the installed SiloPlayer.dll
SHA-256 matches the tested published assembly. The app was relaunched as PID
34028 and its process remained responsive. The diagnostic comparison helper was
rebuilt against the same corrected reader and attached observers now target the
new app process. Playback itself was not automatically started.
