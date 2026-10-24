# October24 upstream updates and Home stability implementation plan

> **For agentic workers:** use superpowers:subagent-driven-development or executing-plans. Preserve accepted scope; source stays uncommitted until separately authorized.

**Goal:** Implement all15 groups in the October24 audit and repair the installed app's reported Home errors and disruptive return loads, then verify a local installer for owner testing.

**Architecture:** Keep WinUI3/.NET8/libmpv. Add focused native/core modules for connection recovery and preference persistence. Preserve a mounted Home surface and reconcile data in place with captured-owner guards; first reproduce its logged failure and return-navigation behavior. Follow exact upstream489fe4d55735f3855b2a0d6276e57d148891b456 contracts and existing older-server fallbacks.

**Spec:** `docs/parity/2026-10-24-upstream-delta.md` plus the user's Home error/rough reload report.

## Constraints and ownership

- Baseline1.2.286, preserve issue8 tests/docs and all frozen release/installer evidence. No production writes, user-account mutations, installed-app shutdown, credentials in artifacts, commit or GitHub push.
- All dotnet build/test lifetimes hold named mutex `SiloOct18Build`; independent file edits can proceed concurrently, shared-file changes must be coordinated.
- Root: HomeViewModel/HomePage, membership API, integration/version/installer/docs, native Program selectors and final verification.
- Playback implementer: low-quality labels, audio/detail/subtitle intent, Auto/forced subtitle behavior and DV+HDR10+ wording; owns ItemDetailPage.xaml.cs and preference sections of PlayerService/PlaybackManager. Coordinate shared runtime integration with connection implementer.
- UI implementer: metadata lifecycle removal, collections paging, Show title rules, Home layout transfer, Connect Apps, history/unwatched-sync controls, Hero overview. No ItemDetailPage.xaml.cs changes.
- Connection implementer: new protocol/API/core controller/settings/connection-test modules. New partial integration file where possible; no concurrent editing of PlaybackManager/PlayerService/SettingsPage common files without coordinating.
- Agents add native fixture classes and report entry methods; root registers Program selectors. No agent changes version/installer or runs full-suite/build without coordination.

## Acceptance/review focus

- Home return preserves visible cards, artwork, hero geometry, scroll and focus; refresh updates only changed data. Retired profile/server data never appears.
- UI-thread affinity, canceled loads, disposed owners, late HTTP and collection enumeration races are exercised, not hidden by catch-and-ignore.
- Explicit quality/version intent and profile/device scope remain authoritative. Auto recovery does not silently downgrade a fixed user choice or disrupt a Watch Party.
- Preferences survive episode/version changes and failed/overlapping writes; deliberate forced-subtitle overrides remain respected.
- Current/new and older/missing capability replies, partial pagination failures and unknown stored rules retain safe behavior.

## Work and checks

- [x] Preserve sanitized installed Home/navigation/crash/API evidence; trace exact exception and mounted return lifecycle. Add original-failure regression before each product repair.
- [x] Repair Home failure(s) and warm return reconciliation; test render identity/scroll, play-return, changed rows, empty/error/retry and authority retirement.
- [x] Implement audit1-4/15 playback choices/preferences and subtitle rules test-first; verify current server requests, actual native selected state and regressions.
- [x] Implement audit5 membership capability/current routes and older-server fallback test-first, without per-item404 probing on capable servers.
- [x] Implement audit6/8-14 UI/API groups test-first at behavior seams; verify native layout and real control actions using isolated servers.
- [x] Implement audit7 adaptive Auto, connection testing/learning/reset/settings/telemetry; verify native observed measurements, consent, outage/pause/seek/room conditions and current protocol.
- [x] Review each group's final changes against the exact source/spec and address findings before integration.
- [x] Run focused regressions and fresh complete main suite, supported installer build, exact-payload native groups and published-service checks. Recheck source/payload hashes and frozen artifacts.
- [x] Record completed correction groups and increment version accordingly; deliver final local installer and precise scope/limits. No publication before owner testing/approval.

Final disposition: Version1.2.304 comprises18 correction groups (15 accepted audit groups, Home return, sidebar navigation and the reader-deadline correction discovered during real native verification). Fresh2,120 main tests,32 exact-payload native runs and24 published-service groups pass. Implementation kept source uncommitted until owner testing; the owner subsequently reported the installer looked good and authorized main/release/README publication. See `docs/releases/1.2.304.md` and `installer/output/SiloInstaller-1.2.304-verification.json`. Original failures, intermediate candidates and test-only lifecycle refinements remain preserved.
