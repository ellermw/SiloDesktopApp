# Notifications grouping and bulk mute

Base: Desktop Beta `bdd033c1c`. Upstream: official Silo Server main
`17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8`, including Notifications grouping
and mute `247fec91a` and the progress performance change `17d5376ce`.
Version numbers are left to the pipeline.

## Behavior

- **Inbox:** notifications are grouped under local-calendar day headings
  (including Last 7 days and Older), with expandable episode groups.
- **Selection:** select-all and mark-selected-as-read, with per-ID reads, at
  most six concurrent.
- **Muting:** capability-gated bulk mute and unmute in ordered 500-ID requests
  without authentication replay, plus a Muted filter. Bulk feedback excludes
  not-found rows, and Undo names only the applied IDs. A single Series unmute
  shows no toast, matching the WebUI.
- **Series:** one ordered Series table with per-row switches and cancellable
  select-all-matching.
- **Layout:** the page column follows the WebUI's 1024px `max-w-5xl` with
  16px/24px side gutters.
- **Reads:** single mark-read keeps its authentication refresh.

New upstream message keys have English fallbacks. They render in English until
the generated catalogs are synced to the upstream translation release, which
the separate localization change does.

## Verification

Local results on this source:

- **Unit tests:** all 7,494 pass, including the new API, grouping, selection
  and bulk-mute tests.
- **Native, all passing:**
  - `notification-series` default and its `page-timeout`, `cap-retry`,
    `undo-timeout` and `scroll-retry` cases;
  - `personal-lists` `notification-states`, `notification-realtime` and
    `notification-pages`;
  - `profile-transfer-blocker`, `shell-localization` and `media-localization`.
- **Already failing on Beta:** `personal-lists` (default, at the external
  watchlist request-badge corner check) and `browse-acceptance` with
  `SILO_NATIVE_BROWSE_NOTIFICATIONS=1` (a XamlParseException constructing
  `NotificationsPage` in that fixture) fail identically on clean Beta
  `bdd033c1c`.

## Remaining differences

Several visual details still differ from the WebUI: the chevrons, stacked
posters on grouped rows, mark-read placement, uppercase day headings, title
size and the selection bar layout. The inbox is not virtualized.
