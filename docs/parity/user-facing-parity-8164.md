# User-Facing Parity Evidence Matrix

## Reference

- Official repository: `https://github.com/Silo-Server/silo-server`
- WebUI commit: `8164fd594b9fdd8c1944bb0b6251f2d00e4a24ca`
- Final reference fetch: official GitHub `origin/main` was fetched again on 2026-08-27 and remained exactly `8164fd594b9fdd8c1944bb0b6251f2d00e4a24ca`.
- Desktop baseline: `8940c11ebba42439e18e0fb8df8a978651d5159c` (`v1.1.92`)
- Audit branch: `codex/user-facing-browse-detail-parity`
- Profile requirement: compare with the same admin profile, server theme, libraries, and personalization on both clients

## Status definitions

- **Mapped:** authoritative WebUI route and desktop implementation are identified.
- **Automated:** behavior is protected by an executable test that observes a user-visible or API-boundary result.
- **Runtime verified:** the installed Windows app was compared directly with the live WebUI for the listed viewport and input path.
- **Complete:** automated and runtime evidence cover all relevant states; no unexplained deviation remains.

No row in this document is complete at milestone start. Source coverage alone is not parity evidence.

## Current route contract

| WebUI route or surface | Authoritative WebUI | Desktop implementation | Initial status | Runtime evidence |
|---|---|---|---|---|
| `/` | `pages/Home.tsx`, `components/HeroBanner.tsx`, `components/SectionRow.tsx` | `Views/HomePage`, `Controls/HeroCarousel`, `Controls/SectionRow` | Mapped; implementation broad | Pending |
| Global search and `/catalog?source=query` | `components/GlobalSearch.tsx`, `pages/Catalog.tsx`; `/search` is a legacy redirect | `Controls/GlobalSearchDialog`, `Views/SearchPage`, `Views/CatalogPage` | Mapped; desktop retains a dedicated full-search page | Pending comparison of direct user path and results |
| `/catalog` | `pages/Catalog.tsx` and catalog components/hooks | `Views/CatalogPage` | Mapped; implementation broad | Pending |
| `/library/:libraryId` | `pages/LibraryPage.tsx`, `LibraryBrowse.tsx`, `LibraryRecommended.tsx`, `LibraryCollections.tsx` | `Views/LibraryPage` and catalog navigation variants | Mapped; implementation broad | Pending stress and subroute comparison |
| `/favorites` | Legacy redirect to personal catalog source `favorites` | `CatalogPage` with `CatalogNavigation("favorites", ...)` | Mapped | Pending |
| `/watchlist` | Legacy redirect to personal catalog source `watchlist` | `CatalogPage` with `CatalogNavigation("watchlist", ...)` | Mapped | Pending |
| `/history` | Legacy redirect to personal catalog source `history` | `CatalogPage` with `CatalogNavigation("history", ...)` | Mapped | Pending |
| `/item/:id` movie | `pages/ItemDetail/MovieContent.tsx`, `DetailHero.tsx`, shared detail components | `Views/ItemDetailPage`, `ViewModels/ItemDetailViewModel` | Mapped; implementation broad | Pending |
| `/item/:id` series | `pages/ItemDetail/SeriesContent.tsx`, `SeasonCarousel.tsx` | `Views/ItemDetailPage`, `ViewModels/ItemDetailViewModel` | Mapped; implementation broad | Pending |
| `/item/:id` season | `pages/ItemDetail/SeasonContent.tsx`, `SeasonEpisodeGrid.tsx` | `Views/ItemDetailPage`, `ViewModels/ItemDetailViewModel` | Mapped; implementation broad | Pending |
| `/item/:id` episode | `pages/ItemDetail/EpisodeContent.tsx`, `EpisodeCarousel.tsx` | `Views/ItemDetailPage`, `ViewModels/ItemDetailViewModel` | Mapped; implementation broad | Pending |
| `/item/:id` audiobook | `pages/ItemDetail/AudiobookContent.tsx`, audiobook components/player | `Views/ItemDetailPage`, audiobook controls/services | Mapped; implementation broad | Pending |
| `/item/:id` ebook | `pages/ItemDetail/EbookContent.tsx` | `Views/ItemDetailPage`, `Views/EbookReaderPage` | Mapped | Pending format and navigation verification |
| `/item/:id` manga | `pages/ItemDetail/MangaContent.tsx` | `Views/ItemDetailPage`, `Views/EbookReaderPage` | Mapped | Pending format and navigation verification |
| `/reader/ebook/:contentId` | `pages/EbookReader.tsx`, `reader/*` | `Views/EbookReaderPage` | Mapped | Pending |
| `/person/:id` | `pages/PersonDetail.tsx` | `Views/PersonDetailPage`, `ViewModels/PersonDetailViewModel` | Mapped | Pending |
| `/collections` | `pages/Collections.tsx` | `Views/CollectionsPage` | Mapped | Pending |
| `/collections/new`, `/:id/edit` | `pages/CollectionEditor.tsx`, template and smart-collection components | `Views/CollectionEditorPage`, `Views/SmartCollectionWizardPage` | Mapped; guided-rule coverage incomplete until verified | Pending |
| `/collections/:id` | Legacy redirect to catalog source for the collection | `Views/CollectionBrowsePage` and `CatalogPage` collection navigation | Mapped; desktop has two paths to reconcile | Pending |
| `/recommendations` | `pages/Recommendations.tsx` | `Views/RecommendationsPage` | Mapped | Pending |
| `/recommendations/section/:kind/:key?` | `pages/RecommendationsSection.tsx` | `Views/RecommendationSectionPage` | Mapped | Pending |
| `/requests` | `pages/Requests.tsx`, guarded by requests-enabled permission | `Views/RequestsPage` | Mapped | Pending permission and state comparison |
| `/requests/:mediaType/:tmdbId` | `pages/RequestDetail.tsx` | `Views/RequestDetailPage` | Mapped | Pending |
| `/requests/browse/{studio,network,genre}/:slug` | `pages/RequestBrowse.tsx` | `Views/RequestBrowsePage` | Mapped | Pending |
| `/calendar` | `pages/Calendar.tsx` | `Views/CalendarPage` | Mapped | Pending |
| `/notifications` | `pages/Notifications.tsx` | `Views/NotificationsPage` | Mapped | Pending |
| `/rooms/:roomId`, `/rooms/join` | Watch Together room/join pages | Watch Together room/join pages | Mapped; playback-focused validation remains separate | Pending |
| Sidebar, header, activity indicator, mobile drawer | `components/Layout.tsx`, `components/AppSidebar.tsx` | `MainWindow.xaml`, `MainWindow.xaml.cs`, `ServerActivityButton` | Mapped; prior regressions reported | Pending |
| Media context menu and user/admin actions | `components/MediaItemMenu.tsx` and detail dialogs | `Controls/MediaItemMenu` and dialogs | Mapped; permissions require admin/non-admin comparison | Pending |

## Authoritative behavior noted at baseline

- Home fetches the layout first, loads at most five section item requests concurrently, retains cached sections, and represents loading or failure per section instead of blanking the full page.
- An initial Home layout failure has a page-level retry; an empty layout links to Home Screen customization. `Explore all` is conditional: it appears only for browseable sections whose `total_count` exceeds `item_limit`, so it must not be promised or rendered for every Home row.
- `/search`, `/browse`, `/favorites`, `/watchlist`, `/history`, and user collection-detail URLs are compatibility redirects into the unified Catalog surface.
- Requests are route-guarded by the server feature/permission state.
- Item detail uses content-specific movie, series, season, episode, audiobook, ebook, and manga layouts plus shared hero, action, metadata, track, subtitle, version, trailer, extra, and technical-information components.
- Admin actions on user-facing media surfaces are conditional; their visibility must be verified with both an admin profile and a non-admin profile or an equivalent permission fixture.

## Runtime comparison log

| Date | Desktop build | WebUI commit | Viewport / scale | Input | Surfaces | Result / evidence |
|---|---|---|---|---|---|---|
| 2026-08-27 | feature branch from `8940c11` | `8164fd594b9fdd8c1944bb0b6251f2d00e4a24ca` | Existing Chrome viewport, 2543x1272 capture | Browser DOM and screenshots | Home, unified search, Movies library, Favorites, movie/series/episode detail, Recommendations, Requests, Calendar, Notifications, Collections, person detail, Watch Party join | Live WebUI was authenticated and inspected directly. Confirmed conditional Home actions, search scope/filters, library tabs/sort/filter controls, compact personal-list cards, detail hierarchy/actions/admin-only media locations, cast/crew rows, supporting-route controls, and current server branding. This is WebUI-reference evidence, not a desktop-runtime parity claim. |
| 2026-08-27 | feature branch from `8940c11` | `8164fd594b9fdd8c1944bb0b6251f2d00e4a24ca` | Existing desktop placement unchanged | Process launch and local logs | Startup/session restoration | A separately named QA process launched from the fresh x64 Release output and restored the saved server/profile on its first attempt in about one second. No new crash or navigation error was written. Automated desktop interaction was stopped after the window-control provider returned an unrelated fullscreen surface for the exact process selector; no visual desktop-runtime claim is made from that attempt. |

## Code and automated milestone evidence

- The full Release test suite covers the current Home, Search, Catalog, Library, item-detail, collection, request, recommendation, calendar, notification, image-cache, input, reader, and authentication contracts. The exact final count is recorded in the PR verification after review fixes.
- Home and library-recommended refreshes reconcile mounted section and item collections without replacing populated pages. Failed background refreshes retain the last good surface.
- Search retains keyboard focus, cancels obsolete work, applies current-query results only, and no longer blocks first results on optional request-discovery work.
- Shared poster, landscape, and virtual-library cards now use the current WebUI action set, progress geometry, episode state, touch/long-press policy, cached artwork path, and personal-source action sizing.
- Movie, series, season, and episode detail navigation preserves prefetched state, supports visible horizontal navigation, and keeps admin-only actions and media locations permission-gated.
- Remote branding and artwork bindings used by authentication, profile, metadata, room, request, and shell surfaces route through the bounded byte cache rather than retaining expiring presigned URLs.
- Ebook content is isolated to the mapped reader origin; top-level external navigation, new windows, remote subresources, permissions, and untrusted web messages are rejected.
- Server and provider URLs now accept only normalized HTTP(S) origins and visibly reject unsupported or credential-bearing URLs.
- The production single-instance mutex and activation pipe remain unchanged. A hashed, opt-in `SILO_QA_INSTANCE_ID` suffix permits a separate internal QA process without exposing the raw identifier.

## Open evidence requirements

- Capture initial loading, cached revisit, background refresh, scoped failure, empty, permission-denied, and expired-image behavior.
- Compare the fresh desktop build at normal desktop, 3440x1440 ultrawide, and 4K/DPI-scaled layouts without resizing or closing the user's existing windows unexpectedly.
- Exercise mouse, keyboard, and controller navigation, including focus restoration after dialogs and horizontal rows.
- Stress the large Movies library and review `%LOCALAPPDATA%\SiloPlayer\ui_lag.txt` after the exact run.
- Repeat the official GitHub fetch before final verification and update this document if the source commit changes.
