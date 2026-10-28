# Desktop Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for continuous inline execution; review the whole branch at the end.

**Goal:** Notify every user of stable updates, show changelogs, support manual installation and opt-in background download/install on normal exit.

**Architecture:** Fixed GitHub release source and existing Inno installer. Core update service handles metadata/download/staging; native partials own presentation and close coordination; a packaged exit helper waits for shutdown and installer completion.

**Tech Stack:** Existing .NET8, WinUI3, HttpClient, SHA256, Inno Setup and Windows PowerShell. No new packages/service.

**Spec:** docs/superpowers/specs/2026-10-28-app-updates-design.md

## Global Constraints

- Both modes always notify; notification-only default; manual update remains available in automatic mode.
- Automatic download defers to playback; automatic install occurs on normal exit; never kill processes/reboot.
- Fixed repository and Windows-x64 asset; require bounded HTTPS metadata/download and SHA256 verification.
- No credentials, production changes, current-app control or feature publication; preserve frozen installers.
- Version1.2.334; keep feature source local until owner approves test installer.

## Review Focus

- Bad/missing digests or hostile release URLs: reject without staging/execution.
- Partial download, cancellation, HTTP200 instead of206: resume only correct bytes, verify entire file.
- Playback starts while automatic download runs: cancel/defer without losing manual update visibility.
- App exit/UAC cancellation/installer error: no force close, no unexpected restart, report retained update on next launch.
- Narrow/high-DPI display and dismissed toast: persistent update access/changelog remains reachable.

### Task1: Release discovery and preferences

Files: new Core/Services/Updates/AppRelease.cs, AppUpdateService.cs; Models/ServerConfig.cs; tests/SiloPlayer.Tests/AppUpdateTests.cs.

- [x] Write failing tests for stable/newer selection, equal normalized versions, missing/bad digest, fixed repository/asset URL, oversized metadata and notification-only default.
- [x] Run targeted tests, confirm expected failure; implement immutable release/state types, UpdateMode and bounded CheckAsync(CancellationToken).
- [x] Run targeted tests green; record evidence.

### Task2: Verified staging and exit handoff

Files: Core/Services/Updates service/staging and launch policy; app Updates/UpdateOnExit.ps1; tests/AppUpdateDownloadTests.cs and exit-helper tests.

- [x] Write failing tests for digest/length mismatch, cancellation, safe range resume, ready-package revalidation and playback deferral. Additional path/range boundary coverage remains for the final review.
- [x] Implement DownloadAsync/manual versus automatic flow and verified ready state under LocalAppData/SiloPlayer/updates; bounded shared state, one transfer/check, resumable partial bytes.
- [x] Implement fixed exit-helper launcher and script; verify size/hash and the global exit gate using noninstalling validation mode. Test cancellation/result handling and normal-exit versus manual-restart policy without elevating/installing. Parent wait/registered-install execution remain source-reviewed; no real installation is authorized.
- [x] Run targeted tests green; preserve exact helper evidence.

### Task3: Native notifications, release notes and settings

Files: MainWindow.Updates.cs, MainWindow.xaml/.cs, Views/SettingsPage.Updates.cs/.xaml/.cs, App.xaml.cs and project content.

- [x] Add native fixture with fake release HTTP/launcher to verify both modes notify, toast action/dialog, persistent access, manual update availability and mode persistence; observe failure before UI implementation.
- [x] Add background lifecycle, green persistent indicator and success toast; text-only release-note dialog, progress/check/update buttons and desktop-settings card.
- [x] Integrate shutdown cancellation, captured staged release, receiver/player drain and helper scheduling only after an eligible normal close. Avoid any installer execution in fixtures.
- [x] Verify narrow,1080p,3440x1440,4K and150% layout; inspect rendered evidence.

### Task4: Review, full checks and installer

- [x] Review all feature changes independently against spec and repo standards; fix concrete findings with regressions.
- [x] Version app/installer1.2.334; add release scope/current-development record, with published download unchanged.
- [x] Run meaningful full unit/published/native regressions, supported installer/build.ps1 signing/native validation, exact-payload checks and receipt.
- [x] Deliver local installer for owner testing; no automatic installation, feature push or release publication.

## Execution ledger

Audit PR24–29 merged/closed in main8c1b238 after2,281 tests/five native/33 published groups. Feature branch codex/app-updates starts there. Owner selected automatic download/install on exit and reiterated always-notify/manual availability. Installer elevation remains a visible Windows requirement. Product implementation has not started at plan creation.

Task1 red→green:16 discovery/preferences tests pass. Task2 download red→green plus failed-metadata disarm regression:29 discovery/download tests pass. Exit helper red→green:four real Windows tests pass, including checksum validation using nonexecutable synthetic files and VerifyOnly, and a separate install gate owner. No installer was launched. Evidence: `D:\SiloPlayer\.codex-tmp\app-updates-oct28`.

Ruling: retain Inno and a packaged fixed exit script; do not add an updater framework or privileged service. Builder check identified handoff races, installed-location validation, journal/result semantics and network range constraints; these are incorporated in the spec. A global per-install startup gate plus refusal when any other instance remains protects replacement; explicit no-close/no-force-close flags preserve other processes. Release that gate before any requested restart. No automatic rollback claim is made for the existing replacement installer.

Task2/3:36 updater unit/helper tests pass. Real native updater fixture passes automatic, explicit-manual restart, notification-only and simulated Windows-session-ending cases with a noninstalling fake launcher; dialog/settings/mode persistence and900/1080p/ultrawide/4K bounds are exercised. The first screenshot sampled the sidebar expansion animation; settled captures wait for that animation. A fixture cleanup callback initially ran after provider disposal; draining unload before restoring/disposal corrects the harness. A production initialization-order null reference and programmatic manual-close bypass were caught and repaired before any installer was made. Evidence retains failing and corrected candidates separately.

Ruling: require a nonce confirmation written only by normal Window.Closed before the helper can execute. Waiting for process exit alone would incorrectly allow a crash after scheduling to trigger installation. A shared five-second progress-drain budget defers an update if shutdown cannot save in time. Limit long changelogs for responsive native text and retain a trusted full-release link. Cleanup is nonrecursive and only removes known generated files for versions already installed; unrelated files and newer staged updates remain.

Final review: Standards found two lifecycle issues (setup timeout releasing the gate early; canceled Windows shutdown retaining the disarm flag). Spec found two issues (Later not revoking manual-install consent; silent download deadline). All four are corrected; independent re-review has no remaining findings. The warning-journal failure variant also retains the setup gate. Native Later/canceled-session regressions fail the frozen old candidate and pass the final payload. Additional wrong-range, streaming overrun and real junction rejection tests pass.

Final334:2,324 main tests,12 native groups, three consumer compilations and33 published-service groups pass. Final app DLL `B3B2BE0FC427A3A184C09A45CC8F645A093C81BD065D0CE76226FEAE0B828A43`; installer `A167B5BDAA9B8E547C8695B1CB63C757FF7450C7E7CC403884D7AAD1AC7A8AC8`. Evidence/receipt `D:\SiloPlayer\.codex-tmp\app-updates-oct28\sealed334-final`. Native UI capture at350ms confirms earlier apparent label clipping was the sidebar animation, not settled geometry. No installed-app control, installation or feature publication occurred. Actual elevated in-app upgrade remains an explicitly documented owner/future-release verification boundary.
