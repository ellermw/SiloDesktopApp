# Official upstream audit — October 20, 2026

Implementation record: these findings are implemented in released **1.2.286**, preserving the earlier1.2.272 caption repair. The two reported catalog/watched repairs are additional to the twelve upstream correction groups below (Seasonal Picks is recorded separately after the eleven numbered groups). See [the release record](../releases/1.2.286.md) for2,005 main tests,16 exact-payload native groups,24 published-service groups and precise limitations. The owner installed and approved the exact installer for publication. The audit baseline and read-only observations below are historical evidence, not a claim that the working source remained unchanged after implementation.

Read-only source audit. Desktop HEAD `9611c102481b1e3a208d5f676ae366ed6bc1bca8`, released 1.2.271; inspected working source deliberately newer at 1.2.272. Preserve the uncommitted October 19 caption sizing correction and all other local changes. No product/tests, installed app, upstream checkout, credentials or production changed by this audit.

Official repository: https://github.com/Silo-Server/silo-server . Exact comparison: `6ab2e85f117935776db052235efeeb149bdc81b4..784f2e439850b25188761c56fb76ee49d31ee32f`, 41 commits. Used `git show`/`git diff` at these exact hashes; the reference checkout's working-tree HEAD is historical and is not the source of current facts. Fetch was performed by the parent before this task; this audit used no network.

## Required alignment groups

### 1. Delivered quality rung, including native/OSC/SiloCast and lower-quality offers

Upstream `4e55f4b7911dcbd731596a0993b5395b88f09008`; `internal/playback/protocol_v3.go`, `plan_v3.go`, `web/src/player/playback-info.ts`, `components/QualityMenu.tsx`, `docs/architecture/playback-protocol-v3.md`.

New optional `playback_plan.active_quality` identifies an available_qualities label. `server_features` includes `plan_active_quality_v1`; when advertised, an absent field positively means no delivered menu entry. With requested preference Auto, Auto remains checked regardless of delivered label. With any other preference, check the named matching entry or none. Do not substitute the requested rung when decoder/server limits change it. Explicit rung retained under a bitrate-only cap; decoder reduction names lower class; wide/cinema frames handled by server. A lower version played unchanged can name its plain resolution entry. For old servers without feature, retain compatible existing inference.

Desktop: `src/SiloPlayer.Core/Models/Playback/PlaybackProtocolV3.cs` lacks the property; `Services/PlaybackManager.cs:196` re-derives a rung. `src/SiloPlayer/Services/PlayerService.cs` exposes requested `SelectedQualityIntent` to `Controls/PlayerOverlay.xaml.cs`, OSC metadata/script updates and `PlayerService.SiloCast.cs`. Separate request from menu highlight; preserve request in replans/settings. `OfferWatchPartyLowerQuality` uses nominal current-rung bitrate ahead of delivered bitrate; use minimum of positive nominal and delivered rates so a capped rung never offers a higher actual rate.

Verify advertised named/absent feature; old-server response; Auto; plain alias; explicit capped rung; 720p decoder holding 1080p; scope source; source copy; native/OSC/SiloCast consistent. Playback agent owns implementation.

### 2. Every Home/library row opens a full catalog page and library rows can pin

Upstream `d2a1488e4`; `web/src/components/SectionRow.tsx`, `MediaCarousel.tsx`, `pages/catalogSearchParams.ts`; server `internal/api/handlers/catalog_sections.go`, `internal/catalog/catalog_resolver.go`.

Desktop `Controls/SectionRow.xaml.cs:54` five-type allowlist rejects recommendations, trending, Continue Watching, Next Up, seasonal, etc. Home `Views/HomePage.xaml.cs:434` additionally requires known overflow; Library `Views/LibraryPage.xaml.cs:3202` requires count overflow. Remove both type/count restrictions for current v2 section browsing. Both title and Explore all target same CatalogNavigation for every nonempty row, including fewer items than item_limit. Current `UpdateNavigationPresentation` shows either title or Explore all; permit both and suppress redundant View suffix when Explore all exists. `UpdatePinAvailability` must not retain type allowlist.

Existing CatalogNavigation source=section/scope/section_id/library_id uses the correct v2 contract. Server now builds non-query rows in the original row order, capped at 200 (recommendation engines often 60); total_exact false at cap. Do not invent a client-side row snapshot or total. Keep current account/profile cursor authority. Check CatalogPage has no separate type allowlist.

Verify Home + library recommendation/seasonal/Continue Watching/Next Up and short row; title and Explore all; library pin; paging preserves row source/authority and total_exact. Native smoke at narrow and desktop sizes.

### 3. Retire user-created On deck; retain server-injected Next Up

Upstream `365e3d436fc2fac8e8b3f4f6d42111ff1baeb48b`; `web/src/lib/homeRows/catalog.ts`, `homeLayoutTransfer.ts`; `internal/api/handlers/sections.go`, `admin_sections_service.go`.

Server refuses new next_up admin/profile rows and type changes, removes existing extra rows via migration, but continues system-next-up when separate mode selected and accepts saving a legacy existing row. Native `Views/Dialogs/RecipeGalleryDialog.cs:87,100` fallback picker still offers On Deck despite upstream recipe removal; filter next_up from new/type-change choices. Existing legacy editing can retain the selected type without allowing creation. Label as Next Up where retained. `Core/Services/HomeLayoutTransfer.cs:117+` must skip imported added next_up rows, with an explanatory skipped entry, to avoid whole import rejection. Do not remove next_up handling from playback/dismissals/reconciliation/landscape rendering.

Verify gallery and Edit type list; legacy existing row survives normal save; layout import skips next_up while retaining unrelated fields/rows; separate mode still shows system Next Up.

### 4. Server defaults for title art and Next Up preferences, including realtime refresh

Upstream `1ca1ba5b3` + `d5126cc29`; `contracts/settings/v1/manifest.json` now revision22 (title art revision21; Next Up defaults revision22); `web/src/hooks/useTitleArt.ts`, `pages/settings/TitleArtSettingsGroup.tsx`, `components/homeRows/HomePreferencesCard.tsx`, `AdminNextUpDefaultsCard.tsx`, `hooks/queries/settingValues.ts`, `lib/settingsAdminDefaults.ts`.

`ui.title_art`, `ui.next_up_mode`, `ui.next_up_new_releases_first` now have admin_default=true. Effective source remains default; explicit equal-to-default value remains an override. Administrator keys are `defaults.<canonical-key>` and hold validated JSON representations via existing admin setting writes. If native admin settings expose corresponding area, add these appearance/Home defaults with current version/concurrency semantics rather than profile writes.

Native title-art reads (`ViewModels/SettingsViewModel.TitleArt.cs`, `ItemDetailPage.LoadTitleArtPreferenceAsync`) already honor returned effective boolean; no default=true hardcode replacement needed after successful read. Missing: retain effective Source, show confirmed inherited explanation, offer Use server default only with own choice. Clear this device value, then profile value only when profile was effective source, preserving other devices. Use captured authority between writes and tolerate missing-value 404. Never show inherited-source claim on pending/failed read.

Home mode already has reset (`SettingsViewModel.cs:1206`) but no source-aware text/button. New-release model `Core/Services/NewEpisodesFirstPreference.cs` and `SettingsViewModel.HomePreference.cs` already load Source but have no reset; add profile deletion with guarded reload and shared HomePreferenceChanged. `Views/SettingsPage.xaml:1396-1420` needs inherited notes and Use server default labels. Home/library must reload layout as well as items when Next Up mode changes, because system row presence changes.

`MainWindow.xaml.cs:1685` handles subtitle setting events only; `LocalizedMediaReads` handles metadata-language keys only. Add narrow invalidation for these3 keys on `user_settings.changed`, respecting profile_id. A scope=default event has no account/profile; reread effective values and relevant detail/Home/library. Schedule one additional guarded reread after >10-second per-node default cache TTL (Web uses11sec); cancel/ignore when account/profile changes. Refresh settings controls currently displayed. Server data, not event values, determines behavior.

Verify profile/device overrides and equal values; reset, other devices preserved; pending/error no false inherited note; default event with no profile; cross-profile ignored; late reread cannot affect new authority; Next Up layout insertion/removal. Do not claim repairing historic intermittent Next Up removal.

### 5. Detail episode captions match current episode-card presentation

Upstream `ed40fa20f`; `web/src/pages/ItemDetail/components/EpisodeCardCaption.tsx`, `episodeCardText.ts`, `EpisodeCarousel.tsx`, `SeasonEpisodeGrid.tsx`, `hooks/useGridRowCap.ts`.

This change targets detail season grids and sibling episode carousel; generic Home `ItemCard` only gets an aria-label correction. Preserve local1.2.272 Home/library poster caption sizing.

Native shared detail card builder `Views/ItemDetailPage.xaml.cs` around8170 uses a separate Episode N row, bare title, runtime before date and 2-line description. New caption: first line `S01E02 · Title` (code alone for blank title), metadata `Feb 18, 2026  ·  52m` using runtime formatting and stored day, three description lines with reserved three-line height when nonempty/visible. Watched indicator at end title, unreadable notice + compact translation label at metadata. Spoken first line should say Season1, Episode2, Title; season0 Specials. Spoiler protection hides only description/image per existing policy; title/date/runtime remain. Keep current episode highlight and context/quick actions. Native TV responsive cards currently collapse metadata/description below1024; compare intentional TV mode behavior separately rather than silently applying browser phone policy to TV.

Verify grids/carousel same helper and missing/title/date/runtime combinations; specials; spoiler hidden overview; machine translated/unreadable indicators; 3 lines and equal heights; no clipping across widths/expand/font sizes. Existing generic poster1.2.272 tests must stay passing.

### 6. Calendar dates stay on stored day

Upstream `ebfd35426`; `web/src/lib/datetime.ts` adds strict valid YYYY-MM-DD parser; date preferences honored by episode hero/card date and person birth/death. Timestamps remain localized.

Native concrete bug `ItemDetailPage.FormatDetailDate` at6832 uses AssumeUniversal|AdjustToUniversal, then DateTimeDisplay.FormatDate calls ToLocalTime. Read-only .NET probe confirmed input2026-10-18 renders2026-10-17 in Central Standard Time. Add explicit DateOnly handling for calendar strings in shared date helper; preserve configured pattern/medium style; retain true timestamp path. `MediaItemDisplayText.FormatDate` uses default DateTimeOffset.TryParse (local for date-only) so is not proven same bug, but centralized calendar helper avoids future UTC drift. PersonDetailViewModel uses DateTime.TryParse with direct formatting and already preserves day; do not rewrite age logic unnecessarily.

Verify YYYY-MM-DD air/release/birth dates across UTC- and UTC+ zones, leap dates/invalid dates, ISO preference; real offset timestamps still localize.

### 7. Extras use still thumbnails and precede YouTube videos

Upstream `3483f716b` + `69f76ed2a`; `web/src/pages/ItemDetail/components/ExtrasSection.tsx`, MovieContent/SeriesContent; server `internal/apiv2/catalog_items.go`, `internal/models/extras.go` adds still_url/still_thumbhash.

Native `Core/Models/Catalog/MediaItemDetail.cs:158` ItemExtra lacks both image fields. `Views/ItemDetailPage.xaml.cs:4706+` currently plain horizontal buttons in1/2/3 columns. Render native16:9 image cards,2 columns narrow /3 >=640 /4 >=1024 /5 >=1280 (same breakpoints); use authenticated ImageService + existing thumbhash loader, hover/focus play affordance, title and duration below. Failed/missing URL falls back to thumbhash/placeholder without empty frame or blocking playback. Honor normal image cancellation/authority guards. Extra.ContentId remains first-class playback target. `ApplyCurrentDetailSectionOrder` around1795 currently trailers then extras for movie/series; swap. Local extra thumbnails are generated/server-side; no native filesystem probing/generation.

Verify model decode, present/missing/failed image, grid aspect/responsive counts, keyboard play, correct ContentId; extras before trailers for movies and series.

### 8. Search immediately below Home/custom primary shortcuts; OAuth provider content centered

Upstream `c87ef256a` + `70f8cf631`; `web/src/components/AppSidebar.tsx`, `web/src/pages/Login.tsx`.

Move SearchNavItem in `MainWindow.xaml` from Discover to after Home and before Libraries. `ApplyPrimaryMenuCustomization` currently inserts shortcuts just before Libraries; ensure custom shortcuts appear before fixed Search, as Web does; Search remains present with custom primary menu. Preserve Ctrl+K and rail/selected state. Hide empty Discover header when custom menu removes For You/Calendar and Requests/Notifications unavailable, if corresponding native current gating warrants.

`Views/LoginPage.xaml:145+` OAuth Button and inner StackPanel both explicitly Left; center icon+display-name content in full-width OutlineButton. Preserve capability/provider gates and callback handshake.

Verify ordinary/custom primary sidebar, collapsed rail, selected Search/Ctrl+K; one/multiple OAuth providers with/without icon.

### 9. Synced collection help text no longer claims500 cap

Upstream `e3ba8a5a3`; `web/src/components/collections/fields/OrderBlock.tsx`, `web/src/lib/collections/copy.ts`.

Native CollectionEditorViewModel and SyncedPresentation positive integer input already accept >500; do not add a new cap. `Views/CollectionEditorPage.SyncedPresentation.cs:86` still says Blank takes whole list up to500. Align to up to10,000 titles from MDBList, first20 of TMDB chart. Backend full matching/paging fixes need no local replication.

Verify blank and10000 serialize unchanged for synced list and copy is accurate. No new implementation-mirroring test needed for only copy.

### 10. Discord account mutations require primary profile

Upstream `cf90805e0`; `web/src/pages/settings/NotificationsSettings.tsx`, `docs/notifications-api.md`.

Server now requires household-manager authority (primary profile, PIN proof where required) for link init, mode writes and unlink. Reads remain available. Native `Controls/NotificationSettingsControl.xaml:119-158` exposes enabled Link/Unlink/toggle/mode controls with no primary gate; `NotificationSettingsViewModel.cs:205-226` lacks permission check. Add primary-profile gating and notice Switch to the primary profile to manage Discord notifications for this account. Recheck captured current authority before sending mutations; retain existing no-replay write semantics and error handling. Admin account role does not override a selected secondary profile. Do not change email/webhook settings authority based on Discord rules.

Verify primary vs secondary (including secondary on admin account), switched-profile pending load/dialog, read-only data still displayed, no mutation HTTP from disallowed profile.

### 11. Watch Party starts only under room command authority

Upstream `c747c4ed8`; `web/src/player/components/VideoPlayer.tsx`, `hooks/useWatchTogetherPlaybackSync.ts`.

Native `PlayerService.cs:2110` begins initial transport restorePaused:false. FileLoaded at4232-4237 unpauses before coordinator's250ms tick. `WatchPartySyncController.Tick` correctly defers commands until Loaded&&!Busy and already projects late play command's position. But initial no-command/waiting startup can still play independently; prevent this by holding room transport paused until applicable room command says playing, and perform late play's current room-position seek before unpause. Preserve standard standalone autoplay. Recovery before first frame should target current playing-room position; inspect native existing recovery path before altering. Busy/not-loaded controller already suppresses reports, so browser readyState mechanism itself does not need porting.

Verify initial waiting barrier, play arriving before media load, late loaded member, initial pre-frame recovery, paused room, initial no command, standalone still autoplays; no paused report claims a held play. Playback agent owns implementation.

## Already aligned / additive only / server-only

Provider links removal `5e1609949`: native ItemDetail actions have no IMDb/TMDB/TVDB provider-link row (rg source); nothing to remove. Keep metadata IDs used for requests/editing. This is separate from intentionally supported external watch-provider apps.

Seasonal Picks `448a8f743` is backward-compatible server rendering, but current feature parity requires native editor support: see next section. Browser decoder speed/default (`373692b7a`,`3efc8b650`) do not replace libmpv; native playback-speed path already exists. Hung-node recovery `c9082ba38` keeps existing retry/replan contract; no new native health probing or routes. API-key hashing `ba2de9827` keeps one-time creation response/auth contract; never display server verifier. Profile PIN atomic creation, initial setup JSON restriction, per-account rate allowance, event personal isolation, Seerr4K gating, curation authorization, notification destination and push fixes are server authority fixes with stable existing client wire contracts. Preserve JSON request bodies, primary-profile authority and nonretryable semantics.

## Seasonal Picks editor (included user-facing addition; wire-compatible)

Upstream `448a8f743`; `docs/architecture/seasonal-picks.md`, `web/src/components/homeRows/addRow/SeasonalFields.tsx`, `web/src/lib/homeRows/seasonal.ts`, `variants.ts`, `rowDraft.ts`, `hooks/queries/seasonalThemes.ts`; endpoint implementation `internal/apiv2/home.go`.

Native `Views/Dialogs/RecipeGalleryDialog.cs:36` hardcodes8 themes and `AddSeasonalFields` at928 only edits enabled_themes/theme_titles. Add authenticated GET `/api/v2/sections/seasonal-themes` models/API and bind server calendar-order catalog. It returns themes(key/name/when/start/end/dates_editable/vibes), max_custom_holidays; vibe includes key/name/title/description/list_url. Includes Easter and New Year missing native; hidden legacy summer omitted from editor but preserved in old config.

Controls needed: each enabled holiday's vibe and optional override title; optional MM-DD start/end for dates_editable themes; custom holidays(name/start/end/public MDBList list_url/optional title) up to server max(current12); Picks from=Silo recommendations(default omitted or recommendations) vs The lists only(source=lists). No season selection/math or list downloads locally; server chooses first active custom holiday then first in-season built-in, list rank/embedding fill.

Retain arbitrary config outside edited fields. Validate all present date pairs (Feb29 permitted annual boundary, invalid month/day refused), custom names/URLs (http/https mdblist.com or www, /lists/, no userinfo, nonstandard ports refused), at least one built-in/custom holiday. Family movie night variant has vibe/source but no holiday/custom/date editor; switching to it clears hidden custom_holidays and theme_dates so they cannot override unseen. `Core/Services/HomeRowVariants.cs` must include new_year/easter in Holidays default, infer custom-only rows as Holidays, and clear custom/date fields on Family switch. `Core/Services/HomeRecipePresentation.cs` should describe list-based behavior rather than old genre/keyword rules if outdated.

Verify config roundtrip with unrelated filters/titles, custom-only row,10themes from server, missing catalog error/retry,404 old-server fallback without wiping modern existing config, leap/wrapping windows, invalid URL, family variant clearing, new-theme default values. Preserve existing rows on catalog read failure; never replace failed catalog with hardcoded modern truth.

## All41 commits disposition ledger

| Commit | Subject / desktop action |
|---|---|
| df3154660 | Query cost SQL site/shape: server observability only. |
| 448a8f743 | Seasonal lists/vibes: server rendering compatible; editor feature parity above. |
| 54844200a | SubDL result ID secret removal: opaque IDs consumed unchanged; server-only. |
| 76d948a56 | Edit row phone fit: browser viewport styling; verify native dialog viewport already scrolls, no API migration. |
| e3ba8a5a3 | Synced lists>500: native accepts values; help copy correction above. |
| 373692b7a | Browser decoder speed: native libmpv speed already independent. |
| a434bbc2f | Jellyfin all named Ids without Limit: server compatibility only. |
| 365e3d436 | Retire added On deck: picker/import changes above. |
| 5e1609949 | Remove provider links: native already absent. |
| ebfd35426 | Stored date: native detail formatter has same UTC-shift bug; fix above. |
| 70f8cf631 | OAuth button centered: native alignment fix above. |
| e10198ce1 | Document no-rate player seek catchup: docs only; native existing rate-capable controller retained. |
| 1ca1ba5b3 | Admin defaults title art+Next Up/revisions21-22: scoped read/reset/admin/realtime parity above. |
| b603a59ea | Initial setup JSON media type: native JSON request helper already valid; server-only tightening. |
| 4859f6fb0 | Personal websocket profile isolation: server envelope filtering; native retain profile guards. |
| 387c497fc | Requested PIN atomic create: existing native PIN payload stays valid; server transaction. |
| dd3f7a604 | Preserve empty v1 group policies: server bridge fix, native v2 unaffected. |
| 3efc8b650 | Browser decoder default: native libmpv unchanged. |
| ed40fa20f | Detail episode cards: native shared caption update above. |
| c747c4ed8 | Room-start authority: native initial FileLoaded autoplay gap above. |
| d5126cc29 | Admin-default event delivery: consume no-profile invalidation as above. |
| c87ef256a | Search after Home: native move/custom-menu ordering above. |
| 3483f716b | Extras beside-file thumbnails: consume optional fields/grid above. |
| d2a1488e4 | Every row full page: native navigation/pin gates above. |
| 69f76ed2a | Local extras before YouTube: swap native order above. |
| 951422ba4 | Jellyfin stored session authority: server compatibility fix. |
| 114575a16 | Private-mode webhook blocked destinations: server validation, existing HTTPS error display retained. |
| 9e81ab0bc | Legacy mobile push account ownership: desktop has no mobile push bridge adoption. |
| 10bed17a9 | Web tsrs typecheck: build tooling only. |
| 1abb94d37 | Frontend Node24 CI: no .NET/native dependency change. |
| 3bc0cdc55 | Vite6.4.4: browser dependency only. |
| cf90805e0 | Discord household authority: native readonly primary gate above. |
| c3093d0d3 | Seerr no4K request gating: server capability/routing; no changed desktop request fields. |
| ba2de9827 | API-key one-way verifier storage: same wire creation/auth; server migration only. |
| d6d84d99b | IntelVAAPI HDR tonemap OpenCL: server FFmpeg executor selection; preserve native playback capabilities. |
| f75cdf363 | Meilisearch draft check credentials bound to saved config: server check semantics unchanged, UI no migration. |
| 54f85f017 | Curation destination authorization: server validation/transaction scope, unchanged native operations/errors. |
| 709381437 | Account-wide shared rate allowance: same429/Retry-After; retain current client response handling. |
| 4e55f4b79 | Active delivered quality: new feature-aware field/menu behavior above. |
| c9082ba38 | Hung node rehome: server liveness/recipe preservation; native uses existing failure-recovery route. |
| 784f2e439 | Testing decision rules docs: no application contract change. |

## Verification boundary

This report is source evidence and one read-only .NET date probe, not implementation verification or visual certification. No tests/build/installer run by this audit. After implementation run relevant main tests, native detail/row/menu/notification scenarios at multiple widths plus existing1.2.272 caption fixtures, and exact-payload playback checks for quality/room startup. Preserve explicit old-server behavior and captured account/profile authority. Record exact upstream hash in release/parity documentation. Actual phone interoperability and historical Next Up removal/buffering root causes remain outside this audit's claims.

## Scoped implementation follow-up (same task)

After the read-only audit, parent authorized implementation of SectionRow/Home/library navigation, retired On deck add/import behavior, and Seasonal Picks contracts/editor only. Implemented in the primary checkout, preserving existing local caption work:

- `Controls/SectionRow.xaml[.cs]`, HomePage.ConfigureSectionNavigation, LibraryPage.ConfigureLibrarySectionRow: every row opens via title and Explore all, short-row restrictions removed, library pin type gate lifted; recommendation-only title-link treatment preserved.
- `RecipeGalleryDialog.cs` plus new `RecipeGalleryDialog.Seasonal.cs`: remove next_up creation/type choices; old existing type remains editable; seasonal server catalog, vibe/title/date fields, custom holidays, source, validation, retry/read-failure save guard, profile guard, 404 legacy fallback. Existing HomeRowDialog already handles full-height narrow viewport, so no duplicate phone-dialog patch.
- New `Core/Models/Home/SeasonalThemes.cs`, `Core/Services/SeasonalRowConfiguration.cs`; HomeApi catalog GET. HomeRowVariants includes Easter/New Year, custom-only inference, family switch clears hidden custom/date overrides; HomeLayoutTransfer skips imported added next_up.
- Added tests: `October20HomeRowsTests` (3), `SeasonalRowContractTests` (9), native `October20HomeRowsNativeFixture.RunAsync(StackPanel)` (caller must register). No Program.cs edits by this agent.

Evidence: initial three regression tests failed for expected custom-only inference, hidden custom/date retention and imported next_up behavior; after code, 16 bounded tests (October20HomeRowsTests + HomeRowVariantTests + HomeLayoutPolicyTests) passed. First shared-output attempt met an existing concurrent testhost file lock; no process stopped. Subsequent red/green used `.codex-tmp/upstream-oct20/rows-test-artifacts`; after parent clarified named-mutex policy, no further dotnet/build/test commands were run by this agent. Remaining new contract/native tests await parent-coordinated run under SiloOct18Build mutex. `git diff --check` passed; CRLF normalization warnings are informational. This does not claim complete app build, native verification, or installer QA.
