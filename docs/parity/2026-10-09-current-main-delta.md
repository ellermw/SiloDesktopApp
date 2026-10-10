# Fresh official Silo main delta — October 9, 2026

**Implementation follow-up:** the user approved both findings. Shared default artwork and10MiB upload alignment are now implemented; see [the implementation and fresh verification record](2026-10-09-current-main-implementation.md). Remaining work is combined visual/physical/live acceptance. The findings below preserve the audit's before-state.

Fetched directly from `https://github.com/Silo-Server/silo-server.git` with `git fetch ... main`. Current fetched main is **88dac5cdae81d52f215a64d5ea079cc189ea93f2**, committed October 9 at 14:10:50 -04:00. The previous implementation/automated checkpoint used **22e3a0ba7c1431dda77b957ed1012508d23f2b80**. Compare exact Git objects; the reference checkout's working files are not authoritative.

The interval contains 13 commits and changes 91 files. These counts are upstream changes, not desktop defects or release-number increments. This audit changes documentation only; installed104, frozen108, product metadata and public download remain unchanged. Computer control remains paused.

## Confirmed desktop work

- **Default artwork, difficulty5 and affected card neighbors13/12/9/7/4.** Upstream `DefaultArtwork.tsx`, `MediaCardArtwork.tsx` and card consumers replace missing-poster title text with a faint Silo color glow and media-type icon. TV/season/episode use TV; audiobook/book/podcast use headphones; ebook/manga/comic use book; movie/unknown use film. Failed artwork retains an available thumbhash; otherwise it uses the new fallback. Changed URL can retry after failure. Native `ExternalTitleCard.cs` still uses centered title text; `LibraryGridCard.cs` still uses `_fallbackTitle`. Implement a shared lightweight native fallback and audit every affected consumer, thumbnail failure/retry, accessibility and dimming before final verification.
- **Collection upload limit, difficulty8.** This audit also identified an existing contract discrepancy: desktop `CollectionEditorPage.xaml.cs` accepts up to20MiB and both XAML captions advertise20MB; official `collection_artwork.go` limits both multipart and linked images to10MiB. The10MiB limit exists in both reference commits, so this is a newly found old gap, not an upstream-added limit. Align validation/copy and verify at/beyond the limit while retaining the draft and existing server artwork on rejection.

## Compatible changes and acceptance cases

| Upstream change | Desktop finding / disposition |
|---|---|
|Device registration on effective-settings reads and successful playback starts|Existing `SiloApiClient.AddHeaders` already sends `X-Silo-Device-Id`, Name and Platform on authenticated requests. No new required header/body field. Include read/start registration in final Settings/playback acceptance; do not introduce duplicate registration writes.|
|People queries match each distinct word at name word starts|`PeopleApi` sends query to the same v2 route and returns server results without applying a fragment filter. No endpoint/UI removal. Retain word-start/multiword/punctuation cases in search acceptance.|
|Interrupted original downloads transition downloading→failed, including disconnected responses|Existing `DownloadFileTransfer.SaveAsync` rejects short Content-Length, writes to a partial file, and commits only on complete transfer. Existing request/response shape remains. Check server Failed state and retry in final download acceptance.|
|Scanner records actual Matroska TrackNumber and backfills matching old probes|Server-side identity improvement; v3 response schema unchanged. Current desktop `ClientFeatures` defaults to `playback_plan_v3` and does not attest `embedded_subtitles_v1`; it must not claim support for the newly documented container identity route without implementing and testing that route. Keep native/sidecar subtitle routing in difficulty1 verification; this audit does not establish a playback regression or buffering cause.|
|Manual marker deletion survives rescans; unsupported library writes return422|Server-owned marker behavior. User-only desktop intentionally has no writable marker editor (`UserOnlyClientArchitectureTests`). Do not add an admin editor merely to mirror new admin copy. Verify marker playback updates where applicable.|
|Invalid collection poster now returns422 validation_failed and preserves stored poster|Existing save catch retains pending poster bytes and reports errors. Include malformed file/source and422 retry/preservation in difficulty8 acceptance; source inspection is not execution of these cases.|
|Emby history import preserves shows hidden between episodes|Server-side Continue Watching reconciliation; existing import route and response contract remain. Check imported row removal and refresh in final Home/history-import acceptance.|
|Redis negative pool size, macOS hook Bash, WebUI source-map-js dependency|Server/tooling/WebUI-only changes. No matching WinUI dependency or desktop settings feature to add.|

The versioned OpenAPI diff changes people-query documentation and adds optional playback device headers; it removes no documented v2 endpoint. Production WebUI changes add the artwork fallback and update admin marker copy. No user-facing page/feature removal was found in this interval.

## Status and handoff

Earlier publish/1,524 unit/3 headless-mpv/15 published-service results remain evidence for the22e3 checkpoint. They do not certify88dac artwork or upload-limit corrections. The follow-up record now supplies source implementation and automated evidence for the88dac delta. **Final combined visual/physical/live verification and release remain outstanding.** No full-parity claim is justified yet. Additions stay in the existing difficulty order; do not create a new endless audit.
