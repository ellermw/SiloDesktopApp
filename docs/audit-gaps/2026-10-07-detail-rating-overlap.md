# Detail ratings overlapping overview — October 7, 2026

The user's Carrie screenshot shows IMDb6.6 in the ratings row and a second7.5 over the first overview line. The released native controls reproduce that presentation with IMDb6.6 and TMDB7.5 at150% display scaling using the Segoe UI fallback font. No live credentials or production changes were needed; a persisted access token was unavailable and no token refresh was attempted.

## Cause and repair

`ScoresPanel` was a horizontal StackPanel containing a WrapPanel. The outer stack measured its child with unlimited width. The child therefore requested only one line of height and a content-sized width. At fractional display scaling, the arranged width could be slightly smaller than the sum of child widths. The wrap panel then put TMDB on a second line during arrangement, without reserving that second line during measurement. The description followed the original one-line height and overlapped the score. More providers also escaped the hero width on narrow windows.

C093 changes the outer stack to vertical. The rating wrap panel now receives the actual hero width during measurement and arrangement, so it reserves all necessary rows. Provider order, score values, spacing, and the shared WrapPanel implementation remain unchanged.

The native reproduction also showed the TMDB mark painted black, making it effectively invisible against the dark page. C094 applies the existing SVG gradient directly to its path instead of relying on a CSS class. Geometry, gradient stops, dimensions, and the official mark remain unchanged. Rendered pixels now show its green/cyan gradient.

## Evidence

- `.codex-tmp/ratings-layout-red.log`: released1.2.92 reproduces the overlap at150% and narrow-window horizontal overflow; native host exits1. The initial100% theme-font case passed and was not treated as proof that the report was absent.
- `.codex-tmp/ratings-layout-green.log`: orientation repair alone reserves correct height; native host exits0.
- `.codex-tmp/ratings-mark-red.log`: the intermediate1.2.93 build still paints the provider mark black; native pixel checks fail, host exits1.
- `.codex-tmp/ratings-fix-ratings-layout.log`: final1.2.94 passes32 native layout scenarios, with actual observed100% and150% display scales. Movie/series,1280/460 widths, two/five providers, theme/fallback fonts, vertical and horizontal containment, and visible provider pixels are checked. Before/after PNGs were visually inspected.
- `.codex-tmp/ratings-fix-first-navigation.log`, `ratings-fix-series-loading.log`, `ratings-fix-latest-media.log`: actual detail navigation/Back, delayed season composition, title-art states and ordered/empty ratings pass.
- `.codex-tmp/ratings-fix-unit.log`:1,399 passed; zero failed/skipped. An obsolete source assertion requiring the defective horizontal orientation was removed; actual native bounds tests cover the behavior.
- `.codex-tmp/ratings-fix-playback.log`: all15 actual published-service playback checks pass, including notification reconnect and real identity-change guards.
- `.codex-tmp/ratings-fix-publish.log`: x64 Release publish succeeds; existing unrelated collection-editor nullable warning remains.

The native host uses isolated fake HTTP services and offscreen windows; the user's running app is not closed or replaced. Published TMDB asset SHA256 matches source: `21AFFB3C178C658D1D297B4AFF747FA4D09C4589616DB7F97CC39AC685758185`.

Version1.2.94 includes94 distinct verified corrections. Broader final parity acceptance remains open; this repair does not claim complete parity or resolution of all historical playback faults. The user subsequently requested GitHub main publication with updated README/download; see the [release record](../releases/1.2.94.md).

Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.94-Setup.exe`,169,746,908 bytes. SHA256: `CBC93218ED20CE0B9330897D615BCAA84485D2B3BDDD31A84BE8AE9C6F1789C7`. Inno compile succeeds and the resulting executable reports1.2.94; it has not been installed or launched. Build receipt: `.codex-tmp/ratings-fix-1.2.94/build-receipt.json`.
