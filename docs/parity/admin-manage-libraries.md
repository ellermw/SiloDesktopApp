# Admin Manage › Libraries › Libraries

Official reference: Silo-Server/silo-server abd85d0cadac28f4ab7db6821dad9634fc114790 (fetched
November 15, 2026). The scope audit's pin 7115ee7028c7c9bde9e201fd5f54e87ea70de3a7 is historical:
between the two, every Libraries operation and schema in `contracts/api/v2/openapi.json` is
unchanged; the WebUI changed only accessibility and motion details (`AdminLibraries.tsx` name ids
and `aria-describedby` for cleanup, reduced-motion sortable hooks; `LibraryFormSections.tsx`
path-input ids, remove-path `aria-label`, `aria-expanded` on provider levels, move up/down names;
poster remove `aria-label`; translate select border token). The native page carries those names.
Desktop base: Beta 2c33599f751e1bb7e18a56ee378ffe918710d74d (1.2.1145-beta.1, version unchanged).

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
  panel over the shell, full-screen below 640 px; confirmation and mode dialogs are ContentDialogs.
- A library created whose provider-chain write then fails turns the session into an edit of the
  created library, so retrying sends PATCH + chain instead of creating a second library (the WebUI
  would POST again).
- The mount check shows one toast with the formatted result (the WebUI shows that plus a generic one).
- The Ignore/Restore toast's description and Undo stay inline under the unmatched list until dismissed.
- Unmatched “Last” stops after 25 requests and says so; append lists stop at 10 pages and say so;
  the WebUI keeps walking/appending without a cap.
- Failed diagnostics reads show an error with Retry (the WebUI shows empty lists for most sections).
- Catalog maintenance and the Library settings link are disabled with an explanation while those
  Configure pages have no native implementation; `?tab=autoscan` links show a notice instead of
  opening an Autoscan page that does not exist natively yet.
- An open card menu keeps the cards unchanged underneath until it closes (so it stays open after a move).

## Integration (lead-owned shared files, additive)

- `App.xaml.cs`: `services.AddAdminLibraries();`
- `installer/build.ps1` approved resources: `Views/Admin/AdminLibrariesPage.xbf`.
- `docs/parity/admin-monitor-surfaces.json`: the 21 Libraries source files.
- Native dispatch: `SILO_NATIVE_TEST_ONLY=admin-libraries` → `AdminLibrariesNativeFixture.RunAsync`.

Text: `AdminLibrariesText` holds 402 official `web.admin.*` English patterns printed from the
compiled ICU catalog, five `web.common.sortable.*` announcements the generated catalog lacks, and
14 English-only `admin.native.libraries.*` state messages.

## Verification and limits

Synthetic server, current DPI, dark theme. 68 focused unit cases (wire bodies/paths, validation,
whole-order preconditions, no-replay writes, presentation, catalog coverage, controller ownership,
editor transactions, diagnostics bounds, folder browser) pass, and so does the full main suite
(7,245). Red evidence: removing reorder serialization or the create→update switch makes its test
fail. Native group `admin-libraries` (168 checks) exercises every workflow above through the
production registration hook. It drives keyboard reorder with real key messages and captures
1440×900, 1440×600, 1920×600 and 800×600, plus dialog captures. The foundation, Configure General,
Subtitle Files and Monitor groups pass on the same bytes. That native run predates the removal of
four unused English-only text keys, so a final native window on the committed bytes is still owed.
Not established: live-server interoperability, pointer-drag reorder (implemented, not
natively driven), poster file picker, applying a match inside the match tool, flyout captures (the
popup layer renders no pixels in the host; their content is asserted through automation), IME,
Narrator, RTL, all-DPI, localization beyond English and complete visual parity.
