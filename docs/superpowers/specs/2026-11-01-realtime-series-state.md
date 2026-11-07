# Realtime series and season state

Approved task B; desktop base a15621a0d6d5960d6b31a620a23ae259d734dedb, source 1.2.519. Official main inspected at 63ef34b236b88dc77acca1a9a3ed90235377691f, including originating change 20b1d359c95d73ca17720ed95013c268c667fdd1 and RealtimeEventsProvider/mediaSurfaceRefresh.

Watched/progress user_state.changed events invalidate the changed item and named series, even without a loaded episode. An open related series/season refreshes its authoritative aggregate state, cached season counts and explicitly supplied server play/resume target; sibling episodes never inherit the event's scalar. Preserve an omitted target from legacy responses, and honor an explicit null. Existing local watched feedback and playback/room lifetimes remain intact.

Capture request context before dispatch. Reject different profiles, retired subscriptions, closed windows and reads completing after navigation/profile/local state changes. Invalidate cached reads immediately; coalesce visible refreshes with a 200ms trailing delay and 1s maximum wait. Preserve all named series across the window, including later events with no series. Existing reconnect catch-up refreshes visible detail state and cache without controlling playback.

No release stamping, installed player control, real accounts or publication. Synthetic HTTP and native fixtures establish only synthetic behavior.

Review correction at 2431f182: mounted snapshots must use their own catalog requests, not the speculative cache's invalidation/retry loop. Remote traffic queues unioned trailing scope (including an active episode/catch-up) without retiring a still-owned read. Initial scoped loads retain invalidations while Item is null and replace pre-event snapshots with a bounded owned read. Newer local watched/progress state retires old remote snapshots; trailing reads wait for the local final save. A named series alone does not invalidate an unchanged sibling's /watch position; actual container changes still do. Mounted season controls recompute availability for both explicit null and restored play targets.

Local final-save barriers wait for settlement, not historical success. A failed/canceled save cannot permanently block later canonical reads after a newer save settles. The write owner retains error reporting; local revision and navigation guards continue rejecting pre-mutation snapshots. A read rechecks a barrier that gained another local save while it waited.
