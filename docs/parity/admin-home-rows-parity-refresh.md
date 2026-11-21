# Home sections (`/admin/sections`) — refresh against official `1a93371`

Scope: the native Manage › Libraries › Home sections page only (its list, page switcher, More menu, row menu, select mode, Next Up defaults card, Add row / Edit row editor, delete / restore / add-to-other-libraries dialogs, the Collections link contract). Collections, the environment-managed settings work and every other page are untouched.

- Official source compared: `Silo-Server/silo-server` at `1a93371807d4762694be3fbb40611375129955b6` (2026-11-21), read with `git show` / `git grep` from the pinned blobless clone.
- Earlier native build: `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb` (2026-11-16), merged to beta as #217 (record: `docs/parity/2026-11-17-admin-home-sections.md`).
- Native baseline: `origin/beta` at `8ff1c34b1`; the `admin-home-sections` native group passed there unchanged (label `homerows-baseline-r1`, 139 PASS lines, 0 FAIL).

## What the brief assumed, and what the source says

The refresh was requested because the official client "now calls the page Home rows". The pinned source says otherwise for the admin client:

- `33a7a64b3` (rename Admin > Sections to Home rows), `ed40bd235` (one list with switches and a row menu), `f1402f6f4`, `5d225c450` and `acfff78b1` are all **ancestors of the old pin `217ff25`** (dated 2026-09-27 and 2026-10-03). The native page built in #217 already follows that structure; nothing in them is new to this refresh.
- The route did not change: `web/src/App.tsx` still has `<Route path="sections" element={<AdminHomeRows />}/>` (`/admin/sections`), with `LegacyAdminHomeRowsRedirect` for old addresses. Native `AdminRoute.HomeSections` already carries both.
- The admin label did not change. The admin sidebar item is `web.admin.shell.nav.item.homeSections` = "Home Sections" under the Libraries topic (`adminNavigation.ts` line 510), the page title and `AdminHomeRows` heading are `web.admin.homeRows.page.title` = "Sections", and "home rows" is only a search keyword (`keywords: ["home rows", "rows", "rails", "featured sections"]`). "Home rows" is the **profile** page, `web.settings.nav.setting.homeRows` (Settings), which is a different surface and out of scope. Native navigation, route catalog and command search already match the admin labels, so nothing was renamed.

## Upstream changes between `217ff25` and `1a93371` that touch this page

`git log 217ff25..1a93371` over `web/src/components/homeRows`, `web/src/lib/homeRows`, `web/src/hooks/queries/homeRows`, `web/src/pages/AdminHomeRows.tsx`, `AdminSectionCommandDialog.tsx`, `api/adminSections.ts`, `adminNavigation.ts`, the `en.json` keys the page uses, `contracts/api/v2/openapi.json` and `internal/apiv2` / `internal/api/handlers` section files:

| Commit | Change | Effect on this page |
|---|---|---|
| `4d3bca685` (#4471) filter the Add row collection pickers by library | Both collection pickers (a collection row, a row of collections) gain a Library filter and show each collection's library names; collections carry every library that shows them | **Changed natively** (library filter, meta line, empty text, keys `web.home.addRow.collection.library` and `…noneInLibrary`) |
| `ce9677313` (#4877) emphasized row meta text at full foreground | Bold parts of a row's description use `text-foreground` instead of `text-foreground/75` | **Changed natively** (strong runs now use the primary text brush over the muted line) |
| `5537df46a` (#4766) URLs left to right in RTL | The custom-holiday list URL field gets `dir="ltr"` | **Changed natively** (the URL box is left to right) |
| `105d0cabf` (#4702) server-managed vs user-managed rows | `RowOriginBadge`, the "Server" tag, `web.home.addRow.shows.serverNote` | **Not applicable**: shown only on the profile surface; "never shown on the admin surface, where every row belongs to the server" (component comment). Admin rows carry no tag |
| `f18678fda` (#4648) hide server rows added since a home layout export | `/api/v2/profile/sections/baseline`, home layout export and import | Not this page (profile settings) |
| `f465d2d9a` (#4939) admin pages and bottom bars above the playback bar | `--playback-bar-clearance` on the dock and select bars | Not applicable: the native admin shell has no playback bar over it |
| `bd40bd9eb` (#3550) plugin destinations + catalog pin `2026.11.18-9054c7f` | Catalog refresh | Changed the English of four keys the page shows (below); no other effect |
| catalog pin (same) | Rule rows are now **"Smart row"** (`web.home.rows.kinds.customFilter`), group "Collections & smart rows", new picker sentence | **Changed natively** (English). The saved default name of a new rule row is stored data and unchanged (`catalog.ts` still names it "Titles matching rules") |
| `internal/sections/types.go`, `ParseConfigFilters` | Server-side config scope parsing now shares the See All parser; adds media scopes `ebook` and `manga` | No client change; the row config the page reads and writes is unchanged |
| `openapi.json` | Section operations and schemas `AdminSection*`, `SectionCapabilities`, `Recipe*` are **identical**; only `ProfileSectionFlags`, `ProfileSectionBaseline*` (profile) and `AdminBackupRestoreHomeSection` (backups) changed | No request body or URL on this page changes |
| `adminNavigation.ts`, `AdminSectionCommandDialog.tsx`, `AdminHomeRows.tsx`, `HomeRowsPage.tsx`, `RowList.tsx`, `SelectModeBar.tsx` | No change since `217ff25` | None. `web.admin.homeRows.commandDialog.*` belong to the admin shell's search dialog (already native in the shell), not to this page |

String comparison (`en.json` rendered to ICU, every key under `web.admin.homeRows`, `web.home.rows`, `web.home.addRow`, `web.common.ui.selectMode`, `web.ui.selectMode`, `web.ui.selectedCount`): 650 official keys; 634 equal to what the page showed, 4 differ (the "Smart row" keys), 12 were missing (8 `web.admin.homeRows.commandDialog.*` that belong to the shell, 2 new collection-picker keys now carried, `web.home.addRow.shows.serverNote` and `web.home.rows.line.server`, both profile-only).

## Differences found between the official page at `1a93371` and the native page on beta, and what was done

Outcome: **fixed** = changed in this refresh; **right** = already matched; **cannot** = cannot be done in WinUI (reason given); **differs** = deliberate difference kept (reason given).

### Structure and chrome

| Area | Official (`1a93371`) | Native on beta before | Outcome |
|---|---|---|---|
| Page header | `PageHeader`: title, subtitle, at the right **More** (outline, ellipsis icon + label) and **Add row** (primary, plus icon), 32px high, radius 10 | Text buttons "⋯" and "Add row", no icons | **fixed** (lucide icons, same sizes) |
| Under 1024px | The two buttons move to a docked bar: More 48×48 radius 14, Add row 48px high radius 14, 15px text, gradient from transparent to the page background; hidden in select mode | Same header buttons at every width | **fixed** (dock at window width < 1024; same two buttons move, so ids and names are the same) |
| Page switcher | `PillSwitcher`: pills (Home with house icon, divider, then libraries), one pressed, count at the right (below the pills under 640px), strip scrolls sideways | A ComboBox | **fixed** (pill strip with the same layout). **Cannot**: the edge fade and the mouse-only paging chevrons (WinUI `ScrollViewer` scrolls with wheel, touch and keyboard; no edge mask) |
| Row count | `{n} rows · {m} on` in the strip's right slot | Separate line | **fixed** |
| List container | `surface-panel rounded-[26px] p-1.5` | Bare list | **fixed** (radius 26, 6px padding, surface and hairline) |
| Row | `ListRow`: grip (28px), art tile 74×50 (48×42 on phones) with the kind's lucide icon, name 15px semibold with tags, one-line meta 13px muted, switch, ⋯ (36px, 44px under 1024), rounded-[18px], hover `accent/60`, hairline between shown rows starting at 124px | Plain grid: hamburger glyph, text, switch, ⋯ | **fixed** (all of it) |
| Off row | Dashed rounded line, eye-off icon, "**Name** is off. …", one line | Plain row with bold name | **fixed** (dashed outline, 30px line, eye-off) |
| Hero tag | `h-[22px]` pill with star icon, warning tint and ring; phones show the short label at the start of the meta line instead | Amber pill without icon, shown at every width | **fixed** |
| Emphasis in the meta line | `text-foreground font-medium` (upstream #4877) | Medium weight, same color as the rest | **fixed** |
| Row art | `PosterPeek`: the first titles' posters (loaded as the row scrolls into view, 2 at a time) | None | **differs**: icon tile only; the row menu keeps **Preview**, which opens the same titles in a dialog (see below) |
| Library note | Info icon + "These rows show above the full {library} grid." | Plain text | **fixed** |
| Conflict | Warning-bordered banner with icon, text and **Reload rows** | Red alert with a button under it | **fixed** (warning banner). The unknown-outcome and library-list alerts stay red; they are native-only |
| Failed read | Surface panel with the problem text and **Reload rows** | Red alert | **fixed**; Retry stays reachable (`HomeSectionsReload`) |
| Loading, empty | "Loading rows…" under the strip; empty text centered inside the panel | Both as one status line | **fixed** (empty text now inside the panel) |
| Reorder hint | Grip icon + sentence with keycap chips for Space and arrows | Sentence only | **fixed** (icon). **Cannot** keycap chips: the shared message formatter prints rich-text tags as their plain text |
| Drag | Grip handle with keyboard drag and announcements | Whole-row drag (`ListView` reorder) plus **Move up / Move down** in the row menu | **differs** (documented in #217; native list has no handle-only drag) |
| Select mode | `SelectAllHeader` inside the panel (checkbox, label, limit, Done on a hairline) and a floating `SelectModeBar` (count, Turn on, Turn off, divider, Delete in destructive ink; labels collapse to icons on phones) | Inline row of buttons under the list | **fixed** |
| More menu | Two items with icon, label and a line of help (`…selectHelp`, `…restoreHelp`) | Two text items | **fixed** for icons; the help line is the item's tooltip and help text. **Cannot** show a second visible line: `MenuFlyoutItem` has no description slot |
| Row menu | Icons; Edit row…, Use / Stop using as hero banner, Move to top, Move to bottom, Add to other libraries…, Delete row… (destructive) | Text only, plus Move up, Move down, Preview | **fixed** for icons and the destructive Delete; Move up / Move down / Preview stay as native additions |
| Next Up defaults | `surface-panel` card, rounded-[22px], divided rows: heading + help; label + description with a 240px select; label + description with a switch | Plain stack | **fixed** |

### Add row / Edit row (`AddRowDialog`, `RowPicker`, `RowCard`, `VariantChips`, `CollectionPicker`, `CollectionsRowEditor`, `RowForm`)

| Area | Official | Native on beta before | Outcome |
|---|---|---|---|
| Frame | `StepDialog`: rounded-[20px], 1000px wide for the picker (860px tall) and 880px for the form, back link above the title, 20px semibold title, 14px description, ✕ at the top right, footer on a tinted band with a hairline; bottom sheet under 1024px and a full window for the form | 800px rounded panel, 24px title, text buttons in a column | **fixed** (same sizes and parts; below 1024px a bottom sheet and a full-window form) |
| Footer | `StepCount`: two bars, "Step N of 2 · note"; Cancel, then **Add row** with plus (or "Add to N pages") / **Save**; Delete row… (destructive ghost) at the left while editing | Plain note and buttons | **fixed** |
| Picker | Search (44px) in the header; a rail of the groups (tinted icon tile, name, count) beside two columns of cards; chips on a phone | One column of plain buttons with a tab row | **fixed** (rail scrolls the card list to its group; two columns from 1024px; chips under 1024px). **Differs**: no scroll-spy (the pressed rail entry follows clicks, not scrolling), and no scroll-linked rail highlight |
| Kind card | 40px tinted icon tile, 14.5px semibold name, sentence, "On {page}" check tag, up to three variant chips and "+N" | Name button and a separate sentence | **fixed**. **Differs**: under 1024px the chips are not shown (the official shows a text summary of the variants) |
| Edit row, what the row shows | `ShowsSummary` with the kind's tile, name, sentence and **Change** | A line of text and a "Change what this row shows" back button | **fixed** |
| Collection pickers | Search, Manual / Smart / Synced chips, **Library** select when the collections come from more than one library (upstream #4471), each collection with library names, type and title count, the empty texts, "Added" marks in the multi picker | Search with Search / Next / Previous buttons over a paged window; label only | **fixed**: one bounded read of the collection list when the picker opens (2,000 entries at most), live search, the same chips and library filter, the same meta line (`Library · Smart · 12 titles`) and empty texts. The lookup by ID stays for a collection the bounded list does not hold |
| Row of collections | Chosen list with move up, move down and remove per collection, "A row holds up to 30 collections." | Text lines with arrows | **fixed** (official strings, icon buttons). **Differs**: no drag handle (move up / down only) |
| Title picker (Editor's picks) | Search-as-you-type | Search button | **differs** (explicit Search, to keep the bounded request pattern); strings still the earlier ones |
| Form fields | Variant chips, name with "Follows … until you type your own", More options disclosure, preview | ComboBox variant, name, Expander, Live preview button | **differs**: the variant is a ComboBox, More options an Expander and the preview loads when the form opens and on **Live preview** (documented in #217). Not restyled further in this refresh |
| Programmatic control changes | n/a | A switch or select re-reported its starting state after realization | **fixed**: every editor switch and select acts only on a value different from the one it started with, so an untouched editor sends nothing and closes without a discard prompt (tested) |

### Dialogs

| Area | Official | Native | Outcome |
|---|---|---|---|
| Delete row / rows, Restore defaults | `variant="destructive"` confirm (`--destructive-fill` `#dc2626`) | The shared destructive style (lightens on hover) | **fixed** with a page-local style: `#dc2626`, darker on hover (`#b91c1c`) and pressed (`#991b1b`) |
| Dialog bodies, wording, stale and unknown handling | As #217 | As #217 | **right** (unchanged) |
| Add to other libraries | `AddToOtherLibrariesDialog` | ContentDialog with checkboxes | **right** (unchanged) |

### Requests

No request body or URL changed. The official operations (`openapi.json` `listAdminSections`, `getAdminSection`, `createAdminSection`, `updateAdminSection`, `deleteAdminSection`, `bulkCreateAdminSections`, `reorderAdminSections`, `restoreAdminSections`, `previewAdminSection`, `getAdminSectionCapabilities`, `getAdminSectionOrder`) are byte-for-byte the same at both pins, and the bodies the native page sends were compared with `adminSections.ts` / `payloads.ts` in #217 and are untouched here. The collection list read (`GET /api/v2/admin/collections`) is now read once when a collection picker opens, with the same 2,000-entry cap (the client reads `library_id`, `library_ids` and `item_count` from it).

### Text

- Every key the page shows is read from the generated interface catalog, or from the page's pinned table (`AdminHomeSectionsText.English`, official English for the keys the catalog does not carry; now rendered from `1a93371`).
- Four keys changed in the official English after the generated catalog was built (rule rows are now "Smart row"). `AdminHomeSectionsText.Newer` gives those their current English while the interface language is English; other languages keep the catalog's earlier translation until the catalog is regenerated (shared tool and package, not touched here). The page's rule-row name, the picker group name and the picker sentence follow it. Other pages that use the same keys (Settings, the recipe gallery) still show the earlier English.
- **Text with no official sentence** lives in `AdminHomeSectionsText.NativeOnly` (`native.homeSections.*`), English only and never translated. It holds: the sign-in-required status, the library-list failure alert, Move up / Move down / Preview in the row menu, the collection lookup by ID, the generic picker's Search / Next / Previous and its status lines, the Next Up defaults review messages, and the labels of the rule builder (the rule builder is the earlier native form, not the official sentence builder; see below). Earlier these were literals in the page code.
- The existing key scan (`EveryTextKeyThePageUsesHasOfficialText`) now also covers `native.homeSections.*`, so a page literal that names a missing key fails the unit run.

## Deliberate differences and what was not done

- **Poster peeks** (`PosterPeek`): not implemented; rows show the icon tile, and **Preview** in the row menu lists the same titles in a dialog.
- **Drag handle and keycap chips**: whole-row drag with Move up / Move down instead; the hint prints plain text.
- **Pill strip**: no edge fade or paging chevrons.
- **Rule builder, seasonal editor, variant control, More options, preview**: functionally as in #217 but not restyled to the official sentence-style `RuleBuilder`, variant chips, disclosure and framed preview. Their labels are the earlier native English (`NativeOnly`) where no catalog sentence matches the control.
- **Title picker** (Editor's picks): the explicit Search / Next / Previous flow of #217.
- **Breakpoints** (1024 and 640) follow the window width, as the official media queries follow the viewport; the admin sidebar is not part of this page.
- **Right-to-left**: not run natively; the custom-holiday URL box is forced left to right as upstream #4766 does.
- **High contrast and Text Size**: not run.
- **Visual parity is not claimed.** There is no official server or screenshot in this environment; the native captures below were compared with the official layout as read from source (sizes, radii, structure), not with pixels.

## Shared files

No shared control, theme or catalog file changed (`AdminSettingsUi`, `AdminMonitorUi`, `DarkTheme.xaml` and the generated catalog are untouched). New or changed files, all page-specific: `AdminHomeSectionsUi.cs` (new: lucide icon paths, icon tiles, tags, pill strip, the page-local destructive button style), `AdminHomeSectionsCollections.cs` (new, Core), `AdminHomeSectionsText.cs`, `AdminHomeSectionsModels.cs` (collection library fields), `AdminHomeSectionsPage*.cs` and `.xaml`, plus `docs/parity/admin-monitor-surfaces.json` (the two new source files) and the tests below. No other page uses any of them. `installer/build.ps1` needs no change (the page's XAML was already listed).

## Guarantees kept from #217 (and how the redesign keeps them)

- Writes act on exactly the row that was on screen, are sent once and are never replayed; an unknown outcome blocks writes until the page is read again; a refused write keeps the draft. These live in `AdminHomeSectionsController` and the editor, which this refresh did not change; the fixture's existing checks for them all still pass.
- **The shown switch saves immediately, as in the official list, with the same guarantees.** It acts only on a change the admin made: a recycled switch used to report its old state while the page put a new row's saved value into it, which turned a page switch into a write against the wrong row (caught by the extended fixture as a conflict banner and a blocked page, with no request sent). The page now ignores every report made while it sets a switch or checkbox itself, and ignores a report from a switch that does not yet belong to the row it is bound to. A changed width or a page switch sends nothing (checked in the fixture).
- Every editor switch and select acts only on a value different from the one it started with, so an untouched editor sends nothing and closes without a discard prompt (checked in the fixture).

## Tests added or extended

- Unit (`AdminHomeSectionsCollectionsTests`, 7 cases): the library list a collection picker offers (named libraries that show a collection, sorted; unnamed ones dropped), the library filter falling back to every library when it stops applying, search + type + library filtering together, the meta line (`Shows, Movies · Manual · 4 titles`), the empty texts, the "Smart row" English and the earlier translation kept for another language, and that native-only text is English in every language.
- Unit (`AdminHomeSectionsPresentationTests.EveryTextKeyThePageUsesHasOfficialText`): the key scan now includes `native.homeSections.*`.
- Native (`admin-home-sections`, 194 checks, up from 139): pills (count, pressed page, choosing a library page by UI Automation toggle), the dock at each of 1440 / 1024 / 800 / 560 (where the two buttons sit and their sizes), that a width change and a page switch send no write (the page-switch check failed before the guard described above), the select bar shown only in select mode, the collection picker's library filter and type filter (library list, hidden collection not offered, a collection in two libraries stays in either filter, narrowing by type, the empty-library text), the From filter's checkboxes, an untouched editor closing without a prompt and sending nothing, the failed-read state with its working Reload, and captures. Checks that depended on the removed ComboBox or on the editor's button text were rewritten for the pills and for `AutomationProperties.Name`; no existing check was removed or weakened.

## Verification

Under the `SiloOct18Build` lease from `accept-family-v2.ps1`, lane `D:/SiloPlayer-build/admin-completion-lane-a`:

- Baseline on unchanged `origin/beta` (`8ff1c34b1`), `-Stage publish-native`, label `homerows-baseline-r1`: `admin-home-sections` 139 PASS, 0 FAIL.
- Final, `-Stage final`, label `homerows-final-r1`, source commit `510aa0062058dd668e12a084cf80d65fa116ab01`: focused unit run (`AdminHomeSections`, `HomeSectionReconciler`, `HomeSectionDedupe`, `HomeSectionCollectionReconciler`) 156 of 156 passed; full unit suite 10,472 of 10,472 passed, none skipped; app build, self-contained publish and native host build passed; native groups `admin-home-sections` 194 PASS / 0 FAIL, `admin-manage-foundation` 36, `admin-manage-library-settings` 84, `admin-configure-general` 188, `admin-monitor` 113, all with 0 FAIL; payload and source seals verified. The groups known to fail on beta for repairs in other pull requests were not run. No intermittent test failed in this run, so none was rerun.
- Earlier runs on this branch (not acceptance): `homerows-focused-r1` (unit, passed); `homerows-publish-r1` and `-r2` (compile errors in new code); `homerows-native-r1` through `-r15` (native, while the page was built; failures were a fixture check that read a tag before it rendered, a crash from an icon path built from SVG attributes with digits in their names, a spurious switch report on a recycled row, and the editor and fixture work above).

## Captures opened

From `homerows-final-r1-admin-home-sections` (each width is 1440, 1024, 800 and 560; a capture is the window content, so popup layers such as menus and confirmation dialogs are not in them):

- Opened and compared with the official layout as read from source: `home-sections-list-1440`, `-800`; `home-sections-select-mode-1440`; `home-sections-add-form-560`; `home-sections-edit-conflict-1440`. Opened from the intermediate runs `homerows-native-r8`, `-r10`, `-r14`, `-r15` (same code paths, before small fixes): list 560, select mode 1024 and 560, failed read 800, empty 1024, add picker 1440 and 560, add form 1024, edit conflict 800.
- Produced but not opened one by one: list 1024; select mode 1024, 800, 560; failed read 1440, 1024, 560; empty 1440, 800, 560; add picker 1024, 800; add form 1440, 800; edit conflict 1024, 800, 560.
- Popup layers (the More menu, the row menu, the delete, restore and add-to-other-libraries dialogs) cannot be captured by the host; they were checked through automation (menu entries and their order, dialog text, buttons, and the sent requests), not by image.
- Seen in the captures and left: while the editor is scrolled the stale-save notice scrolls out of view (the Reload row button is at the top of the form); the form step's variant control, More options and preview keep the earlier look.

## Not verified

- No official server or screenshots: layout is compared from source, not pixel by pixel. No claim of visual parity is made.
- Real pointer drag, touch, hover and pressed states, keyboard drag, Narrator, high contrast, Text Size, right-to-left layout, other DPI scales and languages other than English.
- Live-server behavior: everything ran against the synthetic server of the fixture; the collection list read with `library_ids` was exercised only against synthetic data of the shape in `openapi.json`.
- The collection pickers were not run against a server with more than 2,000 collections (the cap is unchanged from #217).

## Project rules checked

- No page entrance, slide or opacity animation: the page keeps `NavigationCacheMode.Disabled` and the in-place frame; the fixture still checks that the inner frame has no content transition. No storyboard or transition was added.
- No literal `/api/v1` in the page, its Core files or tests.
- The page shows no formatted date or time (the only date code is the seasonal editor's `MM-dd` validation), so `DateTimeDisplay` and the settings service it needs are not involved; the fixture's construction path registers the services the page takes.
