# Books library browsing parity - November 16, 2026

## Owner-directed immediate Beta submission

The owner requests a qualifying non-draft PR now, despite the local shared-output
runtime hold. This browsing/card PR is independent of reader/detail PR182.
Current Beta 7ba72bed148a29e737ec708997d8fedc259f7a0d is merged before
submission; no version stamps are manually changed. The earlier results below
retain their original scope: the final empty-state guard is compiled but lacks
matching native re-verification. Pipeline QA must validate final source.

Review identified remaining requested parity work: separate true-empty library
and filtered-no-match states, Clear Filters, failed-browse Retry, locale-aware
author joining and complete live-WebUI visual comparison. Submission is not a
claim that those gaps or all visual parity are finished. No new local runtime
or cleanup retry occurs for this submission.

## Source and ownership

- Worktree: `C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-f389df94-04054bf9359e5ce4`.
- Base: `173c37974894fe5d52a4a4437c356c84e5c897d9`; app version remains `1.2.1165-beta.1`.
- Official reference checkout: `D:/SiloPlayer-build/books-parity/upstream`, commit `17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8`, supplied from directly fetched official GitHub main. HEAD was rechecked during handoff; no alternate/private upstream was used.
- Scope: library browsing/cards only. No detail pages, audiobook playback controls, ebook reader/API/assets, installed application, credentials/settings, or version stamps changed. Changes remain uncommitted; lead owns integration and PR submission.

## Reference files inspected

Paths below are relative to the official checkout's `web/src`:

- `pages/LibraryBrowse.tsx`: library media scope, filters, sorting, group selection/reset, aspect ratios, loading/empty/error presentation.
- `pages/libraryPageSearchParams.ts`: canonical book aliases, browsing axes and sort scopes.
- `components/ItemCard.tsx`, `components/ItemGrid.tsx`, `components/WatchProgressBar.tsx`: audiobook captions/progress and manga card overlays.
- `components/audiobooks/AudiobookGroupsView.tsx`: series/author/narrator browsing.
- `components/catalog/CatalogFiltersPanel.tsx`, `lib/querySortOptions.ts`: media-specific filters/sorts.
- `api/v2/catalog.ts`, `api/v2/schema.ts`, `components/admin/libraries/libraryTypes.ts`: browse payload authors and canonical media/library types.

## Audit and implementation

Existing code already supplies audiobook Books/Series/Authors/Narrators browsing with group search, sorting and paging; book-specific filters/sorts; square audiobook and portrait ebook/manga artwork, including item-specific artwork in mixed libraries; manga status/count chips; and navigation through the generic detail dispatcher by media type. These are source-audit findings, not a declaration of complete visual parity. Cached navigation/windowing was retained.

Confirmed gaps addressed:

1. Audiobook library cards lacked author captions, resting runtime/time-left and listening progress. Added library-only `LibraryGridCard.Books.cs`, an optional `MediaItem.Authors` browse payload property, and an optional resting-subtitle argument to the shared subtitle helper. Explicit sort metadata wins; author sort omits the duplicate author line. The 3px inset progress bar guards nonfinite positions/durations, clamps progress, refreshes/resizes/recycles, and reserves space below overlays.
2. Manga showed generic watch-status badges alongside its dedicated chips. Suppressed the generic badge for manga library cards.
3. Selecting an audiobook author/narrator/series could retain unrelated advanced filters and ignore the selected group. Group selection now replaces the canonical rule draft with exactly the selected group and returns to Books while preserving sorting.
4. Plural audiobook/ebook libraries used Watch filter captions. Singular/plural and mixed-library selected media scopes now select listening/read captions.
5. A failed catalog could also show an empty-library message. The empty state now requires a blank error and responds to error changes. Its regression was demonstrated failing before the fix; the fix compiled, but a post-fix native run is deferred by the storage freeze.

Changed source: `src/SiloPlayer.Core/Models/Home/HomeSectionsResponse.cs`, `src/SiloPlayer/Controls/LibraryGridCard.cs`, new `src/SiloPlayer/Controls/LibraryGridCard.Books.cs`, `src/SiloPlayer/Controls/MediaItemCardActions.CardLocalization.cs`, and `src/SiloPlayer/Views/LibraryPage.xaml.cs`. No LibraryViewModel changes were necessary.

Tests: new `tests/SiloPlayer.Tests/BooksLibraryCatalogTests.cs`, new `tests/SiloPlayer.NativeRegressionTests/BooksLibraryNativeFixture.cs`, and selector registration in `tests/SiloPlayer.NativeRegressionTests/Program.cs`.

## Evidence and verification boundaries

All generated evidence is beneath `D:/SiloPlayer-build/books-parity/library`; paths in this section are relative to it.

- RED: `red/results.txt` and `red/books-library-in-progress.png` demonstrate the missing audiobook time-left presentation; `red-manga/results.txt` demonstrates the manga badge collision; `red-groups/results.txt` demonstrates plural status captions and stale group queries.
- Unit/source GREEN before the final empty-state change: `unit-tests.log` and `unit-results/books-library.trx`, 121 passed, zero failed/skipped. Includes six new book catalog cases plus focused existing library/cache/rule/poster/progress/card tests. Initial new-test failures caused by recording capabilities requests were corrected by recording catalog requests only.
- Native GREEN before the final empty-state change: `final-books-library/results.txt`, 15 PASS lines including run summaries; `final-books-library-groups/results.txt`, 7 PASS lines; `final-retained-library/results.txt`, 70 PASS lines; `final-overlay-revisions/results.txt`, 50 PASS lines. All four have zero anchored `FAIL:` lines. Assertions containing the word "failed" are not failures.
- Native captures inspected during implementation: `final-books-library/books-library-in-progress.png` and `final-books-library/books-library-resized.png`, covering audiobook captions/progress, portrait reading-card geometry and resize. Group-query coverage invokes the actual Page callback/query path on an unmounted constructed Page; it is not an end-to-end click/navigation recording.
- Latest RED: `red-empty-failure/results.txt` has six passing group/status checks followed by `Failed book catalog must not claim the library is empty.` The prior group GREEN log predates this assertion.
- Last already-running app build finished with exit 0, 512 warnings and zero errors (`green-build.log`, 29.36 seconds). It includes the empty-state fix. No new build/native run was started after the freeze, so that final assertion is not claimed GREEN.
- `git diff --check` passed during handoff. No publish, installer, or release was created.

The reusable build root is `artifacts`, configured using `-p:ArtifactsPath=D:/SiloPlayer-build/books-parity/library/artifacts`, Release/win-x64, `Platform=x64`, `PublishReadyToRun=false`. Native selectors are `books-library`, `books-library-groups`, `retained-library` and `overlay-revisions` via `SILO_NATIVE_TEST_ONLY`. Do not run these until a slot/storage allowance is granted.

## Storage freeze and preserved payloads

The owner reported global output usage of 9.724 GiB against the 10 GiB cap and Books usage of 3.4445 GiB. This worker stopped launching .NET build/publish/native work. The final active build has ended, no owned native fixture remains running, and this worker did not acquire or hold `SiloOct18Build`; no mutex remains to release.

A read-only inventory of this worker's directory, excluding reparse points, measured **0.722028 GiB**. This is not a recount of all Books/global lanes. Observed free space was C: 177.930 GiB, D: 468.952 GiB, both above 50 GiB. Nothing was deleted, copied, published, or cleaned up in another lane.

Preserve both of these distinct products; hashes were read back at handoff:

- Native-verified pre-empty-fix reference: `artifacts/bin/SiloPlayer.NativeRegressionTests/release_win-x64/SiloPlayer.dll`, SHA256 `0384785CBEBF00C6BDF5CD371121F07171EF62179A628B6B497AE8CAEE755CD8`. Preserve its host directory and the app runtime directory needed by fixture execution.
- Latest compiled app, not native-reverified after the last fix: `artifacts/bin/SiloPlayer/release_win-x64/SiloPlayer.dll`, SHA256 `15D27CC5D505134E4CE839B8999B158A33111D31BBA31B241017B16D1E7BF524`.
- Preserve all RED/GREEN results, native captures, build/test logs and `unit-results/books-library.trx`.

Candidates the lead may reclaim after releasing all consumers, without deleting the preserved app/native host/evidence:

- `artifacts/obj`: 0.075613 GiB of rebuildable intermediates.
- `artifacts/bin/SiloPlayer.Core`: 0.008885 GiB of standalone dependency output; app/host carry their own dependency copies.
- `artifacts/bin/SiloPlayer.Player`: 0.122381 GiB of standalone dependency output, chiefly the generated libmpv copy; preserve the app runtime's libmpv instead.
- Optional, if the lead does not need the existing executable unit-test payload: `artifacts/bin/SiloPlayer.Tests`, 0.166611 GiB; preserve its logs/TRX. Together the four candidates total approximately 0.373490 GiB.

No superseded full app/publish directory was found in this worker's root. These are rebuildable output candidates, not permission to remove verified runtime/evidence. The root's `src` and `tests` entries are directory junctions to this worktree: do not recurse into them for cleanup/accounting or delete their targets. No denied cleanup was bypassed.

## Remaining parity and integration limits

- Complete visual parity is **not certified**. There was no live official WebUI/native side-by-side comparison, live account/server library test, multi-DPI/locale/accessibility certification, or full pointer navigation capture.
- The generic library no-items text and bottom error presentation still differ from WebUI's empty-library versus filter-no-match distinction, scan guidance and retry presentation. Those library-shell states require a separate focused implementation/verification pass.
- Author joining is comma-separated rather than WebUI's locale-specific `Intl` unit-list formatting.
- Re-run the failed/empty native assertion on the final product, then the focused unit/native checks under an explicitly granted finite slot; do not infer completion from the prior green payload.
- Shared additive changes needing lead integration review: browse DTO `Authors`, optional `CardSubtitle` parameter, and native Program selectors. Detail/reader behavior is deliberately untouched; no claim is made about remaining cross-scope detail/reader parity.
