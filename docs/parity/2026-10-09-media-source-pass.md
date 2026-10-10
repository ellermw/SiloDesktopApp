# Media detail source implementation pass — October 9, 2026

**Resumed final acceptance:** the source-stage/unrun statements below are historical. The current official reference is1a7a3970a9928efb0157460c10888832a8a74eb1. [Combined verification](2026-10-09-combined-verification.md) records the finite source/native/physical evidence, exact1.2.241 payload and native/live boundaries. Correction IDs are reconciled in the central ledger; GitHub publication awaits the user's final installer approval.


Difficulty item **6**, bounded non-admin detail scope. Root fetched current public GitHub `Silo-Server/silo-server` main directly and pinned **22e3a0ba7c1431dda77b957ed1012508d23f2b80** for this pass. Reference reads here used `git show`/`git diff` on that exact object in `D:\SiloPlayer\.codex-tmp\silo-server-current`; the checkout is older/dirty and was not treated as authority. No private GitLab reference was used.

This is implementation evidence, **awaiting the final combined verification**. No build, test run, native host, browser, computer control, installer, production request or credential read occurred in this subtask. The user requested continuous implementation followed by one verification stage. No failure-first runtime reproduction or passing claim is implied by these source changes. The precise Abbott Elementary symptoms remain subject to runtime and paired/physical acceptance.

## Bounded corrections

| Source evidence | Native correction and scope |
| --- | --- |
| Current `detailLayout.css` final desktop rules apply natural height/minimum viewport height to every `.series-detail-viewport`, including season and single-season-series episode grids. `SeasonContent.tsx` uses that class. This extends the older478 multi-season-only source. | `SizeTvViewport` now gives **all series/season** detail natural height above650px at widths>=1024. Short desktop stays bounded and bottom-pinned; narrow detail retains natural flow. The obsolete count-dependent layout authority property/flag was removed, while actual request ownership/loading/empty/error logic remains in use. |
| `SeasonEpisodeGrid.tsx` uses `useGridRowCap(4, episodes.length)` so long seasons scroll their measured remaining rows. The current native grid had no four-row cap. | The episode grid caps itself at the first four actual `RowDefinition.ActualHeight` values, excluding the final8px card margin. One/fewer than five rows and narrow horizontal mode clear the cap; reflow/reset updates it. Layout is rebuilt only when mode/column count changes, rather than whenever natural navigation height changes. |
| `SeasonEpisodeGrid.tsx` reserves the still's16:9 aspect before laying out number/title/description. Native still Height was assigned from a post-arrange `SizeChanged` handler. This is a source-supported layout risk, not a proven reproduction of the user's symptom. | A page-scoped `EpisodeStillAspectPanel` measures the image/overlay grid with a finite16:9 height during the first measure. Caption placement/scroll extent therefore participates in that same measure. Native assertions are staged before any `ChangeView` or delayed layout. |
| No-art `SeasonEpisodeGrid` uses outlined Lucide Play32 with muted foreground/.30. The old native card still had a camera font glyph28. | Page-owned no-art still uses a theme-brush vector Play32/.30. Shared `LandscapeCard` artwork is not rewritten by this subtask. |
| `EpisodeContent.tsx` mounts sibling navigation only while loading or when results have>1 episode. CSS has a one-row rule when no navigation exists, content-sized navigation for populated desktop,4px title gap, and responsive240/180/140/160px cards. Native retained the wrapper/padding and a fixed navigation-row allocation after hiding the section; missing series/season scope also left a visible skeleton. | The outer navigation shell collapses and its second-row reserve disappears when every navigation section is hidden. Missing scope, empty/single success and failure now consistently clear sibling presentation and pending snap state. Loading stays visible; populated siblings retain navigation. Native desktop episode **full viewport height and the source's short-landscape column declaration are preserved**, so the reported gap is not “fixed” by shortening the hero contrary to source. The hero's navigation-specific height cap is removed when there is no navigation. Row size is content-driven with a bounded desktop navigation maximum. Supporting Media locations padding remains40px desktop/28px narrow. |
| Scoped current CSS: sibling header4px; Embla padding16px desktop,4px narrow with2px leading inset; card widths240px ordinary desktop,180px short desktop,140px short landscape<=450px,160px narrow. | Scoped sibling presentation applies those widths/spacing/insets and uses the same current width for initial snap. Root owns an opt-in `LandscapeCard.SetCardWidth(width, minimumWidth=180)` extension in shared item5; these TV callers request minimum140 while other callers retain the180px floor. This is a required shared compilation/runtime prerequisite until root lands it. |
| Current `SeriesContent.tsx` passes lead jobs Creator then Director to `HeroCrewLine`, and supporting jobs Creator/Director/Writer/Producer to `CrewList`. | Hero credit prefers Creator with Director fallback; supporting series crew gets a Creator row and clears it on other types. Other supporting jobs remain retained. |
| `itemDetailLayout.ts` now formats singular episode counts, including season progress. | A one-episode season reads `1 episode` or `1 of 1 episode`; plural counts remain plural. |
| Current Season/Episode `WatchedActionBar` passes `canAddToCollection={false}`. | Native More no longer shows Add to Collection for those two types. Movie/series and existing book workflows remain eligible according to their existing surfaces. |

`AudiobookContent.tsx`, `MangaContent.tsx` and `EbookContent.tsx` production source has no478-to22e3 diff. Existing narration/chapters, manga collapse/sticky/resume and book-related/read-target acceptance is retained. No book workflow was recreated or declared newly accepted. Relative-font changes in common detail components remain coordinated with shared typography item5. Root's C153 match replacement-ID handling in `ItemDetailPage.xaml.cs` was preserved.

## Staged assertions for root's one combined run

New native selector: `SILO_NATIVE_TEST_ONLY=media-parity`, `SILO_NATIVE_TEST_MEDIA_ACTION_CASE=tv-first-caption`.

- Uses hidden, isolated true native client windows at1440x900,1024x651,1024x650,900x700,460x720. No personal data/credentials.
- Mounts an actual season page with one episode, then checks number/title and desktop description bounds **before scrolling** against both the inner episode scrollport and outer TV navigation. Checks first-measure16:9 geometry and no-art Play dimensions/opacity. Narrow source deliberately hides supporting episode metadata/overview; it is not asserted visible there.
- Changes to26 episodes to require a four-row cap, asserts the fourth row stays whole/the fifth begins outside the cap, then returns to one episode and requires the old cap to clear.
- Uses actual named sibling controls for loading/empty/single/multiple/error geometry; actual `LandscapeCard` children for populated responsive widths. Checks hidden-rail second-row reserve, Media locations28/40px supporting gap and unchanged full desktop episode viewport. This is a **geometry fixture**; it does not simulate real API failures or prove API response authority. Existing owned API/load fixtures retain that responsibility.
- Exercises actual hero/supporting crew/menu formatters for Creator preference/Director fallback, Creator clearing on non-series, collection eligibility and singular season progress.

Existing `latest-media` is extended, not reimplemented: its current natural-flow contract now includes single-season series; it exposes real5-season/3-episode navigation, expands actual long overview copy, and requires the tall viewport to **grow**. The old hidden24px-padding “natural rail” acceptance is not reused. Decoded-logo and ordered-score screenshots are captured at their actual states before replacement by the empty-ratings fixture. Existing title-art/ratings behavior checks remain retained.

`tv-copy`, `tv-populated`, `tv-landscape`, original first-navigation/series-loading gates and the new selectors still require root execution against the combined candidate. Compilation, unit/native results, original season/extras paint roles, matched current WebUI pairs, actual Abbott initial paint and precise sibling Media locations spacing/physical interactions remain unverified here. No symptom/root-cause or full-page100% claim is made.

## Shared playback follow-up: Shuffle, not implemented by this subtask

Root explicitly owns reusable Shuffle under difficulty1 and will wire these detail gates afterward. **Do not mark the missing transport or detail action done.** Exact current source seams:

- `SeriesContent.tsx`: `episodeCount = seasons.reduce((sum, s) => sum + s.episode_count, 0)` from the loaded season response; show More **Shuffle** only when `episodeCount > 1`; invoke `{ kind: "series", id: item.content_id }`.
- `SeasonContent.tsx`: `playableEpisodes = episodes.filter(episode => (episode.files?.length ?? 0) > 0)`; show Shuffle only when `playableEpisodes.length > 1`; invoke `{ kind: "season", id: item.content_id }`. It is not gated by total episode_count or just list length. The single-season series page still uses the series scope.
- `components/ActionBar.tsx`: Shuffle is an overflow item and contributes to overflow-action visibility. Season/Episode collection eligibility remains false independently.
- `api/v2/shuffles.ts`: create `POST /api/v2/shuffles?image_size=large`, body `{ "scope": { "kind": "series|season|library|collection", "id": "..." } }`; read `GET /api/v2/shuffles/{shuffle_id}?image_size=large`; advance `POST .../{shuffle_id}/advance?image_size=large`, body `{ "from_content_id": "..." }`; skip `POST .../{shuffle_id}/skip?image_size=large`, body `{ "next_content_id": "..." }`; delete `DELETE .../{shuffle_id}`. Read/advance/skip response is Shuffle; identical advance/skip is idempotent per source comments.
- `hooks/queries/shuffles.ts`: first playback uses `shuffle.current.content_id`, `shuffleId: shuffle.id`, `restart: true`, and returnHref of current pathname+query.409 create failure reads `Nothing here can be played.`; other failure `Couldn't start shuffle.`. Shuffle details are read fresh (`staleTime:0`) and refreshed when post-roll opens. Native must reject late results under item/profile/navigation authority and retain normal return navigation.

Admin-only **Seek Previews** remains the separately documented audit gap from `2026-10-03-media-finalization.md`. Root explicitly excluded new admin APIs/dialogs from this established non-admin pass. Nothing here certifies that admin gap or Account title-art write behavior.

## Changed files

- `src/SiloPlayer/Views/ItemDetailPage.xaml`
- `src/SiloPlayer/Views/ItemDetailPage.xaml.cs` (contains preserved root C153 changes too)
- `src/SiloPlayer/Views/ItemDetailPage.EpisodeLayout.cs` (new)
- `tests/SiloPlayer.NativeRegressionTests/MediaFirstCaptionNativeFixture.cs` (new)
- `tests/SiloPlayer.NativeRegressionTests/MediaParityNativeFixture.cs` (one selector dispatch)
- `tests/SiloPlayer.NativeRegressionTests/MediaLatestNativeFixture.cs` (current contract/stronger populated states/captures)
- `tests/SiloPlayer.Tests/ItemDetailCurrentParityTests.cs` (existing source expectations updated for measured responsive inset/singular formatter)
- This report.

No ledger edits, correction IDs, commits, release numbering or installers were made in this subtask. Root owns those after deduplication and combined verification.
