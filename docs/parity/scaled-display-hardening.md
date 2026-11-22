# Scaled-display layout hardening (November 21)

Branch `t3/scaled-display-hardening`, from Beta `4b89ef176`. Implements the "to fix on beta files" list of the scaled-display audit
(`scaled-display-audit-nov21.md`, an internal audit of layout code that fails on displays scaled to 125% or 150%). No colors, styles or behavior changed.

## The two mechanisms

1. **Rounded desired sizes.** A desired size is layout-rounded to the device pixel grid. On a scaled display a gap, border or padding that is not on
   the grid makes a panel's rounded width a fraction smaller than what its children need. A child measured a second time at exactly its own desired
   width, or a fit or wrap decision made with an exact floating-point comparison, then trims text with an ellipsis or wraps a nested panel.
   Fix: never measure a child again at exactly its own desired width (skip the second measure, or offer one DIP more), and compare with one shared
   tolerance, `LayoutTolerance.Fit` (1 DIP, `Controls/LayoutTolerance.cs`). Where a panel re-derived row breaks in Arrange from the final width, it now
   arranges the rows it chose in Measure.
2. **Text under rounded corners.** A button with zero padding clips its content to its corner radius (`GhostButtonStyle` 10, implicit Button 10,
   HyperlinkButton and ToggleButton 4), which cuts the first and last letters. Fix: padding of 6 and 2 with the equal negative margin (the label stays
   where it was) and radius 4; for `AdminMonitorUi.LinkButton` the link template's own border is square (the button keeps its radius, so the keyboard
   focus visual is unchanged), because callers of that helper set their own margins on the link (`AdminActivityPage.cs:495`,
   `DashboardWidgets.Operations.cs:1168`), which would drop a negative margin and move the label.

The development machine runs at 100%, so captures never show either. The native fixture reproduces both at 100%: a probe child that needs 100.4
reports a rounded desired width of 100, a gap of 0.4 or a host a pixel short stands in for the grid, and everything is laid out in the live tree.

## Items

Outcome: **fixed** (file), **skipped** (an open pull request owns the file), **left** (reason). The Activity fixes of #246 are not repeated here.

| Item | Outcome |
|---|---|
| A1, A2 `Controls/ShareRowPanel.cs` | fixed: a child that gets its full natural width is not measured again; `Divide` and the arrange rescale compare with `+ Fit`. |
| A3 `AdminUserResourceUi.cs` SharedNamesRow | skipped, #240. |
| A4 `LogsNativeUi.cs` `LogsSummaryLayout` | fixed: the narrow path does not re-measure a metric that keeps its own width; the wrap compare uses `Fit` (was 0.1) and so does the beside-the-action fit. |
| A5 `LogsNativeUi.cs` `LogsWrapPanel` growing child | left: #246 owns that region. Follow-up after #246 merges: re-measure a growing child only when its allocation exceeds its desired width by more than .01. |
| A6 `LogsNativeUi.cs` `LogsNativeTable` | fixed: each column is `Ceiling(widest cell) + Fit` before the proportional share-out. |
| A7 `Controls/WrapPanel.cs` | fixed: rows are decided once in Measure (with `+ Fit`) and Arrange uses them (it re-derives them only if the child count changed without a measure). |
| A8 `AdminAutoscanActivityUi.cs` own wrap panel | skipped, #244 (file). Same fix as A7. |
| A9, A10 `Controls/AdminShrinkPanel.cs` | fixed: shrinkers shrink only for a shortfall over `Fit`, measured at `Max(0, desired * scale)` (no floor); the clip is `Fit` wider than the panel. |
| A11, A12 `Controls/Admin/DashboardFlowLayout.cs` | fixed: `+ Fit` in both. The widget header's title keeps at least its longest word beside the actions. |
| A13 `LogsNativeUi.cs` `LogsPageHeader` | fixed: `+ Fit`. |
| A14 `Controls/AdminSettingsUi.cs` (label, action content, rate flow) | fixed: `+ Fit` in each. |
| A15 `AdminNetworkAccessSettingsPage.Rows.cs` (flex row, flow line) | fixed: `+ Fit` in both. |
| A16 `AdminProvidersPage.Cards.cs` `ProviderActionFlow` | skipped, #239. |
| A17 `AdminSearchSettingsPage.Status.cs` `StatusRowPanel` | fixed: `+ Fit`; a value that fits only by the tolerance keeps the width it asked for (no second measure, arranged at that width). |
| A18 `Controls/OverlayLayoutPanels.cs` `OverlayBandMarks` arrange | fixed: last mark arranged at `Min(desired, room + Fit)`. |
| A19 `OverlayLayoutPanels.cs` `OverlayBadgeMeasure.Limit` | fixed: `MaxWidth` and the derived text limits use the offered width + .5 (the audit's value; the panel's own measure tolerance is also .5). |
| A20 `DashboardWidgets.Operations.cs` `DashboardTagViewport.FirstRowHeight` | fixed: `+ Fit`, the same break rule as `WrapPanel` (A7). |
| A21 `AdminLibrariesUi.cs` `FlexWrapStretchPanel` | fixed: an item held at exactly its natural width is measured at natural + `Fit` (its arranged slot is unchanged). |
| A22 `Controls/CarouselRail.cs`, `ItemDetailPage.xaml.cs` paging | fixed: `Math.Floor(x + .01)`. Pure arithmetic; no native check (the paging methods are private to pages that need a server). |
| A23 `CatalogPage.xaml.cs`, `CatalogPage.Watchlist.cs` card width | left: not obviously safe. The whole-DIP floor leaves up to a pixel of slack per column, which is what stops `UniformGridLayout` dropping a column at exact boundaries (the external watchlist code says so in a comment); a scale-aware floor removes that slack on a viewer page every user sees, and the flooring only makes cards a fraction narrower, it clips nothing. |
| A26 `Controls/CollectionPickRow.cs` | fixed: title `MaxWidth` is heading - badge - 8 + `Fit`. No native check (needs the app's service provider and a template). |
| Plugins Installed/Catalog tabs `AdminPluginsPage.Board.cs:42` | fixed: a horizontal `StackPanel`. |
| B1 `AdminMonitorUi.LinkButton` (IP, user, title links) | fixed in the helper (square link template; see above), so every caller gets it. |
| B2 `AdminActivityPage.Rows.cs:888` "Open ffmpeg logs" | left: #246 owns the file. Follow-up: `AdminMonitorUi.TextRoom(A.Plain(...))`, the helper added here. |
| B3 `Controls/ServerActivityButton.cs` ("View all"; the link label goes through B1) | fixed: `TextRoom`. |
| B4 `AdminPluginsPage.Board.cs` plugin-name link | fixed: `TextRoom`. |
| B5 `DashboardWidgets.Operations.cs` links; `DashboardUi.TextLink` | fixed: the two links go through B1; `TextLink` has padding 6,2, margin -6,-2, radius 4. |
| B6 `CollectionEditorPage.TitleSearch.cs` Add / In this collection | fixed: padding 6,2, margin -6,-2, radius 4. |
| B7 `NotificationsPage.Inbox.cs` row link | fixed: padding 6,2, margin -6,-2, radius 4. The group selection overlay (`:138`) has no content and was not changed. |
| B8 `Controls/ExpandableDescription.cs` "More" | fixed: padding 6,2, margin -6,-2, radius 4. |
| B9 `AdminLibrariesPage.Editor.cs` folder entries, `AdminLibraryEditorView.cs` level toggle | fixed: padding with the equal negative margin, radius 4 (folder entries: 4 on the left, as the row has no gap before them). |
| `AdminLibrariesPage.Roots.cs:499,533` (assessed) | fixed: both ghost buttons wrap text and a chevron at the corners; padding 6,2, margin -6,-2, radius 4. |
| B10 HyperlinkButton padding 0 radius 4 | left, as the audit says (at most a pixel). |
| D4 `AdminSignInSettingsPage.cs` state chip | skipped, #242. |

## Native fixture

`tests/SiloPlayer.NativeRegressionTests/ScaledDisplayLayoutNativeFixture.cs`, selector `scaled-display-layout`, 50 checks. Each hardened panel
is hosted in a grid added to the live page, laid out with `UpdateLayout`, read through layout slots and the probe's last offered width, and removed.
Each check has a counterpart that a real shortfall (20 DIP or more) still wraps, shrinks or trims. Button helpers (`LinkButton`, `TextLink`,
`TextRoom`) check padding against the corner radius, the label's place and that it is not trimmed. `OverlayBandMarks` is laid out without layout
rounding so the panel really is arranged 156.6 wide (a rounded layout never produces that at 100%).

## Runs

Lane `admin-completion-lane-a`. Mutation: `scalefix-mutation-r1` (product source reverted to Beta, fixture kept) failed 15 checks but not every
panel's (the fixture's shortfalls were then half a pixel, which the rounded host width hid for the settings, header, status, network and band-mark
panels); the fixture was corrected and `scalefix-mutation-r2` failed 25 checks (one of them `TextRoom` not existing on Beta), covering every panel
check and every button-helper check; the source was restored and `git diff` was empty. Final: `scalefix-final-r1` on source commit `1336be799` (counts in
`current-development.md`).

## Not verified

- No run at a real 125% or 150% display scale. The fixture reproduces the mechanism at 100% with fractional sizes; it does not render at a scaled DPI.
- A5, B2 and A8 (and A3, A16, D4 of the audit's pull-request list) are not changed: they wait for #246, #244, #240, #239 and #242.
- Visual captures of the changed buttons (padding, radius 4, hit target growing by 6 by 2 around the label) were not taken.
- A22 and A26 have no native check.

## Review and final run

**Review and final run (lead).** An independent read-only review found no P1 and two P2, both fixed in `03e7f18d8`: every log table column had been given 1 to 2 pixels at every scale, so a table that fit its viewport exactly would have gained a sideways scrollbar at 100% (now `LayoutTolerance.ColumnRoom`: one DIP only where the display scale is not a whole number; nothing at 100% and 200%); an action row's result could be arranged a pixel narrower than it measured (`AdminSettingsActionContent`). Also from the review: `NetworkAccessFlexRow` applies no tolerance (the text beside the actions would be left no width); an overlay badge with no room gets none. Added once #239 merged: the Providers action row (audit item A16) allows a pixel before wrapping. Left as reported: where a panel accepts up to one pixel of overflow at 100% the last child can sit up to one pixel past the edge; negative margins extend a text button's hover and focus area 6px past its label. Beta through #239 (`984044bb5`) is merged in. Final run `scalefix-review-r2` on `b41b8311a`: 741 focused and 11,547 full unit cases passed, 0 failed; app build, publish and native host build passed; 20 native groups passed with no failed check and exit code 0, among them scaled-display-layout (54 checks), admin-monitor (1,071), admin-libraries (611), admin-configure-network-access (346), admin-configure-security (376), admin-plugins (365), retained-library (70) and overlay-revisions (91). The run before it (`scalefix-review-r1`) stopped on one new check that tested the wrong coordinate (corrected in the fixture). The review fixes were not independently reviewed again; the table check uses the test switch `LayoutTolerance.AssumeFractionalScale` to stand in for a scaled display.
