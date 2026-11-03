# Browsing Responsiveness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver focused locally verified Beta PRs for episode navigation, sidebar publication and library responsiveness.

**Architecture:** Retain native WinUI, current renderer and service authority. Improve the input/publication/scheduling seams independently; use separate D: branches so each PR is independently reviewable against fresh Beta.

**Tech Stack:** C#/.NET 8, WinUI 3, existing unit and isolated native regression hosts.

**Spec:** `docs/superpowers/specs/2026-11-02-browsing-responsiveness-design.md`.

## Global Constraints

- Beta base initially `6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af`; refresh before each new branch and preserve concurrent heads.
- Keep versions unchanged; no main merge, release, installed-app or production control.
- Hold `SiloOct18Build` for every .NET invocation, verify pinned dependencies first, freeze source during each run, use new outputs.
- No Granary source/asset copying. No new startup/playback loader or admin dashboard.

## Review Focus

- Fast native wheel events must not fall into horizontal inertia after a routed handler appears correct.
- Card paging at narrow widths, partial offsets and the end must remain reachable and clamp without lost focus.
- Clear/Add publication and pins must not repeatedly rebuild the navigation tree; viewer changes must clear old authority.
- Recycled cards must not present artwork from an old bind after motion settles or a page leaves.
- Letter/filter/profile/language changes must retire old pending work without suppressing the latest choice.

---

### Task 1: More Episodes input and card-aligned paging

**Files:** `src/SiloPlayer/Views/ItemDetailPage.xaml`, `ItemDetailPage.xaml.cs`, `ItemDetailPage.Cards.cs` as required; focused native fixture in `tests/SiloPlayer.NativeRegressionTests`, registration in `Program.cs`; pure navigation policy under an existing helper seam only if required.

**Interfaces:** Consume the real sibling rail, `CreateEpisodeCard`, current-episode alignment and existing quick-play ownership. Preserve external page behavior and pending detail changes. Produce an explicit vertical-versus-horizontal wheel contract and card-aligned `ScrollSiblingEpisodes(int direction)` behavior, with responsive end-state buttons.

- [x] Write actual-control native input-policy and paging regressions for partial offsets, narrow/large viewport, clamping and current-episode/focus/quick-play preservation. Expected ordinary wheel contract: parent vertical scrolling without sibling horizontal redirection. Actual physical wheel/drag remains unobserved under the ruling below. Expected arrow position: whole-card boundary or final clamped endpoint.
- [x] Publish the unchanged Beta into a fresh diagnostic path and preserve real paging/native-policy failures. Record the scoped posted-wheel boundary limitation; disabled ScrollModes alone do not establish mouse-wheel behavior.
- [x] Implement the smallest native input correction and whole-card paging. If changing control type, update all actual callers/tests and verify drag/alignment/viewport semantics; do not leave casts to the old type. Keep any larger realization replacement a separately reviewed task.
- [x] Run focused green, full unit suite, fresh Release publish, actual new rail fixture plus current episode/quick-play regressions. Record source/artifact hashes and limits.
- [ ] Commit, run independent Standards/Spec review, correct findings and reverify changed scopes, inspect remote heads, push new branch and open PR against Beta.

**Ruling and receipt:** Parent approved a sibling-only ScrollView migration with explicit native MouseWheel exclusion, deliberate horizontal handoff and actual policy/offset/focus/key checks. Posted HWND wheel input never reached WinUI pointer events, so physical wheel and drag remain unobserved; do not claim otherwise. Native class handling required Keyboard exclusion before the page could own Left/Right once. Cost if wrong: hardware compatibility requires follow-up validation. See the spec's Task 1 evidence and `D:/SiloPlayer/.codex-tmp/browse-polish-nov02/episode-task-report.md`. Local checks: focused 41, full 3,068, three selected native groups all passing against one 9FEEFB... app. Independent review and remote delivery remain with the parent task.
### Task 2: Stable sidebar publication

**Files:** `src/SiloPlayer/ViewModels/MainViewModel.cs`, `MainWindow.xaml.cs`; focused helper/partial for reconciliation if warranted; actual native sidebar fixture and useful policy tests.

**Interfaces:** Consume context-bound authorized library collection, nested pins/custom primary/plugin entries, and selected-navigation synchronization. Produce one coalesced publication per dispatcher turn and identity-preserving changes where possible.

- [ ] Reproduce Clear/Add and pin arrival while measuring actual navigation item identity/rebuild count; test unchanged result, rename/add/remove, selected/focused item, expansion and profile departure.
- [ ] Add failing behavioral tests. Implement batch/coalesced publication plus targeted reconciliation without changing capability/hidden/pin semantics.
- [ ] Verify local focused/full/native checks and source receipts; independently review and submit a separate fresh-Beta PR.

### Task 3: Motion-aware artwork scheduling

**Files:** Actual `LibraryPage` viewport path and `LibraryGridCard`, then shared `PosterCard`/`LandscapeCard` policy as supported by evidence; narrowly owned scheduling helper, unit policy and actual recycled-card fixtures.

**Interfaces:** Consume viewport motion and bind generation/cancellation. Produce a cancellable presentation wait that completes immediately during slow movement and releases promptly on settling/departure; no stale callback or unbounded retained control list.

- [ ] Pin behavioral tests for fast-scroll deferral, slow immediate presentation, settling, cancellation, rebinding, leaving and competing viewports; use controllable time where policy permits.
- [ ] Observe baseline red before implementation. Preserve lightweight content/focus and latest URL authority; avoid broad cache rewrites.
- [ ] Verify focused/full/native and diagnostic scrolling work counts; frame-rate claims require actual matching frame evidence. Independently review and submit a separate fresh-Beta PR.

### Task 4: Letter feedback and retained library activation

**Files:** `LibraryPage.Letters.cs`, `LibraryViewModel.Letters.cs`, `LibraryPage.State.cs` and activation path; existing settings/context APIs remain authoritative.

**Interfaces:** Consume current index/query ownership and scoped saved state. Produce immediate pending feedback and safe latest-target completion; retained scope may skip redundant restoration only with explicit invalidation.

- [ ] Write slow/failed target and rapid-letter regressions, query/profile/language/departure cases, cached return and changed settings tests. Reproduce baseline feedback/restoration gap.
- [ ] Implement focused behavior, preserving server validation, retries, selected target, filter drafts and current card safety. Split feedback and warm-return PRs if their validation differs materially.
- [ ] Run focused/full/native checks, independent reviews and submit fresh-Beta PR(s).

## Delivery sequence

Execute each task through review before the next implementation agent. Review agents can run in parallel. Each PR may queue behind existing Beta work; do not merge unrelated candidates locally or take over pipeline ownership. Record submitted PRs and unfinished tasks in the external ledger so continuation does not repeat completed work. The next milestone after browsing is the full OSC/menu project; its native-host boundary must be validated separately.
