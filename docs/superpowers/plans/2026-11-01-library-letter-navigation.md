# Library alphabet navigation implementation plan

> **For agentic workers:** Use superpowers:executing-plans; implement and verify each task before delivery.

**Goal:** Exact WebUI-style right-hand alphabet navigation in the native Library grid.

**Architecture:** A capability-gated CatalogApi read returns letters and positional runs. LibraryViewModel owns its query-scoped index and exact jump command. A LibraryPage partial owns the native rail and uses the existing bounded pixel-scrolling grid.

**Tech stack:** C#/.NET 8, WinUI 3, xUnit, synthetic native regression fixtures.

**Spec:** `docs/superpowers/specs/2026-11-01-library-letter-navigation-design.md` (owner approved).

## Global constraints

- Pin official main `63ef34b236b88dc77acca1a9a3ed90235377691f` and desktop base `a15621a0d6d5960d6b31a620a23ae259d734dedb`.
- No installed-app control, account reads or setup execution. .NET builds/tests hold `SiloOct18Build`; outputs are fresh.
- Submit to Beta only. The pipeline owns release stamping, packaging and publication.

## Review focus

- Multi-language and advanced OR filters must index the same membership as the actual catalog request.
- Late results after query/profile/library/language changes must not publish or scroll.
- An index response can race a scan: retain any larger already observed catalog extent.
- A letter near the end must stay selected while scrolling is clamped at the bottom.
- Unsupported capabilities, empty libraries and failures must preserve browsing and not invent a jump.

## Tasks

- [ ] Write a behavioral regression against the existing approximate `JumpToLetterCommand`; show it misses the server's exact distant position.
- [ ] Add catalog letter DTOs/API, sharing the current search capability cache. Validate index positions and preserve request context; request the same structured filters as catalog browsing.
- [ ] Replace approximate probes with query-owned exact indexes/jumps, cancellation and stale-response guards. Add protocol/scope/filter/order/empty/failure tests; run red/green and the unit suite.
- [ ] Add a focused native rail partial and XAML host. Consume `LetterIndex`, `EnsureLetterIndexAsync()` and `JumpToLetterCommand`; align scrolling to `CatalogLetter.Position`, reserve rail width, support two-column short layouts and accessible roving keyboard navigation.
- [ ] Extend the synthetic library fixture to verify actual jump/scroll/highlighting, unavailable letters, sorting and short/tall layout. Build/publish into fresh diagnostic output and run native checks.
- [ ] Independently review Standards and Spec, correct findings and rerun scoped checks. Refresh/inspect remote heads and PR threads, then push the exact reviewed branch and open a Beta PR.
