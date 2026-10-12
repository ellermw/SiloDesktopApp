# SiloCast receiver regression tests

Run on Windows with .NET 8:

```powershell
dotnet test tests/SiloPlayer.SiloCastReceiverTests/SiloPlayer.SiloCastReceiverTests.csproj -nologo
```

This project links the production receiver and references Core. Each scenario
starts its real loopback TCP listener and connects a framed TLS-PSK controller.
Production framing, TLS, controller ownership, action queue, handoff HTTP
validation, cancellation, and temporary authentication all execute normally.
HTTP responses contain synthetic credentials and never contact a real server.

The doubles replace the WinUI dispatcher, native network/discovery boundary,
and player adapter. The player records which profile receives launches and
commands, and raises playback lifecycle events. Consequently these tests cover
receiver decisions rather than libmpv, WinUI dispatch timing, final server
progress writes, native DNS callbacks, or real mobile interoperability. Those
remain separate integration/acceptance checks.

Reflection accesses the operation semaphore to hold queued work deterministically,
and observes advertisement/controller lifecycle. Assertions inspect resulting
frames, playback authority, executed operations, and TXT publications; there are
no source text assertions.

Scenarios cover superseded queued launches, stale/matching handoff cancellation,
cross-server local takeover, queued controls after takeover, cancellation without
disturbing current media, memory-only authority reuse, wire close and TCP loss,
effective server identity in discovery/hello, and playing/idle TXT transitions.
