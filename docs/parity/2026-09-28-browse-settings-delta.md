# Browse and settings delta — September 28, 2026

## Scope

Bounded source comparison against official `Silo-Server/silo-server` GitHub main
at **`ad899be9d4fd9f33d4b9e9ac6873166026661c6d`**, freshly fetched by the
coordinating audit. Baseline: **`5e49cc8d376d2aaa1a7b4601ff876895f8d82efe`**
(September 27 audit; 78 additional upstream commits). Upstream was read with
`git diff` and `git show` at these exact refs, without changing its checkout.
Desktop source: current `D:\SiloPlayer`, release 1.1.105. This supplements the
September 27 fresh, browse and settings reports; it does not replace or retest
their carried findings.

No application edits, builds/tests, live API calls, installed UI interaction,
production operations, credentials or private GitLab sources were used. Only
this audit document was added. These are source-confirmed differences, not
deployed-version or visual-parity signoff. Upstream paths/lines below refer to
the exact new hash; `src/` paths refer to desktop source.

## New desktop work, larger scopes first

### D1 — Home customization and filtering (medium)

Three related additions belong together in a Home settings package:

- **Hide watched items:** upstream adds profile-scoped
  `home.hide_watched_items`, default false, in
  `contracts/settings/v1/manifest.json:995`. The control and save are in
  `web/src/pages/settings/HomeScreenSettings.tsx:481` and `:531`; setting
  mutation/realtime invalidates Home in
  `web/src/hooks/queries/settingValues.ts:269`. Desktop has no occurrence of
  that key or corresponding Home preference control; its Home settings begin
  at `src/SiloPlayer/Views/SettingsPage.xaml:1243`. Add setting read/write,
  profile ownership and Home cache invalidation on success and relevant
  cross-client changes. Do not implement a competing local watched filter:
  `internal/api/handlers/home_watched.go:27`, `:131` and `:159` already filter
  per profile, retain Featured/watch-history rows and drop emptied ordinary
  rows. Desktop already reads the corresponding v2 Home endpoints
  (`src/SiloPlayer.Core/Api/HomeApi.cs:7`, `:18`), so server-side filtering is
  inherited after the setting is enabled and Home is refreshed.
- **Recently Added/Recently Released library selection:** upstream now renders
  the picker for those two recipe types only outside library-page scope
  (`web/src/components/RecipeGallery/RecipeParamFields.tsx:44`). Native
  `src/SiloPlayer/Views/Dialogs/RecipeGalleryDialog.cs:574` has no cases for
  either type; its existing library picker at `:912` is for favorites/watchlist.
  Add the picker to gallery and editor, pass scope, and preserve the server's
  filter precedence. `web/src/lib/sectionLibraryFilter.ts:40`, `:64` and `:91`
  cover legacy `filter_library_id`, plural `filter_library_ids`, query-shaped
  `library_ids`, and generated-row ownership. A changed selection must not
  retain a stale legacy ID; only detach generated ownership on save when the
  saved selection excludes its owner. Existing rows filtered by the server
  are not evidence that desktop needs its own filtering engine.
- **Recipe permissions:** upstream permits admin-only choices only for an admin
  or when `/api/v2/profile/sections/flags` reports
  `allow_profile_custom_sections=true`
  (`web/src/pages/settings/HomeScreenSettings.tsx:199`, `:225`). Native always
  removes `AdminOnly` definitions (`RecipeGalleryDialog.cs:69`, `:271`) but
  unconditionally adds fallback `custom_filter` (`:78`, `:93`). Thus allowed
  users lose some choices while disallowed users can still select a custom
  filter that the server refuses. Use the actual permission flag and role in
  both entry points, retain display of an already-owned restricted recipe,
  and show a useful save-denial reason. Do not describe the native gallery as
  wholly unfiltered or assume a successful server write bypasses authorization.

Acceptance: switch profiles and Home/library scopes, update the preference in
another client, edit old/generated recent rows, clear/reselect libraries, and
exercise admin, permitted non-admin and denied non-admin recipe choices.

### D2 — Public TMDB-list imports and source editing (medium)

Upstream adds the Custom TMDB List template
(`internal/collections/templates/builtin.go:776`), source `tmdb_list`,
capability `import_sources` (`internal/apiv2/personal_collections.go:122`) and
`POST /api/v2/collections/import/tmdb-list` (`:433`). The request accepts a
public list URL or numeric ID. Imported collections remain type **`tmdb`** with
`source_config.mode = "tmdb_list"`, as shown in
`internal/api/handlers/user_collection_imports.go:199`.

Desktop template models lack the `tmdb_list` specification
(`src/SiloPlayer.Core/Models/Collections/CollectionImports.cs:15`); capability
models lack `import_sources` (`Collection.cs:105`); API imports at
`src/SiloPlayer.Core/Api/CollectionsApi.cs:88` have no new route. The template
dispatcher only supports mdblist/tmdb/trakt, with an unsupported-source error
at `src/SiloPlayer/ViewModels/CollectionsViewModel.cs:519`. Existing public
TMDB-list collections can be recognized as imported `tmdb` collections, but
the source URL is editable/sent only for MDBList
(`src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:195`;
`src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs:297`). WebUI detects the
mode and edits the URL (`web/src/pages/ImportedCollectionEditor.tsx:158`,
`:447`, `:1168`).

Add capability-aware template availability, URL/ID input and validation, import
API/model support, source-mode-aware labels and URL editing. Keep TMDB presets
working independently; a public list is not another preset. Fold this into the
existing Collections work package, without confusing it with the higher-impact
carried smart-rule preservation defect. Collection synchronization, canonical
URL parsing and membership matching remain server responsibilities.

### D3 — Quick people search (medium-small; extends Search)

Upstream quick search now queries up to four people using the same media scope,
capability-gates access-filtered people search, puts exact name matches first,
and navigates to a person-filtered catalog. Evidence:
`web/src/components/GlobalSearch.tsx:26`, `:294`, `:334`, `:357` and
`web/src/hooks/queries/personSearch.ts:33`. Its selected result identity remains
stable while groups arrive; an optional people failure must not break title
results or falsely claim no matches when nothing else succeeded.

Native `src/SiloPlayer/Controls/GlobalSearchDialog.xaml.cs:125` queries catalog
and requests only, owns no people-result list, and renders only those groups
at `:214`. Add the capability-aware people group, cancellation, keyboard
selection and person-filter navigation. This expands the existing Search
package; September 27's unconnected full-page people search and duplicate-query
paging defects remain carried work, not newly discovered changes here.

### D4 — History-import target scope and run labels (small-medium)

Upstream now enforces that non-primary, non-admin profiles import only into
themselves and see only their runs. Primary profiles/admins can target other
account profiles. The actor-aware server contract and cursor scope are in
`internal/apiv2/history_imports.go:31`, `:354`, `:371`, `:410`; WebUI target
selection and run labels are in
`web/src/pages/settings/HistoryImportSettings.tsx:95`, `:140`, `:635`, `:709`.

Desktop still fills the target selector from every account profile
(`src/SiloPlayer/ViewModels/SettingsViewModel.cs:2018`;
`src/SiloPlayer/Views/SettingsPage.xaml.cs:3714`) and sends the selected
`ImportProfileId` (`SettingsViewModel.cs:1920`). A non-primary non-admin choosing
another profile therefore hits the new server rejection. Errors are already
surfaced (`:1974`); this is not a missing error-handler finding. Native run
cards omit target-profile names (`SettingsPage.xaml.cs:3818`).

Gate target choice using current role/profile, pin restricted requests to the
acting profile, label target profiles for multi-profile users, and reset stale
selection/run state when context changes. Server access enforcement and scoped
run lists are inherited, not client security features to duplicate. Fold this
into Account/profile integration work; do not reopen the already delivered
consumed-session/progress-display repairs.

### D5 — Calendar empty navigation (small)

WebUI now links every empty preset to the other two presets, normalizes legacy
`all` to `everything`, and uses a specific Trending empty message
(`web/src/pages/Calendar.tsx:258`, `:264`, `:285`). Native always supplies
Trending/Show everything buttons (`src/SiloPlayer/Views/CalendarPage.xaml:282`)
and hides both for Everything (`CalendarPage.xaml.cs:804`). Add the two other
views per preset and matching text. This is distinct from the shipped sticky
calendar navigation repair.

### D6 — Viewer-facing “For You” naming (very small)

WebUI renamed Recommendations in page heading/document title
(`web/src/pages/Recommendations.tsx:151`, `:168`) and sidebar
(`web/src/components/AppSidebar.tsx:760`). Desktop still says Recommendations
in `src/SiloPlayer/MainWindow.xaml:191`,
`src/SiloPlayer/Views/RecommendationsPage.xaml:42` and
`src/SiloPlayer/Helpers/DocumentTitle.cs:35`. Update viewer-facing copy without
renaming internal API routes/types or administrative feature terminology.

## Inherited changes and excluded scope

- **Visible collection counts:** the server now computes `item_count` from
  visible members/smart matches/display filters
  (`internal/api/handlers/collections_item_counts.go:20`;
  `collections_service.go:304`; `library_views_service.go:236`). Native already
  consumes `ItemCount` (`src/SiloPlayer.Core/Models/Collections/Collection.cs:25`)
  and renders it (`src/SiloPlayer/Views/CollectionsPage.xaml.cs:1819`, `:2100`).
  This is inherited when deployed and refreshed; no separate count algorithm
  or newly missing desktop feature is established.
- Server recommendation quota classification, collection synchronization,
  metadata-request matching, scanner changes and library realtime monitoring
  do not require copying server algorithms into the viewer. Admin-only logs,
  library monitoring, invitation/public-URL and inherited-policy UI changes
  stay in the deliberately external admin WebUI scope.
- No new personal profile/password/theme/provider controls changed in this
  bounded upstream interval beyond Home and history import above. September
  27's profile rating preservation, required-password flow/recovery,
  advisory-age, appearance, provider sync and theme-music findings remain open;
  their absence from this delta is not completion.

Relative effort estimates describe added native scope, not measured timelines.
Existing state-integrity and blocked-sign-in findings should remain ahead of
these feature/presentation additions in an impact-first combined backlog.
