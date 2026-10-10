# Home/library current-source implementation pass — October 9, 2026

**Resumed final acceptance:** the source-stage/unrun statements below are historical. The current official reference is1a7a3970a9928efb0157460c10888832a8a74eb1. [Combined verification](2026-10-09-combined-verification.md) records the finite source/native/physical evidence, exact1.2.241 payload and native/live boundaries. Correction IDs are reconciled in the central ledger; GitHub publication awaits the user's final installer approval.


Status: **source implementation checkpoint; combined verification pending**. This does not establish visual acceptance or finalize difficulty item 7.

The coordinator fetched official public GitHub main directly and pinned **22e3a0ba7c1431dda77b957ed1012508d23f2b80**. All WebUI reads in this pass use `git show` at that commit in `D:\SiloPlayer\.codex-tmp\silo-server-current`; its checked-out files are not the reference. No GitLab source, production APIs, credentials, app launch, browser or computer control was used. No build, publish or test command was run. Existing dirty files and earlier accepted fixes remain retained.

## Concrete source corrections

These are **eight proposed distinct corrections**, subject to coordinator deduplication against the central ledger. Do not count every visual property or test as another correction.

| Proposal | Concrete correction | Pinned source proof |
|---|---|---|
| H7-1 | Home layout/cache/section/retry ownership now includes API request context, not only a profile string. A retired layout or section cannot publish after a profile, server, authentication or access-context change. Context replacement clears dismissal undo; an active mounted page starts the new owner after a retired layout settles. Optional taste-prompt replies also reject a retired context. | `pages/Home.tsx` load-generation cleanup and `pages/homeSectionState.ts`; authority integration follows the existing desktop `ApiRequestContext` contract. |
| H7-2 | Listening captions suppress a redundant default chapter title, select the first chapter before its first mark, preserve API file/chapter order and per-file fallback chapter names, and fall through from zero detail duration to file duration, then section duration. Author/narrator hydration trims empty names. | `components/NowListeningHero.tsx`, `lib/audiobooks/files.ts`, `lib/audiobooks/chapters.ts`, `lib/audiobooks/duration.ts`. |
| H7-3 | The mounted listening deck issues one owned detail request instead of the previous duplicate request on changed `Bind`. Detail hydration carries `library_id`, rejects retired context/library/deck responses, and invalidates cached or pending detail ownership on rebind/remount. | `components/NowListeningHero.tsx` detail-id/type guard, `hooks/queries/catalogRead.ts` `useCatalogItemDetail` / `fetchCatalogItemDetail`, `lib/mediaNavigation.ts` library context. |
| H7-4 | The ordinary hero subscribes to the active native audiobook's pause/content changes while mounted, so its Pause/Resume label and icon follow controls elsewhere. Handlers detach on unload. | `components/HeroBanner.tsx` `useAudiobookPlaybackController` and `heroPlayLabel`. |
| H7-5 | Slides arriving after an empty hero mounts start the slideshow; a two-to-one-slide refresh stops it. A same-count refresh retaining the active content preserves the existing timer cycle. | `components/HeroBanner.tsx` interval dependencies on slide count, pause and active index. |
| H7-6 | Absent overview/metadata collapse their margins; metadata wraps within the hero's width. Dots belong between adjacent text entries, with the source spacing, rather than being inserted around the separate rating component. Existing rating priority/rounding semantics are retained and receive new mounted coverage. | `components/HeroBanner.tsx` conditional metadata/overview and `app.css` `.hero-meta-track` / `> span + span::before`; `components/heroMetadata.ts`. |
| H7-7 | Home/library hero treatment now follows their separate gradient stops and alpha, soft radial vignette, library top scrim and omission of Home border/vignette. Wide editorial controls use a 40px separator, source breakpoint positions and a 144×2 white progress rail; compact controls retain the slash and no rail. | `components/HeroBanner.tsx` `bleed`, source controls; `app.css` `.hero-gradient`, `.hero-gradient-strong`, `.hero-gradient-left`, `.hero-vignette`, `.hero-top-scrim`, `.hero-slide-counter`, `.hero-progress-rail`. |
| H7-8 | Responsive initial Home skeleton sizing now reaches the actual nested poster controls. Its desktop skeleton gutters follow the loading-source 48px contract, featured-error content has separate horizontal/bottom padding, and a rendered hero has no extra 8px page-top inset. | `pages/Home.tsx` `HomePageSkeleton`, `renderHeroSlot` error, `hasHeroSlot ? "pb-2" : "pt-6 pb-2"`. |

The current primary hero IMDb/TMDB priority, 7.35→7.4 rounding, episode metadata order, genre deduplication, declared item limit and first-featured-section selection already existed. The new assertions do not count those as new product fixes. Earlier Listening artwork composition/remount and Hero title/capsule acceptance remain supporting evidence; these new changes need their own coordinated rerun.

## Exact files owned by this pass

- `src/SiloPlayer/Views/HomePage.xaml.cs`
- `src/SiloPlayer/ViewModels/HomeViewModel.cs`
- `src/SiloPlayer/Controls/HeroCarousel.xaml`
- `src/SiloPlayer/Controls/HeroCarousel.xaml.cs`
- `src/SiloPlayer/Controls/NowListeningHero.xaml.cs`
- `src/SiloPlayer.Core/Api/CatalogApi.Hero.cs` (new; coordinator authorized the separate partial overload)
- `src/SiloPlayer.Core/Services/NowListeningPresentation.cs` (new)
- `tests/SiloPlayer.Tests/NowListeningPresentationTests.cs` (new)
- `tests/SiloPlayer.Tests/HomeHeroCatalogTests.cs` (new)
- `tests/SiloPlayer.NativeRegressionTests/HomeCurrentNativeFixture.cs` (new)
- this report

`HomePage.xaml`, `NowListeningHero.xaml`, LibraryPage, LibraryViewModel, shared theme/icons, Program, settings editors and the central ledger were not edited by this owner.

## Coordinated seams and prepared checks

Root owns assigning `RecommendedHeroCarousel.LibraryId` and `RecommendedNowListeningHero.LibraryId` from the current library at its existing load/reconcile seams. Both properties are regular public `int?` properties. The scoped catalog overload is `GetItemDetailAsync(string contentId, int? libraryId, CancellationToken ct = default)`; the legacy overload remains unchanged. It sends through the captured request authority, URI-escapes content/library values, and rejects authority changes after the response.

Root owns dispatching `HomeCurrentNativeFixture.RunAsync(StackPanel)` using `SILO_NATIVE_HOME_CURRENT=1`. The prepared native fixture exercises actual HomeViewModel held-layout/profile-switch and held-section/access-invalidation responses, single scoped listening hydration and captions, late slideshow/count transitions with retained-cycle checks, rendered IMDb/TMDB metadata, hidden absent blocks, library/Home scrim-border-fade state, 144×2 rail and nested skeleton sizing. The unit files exercise the extracted listening semantics and scoped detail HTTP behavior. All are **unrun**.

## Remaining final verification

1. Run the new unit and native fixture with the final coordinated payload; check compilation, full fixture completion and meaningful failures. Build success alone is not the fixture result.
2. Rerun the existing Listening artwork/lifecycle and actual bundled-mpv transport cases after the caption/hydration changes. Add/observe ordinary Hero Pause/Resume reacting to the same native player events; the new Home fixture does not synthesize a successful playback transport claim.
3. Compare matched Home and Library Recommended frames at 460/900/1280, including loaded, loading, failed, retry, no-featured, empty-featured and populated/empty rows. Check wrapping, title/meta/overview/buttons/counters, theme fades and sticky library header overlay. Verify corrected gutters/error-bottom spacing in the actual page. The source panel assertions do not prove rendered pixels or native text metrics.
4. Retain the existing Library saved-state/profile-isolation, rejected stale reply, grid spacing, loading/retry/empty and scrolling/realization cases from `2026-10-03-browse-finalization.md`. Root owns the dirty filter/library path; no claim of fresh library physical acceptance follows from this pass.
5. Hero `LibraryId` exposure and listening hydration are implemented, but the common native ItemDetail/reader/player navigation interfaces still require coordinator review for library context propagation under item 6/item 1. The current Hero More Info and native playback paths use the existing native routes; property wiring alone is not proof of full route-context parity.
6. Listening capture color uncertainty in the earlier JPEG/PNG pair remains unresolved. No new pixel color claim or attempted color compensation was made.

Computer control remains paused. Visual/physical acceptance, correction-count finalization and installer/release work belong to the final coordinated stage.
