# User-Facing Browse and Detail Parity Closure Design

## Reference

- Desktop baseline: `8940c11ebba42439e18e0fb8df8a978651d5159c` (`v1.1.92`)
- Authoritative Silo Server/WebUI: `8164fd594b9fdd8c1944bb0b6251f2d00e4a24ca` from public GitHub `main`
- Branch: `codex/user-facing-browse-detail-parity`

## Goal

Complete the remaining non-admin browsing and media-detail experience so the Windows client matches the current Silo WebUI in structure, information hierarchy, interactions, visible state, permissions, and responsive behavior, while retaining native Windows performance and accessibility.

This milestone covers the shared application shell; Home; Search; library and catalog browsing; movie, series, season, episode, person, audiobook, ebook, and manga detail experiences; and the supporting user-facing collection, recommendation, request, calendar, notification, favorite, watchlist, and history surfaces.

## Source of truth

The live signed-in WebUI is the visual and behavioral authority. The public GitHub WebUI source at the pinned commit is the implementation and contract authority. Existing desktop behavior and tests are evidence, not proof of parity. A feature is not complete merely because a route exists, an API call succeeds, or a source-marker test passes.

Before final runtime comparison, fetch public GitHub `main` again. If the reference commit changes, record the new commit and review the changed WebUI surfaces before declaring the milestone complete.

## Scope boundaries

Included:

- Shared navigation rail, expanded sidebar, title bar, page width, spacing, theme, focus, and page-transition behavior.
- Every user-facing state of Home, Search, Catalog, Library, detail, and supporting discovery/account surfaces.
- Media cards, carousels, badges, progress, hover and focus overlays, context menus, dialogs, images, skeletons, empty states, partial states, failures, and permission-specific actions.
- Mouse, keyboard, controller, windowed, ultrawide, 4K, and narrower-window behavior.
- Data refresh, cancellation, incremental reconciliation, image caching, virtualization, and perceived performance.
- Admin-only actions exposed on otherwise user-facing detail pages, displayed only when the active profile is permitted to use them.

Excluded:

- Admin pages themselves.
- New playback transport or decoder work, except player launch/resume actions and shared shell handoff from user-facing pages.
- Publishing, release versioning, GitHub releases, and README release updates until explicitly requested.

## Architecture

### 1. Stable shell and navigation state

Navigation expansion is explicit state, never hover state. Selecting a destination must not collapse or briefly reopen the sidebar. Page content is swapped inside a stable shell without rebuilding the navigation surface. Back navigation, keyboard focus, and controller focus restore to the logical initiating element.

### 2. Explicit page presentation states

User-facing pages distinguish:

- `InitialLoading`: no usable content exists; render the WebUI-equivalent skeleton.
- `Loaded`: current content is usable.
- `Refreshing`: retain current content while updating it; do not blank the page.
- `PartiallyLoaded`: retain successful sections and show a scoped retry for failed sections.
- `Empty`: show the page-specific WebUI empty state and available action.
- `Failed`: show the page-specific WebUI error and retry behavior.
- `PermissionDenied`: omit forbidden actions and show the server-equivalent restricted state where applicable.

Realtime and timer refreshes reconcile existing collections and cards. They must not replace a loaded page with a blank frame.

### 3. Shared media presentation

Cards and rows use shared native components for poster, backdrop, still, audiobook, progress, resolution/HDR/audio badges, watched state, titles, metadata, play affordance, context menu, hover, focus, and skeleton presentation. Variant selection follows the current WebUI component for that surface rather than applying one card shape everywhere.

Images are loaded through the existing `ImageService`, which caches image bytes rather than expiring presigned URLs. All page paths use cancellation, bounded concurrency, appropriate decode dimensions, and thumbhash or themed placeholders. A stale URL is refreshed through current API data rather than persisted as an image identity.

### 4. Responsive and input behavior

Desktop layouts match the WebUI at equivalent viewport widths while adapting cleanly to native title bars and scrollbars. Breakpoints cover narrow windows, ordinary 16:9 desktop windows, 3440x1440 ultrawide, and 4K scaling. Every primary action, carousel, menu, and dialog is reachable with mouse, keyboard, and controller. Focus visuals are visible, deterministic, and restored after closing nested UI.

### 5. Performance rules

- Never synchronously block the UI thread on network, image, or disk work.
- Cancel superseded navigation, search, and image requests.
- Debounce search without interrupting text entry or performing a late full-page rebuild.
- Virtualize large catalogs and bound realized cards and filter items.
- Keep previously rendered Home sections visible during refresh and reconcile only changed items.
- Preserve navigation and scroll state when revisiting a page unless the WebUI intentionally resets it.

## Surface requirements

### Home

Match the server-provided section order, hero rules, Continue Watching, Next Up, recommendation variants, literary rows, server customization, card actions, dismissals, progress, loading, empty, and realtime refresh behavior. No invented actions or labels are allowed.

### Search, Catalog, and Library

Match query semantics, tab/filter behavior, result ordering, applied-filter presentation, sort, paging/virtualization, collection/recommended subroutes, row/grid variants, and no-result behavior. Typing remains focused and responsive. Results update incrementally without a delayed page flash.

### Details

Match the current WebUI for movie, series, season, episode, person, audiobook, ebook, and manga routes: hero, artwork, breadcrumbs, badges, scores, metadata, descriptions, actions, versions, tracks, subtitles, seasons/episodes, cast/crew, trailers, extras, related content, technical information, progress, watched state, and authorized administrative actions. Dialogs and popovers remain inside the current detail context and restore focus when closed.

### Supporting user surfaces

Match Collections and editor flows, Recommendations and section pages, Requests and request detail/browse, Calendar, Notifications, Favorites, Watchlist, and History. Each surface must have the same permissions, actions, states, and navigation destinations as the current WebUI.

## Completion evidence

Completion requires all of the following:

1. Contract and state tests cover each changed behavior and prevent known regressions.
2. The x64 Release build and complete test suite pass.
3. The installed Windows app is compared side by side with the live WebUI using the same server, profile, theme, data, and viewport class.
4. Mouse, keyboard, and controller paths are exercised for primary navigation and nested UI.
5. Large-library stress, repeated navigation, background refresh, partial failures, and expired-image scenarios do not freeze, blank, or visibly rebuild the page.
6. A parity evidence matrix records the reference commit, surfaces, viewport/input coverage, deviations, and screenshots or observations.

Any unverified surface remains explicitly incomplete. Native differences are documented only when a WebUI behavior cannot reasonably exist in WinUI; convenience differences are not silently treated as parity.
