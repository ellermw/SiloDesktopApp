# Published playback integration

Run against a Windows x64 publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/SiloPlayer.PlaybackIntegrationTests/run.ps1 -AppDirectory D:\SiloPlayer\.codex-tmp\direct-play-fix-build
```

The runner constructs the actual published `PlayerService` with fixture APIs and
isolated settings. It calls its transport preparation and terminal-state handling
without launching the app, opening a window, contacting a server, or accessing
the user's settings. It verifies signed direct URLs bypass the relay, recovery
uses the requested media position, remux timelines remain relative, account-token
transports retain their authentication relay, and fresh-session retry applies
only to exhausted direct recovery.

The separate `NativeDirectStreamingTests` in the main test project exercise the
real bundled mpv and `MpvPlayer.LoadFile` against a loopback media fixture,
including range bounds, interrupted bodies, reconnect header timeouts, HTTP
404/429/503 retries, seek/resume, signed-URL authentication, and option reset.
