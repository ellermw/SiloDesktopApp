# Incremental side-by-side updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement task-by-task. Independent subtitle and pack/installation modules may use superpowers:dispatching-parallel-agents with fixed contracts and disjoint file ownership.

**Goal:** Download changed files, install beside the running Silo, show centered continuous progress until restart, retain success feedback, and repair subtitle switching that pauses playback.
**Architecture:** A hash-bound manifest/content pack prepares an immutable payload. A finite version-local updater host stages it under the registered installation root while Silo remains open; a stable .NET 8 launcher and atomic activation journal handle restart/recovery. Keep the existing full installer as the one-time migration/fallback distribution.
**Tech Stack:** .NET 8, WinUI 3, System.Text.Json, Brotli, SHA256, Windows named pipes/process identity, Inno Setup.
**Spec:** ../specs/2026-10-29-incremental-side-by-side-updates-design.md (owner approved).

## Global Constraints

- Keep the main Silo window open through target installation; close normally only for final activation/restart.
- Preserve notifications, release notes, automatic install-on-normal-exit/no reopening, current pause/play intent, success feedback and user data.
- No installed-app control, real production installation, credentials, production changes, source push or release publication. Source remains uncommitted until owner authorizes it. Produce a local installer first.
- Only synthetic install IDs/data/roots may execute. Preserve canonical diagnostics and all frozen packages. .NET commands hold SiloOct18Build. New publish outputs must be fresh.
- Preserve native engine pin, updater cooldown/cache protections, current SiloCast private-LAN and PC-started remote control, protocol activation and single-instance scope.
- Count actual behavior modifications in a ledger, not tests/builds. Stamp Version, FileVersion, AssemblyVersion and Inno version together when the candidate is ready.

## Review Focus

- Trust boundary: local cache/IPC/digests cannot authorize arbitrary privileged writes; authoritative manifest is independently verified and paths/handles are bounded.
- Interruption: cancel, disk failure, app exit, helper crash, locked bootstrap files and competing launches preserve a working active version without false success.
- Scope: all versions use the registered root for coordination; automatic exit never reopens the client, and next-launch readiness does not require restoring user data.
- UI: actual progress windows are centered on owner client bounds under resize/scaling/monitor changes and stay visible across the final handoff.
- Playback: subtitle text/Off/sidecar/burn-in switches preserve playing/paused intent and room authority through failure/recovery.

## Shared contracts (freeze before parallel implementation)

Task1 owns these new public Core types under Models/Updates. Keep AppRelease's seven-argument constructor and AppUpdateState's existing constructor intact; add init properties instead of changing positional arity.

- `IncrementalReleaseAssets(Uri ManifestUrl, string ManifestSha256, long ManifestSize, Uri PackUrl, string PackSha256, long PackSize)`; `AppRelease.Incremental` nullable init property.
- `UpdateFileEntry(string Path, long Size, string Sha256, long Offset, long PackedSize, string PackedSha256, string Area = "client")`.
- `UpdateManifest(int Schema, string Version, long PackSize, string PackSha256, IReadOnlyList<UpdateFileEntry> Files, int BootstrapProtocol = 1, bool BackwardCompatibleData = true)`.
- `PreparedUpdatePackage(string Directory, string ManifestPath, string ManifestSha256, UpdateManifest Manifest)`; Directory contains client/ and bootstrap/ relative payload trees.
- `IncrementalUpdateProgress(string Stage, long CompletedBytes, long TotalBytes, int CompletedFiles, int TotalFiles)`.
- `IncrementalUpdateClient(HttpClient http, string cacheDirectory, string installedDirectory, Action<string>? log = null)` exposes `Task<PreparedUpdatePackage> PrepareAsync(AppRelease release, Action<IncrementalUpdateProgress> progress, CancellationToken ct)` and `Task<bool> VerifyPreparedAsync(PreparedUpdatePackage package, CancellationToken ct)`.
- `IncrementalReleaseAuthority(HttpClient http)` exposes `Task<(AppRelease Release, UpdateManifest Manifest, string ManifestPath)> FetchAsync(string tag, string destinationDirectory, CancellationToken ct)`. It establishes authority from the fixed GitHub repo/tag API and hash-verified manifest, independently of caller digest. Reuse existing metadata/rate-limit handling.
- Manifest rules: schema/protocol1; normalized four-component version; exact fixed pack/manifest filenames; maximum4096 entries,2MiB manifest,512MiB pack/single file,2GiB aggregate raw data. Areas are exactly client/bootstrap. Case-insensitive unique area+path; no rooted/traversal/ADS/device/trailing-dot-or-space/control/link paths. Brotli objects have bounded decoded lengths, hashes and nonoverlapping pack spans except identical shared objects. Required client SiloPlayer.exe/dll; bootstrap changes are staged separately until activation.

Task2 owns public Core installation contracts under Services/Updates and Models/Updates (separate files from Task1):

- `SideBySideState(string ActiveVersion, string? PreviousVersion, string? PendingVersion, string? TransactionId)`.
- `StagedUpdate(string InstallationRoot, string TransactionId, string Version, string VersionDirectory, string BootstrapDirectory, UpdateManifest Manifest)`.
- `SideBySideInstallation.ReadState(string root)`, `ResolveClientExecutable(string root)`, `Stage(string root, PreparedUpdatePackage package, Action<IncrementalUpdateProgress> progress, CancellationToken ct)`, `Activate(StagedUpdate update)`, `Confirm(string root,string transactionId)`, `Rollback(string root,string transactionId)` and guarded owned-version cleanup. These are synchronous off-UI operations; the process worker owns the thread-affine root mutex, avoiding awaiting while owning it. ReadState may return null for legacy installation.
- Stable launcher project src/SiloPlayer.Launcher; installer renames its apphost to root SiloPlayer.exe while retaining its distinct managed assembly name. Launch the canonical active version with ArgumentList and unchanged deep-link arguments, as caller. It coordinates on registered root, supports pending boot recovery and exits after dispatch.

## Task 1: Trusted manifest, file pack and changed-file preparation

**Files:** new Models/Updates/Incremental*.cs, UpdateManifest.cs; Services/Updates/Incremental*.cs, UpdatePackBuilder.cs; modify AppRelease.cs and GitHubReleaseReader.cs only for additive compatibility; new IncrementalUpdateTests.cs and pack-builder CLI under tools/SiloUpdatePack.

- [ ] Write failing tests for manifest identity/bounds/paths, skipped-version reuse, corrupt same-size local files, exact range validation/resume, cancellation, raw/full-range fallback and compressed-size/hash limits.
- [ ] Implement fixed contracts; validate trust before paths/I/O, exact ranges before body reads, per-object hash/decompression limits and complete target validation. Resume identity binds manifest and pack. Preserve existing legacy release behavior and metadata ceilings.
- [ ] Implement reproducible pack builder from client/bootstrap directories into a fresh output; validate same schema used by clients. The elevated caller must refetch authority independently. Range-unsupported fallback downloads/verifies the pack explicitly, not an early-closing legacy installer.
- [ ] Run focused red/green tests under mutex; record network bytes and absence of unchanged object requests. Leave source uncommitted.

## Task 2: Immutable version staging, activation journal and launcher

**Files:** new SideBySideInstallation*.cs/SideBySideState.cs in Core; src/SiloPlayer.Launcher/*; focused side-by-side tests. Do not edit Task1 files.

- [ ] Fail tests for staging while a synthetic current client is alive, corrupt/link paths, incomplete/disk-failed copies, stale transactions, locked bootstrap and activation rollback.
- [ ] Implement safe local copying from verified open handles, fresh owned version/staging trees and bounded atomic state/journals. Keep active version until final activation. Bootstrap replacement preflights locks and has recoverable backup; never overwrite the running client's directory.
- [ ] Implement launcher canonical-root resolution/argument forwarding and pending readiness handling. No private credential snapshots or root recursive cleanup. Keep active/previous confirmed versions, preserve foreign additions, defer unused-version cleanup when identity cannot be checked.
- [ ] Run focused tests; provide explicit host integration notes for root registry/mutex ownership and boot receipt. Source stays uncommitted.

## Task 3: Finite updater host, authority and continuous progress

**Files:** src/SiloPlayer.UpdateHost/* (new WinUI host); MainWindow.Updates.cs, WindowsAppUpdateInstaller.cs, IAppUpdateInstaller.cs; AppUpdateService.Download.cs; App.xaml.cs; Core AppUpdateInstallGate.cs root integration after Task2 coordination.

- [ ] Define authenticated app/host/worker pipe protocol with process/start-time/image/root identity, bounded messages and explicit transaction states. Use trusted installed host code; do not elevate cache code or accept caller digest as authority. Worker independently fetches the authoritative manifest, then holds the canonical-root mutation mutex on one thread.
- [ ] Test UAC denial, helper death, stale PID, missing confirmation, poisoned cache and concurrent stage/activation before production integration. Keep prior working installation on failure.
- [ ] Extend service with prepared-package state while retaining full-installer legacy path for compatible callers/fixtures. New side-by-side installs use changed-file staging. Main window closes only after host reports a fully installed target and visible handoff UI; normal-close marker and playback save precede activation.
- [ ] Show clean WinUI progress with real stage/file/byte data, cancellation only when safe, and visible failure/retry. Do not open the client after automatic normal exit. Confirm on next user launch; manual host waits for local UI readiness and preserves one-time success toast. Detect actual startup failure, not server/network failure.
- [ ] Add native centering/bounds/ownership/stage tests at normal/maximized/translated/scaled owner rectangles; no control of installed player.

## Task 4: Subtitle switching preserves playback intent (independent)

**Files:** actual PlayerService subtitle/selection callers, Player-layer subtitle primitives as justified; focused unit/native fixtures. No updater files or native Program routing without root coordination.

- [ ] Reproduce the reported pause through actual track/Off entry points; inspect text/sidecar/burn-in and temporary pauses/room authority rather than guessing.
- [ ] Implement the smallest ownership-safe correction; preserve already-paused state and current user/room pause changes, failure recovery and track preferences.
- [ ] Verify playing and paused selections, Off, rapid changes and relevant burn-in/recovery/native controls. Capture red/green receipts; source uncommitted.

## Task 5: Installer migration and publication assets

**Files:** installer/build.ps1, SiloInstaller.iss, pack CLI invocation, owned inventory/uninstall integration, installer runtime fixtures.

- [ ] Build client + finite host into versioned payload and stable launcher bootstrap into root; full migration must satisfy old-helper file-version checks and existing registered identity. Preserve guarded destinations and logged/owned-file-only uninstall.
- [ ] Keep protocol/shortcut paths stable and authorize staged version's SiloCast image without removing live old version's firewall authorization. Retire rules only after success. All fixtures use unrelated IDs, lowest privileges and dummy payloads, omitting production side effects.
- [ ] Generate complete manifest/pack/full installer from the same sealed payload in fresh output; test old-client metadata compatibility, registration/root resolution, fresh/legacy upgrade, next update while client remains alive, uninstall foreign-file preservation and recovery.

## Task 6: Independent review, regressions and owner-test delivery

- [ ] Review the complete source/callers/security/installer lifecycle independently. Fix concrete findings with red/green tests; do not certify elevated production setup from synthetic fixtures.
- [ ] Run relevant Windows/native/published playback and consumer checks, preserving evidence/source fingerprints. Include current subtitle and updater state/visibility regressions.
- [ ] Itemize behavior changes, choose deliberately newer matching stamps, build fresh final installer/payload/manifest/pack, verify identical bytes and local delivery receipt. Document legacy one-time migration and measured changed-file bytes.
- [ ] Hand owner the local installer for testing. No commits, source push or release publication yet. Clear foreground integration lock with a current handoff; do not let audit automation merge over dirty feature work.

## Execution rulings and traceability

The owner explicitly approved the design and requested building; proceed with engineering execution without another plan-approval round. Native root coordination plus parallel disjoint independent domains uses the existing harness, with independent final review. The design's automatic-exit boot confirmation waits for next manual launch; rollback does not copy/restore credentials or overwrite shared user progress. Exact interfaces above are the inter-agent contract; changes require root coordination and a ledger entry.
