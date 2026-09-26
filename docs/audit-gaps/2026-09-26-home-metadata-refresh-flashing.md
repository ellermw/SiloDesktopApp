# Home flashing during Refresh All Library Metadata

The user reported repeated Continue Watching flashing while running a server
Refresh All Library Metadata task. The installed desktop app is 1.1.104.

## Evidence and cause

The local Home refresh log records `catalog:metadata.updated` triggering new
section-fetch generations roughly every second (for example generations 38–72
between 00:57:38 and 00:58:17 on September 26). These events bypassed the desktop's
existing 30-second catalog-burst cooldown. This is an app refresh/rendering defect;
metadata jobs should not repeatedly clear mounted rows.

A hidden, isolated native WinUI fixture using the published 1.1.104 controls
reproduced two independent unnecessary-replacement paths:

- SectionRow normalizes Continue Watching and Next Up `ItemSource` for display,
  but the reconciler compared that against an unnormalized response. An omitted
  `item_source` therefore made every otherwise identical item look changed.
- Renewed image URL signatures caused a same-object collection Replace event.
  WinUI still re-prepared the card, restarting its artwork fade.

Three refreshes of three unchanged cards caused **nine card preparations** in
each case. A control case changing progress on one item prepared only that item
three times. The fixture fails on card preparations and preserves object identity
checks, rather than treating a successful HTTP response as visual verification.

## Repair in 1.1.105

- Normalize section-specific item sources before comparison.
- Update renewed artwork URLs without collection replacement. Loaded cards keep
  decoded artwork; a dedicated item notification lets missing artwork retry with
  the fresh URLs. Landscape, poster and audiobook cards unsubscribe on detach.
- Route metadata update bursts through the existing 30-second cooldown. Playback,
  watched-state and explicit item-change refreshes retain the short debounce.

Tests and build evidence are stored under `.codex-tmp/home-flashing`. The original
Home refresh log was preserved there without collecting settings or credentials.
Native fixtures use isolated services and hidden windows, not the running app.

## Verification

- `native-red.txt`: published 1.1.104 reproduced both replacement paths (nine
  card preparations per scenario); progress-only control was three.
- `unit-red.txt`: five failures for source normalization, signed URL replacement
  and both metadata-event aliases before the repair.
- `unit-green.txt`: 25 focused tests passed after the repair.
- `full-suite.txt`: 1,140 Release tests passed, zero failures or skips.
- `native-green.txt`: zero preparations for unchanged/renewed-URL responses;
  progress-only control remained three. Failed artwork recovered without replacing
  decoded images for landscape, poster and audiobook cards; subscriptions were
  released on detach. Existing native calendar/account/import/subtitle/watched and
  artwork reattachment checks also passed.

The initial packaging invocation used Windows PowerShell 5, which lacks the
build script's `Path.GetRelativePath` API. App publication succeeded but packaging
stopped. Packaging was rerun with PowerShell 7; no app change was needed for this
tool invocation issue.

`package-final.txt` confirms clean PowerShell 7 packaging, native library checks
and successful Inno Setup compilation. The packaged `SiloPlayer.dll` matches the
native-tested assembly SHA-256 (`939C4864D1976869078B4632C124145B054DB091FD8DC81095492A83B67A9160`);
Core and Player sources are compiled into that app assembly.

Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.105-Setup.exe`.
SHA-256: `7729DD1BCDD9F6E8986FB658CB08F6092562F17558335713417F17BDF6014431`.
The installed app was left running on 1.1.104 during implementation. The user
subsequently authorized publishing this same installer to GitHub as 1.1.105 and
pushing the repair and updated README to main. Publication does not install it.

This repair does not change server metadata jobs or the separate playback-buffering
investigation. Installation and live acceptance are separate from fixture results.
