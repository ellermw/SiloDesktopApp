# Immediate Library letter feedback (Task 4a)

This focused slice implements requirement 4 of the owner-approved `2026-11-02-browsing-responsiveness-design.md`, supplied in the sibling episodes worktree. Safe retained activation is Task 4b and is excluded here. Parent owns independent review and Beta delivery.

Baseline: `origin/beta` / `ac51554902c252814191dd88cf4b1d991efdfea6`, branch `codex/nov02-letter-feedback`. Source remains `1.2.544-beta.1`, numeric assembly/file version `1.2.543.1`. Official server main supplied by parent for this pass is `bb1ee4352b1bec38b4a6441ba81e61a92da81d54`; its only delta from the previously inspected `391e90910e59b864bf4663dd9c5a10a95a21e5c8` is scanner hidden-folder handling. No new server contract is proposed.

## Behavior

An enabled letter activation immediately selects that choice and provides visible, accessible pending feedback before any forced target read completes. Its button help is `Loading titles`; a compact `LibraryLetterStatus` text names the choice (`Loading M…`). The button remains enabled for repeated activation. Existing letter counts, single tab stop, keyboard movement and native Enter/Space behavior remain available.

The existing viewport can remain usable while the response is held. If the page moves before completion, unresolved target slots are inert placeholders. An old nearby card must never appear as a clickable target item. No new loader, dashboard, cache layer, playback, renderer, version or retained-settings behavior is included.

Successful completion navigates to the exact filtered position, clamped only by native viewport bounds, and clears pending feedback. The latest click wins, including repeated clicks of the same letter. Descending order, advanced filters and disjoint repeated letter runs retain their current server-defined positions. At the clamped end, the deliberately chosen final letter remains current although the top visible row contains an earlier run.

A failed target read or a now-missing target reports a scoped concise failure with Retry; ordinary browsing remains usable and `ErrorMessage` does not turn it into a global catalog failure. Retry owns the captured current choice and query. A missing/stale target invalidates and re-reads the optional index before trying again, rather than looping an obsolete cached position. Unsupported optional indexes retain ordinary browsing without guessing a target.

Every pending selection, target completion, error, Retry and potential restoration belongs to the current letter operation, query, library, API context, metadata-language generation and navigation visit. A newer click, explicit manual scroll, filter/sort/library/profile/language change or departure retires earlier work. A failed old operation must not restore its captured windows or scroll offset over a newer manual scroll. Automatic viewport refreshes caused by rendering the still-visible page do not masquerade as manual intent and cancel a held letter read.

Use existing bounded metadata and cursor/server validation. Keep the existing cache ceilings (eight recent windows / 1,200 items) and bounded realization. Current item authority must be verified at card binding and at dispatched callbacks, not inferred from a letter label.

## Known baseline and diagnostic gate

Frozen `artwork/baseline-library-letters` and `artwork/regression-library-letters` both time out on the original final-Z assertion. The baseline receipt binds unchanged `ac515549` to app SHA256 `528721D32A1892012AB208EED8D11E635B5B431BADABE9D6D27940D1E8F1D7FA`. Its catalog log includes successful seeks at 600 and 960 followed by viewport fetch 936; it does not include the actual offset or active-letter snapshot.

Source hypothesis: after `ChangeView`, `ApplyPendingLibraryOffset` immediately republishes `VerticalOffset`; if that still reports the preceding position, `UpdateActiveLibraryLetter` clears `_chosenLibraryEndLetter` before the actual bottom transition. Native diagnostics must establish the offset/extent/logical-offset/selection state before a product correction. Preserve the combined bottom-offset/current-Z assertion and its later stability assertion unchanged.

## Verification and hold

The historical temporary hold below was explicitly lifted by the owner/parent on November 3 after bounded cleanup. Current policy keeps the required baseline/current candidate, compact logs/TRX/screenshots/PDB and source/binary hashes; parent may prune superseded full binaries after bindings are recorded. Use the parent runner's corrected short D: process-local TEMP/TMP path; do not change updater assertions or code for runner path failures.

TEMPORARY STORAGE HOLD: owner queried111.9GBC/260GBD. Root read-only inventory confirmed105.2GiBCworktrees and266.3GiBD.codex-tmp, no deletion. Pending explicit retention-choice approval. Until parent explicitly lifts build hold: source analysis and writing focused tests/spec/plan only; NOdotnet/build/test/publish/native/installer commands, no artifact copies/deletion, no product fixes before intended red. Use existing baseline logs for the known Z-bottom failure and identify the actual cause, but don't claim a new empirical red from source alone. Report prep status/next minimal checks to parent, wait for lift before implementation/testing. No unrelated cache cleanup or filesystem changes.

After the lift, use only parent `Invoke-Verification.ps1`, pinned mpv validation and `SiloOct18Build`, fresh task-owned D: outputs and frozen source while queued/running. Synthetic HTTP, accounts and fixture windows only; no installed app, global input, real settings or credentials. Preserve original/final fixture hashes and every red/green receipt. Native success requires literal `PASS: native regression run completed.`. Unload owned visuals before disposing their provider.

Required evidence: held actual-control pending choice/accessibility; target success/failure/Retry; missing-index revalidation; A/B reversed completion; same-letter repeat; manual scroll during delayed failure; query/profile/language/library/departure retirement; current card/placeholder authority; existing end-clamp and letter layouts at 1280×1000 / 960×550; affected cursor checks. Focused/full unit suite and fresh Release publish/native checks bind exact product inputs and artifact hashes. Synthetic results do not certify physical input or complete visual parity.
