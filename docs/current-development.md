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

## 2026-11-01 — Match / Split library scope source handoff

Task C retains source **1.2.519**, deliberately newer than the historical release context above, on `codex/server-match-scope` from `a15621a0d6d5960d6b31a620a23ae259d734dedb`. Official server/WebUI reference: `63ef34b236b88dc77acca1a9a3ed90235377691f`, including the library-scope behavior introduced by `cb73e114bd09ba0bc748a156f0f129b4bd12822e`. See the [scope specification](superpowers/specs/2026-11-01-match-split-scope.md) and [implementation plan](superpowers/plans/2026-11-01-match-split-scope.md).

Detail Match launches now pass their library. Administrator Match and Split derive choices from complete item file inventory, carry one captured scope through search/selection/apply or preview/commit, and retire stale candidates/plans on membership, selection, profile/account, permission or navigation departure. Curator Match retains supplied/unscoped server resolution without reading administrator inventory; Split requires acting-admin, matching the authoritative endpoint policy. Successful empty inventory preserves the server's unscoped default behavior; failed reads and explicitly departed page scopes block operations. Inventory is revalidated at operation boundaries and through an explicit Refresh command; there is no new periodic poll.

Fresh verification under `SiloOct18Build`: **2,815/2,815 unit tests passed** (`match/full-unit-final`); Release app publish succeeded (`match/publish-final`); actual-control `match-split-scope` plus existing media-tools native fixtures passed (`match/native-final-02`). The native run includes delayed replies/permission revoke-and-restore, Split commit owner departure, 2,500-file virtualization and narrow footer bounds (Split shell 428×626). The same published `SiloPlayer.dll` SHA256 was verified by publish and native receipts: `D92E323C876ECE88FDD0C514B85BCEC064A9460FC310C9E7AACC8C71A2513CB1`.

Full local evidence and the preserved red/green history are in `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-c-report.md`, with receipts below its `match` directory. An earlier full run had an unrelated synthetic update-fixture “Wrong parent identity” failure; the final full run passed it. Existing nullable/compiler analyzer warnings remain documented in the logs. These are local synthetic source checks, not a release or real-server interoperability certification. No installer, real metadata operation, installed-player control, push, merge or publication occurred.

## 2026-11-01 — Match / Split independent review corrections

The first source handoff above remains historical. Independent review at `4a4736b6` identified five supported behavior issues, reproduced by six actual-control cases against that first published app. The corrections separate unmatched Split inventory/selection validity from identity-search eligibility; bind card Match to its launching surface through delayed reads and Apply; use the definitive detail reload after mutation; expose library choices only where they can affect scope; and preserve initial inventory retry with readiness recovery. Split now reuses the canonical captured-context Match search entry point rather than duplicating its route.

Current corrected source has **40/40 focused tests** and **2,817/2,817 full unit tests passing**, zero skipped (`match/review-focused`, `match/review-full-unit`). Its fresh Release publish succeeded (`match/review-publish`), with app DLL SHA256 `BDED834862A2DB9FD4870B6C72B8BDC61D60DB485937A00BD51B354942CF856D`. Product inputs have not changed since that publish; fingerprints are recorded in `D:/SiloPlayer/.codex-tmp/server-align-nov01/match/review-source-inputs.json`.

The full native rerun **passed**: `match/review-native-02`, against `.codex-tmp/match-review-publish-client`. It passed the original scope cases, all six review cases and existing media-tools virtualization/footer checks, with the completion sentinel and the identical `BDED834862A2DB9FD4870B6C72B8BDC61D60DB485937A00BD51B354942CF856D` app DLL hash. The prior recovery-test realization failure was corrected with a bounded row wait in test code only. The native run completed during parent handoff; no further verification was queued. The runner/receipts and detailed supported-finding responses remain in `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-c-report.md`. No further checks are queued by this implementer. No push, release, installer or installed-player action occurred.
