# Recurring playback reconnect — October 3, 2026

Status: cause reproduced, repair implemented and independently reviewed, tests pass, local installer prepared. The installed app was not closed or replaced. This repairs the reported periodic **Reconnecting playback** failure; it is not a claim that all historical buffering or WebUI parity is resolved.

## Live evidence

The installed interim 1.2.0 notification socket reconnects roughly every five minutes. Each reconnect is followed by three locally rejected playback progress calls and an application-triggered restart of the direct stream. Progress is rejected before any HTTP progress request is sent.

Times are local CDT on October 3, not measurements of visible interruption duration.

| Notification reconnect | Locally rejected progress | Playback restart |
|---|---|---|
| 01:08:08 | 01:08:10, 01:08:17, 01:08:24 | After the third failure |
| 01:13:09 | 01:13:14, 01:13:21, 01:13:28 | 01:13:29 |
| 01:18:11 | 01:18:17, 01:18:24, 01:18:31 | 01:18:31 |
| 01:23:12 | 01:23:18, 01:23:25, 01:23:32 | 01:23:32 |

The local rejection is `The account, profile or server changed during playback.` After three failures it becomes `Playback session timed out — the server stopped responding.` PlayerService presents `Reconnecting playback` and reloads the stream at its current position. That message misdescribes this case: the app rejected its own keepalive. No actual account/profile/server transition was recorded at these notification reconnects.

Sanitized evidence is retained in the ignored worktree directory `.codex-tmp/playback-reconnect-oct3-evidence/`: `events_channel.txt`, `progress_error.txt`, `state_trace.txt`, `websocket.txt` and `stall-20261003-063007174-12b03276.json`. URLs, credential fields, JWTs and GUIDs were redacted; no credential settings were copied into tracked files. The evidence does not establish why the independent notification socket disconnected. Playback must tolerate that socket reconnecting.

## Cause and repair

EventChannelClient catches up after notification transport loss by calling `PublishAccessInvalidation`. This advances `SiloApiClient.RequestContextGeneration`, retiring pending browse/account reads that could contain stale access information. PlaybackApi incorrectly stored that same read context as the lifetime authority of its session. After notification reconnect, `Session` / `EnsureAuthority` locally rejected all progress for the still-current account/profile. PlaybackManager's three-failure policy then triggered PlayerService recovery. Superseded-session cleanup also lost the old session's authority after read invalidation.

The repair keeps read invalidation and introduces separate playback ownership:

- `ApiRequestContext` still invalidates stale reads when notification transport catches up.
- `ApiIdentityContext` remains stable across read invalidation and token refresh. Its generation changes on authentication replacement/logout, server changes, profile selection/clearing and PIN-grant removal. Switching away and back still invalidates the old session.
- Playback start, progress, replan, control tickets, telemetry and stop use this identity. Ownership validation and request/header creation happen under the existing authentication lock. Existing authenticated-request retry protections remain intact.
- Reconnect diagnostics now record read generation and playback identity generation without secrets.

The failure threshold was not relaxed, notifications were not disabled and errors were not just hidden. This removes the erroneous local rejection that caused unnecessary playback recovery.

## Verification

The regression uses a real loopback Kestrel WebSocket and actual EventChannelClient, PlaybackApi and PlaybackManager with official playback JSON fixtures. It closes the notification socket with 1013 and 4001, waits for real reconnect/account catch-up, and accelerates the production keepalive timer while preserving its three-failure policy. It asserts that stale browse reads retire, the same session sends at least three progress requests, playback starts only once, ProgressReportingFailed never fires and stop succeeds. It does not open the player, read user settings or contact production.

| Gate | Recorded evidence | Result |
|---|---|---|
| Core regression before repair | `.codex-tmp/playback-event-reconnect-red.log` | Both close codes reproduce the false timeout |
| Core regression after repair | `.codex-tmp/playback-event-reconnect-green.log` | 2 pass |
| Authentication/event/access/playback neighbors | `.codex-tmp/playback-identity-regressions.log` | 41 pass |
| Full unit suite | `.codex-tmp/unit-regressions-reconnect-green.log` | 1,399 pass; zero failures/skips |
| Accepted installer assembly before repair | `.codex-tmp/published-reconnect-red27b.log` | Six existing service checks pass; new socket regression reproduces false timeout |
| Repaired installer assembly | `.codex-tmp/published-reconnect-green.log` | 15 pass: six transport/recovery checks, both socket cases, seven real identity-change guards |
| Independent scoped review | `playback_identity_review_oct3` | No actionable issue in ownership separation, request atomicity, retries, away-and-back changes or stop cleanup |

The seven guards cover profile change, profile change-and-return, server change, server change-and-return, new login, logout and PIN grant removal. Old playback is rejected before sending progress under a different identity.

The published integration runner now links the same socket/keepalive regression, checking the actual application assembly rather than only its separately compiled components. A first published guard invocation used incorrect fixture case names; the names were corrected, unknown cases now throw, and the final complete 15-check run exits successfully. The full suite also caught hardcoded dates in pending sign-in UI; these now use the shared preference-aware formatter. That separate account correction is not the playback cause.

## Installer

Local file: `D:/SiloPlayer/installer/output/SiloInstaller-1.2.0-Playback-Reconnect-Repair-Setup.exe`.

- SHA-256: `77CF094FCFC78FC27D8FE4B3292B60AB9CA4D0D689EFC2CEE6029C9AF99AA9F5`
- Bytes: 169,753,663
- Published SiloPlayer.dll SHA-256: `5E50DE470AC167B9C9D6E8DAD97975580116B08EADE634F33F3B566B56178364`
- Accepted OSD Lua SHA-256 retained: `E168A582F4B6E1742BF4908ADF525C9F9FF763FFD67FA8737A3268376ED6FB8B`
- Compiler succeeded in 52.500 seconds. Payload hashes were verified before and after packaging.

The app compiles Core source directly into SiloPlayer.dll. Adding/replacing a separate Core DLL would not update installed playback. A preliminary candidate that attempted this was marked DO-NOT-SHIP and never compiled into an installer. The actual repair is a complete application rebuild, verified through its published assembly.

The immutable payload has 541 files. Relative to the accepted published27b payload, only SiloPlayer.dll, SiloPlayer.pdb and SiloPlayer.pri change. They include the current parity checkpoint as well as the repair; this is not a binary patch restricted to playback types. Dependencies, assets and the accepted OSD script retain their previous hashes. Broader UI acceptance remains incomplete and paused during the reconnect investigation.

Metadata, manifest and compiler log are `.codex-tmp/playback-reconnect-build.json`, `.codex-tmp/playback-reconnect-payload-manifest.json` and `.codex-tmp/playback-reconnect-installer.log`. Existing uncommitted parity work and the original checkout were preserved.

This is an interim 1.2.0 installer, not the final counted parity release. Installing closes current playback; the installer has not been run. Sustained viewing on the installed repair remains to be confirmed. No main push or production infrastructure change was made.
