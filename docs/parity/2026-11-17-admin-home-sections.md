# Manage › Libraries › Home sections (`/admin/sections`) — Beta candidate

Scope: the native Home sections page, its library-page switcher, the row editor and every dialog,
registered through `AddAdminHomeSections()` and reachable through the Admin navigation. Collections and
Library settings are other families; nothing here depends on their code.

Official source: `Silo-Server/silo-server` at `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`, read with
`git show` from the pinned checkout. Beta base: merge of `origin/beta` into the family branch (project
version unchanged).

## What matches the official page

- **List** (`AdminHomeRows.tsx`, `HomeRowsPage.tsx`, `RowLine.tsx`): title and subtitle; More menu
  (Select rows, Restore defaults…) and Add row; Home plus one page per library; `N rows · M on`;
  the library-page note; loading, failed-read (server problem text and Reload), empty and populated
  states; the official one-line row descriptions (`describe.ts`, ported to `AdminHomeSectionsDescription`);
  the off-row sentence, hero tag, web-only banner tag and title count; a shown switch per row that
  follows the saved value; drag reorder plus Move to top/bottom, and the reorder hint.
- **Row menu**: Edit row…, Use / Stop using as hero banner (not for a row of collections), Move to top/bottom,
  Add to other libraries… (library pages, copyable kinds, more than one fitting page), Delete row….
- **Select mode**: Select all (up to 100), `N selected`, Turn on, Turn off, Delete…, Done, Escape.
  Batches read each row, check it against the page, write only `enabled`, report each row as changed, left alone
  or failed, and continue past a refused row (official `setShownMany`).
- **Delete** (`DeleteRowDialog`, `DeleteRowsDialog`): version captured before confirmation, a stale row keeps
  the dialog and asks for an explicit reload, 404 counts as deleted, bulk delete reports per row with progress.
- **Restore defaults** (`RestoreDialog`, `restoreLibraryPages`): what is replaced, collection rows removed,
  other pages, optional "also restore every library page" and "also reset every profile" (disabled with the
  official note when the server cannot), conflict banner with Reload, per-page kept / unconfirmed results.
- **Add to other libraries** (`AddToOtherLibrariesDialog`): fitting pages by library kind, "already here",
  one bulk write of non-hero copies.
- **Add row / Edit row** (`AddRowDialog`, `RowForm`, `RowPicker`, `MoreOptions`, `ParamFields`, `VariantChoice`,
  `CollectionPicker`, `CollectionsRowEditor`, `CuratedTitlesEditor`, `RuleBuilder`, `SeasonalFields`, `RowPreview`):
  picker groups and kinds from the server catalog (kinds no longer offered are not offered), variants and their
  official names, the name that follows its variant or collection until typed over, per-kind fields in the
  official slots (library filter on Home only for recently added/released; media type, libraries and order for
  watchlist/favorites; trending show; look-back, runtime, milestone, genre; spotlight), More options
  (1–100 titles, hero banner, Show in the apps), rule rows in the official query shape, Editor's picks rows already
  saved, seasonal holidays with per-holiday vibe, name and own dates, custom MDBList holidays with the official
  inline messages, the picks source, family movie night, and the live preview.
- **Next Up defaults** (`AdminNextUpDefaultsCard`): saves as each control changes, one save at a time.
- **Collections link contract** (`rowLinks.ts`, `useRowLinks.ts`, `collections/rows.ts`): `?page=`, `?add=collection:library:<id>`,
  `?edit=<rowId>`, `?return=<path>` are read once the page can change and dropped from the address. A personal
  collection, an unknown kind or an empty id is refused; a missing row, a locked page or a missing catalog says so;
  `?return=` is honoured only for `/admin/collections/…` or `/collections/…` paths that decode cleanly.

## Write contract

| Action | Request |
|---|---|
| Quick switch / hero | `PATCH /api/v2/admin/sections/{id}` with only `enabled` or `featured`, `If-Match` = the version just read |
| Edit save | `PATCH` with `title`, `section_type`, `featured`, `enabled` (saved), `item_limit`, `config`; `web_only` only when a hero's choice changed |
| Add row | `POST /api/v2/admin/sections` with `scope`, `library_id` (string, library only), `position` = bottom, `section_type`, `title`, `featured`, `item_limit`, `config`, `enabled` |
| Add to pages | `POST /api/v2/admin/sections/bulk` with `scope: library`, `library_ids` (strings, this page first), `featured: false` |
| Reorder | `PUT /api/v2/admin/sections/order` with every id, `If-Match` = the order version read between two matching reads |
| Restore | `PUT /api/v2/admin/sections/defaults` with `{"reset_profiles":bool}`, each page over its own order version |

A 4xx answer other than 408, 412 and 404 is a definite refusal: the server's detail and field errors show and the
page stays writable. 412 and 404 ask for a reload. A timeout, transport failure, 5xx, malformed or unconfirmed
answer, or a failed readback after a write, is an unknown outcome that blocks further writes until the page is
reloaded; nothing is replayed. A create or bulk create with an unknown outcome says to check the other library pages.

## Native adaptations and known gaps

- Page and variant choices are ComboBoxes; kind groups are a button row with search instead of the scroll-spy rail.
- Keyboard reorder: Move up / Move down are added to the row menu because the WebUI's handle-and-arrow drag has no
  native list equivalent. A row preview dialog stands in for the hover poster peek.
- The live preview loads when the form opens and on request (Live preview); the WebUI refreshes it as the draft changes.
  A new row is scrolled into view but not highlighted. The `addedRow` navigation state the WebUI hands the Collections
  page has no native equivalent and is not sent (that page is not in Beta).
- Back to the collection is offered only when the Collections route can open in this build.
- When the library list cannot be read the page shows Home with a Reload banner (the WebUI shows a page error only when the
  link names a library page). A link that names a library page is not followed until the list has been read.
- After an unknown outcome stops a select-mode Turn on / Turn off, the rows that were never sent are counted in one closing
  sentence; the WebUI does not stop the run, so it has no such rows.
- Collection and library metadata have no paging parameters at this pin; they are read once, capped at 2,000 entries,
  and a single collection can be looked up by ID.
- Rich-text tags in official messages render as their plain text.

## Verification

See the Home sections entry at the top of `docs/current-development.md` for exactly what was run and what was not.
