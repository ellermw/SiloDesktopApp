# Current development — 1.2.515

Updated October 29, 2026. **1.2.515 is the current published release.** README download, GitHub Latest and app/launcher/updater/installer stamps agree.

## Source and delivery

- [Public release](https://github.com/ellermw/SiloDesktopApp/releases/tag/v1.2.515) · [download/checksum](../README.md#download) · [one-change ledger](releases/1.2.515.md).
- Build and release tag/source: `85554e896b14f53872c2dab4bb9f0d091f73b40c`, tag `v1.2.515`.
- Main includes test-only PR #60 at reviewed head `108944dce8a7ed1fe6177d0e37b55328dd45e6d4`, merge `e303efe89decc9b2364fad79310fe03c0d58b392`. Its two test-file changes leave all shipped app inputs/bytes unchanged. The release tag identifies the exact compiled app source.
- Delivery checkout: `C:\Users\Michael\.codex\worktrees\updater-window-icon\SiloPlayer`, branch `codex/updater-window-icon`. PR #60 also has a separate exact-head review checkout at `C:\Users\Michael\.codex\worktrees\pr60-test-waits\SiloPlayer`.
- Exact current main/documentation head, published source, PR heads/statuses and integration ownership are authoritative in `D:\SiloPlayer\.codex-tmp\pr-audit-watch-oct28\reviewed-prs.json`. Consult it before concurrent work.

## Updater window icon and observation limits

One behavior change over published 1.2.514 gives 1.2.515: the separate Updating Silo window explicitly uses the bundled Silo icon for its small title-bar and large taskbar icons. Its executable already embedded the icon; the WinUI window had no explicit icon. Resolution uses the loaded helper assembly's sibling client Assets directory, not the caller's working directory. Cosmetic icon failures log only their exception type and allow updating to continue.

**An update uses the currently installed helper.** The window installing 1.2.515 may still display an older generic icon. Updates started after the corrected helper is installed use the Silo icon. This does not require a new bootstrap migration for compatible versioned installations.

The owner supplied an actual installation-progress screenshot showing Installing alongside current version and the generic icon. This establishes a visible production progress phase; successful completion/reopening, the corrected icon in the owner's updater, and alternate-admin UAC are not inferred from the screenshot.

The main app's fixed Silo icon and taskbar identity are retained. Existing pins created before that correction may need one manual unpin/re-pin: launch updated Silo and pin it again. Existing pins and Explorer are never modified by verification.

## Sealed installer and checks

Installer: `D:\SiloPlayer\.codex-tmp\updater-window-icon-oct29\installer515-final\installer\SiloInstaller-1.2.515-Setup.exe`.

Size: **235,212,584 bytes**. SHA-256:

```text
58E7CD3908B699B9AC7430501CD58203E80B53855C6011B6DF5D1125E7254E73
```

Frozen layout: `C:\Users\Michael\.codex\worktrees\updater-window-icon\SiloPlayer\installer\publish-1.2.515-updater-icon-final` (1,026 files; 817 version-local client files). App DLL SHA-256 `E3823BB818540801B3EC30A5006B0389EDFF60094146E58E324A2EFE170DF167`; updater DLL `EF54599787A114E8DFBBA57423207DA6D30044E17FD1606BA26E1455BE2924D6`. Preserve the layout, installer and all evidence; never rebuild over them.

The exact app build passed **2,636 tests** (2,588 main Windows, 16 protocol, 32 staging/launcher), **50 native groups**, **33 published playback groups** and **three consumer compilations**. Standards/Spec reviews have zero remaining material findings. All 1,592 source inputs and 1,026 packaged files were unchanged through this verification.

The actual 1.2.514 progress view fails the new regression with no real window icon. Corrected isolated and sealed 1.2.515 views pass actual Windows small/large icon bitmap comparisons against the bundled Silo icon, plus existing owner centering, monitor bounds, progress, error presentation and actionable-control checks. No worker/elevation/installer is started by the view fixture. Original red evidence and fresh prototype green inputs are retained separately; the prototype scope/restore receipt records the temporary-input handling.

PR #60 widens four bounded signal/completion waits from five to thirty seconds. Assertions and immediate passing completion are unchanged. Both independent reviews have zero findings; all discussion and inline threads were read, with a fresh exact-head/zero-unresolved gate before push. **25/25 Windows tests** pass at its exact head, and **25/25** pass on combined source. Original/final hashes of its two test-only edits and scoped re-verification are recorded. No app behavior/version increment is assigned to these changes. Native/published app evidence remains valid because all product inputs and sealed bytes are unchanged; historical thread-pool starvation is not independently established as the cause.

Receipts:

- `D:\SiloPlayer\.codex-tmp\updater-window-icon-oct29\installer515-final\verification.json`
- `D:\SiloPlayer\.codex-tmp\updater-window-icon-oct29\installer515-final\native-receipts.json`
- `D:\SiloPlayer\.codex-tmp\updater-window-icon-oct29\pr60-verification\receipt.json`
- `D:\SiloPlayer\.codex-tmp\updater-window-icon-oct29\pr60-combined\receipt.json`
- `D:\SiloPlayer\.codex-tmp\updater-window-icon-oct29\publication515\public-verification.json`

All five public asset sizes/digests and downloaded checksum match the sealed bytes. Exact published 1.2.464 code discovers 1.2.515, downloads/verifies all 235,212,584 installer bytes and retains the cache across public 200/conditional 304 responses. Exact delivered 1.2.484 Core reuses 807/817 files and fetches ten objects: 3,688,965 ranged pack bytes plus 222,910 manifest bytes (**3,911,875 response-body bytes**), no full-pack fallback, repeat zero objects. Exact 1.2.515 Core also retains the verified prepared cache across public 200/conditional 304 responses. No Authorization is sent. Setup is not launched and the installed app is untouched.

## Preserved behavior and limits

Retain centered keep-open updates, subtitle popup continuity and full replacement-load ownership, genuine viewer pause/room/phone intent, avatar authority/SVG outcomes, off-page room continuation, FIFO logging/explicit flush completion, whole-watchdog single-flight ownership, safe filenames/logical thumbnails, updater ETag/absolute cooldown and installer cleanup protections.

Legacy installs need one full installer migration; compatible 1.2.484 or newer installs reuse unchanged files. Protocol 1 root bootstrap remains immutable between full migrations. Preserve authenticated root/process/pipe checks, normal-close/save gates, immutable staging/prior-version recovery, owned-file-only cleanup, original-user restart, automatic no-reopen behavior and once-only success feedback.

Successful production incremental completion/reopening, alternate-admin UAC, physical phone/hardware, complete visual parity and the owner's corrected updater icon remain separate observations. The supplied screenshot verifies only the visible progress phase. Synthetic checks do not establish those other results or a historical buffering cause. Sidebar cached-file SVG fallback remains outside PR #45 scope. Controlled relay replacement passes STOP to START with paused plan B and zero errors/recovery; historical noisy relay/HLS error ordering remains unverified. Publication never authorizes installing, closing, restarting or controlling the user's app.

## History and future work

The [1.2.514 handoff](archive/2026-10-29-release-1.2.514-handoff.md), [1.2.511 handoff](archive/2026-10-29-release-1.2.511-handoff.md) and [earlier handoffs](archive/2026-10-29-development-handoffs.md) preserve historical details, not current instructions/status. All older public releases/tags remain preserved, as requested.

Preserve the original dirty incremental-updates owner source, canonical `D:\SiloPlayer` diagnostics, Claude memories/worktrees and every frozen package/evidence set. Candidate 1.2.505 has a source tag and rejected package but was never published: never publish it. Planned 1.2.507/508 were never built/published; 1.2.405 is superseded unpublished. Preserve published 1.2.463 red baseline and wrong-stamped local 1.2.430 diagnostics. PR #15 remains declined/closed unmerged: preserve phone control of PC-started playback. PRs #52–59 remain merged/closed with ten reviewed inline threads resolved; PR #60 is merged with final verification comment recorded in the marker.

Start from current main/release, not historical baselines. Use separate clean checkouts; hold `SiloOct18Build` for every .NET build/test. Never clear populated publish outputs. Follow AGENTS.md release delivery and count concrete behavior changes, not builds, assertions or fixture fixes.
