# Admin Manage › Libraries › Libraries

Official reference: Silo-Server/silo-server **e130f262d412cc7ffc4f0e040bdaba02c132515e** (official main,
November 20, 2026; read from the lead's pinned local object store, never fetched by the worker). The page
was built against 17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8 (November 16, 2026), whose Libraries
contracts/pages were unchanged from abd85d0cadac28f4ab7db6821dad9634fc114790. The November 20 refresh
below compares the page element by element with e130f262d.
The scope audit's pin 7115ee7028c7c9bde9e201fd5f54e87ea70de3a7 is historical:
between the two, every Libraries operation and schema in `contracts/api/v2/openapi.json` is
unchanged; the WebUI changed only accessibility and motion details (`AdminLibraries.tsx` name ids
and `aria-describedby` for cleanup, reduced-motion sortable hooks; `LibraryFormSections.tsx`
path-input ids, remove-path `aria-label`, `aria-expanded` on provider levels, move up/down names;
poster remove `aria-label`; translate select border token). The native page carries those names.
Tested desktop base: Beta 173c37974894fe5d52a4a4437c356c84e5c897d9
(1.2.1165-beta.1, version unchanged and pipeline-owned). Remote Beta was subsequently observed at
7ba72bed148a29e737ec708997d8fedc259f7a0d; its changes since the tested base are stable-release
version stamps and documentation, not behavioral application changes. No version edits belong
to this feature's diff. This is a standalone Libraries increment, not all Manage/Configure work.

Route `/admin/libraries` (`AdminRoute.Libraries`), registered through `AddAdminLibraries()`.

## Scope (WebUI files → native)

| WebUI | Native |
|---|---|
| `pages/AdminLibraries.tsx` (header, table, scan queue popover, drag reorder, dialogs, older jobs) | `Views/Admin/AdminLibrariesPage*.cs`, `AdminLibrariesController` |
| `components/admin/libraries/LibraryCardList.tsx`, `LibraryRowParts.tsx`, `useLibraryRows.ts`, `RealtimeMonitoringBadge.tsx`, `admin/trickplay/TrickplayLibraryBadge.tsx` | `AdminLibrariesPage.List.cs`, `AdminLibraryPresentation` |
| `LibraryEditorDialog.tsx`, `LibraryFormSections.tsx`, `useLibraryForm.ts`, `LibraryPosterSection.tsx`, `LibraryTranslatePanel.tsx`, `libraryTypes.ts`, `realtimeMonitoring.ts` | `AdminLibraryEditorView.cs`, `AdminLibraryEditorSession` (+ `AdminLibraryTranslationSession`) |
| `components/FolderBrowser.tsx`, `PathAutocompleteInput.tsx` | `AdminLibrariesPage.Editor.cs`, `AdminFolderBrowserSession`, `AdminPathCompletion` |
| `LibraryRefreshDialog.tsx`, `ConfirmDialog` (delete, empty-root cleanup) | `AdminLibrariesPage.Dialogs.cs` |
| `UnmatchedItemsSection.tsx`, `MetadataMatcherQueuesSection.tsx`, `AmbiguousRootsSection.tsx` (+ `RootOverrideDialog`), `SkippedRootsSection.tsx`, `StaleIDsSection.tsx`, `DiagnosticsListParts.tsx`, `CollapsibleDiagnosticsSection.tsx` | `AdminLibrariesPage.Diagnostics.cs`, `AdminLibrariesPage.Roots.cs`, `AdminLibraryDiagnostics` |
| `hooks/queries/admin/{libraries,scanControls,libraryTranslation,trickplay,taskJobs}.ts`, `api/v2/libraries.ts` | `AdminLibrariesApi(.Diagnostics)`, `SiloApiClient.AdminLibraries`, `AdminLibrariesModels` |

Included: title/subtitle; active scan queue popover (groups by library, running/queued counts, four
rows then “+ N more”, per-library Cancel); Scan all (`scan_libraries` task); Catalog maintenance
cross-link; Add library; the 1024 px table (grip, name, paths or “N folders”, type, status badges
incl. monitoring attention and seek-preview progress, last scanned + warning time, actions) with
active-work rows (refresh job with stop, scans with stop, Show all/Show less) and scan-warning rows
(partial walk, empty root, dead root with Check mount and Confirm cleanup); compact cards below
1024 px with Show paths, card action menu (refresh/stop, verify mounts keeping the menu open, edit,
move up/down keeping it open, delete, cleanup) and the full-width scan/stop button; row commands
(scan/stop scans, metadata refresh mode dialog or stop refresh, mount check with 5 s result, edit,
delete confirmation → deletion job, cleanup confirmation); keyboard and pointer reorder; Load older
refresh jobs / Restart jobs; the editor's General (name, seven type tiles, type-change warning,
enabled, poster upload/replace/remove), Folders (paths with server completion, remove, Browse
server folders, Add path, real-time monitoring switch with server-off link or status line),
Metadata (bare ISO 639-1 language, auto-translate, translation prewarm with Translate/Stop and
progress, box sets with 2–5 (+saved) sizes and search toggle, trailer/extras kinds, provider
priority with local metadata switch, per-level collapsible remote providers, enable, move up/down)
and Advanced (chapter thumbnails, seek previews when offered, marker detection copy by type), with
the Advanced tab only for types that support it and validation switching to the failing section;
the five diagnostics sections with their search, paging, states and actions.

Not in this leaf: Catalog import/export (Configure › Maintenance), item seek-preview regenerate and
item match internals (item tools), the server's stale-ID rematch and matcher-queue cancel routes
(not exposed by the WebUI; the native page follows the WebUI).

## Refresh against e130f262d (November 20, 2026)

Scope: Manage › Libraries › Libraries only (the Library settings and Autoscan pages are separate).
**There was no running official server and there are no official screenshots.** Everything below is a
source comparison (official TSX/CSS read element by element) plus captures of the native page that were
opened and read; no pixel parity is claimed.

### What changed upstream between 17d5376 and e130f262d (209 commits) for this page

Transitive imports of `pages/AdminLibraries.tsx` (175 files), `en.json` keys the page uses, the Libraries
operations and schemas in `contracts/api/v2/openapi.json` (72 paths, 157 schemas in the closure) and the
server library handlers were diffed.

| Upstream change | Effect on this page | What was done |
|---|---|---|
| #4762 matcher queue paths stay left to right on desktop in RTL languages (`dir="ltr"` always) | Matcher detail paths | Already true: `AdminLibrariesUi.Mono` forces LeftToRight for every path in table and card layouts. No change. |
| #4727 / #4766 / #4753 server paths and URLs left to right | Other pages (nodes, downloads, settings, plugins); the Libraries paths already carry `dir="ltr"` | Nothing to do here. |
| #4700 `--destructive-fill` (#dc2626) with `--destructive-foreground` under red buttons and badges | Disabled/warning/error badges, Stop scans, Delete/cleanup confirmations | **Fixed** in the page: destructive badges and buttons use a solid fill that holds 4.5:1 under white text (the theme's destructive colour darkened until it does) instead of the text colour at 90% with dark text. The shared `DestructiveButtonStyle` is unchanged (see below). |
| #4671 `dark:` follows the Silo theme; #4728 accent label contrast | No Libraries component uses `dark:` or the new `*-label` tokens | None. |
| `useGripDragSensors` (touch drag after a 200 ms rest, mouse after 5 px) | Used by Home rows; the libraries table still uses the pointer and keyboard sensors | None. |
| `app.css` static live badge, star ratings, high-contrast underline, `@variant max-sm` header | None of them on this page; the phone header breakpoint stays 640 px | None. |
| #4578 `catalog.library.changed` on enable/disable | The page does not subscribe to it (it reads only `scans` and `jobs`) | None. |
| Removed exports and test files (#4452, #4465) | No behaviour | None. |
| `i18n/format.ts` bidi isolates | The native `web.admin.*` table is English only | None (limit: no RTL or other locale). |
| `openapi.json` | Of 72 library, scan, trickplay, filesystem and matcher paths only `GET /watch/{id}/trickplay` (player) changed, and of 157 schemas only `Section`/`SectionCollectionCard` (Home rows). Every Libraries request body and response shape is as before; every official string the page prints (the table's keys, compared key by key against both commits) is unchanged. | None. |

### Differences found against the official source, and what happened

Radii: the WebUI theme sets `--radius: .75rem`, so `rounded-sm/md/lg/xl/2xl` are 8/10/12/16/20 px (the
theme resources `RadiusSM…Radius2XL` hold these). The page had used Tailwind's default 6/8/12/16 pixel
values. Status: **fixed** unless noted.

| # | Official | Native before | Status |
|---|---|---|---|
| 1 | `Badge` `rounded-md` (10), `bg-secondary text-secondary-foreground` for secondary | 6 px, muted fill (no visible fill on the dark theme) | Fixed: 10 px, secondary fill and text |
| 2 | Destructive `Badge`/`Button`: solid `destructive-fill`, white text | Destructive colour at 90%, dark text | Fixed (see above) |
| 3 | `Button size="sm"`: `text-sm` (14), `rounded-md` (10), `px-3` (`px-2.5` with a direct icon), icon 14 / 16 (`h-4 w-4`), `gap-1.5` plus `me-1` where the icon has it | 13 px text, 8 px radius, 12 px padding, 14 px icon, 6 px gap | Fixed: `Action` takes icon size and gap; Add library, Catalog maintenance, Browse, Add path, Check mount, Confirm cleanup, Up use 16/14 px icons and a 10 px gap; unmatched/stale/override row buttons are `h-7 text-xs gap-1` with 12 px icons |
| 4 | Ghost `icon` buttons `rounded-md` | 8 px | Fixed: 10 px |
| 5 | `TableHead`/`TableCell` `px-2 sm:px-3`, header `h-10 py-3 text-start` | 8 px side padding, "Actions" right aligned | Fixed: 12 px side padding, header start aligned |
| 6 | Table and card panels `surface-panel … rounded-2xl border-0` (20 px) | 16 px | Fixed: 20 px |
| 7 | Setting cards, type tiles, box-set card, dashed empty boxes, matcher table frame, card messages, active-work and warning panels in cards `rounded-xl` (16) | 12 px | Fixed: 16 px |
| 8 | Provider rows, trailer-kind rows, translate panel, folder-browser box, selects and inputs `rounded-md` (10) | 6 / 8 px | Fixed: 10 px |
| 9 | Scan queue rows `rounded-lg` (12); popover and card menu `rounded-md`; menu items `rounded-sm` (8) | 8 / theme default / 6 px | Fixed |
| 10 | Matcher detail entries `rounded-lg` (12) | 8 px | Fixed |
| 11 | `Label` is `text-sm font-medium` (14) | 13 px | Fixed |
| 12 | Editor dialog `rounded-lg` (12); type tile `size-10 rounded-xl` (16); close icon 16 px at `end-4 top-4`, header `pe-12`; phone header `ps-4 pe-2 pt-3 pb-3`, footer `px-4 pt-3 pb-3`, 44 px buttons, error on its own row and Save `flex-1` | 16 / 12 px radii, a 32 px close button, one padding set for all widths | Fixed |
| 13 | Tabs trigger `rounded-lg`, `border` transparent, active `bg-accent border-border`, `gap-2.5` (phone `gap-2`) | 8 px, muted fill, no border | Fixed |
| 14 | `border-dashed` empty/loading boxes and the poster placeholder | solid border | Fixed: dashed outline drawn over the rounded fill (3 px dashes) |
| 15 | Provider level title `tracking-wider` | 0.1 em | Fixed: 0.05 em |
| 16 | Diagnostics section `surface-panel-subtle overflow-hidden rounded-2xl` (surface 72%, 1 px border 62%, 20 px), header `px-4 lg:px-5 py-4`, icon tile `rounded-lg`, open count badge `text-[11px]` | surface 55%, no border, 16 px, 16 px padding at every width, 8 px tile, 12 px badge | Fixed |
| 17 | Header actions below `lg`: `flex flex-wrap gap-2`, items `flex-1 min-w-fit h-11`, Add library `basis-full`, `gap-y-3` under the title | Fixed two columns on phones; 16 px gap | Fixed: `FlexWrapStretchPanel` packs lines by natural width and grows items equally; 12 px gap |
| 18 | Stop-task buttons are `h-5 w-5`; on touch only the hit area grows (`after:-inset-3`) | 44 px button shifting the icon and row | Fixed: 44 px button with a −12 px margin |
| 19 | Card actions `h-11 min-w-0 flex-1` | content width buttons | Fixed for unmatched and root cards (stale and matcher cards already stretched) |
| 20 | Scan queue status dot sits in a 10 px box; trigger text 14 px; group cancel icon 10 px with `gap-1` | dot width decided the column; 13 px; 14 px icon | Fixed |
| 21 | `formatDateTime` follows the user's date and time format settings | Page-local formatter ignoring them | Fixed: the page uses `DateTimeDisplay` (the shared preference formatter) |
| 22 | Scan warning, empty-root and partial-walk rows use `whitespace-nowrap` cells, so the long message widens the table and scrolls | Same (clipped at the table edge until scrolled) | Already matched; left as is |
| 23 | `Switch` is 32 × 18.4 px | WinUI `ToggleSwitch` (40 × 20) | Not possible with the stock control without restyling a shared template; left |
| 24 | `shadow-lg`/`shadow-md` on the dialog and popovers, focus `ring-[3px]` | Platform dialog shadow and system focus visuals | Not possible to match exactly in WinUI; left |
| 25 | HTML auto table layout (columns share extra width by content) | `Auto` columns with a star Paths column | Left: same order, widths differ within a few pixels |
| 26 | Entrance fades (`animate-in fade-in`, scan-row fade), `animate-ping` | none, except the scan spinner | Deliberate (owner preference: no entrance or slide animation) |

Function check (state, request, string parity) found no difference: the page's official strings, all request and
response shapes and every state were re-read against e130f262d. The one function-level defect found was
the date formatter (row 21). Test-side defect found: the native fixture's fake channel never delivered the
scans snapshot that the controller now waits for (it seeds only from the authority-bound activity store and
the subscription's own snapshot), so scan rows and the queue never appeared; the fixture now answers a
`scans` subscription with the snapshot and waits for the rows.

### Shared-control differences noted, not changed (another worker owns the shared chrome)

- `DestructiveButtonStyle` (`DarkTheme.xaml`) fills with `ErrorBrush` (#EF6B73) and its hover is that colour at 90%;
  the official destructive button is `destructive-fill` under white text. The Libraries page overrides the fill and text colour
  locally; hover still uses the shared brush. Every page that uses the style shows the lighter red.
- The shared `ContentDialog` buttons and presenter (radius, shadow) and the `GhostButtonStyle` hover colours were left as they are.
- `ErrorColor` (#EF6B73) differs from the official `--destructive` (#ef4444) in the theme file.
- The WinUI `ToggleSwitch`, `ComboBox` and `TextBox` templates keep their stock sizes and focus visuals.

### Evidence added

`AdminLibrariesVisualMatrixNativeFixture` captures the page at 1440 × 900, 1024 × 768, 800 × 600 and 560 × 800
device-independent pixels in each main state: populated list with scans running (table at 1440 and 1024, cards at 800 and 560),
the diagnostics sections, the scan queue and card menu (popup content cannot be rasterized by this host: the presenter capture
renders no pixels and the content-only capture lacks the popup's background, so those are recorded as notes, not claims),
the editor's General, Folders, Metadata and Advanced sections, a destructive confirmation, the empty list and a failed list read
with a working Retry. It also asserts geometry the captures are read against (badge radius, header actions filling the line,
Add library on its own 44 px row, start-aligned Actions header). Unit source guards
(`AdminLibrariesParityRefreshTests`) keep the named radii, the preference date formatter and the destructive fill from regressing.
Run labels and counts are in `docs/current-development.md`.

## Contracts

- `GET /api/v2/libraries`: one unpaged response, ≤ 1,000 rows; decimal, distinct ids; `has_more`
  means truncated (shown, reorder off). Only a complete read yields the `AdminCompleteSnapshot`
  that `POST /libraries/reorder` requires; the order must be an exact permutation of it, checked
  again under the action lease. One reorder at a time; a list read in flight also blocks moves.
- `POST /libraries` sends only `LibraryCreate` members (no `enabled`/`auto_translate_metadata`);
  `PATCH /libraries/{id}` sends the full update; `trickplay_enabled` and the box-set members only when
  they change (WebUI rolling-upgrade rule). Optional members are absent, never JSON null.
- Every write is sent once without token refresh/replay (`retryAuthentication: false`); 204 is the
  only success for reorder/override/ignore/providers/poster removal. A lost or uninterpretable answer
  is reported as unknown and the page reloads; the editor then disables Save until reopened.
- 202 receipts (scan, refresh, deletion) are never completion: a deleted library stays listed until
  the server removes it; `canceling` jobs read “Cancellation requested”.
- Scans come only from the shared `scans` channel (snapshot + events, accepted/running kept; at most
  500); completed/failed/cancelled events reload the list (1 s coalescing) and the matcher summary.
  `jobs` events for `library_refresh` re-read the first jobs page; `delete_library` reloads the list.
- Diagnostics: unmatched 10-row pages (q trimmed, `ignored=true` view), cursors only retained (≤ 1,000),
  “Last” walks ≤ 25 requests; roots/skipped/stale 50-row pages appended on Load more, ≤ 10 pages kept,
  10-row local pages; matcher detail 10 per list. Repeated/missing cursors, oversized pages and
  duplicate identities fail the read.
- Polls only while the page owns its authority: monitoring 30 s, seek previews 60 s, matcher summary
  10 s, matcher detail 10 s only while open and expanded, translation jobs 2 s only while one is active
  and the editor is open. Retirement cancels every read, timer, channel lease, dialog and editor.
- Folder browsing is read-only `GET /admin/filesystem/browse` (50 per page, ≤ 10 pages, the path probe
  asks for one entry). Nothing reads or changes local Windows media; the poster picker reads one
  user-chosen JPEG/PNG/WebP ≤ 10 MiB.

## Native differences (deliberate)

- The editor, folder browser and dialogs appear immediately (no slide/fade); the editor is a modal
  panel over the shell, 768 × 640 at most with the 12 px dialog radius, full-screen below 640 px;
  confirmation and mode dialogs are ContentDialogs.
- Add keeps Enabled and Auto-translate choices: after POST, a PATCH applies either value when it
  differs from the created library (POST cannot accept these fields). This deliberately avoids
  silently losing choices offered by the native editor. The created id is adopted before PATCH or provider-chain
  writes, so a known failure retries as an update instead of creating a second library; a lost answer
  disables Save with the existing unknown-outcome handling (the WebUI would POST again).
- The mount check shows one toast with the formatted result (the WebUI shows that plus a generic one).
- The Ignore/Restore toast's description and Undo stay inline under the unmatched list until dismissed.
- Unmatched “Last” stops after 25 requests and says so; append lists stop at 10 pages and say so;
  the WebUI keeps walking/appending without a cap.
- Failed diagnostics reads show an error with Retry (the WebUI shows empty lists for most sections).
- Catalog maintenance and the Library settings link open the native Configure pages when they are registered
  (`AdminShellPage.CanOpen`; Maintenance and Library settings are on Beta); otherwise they are disabled with an
  explanation. `?tab=autoscan` links open the native Autoscan page when it is registered and otherwise show a notice.
- An open card menu keeps the cards unchanged underneath until it closes (so it stays open after a move).
- Pointer reorder defers status-only table rebuilds until release/cancellation, retaining the capturing
  grip and row geometry; replacing the owner/library snapshot or leaving the wide layout still cancels.
- Poster previews accept only HTTP(S) on the configured server's scheme and authority, and read at
  most 10 MiB through the authenticated admin lease before decoding locally (no BitmapImage URL fetch).

## Integration (lead-owned shared files, additive)

- `App.xaml.cs`: `services.AddAdminLibraries();`
- `installer/build.ps1` approved resources: `Views/Admin/AdminLibrariesPage.xbf`.
- `docs/parity/admin-monitor-surfaces.json`: the 21 Libraries source files.
- Native dispatch: `SILO_NATIVE_TEST_ONLY=admin-libraries` → `AdminLibrariesNativeFixture.RunAsync`.

Text: `AdminLibrariesText` holds 402 official `web.admin.*` English patterns printed from the
compiled ICU catalog, five `web.common.sortable.*` announcements the generated catalog lacks, and
14 English-only `admin.native.libraries.*` state messages.

## Verification and limits

The evidence below applies only to the earlier reconciled candidate on Beta base 173c3797,
using a synthetic server, current DPI and dark theme; it does not verify head afef78f98544.
Later fixes c4192cde, 53fa9ca7 and afef78f9 changed production code for matcher summary polling,
last-page ignore/restore, scan hydration on open and controller channel locking, and added
regression tests including concurrency coverage. These fixes are source-reviewed only: no builds
or tests were run for them. The 7,576-unit count excludes the later regression tests. Updated
source awaits pipeline verification; the historical counts, receipts and hashes below are not
current-head evidence.

Historical verification:

- `beta-173c-units-r1`: 7,576 passed, zero failed/skipped, including 108 Libraries/folder-browser
  cases and `TaskSourceResolutionProbe.SharedOutputResolvesTheExpectedTaskSource`. Unit PDB
  binding checked 523 test, 486 Core and 15 Player documents. Receipt SHA256:
  `4B3A752BB3D704496033929A06F3D73B2B462D1A2A3DE58BFEE3C33E7145C497`.
- `beta-173c-native-r1`: self-contained app build, in-place publish and native host build all
  exited 0. Its subsequent binding step failed because the helper incorrectly expected separate
  Core/Player PDBs; the application links those sources into `SiloPlayer.dll`. No native test ran
  in that attempt. These qualified app build/publish captures were reused, not repeated.
- `beta-173c-native-r2-binding-correction`: corrected app/host binding passed, then the new
  snapshot-replacement keyboard regression timed out. The fixture had accepted the still-loaded
  old grip before the queued render replaced it, then tried to focus that detached control.
  The fixture correction for that historical continuation waited for the actual new rendered
  snapshot, loaded/enabled replacement grip and real keyboard focus; every existing behavioral
  assertion remained.
- `beta-173c-native-r3-current-render`: source-delta admission stopped before any process because
  Windows PowerShell 5 wrapped the decoded JSON array. Correcting that helper-only defect passed
  a positive one-fixture delta control and rejected absent/extra product changes before relaunch.
- `beta-173c-native-r4-current-render`: host-only rebuild and app/host PDB binding exit 0; focused
  remediation 211, full `admin-libraries` 378, `admin-manage-foundation` 36,
  `admin-configure-general` 188 and `admin-monitor` 113 PASS lines. Each native stage has exactly
  one suite-completion marker, no FAIL lines, empty stderr and numeric exit 0. All seven capture
  receipts match inline/disk and verify empty jobs and retained-member termination signals.

That historical continuation reused the 7,576-unit result after checking its fixture-only source
delta. App PDB binding checked 1,007 source documents including linked Core/Player; host PDB
binding checked 240. Released readback rehashed 2,233 source entries and 666 published app files
for that earlier candidate, not the later production and test changes. Its source review reported
no blocking P0/P1/P2 issues at that time; this is not a review result for the current head.

Historical final lease: `2026-11-16T16:37:47.9426263Z` to `2026-11-16T16:39:59.8164148Z`.
Historical final receipt SHA256: `DB4A061FE7043E50AF112175FD11FD7D01BD5FDEC556C8523434DB230F8A6216`.
Released audit SHA256: `FC6439AB785B9738A169FB7BD918B96489DC8B8D7028E2517668EADF9B21D376`.
Evidence root: `.codex-tmp/admin-libraries-reliability-20261116T153001Z/`.
The older 7,245-unit/168-native evidence also remains historical, not a current-head result.
Red evidence includes the reliability regressions failing against the prior transport/state
implementation, plus the earlier reorder-serialization and create-to-update negative controls.

Native checks drive production registration, real keyboard messages, bounded server-folder
browsing and opening the existing item match tool. Diagnostics refresh uses fixture-triggered
match completion; actual matching-tool selection/apply remains unverified. Pointer release/cancellation/capture loss and
same-owner snapshot retirement exercise the reachable production handlers; this does not prove
a real pointer-device drag. Captures include 1440×900, 1440×600, 1920×600, 800×600 and dialogs;
wide-table, Metadata-editor and compact-card captures were visually inspected.

Not established: live-server interoperability, real pointer-device drag, poster file picker and
growth during picker reads, actual matching-tool selection/apply,
IME, Narrator, RTL, all-DPI, localization beyond English or complete visual parity.
The installed player/settings were untouched. Global generated output was subsequently measured
at 10.191 GiB including hidden files, above the owner's 10 GiB bound; the helper's non-Force scan
had reported 9.905 GiB. Further local builds/native work are held. A native PowerShell attempt
to reclaim only disposable historical Admin D1 restore caches was policy-denied before execution;
zero bytes were removed, with no retry or bypass. This storage limit is separate from the passing
functional verification and is explicitly recorded in the released audit.
