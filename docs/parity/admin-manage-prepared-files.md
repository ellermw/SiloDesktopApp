# Admin Manage > Downloads > Prepared Files

Native page for `/admin/downloads` (`AdminRoute.PreparedFiles`), registered only through `AddAdminPreparedFiles()`.
The official reference is Silo Server/WebUI commit `66b836f82a32de2fb3e5dfae6c285c19eea1096d`, read from the pinned
objects in `D:/SiloPlayer-work/server-upstream-nov14`. The page was first written against `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`;
the comparison below is against the newer commit. Viewer Downloads and Manage > Downloads > Settings are separate surfaces.

## What changed upstream between the two commits (for this page)

- `PreparationTab.tsx`: a row's title now underlines on hover when it links to its item, and the first requester link underlines on hover.
  Layout, columns, strings and actions are unchanged.
- `App.tsx` / `RealtimeEventsProvider.tsx`: every admin page now subscribes the `downloads` channel in addition to `download_preparations`,
  and a `downloads` snapshot or event re-reads the storage views (overview, files, devices, entries, history). The background
  preparation refresh is wrapped in an error boundary (a browser concern with no native counterpart).
- Server: download rows and entries gained `created_client_ip` and `last_client_ip`; the storage capabilities gained `client_ip` and
  `change_events`; node rows gained `env_managed` and a node update that changes a field the node declares from its environment is refused
  with 422 `managed_by_environment`; remote preparation reports node-at-capacity and node-storage-full refusals. The official Downloads
  page displays none of these, so the native page decodes the new entry and node fields and shows nothing new (a refused node save shows
  the server's own detail).
- `web/src/locales/en.json`: every `web.admin.downloads.*` string is taken from this commit
  (`AdminPreparedFilesTextCatalog`, 482 keys with the four node-write keys). The shared translation catalog excludes `web.admin.*`, so
  this text is English, like the other Admin pages.

## Tabs

- **Storage** (`StorageTab`): five totals tiles, official-priority warnings with their actions (edit location, review untracked, review
  devices, view files), one card per location (status, directory, scratch / own disk / temporary badges, pending directory, segmented disk
  meter with ceiling and scratch-admission markers, legend, prepared-versus-budget line, untracked / drift / full notes, counts, browse,
  edit, clean-up) and the meter key. Clean-up has no official confirmation and sends once; confirmed untracked deletion names the location.
  The overview refreshes every 60 s and on events.
- **Preparation** (`PreparationTab`): state bar with per-state filter chips and counts, per-worker chips, live text filter, selection
  toolbar (pause, resume, cancel with the official prompt that names lost encoding and waiting downloads), the
  Title / Output / Worker / Progress / Requested by table with per-job pause, resume, cancel (remove for failed jobs), details panel
  (source, output, job, requesters) and the FFmpeg console (the 12 newest FFmpeg rows of the job, one read per open) with a link to the
  native Logs page. Rows patch from `download_preparation.progress` events; other events re-read the queue (2 s leading-plus-trailing
  coalescing). A 60 s maintenance read runs while jobs are in flight and a single read is scheduled when the oldest listed failure
  ages out of the server's 24 h list.
- **Prepared files** (`PreparedFilesTab`): title search (Enter or leaving the box), location / state / format / sort filters, selection
  with the official delete dialog (cached files by default, in-use files only by explicit opt-in, three titles then "N more"), table
  (title and subtitle, prepared-as, location, on disk, last used, used by with the stale-device note, state with expiry), bounded Load more.
- **Device copies** (`DeviceCopiesTab`): person / device search, stale-only toggle, sort, expandable download lists with per-entry and
  selected revocation, whole-device revocation with the optional reason and the default-on monitor pause, stale and cache-expiry
  consequences, bounded Load more. The Storage "review devices" warning opens it pre-filtered to stale devices.
- **History** (`HistoryTab`): four policy tiles, reason / location (including Devices) / 1, 7, 30 days or all-kept filters, table of
  when, what happened, item, location, size and by, bounded Load more.
- **Edit location** (`EditLocationDialog`): servers show their directory, default budget, cache period and ceiling with a link to
  Download Settings; nodes edit a directory override and a budget (default, custom GB, none) with the official validation and
  directory-change note.

## Contracts and write rules

- Reads are bounded: cursor lists request 50 rows and keep at most 10 pages; repeated or missing cursors, oversized pages, duplicate or
  missing identities and malformed values fail closed. A response is applied only while it is the newest read of its data and the page,
  server and account are unchanged; a superseded read's late failure raises nothing.
- Every write is single-send: no authentication retry, no replay after a timeout. A 4xx answer (other than 408) is a refusal and shows
  the server's own detail; a timeout, transport failure or 5xx after sending is an unknown outcome that blocks destructive actions and
  node writes until the affected lists are read again (a Reload control stays reachable). Deletes, revocations and cancellations send exactly
  the rows their dialog named.
- Node writes send only the changed override fields under the strong `config_etag` the node was read with (an explicit null restores the
  inherited value); an untouched editor sends nothing.
- No local Windows path is read or deleted; all removal is server-side.

## Deliberate differences

- Narrow widths scroll tables horizontally instead of hiding columns per breakpoint, and there is no separate mobile card for
  preparation rows.
- Results are shown in a persistent inline notice with Dismiss instead of toasts, so an unknown outcome stays visible.
- The Prepared files select-all box sits above the table (WinUI tables have no header checkbox or indeterminate state here).
- Loading skeletons are simple placeholders.
- Interface text is English (see above); a language change does not re-render this page.
- Entry client-IP fields, the new storage capability flags and `env_managed` are decoded but not displayed.

## Independent review of November 20 (ten findings) and their fixes

An independent read-only review at commit `c9bc4d83c` found no path that deletes files the administrator did not confirm, and ten
defects (two P1, eight P2). Each was confirmed in the code and against official `66b836f82`, then fixed with a test:

1. **Switching tabs waited on a reload of the tab being left** (P1). `ActivateTabAsync` sets the next tab and every tab's starting query in
   one step and reads only the tab that opens; the tabs being left reset without any read. Unit: `LeavingAFilteredTabResetsItsQueryWithoutWaitingOnOrStartingAReadOfIt`,
   `ActivatingATabWithAQueryReadsOnlyThatTabOnce`; native: leaving a filtered Files tab with the file read stalled.
2. **Every controller change rebuilt the whole active tab** (P1). Progress readings, selection toggles, polls and action flags rebuilt up to
   about 500 rows, replacing the control under the pointer (a lost click), dropping keyboard focus, and building the user-registry
   lookup once per row. Now every list keeps one stable element per row identity (`AdminPreparedFilesPage.Stable.cs`): check boxes,
   action buttons, details buttons, the selection strip, the state and worker chips, expand and revoke controls are built once and updated
   in place; each cell is rebuilt only when the values it displays change; tabs draw only when what they show changed and only while
   shown; the registry is read once per view; Storage redraws only when the overview (or the minute its relative times are read in)
   changed, and a state change only re-gates its buttons; a rebuilt control is found again by automation id and given focus back.
   A progress reading raises `Changed` only while the Preparation tab is shown. Unit: `AProgressReadingRaisesChangedOnlyWhileThePreparationTabIsShown`;
   native: 24 progress readings replace no control or row, keyboard focus stays on Pause, and while readings arrive every 20 ms a check box
   toggle, "Cancel selected" and a row's Cancel all land; a live refresh keeps the Files row, its check box, its selection and focus.
3. **Device entry reads shared one slot** (P2). One read slot per device; unit `EachDevicesDownloadsHaveTheirOwnReadSoOneDeviceNeverCancelsAnother`.
4. **A live refresh collapsed loaded pages to the first page and dropped selections** (P2). A re-read now walks as many pages as were loaded
   (bounded by `MaxRetainedPages`, each continuing from the cursor the previous answer gave), for files, devices, history and each
   device's entries, and publishes once; selection is by id, so only a row that is gone drops out. Unit:
   `ALiveRefreshReReadsEveryLoadedPageAndRowsThatAreGoneDropOut`, `ARereadOfADevicesDownloadsKeepsItsLoadedPages`.
5. **A partial failure looked like success** (P2). `AdminPreparedActionState.IsError` marks "deleted some, some failed" (files and untracked
   files) so it is drawn as an error, as the official `toast.error`; a part the server refused on purpose still is not.
   Unit `AFailureAmongDeletedRowsIsAnErrorNoticeWhileASkippedRowIsNot`; native: the notice icon uses the error brush.
6. **Values were parsed as markup** (P2). `Rich` hides markup characters in values while the pattern is formatted and restores them in the
   parsed runs, so a title, node name or directory is shown exactly. Unit `ValuesInRichPatternsAppearExactlyAsGivenAndNeverRestyleTheRest`,
   `NodeNamesAndDirectoriesInRichPatternsKeepTheirCharacters`; native: `Alien <Directors>` in the delete confirmation.
7. **A far-future failure time threw from the timer calculation** (P2). `NextFailureExpiry` skips a time within a day of the end of time,
   and `FailureReadDelay` maps every time the clock cannot place (past, more than two days away) to the one-minute cadence; the page has no
   other server-time-to-delay calculation (progress, retry, ago and expiry texts only subtract times). Unit
   `NoFailureTimestampCanBreakOpeningThePageOrItsTimers` (four timestamps), `NoFailureTimestampCanThrowFromTheTimerCalculationOrScheduleATightLoop`.
8. **Dismissing the node editor while saving left the page busy** (P2). Only a retired page authority drops a write's answer; a lease that
   ended on its own after the request was sent is an unknown outcome (writes blocked until the storage and node lists are read again,
   Reload reachable) or, if the answer arrives, the applied result. Unit `ASaveAbortedAfterItWasSentIsAnUnknownOutcomeThatBlocksWritesUntilTheNodesAreReadAgain`,
   `ASaveDismissedBeforeItsAnswerNeverLeavesThePageBusy`; native: the editor is hidden while its save is in flight.
9. **Confirmations were not bound to the authority they opened under** (P2). The delete, revoke, untracked, cancel (row and selection) confirmations
   capture an action lease when they open and pass it to the write; if it is no longer current when the administrator confirms nothing is
   sent. (The node editor already captured its lease at open; clean-up, pause and resume have no dialog.) Unit
   `EveryConfirmationSendsNothingUnderAnAuthorityThatIsNoLongerCurrent`. The narrow race itself (retirement callback ahead of the dialog continuation)
   is covered at the controller; it is not reproduced natively.
10. **Dropped requests were logged with exception text** (P2). `SiloApiClient` logs only `admin_downloads_nodes_transport_failed` for
    `/api/v2/admin/downloads/*` and `/api/v2/admin/nodes[/…]`. The nodes prefix is included because this page's node save carries a
    server directory in its body; Playback Nodes and the dashboard share that prefix and now also log only the category. Unit
    `TransportFailureAfterSendLogsOnlyAFixedCategoryAndNeverEchoesTitlesPathsOrNames`.

## Integration

`App.xaml.cs` (`AddAdminPreparedFiles`), `installer/build.ps1` (`Views/Admin/AdminPreparedFilesPage.xbf`),
`docs/parity/admin-monitor-surfaces.json` (all family sources), native selector `admin-prepared-files`.

## Verification (observed)

Current: final run `prepfiles2-final-r5` on source commit `82aca54fa3bd2fbb92b353a1a4f82bb77fefcb52` (lane `admin-completion-lane-b`, task
`admin-families-nov20`, one lease ACQ `2026-11-21T00:16:23Z` / REL `2026-11-21T00:22:27Z`): 83 focused unit cases (filter
`AdminPreparedFiles|AdminPreparation`) and the full unit suite of 10,207 cases with no failures, the unit/app/publish/host builds, payload and
source verification, and five native selectors, all passing: `admin-prepared-files`, `admin-playback-administration`, `admin-manage-foundation`,
`admin-configure-general`, `admin-monitor`. The fixture additionally covers: leaving a filtered Files tab with its read stalled; a live
refresh keeping the row element, check box, selection and keyboard focus; `Alien <Directors>` shown exactly in the delete confirmation; a
partly failed delete drawn with the error icon; 24 progress readings replacing no control or row with focus kept on Pause; and, while readings
arrive every 20 ms, a check box toggle, "Cancel selected" and a row's Cancel all landing; and a node save aborted by hiding the editor ending as an
unknown outcome that Reload lifts.

Not passing in this environment: `admin-signin-security` (check "typed group list is kept as typed (16 characters)") failed in full runs
`prepfiles2-final-r2` and `-r4` and in isolated runs `prepfiles2-signin-hn-r1` and `-r2`; this branch changes no Sign-in file (identical to Beta
`7b0b8012d`), so it is recorded, not fixed here. `admin-playback-administration` ("key rotation confirmation opened") failed in `-r1` and `-r3` and
passed in `-r2`, `-r4` and `-r5` without a source change. Earlier labels `prepfiles2-focus-r1` (two assertions of this change's own new tests, corrected before
any passing run), `prepfiles2-native-r1` (old fixture against the in-place page: a collapsed Preparation table kept detached rows in the
tree, fixed) and the host-only runs `prepfiles2-hn-r1` to `-r3` were development runs.

Previous baseline (kept as history, before the review fixes):


Final run `prepfiles-final-r4` on source commit `30376961398026f1ddc8ca64f0f9ee686d59442a` (lane `admin-completion-lane-a`, task
`admin-families-nov20`, one lease ACQ `2026-11-20T22:16:11Z` / REL `2026-11-20T22:22:05Z`): 150 focused unit cases and the full unit
suite of 9,899 cases with no failures, the unit/app/publish/host builds, and four native selectors, all passing:
`admin-prepared-files`, `admin-manage-foundation`, `admin-configure-general`, `admin-monitor`.

The `admin-prepared-files` fixture drives the registered route through the real Admin shell and Frame on a synthetic server:
every tab's populated state, failed reads with a reachable Reload (files, devices, history, storage, background queue refresh),
the delete dialog with a refused, an unknown-outcome and a successful write (exact ids, sent once, block lifted only by a
successful re-read), clean-up, the untracked dialog, the server and node location editors (untouched editor sends nothing, changed
field only, refusal keeps the draft, stale validator offers Reload, unknown outcome blocks replay), preparation filters with
keyboard focus kept while typing, live progress patching from a realtime event, details panel and FFmpeg console, pause and
cancel dialogs, device and entry revocation with an unknown outcome, the stale-device warning navigation, history defaults, and
authority loss clearing every row and releasing the realtime leases. Wide, compact, Storage and Preparation captures were
inspected (Storage and Preparation at the 1440 px width).

Not observed: a live server, real realtime delivery, light theme, every DPI and width, Narrator/IME, and pixel parity with the WebUI.
One earlier full-suite attempt (`prepfiles-final-r2`) failed two unrelated tests
(`AdminStorageSettingsControllerTests.DismissHidesOnlyTheCurrentFailedJob`, a known flake, and
`AdminLibrariesControllerTests.MatcherDetailReadRetriesWithoutRetryNowAndStopsOnCollapseOrRetirement`); the unchanged rerun passed
with no source change to either.
