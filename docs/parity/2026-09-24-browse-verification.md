# Browse and discovery verification — September 24, 2026 UTC

This is a current-source audit of the desktop working tree, including local 1.1.103 work, against official Silo Server/WebUI commit **`d4e35ba9df416e747822c6c9f2193b89c6b7e9fb`**. The reference is the freshly fetched read-only checkout `.codex-tmp/silo-server-audit-d4e35ba9`. No legacy GitLab source was used. The deployed server version is not established by this comparison.

Six actionable discrepancies were found: one potentially destructive smart-collection edit defect, two search defects, and three remaining behavior/layout gaps. Two defects were reproduced with the actual desktop view-model source in an offline executable harness. This report does not claim installed-app or pixel parity. No app source fixes, production reads/writes, live requests, UI automation, commits, or pushes were performed in this sub-audit.

The already-tested local watched-state repair is **not** listed as missing. Earlier “Substantial” labels are treated as inventory, not evidence of either completion or failure.

## Principal surface coverage

All line references below are to the inspected working tree/reference commit. “Present” means the reachable source implements the named flow; it does not mean every state has been exercised on the installed build.

| Surface | Current desktop evidence | Current WebUI evidence | Result and remaining verification |
|---|---|---|---|
| Home, featured hero and rows | `src/SiloPlayer/ViewModels/HomeViewModel.cs:303` reconciles layout slots; `:328` reserves the first featured section for the hero; `:370` independently fetches sections; `:435` retries a section; `:659` dismisses with undo support | `web/src/pages/Home.tsx:44`, `:212`, `:440` | Core layout/independent section loading, retry, dismiss/undo are present. Runtime: empty/failing individual rows, return after playback, configured layouts, card geometry, and refresh without blanking. |
| Library browse, filters and collections tab | `src/SiloPlayer/ViewModels/LibraryViewModel.cs:146` has grouped advanced rules; `:900` sends those rules; `src/SiloPlayer/Views/LibraryPage.xaml.cs` implements the bounded native grid | `web/src/pages/LibraryBrowse.tsx:176`, `:238` uses a catalog window and current filter panel | Advanced filters and bounded catalog loading are present; do not call these absent. Runtime: large filter lists, rapid scrolling, jump/seek windows, cached revisit, failures and DPI. No recurrence of the historic freeze was asserted or tested here. |
| Search | `src/SiloPlayer/MainWindow.xaml.cs:3143` routes Search to `SearchPage`; `src/SiloPlayer/Views/SearchPage.xaml.cs:35`, `:418`, `:608`; `src/SiloPlayer/ViewModels/SearchViewModel.cs:168` | `web/src/pages/Catalog.tsx:151`, `:166`, `:353`, `:438` | **B2–B4** below. Primary catalog results, optional outside-library discovery, type scope, sort and four basic filters are present. |
| Favorites, watchlist, history and generic catalog | `src/SiloPlayer/MainWindow.xaml.cs:3161` routes personal catalog surfaces; `src/SiloPlayer/Views/CatalogPage.xaml.cs:115`, `:137`, `:358`, `:575` | `web/src/pages/Catalog.tsx:113`, `:353`, `:490` | Reachable generic catalog has source-order selection, filters, pagination and history selection actions. Legacy list view-model files alone are not the correct route evidence. Runtime: source-order persistence, remove/history selection, empty/failure states and long lists. |
| Movie, series, season and episode detail | `src/SiloPlayer/Views/ItemDetailPage.xaml.cs:714`, `:734`, `:772`, `:783`, `:1374`, `:2914`, `:3344` | `web/src/pages/ItemDetail/index.tsx:8` selects separate movie/series/season/episode components | Type-specific hierarchy, season episodes/siblings, conditional similar items, curation authorization and download gating are present. Runtime: versions/tracks, layout and focus at different widths, stale-response navigation and aggregate watched state. Subtitle-provider behavior is assigned to the player audit; not duplicated here. |
| Person detail | `src/SiloPlayer/ViewModels/PersonDetailViewModel.cs:150`, `:218`; `src/SiloPlayer.Core/Api/CatalogApi.cs:329`; `src/SiloPlayer/Views/PersonDetailPage.xaml.cs:59` | `web/src/pages/PersonDetail.tsx:39`, `:70`, `:83` | Biography/date display, all/movie/series filters, descending-year filmography and further pages are present. **B5** addresses asynchronous metadata/photo refresh. Admin editing is outside this user-facing pass. |
| Collections: list, templates, imports, groups and editor | `src/SiloPlayer/Views/CollectionsPage.xaml.cs:114`, `:512`, `:1003`, `:1469`, `:1483`; `src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs:188` | `web/src/pages/Collections.tsx`; `web/src/pages/ImportedCollectionEditor.tsx:128`, `:219`; `web/src/pages/SmartCollectionWizard.tsx:333` | Template gallery/configuration, import flow, grouping, imported display filters and read-only ownership exist. **B1** is in the actual smart-wizard edit path. Runtime: template/provider capability failures, artwork/collages, grouping/order, schedule presentation and multipart saves. |
| Recommendations and expanded section | `src/SiloPlayer/ViewModels/RecommendationsViewModel.cs:61`, `:123`; `src/SiloPlayer/Views/RecommendationsPage.xaml.cs:186`; `src/SiloPlayer/ViewModels/RecommendationSectionViewModel.cs:29` | `web/src/pages/Recommendations.tsx:12`, `:151`, `:188`; `web/src/pages/RecommendationsSection.tsx` | Independent taste-profile/discover loading and kind/key section navigation exist. Runtime: absent taste data, one query failing, long rows, refresh, custom card settings and newer server-side episode-to-series anchors. The server's anchor policy does not require duplicating that policy in the client. |
| Taste onboarding | `src/SiloPlayer/Views/TasteSeedPage.xaml.cs:22`, `:48`, `:62` | `web/src/pages/TasteSeed.tsx:23`, `:29`, `:37` | Reachable loading, completion and further-item loading paths are present. Runtime comparison still needed for preselected favorites, explicit deselection, skip/cancel and return to settings. |
| Requests, discovery browse and request detail | `src/SiloPlayer/ViewModels/RequestsViewModel.cs:56`, `:110`, `:186`; `src/SiloPlayer/Views/RequestBrowsePage.xaml.cs:54`, `:115`; `src/SiloPlayer/Views/RequestDetailPage.xaml.cs:96` | `web/src/pages/Requests.tsx:141`, `:280`, `:698`; `web/src/pages/RequestBrowse.tsx`; `web/src/pages/RequestDetail.tsx` | Server requestability, discovery/mine error separation, search pages, provider browse, detail request and open-in-library are present. Both sides intentionally fetch at most 100 “My requests”; that is not a desktop-only pagination finding. Runtime: disabled providers, exhausted limits, no allowed media types, cancellation and stale permission changes. |
| Calendar | `src/SiloPlayer/ViewModels/CalendarViewModel.cs:69`, `:120`, `:180`; `src/SiloPlayer/Views/CalendarPage.xaml.cs:267`, `:365`, `:851` | `web/src/pages/Calendar.tsx:28`, `:120`, `:199` | Following/Trending/All presets, library scope, viewer timezone, Mon–Sun navigation, selected-day empty state and responsive layout are present. **B6** is a source-verified sticky-navigation difference. Runtime: dense weeks, DST/timezone boundaries and narrow/high-DPI layouts. |

## Actionable findings

### B1 — P1: editing a smart collection changes its grouped query

**Evidence: reproduced offline using current production view-model source.** The real Collections edit action routes smart collections to `SmartCollectionWizardPage`, not the older generic editor (`src/SiloPlayer/Views/CollectionsPage.xaml.cs:1469`). `SmartCollectionWizardViewModel.ConfigureAsync` reads the outer match then flattens every group's rules (`:125`, `:140`). `BuildQueryDefinition` emits exactly one group and assigns the outer match to that group (`:298`, `:319`). `SaveAsync` sends this rebuilt definition (`:246`, `:256`). A metadata-only save therefore changes rule semantics.

Example: `(Comedy OR Drama) AND year >= 2000` becomes `Comedy AND Drama AND year >= 2000`. This can unexpectedly remove most collection members. Preserving the outer match alone does not preserve independent group matches. The load path also substitutes `movie` for a null media scope (`:124`); preserving unscoped collections should be part of the same fix.

The current WebUI retains the query definition in the draft, passes groups and their match structure through the advanced editor, previews that definition and saves the draft. Sources: [SmartCollectionWizard.tsx:126](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/pages/SmartCollectionWizard.tsx#L126), [CollectionRulesEditor.tsx:40](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/components/collections/CollectionRulesEditor.tsx#L40), [preview at :300](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/pages/SmartCollectionWizard.tsx#L300), [save at :417](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/pages/SmartCollectionWizard.tsx#L417).

**Reproduction/acceptance:** use a disposable collection with at least two groups and mixed AND/OR matches. Open desktop Edit, change only the name, and compare the proposed/saved query and members. Preserve all group boundaries, match modes, media scope, typed rule values, sort and limit unless explicitly edited. Preview and save must use the same preserved definition. This audit did not save any real collection.

### B2 — P2: re-submitting an unchanged search disables further pages

**Evidence: reproduced offline using current production view-model source.** `SearchViewModel.SearchAsync` clears `_snapshot` and `_hasMore` before checking whether the search is a duplicate (`src/SiloPlayer/ViewModels/SearchViewModel.cs:177`, `:195`). The duplicate branch returns without restoring them. `LoadMoreAsync` then returns immediately because `_hasMore` is false (`:302`). This is directly reachable by pressing Enter after the same search has already completed (`src/SiloPlayer/Views/SearchPage.xaml.cs:418`).

The result set remains visible, so the user sees an apparently valid but permanently truncated search until changing the query/filter. Upstream keeps paging state inside the keyed catalog window and renders through it: [Catalog.tsx:166](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/pages/Catalog.tsx#L166), [ItemGrid at :490](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/pages/Catalog.tsx#L490).

**Reproduction/acceptance:** search for a term with more than 60 matches, wait for completion, press Enter without editing, then scroll to the next page. Preserve the cursor and has-more state when ignoring a duplicate, or perform a complete valid reload. The next fetch must occur and append distinct items; repeated Enter must not disable paging.

### B3 — P2: Search never loads people results

**Evidence: source-verified reachable mismatch.** The shell Search route opens `SearchPage` (`src/SiloPlayer/MainWindow.xaml.cs:3143`), which binds `PeopleRepeater` to `PeopleResults` (`src/SiloPlayer/Views/SearchPage.xaml.cs:35`). However, `SearchViewModel.SearchAsync` unconditionally supplies `Task.FromResult(new List<Person>())` instead of a people search (`src/SiloPlayer/ViewModels/SearchViewModel.cs:224`). Its comment says a separate global surface owns people, but this is the shell's Search page. The existing `SearchPeopleAsync` helper is unused by that flow.

The current WebUI queries people on query searches and renders a cast carousel with independent loading/error/retry states: [Catalog.tsx:151](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/pages/Catalog.tsx#L151), [people result states at :438](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/pages/Catalog.tsx#L438).

**Reproduction/acceptance:** search for a known actor/author in a media scope supported by the server's people-search capabilities. Display matching people and open their person detail. Respect capabilities/media scope, cancel stale searches, and keep primary catalog results available if people lookup fails. A toolbar suggestion elsewhere does not replace the full Search result section.

### B4 — P2: Search filters expose a smaller query model than the current WebUI

**Evidence: source-verified surface discrepancy.** Search builds only genre, content rating, resolution and country controls (`src/SiloPlayer/Views/SearchPage.xaml.cs:608`); its filter handler and request carry these four values plus media type/sort (`:643`; `src/SiloPlayer/ViewModels/SearchViewModel.cs:394`). Library advanced groups elsewhere do not make them available on this page.

The WebUI query-search result page renders the shared guided/advanced filter panel, with query-definition changes retained in navigation: [Catalog.tsx:353](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/pages/Catalog.tsx#L353). The filter sheet exposes both guided rules and grouped advanced rules: [CatalogFilterSheet.tsx:128](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/components/catalog/CatalogFilterSheet.tsx#L128), [editors at :143](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/components/catalog/CatalogFilterSheet.tsx#L143).

**Reproduction/acceptance:** on the same text search, compare available filters, then apply a year range and a grouped rule combination in each client. Desktop must expose the applicable current query filters, preserve the text query and scope when switching guided/advanced modes, clear them predictably, and page the filtered results. This is a missing search-surface capability, not a claim that desktop library filters are absent.

### B5 — P2: person detail does not follow server-side metadata/photo refresh

**Evidence: source-verified missing refresh flow; visual consequence requires a suitable live fixture.** `PersonDetailViewModel.LoadAsync` performs one person read plus a filmography read and assigns the result once (`src/SiloPlayer/ViewModels/PersonDetailViewModel.cs:150`, `:156`). The page loads on navigation and listens only to its local view model (`src/SiloPlayer/Views/PersonDetailPage.xaml.cs:39`, `:73`). The API includes `PeopleApi.RefreshPersonAsync` (`src/SiloPlayer.Core/Api/PeopleApi.cs:19`), but the person page does not call it or observe subsequent person refresh results.

The current WebUI observes refresh after every person view, invalidates related item/cast detail data, and requests refresh when metadata is incomplete: [PersonDetail.tsx:50](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/pages/PersonDetail.tsx#L50), [incomplete metadata at :59](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/pages/PersonDetail.tsx#L59). Its observer continues polling while relevant views need it: [people.ts:55](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/hooks/queries/people.ts#L55).

**Impact/acceptance:** a person initially lacking a cached photo/biography can stay incomplete on desktop after the background job finishes. Use a controlled fixture whose person metadata changes after the initial read. Update the open page and related cast rows after completion, stop work on irrelevant navigation, and do not confuse rotating presigned URLs with refresh completion. This does not ask for reintroducing server-administration screens.

### B6 — P3: calendar week navigation scrolls out of view

**Evidence: source-verified layout structure; installed side-by-side confirmation pending.** Desktop places `WeekNavigatorBorder` inside the scrolling `StackPanel` (`src/SiloPlayer/Views/CalendarPage.xaml:152`, `:159`, `:240`). `CalendarPage.xaml.cs:407` scrolls to day groups; its responsive code changes navigator margins and padding (`:851`, `:865`) but does not pin it.

The current WebUI explicitly makes the week navigator sticky across all day groups: [Calendar.tsx:194](https://github.com/Silo-Server/silo-server/blob/d4e35ba9df416e747822c6c9f2193b89c6b7e9fb/web/src/pages/Calendar.tsx#L194). Users reading lower days can change week/day without scrolling back to the top.

**Reproduction/acceptance:** open a dense week and scroll below the first few day groups. Keep week/day navigation reachable with comparable sticky behavior, including keyboard focus and selected-day scrolling, at normal/narrow/high-DPI widths. Confirm visual offsets against the WebUI before marking parity complete.

## Offline reproduction evidence

Command completed successfully with exit code 0:

```powershell
dotnet run --project D:\SiloPlayer\.codex-tmp\browse-audit-repro\BrowseAudit.csproj -c Release -nologo
```

```text
Smart query: input groups=2, matches=any/all, outer=all; output groups=1, match=all, rules=3
Search initial: results=1, catalogRequests=1
Search after duplicate + load more: results=1, catalogRequests=1
CONFIRMED: smart groups flattened and duplicate submission prevents next-page fetch.
```

The harness links the complete current `SmartCollectionWizardViewModel.cs`, `SearchViewModel.cs` and `BulkObservableCollection.cs`, using the prebuilt Release Core assembly. All HTTP is intercepted by an in-memory `HttpMessageHandler`; non-GET requests are rejected. The collection goes through the real `ConfigureAsync` and `BuildQueryDefinition` methods. Search goes through the real command twice and then `LoadMoreAsync`, with an explicit `has_more=true` response. The synthetic one-item first page makes the state failure observable without a large fixture; the bug is independent of page size.

These are executable logic reproductions, not screenshots or live-server tests. The harness and its generated outputs remain under ignored `.codex-tmp`; app/test projects were not edited. Full test/build and installed-runtime evidence are owned by the parent audit and must be reported separately.

## Suggested work selection

Address B1 first because an ordinary edit can change a saved collection's meaning. B2 and B3 are bounded search fixes with direct user impact. B4 is a larger filter-surface parity task. B5 needs a delayed-metadata fixture; B6 needs actual side-by-side layout verification. For the other principal surfaces, use the runtime cases in the coverage table to turn source coverage into demonstrable parity, without representing untested visuals as completed work.
