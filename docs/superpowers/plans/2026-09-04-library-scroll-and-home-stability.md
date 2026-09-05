# Library scrolling and Home stability

## Scope and reference

User-authorized work: correct Movie-library tab visuals and scrolling across all movie libraries, investigate the Home crash, and review current official Silo playback changes while preserving original video/audio quality and native direct-play compatibility.

Desktop baseline: `fdbbbcf` (1.1.95). Official GitHub server reference advanced from `7c1cb2d3f34e7a37d2864b63735386300e81ef31` to `d4a083e3618f81b9360e7a2f2308dda4d10be144` on 2026-09-04. Eight new commits; native playback protocol, codec negotiation, progressive transport and HLS contracts are unchanged. Playback changes are Safari video fullscreen fallback and Jellyfin-compatible advisory-lock hardening. Other changes cover Apple push display tokens, section date intervals, metadata GC, scanner roots and history matching. None calls for reducing native codec support, bitrate, resolution, HDR or lossless audio.

## Work and verification

- [x] Fetch official GitHub main and inspect playback-related changes.
- [x] Observe installed Movies Recommended/Library tabs and wheel behavior at the existing window size.
- [x] Replace row-based scroll position with exact pixel offsets in `LibraryPage.xaml.cs`, backed by `VirtualGridScrollGate` geometry tests. Preserve bounded card realization and retain cards that remain visible across a row boundary.
- [x] Apply `web/src/app.css` marquee tab colors, spacing and capsule geometry in `LibraryPage.xaml` and its header palette. Use a radius derived from height rather than the oversized WinUI radius. Runtime capsule shape confirmed; full theme/resolution parity is not claimed.
- [x] Test last-caption reachability, overscan positioning, partial rows, large catalogs and small wheel movement. Runtime check confirmed that a 120-pixel wheel step exposes previously clipped captions; dedicated 4K monitor acceptance remains pending.
- [ ] Investigate Home crash and apply only a supported correction. Crash at 2026-09-04 09:45:23 local: WinRT IList GetAt rejected an index; Windows identified Microsoft.UI.Xaml.dll. The exception message mentions Int32.MaxValue, but the captured stack does not establish the actual index or collection. Exact triggering collection remains unconfirmed.
- [x] Run x64 Release tests and app build; create local QA output through existing packaging; inspect running application and live WebUI. Full Movie-library parity remains incomplete.

## Runtime evidence

Installed 1.1.95 opened successfully during inspection. Movies defaults to Recommended. Tab border visibly becomes an ellipse. Library wheel scrolling moves complete rows with no fractional position and clips the bottom row. No windows were resized. Home down/up scrolling once did not crash; this is not proof of resolution.

QA 1.1.96 launched twice from `publish-test` with a separate QA instance identifier, leaving the installed application running. Verified Movies and Movies - International, Recommended default, capsule tab appearance, a 120-pixel scroll exposing captions, and a 600-pixel scroll across a row boundary. Images and captions remained aligned. Shared Movie-library implementation includes both `movie` and `movies` type aliases. Live WebUI and desktop were inspected at their existing window sizes; no resizing was performed, so exact 1440p/4K comparisons remain pending.

Home checks: returned from the international library, scrolled down/up by 2240 pixels; then scrolled down, opened a movie detail page, returned to the preserved Home position, and scrolled upward again. No crash reproduced. The first QA window disappeared during testing because the user closed it, confirmed by the user; this was not a reproduced crash. Added bounded in-memory collection/lifecycle breadcrumbs to the unhandled-exception report, with no media titles or credentials. This is diagnostic instrumentation, not a crash fix.

## Build evidence

- x64 Release app build: 0 warnings, 0 errors.
- Full x64 Release test suite: 800 passed, 0 failed, 0 skipped. Includes 12 new scroll geometry cases. Three pre-existing source-test fixtures now locate the repository correctly under x64 output directories.
- Existing `installer/build.ps1 -PublishDirectory D:\SiloPlayer\publish-test` completed successfully, including publish, resource guards, mpv smoke check and Inno compilation. Multi-file packaging/security policy unchanged.
- Local QA installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.96-Setup.exe`.
- SHA-256: `6CD9AE0B8D674349E1E222AD004D19DB2D4BECDF9DF5FDAE7EF3574B8D80EF03`.
- Published executable file version verified as `1.1.96.0`; executable launch/runtime checked. Installation of this installer has not been runtime-verified.
- No playback transport, codec, quality, HDR, audio or session code was changed.

No push, PR, release, installer-policy change, server deployment or Windows security change is included in this work.

## Follow-up: bounded library loading and adjustable density (1.1.97 QA)

User approved a bounded nearby-item cache, limits on UI work while scrolling, and smaller adjustable posters. Preserve pixel scrolling; do not restore row/page snapping.

- Installed 1.1.96 fast/deep down/up scrolling reproduced prolonged placeholder grids. Input remained responsive in this observation; the reported whole-app freeze was not reproduced. Snapshot-call duration is not a frame-time measurement.
- Regression tests exercise the actual LibraryViewModel and CatalogApi with only HTTP mocked. They first failed for redundant nearby requests, cancellation/restart of a covering request, refetching recent windows, and a late distant request replacing the current window after reversing direction.
- Retain at most eight recently fetched windows, share covering in-flight loads, cancel obsolete loads on reverse navigation, and clear the cache for changed/forced queries. Requests remain capped at 100 items each; dense viewports aggregate bounded pages as needed.
- Add a local Poster size flyout/slider (smaller at left), with a theme-default reset. Preserve the nearby item anchor on density changes; shared code applies across Movie libraries.
- Bound each card allocation/binding batch to four cards and check a four-millisecond elapsed budget between cards. Schedule further realization at low dispatcher priority and avoid resetting already hidden slots.
- Full x64 Release tests after loading fixes: 813 passed, 0 failed, 0 skipped. Final package and runtime evidence will be recorded below after verification.
- Home crash remains unresolved; instrumentation is diagnostic only. No playback quality, transport, or compatibility changes.

### 1.1.97 verification

- Existing packaging script completed successfully: clean multi-file publish, published-resource checks, libmpv hash/create smoke test, and Inno installer. Published executable version is `1.1.97.0`.
- Re-ran the compiled x64 Release suite: 813/813 passed. `git diff --check` passed.
- Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.97-Setup.exe` (159.7 MB).
- SHA-256: `FD7D70D90BD7211B1F3A9E7CB4059032AFF5F6F60BB7D794ADA71A984924AABB`.
- Launched the published application as a separate QA instance. Verified minimum poster density (19 columns at the existing window size), full visible-grid population, theme-default reset, and the shared preference in Movies and Movies - International. No window was resized; the installed app remained running. Installer installation itself was not tested.
- Movies wheel sequence included 120-pixel fractional movement, repeated 1,200-pixel movements/reversal, two 12,000-pixel deep jumps, and 600-pixel forward/reverse movements. International included a 4,800-pixel jump and return. No complete-row snapping or whole-app freeze reproduced in these checks. Large uncached jumps still temporarily display placeholders; this is not a claim of uninterrupted artwork or exhaustive stability.
- UI-lag samples during native-control testing included roughly 0.9–2.3-second delayed ticks, including intervals with no pending card binds. After control was released the repeated delays stopped and process CPU was nearly idle. This correlation does not establish the cause, and automated-control timings are not accepted as a clean frame-time benchmark. Longer user-driven stress testing remains required.
- Native control was released without closing the user's existing windows. QA logs: `.codex-tmp/qa-1197-logs`. The previous Home crash was not diagnosed or fixed by this change.

## Native Library scrolling replacement (1.1.98 QA)

User reported that 1.1.97 no longer freezes the whole app, but wheel scrolling stops in both directions and feels choppy. Direct runtime reproduction: over the gap at x=578 in the observed Movies grid, +600 and -600 wheel inputs did not move the content; the same +600 input over a poster at x=700 moved immediately. The viewport Border and Canvas had no background and therefore did not provide a continuous hit-test surface. This is the red runtime regression case, not a mock or a source-text assertion.

User approved replacing the manual scrolling mechanism with the same native ScrollViewer class used by Home. Keep cache/card pooling/poster density; no playback changes.

- Replace the custom wheel handler and standalone scrollbar with native ScrollViewer movement and inertia. Transparent backgrounds cover gaps and unloaded regions for input.
- Give the canvas the full logical catalog extent but retain only a bounded visible/nearby card pool. Cards use content coordinates; scrolling within a realized range does not reposition every card on the UI thread.
- Native ViewChanged updates the virtual range and schedules data loads without waiting for network or poster downloads. Realization includes one nearby row on each side, even for dense viewports.
- Native programmatic scrolling handles reset, scroll-to-top, and density anchor changes.
- x64 Release build: 0 warnings/errors. Full test suite: 813/813 passed. Existing tests cover extent/last-caption geometry, fractional rows, capped requests, cancellation, and bounded cache eviction. Native pointer hit testing and animation require runtime verification; the unit suite does not prove them.
- Package and runtime results pending below.

### Runtime-discovered card-pool failure

- First 1.1.98 QA run passed gap wheel input down/up and longer movements, then exited during a default-to-minimum poster-size change at a nonzero offset. Windows recorded APPCRASH with HRESULT 0x80131509. That installer was withheld.
- Extracted the existing card assignment algorithm into `VirtualGridSlotAllocator` and reproduced `InvalidOperationException: Queue empty` with 40 retained cards (indices 96–135), four newly created slots, and an expanded requested range (76–209). The loop exhausted free slots before reaching retained indices.
- Assignment now skips not-yet-realizable indices while preserving later retained cards. Subsequent bounded creation batches fill remaining indices. The production page calls this tested allocator.
- Added regression cases for expanding before retained cards, disjoint ranges, and shrinking ranges. Full x64 Release suite after the fix: 816 passed, 0 failed, 0 skipped. Rebuilt package and repeat runtime verification pending.

### Corrected 1.1.98 QA verification

- Rebuilt through the existing multi-file packaging script; publish, resource guards, mpv smoke check and Inno compilation succeeded. No signing/security or playback changes.
- Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.98-Setup.exe`; SHA-256 `55313286A79581097BFED26DEA32AE7162B725EE80BB138D48791C737B895843` (replaces the withheld first build). Published executable version: `1.1.98.0`.
- Repeated the previously crashing default-to-minimum density change after scrolling to the 10 Days/10 MPH titles. The app remained open, preserved nearby titles, and populated the expanded 19-column grid. Reset-to-theme sizing also ran successfully.
- Runtime wheel input over poster gaps now moves the grid. Tested default and compact grids, forward/reverse movement, a 6,000 input delta, further compact forward/reverse deltas, Movies - International, and its scroll-to-top button. Native input was released afterward; no user window was resized or closed.
- Temporary placeholder grids remain visible on large uncached jumps and during density reflow. Instrumented native-control runs still include delayed dispatcher ticks (up to roughly five seconds in the compact test); these are not accepted as a 120 Hz frame-time benchmark or proof of fully smooth performance. Keep this limitation open for user-driven testing and profiling.
- QA runtime logs: `.codex-tmp/qa-1198-fixed-logs`. Tested the published app, not installation of the installer. The separately reported Home crash remains unresolved; this card-pool bug is not asserted to be its cause.

## Bounded library read-ahead (1.1.99 QA)

User confirmed the lockup was gone but reported empty viewports, visible rebinding, and no useful buffer after sitting idle. Approved directional read-ahead, bounded background artwork warming, preservation of unchanged artwork, and latest-viewport request coalescing.

- Cached neighboring windows are now available directly to the virtual grid, including ranges spanning two responses. Regression tests first reproduced missing cached items and an unnecessary third request, then passed after remediation.
- Once visible data is ready, preload approximately three screens in the travel direction and one behind (screen contribution capped at 100 items). Request at most 100 items at a time, sequentially. Keep at most eight cached responses / 1,200 cached records, plus the active window; no whole-library loading or offscreen XAML creation.
- Background prefetch does not replace the active window, emit WindowLoaded, or set the visible loading/error state. It observes both query and viewport cancellation. Tests cover both directions, unchanged active identity, no redraw event, query cancellation, catalog end boundaries, and bounded retention across repeated movement.
- Warm nearby image files through two shared background slots; visible artwork retains its separate queue. Already-started shared downloads keep their warm-up slot until completion so cancellation cannot create uncontrolled parallel downloads.
- Range scheduling is throttled, not trailing-edge debounced: continuing wheel events no longer indefinitely postpone the latest request. Scrolling itself is never gated on network completion.
- LibraryGridCard preserves loaded/in-flight artwork for a metadata rebind with the same content ID and image source. Recycled cards and changed sources still reload; four focused tests cover that decision. Failed image loads may be retried on a subsequent bind.
- Full x64 Release suite: 827 passed, zero failed/skipped. App Release build: zero warnings/errors. Existing multi-file installer build, published-resource checks, mpv hash/create smoke check, and Inno compilation succeeded. No signing-policy, Windows-security, or playback changes.
- Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.99-Setup.exe`; SHA-256 `B08EB69430A38F5667E4C08E3166D78AB6CDE55C3C24690A3AB7C194E3440B37`.
- Official server reference fetched and fast-forwarded to `658be10eb03615f104790fba0431a4d19fd02d15`. New upstream subtitle-delivery/auth/history changes require a separate compatibility pass; this change does not claim to implement those playback changes.
- Runtime QA: published 1.1.99 launches; Movies Library compact grid and forward wheel movement work. Initial dense-card binding still filled gradually under Windows control, with delayed dispatcher ticks recorded. After releasing control for more than 30 seconds, the observed grid was populated, but its window size and position in the catalog had changed; this is not a controlled idle-buffer benchmark. The subsequent input timed out and a new observation showed placeholders. Control was released; repeated input was not attempted. Do not label scrolling fully smooth or infer clean frame-rate measurements from this run. Installer installation itself remains untested.

### Final 1.1.99 package correction

- Added a failing-then-passing regression for renewed signed artwork URLs. Poster preservation now shares ImageService's stable URL normalization, retaining significant image parameters while ignoring expiring signatures. A changed URL may restart an in-flight load, but does not clear an already loaded unchanged image.
- Final full x64 Release suite: **828/828 passed**. Final existing installer build succeeded. `git diff --check` passed.
- Final installer SHA-256: `144A88C47CD46D1A7547C343B67487A3F511D4417CDD3DDEADDD27E44D5A384F` (supersedes the initial B08E package above). Same versioned installer path.
- Final publish directory: `D:\SiloPlayer\.codex-tmp\qa-publish-1199`. Launched its executable separately, verified Home rendering and navigation through Movies - International > Library. Catalog cards loaded; dense binding still filled progressively. This verifies launch/navigation, not completion of the scrolling-performance objective. Logs: `.codex-tmp/qa-1199-final-logs`.
- Native control released after checks. No window-resize action was issued. No GitHub push, PR, release, Windows policy change, or installer installation was performed.
- Next investigation remains dense-grid realization/binding latency, separate from catalog/artwork read-ahead. The original Home crash also remains open.
