# Published playback integration

Run against a Windows x64 publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/SiloPlayer.PlaybackIntegrationTests/run.ps1 -AppDirectory D:\SiloPlayer\installer\publish
```

The runner constructs the actual published `PlayerService` with fixture APIs and
isolated settings. It calls its transport preparation and terminal-state handling
without launching the app, opening a window, contacting a server, or accessing
the user's settings. It verifies signed direct URLs bypass the relay, recovery
uses the requested media position, remux timelines remain relative, account-token
transports retain their authentication relay, and fresh-session retry applies
only to exhausted direct recovery.

`SiloCastScopeChecks` additionally exercises the published service with separate
local and remote HTTP fixtures and an in-memory credential store. It checks
browsing/hover isolation, playback watch-cache ownership, advertised audio and
sparse subtitle IDs, explicit subtitle-off, final progress and DELETE ordering,
concurrent close completion, authority restoration, and preserved desktop
credentials. Final progress and DELETE are held separately to prove the service
keeps the remote authority until both writes finish. Native window setup and an
already-started manager session are installed through reflection; API reads,
API v2 session registration, state capture, and `CloseAsync` execute real code.
These checks do not establish phone/TLS/DNS-SD interoperability.

The separate `NativeDirectStreamingTests` in the main test project exercise the
real bundled mpv and `MpvPlayer.LoadFile` against a loopback media fixture,
including range bounds, interrupted bodies, reconnect header timeouts, HTTP
404/429/503 retries, seek/resume, signed-URL authentication, and option reset.
