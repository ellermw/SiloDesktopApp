# Detail metadata freshness and Refresh Metadata job follow (viewer audit 6c, 6d)

Date: 2026-11-16. Branch `t3/detail-metadata-refresh`, based on Beta
`bdd033c1c` (origin/beta). Spec: `D:/SiloPlayer-build/server-review-nov15/viewer-audit.md`,
section 6, items 6c and 6d.

## Upstream references

Official `Silo-Server/silo-server` main fetched directly at
`8f93594ce19723dde8a204be21ba59a24ef06e3b` (2026-11-16). All three references are
ancestors of it, and none of the files they touch changed after #4355.

| PR | Commit | What it changed upstream |
| --- | --- | --- |
| #4357 | `3117565e057405baa61d0f938e0bedce140f0e4d` | Another device's metadata save stays visible on an item page after a toggle patches the cached detail. |
| #4355 | `b83842fbe1190ac452cb9d10917fccc80fc8cd86` | A job wait survives transient read failures and socket rebinds, honors `Retry-After` (30 s cap on backoff only), and ends quietly on a session change. |
| #4187 | `f7c5f87406e91ce11a87cf9b76fe3d0e1479f6a1` | A refreshed item is followed to its new ID only from the page that started the refresh. |

Contract details used: `GET /api/v2/admin/jobs/{id}` (AdminTaskJob: `state`,
`terminal`, `failure`, `item_result`), `Retry-After: 5` on a running job read and
on the 202 refresh receipt (`internal/apiv2/admin_task_jobs.go`), and the
`catalog` channel's `catalog.item.changed` / `metadata.updated` /
`library.item_added` events naming `content_id` (`docs/realtime-api.md`,
"Catalog item changes").

## Behavior

### 6c: remote metadata changes reach the cache and an open detail page

- The shell's app-lifetime subscription now includes `catalog`
  (`notifications`, `user_settings`, `user_state`, `catalog`). Each event goes to
  the new `DetailMetadataChangeHub` from the socket thread. Only cheap work
  happens there: parse `content_id`, drop that item's cached detail, and signal
  matching open pages. No read starts on the socket thread.
- `ItemDetailPrefetchCache.InvalidateCached` drops every library scope of the
  named item. It records nothing for an uncached item, so an admin's scan
  stream (up to about 320 events per second) cannot grow the per-item marks.
  A consumer awaiting a dropped request still rereads, as with `Invalidate`.
- Each detail visit owns a `DetailMetadataWatch`, created before the first read
  and disposed in `OnNavigatedFrom`. Signals coalesce into one reload after a
  1 s window. At most one reload runs, with one trailing reload for signals
  that arrive during it.
- The reload patches the mounted item in place for every type
  (`ItemDetailViewModel.RefreshServerMetadataAsync`, which reuses the existing
  container metadata patch). Rows, companions, selections and viewer state stay
  mounted. The page restores the `ContentScroll` offset after layout and puts
  back focus only if the refresh dropped it. There is no navigation and no page
  animation.
- Holds: the reload waits while the first load is still painting, while any
  `ContentDialog` is open (Edit Metadata, Match, Refresh and others), and while
  the player is Expanded or Fullscreen. A held reload stays pending without
  polling. It resumes on first paint, on the dialog's `Closed`, when the player
  stops covering the page, or on the next change.
- An identity change (sign-out, account, profile or server switch) retires the
  watch. Pending work is dropped and later signals are ignored.

### 6d: Refresh Metadata follows its job

- The detail page captures the identity, posts the refresh, and shows the
  localized `web.item.toast.refreshRunningQuick` or `refreshRunningComplete`
  toast instead of the hard-coded English "...queued" text.
- `MaintenanceJobFollower` reads `GET /api/v2/admin/jobs/{id}` through an
  identity-bound request (`MediaMaintenanceApi.GetJobAsync`). A transport
  catch-up or socket rebind does not retire it.
  - A running job is reread after max(2 s, `Retry-After`). The current server
    sends 5 s.
  - A transient failure (network error, request retired while the identity
    holds, 0/408/429/5xx including a proxy's non-JSON page) backs off 2, 4, 8,
    16, 30 s. It never retries before `Retry-After`, and a `Retry-After` longer
    than the 30 s backoff cap is honored in full.
  - The whole wait is bounded at 10 minutes. A delay that would pass the
    deadline ends the wait as TimedOut without an early read.
  - The identity check runs before and after every read, so a sign-out
    mid-wait ends as Stale, even when the failing read lands after it.
- On completion:
  - Stale shows nothing and changes nothing.
  - Otherwise the cache drops the item, `refresh_content_id`,
    `detail_content_id` and, for a season or episode, the series.
  - Failed or Canceled shows `web.item.toast.refreshFailed` and logs the server
    detail.
  - TimedOut only logs.
  - Success shows `refreshArtworkWarning`, `completeRefreshFinished`,
    `refreshedNewFiles` (`count` and `new_files`) or `refreshed`, in upstream's
    order.
- Only the visit that started the refresh reloads (#4187), through that
  visit's watch. A visit disposed by navigation, including a later revisit of
  the same item, never reloads. A new `detail_content_id` replaces the starting
  visit with the new item through `NavigateReplacingCurrentEntry`. A complete
  refresh, or one with new files, is structural: for a leaf the page also
  rereads `/watch` versions.

## Files changed

- `src/SiloPlayer.Core/Models/MediaMaintenance/MaintenanceJobStatus.cs` (new): AdminTaskJob fields, the item result, and `MaintenanceJobRead`.
- `src/SiloPlayer.Core/Api/SiloApiClient.MaintenanceJobs.cs` (new): identity-bound GET returning the `Retry-After` hint.
- `src/SiloPlayer.Core/Api/MediaMaintenanceApi.cs`: `CaptureIdentityContext`, `IsCurrentIdentityContext`, `GetJobAsync`.
- `src/SiloPlayer.Core/Services/MaintenanceJobFollower.cs` (new): the bounded job wait.
- `src/SiloPlayer.Core/Services/MetadataRefreshFollow.cs` (new): the post-wait decision and the invalidated IDs.
- `src/SiloPlayer.Core/Services/DetailMetadataChangeHub.cs` (new): catalog event routing and the per-visit coalesced watch.
- `src/SiloPlayer.Core/Services/ItemDetailPrefetchCache.cs`: `InvalidateCached`.
- `src/SiloPlayer/ViewModels/ItemDetailViewModel.cs`: `RefreshServerMetadataAsync`.
- `src/SiloPlayer/Views/ItemDetailPage.ServerRefresh.cs` (new): watch lifecycle, the UI-thread reload with holds and scroll/focus restore, `ShowRefreshMetadataDialogAsync` (moved here), and the job follow.
- `src/SiloPlayer/Views/ItemDetailPage.xaml.cs`, limited to small hooks:
  - the old `ShowRefreshMetadataDialogAsync` is removed;
  - `BeginServerMetadataWatch` before the first read;
  - `Resume` after first paint and on player state changes;
  - `EndServerMetadataWatch` in `OnNavigatedFrom`.
  - The Edit Metadata open path is untouched.
- `src/SiloPlayer/MainWindow.xaml.cs`: shell subscribes `catalog` and routes events to the hub.
- `src/SiloPlayer/App.xaml.cs`: registers the hub singleton.
- `tests/SiloPlayer.Tests/DetailMetadataRefreshTests.cs` (new).

## Verification

Local results on this source:

- **Unit tests:** all 7,491 pass, including the 17 `DetailMetadataRefreshTests`.
  The coalescing tests step the pump synchronously and run on a thread-pool
  thread, because the test runner's SynchronizationContext would otherwise
  queue the `ConfigureAwait(false)` continuation.
- **Contract tests:** two existing source-contract tests now match this
  change: the detail page's Refresh Metadata entry point reads both
  `ItemDetailPage` partials, and the shell subscription includes `catalog`.
- **Naming:** the job-read types use `MaintenanceJob*` names. They are item
  maintenance reads, like `MediaMaintenanceApi`'s existing admin item routes,
  not Admin surfaces, so the user-only client architecture test passes
  unchanged.
- **Native:** these all pass: `conditional-editors`, `profiles-metadata`,
  `detail-action-hover`, `oct20-detail-cards`,
  `audiobook-detail-interactions` and `request-detail-parity`.

Not yet exercised by a native fixture:

- a remote save while a detail page is open;
- a save while the editor or the full-screen player is open;
- Refresh Metadata Quick and Complete against a real server;
- navigating away or signing out mid-job;
- the load of an admin's catalog event stream during a full scan.

## Limits and follow-ups

- The card menu's Refresh Metadata (`MediaItemMenu.cs`) still posts and shows
  the old English "...queued" toast without following the job. The audit scoped
  6d to the detail page.
- The 6a item-keyed pending-write registry (another worker) is not present here.
  Once it lands, add its "writes pending for this item" check to
  `IsServerMetadataReloadHeld` in `ItemDetailPage.ServerRefresh.cs`. Today an
  open dialog is the only write hold.
- After a save in the Edit Metadata dialog, the existing post-save reload runs,
  and a remote event held during the dialog adds one more in-place reread.
- The desktop has no `jobs`-channel waiter. The follow always reads the job
  with the server's poll hint, which upstream falls back to when its socket is
  not live.
