# Home Sections source candidate — November 17, 2026

Source checkout: `C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-1963c645-f210096e46b4d872`,
branch `t3/admin-home-sections`, base `248aafd62ed65b868c0e7356926e4d83204649cd`.
Project version remains `1.2.1181-beta.1`; pipeline stamps are unchanged. This is an
uncommitted source candidate for coordinator acceptance. No commit, push, PR,
installer, installed-player/settings action or production operation was performed.

Official source was read with `git show` from
`D:/SiloPlayer-work/server-upstream-nov14` at
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`; no clone, checkout or upstream modification.
The previous stopped checkout `t3-61aa99c1-2348f980b65130fd` remains unchanged.
Its `AdminHomeSectionsApi.cs`, `AdminHomeSectionsApiTests.cs` and
`2026-11-17-admin-curation-incomplete.md` were read. The complete-membership order
fence and its regression tests were deliberately ported with apply_patch, then
extended against the pinned contract. Its incomplete delivery claim is not reused.

## Implemented route and workflows

`AddAdminHomeSections()` registers the actual `AdminHomeSectionsPage` at
`/admin/sections` through the production shell/Frame. Collections and Library
Settings are not registered or implemented by this candidate. Library-page tabs
inside `/admin/sections` belong to this route and are included.

- Home/library page selector, scope-specific definitions including disabled rows,
  count summary, loading/error/reload/empty states, shown toggles, hero and web-only
  audience choice, row actions and previews. The first enabled featured row is
  identified as the banner; collection-poster rows cannot be heroes.
- Add row: catalog search and six official groups; source/type selection; variants;
  row name; collection and library selectors; ordered collection-poster sources;
  legacy curated-title editing; rule groups and match/scope/sort controls;
  bounded people/facet/language suggestions; per-kind parameters; seasonal
  holiday/vibe/name/date/custom-list controls and family movie night; More options;
  item count, hero and Show in the apps; draft preview cards with named artwork
  placeholders and bounded same-origin image reads.
- Edit row reads its own strong ETag before opening. Unknown config is retained
  for untouched sources/renames, variant changes preserve unrelated tuning,
  Continue Reading keeps its locked kind, and retired kinds are editable without
  offering them for new rows. Missing/hidden selected collections remain named as
  unavailable without silently clearing saved identities.
- Native drag reorder and keyboard-accessible Move up/down/top/bottom submit the
  complete reviewed scope membership, never just the visible rows. Selection is
  bounded to 100 for show/hide/delete operations. Delete snapshots precede the
  confirmation and retain their exact row ETags. Restore confirms the captured
  current scope, offers profile reset when supported and optional other library
  pages, and stops on the first unconfirmed/conflicting page.
- Library rows offer eligible same-kind page copies; new rows can be added to
  selected library pages in one bulk request. Copies are never heroes. Home and
  page-bound collection/rule/curated rows do not offer cross-library copies.
- Server-wide Next Up defaults use the existing settings transport with the
  effective settings ETag, per-profile override explanation, mode and new-episode
  preference. Confirmed row/default changes invalidate the retained Home cache.
- `?page=`, `?edit=` and admin collection `?add=collection:library:<id>` links are
  consumed in place; safe registered admin collection return links are supported.
  No source code for another route is required by the registration hook.

## Guarding, bounds and lifetime

Rows are loaded between two strong order ETag witnesses. Duplicate IDs, scope
mismatches, incomplete membership, stale witnesses, pagination and weak validators
cannot authorize a reorder. Each row mutation is tied to its current page owner,
target identity and reviewed row version. Quick and selection actions reject rows
that changed since display. No write refreshes authentication or retries.

An uncertain answer, malformed acknowledgement, 202, transport failure or failed
post-write readback locks further mutations. A 412 requires explicit review. The
UI never silently rebases or replays either outcome; explicit Reload reads the
server state before another operation. A failed batch may have changed its earlier
targets and says to reload/review affected pages. A conflict reload of an editor
requires confirmation before replacing the draft.

Every operation runs under the shared acting-admin authority. Retirement cancels
reads, clears private rows/names/drafts/settings, closes dialogs/editor, and rejects
late continuations. Page departure suspends the controller; a new activation reads
fresh state. No page polling or extra sockets are introduced. Work does not resume
from a retired draft.

Success and error JSON are limited to 4 MiB, each API request has a 20-second
deadline (existing settings transport uses its own deadlines), definitions/order
are capped at 10,000, config at 128 KiB, previews at 12 cards, and same-origin
poster bytes at 10 MiB with at most two reads at once. Multi-page restore has a
90-second total deadline. No media catalog is walked. Media search replaces one
50-item server page at a time. Metadata endpoints at this pin have no paging or
search parameters: collection/library metadata is capped at 2,000 entries and
rendered in 50-choice local windows. Oversized metadata fails closed; a validated
single-collection ID lookup remains available. Existing selected collection-name
hydration reads at most 30 identities sequentially. Source URLs are never placed
in labels, diagnostics or image decoder network URIs.

Native adaptations: page/variant choices use ComboBoxes, group choices use a
scrollable button row, and the editor swaps in place in a native modal panel.
Confirmations use ContentDialog. The list uses native virtualization and scrolling;
there are no slide transitions. The approved source inventory and installer XBF
allowlist include the actual page. New-row item controls follow the WebUI's 1–100
range while existing larger API-valid limits are retained.

## Exact official references

All paths below refer to the official commit above:

- `web/src/pages/AdminHomeRows.tsx`
- `web/src/components/homeRows/{HomeRowsPage,RowLine,RowList,DeleteRowDialog,RestoreDialog,AdminNextUpDefaultsCard}.tsx`
- `web/src/components/homeRows/addRow/{AddRowDialog,RowForm,RowPicker,MoreOptions,ParamFields,CollectionPicker,CollectionsRowEditor,CuratedTitlesEditor,RuleBuilder,SeasonalFields,RowPreview}.tsx`
- `web/src/lib/homeRows/{catalog,payloads,rowDraft,variants,collectionList,bulkCopy,pages,rowLinks,seasonal}.ts`
- `web/src/hooks/queries/homeRows/{useAdminHomeRows,useRowPreview,useRowCollectionOptions}.ts`
- `web/src/api/adminSections.ts`, `web/src/lib/recipes.ts`, `web/src/locales/en.json`
- `internal/apiv2/{admin_sections,admin_sections_types,admin_sections_extras,admin_collections}.go`
- `web/src/hooks/queries/admin/libraries.ts`, `web/src/lib/settingsAdminDefaults.ts`

## Authored versus observed verification

**All new unit and native regression tests are unrun.** No RED/GREEN result,
compilation, binding result, native interaction, screenshot inspection or live
server result has been observed for this candidate. Source-only instructions
prohibit build/test/app/browser/native execution, mutex acquisition and generated
outputs in this lane. Static inspection, manifest/XAML XML parsing and
`git diff --check` are the only local checks; they do not establish compilation.

The unit sources cover full-membership fences, disabled rows, weak/stale validators,
scope/identity rejection, permutation writes, retirement, unknown no-replay lockout,
explicit reload, config/variant preservation, picker limits, bounded preview,
library create/copy payloads, seasonal validation and malformed/oversized outcomes.
The native source runs through production registration, shell and Frame; drives
add/source/preview/save/reorder/conflict/discard/delete, library tabs and bulk copies,
unknown applied create and authority retirement; and authors list/editor/conflict
captures at 1440/1024/800 DIP. It waits for editor/dialog closure and page unloading
before restoring fixture globals. It uses synthetic identities and data only.

Coordinator acceptance, only in an assigned runtime slot after quota checks,
the one nonblocking `SiloOct18Build.WaitOne(0)` lease, deadlines and containment:

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --filter "FullyQualifiedName~AdminHomeSections|FullyQualifiedName~UserOnlyClientArchitectureTests"
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishDir=D:/SiloPlayer-build/admin-acceptance/sections/app/
powershell -NoProfile -ExecutionPolicy Bypass -File tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory D:/SiloPlayer-build/admin-acceptance/sections/app -Only admin-home-sections
```

These are command payloads for the coordinator's serialized bounded runner, not
authorization to bypass its source/PDB binding, output-root and process-containment
requirements. Run the full unit suite and relevant Admin foundation native checks
after focused acceptance. Inspect the emitted captures and drive remaining rule,
seasonal, selection, restore and settings states against the pinned WebUI before
any visual-parity claim. Live server interoperability, real device drag, Narrator,
IME, RTL/localization and all-DPI behavior remain unobserved acceptance limits.
