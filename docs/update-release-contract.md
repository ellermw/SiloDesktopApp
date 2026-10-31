# Windows Stable/Beta release contract — local implementation

This interface belongs to the Windows client/updater. OpenClaw/Mission Control owns batching, auditing, QA approvals and promotion. This task does not deploy that system, modify its files or the production Codex heartbeat, or publish a stable release. Base/public baseline is 1.2.516; production remains there until an authorized delivery.

## Release identities

Use canonical tags `vM.m.P` for stable and `vM.m.P-beta.N` for Beta, without build metadata or leading zeros. N must be 1..65534, P must be >=1 for a beta, and all numeric fields must fit 0..65534. Other SemVer identifiers can be compared by the client but are not supported package labels in this two-channel contract.

Stable `1.2.518` keeps numeric AssemblyVersion/FileVersion `1.2.518.0`. Beta `1.2.518-beta.12` maps to `1.2.517.12`; beta.2 maps to `1.2.517.2`. Stable 1.2.518 therefore follows every beta for 1.2.518. Nonzero revisions are reserved for beta sequencing; do not independently allocate them or reuse a numeric identity for different semantic versions. Reject exhausted/unsupported mappings rather than wrapping numbers. Do not automatically change MAJOR; a future major migration needs explicit owner approval and a separately reviewed client contract.

App `<Version>` / informational identity and Inno display AppVersion use semantic identity. All app/updater/launcher numeric assembly/file stamps, installer numeric VERSIONINFO, manifest Version and `versions/<numeric>/` directory must use the exact mapping. A source hash may be appended to assembly informational metadata, but release tags and package manifest identities stay canonical. A beta must never be relabeled stable by changing only GitHub metadata: stable promotion requires a deliberately stamped and verified stable package.

## GitHub metadata and asset names

- Stable: published, `draft=false`, `prerelease=false`, canonical stable tag. Promote the approved release to GitHub **Latest**. Stable users continue using `/releases/latest`.
- Beta: published, `draft=false`, `prerelease=true`, canonical beta tag. Do not mark beta Latest. Beta users scan published prereleases and compare them with approved Latest stable, using semantic precedence rather than publish order.
- Drafts and bare tags are never installable channels. No authorization/token is sent by clients.
- Each chosen release needs exactly one uploaded full installer, positive bounded byte size, GitHub-provided `sha256:<64 hex>` digest, valid publication time, and exact repository/tag-scoped HTTPS download URLs.
- Keep release summaries concise and customer-facing; the app displays the body directly.

The same five asset names apply under each tag:

| Asset | Required contents |
|---|---|
| `SiloInstaller-Windows-x64.exe` | The sealed tested full installer, with mapped numeric VERSIONINFO |
| `SiloInstaller-Windows-x64.exe.sha256` | SHA-256, two spaces, installer filename, newline |
| `SiloUpdate-Windows-x64.manifest.json` | Hashed bounded full client manifest for compatible incremental delivery |
| `SiloUpdate-Windows-x64.pack` | Hashed content-addressed compressed file objects referenced by the manifest |
| `SHA256SUMS.txt` | Checksums of the other four assets |

Both incremental assets must have unique uploaded GitHub identities, valid sizes and SHA-256 digests. Their URLs must match the same release tag as the installer. Hashes, sizes and semantic/numeric identities are verified before preparation and again by the installed privileged helper's independent tag-authority fetch.

Catalog discovery is bounded to ten pages of 100 entries and two MiB per page; exceeding that limit fails the check. GitHub's owner-name and numeric-repository pagination hints are accepted, but every request is constructed from the pinned owner/repository endpoint. Link URLs are never followed. A 304 needs a previously received validator for that representation. Keep the retained public release catalog within these limits or implement a separately reviewed discovery contract before it exceeds them.

## Manifest and skipped-version behavior

Preserve Schema=1, BootstrapProtocol=1, BackwardCompatibleData=true and numeric four-part Version. The added semantic ReleaseVersion field is mandatory for beta manifests and must equal the tag without `v`; when present on stable manifests it must also agree. Old stable manifests without it remain valid. Protocol1's root bootstrap is immutable between full migrations; versioned client updates cannot silently replace it.

The manifest is a complete inventory, not an ordered delta chain. Each installed file may be reused only after its content hash and size match the target object. Missing/changed objects are fetched from their verified target pack ranges, then checked compressed and expanded. Beta.2 can therefore go directly to beta.12 when the target protocol/data contract is compatible. No dependency on publishing or installing beta.3 through beta.11 may be introduced.

An unavailable or explicitly unsupported incremental migration can use an independently verified full installer. Hash/path/version tampering and arbitrary transport failures stay failures rather than triggering a silent fallback. A versioned full migration requires manual consent, registered-root targeting, normal playback-save/close confirmation and original-user restart through the stable root launcher. Automatic mode does not silently run a full migration on exit. Existing legacy full-installer update semantics remain supported.

## Channel changes and coordination

Stable is the default for existing/missing/invalid preferences. Beta is opt-in and shows a warning. Selection persists across restarts/upgrades without changing account/profile/playback data. A channel change cancels and retires old checks, downloads and prepared offers. ETags are scoped to their endpoint/channel; shared absolute authority cooldowns survive changes. Beta->Stable never silently downgrades: an ahead-of-stable beta waits for a suitable approved stable version.

The production heartbeat, remote main/release and installed app are outside this local task. The owner explicitly forbids pushes, publication and other remote writes until further instruction. This contract is a local handoff for the owner to share; this task has not messaged the orchestration chat or modified its files. The prior heartbeat path was absent during a read-only check; no automation mutation was attempted.

## Local candidate and rollout

The local candidate is `1.2.523-beta.1`, mapped numeric `1.2.522.1`, based on published 1.2.516/docs main `a83b3e2158983159baac0bc4ad46c8422d6fcffd`. Source remains uncommitted on `codex/stable-beta-updates`. Production remains 1.2.516; do not mark this candidate Latest or publish it under the current owner hold.

Seven concrete behavior additions over 516 determine the candidate patch: semantic identity/mapping integrity; eligible Beta/approved Stable discovery with major guard; tolerant persisted channel preference; cancellation/cache retirement during channel changes; semantic manifest binding and direct skipped-version staging; explicitly verified manual full migration; native channel/warning/version/success presentation. Tests, packaging and verification attempts add no increments.

Existing clients have no Beta preference and continue receiving approved stable updates. They must first receive a channel-capable client **and its matching version-local helper** through an approved stable update or an owner-run full installer before opting into Beta. The root Protocol1 launcher stays immutable for compatible changed-file updates. A stable promotion is a newly stamped/verified stable artifact, not a metadata-only relabel of this Beta. Both fresh and upgraded preferences default Stable unless a valid Beta selection was saved; installing a Beta manually does not opt in on the user's behalf.

The supported local build is `installer/build.ps1`, with fresh publish and installer output directories. It preflights semantic/FileVersion/AssemblyVersion/Inno stamps before dependency work and passes both `--version` (numeric) and `--release-version` (semantic) to SiloUpdatePack. The tool refuses disagreement before creating output. Its existing numeric API/CLI remains available for legacy stable fixtures. New Beta manifests must use the semantic overload/option.

Final local artifact and test receipts are recorded in [current-development](current-development.md) and the evidence root `D:\SiloPlayer\.codex-tmp\stable-beta-updates-oct29`. Synthetic tests do not establish production UAC, alternate-admin behavior, installation completion/reopening or phone/hardware behavior. No setup or installed-player control is part of this delivery.

References: [SemVer2](https://semver.org/spec/v2.0.0.html), [GitHub Releases API](https://docs.github.com/en/rest/releases/releases), [Windows VERSIONINFO](https://learn.microsoft.com/en-us/windows/win32/menurc/versioninfo-resource), [Inno numeric file version](https://jrsoftware.org/ishelp/topic_setup_versioninfoversion.htm), [Inno numeric product version](https://jrsoftware.org/ishelp/topic_setup_versioninfoproductversion.htm).
