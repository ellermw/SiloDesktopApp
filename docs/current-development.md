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
# November 1 — marker source/confidence Beta PR candidate

Independent candidate on `codex/server-marker-details`, based on Beta `a15621a0` and official server/WebUI `63ef34b236b88dc77acca1a9a3ed90235377691f`. Source stamps remain 1.2.519; the Beta pipeline owns release stamping. This is not a published release.

Current watch `start_seconds/end_seconds` now deserialize alongside legacy ranges; optional source/provider/confidence/algorithm survive watch and live replacement. Revision24 typed settings expose two default-off switches. Active profile/device/authority reads and serialized setting writes reject retired owners; failed queued changes restore the last accepted value per key. Native skip controls and OSC share the official label vocabulary. A separate escaped cosmetic OSC message preserves countdown, pending seek and undo ownership.

Verification under `D:/SiloPlayer/.codex-tmp/server-align-nov01/markers`: actual wire red/green; Lua3 missing-handler/raw-markup red then escaped rendering/clear-state green; focused18 passed; full-main-final **2817 passed, zero failed/skipped**. Release app publish `corrected-native-app` passed, DLL SHA256 `E2429EC86563B580B441E9FAA29B65AA06C48FABA239930AFC1382BE5D888BF6`. Actual synthetic native controls in `native-corrected` pass setting persistence, queued same/different-key failure rollback, repeated/manual/absent marker data, profile retirement and label rendering. The native screenshot was visually inspected: the details line fits above Skip Intro. The initial native fixture's authoritative-position setup was corrected; its original/final hashes and the reproduced rapid-toggle defect are retained in the task report. No product input changed after the corrected Release build; only fixture checks and documentation followed.

All .NET commands held SiloOct18Build and used fresh isolated outputs after pinned native dependency verification. Known existing warnings remain. Independent Standards/Spec review and Beta submission are next. No installer, installed app, real account/media or production endpoint was used; this does not certify complete visual parity or physical-device behavior.

