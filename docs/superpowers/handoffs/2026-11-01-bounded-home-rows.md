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
- `final-native-control`: **passed**, external shell session **73737** completed. Includes real Dismiss/Undo and the existing `HomeCurrentNativeFixture`; final PASS sentinel and receipt exitCode 0 verified against the final Release DLL.

Final product manifest: `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-e/final-product-fingerprints.json`, SHA256 `21F35654DF6327888CA9A8B65169EEE0453A1C0609D002FE42D645B235FA2B3C`. The manifest still matched all product sources after the final unit/publish checks. Receipts are precommit working-tree validation and record the base HEAD; the external task report maps those unchanged bytes to the committed task head.

Existing compiler/analyzer warnings remain in the app/native/unit fixtures; no new Task E warning was observed. `git diff --check` and implementer self-review passed. Verification is synthetic: it does not certify live-server visual parity, phone interoperability, playback buffering, or the entire default native suite. No installer, production endpoint, real account/settings/credentials, installed app control or release publication was used. Parent owns independent review, final native gate and any push/PR.

## Verified review correction pass

All grounded review findings were supported by production-path regressions and fixed: queued account/profile/server ownership; partial-row promotion to complete hero; real menu/messenger Undo surviving a pre-Undo refresh; French title/overview replacing the complete English prefix; capability HttpClient timeout falling back uncapped; and an unseen episode21 remaining removed after whole-show dismissal during completion. The initial native evidence used a different dismissal route; the new fixtures automate MediaItemMenu and the actual HomePage UndoButton, and also run the existing real Next Up dismissal suite. Shared batch/retry result application resolves the P3 duplication observation.

Fresh verification: `review-timeout-red` expected failure, focused `review-timeout-green`19 passes, `review-full-green` **2,819 passes**, `review-final-release-publish` success, `review-session-red` seven expected native failures against frozen EAD30AC0 bytes, and `review-session-green` all eight native review cases plus existing row/current/dismissal suites passed. The eighth case proves same-account token refresh remains usable; the account replacement case uses AuthService.SetTokens, the production new-session path. Earlier probes that conflated SetAccessToken with account replacement remain preserved and are explained in the report.

Current app DLL SHA256: `B803FC4EDF0BF79A374D62E71118675B9A92FF72770679B7293D903C4B25F3F3`. Current product manifest: `task-e/review-final-product-fingerprints.json`, SHA256 `3FCCF2414ED8512AFBB385B51E028634CF0DA4EDCCF08D252E3CE7715530A0AC`. The original report is frozen as `task-e-report-initial-6a5c331.md`; the current `task-e-report.md` maps every review finding to its evidence and final source head. No further native/Release test remains pending; remote work stays with the parent.

## PR68 grounded cloud follow-up

All four cloud inline findings on abb6223c were reproduced and corrected locally. Both section read paths now preserve null wrappers for mounted fallback. Null/malformed/transport-failed optional discovery shares uncapped fallback across the current layout's pending and queued rows; a later GetLayoutAsync retries transient discovery and can restore advertised bounded support. Queued validity checks inspect mounted collections on the dispatcher. No owner/language/generation or same-page reconciliation behavior was relaxed.

Fresh mutex-runner evidence under `home/`: `cloud-null-sharing-red` six expected API failures; `cloud-native-red` three expected failures on frozen B803FC4E; `cloud-focused-green`18 passes; `cloud-full-green` **2,825 passes**, zero failures/skips; `cloud-release-publish` passed; `cloud-native-green` passed all11 review cases plus existing row/current/dismissal suites. The red/green native fixture and Program sources are unchanged between runs. Final app DLL SHA256 `EBD01E208EAA49DC20869316306EC7C232C51A702400735C63F81F4B4FC26CFA` matches the actual native runner copy. `home/cloud-native-binding.json` records both original and final runner bindings; original/final source hashes and final commit mapping are in the external report. Product manifest SHA256 `BD8C7595AD2C02D78E576A7BB1346A5BABF4711E03802ADD13EB0B0EFED73F7E` still matches every product source after validation. Source version remains1.2.519; parent owns remote delivery. Frozen evidence and installed player remain untouched.
