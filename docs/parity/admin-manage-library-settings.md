# Admin Manage Library Settings

## Scope and source

This increment owns only Manage > Libraries > Settings (`/admin/settings/library`). It was written against Desktop Beta
`248aafd62ed65b868c0e7356926e4d83204649cd`, merged with origin/beta on 2026-11-20, and official Silo Server/WebUI
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb` from `D:/SiloPlayer-work/server-upstream-nov14`.

Official page and supporting contracts:

- `web/src/pages/admin-settings/LibraryMetadataSettings.tsx`
- `web/src/pages/admin-settings/LibraryMetadataSettings.test.tsx`
- `web/src/pages/admin-settings/settingsWorkerDefaults.ts`
- `web/src/pages/admin-settings/ratingSources.ts`
- `web/src/pages/admin-settings/playbackSettings.utils.ts`
- `web/src/hooks/queries/admin/ratingSources.ts`
- `web/src/hooks/queries/ratingsSurfaceRefresh.ts`
- `internal/apiv2/admin_rating_sources.go`
- `internal/config/admin_settings.go`
- `web/src/locales/en.json`

Libraries CRUD, provider settings and playback settings are explicitly outside this increment.

## Implemented contract

The registered native page uses the shared guarded `AdminSettingsForm` and `AdminServerSettingsStore`. One settings
snapshot owns exactly these current official values:

- General: library-scoped versions (presented as the inverse “Show all library versions” switch) and fallback metadata language.
- Scanning: conditional server-wide real-time monitoring; scanner, matcher and image worker counts; person lookups and matcher batch size behind Advanced; restore all five official worker defaults.
- Maturity limits: the server-wide unrated-title policy for profiles with a content ceiling.
- Ratings: capability-gated plugin declarations, provider grouping, always-shown IMDb/TMDB suppression, and retained undeclared saved sources that remain switchable.
- Subtitle sync: automatic sync plus execution placement and conditional per-node capacity behind Advanced, including the known-no-node warning and saved-choice exception.

The settings write carries one strong `If-Match` validator and only dirty owned keys. Conflicts and unknown outcomes retain
the draft and require explicit reload; non-idempotent writes are never replayed. Late edits remain dirty over acknowledged
values. Environment-managed keys stay read-only. Navigation retirement cancels in-flight reads, clears values and controls,
and closes the dirty-leave dialog.

Rating capability/source reads and the bounded admin node list are advisory. A capability failure behaves like an older
server and hides Ratings; a source-list failure keeps the supported group with its own warning. A node-list failure leaves
all execution choices reachable rather than blocking a valid mode on transient evidence.

An acknowledged rating-source change invalidates the desktop rating-capability and detail-prefetch caches immediately and
again after 11 seconds, matching the official WebUI's second refresh after the server's ten-second rating-policy cache.

## Verification (2026-11-20, lane D:/SiloPlayer-build/admin-completion-lane-b)

Run through the shared acceptance script under the SiloOct18Build lease, on the source after merging origin/beta:

- `library-settings-focused-r2`: 57 focused unit cases (the four AdminLibrarySettings classes plus the source-root proof),
  0 failures. The earlier rating-source failure was a wrong expectation in the test, not in production: the pinned official
  `ratingSources.ts` keeps an unrelated `rt_critic` source while enabling `kinopoisk`.
- `library-settings-native-r5` (app build, publish, native host): selectors `admin-manage-library-settings` (84 passing checks),
  `admin-manage-foundation`, `admin-configure-general` and `admin-monitor` all passed on one published payload.
- `library-settings-final-r1` (full unit suite): 8,693 passed and 1,060 failed of 9,753; its focused stage passed. Every failure
  inspected is repository-root discovery (about 100 existing test classes walk up from `AppContext.BaseDirectory`, which the
  script places under D:/SiloPlayer-build, outside the checkout) or a fixture read that depends on it; none is in an
  AdminLibrarySettings test. The full suite has therefore NOT passed under this script, and no single `-Stage final` run
  passed. This is not attributed to this family and was not changed here.

Defects found by running the page and fixed here: `Render` looked up a row, description and error block for
`catalog.extra_rating_sources`, which has no single field, raising `KeyNotFoundException` on the UI thread and crashing the
host when the page first loaded (now only keys that own a row are rendered); `AdminSettingsGroup.SetAction` enumerated
`UIElementCollection` as `FrameworkElement` and did not compile. `TestRepository.Root` now resolves from the compiled-in
source path first, so unit tests built outside the checkout find it.

The native fixture drives the registered shell, Frame and page through: loading skeleton, populated, failed first read with
Retry, rating-source failure and recovery, older server without plugin rating sources (Ratings hidden), empty rating list,
undeclared saved rating, environment-managed key, node-less and node-present sync placement, both Advanced sections, inline
validation, Restore defaults, one guarded save with its strong ETag, a definite server rejection (sent once, retry allowed), an
unknown outcome (not replayed, Save blocked until Reload keeps the draft), a conflict, Discard, dirty departure, and
authority retirement. The wide, narrow, unknown-outcome and load-error PNG captures were opened and look correct; the
rejected, conflict, empty-ratings and loading captures were produced but not opened.

All 48 official `libraryMetadata`, playback-execution and advanced-section strings used by the page were compared with
`web/src/locales/en.json` at the pin and match. Server limits (worker counts 1-1024, image workers 0-256, person lookups
1-6000, matcher batch 1-100000, sync capacity 1-1024, rating-source pattern) were compared with
`internal/config/admin_settings.go`.

## Remaining verification limits

Synthetic fixtures do not establish live-server behavior, every DPI, Narrator/IME behavior, real metadata-plugin combinations,
a physical transcode node, or complete visual parity with the live WebUI. Libraries CRUD, provider and playback settings remain
outside this increment. Version stamps are unchanged.
