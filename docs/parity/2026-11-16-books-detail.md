# November 16 — Book detail pages (audiobook, ebook, comic)

## Lead source reconciliation for immediate Beta submission

Reader/detail changes are grouped together because canonical comic ordering
is shared by detail Start/Continue and reader Next. Volume aliases v01/01/1
now share one section, loose chapters participate in the same ranked order,
and comic expanders stretch across their chapter panel. Retained ebook related
rails rebuild when their responsive card width changes. New unit/native
regression source accompanies these corrections but is unexecuted locally.

The owner explicitly directs Beta submission despite the local runtime/storage
hold. Earlier worker checks below do not cover final lead corrections. Pipeline
QA/review remains required. Clickable audiobook series navigation, new-key
translations, sticky controls and full live-WebUI visual comparison remain
outstanding; do not label this source as comprehensive all-books parity.

Baseline: Beta `173c37974894fe5d52a4a4437c356c84e5c897d9`, version
`1.2.1165-beta.1` (unchanged; the pipeline owns stamping). Official Silo
Server main pinned read-only at `17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8`
(`D:/SiloPlayer-build/books-parity/upstream`). Source changes are
uncommitted for the lead.

## Why the audiobook page looked nothing like WebUI

The native page still rendered audiobooks through DetailHero (backdrop hero,
72px title, `AUDIOBOOK` eyebrow, runtime pill, split Play button), followed by
a separate narrator card, a collapsed chapter list with a sort menu and
portrait poster rails. Upstream replaced that design after the localization
catalog the app pins (`1452ea2d…`): `AudiobookContent.tsx`,
`pages/audiobooks/audiobooks.css`, `components/{ChaptersSection,RelatedRail,NarratorPicker}.tsx`
were rewritten and `NarratorCard.tsx` deleted (diff 1452ea2d..17d5376: +669/−352
across 12 files).

## What changed (scope: ItemDetailPage / ItemDetailViewModel book surfaces)

Audiobook detail now follows `AudiobookContent.tsx` + `audiobooks.css`:

- **Page**: `.audiobook-tint` — no backdrop; the cover thumbhash's guarded
  average colour (`ThumbhashDecoder.GetAmbientColor`, same as `useAmbientColor`)
  mixed 34% → 12% → background plus a 24% radial glow at 18%/0, height
  `min(680px, 95vh)`; recomputed on theme change.
- **Grid**: from lg (≥1024) a 260px side column (square cover r16, then
  actions) beside head and body, gaps 2rem/3.5rem, `pt-24 pb-14`. Below lg the
  cover (176px sm, 112px base) sits beside the head (`align-items:end`) with
  actions and body across the page. Sticky side column is not reproduced.
- **Head**: series line (`<Series>, book N`), 48/36/24px extra-bold title,
  `By **Author**` and `Narrated by **Narrator**` with localized label/name
  emphasis; with other narrations the name becomes the NarratorPicker
  (count header, current narration selected, alternatives with year).
- **Actions**: full-width Listen / `Resume · <chapter>` / Pause / Resume
  (live player), listened bar with `<time> listened · N%` and `<x> left`,
  then Listen from Start (only with progress), Add to collection (icon) and a
  curator-only More menu (Edit metadata, Match, Refresh metadata).
- **Body**: bordered facts row (Length, Chapters, Released, Publisher),
  overview, genre `.metadata-badge` pills, Chapters, then rails.
- **Chapters** (`ChaptersSection`): open by default; >10 chapters show an
  8-row window with 3 rows before the current chapter and Show all N / Show
  fewer; number, title, `left in this chapter` on the current row, `h:mm:ss`
  start (sm+), duration with a tick on finished chapters, current row
  `bg-primary/15`; follows the live player position. Chapter rows keep server
  order and `fallbackTitle` uses `index+1`.
- **Rails** (`RelatedRail`): In <series> (kicker `Book N`, current ringed),
  Also by <author>, You might also like; square covers one poster size down
  (`squarePosterSize`), status `Finished` / time left / length / year and a
  progress bar for started books. `AudiobookRelatedItem` gained
  `poster_thumbhash`, `duration_seconds`, `played`, `progress_ratio`.

Ebook (DetailHero, `EbookContent.tsx`): series rail uses 2:3 covers with a
`Book N` subtitle and the current book ringed; also-by/similar show the year.
The `By <author>` crew line now comes from the ebook extension authors inside
the crew-line refresher (previously a deferred refresh blanked it), and the
DetailHero type eyebrow stays uppercase across localization rebinding (it
regressed to `Ebook`). Comic (`manga`) keeps its existing DetailHero chapter
list; verified only that a retained page returns to it after an audiobook.

The layout mode is reversible on the retained page: borrowed overview/genre
and refresh-error controls return to their exact DetailHero slots, and the
chapters/rails sections move back to the shared stack.

Files: `src/SiloPlayer/Views/ItemDetailPage.{xaml,xaml.cs,BookLayout.cs}`,
`src/SiloPlayer.Core/Helpers/AudiobookDetailPresentation.cs` (pure rules),
`src/SiloPlayer.Core/Models/Home/HomeSectionsResponse.cs`; tests
`tests/SiloPlayer.Tests/{BookDetailParityTests,ItemDetailCurrentParityTests}.cs`,
`tests/SiloPlayer.NativeRegressionTests/{BookDetailParityNativeFixture,AudiobookDetailInteractionsNativeFixture,Program}.cs`.

## Verification

All output under `D:/SiloPlayer-build/books-parity/details` (about 1.0 GiB;
C: 178 GiB and D: 469 GiB free). The directory holds a copy of
`SiloPlayer.sln`/`README.md` and junctions to the worktree source so source
tests resolve the repository root while builds use
`--artifacts-path D:/SiloPlayer-build/books-parity/details/artifacts`.

- Red: `logs/01-red-source-tests*.log` — both new source tests failed on the
  baseline page (`AudiobookTint`, `BookRelatedCard(` not found).
- Focused units after the change: 64/64 (`logs/03-green-focused.log`).
- Full unit suite: 7,485 passed, 1 failed (`logs/07-full-units.log`); the
  failure was the harness root lacking `README.md` and passes once present
  (`logs/07c-rebrand-rerun.log`).
- Release publish (`logs/05-publish-2.log`) and matching native host
  (`logs/06-native-build-2.log`). Published/reference `SiloPlayer.dll`
  SHA256 `132BB49C182B797E89E22BE72B823E88BC91476C2214AB7452FDB7D9A053FE57`;
  host fixture DLL `B885CC40B31FB61D59D7BA2090363E89E91A4133AF96DF463A49692C557253B4`.
- Native `book-detail-parity`: PASS (`native-results/book-detail-parity-2`),
  asserting the actual page at 1440/900/500: tint replaces backdrop, side
  column vs stacked geometry and cover sizes, series/title/author/narrator,
  `Resume · Part Five`, `1h 23m listened · 30%` / `3 hr 17 min left`, facts,
  chapter window Part Two…Part Nine with current Part Five and
  `16m 40s left in this chapter`, start column only from sm, Show all → 14 /
  Show fewer, three rails with 160/140/120px square cards, ring and statuses;
  retained switch to ebook (DetailHero restored, crew line, `EBOOK`, 2:3
  `Book N` rail) and comic, then back to audiobook. The first run
  (`book-detail-parity-1`) surfaced the ebook crew line/eyebrow gaps in its
  captures before those assertions and fixes were added.
- Native `audiobook-detail-interactions`: PASS
  (`native-results/audiobook-detail-interactions-1`) — narration menu and
  navigation, local mpv Pause/Resume driving the new Listen button and
  listened bar, no remote request.

Captures: `native-results/book-detail-parity-2/book-detail-audiobook-{1440,900,500}.png`,
`-1440-viewport.png`, `-500-all-chapters.png`, `book-detail-ebook-1440.png`,
`book-detail-comic-1440.png`.

## Limitations and follow-ups

- Visual comparison is against the upstream source/CSS, not a live WebUI
  screenshot; synthetic data, dark theme and one DPI only. No installed
  player, settings, credentials, installer or production action.
- New WebUI keys (`web.audiobooks.detail.{length,released,publisher,finished,seriesBook}`,
  `web.audiobooks.chapters.{showAll,showFewer,leftInChapter}`) are absent
  from the pinned catalog (`2026.11.15`, source `1452ea2d`); they render the
  upstream English via fallbacks until the catalog is refreshed to release
  `2026.11.16-20af7f8` (`tools/SiloTranslationsImport` at `17d5376`) — a
  shared change for the lead.
- The series line is not a link: WebUI opens a series-rule-filtered library
  browse, but `LibraryPage.NavigationArgs` (lead-owned) only accepts a genre.
- Not reproduced: sticky side column, cover drop shadow, ring offset (the
  ring is drawn inside the cover), list-format joining of several names
  (`, `), `is_in_progress ?? !played` (native field is non-nullable; uses
  `IsInProgress || !Played`), and smart-rewind cold resume differences
  belong to the player. Ebook Files/MediaLocations and Download visibility,
  and the comic detail, were not re-audited beyond the checks above.
