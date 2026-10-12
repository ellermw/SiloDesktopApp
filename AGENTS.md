# Silo Desktop Player — Current Development Context

## Start here

The canonical local development checkout is `D:\SiloPlayer`. Current source and GitHub release are **1.2.243**, including the full 1.2.242 badge repair and Windows SiloCast receiver. Release tag: `v1.2.243`.

Before editing or building, read `docs/current-development.md` and verify `<Version>` in `src/SiloPlayer/SiloPlayer.csproj` is 1.2.243 or a deliberately newer version. The 1.2.242/1.2.243 changes are committed on GitHub main. When creating an isolated checkout, use the current main/release or deliberately selected newer source; preserve any subsequent local changes and verify the resulting source before building. Never choose a baseline from a historical handoff without checking it against current source.

Current installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.243-SiloCast-Test-Setup.exe`. Current published payload: `D:\SiloPlayer\installer\publish`. Current launchable test copy: `D:\SiloPlayer\publish-test\SiloPlayer.exe`. Verify versions and receipt before running these paths. Historical installers and stale generated app outputs were removed; historical records retain their original versions for evidence.

## Product and architecture

Native WinUI 3/.NET 8 Windows client with embedded libmpv. Match the current Silo WebUI as closely as native WinUI permits, using broad direct-play codec support, fast loading/seeking and a lightweight stable UI. Functional implementation alone does not establish visual parity; verify layout, states, data and interactions against the official reference when doing parity work.

- `src/SiloPlayer`: WinUI app, views, settings and playback integration.
- `src/SiloPlayer.Core`: API models/services, authentication, playback authority, SiloCast transport/handoff.
- `src/SiloPlayer.Player`: native libmpv integration.
- `tests`: unit, receiver/discovery, published playback and native regression fixtures.
- `installer/build.ps1`: existing supported x64 publish/installer workflow; preserve native dependency validation and QA signing policy.

Use the current source/API contracts. The old Continuum/API v1 primer is historical context, not the current protocol specification.

## Source of truth and preservation

- Authoritative Silo server/WebUI source: `https://github.com/Silo-Server/silo-server`. Fetch official GitHub main directly and record the exact commit before each parity pass. Never use the legacy private GitLab Continuum source for current Silo parity.
- Desktop repository: `https://github.com/ellermw/SiloDesktopApp`.
- Preserve user source changes, Claude memories/transcripts/worktrees and Codex evidence. Source changes remain uncommitted unless the user authorizes committing. Publishing, push and production changes require their own authorization.
- Keep the user's running installed player open. Cleanup applies to generated development outputs, not `%LOCALAPPDATA%\SiloPlayer` or `C:\Program Files\Silo Desktop Player`.
- Never copy or print credentials, access/refresh tokens, API keys or settings JSON values. Read account context only with careful redaction.

## Current feature verification

Read `docs/silocast-testing.md` before SiloCast testing and `docs/releases/1.2.243.md` for scope. Receiver is off by default; it uses private physical LAN discovery and server-approved temporary playback identity without overwriting the desktop account. The optional installer firewall task is restricted to Private networks/local subnet. TV Mode/directional browsing are separate features.

Passed on the frozen 1.2.243 payload: 1,657 main tests, 14 receiver tests, 16 discovery tests, 23 published playback groups, settings rendering at 600/900 widths and 42 badge render cases. Actual phone interoperability remains unverified. Full default native regression verification is incomplete at the preexisting `DetailPresentationNativeFixture.cs:55` expectation of a hidden unavailable series button; current baseline shows it disabled. See the guide and verification receipt for precise evidence. Do not report a complete native pass or complete visual parity from these checks.

## Historical and production references

For historical troubleshooting only, read `docs/archive/2026-10-12-legacy-development-context.md`; its old app versions, API examples and build paths are superseded by this handoff. The previous local README/audit notes are preserved under `.codex-tmp/cleanup-2026-10-12` and in the stash recorded in `docs/current-development.md`.

For production infrastructure tasks, first read `C:\Users\Mike\.codex\memories\00-read-me-first.md`, then the focused runbook. Production changes need explicit approval. Claude project memories under `C:\Users\Mike\.claude\projects\F--ContinuumPlayer\memory` remain read-only unless the user asks to update them.
