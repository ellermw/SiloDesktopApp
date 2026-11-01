# Current development — 1.2.519

## November 1 desktop follow-up

Verified remote stable release is `v1.2.519`, source `3415f0720538bf7217e194bc84ac38098404361f`; main `a26ff559de24fa654e21a11e3132a1b18295c7d8` adds README download links. Beta base `a15621a0d6d5960d6b31a620a23ae259d734dedb` has the same tree. The owner authorizes focused PRs into Beta; the pipeline owns review, merge, version stamping and beta publication. Stable/main promotion still requires approval.

Updater wording is separate [PR #64](https://github.com/ellermw/SiloDesktopApp/pull/64), head `8563dbbb22bf8f52ef43001d8f1f89bb40132b53`: window title and heading say **Updating Silo Desktop Player**. Its safe isolated startup smoke check fails against preserved 1.2.516 and passes against the newly built helper; independent Standards/Spec reviews found no issues. It does not exercise installation or the user's installed helper. Updates use the helper from the currently installed version, so new wording is observed on updates started after that helper has been delivered.

The current branch adds the owner-approved Library alphabet rail. Official server/WebUI main is pinned to `63ef34b236b88dc77acca1a9a3ed90235377691f`. See the [comparison and proposed PR boundaries](research/2026-11-01-server-desktop-gap-analysis.md), [design](superpowers/specs/2026-11-01-library-letter-navigation-design.md) and [plan](superpowers/plans/2026-11-01-library-letter-navigation.md). This is a focused source comparison, not a claim of complete visual parity or that every follow-up is implemented. The alphabet uses the capability-gated server index for exact positions, keeps query/profile/language ownership, and preserves bounded card virtualization.

Local evidence is under `D:\SiloPlayer\.codex-tmp\desktop-nov01`. An initial full test attempt used an external assembly output path; source-inspection fixtures walked up to the historical canonical checkout and read the wrong source. That run was stopped and preserved as invalid evidence; rerun outputs live beneath this worktree so source lookup resolves correctly. Installed Silo, user data, previous frozen packages and source worktrees are preserved. No production setup or app control is authorized by this work.

## Historical 1.2.517 channel implementation

## Opt-in Beta update channel (PR #62)

PR #62 adds Stable-default, opt-in Beta updates. The source is stamped **1.2.517**, with numeric AssemblyVersion/FileVersion **1.2.517.0**, in the app and update helper projects; the installer defaults match. This is a stable source identity, not a `1.2.523-beta.1` package. The PR base is `fdccad18` (abbreviated commit ID).

The implementation includes persisted channel preferences, semantic release ordering and identity validation, bounded GitHub catalog discovery, same-MAJOR offers without silent downgrades, channel cancellation/cache retirement, direct skipped-version hash reuse, explicitly verified full migration and semantic version/channel presentation.

See the [release contract](update-release-contract.md) for canonical tags, GitHub flags, the five public assets, semantic-to-Windows version mapping and rollout requirements. Source stamps do not establish publication status or prove that an installer has been built, sealed or installed. Build/test results for this PR must come from its pipeline; machine-local candidate receipts are not evidence for this source.

## Compatibility and rollout

Stable remains the default for existing and fresh preferences. Installing a Beta manually does not select the Beta channel. Existing clients need a channel-capable client and matching version-local helper before opting in. Compatible incremental updates keep the Protocol1 root bootstrap immutable, preserve prior versions and user data, and reuse only hash-verified files. Full migration requires the existing verification and manual-consent gates.

The update dialog retains its bounded scrolling body and fixed actions. Channel changes retire prior offers and prepared packages while preserving shared authority cooldowns. Checks wait for overlapping checks and other short operations, but return immediately without changing state when a download is active or pending. Canceling a queued check leaves the active operation untouched; checks read the channel epoch after acquiring the operation gate. Switching from an ahead-of-Stable Beta never downgrades the installation.

## Historical release documentation

The [1.2.516 release ledger](releases/1.2.516.md), [1.2.515 handoff](archive/2026-10-29-release-1.2.515-handoff.md), [1.2.514 handoff](archive/2026-10-29-release-1.2.514-handoff.md) and [earlier handoffs](archive/2026-10-29-development-handoffs.md) describe historical releases, not this PR's source identity or verification status.
