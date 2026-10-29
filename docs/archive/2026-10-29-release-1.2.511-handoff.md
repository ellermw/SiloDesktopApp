# Current development — 1.2.511

Updated October 29, 2026. **1.2.511 is the current published release.** The README download, GitHub Latest release and app/launcher/updater/installer version stamps agree. This documentation cleanup changes no app behavior or release bytes.

## Source and delivery

- [Public release and assets](https://github.com/ellermw/SiloDesktopApp/releases/tag/v1.2.511) · [download/checksum](../README.md#download) · [47-change ledger](releases/1.2.511.md).
- Release tag/source: `v1.2.511`, commit `a7a4e3d9623e183d756a9238c240765604b33bad`.
- Compiled source: `3e3b0a74cceede37f9623a0d17619655d9105183`. Later release-record/documentation commits do not alter the sealed payload.
- Current source: GitHub `main`. Its exact documentation head, reviewed PR heads and integration ownership are recorded in `D:\SiloPlayer\.codex-tmp\pr-audit-watch-oct28\reviewed-prs.json`; consult that marker before starting concurrent work.
- Delivery checkout: `C:\Users\Michael\.codex\worktrees\audit-oct29-batch\SiloPlayer`. PRs #52–59 are merged and closed; all ten reviewed inline threads were resolved. Recheck incoming PRs against their exact heads.

## Sealed installer and verification

Installer: `D:\SiloPlayer\.codex-tmp\pr52-59-audit-oct29\installer511-final\installer\SiloInstaller-1.2.511-Setup.exe`.

Size: **235,230,544 bytes**. SHA-256:

```text
BF40B61C42274B38B62FDB5D9E5A5A44A894511A523268D1C00F57289778CCD2
```

Frozen layout: `C:\Users\Michael\.codex\worktrees\audit-oct29-batch\SiloPlayer\installer\publish-1.2.511-audit-final` (1,026 files; 817 version-local client files). All five public assets match the sealed sizes/digests. Preserve this layout, installer and evidence; do not rebuild over them.

The release passed 2,588 main Windows tests, 16 protocol tests and 32 staging/launcher tests (**2,636 total**), **47 native groups**, **33 published playback groups** and **three consumer compilations**. Source and package fingerprints are recorded in:

- `D:\SiloPlayer\.codex-tmp\pr52-59-audit-oct29\installer511-final\verification.json`
- `D:\SiloPlayer\.codex-tmp\pr52-59-audit-oct29\publication511\public-verification.json`

The exact 1.2.464 updater discovered 1.2.511 and downloaded/verified the public installer. The delivered 1.2.484 Core prepared 1.2.511 using 807 reused files and ten changed objects: 3,675,253 ranged pack bytes plus 222,910 manifest bytes (**3,898,163 response-body bytes**), without full-pack fallback. Repeated preparation downloaded zero objects. Exact 1.2.484 and 1.2.511 Core checks retained prepared caches across public 200/conditional 304 responses. The legacy conditional probe's stdout label is inaccurate; the receipt identifies its actual 1.2.464 host and records the separate exact-1.2.511 check.

## Behavior and verification limits

This release retains centered update windows, preparation while the main app stays open until the final restart, subtitle popup continuity, complete replacement-load subtitle ownership, genuine pause/room/phone intent, and the earlier updater cooldown/cache and installer safety protections. Official server/WebUI reference during this audit: `cc8514943289045e7b9f7ac8d36d4968bfe93926`.

Legacy installations require one full installer migration. Compatible 1.2.484 or newer installations reuse unchanged files. Protocol 1 root bootstrap files remain immutable between full migrations. Preserve authenticated root/process/pipe checks, normal-close/save gates, immutable staging and recovery, owned-file-only cleanup, original-user restart, automatic no-reopen behavior and once-only success feedback.

The owner reported 1.2.484 installed and in use. Production elevated incremental staging/reopening, alternate-admin UAC, physical phone/hardware interoperability and complete visual parity remain unobserved. Synthetic verification does not establish those results or a historical buffering cause. The sidebar cached-file SVG fallback remains outside PR #45 scope. Publication does not authorize installing, closing, restarting or controlling the user's running app.

## Historical candidates and preservation

The [previous development handoffs](archive/2026-10-29-development-handoffs.md) preserve the original source, hashes, failures and receipts verbatim. They are historical evidence, not current instructions or release status. The archived document's SHA-256 before this cleanup is `C1211010339FD5A22ECD26E3431091DCA6A6AD1FEF597E43EFA0D766A70A1DA5`.

- **1.2.505:** unpublished rejected candidate; its source tag exists, but no GitHub release was published. Additional subtitle corrections are included in 1.2.511. Preserve its frozen installer/payload and `installer505-final\UNPUBLISHED-DO-NOT-RELEASE.md` under the audit evidence root.
- **1.2.484:** historical owner-test delivery, incorporated into 1.2.511. Preserve the original dirty `incremental-updates` worktree and `D:\SiloPlayer\.codex-tmp\incremental-updates-oct29\installer484-final`.
- **1.2.507/508:** planned versions that were never built or published. **1.2.405:** superseded unpublished baseline, never publish. Preserve the published 1.2.463 red baseline, wrong-stamped local 1.2.430 diagnostics and all earlier frozen evidence.
- PR #15 was declined and closed unmerged. Preserve phone control of PC-started playback.

Start future work from current main/release, not archived candidates. Preserve dirty canonical `D:\SiloPlayer` work and diagnostic evidence. Use separate clean checkouts and hold `SiloOct18Build` for every .NET build/test. Never clear populated publish directories. Release delivery must follow AGENTS.md; count concrete behavior changes in the ledger, not builds or assertions.
