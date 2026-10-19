# Silo Desktop Player — Current Development Context

## Start here

The canonical local development checkout is `D:\SiloPlayer`. Current source and GitHub release are **1.2.271**, including all prior 1.2.268 changes plus current quality contracts, detail/subtitle menus and login recovery alignment. Release tag: `v1.2.271`. The owner installed and approved this exact installer on October 18, 2026.

Before editing or building, read `docs/current-development.md` and verify `<Version>` in `src/SiloPlayer/SiloPlayer.csproj` is 1.2.271 or a deliberately newer version. All changes through 1.2.271 are included on GitHub main. When creating an isolated checkout, use the current main/release or deliberately selected newer source; preserve any subsequent local changes and verify the resulting source before building. Never choose a baseline from a historical handoff without checking it against current source.

Current installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.271-Setup.exe`. Current published payload: `D:\SiloPlayer\installer\publish-1.2.271-final`. Native fixture copy: `D:\SiloPlayer\.codex-tmp\upstream-oct18\final-native-payload\SiloPlayer.exe`. Verify versions and receipt before running these paths. Preserve frozen earlier artifacts and their original evidence; they are historical baselines, not current build inputs.

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

Read `docs/silocast-testing.md` before SiloCast testing and `docs/releases/1.2.271.md` for scope. Receiver is enabled for fresh/missing preferences; saved opt-outs remain respected. It uses private physical LAN discovery and server-approved temporary playback identity without overwriting the desktop account. The installer automatically adds its Private-network/local-subnet firewall rule without a SiloCast checkbox. TV Mode/directional browsing are separate features.

Version 1.2.271 passed 1,920 main tests, nine exact-payload native groups and 24 published-service playback groups. All 565 packaged files and 1,383 source/test/build inputs were checked; one post-build test-only Original-to-Auto assertion correction is documented in the receipt. Official upstream reference: `6ab2e85f117935776db052235efeeb149bdc81b4`. Earlier Add Row, receiver/discovery, Library cursor, favicon and Settings/Home checks retain their original versioned evidence. Actual phone interoperability remains unverified. The intermittent Next Up removal root cause remains unresolved; added diagnostics do not establish a repair. Full default native regression verification has the preexisting `DetailPresentationNativeFixture.cs:55` hidden-versus-disabled assertion limitation. See release records and receipts for precise scope; these checks do not certify complete visual parity.

## Historical and production references

For historical troubleshooting only, read `docs/archive/2026-10-12-legacy-development-context.md`; its old app versions, API examples and build paths are superseded by this handoff. The previous local README/audit notes are preserved under `.codex-tmp/cleanup-2026-10-12` and in the stash recorded in `docs/current-development.md`.

For production infrastructure tasks, first read `C:\Users\Mike\.codex\memories\00-read-me-first.md`, then the focused runbook. Production changes need explicit approval. Claude project memories under `C:\Users\Mike\.claude\projects\F--ContinuumPlayer\memory` remain read-only unless the user asks to update them.
