# Collections source candidate — November 17, 2026

Prepared checkout: `C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-07e6e8f7-ad1c1ca2a7201e01`.
Branch: `t3/admin-collections-page`. Base: Beta
`248aafd62ed65b868c0e7356926e4d83204649cd`, version `1.2.1181-beta.1` unchanged.
Candidate consists of 22 changed files, all uncommitted, in this checkout. No child agents,
commits, pushes, PR actions, builds, tests, native/browser launches, mutex acquisition,
generated outputs, installer/settings changes, or installed-player actions occurred.

## Official source and contracts

All upstream reads used `git show 217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb:<path>`
in the existing `D:/SiloPlayer-work/server-upstream-nov14` repository. No clone or checkout
setup was performed. Primary references at that exact commit:

- `web/src/pages/AdminCollections.tsx`: List/Arrange, library/type/search/failed filters,
  selection, visibility, sync, deletion and starter-pack jobs.
- `web/src/api/adminCollections.ts`: canonical editor validators and
  `fetchAdminBoardOrderSnapshot`. Native `BoardAsync` reads the library group-order witness,
  definitions and collections, at most four member-order reads concurrently, then the same
  witness again. A changed witness, duplicate/missing memberships, partial orders or invalid
  validators reject the board. Whole-order writes use the observed target ETag and complete
  order; collection moves retain every destination member.
- `web/src/api/adminCollectionRows.ts`: guarded reference removal before collection delete.
  Native deletion reads the collection before confirmation, checks the same validator before
  any row changes, reads each row freshly with a strong ETag, preserves other references and
  unrelated config in `collection_posters`, and deletes a row only when it solely references
  this collection. A missing row is already done. The first other failure stops the sequence.
  After this run changed rows, a canonical collection read advances the validator only if
  its definition is unchanged (signed poster/backdrop URLs excluded, as upstream does).
  There is no whole-section-set replacement. Bulk deletion keeps collections used by rows.
- `web/src/components/collections/admin/{GroupsBoard,CollectionListItem,ShelfDialogs,ViewerPreview}.tsx`
  and `web/src/lib/collections/shelves.ts`: regular versus personal shelves, synthetic
  `ungrouped`, shelf sorts, pin behavior, move and rename/delete controls, hidden preview.
- `web/src/components/collections/editor/CollectionEditor.tsx`, `DetailsPanel.tsx`,
  `ManualContentsPanel.tsx`, `SmartRulesPanel.tsx`, `SyncedListPanel.tsx`, `LookPanel.tsx`,
  `WhereItShowsPanel.tsx`, `ConflictBanner.tsx`; fields `OrderBlock.tsx`, `ScheduleField.tsx`;
  `web/src/lib/collections/{scope,rows}.ts` and `web/src/lib/homeRows/rowLinks.ts`.
- `internal/apiv2/admin_collection_{types,groups,extras,sections}.go`,
  `internal/apiv2/admin_collections.go`, `internal/collections/templates/templates.go`.
  Outer v2 library IDs are strings. Raw `query_definition.library_ids` remain numeric.
  Template catalogs contain `categories[].templates`; bundles contain their template summaries.
- `web/src/locales/en.json`: primary labels, navigation, list/arrange text and named schedule
  choices. Native feedback describes bounded lists, unknown outcomes and unavailable cross-links.

## Delivered source

`AddAdminCollections()` registers an actual activatable WinUI page for `AdminRoute.Collections`.
The existing shell exposes the Libraries › Collections leaf/search entry and routes
`/admin/collections`, `/admin/collections/new`, and `/admin/collections/:id/edit` through its
real inner Frame. Create replaces `/new` in place after receiving its saved identity.
The app registration, source allow-list and installer XBF allow-list are updated.

List includes collection artwork, title/type/library/count metadata, pin/Home/hidden/sync
states, filters, local 50-row display windows, selection, show/hide confirmation, sync and
delete. Arrange includes shelves, ungrouped/personal states, create/rename/sort/delete shelf
dialogs, native drag/drop and Space/arrow handles, Move to shelf, pin, delete and viewer
preview. Sorted shelves follow their sort; only manual shelves apply the pin band.

Editors include details, library selection/removal warning, Collections-tab visibility,
manual title search and staged creates, individual membership removal and guarded complete
item-order changes, grouped smart rules and advanced rules, sort/limit and unsaved preview,
saved list sources (MDBList, TMDB charts/list/franchise/Discover), named/custom schedules,
sync status and saved-title preview, staged artwork URL/upload/removal, leave confirmation,
and explicit stale-editor field review. Review adopts a fresh ETag and selected local fields;
it sends no write until the user saves again. Template selection seeds a real editor.
Starter packs support library choices, replacement, optional Home/library featured templates,
dry-run review and a single queued apply with bounded job observation and result summaries.

Collection links to Add as a row/Open row use official `page`, `add`, `edit`, `return` query
parameters. Home Sections has no implementation in this base, so these cross-links report its
unavailability when unregistered. This candidate neither registers nor implements Home Sections.
Viewer `CollectionsPage`, `CollectionBrowsePage` and `CollectionEditorPage` are untouched.

Reads are identity/page bound and size limited (4 MiB JSON). Superseded list/editor reads
cancel. Retirement clears drafts, staged images/titles, dialogs, timers and displayed data;
unloaded controllers detach from shared authority. Writes are single-send with authentication
replay disabled. Ambiguous transport/schema/server outcomes require reconciliation; a known
created ID survives a failed canonical read so Save cannot create it again.
No API traverses a whole media library. Title searches request 30 results; previews 24;
manual membership pages 50 with at most 100 retained cursor witnesses; item order rejects the
server's `has_more` window above 200. Collection lists are bounded at 5,000 and library lists
at 1,000. Board has at most 200 shelf positions. Oversized all-library lists retain a library
recovery picker, and direct editor routes need no collection-list walk. Select/bulk/view
deletion is capped at 100 reviewed targets. Artwork loads only when visible, four at a time,
with 2 MiB encoded and 256×384 decode limits; external asset URLs receive no Silo authorization
and signed URLs are never printed. Job polling ends on retirement, completion, error or 120
three-second observations; an accepted job is never represented as completed work.

## Authored evidence — all execution pending

`AdminCollectionsTests.cs` contains 14 Facts and one three-case Theory (17 authored cases):
mixed-revision board rejection, strong ETags, single-send unauthorized save, partial-order
rejection, delete precheck, shared-row preservation, retired ownership, lost-create recovery,
confirmed-create/read-failure recovery, numeric query IDs, row conflict stopping, explicit
field rebase, superseded-read cancellation, public-artwork credential isolation and conditional
TMDB chart choices.
The first safety tests were authored before production code. No RED/GREEN result is claimed.

`AdminCollectionsNativeFixture.cs`, selector `admin-collections`, mounts production DI,
AdminShellPage and nested Frame against a synthetic stateful HTTP handler. It authors checks
for registration/no transitions/no catalog walk on entry, list/arrange, native drag wiring,
hidden viewer preview, canceled move, smart preview, create and in-place route replacement,
leave guard, bounded title search, canceled and confirmed guarded shared-row deletion,
responsive capture and authority retirement. It closes dialogs and unloads the page before
restoring global services. Five captures are authored (1440 list/arrange/editor/delete and
800 list); no images have been generated or inspected.

Observed here: source/contract review and `git diff --check` only. Compilation, unit/native
execution, live-server compatibility, visual parity, physical drag, IME/Narrator, external
artwork storage and all-DPI behavior remain unobserved. This source candidate is not a passing
build or a parity certification. Existing historical native receipts verify other source.

## Coordinator acceptance commands (not run by this worker)

Run from this exact checkout, only in the assigned runtime lane after the required storage,
single nonblocking `SiloOct18Build.WaitOne(0)`, deadline and containment checks. Preserve
matching source/PDB/publish/host byte receipts. Use the coordinator's one authorized output
root; do not use the generic native script's default extra build/copy outputs.

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --filter FullyQualifiedName~AdminCollectionsTests --artifacts-path D:/SiloPlayer-build/artifacts
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --filter FullyQualifiedName~UserOnlyClientArchitectureTests --artifacts-path D:/SiloPlayer-build/artifacts
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path D:/SiloPlayer-build/artifacts
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -p:Platform=x64 -p:PublishDir=D:/SiloPlayer-build/publish/ --artifacts-path D:/SiloPlayer-build/artifacts
dotnet build tests/SiloPlayer.NativeRegressionTests/SiloPlayer.NativeRegressionTests.csproj -c Release -p:SiloPlayerAssemblyDirectory=D:/SiloPlayer-build/native-reference -p:OutputPath=D:/SiloPlayer-build/native-host/ --artifacts-path D:/SiloPlayer-build/artifacts
```

After the matching host/reference/publish receipt is established, launch the contained host
with these exact environment values and the coordinator's task-specific result directory:

```powershell
$env:SILO_NATIVE_TEST_APP_DIRECTORY = 'D:/SiloPlayer-build/publish'
$env:SILO_NATIVE_TEST_THEME_PATH = (Join-Path (Get-Location) 'src/SiloPlayer/Themes/DarkTheme.xaml')
$env:SILO_NATIVE_TEST_ONLY = 'admin-collections'
$env:SILO_NATIVE_TEST_RESULT_DIRECTORY = 'D:/SiloPlayer-build/admin-collections-evidence'
# Create the result directory and execute the next command only through assigned containment.
& 'D:/SiloPlayer-build/native-host/SiloPlayer.NativeRegressionTests.exe'
```

Inspect every rendered capture and failure state before acceptance. Exercise template/import
provider responses, artwork picker and job completion with synthetic fixtures and the pinned
live reference; unit/native source alone does not certify those external interactions.
