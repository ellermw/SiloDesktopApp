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

## Integration

`App.xaml.cs` (`AddAdminPreparedFiles`), `installer/build.ps1` (`Views/Admin/AdminPreparedFilesPage.xbf`),
`docs/parity/admin-monitor-surfaces.json` (all family sources), native selector `admin-prepared-files`.
