# Shared appearance implementation — September 28, 2026

Implemented package 3 from `2026-09-28-top-three-plan.md` in the isolated
`codex/parity-top-three` worktree. Official Silo Server reference:
`ad899be9d4fd9f33d4b9e9ac6873166026661c6d`, inspected with `git show` from the
public GitHub reference repository. Relevant contracts are
`internal/apiv2/branding.go` (`/api/v2/theme/admin-css`, string-valued `vars`),
`web/src/contexts/CustomThemeProvider.tsx`, and the hex color input in
`web/src/components/theme/TokenEditor.tsx`. This implements SD5 from the
September 27 settings delta; no production operations or installed-player
changes are part of this work.

## Behavior

- Cinema Dark is the native base on startup. Deprecated cached per-profile
  themes and overrides are no longer read or applied.
- `SharedAppearanceState` reads the public shared-theme endpoint without an
  Authorization header and produces only supported native color tokens.
  Native `ThemeService` updates existing brushes, raw color resources, and
  derived control resources, including primary actions, the focus ring, sidebar,
  surface, text, chart and semantic state colors.
- Hex colors emitted by the current admin color picker are supported, including
  CSS short hex and alpha forms. CSS RRGGBBAA is converted to native AARRGGBB.
  Invalid/unknown tokens, fonts, CSS expressions/functions and `raw_css` are
  ignored; no arbitrary CSS or external resource is executed. Full browser CSS
  and admin editing remain in WebUI.
- A new server/auth/profile context immediately drops the previous appearance.
  Response generation/context checks reject stale reads. Explicit reset also
  invalidates outstanding work. Same-context refresh stages the result and
  applies once, avoiding an intermediate base-theme flash. Empty/invalid,
  missing endpoint and failed responses fall back to Cinema Dark.
- The focus-refresh hook uses a 60-second freshness window and joins a pending
  read. Root owns shell hydration, logout reset and focus integration in
  `MainWindow`; this domain does not edit it.
- Removed the native profile theme gallery, theme editor/import/export/catalog,
  autosaving CSS and all corresponding settings-page/view-model theme reads and
  writes. The root removes the shell theme dots. Existing routes for Appearance
  or Theme Editor resolve to Accessibility.
- Accessibility retains text scale, stronger weight and high contrast. High
  contrast wins over shared decoration on refresh/reset; removing it restores
  the current shared colors. Date/time formatting moved into Accessibility,
  retains persistence and preview, and choice buttons stack on narrow pages.

## Verification

`SharedAppearanceTests` has 10 behavioral tests covering actual API traffic,
hex/alpha mapping, rejected CSS, empty/malformed documents, fallback, reset,
cancellation, stale server/profile responses, same-context single application,
and focus freshness. The first run failed four missing-behavior cases; the
later no-flash test failed on the intermediate base-theme snapshot. The current
isolated Release run passes all 10. Logs:

- `.codex-tmp/appearance-red.txt`
- `.codex-tmp/appearance-red-literals.txt` (includes the no-flash failure; optional
  CSS-function experiments were dropped when scope was narrowed to the actual
  upstream hex-input contract)
- `.codex-tmp/appearance-green.txt`

Tests were run using the temporary `.codex-tmp/appearance-tests/Appearance.Tests.csproj`
with a separate artifacts directory to avoid colliding with parallel domains.
The canonical test file remains in `tests/SiloPlayer.Tests/SharedAppearanceTests.cs`.
Two obsolete source-string tests demanding profile themes/editor behavior were
removed in favor of behavioral coverage; the navigation inventory assertion was
updated to the current settings sections.

`SharedAppearanceNativeFixture` exercises the real WinUI brushes, derived focus
and hyperlink resources, startup ignoring legacy settings, shared reset/error
fallback, high-contrast refresh, text scaling, retained date/time writes and the
actual Accessibility page at 900 px and 460 px. It writes wide/narrow PNGs through
RenderTargetBitmap using fixture HTTP and isolated settings only. Root will run
this against the integrated build; native pass/screenshot inspection and the
combined suite/build status must be recorded before final completion. No live
server settings were changed, and no arbitrary CSS parity is claimed.
