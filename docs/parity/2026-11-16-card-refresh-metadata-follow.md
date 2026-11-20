# Card menu Refresh Metadata follows its job

A follow-up to the detail-page refresh work (#192, now in Beta), closing the
first item in that record's "Limits and follow-ups". Before this, the card menu
posted the refresh and showed a hard-coded English "...queued" toast without
following the job.

Base: Beta `3d9b64224`. Upstream: official Silo Server main `5b3dcaa2f`. The
card menu (`web/src/components/MediaItemMenu.tsx`) calls the same
`useRefreshItemMetadata` hook as the detail page (`web/src/hooks/queries/items.ts`),
without an `onReplaced` callback, so it never navigates.

## Behavior

- **Toasts and follow:** the card menu's Refresh Metadata shows the localized
  running toast (`web.item.toast.refreshRunningQuick` / `refreshRunningComplete`)
  once the server accepts the refresh. It then follows the job with
  `MaintenanceJobFollower`, which is bounded, `Retry-After` aware,
  identity-bound, and quiet on sign-out or an account switch. A finished job
  ends with the localized finish toast: artwork warning, complete finished,
  refreshed with new files, or refreshed.
- **Shared code:** both callers share `Services/MetadataRefreshFlow.cs` (post,
  running toast, follow, cache drop, finish toast) and the pure planning in
  `Core/Services/MetadataRefreshFollow.cs`. The detail page keeps only its own
  reload or replacement follow (`Decide`).
- **Outcome semantics (#192 as merged):**
  - **Definite failure:** the server reports the job failed or canceled, and
    the failure toast shows.
  - **Unknown completion:** the wait ran out, or the job read failed. This
    shows no toast, still drops the cache, and rereads the item without
    trusting any replacement ID or new-file count. The detail page's `Decide`
    reloads it; the card notifies open pages of the item.
- **Cache:** the follow drops every cached detail the job touched (the item,
  its refresh and replacement IDs, and the series of a season or episode)
  through `ItemDetailPrefetchCache.Invalidate`, for every outcome except a
  stale one.
- **Open detail pages:** a card has no page of its own to reload. After a
  finished job or an unknown completion, each touched ID goes to
  `DetailMetadataChangeHub.NotifyItemChanged`, so an open detail page of those
  items rereads in place through its watch (held under a dialog or the player).
  Only the refreshed item is structural: after a finished job when the refresh
  was complete or found new files, and after an unknown completion only for a
  complete refresh. The card never follows a replacement ID (#4187).
- **Surface refresh:** `MediaSurfaceChanged(ItemMetadataRefreshed)` now goes
  out when the follow ends, not when the refresh was queued. The detail page
  sends it too, for the same touched IDs and whether or not its visit is still
  open, so Recommendations rows and a section held for Back reread after a
  refresh started from either place.
- **UI thread:** the wait never holds it. The follower's ten-minute limit
  bounds the follow, and it accepts a cancellation token.

## Verification

Local results on this source:

- **Unit tests:** all 7,954 pass. They include the `DetailMetadataRefreshTests`
  toast plans for every outcome, the failed-job-read case, the changed-item
  plan for finished and unknown completions, a polled job followed by an open
  page's structural reread, and the source checks.
- **Native, passing:** `conditional-editors` (22 checks), `profiles-metadata`
  (11), `detail-action-hover`, `oct20-detail-cards` and
  `audiobook-detail-interactions`.
- **Already failing on Beta:** `request-detail-parity` fails identically on
  clean Beta `3d9b64224` (13 passes, then "Sequence contains no matching
  element"). This change does not cause it.

## Limits

- No live click-through of the card menu against a server was done.
