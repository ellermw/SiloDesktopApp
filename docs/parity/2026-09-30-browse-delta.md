# Browse, discovery and requests delta — September 30, 2026

Bounded source review of official Silo GitHub objects from
[`ad899be9d4fd9f33d4b9e9ac6873166026661c6d`](https://github.com/Silo-Server/silo-server/commit/ad899be9d4fd9f33d4b9e9ac6873166026661c6d)
to the coordinating audit's freshly fetched
[`8e2e840474a085c6df6571a5a2850f7eb996810c`](https://github.com/Silo-Server/silo-server/commit/8e2e840474a085c6df6571a5a2850f7eb996810c).
Read actual `git diff`/`git show` code without moving the reference checkout.
Desktop checked: the uncommitted 1.1.106 candidate at
`C:\Users\Michael\.codex\worktrees\parity-top-three\SiloPlayer`; released
`D:\SiloPlayer` remains 1.1.105. No implementation, build, tests, installed-app
interaction, live API requests, credentials or production access occurred.
Source findings do not establish deployed behavior or visual parity. Previously
completed packages 1–3 are preserved; none is reopened here.

## New package: title requests and external watchlists (large)

The September 28 list needs a substantial viewer package spanning existing
Requests, Search, item detail and Watchlist. Native already has Requests
discovery/search, brand browse, request detail, submission, cursor-paged mine
and catalog-item Watchlist. Those are foundations to extend, not missing whole
features.

### Selectable seasons and requests for an existing series

Current request detail supplies regular `seasons[]` with `availability`
(`missing`, `partial`, `available`) and `requested`; create accepts optional
`seasons`. Omission deliberately lets the server select missing aired seasons,
or request a whole external series when none has aired. WebUI has a season picker
and a capability-gated **Request seasons** action on catalog series detail.
This is also how users request upcoming seasons of an existing series.
[Request contract](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/internal/apiv2/requests.go),
[picker](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RequestSeasonsDialog.tsx),
[series action](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/SeriesContent.tsx).

Candidate [MediaRequests.cs](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer.Core/Models/Requests/MediaRequests.cs)
contains only season counts; request detail and input lack `Seasons`, and
[RequestDetailPage.xaml.cs:135](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/RequestDetailPage.xaml.cs:135)
always submits without a selection. Current [ItemDetailPage.xaml.cs](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/ItemDetailPage.xaml.cs)
has catalog season browsing but no request-seasons action. The native default
request remains meaningful and compatible; the missing behavior is choosing
specific/latest/upcoming seasons and requesting missing seasons from catalog
detail. Respect both `season_requests_supported` and
`missing_seasons_requestable`; the latter depends on active router-plugin
capability, so it must not be inferred from a series existing.
[Status contract](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/internal/apiv2/request_lifecycle.go).

### External Watchlist titles and optional auto-request

Upstream adds `GET /api/v2/watchlist/titles` (cursor paging), PUT/DELETE by
`{media_type}/{tmdb_id}`, `in_watchlist` on discovery/detail, and **In library /
Not in library** Watchlist tabs. Adds resolve to an existing catalog entry when
appropriate, otherwise retain an external title. Entries can need review or be
removed after provider identity changes. Adding can also request/follow the
title, according to the effective `watchlist_requests` flag and profile setting
`requests.watchlist_auto_request`; the preference defaults on and can opt out.
[Title API](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/api/v2/watchlistTitles.ts),
[tabs and identity handling](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/lib/watchlistTitles.ts),
[preference](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/settings/RequestsSettings.tsx).

Candidate [WatchlistViewModel.cs](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/WatchlistViewModel.cs)
loads only `_catalogApi.GetWatchlistAsync`; no candidate C# consumer of
`/watchlist/titles`, the preference, or `in_watchlist` was found. Add title
DTO/API support, tabs and complete paging, watchlist toggles on existing
discovery/detail surfaces, and truthful action results (added, requested,
following, blocked/quota reason). Keep the catalog Watchlist path. Gate the new
surface with `watchlist_titles_supported`; requests disabled retains stored
entries but disables these routes. Reconcile profile switches, realtime changes,
external-to-library promotion, identity-review states and removals.

### Request state, following and fulfillment information

The server now supplies viewer-facing `state` (including `processing`,
`partially_available`, `available`), `outcome_reason`, requested `seasons`,
`season_progress`, optional request/target `download`, and title-state
`following` / `requested_by_viewer`. PUT/DELETE
`/api/v2/requests/follows/{media_type}/{tmdb_id}` supports notification interest
in someone else's active request. Raw integration identities/errors are now
admin-only. [Actual mapper and routes](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/internal/apiv2/requests.go),
[WebUI state/withdrawal rules](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/lib/mediaRequests.ts).

Native DTOs discard those fields; [RequestsApi.cs](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer.Core/Api/RequestsApi.cs)
has no follow operations. [RequestsPage.xaml.cs](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/RequestsPage.xaml.cs)
groups by raw status/outcome, displays `LastError` and enables Cancel for every
active outcome. Align this existing UI with authoritative `state`, outcome
reason, partial season fulfillment and optional download phases/unknown-size/
stale estimates. Offer follow only when supported and not the viewing profile's
own request. Current withdrawal is pending, or approved with no targets;
queued/downloading active requests should not advertise Cancel. Preserve
compatible status/outcome fallback for older servers. Do not expose fabricated
server details when an ordinary user's fields are omitted.

### External title detail and access/error behavior

WebUI now serves TMDB titles through shared item-detail presentation, with
request/watchlist/follow actions and a season rail. It redirects to catalog
detail only after the library item actually loads; inaccessible library copies
retain external presentation. Authoritative 404 and transient failures receive
different unavailable/Retry behavior.
[TitleDetail.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/TitleDetail.tsx),
[ExternalTitleContent.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/ExternalTitleContent.tsx).
Native already has a rich request detail page and an Open in library button,
but presents `REQUEST` context, no shared-title promotion/access check, no new
actions/season rail, and one generic TMDB-failure message for every read error
([RequestDetailPage.xaml.cs:27](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/RequestDetailPage.xaml.cs:27),
[same file:99](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/RequestDetailPage.xaml.cs:99)).
Expand the existing **Detail pages and meaningful failure states** package to
cover this title surface; share the request contracts with the new package.
Exact native visual adaptation still needs side-by-side acceptance.

## Search package expansion (medium added scope)

- **Request suggestions join quick-search keyboard selection.** Upstream now
  traverses catalog, people and rendered request suggestions as one stable
  option sequence, with Enter opening the selected external title.
  [GlobalSearch.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/GlobalSearch.tsx).
  Native already renders clickable request suggestions, but Up/Down/Enter
  index only `_results` at
  [GlobalSearchDialog.xaml.cs:514](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/GlobalSearchDialog.xaml.cs:514).
  Add the common selection sequence alongside previously missing people,
  keeping identity stable as optional groups arrive/fail.
- **Request-to-add search has independent retained paging.** Upstream uses
  `request_page` so paging external results retains catalog state and Back
  restores the request page; query/filter changes reset it.
  [catalogSearchParams.ts](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/catalogSearchParams.ts),
  [Catalog.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/Catalog.tsx).
  Native [SearchViewModel.cs:409](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/SearchViewModel.cs:409)
  always requests external page 1 and takes 20. Add independent request-result
  paging and native navigation-state restoration, rather than a literal URL
  parameter requirement.
- **Discover rows have Explore all destinations.** New
  [RequestDiscoverSection.tsx](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/RequestDiscoverSection.tsx)
  uses progressive paging, next-page retry and restricted-viewer `next_page`.
  Native DTO/API already support `NextPage` and fetching a section page, but
  [RequestsPage.xaml.cs:259](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/RequestsPage.xaml.cs:259)
  renders only the initial row cards; existing RequestBrowse handles brands,
  not a discovery-section destination. Add the reachable row destination and
  use the server continuation rather than blindly incrementing.

Old Search paging repair remains: candidate clears `_snapshot`/`_hasMore`
before duplicate-query return ([SearchViewModel.cs:176](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/SearchViewModel.cs:176)).
Full-page people remains unqueried at `:224`; current upstream Catalog still
renders people, despite the native comment saying otherwise. The candidate
quick dialog still has no people query/result collection. Advanced search
scope from September 27 remains carried acceptance, not newly retested here.

## Collections: expand existing package, protect rule preservation first

TMDB public-list creation/source editing remains the September 28 gap: native
import dispatch supports mdblist/tmdb/trakt, not `tmdb_list`, and capability DTO
lacks `import_sources`. A current template can therefore reach its unsupported
source branch ([CollectionsViewModel.cs:447](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/CollectionsViewModel.cs:447)).
New upstream policy removes Trakt from advertised creation sources and personal
templates; new Trakt imports return Gone/unsupported_source while existing Trakt
collections continue syncing.
[Capability list](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/internal/apiv2/personal_collections.go),
[template filtering](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/internal/apiv2/personal_collections_lifecycle.go),
[actual rejection](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/internal/api/handlers/user_collection_imports.go).
Native loads server-provided templates, so current server filtering already
removes Trakt choices after a fresh load; retained Trakt API code alone is not
proof of a newly reachable failure. Honor advertised sources, handle stale
draft/capability changes, and preserve existing imported collection sync/edit.

The urgent older mixed-group corruption remains source-confirmed in the
candidate: null scope becomes movie, groups flatten via `SelectMany`, and one
group is rebuilt using outer match
([SmartCollectionWizardViewModel.cs:124](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/ViewModels/SmartCollectionWizardViewModel.cs:124),
`:140`, `:319`). Fix preservation independently before broader imports/UI.

## Inherited/server-only changes and verification

Access-group request approval/quota resolution, router modes/season submission,
anime identity matching, provider metadata enrichment, background fulfillment,
watchlist promotion/sweeps and notification deduplication run on the server.
Do not copy their engines into desktop or expand the intentionally external
admin UI scope. Native already surfaces request failures; group-limit changes
alone do not establish a need for a viewer quota dashboard. Verify blocked,
quota-exhausted and approval-required outcomes with authoritative server flags
and ordinary-user error copy.
[Group limits](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/internal/requests/group_limits.go).

New viewer-specific collection collages remain delivered through existing
poster URL/thumbhash fields consumed by native
[LibraryCollections.cs](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer.Core/Models/Catalog/LibraryCollections.cs).
Their composition/cache/access correctness is server work; verify profile
changes, initially absent poster becoming ready, and signed URL rotation without
inventing another native collage engine.
[Viewer poster mapping](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/internal/api/handlers/library_collections.go).

## Recommended order/dependencies

Keep completed 1–3 closed. For a largest/broadest ordering, insert **Title
requests and external watchlists** among the remaining large packages, beside
expanded Detail and Search; it is considerably broader than prior small polish
items. Deliver DTO/capability/API support first, then season requests and
authoritative status/withdrawal, then external-title/watchlist/follow surfaces
and shared detail presentation. Search keyboard/paging can proceed once title
navigation contracts exist. Preserve urgent small integrity fixes (smart rules,
profile-limit preservation, blocked sign-in) ahead of optional feature rollout.
Do not renumber previously completed work as unfinished.

Acceptance needs controlled fixtures for partial/complete/upcoming seasons,
router-supported/unsupported missing-season requests, owned/other-profile
request follows, allowed/blocked/exhausted quota, all download phases and absent
figures, profile switch/realtime completion, external-title promotion and
inaccessible library copies, multi-page titles/search/discovery and later-page
failure, and repeated Enter followed by catalog paging. No source pass closes
those runtime or visual checks.
