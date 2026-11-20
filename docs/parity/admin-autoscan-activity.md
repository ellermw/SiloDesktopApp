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
- Requests: a page, filter, search, view or page-size change reads only the history query (the cursor walk it needs); the refresh button and Try again read the status and the shown
  history again; the library, source, connection and plugin names are read when the page opens and again only to recover from a failed lookup. A finished scan only updates the live
  queue; it never triggers a name lookup (the official page does not refetch names per scan event). While the next page loads the previous rows and the pager the admin used stay on
  screen (a native choice: the official page has no placeholder data and shows loading rows for an uncached page, but keeping the pager mounted keeps keyboard focus on it).
- Keyboard focus: tab, chip, scan-id jump and pager buttons of the inspector and the main pager keep focus across rebuilds, and across the 15-second refresh, by restoring it to the
  control with the same automation id (the neighbouring pager button when the pressed one became disabled; the Scans tab after a scan-id jump, because the pill is hidden with the Paths tab).
- Live updates: status and the shown history refresh every 15 seconds through a bounded one-shot timer that stops when the page is left, the window is hidden or the
  server/account changes; a refresh failure keeps the rows and offers Try again. Every response is applied only if it is still the newest request.

## Writes

The only write is the official per-library cancel (`POST /api/v2/scan/cancel`, body `{"library_id":"<decimal string>"}`), sent once. A 4xx (not 408) answer is a refusal: the server
reason is shown and Cancel stays available. A timeout/transport failure/5xx is an unknown outcome: Cancel for that library is blocked and is never replayed. The way out does not
depend on a realtime snapshot: the notice offers **Reconcile cancellation**, a read-only status read started by the admin after the unknown outcome. When it succeeds it lifts the block whatever it
shows (the scan is then gone, or still listed and visibly running) and says which; cancelling again is then a new, deliberate request by the admin. A failed read keeps the block, the periodic
refresh and a realtime event or snapshot alone never lift it, and a status answer that a newer status read has superseded lifts the block without overwriting the newer counts.

## Native adaptations and limits

- Names, paths and errors pass the credential/signed-URL redactor before display; dates use the shared date/time preference formatter.
- The WebUI's inline `<details>` of the earlier page is gone; the older per-row source/library links, the "linked poll" jump and the Previous/Next-only pager were removed.
- Event runs are bounded at 5,000 per event (plus the 4 MiB transport cap). An event with more keeps its first 5,000 runs and the rest of the page still shows: the Scans tab count and the Details
  summary include the omitted runs, and the inspector states that only the first runs are loaded. Changes keep the server's 50-entry cap.
- Row validation follows the official hooks: only the page being shown has its rows validated (an invalid row fails that page visibly); pages walked only to reach its continuation keep the envelope checks.
- The sheet is the shared Admin detail sheet (slide-over, Escape and close button, page disabled behind it).
- Not verified: live-server and phone/plugin interoperability, all-DPI and screen-reader behaviour, and visual parity beyond the captures reviewed below. A row click that
  opens the inspector (as opposed to its Details button) is implemented but not driven by the fixture.

## Independent review (November 20)

A read-only review of commit `0bc5eb4fc` against official `66b836f82a32de2fb3e5dfae6c285c19eea1096d` (nothing this page uses changed since `88b825d57`) found nine defects, all confirmed and fixed with tests that fail without the fix:

- D1 pager gap written as mis-encoded text -> escape for the ellipsis; no other mis-encoded text in the family; a source test scans every family file for it.
- D2 every page/filter/view change also read the status and, after a failure or reload, the names -> history-only requests (counted in unit and native tests).
- D3 an older reconciliation read could overwrite a newer status -> sequence guard.
- D4 an unknown cancellation could stay blocked for ever -> the explicit read lifts the block (rule above), both branches tested.
- D5 terminal scan events re-ran the name lookups -> removed.
- D6 a trailing space rewrote the search box and closed the inspector -> trimmed comparison and a no-change guard.
- D7 focus lost on tabs, chips, jump, pagers and refresh; main pager rebuilt to loading rows -> focus restored by automation id; rows kept while loading.
- D8 inspector chip filters reset themselves when a newer snapshot had no matching rows -> kept, with "No matches" and Reset, as the official sheet.
- D9 every row of every walked page was validated -> target page only; over-bound events keep the page.

Found while verifying natively: programmatic changes to an inspector search box raised a delayed TextChanged that sent the pager back to the first page (fixed: a change to text the class set itself is ignored).
The earlier assertion that Cancel stays blocked after a successful reconcile while the scan is still listed was removed from `TerminalTargetEventAllowsExplicitReviewWhileOtherLibrariesRemainActive` and replaced by the D4 tests, because the rule changed; the native fixture's wait-for-a-snapshot steps were replaced likewise.

## Verification

See the dated entry in `docs/current-development.md` for the exact runs. Unit tests: `AdminAutoscanActivityTests`, `AdminAutoscanActivityLifecycleTests`,
`AdminAutoscanActivityDisplayTests`, `AdminAutoscanActivityReviewTests` (review regressions). Native selector: `admin-autoscan-activity`.
