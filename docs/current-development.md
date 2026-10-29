# Current development — 1.2.514

Updated October 29, 2026. **1.2.514 is the current published release.** The README download, GitHub Latest release and app/launcher/updater/installer version stamps agree.

## Source and delivery

- [Public release](https://github.com/ellermw/SiloDesktopApp/releases/tag/v1.2.514) · [download/checksum](../README.md#download) · [three-change ledger](releases/1.2.514.md).
- Release tag/source: `v1.2.514`, commit `a1b65b2ab6f1e348e013f2c0bbe9f888aef3decd`.
- Build base: `b3d5efdbd2b8a3b54e5ed17553eb5757987c7c8d` plus the 1,592 hash-bound source inputs in the build receipt. Those inputs remained unchanged through verification and were committed in the release source; the base SHA alone does not describe the compiled correction.
- Delivery checkout: `C:\Users\Michael\.codex\worktrees\taskbar-identity\SiloPlayer`, branch `codex/taskbar-icon-identity`.
- The exact current main/documentation head, published source, reviewed heads and integration ownership are authoritative in `D:\SiloPlayer\.codex-tmp\pr-audit-watch-oct28\reviewed-prs.json`. Consult it before concurrent work.
- PRs #52–59 remain merged/closed with ten reviewed inline threads resolved. New test-only PR #60 arrived during delivery and is queued for independent review; it is not included in this release. Recheck its exact head and discussion before work.

## Changes and pinning

Three concrete changes over published 1.2.511 give 1.2.514: retain the bundled Silo window/taskbar icon instead of the server favicon; align process/window/installer-shortcut AppUserModelIDs; and give pins a stable registered-root launcher/icon target that survives version cleanup. In-app server wordmarks and branding remain available. Unregistered development builds retain their own executable target. Window-owned Shell properties clear on final close.

Windows can retain an old pin's shortcut metadata. After installing the correction, **unpin the old entry once, launch the updated app and pin it again**. Verification does not rewrite existing pins or restart Explorer. The owner's actual pin/unpin/relaunch flow remains for observation.

## Sealed installer and verification

Installer: `D:\SiloPlayer\.codex-tmp\taskbar-icon-oct29\installer514-final\installer\SiloInstaller-1.2.514-Setup.exe`.

Size: **235,199,330 bytes**. SHA-256:

```text
FF1FD4D9B0A23824EF6A5B0DD2EB258DF9D8B3D2D53F0AE4F07ED115896B5898
```

Frozen layout: `C:\Users\Michael\.codex\worktrees\taskbar-identity\SiloPlayer\installer\publish-1.2.514-taskbar-final` (1,026 files; 817 version-local client files). Preserve this layout, installer and evidence; never rebuild over them. App DLL SHA-256: `C67D216AB1AC4DF9AFE0A96C297FE17D3880CD56A7D949F31CC49E4CAF44A334`.

Passed **2,636 tests** (2,588 main Windows, 16 protocol, 32 staging/launcher), **50 native groups**, **33 published playback groups** and **three consumer compilations**. Independent Standards/Spec reviews have zero remaining material findings. All 1,592 source inputs and 1,026 packaged files were unchanged after verification.

The real hidden MainWindow regression reproduces seven failures on 1.2.511, then passes on the sealed correction: two synthetic server-branding loads/reset retain the Silo icon, no server favicon downloads occur, and Windows reads the stable ID/relaunch/name/icon properties. A separate real hidden window verifies property cleanup during close. Synthetic roots cover old/new version directories, stable launcher/icon, legacy root launch, missing launcher, development and unrelated-root fallbacks.

The first full unit run had one synthetic updater process-identity failure in an unchanged fixture. Its original evidence is preserved under `installer514-verified`; the focused four-case recheck and the fresh complete 2,588-test run pass. No app code was changed to suppress this failure.

Receipts:

- `D:\SiloPlayer\.codex-tmp\taskbar-icon-oct29\installer514-final\verification.json`
- `D:\SiloPlayer\.codex-tmp\taskbar-icon-oct29\installer514-final\native-receipts.json`
- `D:\SiloPlayer\.codex-tmp\taskbar-icon-oct29\publication514\public-verification.json`

All five public asset sizes/digests and the downloaded checksum match the sealed bytes. Exact published 1.2.464 code discovers 1.2.514 and downloads/verifies all 235,199,330 installer bytes, including public 200/conditional 304 cache retention. Exact delivered 1.2.484 Core reuses 807/817 files and fetches ten objects: 3,684,112 ranged pack bytes plus 222,910 manifest bytes (**3,907,022 response-body bytes**), no full-pack fallback, repeat zero objects. Exact 1.2.514 Core also retains the verified prepared cache across public 200/conditional 304 responses. No Authorization is sent, setup is not launched, and the installed app is untouched.

## Preserved behavior and limits

Retain centered update windows, preparation while the main app stays open until the final restart, subtitle popup continuity and complete replacement-load ownership, genuine pause/room/phone intent, avatar response authority/SVG outcomes, off-page room continuation, FIFO logging/explicit flush completion, whole-watchdog single-flight ownership, safe filenames/logical thumbnails, updater ETag/absolute cooldown and installer cleanup safeguards.

Legacy installs need one full installer migration; compatible 1.2.484 or newer installs reuse unchanged files. Protocol 1 root bootstrap stays immutable between full migrations. Preserve authenticated root/process/pipe checks, normal-close/save gates, immutable staging/prior-version recovery, owned-file-only cleanup, original-user restart, automatic no-reopen behavior and once-only success feedback.

Production elevated incremental staging/reopening, alternate-admin UAC, physical phone/hardware and complete visual parity remain unobserved. Synthetic checks do not establish them or a historical buffering cause. The sidebar cached-file SVG fallback remains outside PR #45 scope. The controlled relay replacement passes STOP to START with paused plan B and zero errors/recovery; historical noisy relay/HLS error ordering remains unverified. Publication never authorizes installing, closing, restarting or controlling the user's running app.

## History and future work

The [1.2.511 handoff](archive/2026-10-29-release-1.2.511-handoff.md) and [earlier development handoffs](archive/2026-10-29-development-handoffs.md) preserve historical details; they are evidence, not current release status or instructions. All older public releases/tags remain preserved, as requested by the owner.

Preserve the original dirty `incremental-updates` owner source, canonical `D:\SiloPlayer` diagnostics and every frozen package/evidence set. Candidate 1.2.505 has a source tag and rejected package but was never a public release; never publish it. Planned 1.2.507/508 were never built/published; 1.2.405 is superseded unpublished. Preserve the published 1.2.463 red baseline and wrong-stamped local 1.2.430 diagnostics. PR #15 remains declined and closed unmerged: preserve phone control of PC-started playback.

Start future work from current main/release, not historical baselines. Use separate clean checkouts and hold `SiloOct18Build` for every .NET build/test. Never clear populated publish directories. Follow AGENTS.md delivery rules and count concrete behavior changes, not builds, assertions or fixture fixes.
