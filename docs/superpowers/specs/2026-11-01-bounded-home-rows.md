# Bounded Home rows

Approved Task E, desktop base a15621a0; official Silo main 63ef34b236b88dc77acca1a9a3ed90235377691f (read-only local official checkout).

Home reads non-featured rows with item_limit=20 only when row_item_limit_max >=20. Featured rows and previously expanded rows read whole. Missing/unsupported capabilities preserve old-server uncapped reads. Preserve row_item_limit_max and section_item_limit_max wire fields and has_more. Do not alter section recipes or settings.

Native carousel near-end demand completes a partial row with an uncapped read once, sharing in-flight demand and retrying after errors. Merge existing cards first, then unseen whole-row cards; cards removed since request start stay removed. Cross-row dedupe considers every row's initial cards before expansion cards, in completion order. Fully hidden partial rows recover automatically. Expanded cache stays expanded on refresh. Context, language, cancellation, generation and reconciliation ownership remain authoritative.

Acceptance: synthetic wire regressions for cap negotiation/fallback; completion ordering/dedupe/removal/cache tests; native mounted Home/SectionRow scrolling, duplicate demand, error retry and retired owner; full unit suite and fresh Release publish. No installed app control or release publication.
