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

## November 1 episode still quick-play candidate

Independent `codex/server-episode-quickplay` candidate starts from newer Beta `5a6555492801aa0172d2559257ef8711a8e0efe4`, with pipeline-owned source stamp **1.2.519**. Official Silo server/WebUI main was freshly confirmed at `63ef34b236b88dc77acca1a9a3ed90235377691f`. The episode grids and sibling cards gain a separate accessible Play/Resume action based on authorized readable-file fields. It reuses normal scoped playback/resume preparation and rejects departed card/navigation/API owners across queued playback gates.

Synthetic local verification: main suite **2,799/2,799**, Release publish successful, and actual-control native run `quickplay/final-native` passed for series/season/sibling builders, keyboard invocation, separate detail navigation, library/profile/resume requests, omitted zero override, readable/missing files, 128/220/400px non-overlapping action rectangles, spoiler blur/overview, stale queued scopes and existing room transport behavior. Release/native app DLL SHA256 is `6D088A603C6C798EDC2296CBEAEA63E2EC09819DBDB7C188B8AC34709C4C1470`; receipts/logs/screenshots are under `D:/SiloPlayer/.codex-tmp/server-align-nov01/quickplay`. Full task report is `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-f-report.md`.

This is a Beta source candidate awaiting independent review and parent-managed delivery, not a release or installer receipt. No installer was built/executed and the installed player was not controlled. Native requests use synthetic in-memory responses and stop at an intentional server denial before media load; real account/media playback and complete visual parity remain unverified.

### Episode quick-play review corrections

The correction pass stacked on `8e6226c31ddc700e8cf28b9a43ebd739cc9dec41` carries the captured playback owner across the transport drain and preparation into context-bound capability/start requests and retries, retires superseded viewer starts, and cleans up accepted cancellation with explicit loading/error surface ownership. Active-room clicks use existing room transport or detail staging/suggestion authority, preserving attachment and guest permissions. Pointer/focus visibility uses their union, and positive rewatch progress is labeled Resume.

Frozen corrected source passed **2,800/2,800** unit tests (`quickplay/reviewed-final-unit`), Release publish (`reviewed-final-app`) and the combined native suite (`reviewed-final-native`, exit0). Actual checks include held transport/watch/capability/start boundaries, resize/navigation cancellation, successor loading/error ownership, an actual manager-created/coordinator-attached room session, guest/host/lobby/vote paths and canceled staging, plus the original grid/resume/geometry/spoiler checks. Corrected app DLL SHA256: `0BD4733B60C71F8BA631773FDF756C6761D422092D81CB55E2FD588CB2C61305`; all managed artifact hashes/native-reference equality are recorded in `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-f-reviewed-artifacts.json`, with exact product source fingerprints in `task-f-frozen-product.json`. Earlier `6D088...` baseline evidence remains frozen and is superseded for the current candidate.

All .NET work used the external mutex runner with fresh worktree-local outputs. The isolated native shell used synthetic in-memory APIs, result-root settings, disabled SiloCast discovery and no updater/installer service. Intermediate failures, including the untouched synthetic updater parent-identity check, remain in the task report; the final complete suite passed. Review P3 click-to-first-frame timing is deferred (no authorization/resume/room-flow impact). Source stamp remains pipeline-owned 1.2.519; no release, installer, production or installed-player action occurred.
