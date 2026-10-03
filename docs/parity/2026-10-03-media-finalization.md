# Media finalization evidence — 2026-10-03

This is a finite read-only review of the existing October 1 media findings and the separate latest-main delta. Only this document was changed. Product source, fixtures, builds, runners, the live application and credentials were not touched.

Official public GitHub main was fetched by root on October 3 and remains `478afa5257332df52f10650cd00b88596562d4c9`. Exact Git objects in `D:\SiloPlayer\.codex-tmp\silo-server-current` were used. Original acceptance references remain pinned to `8e2e840474a085c6df6571a5a2850f7eb996810c`. An old-pin capture cannot certify a new478 behavior. The historical implementation journal is `docs/parity/2026-10-01-media-implementation.md`; its earlier pending/failed statements are superseded only by the explicit later evidence below.

## Accepted evidence to retain

All paths below are relative to `C:\Users\Michael\.codex\worktrees\parity-top-three\SiloPlayer`, unless another root is stated.

| Accepted scope | Existing evidence | What it establishes |
| --- | --- | --- |
| First real detail navigation, Coven blank-page regression, attachment and Back revisit | `.codex-tmp/native-first-navigation-green27b.log` | Movie/series/season/episode, unloaded first metadata, attachment, and revisit at1280/460. Root owns the sizing lifecycle repair; do not reopen it. |
| Unknown/stale/empty season authority and known single-season first/settled composition | `.codex-tmp/native-series-loading-green27b.log` | Root's accepted loading repair. Do not reinstate assumptions from the old hidden-section fixture. |
| Populated TV collections | `.codex-tmp/native-tv-populated-green27.log` | Real five seasons/three episodes, progress states, mounted extras, five true clients. Tall multi-season1440x900 has natural Height; short1024x600 remains bounded; narrow navigation is below the hero. This does not separately assert the stronger original paint roles or latest long-content growth. |
| Original TV copy and short desktop composition | `.codex-tmp/native-tv-copy-25.log`, `native-tv-green24.log`, `native-tv-landscape-green24.log` | Copy inset, stable gutter, title spacing/line budgets, and bottom-pinned1024x600 composition. |
| Actual EPUB/PDF renderer, settings body split, nested TOC | `.codex-tmp/native-reader-visible-green19.log`, `native-reader-panel-green19.log`, `native-reader-toc-green19.log` | Real WebView2 CFI/bookmarks/progress, PDF visible white/ink raster, stale authority rejection, PDF narrow312/312 versus EPUB full624, and nested publication labels/depth/href navigation. Actual PDF content is an IMG, not a script-disabled canvas; historical capture filenames still say canvas. These functional/major bounds checks stay closed. |
| Native volume input and pixels | `.codex-tmp/native-volume-visible23c.log` | Expanded/mini actual UIA0/50/100, events1/2/3, fill0/48/96x2, bitmap96x2 and white pixels0/92/188. No further RenderTargetBitmap-only fault inference is justified. |
| Listening sizing and mounted media layout | `.codex-tmp/native-listening-green23c.log`, `native-media-layout21.log` | Existing actual natural-fit/remount and responsive layout acceptance. |
| Finite original functional groups | `.codex-tmp/native-media-final15b.log` | Manga sticky/collapse/resume; Party mode/start/retry/authority/sheet/paste; audiobook settings/narration/real player transport; RequestDetail actions/polling. Final `MEDIA_PARITY_ALL_ACCEPTANCE_COMPLETED` plus successful process supersedes older unexecuted journal entries. |
| Latest non-admin media batch | `.codex-tmp/native-latest-media26b.log` | Pending/false/true/default/stale-profile title art, decoded logo/accessibility, ordered unknown/TMDB/RT entries and absent-ratings hiding, official TMDB nonempty pixels, finite primary-card formatter, actual Party detail pills, and rail mode boundary properties. Limits below remain. |

Root reported user acceptance of published27b's latest interim detail and retained OSD. Preserve that acceptance. This document does not turn it into a full-page visual-parity claim. Physical audible theme/speech and HDR/fullscreen/letterbox observation remain device acceptance where requested; they are separate from the source repairs below.

## Original finite acceptance still needed

Native media dispatch uses `SILO_NATIVE_TEST_ONLY=media-parity` and `SILO_NATIVE_TEST_MEDIA_ACTION_CASE=<selector>`. Root owns execution. A selector listed as proposed below does not exist yet.

| Existing selector / finding | Latest meaningful evidence | Bounded next step |
| --- | --- | --- |
| `party-presentation` / V10,V11 | `native-party-25.log` and `native-party-diagnostic25.log` remain the newest stronger presentation runs. Diagnostic directly awaited render and proved the source-less glow0x0 versus card436x374/450x422; earlier GREEN24 predates stronger line/role assertions. | Run current published source once at1280/600/500. Current source now sizes glow from card interior and clears both render targets Transparent; Invite uses actual preferred-width fit and0 wide/12 wrapping RowSpacing; named/visible locators avoid hidden duplicate text. Require rendered nonzero glow alpha, typography/row bounds,58px fitting Invite at600/wide, genuine narrow wrap, Code icon order, count capsule/check/status, and retained shelf authority. Then compare actual hub/room PNGs to the existing source pairs. Do not diagnose the corrected compact headline newline or a hidden Riley duplicate as product defects. |
| `tv-copy` / V02,V03 original paint roles | `native-tv-roles-red25.log` proved season12 instead of16; extras8/Secondary/raised disc/outlined font glyph instead of12/Surface/Muted/filled16. Initial run aborted on inapplicable hidden single-season cards; current fixture skips those. No later `tv-copy26/27` result was found. | Run the corrected applicable season roles plus extras in both variants at all five original sizes. Current source has season16 Surface and extras12 Surface/border,36 Muted disc and filled16 Play. Root's populated27 count/layout pass is retained, but is not this role assertion. |
| `reader-panel` extended, or proposed `reader-controls` / existing V07 + shared controls | Independent saved-image inspection below reveals concrete residuals despite passed body split. | Add only rendered control bounds/state assertions at460/1280, repair owned reader controls, and rerun that bounded case. Keep CFI/TOC/settings save/recovery functional groups closed. |

### Concrete reader control residuals from existing paired images

Use the directreload settings references; the file named `reader-epub-settings-matchedcfi-460-720.png` actually shows the Contents empty view and cannot certify Settings. Both clients use the same publication bytes/location, but their browser scrollbar widths differ; do not infer offsets by scaling screenshots.

1. `Views/EbookReaderPage.xaml:157` and the two sibling profile Buttons set `HorizontalContentAlignment=Stretch` but leave outer alignment to the shared left-aligned Button default. Actual native buttons are content width; source profiles fill the panel. Official `web/src/pages/EbookReader.tsx` uses `w-full`, `min-h-11`, `px-3 py-2`, active secondary/outline style, `aria-pressed`, and a16px active check. Native `Profile_Click` applies/saves values but does not update active presentation. Require full available widths and active/inactive presentation after profile/custom/reset changes.
2. Four native tabs at `EbookReaderPage.xaml:115` similarly paint content-width controls within star columns; source tabs fill four grid cells, are40px high, use14px icons, and switch secondary versus ghost paint. Native `ShowPanel` only changes opacity. Repair tab outer stretch/40px bounds and selected/unselected source roles without changing handlers.
3. Native `EbookReaderPage.xaml:70` reserves42px for progress, but `EbookReaderPage.xaml.cs:597` puts `Page 3 of 3 · 67%` there for PDF. The saved native PNG clips it to `Page 3 o`. Official `formatReaderProgress` supplies percentage-only to the44px progress slot for every format. Keep any useful page detail in an accessible tooltip/other suitably sized surface; match the visible percentage slot.
4. The same actual narrow pair shows native toolbar buttons34/36px with persistent outline/Surface fills versus source32px ghost controls and a32px outline File action (`EbookReader.tsx` header). Native bookmark is a star (`E735`) rather than the source bookmark outline; ruler uses the alternate text-looking font glyph. These belong to the existing shared icon/control findings, not new finding counts. Native input/hit area can be retained independently of the visible32px surface.

No blank PDF diagnosis follows from the WinUI chrome PNG: WinUI bitmap capture omits WebView pixels. Pair the separate `media-reader-document-*` preview too. The real bitmap/ink and major narrow panel split already passed.

### One remaining TV icon role exposed by the current27 pair

No-art episode cards in actual27 render a28px video-camera font glyph (`ItemDetailPage.xaml.cs:7784`, E714). Official unchanged `SeasonEpisodeGrid.tsx` uses32px outlined Lucide Play at muted-foreground/30. The narrow paired PNGs make this difference concrete. Repair this placeholder in the existing V02/shared-icon scope; it is separate from already repaired filled Play on extras. Root owns ItemDetail. Uppercase eyebrow is a478 change and must not be declared wrong against the old8e2e sentence-case screenshot.

## Latest478 finite delta and concrete mismatches

### Title-art settings capability guard — concrete source repair

`ViewModels/SettingsViewModel.TitleArt.cs:38` sets support with `ApiVersion==1 && Revision>=16` only. Official `hooks/queries/settingValues.ts::settingsCapabilitiesSupportKey` additionally requires `supports_batched_effective===true`; `pages/settings/TitleArtSettingsGroup.tsx` hides the group unless that predicate passes. Native `SettingsContractCapabilities` already exposes `SupportsBatchedEffective`. A revision16 server advertising false/missing batched-effective currently exposes and requests the native control incorrectly. Add that flag to support detection and assert unsupported means hidden/no effective fetch/no write. This is a concrete latest settings mismatch, not an absence-of-test inference.

Current scope/write implementation otherwise follows the reviewed official `hooks/useTitleArt.ts`: profile first, profile_device when per-device, device PUT before profile DELETE when disabling all-devices,404-tolerant clear, captured context before delete, and authoritative key-specific event after success. `native-account26.log` covers older account parity; it contains no title-art control/write assertions. No existing native Account fixture references `TitleArt`. Coordinate the finite capability+scope/rejection/stale write/event case with Account/root instead of treating media26b's read test as settings-write acceptance. Test successful event re-read on a mounted detail page and no event after rejected/stale writes.

### Admin detail Seek Previews — implementation absent

This is the separate admin-only delta, not part of the non-admin title-art/ratings batch. Native `src` contains no `trickplay`/`Seek Previews` implementation or API methods; current detail More flyout lacks it.

Exact478 source seams:

- `MovieContent.tsx`, `EpisodeContent.tsx`, `SeriesContent.tsx` call `useLibraryCapabilities(isAdmin)` and require both `trickplay===true` and `trickplay_supported===true`.
- `components/ActionBar.tsx` shows GalleryHorizontal16 `Seek Previews` only for admin, both flags, and nonempty contentId; opens `components/admin/trickplay/TrickplayStatusDialog.tsx` with versions.
- `hooks/queries/admin/libraries.ts`: `GET /api/v2/libraries/capabilities`, disabled for non-admin, no retry, indefinitely fresh for the current query authority.
- `hooks/queries/admin/trickplay.ts`: status `GET /api/v2/admin/items/{id}/trickplay` only while dialog open, `files`; poll every5s only while pending/running; no retry. `POST /api/v2/admin/items/{id}/trickplay/regenerate` has **authentication replay disabled** and no automatic retry. Success reports returned `requeued` and invalidates item/library status.
- Dialog labels files by matching version file_name, quality summary, or `File {id}`; shows off/waiting/in-progress/ready/failed and retrying-after-failures labels, count/width/interval/bytes/generated timestamp/error. Servable pending/running retains-current-preview copy. Loading/error and all-off helper are distinct. Make Again is disabled until successful status, when all files off, and during mutation. Close remains available.

Proposed new selector `detail-trickplay` must first show the current missing-action RED. Finite cases: role/flag/contentId gates, no eager status read, dialog loading/error/empty/off and mixed files, pending/running poll stops on close/navigation/profile change, queued0/N success, mutation failure retained dialog, and exactly one non-replayed regenerate request. Root must decide/admin-own this explicitly; do not silently mark current-main admin parity complete or duplicate unrelated admin-library work.

### Latest non-admin evidence limits, not proven defects

`latest-media`26b passes its asserted states and stays accepted. Its title/rating screenshot is captured **after** the empty-ratings item replaces the ordered-rating item; the filename `media-latest-title-ratings-1280.png` does not provide a visible ordered ratings/decoded-logo pair. Add captures at the already exercised true-logo and ordered-score states, rather than rerun/reinvent their data behavior.

The same natural-rail fixture builds seasons but never exposes the real SeasonsSection. Tall multi navigation is24px (padding only), so its `ActualHeight >= DesiredSize` check cannot establish whole visible rail or long-content growth. Populated27 proves five visible cards, while the latest long-copy case remains a finite evidence gap. Use real loaded5/1-season flows at1024x650/651 and1440x900, enough copy/rail content to exceed the viewport, and a count/type/size transition. Compare with exact478 source DOM/PNG. Do not accept hidden content as natural-flow proof or reopen root's already passed first loading gate.

Optional extensions to the same bounded fixture cover460 pending80x420, late item/image decode authority, and empty/rejected/stale Party candidate ratings. Current source candidate detail has revision/context/candidate-reference/visible/IsLoaded guards and failure isolation; no product fault is demonstrated for those unexecuted variants. Type-only eyebrow should omit the dot; rich episode breadcrumbs remain separate. These are final state coverage, not a fresh catalog audit.

## Existing capture inventory for root's final pairs

`R = .codex-tmp/media-reference-captures/`; `N = .codex-tmp/native-artwork-tests/`. All listed files exist. Original R references are8e2e; a fresh exact478 source pair for changed hero/ratings/natural flow is still needed.

| Pair | Original R files | Native N files / evidence status |
| --- | --- | --- |
| TV both collections/five clients | `tv-populated-{seasons\|episodes}-8e2e-{1440x900\|1024x600\|900x700\|600x450\|460x720}.{json,png}` | `205b325d828f4fef860147db0b03b193/media-tv-populated-{seasons\|episodes}-{size}.png` and `media-tv-populated-extras-{seasons\|episodes}-{size}.png`; actual27 successful results.txt. |
| Focused season/extras | `tv-populated-extras-seasons-8e2e-460x720.png` | Same actual27 folder, `media-tv-populated-extras-seasons-460x720.png`. Independently inspected this pass; role repairs are visible, stronger native role gate still needed. |
| No-art episodes | `tv-populated-episodes-8e2e-460x720.png` | Same actual27 folder, `media-tv-populated-episodes-460x720.png`; independently inspected, confirms camera-versus-Play residual. |
| Party hub/room1280/500 | `party-{hub\|room}-correctcss-dom-{1280\|500}-720.{json,png}` | `92f2435cdaac48269a497239eba73020/media-party-presentation-{hub\|room}-{1280\|500}.png` is25 diagnostic RED, **not a repaired green pair**. Older24/23c folder `5345214ac4844820841e61652e68b8dd` proves earlier geometry only. Source600 Invite DOM is recorded in implementation journal (568x57.33, same-row policy); no600 PNG/JSON pair was found. |
| EPUB/PDF publication1280/460 | `reader-{epub\|pdf}-matchedcfi-{1280\|460}-720.{json,png}` | `84cbea95f6e948b28ffe86b4183f41f7/media-reader-populated-{epub\|pdf}-{width}.png` plus `media-reader-document-{epub\|pdf}-{width}.png`, reader-real19 successful results. |
| Actual narrow Settings | `reader-{epub\|pdf}-settings-directreload-460-720.{json,png}` | `dff8f73641e043a1b813a6e3b4b09b59/media-reader-settings-{epub\|pdf}-460.png`, reader-panel19 successful results. Independently inspected; concrete controls above. Use separate document preview for PDF ink. |
| Wide reader Settings | `reader-{epub\|pdf}-settings-matchedcfi-1280-720.{json,png}` | Same dff8 folder,1280 variants. Verify visible selected panel before using a historical filename as proof. |
| Nested TOC / consolidated documents | Existing matching publication references; nested publication fixture bytes are distinct from flat original sample | `58517e998c344d7d86f8813728547e23/media-reader-*-epub-toc-{1280\|900\|460}.png` plus ordinary EPUB/PDF captures; successful actual nested-TOC results. Do not compare nested contents to a flat source book and call it a layout fault. |
| Audio/mini1280/500 | `audio-correctcss-{1280\|500}-720.png`, `mini-correctcss-{1280\|500}-720.png` | `e5823a93bc32460982b47e81484dce23/media-audiobook-{expanded\|mini}-{1280\|500}.png`; latest existing original layout loops. Keep actual volume23c proof separately. |
| Latest media native | No matched exact478 source PNG set found | `eb5dffe2909640ce9827535df728e776/media-latest-party-ratings-1280.png`, `media-latest-rail-{1024x650\|1024x651\|1440x900}-{single\|multi}.png`, `media-latest-title-ratings-1280.png`;26b successful results with limits above. |

Final closure requires only the bounded original control/icon corrections, the two current-source stronger gate/pair confirmations, the latest settings capability/write/event case, the populated long-flow/latest visual pairs, and separately authorized admin Seek Previews. Existing passed behavior is not an invitation to rerun the entire audit. Findings remain deduplicated within the original V/control/icon inventory; no100% or rendered-complete claim is made here.
