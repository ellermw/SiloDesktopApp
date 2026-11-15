# Admin Configure Delivery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Each delivery group has its own requirements brief and verification record.

**Goal:** Implement every official Admin Configure page and nested workflow, delivering verified, independently testable Beta PRs as groups become ready.

**Architecture:** Extend the registered-page Admin shell and authority, leave-guard, validator and bounded-data contracts from PR167. Apply PR171's full-width scrolling contract where appropriate. Keep domain APIs and controllers separate from Monitor APIs, and keep personal Settings separate from server configuration.

**Tech Stack:** Native WinUI 3, .NET 10, existing Core HTTP transport and native regression host.

**Spec:** Owner's November 15 Configure request and the recovered source inventory at `D:/SiloPlayer-build/server-update-nov14/configure-scope/configure-scope-audit.md`; official upstream `c11b0a968f0d5980b7005579fb95acc2d11d8321`.

## Global Constraints

- Refresh official main and Beta/PR states before selecting or reconciling source. Read explicit upstream SHAs; the clone's detached HEAD is historical.
- Preserve installed player, account/settings values, unrelated checkouts, memories and historical receipts.
- Keep page navigation without slides. Register production pages only when their whole route and workflows work.
- At least 50 GiB free on every used drive; at most 10 GiB active generated output across workers. Serialize .NET and native work, including candidate publish/host/fixture binding.
- Reads have finite deadlines and response/list bounds, share when appropriate, cancel on retirement/departure, and yield to foreground navigation/playback. No whole-library prefetch or hidden-page polling.
- Match official functionality, labels, typography, spacing, scrolling, responsive layout, accessible names and hover/focus/pressed/disabled states. Verify actual native templates so local theme styles cannot add unwanted oval backdrops.
- Scoped commits/pushes and qualifying non-draft same-repository PRs into beta are authorized. Each PR must stay within 200 changed files and 250 commits relative to Beta. Link immediately through T3 after creating/pushing a PR.
- Pipeline owns review/fixes/reconciliation/merge/stamping/packaging. No manual Beta merge, Stable publication, install or installed-player control.
- Done means verified and pushed to a PR with its link. Keep merged/released status separate. A foundation or inventory does not finish a Configure leaf.

## Review Focus

- A late response or retained action after account/profile/page replacement must not publish private data or issue a mutation for a replacement owner.
- A timeout or ambiguous successful mutation response must not cause an automatic replay; preserve draft/uncertainty and require explicit reconciliation where appropriate.
- A missing/weak ETag, redacted credential, failed credential-status read or environment-managed value must never become an invented writable baseline.
- Narrow layouts and scrolling must preserve readable content and keyboard access; control template states must retain the intended rectangular/transparent shape and visible focus.
- Partial or inconsistent configuration lists cannot silently authorize destructive whole-set writes; enrollment URLs and credentials must never enter logs or receipts.

## Delivery sequence

- [x] Recover the interrupted audit's existing extracts and thread evidence; produce the complete route/API/UI-state matrix and machine-readable checklist. The result tracks 19 leaves, 88 nested feature entries and three descendant groups (four URL patterns). The audit alone finishes no leaf.
- [x] Reconcile Beta `7e62ef4c562b88d0b16cc4025b3fc74c23a9264e` with PR167 `66926757ff32cd53d92b5e15e8ad89531e381e66` and PR171 `3331916bbf295808fd6169216a67d721e0b70c1e`. Integration source `5cc7a0da3a17906a1cafce81c70d0ae42f01c88a`; 508 focused tests pass. Preserve both development-log records and both shell behaviors.
- [x] Select and write a concrete brief for General plus the reusable guarded settings editor: `D:/SiloPlayer-build/server-update-nov14/configure-scope/general-implementation-brief.md`. Source audit section 5 supplies detailed layout and shared settings contracts.
- [x] Implement that brief with failing behavioral tests first, then focused/full units and actual published native controls. Keep one writer per checkout and one implementation assignment at a time. Final General code c0a3a2cc; native General/Manage foundation/Monitor/Dashboard pass on candidate m. Main/Core unit source remains unchanged after the 6,980-test full run; final focused run passes 525 tests.
- [x] Independently review the actual final diff against official source and repository standards. Claude review at bad7dc82; lead fixed Redis erasure, indicators, live-region events and Reload focus. Final code54afa5d4 includes refreshed Beta a06cc8b3 and passes7,019 full units,564 focused tests, Release publish and General/foundation/SubtitleFiles/Monitor/Dashboard native checks.
- [x] Record exact source/payload/fixture hashes, tests, captures and limits. Qualifying non-draft Beta PR173 is pushed and linked through T3; 35 files/43 commits at initial submission. General is Done, separately unmerged/unreleased.
- [x] Deliver Email (C2a) as independent non-draft Beta PR174 on General PR173. Final code 3ab6d459 passes 7,071 full units, 616 focused tests, Release publish/matching host and six native groups; Email records 297 PASS including the lead’s red/green authority-retirement password regression. General and Email are Done by delivery definition, separately unmerged/unreleased.
- [ ] Continue Security (C2b) using the completed security-implementation-brief.md and the final Email dependency. Track all remaining 17 leaves and nested functionality. Do not wait for the whole section before delivery.

## Execution rulings

The owner explicitly asked to continue autonomously into grouped implementation and authorized scoped commits, pushes and Beta PRs. That supplies the execution method and removes intermediate design/plan confirmation gates. Standard architecture choices remain the implementer's responsibility. The recovered audit is a source inventory, not evidence of runtime visual parity.
