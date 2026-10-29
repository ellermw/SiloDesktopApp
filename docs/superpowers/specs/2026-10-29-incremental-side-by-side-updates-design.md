# Incremental side-by-side updates — design for owner review

Date: October 29, 2026. Baseline: published 1.2.464, main 1c9debd795c292d07ca44a7d2df9250ee63d3e4a. Status: approved by owner on October 29; implementation authorized, publication held for owner testing.

Implementation safety ruling: protocol-1 incremental manifests contain client files only. The managed launcher and its runtime remain immutable between full-installer migrations; replacing several bootstrap files cannot guarantee a runnable recovery selector after power failure. A differing bootstrap/protocol requires the full installer. Only a protected machine installation may elevate its installed updater; writable custom installations receive manual recovery guidance. These compatibility limits preserve the selected keep-open flow for supported incremental updates.

## What the owner selected

Download changed files instead of the full installer, show a clean update interface, and keep the main Silo window open until the final restart. Center update UI over the owning app. Preserve release notes, update notifications, manual Update Now, optional automatic download/install on normal exit, saved playback position and the existing post-update success message.

## Approaches

1. Stage downloads, then close Silo for an in-place replacement while a separate progress window remains. This is smaller, but does not keep the main window open throughout installation. The owner declined it.
2. Install each release into an independent version directory, leaving the current one intact, then activate/restart only after verification. The owner selected this approach.

This is a deployment/update project, not a small dialog adjustment. Keep .NET 8/WinUI 3 and the supported x64 Inno installer. Do not migrate to MSIX, add a privileged background service, or change the playback engine.

## User-visible flow

The centered Silo updates dialog shows the target version, changelog, download size and progress through Checking, Downloading changed files, Verifying, Installing alongside current version, and Ready to restart. It stays responsive; it does not pretend work is complete based on elapsed time. Errors include an actionable retry/defer message while preserving the working version.

Manual Update Now authorizes installation and the final restart. Keep Silo and any playback running while downloading/installing the new version; save playback state and close normally only for the final switch. Disable duplicate update actions, not the rest of the app. Respect existing background-download pauses for playback. Automatic mode continues to mean download in the background and install/select the pending version after a normal app exit, without reopening Silo. Boot confirmation waits for the next user launch; keep the prior verified version until that confirmation. This resolves the automatic-exit versus readiness distinction without changing the owner's saved preference.

For a manual update, show an independent progress window before closing the main window. It stays visible through the final switch/startup and closes after the new app signals local UI readiness. Restart without elevation. Preserve the existing successful-update message after restart. Center both dialog and handoff window on the app's client rectangle, clamped to the relevant monitor work area; verify moved, resized, maximized and scaled windows.

## Installation layout and migration

Keep the registered installation root, shortcuts and protocol paths stable. A small stable launcher at the existing SiloPlayer.exe path selects the active client from versions/<normalized-version>/. Client binaries, libmpv, resources and their runtime live within each version directory. They never read UI/resources from an independently updated version directory. The launcher exits after dispatch; installed client and updater host use .NET 8, and the progress host uses WinUI 3. No new third-party update framework is required.

Existing single-directory installations need one full installer update to introduce this layout. The current 1.2.464 helper cannot learn the new protocol without that upgrade. Continue publishing the ordinary full installer/checksum for older clients, fresh installs, repair and recovery. Validate the launcher file version expected by the old helper on the migration update. Settings, authentication, profiles and playback history remain in their existing user-data location.

Install new client files into a new private staging directory under the registered root; reuse unchanged installed bytes by local copy after hash validation. Seal/rename the completed directory before readiness. Keep the active version unchanged until final restart. Apply any bootstrap changes only in the final activation window, when no launcher process owns those files. All versions coordinate using the canonical registered root, not a version-specific executable directory.

## Changed-file distribution

Publish a small verified manifest plus a single Windows x64 content pack alongside the existing installer assets. Do not create hundreds of release assets: old clients cap release metadata at 512 KiB. The pack contains independently compressed file objects; the manifest binds each relative file path, raw SHA256/length and compressed object offset/length/hash. Equal content may share objects.

Read trusted release metadata from the existing fixed GitHub repository and require the manifest asset's GitHub SHA256 digest. Validate the bounded manifest before using paths or offsets. Compare destination entries against verified local files and fetch only needed pack ranges. This supports skipping releases without requiring a separate patch for every version pair. Resume verified partial transfers; bound concurrency and disk/decompression sizes. Reject incorrect ranges, changed identity, truncated/oversized data and hashes. Never execute a staged object until the complete target version verifies.

If the host does not support the required ranges or incremental data is absent/incompatible, offer a clearly described full-download fallback. Do not silently close the main window early to use the old installer: the new client must stage the full target payload side by side too. The existing full installer remains a manual recovery path when safe staging cannot proceed. UAC rejection or metadata rate limits defer installation with Silo still open.

## Worker, authority and activation

The app coordinates state; a finite updater host performs the registered-root filesystem work and reports progress. Elevate only the installation work that needs it. No always-running service, no credentials passed to the helper, and no Silo server infrastructure changes.

Treat user-writable cache, IPC and command-line input as untrusted. The elevated worker independently establishes manifest identity from the fixed GitHub release authority rather than accepting a caller-supplied digest as trust. It validates every source/destination, holds verified source handles through copy and validates the completed target. Use the installed trusted updater implementation; never elevate an arbitrary cache executable or script. Authenticate IPC to the expected user/process and restrict its commands to a particular registered installation and transaction. Installer signing requirements remain the existing QA policy; do not introduce an unmanaged signing secret.

A single installation-root transaction owns staging/activation. Persist bounded atomic transaction records so a crash can be distinguished from success. Wait for the exact old process identity and normal-close confirmation before switching the active pointer. Launch the new version as the original user. Commit success only when that process reports local UI readiness. A network/server outage alone is not startup failure. A process exit before readiness triggers recovery to the prior verified version; a slow still-running startup shows status/retry rather than killing the app or reporting success.

This feature must not change shared user-data formats. Binary rollback requires backward-compatible shared settings/cache contracts; a manifest requiring a breaking user-data migration is not eligible for automatic/incremental activation and needs a separately designed migration. Do not snapshot or restore credentials/user data through the updater, and never overwrite newer playback progress during recovery. This is an engineering choice to preserve the existing data contract, not a new owner requirement.

Keep both active and previous confirmed version. Cleanup only updater-owned, validated files in older version directories, only when unused; preserve unknown files and all user data. Make uninstall remove the updater-owned version inventory without a recursive installation-root wipe. Preserve the current install identity, guarded destinations, original-user launch, deep links, single-instance behavior and SiloCast Private/LAN firewall behavior. Stage any new-version firewall authorization without removing the running version's authorization; retire old rules only after successful activation.

## Verification and delivery

Before building, write the implementation plan and explicit interfaces; keep all source uncommitted and publication on hold for owner testing. Preserve the frozen 1.2.464 bytes and canonical dirty diagnostics. Use SiloOct18Build for .NET builds/tests and fresh outputs.

Prove unchanged large engine/runtime objects are not requested, including skipped-version updates. Exercise wrong hashes/paths/case collisions/reserved names/links, range errors, decompression limits, cancellation, disk exhaustion, rate limiting, cache tampering and competing transactions. Native/installer fixtures must prove the original synthetic client stays alive until target verification, normal-close activation/restart, startup failure recovery, interrupted transactions and guarded cleanup/uninstall. Use unrelated synthetic install IDs, roots and accounts; never run an installer against the owner's installed player.

Verify actual update window bounds and visible stage changes in isolated UI fixtures. Retain relevant full/native/published playback regressions and update security/installer checks. Count concrete product changes in an itemized version ledger, not tests/build attempts. Produce a matching local installer and measured download-size evidence for the owner to test before any publication request. Tests do not certify physical phone/hardware behavior or full WebUI parity.

## Builder decisions and implementation boundaries

The owner chose behavior and the side-by-side approach; the file-pack format, separate finite host and registered-root launcher are engineering proposals. Final code interfaces, exact manifest ceilings and launcher publishing details belong in the implementation plan after feasibility checks. Existing public updater signatures/reflection callers need explicit compatibility decisions. No second architecture or protocol layer is to be added unless required by one of the stated invariants.

Measured feasibility: sealed 1.2.463 versus 1.2.464 changed only four of 573 payload files, totaling 9,297,525 uncompressed bytes out of 264,007,794. This is not a compressed-transfer estimate. A read-only request to the current public installer returned HTTP206 and Content-Range bytes0-127/172799212; its body was not read. Receipts: D:\SiloPlayer\.codex-tmp\incremental-updates-oct29\sealed-payload-comparison.json and public-range-response.json.

Primary reference: GitHub release assets expose SHA256 digests (https://docs.github.com/en/rest/releases/releases). Existing GitHubReleaseReader already requires the installer digest and fixed repository URL. Existing sources: MainWindow.Updates.cs, WindowsAppUpdateInstaller.cs, UpdateOnExit.ps1, AppUpdateService*, AppUpdateInstallGate.cs, App.xaml.cs and installer/SiloInstaller.iss.

## Additional owner request — subtitle continuity

Switching a subtitle track or turning subtitles off during playback must not leave playback paused. Preserve the viewer''s pre-change play/pause intent, including already-paused and Watch Together authority cases; diagnose actual text/sidecar/burn-in selection paths and retain relevant track-selection recovery behavior. Include the verified repair in this local owner-test update.
