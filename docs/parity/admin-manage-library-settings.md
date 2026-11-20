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
values. Environment-managed keys stay read-only; when that status cannot be read, every key stays read-only and no draft
is written until a reload succeeds. Navigation retirement cancels in-flight reads, clears values and controls, and closes
the dirty-leave dialog.

Rating capability/source reads and the bounded admin node list are advisory. A capability failure behaves like an older
server and hides Ratings; a source-list failure keeps the supported group with its own warning. A node-list failure leaves
all execution choices reachable rather than blocking a valid mode on transient evidence.

An acknowledged rating-source change invalidates the desktop rating-capability and detail-prefetch caches immediately and
again after 11 seconds, matching the official WebUI's second refresh after the server's ten-second rating-policy cache.

## Verification (2026-11-20)

Run through the shared acceptance script under the SiloOct18Build lease, on the source after a normal merge of Beta
`f35bb7e59d14f8e7f2e53bac982953fa3d5dc7da` (`1.2.1194-beta.1`):

- `library-settings-lead-final-r3`, the final source (commit `19a414df496d8a75c7e9daaa4f01ead637c9ac96`): nonincremental unit
  build; 108 focused cases (the AdminLibrarySettings classes and `AdminLibrariesTests`) and 9,755 full unit cases passed with
  no failures or skips; nonincremental x64 app build, self-contained publish and native host build; native selectors
  `admin-manage-library-settings` (84 passing checks), `admin-manage-foundation`, `admin-configure-general` and
  `admin-monitor` passed. ACQ `2026-11-20T14:55:08.9407468Z`, REL `2026-11-20T15:00:40.1008712Z`. Source seal: 2,484 build
  inputs, `0794282c6265c1fcd63f436fac83d24f449facf11f49cd820df8311fb07fc329`; payload: 582 files,
  `072a8c759bf16b2d72a857b4e2667fb76856d23ca1514aa458a2b921988fd739`, unchanged across the run.
- Earlier runs on the way there: `library-settings-focused-r2` and `library-settings-native-r5` (first passing focused and
  native runs); `library-settings-final-r1` failed 1,060 full-suite cases only because an early version of the script built
  outside the checkout, where about 100 existing test classes cannot find the repository (the script was corrected, no test
  infrastructure is changed here); `library-settings-lead-final-r1` then failed one case,
  `AdminLibrariesTests.EveryLibrariesKeyUsedBySourceHasEnglishText`, described below; `library-settings-lead-final-r2`
  passed everything before the two review corrections.

Defects found and fixed:

- By running the page: `Render` looked up a row, description and error block for `catalog.extra_rating_sources`, which has
  no single field, raising `KeyNotFoundException` on the UI thread and crashing the host on first load (now only keys that
  own a row are rendered); `AdminSettingsGroup.SetAction` did not compile.
- By the full suite after the Beta merge: the Libraries page's text-key scan matches every `AdminLibrar*` source, so it
  picked up these files, whose text lives in `AdminText`. That scan now skips `AdminLibrarySettings*` and
  `AdminLibrarySettingsNavigationTests` runs the equivalent scan against `AdminText` (51 keys).
- By an independent read-only review of the final diff (two P2, no other findings): toggling a malformed saved rating
  source threw on the UI thread, and an invalid list disabled Save with no visible reason. The toggle no longer validates,
  as in the official page; the list is checked as the server does it, lowercased first; and the Ratings group shows the
  problem. One environment-managed worker setting disabled Restore defaults for all five; the official button has no such
  condition and the controller already skips managed keys, so it now stays available. One unit expectation (`RT` is a
  valid list because the server lowercases it) and one native expectation (Restore defaults stays enabled) changed to
  match the official source. The new Ratings-group message has unit coverage of its rule only, not a native check.

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
