# Task 3 implementation plan

1. Add actual LibraryPage fast-scroll request/presentation regression; publish unchanged product and record baseline red.
2. Add controllable-time motion policy tests. Implement viewport-owned adapter and cancellable gates at fetch/decode boundaries. Integrate LibraryGridCard lifecycle and real shared-card ancestors, retaining cheap cache hits.
3. Extend native cases for settling, slow movement, late downloads, rebind/cancel/hide/departure/resume, shared cards and independent viewports.
4. Run focused/full units, fresh Release and new/affected native groups via assigned mutex runner. Freeze source and bind fingerprints; record limitations, self-review, commit and hand to parent for independent review. No push.

Completed local implementation and evidence: real baseline red; controllable-time policy tests; actual LibraryPage/CarouselRail shared-card lifecycle and work observations; hidden warmup/profile/retained DTO and direct audiobook group-return regressions. Final full3449, Release publish, new artwork/group-return and cursor routes pass. Earlier broad card/Home evidence and two baseline-equivalent native failures are scoped in docs/current-development.md and the external task report. Independent Standards/Spec review and push belong to the parent.

Fix round1/5 implemented the two independent P2 findings: completed Recommended lifecycle and spoiler stage gates. Actual frozen-product red,16 native stage/outcome cases, delayed first-mount red/green and original Home regression are recorded in artwork-task-report.md. Final A900E4CD artifact passes all five affected native routes. P3 EnterDecodeAsync naming is deferred, nonblocking. Parent owns scoped re-review and delivery; no push.
