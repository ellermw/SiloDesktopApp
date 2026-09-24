# Native UI regressions

This Windows-only runner loads actual `ItemDetailPage` and `LandscapeCard` controls from a published
build in a hidden WinUI window. It uses local fixture image responses, never
starts the Silo application, and does not access the user's settings or player.

```powershell
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -p:Platform=x64 -p:PublishDir=D:\SiloPlayer\.codex-tmp\native-test-build\
powershell -NoProfile -ExecutionPolicy Bypass -File tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory D:\SiloPlayer\.codex-tmp\native-test-build
```

The watched-action test sends completion to the real movie/episode detail view
model and asserts the WinUI button changes to Mark Unwatched. It also verifies
return navigation and manual watched/unwatched notifications. The fixture uses
an isolated service container and never authenticates or starts playback.

The artwork test replaces an episode carousel and reparents its section at several image
load timings, checking that all artwork remains decoded and visible. It also
checks that real removal releases the images. WinUI can deliver a deferred
`Unloaded` after a card has already received `Loaded`; the test requires observing
that event ordering so a run that misses the race cannot report success.

Exit code 0 means success. Logs and fixture cache files are written under
`.codex-tmp/native-artwork-tests/`. The installed app remains untouched.

The finalization fixtures also exercise the real calendar navigator at narrow and
desktop widths, the subtitle dialog's enabled/disabled/unavailable provider states
from both entry modes, and Account password/history-import controls. All requests
use isolated handlers. Password success/failure/pending/departure and consumed-import
authorization/progress are checked through actual WinUI controls.
