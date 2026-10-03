# Conditional ordinary-page dialog interiors — 2026-10-01

## Scope and evidence

This report closes the selected dialog-interior ownership gap in the browse and media audits: **EditMetadataDialog, MatchItemDialog, RefreshMetadataDialog, MangaFilesDialog and EditPersonDialog**. All five official component interiors were inspected from public `Silo-Server/silo-server` commit **`8e2e840474a085c6df6571a5a2850f7eb996810c`**, the reference supplied by the parent audit after its current-main fetch. Reads used `git show`/`git grep` on that object in `D:\SiloPlayer\.codex-tmp\silo-server-current`; its checked-out files were not treated as the reference. No independent fetch or legacy GitLab source was used.

Native evidence is the current source in `C:\Users\Michael\.codex\worktrees\parity-top-three\SiloPlayer`, including existing uncommitted work. Native file links identify this local snapshot, not an immutable published native revision. This is **selected source-contract coverage**, not a rendered or runtime parity certification. No app/server was launched, account used, API mutation performed, settings/credentials inspected, build installed, or application code changed. Only this report was written.

**Counts:** five top-level interiors inspected; three native counterparts found and two absent; ten source findings below (six P2, four P3); zero rendered comparisons or live interaction tests. Nested editor sections, translation/image controls, tag input and reset confirmation are mapped below; backend behavior, arbitrary provider permutations and vendor internals are outside this pass.

## Permission and reachability corrections

These are conditional controls on ordinary browsing/detail pages, but they do not all have the same gate:

| Interior | Official ordinary-page entry/gate | Native entry/counterpart | Source coverage |
|---|---|---|---|
| EditMetadata | Card menus for movie/series require `canCurateMetadata`; movie/series/season/episode detail action bars also receive curator-gated callbacks. Images require acting admin in addition. | No editor or edit action found. ItemDetail overflow and MediaItemMenu provide refresh/match only. | Header, responsive navigation, all type-specific field sections, lock/save/reset and nested translation/image contracts inspected. |
| MatchItem | Curator movie/series card menu/detail action; component also supports video aliases and generic provider rows. Admin Libraries reuses it but that page is outside this pass. | MatchItemDialog, ItemDetailPage.MatchButton_Click and MediaItemMenu.ShowMatchItemDialogAsync. Native ordinary entry policy is movie/series only. | Search fields, local-media variants, candidates, selection, truncated/empty/pending/error and apply dispatch inspected. Generic branch is inspected source, not asserted reachable from ordinary native pages. |
| RefreshMetadata | Curator item menu/action bar; manga detail also mounts the chooser. | RefreshMetadataDialog, detail and card-menu handlers. | Two modes, copy, pending indicators, dismissal, error recovery and queued-success dispatch inspected. |
| MangaFiles | Manga card `View Details` is outside the curator block; manga detail also mounts it. Available to manga viewers, with server-stripped paths when not permitted. | MangaFilesDialog from MediaItemMenu and ItemDetailPage. | Loading/error/empty/populated, count/size, optional folders and file label/tooltip rows inspected. |
| EditPerson | PersonDetail `Edit metadata` is **acting-admin-only**, rather than general curator. The separate person-refresh button is available to authenticated users. | PersonDetailPage has no edit dialog/action; PeopleApi has read/search/refresh, no person update method. | Nine fields, responsive grid, diff-only save, cleared dates, no-change/pending/success/error contracts inspected. |

Gate evidence: [MediaItemMenu entries, lines 210–250](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MediaItemMenu.tsx#L210-L250), [detail overflow, lines 898–946](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/components/ActionBar.tsx#L898-L946), [Season detail callback](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/SeasonContent.tsx#L154), [Episode detail callback](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/ItemDetail/EpisodeContent.tsx#L394), [PersonDetail action gates, lines 159–189](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/PersonDetail.tsx#L159-L189), [native maintenance policy](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer.Core/Services/ItemMaintenanceActionPolicy.cs:13).

## Editor interior contract inventory

The absent native editors still require a precise source contract so later implementation does not treat a single text form as complete parity.

| EditMetadata region | Selected official contract and evidence | Native status |
|---|---|---|
| Shell/navigation | Up to 5xl width; zero outer padding; header with type chip and movie/series locked-count; body `min(70vh,580px)`; 160px vertical section rail at sm+, horizontal tabs below sm; content scroll; pinned responsive footer. [Shell](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/EditMetadataDialog.tsx#L286-L333). | Absent. |
| General | Title, sort/original title, overview, tagline, content rating; movie runtime; series Continuing/Ended; season editable number; episode disabled season number, editable episode number/runtime. [General](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/EditMetadataDialog.tsx#L333-L451). | Absent. |
| Dates & Ratings | Movie/series year; movie release date; series first/last air dates, air time and timezone suggestions; season/episode air date; movie/series IMDb/TMDB 0–10 and RT scores 0–100. [Dates/ratings](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/EditMetadataDialog.tsx#L453-L606). | Absent. |
| Tags & Genres / IDs | Movie/series genres/studios/countries, series networks; IMDb/TMDB/TVDB IDs for all four types. TagInput trims/deduplicates, commits Enter/comma/blur, removes last tag with empty-input Backspace and individual chips with X. [Sections](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/EditMetadataDialog.tsx#L608-L666), [TagInput](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/TagInput.tsx#L12-L77). | Absent. |
| Locks/save/reset | Movie/series changes auto-lock mapped metadata fields; lock overrides merge with current item locks, including image changes while open. Save sends changed fields only; unchanged form closes; cleared dates become null, cleared timezone is empty string. Save disabled for metadata save/image apply. Reset has destructive confirmation and queues quick refresh; ordinary Cancel remains available. [State/save](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/EditMetadataDialog.tsx#L122-L251), [footer/reset](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/EditMetadataDialog.tsx#L687-L733), [confirmation](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/ConfirmDialog.tsx#L24-L63). | LockedFields model exists, no editor/update/reset handling found. |
| Translate with AI | Hidden unless metadata AI enabled; language selector, re-translate switch, Translate button; disabled while mutation/job pending; series includes children; job progress/count and terminal success/error toasts with surface invalidation. [MetadataTranslatePanel](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MetadataTranslatePanel.tsx#L30-L139). | No editor panel. Existing detail `Translate description` is a different, narrower pending-description path. |
| Images | Acting-admin only; movie/series poster/backdrop/logo; season poster only; episodes excluded from section list. Textless toggle except logos; provider warnings; loading/error/empty; responsive 3/4-column posters and 2/3-column landscapes; current/selected badges; apply original URL/type/provider, pending notification to parent, immediate current-image update and image lock. [Visibility](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/EditMetadataDialog.tsx#L44-L53), [Images](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/ImageSelectorTab.tsx#L10-L266). | No item image selector/apply editor found. |

EditPerson uses a 2xl dialog and responsive two-column grid, full-width Name/Bio (bio minimum 128px), Birth Date, Death Date, Birthplace, Homepage and TMDB/IMDb/TVDB ID. It submits only differences, sends null for cleared dates, closes without mutation if unchanged, disables Save while pending, closes on success, and updates/invalidate person and associated item detail queries. [Form and save](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/EditPersonDialog.tsx#L22-L172), [mutation outcome](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/hooks/queries/people.ts#L267-L294).

## Source findings

### D01 — P2: EditMetadata interior and entry actions are absent

**Expected:** Curators can edit all four video types from detail; movie/series card menus provide edit. The region inventory above defines type-specific sections, lock state and mutations, with extra admin image capability.

**Native:** ItemDetail overflow only builds Refresh Metadata/Match Item; card menu likewise. Searching native `.cs`/`.xaml` for `EditMetadata`, `Edit Metadata`, `UpdateItemMetadata`, `Reset to Provider`, `ImageSelector` and `LockedFields` found only the LockedFields model. No native equivalent of the form or update/image/reset actions was found.

**Evidence:** [Official editor](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/EditMetadataDialog.tsx#L122-L251), [native detail action list](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/ItemDetailPage.xaml.cs:2936), [native card action list](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MediaItemMenu.cs:160), [model](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer.Core/Models/Catalog/MediaItemDetail.cs:49).

**Missing acceptance:** Curator/admin/viewer matrix for all four types; every section at narrow/large widths; changed/unchanged/clear/locked forms; update failure; reset Cancel/confirm; image apply interleaved with metadata edits; translation enabled/disabled/pending/terminal.

### D02 — P2: Acting-admin person metadata editor is absent

**Expected:** Admin PersonDetail includes Edit metadata and the nine-field dialog described above.

**Native:** PersonDetail header has name/badges/bio without edit action or editor handler. PeopleApi supports read/search/refresh without metadata update. This is an admin conditional feature on an ordinary page, not an assertion that every viewer should see edit.

**Evidence:** [Official admin action](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/PersonDetail.tsx#L178-L189), [official editor](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/EditPersonDialog.tsx#L36-L172), [native header](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/PersonDetailPage.xaml:152), [native page update](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/PersonDetailPage.xaml.cs:110), [native API](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer.Core/Api/PeopleApi.cs:34).

**Missing acceptance:** Admin versus non-admin entry; two-column/single-column layout; clearing dates, no-op save, success detail propagation, failed save retained form.

### D03 — P2: Match search lacks explicit 500-result limit and truncation notice

**Expected:** Search requests set `limit:500`; a truncated response displays a refinement notice above results.

**Native:** Request model has no Limit, response model no Truncated, and DoSearchAsync only branches candidates/empty/error. Native cannot display the current contract's truncation state. The actual server default/result count was not exercised.

**Evidence:** [Official request](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/hooks/queries/items.ts#L405-L429), [official notice](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MatchItemDialog.tsx#L305-L311), [native models](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer.Core/Models/MediaMaintenance/MatchModels.cs:3), [native search](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MatchItemDialog.xaml.cs:498).

**Missing acceptance:** Truncated/500-result response, refinement and reselection; confirm request payload and retained scroll performance.

### D04 — P2: Match body uses nested result scrolling and different generic form geometry

**Expected:** One body scrolls current summary, local media, search and all candidates; Apply stays pinned below. Generic matching keeps Title full width, Year below, then provider rows. Dialog height follows 85vh (with shared viewport cap).

**Native:** Outer ScrollViewer contains a second results ScrollViewer capped at 360px; root caps at fixed 720px. Generic Title/Year occupy a 2:1 single row. Apply is correctly outside the body scroller, but scroll region/layout contracts differ. Nested wheel/focus consequences are unobserved.

**Evidence:** [Official shell/form](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MatchItemDialog.tsx#L161-L293), [official pinned Apply](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MatchItemDialog.tsx#L427-L441), [native root/body](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MatchItemDialog.xaml:13), [generic form](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MatchItemDialog.xaml:124), [results scroller](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MatchItemDialog.xaml:189).

**Missing acceptance:** Small viewport/large text/many paths and results; wheel, keyboard and touch scroll/focus transitions; provider form if exposed through additional native entry.

### D05 — P2: Match badges cannot wrap, and fallback native title is not trimmed

**Expected:** Candidate source/score/agreement badges use a wrapping flex row; title variants truncate to fit. Thumbnails are 64×96 and hover previews 192×288.

**Native:** Poster/preview sizes and alias/score/reason data are represented, but badges use a horizontal StackPanel without wrapping. The fallback `Native title:` TextBlock lacks trimming, unlike the other title variants. Many sources/long titles can exceed narrow candidate width; actual clipping requires rendering.

**Evidence:** [Official candidate layout](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MatchItemDialog.tsx#L358-L428), [native fallback](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MatchItemDialog.xaml.cs:617), [native badges](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MatchItemDialog.xaml.cs:659).

**Missing acceptance:** Narrow dialog, long fallback/original/alias titles, many providers, zoom and keyboard tooltip placement.

### D06 — P2: Candidate selection can re-enable Apply during an in-flight native match

**Expected:** Apply remains disabled whenever applyMutation is pending, even if another candidate is selected.

**Native:** ApplyMatch_Click disables the button before awaiting the request; every candidate click unconditionally sets IsEnabled=true and restores Apply Match text. There is no applying guard in the handler. Selecting another candidate during the await therefore permits a second apply action in source; no duplicate mutation was performed in this audit. Web candidate selection also remains available, but its pending disabled binding persists.

**Evidence:** [Official pending Apply](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MatchItemDialog.tsx#L427-L441), [native apply](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MatchItemDialog.xaml.cs:72), [native selection](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MatchItemDialog.xaml.cs:690).

**Missing acceptance:** Hold apply response pending, select another candidate, confirm Apply stays disabled and only one mutation occurs; failure restores selection/retry coherently.

### D07 — P3: Refresh chooser uses fixed geometry and partly hardcoded colors

**Expected:** Shared viewport-aware dialog with sm max-width lg; choices use theme surface/border, 16px padding, 12px corners, 12px gap and 14px descriptions.

**Native:** Content width is fixed 480; root spacing14 and descriptions13. Choice backgrounds/borders use hardcoded translucent white instead of semantic surface/border brushes. Padding/corners/icon size match source intent. Actual ContentDialog chrome, width constraints and light/custom-theme contrast need screenshots.

**Evidence:** [Official chooser](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RefreshMetadataDialog.tsx#L26-L75), [shared viewport shell](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/ui/dialog.tsx#L41-L84), [native root](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/RefreshMetadataDialog.cs:28), [native choices](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/RefreshMetadataDialog.cs:62).

**Missing acceptance:** Narrow/short window, dark/light/custom theme, loading indicators, focus/hover/disabled appearance.

### D08 — P3: Manga file inspector lacks row separators and differs in size formatting/shell

**Expected:** 2xl width and 85vh max height; bordered list with divided rows, 12px horizontal/8px vertical padding, 112px label; file sizes >=1KB keep one decimal. Only the shared corner close is present.

**Native:** Fixed620 content width, fixed680/600 height caps and extra Close footer; row padding is12×9, and plain StackPanel rows have no separators. Size formatting uses `0.#`, so exact1KB displays `1 KB` instead of `1.0 KB`. Label/name/size columns and empty/error copy otherwise broadly map. Folder paths sit in horizontal StackPanels rather than the Web flex constraints; very long wrapping is unrendered.

**Evidence:** [Official inspector](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MangaFilesDialog.tsx#L44-L111), [official size format](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/lib/mediaFormat.ts#L48-L56), [native shell](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MangaFilesDialog.cs:20), [native rows](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MangaFilesDialog.cs:131), [native formatter](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MangaFilesDialog.cs:199).

**Missing acceptance:** Loading/failure/no-files/many-files/path-stripped/path-visible fixtures; long paths; integer and fractional sizes; short window and footer comparison.

### D09 — P3: Match local-media rows differ in series path presentation

**Expected:** Series folders share one bordered list with row dividers, folder icons, final folder-name labels and full-path tooltips; per-folder copy appears on hover, root copy stays visible.

**Native:** Each folder has an independent bordered row, displays its entire path (trimmed), has no folder icon and an always-visible copy button. Root section sits separately. Copy/root calculations exist, but the interior appearance and information density differ.

**Evidence:** [Official folder list](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MatchItemDialog.tsx#L476-L569), [native folder row](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MatchItemDialog.xaml.cs:279), [native root](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MatchItemDialog.xaml.cs:326).

**Missing acceptance:** One/multiple roots, mixed separator paths, long folder names, keyboard-accessible copy and clipboard success/failure feedback; movie local-media summaries remain source-covered only.

### D10 — P3: Refresh dismissal and error feedback differ while pending

**Expected:** Choices and Cancel disable while pending; shared DialogContent still renders its normal corner Close (no pending override is passed). Mutation errors are handled outside this interior. Escape/backdrop behavior depends on the shared dialog runtime and was not exercised.

**Native:** Closing is vetoed for all reasons during `_pending`, while CloseButtonText remains Cancel and is not explicitly disabled. Failure is displayed inline with raw exception message and retry re-enables choices. This is a visible state/dismissal divergence, not a request to weaken native pending protection.

**Evidence:** [Official chooser pending/cancel](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/RefreshMetadataDialog.tsx#L24-L82), [shared corner Close](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/ui/dialog.tsx#L65-L81), [native closing veto](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/RefreshMetadataDialog.cs:16), [native pending/error](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/RefreshMetadataDialog.cs:124).

**Missing acceptance:** Pending choice/cancel/corner/Escape interaction comparison; confirm displayed disabled states agree with actual behavior; failed mode leaves usable retry and accessible error feedback.

## Existing action/state mapping and remaining limits

Native Match sends independent title/year/video IDs or normalized generic provider IDs, resets selected candidate on search, builds thumbnail/alias/year/score/source/reason rows, reports no candidates/errors, applies chosen provider IDs, keeps the dialog open on failure and closes on explicit success. Card-menu entry fetches detail before showing the dialog (Web enriches inside with Loading local media); detail entry uses available watch/item versions. Card handler broadcasts MediaSurfaceChanged after applied match; detail reloads on explicit success. These source contracts are represented; broad cache/related-season propagation, permission-context races, close-during-request and error/rapid-click behavior were not executed. [Native search/candidates](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MatchItemDialog.xaml.cs:472), [card detail/apply](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MediaItemMenu.cs:239), [detail apply](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/ItemDetailPage.xaml.cs:3547), [official enrichment](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MatchItemDialog.tsx#L65-L78).

Native Refresh passes `quick`/`complete`, replaces both icons with progress rings while pending, queues through MediaMaintenanceApi, displays mode-specific success copy, and card-menu handler broadcasts a media change. Detail handler's immediate work is queue/toast only; eventual server event/update propagation is outside this interior pass and remains unverified. [Dialog pending implementation](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/RefreshMetadataDialog.cs:124), [card callback](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MediaItemMenu.cs:218), [detail callback](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Views/ItemDetailPage.xaml.cs:3369), [native API](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer.Core/Api/MediaMaintenanceApi.cs:35).

Native MangaFiles fetches on open, cancels its lifetime on close, sums file count/bytes, conditionally shows folders, uses volume/chapter/title fallback labels and tooltip paths. Server stripping/role correctness, malformed volume labels, close-before-completion, localization and all real file-list sizes remain untested. [Native load/render](C:/Users/Michael/.codex/worktrees/parity-top-three/SiloPlayer/src/SiloPlayer/Controls/MangaFilesDialog.cs:60), [official row-label rule](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/MangaFilesDialog.tsx#L14-L24).

All five interiors remain **visual-parity-incomplete** until side-by-side rendered checks cover size, spacing, typography, color/theme, responsive constraints, loading/empty/error/pending states, pointer/keyboard focus and the action contracts above. Shared native ContentDialog chrome/theme styles were not exhaustively expanded here; the shared-controls audit owns cross-cutting dialog primitives. Admin-page-only dialogs, Split/Merge/Redetect/MediaInfo and other conditional interiors are not silently included in this five-dialog count.
