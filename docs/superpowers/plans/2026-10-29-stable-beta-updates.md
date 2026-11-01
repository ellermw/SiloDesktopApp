# Stable and Beta updates implementation plan

> For agentic workers: use superpowers:executing-plans inline. The owner requested direct implementation/testing; preserve this execution choice and do not add approval stops.

**Goal:** Optional Beta updates with safe semantic ordering, persistent channel choice and the current update/recovery protections.

**Architecture:** Add semantic identity beside numeric installation versions. Extend the bounded GitHub authority reader and service lifecycle, existing settings/dialog, manifest builder and explicitly verified full fallback. Preserve the immutable numeric Protocol1 bootstrap.

**Tech stack:** Existing .NET8/C#, WinUI3, PowerShell/Inno, xUnit/native fixture systems; no new runtime dependencies.

**Spec:** ../specs/2026-10-29-stable-beta-updates-design.md and owner's attached stable/beta proposal.

## Global constraints

- Stable default, no silent downgrade or automatic MAJOR change.
- No heartbeat/orchestrator edits, stable publication, main push or installed-app control.
- Worktree stable-beta-updates; base a83b3e2158983159baac0bc4ad46c8422d6fcffd; source remains uncommitted unless authorized.
- Hold SiloOct18Build for .NET builds/tests. Fresh outputs, pinned dependencies, synthetic accounts/data only.
- Preserve all legacy safety and exact authority/hash/version checks.

## Review focus

- A delayed old-channel check/download must not revive an actionable offer after switching.
- A cached Beta response must never be reused as Stable authority or reset shared cooldowns.
- Semantic and numeric versions/tags/manifest/PE stamps must agree before privileged mutation.
- Unsupported protocol may use verified full fallback; corruption must stay a failure, and migration needs manual consent.
- Skipped versions, stable promotion and interrupted activation must retain prior versions and user data.

### Task 1: Semantic identity and numeric contract

Files: new Core Models/Updates/ReleaseVersion.cs; AppRelease.cs; new tests/ReleaseVersionTests.cs.
Interfaces: ReleaseVersion.TryParse/Parse, CompareTo, Text, IsPrerelease, GetWindowsVersion; AppRelease semantic identity/display keeps numeric Version ABI.
- [x] Add failing precedence/mapping/bounds tests (beta2<beta12<stable; build metadata; invalid forms).
- [x] Run RED; implement parser/comparator and explicit mapping; run GREEN and existing update tests.

### Task 2: Channel discovery and lifecycle

Files: GitHubReleaseReader.cs, AppUpdateService.cs/.Download.cs, new channel tests.
Interfaces: AppUpdateChannel Stable/Beta, InstalledReleaseVersion, SetChannelAsync, Channel, ChannelNotice; channel-scoped ReleaseCheck caches.
- [x] Add failing discovery, pagination/ETag, flag/authority, major guard and switching-race tests.
- [x] Implement reader and serialized/cancelled channel transition with epoch guards; preserve cooldowns and ready-cache identity.
- [x] Run new and all existing update/download/rate-limit tests GREEN.

### Task 3: Manifest identity, skipped updates and full fallback

Files: manifest/transport/pack builder, side-by-side transaction gate, service download, WindowsAppUpdateInstaller and UpdateOnExit helper; protocol/staging/download tests.
Interfaces: optional manifest ReleaseVersion (required beta), typed migration incompatibility; full fallback state/consent; root-targeted verified full schedule.
- [x] Add failing beta identity, beta2->12 direct hash reuse, promotion/recovery, tamper/fallback and manual migration gate tests.
- [x] Implement identity binding, same-major staging, recognized migration fallback, full installer registered-root/save/restart handling.
- [x] Run protocol, staging, transaction and download suites GREEN; preserve corruption failures.

### Task 4: Persisted preferences and native presentation

Files: AppSettings model, SettingsService, App.xaml.cs, SettingsPage.xaml/.Updates.cs, MainWindow.Updates.cs/.UpdateStartup.cs; settings/native fixtures.
- [x] Add failing old-settings preservation/defaults and native beta/channel label, warning, switching, long-note/fixed-action tests.
- [x] Wire installed informational identity and persisted selection; add channel controls, notices and semantic success/offer labels without unrelated UI changes.
- [x] Verify native synthetic views and unchanged consent/centering/playback gates.

### Task 5: Builder and external release contract

Files: installer/build.ps1 (self-contained Get-StampedAppVersion preflight), Inno stamps, SiloUpdatePack CLI; docs/update-release-contract.md.
- [x] Add failing preflight/pack version identity tests for stable/beta and manipulated stamps.
- [x] Implement semantic/numeric preflight and pack binding, supported Windows/Inno informational labels.
- [x] Build fresh local beta candidate; document exact five asset names/flags/tags/version contract for the other session. No publish.

### Task 6: Combined verification and handoff

- [x] Run full Windows/protocol/staging tests under mutex; exact-source fresh package/native/published/consumer checks.
- [x] Request independent whole-change Standards/Spec review; reproduce/correct any material findings with RED/GREEN tests.
- [x] Record source/input/fixture hashes and receipts in current-development and feature ledger; preserve plan/evidence and publication hold. Final handoff lists contract, local package, verified scope and unobserved production behavior.
