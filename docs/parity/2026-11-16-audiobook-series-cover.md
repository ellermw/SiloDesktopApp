# Audiobook Series cover and run bar - November 16, 2026

## Source and scope

- Official WebUI reference: Silo Server commit
  `749db96ac72920c4cb1bb52c8653aa9f0981cf5d` (#4397, "show one cover per
  audiobook series with a bar of its books"), parent
  `17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8`. Read from the existing scratch
  clone `D:/SiloPlayer-work/server-upstream-nov14`
  (`web/src/components/audiobooks/AudiobookGroupsView.tsx`, plus
  `web/src/lib/uiCustomization.ts` `cardGridClasses` at that commit).
- Desktop base: `origin/beta` `bdd033c1cab7cd601d6771bb853f4b0af044d67e`
  (includes Books #183), branch `t3/audiobook-series-cover`. Uncommitted.
- Scope: the audiobook library's Series tab. The listening-text rule is
  shared upstream, so Authors and Narrators row text follows it too. Their
  row layout and three-cover run are unchanged.

## Behavior ported

1. **One square cover per series.** The first poster URL that loads is used.
   A URL that fails to download (`ImageService` returns no path) or decode
   (`Image.ImageFailed`) falls through to the next URL. When no URL loads,
   the initials tile is shown. Failed URLs are tracked in a per-card set,
   not as a count, so a refetch that brings new URLs tries again. The card
   is rebuilt on every reset load. One cover request runs at a time per
   card, and the group-load cancellation token stops the fallthrough on a
   reset or page departure. Showing the Series tab again resumes retained
   covers whose token was cancelled, keeping their failed-URL sets. Download
   timeouts fall through unless the caller's token itself was cancelled.
2. **Decorative run bar** under the cover: 4 px tall with a 2 px gap
   between segments. Series of up to 12 books get one segment per book,
   with finished books filled and in-progress books half-filled. Longer
   series get one continuous bar, filled to finished/total plus half a book
   for each in-progress book. Counts are clamped as upstream:
   total = max(items, 1), finished ≤ total, in progress ≤ total − finished.
   The desktop also floors negative counts at 0. The bar and the cover use
   `AccessibilityView.Raw` and are not hit-testable; the stat text carries
   the meaning.
3. **Listening text.** It reads "all finished" when every book is finished.
   Otherwise the finished and in-progress counts are listed together,
   joined with " · ". Before this change the desktop preferred in-progress
   and dropped finished. Series cards now show totals ("N books · duration",
   one trimmed line) and listening (its own line, wrapped by whole words,
   hidden before the profile starts the series). Author and narrator rows
   keep their single combined stats line, which uses the new listening rule.
4. **Grid sizing.** The Series grid uses the Books grid's columns for the
   profile's poster size (`UICustomizationService.PosterColumnCount` at the
   WebUI breakpoint width, gap 16 for large and 12 otherwise). It also
   honors the local poster-width override, and it keeps the Books grid's
   100 px minimum and raster-floor rounding. Rows are 24 px apart (official
   `gap-y-6`). Existing cards and skeletons resize in place when the panel
   size, UI customization or poster-width override changes, with no
   refetch. Loading skeletons stay square.

Local behavior preserved: retained page state and offset restore (returning
straight to Library rebuilds cards; returning via Home resumes retained covers
when Library is shown), no page-navigation animation,
bounded loading, keyboard and Button activation, and the existing
localization keys (`web.audiobooks.groups.*`).

## Files changed

- `src/SiloPlayer/Helpers/AudiobookSeriesPresentation.cs` (new): pure cover
  fallthrough, run-bar counts and fills, listening parts and text.
- `src/SiloPlayer/Views/LibraryPage.xaml.cs`: series card, cover loader,
  run bar, grid sizing and resizing, and shared stats text. The row cover
  method lost its dead series-fan branch.
- `src/SiloPlayer/Views/LibraryPage.xaml`: `AudiobookGroupsPanel` `SizeChanged`.
- `tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj`: links the helper.
- `tests/SiloPlayer.Tests/AudiobookSeriesPresentationTests.cs` (new): unit tests.
- `tests/SiloPlayer.Tests/CurrentLibraryParitySourceTests.cs`: the old
  fan-grid `viewportWidth >= 1280 ? 6` assertion is replaced by a
  series-card source contract test.
- `tests/SiloPlayer.NativeRegressionTests/BooksLibraryNativeFixture.cs`:
  series-card native cases (initials cover, segment and continuous bars,
  UIA Raw, wrapping listening text) in the `books-library-groups` selector.

## Verification

Original PR verification results (before the cancellation/timeout follow-up):

- **Unit tests:** all 7,481 pass, including `AudiobookSeriesPresentationTests`
  and `CurrentLibraryParitySourceTests`.
- **`books-library-groups` native:** passes all 15 checks. They include the
  one square cover with initials fallback, no fan, per-book segments hidden
  from UI Automation, finished and in-progress text that wraps, the
  continuous bar past 12 books, and no listening line for an unstarted
  series.
- **`books-library` native:** passes all 15 checks.

Follow-up native cases cover cancellation during pending downloads, re-showing
Library from Home, timeout fallthrough to a valid cover, and timeout-only
initials fallback. These additions have not been built or run locally; the
pipeline must verify them.

Not certified: visual comparison against the live WebUI across poster sizes,
a real cover URL that fails and falls through, and live data. Image decode
width is set when the card is built and is not raised by a later in-place
resize.
