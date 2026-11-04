# Preserve regional subtitle identity in metadata fallback

Base: pipeline PR75 source `09572257348eb6237eca6e78e0fe48894f1c6683`.
Scope: P2 in the external pipeline Spec review; approved PR2 identity-or-supported-route requirement. Global preference normalization and authoritative container/FFmpeg matching remain unchanged. P3 pending-preference duplication advisory is deferred.

- [x] Reproduce region/script and unspecified-versus-explicit mismatches using the actual resolver.
- [x] Reproduce an actual no-ID en-GB request against loaded eng media selecting the wrong native route instead of its authorized sidecar.
- [x] Normalize only the primary ISO alias while preserving language subtags for native identity.
- [x] Verify focused/full tests, Release, combined identity/adoption/room native fixtures and sidecar ownership on the same bytes.
- [x] Record source hashes/receipts/limits and commit locally for root review; no remote action.

All work and fresh output stay in the assigned D checkout. Every .NET command uses the shared mutex runner with Task identity-companion; hydrate and verify the pinned native engine first. Preserve original PR2 sources and artifacts.

Execution: six pure mismatch cases and an actual native en-GB-to-eng substitution failed before correction. Final focused56/56, source-fixture maintenance3/3, full3,096/3,096, Release and seven combined native fixture groups pass. Existing sidecar-ownership suite is22/27 on both frozen095baseline and corrected build, with the same five pre-existing fixture/precondition/obsolete preference-count failures. Original ownership assertions remain untouched; classification and limits are in the external companion report.

Ruling: Maintain three stale source-inspection expectations for the pipeline's new boolean/executed-selection signatures and restoration guard; no additional product change. Ruling: Preserve baseline ownership failures and defer real-load fixture reconciliation to parent rather than broadening this regional-language correction. P3 advisory remains deferred.
