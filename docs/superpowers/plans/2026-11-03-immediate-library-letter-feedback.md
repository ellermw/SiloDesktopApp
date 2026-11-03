# Immediate Library Letter Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. No implementation subagents; parent owns reviews and delivery. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Acknowledge the latest alphabet choice immediately and resolve it safely with scoped Retry, including the inherited final-letter defect.

**Architecture:** Keep the letter operation and its immutable ownership in `LibraryViewModel.Letters.cs`; expose its pending choice/failure through observable state. The page renders that state and owns native scroll intent; explicit manual movement retires the operation while automatic render requests remain suppressed. Keep existing catalog paging, validation and bounded caches.

**Tech Stack:** C#/.NET 8, WinUI 3, CommunityToolkit.Mvvm, xUnit, synthetic native regression fixture.

**Spec:** `docs/superpowers/specs/2026-11-03-immediate-library-letter-feedback.md` (read with the owner-approved parent browsing spec).

## Global Constraints

- Historical TEMPORARY STORAGE HOLD was lifted explicitly November 3; parent owns deletion/pruning. Keep source/binary fingerprints and compact evidence before superseded full binaries are pruned.
- Source remains `1.2.544-beta.1`, numeric assembly/file version `1.2.543.1`; pipeline owns versions/main/stable.
- All .NET commands through parent Invoke-Verification.ps1 (SiloOct18Build, pinned mpv verification, fresh outputs), freeze source while commands queue/run.
- No subagents/push/PR/source-control destructive actions/installed-app/global-input/real accounts.
- Do not begin Task 4b retained activation or change loaders/dashboard/playback.

## Review Focus

- Explicit manual scroll and automatic viewport callbacks must have different ownership effects.
- Queued UI dispatch after newer clicks, query changes or navigation must remain retired.
- Same-letter repeated activation must create a new operation despite identical label/position.
- Partial pages and a shrinking exact count must not bind an absent target or repeatedly use its old index.
- Native `ChangeView` observation and rounding must preserve a deliberate final-letter choice without weakening the bottom assertion.

### Task 1: Verify symptom and ownership regressions

**Files:** `tests/SiloPlayer.Tests/LibraryLetterNavigationTests.cs`, `tests/SiloPlayer.NativeRegressionTests/LibraryLettersNativeFixture.cs`, `tests/SiloPlayer.NativeRegressionTests/Program.cs`.

**Interfaces:** Keep existing `JumpToLetterCommand.ExecuteAsync(string)`, `LetterJumpRequested`, `IsLetterJumpInProgress`, `GetWindowItem(int)` test seams. New native route `library-letter-feedback` invokes actual page buttons; original `library-letters` retains its assertions and receives timeout diagnostics only.

- [x] Write held native pending check: force M target read to wait; before release require `Loading titles` button help, visible `LibraryLetterStatus` naming M, enabled repeated activation and exact position/card identity.
- [x] Write xUnit failure isolation, stale-index retry and profile/language/filter/library/departure late-response cases against real viewmodel and synthetic HTTP.
- [x] Add bottom-timeout diagnostic for native/logical offset, extent, viewport, pending/chosen/active fields and target presence; preserve both original final-Z assertions.
- [x] After explicit parent lift, request smallest focused unit run plus existing baseline app `library-letters` native diagnostic and held `library-letter-feedback` red run through parent runner. No product changes until intended assertion failures are observed; distinguish fixture setup failures.
- [x] Add controlled independent A/B and repeated-M transport gates, reversed completion/failure, Retry and actual manual-scroll delayed-failure native cases before each corresponding correction. Assert latest help/status/native offset, no global error and current card identities; release all gates in `finally`.

### Task 2: Implement current-operation feedback and safe completion

**Files:** `src/SiloPlayer/ViewModels/LibraryViewModel.Letters.cs`, letter-specific call boundaries in `LibraryViewModel.cs`, `src/SiloPlayer/Views/LibraryPage.Letters.cs`, letter-specific scroll/navigation hooks in `LibraryPage.xaml.cs`, compact feedback in `LibraryPage.xaml`.

**Interfaces:** Expose `PendingLetter` and `LetterJumpError` observable strings and `RetryLetterJumpCommand`; keep existing public successful `LetterJumpRequested(int)` semantics. Add `RetireLetterJump()` for page-owned manual/navigation retirement. Internal operation captures query key, index, context/language and generation; completion must test that owner before any publication/restoration. Pending state is published before forcing the target request. Retry captures the current failed choice and revalidates a known stale optional index.

- [x] Use verified diagnostic to correct the final-letter ownership cause; rerun unchanged combined bottom/current assertion plus stability and disjoint repeated-run checks.
- [x] Implement immediate observable selection and compact status/Retry using `Loading M…`, `Loading titles` help and concise failure copy. Trigger UI update directly on owning choice, not after a card publication.
- [x] Keep global catalog errors separate from letter errors; preserve ordinary viewport cards on failure only while their captured owner is still current. Preserve existing count/cursor validation and latest window request rules.
- [x] Add page manual-scroll ownership seam that retires pending jump/restoration, then immediately permits the new visible range to load. Programmatic jump/extent callbacks must not retire their own operation.
- [x] Add current-query/context/language guards around target card publication and every queued page callback. Clear pending/error/retry authority on reset, departure and all owner changes.
- [x] Run focused red/green checks after each correction through parent runner. Re-run held failure after manual scroll to confirm no snap-back and inspect inert placeholder/current-card authority with actual controls.

### Task 3: Verify, self-review and commit focused result

**Files:** all Task 1/2 inputs, development record and external `letters-task-report.md`.

- [x] Review load/state/callback ownership from command entry through transport response, page dispatch, native scroll completion and card binding. Review stale errors/Retry and every cancellation/restore branch.
- [x] Parent runner: focused unit checks; complete main unit suite; fresh Release publish; native `library-letter-feedback`, existing `library-letters`, affected `library-cursor`. Every selected native route must log `PASS: native regression run completed.`.
- [x] Unload all fixture-owned visuals before restoring/disposal of their synthetic provider; capture original/final fixture and product/artifact fingerprints, exact source and honest verification limits.
- [x] Confirm unchanged versions, clean diff checks, no other features. Update external report and development record; commit locally once authorized verification passes. Return clean exact head and evidence to parent; no push/PR.

Prep self-review: each 4a requirement maps to Task 1/2 tests or Task 3 verification. Task 4b is intentionally absent. Actual red/green results and runner environment ruling are maintained in the external `letters-task-report.md`; unchecked verification steps remain open until their corresponding final evidence is read.

## Task4a fix round1/5

Review base `adbef50a9b033014ff81d1ac0af414f2c21d5838`: Standards0 findings; Spec2 P2 findings. Corrections preserve generic cursor recovery while revalidating current semantic letter intent after signed-scope retirement, and retain complete authority through both native scroll queues. Snapshots for possible restoration are captured afresh after scope reset. Automatic visible-range requests share one page boundary that respects scoped Retry until manual/new intent retires it.

Covering tests: `LibraryCursorScopeTests.cs` (moved M900, scoped failure/Retry720, missing-target old-card retirement, reversed old-scope completion and five revalidation retirements); `LibraryLettersNativeFixture.cs` (actual signed outcomes, settled200ms/fill/resize, first/second queue interleaving for newer M/query/manual0.5/cached visit). Original letter/end-clamp assertions are retained. Runner `review-focused-final`:115 passed; `review-r1-final-full`:3466 passed. Fresh publish and five selected native groups pass on appD9B9F81F... with completion sentinels. Exact commands, red/inconclusive setup evidence, source/PDB hashes and final commit are in the same external report and `letters/review-r1-bindings.json`. Two corrections implemented; parent owns scoped re-review.
