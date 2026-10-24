# Library filters return after clearing — October 24, 2026

## Report and reference

The owner reports that Adult Scenes restores unwanted filters whenever its Library tab is reopened, even after clearing them. Baseline is the owner-tested, published 1.2.309 at `47f7e6f8f6070feca99fe9d45b0c078065129fa5`.

Official Silo main was fetched directly from `https://github.com/Silo-Server/silo-server.git` for this focused comparison: `d2c12dbb0f8f40f0a33304f3032f2104c5ee9674`. The WebUI's `web/src/pages/LibraryPage.tsx` updates canonical library search state on query changes, then persists that state through `web/src/hooks/queries/libraryPageState.ts`. Remembering intentionally selected filters is expected; restoring filters the user already cleared is a defect.

## Reproduction and cause

`LibraryFilterClearNativeFixture` mounts the real published WinUI Library page against an isolated in-memory HTTP transport. It seeds two saved filters, invokes the actual Clear All or individual chip buttons, allows the existing save debounce to settle, navigates away and creates a fresh page. It uses synthetic media only; no installed player, real account settings or production data are accessed.

The complete red run against the frozen 1.2.309 payload is `.codex-tmp/library-filter-clear-oct24/baseline-red3/results.txt`:

| Restored filter form | Action | Save writes after removal | Cleared filters restored on reopen |
|---|---|---:|---:|
| Grouped rules | Clear All | 0 | 2 |
| Grouped rules | Individual chips | 1 | 0 |
| Guided/legacy fields | Clear All | 0 | 2 |
| Guided/legacy fields | Individual chips | 0 | 2 |

Both `ClearAllFilters_Click` and the guided `CreateFilterBadge` click handler changed the live filter state and reloaded catalog results, but omitted `SaveViewState`. Their initialization save had already completed, so no pending save contained the cleared state. Reopening correctly read the old stored document, causing the apparent preselection to return. Grouped chip removal already calls `ApplyAdvancedRulesAsync`, which saves the new state and provides a working comparison.

The first harness compilation lacked a Helpers import; the next run exposed cross-scenario fixture setup interference from a pending initialization save. Those harness issues were corrected before the complete red run. Earlier evidence directories are preserved. No product repair was made before the complete red run.

## Repair

Add `SaveViewState(_currentTab)` to both missing removal paths immediately after the filter model is cleared and before awaiting catalog reload. The existing debounce and navigation flush persist the latest snapshot using the existing profile/device-scoped store. No global reset, default-filter override or settings migration is added. Other libraries' saved state and intentionally selected filters remain respected.

Version 1.2.310 counts this as one correction group beyond 1.2.309. The engine, playback implementation and XAML layout are unchanged. Source was left uncommitted for owner review; the owner subsequently authorized publication to main with minimal README changes. Final verification and installer hashes are recorded in [the release record](../releases/1.2.310.md) and the local receipt.
