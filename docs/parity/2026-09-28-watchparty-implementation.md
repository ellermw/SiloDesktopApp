# Watch Party implementation — September 28, 2026

Official reference: public Silo-Server/silo-server main `ad899be9d4fd9f33d4b9e9ac6873166026661c6d` (git objects inspected without checkout changes). Implements ranked package 1 / PB-C1, PB-C2, PB-N2. No production access, user app control, installation, commit or push.

## Implemented behavior

- New `WatchPartySyncController` in Core is the synchronization state machine used directly by the native coordinator through its `IWatchPartyPlayback` adapter. Injected player/message sink and monotonic time make policy behavior deterministic in fixtures.
- Viewer pause/seek input from native overlay, keys, mouse/controller OSC, markers/chapters and MiniPlayer now enters PlayerService's user-intent boundary. Room actions request server transport; denied or disconnected actions do not change local playback. Guests may pause/resume only when permitted; only hosts seek. Applied server commands use separate native methods, with no intent echo.
- Commands validate session and selection revision, deduplicate bounded command history, schedule on the UI timer, and supersede earlier pending commands. Native exact seeks and existing v3 reanchor paths remain authoritative; standalone controls retain their existing seek path.
- Reconnect changes are observable immediately on socket loss. The unchanged active session is reattached on connected transitions; websocket sends are serialized and canceled with their connection. Readiness includes the applied `command_id`, loaded/seeking/buffering guards and target tolerance (1 second guest / 15 seconds host, matching upstream).
- Initial playing REST snapshots trigger room startup with the room's selected file and anchor. Initial lobby snapshots do not close playback. Playing-to-lobby/ended snapshots flush/close the matching session through existing CloseAsync, retain room membership, and show finished versus host-stop text in the room page. Room episodes suppress independent early post-roll and next-episode autoplay. Version changes are locked to the shared room source; playback quality remains available.
- Stalls under two seconds remain local. Sustained stalls report once per outage; unready streams cannot emit misleading state reports. Recovering/ignored slow members explicitly acknowledge readiness. Waiting-room members left behind are explained in a notice.
- Small nonlocal drift converges at 0.9–1.25x for at most 20 seconds; the user's previous speed is restored unless they changed it explicitly. Corrections that reload unbuffered media have one in-flight operation, a 30-second stale bound, 10–60-second backoff and at most 10 seconds of measured load-time lead. Explicit room seeks bypass correction budgets.
- Native remux adaptation reads libmpv's actual cached seek ranges. If a rebuilt stream lands before a waiting target, the controller performs at most one exact cached seek for that command once the target is buffered. It does not play muted at 4x and does not change mute, user speed or pause for preroll.
- Two sustained stalls within five minutes offer a lower advertised quality once per quality/session. The OSC Watch Party panel exposes the action; accepting checks the current offer/quality and uses the existing v3 quality replan. Offers clear on quality changes. Reconnect warnings wait two seconds, and a recovery notice replaces them.

## Verification performed by this implementation agent

- Initial focused test build failed because the new engine contract did not exist; implementation followed that failing test.
- `dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --filter 'FullyQualifiedName~WatchPartySyncTests|FullyQualifiedName~WatchPartyKeyboardTransport' --no-restore -o .codex-tmp/watchparty-tests -v minimal`: **18 passed** (17 behavior cases including four permission theory rows, plus actual bundled-mpv Lua input execution).
- The bundled-mpv runtime test executes the real OSC's Space/K handlers and verifies exactly one emitted intent each and zero direct pause mutations.
- `dotnet build src/SiloPlayer/SiloPlayer.csproj -c Release -p:Platform=x64 -v quiet`: **succeeded**, zero errors, two then-in-progress unrelated SettingsPage unused-field warnings. Subsequent small additions are subject to root combined verification.
- Added `WatchPartyNativeFixture.RunAsync()` for the root's hidden WinUI fixture runner. It loads the actual published coordinator, PlayerService and VM and checks initial startup, attachment, unchanged-session reconnect, intent/no-echo wiring and standalone isolation. Root owns running this after combined publish.
- Deterministic coverage includes target readiness versus duration-only readiness, delayed/duplicate/stale/superseded commands, initial lobby versus stopped room, grace versus sustained stalls, ignored guest recovery, exact cached preroll seek, rate restoration/viewer override, reload timeout/backoff/lead, explicit seeks, quality offer deduplication and reconnect notice delay.

## Remaining verification boundary

These results establish behavior in the controller and actual OSC input code. They do not certify two real networked clients, server-side remux keyframe landing in bundled libmpv, HDR/HEVC/lossless playback, scaled visual parity, or persistent reconnect-warning timing across other overlay notices. Native remux fixtures model cache availability and exact landing; live remux decoding remains a runtime acceptance item, not a claimed result. Root's published native fixture and combined build/test results should be recorded separately.

This package does not diagnose, fix or close the separate standalone signed-stream 404/buffering incident.

## Integration additions after initial focused pass

- Runtime mpv built-in input bindings are disabled while room authority is active, because unmapped defaults could otherwise bypass the explicit OSC intent handlers. The prior option value is restored on leaving. The bundled-mpv OSC test also exercises changing this option and retaining forced key handlers.
- Added three deterministic two-client relay fixtures in `WatchPartyTwoClientTests`: host/guest permission and no echo, waiting-target barrier plus sustained-stall pause/recovery, and disconnected guest denial plus same-session reattachment. Both production controllers run together against an in-process authoritative relay; this is not a real server or two networked client acceptance test. Root owns the post-addition execution result.
