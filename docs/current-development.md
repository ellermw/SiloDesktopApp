# Current development — 1.2.516

Updated October 29, 2026. **1.2.516 is the current published release.** README download, GitHub Latest and app/launcher/updater/installer stamps agree.

## Source and delivery

- [Public release](https://github.com/ellermw/SiloDesktopApp/releases/tag/v1.2.516) · [download/checksum](../README.md#download) · [one-change ledger](releases/1.2.516.md).
- Exact compiled app and release tag source: `b96902c472ba3f407671431359f4e355df2ead4f`, tag `v1.2.516`.
- Delivery checkout: `C:\Users\Michael\.codex\worktrees\update-dialog-fit\SiloPlayer`, branch `codex/update-dialog-fit`. Later delivery documentation does not change the tested product bytes.
- Exact current main/documentation head, published source, PR heads/statuses and integration ownership are authoritative in `D:\SiloPlayer\.codex-tmp\pr-audit-watch-oct28\reviewed-prs.json`. Consult it before concurrent work.

## Update dialog correction and observations

One behavior change over published 1.2.515 gives 1.2.516: **Check for updates stays fully visible above the fixed Update now/Later footer**. Release notes, preferences and explanations scroll within a bounded body that adapts when the app window resizes. Live metadata/state refresh retains access to notes, publication date, release link, preference, status and progress. The size handler is removed and body references cleared when the dialog exits, including show failures. Existing centering and installation consent remain intact.

Published 1.2.515 reproduces the defect at 3840×2160: 33 of the 36 button pixels fall below the content viewport. Prototype and exact sealed 1.2.516 pass long-note checks at **3840×2160, 1920×1080, 1280×720, 900×540 and 460×700**, plus enlarged body text, preference focus/scroll access and live metadata rechecking. Assertions check actual control rectangles against clipping ancestors. Recorded raster scale is 1; body text was separately enlarged 1.5×, not presented as an observed high-DPI/system text-scaling test. The view fixture starts no download/installer/worker/elevation.

The public 1.2.515 release summary was shortened from 1,082 to 363 characters. Original metadata and unchanged tag/asset receipts remain at `D:\SiloPlayer\.codex-tmp\update-dialog-fit-oct29\release515-original.json` and `release515-summary-receipt.json`. This metadata-only edit adds no app behavior increment. New GitHub release summaries stay concise and customer-facing because the app displays them. The new layout takes effect after 1.2.516 is installed; short notes help existing clients during that update.

The owner's actual screenshot shows installed/running **1.2.514**, discovered 1.2.515 and the clipped control. It does not establish the installation/reopening route. The earlier screenshot establishes a visible production Installing alongside current version phase. Successful incremental completion/reopening, alternate-admin UAC and observation of the corrected updater icon on the owner's machine remain separate observations.

## Sealed installer and verification

Installer: `D:\SiloPlayer\.codex-tmp\update-dialog-fit-oct29\installer516-final\installer\SiloInstaller-1.2.516-Setup.exe`.

Size: **235206533 bytes**. SHA-256:

```text
25DE4EE8650B50C5A5E2B6D6BCBD708A1842DD2125A0652EF3FB1618D5F42E6E
```

Frozen layout: `C:\Users\Michael\.codex\worktrees\update-dialog-fit\SiloPlayer\installer\publish-1.2.516-dialog-fit-final` (1026 files; 817 version-local client files). App DLL SHA-256 `1B5E2E874D147A105E83684D850E30AF7D8ABFE1CAE11329209DF0B6FD59912B`; updater DLL `32915F456D92D58DF87570527CA1AE876F2864561BB536BB8EA16FFAA7B8C064`. Preserve this layout, installer and every earlier frozen artifact; never rebuild over them. The prototype515-client is an isolated diagnostic, not a release package.

The exact build passes **2636 tests** (2,588 main Windows, 16 protocol, 32 staging/launcher), **51 native groups**, **33 published playback groups** and **three consumer compilations**. Independent Standards/Spec reviews have zero material findings. All 1592 source inputs and 1026 packaged files remain unchanged through verification.

Receipts:

- `D:\SiloPlayer\.codex-tmp\update-dialog-fit-oct29\source-review.json`
- `D:\SiloPlayer\.codex-tmp\update-dialog-fit-oct29\red515\dialog-fit-red\receipt.json`
- `D:\SiloPlayer\.codex-tmp\update-dialog-fit-oct29\prototype-green515\dialog-fit-green\receipt.json`
- `D:\SiloPlayer\.codex-tmp\update-dialog-fit-oct29\installer516-final\verification.json`
- `D:\SiloPlayer\.codex-tmp\update-dialog-fit-oct29\installer516-final\native-receipts.json`
- `D:\SiloPlayer\.codex-tmp\update-dialog-fit-oct29\publication516\public-verification.json`

All five public asset sizes/digests and downloaded checksums match the sealed bytes. Exact published 1.2.464 code discovers 1.2.516, downloads/verifies all 235206533 installer bytes and retains its cache across public 200/conditional 304 responses. Exact delivered 1.2.484 Core reuses 807/817 files and fetches 10 objects: 3674096 ranged pack bytes plus 222910 manifest bytes (**3897006 response-body bytes**), no full-pack fallback, repeat zero objects. Exact 1.2.516 Core also retains the verified prepared cache across public 200/conditional 304 responses. No Authorization is sent. Setup is never launched and the installed player remains untouched.

## Preserved behavior and limits

Keep the fixed main Silo icon, stable process/window/shortcut AppUserModelID, registered root launcher/icon relaunch targets, safe development fallback and close cleanup. Old pins created before 1.2.514 may need one manual unpin/re-pin; existing pins and Explorer are never modified by verification. The separate progress window resolves the bundled icon relative to the loaded helper assembly's sibling Assets and continues on cosmetic failures. Updates use the currently installed helper, so corrected icons apply to updates started after 1.2.515's helper is installed.

Retain centered keep-open updates, subtitle popup continuity/early BeginMpvLoad retirement, latest manual choices and bitmap plan convergence, genuine viewer pause/room/phone intent and stale preference rejection. Preserve avatar authority/SVG outcomes, room joins/off-page continuation, FIFO logging/explicit flush completion, whole-watchdog single-flight/native publication, safe filenames/logical thumbnails, updater ETag/absolute cooldown and installer cleanup guards.

Legacy installs need one full migration; compatible 1.2.484+ installations reuse unchanged files. Protocol 1 bootstrap remains immutable between full migrations. Preserve authenticated root/process/pipe checks, normal-close/save gates, immutable staging/prior-version recovery, owned-file-only cleanup, original-user manual restart, automatic no-reopen behavior and once-only success feedback.

Physical phone/hardware, complete visual parity and historical recurring buffering need their own observed evidence. Sidebar cached-file SVG fallback remains outside PR #45 scope. Controlled relay replacement passes STOP to START with paused plan B and zero errors/recovery; historical noisy relay/HLS error ordering remains unverified. Publication never authorizes installing, closing, restarting or controlling the owner's app.

## History and future work

The [1.2.515 handoff](archive/2026-10-29-release-1.2.515-handoff.md), [1.2.514 handoff](archive/2026-10-29-release-1.2.514-handoff.md), [1.2.511 handoff](archive/2026-10-29-release-1.2.511-handoff.md) and [earlier handoffs](archive/2026-10-29-development-handoffs.md) preserve historical evidence, not current instructions/status. Older public releases/tags remain preserved, as requested. PRs #52–59 and #60 remain merged/closed; their exact review/comment/thread receipts remain in the marker and prior handoffs. PR #60 changes only bounded test waits, with no app behavior increment; its original/final hashes and 25 exact-head/25 combined tests remain recorded.

Preserve original dirty incremental-updates owner source, canonical `D:\SiloPlayer` diagnostics, Claude memories/worktrees and all frozen packages/evidence. Candidate 1.2.505 has source/tag and rejected package but was never published: never publish it. Planned 1.2.507/508 were never built/published; 1.2.405 is superseded unpublished. Preserve published 1.2.463 red baseline and wrong-stamped 1.2.430 diagnostics. PR #15 remains declined/closed unmerged: preserve phone control of PC-started playback.

Start from current main/release, not historical baselines. Use clean isolated checkouts and hold `SiloOct18Build` for every .NET build/test. Never clear a populated publish target. Count concrete behavior modifications/removals, not tests, assertions, fixture corrections, builds or packaging. Read all unresolved PR threads including outdated ones plus discussion before readiness and immediately before a separately inspected fresh-head/zero-unresolved push gate.
