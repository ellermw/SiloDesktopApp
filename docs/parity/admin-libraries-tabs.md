# Admin Manage › Libraries — Libraries, Scan Activity and Catalog Health tabs

Official reference: Silo-Server/silo-server **93794454b84aa818979bee19b8e192944395635a**
(`feat(web): reorganize admin Libraries page into Libraries, Scan Activity, and Catalog Health tabs`, #5087),
read from the lead's pinned object store (`D:/SiloPlayer-work/server-upstream-nov14`, never fetched) with
`git show 93794454b:<path>` and `git diff 93794454b~1 93794454b -- <path>`. It also carries the one earlier official
change to this page that was not yet native: **5e26ca784** (#4994, ambiguous root types and confidence in the interface
language). The page was last refreshed against `e130f262d` (#229, see `admin-manage-libraries.md`); this note is the
delta from there to 93794454b. Route `/admin/libraries` (`AdminRoute.Libraries`), unchanged.

Official files read (all under `web/src/`): `pages/AdminLibraries.tsx`, `pages/AdminLibraries.test.tsx`,
`components/admin/libraries/{LibraryActivityPanel,LibraryCatalogHealthPanel,LibraryCardList,LibraryRowParts,
AmbiguousRootsSection,SkippedRootsSection,StaleIDsSection,UnmatchedItemsSection,MetadataMatcherQueuesSection,
LibraryEditorDialog}.tsx`, `libraryActivityHelpers.ts`, `useLibraryRows.ts`, `useCollapsedScans.ts` (deleted), `locales/en.json`.
There was no running official server and there are no official screenshots: everything below is a source comparison.

## What the official page does

### The page and its tabs (`pages/AdminLibraries.tsx`)

| Topic | Official | Line |
|---|---|---|
| Tab ids and parsing | `libraries` \| `activity` \| `health`; any other value is `libraries` | 90–95 |
| Selected tab | the `tab` query value (`activity`, `health`; **deleted** for Libraries); `replace: true`, other query values kept | 108–109, 156–169 |
| Default tab | Libraries | 94 |
| Legacy link | `?tab=autoscan` still redirects to the Autoscan topic before the page renders | 97–103 |
| Page header | title, subtitle (new text), **Scan All Libraries** and **Add Library**; the scan queue popover and the **Catalog Maintenance** link are gone | 333–402 |
| Page loading | `web.admin.libraries.page.loading` until the list read answers | 275 |
| Tab strip | `TabsList variant="line"`, bordered, scrolls horizontally, triggers `max-lg:h-11` | 412–454 |
| Libraries tab count | number of libraries (secondary pill, always) | 416–421 |
| Scan Activity tab count | `tabs.activeCount` (“N active”) = scans + metadata refreshes, only when > 0; green pill with a dot while anything **runs**, otherwise a secondary pill | 288–292, 423–444 |
| Catalog Health tab count | unmatched total + matcher total, amber pill, only when > 0; unknown until either arrives | 122–134, 446–453 |
| “View queue” | opens Scan Activity with the filter reset to all, and moves focus to the Activity trigger | 171–176, 457–461 |
| Row scan / refresh pill | opens Scan Activity filtered to that library | 474, 482 |
| Tab change by the admin | resets the Scan Activity library filter (not when reached through a pill) | 406–410 |
| Editor | `initialSection`: a row's folders link opens the editor on **Folders**; every other entry opens General | 148, 178–182, 360–370; `LibraryEditorDialog.tsx` 31–33, 74–77, 100–109 |

What moved from the old single page:

| Old (e130f262d) | Now |
|---|---|
| header Scan queue popover (`ScanQueuePopover`, `ScanQueueGroup`, `useCollapsedScans`) | **Scan Activity** tab (`LibraryActivityPanel`); `useCollapsedScans` deleted |
| header Catalog Maintenance link | removed |
| per-row “active work” block (`LibraryActiveWork`) | a pill in the row's Status cell that opens Scan Activity |
| Unmatched Items, Metadata Matcher, Ambiguous Roots, Troubleshooting (Skipped Roots), Stale External IDs sections, stacked under the table | **Catalog Health** tab (`LibraryCatalogHealthPanel`), five tiles |
| `JobPageControls` (Load older refresh jobs) under the whole page | Scan Activity footer |
| table: name, paths, type, status, last scanned, five icon buttons | table: grip, **Library** (type tile, name, “type · folder”), **Status**, **Last Scanned**, **Actions** (Scan/Stop, Edit, “…” menu) |

### Libraries tab

`LiveActivitySummaryStrip` (`LibraryActivityPanel.tsx` 44–183), then the list (`AdminLibraries.tsx` 527–785, `LibraryCardList.tsx`,
`LibraryRowParts.tsx`). Order: strip, then table (from `lg`) or cards.

* **Summary strip** — nothing while no scan runs/queues and no refresh is active. Headline “N running · N queued” (or “Refreshing metadata” /
  “Refresh queued” when only refreshes), detail “Library (target, target) + N other libraries” (or the refresh library), a success dot while
  running, border `success/30` while running, and **View queue →** (44 px under `lg`).
* **Empty list** — “No libraries configured yet.” (`list.empty`) in a panel, for table and cards.
* **Table** — columns Library, Status, Last Scanned, Actions; Library cell `max-w-[26rem]`; a 2 px success progress line along the row's bottom edge
  for a row with running scans that report progress (mean of their percents, `libraryActivityHelpers.ts` 26–41); the library's warning row sits
  **directly beneath its own row**; the grip is quiet (`opacity-45`) until hover or focus.
* **Status cell** (`LibraryStatusBadges`, `LibraryRowParts.tsx` 45–179) — no “Enabled” pill; Disabled; one scan pill “N running · N queued”
  (accessible name continues “View active scans for {name}”) that opens Scan Activity; one refresh pill (“Refreshing metadata” or “Refresh queued”);
  scan-warning badges; monitoring and seek-preview badges; a quiet “Idle” when none of these show.
* **Last Scanned** (`LibraryRowParts.tsx` 182–230) — relative time (floor, absolute after 30 days) that opens a popover with the exact timestamp when the
  two differ; “Never scanned”; the warning time under it.
* **Actions** — outline **Scan** (labeled “Scan”, named “Scan Library”) or destructive-tinted **Stop** (named “Stop Library Scans”), ghost **Edit**
  (named “Edit {name}”), and the labeled “More actions for {name}” menu (`LibraryActionMenu`, 237–372): Refresh metadata (with its description) or Stop
  refresh, Verify mounts (description or the result; stays open), divider, Confirm cleanup (only for empty/dead roots), Delete. Cards add Edit and Move
  up / Move down (stay open) to the menu and a full-width Scan/Stop button.
* **Cards** (`LibraryCardList.tsx` 27–197) — type tile, name (2 lines), “type · N folders” (opens Folders), status badges (+ a “0 folders” badge),
  paths (one inline, several behind Show/Hide paths), Last Scanned, the scan warning, Scan/Stop.

### Scan Activity tab (`LibraryActivityPanel.tsx` 186–601)

States, in the official order (255–280): **error** (refresh jobs read failed) → only the footer; **loading** (nothing active and the first refresh
jobs page not here) → “Loading”; **empty** → icon tile, “No active scans or refreshes”, “Scans and metadata refreshes appear here while they run.”,
then the footer; otherwise **populated**.

Populated, top to bottom: library filter chips when more than one library has work (`All (N)` then one chip per library with its count; groups first,
then refresh-only libraries; a selection that no longer has work shows all); the **Metadata Refreshes** section (library tile, name, “Metadata” badge, message,
progress, **Stop** named “Cancel refresh”); one card per library (type tile, name, “N running · N queued”, **Stop** named “Cancel scans”; each running scan
in a bordered box with a success dot, mode, target (path left to right, `title` the full path), trigger badge, progress text or “Started …”, and a
`role=progressbar` labeled by the mode and target when a percent is known; a dashed queued box “N queued — a, b + N more” with **Show all (N)** /
**Hide queue**, expanding to every queued scan with “Waiting for capacity”). The footer (`JobPageControls`): “Jobs could not load. Restart jobs” while
a read failed, then **Load older refresh jobs** — kept in the empty and error states too (the #5087 fix “preserve refresh-job pagination in Scan Activity footer”).

### Catalog Health tab (`LibraryCatalogHealthPanel.tsx`)

Five tiles in a tab list (`aria-label` “Catalog Health”): 2 columns, 3 from `sm` (640 px), 5 from `lg` (1024 px). Each tile: count (`xl` bold; muted when 0),
**!** with the “Error loading count” text on a failed count, or “Inspect” while unknown; an icon tile; the section name. Default tile Unmatched Items.
Counts: Unmatched = unfiltered awaiting total; Metadata Matcher = sum of `total_count`; **Ambiguous Roots = a count only with exactly one library** (the list
reads one library at a time: with several the tile stays on “Inspect”); Skipped and Stale = their first page's total. A search never replaces a tile's count.
Only the selected tile's section is mounted (so the three heavy reads wait for their tile and leave with it). The sections are `embedded`: a card
(`surface-panel rounded-2xl p-4 sm:p-5`) with the heading and description, no collapsible header; Unmatched and Matcher show an alert with **Retry** when
their read failed with no data, “Loading”, and for Matcher/Skipped/Stale a message when empty (`health.matcherEmpty`, `skippedEmpty`, `staleEmpty`);
Ambiguous Roots with no libraries shows `list.empty`, and shows each root's type (`itemTypeLabel`) and confidence (Low/Medium/High) in the interface language (#4994).

## Strings (official `en.json` at 93794454b and 5e26ca784)

New keys now in `AdminLibrariesText.English`: `activity.{emptyDescription,emptyTitle,filterAll,hideQueue,moreLibraries,refreshSectionTitle,
refreshingMetadata,viewQueue}`, `health.{inspect,matcherEmpty,skippedEmpty,staleEmpty}`, `list.{editAction,empty,idle,refreshMetadataDescription,scanAction,
stopAction,verifyMountsDescription,viewScansFor}`, `tabs.{activeCount,activity,health,libraries}`, `ambiguousRoots.confidence.{low,medium,high}`,
`unmatched.status.{ambiguous,pending,unmatched}` (added with the catalog; no official component reads them yet), and the changed `page.subtitle`.
`web.search.type*` (item type labels) and `common.loading`/`common.retry` come from the localized `AppStrings` catalog. `list.empty` was missing from the table.

## What changed natively

Source map: `Views/Admin/AdminLibrariesPage.Tabs.cs` (new: tab strip, summary strip, Scan Activity, Catalog Health tiles), `AdminLibrariesPage.List.cs`
(table, status badges, row actions, menu, cards), `AdminLibrariesPage.cs` (layout, header, lifecycle, render), `AdminLibrariesPage.Diagnostics.cs` / `.Roots.cs`
(sections embedded in the tiles), `AdminLibrariesUi.cs` (`DiagnosticsSection` embedded mode), `AdminLibrariesPage.Editor.cs` / `AdminLibraryEditorView.cs`
(open on a section), `Core/Services/Admin/AdminLibraryTabs.cs` (new: the rules), `AdminLibrariesController.cs`, `AdminLibraryDiagnostics.cs`,
`AdminLibrariesText.cs`. `AdminLibrariesPage.ScanQueue.cs` (the popover) is deleted.

* **Tab strip** — Buttons in the `GhostButtonStyle` line-variant pattern the Prepared Files page and the library editor already use (selected → bottom indicator
  and `ItemStatus selected`, position in set, 44 px high below 1024 px, strip scrolls instead of clipping), plus what those two lacked: Left/Right (mirrored
  in a right-to-left page), Home and End move focus **and** select, looping at the ends (`AdminLibraryTabs.Navigate`). Tab changes swap panels in place:
  `Visibility` only, no transition, slide or opacity animation. The same helper (`OnTabKey`) drives the Catalog Health tiles.
* **Route** — the selected tab is the route's `tab` value (`activity`, `health`; removed for Libraries) and replaces the history entry through
  `AdminShellPage.TryReplace`, keeping every other query value; opening `?tab=activity|health` lands on that tab; an unknown value opens Libraries;
  `?tab=autoscan` still redirects to Autoscan first.
* **Libraries tab** — the summary strip; the table (grip quiet until hover/focus, type tile, “type · folder” button that opens the editor on Folders, Status
  with scan/refresh pills, no Enabled pill, Idle, Last Scanned with the exact-time popover, Scan/Stop + Edit + “More actions” menu, a 2 px progress line, the
  warning row under its own row); cards with the same pieces; the empty list message.
* **Scan Activity tab** — loading / error-footer / empty / populated exactly in the official order (`AdminLibraryTabs.PanelKind`, driven by the new
  `RefreshJobsLoaded` snapshot flag so “nothing is running” is never shown before the refresh jobs arrived); filter chips (`FilterEntries`, `EffectiveFilter`);
  refresh rows with Stop; per-library cards with running scans (labeled `ProgressBar`, `AutomationProperties.LabeledBy` the mode and target) and the
  dashed queued box (preview of two, Show all / Hide queue, “Waiting for capacity”); the footer with Restart jobs and Load older refresh jobs (and the
  native page cap note) in the empty and error states too.
* **Catalog Health tab** — five tiles (2/3/5 columns at 560/800/1024 px and up) with count, `!` or Inspect; embedded sections (`DiagnosticsSection.SetEmbedded`);
  only the selected tile's section is open: choosing a tile opens it (the roots, skipped and stale reads start then), leaving closes it (the matcher's expanded
  library and detail polling reset, as a remounted section does). The page still reads the unmatched totals and the matcher summary on open so the tab badge and
  tiles are right from the first moment. Matcher: failed first read → alert + Retry; “Loading”; empty message; table/cards. Skipped/Stale: “Loading”, empty message.
  Ambiguous: no libraries → `list.empty`; type and confidence in the interface language (`AdminRootLabels`). Tile counts use `AdminLibraryTabs` (Ambiguous only for
  one library; a search never replaces a count; `MatcherState.HasData` tells a failed first read from an empty queue).
* **Paths** — shown through `AdminPathText.Isolate` (official `PathText`: each folder in a first-strong isolate, left to right, 32-separator limit) on the new
  surfaces: the row path, the cards' paths, the summary strip and the Scan Activity targets. Library names, titles and messages are plain `TextBlock.Text`
  (never parsed as markup).
* **Strings** — every label is an official key in `AdminLibrariesText` (list above); the subtitle changed; `list.empty` was added (it was missing).

## Requests

**No request is new or changed** (path, query, headers and bodies are as before; the fixture's request log is the evidence):

| Read | When | Now |
|---|---|---|
| `GET /api/v2/libraries`, capabilities, realtime-monitoring, trickplay, `admin/jobs?kind=library_refresh&limit=50` | page open | unchanged |
| `GET /api/v2/libraries/unmatched-items?limit=10` and `…&ignored=true`, `GET /api/v2/libraries/metadata-match-queue` | page open (the tab badge needs them) | unchanged |
| `GET …/libraries/roots`, `…/skipped-roots`, `…/stale-ids` | when the section is opened | when **its tile** is selected (was: its header); none before |
| `GET admin/jobs?…&cursor=…` | Load older refresh jobs | unchanged; now also reachable from the empty and error states |
| every write (scan, stop, refresh, cancel job, mount check, cleanup, delete, reorder, ignore/undo, retry, override, editor saves) | | unchanged bodies; the menu items call the same controller methods the icon buttons did |

## Not implemented, and why

* **A refresh job's server message.** The official strip and refresh rows print `job.message`; the v2 job row the native page reads has no message, so they
  use the existing “Cancellation requested” / “Refresh queued” text (the strip shows the message only for a canceling job).
* **Pulsing dots** (`animate-ping`) on running scans: static dots (no entrance or looping animation, owner preference).
* **A Radix remount's reset of a section's search text**: the native views stay alive, so a search typed in Unmatched, Skipped, Stale or Ambiguous is still there
  when its tile is chosen again (the expanded matcher library, the opened state and the detail polling do reset).
* **`unmatched.status.*` keys** are in the table but no official component reads them at 93794454b, so nothing prints them.
* **Forced-colors outline/Highlight** on the progress bar and the line along a row (WinUI high-contrast resources drive them).

## Verified, and not

Verified by the checks named in `docs/current-development.md` (unit tests for the rules and controllers; the `admin-libraries` native selector for the existing
page flows on the new layout; the new tabs block of that selector (`AdminLibrariesTabsNativeFixture.cs`) for tabs, counts, route, keyboard, every state, in-flight and editor behaviour, widths
1280/1024/800/640/560). **Not verified**: pixel parity with the official page (no official server or screenshots); right-to-left layout and screen-reader speech
(names and `ItemStatus` are set and read back; nothing was listened to); touch input; forced-colors/high contrast; displays scaled to 125%/150% (the layout rules
are followed, no scaled capture was taken); an official server (all reads came from the synthetic server).
