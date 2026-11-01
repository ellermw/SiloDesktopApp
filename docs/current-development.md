# Current development — 1.2.519

## November 1 desktop follow-up

Verified remote stable release is `v1.2.519`, source `3415f0720538bf7217e194bc84ac38098404361f`; main `a26ff559de24fa654e21a11e3132a1b18295c7d8` adds README download links. Beta base `a15621a0d6d5960d6b31a620a23ae259d734dedb` has the same tree. The owner authorizes focused PRs into Beta; the pipeline owns review, merge, version stamping and beta publication. Stable/main promotion still requires approval.

Updater wording is separate [PR #64](https://github.com/ellermw/SiloDesktopApp/pull/64), head `8563dbbb22bf8f52ef43001d8f1f89bb40132b53`: window title and heading say **Updating Silo Desktop Player**. Its safe isolated startup smoke check fails against preserved 1.2.516 and passes against the newly built helper; independent Standards/Spec reviews found no issues. It does not exercise installation or the user's installed helper. Updates use the helper from the currently installed version, so new wording is observed on updates started after that helper has been delivered.

The current branch adds the owner-approved Library alphabet rail. Official server/WebUI main is pinned to `63ef34b236b88dc77acca1a9a3ed90235377691f`. See the [comparison and proposed PR boundaries](research/2026-11-01-server-desktop-gap-analysis.md), [design](superpowers/specs/2026-11-01-library-letter-navigation-design.md) and [plan](superpowers/plans/2026-11-01-library-letter-navigation.md). This is a focused source comparison, not a claim of complete visual parity or that every follow-up is implemented. The alphabet uses the capability-gated server index for exact positions, keeps query/profile/language ownership, and preserves bounded card virtualization.

Verified product head: `e344727663e8aa47a707a41b2ef3075aebfd3783`. Final main suite: **2,817 passed**, including 19 letter-navigation cases. Final isolated Release publish succeeds; five native cases pass: alphabet interaction/layout at 1280×1000 and 960×550, plus existing cursor scrolling at 1920×1080, 3440×1440 and 3840×2160. These exercise synthetic data and actual native controls. Independent Standards/Spec re-review has zero remaining findings. Existing nullable/analyzer warnings remain. Pre-review regressions reproduced slow-index blocking and independent count races; focused red/green receipts and the full rerun are preserved.

Diagnostic client `.codex-tmp/library-client-r3` remains version 1.2.519; it is not a release package and must not be uploaded. Its app SHA256 is `7647C4A35BAE68987B324B9E668358BD660DA997C65F5C2523B46893B9A153F2`. Final receipt: `D:\SiloPlayer\.codex-tmp\desktop-nov01\library-final3-receipt.json`; unit TRX: `library-tests-final2-results/main.trx`. Native receipts are under this worktree's `.codex-tmp/native-artwork-tests/2cdcc4d8990347e4a71c5a31ff19d18f` (letters) and `cf5978cde4fe43f18637b6a29468df3e` (cursor). The native runner stages the actual published Updates content in its own reference folder to satisfy WinUI content imports; original/final runner hashes and failed/corrected fixtures remain in `native-runner-correction.json`. Product publish outputs were not modified by fixtures. Subsequent documentation-only commits do not change these tested inputs.

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
