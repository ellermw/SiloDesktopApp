# October 20 catalog cursor recovery

## Observed evidence

The installed application's preserved `catalog_errors.txt` contains 27 HTTP400 `invalid_cursor` responses on October20, between15:23 and15:28UTC. Requests included virtual-window starts623,402,419 and436 and local query versions14,19 and22. The wire limit was consistently100. Sanitized copies of the catalog, API diagnostic, Home and navigation logs are retained under `.codex-tmp/upstream-oct20/saved-*.txt`. No signed URLs, actual cursor values, credentials or account settings are included in this report.

These files establish repeated requests using a cursor rejected by the server. They do **not** establish which server-side scope input invalidated the first cursor. No older rotated catalog-error files were present when inspected; the user's report of errors a day or two earlier cannot be precisely correlated to an unavailable earlier file.

## Client failure and repair

`LibraryViewModel` retained its snapshot and loaded page/window cache after an `invalid_cursor` response. Subsequent viewport and optional count requests could therefore reuse the rejected snapshot and repeatedly fail. Current Silo cursors bind the query, account/profile and viewer policy state; a locally unchanged query does not guarantee that a previously issued cursor remains acceptable. The earlier fixed100 wire-limit correction remains necessary and is preserved.

The client now retires the rejected query state, cancels its outstanding work, clears snapshot/boundary/page/window caches and removes loaded cards from that scope. It retries the same viewport once with a new cursorless query. The old total is retained only as a positioning estimate until the new response arrives. A failing fresh request follows the normal visible error path; it does not enter an unbounded retry loop. Existing account/profile/query/window authority guards prevent a late response from restarting a replacement query.

New `catalog_queries.txt` entries record lifecycle, local query/window versions and short SHA256 fingerprints of the query and cursor, correlated with API operation IDs. They do not log actual cursor contents or search terms. This allows future reports to distinguish a new query, a rejected old snapshot, its single recovery attempt and a subsequent failure.

## Reproduction and verification

`LibraryCursorScopeTests` rotates a fake server's cursor scope after the first load. Before the repair both new cases failed. After the repair the current range reloads without the rejected cursor, old loaded cards disappear and a later range uses the new cursor successfully. A separate failure case proves that recovery is bounded when the fresh request also fails. All nine cursor-scope cases passed in the focused run; final release verification is recorded in `docs/releases/1.2.286.md`.

The original first-token invalidation trigger remains unknown. This repair addresses the demonstrated client failure to recover from a rejected cursor; it is not a claim that historical playback buffering or intermittent Next Up removal has been resolved.
