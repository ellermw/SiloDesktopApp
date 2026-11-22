# Admin Collections: refresh against Silo Server 02a9d1d3e (November 21, 2026)

Branch `t3/admin-collections-parity-refresh`, from `origin/beta` at `d1144b96f`. The page was built against official
`217ff25abc`; this pass compares it with `02a9d1d3e1b48d86437ad10c0b66ed0e3645d098` (read-only with `git show`).
Earlier decisions in [admin-collections.md](admin-collections.md) were not re-audited except where the official side changed.
Version numbers are untouched. Run labels and counts are in `docs/current-development.md`.

## Upstream changes since 217ff25abc that reach this page

`git log 217ff25abc..02a9d1d3e` over `web/src/pages/AdminCollections.tsx`, `web/src/components/collections/**` and their imports.

| Change | What it does officially | Outcome on the native page |
|---|---|---|
| `2e67124ce` #4796, `bc53d8b2f` #4823 (pin button on keyboard focus and touch; matches the menu chip) | The pin button on a collection **poster card** (`CollectionPosterCard`, used by the viewer's Collections tab, `LibraryCollections`, `CollectionsRow`) is revealed on keyboard focus and on touch. | **Not applicable to this page.** `CollectionPosterCard` is not used by `AdminCollections.tsx`, the admin list (`CollectionListItem`) or the Arrange shelves (`GroupCard`, `CompactItem`); the admin Pin is an item of the card's `⋯` menu. The native Arrange card has **no hover reveal at all**: Pin/Unpin is an always-drawn button in the tab order (checked: visible, opacity 1, takes keyboard focus). It is reachable by keyboard, pointer and touch, so there is nothing to fix. The viewer's poster card is not touched. |
| `6d954ffff` #4832 (drag handle chip size `max-lg:size-9`) | Same poster card and `Collections.cards.tsx` (the viewer's own-collections page). | **Not applicable.** The admin grip is `Grip` in `ListRow`/`CompactItem`, unchanged. The native handle is an always-drawn 36 px button. |
| `472476910` #4797 (sticky panels stay in view on desktop admin pages) | `AdminLayout` stops clipping with `overflow-x-hidden`; the Arrange "What viewers see" preview is pinned beside the board from `xl` with its own scroll. | **Not applicable to the Arrange preview (no sticky side panel in WinUI)**: the native preview is a toggled view ("What viewers see" / "Back to Arrange"), not a pinned aside. **The same commit also changed `SelectModeBar`, which this page uses**: official it is fixed to the bottom of the viewport; the native bar sat at the top of the list and scrolled away. Fixed in the second pass, see "Review fixes". |
| `002a6c601` #4507 (Filters sheet hides Advanced sort on a search) | `showSortControls` added to `FilterRuleEditor`/`CollectionRulesEditor`; the only caller that passes `false` is the viewer catalog's `CatalogFilterSheet` during a search. | **Not applicable.** The admin page has no Filters sheet. Its smart editor uses `SmartRulesPanel` → `RuleBuilder`, which does not take `showSortControls`; the admin editor keeps its sort. The native admin page has no search-bound sort to hide. |
| `faa406d92` #4528 (collections a movie or series is in, on its page) | New viewer feature: `ItemCollectionsRow` on the item page, `GET` item collections, `CollectionPosterCard` meta. | **Not applicable.** Viewer feature. It adds no field or route the admin list, editor, members, "Rows that show it" or delete reads. `contracts/api/v2/openapi.json`: the 37 path items under `/admin/collections*`, `/admin/collection-*`, `/admin/sections*`, `/admin/libraries/{id}/collection-*` and `collections/import` are byte-identical (hash of their JSON) between the two commits; of the collection and section schemas only `AdminBackupRestoreHomeSection` (backup restore) differs. |
| `b63397757` #5004, `9efbdf506` #4991, `a7f067168` #5016 (cut-short names keep their start in right-to-left) | `titleDir="auto"` / `dir="auto"` on admin list and shelf names (`ListRow`, `CompactItem`), poster card titles. | **Fixed.** A name is now one `TextBlock` with `TextTrimming=CharacterEllipsis`, `TextWrapping=NoWrap`, `TextReadingOrder=DetectFromContent` and the plain name (bounded to 200 characters) as tooltip; the control shows the whole plain name and never slices it. **Corrected in the second pass**: the first record said the full raw name was also the automation name; it is now the plain name bounded to 80 characters (a long or multi-line name is not read out in full), and a name with a line break is drawn on one line. Applied to list rows, Arrange cards, shelf headings, the editor heading, delete-rows lines and member lists. |
| `5537df46a` #4766 (URLs left to right) | `dir="ltr"` on the MDBList link, TMDB list URL and artwork URL inputs. | **Fixed.** The list link and both artwork URL boxes are `FlowDirection=LeftToRight`. |
| `6d73d7a8f` #5021 | Safari-only Arabic joining. | **Not applicable** (WinUI text shaping). |
| `f6ed24869` #4700, `160a4425e` #4704 (destructive fill `#dc2626`, delete confirms in the destructive variant) | `--destructive-fill` under white label; hover at 90%. `DeleteCollectionDialog`, `DeleteCollectionsDialog`, `ShelfDialogs` confirm with `variant="destructive"`; `HideCollectionDialog` is the default variant. | **Fixed (page-local).** Single delete, delete with rows, bulk delete and delete shelf draw their confirm in `#dc2626`, white label, `#dc2626` at 90% (alpha 230) on hover and press, through a page-local style (`AdminCollectionsDestructiveStyle`) based on the shared `DestructiveButtonStyle`. Hide stays an ordinary confirm. In a Windows contrast theme the dialog keeps its system colors. The shared style, controls and `DarkTheme.xaml` are not changed. |
| `0ad4c13d2` #4852, `e8ad23294` #4867 (red text readable in high contrast) | App-wide red text tokens. | **Not applicable here** beyond the above: a failed sync reads in the page's destructive text color, which is the theme's own. |

## Differences found between the official page at 02a9d1d3e and the native page on beta

Each string the page writes was compared with the official `en.json` (`web.collections.*`, `web.admin.collections.*`,
`web.shell.confirmDialog.*`, `common.*`) mechanically (exact and best-match scripts, then by hand). Strings the earlier record already settled were left.

| Surface | Official | Native on beta | Outcome |
|---|---|---|---|
| Single delete: title, description | `Delete {title}?`; "It's removed from {libraries} for everyone. This can't be undone."; with rows "…{n} row(s) show(s) it:" | `Delete {title}?`; "Your files and watch history are kept." | Fixed: official sentences; libraries named. |
| Single delete: confirm button | `Delete`; with rows `Delete it and its N row(s)` or `Delete it and change N row(s)` when a row lists other collections | `Delete collection` | Fixed. Rows that list other collections (`collection_count` > 1) say the row keeps its others and the button says "change". |
| Single delete: hint and footer with rows | "To keep the rows, open each one…"; "Titles stay in your libraries. This can't be undone." | absent | Fixed. |
| Hide (list switch, editor switch) | `Hide {name} from Collections tabs?` + "It leaves {Library} › Collections. N row(s) still show(s) it, but its See all won't open while it's hidden." | `Hide from Collections tabs?` + one generic sentence | Fixed. |
| Bulk delete title | `Delete N manual/smart/synced…/collections[ in {library or this view}]?` counting what will be deleted | `Delete collections?` + "N selected." | Fixed. |
| Bulk delete body | description; "Also": collections that are also in other libraries; those kept because rows use them; "This can't be undone." | "N selected. K collections in use by rows will be kept." | Fixed: bullets and footer. |
| Bulk delete result | `Deleted N collection(s)` / `Deleted X of N` / `Failed to delete`, `Kept N collection(s) in use by home or library sections` | "{done} changed; {kept} kept; {failed} failed." | Fixed. |
| Show/Hide/Sync lists result | `Showed N collection(s) on Collections tabs.`, `Hid…`, `Synced N list(s).`, `X of N`, `Couldn't …` | the same generic line | Fixed. |
| Hide several | `Hide N collection(s) from Collections tabs?` + row sentence | generic | Fixed. |
| Delete all in a view whose collections are all used by rows | toast "Rows still use them. Remove the rows first.", no dialog | a dialog | Fixed: nothing is deleted and no dialog opens. |
| Bulk delete while a starter pack is being added | "A starter pack is being added. Delete once it finishes." | different wording | Fixed (single and bulk). |
| Delete shelf | `Delete the {name} shelf?` + "Its N collections move to No heading on {Library} › Collections. They aren't deleted. Only {Library} changes; shelves in other libraries stay as they are. You can make the shelf again later."; button `Delete shelf` (destructive) | `Delete {name}?` + one generic sentence | Fixed. |
| Failed-list message | "Couldn't load collections" (straight apostrophe) | typographic apostrophe | Fixed. |
| Save-before-sync notice | "Save your changes first; a sync runs the saved list." | "Save changes before syncing. Sync now uses the saved list." | Fixed. |
| Counts | formatted with the locale | plain integers | Fixed for the list count and the title counts. |
| Names written into a confirmation or title | plain text in a React node | the raw server string | Fixed: one plain bounded line (see below). |

Left as the earlier record decided, because the official text is built from components or data the native page models differently (stated, not hidden):
the library switcher, menus and chips (`⋯` menu items map to inline buttons), the editor's field labels and help lines, template and starter-pack dialog wording,
the web's "Select collections" help text, and the select-mode "kinds skipped" note for Sync. These were not part of the 217ff25abc→02a9d1d3e delta (`en.json` has no changed
`web.admin.collections.*` or `web.collections.deleteDialog.*` value in that range). Changed official strings in that range that concern this area:
`web.collections.count.unavailable`, `web.collections.manual.addCheck*`/`addNeedsFullList`/`addNeedsReload`/`addWaitsForTitles` (new, see "Not established") and the Home-row wording
(`web.home.rows.*`, `web.home.addRow.collection.*`), which belong to the Home sections page and are not touched here.

## Plain bounded names

`AdminCollectionNames.Plain` (Core, unit-tested) is used for every collection, row, shelf and library name written into a confirmation, a dialog title or a notice sentence:
control characters, tab, NEL, line and paragraph separators and every space become one space; direction characters (marks, embeddings, overrides, isolates, Arabic letter mark),
zero width space, word joiner, BOM, soft hyphen, tag characters, Hangul/Khmer/Mongolian/Braille fillers and combining grapheme joiner are removed; a lone surrogate is removed (a real
U+FFFD stays); zero width joiner and non-joiner stay only between two visible characters (Persian, Indic, emoji sequences), never at an end or next to a space or another joiner;
the result is cut at 80 user-perceived characters (and 16 UTF-16 units each, so a character with thousands of marks cannot pass) with an ellipsis, never inside a character. A name that shows
nothing is a placeholder (see "Review fixes"; the delete title keeps the official "Delete collection?"). Lists and cards keep the whole name and cut it by trimming, never by slicing.

## Hardening kept

Unchanged and re-checked by the existing fixture steps and review tests: a delete or bulk delete acts on exactly the collections shown and confirmed, once (bulk delete now deletes the same
set its title counts and names the kept ones, instead of confirming a larger selection and skipping some silently; a collection deleted drops out of the selection); an unknown outcome blocks a
repeat until the list is read again; a refused write keeps the draft and shows the server's reason; member edits and reorders are sent once.
**Request bodies and URLs are unchanged**: no `/api/v1`, no new request, no changed field. The official client did not change a collection request between the two commits. **One request change in the second pass**: Show on tabs and Hide from tabs now send a PATCH only for the collections whose visibility differs from what was asked (official `setSelectedVisible`), so a selection that holds collections already in that state sends fewer PATCHes than before; with none to change nothing is sent and no dialog opens.

## Review fixes (second pass, November 21)

An independent review of the first pass found no P1 (no path deletes something the confirmation did not name or count, sends a delete
twice or replays one). It reported two P2 and smaller items; each is below with the check that guards it. Unit: `AdminCollectionNamesTests`,
`AdminCollectionsBulkTests`. Native: `AdminCollectionsNativeFixture` (selector `admin-collections`).

1. **A confirmed bulk action that never ran showed nothing.** `RunAsync` now returns whether the change started; when it did not (another
   change running, or the gate refused it after an unknown outcome) the refusal's own sentence stays in the notice, or "A collection change is
   already in progress. Nothing was changed." when there is none, and no result sentence is written over it. `BulkDeleteResult` is never empty
   for a total above zero. Bulk Delete, Show and Hide are off while nothing is selected, and Sync while no synced list is (official bar).
   Native: single delete ends unknown, cancel, select, confirm Delete 2 / Hide / Show: the notice says why, no request is sent.
2. **Show and Hide counted collections that did not change.** Filtered first (`AdminCollectionBulk.ChangingVisibility`), then counted, confirmed
   and sent. None differ: the official "The selected collections are already hidden." / "…already on Collections tabs.", no dialog, no request.
   **Request change: fewer PATCHes** (stated above). The confirmation says "Hide 2 collections from Collections tabs?" (official title) with
   "2 of 3 selected will change; 1 already hidden." (native wording; the official show has no confirmation, so "Show N collection(s) on
   Collections tabs?" is native too). Native: 3 selected, 1 already hidden: title 2, two PATCHes, result "Hid 2 collections…".
3. **Kept and failed counts.** Official `runBulkDelete`: 404 at delete time counts as deleted, 409 as kept for rows, anything else
   (412, transport) as failed; "Kept N…" counts only 409s. The native pre-filter stays (it avoids requests the server would refuse); those
   collections are named in the confirmation ("N is kept because rows use it: …"), as in official, and are **not** counted again in the result.
   Native: k1 409, k2 404, k3 412, k4 deleted, one collection a row uses: "Deleted 2 of 4 collections. Kept 1 collection in use by home or
   library sections. Changed one: It changed since you opened this. …". A lost reply on one of two: that one fails ("The outcome is unknown…"),
   the other is deleted, nothing is replayed.
4. **A long title lost its library names.** The title part is bounded on its own; entries that are already plain are not cut again.
5. **Counts and names agreed only for names that show something.** Empty names are placeholders, so "N more" and the entries always agree:
   "Untitled collection" (native wording; the catalog has none for collections), "Untitled library", "Untitled row" (official
   `web.home.addRow.preview.untitled`), and "Untitled" for a shelf ("Delete the Untitled shelf?"). `HideTitle` and `DeleteShelfTitle` build the
   official sentence with the placeholder; `DeleteTitle` keeps the official "Delete collection?" fallback.
6. **The starter-pack block.** Wired as official: the bulk confirm stays open and off with "A starter pack is being added. Delete once it
   finishes." while a pack is being added (looked at again every 300 ms while it is open), is on again when it ends, and is asked again at the
   press. The single delete dialog does the same (official has the block on bulk delete only; the first pass had already added it to single
   delete). The unused `blocked:` parameter is gone. Native: begin/end a job under both open dialogs and press while blocked: nothing is sent.
7. **Names still shown raw** now pass through `Plain`: the "Rows that show it" body, bulk failure lines, "Add as a row" labels, the starter-pack
   dialog's library names, the empty state "No collections in {library} yet", the library line on each list row, select labels, and the automation
   names of buttons, selection boxes, tab switches and posters.
8. **A name with a line break drew on several lines (confirmed).** `NameBlock` shows the plain text (no cut; trimming cuts). Native: the block
   is one line high, and a copy with the raw name is measured taller, so the check can fail.
9. **`Plain` edge cases.** A lone surrogate is removed whether or not the name also holds a real U+FFFD. A name of only combining marks (or a
   variation selector) shows nothing. **Valid emoji tag sequences are kept whole**: U+1F3F4, one to six tag letters or digits, U+E007F (the England
   flag is five), counted as one character; any other tag character is removed (so a hidden text cannot ride on another character). Unit tests.
10. **Contrast.** A delete confirm follows a contrast change while it is open (the change arrives on any thread, is applied on the dialog's own, and
    nothing stays subscribed after it closes). **The fixture now asserts real focus, and it found a defect**: in the single delete dialog with rows
    focus started on the checkbox, not Cancel (`DefaultButton` alone does not move it). Fixed with `FocusCancel`. Native: a fake contrast source is
    switched on and off from another thread under an open dialog.
11. **Select bar and a race that matches upstream.** Official `SelectModeBar` is a bar fixed to the bottom of the viewport. The native bar is now a
    row of the page below the scrolling list (not inside the `ScrollViewer`), so it stays in view while the list scrolls and the last collection is
    never under it; no animation. **Difference kept, with the reason**: it is a full-width docked row, not a floating, centered, shadowed pill
    over the content column, which WinUI has no cheap counterpart for. Also recorded: in delete-with-rows, a row the dialog says "keeps its other
    collections" can still be deleted if another administrator removes those other collections between the read and the press; the same holds
    upstream (the server answers the row write).
12. **Test gaps closed.** Native and unit: bulk delete 409 and 412 and a lost reply, the unknown outcome blocking the next change, a refused single
    delete (409 then 412: reason shown, dialog stays, one request, not an unknown outcome), and a collection whose own validator changed between
    the confirmation and the press (per-collection validators in the fixture's server): that one is refused, the others deleted, the result says so.

## Not established

- The official WebUI was not run and no official screenshot exists: layout, color and spacing were compared from source only. No visual or pixel parity is claimed.
- New official text `web.collections.manual.addCheck*`, `addNeedsFullList`, `addNeedsReload`, `addWaitsForTitles`, `count.unavailable`: they belong to a check of whether titles can be safely added while only part of a manual collection is loaded (`ManualContentsPanel`).
  The native page already adds a title after the last position by reading the full position order (see the earlier record, second review item 3) and sends no body for it that this change alters. Whether the official check changes which
  server error a partial-list add produces was not established; no request was changed for it.
- Official hide re-reads the list for fresh row counts before it asks (`withFreshRowCounts`); native hide uses the row counts of the list it already holds (a row added elsewhere in the meantime would be missed by the "rows still show it" sentence). No request was added for it.
- The select bar is a docked row, not a floating pill (item 11); no screenshot comparison was made.
- Focus on Cancel was measured in the native test host's window; a contrast theme, a screen reader and right-to-left rendering were not run.
- The unit test `AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling` (another family) failed in two full-suite runs and passes alone and in the third; see the run record.
- The `⋯` menu and the poster-card pin were not mirrored: the native admin page uses inline buttons by design.
- Hover on the confirm button is verified through the visual state, not a pointer move; a Windows contrast theme was not run.
