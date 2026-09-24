# Native artwork regression

This Windows-only runner loads actual `LandscapeCard` controls from a published
build in a hidden WinUI window. It uses local fixture image responses, never
starts the Silo application, and does not access the user's settings or player.

```powershell
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -p:Platform=x64 -p:PublishDir=D:\SiloPlayer\.codex-tmp\native-test-build\
powershell -NoProfile -ExecutionPolicy Bypass -File tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory D:\SiloPlayer\.codex-tmp\native-test-build
```

The test replaces an episode carousel and reparents its section at several image
load timings, checking that all artwork remains decoded and visible. It also
checks that real removal releases the images. WinUI can deliver a deferred
`Unloaded` after a card has already received `Loaded`; the test requires observing
that event ordering so a run that misses the race cannot report success.

Exit code 0 means success. Logs and fixture cache files are written under
`.codex-tmp/native-artwork-tests/`. The installed app remains untouched.
