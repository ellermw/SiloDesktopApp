# November 12 Server Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the six approved Server build-2210 update groups to Desktop Beta and submit verified work to its existing pipeline.

**Architecture:** Preserve Core/API policy, native WinUI presentation and libmpv/Lua boundaries. Independent workers own contracts, playback, hero and localization foundation in managed worktrees; native localization coverage consumes the foundation afterwards. The lead integrates and verifies combined source.

**Tech Stack:** C#/.NET 10, WinUI 3, Lua/ASS libmpv, xUnit, existing native regression hosts.

**Spec:** `docs/superpowers/specs/2026-11-12-server-alignment-design.md`, with functional source evidence in `docs/research/2026-11-12-server-desktop-gap-analysis.md`.

## Global Constraints

- Base `6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460`; official reference `0b579aa6192d49d7e8e0921dde5e955ac29ce302`.
- Keep `1.2.998-beta.1` / numeric `1.2.997.1` until the pipeline stamps; API v2 and version-neutral worker URLs remain.
- No real settings/accounts, credentials, installed-player control or production changes.
- Every .NET command holds `SiloOct18Build` and validates the pinned engine; outputs on D:, task-scoped short TEMP.
- One writer per worktree; no child agents from workers; only lead submits PRs.

## Review Focus

- A late profile/account callback must neither show another owner's settings/secrets nor apply an old locale/playback transition (Tasks 1,2,4,5).
- Provider outage must not pre-unlock a metadata target; unsupported old servers must not receive a new enum (Task 1).
- Credits countdown and ended post-roll must not double-advance or bypass the unattended gate (Task 2).
- Long/RTL titles and large text must keep hero/HUD actions, menus and timeline reachable (Tasks 2,3,5).
- Translated labels must never replace stored recipe/custom titles or wire identifiers (Tasks 4,5).

### Task 1: Household contracts and atomic metadata reset

**Files:** `Views/SettingsPage.xaml.cs` (extract focused partial if needed), `Core/Api/MediaMaintenanceApi.cs`, `Views/Dialogs/EditMetadataDialog.cs`, metadata capability models, existing `WebhookSyncTests.cs`, `MetadataResetApiTests.cs`, native profile/settings/metadata fixture.

**Interfaces:** Consume existing captured `ApiRequestContext`/authorization policy. Produce capability-aware `ResetToProviderAsync` without changing callers' completion contract, and reusable household-management availability used by every Webhook Sync entry point.

- [x] Add failing behavior cases for secondary-profile/deep-link/search refusal, owner retirement and locked-primary refusal guidance; capability-supported reset emits only refresh `mode:reset`, old-server path emits no unsupported mode, outage cannot be preceded by unlock.
- [x] Run focused tests and relevant native regression against base; record expected failures.
- [x] Implement gate/retirement and capability reset, updating all confirmation semantics without restoring locks after a job.
- [x] Run focused/full tests, Release publish and actual-control Webhook/metadata cases. Self-review, commit scoped files and write worker receipt.

### Task 2: Autoplay protection and current native playback presentation

**Files:** `Core/Services` deterministic autoplay/end-time policies, local preferences/settings models, `MainWindow.xaml.cs` and focused partials, `PlayerService*.cs`, `libs/mpv/scripts/silo-osc.lua`, playback menu models, `SettingsPage.xaml`/view model, production-Lua/runtime/native fixtures.

**Interfaces:** Consume existing playback owner/session/pause/seek/next events and profile date/time choices. Produce local threshold/streak gate and live finish-time/loading/HUD rendering. Keep existing playback and menu public contracts; expose new user-visible messages through explicit localization keys during final integration.

- [ ] Add failing real-policy/production-Lua cases: threshold 0/default/max, manual reset, automatic streak, both advance boundaries, shuffle and Watch Party; end time at 1x/2x, paused clock/scrub/unknown duration; loading art and menu/timeline hit regions at compact/large sizes.
- [ ] Record red before implementation; use upstream player components/behavior as reference.
- [ ] Implement complete native behavior and applicable HUD layout, using owned images/loads and cancellation. Keep thumbnail/first-frame/error/rebuffer states distinct and preserve retry/pause/track intent.
- [ ] Run focused and full units, publish and native playback/production-Lua routes. Inspect images against the pinned reference, document remaining true platform differences, self-review and commit.

**Completion pass (worker returned partial `efce4e60`, not task approval):**

- [ ] Implement the applicable desktop Playing Next composition with finishing episode beside the next, credits-card episode still respecting spoiler preferences, chapter segments/lamp-lit seek styling and compact native speed picker.
- [ ] Port HUD accent contrast adjustment, show catch-up/reconnect labels only for those waits, fade the OSC loading image over500ms without excessive allocation, and request an appropriate screen-sized watch backdrop (update exact URL expectations rather than omitting the query).
- [ ] Add end-to-end native/synthetic playback-manager coverage for manual versus automatic starts and both advance boundaries, including failure/retirement/shuffle/Watch Party. Existing policy/Lua tests alone do not prove the full wiring.
- [ ] Verify actual native small/large/loading/post-roll/menu/hit regions against the pinned official reference, then independent review of the entire Task2 change. Phone-only layout and physical display/live-server claims remain outside scope.

### Task 3: Content-sized Featured hero

**Files:** `Controls/HeroCarousel.xaml`, `HeroCarousel.xaml.cs`, existing hero policies/fixtures; affected Home/Library layout only as necessary.

**Interfaces:** Preserve public hero properties/events/navigation; sizing becomes a content-dependent minimum with current viewport/library clearance semantics.

- [x] Extend actual-control fixture with long multiline titles and narrow/short/large-text cases that currently clip, including actions/counter bounds.
- [x] Run red, then change measurement and remove only obsolete eyebrow/margin.
- [x] Verify Home and Library minima, resizing/data changes, no layout loop, carousel/counter/input reachability, static Featured backdrop and unchanged collection hero. Publish/native checks, self-review and commit. Independent review approved; integrated as `a72ca983` (worker `d7369cee`).

### Task 4: Localization foundation, server titles and problems

**Files:** New `Core/Localization` modules/catalogs, deterministic catalog import script/provenance, Home/recipe/settings DTOs/copy paths, `SiloApiClient.cs` structured errors, native localization service registration/lifecycle, language setting/picker, `DateTimeDisplay.cs`, tests and native language fixture.

**Interfaces:** Produce `AppStrings.Get(string key, IReadOnlyDictionary<string, object?>? arguments = null, string? fallback = null)` and a locale service with current culture/direction and change notification. Server text takes explicit optional key/args/raw fallback. Structured API exceptions retain field errors without changing machine code/status/retry contracts. Task 5 consumes these public seams; document exact additional signatures in worker report.

- [ ] Pin authoritative catalog and add failing tests for effective language/device matching, null/unknown tags, plural/argument/fallback formatting, profile retirement, title args copying/raw persistence and structured field errors.
- [ ] Implement offline generated resources for shipped native languages with reproducible import/provenance; do not hand-edit generated translations or invent a Windows upstream artifact.
- [ ] Wire profile-owned effective locale/settings events and picker, RTL and display culture, DTO/reconciliation data and presentation-only problem resolution. Keep raw data unchanged and English detailed fallback.
- [ ] Verify focused/full tests and native picker/switch/Arabic/unknown-tag behavior. Commit and document API/call-site examples plus exact remaining Task-5 coverage.

### Task 5: Complete native localization coverage and integrate all groups

**Files:** Native shell/sign-in, Home/library/detail/search/calendar, collections, settings/profile/account, Watch Party, reader and production playback text; XAML resources/keyed bindings and locale-refresh paths.

**Interfaces:** Consume Task-4 `AppStrings`/locale service and explicit title/problem APIs. Preserve untranslated IDs, saved user/custom text, API strings, routing tags and credentials.

- [ ] Add failing observable coverage for representative reachable controls in every named area and language-switch/profile-switch races, including raw-title persistence and dialog/menu direction.
- [ ] Replace user-visible literals with explicit authoritative keys/fallbacks, wire retained views/dialogs/Lua messages to locale updates, format numbers/dates and mirror relevant navigation.
- [ ] Check all shipped locale resources, long translated text, mixed-direction media titles, English fallback and unknown keys. Ensure new contract/playback/hero text participates.
- [ ] Run final full suites, Release publish and all affected native routes against integrated bytes; independently review final source and fix findings, record receipts/handoff, submit qualifying Beta PR(s).
