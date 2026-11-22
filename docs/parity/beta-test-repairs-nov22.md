# Beta test repairs — November 22, 2026

Branch `t3/beta-test-repairs-nov22` from Beta `12fa06ced`. Versions unchanged. Evidence is in lane `D:\SiloPlayer-build\admin-completion-lane-a`, run labels starting `rep22-`. Synthetic values only.

## A. Native group `oct20-watched` — test defect (stale fixture), no product defect

Failing check: "Both native episode check badges must repaint without navigation" (`WatchedStateOct20NativeFixture.cs`, series case). Reproduced on unchanged Beta: `rep22-base-r1`.

Cause, found with a temporary trace of the page after the mark (removed): the fixture's "series" had two seasons. Since #69 (`33d26065e`, 2026-11-04) a series page that does not have exactly one season lists its seasons and draws no episode rows: `ItemDetailPage.xaml.cs:8133` (`RefreshEpisodeCards`) and the matching guard in `LayoutEpisodeGrid`. The episode panel had 0 children, so there were no badges to count. The check was also hollow in one place: with no server-named play target, the page reads the primary season's episodes to find one (`ApplySeriesActionAsync`, `ItemDetailPage.xaml.cs:4864`) and the fixture's server answered that read with an empty list, so "every loaded episode is played" held over an empty list. Two more stale expectations showed once those were fixed: the episode quick button is now named by the catalog, `Mark Watched` (#145, `3e25c8641`, 2026-11-14), not `Mark Episode Watched`; and a one-season series reads its episode list from the server, which the fixture did not serve.

Fix (fixture only): the series case is split into `series` (two seasons: asserts that no episode rows are drawn, plus the season completion check) and `series-single` (one season: asserts both episode badges, the season check, the child quick mark and the partial season card). The series item names its play target as the current server does, and the fixture server now serves the season's episodes with the played state the writes left. Nothing was deleted or loosened.

Which change broke it: the multi-season guard arrived in #69, the label in #145. An older note in `docs/current-development.md` says the group passed at #69; that was not rebuilt and is not confirmed here, so the exact last passing commit is not established. Not changed, noted: on a server that names no play target, marking a multi-season series watched from a season view makes the legacy fallback load the next season's episodes into the model (design of #69).

After: `rep22-final-r1` oct20-watched 5 PASS, 0 FAIL.

## B. Native group `realtime-series-state` — one product defect and four stale fixture expectations

Failing check: "Mounted series season-count badge did not refresh".

Product defect: the series page's count badges read `1 Season` / `6 Episodes` instead of capitals. #145 (`3e25c8641`) replaced `$"{n} SEASON(S)"` / `EPISODE(S)` with the catalog messages `web.detail.badges.seasons` / `.episodes` and dropped the upper-casing; the official `.metadata-badge` is `text-transform: uppercase` (`web/src/app.css:1828` at `93794454b`). A viewer saw the badges change from `1 SEASON` to `1 Season`. The page did refresh the count from the realtime event; only the case was lost. Fix: `ItemDetailPage.xaml.cs` lines 1084, 1086, 1092, 1907, 1918, 1923, 8726, 8728 wrap the catalog text in `AppStrings.Uppercase` (language-aware, as other upper-cased labels do).

Further failures behind it, fixture or fallback text, fixed one at a time because each only appeared once the previous one passed:

- `RealtimeSeriesStateNativeFixture.cs:973`: `ReloadContainerMetadataAsync` gained a second parameter (`structural`) in #192 (`3d9b64224`); the reflection call passed one argument ("Parameter count mismatch"). It now passes the default.
- `:1081`, `:1090`, `:1110`: the busy episode-translation label is the catalog `Translating…` (not `Translating descriptions…`) and the idle label is the catalog `Translate` (not `Translate description`), both from #145. Same cause as the November 21 `episode-overview` repair.
- `ItemDetailPage.xaml.cs:3506`: the not-yet-opened menu item carried the literal `Request seasons`; the catalog and the official menu say `Request Seasons`. The literal is only the fallback before the menu opens, but the fixture reads it there. Literal changed to `Request Seasons`.

After: `rep22-final-r1` realtime-series-state 63 PASS, 0 FAIL.

## C. Native group `admin-configure-providers` — test race, no product defect

Fixture: `AdminProvidersNativeFixture.cs`, checks "error: the load failure is announced" and "error: Reload is reachable".

Cause: the controller publishes its state (`AdminProvidersController.cs:189`, `Loading=false, Error=…` in one record) and then raises `Changed`; the page paints it from a queued render (`AdminProvidersPage.QueueRender` → `Render`). The fixture waited for `Controller.State.Error != null` and read the page at once, so it sometimes read the page before the queued render ran. The page cannot show a failed load without the message and a Reload: `Render` sets the alert, the text and `_reload` from the same state in one pass.

Fix: the load-error scenario waits for the painted error text and a shown Reload before checking, and every scenario that waits for `Loaded` now also waits for the painted MDBList tile. The checks themselves are unchanged.

Evidence (`-Stage native`, one build, results.txt checks; each run also exits with the known 0xC000027B crash, which is not judged): before, 2 of 12 runs failed on exactly the two named checks (`rep22-cbefore-r1`…`r12`; r3 and r4). After, 12 of 12 runs have no FAIL line, 251 PASS each (`rep22-cafter-r1`…`r12`).

## D. Admin > Logging settings at a narrow window — product defect

Cause: below 640 wide a row stacks. `AdminLoggingSettingsPage.Field` moved the control block to column 0 and row 1 but left its column span at 1 and set an explicit width equal to the row's. A star column is the row less the 24 px column gap, so the control was 24 px wider than its slot and the layout cut it at the slot: the select lost its right border and chevron, and so did the Quiet log prefixes and Maximum log entries boxes. (An early guess, that a vertical scroll bar narrows the viewport, was measured and is wrong: the viewport equals the page.) Only 560 was affected; 640, 800 and 1024 already fitted.

Fix (`AdminLoggingSettingsPage.cs:112`): a stacked control block spans both row columns and has no explicit width; its gap and unit column follow the official stacked row (unit text after a full-width control, no empty gap when there is no unit).

The "counts at the right of Advanced rows" are not cut: measured, the digit is 4–7 px wide, fully inside the page, and its rightmost pixel column is blank in old and new captures. In the direct-hosted capture the page has no margin, so the count sits on the page edge; in the shell it sits inside the shell margin. Unchanged.

Native check (`AdminMonitorLogsNativeFixture.LoggingFit.cs`, called from the `admin-monitor-logs` fixture): at 560, 640, 800 and 1024 it waits for the layout to settle (window content at the target width and every text/control rect unchanged for four consecutive polls), expands both Advanced sections, and requires every control and text on the page to lie inside the page viewport as narrowed by any ancestor wider than the slot its parent gave it (the cut the layout applies), with one pixel of tolerance, and no `TextBlock.IsTextTrimmed`. It runs on the page hosted directly and inside the Admin shell: 8 checks. Proof it fails on the old layout: `rep22-dold-r5` (unchanged page, new check) fails at 560 only, in both hosts, naming `ComboBox LoggingField_server.log_level spans 0..552, visible 0..536`, `TextBox LoggingField_server.log_quiet` and `TextBox LoggingField_opslog.max_rows` (shell: 0..520, visible 0..504). Two earlier versions of the check, which compared bounds with the viewport only, passed on the old layout (`rep22-dold-r1`) and were rejected for that reason.

After: `rep22-final-r1` admin-monitor-logs 281 PASS, 0 FAIL. Captures opened: direct 560, 800, 1024; shell expanded 560, 640, 800.

## E. Collection title search item kinds — product defect

`CollectionEditorPage.TitleSearch.cs:55-57` showed the raw server value (`ebook`, `audiobook`, `manga`) for any kind other than movie, series, season or episode. New `SiloPlayer.Core.Localization.ItemTypeLabels` mirrors the official `itemTypeLabel` (`web/src/lib/itemTypeLabels.ts` at `2c75019cb`): the seven kinds the catalog names (`web.search.typeMovie/Series/Season/Episode/Ebook/Audiobook/Manga`) are shown in the interface language by exact match; any other kind is shown as the server sent it. The collection editor keeps its own four labels for movie/series/season/episode (`web.collections.manual.typeLabel`, the official collection editor's own, `Show` for a series) and uses the helper for every other kind. The one-line button padding near line 61, owned by another open pull request, is untouched.

Tests: `ItemTypeLabelsTests` (labels in en/de/fr for all seven kinds, unknown and wrongly cased kinds unchanged, and a source test that the title search uses the helper); they cannot compile or pass without the change. New native group `collections-title-search` (route added to `Program.cs` beside `synced-sort-direction`; it runs the existing manual title search case, which no named group reached before) with a new check that ebook, audiobook and manga rows read `2024 · Ebook`, `2023 · Audiobook`, `2022 · Manga` and an unknown kind reads `2021 · comic`. On the old page (`rep22-eold-r1`) it fails with `2024 · ebook / 2023 · audiobook / 2022 · manga / 2021 · comic`; on the fix, `rep22-e-r3` and `rep22-final-r1` pass (3 PASS).

## Final run

`rep22-final-r1` (`-Stage final`, source `b3728472c`): focused unit filter (ItemTypeLabels, Collection, ItemDetail, Realtime, Localization) 1,718 passed; full unit suite 11,867 passed, zero failures. Native PASS lines, zero FAIL: oct20-watched 5, realtime-series-state 63, admin-monitor-logs 281, admin-manage-foundation 36, admin-monitor 1,071, collections-title-search 3.

`synced-sort-direction` was tried as the collection-editor group and fails in `SyncedDiscardAsync` with `COMException 0x800F1000 No installed components were detected` (a missing image decoder component on this machine); it was not run on unchanged Beta and is not in the passing list. Not touched by this work: the Admin shell exit crash, `admin-plugin-apps`, `media-localization`, `media-catalog-localization`, `ServerActivityStoreTests`, `admin-user-resources`.
