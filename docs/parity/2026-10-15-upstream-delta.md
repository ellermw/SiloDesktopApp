# Official Silo upstream comparison — October 15, 2026

## Reference and limits

Fetched official GitHub main directly from `https://github.com/Silo-Server/silo-server.git` into the existing reference repository, without changing its checked-out historical files. All inspection used explicit Git objects, not that checkout's HEAD.

- New reference: **`9d852a15b21cfbb3f615a1b7fcea2055c15279ae`**, October 15, 2026, `feat(devices): let a profile name its devices (#2520)`.
- Previous recorded reference: **`22489b17b9ced83dbcb1d8281b1ac01bcbb30064`**, used for desktop 1.2.247's October 13 cursor investigation.
- Range: **72 commits**, 1,074 changed files. This includes tests, generated contracts, admin pages and server internals; it is not 1,074 desktop tasks.
- Desktop inspected: **1.2.247**, commit `1536209342909d8aa249f94df623f51f06ce5333`, initially clean.
- [Official source comparison](https://github.com/Silo-Server/silo-server/compare/22489b17b9ced83dbcb1d8281b1ac01bcbb30064...9d852a15b21cfbb3f615a1b7fcea2055c15279ae).

This is a source/contract audit. It does not certify rendered visual parity, prove a new playback failure, or identify the installed production server revision. No desktop code, installer, running app, account or infrastructure was changed. No build or runtime tests were run. This report remains uncommitted.

Implementation follow-up: the owner subsequently authorized these findings; the local **1.2.260** candidate and its scoped tests/native evidence are recorded in [the release record](../releases/1.2.260.md). The statements above describe the audit stage, before implementation.

## Confirmed desktop update areas

The ordering emphasizes compatibility and user-visible behavior before smaller presentation changes. Each numbered group describes an upstream delta verified against the desktop, not an assertion that the whole surrounding feature is missing.

### 1. Replace the retired appearance endpoint with branding accent handling

Upstream removed `GET /api/v2/theme/admin-css` and its override capability; its WebUI no longer applies admin CSS, font or token overrides. Branding's `accent_color` now drives primary actions, focus ring and sidebar primary, with contrasting foreground text and a high-contrast focus exception.

Desktop `SettingsApi.cs:418` still calls the removed endpoint. `SharedAppearanceState.cs:38` consumes its `Vars`, catches failures and resets overrides. `ThemeService.cs:250` applies those overrides. `MainWindow.xaml.cs:2697` reads branding but does not apply its AccentColor. This is a confirmed obsolete request and appearance gap; the catch means it is **not evidence of a startup crash**. Preserve existing server logo/favicon behavior while migrating appearance.

Sources: [removed contract](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/docs/settings-api.md), [current BrandingProvider](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/contexts/BrandingProvider.tsx), [contrast/accent rules](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/lib/accentColor.ts).

### 2. Add Reset Progress without changing watched history

New `GET /api/v2/progress/capabilities` advertises `clear_resume_position`; `DELETE /api/v2/progress/{media_item_id}/position` clears a movie/episode's resume point while preserving watched/completed state and history. WebUI offers **Reset Progress** on eligible cards and detail actions, including rewatches with a resume point.

Desktop has Mark Watched/Unwatched and Play from Beginning, but no capability, API method or Reset Progress action. Those existing actions do not substitute for the new operation. The new delete is explicitly non-retryable: retrying later could erase progress recorded since the first request.

Sources: [progress semantics](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/docs/progress-api.md), [card action model](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/components/MediaItemMenu.tsx). Desktop: `Controls/MediaItemMenu.cs`, `Core/Api/CatalogApi.cs`.

### 3. Support profile-specific device names

New `PATCH /api/v2/devices/{device_id}` accepts `custom_name`; null resets it. The list returns that field, and `supports_device_rename` gates Rename. The UI retains the reported hardware name beneath a different custom name, with Save/Cancel and reset behavior.

Desktop's `UserDevice` and settings capabilities lack the new fields; its device methods only list, clear settings and forget. `SettingsPage.Account.cs` displays `DeviceName`. Thus names assigned in the WebUI are also ignored today.

Sources: [device API](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/internal/apiv2/devices.go), [current device UI](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/pages/settings/DeviceSettings.tsx). Desktop: `Core/Api/SettingsApi.cs`, `Views/SettingsPage.Account.cs`.

### 4. Refresh localized reads when metadata-language preferences change

WebUI now cancels obsolete localized reads before invalidating catalog, Home, collection, episode, Calendar, shuffle and picker caches on local and realtime metadata-language changes. This prevents old-language in-flight responses from becoming fresh data.

Desktop `SettingsViewModel.SaveContractProfileSettingAsync` saves/clears supported overrides but has no corresponding localized-media invalidation. Its metadata-language setters use that generic save path. Add the matching cache/request lifecycle, including changes made by another client; do not implement it by repeatedly rebuilding Home.

Source: [localized invalidation and query families](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/hooks/queries/mediaSurfaceRefresh.ts), [settings write/realtime hooks](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/hooks/queries/settingValues.ts). Desktop: `ViewModels/SettingsViewModel.cs:930,966`.

### 5. Expand translation states beyond an individual detail description

New optional `machine_translated_fields` identifies overview/tagline provenance separately on titles, seasons, episodes and section cards. WebUI labels the actual translated field, adds on-view translation to visible Featured heroes, and starts season translation when a series/season page lists episodes missing the viewer's language.

Desktop already has an individual detail translation path in `ItemDetailPage.xaml.cs:2662`. Its detail/episode/Home-card DTOs do not carry the new machine-translated fields; the episode and Home-card DTOs also lack the new pending-language field. Therefore this is expanded scope and provenance UI, not a need to recreate existing detail translation from scratch.

Sources: [field definitions](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/api/types.ts), [hero behavior](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/components/HeroOverview.tsx), [series episode translation](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/pages/ItemDetail/SeriesContent.tsx).

### 6. Reconcile the reorganized Settings UI and moved controls

At desktop widths, Settings now replaces the app sidebar with settings navigation and opens Playback by default. The narrow index is a grouped searchable list. Navigation & Cards integrates the poster preview/badges; **Title Pages** is a separate page for title art, theme music, spoilers and advisory age. **Language & Subtitles** groups spoken/metadata/subtitle choices; Next up episodes moves to Home Screen. Watch-provider settings are collapsed until opened, and a save conflict opens them.

Desktop `SettingsPage` still opens its card overview, keeps the nested SettingsLayoutGrid rail, has separate overlay navigation and lacks the new Title Pages route. Existing controls largely exist but their grouping, location, labels and presentation are behind this upstream change. Preserve native-only SiloCast/playback preferences. Compare profile-default writes/reset behavior too: desktop Next Up save/reset still uses direct profile wrappers instead of the current effective-value writer that clears a shadowing device row.

Sources: [SettingsLayout](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/pages/SettingsLayout.tsx), [navigation/control index](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/lib/userSettingsNav.ts), [Title Pages](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/pages/settings/TitlePagesSettings.tsx), [Home preferences](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/pages/settings/HomeScreenSettings.tsx), [provider expansion/conflict](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/pages/settings/WatchProvidersSettings.tsx).

### 7. Honor cross-row duplicate suppression on independently loaded sections

WebUI Home and Library Recommended now apply recipe `avoid_duplicates` in layout order after separate section responses arrive, preserving unchanged section identity. Desktop loads sections individually; its RecipeCatalog model has `AvoidDuplicates`, but source usages do not apply it to displayed sections. Add the same ordered presentation rule without erasing raw source rows or causing poster refresh loops.

Source: [complete algorithm and loading semantics](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/pages/sectionDedupe.ts). Desktop: `ViewModels/HomeViewModel.cs`, `ViewModels/LibraryViewModel.cs`, `Core/Models/Home/RecipeCatalog.cs`.

### 8. Match episode/season personal-list eligibility

Current WebUI no longer offers Add to Favorites/Watchlist or a favorite shortcut for episodes/seasons. An item already saved by another/older client retains a Remove entry. Desktop `MediaItemMenu.cs:93` adds both actions whenever `showCollectionActions` is true; `PosterCard.xaml.cs:986` derives the heart from state/preferences without a type guard. Some episode surfaces already disable collection actions, but the shared model still needs the current rule, including removal of previously saved entries.

Source: [current menu model and shortcut eligibility](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/components/MediaItemMenu.tsx).

### 9. Match the combined Cast & Crew row

Movie/series pages now show one strip: directors/creators, writers, then cast, with labeled dividers, person deduplication and sharper portraits. Movie directors cap at two, writers at three, series creators are not capped, and cast caps at twelve. Series without creators fall back to directors.

Desktop `ItemDetailPage` still builds separate Cast and Crew sections, with cast capped at twenty. This is a new concrete layout/data-order difference.

Sources: [row implementation](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/components/CastCarousel.tsx), [crew grouping rules](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/components/castCrewGroups.ts). Desktop: `Views/ItemDetailPage.xaml:901,951`, `.xaml.cs:7215,7390`.

### 10. Recognize Atmos from track profile/title evidence

WebUI now recognizes Atmos from FFprobe profile/title/embedded-title evidence, distinguishes DD+ Atmos from TrueHD Atmos, and rejects misleading source titles on incompatible codecs such as an AC3 compatibility track. Version ranking preserves the evidence without changing the selected audio family incorrectly.

Desktop has track profile/title data but `VersionRanking.MapAudioLabel` accepts only the codec and recognizes Atmos only in that string. Update shared labels/ranking and their call sites, not only one badge's text.

Sources: [format/evidence rules](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/lib/mediaFormat.ts), [version selection](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/pages/ItemDetail/components/versionRankingUtils.ts). Desktop: `Core/Services/VersionRanking.cs`, `Core/Models/Playback/WatchDetailResponse.cs`.

### 11. Consume ratings supplied with episode lists

The v2 item/season episode lists now supply `ratings` in the normal source/name/score/display shape, allowing clients to avoid fetching every episode detail for scores. Desktop `Core/Models/Catalog/EpisodesResponse.cs` does not retain the field. Add the data mapping and check the intended episode presentation against the current WebUI; this source addition alone does not justify inventing new visible badges.

Sources: [catalog contract](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/docs/catalog-api.md), [episode fixture](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/contracts/api/v2/fixtures/list_season_episodes_ok.json).

### 12. Update recommendation explanations and headings

Current For You explains when rows are shared cold-start suggestions rather than personalized, removes the signal counter and changes empty-state wording. Section headings include **Profiles Like You Enjoyed** and **Highly Rated in Your Library**. Desktop retains the old unconditional personalized subtitle, signal count, empty text and headings.

Sources: [For You](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/pages/Recommendations.tsx), [section titles](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/pages/RecommendationsSection.tsx). Desktop: `Views/RecommendationsPage.xaml`, `.xaml.cs:96`, `ViewModels/RecommendationSectionViewModel.cs:76`.

## Playback verification priority — not a diagnosed desktop regression

Upstream added preventive reopening/reanchoring for **forward-only progressive remux after at least 60 seconds paused**, excluding Watch Together. This addresses an unread response hitting the server's write-stall deadline. Desktop has seek-reanchor and recovery already, but its normal pause/resume path does not contain this idle-time preventive step. Native mpv/relay buffering differs from browser playback, so reproduce that exact long-pause case before deciding whether the native path needs a change. Include local, remote/SiloCast and websocket pause/resume ownership in the test.

This is **not evidence that it caused the user's historical recurring direct-play buffering**. No fresh live buffering trace was examined in this audit.

Sources: [playback protocol](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/docs/architecture/playback-protocol-v3.md), [WebUI resume implementation](https://github.com/Silo-Server/silo-server/blob/9d852a15b21cfbb3f615a1b7fcea2055c15279ae/web/src/player/components/VideoPlayer.tsx). Desktop: `Services/PlayerService.cs:502` and pause command handlers.

Server-side dead-node replanning, HLS rehoming and immediate administrator pause/resume confirmation retain existing client wire shapes. Desktop already implements v3 replans and command results; targeted interoperability checks remain appropriate. Do not duplicate server routing logic in the app merely because the server implementation changed.

## Changes that do not require a matching native feature implementation

- Browser-only WASM/WebCodecs/WebGPU decoding and PGS drawing are browser implementation work. Native libmpv already owns decoding/subtitle rendering. Test outcomes where relevant rather than embedding a second browser decoder. Video playback speed is also already implemented in desktop `PlayerOverlay`.
- Recommendation isolation/ranking/embedding fixes, metadata localization, scanner sidecar-language/SDH support, request-router optional API keys, trickplay fixes, QSV/HDR transcode fixes, rate-limit Retry-After corrections and watched-state server fixes can benefit the desktop through existing APIs. Their existence is not proof of a required client rewrite or a cause of earlier buffering.
- Admin-only changes include ignored-unmatched management, bulk metadata translation jobs, recommendation embedding reset, all-library default-row restoration and large portions of the Settings/admin reorganization. These are separate from the requested non-admin user-facing parity scope. New translation outputs and account policy effects still matter to user-facing reads.
- Upstream deleted its Watch Tonight frontend dialog/cards but retains endpoints. Desktop retains unused dialog/API source; source search found no caller constructing/opening that dialog. Record as legacy cleanup, not a newly discovered visible desktop feature to remove blindly.
- Access-group household profile limits are now inherited policy. No new mandatory desktop user endpoint was identified; verify create-profile rejection wording against `profile_limit_reached` rather than claiming desktop account management is broken.

## Contract inventory and next execution

Parsed both pinned OpenAPI documents: **nine operations added, one removed**. User additions: progress capabilities, clear resume position, update device. Six additions are admin-only: ignore/unignore unmatched, start/list/cancel library metadata translation, reset recommendation embeddings. Removed operation: getThemeOverrides. The official `contracts/settings/v1` tree has **no diff** across this range; the new device-rename capability is outside that manifest. Generated OpenAPI churn does not mean every existing endpoint changed behavior.

Recommended next sequence: appearance compatibility first; reproduce the long-pause remux case early; implement Reset Progress/device names and language/cache lifecycle; then reconcile Settings and the remaining presentation groups with targeted regression and rendered verification. This recommendation is an audit result, not authorization to publish or replace the installed player.

Existing unresolved Next Up removal root cause, phone interoperability and historical native-suite verification limits remain as recorded in `docs/current-development.md`. This upstream pass does not close them or certify full parity.
