# Library browse item count (official Silo Server #4316)

Parity pass dated 2026-11-16. Source-only: nothing here has been built or run yet.

## Upstream reference

- Official change: `321223364cb3a1e51478a4e9d65aca0ad83d7288` "feat(web): show the item count on library pages (#4316)" (2026-11-15).
- Official `main` when read: `8f93594ce19723dde8a204be21ba59a24ef06e3b` (fetched from `https://github.com/Silo-Server/silo-server.git`).
- Upstream files read: `web/src/pages/LibraryBrowse.tsx` (`countNoun`, `countLabel`, `resultCount*`),
  `web/src/hooks/queries/catalog.ts` (`useCatalogCount`, `firstPageComplete`),
  `web/src/components/catalog/CatalogFilterBar.tsx` (placement), `web/src/pages/LibraryBrowse.test.tsx`,
  and `web/src/locales/en.json` (English messages).
- Audit entry: `D:/SiloPlayer-build/server-review-nov15/viewer-audit.md` section 8, "Library item count (#4316)".

## Upstream behavior

- The library page's filter bar shows how many titles the **current filters** match, directly after the Filters button.
- The number is always exact. The pager's estimated runway is never shown as a count. Sources, in order:
  1. title order: the letter index's exact total;
  2. a first page that holds the whole result (`has_more` false): the loaded items;
  3. otherwise one separate count read (`useCatalogCount`), made only once the first page shows there is more. It is
     keyed without the sort, so a re-sort reuses it, and it is dropped when the server could only estimate (`total_exact: false`).
- Label by what the browse lists (`countNoun`):
  - series library: `series`, or `episodes` when the type picker is on Episodes;
  - `movie`/`movies` library: `movies`;
  - audiobook(s)/ebook(s) library: `books`;
  - any other library (mixed, manga): the media scope (`movie`, `series`, `episode`, `audiobook`/`ebook`), else generic `titles`.
- English messages (ICU plural on `count`, number in `shown`):
  `web.library.browse.countMovies` "{shown} movie(s)", `...countSeries` "{shown} series" (one and other alike),
  `...countEpisodes` "{shown} episode(s)", `...countBooks` "{shown} book(s)", generic `web.catalog.page.titleCount` "{shown} title(s)".
- While the number is being read a small spinner labelled `web.catalog.filters.loadingCount` ("Loading item count") shows.
  A failed refresh drops the old number (a count that never loaded shows nothing). Zero or unknown shows nothing.
  Audiobook series/author/narrator groups use their own group count and no browse count.

## Desktop behavior now

- The desktop already read the exact total once per query (`LibraryViewModel.EnsureExactCountRequested`, one page-0 request
  with totals) and already kept `DisplayTotalCount` equal to the exact total only. That is the bounded mechanism, so **no
  new network read was added**; the previous bug was only that `ResultCountText` was forced `Collapsed` and used a generic
  "N results" message. Retained page state is untouched and there is no navigation or entrance animation.
- `ResultCountText` now shows `LibraryResultCount.Label(...)`: the exact `DisplayTotalCount` with the noun above ("1,234 movies",
  "12 episodes", "640 books", "1 series"/"42 series", generic "N titles"). `tv` is treated like `series`, as the desktop type
  picker already does.
- Waiting: a 16 px `ResultCountProgress` ring (named "Loading item count") shows while a first window is loading or an
  estimated extent exists without an exact total.
- Hidden: exact total is zero (empty), the latest exact-count read failed or the server only estimated
  (`LibraryViewModel.ExactCountFailed`, which also hides an older number), and whenever the sort/order/Filters toolbar is
  not the visible one (audiobook series/author/narrator axes, which keep their own group count).
- Placement: the filter-bar grid cell right after the Filters button (column 3 without a type picker, column 4 with one,
  sharing that star column with the right-aligned poster-size button; 48 px right margin avoids overlap), as a polite
  live region.
- Localization: the `web.library.browse.count*` keys are not in Beta's generated catalogs until the localization PR #186.
  Every call passes an explicit English fallback equal to the upstream English string, so the label reads the same before
  and after #186 merges and picks up translations then. Counts use the existing `count` + `shown` plural arguments.
  The generic noun uses `web.catalog.page.titleCount`, which is already in the catalogs.

## Differences kept on purpose

- Desktop does not derive the count from the letter index (title order) or from "the first page holds everything"; it waits
  for its existing exact-count read, which `LibraryViewModel` tests pin to leave `DisplayTotalCount` at 0 with only an index.
  The result is the same number, possibly a moment later.
- Desktop library browse has no `query_limit`, so the upstream `min(total, limit)` clamp has no counterpart.
- Spinner is a `ProgressRing` with the automation name; no visible "Loading item count" text, as upstream.

## Files changed

- `src/SiloPlayer/Helpers/LibraryResultCount.cs` (new): noun choice, localized label with English fallbacks, show/wait/hide rule.
- `src/SiloPlayer/ViewModels/LibraryViewModel.cs`, `LibraryViewModel.Letters.cs`: `ExactCountFailed` (set when the exact-count read
  fails or is estimate-only, cleared by a new query, a retry or any exact total).
- `src/SiloPlayer/Views/LibraryPage.xaml`: `ResultCountText` placed after Filters with live region; new `ResultCountProgress`.
- `src/SiloPlayer/Views/LibraryPage.xaml.cs`: `UpdateCountDisplay` rewritten; `_catalogToolbarShown`; the two forced-collapse sites
  replaced by `UpdateCatalogToolbarVisibility`; `IsLoading`/`ExactCountFailed` changes refresh the count; column and margin set
  in `ConfigureBrowseTypeSelector`.
- `tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj`: links the new helper.
- `tests/SiloPlayer.Tests/LibraryResultCountTests.cs` (new): noun per library/type, label pluralization and grouping,
  show/wait/hide matrix, source checks of the page wiring and fallbacks.
- `tests/SiloPlayer.Tests/LibraryLetterNavigationTests.cs`: view-model tests for the exact-only count and the estimate-only hide.
- `tests/SiloPlayer.NativeRegressionTests/RetainedLibraryStateOrderingsNativeFixture.cs`: group axes also keep
  `ResultCountProgress` collapsed; the Books axis never shows an empty count or a count beside the ring.

## Verification

Local results on this source:

- **Unit tests:** all 7,518 pass, including `LibraryResultCountTests` and
  the two new `LibraryLetterNavigationTests`.
- **Native, all passing:**
  - `retained-library-review` with the `audiobook-author`,
    `audiobook-narrator` and `audiobook-series` ordering cases;
  - `books-library` (15 checks);
  - `books-library-groups`.

Not certified, and needing a look in the real window: the count's position
and clipping beside Filters at narrow widths (with and without the type
picker), the ring's size and alignment, RTL, and a real library with filters
changing the count.
