# Independent reader review — September 28, 2026

Read-only review of package 2 in the shared isolated worktree. Reviewed the actual `EbookReaderPage`, local `reader.mjs`/`locations.mjs`, `EbooksApi`, native fixture, extractor and relevant Foliate sandbox/loading code. No builds, tests, user app control or live requests were run during this review. Findings were sent to the reader implementer and root for repair before integration.

## Findings

### P1 — Pin new annotation writes to the page's captured account/profile context

`src/SiloPlayer/Views/EbookReaderPage.xaml.cs` `Bookmark_Click` (around line 700) and `Highlight_Click` (around line 835) await WebView snapshot/selection work before calling `EbooksApi.CreateAnnotationAsync`. Unlike the newly context-pinned progress call at `SaveProgressAsync`, annotation creation captures the API's current context inside `CreateAnnotationAsync` (`src/SiloPlayer.Core/Api/EbooksApi.cs`, around line 55). If profile/server authority changes during the awaited script call, the old reader page can create a bookmark/highlight using the new authority. There is no prior ETag on a new annotation to reject this case. Add a context-pinned create overload and capture/validate page generation and `_readerContext` across the await; discard the stale selection instead of committing under the new context. Existing update/delete/config writes have ETag-context guards and should retain them.

### P2 — Include file identity/generation in host messages and snapshot adoption

`OpenVersionAsync` assigns `_fileId` and replaces `_book` before navigating the shared shell (around lines 246–280). `ReaderWebView_WebMessageReceived` accepts any message from the stable trusted shell origin, including `reader-ready` and `relocate`, without a file/generation identity (around lines 413–440); `ReadScrollFractionAsync` similarly adopts an awaited snapshot into mutable current fields (around lines 528–539). The JS `generation` guards asynchronous work inside one shell, but navigation creates a fresh shell with a reset generation, and previously queued host messages can outlive the old document. An old ready/relocate can therefore mark the new file ready and supply its location for a save. Pass a monotonically increasing host token into each open and echo it on messages/snapshots; only adopt values matching the captured file/token. Guard search/selection/annotation callbacks and `ExecuteScriptAsync` continuations by the same token. Include a delayed old-ready/relocate after file-switch test.

## Reviewed behavior that supports the package

- EPUB uses the local official-fork Foliate renderer rather than equal chapter weighting. Current locations come from renderer CFI; whole-book fraction delegates to renderer navigation. Legacy chapter references are resolved by extracted spine path, preserving old desktop chapter indexing when spine entries differ.
- Unknown locations are rejected visibly rather than silently accepted at chapter zero. An unresolved restore leaves the renderer unready and suppresses progress saving until explicit navigation succeeds.
- PDF uses local PDF.js through Foliate with observable page sections, progress and CFI/fraction navigation. Page-count chrome and bookmarks consume the same shared snapshot route.
- Annotation selections obtain renderer range CFIs, and drawing uses the renderer annotation API. Inputs are serialized into JS, not interpolated as executable strings.
- The shared shell has restrictive CSP, disabled host objects/permissions/new windows, allowlisted resources, and script-disabled book iframes. EPUB script-resource transforms also deny script items. Web messages require the trusted reader origin. These checks materially isolate untrusted book content; no current external-content bypass was found in the reviewed shared renderer path.
- Native fixtures include renderer interoperability plus actual EbookReaderPage persistence/bookmark wiring. Their results must come from root/reader execution; this review did not rerun them.

The two findings describe the pre-repair code observed during review. Root should record their disposition after the reader agent's lifecycle changes and combined tests.

## Implementer response

The reader implementer reports both findings repaired before root integration: host-generation query tokens echoed on shell messages with host-side generation/file guards; context-pinned annotation creation and stale-context suppression for preference/reload work. The actual-page fixture now injects old-generation relocation and profile-switch save/bookmark cases. Root's combined build/native fixture run is the execution authority for those repairs; this reviewer did not run additional tests.
