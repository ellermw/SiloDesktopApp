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

## Second independent review (November 20): eight more findings, all confirmed and fixed

Each was checked in the code and against the official source at `217ff25a` before changing anything. Tests are in
`AdminCollectionsReviewTests` (unit) and `AdminCollectionsNativeFixture` (real page). Native checks for 1, 3 and 8 were
shown to fail with those three fixes temporarily reverted (run label `collections-fix-negative-r1`, then restored).

1. **Starter job stuck.** A failed poll read or the 120-read cap left `StarterJobId` set with no timer, so Refresh
   (`ResumeJobAsync` returns while an id is set) could not rediscover the job and delete, batch delete and starter
   packs stayed blocked. Both ends now call `EndStarterJob()` (the failed read only when the id is still the one
   being polled). Native step: a failing poll, then the cap, each end observation with the timer stopped and Refresh
   rediscovers the running job and polls it again.
2. **Job id stored for a retired authority.** `BeginStarterJob(owner, id)` stores the id only while that lease is
   current and is still the controller's owner (checked under the state lock); the dialog passes the authority it was
   opened under and treats `false` as not accepted. Unit test covers retired, replaced and null owners. The window
   between the command's last retirement check and the UI continuation cannot be produced deterministically in the
   fixture, so the native run covers only the accepted path through the dialog's new call (existing starter-pack steps).
3. **New member position collision.** A title added to a saved manual collection goes after the last position
   (`NextMemberPosition`, official `ManualContentsPanel`): max(position)+1 when the first items page is the whole list,
   otherwise the larger of `item_count` and the items read. Like the official panel, a collection of more than 50
   titles falls back to that larger value rather than walking every page.
4. **Validation errors hidden.** Any 4xx other than 401/403/409/412 now shows the server's own sentence
   (`ApiProblemText.Display`, redacted, limited to 300 characters); an empty problem or a 5xx stays generic.
5. **Failed hide after import.** The follow-up hide is caught on its own: the confirmed create (`CreatedId`) is kept,
   the unknown-outcome flag it may have set is cleared because the canonical read of that collection follows, and
   `SaveWarning` ("It still shows on Collections tabs: …", the official wording) is shown after the reload.
6. **List link not cleaned.** A typed MDBList link loses surrounding spaces, `?query`, `#fragment` and trailing slashes
   (`/json` stays), for new and saved lists alike. TMDB list links and bare ids are stored as the canonical list page
   (the official `tmdbListSource`), or only trimmed when they name no list. Only typed text is cleaned, as in the
   official editor; a stored link is left alone, and the "Saved source" JSON editor stays raw by design.
7. **Template poster not sent.** The template's `poster_path` is carried into the draft and sent as `poster_url` with
   the import unless a poster is staged; no other kind of save carries it.
8. **Duplicate create after an unknown outcome.** "Check the collections list" reads the unfiltered list, so a create
   that went to another library than the page's filter is found instead of offered again.

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

The retirement race in second-review item 2 is covered by a controller unit test, not by the real page. Live-server compatibility, all-DPI/IME/Narrator behavior, physical drag-and-drop, external artwork storage and
complete visual parity were not observed. Native captures are synthetic-data, one DPI.
