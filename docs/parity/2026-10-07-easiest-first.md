# Easiest-first parity execution — October7,2026

User direction: start at difficulty item14 and work backward. These difficulty numbers are distinct from the implementation ledger's original package numbers. Official public GitHub main was fetched directly for this pass: **74158b4a8a799192312c13253552b8030af8575c**. The exact archive is under ignored `.codex-tmp/easiest-parity-74158b4`; its original WebUI components run against bounded local fixture responses. No production APIs, credentials or installed player controls were used.

## Execution order

Updated after the user's status question: **neither14 nor13 is marked fully finalized**. Seven fixes in these areas are verified; that is distinct from completing their entire acceptance checklist. Earlier implementations and verified repairs in other areas remain retained.

| Difficulty item | Area | Current status / next work |
|---|---|---|
|14|Quick Search|Fixes implemented and verified; final physical mouse hover/selection check remains open.|
|13|Favorites / Watchlist / Notifications|In progress. Notifications row/preferences repairs verified; Watchlist badge sizing and remaining visual/interaction/state comparisons remain.|
|12|Person / Calendar|Pending finalization.|
|11|Filters|Pending finalization.|
|10|Metadata / conditional dialogs|Pending finalization.|
|9|Requests|Pending finalization.|
|8|Collections / wizard|Pending finalization.|
|7|Home / library|Pending finalization.|
|6|Movie / series / audio / manga detail|Pending finalization.|
|5|Shared controls / shell|Pending finalization.|
|4|Authentication / profiles|Pending finalization; prior verified sign-in repairs retained.|
|3|Settings|Pending finalization; prior verified account/linking repairs retained.|
|2|Watch Party|Pending finalization.|
|1|Native playback / reader|Pending hardening/final acceptance; prior playback and reader repairs retained.|

Continue13, resolve14's remaining physical-input check when available, then work12 down to1. Pending finalization does not mean these features are absent or all earlier work must be repeated.

## Current checkpoint

**1.2.104** contains seven distinct product corrections after1.2.97. IDs C098–104 and detailed evidence are in the [ledger](2026-10-01-parity-implementation-ledger.md). These numbers count corrections, not tests, files, visual properties or completed checklist groups.

- **14 Quick Search:** current-source initial/results/selected/loading/empty/error/request-library/request-only frames compared at900/460; partial failures and slow catalog preserve the matching healthy-source presentation. Server playable-target versus detail-target behavior, absent Play for unavailable targets, circular decoded person artwork, input keyboard boundaries and actual Enter destinations pass. Pointer-selection identity callback passes with retained focus; this offscreen fixture does not prove physical mouse event delivery. No claim of real provider/network throughput acceptance follows from bounded fixtures.
- **13 Notifications:** current two-row wide/narrow rendering paired with current WebUI. Timestamp gutter,28px inline action,16px row/10px artwork corners and288x251 Preferences inner panel now pass measured native checks. Existing external notification actual invocation and request-destination routing pass.
- **13 Watchlist:** actual sidebar Catalog route, external tab/count/attention, dedicated large-card density, five overlay presets, download progress and retained-data retry pass. Current500px WebUI external tab was compared with the native capture. Subsequent actual measurements show title/meta/status caption origins within1px of source; the initial impression of a caption-spacing defect was not confirmed. Overlay badge geometry still differs: current source derives height from scaled font/padding/border while native BuildBadge retains fixed preset heights. This remains unaccepted shared-control work; it is not silently declared equal or counted as repaired.
- **13 Favorites and remaining states:** final current-source populated/empty/loading/error/context/actual removal pairs, notification read-all/filter/dismiss/realtime/pending preference cases and final overlays/hover physical checks remain open. Earlier source/unit checks are supporting evidence, not whole-group acceptance.
- **12 onward:** remaining finite acceptance records stay open. PersonDetail.tsx, Calendar.tsx and Notifications.tsx themselves are unchanged between the previous reference478afa5 and74158b4; dependencies and rendered states still require verification before completion. Catalog's collection-shuffle delta belongs to item8; no unrelated shortcut rewrite was made here.

## Verification and review

Final published1.2.104 payload: `.codex-tmp/parity-easiest-candidate/payload`. All native final logs end with exit0 and `PASS: native regression run completed.`:

- `parity-quick-interactions-final104.log`: real detail UIA/owned keyboard/PlayerService playable-target preparation, absent target, decoded portrait and populated-to-pending/stale-Play transitions.
- `parity-quick-pointer-final104.log`: identity callback/retained input/actual Enter; explicitly excludes physical pointer synthesis.
- `parity-quick-visual-final104.log`:900/460 frames, selected hints, request headers/status/dimming.
- `parity-quick-failures-final104.log`: people error, optional request failure, catalog failure with healthy people, slow catalog.
- `parity-personal-final104.log`:1280/900/500 external Watchlist and Notifications; additional `parity-personal-caption-measure104.log` records actual caption positions.
- `parity-easiest-unit.log`:1,406 pass, zero fail/skip. Three old string-only assertions were updated to the newly verified current contract; no behavioral failure was suppressed.
- `parity-easiest-playback.log`:15 actual published-service checks pass; accepted player/OSD bytes are unchanged.

Independent review identified retired results staying visible until a new source completed, and their Play action bypassing current search ownership. Actual profile/all-source-pending and debounce tests reproduced both failures. Starting the new query through Render removes retired rows; captured query/owner guards prevent stale Play before dismissal. Final review found no remaining substantive issue in that correction. Review fixes are included in C102, not counted again.

Fresh source screenshots/DOM measurements are in `.codex-tmp/easiest-webui-74158b4-captures`; native screenshots/logs are under `.codex-tmp/native-artwork-tests`. Native/browser font rendering and subpixel rounding are distinguished from layout mismatch; Quick dialog outer height differs by at most2px in wide status states. Native Quick capture uses an isolated black backdrop, not the full app body, so it is a dialog comparison rather than whole-shell acceptance.

## Handoff

Continue item13's remaining states and shared overlay discrepancy, then12 downward. Preserve the frozen104 payload/installer, prior97 payload/installer, installed app and original dirty D:\SiloPlayer checkout. Publish final parity/main/README only when its accepted scope is concrete; current public release remains1.2.94. No automatic installer launch or playback interruption is authorized by this checkpoint.
