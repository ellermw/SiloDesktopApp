# Library alphabet navigation

Desktop base: `a26ff559de24fa654e21a11e3132a1b18295c7d8` (stable 1.2.519).
Official server/WebUI main: `63ef34b236b88dc77acca1a9a3ed90235377691f`.

## Intent

Add the WebUI's right-hand `#` / A–Z quick navigation to Libraries → Library. Clicking an available letter reveals its first matching title without loading the preceding library. Preserve library filters, title sort direction, bounded card virtualization, and account/profile ownership.

## Approaches

Recommended: use the advertised `letter_index` capability and `POST /api/v2/catalog/letters`. Its counts/positions/runs match server sort titles, accent folding and filters. Use those exact positions in the existing catalog seek and pixel-scroll grid.

Alternative: expose desktop's existing 20-point sampler. Rejected because its approximate offsets ignore filters and server collation, and can jump to the wrong letter. Loading every title to calculate an index is also rejected because it defeats large-library virtualization.

## Behavior

- The rail appears only in the Library title-sorted item grid, in either direction. Recommended, Collections, non-title sorts and grouped audiobook axes do not show it.
- The endpoint receives the same library, media type, grouped rules and order as the catalog. Unsupported/older servers retain normal browsing without guessed jumps.
- `#` means numbers, symbols and other scripts. Only letters with results can jump. Use returned runs to mark the current letter; preserve a chosen final letter while clamped at the bottom.
- Keep the rail outside the scrolling card canvas and reserve space so it does not cover posters or the scrollbar. Adapt to short windows with two columns rather than clipping letters.
- Keyboard navigation uses a single tab stop, arrow keys/Home/End or letter typing to move focus, and Enter/Space to activate. Give letters accessible names/counts. Mouse activation is required; native pointer handling must not hijack ordinary library scrolling.
- Filter, library, profile/account or language changes retire pending indexes and jump operations. Late responses must not restore another query's letter positions. Navigating away cancels work. Errors must preserve a usable catalog.

## Verification and delivery

Behavioral tests cover exact distant seeks, descending order, grouped filters, old capability fallback, empty results and stale responses. A synthetic native view fixture checks the actual rail, activation/scroll position, active/disabled letters, short-window sizing and tab/sort visibility. No installed player, credentials or setup actions are used. Hold `SiloOct18Build` for .NET builds/tests and preserve existing frozen outputs.

Submit a focused PR to `beta`; leave release stamps/publication to the beta pipeline. Stable/main promotion is outside this request.
