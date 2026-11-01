# Current development — 1.2.517

## Password session revocation — 2026-11-01 task handoff

Task branch `codex/server-password-sessions` deliberately starts from the approved newer source `a15621a0d6d5960d6b31a620a23ae259d734dedb`, stamped **1.2.519**. This section is a task handoff, not a new release or installer receipt. Official server/WebUI reference is `63ef34b236b88dc77acca1a9a3ed90235377691f` (`internal/apiv2/account.go` and `web/src/pages/settings/AccountSettings.tsx`).

Password settings now offer an unchecked **Sign out of other devices** checkbox only when the server advertises support and permits password changes. Requests omit the member unless selected; the server preserves this sign-in and API keys. Selection follows existing page/profile ownership and resets on reload, departure and success. Success feedback uses the captured request choice.

Fresh synthetic verification for this source: focused account-password **29/29**, full unit suite **2813/2813**, successful self-contained Release `win-x64` app publish, and successful `password-sessions` native fixture checking actual visibility/default/toggle/payload/pending/reset/current-session behavior and compact wrapped layout. All .NET commands held `SiloOct18Build`; pinned mpv hash matched `engine.json`. Existing unit, app and native compiler warnings remain documented in the logs. Native `run.ps1` remains unchanged; the external task runner staged published references including `Updates`.

Evidence: `D:/SiloPlayer/.codex-tmp/server-align-nov01/password/{focusedgreen2,fullmain,releaseapp,nativecontrols}` and `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-a-report.md`. Owned publish: `.codex-tmp/password-releaseapp-client`, `SiloPlayer.dll` SHA256 `5F2C544FF5D67497A7C1B378D99EE54FF8F06AEB5A9CDEED1EC7536EA87B9CF2`. Tests/publish preceded the implementation commit `fdfa908a0651c14c2ea883252fa2ccc8301ef8ea`; their baseline HEAD receipts accompany the working-tree changes subsequently committed there. The final fixture-only service-resolution adjustment was verified by the native run.

No release stamps changed, installer ran, installed player was controlled, real account was accessed, or release was published by this task. Live multi-device revocation and complete WebUI visual parity are outside this synthetic evidence. Parent task owns independent review and Beta PR delivery.

## Opt-in Beta update channel (PR #62)

PR #62 adds Stable-default, opt-in Beta updates. The source is stamped **1.2.517**, with numeric AssemblyVersion/FileVersion **1.2.517.0**, in the app and update helper projects; the installer defaults match. This is a stable source identity, not a `1.2.523-beta.1` package. The PR base is `fdccad18` (abbreviated commit ID).

The implementation includes persisted channel preferences, semantic release ordering and identity validation, bounded GitHub catalog discovery, same-MAJOR offers without silent downgrades, channel cancellation/cache retirement, direct skipped-version hash reuse, explicitly verified full migration and semantic version/channel presentation.

See the [release contract](update-release-contract.md) for canonical tags, GitHub flags, the five public assets, semantic-to-Windows version mapping and rollout requirements. Source stamps do not establish publication status or prove that an installer has been built, sealed or installed. Build/test results for this PR must come from its pipeline; machine-local candidate receipts are not evidence for this source.

## Compatibility and rollout

Stable remains the default for existing and fresh preferences. Installing a Beta manually does not select the Beta channel. Existing clients need a channel-capable client and matching version-local helper before opting in. Compatible incremental updates keep the Protocol1 root bootstrap immutable, preserve prior versions and user data, and reuse only hash-verified files. Full migration requires the existing verification and manual-consent gates.

The update dialog retains its bounded scrolling body and fixed actions. Channel changes retire prior offers and prepared packages while preserving shared authority cooldowns. Checks wait for overlapping checks and other short operations, but return immediately without changing state when a download is active or pending. Canceling a queued check leaves the active operation untouched; checks read the channel epoch after acquiring the operation gate. Switching from an ahead-of-Stable Beta never downgrades the installation.

## Historical release documentation

The [1.2.516 release ledger](releases/1.2.516.md), [1.2.515 handoff](archive/2026-10-29-release-1.2.515-handoff.md), [1.2.514 handoff](archive/2026-10-29-release-1.2.514-handoff.md) and [earlier handoffs](archive/2026-10-29-development-handoffs.md) describe historical releases, not this PR's source identity or verification status.
