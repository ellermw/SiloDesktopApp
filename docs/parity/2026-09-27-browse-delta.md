# Browse and discovery delta audit — September 27, 2026

Desktop inspected: **`3488a4942ee0d03448bd609d04334e74b963bc7d`**, release **1.1.105**. Official Silo Server/WebUI reference: **`5e49cc8d376d2aaa1a7b4601ff876895f8d82efe`**, freshly fetched by the parent audit from `https://github.com/Silo-Server/silo-server.git`, checked out at `D:\SiloPlayer\.codex-tmp\silo-server-audit-20260927-5e49cc8d`. The comparison baseline is the previous audit's **`d4e35ba9df416e747822c6c9f2193b89c6b7e9fb`** (132 upstream commits behind). No legacy GitLab source was used. This establishes source versions, not the deployed server version.

This pass compares the actual changed upstream code with reachable desktop implementations. It identifies **six new source-confirmed discrepancies** and reconfirms **four carried findings**. “New” means a difference against behavior added or changed upstream since the prior reference; it does not imply every underlying desktop limitation was introduced recently. The 1.1.104 person-refresh/calendar repairs and 1.1.105 Home artwork repair are present and are not reopened as missing features.

No app code was changed. No installed app, playback, credentials, live server requests, or production system was accessed. The only executable verification was the existing isolated, in-memory HTTP reproduction for the two old logic defects. No full build or test suite, screenshot comparison, or visual-parity completion is claimed.

## New source-confirmed gaps

### N1 — P2: series Play still guesses from watched counts instead of using the server target

The current WebUI resolves the primary action directly from `item.play_content_id`, with labels based on the same detail response's watch rollup. It no longer depends on a Continue Watching lookup and then a guessed episode number: [itemDetailLayout.ts:84](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/web/src/pages/ItemDetail/itemDetailLayout.ts#L84), [SeriesContent.tsx:71](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/web/src/pages/ItemDetail/SeriesContent.tsx#L71). The server target is also used to select the Watch Together default episode (`SeriesContent.tsx:84`).

Desktop `src/SiloPlayer.Core/Models/Catalog/MediaItemDetail.cs:9` has no `PlayContentId` property (nor does any other desktop source reference `play_content_id`). The reachable series path in `src/SiloPlayer/Views/ItemDetailPage.xaml.cs:798` starts a separate Home/progress search, then at `:849` calls `SeriesPrimaryActionResolver.Resolve`, fetches the selected season's episodes, and picks a list index. `src/SiloPlayer.Core/Services/SeriesPrimaryActionResolver.cs:33` uses `watched_count + 1` and returns “Play Latest” at `:37`.

**Concrete difference:** with no in-progress episode and episodes 1 and 3 watched in a four-episode season, desktop's fallback chooses episode 3 by index; the current server/WebUI target chooses the first unwatched episode, episode 2. A fully watched final season also chooses its last episode on desktop instead of the server's first available episode. These are deductions from the selection branches, not live playback reproductions. The extra Home/progress/episode reads can also delay resolving Play: desktop's lookup is at `ItemDetailPage.xaml.cs:7717`, including up to 20 detail reads at `:7735`.

**Acceptance:** honor the authoritative target in the detail response, match Resume/Play Next/Start From Episode 1/Browse Series, and keep primary Play and party defaults consistent. Test noncontiguous watched episodes, fully watched series, missing/unavailable files, and a resume entry absent from Home. Do not apply a guessed client target when the server explicitly supplies none.

### N2 — P2: Home dismissal wording understates the new whole-show action

The same v2 dismissal route now drops an episode's entire series from both Continue Watching and Next Up until it is watched again or undone, and can synchronize that drop to supported watch providers: [home_dismissals.go:133](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/internal/api/handlers/home_dismissals.go#L133), [home.go:174](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/internal/apiv2/home.go#L174). WebUI therefore labels episode/series actions **“Drop show”** and success **“Show dropped”**: [MediaItemMenu.tsx:829](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/web/src/components/MediaItemMenu.tsx#L829), `web/src/hooks/queries/homeDismissals.ts:31`.

Desktop already calls that v2 endpoint (`src/SiloPlayer.Core/Api/HomeApi.cs:24`), but its menu still says **“Remove from Continue Watching” / “Remove from Next Up”** (`src/SiloPlayer/Controls/MediaItemMenu.cs:186`). `src/SiloPlayer/ViewModels/HomeViewModel.cs:679` immediately removes only the clicked card, and `:683` reports only that title as “dismissed.” This is a confirmed description mismatch for a materially broader action, not an absent server feature.

**Acceptance:** identify the show-wide action before activation, preserve the movie/book-specific labels, and reflect both affected rows and undo. Home has realtime refresh machinery (`HomeViewModel.cs:175`), so this audit does **not** assert that stale sibling cards persist indefinitely. Verify convergence and undo against an authorized fixture with the show represented in both rows, including watch-provider sync where enabled.

### N3 — P2: new optional detail-page theme music has no desktop consumer

Upstream now includes a `themes` set on eligible item details: [catalog_items.go:1045](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/internal/apiv2/catalog_items.go#L1045). The page invokes theme playback (`web/src/pages/ItemDetail/index.tsx:229`); [useThemeMusic.ts:16](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/web/src/pages/ItemDetail/useThemeMusic.ts#L16) reads opt-in/loop settings, checks capability, obtains a scoped playback grant (`:51`), and stops/suspends music for playback, trailers, profile changes, and navigation (`:71`, `:95`).

Desktop's detail DTO ends without a themes field (`src/SiloPlayer.Core/Models/Catalog/MediaItemDetail.cs:40`, `:93`), and detail population only builds trailers/extras (`src/SiloPlayer/Views/ItemDetailPage.xaml.cs:1381`). Searches across `src` for theme-music/theme-song names, the setting keys, and the new theme routes found no implementation. Thus an enabled, available server theme is ignored by this build.

**Acceptance:** add the optional detail behavior together with capability/setting support, owner continuity, grant/context cancellation, and interruption by every existing playback surface. Keep opt-in behavior. Settings controls belong to the separate settings audit; this finding is the absent data/playback consumer. No audio was started during this audit.

### N4 — P2: optional advisory-age badges are discarded

The current WebUI's detail adapter preserves `advisory_age` and `advisory_source` (`web/src/api/v2/catalog.ts:273`). It reads `catalog.show_advisory_age` only when supported and explicitly enabled: [useShowAdvisoryAge.ts:23](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/web/src/hooks/useShowAdvisoryAge.ts#L23). [MetadataBadges.tsx:39](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/web/src/pages/ItemDetail/components/MetadataBadges.tsx#L39) renders the age and known provider label/tooltip; movie, series, ebook and manga content receive the display decision (`web/src/pages/ItemDetail/index.tsx:261`).

Desktop's `src/SiloPlayer.Core/Models/Catalog/MediaItemDetail.cs:20` carries only `ContentRating`; there are no advisory-age/source consumers in `src`. `src/SiloPlayer/Views/ItemDetailPage.xaml.cs:1305` renders the certification badge but cannot render the advisory supplied by the new server.

**Acceptance:** preserve the new fields and render the optional badge only when supported/enabled, with the source label and unknown-source fallback. This display setting is distinct from server-enforced profile maturity limits; absence of the badge does not establish an authorization bypass. Profile-setting editing is owned by the settings audit.

### N5 — P3: TV detail retains the old scrolling layout and unbounded overview

The upstream episode/season/series layout now reserves viewport space for navigation and scrolls long information independently, keeping Play and the rail usable: [detailLayout.css:9](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/web/src/pages/ItemDetail/detailLayout.css#L9), `:58`, `:75`. `SeriesContent.tsx:107`, `SeasonContent.tsx:110`, and `EpisodeContent.tsx:283` opt into it. [DetailOverview.tsx:43](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/web/src/pages/ItemDetail/components/DetailOverview.tsx#L43) adds a three-line overview and More/Less; `DetailTitle.tsx:45` fits titles to a bounded line budget. The CSS includes different narrow/short viewport rules; it is not a single fixed-height design for every size.

Desktop still puts the hero and supporting content into one outer `ScrollViewer`/`StackPanel` (`src/SiloPlayer/Views/ItemDetailPage.xaml:159`, `:163`). Its overview is an unrestricted wrapping `TextBlock` (`:728`); `src/SiloPlayer/Views/ItemDetailPage.xaml.cs:522` moves metadata, the entire overview and actions into the same hero information panel. Season/episode sections remain farther down the outer content (`ItemDetailPage.xaml:1010`, `:1087`, `:1135`). There is no equivalent More/Less overview control in this path.

**Acceptance:** compare the new TV detail information/controls/navigation structure at ordinary, narrow, short and high-DPI sizes, with long titles/overviews and the mini-player present. This is a source-confirmed structural difference; exact clipping, spacing, typography and acceptable native adaptation require side-by-side runtime verification. It is not a screenshot-based pixel finding.

### N6 — P2: detail unavailable/retry states do not match the new error handling

The new WebUI distinguishes an unavailable item from a transient load error with Retry; a fresh 404 outranks previously cached data: [ItemDetail/index.tsx:213](https://github.com/Silo-Server/silo-server/blob/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe/web/src/pages/ItemDetail/index.tsx#L213), `:246`. Person detail does the same (`web/src/pages/PersonDetail.tsx:58`, `:113`), and collections have explicit unavailable navigation (`web/src/pages/Catalog.tsx:294`, `:326`; editor `web/src/pages/CollectionEditor.tsx:53`).

The clearest desktop mismatch is item detail: all non-cancellation load exceptions set a generic error (`src/SiloPlayer/ViewModels/ItemDetailViewModel.cs:257`), while the visible panel hardcodes **“Item not found.”** for every such failure and has no Retry button (`src/SiloPlayer/Views/ItemDetailPage.xaml:112`, `:123`). A network/500 failure is therefore presented as missing content. Person initial errors remain plain text without Retry (`src/SiloPlayer/Views/PersonDetailPage.xaml:92`); its new observer deliberately catches **all** read failures (`src/SiloPlayer.Core/Api/PeopleApi.cs:51`) and keeps the old person, including on a later 404. That is a newer unavailable-state difference, not a claim that B5's delayed metadata observer is still absent.

**Acceptance:** distinguish 404 from transient failures, offer the appropriate retry/navigation affordance, and replace previously shown person/detail data after authoritative removal. Test initial offline/500/404 responses separately from a successful view followed by 404. Do not infer whether an inaccessible item was deleted versus permission-restricted. Collection unavailable presentation is a related comparison target; the actionable evidence above is sufficient without assuming identical bugs in every collection path.

## Carried findings, rechecked against 1.1.105

| Previous finding | Current evidence and status | Current upstream reference |
|---|---|---|
| **B1 — P1: smart collection AND/OR/group loss** | Still present and reproduced. Actual edit routes to `src/SiloPlayer/Views/CollectionsPage.xaml.cs:1475`; `SmartCollectionWizardViewModel.cs:140` flattens groups, `:319`/`:324` writes outer match into the one rebuilt group, `:256` saves it. Null media scope is still replaced with movie at `:124`. Metadata-only edits can change members. | `web/src/pages/SmartCollectionWizard.tsx:127`, `:302` preserves the draft definition; `web/src/components/collections/CollectionRulesEditor.tsx:40` and `:119` preserve distinct match/groups. |
| **B2 — P2: duplicate Search disables pagination** | Still present and reproduced. `src/SiloPlayer/ViewModels/SearchViewModel.cs:177` clears snapshot/has-more **before** the duplicate return at `:194`; `:302` then blocks the next page. Enter is wired at `src/SiloPlayer/Views/SearchPage.xaml.cs:418`. | `web/src/pages/Catalog.tsx:174` retains keyed `useCatalogWindow` paging. |
| **B3 — P2: Search people never queried** | Still present. Shell route is `src/SiloPlayer/MainWindow.xaml.cs:3143`; `SearchPage.xaml.cs:35` binds PeopleResults, but `SearchViewModel.cs:224` always supplies an empty list. The helper at `:464` is not called by this search. | `web/src/pages/Catalog.tsx:159` calls scoped people search and `:466`/`:479` renders error/retry/results. |
| **B4 — P2: Search filter model is smaller** | Still present. `src/SiloPlayer/Views/SearchPage.xaml.cs:608` builds only genre/content-rating/resolution/country; `SearchViewModel.cs:383` sends those basic values plus type/sort. Library's advanced groups do not make them available on Search. | `web/src/pages/Catalog.tsx:380` uses the shared guided/advanced panel and `:391` retains the full query in navigation; `web/src/components/catalog/CatalogFilterSheet.tsx:128` selects modes. |

B1 acceptance remains a metadata-only edit of a disposable mixed-group query, preserving all groups, their match modes, null scope, typed rule values, sort and limit. B2 acceptance remains repeated Enter followed by another distinct page. B3 needs people capabilities/scope, stale cancellation and isolated failure. B4 needs grouped/year-range Search filters with the text query and paging preserved. See `2026-09-24-browse-verification.md` for the fuller original cases; its upstream line references refer to the old commit, not this report's current reference.

### Narrow offline verification

Inspected and reran the existing harness without modifying it:

```powershell
dotnet run --project D:\SiloPlayer\.codex-tmp\browse-audit-repro\BrowseAudit.csproj -c Release --no-restore -nologo
```

Exit code **0**; output:

```text
Smart query: input groups=2, matches=any/all, outer=all; output groups=1, match=all, rules=3
Search initial: results=1, catalogRequests=1
Search after duplicate + load more: results=1, catalogRequests=1
CONFIRMED: smart groups flattened and duplicate submission prevents next-page fetch.
```

The harness links the current production smart-wizard and Search view-model files, intercepts every HTTP call in memory, and rejects non-GET requests. It reads a prebuilt Release Core assembly and is evidence for the two linked view-model branches, not a new whole-app build or installed-runtime test. The fixture has an explicit `has_more=true`; the one-item page is intentional and the loss does not depend on page size. No collection was saved.

## Repairs retained and changes that do not establish a new gap

| Area | Reassessment |
|---|---|
| Person metadata/photo refresh, old B5 | Repaired source is present: `src/SiloPlayer/ViewModels/PersonDetailViewModel.cs:169` starts observation; `:192` updates person metadata without replacing filmography; `src/SiloPlayer.Core/Api/PeopleApi.cs:29` implements the 3-second/30-second observer with context/cancellation checks. Initial delayed metadata/photo behavior is not missing. N6 concerns the distinct new 404 requirement. |
| Calendar sticky navigation, old B6 | Repaired source is present: `src/SiloPlayer/Views/CalendarPage.xaml:232` keeps a spacer, the interactive navigator is outside the scrolling content at `:299`, and `CalendarPage.xaml.cs:127` pins its translated position. The documented 1.1.104 native verification is retained; this pass did not rerun it. |
| Home refresh/flashing | 1.1.105 keeps mounted item identity and updates volatile signed artwork: `src/SiloPlayer.Core/Services/MediaItemCollectionReconciler.cs:28`, `:70`, `:142`; `src/SiloPlayer/Controls/PosterCard.xaml.cs:256` observes artwork changes. Upstream now similarly separates signed URL changes from image identity (`web/src/pages/ItemDetail/DetailHero.tsx:133`). No recurrence was observed or tested, so neither regression nor full closure is inferred. |
| Home load-generation fix | Upstream `web/src/pages/Home.tsx:149`/`:165` guards late section completion by generation. Desktop already has `_sectionLoadGeneration` and `IsCurrentSectionLoad` in `src/SiloPlayer/ViewModels/HomeViewModel.cs:353`, `:387`, `:539`. The upstream fix's existence is not proof the same bug exists on desktop. Library Recommended has its own owner/cancellation and deferred-refresh path (`src/SiloPlayer/Views/LibraryPage.xaml.cs:615`); rapid library/layout switches remain runtime checks, not a newly asserted defect. |
| Global Search/card/performance refactors | Web chunk prefetch (`web/src/components/GlobalSearch.tsx:314`), lazy metadata dialogs (`web/src/components/MediaItemMenu.tsx:87`) and browser first-frame intent calls do not need literal WinUI equivalents. Native launch, dialog readiness, artwork-failure fallback and time-to-play are behavior/performance checks. Existing Search defects are listed above from actual desktop branches. |

## Upstream server-only changes / no duplicated desktop logic required

These changed files are not counted as missing desktop features merely because they differ from the last reference:

- **MDBList import pagination:** `internal/collectionutil/mdblist.go:136` now fetches public feeds page by page, bounded by entry/response limits. The server import/synchronization consumer gets the corrected membership; the desktop must display those results, not reimplement provider paging. Imported-collection save/list/error behavior still needs fixture verification.
- **Recommendation anchor consistency:** `internal/recommendations/reader.go:122` retains the actual source anchor with the returned recommendations, returning it at `:158`. This is server selection/cache logic. A visible section/title mismatch should be demonstrated before treating it as a separate desktop algorithm gap.
- **Maturity/access and Jellyfin browse predicates:** `internal/catalog/browse.go:49` carries server maturity limits; `:1766` adds compatibility predicates. Server visibility policy and Jellyfin protocol expansion are not native-client browse features to reproduce. Display/edit support for new advisory settings is separate from enforcement (N4 and the settings audit).
- **Show-drop persistence and provider dispatch:** `internal/api/handlers/home_dismissals.go:159` and `:260`, plus `internal/catalog/nextup_repo.go:67`, implement storage/sync/filtering on the server. Desktop already uses the v2 API. N2 requests accurate user-facing semantics and reconciliation, not a parallel local dropped-show database.

## Remaining runtime verification

Prioritize B1, B2 and N1 with controlled fixtures before live changes. For the broader surfaces, verify Home/Recommended independent loading, cancellation across library/profile/layout switches, metadata bursts and signed-artwork rotation without flashing; large Library filter lists and rapid scroll; Search long-result paging and people failure; smart/imported collection mixed rules and access changes; and detail initial/transient errors, later 404s, new TV layouts and long copy. Person background completion and the repaired calendar need representative server data and DPI/width checks before claiming visual parity. N2 requires explicit awareness that a live episode dismissal now changes the entire show's state and can propagate to watch providers.

Source coverage of browse, Home, Library, detail, collections and search is not proof of visual equivalence. This delta report does not close any whole-page or whole-app parity milestone.
