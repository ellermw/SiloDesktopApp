# Marker details implementation plan

> Use superpowers:executing-plans for this task. Owner approved report D and requested execution in a separate Beta PR.

**Goal:** Optional trustworthy marker source/confidence presentation without changing playback.
**Architecture:** DTO/live range metadata → shared formatter and scoped preferences → native/OSC text labels. Separate cosmetic OSC messages preserve marker timing ownership.
**Tech stack:** .NET8, WinUI3, libmpv/Lua, typed settings revision24.
**Spec:** docs/superpowers/specs/2026-11-01-marker-details-design.md.

## Constraints and review focus

- Keep source1.2.519; pipeline stamps release. Hold SiloOct18Build for every.NETbuild/test, hydrate pinnednative first; output is fresh and tests live below worktree.
- Null/unrated confidence, invalid values, provider names and ASS escaping.
- Repeated ranges and old/current version metadata ownership.
- Old-server unsupported keys and late old-profile setting reads.
- Cosmetic messages must not reset intro countdown/undo or issue transport commands.
- Native line remains outside actionable pill and clips no controls.

## Tasks

- [x] Reproduce actual watch wire/metadata loss and Lua missing-detail behavior.
- [x] Add compatible TimeRange metadata/read handling, formatter and repeated-occurrence preservation; verify focused red/green cases.
- [x] Add typed capability-gated effective preferences and settings switches with context/lifetime guards.
- [x] Render native and OSC labels via separate cosmetic messages; verify native/Lua behavior and unchanged transport.
- [x] Run main suite, Release publish and scoped native checks; independent Standards/Spec review and corrections.
- Delivery gate: refresh remote heads/threads, inspect the clean exact-source gate, then push the reviewed branch and open its Beta PR. The external task report records the resulting PR receipt.
