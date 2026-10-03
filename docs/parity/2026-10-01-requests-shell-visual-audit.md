# Requests and shared shell comparison — October 1, 2026

## Reference and evidence boundary

Official GitHub main was fetched directly on October 1: [`8e2e840474a085c6df6571a5a2850f7eb996810c`](https://github.com/Silo-Server/silo-server/commit/8e2e840474a085c6df6571a5a2850f7eb996810c). The reference checkout was not changed; comparisons read that exact Git object. Desktop source is the uncommitted **1.2.0** candidate in `C:\Users\Michael\.codex\worktrees\parity-top-three\SiloPlayer`, not released main or an assumed installed version.

This report establishes **source-confirmed differences**. It does not certify rendered parity. Native computer surfaces are unavailable in this session and browser discovery showed no existing WebUI tabs. No signed-in paired screenshots were captured. The prior native request fixture proves poster visibility, Back history and a Season 2 submission, not visual matching of the page.

The requirement remains every non-admin page, menu, button and behavior matching the current WebUI. Native implementation differences are not automatic exceptions to that requirement.

## Coverage ledger

| Surface | Official source | Native source | Evidence |
|---|---|---|---|
| Requests hub, search entry, Discover/Yours tabs, count, loading/error/empty states | `pages/Requests.tsx` | `Views/RequestsPage.xaml(.cs)`, `ViewModels/RequestsViewModel.cs` | Source reviewed; several confirmed differences below; rendered acceptance outstanding |
| Discover rails and Explore links | `Requests.tsx`, `MediaCarousel.tsx`, `RequestPosterCard.tsx` | `RequestsPage.BuildDiscovery`, `BuildMediaPosterCard` | Source reviewed; carousel details need paired render and input checks |
| Studio/network/genre cards | `BrandCarousel.tsx`, `BrandCard.tsx` | `RequestsPage.BuildBrandRow` | Source reviewed; brand geometry/genre gradient differ; rendered acceptance open |
| Discovery-section and studio/network/genre full grids | `RequestDiscoverSection.tsx`, `RequestBrowse.tsx`, `RequestResultsGrid.tsx` | `RequestBrowsePage.xaml(.cs)` | Source reviewed; paging, grid/card and controls differ |
| Search/discovery request-card artwork, primary action, status, Library and Watchlist actions | `RequestPosterCard.tsx`, `MediaCardArtwork.tsx` | `RequestsPage.BuildMediaPosterCard`, `RequestBrowsePage` template | Source reviewed; button and missing corner action confirmed |
| Yours grouping, row detail, dates, download state, status help, cancel | `Requests.tsx`, `RequestDownloadProgress.tsx`, `CancelRequestDialog.tsx` | `BuildMyRequests`, `BuildRequestPosterCard`, `StatusGuide`, cancellation handlers | Source reviewed; structure, help and confirmation differ |
| External title detail, library promotion, transient/not-found states | `TitleDetail.tsx`, `ExternalTitleContent.tsx`, `DetailHero.tsx` | `RequestDetailPage.xaml(.cs)`, `ExternalTitleResolution` | Source reviewed; promotion repair already has native regression evidence; visual and error-state acceptance still open |
| Request/follow/watchlist/cancel/detail action bar | `ItemDetail/components/RequestActionBar.tsx`, shared `ActionBar.tsx` | `RequestDetailPage.BuildActions` | Source reviewed; text buttons and download presentation differ |
| Season picker and external-title season rail | `RequestSeasonsDialog.tsx`, `ExternalTitleContent.tsx` | `Views/Dialogs/RequestSeasonsDialog.cs`, `RequestDetailPage.BuildSeasons` | Source reviewed; native Season 2 fixture exists; control structure/copy differ |
| External-title cast/recommendation rails | `ExternalTitleContent.tsx`, shared cast/recommendation controls | `RequestDetailPage.BuildCast`, `BuildRecommendations` | Native source reviewed; render/interaction acceptance open |
| Shell rail/expanded/sidebar/mobile header and drawer | `AppSidebar.tsx`, `AppSidebar.logic.ts`, `Layout.tsx` | `MainWindow.xaml(.cs)` | Source reviewed; 260px/64px geometry and hover logic already present; whole shell not certified visually |
| Profile menu, server theme, navigation footer | `AppSidebar.tsx` | `MainWindow.xaml(.cs)`, `ThemeService.cs` | Source reviewed; profile menu structure and retired-theme residue differ |
| Shared fonts/brushes/control templates/focus/toasts | `app.css`, `components/ui/*` | `Themes/DarkTheme.xaml`, `ThemeService`, `ToastContainer` | Existing shared theme/font work acknowledged; resolved runtime colors, Fluent template metrics, focus, shadows and animations still require render comparison |

## Confirmed differences and functional consequences

Priority **P1** means a missing interaction or a major page/workflow difference. **P2** means a concrete presentation/state mismatch. Priorities order repair impact, not permission to omit smaller details.

### RQ-01 — P1: Requests hub is a different composition

WebUI uses `Requests`, 24px/30px responsive heading, one short explanatory sentence and a 288px search field beside it. Native uses `YOUR WISHLIST`, a 40px `Find something worth waiting for.` hero and a separate bordered type/search/submit panel. The native search remains on Requests; WebUI routes to shared app search with Request to add. This affects both visual hierarchy and navigation.

Evidence: [Requests.tsx:125](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Requests.tsx#L125); native `Views/RequestsPage.xaml:47`, `RequestsPage.xaml.cs:69`.

### RQ-02 — P2: Tabs stretch to half the page

WebUI line tabs are left-aligned, intrinsic-width Discover and Yours triggers (`flex-none px-3`). Native puts each button in a star-width column with centered content. A native comment claiming that the current WebUI divides the full width evenly is wrong for the pinned source.

Evidence: [Requests.tsx:147](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Requests.tsx#L147); native `Views/RequestsPage.xaml:88`.

### RQ-03 — P1: Yours is poster rails instead of compact rows

WebUI groups requests into Needs attention, On the way, In your library and Cancelled, in that order, using separated list rows with 48×72px artwork. Native has summary chips and horizontally scrolling large poster cards under In motion, Landed in your library and Needs attention. Cancelled is mixed with failures and attention appears last.

Evidence: [Requests.tsx:55](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Requests.tsx#L55), [RequestRow:386](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Requests.tsx#L386); native `Views/RequestsPage.xaml.cs:166` and `:533`.

### RQ-04 — P2: Status help is an always-visible panel

WebUI has a `What the statuses mean` Info button opening a compact status-description popover. Native shows a large multi-column StatusGuide panel. The copy and status presentation also need migration to current display states.

Evidence: [Requests.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Requests.tsx); native `Views/RequestsPage.xaml:137`.

### RQ-05 — P2: Request button location and styling

WebUI Request is centered on the artwork, 36px high, theme-primary filled, fully rounded, with a 14px Lucide Plus, 12px semibold text, a shadow and hover/press scaling. Native places it at the bottom of the poster with a 12px inset, uses text `+  Request` and an ordinary Button with radius 16. This is the user's reported visual mismatch in both hub cards and browse grids.

Evidence: [RequestPosterCard.tsx:324](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestPosterCard.tsx#L324), [MediaCardArtwork.tsx:18](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MediaCardArtwork.tsx#L18); native `Views/RequestsPage.xaml.cs:384`, `Views/RequestBrowsePage.xaml:65`.

### RQ-06 — P1: Request cards lack the watchlist corner toggle

WebUI card grids and discovery cards offer the capability-gated bottom-right bookmark toggle with pressed/pending/tooltip states. Native hub and browse cards expose Request and Library but no equivalent watchlist corner control. Detail-page watchlist support does not satisfy the card interaction.

Evidence: [RequestPosterCard.tsx:355](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestPosterCard.tsx#L355), [RequestResultsGrid.tsx:30](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestResultsGrid.tsx#L30); native `Views/RequestsPage.xaml.cs:302`, `Views/RequestBrowsePage.xaml:43`.

### RQ-07 — P2: Pending Request state can disappear on pointer exit

WebUI keeps a pending card action visible and displays spinner plus Sending. Native PointerExited sets opacity to zero unless the button retains keyboard focus; pending is not part of that condition. Native hub changes text to Sending… but supplies no spinner. Verify with delayed requests and pointer exit, including failure and simultaneous different-card submissions.

Evidence: [RequestPosterCard.tsx:333](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestPosterCard.tsx#L333); native `Views/RequestsPage.xaml.cs:396`, `:700`; browse `RequestBrowsePage.xaml.cs:148`.

### RQ-08 — P2: Card captions differ

WebUI shows 14px semibold title with 12px top caption padding, 4px inset, then uppercase tracked `Movie/Series · year`, without the native star rating. Native hub title is 13px, rows spaced 6px, metadata is `year · Series/Movie · ★ rating`, and title/meta are under the same full-card hit target rather than separate hover-underlined links.

Evidence: [MediaCardArtwork.tsx:7](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MediaCardArtwork.tsx#L7), [RequestPosterCard.tsx:264](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestPosterCard.tsx#L264); native `Views/RequestsPage.xaml.cs:328`.

### RQ-09 — P2: Artwork treatment and missing-art state differ

WebUI uses rounded-xl clipping, a 96px bottom black gradient, dimming for non-requestable titles, fade-in and centered title fallback on missing/error artwork. Native hub creates an Image in a radius-8 Border without an equivalent scrim/dimming/fallback label or error fallback. A Border corner radius alone is not proof the child image is clipped in native WinUI; include that in rendered verification rather than assuming it.

Evidence: [MediaCardArtwork.tsx:21](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MediaCardArtwork.tsx#L21), [RequestPosterCard.tsx:278](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestPosterCard.tsx#L278); native `Views/RequestsPage.xaml.cs:308`.

### RQ-10 — P2: Status colors/icons are legacy hard-coded ribbons

WebUI uses theme variants: pending outline, approved/processing/partial secondary, available primary, failed destructive, declined/cancelled muted outline. Native uses hard-coded amber/sky/emerald/zinc, uppercase labels and a dot. Several hub conditions use raw Status instead of the explicit current State; verify `partially_available`/failed and raw completed-but-not-scanned input.

Evidence: [RequestStatusBadge.tsx:16](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestStatusBadge.tsx#L16); native `Views/RequestsPage.xaml.cs:317`, `:402`, `:443`, and browse `ApplyStatusRibbon`.

### RQ-11 — P2: Card size/caption preferences are not consumed by Requests

WebUI request cards use shared poster-size and caption preferences and responsive grid/rail width helpers. Native Requests owns `_requestCardWidth`, while browse templates hard-code 184px posters/196px cells; neither card builder consumes the shared `cardPresentation` size/caption setting. Native has some width reflow, but that does not implement the preference contract.

Evidence: [RequestPosterCard.tsx:262](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestPosterCard.tsx#L262), [uiCustomization.ts](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/lib/uiCustomization.ts); native `Views/RequestsPage.xaml.cs:22`, `Views/RequestBrowsePage.xaml:40`.

### RQ-12 — P2: Brand rails have different tile geometry and controls

WebUI BrandCarousel provides responsive skeletons, drag/keyboard left-right navigation and hover/focus edge chevrons. BrandCard tiles are 208×112px, increasing to 256×128px at sm; genres use their API-provided gradient, while studio/network tiles use the defined gray surface, ring and logo padding. Native is an ordinary horizontal ScrollViewer with 154×82px solid-surface buttons and visible horizontal scrollbars. Match each actual Studio/Network/Genre tile variant, not just its link target.

Evidence: [BrandCarousel.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/BrandCarousel.tsx), [BrandCard.tsx:32](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/BrandCard.tsx#L32); native `Views/RequestsPage.xaml.cs:448`.

### RQ-13 — P1: Browse paging is a different interaction

WebUI studio/network/genre and discover grids append next pages with an intersection sentinel, appended skeletons and a retry footer (Load more fallback where observers are unavailable). Native replaces the grid page and has fixed bottom Prev/Next controls. Genre media type is WebUI tabs versus native ComboBox; sort widths/copy/back controls also differ.

Evidence: [RequestResultsGrid.tsx:97](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestResultsGrid.tsx#L97), [RequestBrowse.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/RequestBrowse.tsx); native `Views/RequestBrowsePage.xaml:36`, `:87`, `RequestBrowsePage.xaml.cs:56`.

### RQ-14 — P1: Cancellation bypasses the confirmation dialog

WebUI hub and title actions open Cancel this request?, naming the title and offering destructive Cancel request versus Keep request. Native hub and external-title handlers call cancellation directly. Preserve the existing ownership/cancellability policy, but add the missing confirmation surface and pending states.

Evidence: [CancelRequestDialog.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/CancelRequestDialog.tsx), [RequestActionBar.tsx:154](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/components/RequestActionBar.tsx#L154); native `Views/RequestsPage.xaml.cs:714`, `Views/RequestDetailPage.xaml.cs:194`.

### RQ-15 — P2: External title actions and automatic-request explanation differ

WebUI reuses the library action-bar visual structure, icon states and secondary icon buttons. Native uses a WrapPanel of labeled buttons/status pills and exposes IMDb/TMDB as more inline buttons. The WebUI automatic-watchlist-request warning is visible text with a Settings › Requests link; native provides only a tooltip.

Evidence: [RequestActionBar.tsx:136](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/components/RequestActionBar.tsx#L136), [RequestActionBar.tsx:191](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/components/RequestActionBar.tsx#L191); native `Views/RequestDetailPage.xaml.cs:129`.

### RQ-16 — P2: Download progress is flattened into text

WebUI Yours rows and external-title actions include shared RequestDownloadProgress with progress/phase presentation. Native concatenates DownloadLabel into card text and has a DownloadText TextBlock in detail. Phase data being present does not supply the progress visualization.

Evidence: [RequestActionBar.tsx:203](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/components/RequestActionBar.tsx#L203), [RequestDownloadProgress.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestDownloadProgress.tsx); native `Views/RequestDetailPage.xaml.cs:179`, `Views/RequestsPage.xaml.cs:605`.

### RQ-17 — P1/P2: Season picker controls and zero-selection contract differ

WebUI uses switches, an All seasons switch, separate season title/meta/status columns, conditional Latest season/Upcoming seasons shortcuts, description and selection-count action text. Native uses single-line CheckBoxes, always-present Missing aired/Latest/Upcoming buttons and fixed Request selected text; no All seasons control. Native requires nonempty defaults/selection, while WebUI represents whole-series requests for a not-yet-aired external series when no specific seasons are picked. This last behavioral difference needs a focused fixture covering upcoming-only external series before repair; do not extrapolate from the successful Season 2 test.

Evidence: [RequestSeasonsDialog.tsx:127](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestSeasonsDialog.tsx#L127), [RequestSeasonsDialog.tsx:148](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestSeasonsDialog.tsx#L148); native `Views/Dialogs/RequestSeasonsDialog.cs:24`.

### RQ-18 — P2: Season rail status copy differs

WebUI SeasonStatus uses In library, Requested, Partly in library, Not aired yet, Not announced, and no missing label for aired-unavailable seasons. Native displays Available, Requested, Partially available, Missing and Upcoming. Verify air-date and episode-count combinations, including specials.

Evidence: [RequestSeasonsDialog.tsx:253](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestSeasonsDialog.tsx#L253); native `Views/RequestDetailPage.xaml.cs:246`.

### RQ-19 — P2: External title hero uses an independent layout

WebUI shares DetailHero/DetailLayout with library items. Native request details have an independent 620px-minimum hero, 210×315 poster, 54px gutters, 42px title and hard-coded #0B0C0F gradients. These fixed values are not derived from the same responsive/theme tokens as the reference hero. Paired rendering must enumerate precise differences at each width/theme rather than declaring this visually matched because both have a poster and backdrop.

Evidence: [ExternalTitleContent.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/ExternalTitleContent.tsx), [DetailHero.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/DetailHero.tsx); native `Views/RequestDetailPage.xaml:11`.

### RQ-20 — P2: Empty/error/skeleton surfaces differ

WebUI uses compact unbordered Yours empty/error content with Retry, row skeletons for Yours and carousel skeletons for Discover. Native shows bordered wishlist/large status-guide panels, a shared poster loading skeleton and text telling viewers to refresh later in MineErrorPanel without an inline Retry button. Match copy and retry scope as well as geometry.

Evidence: [Requests.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Requests.tsx); native `Views/RequestsPage.xaml:109`, `:144`.

### RQ-21 — P1: Grid series Request behavior differs

WebUI RequestResultsGrid submits `requestInputFromMediaResult(item)` using useSubmitMediaRequest. Native grid/hub series buttons navigate to title detail instead of submitting the card request. This is distinct from title-detail Request series, which opens a season picker when season data exists. Verify the reference's actual series request payload before altering this behavior; the two entry points must not be conflated.

Evidence: [useSubmitMediaRequest.ts:17](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/hooks/useSubmitMediaRequest.ts#L17); native `Views/RequestsPage.xaml.cs:702`, `Views/RequestBrowsePage.xaml.cs:151`.

### RQ-22 — P2: External-title recommendations use another card design

WebUI reuses RequestPosterCard in MoreLikeThisRow, including centered Request, watchlist corner action, shared card preferences and captions. Native builds fixed 142px cards wrapped in GhostButtons with separate full-width Library/Request buttons below them. Fixing the hub's overlay alone leaves this third card presentation different.

Evidence: [ExternalTitleContent.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/ExternalTitleContent.tsx); native `Views/RequestDetailPage.xaml.cs:311`.

### SH-01 — P2: Shell uses different icon artwork

WebUI uses Lucide line icons with explicit 18px sidebar slots and consistent stroke shapes. Native uses Segoe FontIcon glyphs in 18px slots. Matching the slot size does not match the glyph shape/stroke. Create an icon-by-icon ledger for navigation, page Back, card/library/request/bookmark and all menus; do not accept an arbitrary system glyph as identical.

Evidence: [AppSidebar.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/AppSidebar.tsx); native `MainWindow.xaml:55` and menu definitions.

### SH-02 — P2: Profile dropdown geometry and grouping differ

WebUI profile dropdown is up to 320px wide, radius 16, padding 8, header avatar 40px, separator, Settings, separator, then bordered Switch Profile and Logout rows. Native custom Flyout uses a different presenter/header/avatar and button hierarchy. Native keeps a THEME label/grid after BuildThemeDots hides only its dots, even though profile theme selection is retired in current WebUI.

Evidence: [AppSidebar.tsx:1018](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/AppSidebar.tsx#L1018); native `MainWindow.xaml:327`, `:375`, `MainWindow.xaml.cs:3035`.

### SH-03 — P2: Page Back controls differ

WebUI PageBack is a circular glass ChevronLeft with 6px padding, specific responsive offsets and known-up/cold-entry navigation rules. Native Requests browse uses an ordinary Button; external details use a 44×44 Button inset 24px. The recently repaired Back redirect behavior is acknowledged, but does not make the control itself visually identical.

Evidence: [PageBack.tsx:33](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/PageBack.tsx#L33); native `Views/RequestBrowsePage.xaml:22`, `Views/RequestDetailPage.xaml:17`.

## Acceptance work required for every finding

- Use identical server response fixtures, title images, selected profile/capabilities, theme and interface settings on both sides. Record the source commit and exact candidate executable.
- Capture equal content viewport dimensions, separately recording native DPI versus browser zoom. Include narrow/medium/wide layouts at 100%, 150% and 200% Windows scaling. Native minimum window size is a measured constraint, not an undocumented parity exemption.
- Compare normal, hovered, pressed, keyboard focused, disabled, pending, selected, open-menu, error, empty, missing-image and stale-data states. Include long titles, unavailable-library copies, all request states and partially available seasons.
- Exercise every action from its actual entry point: hub, search, browse, recommendation card, external watchlist, notification and cold entry. Record mutations on isolated fixtures rather than creating real requests during visual tests.
- Require image/geometry evidence and actual event/payload/navigation checks before changing any row from source-reviewed to rendered/functional accepted. No source review, control count or automated-test total establishes 100% parity.

No application code, installed app, credentials or production data was changed by this audit.
