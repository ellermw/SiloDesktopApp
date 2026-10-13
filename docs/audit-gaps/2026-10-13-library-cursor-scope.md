# Library scrolling rejects a cursor signed for a different page size

Local repair: **1.2.247**, preserving all 1.2.244–246 changes. Official Silo server main was fetched directly on October 13, 2026: `22489b17b9ced83dbcb1d8281b1ac01bcbb30064`.

## Cause

The current server's `internal/apiv2/catalog_items.go` adds `limit` to the canonical query before signing either the root `window_cursor` or a continuation cursor. `seek`/cursor position and the count preference do not change that scope. In `internal/apiv2/cursor.go`, a mismatched scope produces HTTP 400 with the exact reported detail: “The cursor is malformed, tampered with, or belongs to a different query.”

The desktop's `LibraryViewModel` requested changing page sizes with the same cursor: an initial 60-item load, larger viewport windows, smaller remaining tails, count-only requests of size 1, and variable prefetch neighborhoods. The completed-continuation cache checked owner/filter/version but did not account for changing network limits. Thus normal scrolling changed a signed query parameter while retaining the old token. The oversized-filter POST adapter also shrank its limit after a partial page, producing the same error.

This is a desktop request-construction defect. Earlier paging mocks accepted arbitrary root/next strings and did not enforce the server's signature scope, so those passing checks missed it.

## Reproduction and repair

An independent HMAC fixture enforces the server's query/page-size binding against the actual desktop view-model and HTTP builder. Movie, series and audiobook tab/scroll scenarios, a distant/end jump and legacy load-more all failed with the reported catalog error before repair. A separate partial-page, large structured-filter case then exposed the POST adapter's matching defect. The native Library-page fixture also rejected the old requests when entering the real Library tab and scrolling.

All Library catalog requests now use a fixed network page size of 100, independent of the viewport, prefetch size, exact count or 60-slot legacy UI pages. The legacy list still retains its 60-slot layout; its adapter trims the returned UI page. Fresh query/profile/filter changes continue to reset snapshot/continuation authority. Whole fetched pages remain in the bounded viewport cache, avoiding extra reads for small extensions.

The large-filter POST adapter keeps its signed wire limit throughout its internal loop. If a partial-page aggregation fetches beyond the requested count, it trims the returned items and retires the continuation that would skip those discarded rows; the root seed and explicit seek remain valid. Combined window responses also propagate the final page's `has_more` value instead of retaining the first page's value.

Failures now record the complete exception in `catalog_errors.txt` with offset, fixed limit, count preference, cursor **kind**, query version and read generation. Raw cursor values, filters, account credentials and request URLs are not written by this diagnostic. Existing logger redaction and rotation apply. The original failure remains visible to the user; it is not hidden or retried without its cursor.

## Verification

- Original failures: `.codex-tmp/library-cursor-oct13/red-tests.txt`, `red-large-query.txt`, `native-red/results.txt`.
- New scope checks cover three library types, scroll/prefetch/count, distant/end jumps, legacy paging, sort/filter/profile changes and partial large-filter POST responses.
- All **1,669 main tests** pass. The two changed existing request-count expectations now require fewer requests because the final full page supplies the small extension.
- **23 published playback checks** pass against the exact packaged app service.
- The actual Library tab and ScrollViewer pass against a scope-validating fixture at **1920×1080, 3440×1440 and 3840×2160**, using movie, series and audiobook libraries respectively. This is isolated native verification, not a live production-server session.
- Native favicon conversion/loading also passes on this payload for PNG/WebP/ICO/SVG, source colors/dimensions, independent server assets and fallback cases.
- All **561 packaged files** match the test copy. Additional source assets required by the host were supplied only to that copy.

The source and installer remain local/uncommitted. No user playback was closed, no real server data was changed, and no GitHub publication occurred. The unrelated intermittent Next Up removal error, full native-suite limitation and phone interoperability limits remain as recorded in earlier releases.
