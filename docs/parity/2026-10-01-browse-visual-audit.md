# Browse visual and functional source audit — 2026-10-01

This is a source audit of the pulled official Silo WebUI and the current native candidate. It is not a claim of 100% visual or functional parity. The official reference is [Silo-Server/silo-server main at 8e2e840474a085c6df6571a5a2850f7eb996810c](https://github.com/Silo-Server/silo-server/tree/8e2e840474a085c6df6571a5a2850f7eb996810c); every official citation below is pinned to that commit. The checkout is stale, so reference reads used `git show`, `git grep` and `git ls-tree` at the exact ref rather than checkout files.

Native candidate: `C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer` (1.2.0 candidate, existing dirty changes preserved). Native line references refer to the current working files, not an immutable commit. Applicable AGENTS instructions were read; no root CONTEXT.md was found. This pass made no application-code changes, production writes, credential reads, builds or test runs. The only write by this subagent is this requested report.

There are **45 actionable source discrepancies: 7 P1, 31 P2 and 7 P3**. P1 means a significant route/interaction or query contract gap to prioritize; P2 means meaningful functional, state, layout or responsive divergence; P3 means a narrower visual difference. These are audit priorities, not claims of observed production incidents. Race and overflow outcomes are explicitly marked as source inferences requiring runtime reproduction.

Native CUA surfaces are disabled and browser inventories were empty. No matched screenshots, pixel measurements, live browser/native interactions or production API mutations were obtained. Rendered acceptance remains pending and does not block this requested source pass. The discovery inventory [2026-10-01-user-surface-inventory.json](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/docs/parity/2026-10-01-user-surface-inventory.json) is an index, not evidence that all 323 TSX or 132 native files have had every state validated.

## Route and surface ledger

**S = source-reviewed; U = runtime/rendered-unverified; N = not-inspected in this browse report.** A source-reviewed row means its layout and named dispatch paths were traced; it does not certify every data/permission/error combination. The ledger has 22 entries, including the global quick-search dialog and conditional collection editor variants. All are U. The exact official route declarations are in [web/src/App.tsx:619–644](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/App.tsx#L619-L644) and [web/src/App.tsx:698–711](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/App.tsx#L698-L711); Home is the app root surface.

| Route/surface | Official page and imported structure | Native mapping (under src/SiloPlayer unless noted) | Status | Reviewed states/actions and limits |
|---|---|---|---|---|
| Home `/` | Home.tsx → HeroBanner, TasteSeedBanner, SectionRow; layout/loading/section retries/empty | HomePage.xaml/.cs; HomeViewModel; HeroCarousel; SectionRow | S / U | Hero controls, seed customization entry, row Explore/pin/retry, dismiss/watch-state dispatch reviewed. Settings Home editor is outside this report. |
| Library Recommended `/library/:id?tab=recommended` | LibraryPage + LibraryHeader + LibraryRecommended | LibraryPage.xaml/.cs; LibraryViewModel; HeroCarousel; SectionRow | S / U | Default tab/type labels, hero/loading/error/empty, collection/section shelves, pins and scroll overlay reviewed. |
| Audiobook Library Home | LibraryRecommended + NowListeningHero | LibraryPage; NowListeningHero; audiobook square cards | S / U | Deck/rest-row dispatch, Resume/More Info and static metadata reviewed. Player internals belong to media audit. |
| Library Browse `/library/:id?tab=library` | LibraryBrowse + CatalogFiltersPanel + ItemGrid + ScrollToTopButton | LibraryPage; LibraryViewModel; LibraryGridCard; virtual catalog path | S / U | Search/type/sort/order/filters/badges, advanced groups, load-more/scroll-to-top, state retention and cancellation reviewed. |
| Audiobook Books/Authors/Narrators/Series | LibraryBrowse + AudiobookGroupsView | LibraryPage audiobook axis/group handlers; CatalogApi | S / U | Axis, group search/sort/paging/covers, group-to-books filter dispatch reviewed. Every grouped API response variant remains unexercised. |
| Library Collections `/library/:id?tab=collections` | LibraryCollections + CollectionPosterCard | LibraryPage collection section/card builders; LibraryViewModel | S / U | Group/ungrouped/personal sections, empty/loading, poster/count/caption, pin and collection navigation reviewed. |
| Generic `/catalog` and section catalog | Catalog + shared filters + ItemGrid | CatalogPage.xaml/.cs; CatalogNavigation; MainWindow section routing | S / U | Source/scope/type/search/sort/order/guided/advanced, loading/error/empty, item menu and section navigation reviewed. All source permutations need fixtures. |
| Favorites `/favorites` → catalog | Catalog + narrowPosterActions | MainWindow → CatalogPage (legacy FavoritesPage dormant) | S / U | Actual sidebar path, list order, filters, state changes and card dispatch reviewed; legacy page is not credited as route coverage. |
| Watchlist `/watchlist` → catalog | Catalog + WatchlistTabs/TitlesTab | MainWindow → CatalogPage; legacy WatchlistPage not sidebar target | S / U | Local list reviewed. External-title route omission confirmed; dormant external implementation inspected for routing diagnosis. |
| History `/history` → catalog | Catalog selection toolbar + ConfirmDialog | MainWindow → CatalogPage history mode | S / U | Loaded selection/select all/clear, selected/all-history removal, confirmation and query refresh dispatch reviewed. Full retention backend is outside scope. |
| Full Search `/search` redirect and query Catalog | Catalog + SearchBar/ScopeChips/CastCarousel/RequestToAddSection | SearchPage.xaml/.cs; SearchViewModel | S / U | Empty/results shells, media scopes, sort, people/media/outside-result regions, filters, pagination and request/detail dispatch reviewed. |
| Quick Search global dialog (no route) | GlobalSearch | GlobalSearchDialog.xaml/.cs; MainWindow entry | S / U | Debounce/scopes/library/people/request rows/footer/arrows/Enter/Escape/navigation/cancellation reviewed; CUA focus proof pending. |
| Collections `/collections` | Collections + GroupedCollectionsBoard + template gallery | CollectionsPage.xaml/.cs; CollectionsViewModel | S / U | Personal grouping, create/rename/delete, collection edit/sync/delete/menu, drag/move, server shelves, templates/import reviewed. All drag/drop coordinates unverified. |
| Collections new `/collections/new` | CollectionEditor + UserCollectionForm/CollectionBuilder | CollectionEditorPage; CollectionEditorViewModel | S / U | Mode/default, name/access/visibility/rules/manual controls, validation/save/cancel dispatch reviewed. |
| Manual edit `/collections/:id/edit` | CollectionEditor + ManualCollectionItemsEditor | CollectionEditorPage manual item builders; CollectionEditorViewModel | S / U | Item search/add/remove/reorder staging, capability/read-only gates and display controls reviewed. |
| Imported edit `/collections/:id/edit` | ImportedCollectionEditor | CollectionEditorPage source sections; CollectionEditorViewModel | S / U | Source banner, display/scope/share/visibility/poster/source settings, sync and save/discard/cancel reviewed. Every provider-specific option combination not inspected. |
| Smart edit `/collections/:id/edit` | SmartCollectionWizard | SmartCollectionWizardPage; SmartCollectionWizardViewModel | S / U | Filter/details steps, query groups/preview/limit, access/visibility, upload/URL and save/navigation reviewed. |
| Collection browse `/collections/:id` redirect / catalog collection sources | Catalog for user/library collection | CollectionBrowsePage.xaml/.cs; CollectionBrowseViewModel | S / U | Header/back/pin/order/type/filter/badges/items/error dispatch reviewed; unified Web filter mismatch documented. |
| Person `/person/:id` | PersonDetail + ItemGrid + EditPersonDialog | PersonDetailPage.xaml/.cs; PersonDetailViewModel | S / U | Photo/bio/metadata/type-filter/filmography/loading/error/realtime dispatch reviewed. EditPersonDialog interior N in this report. |
| Calendar `/calendar` | Calendar + WeekNavigator/DayGroup/CalendarEventCard | CalendarPage.xaml/.cs; CalendarViewModel | S / U | Preset/library/week/today/day selection, cards/badges/time, sticky/scroll/empty/loading/error and item navigation reviewed. |
| Notifications `/notifications` | Notifications + preferences popover | NotificationsPage.xaml/.cs; NotificationsViewModel; NotificationsApi | S / U | All/unread/paging/cutoff/mark-read/mark-all, request/catalog row targets, preferences and failure presentation reviewed. |
| Recommendations `/recommendations`, `/recommendations/section/:kind[/:key]` | Recommendations/RecommendationsSection + SectionItemCard | RecommendationsPage; RecommendationSectionPage; matching view models; SectionRow | S / U | Taste summary, row/Explore, section back/loading/retry/empty/cards and prefs dispatch reviewed. Recommendation-model ranking correctness not inspected. |

## Imported menu, dialog and shared-control ledger

These 24 rows group imports sharing a source implementation; the root discovery JSON retains individual component/control locations. **No control below has runtime/rendered acceptance.** N interiors are declared rather than silently credited as complete.

| Official import/control | Native counterpart | User-facing controls traced | Status | Pending/not-inspected boundary |
|---|---|---|---|---|
| HeroBanner | HeroCarousel | Slide controls/auto-advance/arrows, Play/Resume/More Info, overview/metadata/artwork/size | S; U | Gradient compositing, animation timing, reduced-motion output and hit targets need rendered verification. |
| NowListeningHero | NowListeningHero; Library rest SectionRow | Deck/rest-row, cover, credits/progress/time-left, Resume/More Info | S; U | Active player state and hydrated chapters diverge (B04); playback details audited separately. |
| TasteSeedBanner | HomePage banner/build handler | Show/hide and customization navigation | S dispatch; U | Full settings editor interior N here; owned by settings audit. |
| SectionRow + MediaCarousel | SectionRow.xaml/.cs | Explore capability, pin, horizontal scroll/arrows, loading/retry and poster/wide/audiobook variant | S; U | No matched layout/gesture/focus screenshots. |
| ItemCard/SectionItemCard/ContinueWatchingCard/MediaCardArtwork/CardOverlays/CardPlayOverlay | PosterCard; LibraryGridCard; landscape/audiobook card builders; overlay settings | Caption/artwork dimensions, overlay/action dispatch and continuation variant selection | S structural/dispatch; U | All artwork failure/thumbhash/overlay combinations and long-press/touch states remain runtime-unverified; individual decorative overlay interiors only sampled. |
| MediaItemMenu | MediaItemMenu.cs | Restart/play, watched/unwatched, favorites/watchlist, add collection, refresh/match, manga details and dismiss | S; U | Edit Metadata/Play History missing (B44). Refresh/Match/EditMetadata/MangaFiles dialog interiors N in this report; the [conditional-dialog supplement](2026-10-01-conditional-dialogs-visual-audit.md) records selected interior contracts, and media covers detail entry/state boundaries. |
| LibraryHeader/tabs | LibraryPage header handlers | Type-dependent labels, transparent/glass overlay, Recommended/Home/Library/Collections selection | S; U | Window-chrome/content viewport alignment and scroll threshold need side-by-side capture. |
| AudiobookGroupsView | LibraryPage group builder | Axis/search/sort/load-more/group selection and covers/counts | S; U | All query/race fixtures pending; B06 identifies superseded load path. |
| CollectionPosterCard | Library collection card builders; Collections server shelves | Artwork fallback/count/caption/pin/collection navigation | S; U | Pin popover styling and artwork loading animation not rendered. |
| CatalogFiltersPanel/Bar/Sheet | Library/Catalog/CollectionBrowse/Search filter sheets | Type/library/sort/order, Guided/Advanced modes, clear/done/count | S; U | Substantial editor divergence B07/B11–B16/B31; sheet modality/accessibility runtime pending. |
| CollectionGuidedRulesEditor/RulesEditor/FilterRuleEditor/PersonSearchSelect/FacetSearchSelect/LibraryMultiSelect | Library advanced rows; Catalog flat rows; QueryRulesEditor | Field/operator/group/match/value controls, multi-library dispatch | S; U | Every allowed operator/field combination not exercised; autocomplete native controls absent on several surfaces. |
| ActiveFilterBadges | Library/CollectionBrowse badge builders; Search aggregate badges | Readable active state, clear one/all | S; U | Catalog badge row absent B13. Badge recomputation/reflow under all combinations remains U. |
| CastCarousel / full-search people | SearchPage horizontal PeopleRepeater and person navigation | People region/results/fallback/target | S; U | Person thumbnail loading and keyboard carousel focus require rendering; people errors swallowed B17. |
| WatchlistTabs/WatchlistTitlesTab | Legacy WatchlistPage only, not current Catalog sidebar route | Local/outside list switch, attention/count/request/remove | S routing; U | Current route does not mount these controls B10; all outside-title states cannot be validated there. |
| RequestToAddSection / quick-search requested rows | SearchPage/GlobalSearchDialog request row builders | Feature/scoped dispatch and request-detail navigation | S dispatch; U | Full RequestDialog/detail interior N here; requests audit owns it. |
| GroupedCollectionsBoard | CollectionsPage group section/menu/drag builders | Create/rename/delete groups, empty drop target, move/reorder collections | S; U | Exhaustive drag sensor/keyboard/empty-group mutation paths unexercised. |
| CollectionTemplateGallery/Card/ConfigForm/TemplatePosterField/MDBListBrowser | CollectionsPage gallery/config/MDBList builders; import methods | Category/search/pick/back, source config, library/profile scope, poster/default/custom, top/search/select/import | S; U | All source-provider configuration variants only partly inspected; no provider requests run. B21–B24 concrete source divergences. |
| CollectionBuilder/AccessEditor/ManualCollectionItemsEditor | CollectionEditorPage/ViewModel | Mode/name/access/visibility/display/query, manual search/add/remove/reorder | S; U | Server capability permutations and concurrent item snapshot mutations untested. |
| ImportedCollectionEditor/ImageUploadField/dirty dock | CollectionEditorPage imported sections; poster handlers | Source-managed fields, edit/sync/share/display/scope/poster/save/discard/cancel | S; U | All provider-specific immutable/mutable field sets not fully inspected. File picker, drop and all MIME errors remain U. |
| SmartCollectionWizard/ImageUploadField/preview ItemGrid | SmartCollectionWizardPage/ViewModel | Step navigation, filter/query/preview/limit, metadata/access/poster/save | S; U | Artwork deletion absent and preview >100 absent. All form validity/race states pending. |
| WeekNavigator/DayGroup/CalendarEventCard | CalendarPage builders | Week/date/today/dots/day focus, event poster/badges/title/time/link | S; U | Watched/size/focus display differs B37–B39. Full timezone/day-boundary combinations untested. |
| NotificationPreferencesPopover | NotificationsPage Flyout | Master/Favorites/Watchlist/Continue Watching/Next Up toggles, pending/error and dependent disablement | S; U | All rapid-toggle/error/rollback behavior and popover placement unrendered. |
| ConfirmDialog | Native ContentDialogs for history/group/collection deletion | Cancel/confirm/text/dispatch | S dispatch; U | Every conditional message/disabled/pending path and default-focus interior only partly inspected. |
| PageUnavailable/Skeleton/empty states | Per-page TextBlocks/panels/retry/skeleton builders | Loading/no-items/not-found/transient-error navigation/recovery | S; U | B17/B41 identify recovery gaps; decorative dimensions only sampled outside explicitly cited findings. |

## Findings

Each finding states the official contract, the native source behavior, exact evidence and the missing interaction/rendered test. Differences proved by source are separate from runtime consequences that have not been observed.

### B01 — P2: Home hero and skeleton still use the previous size contract

**Expected:** Home hero is 54vh, min 380px/max 760px, rising to 66vh at lg; its skeleton and error slot use the same contract.

**Actual/source implication:** HeroCarousel computes 50vh/60vh, min 350px/max 700px. HomePage repeats these old dimensions for its slot. Tall Library hero ratios are current. Native also derives dimensions from XamlRoot.Content, so viewport correspondence requires rendered measurement.

**Evidence:** [Web web/src/lib/design-system.ts:591–601](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/lib/design-system.ts#L591-L601); [Native src/SiloPlayer/Controls/HeroCarousel.xaml.cs:120–147](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/HeroCarousel.xaml.cs:120); [Native src/SiloPlayer/Views/HomePage.xaml.cs:126–134](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/HomePage.xaml.cs:126)

**Missing acceptance test:** At 600×800, 1024×900 and 1440×1080 compare loaded, loading, failed and empty hero height; verify sidebar/window chrome do not change the intended content breakpoint.

### B02 — P3: Library mobile hero counter lacks header clearance

**Expected:** With reserveHeaderSpace, the small-screen counter is positioned top-24 (96px), clearing the overlaid Library header.

**Actual/source implication:** HeroCarousel always moves SlideControlsPanel to a 16px top inset below 640px, including IsTall. The source proves the inset difference; whether it overlaps a particular native header needs a screenshot.

**Evidence:** [Web web/src/components/HeroBanner.tsx:380–392](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/HeroBanner.tsx#L380-L392); [Native src/SiloPlayer/Controls/HeroCarousel.xaml.cs:164–168](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/HeroCarousel.xaml.cs:164)

**Missing acceptance test:** At 480px and 639px widths open Library Recommended with several slides; compare counter/header spacing, then scroll through the transparent-to-glass transition.

### B03 — P2: Audiobook Now Listening hero is a fixed desktop composition

**Expected:** Resume deck stacks below sm, has 144/192/224px square cover sizes, adaptive gutters, cover blur/brightness/saturation and ambient/scrim layers.

**Actual/source implication:** NowListeningHero is always 560px tall with a 220px first column, 40px column gap and 48/104/48/48 margin. Its background is the same unblurred image at opacity .28 plus fixed color gradients. It has no SizeChanged layout or alternate stacked template.

**Evidence:** [Web web/src/components/NowListeningHero.tsx:123–183](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/NowListeningHero.tsx#L123-L183); [Native src/SiloPlayer/Controls/NowListeningHero.xaml:6–34](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/NowListeningHero.xaml:6); [Native src/SiloPlayer/Controls/NowListeningHero.xaml.cs:15–18](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/NowListeningHero.xaml.cs:15)

**Missing acceptance test:** Compare 480/640/1024/1440 widths with long title, no artwork and no credits; check clipping and cover/text placement, brightness and background treatment.

### B04 — P2: Now Listening omits detail-derived chapters and live Pause state

**Expected:** Deck fetches matching audiobook detail, resolves author/narrator/files/chapters, uses live active-player position, and switches Resume/Listen to Pause for the currently playing book.

**Actual/source implication:** Bind reads only the section MediaItem and renders elapsed-of-duration. There is no detail query, chapter construction or player subscription; Resume_Click always calls PlayerService.PlayAsync. Rest-of-section row is implemented and is not missing.

**Evidence:** [Web web/src/components/NowListeningHero.tsx:44–121](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/NowListeningHero.tsx#L44-L121); [Web web/src/components/NowListeningHero.tsx:194–225](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/NowListeningHero.tsx#L194-L225); [Native src/SiloPlayer/Controls/NowListeningHero.xaml.cs:20–37](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/NowListeningHero.xaml.cs:20); [Native src/SiloPlayer/Controls/NowListeningHero.xaml.cs:67–77](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/NowListeningHero.xaml.cs:67); [Native src/SiloPlayer/Views/LibraryPage.xaml.cs:2987–3007](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/LibraryPage.xaml.cs:2987)

**Missing acceptance test:** Use section payload lacking credits/chapters; start that book, return to Library Home, seek and pause/resume. Confirm matching-detail race behavior when the deck changes.

### B05 — P1: Remember Library State setting is disconnected from Library page state

**Expected:** Persist canonical tab, browse axis and query under profile owner + library, only when the remember preference permits; hydrate across launches/devices and isolate profile changes.

**Actual/source implication:** Library stores a static Dictionary<int,LibraryViewState> keyed only by library. It is in-memory, not preference-gated, and its state model omits advanced rule groups. Settings exposes library.page_state, but Library does not consume that preference. Profile sharing of the static cache is a source-level isolation risk; no profile-switch runtime test was performed.

**Evidence:** [Web web/src/pages/LibraryPage.tsx:44–96](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/LibraryPage.tsx#L44-L96); [Web web/src/pages/LibraryPage.tsx:223–245](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/LibraryPage.tsx#L223-L245); [Native src/SiloPlayer/Views/LibraryPage.xaml.cs:71–119](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/LibraryPage.xaml.cs:71); [Native src/SiloPlayer/Views/SettingsPage.xaml.cs:474–502](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/SettingsPage.xaml.cs:474)

**Missing acceptance test:** Toggle remember off/on; change tab, axis and grouped rules; leave/return, restart and switch profiles. Verify server-saved state, explicit navigation state and profile reset precedence.

### B06 — P1: Audiobook axis/search changes can drop their replacement load

**Expected:** Authors/Narrators/Series views are keyed to the chosen axis and query so superseded results cannot populate the new view.

**Actual/source implication:** LoadAudiobookGroupsAsync returns immediately when _isLoadingAudiobookGroups is true, before its reset cancellation. A reset triggered while a request is in flight is discarded. Completion then renders with mutable _currentAudiobookAxis rather than the request's captured axis. This is a source-confirmed race path; observed stale data has not been reproduced.

**Evidence:** [Web web/src/pages/LibraryBrowse.tsx:186–211](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/LibraryBrowse.tsx#L186-L211); [Native src/SiloPlayer/Views/LibraryPage.xaml.cs:2034–2087](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/LibraryPage.xaml.cs:2034)

**Missing acceptance test:** Delay authors request; switch to series and change search/sort before completion. Assert latest request runs and only its results/count/noun render; repeat after navigating away.

### B07 — P2: Library and Catalog guided people/book fields lack the Web selection controls

**Expected:** Actor/director/writer/producer use PersonSearchSelect; scoped author/narrator/series use searched facets (or searchable options when no catalog state).

**Actual/source implication:** Library's people and book fields are raw TextBoxes. Catalog people are TextBoxes and book fields are static ComboBoxes. They omit the searched identity/option selection interaction and the exact selected-value presentation. This finding concerns control behavior; whether free text happens to resolve server-side is unverified.

**Evidence:** [Web web/src/components/collections/CollectionGuidedRulesEditor.tsx:644–679](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/collections/CollectionGuidedRulesEditor.tsx#L644-L679); [Web web/src/components/collections/CollectionGuidedRulesEditor.tsx:714–769](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/collections/CollectionGuidedRulesEditor.tsx#L714-L769); [Native src/SiloPlayer/Views/LibraryPage.xaml:648–706](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/LibraryPage.xaml:648); [Native src/SiloPlayer/Views/CatalogPage.xaml:208–246](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CatalogPage.xaml:208)

**Missing acceptance test:** Search ambiguous person names and large author/narrator/series facets; select, clear, keyboard navigate and verify transmitted values and restored labels.

### B08 — P3: Library collection artwork fallback differs

**Expected:** Missing artwork shows centered wrapped collection title, with User icon only for user collections; user metadata caption is 12px.

**Actual/source implication:** CreateCollectionCard shows a folder glyph instead of the title inside missing artwork. User metadata caption is 11px. Count and library pin actions exist.

**Evidence:** [Web web/src/components/collections/CollectionPosterCard.tsx:50–65](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/collections/CollectionPosterCard.tsx#L50-L65); [Native src/SiloPlayer/Views/LibraryPage.xaml.cs:3531–3605](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/LibraryPage.xaml.cs:3531)

**Missing acceptance test:** Compare regular/user collections with no poster, failed poster and long title under artwork-only/title/title+metadata preferences; verify pin visibility and click target.

### B09 — P3: Grid gaps do not follow the poster-size contract

**Expected:** Compact/standard grids use 12px gap in both directions; large uses 16px in both. Grid columns also vary with size and viewport.

**Actual/source implication:** Catalog and Person use fixed 12px horizontal/16px vertical gaps. Recommendation section uses fixed 16px/24px. Library collection WrapPanel uses 12px/16px. Native card widths do change, but gaps are not selected from the same size contract.

**Evidence:** [Web web/src/lib/uiCustomization.ts:258–266](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/lib/uiCustomization.ts#L258-L266); [Native src/SiloPlayer/Views/CatalogPage.xaml:139–140](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CatalogPage.xaml:139); [Native src/SiloPlayer/Views/PersonDetailPage.xaml:258–263](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/PersonDetailPage.xaml:258); [Native src/SiloPlayer/Views/RecommendationSectionPage.xaml:101–103](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/RecommendationSectionPage.xaml:101); [Native src/SiloPlayer/Views/LibraryPage.xaml.cs:3523](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/LibraryPage.xaml.cs:3523)

**Missing acceptance test:** For every affected grid select compact, standard and large at each breakpoint; measure horizontal/vertical gaps, column counts, card widths and caption height.

### B10 — P1: Current Watchlist sidebar route loses external-title watchlist

**Expected:** Catalog Watchlist exposes In your library / Not in your library yet, external-title count/attention, retry and request actions.

**Actual/source implication:** MainWindow navigates Watchlist to CatalogPage. That page has no external-title tabs or WatchlistTitlesTab equivalent. A separate legacy WatchlistPage implements external titles but is not the sidebar target; its presence is not usable coverage for this route.

**Evidence:** [Web web/src/pages/Catalog.tsx:418–437](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Catalog.tsx#L418-L437); [Native src/SiloPlayer/MainWindow.xaml.cs:3084–3094](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/MainWindow.xaml.cs:3084); [Native src/SiloPlayer/Views/CatalogPage.xaml:18–82](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CatalogPage.xaml:18); [Native src/SiloPlayer/Views/WatchlistPage.xaml:1–35](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/WatchlistPage.xaml:1)

**Missing acceptance test:** Enter Watchlist through sidebar and a pinned/deep navigation with both local and TMDB-only titles. Verify tabs, amber attention, count, request-disabled state, remove and request-detail navigation.

### B11 — P1: Catalog advanced editor cannot represent Web rule groups or ranges

**Expected:** Advanced uses CollectionRulesEditor with Rule Groups, per-group rules/match, overall match, field-aware operators/values and available book/language fields.

**Actual/source implication:** Catalog uses one _advancedRules list with one All/Any control. Its fixed fields omit author/narrator/series/original_language/audio_language and its operators omit between. Existing complex query groups cannot be faithfully edited through this surface.

**Evidence:** [Web web/src/components/catalog/CatalogFilterSheet.tsx:119–154](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/catalog/CatalogFilterSheet.tsx#L119-L154); [Web web/src/components/collections/CollectionRulesEditor.tsx:108–127](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/collections/CollectionRulesEditor.tsx#L108-L127); [Web web/src/components/FilterRuleEditor.tsx:274–336](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/FilterRuleEditor.tsx#L274-L336); [Native src/SiloPlayer/Views/CatalogPage.xaml:254–265](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CatalogPage.xaml:254); [Native src/SiloPlayer/Views/CatalogPage.xaml.cs:62–78](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CatalogPage.xaml.cs:62)

**Missing acceptance test:** Build (genre A OR genre B) AND a year range; combine author/language rules, switch guided/advanced, reopen and compare request JSON/results with Web.

### B12 — P2: Catalog guided editor reduces multi-selection and input choices

**Expected:** Genres and original languages are searchable multi-selects, decade has an explicit selector, and rating uses the shared editor's controlled value model.

**Actual/source implication:** Catalog exposes one Genre ComboBox, one original-language ComboBox and year text boxes without the decade shortcut; minimum IMDb rating is limited to integer 5–9 dropdown presets. Several filters have no equivalent searched multi-selection UI.

**Evidence:** [Web web/src/components/collections/CollectionGuidedRulesEditor.tsx:544–571](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/collections/CollectionGuidedRulesEditor.tsx#L544-L571); [Web web/src/components/collections/CollectionGuidedRulesEditor.tsx:629–639](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/collections/CollectionGuidedRulesEditor.tsx#L629-L639); [Native src/SiloPlayer/Views/CatalogPage.xaml:188–207](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CatalogPage.xaml:188)

**Missing acceptance test:** Apply two genres and two original languages, select a decade then custom years and a non-preset rating; verify intersection semantics, clear behavior and round-trip editing.

### B13 — P2: Catalog omits individual active refinement chips

**Expected:** CatalogFiltersPanel renders ActiveFilterBadges outside the filter sheet with direct removal controls.

**Actual/source implication:** Catalog only updates the filter count on the Filters button; its XAML has no equivalent badge row. Library and CollectionBrowse already have badge panels and should not be treated as missing.

**Evidence:** [Web web/src/components/catalog/CatalogFiltersPanel.tsx:125–138](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/catalog/CatalogFiltersPanel.tsx#L125-L138); [Native src/SiloPlayer/Views/CatalogPage.xaml:34–82](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CatalogPage.xaml:34)

**Missing acceptance test:** Apply mixed refinements on Favorites, History and generic Catalog; compare readable chips, removing one without reopening the sheet, and clear-all state.

### B14 — P1: Collection browse uses a reduced separate filter sheet

**Expected:** Collection catalog shares the guided/advanced panel, contextual sort and refinements used by Catalog, with source-specific capability restrictions.

**Actual/source implication:** CollectionBrowsePage has a bespoke guided form with comma-delimited genres, years/rating/language/studio/country and 4K/HDR/DV. It has no guided/advanced switch, grouped rule editor, people/network/watch/date/book refinements. The separate back/pin header also differs structurally from the unified Web Catalog.

**Evidence:** [Web web/src/pages/Catalog.tsx:455–480](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Catalog.tsx#L455-L480); [Web web/src/components/catalog/CatalogFilterSheet.tsx:119–155](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/catalog/CatalogFilterSheet.tsx#L119-L155); [Native src/SiloPlayer/Views/CollectionBrowsePage.xaml:27–110](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionBrowsePage.xaml:27); [Native src/SiloPlayer/Views/CollectionBrowsePage.xaml:270–332](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionBrowsePage.xaml:270)

**Missing acceptance test:** Open user and library collections from Collections, Library and pins; attempt the same grouped/personalized refinements, preserve Collection Order and confirm correct source/capability gating.

### B15 — P2: Full Search has four guided filters and one-way Advanced switching

**Expected:** Full search is Catalog query state and uses the same Guided/Advanced toggle and full contextual shared editor.

**Actual/source implication:** SearchPage guided controls are Genre, Content Rating, Resolution and Country only. AdvancedSearch_Click hides both guided controls and its trigger; no Guided return control exists within the sheet. This prevents the same editing path and omits shared guided facets.

**Evidence:** [Web web/src/pages/Catalog.tsx:455–480](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Catalog.tsx#L455-L480); [Web web/src/components/catalog/CatalogFilterSheet.tsx:119–138](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/catalog/CatalogFilterSheet.tsx#L119-L138); [Native src/SiloPlayer/Views/SearchPage.xaml:520–548](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/SearchPage.xaml:520); [Native src/SiloPlayer/Views/SearchPage.xaml.cs:714–728](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/SearchPage.xaml.cs:714)

**Missing acceptance test:** From full Search choose multiple libraries, dates, watch-state and people; switch to advanced and back without losing semantic query state; close/reopen and repeat.

### B16 — P2: Shared native advanced rows expose raw fields and comma range input

**Expected:** Web rules use friendly field/operator labels, paired From/To inputs for between, controlled select values and PersonSearchSelect for people.

**Actual/source implication:** QueryRulesEditor presents raw identifiers (rating_imdb/in_watchlist/etc.), raw operator codes and one TextBox with Values separated by commas for ranges; non-boolean selected/person fields use generic text input. It validates parsing, but its interaction and appearance do not match.

**Evidence:** [Web web/src/components/FilterRuleEditor.tsx:265–340](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/FilterRuleEditor.tsx#L265-L340); [Native src/SiloPlayer/Controls/QueryRulesEditor.cs:13–17](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/QueryRulesEditor.cs:13); [Native src/SiloPlayer/Controls/QueryRulesEditor.cs:66–91](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/QueryRulesEditor.cs:66)

**Missing acceptance test:** In Search and both smart collection editors use numeric/date ranges, person selection, content-rating/type select and invalid values; verify labels, focus order, validation and serialized value types.

### B17 — P2: Several browse error surfaces lose explicit recovery states

**Expected:** Catalog/Search have descriptive alert panels and Retry catalog/search/people; Person and collection editors distinguish unavailable/not-found from transient errors with Retry.

**Actual/source implication:** Catalog and Search primary failures are inline text, without the matching Retry panel. SearchPeopleAsync swallows failure to an empty list. Person shows error text without Retry; collection not-found state has no All collections link, while other editor failures can leave the form visible without the Web unavailable/retry shell. Home/Library section retries are implemented.

**Evidence:** [Web web/src/pages/Catalog.tsx:541–587](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Catalog.tsx#L541-L587); [Web web/src/pages/PersonDetail.tsx:112–127](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/PersonDetail.tsx#L112-L127); [Web web/src/pages/CollectionEditor.tsx:53–75](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/CollectionEditor.tsx#L53-L75); [Native src/SiloPlayer/Views/CatalogPage.xaml:111–116](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CatalogPage.xaml:111); [Native src/SiloPlayer/Views/SearchPage.xaml:438–445](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/SearchPage.xaml:438); [Native src/SiloPlayer/ViewModels/SearchViewModel.cs:455–465](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/SearchViewModel.cs:455); [Native src/SiloPlayer/Views/PersonDetailPage.xaml:92–99](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/PersonDetailPage.xaml:92); [Native src/SiloPlayer/Views/CollectionEditorPage.xaml:68–81](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionEditorPage.xaml:68); [Native src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:69–81](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:69)

**Missing acceptance test:** Independently fail catalog, people, filmography and editor metadata requests with 404/403/5xx; keep unrelated results, retry only failed query and verify alert focus and available navigation.

### B18 — P2: Quick Search geometry is not the Web dialog contract

**Expected:** Dialog is top 20%, no vertical translation, max height min(512px, viewport−96px), max width 512px; result pane max min(352px,55vh).

**Actual/source implication:** GlobalSearchDialog is a normal ContentDialog with fixed 520px content width and 420px result max-height. There is no adaptive top-position or viewport-height contract. WinUI outer dialog padding/template adds another measurement uncertainty.

**Evidence:** [Web web/src/components/GlobalSearch.tsx:573–575](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/GlobalSearch.tsx#L573-L575); [Web web/src/components/GlobalSearch.tsx:636–639](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/GlobalSearch.tsx#L636-L639); [Native src/SiloPlayer/Controls/GlobalSearchDialog.xaml:2–14](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/GlobalSearchDialog.xaml:2); [Native src/SiloPlayer/Controls/GlobalSearchDialog.xaml:54–62](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/GlobalSearchDialog.xaml:54)

**Missing acceptance test:** Open quick search at small height/width, portrait and desktop with many results; measure location, outer width, inner padding and clipping; test IME, arrows, Enter and Escape.

### B19 — P2: Quick Search results have no direct-play overlay

**Expected:** Playable movie/episode results show CardPlayOverlay on the thumbnail, allowing playback without first opening detail.

**Actual/source implication:** BuildResultRow creates a single detail-navigation Button with image/text; no playback child overlay/action is created.

**Evidence:** [Web web/src/components/GlobalSearch.tsx:188–200](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/GlobalSearch.tsx#L188-L200); [Native src/SiloPlayer/Controls/GlobalSearchDialog.xaml.cs:363–446](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/GlobalSearchDialog.xaml.cs:363)

**Missing acceptance test:** Hover/focus movie and episode thumbnails, activate quick play and verify dialog closes/starts correct play_content_id; confirm a regular row activation still navigates detail.

### B20 — P3: Quick Search people reuse rectangular media artwork

**Expected:** Person results have a 40×40 circular photo or initials fallback.

**Actual/source implication:** People are passed through BuildResultRow's generic 40×56 image/fallback and media text layout.

**Evidence:** [Web web/src/components/GlobalSearch.tsx:256–278](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/GlobalSearch.tsx#L256-L278); [Native src/SiloPlayer/Controls/GlobalSearchDialog.xaml.cs:363–405](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/GlobalSearchDialog.xaml.cs:363)

**Missing acceptance test:** Compare person photo/missing-photo rows with media and request rows; ensure initial fallback, circle clipping and keyboard active-row highlight.

### B21 — P2: Template gallery fixed children can exceed its compact dialog

**Expected:** Gallery width adapts to available viewport (sm max768/lg max896) with responsive content.

**Actual/source implication:** Dialog computes a compact width from ActualWidth−48, but one-column template content width remains 780px and configuration stack remains 810px. Fixed children can exceed the compact dialog. Rendered overflow/clipping is pending.

**Evidence:** [Web web/src/components/CollectionTemplateGallery/CollectionTemplateGallery.tsx:115–130](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/CollectionTemplateGallery/CollectionTemplateGallery.tsx#L115-L130); [Native src/SiloPlayer/Views/CollectionsPage.xaml.cs:131–151](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionsPage.xaml.cs:131); [Native src/SiloPlayer/Views/CollectionsPage.xaml.cs:399–403](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionsPage.xaml.cs:399); [Native src/SiloPlayer/Views/CollectionsPage.xaml.cs:632–634](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionsPage.xaml.cs:632)

**Missing acceptance test:** At 480/640/759px open gallery, pick each source type, navigate Back and resize while open; verify no horizontal clipping and all primary/close controls reachable.

### B22 — P3: Template cards use a different visual hierarchy and omit sync summary

**Expected:** Cards have small radius, padding16, a 40px tinted icon tile, title14, source/profile badges and media-kind plus sync schedule summary.

**Actual/source implication:** Native cards have radius16/padding14, bare icon24 and title15 semibold; footer shows first three tags or raw media kind and omits the Web schedule summary/profile badge structure.

**Evidence:** [Web web/src/components/CollectionTemplateGallery/CollectionTemplateCard.tsx:23–65](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/CollectionTemplateGallery/CollectionTemplateCard.tsx#L23-L65); [Native src/SiloPlayer/Views/CollectionsPage.xaml.cs:373–440](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionsPage.xaml.cs:373)

**Missing acceptance test:** Compare templates with requires_profile, schedules, long descriptions, no tags and all source kinds; measure tile/icon/title/footer and hover/focus states.

### B23 — P2: Template poster default has no image preview

**Expected:** Poster choice is a two-tile radiogroup and Server default shows the actual 56×80 image plus path.

**Actual/source implication:** Native uses a ComboBox for Server default/Custom URL and a folder glyph/path for Server default. The default image cannot be reviewed before creating the collection.

**Evidence:** [Web web/src/components/CollectionTemplateGallery/TemplatePosterField.tsx:30–59](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/CollectionTemplateGallery/TemplatePosterField.tsx#L30-L59); [Native src/SiloPlayer/Views/CollectionsPage.xaml.cs:588–630](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionsPage.xaml.cs:588)

**Missing acceptance test:** Select templates with/without a default poster, swap custom/default, inspect broken images and verify created poster source and keyboard selection.

### B24 — P2: MDBList browsing waits for a Search click

**Expected:** Typing searches automatically after a 300ms debounce, while Top lists is a distinct mode; not-configured/error/loading states keep URL import possible.

**Actual/source implication:** Native builds an explicit Search button handler. Its search box has no matching debounced TextChanged search. Top lists and source configuration checks exist.

**Evidence:** [Web web/src/components/CollectionTemplateGallery/MDBListBrowser.tsx:14–41](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/CollectionTemplateGallery/MDBListBrowser.tsx#L14-L41); [Native src/SiloPlayer/Views/CollectionsPage.xaml.cs:853–905](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionsPage.xaml.cs:853)

**Missing acceptance test:** Type/paste/clear a query without pressing Search, switch Top lists and choose a result; verify stale cancellation, configured=false, retry and URL fallback.

### B25 — P2: New collection defaults to Manual instead of Smart

**Expected:** New user collection builder defaults to smart.

**Actual/source implication:** CollectionEditorViewModel initializes _collectionType to manual and the new route loads reference data without overriding it.

**Evidence:** [Web web/src/pages/userCollectionsShared.tsx:40–45](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/userCollectionsShared.tsx#L40-L45); [Native src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs:46–47](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs:46); [Native src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:101–106](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:101)

**Missing acceptance test:** Create from Collections New and navigation shortcut; compare initial mode, preview/filter affordances and the type of the first saved draft.

### B26 — P2: Manual collection display-filter controls are hidden

**Expected:** Manual user collections include DisplayFilterControls and serialize display_query_definition.

**Actual/source implication:** Native Watch state/Content/default-sort controls are inside ImportedDisplayOptions and shown only when IsImportedCollection. Manual collections therefore lack the corresponding display filter UI.

**Evidence:** [Web web/src/components/collections/CollectionBuilder.tsx:263–265](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/collections/CollectionBuilder.tsx#L263-L265); [Web web/src/pages/userCollectionsShared.tsx:57–69](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/userCollectionsShared.tsx#L57-L69); [Native src/SiloPlayer/Views/CollectionEditorPage.xaml:245–260](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionEditorPage.xaml:245); [Native src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:176–186](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:176)

**Missing acceptance test:** Create/edit a manual collection, set watched/unwatched and movie/series display filters, save/reopen and compare visible items and display_query_definition.

### B27 — P2: Manual item mutations persist only on main Save

**Expected:** Web Add/Remove and successful reorder are immediate collection-item mutations with their own pending/error behavior.

**Actual/source implication:** Native mutates ManualItems locally and performs remove/add/reorder only in SaveManualItemsAsync. Navigate-away before Save can discard apparently applied item changes; the exact loss path is runtime-unverified.

**Evidence:** [Web web/src/components/collections/ManualCollectionItemsEditor.tsx:130–137](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/collections/ManualCollectionItemsEditor.tsx#L130-L137); [Web web/src/components/collections/ManualCollectionItemsEditor.tsx:181–191](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/collections/ManualCollectionItemsEditor.tsx#L181-L191); [Native src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs:426–444](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs:426)

**Missing acceptance test:** Add/remove/reorder, then cancel/navigate/back/restart without main Save; compare persistence and explicit pending/failure feedback. Test partial mutation failure without losing order.

### B28 — P2: Manual reorder UI does not use the Web capability/snapshot conditions

**Expected:** Reorder requires item_reorder capability, same complete order snapshot, no remaining page and a matching etag.

**Actual/source implication:** Native fetches capabilities but does not retain/use that response for manual drag gating. Grip CanDrag depends only on IsReadOnly; Save unconditionally requests reorder for nonempty items. Source contract checks are not equivalent.

**Evidence:** [Web web/src/components/collections/ManualCollectionItemsEditor.tsx:161–191](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/collections/ManualCollectionItemsEditor.tsx#L161-L191); [Native src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs:178–184](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs:178); [Native src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:854–859](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:854); [Native src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs:440–441](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs:440)

**Missing acceptance test:** Disable item_reorder, use paged manual collection and concurrent order updates. Verify disabled handles, no unsupported write, complete snapshot/etag conflict handling and recovery.

### B29 — P2: Collection editor section label columns never stack

**Expected:** Imported section labels become a 14rem/content grid only at lg; main sidebar splits at xl. Below those breakpoints sections are stacked.

**Actual/source implication:** BasicInfoSection has a permanent 220px label column plus 36px gap and 28px side padding. Resize logic moves only the outer sidebar/banner. At narrow widths, the fixed inner column consumes most of the editable field area.

**Evidence:** [Web web/src/pages/ImportedCollectionEditor.tsx:316–318](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ImportedCollectionEditor.tsx#L316-L318); [Web web/src/pages/ImportedCollectionEditor.tsx:780–790](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ImportedCollectionEditor.tsx#L780-L790); [Native src/SiloPlayer/Views/CollectionEditorPage.xaml:194–208](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionEditorPage.xaml:194); [Native src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:47–62](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionEditorPage.xaml.cs:47)

**Missing acceptance test:** Compare 480/640/900/1024/1100/1280 widths for manual/imported forms, all sections and errors; confirm label/field wrapping and available input width.

### B30 — P2: Imported editor action bar is in the document rather than a dirty-state dock

**Expected:** Imported dirty state reveals a fixed bottom20 centered pill, unsaved-change count and Cancel/Discard/Save, disabling appropriately.

**Actual/source implication:** Native shows action buttons at the end of the ScrollViewer, with no equivalent dirty-count/docked visibility. Buttons can be offscreen when editing earlier sections; unchanged forms still show Save/Discard.

**Evidence:** [Web web/src/pages/ImportedCollectionEditor.tsx:1019–1062](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ImportedCollectionEditor.tsx#L1019-L1062); [Native src/SiloPlayer/Views/CollectionEditorPage.xaml:752–782](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CollectionEditorPage.xaml:752)

**Missing acceptance test:** Edit top/middle fields at short height, observe unsaved count/dock; discard resets all fields/artwork and hides bar; save failure preserves edits and prevents duplicate action.

### B31 — P2: Smart wizard Filter step lacks the shared guided filter panel

**Expected:** Step1 uses CatalogFiltersPanel with guided/advanced modes, libraries/media scope, personalized refinements, contextual sorts/count.

**Actual/source implication:** Native Filter step is an inline controls/rules panel using QueryRulesEditor; there is no equivalent Guided editor or shared filter-sheet interaction. Libraries/sort/limit and rule groups exist, so this is interaction parity rather than total query absence.

**Evidence:** [Web web/src/pages/SmartCollectionWizard.tsx:331–345](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/SmartCollectionWizard.tsx#L331-L345); [Native src/SiloPlayer/Views/SmartCollectionWizardPage.xaml:191–250](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/SmartCollectionWizardPage.xaml:191); [Native src/SiloPlayer/Controls/QueryRulesEditor.cs:13–17](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/QueryRulesEditor.cs:13)

**Missing acceptance test:** Build the same query via guided facets in Web and native, switch advanced, clear one badge, change personalized sort and verify identical preview/query serialization.

### B32 — P1: Smart wizard preview cannot browse beyond its first 100 results

**Expected:** Preview uses visible-range catalog windows, requests later pages when scrolling, and shows a count for all matches within collection limit.

**Actual/source implication:** PreviewAsync always requests offset0 and at most100 items, then copies only that response. Page code builds these items without a subsequent-page loader. PreviewTotal can display up to the 500 collection limit while only first100 are inspectable.

**Evidence:** [Web web/src/pages/SmartCollectionWizard.tsx:277–319](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/SmartCollectionWizard.tsx#L277-L319); [Native src/SiloPlayer/ViewModels/SmartCollectionWizardViewModel.cs:198–220](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/SmartCollectionWizardViewModel.cs:198); [Native src/SiloPlayer/Views/SmartCollectionWizardPage.xaml.cs:431–480](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/SmartCollectionWizardPage.xaml.cs:431)

**Missing acceptance test:** Preview a query matching 400 titles with limit300; scroll to 101/200/300, compare order/count and query-change cancellation; inspect an item beyond first page.

### B33 — P2: Smart wizard poster field omits current-artwork preview/delete

**Expected:** When artwork capability permits, ImageUploadField displays currentUrl and exposes deletion for existing poster.

**Actual/source implication:** PosterDetailsPanel shows a 128px upload placeholder, file status and source URL, with no current poster image or Delete action. Upload/source URL work paths exist.

**Evidence:** [Web web/src/pages/SmartCollectionWizard.tsx:473–487](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/SmartCollectionWizard.tsx#L473-L487); [Native src/SiloPlayer/Views/SmartCollectionWizardPage.xaml:171–185](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/SmartCollectionWizardPage.xaml:171)

**Missing acceptance test:** Edit existing smart collection with poster, delete it, restore/custom-upload/drop/URL, cancel and save; verify capability-disabled/read-only views and failure feedback.

### B34 — P2: Person header stays horizontal below the Web lg breakpoint

**Expected:** Photo/info are a vertical stack below1024 and a row at lg; photo width140 below640/180 above.

**Actual/source implication:** Native always uses a two-column Grid. ApplyResponsiveLayout resizes photo/title/gutters, but never changes those columns or moves info below photo.

**Evidence:** [Web web/src/pages/PersonDetail.tsx:134–139](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/PersonDetail.tsx#L134-L139); [Native src/SiloPlayer/Views/PersonDetailPage.xaml:122–135](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/PersonDetailPage.xaml:122); [Native src/SiloPlayer/Views/PersonDetailPage.xaml.cs:183–201](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/PersonDetailPage.xaml.cs:183)

**Missing acceptance test:** Compare no-photo/long-name/long-bio at480/640/1023/1024; measure photo, name, metadata and filmography start.

### B35 — P2: Person biography is truncated behind an extra Show more action

**Expected:** Web renders full bio with max-width672 and relaxed14px text.

**Actual/source implication:** Native applies MaxLines8 and a Show more/less control. It changes initial page height/content visibility and adds a button absent in Web.

**Evidence:** [Web web/src/pages/PersonDetail.tsx:210–214](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/PersonDetail.tsx#L210-L214); [Native src/SiloPlayer/Views/PersonDetailPage.xaml:180–198](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/PersonDetailPage.xaml:180); [Native src/SiloPlayer/Views/PersonDetailPage.xaml.cs:143–145](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/PersonDetailPage.xaml.cs:143)

**Missing acceptance test:** Use a biography longer than8 lines, resize and return from an item; compare complete text, initial filmography position and whether expansion survives navigation.

### B36 — P2: Person page lacks its signed-in metadata refresh button

**Expected:** All signed-in users can request metadata refresh; admin wording becomes Refresh now and an admin-only Edit metadata opens EditPersonDialog.

**Actual/source implication:** Person native header has no refresh/edit button. It observes refresh events but offers no corresponding user action on this page. Conditional admin-only controls on a non-admin route are noted separately from admin-page scope.

**Evidence:** [Web web/src/pages/PersonDetail.tsx:158–187](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/PersonDetail.tsx#L158-L187); [Native src/SiloPlayer/Views/PersonDetailPage.xaml:150–198](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/PersonDetailPage.xaml:150)

**Missing acceptance test:** Use ordinary user and curator/admin profiles; queue refresh, observe Queueing/pending/success/error; verify button enablement and edit dialog only where authorized.

### B37 — P2: Calendar cards omit watched appearance

**Expected:** Watched artwork is opacity60, grayscale, with centered32px check badge.

**Actual/source implication:** BuildEventCard never applies a watched state; it builds poster, gradient, badges and caption uniformly for all events.

**Evidence:** [Web web/src/components/calendar/CalendarEventCard.tsx:32–77](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/calendar/CalendarEventCard.tsx#L32-L77); [Native src/SiloPlayer/Views/CalendarPage.xaml.cs:508–575](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CalendarPage.xaml.cs:508)

**Missing acceptance test:** Compare watched/unwatched aired/upcoming episodes, change watched state elsewhere then return/realtime-refresh; measure opacity/check placement and verify target navigation.

### B38 — P2: Calendar ignores compact/large poster-size preferences

**Expected:** DayGroup takes carousel widths from poster_size: compact120/140/160, standard140/160/185, large170/195/220.

**Actual/source implication:** Calendar fixes event-card width to140/160/185 based on window width, regardless of preference. Skeleton follows same fixed width.

**Evidence:** [Web web/src/components/calendar/DayGroup.tsx:16–17](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/calendar/DayGroup.tsx#L16-L17); [Web web/src/lib/uiCustomization.ts:269–276](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/lib/uiCustomization.ts#L269-L276); [Native src/SiloPlayer/Views/CalendarPage.xaml.cs:890–905](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CalendarPage.xaml.cs:890)

**Missing acceptance test:** Change all three poster sizes while Calendar is open and after revisiting, at mobile/sm/lg; compare cards/skeletons, horizontal paging and captions.

### B39 — P3: Calendar day groups omit Focused indicator and use smaller headings

**Expected:** DayGroup uses MediaCarousel normal heading20 plus Today and Focused pills, marking selected day.

**Actual/source implication:** Native heading is14 semibold and creates only Today. BuildDayGroup has no selected/focused pill despite date selection in navigator.

**Evidence:** [Web web/src/components/calendar/DayGroup.tsx:27–40](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/calendar/DayGroup.tsx#L27-L40); [Web web/src/components/MediaCarousel.tsx:50–63](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MediaCarousel.tsx#L50-L63); [Native src/SiloPlayer/Views/CalendarPage.xaml.cs:438–477](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/CalendarPage.xaml.cs:438)

**Missing acceptance test:** Select non-today day, jump to it, switch week and resize; compare focused/today distinctions, header text size and sticky scroll offset.

### B40 — P1: Approved/declined request notifications do not open their title

**Expected:** Notifications without catalog IDs can navigate approved/declined requests using reason_flags media_type and tmdb_id.

**Actual/source implication:** Native click handler only uses EpisodeId/SeriesId. Such request rows can be marked read but do not navigate to the request-title page.

**Evidence:** [Web web/src/pages/Notifications.tsx:97–110](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Notifications.tsx#L97-L110); [Native src/SiloPlayer/Views/NotificationsPage.xaml.cs:72–78](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/NotificationsPage.xaml.cs:72)

**Missing acceptance test:** Click unread/read approved and declined movie/series notifications with TMDB IDs but no catalog IDs; confirm correct request target and independent mark-read behavior.

### B41 — P2: Notification failures lose retained error/reload recovery

**Expected:** Mark-all failure remains an alert requiring Reload inbox before retry; list failure exposes Reload notifications.

**Actual/source implication:** Native shows generic ErrorText with no reload buttons. MarkAllRead catch sets ErrorMessage then calls LoadPageAsync(reset:true), which clears ErrorMessage immediately; a successful refresh can erase the mutation failure.

**Evidence:** [Web web/src/pages/Notifications.tsx:395–419](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Notifications.tsx#L395-L419); [Native src/SiloPlayer/ViewModels/NotificationsViewModel.cs:221–235](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/NotificationsViewModel.cs:221); [Native src/SiloPlayer/ViewModels/NotificationsViewModel.cs:77–82](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/NotificationsViewModel.cs:77); [Native src/SiloPlayer/Views/NotificationsPage.xaml.cs:148–159](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/NotificationsPage.xaml.cs:148)

**Missing acceptance test:** Fail mark-all but permit refresh, then fail list load; error must remain reviewable and explicit reload reset state. Verify inline mark-read failure and unread count reconciliation.

### B42 — P2: Mark all read button is not bound to pending/error/cutoff eligibility

**Expected:** Button is disabled while mutation pending, after error or until a read cutoff exists.

**Actual/source implication:** Native visibility tracks unread count, but XAML click button has no IsEnabled/command binding to those eligibility states and UpdateVisuals does not set one. Handler calls command directly. Source shows missing UI disablement; duplicate network behavior depends on generated command guards and is unverified.

**Evidence:** [Web web/src/pages/Notifications.tsx:367–376](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Notifications.tsx#L367-L376); [Native src/SiloPlayer/Views/NotificationsPage.xaml:27–29](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/NotificationsPage.xaml:27); [Native src/SiloPlayer/Views/NotificationsPage.xaml.cs:59–63](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/NotificationsPage.xaml.cs:59); [Native src/SiloPlayer/Views/NotificationsPage.xaml.cs:148–159](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/NotificationsPage.xaml.cs:148)

**Missing acceptance test:** Delay mutation, render positive unread count before first cutoff, fail mutation and activate repeatedly with keyboard/pointer; verify disabled affordance and no unsafe retry.

### B43 — P3: Notification desktop gutter and preference-popover measurements diverge

**Expected:** Notifications keeps16px horizontal padding within max768. Preference popover is320px wide with16px padding.

**Actual/source implication:** Native applies24px horizontal gutter at640+; preference Flyout contains Grid width300 padding4 plus platform template padding. Actual outer dimensions need rendered verification.

**Evidence:** [Web web/src/pages/Notifications.tsx:285–292](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Notifications.tsx#L285-L292); [Web web/src/pages/Notifications.tsx:360](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Notifications.tsx#L360); [Native src/SiloPlayer/Views/NotificationsPage.xaml.cs:205–210](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/NotificationsPage.xaml.cs:205); [Native src/SiloPlayer/Views/NotificationsPage.xaml:32–35](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/NotificationsPage.xaml:32)

**Missing acceptance test:** Compare inbox content at640/768/1024 and popover aligned to trigger at small/large widths, toggle labels, row spacing and viewport edge collision.

### B44 — P2: Shared card menu omits Edit Metadata and conditional play history

**Expected:** Curators receive Edit Metadata for movies/series along with Refresh Metadata/Match Item; admin receives View Play History on the shared card menu.

**Actual/source implication:** MediaItemMenu maintenance block adds only Refresh Metadata and Match Item. Edit Metadata and View Play History are absent. The curator action is in non-admin browse scope; admin-only menu entry is conditional coverage on these same pages.

**Evidence:** [Web web/src/components/MediaItemMenu.tsx:220–249](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MediaItemMenu.tsx#L220-L249); [Native src/SiloPlayer/Controls/MediaItemMenu.cs:150–176](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MediaItemMenu.cs:150)

**Missing acceptance test:** Open card menus as ordinary user, curator and admin across Home/Library/Search/Recommendations; compare order, separators, visible actions and dialog dispatch, including menu-close focus restoration.

### B45 — P2: Hero title is capped at two lines and lacks Web tracking

**Expected:** Hero h1 has balancing, tracking−.04em and no line clamp.

**Actual/source implication:** Both native title/shadow cap MaxLines2; DisplayTextStyle does not set character spacing. FontWeight ExtraBold and responsive font sizes are already implemented and are not missing.

**Evidence:** [Web web/src/components/HeroBanner.tsx:304–307](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/HeroBanner.tsx#L304-L307); [Native src/SiloPlayer/Controls/HeroCarousel.xaml:119–136](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/HeroCarousel.xaml:119); [Native src/SiloPlayer/Themes/DarkTheme.xaml:199–204](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Themes/DarkTheme.xaml:199)

**Missing acceptance test:** Use a title requiring3 lines at mobile and a long single-line title on desktop; compare full text, line wrapping, tracking and metadata/button displacement.

## Source-confirmed implemented coverage to preserve

The native candidate already contains substantial parity work. Source inspection found current carousel widths/gutters and continued-watching poster-versus-wide selection, Library advanced rule groups and active badges, audiobook grouping and group-to-books navigation, the Now Listening rest row, history selection/removal confirmation, list-order persistence, grouped collection create/rename/delete/move controls, template imports/source configuration checks, smart query groups and validation, notification cutoff use/preferences/errors, and recommendation empty/retry states. These must not be counted as absent merely because other portions differ.

In particular, [web/src/components/SectionRow.tsx:91–114](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/SectionRow.tsx#L91-L114) corresponds to [src/SiloPlayer/Controls/SectionRow.xaml.cs](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/SectionRow.xaml.cs:193); [web/src/lib/uiCustomization.ts:269–276](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/lib/uiCustomization.ts#L269-L276) corresponds to the widths in [src/SiloPlayer/Controls/SectionRow.xaml.cs](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/SectionRow.xaml.cs:556). Current Sidebar Favorites/Watchlist/History route to CatalogPage, so old standalone pages do not establish acceptance for those live navigation paths.

## Remaining interaction and visual acceptance matrix

Every row below is pending. Use local/reviewable fixtures or an authorized test environment; this source pass does not authorize production mutations. Both sides must use the same content, profile/permissions, preferences, viewport/content width and state.

| Family | Required visual/state captures | Required functional assertions |
|---|---|---|
| Home | Loading, hero loaded/empty/error, row loading/error/empty, long title/missing art, seed banner; all breakpoints, sidebar open/closed | Slide click/arrows/timer/reduced motion, play/resume/detail, Explore, pin, watched/favorite/watchlist, dismiss with progress timestamp and row refresh |
| Library all tabs | Movie/series/audiobook/ebook/manga library, hero/no hero, tab overlay transition, grouped/ungrouped collections; compact/standard/large and every caption mode | Tab/axis/filter/sort state restore, remember setting/profile isolation, cancellation, grouped race B06, pagination, scrollbar/jump/scroll-to-top, group-to-books refinements |
| Catalog/personal/history | Every source including section/library/user collection, source-order and custom sort, populated/empty/errors, active chips, disabled capabilities | Exact query serialization, grouped/multi-select/range editing, selection/delete confirmations and correct scope, watchlist external tabs, state mutations and navigation targets |
| Full/quick search | Empty/query/loading/results/errors, media+people+outside results, all three search scopes, quick active row/focus, small height | Debounce/cancellation, keyboard/IME, scoped request state, quick play, person/detail targets, retry failed people without discarding media, filtered pagination |
| Collections/list/import/templates | Personal/server groups, empty groups, owner/shared/read-only, dialogs/search/config/preview/default/custom artwork; resize during dialogs | Capability-gated create/edit/delete/sync/group/move, keyboard and pointer reorder with etag conflict, immediate item changes, import payload/options and partial failure recovery |
| Collection editors | Manual/smart/imported/new/missing/transient-error, all source kinds, invalid/dirty/saving/read-only; dock/field columns/artwork | Display filters, mode default, guided/advanced round-trip, preview past100, limit/order, source-managed immutable fields, save/discard/cancel, delete/upload/URL/drop poster, shared profile scope |
| Person | Missing photo, long name/bio, no filmography, loading/person-error/filmography-error; vertical/row header breakpoints | Refresh metadata user action, conditional edit action, type switching, filmography pages and card navigation, primary-header resilience to optional filmography error |
| Calendar | Preset/library compact/desktop controls, week loading/empty/error, selected/today, upcoming/aired/watched, missing poster, all poster sizes | Prev/next/today/day jump/sticky offset, profile preference restoration, timezone/day boundary, watched refresh, correct item/series targets |
| Notifications | Loading/empty/all/unread/paged/error/mutation error, preference open/disabled/saving/error, long text and no artwork | Request notification TMDB target, cutoff/new arrivals, mark-read/all pending/disable/reload, count reconciliation and preference rollback/race/profile isolation |
| Recommendations | Taste panel, all row kinds, section with/without key, empty/retry/loading, all size/caption/overlay modes | Explore/back target, section item IDs/order/caption data, favorite/watchlist/watch state mutation, retry and cancellation, row horizontal keyboard/pointer navigation |
| Shared controls | Hover/focus/pressed/disabled/selected/long-press/touch, menus/sheets/dialogs, scrollbars, thumbhash/expired/broken art, theme/font/DPI | Correct role/capability gates, no nested click propagation, focus trap/restoration, accessible names/read order, shortcuts and Escape, state refresh across all mounted surfaces |

## Acceptance checklist

- [ ] Address or explicitly accept each B01–B45 discrepancy against the pinned official source; record any newer reference commit separately.
- [ ] Re-run route/control discovery after fixes and identify any newly imported user-facing control; do not use feature coverage as visual acceptance.
- [ ] Verify actual Sidebar and collection/search navigation paths, including legacy redirects and dormant-page exclusions.
- [ ] Capture both implementations with matching data/profile/preferences at 480, 640, 768, 1024, 1280 and 1440 content widths plus short windows and Windows DPI scaling.
- [ ] Measure page gutters/max-width, header/hero heights, type sizes/weights/tracking/line height, grid columns/gaps/card aspect, button/pill geometry, colors/gradients/shadows and focus/hover/disabled states.
- [ ] Exercise each named button/menu/sheet/dialog action and cancel path in the matrix, plus loading/empty/404/403/transient/mutation failure states.
- [ ] Validate exact query/state payloads, capability/read-only gates, profile switching, request cancellation, race conditions, pagination and restore behavior.
- [ ] Use the conditional-dialog supplement for Edit Metadata/Match/Refresh/Manga Files/Edit Person and Requests report for request dialogs; verify their remaining source-depth limits and mark rendered acceptance independently.
- [ ] Record fixtures, screenshots, interaction results and unresolved exceptions per ledger row before declaring that particular page/control accepted.
- [ ] Leave every U and N item explicit until evidence exists. **This source audit does not certify 100% visual parity or exhaustive runtime functionality.**

This report is the browse-family contribution to the overall non-admin audit. It deliberately does not inspect unrelated administrative pages, ranking algorithms, playback engines or infrastructure.


