# Easiest-first parity execution — October7,2026

User direction: start at difficulty item14 and work backward. These difficulty numbers are distinct from the implementation ledger's original package numbers. Official public GitHub main was fetched directly for this pass: **74158b4a8a799192312c13253552b8030af8575c**. The exact archive is under ignored `.codex-tmp/easiest-parity-74158b4`; its original WebUI components run against bounded local fixture responses. No production APIs, credentials or installed player controls were used.

## Execution order

Updated after resumed live and physical verification: **14 is finalized**, with its own1.2.108 installer.13 is next and remains incomplete. Earlier implementations and verified repairs in other areas remain retained.

| Difficulty item | Area | Current status / next work |
|---|---|---|
|14|Quick Search|Finalized against public051253dd; paired states, physical hover/Enter, shortcut/backdrop dismissal and regression gates pass. Local installer1.2.108.|
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

Continue14 through final acceptance, create its installer, then work13 down to1. Pending finalization does not mean these features are absent or all earlier work must be repeated.

## Resumed physical and live-browser verification

Ruling: the user's latest direction authorizes launching candidate builds and physical UI checks, and requires one installer after each whole difficulty item is finalized. This supersedes the checkpoint's earlier no-launch instruction. It does not authorize production administration or destructive data changes. Preserve all prior payloads and installers.

Official public GitHub main was fetched again for the resumed pass: **051253dd63118940e54a0a26477da24d811c569d**. GlobalSearch.tsx, personSearch.ts, Notifications.tsx, watchlist components and overlay presets have no diff from74158b4. This statement is limited to those paths.

The installed104 DLL matches the frozen verified104 payload. Actual physical pointer movement over the second Game of Thrones result highlights that row without taking focus from SearchBox; physical Enter opens Game of Thrones: The Iron Anniversary. Chrome live WebUI returns the same five library titles and four request suggestions for this query. Live frame width512, input48, results352 maximum and footer placement match the native dialog contract after accounting for viewport height and native titlebar.

Additional14 acceptance found gaps rather than silently closing the item:

- Repeated physical Ctrl+K leaves native104 open; Chrome's same chord closes its palette.
- Actual click on the native backdrop leaves104 open; Chrome's backdrop click closes without following the obscured media link.
- Exact person names retain non-exact people natively, unlike the current source. The new actual native test returned Portrait Person and Portrait Person Junior and failed with `Exact person match must exclude non-exact people from the visible option sequence.` Partial-name behavior and actual owned Enter destination are also covered by this fixture.

These failures now pass. The dialog captures Ctrl+K on its focused input because the modal popup isolates the main-window accelerator. The separate full-root WinUI smoke popup owns backdrop clicks; its handler is removed when the dialog closes. Ctrl+K retains the query for reopening, while Escape/backdrop/result selection clears it, matching the current WebUI.

## Item14 final acceptance —1.2.108

Actual physical108 candidate checks: populated Ctrl+K closes, reopening retains Game of Thrones, backdrop closes without changing Home or activating obscured media, reopening after backdrop starts empty, physical mouse hover highlights The Iron Anniversary without moving focus from SearchBox, and physical Enter opens that exact series. The frozen installer payload differs from this physical candidate only by a nullable-root guard and final source version metadata; all native checks below run against the exact frozen payload.

`parity-quick-accepted108-*` logs record actual native dismissal/query cleanup/full-search Enter/Escape, exact-person/partial-name filtering and owned destination, row/Play targets, stale requests/actions, request gating and circular portrait pixels.900/460 initial/results/selection/loading/empty/error/request and independent-source failure states pass with normal completion markers.1,406 Release unit tests and15 published playback checks pass. The pre-existing CollectionEditor nullable warning remains; no new warning was introduced. Public source and live Chrome were compared; no production administration or personal-list mutation was performed.

The final installer is `D:\SiloPlayer\installer\output\SiloInstaller-1.2.108-Setup.exe`; [receipt](../releases/1.2.108.md). Four distinct product corrections C105–108 were added after104. Item13 and all remaining groups remain open; this is not a claim of whole-app100% parity. The installed104 payload was not replaced; idle test candidates were launched under the user's authorization.

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
