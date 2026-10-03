# Coven Academy loading layout jump

Status: reproduced in the installed app, repaired in published27b, native regression gates passed. Installed playback was not closed or replaced.

User installed the interim repair and confirmed on October3: "that fixed it." Computer control remains released; continued parity work uses isolated fixtures.

## Observed symptom and cause

With the user's explicit permission, opened Coven Academy using its Home Next Up heading. The first captured detail frame already displayed `1 SEASON`, poster, logo, overview, and actions, with navigation placeholders below. In the settled frame the poster and copy had moved downward by roughly156 pixels as Episodes appeared. This confirms a real layout transition rather than a blank-page recurrence.

`SizeTvViewport` selected the natural multi-season composition whenever `ViewModel.Seasons.Count != 1`. The collection is empty while the companion season query is pending, even when the completed item response already has `SeasonCount = 1`. The initial paint therefore used the wrong composition. The season-loading continuation did not explicitly resolve viewport layout; a later recommendations/layout update switched it to the bounded single-season composition.

The isolated native replay uses actual Frame navigation, ItemDetailPage, view model, and HTTP response deserialization, delaying only season and recommendation responses. No remote artwork is required to reproduce the jump. Published26 trace: viewport `NaN -> 720`, titleY `90 -> 234`, posterY `52 -> 94`. This distinguishes the composition fault from artwork loading and ordinary placeholder replacement. Evidence: `.codex-tmp/native-series-loading-red26c.log` (exit1, exact jump assertion).

## Repair

Before seasons resolve, use the item response's known season count rather than interpreting an empty collection as multi-season. A populated collection is authoritative immediately; a resolved empty result is authoritative too. Reset resolution on each detail navigation, guard canceled/departed continuations, and resize explicitly when the season result resolves. The natural rail's SizeChanged callback uses the same decision.

The repair preserves immediate metadata paint and does not wait for episodes or recommendations to reveal the page. Unknown or stale metadata can still require a composition change once the authoritative season result arrives; that change is now owned by the season response rather than an unrelated recommendation request.

## Verification

Final candidate: `.codex-tmp/parity-publish27` from successful `parity-publish27b.log` (existing CollectionEditor nullable artwork warning remains).

- `native-series-loading-green27b.log`: known single-season titleY stays234 and posterY stays94, before and after delayed responses; actual unknown, stale count, and zero-season responses select their authoritative layouts without depending on recommendations; exit0 and native completion sentinel.
- `native-first-navigation-green27b.log`: actual pre-attachment Frame navigation, mounted metadata, and Back revisit at1280/460 for movie/series/season/episode; exit0.
- `native-tv-populated-green27.log`: populated single/multi-season navigation, episodes, progress, and extras at1440×900,1024×600,900×700,600×450,460×720; exit0.
- `unit-regressions27.log`: all1386 unit tests passed, zero failures/skips.
- `published-playback27.log`: all six actual published PlayerService transport/recovery checks passed.
- Targeted `git diff --check` passed.

An initial invocation without `-Only media-parity` ran the unscoped native host and stopped at the existing `AccountSettingsNativeFixture` primary-profile password-form assertion. That run is not counted as a passing full native suite (`native-series-loading-red26b.log`). The targeted detail gates above explicitly select their fixtures and validate their completion sentinel.

Native screenshots for the final replay: `.codex-tmp/native-artwork-tests/11af8e9642e241f18375a2ec42b92445/series-loading-1-1-before.png` and `series-loading-1-1-after.png`. Separate tests do not control the user's open app. At the user's request, native computer interaction stopped and the Node computer-control kernel was reset after the live observation.

## Delivery

Local interim installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.0-OSD-and-Detail-Layout-Repair-Setup.exe`. It includes the prior OSD and first-navigation blank-detail repairs. Immutable payload/manifests/build record are under `.codex-tmp/interim-layout-*`. The broader parity release and authorized final main push remain incomplete; this checkpoint is not100% visual/runtime parity acceptance.
