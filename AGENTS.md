# Silo Desktop Player — Current Development Context

## Release delivery

When the owner authorizes pushing or merging app changes to main, finish delivery with a matching versioned installer, GitHub release, README version and download/checksum links. Verify and publish the same installer bytes. An explicit owner request to hold publication takes precedence. Keep the installed player open; release publication does not authorize installing or restarting it. Record current source, release and verification in `docs/current-development.md`.

## Start here

The canonical local development checkout is `D:\SiloPlayer`. Read `docs/current-development.md` before editing or building for the current source, published version, installer, receipts and outstanding verification limits. Verify `<Version>` in `src/SiloPlayer/SiloPlayer.csproj` matches that context or is deliberately newer.

When creating an isolated checkout, use current main/release or deliberately selected newer source. Preserve subsequent local changes and verify the resulting source before building. Check historical handoffs against current source before choosing a baseline.

Verify versions and receipts before running an installer or fixture payload. Preserve frozen earlier artifacts and original evidence as historical baselines; build new releases from the verified current source.

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

Read `docs/silocast-testing.md` before SiloCast testing and the current release record for scope. Receiver is enabled for fresh/missing preferences; saved opt-outs remain respected. It uses private physical LAN discovery and server-approved temporary playback identity without overwriting the desktop account. The installer automatically adds its Private-network/local-subnet firewall rule without a SiloCast checkbox. TV Mode/directional browsing are separate features.

Use current release records and receipts for exact test counts, upstream references and limitations. Earlier checks retain their original versioned evidence. Actual phone interoperability, historic recurring buffering and complete visual parity require their own evidence; a passing synthetic test does not certify them. The default native regression suite has a historical `DetailPresentationNativeFixture.cs:55` hidden-versus-disabled assertion limitation; consult current evidence before attributing a failure.

## Historical and production references

For historical troubleshooting only, read `docs/archive/2026-10-12-legacy-development-context.md`; its old app versions, API examples and build paths are superseded by this handoff. The previous local README/audit notes are preserved under `.codex-tmp/cleanup-2026-10-12` and in the stash recorded in `docs/current-development.md`.

For production infrastructure tasks, first read `C:\Users\Mike\.codex\memories\00-read-me-first.md`, then the focused runbook. Production changes need explicit approval. Claude project memories under `C:\Users\Mike\.claude\projects\F--ContinuumPlayer\memory` remain read-only unless the user asks to update them.
