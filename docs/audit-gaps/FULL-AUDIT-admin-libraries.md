# Admin Libraries — Full Audit Report

Webui (2538 lines) vs Desktop (1125 + 247 + 221 = 1593 lines).

---

## 1. Page Header
**Webui (lines 280-350)**: Title/subtitle left + 3-4 action buttons right (ScanQueuePopover conditional, Scan All, Maintenance, Add Library)
**Desktop (lines 55-122)**: Title/subtitle left + 3 fixed buttons right (no popover)
**Gap**: Missing ScanQueuePopover
**Severity**: Functional

## 2. Scan Queue Popover
**Webui (lines 641-795)**: Complete real-time scan queue monitoring with pulsing "N scans" indicator, per-library groups with cancel buttons, individual scan rows showing status/mode/trigger/path/timing/progress, formatActiveScanMode/Trigger/Time/Progress helpers
**Desktop**: No equivalent
**Severity**: **CRITICAL** — Users cannot monitor active scans

## 3. Add Library Button + Dialog
**Match**: Both open dialogs; webui description mentions metadata sources and chapter thumbnails
**Severity**: Visual/polish

## 4. LibraryForm — Every Field Comparison

| Field | Webui | Desktop | Gap |
|-------|-------|---------|-----|
| Name | ✓ Input | ✓ TextBox | None |
| Enabled Toggle | ✓ Switch | ✓ ToggleSwitch | None |
| Paths | ✓ Dynamic list + Browse | ✓ Dynamic list | Missing: Browser |
| Type | ✓ Select | ✓ ComboBox | None |
| Metadata Language | ✓ Language select | ✗ Missing | **P1 Functional** |
| Chapter Thumbnails | ✓ Toggle + desc | ✗ Missing | **P1 Functional** |
| Poster | ✓ Upload/Replace/Delete | ~ Show only (edit mode) | P2 Polish |
| Metadata Providers (per-level) | ✓ ProviderLevelSection (lines 2059-2139, 2437-2465) | ✗ Missing | **P0 CRITICAL** |

## 5. Table Wrapper
**Match**: Both surface panels with styled borders; minor styling differences

## 6. Table Header
**Match**: 7 columns with proportional widths

## 7. Drag-and-Drop Reordering
**Webui (lines 141-171, 352-631)**: Full @dnd-kit implementation with sortable rows, drag overlay preview
**Desktop (lines 124-136)**: Drag handle visual exists but marked "Non-interactive for now — reorder is a TODO"
**Severity**: **P0 Functional** (feature not implemented)

## 8-12. Library Row Cells (Name, Paths, Type, Status, Last Scanned)
**Match**: Functional equivalents across all; desktop has proper formatting

## 13-19. Library Row Actions

| Button | Webui | Desktop | Gap |
|--------|-------|---------|-----|
| Check Mount | ✓ h-7 w-7 icon + pulse | ✓ 28x28 icon | None |
| Scan | ✓ RefreshCw + spin | ✓ RefreshCw + spin | None |
| Refresh Metadata | ✓ DatabaseBackup + spin | ✓ DatabaseBackup + spin | None |
| Confirm Empty Root | ✓ Conditional Trash2 | ✓ Conditional Trash2 | None |
| **Cancel Scans** | ✓ Square icon, conditional | **✗ Missing** | **P1 Functional** |
| Edit | ✓ Pencil | ✓ Pencil | None |
| Delete | ✓ Trash2 | ✓ Trash2 | None |

## 20-22. Library Row Inline Display

| Feature | Webui | Desktop | Gap |
|---------|-------|---------|-----|
| Mount check inline result | ✓ Lines 530-532 | ✓ Lines 310-312 | None |
| **Refresh job progress inline** | ✓ Lines 533-538 (message + formatJobProgress) | **✗ Missing** | **P1 Functional** |
| **Active scans list inline** | ✓ Lines 539-552 (up to 2 scans + "+N more") | **✗ Missing** | **P1 Functional** |

## 23. Empty Root Warning Row
**Match**: Both show full-width warning

## 24-25. Confirmation Dialogs
**Match**: Delete and Empty Root cleanup dialogs functionally identical

## 26. Unmatched Items Section
**Webui (lines 1642-1781)**: Section with count badge, search filter (title, library, type, status), table: Title (link to /item), Library, Type, Status, Actions. "Match" button per item → MatchItemDialog. Pagination with UNMATCHED_PAGE_SIZE.
**Desktop**: ✗ Completely missing
**Severity**: **P0 CRITICAL** — Cannot discover/manage unmatched items

## 27. Stale Media IDs Section
**Webui (lines 1783-1964)**: Section with count badge, search + sortable columns (Title, Year, Library, Provider, Provider ID, First Seen, Last Seen). "Match" button per item → MatchItemDialog. Pagination.
**Desktop**: ✗ Completely missing
**Severity**: **P0 CRITICAL** — Cannot manage stale provider IDs

## 28. Ambiguous Roots Section
**Webui (lines 1131-1274)**: Library dropdown selector, search filter, table: Root (with evidence summary), Type, Confidence, Files, Actions. "Override" button → RootOverrideDialog (lines 1299-1442). Force type/title/year/TMDB/IMDb/TVDB IDs/note.
**Desktop**: ✗ Completely missing
**Severity**: **P0 Functional** — Cannot override ambiguous roots

## 29. Skipped Roots Section
**Webui (lines 1447-1638)**: Search input, SortableHead columns (Item, Library, Reason, Files, First Seen, Last Seen), expandable rows showing full root path (selectable monospace), sample file, count, pagination.
**Desktop (lines 197-242 xaml, 497-612 xaml.cs)**: Static table: Root, Library, Reason, Sample, First Seen, Last Seen. No search. No sorting. No expansion. No pagination.
**Severity**: P1 Functional

## 30-31. Helper Functions

| Helper | Webui | Desktop | Purpose |
|--------|-------|---------|---------|
| formatActiveScanMode | ✓ Lines 814-825 | ✗ | "library" → "Full library scan" |
| formatActiveScanTrigger | ✓ Lines 827-840 | ✗ | Format trigger type |
| formatActiveScanTime | ✓ Lines 842-866 | ✗ | Format elapsed time |
| formatActiveScanProgress | ✓ Lines 868-881 | ✗ | Format file progress |
| formatJobProgress | ✓ Line 119 (imported) | ✗ | Format refresh job progress |
| buildLibraryReorderEntries | ✓ Line 40 (imported) | ✗ | Build reorder payload |
| useSort | ✓ Lines 1110-1127 | ✗ | Manage sort state |
| SortableHead | ✓ Lines 1072-1108 | ✗ | Sortable column header |

## 32. useEventChannel / Realtime Events
**Webui (line 122)**: `useEventChannel("scans")` for live scan updates
**Desktop**: No event subscription; likely relies on polling or manual refresh
**Severity**: P1 Functional

## 33. Status Column: Active Scan Badges
**Webui (lines 418-423)**: Badge 1: Enabled/Disabled; Badge 2 (if running): "{N} running"; Badge 3 (if queued): "{N} queued"; Badge 4 (if empty_root): "Empty root guarded"
**Desktop (lines 215-224)**: Only Enabled/Disabled + empty_root. **Missing running/queued count display**
**Severity**: P1 Functional

---

## Priority Fix List

### P0 (Critical/Blocking)
1. **ScanQueuePopover with realtime monitoring** — Complete absence
2. **Unmatched Items Section** — Essential discovery/management
3. **Metadata Provider Configuration** — Critical library customization
4. **Library Drag-and-Drop Reordering** — UI exists but non-functional (TODO)
5. **Stale Media IDs Section** — Maintenance visibility
6. **Ambiguous Roots Section with Override Dialog** — Troubleshooting

### P1 (Functional Gaps)
1. Active scan count badges in Status column (running/queued)
2. Inline refresh job progress below actions
3. Inline active scans list
4. Cancel Scans button when scans active
5. Realtime event channel integration
6. Skipped Roots: search, sort, pagination, expandable rows

### P2 (Polish/UX)
1. Folder Browser in LibraryForm paths
2. Chapter Thumbnails toggle
3. Metadata Language selector
4. Poster upload/replace/delete with preview
