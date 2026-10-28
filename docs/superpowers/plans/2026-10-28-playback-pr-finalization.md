# Playback audit corrections and release plan

> Execute inline with systematic debugging, test-driven development and a final independent Standards/Spec review.

**Goal:** fix the reproduced PR32 recovery defects, validate corrected PR33, merge both and publish a matching installer for owner updater testing.

**Architecture:** preserve viewer subtitle intent through the authoritative v3 replan and recovery, and transfer the captured pause intent before recovery takes ownership. Keep native fixtures synchronized with the cleaned transport constructor. Preserve current337 updater repair and all unrelated work.

**Spec:** owner instruction to review, validate, apply corrections and publish; PR32/33 and reproduced evidence under `.codex-tmp/pr32-review-oct28` and `.codex-tmp/pr33-review-oct28`.

**Constraints:** isolated checkout; synthetic accounts/loopback media; SiloOct18Build for .NET; hydrate pinned engine; no installed-app or production changes; no force-push. Publish the exact tested installer with README/checksum links. Complete elevated update/reopening remains an owner test.

## Review focus

- Failed text and Off changes must clear burn-in without losing the requested subtitle.
- Recovery must preserve both playing and intentionally paused states.
- Successful DVD selection must not replan repeatedly on native file load.
- Old/reflection callers must match the new transport constructor.
- Combined release must retain the337 helper bytes and phone control of PC-started playback.

## Tasks

- [x] Add Core recovery intent cases and native failed-switch cases for text/Off and playing/paused; verify red against the pinned old payload.
- [x] Request the actual desired subtitle in the v3 replan, retry an unaccepted subtitle request through track_change during recovery, transfer the captured pause intent; verify green and normal transitions.
- [ ] Commit/push corrected PR32, fold it into corrected PR33, verify four repaired native callers and independent Standards/Spec reviews; report work on both PRs.
- [ ] Prepare338 from the combined current-main source; run full tests and relevant exact-payload native/published regressions, seal matching installer.
- [ ] Merge verified exact heads, publish338 and README/download/checksum, verify public asset bytes/discovery, record delivery and clear the integration lock.

Ruling: retry pending subtitle changes with track_change specifically for subtitle-switch-failed. Current official server f133b241f3526604aedb0791b39e65dac6edc18a treats omitted subtitles as Off only for track_change; failure recovery otherwise preserves outgoing intent. Avoid changing general decoder/stream recovery behavior. Two Core cases and the native failed text switch failed before the repair;11 Core intent cases and five actual native scenarios pass after it. Final release repeats the scenarios against the packaged combined payload.
