# Current development and release — 1.2.243

Use **D:\SiloPlayer** for future development sessions. On October 12, 2026 this primary checkout was fast-forwarded to desktop commit `d5de162a9669418d072db99aa12e3fb66fe2bd1a`, then the complete verified local 1.2.243 source was promoted from the SiloCast worktree. All 1,502 promoted tracked/new source and documentation files matched that worktree byte-for-byte before the handoff documentation was updated. No feature source was changed during cleanup.

Version 1.2.243 includes every 1.2.242 change plus SiloCast. The owner installed the candidate and explicitly authorized GitHub publication on October 12. These changes are committed on main and released as `v1.2.243`. Future isolated work should start from current main or this release tag, preserving any subsequent local working changes. Verify the project version before building; historical 1.1.x/1.2.241 pointers are not the current baseline.

## Current artifacts

- Installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.243-SiloCast-Test-Setup.exe`.
- SHA256: `16D2841FA4E65B31847715EC835F0CB9C8A2DFB9C66E6D4D338C701015D88C7B`.
- GitHub release: [v1.2.243](https://github.com/ellermw/SiloDesktopApp/releases/tag/v1.2.243).
- Public installer: [Windows x64](https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.243/SiloInstaller-Windows-x64.exe), the same bytes as the installed local candidate.
- Installer verification: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.243-SiloCast-Test-verification.json`.
- Published payload: `D:\SiloPlayer\installer\publish`, file version 1.2.243.0.
- Launchable test copy: `D:\SiloPlayer\publish-test\SiloPlayer.exe`, file version 1.2.243.0.
- Testing instructions: [SiloCast testing](silocast-testing.md).
- Scope and verification limits: [1.2.243 release record](releases/1.2.243.md).

Use the existing `installer/build.ps1` workflow for a new installer. It validates native dependencies and the established local QA signing policy. Preserve the current source, dependency pins and playback authority fences.

## Preserved history

Before updating the primary checkout, its local README and 19 untracked audit/spec files were preserved in `.codex-tmp/cleanup-2026-10-12` and Git stash `8ef8d6e5e29dfa665b4be7287898f8859d45e84c`. Their original versions are historical evidence; applying that stash wholesale would reintroduce obsolete current-status text. The previous AGENTS context is retained in [the historical context archive](archive/2026-10-12-legacy-development-context.md).

The obsolete `silocast-receiver` worktree is recoverably archived, and the missing historical `siloplayer-v1155` worktree registration was pruned. The `silocast-current` worktree remains as the original verification/evidence location; the primary checkout is now canonical. Other chats' source worktrees and Claude artifacts are preserved. Confirmed stale generated binaries and old local installer executables were removed; diagnostic source files, logs, screenshots and records were retained. The installed/running player and its account data are untouched.

Cleanup inventory, removal results and source/payload checks are recorded in `.codex-tmp/cleanup-2026-10-12`. Historical verification receipts, screenshots, diagnostics and reference repositories remain available. Historical documents may cite deleted installer files; use the current artifact paths above for testing and development.

Final verification confirms all 891 primary build inputs match the frozen 1.2.243 manifest; both primary payload mirrors contain exactly 561 files with matching hashes and executable version 1.2.243.0. The current installer checksum is unchanged. The final scan of the primary and active Codex worktrees found only 1.2.243 app executables/installers. The central installer output contains one installer. Recorded file removals account for more than 35 GB, excluding whole generated directories and the archived checkout.
