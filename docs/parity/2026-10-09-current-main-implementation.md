# Current-main desktop updates — October 9, 2026

**Resumed final acceptance:** the source-stage/unrun statements below are historical. The current official reference is1a7a3970a9928efb0157460c10888832a8a74eb1. [Combined verification](2026-10-09-combined-verification.md) records the finite source/native/physical evidence, exact1.2.241 payload and native/live boundaries. Correction IDs are reconciled in the central ledger; GitHub publication awaits the user's final installer approval.


Reference: official public Silo main **88dac5cdae81d52f215a64d5ea079cc189ea93f2**, freshly fetched again before implementation. It remained unchanged from the [delta audit](2026-10-09-current-main-delta.md). The user approved implementing the two findings. Computer control remains paused; no installed player or user browser was opened, changed or interrupted.

## Two implementation groups

1. **Current default artwork.** One shared `DefaultArtwork` native control now supplies the faint Silo glows and media-type mark in PosterCard, virtual LibraryGridCard, LandscapeCard, audiobook cards, external Requests/Watchlist cards, Calendar and Taste Seed. Recommendation/Home/section consumers reuse these shared cards. Its gradients remain circular and use the longer artwork side, with a14–40px centered mark and current background/opacity. Dimmed external titles retain brightness/saturation treatment. Decorative artwork does not intercept pointer input and remains outside the accessibility content view; card title text/names remain.

   Valid artwork loading keeps the new fallback hidden. Missing/failing artwork reveals it; available thumbhashes are decoded off the UI thread and used on failure rather than eagerly decoding every scroll card. Existing image throttles and cache identities remain. Binding/reset generation checks prevent old hash results from applying after reuse; real detach invalidates pending completion.

   Source review found that `UrlToImageSourceConverter` could return an empty bitmap after a fetch failure without raising WinUI `ImageFailed`. It now publishes completion through weakly held source outcomes, allowing Requests and Taste Seed to select the fallback for HTTP failures as well as decoder failures. Failed bitmap entries are removed by key/value identity so they cannot evict a replacement. Bitmap operations use the complete URL as identity so a renewed URL does not reuse an older URL's pending operation; disk-image identity remains stable. This is part of the artwork group, not a third independent version correction.

2. **Collection image limit.** File picker/drop validation and both editor captions now match the server's10MiB limit. `SetPosterFile` also rejects oversized bytes before changing an existing draft. Exactly10MiB is accepted; one byte more is rejected. Real view-model/API fixture checks verify that422 leaves pending poster bytes and the stored artwork intact, Save does not signal completion on rejection, retry succeeds, and an artwork failure after collection creation retries the same collection rather than creating a duplicate.

These are two proposed correction groups for final deduplicated version accounting. Do not increment release metadata for each touched file, test, icon or compiler repair. Product metadata remains1.2.108 until the accepted final release is frozen.

## Evidence

Ignored artifacts: `.codex-tmp/combined-oct9` in the active worktree.

- `current-main-red.log` / `unit/current-main-red.trx`:23 focused cases execute;21 fail for missing artwork policy and absent upload-size rejection,2 pass for retained422 behavior.
- `current-main-green.log`:23 focused cases pass after implementation.
- `current-main-full.log` / `unit/current-main-full.trx`:**1,550 pass,0 fail/skipped**, including the3 actual headless-mpv OSC cases from the previous combined stage. This run precedes the last converter/loading refinements.
- `converted-image-guard.log`:one old source-shape assertion fails after safe key/value cache eviction replaces unconditional removal;18 neighboring guards pass. Its assertion was updated to retain the same cleanup/identity purpose. This is not a reproduced visual defect or an extra correction.
- `current-main-final-focused.log` / `unit/current-main-final-focused.trx`:**42 affected cases pass,0 fail/skipped** after final artwork/converter adjustments. They are a subset of the1,550, not42 additional tests.
- `current-main-publish-final.log`:**Release x64 publish succeeds**, exit0, no warnings/errors. Payload: `.codex-tmp/combined-oct9/current-main-payload`. It is an unreleased verification payload, not an installer. Source assets were staged into this ignored payload for the referenced-assembly regression hosts; an initial host build's missing-content copy errors were fixture preparation failures.
- `current-main-playback-final.log`:**15 checks pass against the final published assembly**, exit0. Isolated service checks exercise direct bypass/resume/startup recovery, remux timeline, account-auth transport, event disconnects and auth/profile boundaries; the fixture rejects remote HTTP and opens no player window.
- `current-main-native-build-final.log`:native fixture host compiles, exit0,0 errors and9 pre-existing nullable warnings. Its new geometry, invalid-hash, HTTP404, rotated-URL and pending-old-URL cases are **compiled, not executed** during paused computer control.

## Remaining acceptance

Add these cases to the existing single combined verification, rather than starting another parity checklist:

- Difficulty5 and affected13/12/9/7/4: missing/failed/no-hash/valid-hash poster and still pairs, media-type marks, dimming, rounded clipping, tiny/square/wide geometry, loading without a glow flash, recovery with a renewed URL and a previous URL still pending, physical hover/selection and accessible title retention. Native `shared-controls` with `SILO_NATIVE_SHARED_CURRENT=1` now includes the source-outcome/geometry cases.
- Difficulty8: real picker/drop at and beyond10MiB, both normal/imported editor notices, invalid-file/source422 preservation/retry and durable-created identity.

The latest-main findings are implemented and the recorded automated stage passes. Whole-page visual/physical/live acceptance and final release/publication remain outstanding. Preserve the older22e3 receipt; the new ignored `current-main-verification-receipt.json` identifies this payload separately.

## Installer for the user's launch check

At the user's request, a fresh clean installer was packaged before the combined verification. It is a verification build with existing1.2.108 metadata; creating it does not mark the checklist accepted or publish a release.

- Delivered file: `D:\SiloPlayer\installer\output\SiloInstaller-1.2.108-ParityVerification-20261009-Setup.exe`,169,891,329 bytes.
- Installer SHA256: `FD08E8310E0D4DE0D30159D8C1BC9F76A3CEA1EA711AC7AA6BF5B8F879BEF7D8`.
- Clean build/packaging exit0; Windows App Runtime and libmpv validation passed.
- Packaged `SiloPlayer.dll` SHA256: `83EDD458DBFAFCC1C18E9E4224D0CA18A3C55B197CF4E873986181568897A5AC`; file version1.2.108.0.
- All15 isolated published-service playback checks passed against this exact assembly, exit0.
- Build log, playback log and machine-readable receipt: `.codex-tmp/launch-check-oct9/` in the active worktree. Source artwork assets were staged after packaging for the referenced-assembly fixture; the packaged assembly remained unchanged.

The user will install and confirm launch, then explicitly hand over computer control. Do not install, activate the app, or begin interactive/native/browser verification before that handover. The signed-in Chrome session is an admin account: avoid administrative mutations during parity comparison. Credentials have not been requested or recorded.
