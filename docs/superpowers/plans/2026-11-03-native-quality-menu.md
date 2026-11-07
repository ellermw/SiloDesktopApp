# Native Quality menu implementation plan

> **For agentic workers:** Use superpowers:executing-plans or superpowers:subagent-driven-development. Execute the approved stage through verification and Beta PR delivery without another approval request.

**Goal:** Move the active OSC Quality/version menu into the proven owned native host while preserving accepted playback plans and viewer intent.

**Architecture:** Generalize the existing Audio host, then immediately consume it from Audio and Quality. PlayerService owns scoped selection, source inventory and existing replans. The presentation owns only native controls, placement, input and its dialog lease.

**Tech stack:** WinUI3/.NET8, current libmpv renderer, actual Lua OSC and existing protocol3 services.

**Spec:** [Approved OSC/native-menu design](../specs/2026-11-03-osc-native-menus-design.md).

## Constraints

Use the freshest Beta source and keep pipeline-owned versions unchanged. Preserve the renderer, genuine pause/room intent, original server file IDs, server quality ladder, Auto/delivery/cap distinctions, buffering Keep/Restore paths and current playback/account ownership. No loading-icon, Dashboard or installed-player changes. Native Subtitle remains a separate stage dependent on reconciled PR75/77; this PR retains the legacy subtitle backend and caller. Do not edit current-development.md; put verification results in the PR description.

## Tasks

- [ ] Extract shared owned choice presentation from Audio with one native menu gate, scoped Lua handshake and existing dismissal protection. Preserve Audio behavior.
- [ ] Add the Quality model and actual pointer/controller caller. Keep Playing, Requested and pending distinct; explain room-locked versions. Unsupported advertised future choices must be visibly unavailable rather than silently selecting Auto.
- [ ] Bind selection to existing version/quality replans. Check owner after transport drain and later awaits, permit retry of a requested version when another file is playing, and retain pause intent.
- [ ] Verify late option arrival updates the HUD without resize; native focus/scroll/disabled reasons and live size changes remain bounded. Preserve deliberate wheel position and detach queued resize work on close.
- [ ] Run full unit/runtime tests, a fresh diagnostic app build, actual native Quality and Audio/geometry regressions, inspect captures, then independent Standards/Spec reviews. Refresh remote heads/threads separately before the authorized Beta PR push. Record receipts in the PR description.
