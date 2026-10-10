# Shared controls current-source pass — October9,2026

**Resumed final acceptance:** the source-stage/unrun statements below are historical. The current official reference is1a7a3970a9928efb0157460c10888832a8a74eb1. [Combined verification](2026-10-09-combined-verification.md) records the finite source/native/physical evidence, exact1.2.241 payload and native/live boundaries. Correction IDs are reconciled in the central ledger; GitHub publication awaits the user's final installer approval.


Status: **bounded source implementation checkpoint; final combined verification pending**. This does not establish rendered or physical-state acceptance for difficulty5.

The coordinator fetched official public GitHub main directly and pinned **22e3a0ba7c1431dda77b957ed1012508d23f2b80**. WebUI reads use `git show` at that commit in `D:\SiloPlayer\.codex-tmp\silo-server-current`, rather than its dirty checkout. The icon nodes come from the locally installed official `lucide-react`0.576.0 package under that reference's `web/node_modules/lucide-react/dist/esm/icons`; pinned `web/package.json` declares `^0.576.0`. No GitLab, production API, credential, browser, computer-control or app-launch access was used. No build, test or publish command was run. Existing dirty changes remain retained.

## Concrete corrections

Seven proposed distinct corrections, subject to coordinator deduplication. Individual properties, four icon names, and extra assertions do not each add another count.

| Proposal | Correction | Pinned source proof |
|---|---|---|
| S5-1 | External request/watchlist title uses14px semibold; visible captions always retain11px metadata even with the title-only card preference. Artwork-only hides the empty caption margin, while an optional status child keeps the source12px top inset. | `components/RequestPosterCard.tsx` `showMetadata=showCaption`, `showCaption || children`; `components/MediaCardArtwork.tsx` shared title/metadata/caption classes. |
| S5-2 | External artwork frame uses the current computed16px radius,65% border color, and an explicit composition overflow clip around the inner artwork. | `app.css` `.media-card-image`: `--radius-xl`, border65%, overflow hidden; theme base radius12px plus4px. Existing shared `PosterCornerRadius` is already16 and is preserved. |
| S5-3 | Request/attention artwork applies brightness.85 and saturation.8 without reducing image alpha. Normal and dim bitmap sources have separate weak-cache identities; raw artwork bytes still share the existing image cache. | `components/MediaCardArtwork.tsx` dim image filter classes. Native `ArtworkEffects` uses the existing Win2D infrastructure. |
| S5-4 | Unrevealed external request/bookmark buttons suppress pointer hit testing, reveal on keyboard focus, and retain the pending reveal. Pending request accessible name changes to `Sending request for {title}` and restores afterwards. Request plus/library strokes use2.5/2.4 source widths. | `app.css` `.media-card-action-trigger` / `.media-card-play-trigger`; `components/RequestPosterCard.tsx` `RequestAction` / `LibraryChip`. |
| S5-5 | External bookmark geometry follows actual window viewport:24px button/12px icon/6px inset below640,32px/16px/10px above. Rounded-md is10px; fill uses background80%, border border20%; tooltip follows Add to Watchlist/On Watchlist. XamlRoot change subscription detaches on unload. | `components/mediaItemMenuTrigger.ts` standard poster role; `components/RequestPosterCard.tsx` corner classes and `WatchlistAction`. |
| S5-6 | `WebUiIcon.Create` now renders users, list-ordered, wand-sparkles, grip-vertical instead of falling back to plus. Existing icon mappings remain intact. Optional fourth stroke-width argument preserves existing calls. | Current Collection chooser/shared-pill source uses these Lucide names. Exact nodes read from official local0.576.0 package files. |
| S5-7 | Shared readability uses current1.125/1.25 root text scales, scales explicit TextBlock line heights, maps strong body/medium/semibold/bold roles to520/600/700/800, and captures descendant baselines before mutating an ancestor to avoid inherited scale compounding. | `app.css` `html[data-text-scale]`, `html[data-text-weight]`, semantic weight variables/classes. Root explicitly assigned the scoped `AccessibilityService` change. |

## Files changed in this pass

- `src/SiloPlayer/Controls/ExternalTitleCard.cs`
- `src/SiloPlayer/Controls/WebUiIcon.cs`
- `src/SiloPlayer/Converters/UrlToImageSourceConverter.cs`
- `src/SiloPlayer/Helpers/ArtworkEffects.cs`
- `src/SiloPlayer/Services/AccessibilityService.cs`
- `tests/SiloPlayer.NativeRegressionTests/SharedCurrentNativeFixture.cs` (new)
- `tests/SiloPlayer.NativeRegressionTests/SharedAppearanceNativeFixture.cs`
- This report.

The first two controls already contained dirty work, which is preserved. No MainWindow, Settings/account, theme palette, Collection chooser, Hero/NowListening, LandscapeCard, QueryRules/QueryFilter or shared ledger edits were made by this pass.

## Accepted source roles retained

Current switch source explicitly uses foreground for the off thumb and primary-foreground for the on thumb; current native theme follows both roles. Historical C05's earlier background-thumb description is stale. Default36px buttons,32×18.4 switch/16px thumb, slider geometry, card24px padding, shared computed corner aliases, shared menu rows, QuickSearch C098/C099, and the accepted overlay preset geometry are retained. Current shell logo112×48, mark36, icon18, sidebar surface260 and rail64 match source. Existing MainWindow constructor already replaces sidebar Fluent glyphs with the corresponding SVGs.

## Coordinator integration seams

1. Coordinator wired `SILO_NATIVE_SHARED_CURRENT=1` to `SharedCurrentNativeFixture.RunAsync(parent)` in the shared native runner. Program ownership stays with the coordinator.
2. MainWindow.xaml `NavigationViewItem` style previously used corner12 and MinHeight42. Coordinator now changed these to16/43.5, matching `AppSidebar.tsx` rounded-xl,13px text with normal1.5 line height and12px vertical padding. Preserve fixed18px icon/260px surface/64px rail and browser-default640/1024 media-query boundaries while evaluating text/padding scaling. MainWindow ownership remains with coordinator.
3. SearchPage.xaml.cs request heading previously used `Request to Add` / `Not in your library, but you can request`. Coordinator now changed the grid heading to current `Request to add`. The separate source no-library-hits sentence is `Nothing in your library matches “{query}”.` Coordinator owns this seam; QuickSearch's accepted request header is a different source role.

## Prepared final verification

`SharedCurrentNativeFixture` exercises mounted title-only/artwork caption policy; actual14/11px type/line height;16px/65% frame/clip; hidden pointer targets; keyboard reveal; held pending request authority/copy; bookmark completion tooltip, current icon/button size and radius; rendered icon pixel uniqueness versus plus; and an actual opaque-red PNG effect measurement for brightness/saturation/alpha. `SharedAppearanceNativeFixture` now asserts25px/37.5px at x-large,22.5px/33.75px at large, restoration without compounding, all four strong weight roles and a realized parent/child inherited font.

Run these with the existing shared-controls, appearance, QuickSearch, Requests, external Watchlist, overlay presets and browse fixtures only at the final coordinated stage. Prepared assertions have not run. `git diff --check` on the changed tracked files is a text hygiene check only.

Final visual/physical cases remain open: normal/large/x-large layouts including fixed versus rem geometry, below/above640 bookmark resize, mouse/keyboard/touch reveal, pending success/failure, custom theme alpha colors, source glass/backdrop blur and shadows, no-poster/expired/failing artwork, dim fallback appearance, hover brightness/scale/lift, menu width/focus/close, shell collapse/expansion and rendered text clipping. The normal converter's existing failed-byte fallback signaling is unchanged and must be exercised; it is not newly accepted by this source pass. Shared rem padding/width geometry needs role-by-role measurements rather than globally scaling every native pixel value.
