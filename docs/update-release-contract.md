# Windows Stable/Beta release contract

This interface belongs to the Windows client/updater. PR #62 adds the opt-in Beta channel on base `fdccad18` (abbreviated commit ID). Its app/helper source version and installer defaults are `1.2.517`, numeric `1.2.517.0`. These source stamps do not assert that a package has been built or published. The examples below describe the release identity contract, not additional artifacts shipped by this PR.

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

Beta catalog discovery inspects only the first two pages of 100 entries, with two MiB per page and a six-second catalog deadline within the overall check deadline. Each Beta check uses at most three requests including Latest, regardless of retained catalog size; conditional requests still consume the unauthenticated shared rate budget. Selection uses semantic precedence within this recent creation-ordered window, not the entire historical catalog. A Beta intended for discovery must remain within that window. Catalog failures retain the independently validated Latest stable result, including no offer when already up to date, without triggering the hard-error retry interval; rate limits still enforce the shared cooldown. GitHub's owner-name and numeric-repository pagination hints are accepted, but every request is constructed from the pinned owner/repository endpoint. Link URLs are never followed. A 304 needs a previously received validator for that representation.

## Manifest and skipped-version behavior

Preserve Schema=1, BootstrapProtocol=1, BackwardCompatibleData=true and numeric four-part Version in every published manifest (`UpdateManifestValidation.PublishedBootstrapProtocol`).

Owner rule: updating from inside the app must always work from every client still in use, including the current stable and older betas, with the keep-open progress window. A release must never move older clients onto a path they cannot finish or that skips their in-app updater. Clients up to 1.2.709 accept only protocol 1 and treat anything else as a full-installer migration (windowless, and blocked for them by the padded-version check below). Releases 1.2.810-beta.1 and 1.2.811-beta.1 published protocol 2 and broke in-app updates for every older client; protocol 1 restores them.

Protocol 1 stays correct for .NET 10 packages because manifests carry version-local client files only. Each version directory is self-contained, and the root launcher starts it as a separate process, so a .NET 8 root launcher starts a .NET 10 client normally. Installations that update in-app keep their existing root launcher until an in-app bootstrap refresh ships; fresh and full-installer installations get the .NET 10 launcher. Clients still accept protocol 2 (`CurrentBootstrapProtocol`) so the installations and journals created by 1.2.810/811 remain readable. Both protocols keep the root bootstrap immutable during incremental updates; versioned client updates cannot silently replace it.

The full installer's version resource must hold the exact semantic version text. Inno Setup pads ProductVersion with spaces to a fixed width, and clients up to 1.2.709 compare that text exactly in their own copy of `UpdateOnExit.ps1`, so `installer/build.ps1` replaces the padding with NULs in place (same size and layout) through `installer/Repair-InstallerVersionText.ps1` before the installer is verified, signed or published.

The semantic ReleaseVersion field is mandatory for beta manifests and must equal the tag without `v`; when present on stable manifests it must also agree. Old stable manifests without it remain valid.

The manifest is a complete inventory, not an ordered delta chain. Each installed file may be reused only after its content hash and size match the target object. Missing/changed objects are fetched from their verified target pack ranges, then checked compressed and expanded. Beta.2 can therefore go directly to beta.12 when the target protocol/data contract is compatible. No dependency on publishing or installing beta.3 through beta.11 may be introduced.

An unavailable or explicitly unsupported incremental migration can use an independently verified full installer. Hash/path/version tampering and arbitrary transport failures stay failures rather than triggering a silent fallback. A versioned full migration requires manual consent, registered-root targeting, normal playback-save/close confirmation and original-user restart through the stable root launcher. On a versioned installation, this manual-only full fallback applies both to releases without incremental assets and to verified unsupported incremental migrations. Automatic mode neither downloads that full installer nor silently runs it on exit; downloading and installing that fallback require explicit manual choices. Existing legacy full-installer update semantics remain supported for non-versioned installations.

Before requesting elevation for any full migration, including a legacy non-versioned installation, the helper independently resolves the HKLM machine registration, requires protected installation permissions, and derives versioned mode from the installed layout/state rather than handoff arguments. A versioned handoff must name the active version-local client, not the root launcher. It independently fetches the published release from the pinned GitHub tag endpoint without authentication or redirects, with bounded metadata and a deadline. The canonical tag, prerelease flag, mapped numeric version, installer URL, size and GitHub SHA-256 must agree with the handoff and locked package, whose numeric file/product and semantic product PE identities must also agree; unavailable or disagreeing authority blocks setup. `VerifyOnly` validates local bytes/gates without machine registration or network authority and cannot launch setup. Per-user or unprotected installations require a manual full installer.

## Channel changes

Stable is the default for existing/missing/invalid preferences. Beta is opt-in and shows a warning. Selection persists across restarts/upgrades without changing account/profile/playback data. A channel change cancels and retires old checks, downloads and prepared offers. ETags are scoped to their endpoint/channel; shared absolute authority cooldowns survive changes. Beta->Stable never silently downgrades: an ahead-of-stable beta waits for a suitable approved stable version.

Update checks serialize behind other checks, channel transitions and cache cleanup. A check requested while a download is active or pending returns immediately without disturbing the transfer. Cancellation while waiting completes the check without changing the active operation's state. After acquiring the operation gate, checks read the current channel epoch and retain epoch guards on publication so old-channel work cannot revive an offer.

## Rollout

Existing clients have no Beta preference and continue receiving approved stable updates. They must first receive a channel-capable client **and its matching version-local helper** through an approved stable update or an owner-run full installer before opting into Beta. The root Protocol1 launcher stays immutable for compatible changed-file updates. In-app full migration on a versioned installation requires the root launcher's exact FileDescription capability resource `Silo Desktop Player launcher; FullUpdateGate=1`, emitted from the gated launcher's AssemblyTitle. FileVersion is not capability evidence: gate-less builds also used 1.2.517.0. Both the app and exit helper require the marker without loading or executing the launcher. If incremental rollout retained an unmarked or unrecognized launcher, the helper defers before setup; close Silo and run the full installer manually to migrate that bootstrap first. A stable promotion is a newly stamped/verified stable artifact, not a metadata-only relabel of this Beta. Both fresh and upgraded preferences default Stable unless a valid Beta selection was saved; installing a Beta manually does not opt in on the user's behalf.

The supported build entry point is `installer/build.ps1`, with fresh publish and installer output directories. It preflights semantic/FileVersion/AssemblyVersion/Inno stamps before dependency work and passes both `--version` (numeric) and `--release-version` (semantic) to SiloUpdatePack. The tool refuses disagreement before creating output. Its existing numeric API/CLI remains available for legacy stable fixtures. New Beta manifests must use the semantic overload/option.

[Current development](current-development.md) records the PR's source identity. Build, test and package verification results must be tied to the actual release source and artifacts. Synthetic tests alone do not establish production UAC, alternate-admin behavior, installation completion/reopening or phone/hardware behavior.

References: [SemVer2](https://semver.org/spec/v2.0.0.html), [GitHub Releases API](https://docs.github.com/en/rest/releases/releases), [Windows VERSIONINFO](https://learn.microsoft.com/en-us/windows/win32/menurc/versioninfo-resource), [Inno numeric file version](https://jrsoftware.org/ishelp/topic_setup_versioninfoversion.htm), [Inno numeric product version](https://jrsoftware.org/ishelp/topic_setup_versioninfoproductversion.htm).
