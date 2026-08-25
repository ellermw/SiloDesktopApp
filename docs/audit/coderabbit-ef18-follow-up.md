# CodeRabbit Follow-up Audit — `ef18b13`

## Scope

- Reviewed commit: `ef18b137a39ca03779b9fba2c6c084bec70b4476`
- Commit title: `Address full CodeRabbit audit`
- Comparison base: `1ca43e4`
- Review method: CodeRabbit CLI review of all 170 changed files, followed by manual triage against the committed source.
- CodeRabbit result: 17 findings (10 major, 7 minor); no critical findings.

## Summary

The remediation commit materially improves the project, but it should not yet be treated as a clean 184/184 closure. Fourteen of the new findings are actionable, one is a useful test-coverage improvement, and two should not be applied as written.

The highest-priority remaining defects are a possible duplicate collection on retry, loss of catalog-total exactness, redundant event-channel reconnects after token refresh, and source-of-truth weaknesses in the parity-audit scripts.

The original full-audit document is structurally complete: all 184 entries contain a decision, evidence, and validation field. However, 173 entries use the same generic evidence sentence. The document therefore proves that every entry was dispositioned, but it does not independently demonstrate each fix in meaningful detail.

## Major findings

### 1. Manual collection retry can create a duplicate

- **File:** `src/SiloPlayer/ViewModels/CollectionEditorViewModel.cs`
- **Decision:** Valid — fix.
- **Evidence:** After `CreateCollectionAsync` succeeds, `CollectionId` and `IsEditing` are assigned only in the partial item-add failure branch. If item reordering or another post-create operation throws first, retrying enters create mode again and can create a duplicate collection.
- **Required change:** Persist the created collection ID, enter edit mode, and initialize the persisted-item baseline immediately after creation succeeds, before item additions or reordering.

### 2. Estimated catalog total is incorrectly marked exact

- **File:** `src/SiloPlayer/ViewModels/LibraryViewModel.cs`
- **Decision:** Valid — fix.
- **Evidence:** `JumpToLetterAsync` retains `TotalCount` across a sort reset but unconditionally assigns `_hasExactTotal = true`. `TotalCount` can contain `_estimatedTotalItems`, so subsequent loads may stop requesting the authoritative total.
- **Required change:** Capture and restore the previous `_hasExactTotal` value along with the total.

### 3. Event channel reconnects twice during token refresh

- **File:** `src/SiloPlayer.Core/Services/EventChannelClient.cs`
- **Decision:** Valid — fix.
- **Evidence:** `TryRefreshAsync` raises `TokenRefreshed` and later `UserChanged`. `OnTokenRefreshed` already restarts the socket, while `OnUserChanged` calls `EnsureRunning_NoLock(forceReconnect: true)` again. This can abort the newly established run, request another ticket, and replay channel snapshots.
- **Required change:** Let `OnUserChanged` restart only a suppressed or stopped run. Preserve the forced reconnect in `OnTokenRefreshed`, where the credential actually changes.

### 4. Audit scan can use a stale local source commit

- **File:** `scripts/audit-scan.ps1`
- **Decision:** Valid — fix.
- **Evidence:** Without `-Pull`, source metadata is taken from local `HEAD`; it does not establish that the checkout represents current `origin/main`. This conflicts with the repository rule requiring the current official main commit for parity work.
- **Required change:** Fetch official `origin/main`, verify ancestry/cleanliness as appropriate, and record the fetched remote commit used for the scan.

### 5. Audit remote matching is not exact

- **Files:** `scripts/audit-bump.ps1`, `scripts/audit-delta.ps1`, `scripts/audit-scan.ps1`
- **Decision:** Valid — fix.
- **Evidence:** The current regular expression is end-anchored but not start-anchored and can accept a URL with an arbitrary prefix that happens to end in the official repository spelling.
- **Required change:** Accept only explicitly supported, fully anchored HTTPS and SSH forms of `Silo-Server/silo-server`.

### 6. Delta script validates the source after optional network mutation

- **File:** `scripts/audit-delta.ps1`
- **Decision:** Valid — fix.
- **Evidence:** With `-Pull`, the script fetches/merges before verifying that `origin` is the official repository.
- **Required change:** Validate the resolved repository and exact origin URL before any fetch or merge.

### 7. Bump script records local rather than current upstream state

- **File:** `scripts/audit-bump.ps1`
- **Decision:** Valid — fix.
- **Evidence:** The script validates the origin but does not fetch and fast-forward main before generating baseline metadata.
- **Required change:** Fetch the validated official remote, require a clean fast-forwardable checkout, and record the resulting official commit.

### 8. Search parity test no longer compares with an official fixture

- **File:** `tests/SiloPlayer.Tests/SearchRuntimeRegressionTests.cs`
- **Decision:** Test-coverage improvement — address or document.
- **Evidence:** Assertions exercise the local implementation but no longer directly compare the relevant behavior with a pinned official WebUI fixture. A local regression can therefore satisfy a local expectation without detecting parity drift.
- **Required change:** Restore a pinned official fixture/comparison where practical, recording its source commit. If intentionally replaced, document what equivalent contract test now proves parity.

### 9. Windows App Runtime `--force` recommendation

- **File:** `installer/SiloInstaller.iss`
- **Decision:** Do not apply without installer-specific validation.
- **Evidence:** CodeRabbit suggested removing `--force`, but the original audit remediation explicitly required moving installation into checked code while preserving `--quiet --force`. Commit `ef18b13` implements that requested behavior and checks both process launch and exit code.
- **Required change:** None based solely on this follow-up. Decide installer policy from official runtime-installer behavior and supported upgrade/recovery scenarios.

### 10. Package publisher identity

- **File:** `src/SiloPlayer/Package.appxmanifest`
- **Decision:** False positive/no-op.
- **Evidence:** The manifest already contains `Publisher="CN=ellermw Silo Desktop Player Local Testing"` exactly as the finding requests.
- **Required change:** None.

## Minor findings

### 11. Download token regression test is too narrow

- **File:** `tests/SiloPlayer.Tests/DownloadsCurrentParityTests.cs`
- **Decision:** Valid — strengthen test.
- **Evidence:** The test focuses on the literal `?token=` form. It may miss a token added as a later query parameter or token transport through another runtime path.
- **Required change:** Assert the complete request-building contract and reject token query parameters regardless of position.

### 12. Image-source cache retains unbounded keys

- **File:** `src/SiloPlayer/Converters/UrlToImageSourceConverter.cs`
- **Decision:** Valid — fix.
- **Evidence:** Values are weak references, but dead entries and their URL strings remain in the static `ConcurrentDictionary` unless an exception happens on that key. Long-running browsing sessions can grow the key set indefinitely.
- **Required change:** Remove dead entries on lookup and/or add bounded periodic pruning.

### 13. Admin-user source tests do not prove success calls are guarded

- **File:** `tests/SiloPlayer.Tests/AdminUsersParitySourceTests.cs`
- **Decision:** Valid — strengthen test.
- **Evidence:** Broad source-text assertions can pass while a success notification or follow-up call sits outside the required non-null branch.
- **Required change:** Use behavioral tests where possible, or at minimum bound source assertions to the guarded block.

### 14. Invalid IMDb input can produce an active filter badge

- **File:** `src/SiloPlayer/Views/CollectionBrowsePage.xaml.cs`
- **Decision:** Valid — fix.
- **Evidence:** Badge construction can reflect non-empty IMDb input even when parsing rejected it and no effective filter rule was added.
- **Required change:** Build the badge from the validated/applied filter state, not raw text input.

### 15. Maximum-profiles editor default conflicts with its label

- **File:** `src/SiloPlayer/Views/Admin/AdminUsersPage.xaml.cs`
- **Decision:** Valid — fix.
- **Evidence:** The numeric control permits/defaults to a value inconsistent with the displayed minimum/default policy.
- **Required change:** Align `Minimum`, `Value`, label text, and server contract; CodeRabbit recommends minimum 1 and default 5.

### 16. History progress ignores the runtime fallback

- **File:** `src/SiloPlayer/ViewModels/HistoryViewModel.cs`
- **Decision:** Valid — fix.
- **Evidence:** Displayed duration falls back from `DurationSeconds` to runtime minutes, but `ProgressPercent` only uses `DurationSeconds`. Entries with runtime metadata and no duration can display a duration with zero progress.
- **Required change:** Calculate display duration and percentage from the same resolved duration value.

### 17. Failed admin-node toggle leaves misleading UI state

- **File:** `src/SiloPlayer/Views/Admin/AdminNodesPage.xaml.cs`
- **Decision:** Valid — fix.
- **Evidence:** On a failed enable/disable request, an error is shown but the switch is not restored to the server-backed value.
- **Required change:** Roll back the switch under an event-suppression guard or reload authoritative state after failure.

## Verification performed in this environment

- Local `main` and `origin/main` both resolved to `ef18b137a39ca03779b9fba2c6c084bec70b4476` before this report was added.
- The worktree was clean before this report was added.
- `git show --check ef18b13` passed.
- No compiled artifacts were introduced by `ef18b13` based on the changed-file list.
- The CodeRabbit CLI completed review of all 170 changed files.

The reported Windows x64 Release build and 800/800 test pass could not be independently reproduced in this Linux review environment because neither `dotnet` nor PowerShell is installed. Those results should remain attributed to the development session that ran them.

## Recommended completion criteria

1. Resolve or explicitly reject each finding above with concrete file/line evidence.
2. Add regression tests for the collection retry, catalog-total exactness, and event-channel reconnect cases.
3. Run the Windows x64 Release build and complete test suite.
4. Rerun CodeRabbit against the remediation commit.
5. Update this report with the resulting commit and validation evidence rather than replacing it with generic disposition text.
