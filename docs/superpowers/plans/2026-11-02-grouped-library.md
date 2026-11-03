# Grouped Library implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline. No subagents. The owner approved the design and parent assigned local implementation.

**Goal:** Browse enabled movie/mixed libraries as the server's grouped list with exact matching alphabet navigation.
**Architecture:** Carry one optional `box_set` scope through the existing catalog requests, virtual window identity and letter index. Reuse the foundation destination and aggregate card presentation; preserve server order and totals.
**Tech Stack:** C#/.NET 8, WinUI 3, xUnit and synthetic native fixtures.
**Spec:** ../specs/2026-11-02-server-parity-design.md

## Global constraints
- Base 48e04f74 locally combines fresh Beta 6d3ac20f and reviewed foundation abba1d76; no submission/readiness until foundation reaches Beta and parent reconciles fresh Beta.
- Official GitHub main fetched and unchanged at bf53b65450244751013a2965b39da4111d9a0f72.
- Retain version 1.2.544-beta.1 / 1.2.543.1. Every .NET operation uses the external verification runner/mutex and fresh D: outputs. Engine SHA256 04E4D27DC385DC18DDD93FBE69C75E635B864F3C6EC53A9122DF54C2F7842F4A.

## Review focus
Old servers omit group/index support; library disable retires cached aggregate windows; delayed profile/library/filter reads never repopulate retired owners; reverse filtered jumps retain grouped positions; aggregate cards never enter title actions.

### Task 1: Catalog scope and ownership
Modify CatalogApi.cs, CatalogApi.LargeQuery.cs, CatalogApi.Letters.cs and LibraryViewModel.cs/Letters.cs. Existing MediaItem.BoxSet is the typed wire mapping (there is no CatalogItemV2 adapter in this source).
- [x] Add HTTP/model tests for GET/large POST/count/cursor/distant seek/index scope, typed summary, unsupported and disabled libraries, reverse filters and stale completion.
- [x] Run focused tests through Invoke-Verification.ps1 and preserve behavioral RED.
- [x] Add optional trailing `group` argument to catalog and letters; pass the same scope through POST. Gate model grouping on enabled movie/mixed library plus advertised box_sets; gate grouped index on box_sets and letter_index.
- [x] Include group in query/index identity; retire pages on library scope changes and disable. Verify focused GREEN.

### Task 2: Native card surfaces and refresh
Modify LibraryGridCard.cs and LibraryPage group refresh handling; inspect poster/landscape/list routes and reuse BoxSetCard/destination.
- [x] Add actual-control regression for far-letter jump, returned-library aggregate navigation, missing art, recycling to ordinary actions and compact/wide keyboard behavior; run RED against hash-bound foundation app.
- [x] Render aggregates with BoxSetCard in the virtual grid, preserving bounded card slots and responsive sizing; refresh grouped counts on existing watched/catalog/library invalidations.
- [x] Run focused/full unit, fresh Release publish and native fixture through external runner. Inspect captures.
- [x] Commit verified local source and record exact hashes/red-green/limits in external grouped-library-report.md; parent owns review and remote gates.

Final local result: focused108/108, full3108/3108, Release and combined native PASS. Exact source/artifact hashes and all red/intermediate/final receipts are in the external grouped-library-report.md. The dependency/readiness gate in Global Constraints remains a parent delivery obligation; this plan's local implementation and handoff are complete.

## Corrected foundation reconciliation

Parent authorized local reconciliation of verified grouped cursor-recovery head `d97666f22e44987c65ba78f066888cb4f892d9cd` with complete foundation companion `7617ddf2d5a90b5ea0a0e9792c3e8e95326a8123` (product091defe9 on pipeline8071bdb3). Preserve every earlier source/artifact baseline. The parent owns independent review and remote delivery; a complete reviewed/tested stack may precede foundation's Beta merge.

- [x] Preserve both development histories and native dispatch routes in the two merge conflicts; retain grouping recovery and incoming unscoped-library capability/lifecycle guards.
- [x] Verify pinned native dependency and scoped grouped/foundation/letter/cursor/window tests (181/181).
- [x] Freeze combined source inputs; run full units (3,146/3,146), fresh Release and grouped plus foundation/unscoped/room native suites against identical published bytes.
- [ ] Incorporate the additional focused Play from Beginning library-owner dependency when supplied; refresh affected evidence.
- [ ] Record source/artifact/fixture hashes and exact clean local commit; return the handoff without remote submission.
