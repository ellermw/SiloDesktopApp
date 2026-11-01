# Task E implementation ledger

1. Trace official home.go, Home.tsx and homeRowLimit.ts and native HomeApi -> HomeViewModel -> HomePage -> SectionRow.
2. Red: demonstrate lost has_more and expansion duplicate priority. Implement capability negotiation, partial/expanded model and stable completion merge.
3. Integrate generation-owned completion, bounded refresh/retry and native near-end demand. Preserve existing hero and mounted row lifecycle.
4. Verify focused/full unit tests under external mutex wrapper; fresh Release publish and actual-control native fixture. Self-review and diff check.
5. Commit source/spec/tests/handoff only; report exact commit, fingerprints and receipts externally. Parent owns independent review and push.

Ruling: retain existing whole-row API defaults for non-Home callers; Home opts into the negotiated bounded read. Capability failure falls back to an uncapped read, never sends an unadvertised parameter.

Execution: isolated branch codex/server-home-window from a15621a0; pinned libmpv hydrated and SHA256 verified against engine.json.

Ruling: removal and Undo must preserve initial-card priority, including when a row completes after removal. The focused Undo regression reproduced expected 2/actual 1 and drove snapshot/manual restoration and projected-prefix correction.

Implementation complete; final full unit suite 2,817/2,817, fresh Release publish and final native row-window plus current Home regressions passed. Parent monitored queued final native verification (session 73737), then implementer verified the final PASS sentinel and zero-exit receipt. Independent review and remote mutation remain with the parent. No source version stamp or remote mutation performed.
