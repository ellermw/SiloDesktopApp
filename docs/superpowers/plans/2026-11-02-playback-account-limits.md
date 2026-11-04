# Playback account-limit implementation plan

**Goal:** Show actionable, accurate account-limit advice.
**Spec:** ../specs/2026-11-02-playback-account-limits.md
**Architecture:** Extend the existing pure protocol-terminal presentation map. Keep normal start ownership and retry protocol unchanged.
**Constraints:** Same-repository Beta PR; no version edits; .NET under SiloOct18Build after pinned-native verification; synthetic data, fresh outputs and no installed-app/setup/production access.
**Review focus:** Blank and padded messages, false retryability, capacity distinction, fresh playback_attempt_id after terminal.

- [ ] Add failing presentation cases in PlaybackFailureDescriptionTests and a real synthetic HTTP start-after-limit regression in PlaybackProtocolV3RecoveryTests.
- [ ] Observe expected mapping failures with a focused test run; preserve the existing passing fresh-attempt behavior.
- [ ] Extend PlaybackFailureDescription.Describe for both codes, sharing normalized account-limit advice without changing other reason fallbacks.
- [x] Run focused/full tests and diff checks: 45 focused and 3,068 full tests pass. Use a local commit to pin the review range; independent Standards/Spec review precedes push. Exact source/evidence are documented in current-development. Pipeline owns app build/packaging.
