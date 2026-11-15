# Admin Manage › Libraries › Subtitle Files

Official reference: Silo-Server/silo-server ad7fe63a3428be01bf3da56738ce0e861c871393 (November 15, 2026; relevant Subtitle Files contracts and WebUI unchanged since fd0d8c6c09af11aa2b165034e6c553a45334f2fd)
(`web/src/pages/AdminSubtitles.tsx`, `web/src/components/admin/subtitles/{AdminSubtitlesFilters,
AdminSubtitlesTable,AdminSubtitleEditSheet,AdminSubtitleDeleteDialog,subtitleAdminStyles}.tsx`,
`web/src/hooks/queries/admin/subtitles.ts`, `web/src/api/v2/adminSubtitle{s,Metadata,Bytes,Delete}.ts`,
`internal/apiv2/admin_subtitle_{list,metadata,bytes,delete}.go`). Desktop base: PR167 head
4b962d5d79c42d0ea27e0c45fd418a14ec54c815 (Admin Manage foundation).

## Scope

Route `/admin/subtitles` (`AdminRoute.SubtitleFiles`). Included: stats (total stored, user uploads,
provider downloads, languages on page); release-name search (`q`), provider pills (All, Upload,
OpenSubtitles, SubDL, SubSource), language and uploader selects, all mirrored into the route query
with in-place replacement; Reset filters; cursor paging with Previous / Page N / Next and 25/50/100
page sizes; loading skeleton, inline load error with Retry, empty and filtered-empty states; the
ten-column table with item links, file basename with path tooltip, language chip, provider badge,
release, format, HI, uploader and relative added time; bounded download; the Edit-subtitle panel
(language, release name, hearing impaired) and the delete confirmation.

Excluded by contract: provider configuration (Configure) and subtitle content/timing editing.

## Contracts

- `GET /api/v2/admin/subtitles` — filters omitted when blank/All, `q` trimmed, `limit` 25/50/100,
  `cursor` ≤ 8192. Response validated like the WebUI: page envelope, `has_more` requires rows and a
  fresh cursor, no cursor at the end, decimal ids, no duplicates, no more rows than requested.
- Cursor scope = authority epoch/account + page size + provider + language + user + trimmed q.
  Any change restarts at the first page synchronously; late responses for earlier scopes are
  discarded; an invalid/stale cursor error restarts at page one on Retry.
- `GET /api/v2/admin/subtitles/{id}` — strong ETag required; id and media_file_id must match the
  row. `PATCH` sends only changed fields with `If-Match` = that exact tag; never retried.
  A failure (412, 409 language conflict, transport) keeps the draft and disables Save; the native
  in-place "Reload current metadata" re-reads, keeps only the user's changed fields and adopts the
  new validator (the WebUI asks to close and reopen). Release name ≤ 4096 is validated first.
- `DELETE /api/v2/admin/subtitles/{id}` — the confirmation reads the record first, owns at most one
  DELETE with its tag; 204 is the only success (404 is a failure, unlike Monitor's delete).
- `GET /api/v2/admin/subtitles/{id}/download` — 200 with `application/x-subrip`, `text/vtt`,
  `text/x-ssa` or `application/octet-stream` only; ≤ 16 MiB in memory; declared length must match;
  `Content-Disposition` ignored; file name `subtitle-{id}.{srt|vtt|ass|ssa|sub|bin}`. Authority is
  checked before the request, after it, before the atomic publish and after (rollback on a late
  retirement) via `DiagnosticsFileTransfer`.
- Uploader picker: `GET /api/v2/admin/users` walked at 200/page, ≤ 5 pages / 1,000 accounts; a
  truncated list says so; a URL `user_id` outside it stays selectable as "Account {id}".

## Native differences (deliberate)

- After the first page, filter/page changes keep the header, stats and filters mounted (stat values
  and rows show skeletons) so typing keeps focus; the WebUI swaps the whole page to a skeleton.
- Search is debounced 300 ms (WebUI queries per keystroke).
- Row actions reveal on pointer or keyboard focus at ≥640 px and remain visible below that viewport width, matching the WebUI.
- One download at a time: every download button is disabled while one is in flight.
- The load-error state shows Retry and omits the contradictory empty-state card.

## Integration (lead-owned shared files)

- App.xaml.cs: `services.AddAdminSubtitleFiles();` (`Views/Admin/AdminSubtitlesRegistration.cs`).
- installer/build.ps1 approved page resources: `Views/Admin/AdminSubtitlesPage.xbf`.
- docs/parity/admin-monitor-surfaces.json `sourceFiles`: the files listed in the domain report.
- Native fixture dispatch: `SILO_NATIVE_TEST_ONLY=admin-subtitles` →
  `AdminSubtitlesNativeFixture.RunAsync(_window)`.

Text: `AdminSubtitlesText` holds the 77 official `web.admin.*` English messages (generated catalogs
exclude `web.admin.`) plus eight English-only `admin.native.subtitles.*` keys for native states;
shared keys (`common.*`, `web.common.*`, `web.shell.*`, `language.*`) use the localized catalog.

## Verification limits

Lead verification passed: 184 focused cases; 6,785 full unit tests; Release publish and native host build; admin-manage-foundation and admin-monitor; and three consecutive admin-subtitles native runs on the final product source. The subtitle runs cover filter/pager/reset, metadata/412 recovery/delete, focus retention, viewport breakpoints and authority retirement. Captures at 1440/1024/800 were produced; 1440/800 and editor-conflict captures were inspected. No live server,
real phone/server interoperability, file-picker download, visual parity, all-DPI or localization
beyond English is established by this increment.

## Render lifetime and resource limits

The pager and its controls stay mounted across list reads, avoiding reuse of controls still owned by detached panels. Dispatcher render callbacks log the exception type and HRESULT without response data if rendering fails. List pages are capped at 100 rows, uploader discovery at 1,000 accounts, and downloads at one 16 MiB buffer. Download and uploader-only notifications keep existing table rows mounted; download buttons update in place so keyboard focus is retained. Search waits 300 ms. Runtime latency, allocation counts and real-server load have not been measured.

## Remaining review limits

Conflict reload retains fields the user changed and adopts a fresh validator; it does not present a per-field comparison against another admin’s values. Uncertain write outcomes are not automatically replayed. Leaving or losing admin authority cancels pending work, so a sent write can have an unknown outcome until reloaded. The save-picker-before-fetch path has not been exercised with a real picker. UTF-16 release-name length validation can reject supplementary Unicode characters earlier than the server. Uploader options may refresh while their dropdown is open. These are not certified as complete state or multilingual parity.
