# Current development — released 1.2.271

Current source/main/release are **1.2.271**. The owner installed this exact installer, confirmed it works well and authorized publication on October 18, 2026. It implements three update groups against official main `6ab2e85f117935776db052235efeeb149bdc81b4`: current quality/cap/Auto behavior across native/OSC/SiloCast, detail More/Manage and Subs search menus, and login recovery placement. See [scope, verification and limits](releases/1.2.271.md) and [the fresh 33-commit comparison](parity/2026-10-18-upstream-delta.md).

Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.271-Setup.exe`; payload `installer\publish-1.2.271-final`; file version **1.2.271.0**. SHA256: `88AD817966CF41C64150DD9B9AD8EE9A3087CA530D0A564EFA797F18DC0A5A9D`. Publish the exact tested bytes without rebuilding. Passed **1,920 main tests**, **nine exact-payload native groups** and **24 published-service playback groups**. All 565 packaged files match the verification copy. Frozen receipt: `installer\output\SiloInstaller-1.2.271-verification.json`; evidence `.codex-tmp\upstream-oct18`; separate publication checks/receipt in its `publication` directory. Public installer: [v1.2.271 Windows x64](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.271/SiloInstaller-Windows-x64.exe). Preserve all existing source/artifacts and keep the installed player open.

## Historical published baseline — 1.2.268

Previously published **1.2.268** adds the oversized-corner repair to all thirteen 1.2.260 and seven 1.2.267 update groups. The owner installed it, confirmed the backgrounds look correct and authorized publication on October 16, 2026. See [1.2.268 scope and verification](releases/1.2.268.md). Frozen installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.268-Setup.exe`; payload `installer\publish-1.2.268-final`; executable/DLL version **1.2.268.0**. SHA256: `FB7246AF2E77E30BB8EA60DD38ADF9128190367EDE1D2BACAB7EF529B88C84AF`. Five exact-payload native runs passed,565 packaged files matched and1,379 source/build inputs stayed unchanged. Receipt: `installer\output\SiloInstaller-1.2.268-verification.json`; evidence `.codex-tmp\oval-audit-oct16`, publication evidence in its `publication` subdirectory. Official upstream fetched before the visual pass: `f2347ace3643b1add24ff5e6514862f5f26986e0`. Public installer: [v1.2.268 Windows x64](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.268/SiloInstaller-Windows-x64.exe).

## Preserved seven-update candidate 1.2.267

The following candidate notes preserve their original verification status; their changes were subsequently included in released 1.2.268 and 1.2.271.

The preserved **1.2.267** candidate implements all seven ordered October 16 update groups on top of the preserved 1.2.260 candidate. Read [the 1.2.267 scope, verification, decisions and limits](releases/1.2.267.md) and [completed implementation plan](superpowers/plans/2026-10-16-seven-updates.md). This does not certify complete historical parity or resolve the historic recurring direct-play buffering/Next Up removal reports.

Official upstream reference, fetched directly before this pass: `9cc3091d6ec19996ee4f7149c7b72bfad3a0323f`, 43 commits after the October 15 reference in 1.2.260. The seven groups in [the October 16 comparison](parity/2026-10-16-upstream-delta.md) are implemented locally; optional LAN routing/Auto-quality enhancements remain outside scope. Preserve this actual uncommitted candidate when starting subsequent work; public main alone does not contain it.

Local installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.267-Setup.exe`; payload `installer\publish-1.2.267-final`; executable/DLL version **1.2.267.0**. Receipt: `installer\output\SiloInstaller-1.2.267-verification.json`. SHA256: `6B8234BEE7BB0AD1A9072A272DB9DA3CE2C160C64DCA7D93EF3557C77F577742`. Passed **1,910 main tests**, **24 packaged playback groups** and **58 targeted native scenario runs**. All 565 packaged files matched the verification copy before/after execution, and 1,379 source/test/build inputs stayed unchanged. The running installed player was not replaced. No commit, push, GitHub publication or installation was performed; the owner explicitly requires testing before GitHub publication. Public README/download remain **1.2.247**.

Frozen previous candidate 1.2.260 remains at `installer\output\SiloInstaller-1.2.260-Setup.exe` with unchanged SHA256 `CC96F5423AF8044A683E7DF49D0E08439A3A538C825F00BCE80EB39A5A1C7A91`, payload `installer\publish-1.2.260-final` and original receipt. See [its 13-group scope](releases/1.2.260.md). Preserve all earlier release/candidate artifacts and evidence. Current final evidence/review/ledger live under `.codex-tmp\oct16-seven`; no cleanup of preserved source, Claude data or evidence is authorized.

## Historical published baseline — 1.2.247

Published main and release are **1.2.247**. The owner authorized committing, pushing to main and publishing the verified installer on October 13, 2026. This includes the complete 1.2.244 diagnostic/removal, 1.2.245 settings/Home/SiloCast-default, 1.2.246 server-favicon and 1.2.247 Library cursor changes. See [1.2.247](releases/1.2.247.md), [the cursor investigation](audit-gaps/2026-10-13-library-cursor-scope.md), [the settings/Home investigation](audit-gaps/2026-10-12-settings-home-refresh.md) and [the Home removal investigation](audit-gaps/2026-10-12-home-removal-diagnostics.md). The latter's intermittent root cause remains unresolved; preserved logging/wording changes do not establish that it is fixed.

Frozen earlier local candidate: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.244-Setup.exe`. Its scope and verification are recorded in [1.2.244](releases/1.2.244.md), with machine-readable receipt `installer\output\SiloInstaller-1.2.244-verification.json`. Preserve earlier evidence and any subsequent user changes.

Published installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.247-Setup.exe`; payload `installer\publish-1.2.247-final`. See [1.2.247](releases/1.2.247.md) and receipt `installer\output\SiloInstaller-1.2.247-verification.json`. This frozen payload is version **1.2.247**; working source is the newer local candidate above. Preserve the frozen 1.2.243/244/245 installers and payloads. Start future work from current main/release and preserve subsequent local changes. The installed app was not closed or replaced during verification/publication.

Use **D:\SiloPlayer** for future development sessions. On October 12, 2026 this primary checkout was fast-forwarded to desktop commit `d5de162a9669418d072db99aa12e3fb66fe2bd1a`, then the complete verified local 1.2.243 source was promoted from the SiloCast worktree. All 1,502 promoted tracked/new source and documentation files matched that worktree byte-for-byte before the handoff documentation was updated. No feature source was changed during cleanup.

Version 1.2.243 includes every 1.2.242 change plus SiloCast. The owner installed the candidate and explicitly authorized GitHub publication on October 12. These changes are committed on main and released as `v1.2.243`. Future isolated work should start from current main or this release tag, preserving any subsequent local working changes. Verify the project version before building; historical 1.1.x/1.2.241 pointers are not the current baseline.

## Historical 1.2.247 artifacts

- Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.247-Setup.exe`.
- SHA256: `3284F696C29C75F14F41F7631CB1C9199E9CE00B014199BD4404FAA20C7338FE`.
- GitHub release: [v1.2.247](https://github.com/ellermw/SiloDesktopApp/releases/tag/v1.2.247).
- Public installer: [Windows x64](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.247/SiloInstaller-Windows-x64.exe), the same verified bytes without rebuilding.
- Installer verification: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.247-verification.json` (frozen pre-publication record).
- Published payload: `D:\SiloPlayer\installer\publish-1.2.247-final`, file version 1.2.247.0.
- Native fixture copy: `D:\SiloPlayer\.codex-tmp\library-cursor-oct13\native-payload\SiloPlayer.exe`, file version 1.2.247.0; all 561 packaged files match, with additional source assets for the host.
- Testing instructions: [SiloCast testing](silocast-testing.md).
- Scope and verification limits: [1.2.247 release record](releases/1.2.247.md).

Use the existing `installer/build.ps1` workflow for a new installer. It validates native dependencies and the established local QA signing policy. Preserve the current source, dependency pins and playback authority fences.

## Preserved history

Before updating the primary checkout, its local README and 19 untracked audit/spec files were preserved in `.codex-tmp/cleanup-2026-10-12` and Git stash `8ef8d6e5e29dfa665b4be7287898f8859d45e84c`. Their original versions are historical evidence; applying that stash wholesale would reintroduce obsolete current-status text. The previous AGENTS context is retained in [the historical context archive](archive/2026-10-12-legacy-development-context.md).

The obsolete `silocast-receiver` worktree is recoverably archived, and the missing historical `siloplayer-v1155` worktree registration was pruned. The `silocast-current` worktree remains as the original verification/evidence location; the primary checkout is now canonical. Other chats' source worktrees and Claude artifacts are preserved. Confirmed stale generated binaries and old local installer executables were removed; diagnostic source files, logs, screenshots and records were retained. The installed/running player and its account data are untouched.

Cleanup inventory, removal results and source/payload checks are recorded in `.codex-tmp/cleanup-2026-10-12`. Historical verification receipts, screenshots, diagnostics and reference repositories remain available. Historical documents may cite deleted installer files; use the current artifact paths above for testing and development.

Final verification confirms all 891 primary build inputs match the frozen 1.2.243 manifest; both primary payload mirrors contain exactly 561 files with matching hashes and executable version 1.2.243.0. The current installer checksum is unchanged. The final scan of the primary and active Codex worktrees found only 1.2.243 app executables/installers. The central installer output contains one installer. Recorded file removals account for more than 35 GB, excluding whole generated directories and the archived checkout.
