# Collections list and creation source checkpoint — 2026-10-09

**Resumed final acceptance:** the source-stage/unrun statements below are historical. The current official reference is1a7a3970a9928efb0157460c10888832a8a74eb1. [Combined verification](2026-10-09-combined-verification.md) records the finite source/native/physical evidence, exact1.2.241 payload and native/live boundaries. Correction IDs are reconciled in the central ledger; GitHub publication awaits the user's final installer approval.


Reference: official public `Silo-Server/silo-server` main fetch supplied by the root task, pinned at `22e3a0ba7c1431dda77b957ed1012508d23f2b80`. Used `git show` at that commit and its exact extracted `.codex-tmp/collections-reference22e3/web/src` files. The reference checkout was not used as authoritative working contents. No GitLab, production, credentials, browser, computer control, app launch, build, publish, or test execution was used.

This is a bounded source checkpoint, not visual or functional acceptance. Prior C158–C165 behavior remains in place. The eight groups below are proposed distinct source corrections for root-led ledger reconciliation; these do not constitute eight accepted parity completions.

## Proposed correction groups

1. Current chooser cards: 168px desktop-only decorative stages; manual36px rows/22×33 posters/middle translated and rotated row; smart24px individual rules/50×75 posters/fourth highlighted poster; synced36px service rows/24px marks/36×54 posters; current five gradient palettes and tint roles. Card18px corners,20/18/20px copy inset,32px icon tile/16px icon,17px semibold title,13.5px description,12.5px examples,16px responsive chevron and hover/focus lift. Wide choices use one grid row, avoiding empty-row spacing.
2. Current chooser dialog/state roles: desktop1000px or viewport-minus48, narrow full-width/bottom placement,20px corners,34/44px close target, footer note beside36px Cancel; Synced loading/error tiles use the same18px card frame, Retry32px, noninteractive off card opacity0.6, and any advertised import source enables the chooser. Capability lifetime/context checks are retained. The WinUI template placement is staged and unmeasured.
3. Current source pick rows: native radio selection with18px/1.5px ring/8px dot, full-row checked and hover surfaces,30×45/5px cropped poster,14.5px semibold truncated title, adjacent10.5px source tag, and12.5px media/title-limit/cron summary. Removed the obsolete Profile badge contract. Accessible description preserves tag/metadata.
4. Shared collection poster role:16px frame/crop and65% border, surface background, personal User fallback, contained1.05 image hover scale; owned Shared pill now sits below metadata at6px top inset rather than over artwork. Sync failed metadata uses destructive medium text and retains the server's explanation as a tooltip. Empty New card uses the20px plus/13px medium caption and native dashed16px frame.
5. Owner menu: persistent32/36px More trigger,10px chip radius,16px Lucide ellipsis/white foreground and current dark hover/open fills. Retains the sole `ContextFlyout` owner and opens through the visible trigger. Sync/share reflect pending state on reopening; Home/share descriptions remain accessible. Delete and unshare consequence copy now matches current personal collection source, including rows other profiles made from a shared collection. Reorder handle resets retain the vector grip.
6. Personal/shared sections: visible counts and current owner-only/read-only notes; shared owner initial20px with primary20% tint, muted “by” and semibold foreground name. Source board3/5/7 columns and14/20px gaps and ownership partition remain unchanged.
7. Server board: See all is in the section header and preserves its exact library Collections route. Pin is top-left26px/12px radius with14px icon, correct pinned/background fills/foreground and tooltips; hidden pin is not a pointer target, focus and pointer departure refresh visibility. All-library deduplication and no-library/no-pin behavior for repeated collections remain intact.
8. Current list shell/loading: content caps at1000px inside existing16/24/40px outer gutters; title follows32–48px clamp,800 weight,−0.05em tracking and0.95 leading; subtitle14/16px and1.7 leading. Header remains mounted during loading; personal/server loading both use seven real responsive2:3/16px poster skeletons with10px caption inset. Removed the old six96px tiles and duplicated server skeleton rows.

## Source proof

All paths below are under `web/src` at the pinned public commit:

- `components/collections/NewCollectionPicker.tsx`: type-card body, Stage, poster palettes, offered-source/loading/error/off branches,3.5 gap and footer copy.
- `components/calm/StepDialog.tsx`: choice width, narrow sheet, footer and close roles.
- `components/CollectionTemplateGallery/CollectionTemplateCard.tsx` and `components/ui/radio-group.tsx`: current PickRow, source tag, schedule description, ring/dot and selection surfaces.
- `components/collections/CollectionPosterCard.tsx`: caption/meta/tag placement, media frame, persistent owner menu and top-left sidebar-pin roles.
- `components/collections/CollectionActionsMenu.tsx` and `components/calm/ActionMenu.tsx`:32/36px poster trigger overrides,10px radius,16px Ellipsis, syncing/disabled state and help.
- `pages/Collections.tsx`: Personal/shared ownership sections, counts/notes,20px OwnerInitial, responsive poster columns, empty dashed card, seven skeletons and server header/pill/dedup/one-row behavior.
- `components/calm/CalmPage.tsx` and `app.css`:1000px content, heading clamp override and page-title/subtitle weight/tracking/leading/gutters.
- `lib/collections/copy.ts`: personalDeleteDescription, unshareConsequence, source Home/share help.

## Changed files in this pass

- `src/SiloPlayer/Controls/NewCollectionDialog.cs`
- `src/SiloPlayer/Controls/CollectionPickRow.cs`
- `src/SiloPlayer/Controls/WebUiIcon.cs` — only additional `ellipsis`/`user` nodes for this pass; earlier shared-pass changes preserved.
- `src/SiloPlayer/Views/CollectionsPage.xaml`
- `src/SiloPlayer/Views/CollectionsPage.xaml.cs`
- `src/SiloPlayer/Views/CollectionsPage.Posters.cs`
- `src/SiloPlayer/Views/CollectionsPage.Server.cs`
- `tests/SiloPlayer.NativeRegressionTests/CollectionsAcceptanceNativeFixture.cs`
- `tests/SiloPlayer.NativeRegressionTests/BrowseAcceptanceNativeFixture.cs` — collection/chooser/pick/menu/server cases only; other browse cases preserved.
- `tests/SiloPlayer.Tests/CollectionsParitySourceTests.cs`
- `tests/SiloPlayer.Tests/CollectionsInteractionParitySourceTests.cs`
- `tests/SiloPlayer.Tests/CurrentCollectionsParitySourceTests.cs` — obsolete loading and server-caption assertions only.
- This report.

Removed `ShowCollectionTemplateGalleryAsync` and its gallery/config/search helper chain after repository-wide caller inventory found no production entry point. Remaining callers were obsolete native/source tests; those now exercise the current picker or pick row. Compatibility OpenTemplates navigation continues to the current picker. Legacy grouped collection/server builders and their transport source assertions remain untouched.

## Staged coordinated verification

No tests were run. Prepared contracts now exercise:

- Actual visible New button at1280 and dock button at460, all Manual/Smart/Synced destinations, absence of writes before editor selection, shell/card/stage/icon/close geometry and generated captures.
- Synced off, failed capabilities→actual Retry→enabled, and future advertised import source behavior.
- Actual current synced editor search debounce, empty-query restoration of popular picks, late completion exclusion, fresh search and UIA radio selection; actual decoded loopback256px image inside30×45/5px pick crop.
- Existing failed manual add/remove retention, imported Save remaining clean/in editor, local file/drop artwork, Cancel, pending/rejected/retried artwork and Discard remain asserted. Obsolete imported32/48px/header geometry now checks current26/32px, Name minimum36,22px panel and768px shell source roles.
- OWNER_MENU clears fixture profiles, uses one ContextFlyout, invokes the actual persistent More trigger and waits for the real popup before invoking Add to my Home. Its earlier failure has not been reproduced or resolved by execution.
- Server deduplication, actual library pill→header See all→library Collections route, actual own/shared board caption/count/title roles, read-only/missing-profile owner-menu exclusion, full-row selected PickRow/radio/poster geometry.

Text-only inspection: scoped normal `git diff --check` passed; edited/new files had no trailing spaces; CollectionsPage XML parses. This is not C#/XAML compilation or runtime evidence.

## Still open

- Entire combined build/test/native verification remains pending under the user’s one-final-run instruction, including root's ongoing editor Header/Look/Where/Departure/Saved identity changes.
- Measure the real ContentDialog template: full narrow width/bottom alignment, footer columns, title/corner close placement, vertical fit/scroll, initial focus and Escape/Tab. The source adaptation is implemented; it is not accepted visually.
- Verify page/viewport/sidebar width interaction, narrow section-note wrapping, actual board/card widths, physical pointer/touch/focus/reorder states, profile switches and stale mounted menu transitions, server pin success/failure, and dock position above background playback bars.
- WinUI-native menu chrome/help tooltips, poster shadows/backdrop blur and transition easing still require side-by-side visual acceptance; help is accessible/native tooltip rather than a custom two-line WebUI menu template. Current menu functional routes are staged, not executed.
- Root alone owns all editor/core edits and any further shell/playback-bar seams; no MainWindow changes were made here. The apparent Order-combo parent conflict raised during inspection was not a proven defect because shared Field() already detaches the control; it is not counted.

## Coordinated source-assertion follow-up

Root explicitly assigned five additional precise source-shape corrections during the combined verification stage. Updated only the assertions in:

- `tests/SiloPlayer.Tests/CurrentHomeParitySourceTests.cs`: landscape detail navigation must retain content identity and inherited library scope.
- `tests/SiloPlayer.Tests/HomeHeroCurrentParityTests.cs`: primary playback and detail navigation must retain Hero.LibraryId.
- `tests/SiloPlayer.Tests/CodeRabbitFollowUpRegressionTests.cs`: dead weak source entries/failure cleanup use the variant source key; raw byte caching retains the stable artwork key and periodic pruning/pairwise removal remains asserted.
- `tests/SiloPlayer.Tests/RequestBrowseCurrentParitySourceTests.cs`: request finally still clears pending, reenables the button, restores normal content, restores its accessible Request name and refreshes reveal state.
- `tests/SiloPlayer.Tests/MainWindowSourceTests.cs`: current sidebar row minimum43.5px replaces the retired42px literal; surrounding capability/compact behavior assertions remain.

These are coordinated stale-contract updates, not additional product corrections. Scoped `git diff --check` passed for these five files. This agent did not execute tests or builds.
