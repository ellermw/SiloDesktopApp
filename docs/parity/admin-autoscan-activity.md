# Autoscan Activity — scoped parity note

## Identity and scope

- Branch `t3/admin-autoscan-activity`; app version stays `1.2.1181-beta.1` (no version, release or installed-player change).
- Official Server/WebUI reference: `88b825d57e4b2296050fda69eaf619eefbf208d1`, read as pinned Git objects from `D:/SiloPlayer-work/server-upstream-nov14`.
  Earlier work on this branch was written against `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`; upstream commit `e3887af09`
  ("show autoscan poll details in a paginated inspector sheet (#4631)") then rewrote the Activity page, so the page was brought to the new design.
- Scope is exactly `/admin/autoscan/activity`. Autoscan Sources and Autoscan Settings are separate families and are not implemented or imported here.
  The official Activity page has no link to either, so none is rendered.

## Official references (at 88b825d57)

- `web/src/pages/AdminAutoscanActivity.tsx`, `web/src/pages/admin/autoscan/ActivityPanel.tsx`, `PollActivityBadges.tsx`, `PollEventInspectorSheet.tsx`,
  `pollEventDisplay.ts`, `PollEventTable.test.tsx`, `sourceDisplay.ts`; `web/src/components/ui/pagination.tsx`, `pagination.utils.ts`.
- `web/src/hooks/queries/useAutoscan.ts` (15-second refresh), `web/src/api/v2/adminAutoscanEvents.ts`, `adminAutoscanScans.ts`, `web/src/lib/scanRuns.ts`, `autoscanLabels.ts`.
- `internal/apiv2/admin_autoscan_events.go`, `admin_autoscan_scans.go`, `scan_controls.go` (response shapes, 50-change cap, signed continuation, `POST /api/v2/scan/cancel`).
- `web/src/locales/en.json` — every `web.admin.autoscan.activity.*` key the page uses matches the current text (checked by script against the compiled catalog).
  The server's autoscan handlers and `types.ts` shapes did not change between the two pins.

## What the page does

- Header, a stat card (Active, Queued, Running scans, Polling, Latest poll) with the refresh button, a "Polling now" section, and the Autoscan queue
  (live scans from the shared `scans` realtime channel, autoscan trigger only, eight per page, per-library Cancel).
- History with a Scans/Polls switch, search (300 ms debounce, shared by both views), a status filter per view, Reset, loading rows, filtered and unfiltered empty
  states, and a failed state with **Try again**.
- Scan history columns: status, library (with scan id), source, scope (mode, path, error), result, time, poll badge. Poll log columns: status, source (+ webhook badge),
  counts (metric strip + truncated error), Details button, duration, time. Below 1024 content DIPs rows become cards; below 640 the stats and pager compact.
- Numbered pagination exactly as `TablePagination`: summary, rows selector (25/50/100), first, previous, numbered window with ellipses, next, last.
  The server pages by signed continuation, so reaching page N walks the earlier cursors (as the WebUI does), reusing cursors already seen; an explicit refresh or
  Try again re-walks from the first page. The WebUI refuses an offset of 100 pages or more, so at most 100 pages are offered and a note says so when more rows match.
- Poll inspector sheet (opened by the Details button, or a click on the row): status/delivery badges, a bounded scrolling error box that takes keyboard focus, metric pills,
  **Paths** and **Scans created** tabs (opening on Scans created only when no paths were recorded), each with search, outcome/status chips with counts, reset, empty and
  no-match states and its own pagination (paths 15/25/50, scans 20/50/100). A path's scan-id pill jumps to that scan. All 14 official reason texts, the follow-up scan
  wording, unknown outcomes (raw value) and unknown reasons (server detail) are shown as the official page shows them. Closing returns focus to the row's Details button.
- The inspector keeps the **latest snapshot** of the inspected poll: newer reads replace its data without resetting its tab, filters or pages, and it stays open with the
  last data if the poll leaves the current page. View, filter and page changes close it (the official table unmounts then).
- Live updates: status and the shown history refresh every 15 seconds through a bounded one-shot timer that stops when the page is left, the window is hidden or the
  server/account changes; a refresh failure keeps the rows and offers Try again. Every response is applied only if it is still the newest request.

## Writes

The only write is the official per-library cancel (`POST /api/v2/scan/cancel`, body `{"library_id":"<decimal string>"}`), sent once. A 4xx (not 408) answer is a refusal: the server
reason is shown and Cancel stays available. A timeout/transport failure/5xx is an unknown outcome: Cancel for that library is blocked until an explicit read-only
reconcile finds newer live state; it is never replayed.

## Native adaptations and limits

- Names, paths and errors pass the credential/signed-URL redactor before display; dates use the shared date/time preference formatter.
- The WebUI's inline `<details>` of the earlier page is gone; the older per-row source/library links, the "linked poll" jump and the Previous/Next-only pager were removed.
- Event runs are bounded at 5,000 per event (plus the 4 MiB transport cap); an over-limit response fails visibly. Changes keep the server's 50-entry cap.
- The sheet is the shared Admin detail sheet (slide-over, Escape and close button, page disabled behind it).
- Not verified: live-server and phone/plugin interoperability, all-DPI and screen-reader behaviour, and visual parity beyond the captures reviewed below. A row click that
  opens the inspector (as opposed to its Details button) is implemented but not driven by the fixture.

## Verification

See the dated entry in `docs/current-development.md` for the exact runs. Unit tests: `AdminAutoscanActivityTests`, `AdminAutoscanActivityLifecycleTests`,
`AdminAutoscanActivityDisplayTests`. Native selector: `admin-autoscan-activity`.
