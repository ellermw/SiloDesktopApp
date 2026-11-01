# Task E handoff — bounded Home rows

Branch `codex/server-home-window`, base `a15621a0d6d5960d6b31a620a23ae259d734dedb`, unchanged source version 1.2.519. Official server/WebUI main `63ef34b236b88dc77acca1a9a3ed90235377691f` was supplied freshly fetched and verified in the read-only official checkout.

The contract is traced through `internal/apiv2/home.go` (capability maxima and row query), `web/src/hooks/queries/homeRowLimit.ts` (20-card first read, whole featured/expanded reads, stable completion), `web/src/pages/Home.tsx` (near-end completion and hidden-row recovery), and `web/src/pages/sectionDedupe.ts` (initial cards before tails, completion order). Desktop preserves the layout-first, five-request batch, existing hero and owner/cancellation/reconciliation routes. Other consumers retain the existing whole-row API default.

New state retains `has_more` and client completion boundaries through snapshots/reconciliation. Partial completion keeps shown cards, excludes concurrent removals and appends unseen cards to the whole row's own limit. A bounded refresh overtaken by completion cannot shrink it. Expanded refreshes read whole. Removals and Undo retain initial-versus-tail duplicate priority; fully hidden partial rows recover automatically. Old servers never receive an unadvertised cap.

Verification under external `Invoke-Verification.ps1` and `SiloOct18Build`:

- `red-contract`: both behavioral gaps reproduced (lost partial state; expansion taking another row's initial card).
- `green-contract`: 16 focused tests passed.
- `full-green`: 2,816 passed before final Undo correction.
- `red-undo`: expected prefix 2, actual 1 reproduced.
- `final-full-green`: **2,817 passed**, zero failed/skipped.
- `final-release-publish`: fresh Release passed; app DLL SHA256 `EAD30AC02DAA9FCF86B32D1724E8D5CF95CBDA35A37A300F03F87FB428BF648B`.
- `native-control`: actual SectionRow scrolling, cards 21–35, duplicate demand, cache refresh, error retry, concurrent removal/refresh, hidden duplicate row recovery and stale profile rejection passed before final Undo correction.
- `final-native-control`: **pending**, external shell session **73737**. Includes real Dismiss/Undo and the existing `HomeCurrentNativeFixture`; parent monitors and records completion before push. Do not cancel it.

Final product manifest: `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-e/final-product-fingerprints.json`, SHA256 `21F35654DF6327888CA9A8B65169EEE0453A1C0609D002FE42D645B235FA2B3C`. The manifest still matched all product sources after the final unit/publish checks. Receipts are precommit working-tree validation and record the base HEAD; the external task report maps those unchanged bytes to the committed task head.

Existing compiler/analyzer warnings remain in the app/native/unit fixtures; no new Task E warning was observed. `git diff --check` and implementer self-review passed. Verification is synthetic: it does not certify live-server visual parity, phone interoperability, playback buffering, or the entire default native suite. No installer, production endpoint, real account/settings/credentials, installed app control or release publication was used. Parent owns independent review, final native gate and any push/PR.
