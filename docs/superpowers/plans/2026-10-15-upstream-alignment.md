# October 15 upstream alignment implementation plan

> **For agentic workers:** Implement the approved findings task by task, using test-first behavioral checks and combined native verification.

**Goal:** Align desktop 1.2.247 with the twelve source-confirmed changes in the October 15 audit and investigate progressive-remux long-pause behavior.

**Architecture:** Retain WinUI/libmpv and existing authority, cache, cancellation and diagnostics boundaries. Use the pinned official WebUI/API as behavior/layout reference, with capability gates for newer server features. Separate independent Settings/appearance, media-detail/actions, Home/recommendations and localized-read lifecycle work; integrate and verify before packaging.

**Tech Stack:** WinUI 3, .NET 8, libmpv, xUnit, existing native regression host and supported installer script.

**Spec:** `docs/parity/2026-10-15-upstream-delta.md`; the user approved implementing these findings on October 15.

## Global constraints

- Reference official `9d852a15b21cfbb3f615a1b7fcea2055c15279ae`, refetched immediately before implementation and unchanged.
- Preserve running installed player, accounts, credentials and frozen installers; isolated fixtures only. No production writes, installs, commits or publication.
- Preserve native playback/SiloCast preferences. Do not add a browser decoder or server administration features.
- Never equate source/unit passing with rendered parity or a long-pause remux finding with historic direct-play buffering.
- Version increments identify completed correction groups; final version and scope recorded before packaging.

## Review focus

- Old server capability absence must hide new mutation actions; no destructive mutation retries.
- A profile/server switch or navigation during async work must reject retired completion and release subscriptions.
- Localization invalidation must cancel old reads without repeatedly rebuilding healthy Home/poster state.
- Metadata omissions, empty cast/crew and single episodes must not leave reserved blank sections.
- Settings must remain usable at narrow, 1080p, ultrawide and 4K sizes; platform text scaling must not clip controls.

## Task A — Settings, appearance and devices (audit 1, 3, 6)

Files: `Core/Api/SettingsApi.cs`, `Core/Services/SharedAppearanceState.cs`, `Services/ThemeService.cs`, `Views/SettingsPage*`, Settings tests/native fixture.

- [x] Add/run failing behavior checks for branding accents/contrast/reset, device custom-name round trips/capability, and moved/default Settings presentation.
- [x] Replace retired appearance request; preserve favicon/sidebar branding and high-contrast foreground behavior.
- [x] Add capability-gated device rename with profile ownership, validation, pending/error/cancel/reset state.
- [x] Match official sidebar/compact index, page grouping, controls, provider expansion/conflict behavior and Next Up writer semantics.
- [x] Verify behavioral/native cases and report exact scope.

## Task B — Media actions, detail data/presentation (audit 2, 5, 8–11)

Files: Catalog progress API/models, detail/episode/Home DTOs, `Controls/MediaItemMenu.cs`, `PosterCard.xaml.cs`, `Views/ItemDetailPage*`, `Core/Services/VersionRanking.cs`, media tests/native fixture.

- [x] Add/run failing behavior checks for Reset Progress capability/non-retry preserving watched state, personal-list eligibility, field-specific translation provenance, grouped crew and Atmos evidence.
- [x] Implement new reset action on eligible movie/episode cards/details with invalidation and no automatic retry.
- [x] Retain episode ratings and translation fields; extend existing translation machinery to listed episodes and visible heroes without stale profile/slide completions.
- [x] Match episode/season list eligibility, combined movie/series Cast & Crew, person grouping and current audio labels/ranking.
- [x] Verify empty/partial media and native responsive detail states; report exact scope.

## Task C — Home, recommendations and long-pause playback (audit 7, 12 + playback check)

Files: Home/Library view-models, recipe presentation service, Recommendations pages/view-models, playback service/policy only if reproduced, corresponding tests/native fixture.

- [x] Add/run failing tests for layout-ordered recipe suppression independent of response arrival, retained raw rows and stable presentation references.
- [x] Apply suppression without refresh loops; update conditional recommendation explanations/headings and remove signal counter.
- [x] Reproduce native progressive-remux long-pause resume; add preventive reopen only if verified necessary for native path. Preserve pause intent/room authority/direct-play modes.
- [x] Report original/updated evidence and distinguish server-compatible changes from native repairs.

## Task D — Localized reads and integration (audit 4)

Files: localized-read coordinator, settings write/event seam, prefetched detail/cache services, tests.

- [x] Trace current read ownership and add failing checks for old-language in-flight completion, local setting writes and realtime changes.
- [x] Cancel/retire localized cache reads, refresh mounted surfaces in place and preserve unrelated playback/session state.
- [x] Review all task changes against the pinned source and reconcile integration issues.
- [x] Run complete main test suite, relevant native fixture groups and published playback checks on exact packaged payload.
- [x] Build a uniquely versioned installer with `installer/build.ps1`, preserve earlier artifacts, record receipt and deliver installer with precise remaining limitations.

## Execution record

Completed this scoped update pass as local 1.2.260. Evidence: docs/releases/1.2.260.md and installer/output/SiloInstaller-1.2.260-verification.json. Final 1,764 main tests, 24 packaged playback groups and nine targeted native groups pass; review found five integration defects, all corrected. A separate actual MainWindow check caught initial Settings routing and screenshots caught search clipping. One party-picker native host launch crashed before results; the unchanged rerun passed and is documented. Full historical parity, original direct-play buffering, intermittent Next Up removal, live AI provider and phone/room interoperability remain outside these completion claims. Source is uncommitted; no install/push/publication.
