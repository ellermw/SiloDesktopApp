# Admin Manage — Playback History and Skip Markers (G10 read-only audit)

Official reference: Silo-Server/silo-server
e61187daaf72e8accad486eb2f632c753ae211d5 (official main, fetched November 15,
2026; read with `git show` from a no-checkout clone). Desktop baseline: PR167
Manage foundation 4b962d5d79c42d0ea27e0c45fd418a14ec54c815, version 1.2.1064.
Scope: manage-scope-audit P1/P2 (group G10, read-only histories only). Nodes,
Playback Settings and marker tasks are separate groups.

## Sources ported

| Native | Official source at e611 |
|---|---|
| `AdminPlaybackHistoryPage`, `AdminPlaybackHistoryController` | `web/src/pages/AdminPlaybackHistory.tsx`, `web/src/hooks/queries/admin/history.ts` |
| `AdminPlaybackHistoryApi`, `AdminPlaybackHistoryModels` | `web/src/api/v2/adminPlaybackHistory.ts`, `internal/apiv2/admin_playback_history.go`, `contracts/api/v2/fixtures/list_admin_playback_history_ok.json` |
| account/profile pickers | `web/src/api/v2/adminUsers.ts` (`listAdminUsers`), `internal/apiv2/admin_accounts.go` (`listAdminUserProfiles`) |
| `AdminMarkerHistoryPage`, `AdminMarkerHistoryController` | `web/src/pages/AdminMarkerHistory.tsx`, `web/src/hooks/queries/admin/markers.ts` (`useAllMarkerEditHistory`), `web/src/lib/markers.ts` |
| `AdminMarkerHistoryApi`, `AdminMarkerHistoryModels` | `web/src/api/v2/markers.ts`, `internal/apiv2/admin_catalog_marker_history.go`, `contracts/api/v2/fixtures/admin_marker_history_all.json` |
| `AdminPlaybackAuditText` | `web/src/locales/en.json` (`web.admin.history.*`, `web.admin.markers.history.*` and the shared keys listed in the file) |

## Behaviour

Playback History lists FINALIZED attempts (never `/admin/sessions`). URL state
`user_id`, `profile_id`, `completed` (`all`/`true`/`false`) and `media_item_id`
is replaced in place through `AdminShellPage.TryReplace`; choosing an account
drops the profile; a profile without an account, or one the account does not
have, is replaced away; Reset removes every parameter. One request reads the
first page with the official page's `limit=100` (v2 ceiling 200); it never
follows `next_cursor`. That window is paged locally at 25/50/100 with no further
request. Summary cards are counts of that visible window (official label
"Visible Rows"); when the server reports more, a native note says older attempts
are not loaded. Pagination validation follows `listAdminPlaybackHistory`:
`has_more` needs rows and a cursor, a cursor without `has_more` and missing
required fields refuse the whole page, and more rows than requested are refused.

Columns match the official table: media (series-led title, item link, media type
and short session), user (account link only when that page is registered, else
text), profile (link filtering this page) and its id, method badge, watch time
(measured, or the `≈` estimate capped at the session length) of duration,
Completed/Partial badge, ended time with relative start, and View Logs / FFmpeg
Logs links to Monitor › Server Log. The model also carries the e611 measurement,
client, device, stream-decision and end-reason fields; the official table does
not render them, so neither does the native page.

Refresh: one history read per filter at a time. Manual, focus-return and
periodic refreshes join the read in flight; a filter change cancels the previous
read and discards its answer by sequence. The manual spinner lasts at least one
second. The official hook polls every 15 s even in the background; natively the
poll runs only while the page is open and the window visible, and returning from
hidden with data older than 15 s refreshes once. Departure (navigation, outer
Admin exit, unload) suspends or disposes the controller; authority changes
(account, role/permission, profile or proof, server, request context) retire
every read through the foundation `AdminAuthorityLifetime` and clear rows and
pickers before an optional reopen.

Pickers: accounts are a bounded foundation walk (200 per page, at most 5 pages /
1000 accounts) with truncation disclosed; a chosen account outside that list
stays selectable by id. One account's profiles are one read (`has_more` refused;
at most 100 retained, truncation disclosed). Picker failures leave "All users" /
"All profiles", as the WebUI does.

Skip Markers is the read-only marker EDIT HISTORY: count selector 25/50/100
(default 50, server cap 100), Refresh disabled while fetching, skeleton, load
error, empty, then when (short date and time; unparsable values shown as sent),
item label (title, file name or "File {id}", with type) linking to the item,
file path, segment (Intro, Recap, Credits / Outro, Preview from the shared
catalog; any other kind shown as sent), Set/Cleared badge with before -> after
ranges (`none` when absent), actor (`??` semantics), impersonator, request id
and client address or agent. No delete, bulk edit, termination, redetection,
provider or contribution action exists on either page.

## Differences from the audit and from the WebUI

- The audit (pinned 7115ee70) states a 200-row request; at e611 the page passes
  `limit: 100` (200 is the hook's ceiling). Native follows e611: 100.
- WebUI polls in the background and `refetch()` cancels and restarts a read in
  flight; native polls only while visible and joins the read in flight.
- WebUI walks every account for the picker; native stops at 1000 and says so.
- Positive Int64 account identifiers stay exact across filter, URL and request,
  including IDs above JavaScript numeric precision. Invalid IDs are cleared and
  padded IDs normalize to canonical decimal.
- Unchanged History rows retain their table, keyboard focus and horizontal scroll.
- A selected profile outside a truncated picker stays selected.
- A refresh that shrinks the window clamps the local page to the last page
  (the WebUI can show an empty page past the end).
- Marker history: the WebUI hook keeps react-query's default automatic retries
  and refetches on window focus. Native uses explicit Refresh and does not
  automatically retry or refetch marker history on focus.
- Unknown marker kinds render the server value (the WebUI would look up an
  undefined key). Episode codes and ranges use Latin digits (WebUI localizes
  digits for non-Latin-digit languages).
- `web.admin.*` text is English (copied official patterns), as in the other
  Admin domains; `web.markers.kind.*`, `web.common.activity.title.file` and
  `web.common.date.*` come from the shipped catalog in the interface language.
  Native-only English keys (`admin.native.playbackAudit.*`) label the bounded
  notes and picker/page-size accessible names that have no official string.

## Evidence and limits

Final integrated verification on November 15, 2026:
- 198 focused history/foundation/navigation/architecture checks passed.
- 6,793 full unit tests passed; none failed or skipped.
- Release app publish and native host build passed.
- Native groups admin-playback-audit, admin-manage-foundation and admin-monitor passed.
- History fixture proves a real 15-second poll, table retention for unchanged
  refresh/poll, redraw for changed data, exactly one coalesced manual read,
  and no History polling beyond a full interval after departure.
- Captures cover 1440/1024/800 DIP; desktop History and narrow History/Marker
  captures were inspected. Log actions have explicit cell bounds assertions.
- Seven account/profile regressions failed before review fixes and passed after.

Independent review and lead disposition are retained under
D:/SiloPlayer-build/server-update-nov14/manage-playback-audit/.
Final native receipts/captures: D:/SiloPlayer-build/results/
admin-playback-audit-history-delivery/ (and the corresponding foundation and
Monitor groups). Worker-only patch/hashes are historical; final delivery
receipt records the integrated source hashes.

Synthetic backend only. No live server, phone, real account/profile switch in
native UI, actual main-window hiding, complete visual/DPI/multilingual parity,
or latency/memory/throughput measurement is certified. Controller units cover
visibility and authority changes. The English Admin adapter and explicit
marker refresh differences above remain. Malformed-server validation is looser
than the WebUI for absent nullable duration/played fields and some picker page
metadata. No newest-server claim. Installed player/settings untouched.

Version 1.2.1064 remains under the Beta pipeline's control. Shared reusable build,
publish and test-host payloads remain active for the ongoing parity task; no
cleanup was retried after the earlier automatic approval rejection. Recovery:
0 GiB on C: and D:.
