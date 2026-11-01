# Stable and Beta updates — implementation design

The owner supplied `Pasted text.txt` (attachment c24e74d5-6ba5-4c92-b119-835cff469095) and requested implementation/testing without more planning questions. This design records implementation choices, not a new approval gate. Work remains local/unpublished; do not modify the production heartbeat or the separate OpenClaw/Mission Control implementation. Preserve published 1.2.516, installed applications and frozen artifacts. Base: a83b3e2158983159baac0bc4ad46c8422d6fcffd.

## Approach

Keep the current GitHub `/releases/latest` authority for Stable. Beta additionally scans a bounded paginated public release catalog for published prereleases and compares them with the approved Latest stable using SemVer precedence. Never follow arbitrary pagination URLs, use authentication, expose drafts, or bypass existing rate-limit/cooldown protections. Per-channel conditional caches must retain verified prepared packages only for the exact same release authority; channel transitions cancel and retire old work before a new offer can become actionable.

Retain numeric `AppRelease.Version` for transport/installation and add semantic release identity/display separately. A strict SemVer parser/comparator supports numeric prerelease identifiers and ignores build metadata in precedence. The installable two-channel release contract permits canonical stable `M.m.P` and `M.m.P-beta.N` only; other valid SemVer labels can be compared but are not package identities for this contract. Keep legacy stable four-part tags working as appropriate to existing behavior.

### Numeric mapping and bootstrap compatibility

- Stable `M.m.P` keeps existing Windows `M.m.P.0` stamps.
- Beta `M.m.P-beta.N` maps to `M.m.(P-1).N`, P>=1 and N in 1..65534; numeric components must fit the Windows/.NET supported range. Example `1.2.518-beta.12` -> `1.2.517.12`; final `1.2.518` -> `1.2.518.0`.
- This deliberately reserves nonzero revisions for beta sequencing. It orders beta2 < beta12 < final stable while preserving the current Protocol1 numeric launcher, immutable directories, inventory validation and recovery. Reject unsupported/exhausted mappings; never wrap or increment MAJOR automatically.
- Installed semantic identity comes from the app's assembly informational version, checked against the numeric assembly/file version. Discovery, UI and channel eligibility use semantic identity; privileged staging continues independently validating numeric/tag/manifest identity.
- All automatically offered releases must keep the installed MAJOR. A major migration requires a separately owner-approved contract/source change.
- Keep Protocol1 bootstrap behavior immutable. Channel-aware helpers must be installed before opting into future beta updates; legacy clients continue discovering only stable Latest.

## Discovery, switching and data

Stable is the persisted default for missing/invalid settings. Beta offers the highest eligible published beta or newer approved stable. Compare beta sequence numbers numerically, independent of publish time/list order. Switching to Stable never downgrades; explain when the installed beta is ahead and wait for a suitable stable release. Preserve settings, profiles, preferences, credentials and recovery records. Reject stale offers, prepared packages, download completions and authority caches after a channel switch; retain the shared absolute cooldown.

The existing pack is a full client manifest of content-addressed objects, not a chain of deltas. Any installed version can reuse individually hash-verified files; missing/changed objects are fetched directly. Test skipping beta2 to beta12 with actual pack preparation/staging and recovery.

## Fallback and user interface

Use incremental staging whenever its validated protocol/data contract is supported. Missing incremental assets or a specifically recognized migration incompatibility may fall back to an independently SHA/size-verified full installer. Corrupt hashes, manipulated paths/versions and transport failures must not silently become fallback success. Full migration fallback on a versioned install requires explicit manual consent, a clear close/save explanation, registered-root targeting, normal-close confirmation and original-user restart through the stable root launcher. Do not automatically run that migration on exit merely because the user enabled background updates. Keep existing legacy full-update behavior.

Add Stable/Beta selection under the existing update preference in Settings and the updates dialog, with a short beta warning. Show installed semantic version/designation, selected channel, latest eligible version/designation, notes and progress. Keep the centered bounded scrolling layout and fixed actions. Show the real semantic version in notifications and once-only post-update success feedback.

## Packaging and handoff

Preserve existing five public asset names. The builder must preflight all semantic/numeric app/helper/Inno stamps, embed semantic informational identity, emit the matching numeric version-local layout, and bind beta semantic identity into the hashed update manifest. Finalized interface belongs in `docs/update-release-contract.md`, including flags, tags, numeric mapping, hashes, protocol limits and tested skip/fallback behavior. No deployment or orchestrator implementation is included. Do not publish, push main, install, restart or control the owner's app.

## Verification

Test real SemVer precedence/mapping and malformed inputs; Stable and Beta metadata, pagination, ETags, flags and SHA/URL validation; settings migration/preservation; rapid switching during checks/downloads; old/new stable promotion; skipped beta file reuse and interrupted recovery; compatible staging and explicit verified full fallback. Use synthetic data/roots, isolated fixture processes and the SiloOct18Build mutex for every .NET build/test. Final full/native/published verification must bind exact source fingerprints and fresh outputs. Report production UAC/reopening and physical interoperability as unobserved.

Primary references: [SemVer2](https://semver.org/spec/v2.0.0.html), [GitHub Releases API](https://docs.github.com/en/rest/releases/releases), [Windows version resources](https://learn.microsoft.com/en-us/windows/win32/menurc/versioninfo-resource).
