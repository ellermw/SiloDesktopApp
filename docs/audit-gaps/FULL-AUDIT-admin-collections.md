# Admin Collections + Editor — Full Audit

Webui AdminCollections.tsx (258) + AdminCollectionEditor.tsx (160) + adminCollectionsShared.tsx (1260) = ~1,678 lines.
Desktop AdminCollectionsPage.xaml (145) + .xaml.cs (629) + ViewModel (89) + CollectionEditorPage (user, partial) = ~863 lines.

## GAP 1: Admin vs User Collections Confusion — **CRITICAL**
Webui AdminCollectionEditor handles ADMIN collections with a **full-page dedicated workspace**. Desktop AdminCollectionsPage uses **inline ContentDialogs** (lines 314-410, 342-370, 374-395) instead. Also has a separate user-scope CollectionEditorViewModel mixed in.
**Fix**: Migrate admin editing to full-page workspace.

## GAP 2: Collection Import Types (MDBList/TMDB) — **CRITICAL**
Webui `adminCollectionsShared.tsx`:
- MDBListImportForm (750-913): URL input, limit, sync schedule.
- TMDBPresetForm (516-749): preset selection (trending/popular/top_rated/etc), time window, media type filters, sync schedule.
- SourceTypeSelector (450-489): type switcher.
Desktop: **NONE of these exist.** No import forms. Admin cannot create TMDB/MDBList collections on desktop.

## GAP 3: Source Configuration Management — **HIGH**
Webui tracks `collection_type` + `source_config` separately (lines 184-244, 119-122, 965-1007), allowing re-editing.
Desktop (ViewModel:46 basic load, form 399-555 simple dropdown): No source config handling.

## GAP 4: Sync Schedule Management — **HIGH**
Webui: SyncScheduleField (line 30, 727, 891, 1138) in every form.
Desktop: Displays schedule (208-238) but **no edit UI in form (507-555)**.

## GAP 5: Collection Builder Integration — **HIGH**
Webui: shared CollectionBuilder component (13-16, 330-448, 363-399) for smart rules.
Desktop: custom inline rule builder in CollectionEditorPage, not a unified component.

## GAP 6: Image Management — **MEDIUM**
Webui: ImageUploadField (350, 408-444, 711-724) for poster/backdrop with source URLs.
Desktop: No image field in form builder.

## GAP 7: library_ids Multi-Selection — **MEDIUM**
Webui: library_ids array (64-72, 90-94).
Desktop (466-489): Single library picker only.

## GAP 8: Collection Summary Sidebar — **LOW-MEDIUM**
Webui: AdminCollectionSummary (275-328, 390-398) — metadata preview while editing.
Desktop: No sidebar preview.

## GAP 9: Advanced Form State Management — **MEDIUM**
Webui: React hooks with useEffect + draft state + image state (340-398).
Desktop (399-555): Inline builder, tuple return, closure-captured state.

## GAP 10: Edit vs Create Form Unification — **MEDIUM**
Webui: separate CollectionEditForm (915-1259) that delegates to CollectionForm for manual/smart.
Desktop: single BuildCollectionForm with parameter (399-555).

## GAP 11: Error Handling & Validation — **MEDIUM**
Webui: granular validation (hasInvalidLimit 540, hasInvalidSourceLimit 952-953, hasInvalidTmdbLimit 954-955).
Desktop: try-catch on Load (336-338), basic trim/null only.

## GAP 12: Visibility vs Shared Semantics — **LOW**
Webui admin: `visibility: "visible" | "hidden"` (77-78).
Desktop user: `IsShared` toggle. Different concepts; minor cross-scope confusion.

## GAP 13: Featured Collection — **LOW**
Webui: integrates Featured into CollectionBuilder (136-137, 78, 303).
Desktop: MakeBadgeDefault for Featured (137-140). Minor.

---

## Prioritized Fix List

### CRITICAL
1. **TMDB Preset Import Form** — preset selector, media_type, time_window, sync schedule. (webui 516-749)
2. **MDBList Import Form** — URL input, limit, sync schedule. (webui 750-913)
3. **Migrate to Full-Page Editor Workspace** — replace dialogs. (webui AdminCollectionEditor.tsx)

### HIGH
4. **Sync Schedule field in form** (webui 30, 727, 891, 1138)
5. **Source config persistence** — track/edit source_config for tmdb/mdblist
6. **Image upload fields** — poster/backdrop + source URL
7. **library_ids multi-select** — CreateLibraryCollectionRequest supports array

### MEDIUM
8. Port CollectionBuilder to desktop (shared smart-rule editing)
9. Summary sidebar during editing
10. Reactive form state with cleanup
11. Granular validation (limits, URLs)
12. Separate CollectionEditForm

---

## Summary Table

| Feature | Webui | Desktop | Gap |
|---------|-------|---------|-----|
| List view | ✓ | ✓ | Parity |
| Manual collections | ✓ | ✓ | Parity |
| Smart collections | CollectionBuilder | Inline rules | Missing abstraction |
| TMDB imports | TMDBPresetForm | None | **CRITICAL** |
| MDBList imports | MDBListImportForm | None | **CRITICAL** |
| Sync schedule | SyncScheduleField | Display only | **HIGH** |
| Image upload | ImageUploadField | None | **HIGH** |
| Multi-library | Yes | No | **HIGH** |
| Summary sidebar | ✓ | ✗ | MEDIUM |
| Full-page editor | ✓ | Dialogs | **HIGH** |
| Source config editing | ✓ | ✗ | **HIGH** |
| Granular validation | ✓ | Minimal | MEDIUM |

12 distinct gaps, 7 CRITICAL or HIGH.
