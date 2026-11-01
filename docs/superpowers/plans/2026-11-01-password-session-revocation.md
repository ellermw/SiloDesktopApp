# Password session revocation implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline; this approved task forbids further delegation.

**Goal:** Add capability-gated password session revocation without signing out this client.

**Architecture:** Extend the existing capability model and optional API request. Keep choice ownership in AccountPasswordViewModel and synchronize an actual WinUI CheckBox in SettingsPage.

**Tech Stack:** .NET 8, WinUI 3, xUnit, synthetic native fixture.

**Spec:** `docs/superpowers/specs/2026-11-01-password-session-revocation-design.md`

## Global constraints

Keep source stamps 1.2.519. Use official reference `63ef34b236b88dc77acca1a9a3ed90235377691f`. All dotnet commands hold `SiloOct18Build`; output stays beneath this checkout. No real accounts, installed app, installer or production operations.

## Review focus

Absent capability must omit the member even if selection is attempted. Unsupported/read-only forms cannot opt in or submit. Reload and delayed responses cannot restore an old choice. Pending submits freeze payload and message. Current authentication survives opted-in success.

## Tasks

- [ ] Add behavioral regressions to AccountPasswordTests for the review focus, observe missing feature failure under the build mutex.
- [ ] Extend AccountPasswordCapability with SignOutOtherSessions; add optional supported opt-in serialization to AccountPasswordApi.ChangePasswordAsync while preserving existing callers.
- [ ] Add CanSignOutOtherSessions and guarded SignOutOtherSessions selection, capture request before pending state, reset on deactivation/success, notify capability changes and preserve generation guards.
- [ ] Add wrapped checkbox/help in SettingsPage XAML and synchronization/event handler in SettingsPage.AccountPassword.cs; disable pending selection.
- [ ] Add dedicated PasswordSessionsNativeFixture and Program route `password-sessions` with synthetic service isolation, actual controls and layout assertions; leave shared run.ps1 untouched.
- [ ] Run focused and full SiloPlayer.Tests, fresh Release publish and native fixture under mutex; inspect diff and record exact evidence/limits in task report and development handoff.
- [ ] Commit assigned branch after successful relevant unit tests; parent performs independent review and remote delivery.
