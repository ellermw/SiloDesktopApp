# CodeRabbit Follow-up Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox tracking so interrupted work can resume without ambiguity.

**Goal:** Resolve every actionable finding in `docs/audit/coderabbit-ef18-follow-up.md`, verify rejected/no-op findings with evidence, and leave the Windows solution passing its complete test and Release build checks.

**Architecture:** Keep behavior fixes inside their existing view models, services, and views; add narrow regression coverage around each defect; harden the three audit scripts around one exact official-origin policy; and pin the search parity contract to a recorded official Silo WebUI commit. Avoid unrelated refactors and installer-policy changes that are not supported by authoritative evidence.

**Tech Stack:** C# 12, .NET 8, WinUI 3, xUnit, PowerShell, Git, official `Silo-Server/silo-server` GitHub main.

**Spec:** `docs/audit/coderabbit-ef18-follow-up.md`

---

### Task 1: Protect collection creation retries

- [x] Add a failing regression test proving successful collection creation transitions immediately to edit state and preserves already-persisted item IDs if a later step fails.
- [x] Update `CollectionEditorViewModel` to save the created ID/edit state before item work and incrementally maintain its persisted-item baseline.
- [x] Run the focused collection tests.

### Task 2: Preserve catalog-total certainty

- [x] Add a failing regression test for a letter jump performed while the current total is estimated.
- [x] Preserve and restore `_hasExactTotal` together with `TotalCount` in `LibraryViewModel.JumpToLetterAsync`.
- [x] Run the focused catalog/library tests.

### Task 3: Prevent duplicate event-channel reconnects

- [x] Add a failing regression test for the `TokenRefreshed` followed by `UserChanged` event sequence.
- [x] Restrict `OnUserChanged` restart behavior to suppressed or stopped runs while retaining the credential-driven forced reconnect.
- [x] Run the focused event-channel tests.

### Task 4: Harden official WebUI audit scripts

- [x] Add regression coverage for exact accepted/rejected GitHub origin forms and validation-before-mutation ordering.
- [x] Make `audit-scan.ps1` always fetch and record official `origin/main`.
- [x] Make `audit-delta.ps1` validate the exact origin before any fetch/merge.
- [x] Make `audit-bump.ps1` fetch, require a clean fast-forwardable checkout, and record the resulting official commit.
- [x] Run script regression tests and PowerShell parse checks.

### Task 5: Restore pinned search parity evidence

- [x] Fetch current official `Silo-Server/silo-server` main and record its commit.
- [x] Inspect the current WebUI search implementation and add a minimal pinned fixture/contract derived from it.
- [x] Update `SearchRuntimeRegressionTests` to compare the desktop behavior contract with that pinned fixture.
- [x] Run focused search tests.

### Task 6: Strengthen download-token and guarded-success tests

- [x] Add a request-contract test rejecting `token` in any query-string position and confirming authorization-header transport.
- [x] Bound admin-user success assertions to the actual non-null guarded blocks.
- [x] Run focused downloads and admin-user tests.

### Task 7: Bound the image-source cache

- [x] Add a failing source/behavior regression test for dead-entry removal and periodic pruning.
- [x] Remove dead entries on lookup and periodically prune dead weak-reference keys.
- [x] Run focused converter tests.

### Task 8: Align applied filters, history progress, and node toggle state

- [x] Add failing regressions for invalid IMDb badges, runtime-based history percentages, and failed node-toggle rollback.
- [x] Build IMDb badges only from validated filter input.
- [x] Resolve history duration once and use it for both display and percentage.
- [x] Restore a failed node toggle under an event-suppression guard.
- [x] Run focused UI/source and view-model tests.

### Task 9: Verify already-correct and policy-dependent findings

- [x] Verify maximum-profile minimum/default/label/server payload alignment and correct the remaining stale defaults form.
- [x] Verify the package publisher identity exactly matches the intended local-testing certificate.
- [x] Leave Windows App Runtime `--quiet --force` unchanged unless authoritative installer validation proves a different policy.

### Task 10: Full validation and audit evidence

- [x] Run the complete test suite.
- [x] Run the Windows x64 Release build/publish checks and PowerShell script parse checks.
- [x] Run repository secret/artifact checks and `git diff --check`.
- [x] Update `docs/audit/coderabbit-ef18-follow-up.md` with per-finding dispositions, exact evidence, and validation results.
- [x] Review the final diff; do not commit, push, publish, or open a PR without a new explicit user instruction.
