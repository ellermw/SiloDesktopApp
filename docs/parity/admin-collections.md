# Admin Manage > Libraries > Collections (`/admin/collections`)

Branch `t3/admin-collections-page`. Official source: Silo Server/WebUI at
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb` (read with `git show` from the existing upstream checkout).
Version numbers are untouched. This note records what was observed; the run label and counts are in
`docs/current-development.md`.

## Scope delivered

A registered, activatable WinUI page for `AdminRoute.Collections`, reached through the real Admin
navigation (Manage > Libraries > Collections) and the shell search, with the descendants
`/admin/collections`, `/new` and `/:id/edit` served in the shell's inner Frame (no slide, in-place
`/new` -> `/:id/edit` replacement after a confirmed create).

- List: artwork, kind/library/count metadata, On tabs / On Home / Pinned / sync-failed states, search,
  type and "Sync failed" filters, Clear filters, 50-row local pages, select mode (Show on tabs, Hide from
  tabs, Sync lists, Delete…), Delete all in this view…, loading, load-failed (with library recovery),
  empty (with Add a starter pack) and no-match states.
- Arrange: shelves (add, rename with sort, delete, reorder), collections (drag handle with Space/arrow
  keys, Move to shelf, Pin, Delete), "What viewers see" preview (hidden omitted, sorted shelves follow their sort).
- New collection picker (Manual, Smart, Synced list), template catalog dialog that seeds a real editor,
  Starter packs (library choice, dry-run review, single queued apply, bounded job observation).
- Editors: details, Where it shows (libraries with removal warning, Collections-tab visibility, Rows that
  show it, Add as a row), manual titles (search limited to 30, staged adds, individual removal, guarded
  complete reorder), smart rules (own rule libraries, grouped rules, advanced JSON, max titles, saved
  default sort line with Clear, live preview of unsaved rules), synced lists (MDBList search when the server
  reports `mdblist_search`, pasted link, TMDB chart/list/franchise/Discover, default sort, schedule, sync now),
  artwork URL/upload/removal, leave guard, conflict review that adopts a fresh validator and writes nothing.

## Review findings fixed (each has a failing-without-fix test in `AdminCollectionsReviewTests`)

1. Create with a malformed or missing identity (a number, blank, nested object) is an unconfirmed outcome:
   `OutcomeUnknown` blocks writes. Only a re-read of the collection list clears it; a fresh draft or editor-only
   load does not. Applies to create and import.
2. A confirmed create keeps its identity (`CreatedId`) across a failed canonical read. Save and the editor's
   Reload reload that identity and never create again; Reload also replaces `/new` with the identity.
3. The starter-pack job identity and its read count live in the controller and are dropped on authority
   retirement and suspension; a retired page never polls or acts on it.
4. A smart collection's rules keep their own `query_definition.library_ids` (numeric; none = every library),
   independent of Where it shows (`library_ids`, strings). Save no longer overwrites them; a new smart editor
   opened from a library starts with that library, as upstream does. The editor has its own "Titles from" picker.
5. Smart preview is a read-only POST answered with `{items:[{content_id,title,type}],total}`. It is parsed
   strictly (`ParsePreview`), never goes through the write gate (a failed preview cannot block saving), and
   now runs live (debounced) with the official wording.
6. Changing a smart collection's Order clears the stored default `sort_config.field/order` (other settings stay),
   and a saved default sort is explained with a Clear button, as upstream `clearDefaultSort` does.
7. MDBList search (`GET /api/v2/collections/import/mdblist/search`) is offered in a new synced list editor when
   capabilities report `mdblist_search`; otherwise the official "needs an API key" explanation is shown.

## Other defects found by running the code

- `JsonObject` request bodies were serialized by the generic body path as the node's own public members
  (`Options`, `Parent`, `Root`), so every collection write would have sent garbage. Bodies now travel as
  `JsonElement`.
- The editor re-render re-parented the same panels into a second card and threw; panels are rebuilt per render.
- Checkbox handlers used `Click`, which the UI Automation Toggle pattern never raises; they use Checked/Unchecked.
- Manual collections showed an invented sort selector; they now show the official read-only "Your order…" line.
  Synced lists gained the official default sort.

## Deliberately not implemented / deviations

- Home Sections is a separate family and is not registered here: "Open row" and "Add as a row" use the official
  `/admin/sections` query contract and report that Home Sections is unavailable when it does not resolve.
- Smart rules are edited with the shared native rule editor (sentence layout differs from the web builder);
  the web's popular-picks tab for MDBList is reached through Browse templates, not inline in the editor.
- Live preview shows titles as text, not poster tiles.
- Artwork picker (OS file dialog) is not driven by the fixture; URL staging and the guarded write are.
- Text is English-only, in the page code, as in the other merged Admin pages.

## Not verified

Live-server compatibility, all-DPI/IME/Narrator behavior, physical drag-and-drop, external artwork storage and
complete visual parity were not observed. Native captures are synthetic-data, one DPI.
