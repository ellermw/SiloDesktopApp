# Realtime series and season state

Approved task B; desktop base a15621a0d6d5960d6b31a620a23ae259d734dedb, source 1.2.519. Official main inspected at 63ef34b236b88dc77acca1a9a3ed90235377691f, including originating change 20b1d359c95d73ca17720ed95013c268c667fdd1 and RealtimeEventsProvider/mediaSurfaceRefresh.

Watched/progress user_state.changed events invalidate the changed item and named series, even without a loaded episode. An open related series/season refreshes its authoritative aggregate state, cached season counts and explicitly supplied server play/resume target; sibling episodes never inherit the event's scalar. Preserve an omitted target from legacy responses, and honor an explicit null. Existing local watched feedback and playback/room lifetimes remain intact.

Capture request context before dispatch. Reject different profiles, retired subscriptions, closed windows and reads completing after navigation/profile/local state changes. Invalidate cached reads immediately; coalesce visible refreshes with a 200ms trailing delay and 1s maximum wait. Preserve all named series across the window, including later events with no series. Existing reconnect catch-up refreshes visible detail state and cache without controlling playback.

No release stamping, installed player control, real accounts or publication. Synthetic HTTP and native fixtures establish only synthetic behavior.
