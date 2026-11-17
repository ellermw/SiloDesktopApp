# Books reader implementation — November 16, 2026

## Lead source reconciliation for immediate Beta submission

The owner directs submission while the local runtime/storage hold remains in
force. Final source now uses stable reader identity and fresh request contexts
for download/progress/annotation operations; request retirement no longer owns
the page lifetime. Startup failures are separated from runtime script errors;
unresolved saved locations stay recoverable with contents available and saves
suppressed until explicit recovery. FB2/FBZ legacy open uses saved overall
progress, comic image ordering preserves case-insensitive desktop order, and
comic Next uses canonical shared series ordering. Canceled navigations no
longer become fatal reader errors. Browser/render process exits and unresponsive
renderers still show a failure screen and require reopening the reader;
automatic renderer recovery is not implemented.

These lead corrections and their new regressions have not run under the local
hold. All verification below is earlier worker evidence, not final-payload
certification. Pipeline QA must execute the final unit/Node/native matrix.
Normal detail-page Read handoff, legacy FB2 highlight migration and remaining
low-priority review observations are not certified by this handoff.

Legacy FB2/FBZ annotations are retained, not renumbered into foliate's different
section layout. Bookmarks and highlights with `chapter:N;fraction:F` but no CFI
report an unavailable location when selected; legacy highlights remain listed
but are not painted. Only the saved reading progress has an overall fraction
for safe restoration. New CFI annotations are unaffected.

## Source and scope

- Desktop base: `173c37974894fe5d52a4a4437c356c84e5c897d9`, Beta
  `1.2.1165-beta.1` / numeric `1.2.1164.1`; version stamps unchanged.
- Official `Silo-Server/silo-server` main was fetched directly by the lead and
  pinned at `17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8`. Read-only reference:
  `D:/SiloPlayer-build/books-parity/upstream`.
- This increment owns EbooksApi, extraction, EbookReaderPage, packaged reader
  assets and focused fixtures. ItemDetailPage/ViewModel, Library, audiobook
  controls and `docs/current-development.md` are unchanged. No entry-point
  change is required for the reproduced failure: existing Read navigation
  reaches EbookReaderPage. Source remains uncommitted for lead integration.

## Diagnosis and contract

`internal/apiv2/ebook_file.go` registers profile-scoped authenticated GET/HEAD
`/api/v2/ebooks/{content_id}/files/{file_id}/read` as **raw bytes**, not a JSON
envelope. GET can return server-converted EPUB or original Kindle fallback.
The existing desktop URL/method were correct. Reader config remains a
`config` envelope with ETag/If-Match; progress remains a `progress` envelope;
annotations retain their existing validators and paginated `items` contract.

A native fixture using real `Frame.Navigate(EbookReaderPage, ...)` reproduced
**Unable to open ebook** on baseline when the endpoint returned a valid raw
MOBI fallback. The native extractor required ZIP bytes even though the packaged
Foliate engine already supports Kindle. Ordinary EPUB opened on the same
baseline. This proves a reachable cause, not the format or cause of an
unavailable real user book. The original red fixture's failure label says EPUB
even for its MOBI invocation; its loading-state text is the observed symptom.

## Implementation and WebUI comparison

- MOBI/AZW/AZW3 BOOKMOBI fallback reaches the existing engine; converted EPUB
  is still identified by its package. SVG-only EPUB spines are accepted.
- FB2/FBZ and CBZ/CBR now use the packaged engine instead of separate native
  generated-chapter/image navigation. Bounded native RAR extraction is repacked
  into CBZ with existing dependencies. Archive traversal/entry/expanded-byte
  guards remain; AVIF/JXL pages and case-insensitive comic image extensions
  are recognized. Zipped FB2 retains the `fbz` loader identity.
- Existing WebUI-equivalent native navigation, TOC, bounded search, theme,
  typography, profiles, bookmarks/highlights and progress controls are retained.
  The same engine now provides them across supported formats. Comic prose
  controls/search stay hidden or disabled like WebUI. PDF TOC destinations use
  the actual publication resolver; fixed-layout spread preferences now reach
  the renderer. Legacy saved chapter locations remain accepted except for
  FB2/FBZ annotation locations without a safe mapping, as described above.
- Downloads check request authority before sending and reader identity after
  receiving bytes, surviving request retirement without adopting a new profile.
  Unexpected cancellation during active opening shows a failure, not a spinner.
  The page checks asynchronous startup and retires/cancels/closes its renderer
  on identity changes, including profile setters which emit no event. A 250 ms
  scoped timer covers those setters; saves cannot adopt a new profile.
- Failed WebView navigation/process/module startup and book opening now show
  a visible failure state. Bootstrap messages contain generation only, not
  exception details. Book iframes remain script-disabled, host objects and
  permissions denied, external navigation/network access constrained by the
  existing host policy/CSP. No engine rewrite or package dependency added.

## Executed evidence

All generated output is under `D:/SiloPlayer-build/books-parity/reader`.

| Command/check | Observed result |
| --- | --- |
| `verify.ps1 units` before fixes | 4 failed / 36 passed / 40 total; Kindle, SVG EPUB, modern comic images, stale-profile bytes |
| `verify.ps1 opening` on baseline, EPUB then MOBI | EPUB opened; MOBI showed the exact loading-state error; startup exploration failures are retained separately in the same log |
| `verify.ps1 units` after Core/extractor fixes | 40/40 passed; `results/reader-units.trx` |
| `node tests/reader-fixtures/run-browser.mjs` | PDF TOC/spread reds, then 45 Chromium EPUB/PDF checks passed; `renderer-final.log` |
| `node --test tests/reader-fixtures/bootstrap.test.mjs` | Bootstrap reporting red, then passed in the Node suite |
| `verify.ps1 opening` after Kindle repair | MOBI rendered, sandbox/progress assertions passed; profile retirement failed, motivating the timer repair |
| `verify.ps1 build` after timer repair | App publish and native host build passed; `publish.log`, `host-build.log`; mutex released in `finally` |
| `node --test tests/reader-fixtures/comics.test.mjs` | New uppercase-extension regression failed before the one-line filter repair |
| `node --test` with all `tests/reader-fixtures/*.test.mjs` | Final source: 28 passed, 0 failed; no browser profile/build output created |
| `git diff --check` | Passed |

The reusable payload/host were built at 16:01 UTC on November 16. The later
uppercase comic filter is source-only and **not** copied into that payload.
`node-green.log` is an older unsuccessful run and is not final green evidence.
`results/opening-red.txt` and `results/opening/results.txt` preserve exploratory
and genuine failures; neither is an all-green native receipt.

## Pending verification and limits

The storage coordinator stopped new .NET/publish/native/browser runs. Re-run
latest native MOBI profile retirement, then EPUB/PDF/AZW/AZW3/FB2/FBZ/CBZ/CBR/
SVG EPUB and existing reader interop/controls after a finite slot is granted.
The new stored-RAR CBR fixture and FB2/FBZ paths have compiled but have not run.
No final native all-format or visual/accessibility parity claim is made.
The reader identity survives request-context invalidation, including transport
catch-up; the new download-retirement and native TOC recovery regressions still
require pipeline execution.
DRM/encrypted Kindle, compressed real-world RAR variants, JXL runtime decoding
and the actual user's book/live server are not certified by these fixtures.

No installed player, user settings, production, installer, commit, push or PR
action occurred. Current publish/host/artifacts and red/green evidence are
preserved. Four closed `reader/temp/silo-reader-browser-*` profiles (62.51 MiB)
are superseded candidates for lead-directed reclamation; no cleanup occurred.
`reader/src` and `reader/tests` are source junctions, not disposable copies.
