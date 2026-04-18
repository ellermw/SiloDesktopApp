# Admin Sections — Full Audit

Webui AdminSections.tsx (784 lines) + sections.ts hooks (117-246).
Desktop AdminSectionsPage.xaml (223) + .xaml.cs (803) + AdminSectionsViewModel.

## Critical Headlines
- Webui uses **dnd-kit drag-drop**; desktop uses **move-up/move-down buttons** (P0 UX model gap)
- Desktop form **missing collection picker, library multi-select, media scope, collection rules editor** (P0 functionality)
- Desktop BuildCreateBody **doesn't create config payload** (P0)
- Desktop SectionTypeLabels **missing "recommended_for_you"** (P1)
- Collection badges show generic "Collection" text instead of actual name (P0)

## Section 1: Imports & Dependencies
Webui: dnd-kit, CollectionRulesEditor, LibraryMultiSelect, ConfirmDialog, lucide-react. **Desktop has no dnd-kit equivalent.**

## Section 2: Helper Functions
| Webui | Desktop | Gap |
|-------|---------|-----|
| getSectionCollectionId (73-76) | — | — |
| getSectionFilterLibraryIds (78-80) | — | — |
| getSectionMediaScope (82-84) | inline | **P1** |
| formatCollectionOptionLabel (86-92) | — | **P1** |
| LibraryPicker (94-120) | ComboBox | pattern |
| — | GetConfigString (347-353) | — |
| — | GetConfigLibraryIds (355-377) | — |
| queryDefinitionFromSectionConfig | **MISSING** | **P0** |
| queryDefinitionToSectionConfig | **MISSING** | **P0** |

## Section 3: SortableRow vs BuildSectionRow
Webui: useSortable hook, CSS transform, GripVertical icon active/inactive states (122-215).
Desktop: manual move-up/move-down buttons (189-343, 213-218), Grid layout.
**P0 CRITICAL** — fundamentally different UX paradigm.

## Section 4: Drag Overlay
Webui: DragOverlayRow (217-227) visual preview during drag.
Desktop: none (not applicable to button model).
**P2** — visual polish missing.

## Section 5: Main Component State
Webui (229-281): scope, selectedLibraryId, dialogOpen, editingSection, orderedSections, activeId, confirmDeleteSection, confirmRestoreOpen, resetProfiles + effects.
Desktop (13-27, 45-58): _suppressPickerChange, _currentScope, _rebuildPending; VM-backed properties; ScheduleRebuild pattern (60-68); no canDrag flag.
**P1** — state management pattern differs.

## Section 6: Scope Tabs & Library Picker
Webui Tabs + LibraryPicker custom (451-473).
Desktop manual button styling + ComboBox (xaml 119-165; code 73-151).
**P2**.

## Section 7: Table Structure & Columns
Both 7-col (blank/Title/Type/Items/Featured/Enabled/Actions). Webui shadcn Table, desktop Grid. Column widths semantically identical.
**P1 MEDIUM** — same structure, different primitives.

## Section 8: Type Column Badges
Both show Type + media scope + library filter + collection. Desktop shows generic "Collection" text instead of actual name.
**P2 LOW-MEDIUM** — display bug.

## Section 9: Featured Column
Both show yellow star if featured=true. Match.

## Section 10: Enabled Column
Both show "On" (filled) / "Off" (secondary) badge. Match.

## Section 11: Actions Column (Edit/Delete)
Both ghost 28px buttons with edit (Pencil) + delete (Trash2) icons. Match.

## Section 12: Move Up/Down Buttons
**Desktop-only** — MakeSmallIconButton "\uE70E" / "\uE70D" invoking MoveSectionAsync(section, ±1).
Webui uses drag-drop exclusively.
**P0 CRITICAL** — incompatible interaction models.

## Section 13: Empty State
Both: "No sections configured for {scope} scope." Match.

## Section 14: Reorder Hint
Webui: "Drag and drop sections to change their order."
Desktop: "Use the arrow buttons to change section order."
**P1** — hints reflect interaction model.

## Section 15: Delete Confirmation Dialog
Functionally identical. Match.

## Section 16: Restore Defaults Dialog
Both have reset_profiles toggle, same labels, same text, same behavior. Match.

## Section 17: Section Form — Title & Type
Both have Title input + Type selector. Desktop SectionTypeLabels (20-31) **missing "recommended_for_you"**. **P1**.

## Section 18: Section Form — Item Limit
Both use number input (min=1, max=100, default=20). Match.

## Section 19: Section Form — Featured & Enabled Switches
Both have label + switch side-by-side. Match.

## Section 20: Section Form — Collection vs Filter Selection
Webui shows:
- Collection picker if type=collection (678-702)
- Type + LibraryMultiSelect if not filter type (705-748)
- CollectionRulesEditor if filter type (genre/custom_filter) (753-764)
Desktop: **NONE of these exist.** Form only has title/type/limit/featured/enabled.
**P0 CRITICAL** — form incomplete.

## Section 21: Section Form — Submission
Webui handleSubmit (584-607): builds `configPayload` via queryDefinitionToSectionConfig or `{library_collection_id}`, includes scope + library_id.
Desktop GetBody (657-669): calls BuildCreateBody(title, type, limit, featured, enabled) — **no config object**, no scope/library_id in body.
**P0 CRITICAL** — sections created without filter/collection config.

## Section 22: Dialog Trigger
Webui Dialog with DialogTrigger + dynamic title (Add/Edit).
Desktop OpenCreateDialogAsync / OpenEditDialogAsync (446, 476).
**P1 MEDIUM** — pattern differs, functionally equivalent.

## Section 23: Header & Buttons
Title + subtitle + "Restore Defaults" + "Add Section" buttons. Same layout. **P2**.

## Section 24: Drag Handlers & Reorder Logic
Webui (291-318): handleDragStart/End/Cancel → arrayMove → mutation.
Desktop (ViewModel 113-132): MoveSectionAsync → ObservableCollection.Move → ReorderSectionsAsync.
Both hit same API endpoint but **P0 CRITICAL** — interaction models diverge.

## Section 25: Section Type Labels
Webui (sectionTypes.ts 1-12): 10 types incl. "recommended_for_you".
Desktop (20-31): 9 types, **missing "recommended_for_you"**.
**P1 HIGH** — form crashes if server returns this type.

## Section 26: Query Definition Helpers
Webui api/types.ts 2115-2156: queryDefinitionFromSectionConfig + queryDefinitionToSectionConfig.
Desktop: **NONE.**
**P0 CRITICAL** — no bidirectional config conversion.

## Section 27: Badge Helpers
Webui inline Badge component.
Desktop: MakeSecondaryBadge / MakeOutlineBadge / MakeFilledBadge (676-738). Match.

## Section 28: Action Button Helpers
Both create ghost 28px icon buttons. Match.

## Section 29: Status Banner
Webui: toast (Sonner) at 387, 392.
Desktop: inline banner (790-802) with 4s auto-hide.
**P1 MEDIUM** — different UX pattern.

## Section 30: Hooks & Async Patterns
Webui: React Query hooks.
Desktop: RelayCommands + Task methods.
Desktop has **extra** ToggleEnabledAsync (134-157) not in webui.
**P1** — pattern differs.

---

## Priority Fix List

### P0 — Critical / Blocks Functionality
1. **Implement complete section form** — collection picker, library multi-select, media scope, collection rules editor (BuildSectionForm lines 534-671).
2. **Construct config payload** — Desktop's BuildCreateBody doesn't produce config object (lines 657-669).
3. **Drag-drop reordering** OR mark as intentional design decision. Fundamental UX gap.
4. **Collection badge shows actual name** — load collection map, display title instead of "Collection" (line 269).
5. **Add queryDefinitionFromSectionConfig + queryDefinitionToSectionConfig helpers.**

### P1 — High
1. Add "recommended_for_you" to SectionTypeLabels (line 20-31).
2. Load collections cache in VM, create lookup map for display.
3. Initialize queryDefinition from existing config on edit (so filter/collection sections can be re-edited).

### P2 — Medium
1. Consider toast notifications for parity (vs inline banner).
2. Drag visual overlay feedback (if/when drag is added).
3. Align reorder hint text with actual interaction model.

## Summary

| Feature | Webui | Desktop | Gap |
|---------|-------|---------|-----|
| Drag-drop reordering | ✓ | ✗ (arrow buttons) | **P0** |
| Move-up/down buttons | ✗ | ✓ | — |
| Collection picker | ✓ | ✗ | **P0** |
| Library multi-select | ✓ | ✗ | **P0** |
| Filter rules editor | ✓ | ✗ | **P0** |
| Config serialization | ✓ | ✗ | **P0** |
| Collection name badges | ✓ | generic text | **P0** |
| Section type labels | 10 | 9 | **P1** |
| Form state mgmt | hooks | VM | pattern |
| Status feedback | toast | banner | **P2** |

4 CRITICAL P0 functionality gaps + 3 HIGH P1 missing features.
