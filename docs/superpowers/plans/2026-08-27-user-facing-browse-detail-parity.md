# User-Facing Browse and Detail Parity Closure Implementation Plan

> Execute on `codex/user-facing-browse-detail-parity`. Do not publish or open a PR unless the user explicitly requests it.

**Goal:** Complete and runtime-verify every remaining non-admin browsing and detail surface against the current Silo WebUI without regressing native performance or stability.

**Architecture:** Establish stable shared shell, presentation-state, media-card, image, and refresh primitives first. Apply those primitives to Home and browse routes, then detail routes and supporting user surfaces. Treat the live WebUI as the visual authority and the pinned public GitHub source as the behavioral contract.

**Tech stack:** .NET 8, WinUI 3, xUnit, libmpv integration at navigation boundaries, public Silo React/WebUI reference, Inno Setup 6 for the final local QA installer.

**Spec:** `docs/superpowers/specs/2026-08-27-user-facing-browse-detail-parity-design.md`

**Execution status (2026-08-27):** Tasks 1-6 are implemented and covered by the 964-test Release suite. Task 7 includes a direct authenticated live-WebUI pass and desktop startup/log verification; interactive desktop visual comparison remains explicitly open because the Windows-control provider selected an unrelated fullscreen surface for the exact Silo process selector, so that attempt was stopped without disturbing the user's windows. Task 8 automated verification and packaging are complete: official GitHub `main` remained at `8164fd594b9fdd8c1944bb0b6251f2d00e4a24ca`, the x64 Release build completed with zero warnings and errors, and the established multi-file installer pipeline produced `SiloInstaller-1.1.92-Setup.exe`. Installed-app runtime checks remain explicitly pending user QA.

---

## Task 1: Capture the current parity contract and regression baseline

**Files:**
- Create: `docs/parity/user-facing-parity-8164.md`
- Modify: `tests/SiloPlayer.Tests/CurrentHomeParitySourceTests.cs`
- Modify: `tests/SiloPlayer.Tests/CurrentCatalogParitySourceTests.cs`
- Modify: `tests/SiloPlayer.Tests/CurrentLibraryParitySourceTests.cs`
- Modify: `tests/SiloPlayer.Tests/ItemDetailCurrentParityTests.cs`

- [x] Record every authoritative WebUI route, component, state, and permission branch at commit `8164fd594b9fdd8c1944bb0b6251f2d00e4a24ca`.
- [ ] Add failing contract tests for current server fields and interactions that the desktop does not yet represent.
- [ ] Run `dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --filter "FullyQualifiedName~CurrentHomeParitySourceTests|FullyQualifiedName~CurrentCatalogParitySourceTests|FullyQualifiedName~CurrentLibraryParitySourceTests|FullyQualifiedName~ItemDetailCurrentParityTests"` and confirm only the new expectations fail.
- [ ] Update desktop models and contract handling only where the pinned WebUI proves a gap.
- [ ] Run the focused tests to green and update the parity matrix with implementation locations.

## Task 2: Stabilize shared shell, navigation, page state, and images

**Files:**
- Modify: `src/SiloPlayer/MainWindow.xaml`
- Modify: `src/SiloPlayer/MainWindow.xaml.cs`
- Modify: `src/SiloPlayer/Helpers/NavigationService.cs`
- Modify: `src/SiloPlayer/Helpers/PageTransitionHelper.cs`
- Modify: `src/SiloPlayer.Core/Services/ImageService.cs`
- Modify: `src/SiloPlayer/Converters/UrlToImageSourceConverter.cs`
- Modify: `src/SiloPlayer/Controls/PosterCard.xaml.cs`
- Modify: `src/SiloPlayer/Controls/LandscapeCard.xaml.cs`
- Modify: `src/SiloPlayer/Controls/BackdropImage.xaml.cs`
- Test: `tests/SiloPlayer.Tests/NavigationResourceRegressionTests.cs`
- Test: `tests/SiloPlayer.Tests/ImageServiceTests.cs`

- [ ] Add failing tests for explicit sidebar persistence, navigation-state restoration, expired image URL replacement, byte-cache reuse, cancellation, and duplicate concurrent image loads.
- [ ] Run `dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --filter "FullyQualifiedName~NavigationResourceRegressionTests|FullyQualifiedName~ImageServiceTests"` and confirm the intended failures.
- [ ] Remove hover-driven sidebar state and page-triggered collapse/reopen paths.
- [ ] Preserve the shell while navigating and restore focus to the initiating element after back navigation or dialog closure.
- [ ] Route all shared card/backdrop remote images through `ImageService` with bounded work, decode sizing, cancellation, and placeholders.
- [ ] Add explicit initial-loading, refreshing, partial, empty, failed, and permission-denied presentation primitives without blanking loaded content.
- [ ] Run focused tests to green, then run the complete test suite.

## Task 3: Complete Home parity and refresh behavior

**Files:**
- Modify: `src/SiloPlayer/ViewModels/HomeViewModel.cs`
- Modify: `src/SiloPlayer/Views/HomePage.xaml`
- Modify: `src/SiloPlayer/Views/HomePage.xaml.cs`
- Modify: `src/SiloPlayer/Controls/HeroCarousel.xaml.cs`
- Modify: `src/SiloPlayer/Controls/SectionRow.xaml.cs`
- Modify: `src/SiloPlayer/Controls/NowListeningHero.xaml.cs`
- Modify: `src/SiloPlayer/Controls/AudiobookSquareCard.xaml.cs`
- Test: `tests/SiloPlayer.Tests/CurrentHomeParitySourceTests.cs`
- Test: `tests/SiloPlayer.Tests/HomeHeroCurrentParityTests.cs`
- Test: `tests/SiloPlayer.Tests/HomeRealtimeRefreshGateTests.cs`
- Test: `tests/SiloPlayer.Tests/HomeSectionReconcilerTests.cs`

- [ ] Add failing tests for current WebUI section variants, ordering, hero eligibility, dismissals, realtime reconciliation, partial failures, refresh-without-blanking, and removal of completed Next Up items.
- [ ] Run the four focused Home test classes and confirm the new regression cases fail.
- [ ] Match the current WebUI section/card variants and remove invented labels or actions.
- [ ] Reconcile section and item changes in place while preserving hero, carousel, focus, and scroll state.
- [ ] Ensure progress and newly scanned content update while Home remains active without a full page rebuild.
- [ ] Match skeleton, empty, partial-error, context-menu, mouse, keyboard, controller, and responsive states.
- [ ] Run focused and complete tests to green.

## Task 4: Complete Search, Catalog, and Library parity and performance

**Files:**
- Modify: `src/SiloPlayer/ViewModels/SearchViewModel.cs`
- Modify: `src/SiloPlayer/Controls/GlobalSearchDialog.xaml.cs`
- Modify: `src/SiloPlayer/Views/SearchPage.xaml`
- Modify: `src/SiloPlayer/Views/SearchPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/CatalogPage.xaml`
- Modify: `src/SiloPlayer/Views/CatalogPage.xaml.cs`
- Modify: `src/SiloPlayer/ViewModels/LibraryViewModel.cs`
- Modify: `src/SiloPlayer/Views/LibraryPage.xaml`
- Modify: `src/SiloPlayer/Views/LibraryPage.xaml.cs`
- Modify: `src/SiloPlayer/Helpers/VirtualCatalogItems.cs`
- Modify: `src/SiloPlayer/Controls/LibraryGridCard.cs`
- Test: `tests/SiloPlayer.Tests/SearchRuntimeRegressionTests.cs`
- Test: `tests/SiloPlayer.Tests/GlobalSearchInteractionParitySourceTests.cs`
- Test: `tests/SiloPlayer.Tests/CurrentCatalogParitySourceTests.cs`
- Test: `tests/SiloPlayer.Tests/CurrentLibraryParitySourceTests.cs`

- [ ] Add failing tests for input focus retention, cancellation of obsolete searches, no late result-page rebuild, current query semantics, type tabs, ordering, filters, sort, no-result state, and back/scroll restoration.
- [ ] Run the focused Search/Catalog/Library tests and confirm the intended failures.
- [ ] Make the direct user search path issue one debounced current request, cancel stale requests, and reconcile results without replacing the page.
- [ ] Match WebUI search field geometry, tab highlight, filter chips/panels, sort, card variants, collection/recommended routes, and responsive layout.
- [ ] Preserve the bounded virtualized library grid and cap large filter option construction without changing server semantics.
- [ ] Stress the 100,000-item library path using existing instrumentation and verify no UI-thread waits or unbounded card/image realization.
- [ ] Run focused and complete tests to green.

## Task 5: Complete media and person detail parity

**Files:**
- Modify: `src/SiloPlayer/ViewModels/ItemDetailViewModel.cs`
- Modify: `src/SiloPlayer/Views/ItemDetailPage.xaml`
- Modify: `src/SiloPlayer/Views/ItemDetailPage.xaml.cs`
- Modify: `src/SiloPlayer/ViewModels/PersonDetailViewModel.cs`
- Modify: `src/SiloPlayer/Views/PersonDetailPage.xaml`
- Modify: `src/SiloPlayer/Views/PersonDetailPage.xaml.cs`
- Modify: `src/SiloPlayer/Controls/MediaItemMenu.xaml.cs`
- Modify: `src/SiloPlayer/Controls/AddToCollectionDialog.xaml.cs`
- Modify: `src/SiloPlayer/Controls/MatchItemDialog.xaml.cs`
- Modify: `src/SiloPlayer/Controls/EditMetadataDialog.xaml.cs`
- Modify: `src/SiloPlayer/Controls/RefreshMetadataDialog.xaml.cs`
- Test: `tests/SiloPlayer.Tests/ItemDetailCurrentParityTests.cs`
- Test: `tests/SiloPlayer.Tests/EpisodeNavigationStateTests.cs`

- [ ] Add failing tests for movie, series, season, episode, person, audiobook, ebook, and manga variants; admin-action visibility; resume; seasons/episodes; extras; trailers; tracks; versions; technical information; related rows; progress; watched state; loading; and failure states.
- [ ] Run the focused detail tests and confirm the new expectations fail.
- [ ] Match `DetailHero`, breadcrumbs, badges, scores, crew, action bar, version and track popovers, media information, season/episode structures, cast, extras, trailers, and recommendations from the pinned WebUI.
- [ ] Make authorized administrative dialogs match WebUI behavior while keeping them in the current detail context and restoring focus on close.
- [ ] Add visible arrow and drag/controller navigation to every horizontally scrollable detail row, including More Episodes.
- [ ] Preserve detail state and selected season when returning from playback, a person, an extra, or a nested dialog.
- [ ] Run focused and complete tests to green.

## Task 6: Complete remaining user-facing discovery and account surfaces

**Files:**
- Modify: `src/SiloPlayer/Views/CollectionsPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/CollectionEditorPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/CollectionBrowsePage.xaml.cs`
- Modify: `src/SiloPlayer/Views/RecommendationsPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/RecommendationSectionPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/RequestsPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/RequestBrowsePage.xaml.cs`
- Modify: `src/SiloPlayer/Views/RequestDetailPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/CalendarPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/NotificationsPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/FavoritesPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/WatchlistPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/HistoryPage.xaml.cs`
- Test: `tests/SiloPlayer.Tests/CurrentCollectionsParitySourceTests.cs`
- Test: `tests/SiloPlayer.Tests/RequestsCurrentParitySourceTests.cs`
- Test: `tests/SiloPlayer.Tests/RequestBrowseCurrentParitySourceTests.cs`
- Test: `tests/SiloPlayer.Tests/RequestDetailCurrentParitySourceTests.cs`
- Test: `tests/SiloPlayer.Tests/RecommendationSectionCurrentParitySourceTests.cs`
- Test: `tests/SiloPlayer.Tests/CalendarCurrentParitySourceTests.cs`
- Test: `tests/SiloPlayer.Tests/NotificationsCurrentParitySourceTests.cs`

- [ ] Add failing tests for every current route, action, permission branch, state, and destination absent from the desktop surfaces.
- [ ] Run the focused discovery/account parity tests and confirm the new expectations fail.
- [ ] Match the current WebUI layouts, menus, dialogs, filters, badges, states, and navigation for each listed surface.
- [ ] Replace remaining direct presigned-image bindings on these user-facing pages with the shared cached image path.
- [ ] Verify notification/activity count geometry cannot clip at one-, two-, or three-digit values and common DPI scales.
- [ ] Run focused and complete tests to green.

## Task 7: Runtime parity verification and performance closure

**Files:**
- Modify: `docs/parity/user-facing-parity-8164.md`
- Modify: `README.md` only to correct the development parity table if runtime evidence changes its status; do not publish a release.

- [ ] Warn the user immediately before taking Windows control and leave existing app/browser window sizes and placement unchanged.
- [ ] Compare the live WebUI and installed desktop app on the same server, admin profile, theme, and data at narrow, 1080p-class, 3440x1440, and 4K/DPI-scaled layouts.
- [ ] Exercise mouse, keyboard, controller, context-menu, dialog, back-navigation, refresh, empty, partial-failure, expired-image, and permission-specific paths.
- [ ] Exercise repeated rapid navigation, repeated searches, Home background updates, 100,000-item library scrolling, detail-page nested navigation, and return from playback.
- [ ] Review `%LOCALAPPDATA%\SiloPlayer\ui_lag.txt` and runtime logs; fix demonstrated regressions with a failing test before implementation.
- [ ] Record observed parity, screenshots, known deviations, and exact WebUI commit in the evidence matrix. Leave unverified entries incomplete.

## Task 8: Final local QA build and verification

**Files:**
- Modify only files required by defects discovered during final verification.
- Generate ignored output: `installer/output/SiloInstaller-<version>-Setup.exe`

- [x] Fetch official Silo GitHub `main` again and review any commits after `8164fd594b9fdd8c1944bb0b6251f2d00e4a24ca` (no newer commit as of the final fetch).
- [x] Run `dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --no-restore` (964/964 passed after the latest review fixes).
- [x] Run the x64 Release publish through the established `installer/build.ps1` pipeline (zero warnings and errors).
- [x] Build the installer through the established `installer/build.ps1` Inno Setup pipeline (`SiloInstaller-1.1.92-Setup.exe`, SHA256 `50398E2229B8DCE15A0DBE04AD03BE4795194AE45019C2290923DEE10AB95C06`). Earlier test counts and installer hashes are superseded by this latest post-review build.
- [ ] Install and launch the QA build, repeat the high-risk runtime checks, and confirm Smart App Control packaging follows the known working installer layout.
- [x] Review the complete diff and working tree for unrelated files, secrets, generated outputs, and accidental Claude/Codex artifacts (only the reviewed remediations, their regression tests, and verification evidence are included).
- [ ] Report exactly what was runtime-verified, what remains unverified, the QA installer path, and a concise test checklist for the user.
