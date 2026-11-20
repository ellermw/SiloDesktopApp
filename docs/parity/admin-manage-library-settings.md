# Admin Manage Library Settings

## Scope and source

This increment owns only Manage > Libraries > Settings (`/admin/settings/library`). It is based on Desktop Beta
`248aafd62ed65b868c0e7356926e4d83204649cd` and official Silo Server/WebUI
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

## Tests authored first

The retained tests-only checkpoint is `.codex-tmp/admin-library-settings-tests-only-checkpoint.md`. The initial test files
preceded production; the final checkpoint also retains regression additions made before their corresponding static-review
corrections. It identifies the exact test-only diff and RED-capable commands. Unit source covers rules, API bounds/sanitization, guarded controller behavior,
registration, XBF approval and exact catalog labels. The native fixture enters through production registration, shell and
Frame; exercises conditional controls, rating failure/recovery and cache invalidation, Advanced sections, inline validation, one guarded save, responsive layout,
dirty departure and authority retirement; and records visual captures when the coordinator runs it.

This worker was assigned a source-only slot. No restore, .NET build, unit test, app launch, browser, native fixture, mutex
acquisition or generated-output change was performed. All authored tests and runtime behavior remain unobserved until the
coordinator runs the serialized acceptance commands.

## Remaining verification limits

Acceptance still needs a fresh compile, focused and full unit suite, production publish/native-host binding, the
`admin-manage-library-settings` native fixture, and visual inspection of its captures. Synthetic fixtures do not establish
live-server behavior, every DPI, Narrator/IME behavior, real metadata-plugin combinations, a physical transcode node, or
complete visual parity. Version stamps remain pipeline-owned and unchanged.
