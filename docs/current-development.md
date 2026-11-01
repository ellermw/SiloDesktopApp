# Current development — 1.2.517

## Opt-in Beta update channel (PR #62)

PR #62 adds Stable-default, opt-in Beta updates. The source is stamped **1.2.517**, with numeric AssemblyVersion/FileVersion **1.2.517.0**, in the app and update helper projects; the installer defaults match. This is a stable source identity, not a `1.2.523-beta.1` package. The PR base is `fdccad18` (abbreviated commit ID).

The implementation includes persisted channel preferences, semantic release ordering and identity validation, bounded GitHub catalog discovery, same-MAJOR offers without silent downgrades, channel cancellation/cache retirement, direct skipped-version hash reuse, explicitly verified full migration and semantic version/channel presentation.

See the [release contract](update-release-contract.md) for canonical tags, GitHub flags, the five public assets, semantic-to-Windows version mapping and rollout requirements. Source stamps do not establish publication status or prove that an installer has been built, sealed or installed. Build/test results for this PR must come from its pipeline; machine-local candidate receipts are not evidence for this source.

## Compatibility and rollout

Stable remains the default for existing and fresh preferences. Installing a Beta manually does not select the Beta channel. Existing clients need a channel-capable client and matching version-local helper before opting in. Compatible incremental updates keep the Protocol1 root bootstrap immutable, preserve prior versions and user data, and reuse only hash-verified files. Full migration requires the existing verification and manual-consent gates.

The update dialog retains its bounded scrolling body and fixed actions. Channel changes retire prior offers and prepared packages while preserving shared authority cooldowns. Checks wait for overlapping checks and other short operations, but return immediately without changing state when a download is active or pending. Canceling a queued check leaves the active operation untouched; checks read the channel epoch after acquiring the operation gate. Switching from an ahead-of-Stable Beta never downgrades the installation.

## Historical release documentation

The [1.2.516 release ledger](releases/1.2.516.md), [1.2.515 handoff](archive/2026-10-29-release-1.2.515-handoff.md), [1.2.514 handoff](archive/2026-10-29-release-1.2.514-handoff.md) and [earlier handoffs](archive/2026-10-29-development-handoffs.md) describe historical releases, not this PR's source identity or verification status.

## Approved Beta Task E — bounded Home rows (2026-11-01)

Task E deliberately uses Beta base `a15621a0d6d5960d6b31a620a23ae259d734dedb`, whose unchanged source version is **1.2.519**, newer than the historical 1.2.517 context above. Branch `codex/server-home-window` implements advertised first-20 Home row reads and complete recovery against official Silo main `63ef34b236b88dc77acca1a9a3ed90235377691f`. Featured and expanded rows read whole. Stable completion preserves order, removals, duplicate priority, cache ownership and Undo boundaries.

The final full unit suite passed **2,817/2,817**, the fresh Release app publish passed, and final native verification passed. Candidate `SiloPlayer.dll` SHA256: `EAD30AC02DAA9FCF86B32D1724E8D5CF95CBDA35A37A300F03F87FB428BF648B`. The final `home-row-window` runner exercised the new actual carousel/Dismiss/Undo cases and existing Home ownership, hero and presentation regressions against those same published bytes. Receipts are in `task-e/final-full-green`, `task-e/final-release-publish` and `task-e/final-native-control` below the evidence root.

See [Task E handoff](superpowers/handoffs/2026-11-01-bounded-home-rows.md). Detailed receipts and exact committed source identity are under `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-e-report.md`. This candidate has not been pushed, packaged as an installer, published, installed or used to restart the installed player. Historical release receipts remain unchanged.

### Task E review corrections — current local candidate

Independent review of `6a5c331b` identified queued-read ownership, cached hero promotion, stale Undo refresh, mixed metadata-language completion, capability timeout fallback and unseen series-member dismissal gaps. All were reproduced on the frozen earlier candidate, then corrected without changing source version 1.2.519. Queued reads carry their initiating API context and language through context-bound sends; the layout owns featured reads; Undo retires earlier section generations; completion replaces stale-language text and filters the whole-show dismissal predicate across pending reads.

Current verification: **2,819/2,819 unit tests**, fresh Release publish, eight review-path native cases (including the same-account token-refresh negative control), and existing row/current-Home/real Next Up dismissal native suites passed. Current `SiloPlayer.dll` SHA256: `B803FC4EDF0BF79A374D62E71118675B9A92FF72770679B7293D903C4B25F3F3`. Receipts: `task-e/review-full-green`, `task-e/review-final-release-publish`, `task-e/review-session-green`. Earlier `EAD30AC0…` artifacts and receipts remain frozen historical evidence. No remote mutation, installer or installed-player control occurred.

### PR68 cloud follow-up — current local candidate

The four exact cloud findings on reviewed head `abb6223c` were reproduced and corrected: JSON null section responses preserve mounted row/hero fallback, null optional capability documents use uncapped reads, transient capability fallback is shared across pending/queued rows until the next layout refresh, and queued mounted-collection checks run on the UI dispatcher. The existing owner, language, cancellation, generation and reconciliation guards remain in force. Source remains **1.2.519**; parent owns the Beta PR's remote delivery.

Fresh verification passed **2,825/2,825 unit tests**, Release publish and the native `home-row-review` run (11 review cases plus existing carousel, Home ownership/hero and real menu/Undo suites). Current app DLL SHA256: `EBD01E208EAA49DC20869316306EC7C232C51A702400735C63F81F4B4FC26CFA`. Receipts are `home/cloud-full-green`, `home/cloud-release-publish` and `home/cloud-native-green` below `D:/SiloPlayer/.codex-tmp/server-align-nov01`. The same published bytes are verified in the native runner; final source/product fingerprints still match. Frozen `B803FC4E…` and `EAD30AC0…` apps and receipts remain unchanged. Detailed thread dispositions, red evidence, original/final fixture hashes and final commit binding are in `task-e-report.md`. No installer, startup/receiver/updater, real account, user data or installed-player control was used.
