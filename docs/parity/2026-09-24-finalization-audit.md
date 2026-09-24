# Desktop parity finalization — first verification pass

Prepared September 24, 2026 UTC (September 23 local). Audit only: the user will
select the next implementation work. No new application fixes, installation,
publication, production mutations or playback controls were performed in this pass.

Implementation follow-up: the user subsequently reordered these packages by effort
and selected the first six (visuals, subtitle availability, person refresh,
downloads, history imports and Account password settings). See the
[local 1.1.104 implementation record](2026-09-24-first-six-implementation.md).
The findings below preserve the original audit evidence and numbering.

## Exact baselines

- Official public repository: https://github.com/Silo-Server/silo-server.
- Freshly fetched `main`: **d4e35ba9df416e747822c6c9f2193b89c6b7e9fb**.
  Upstream commit time: September 23, 2026, 21:20:13 -04:00.
- Separate clean reference checkout: `.codex-tmp/silo-server-audit-d4e35ba9`.
  The older reference checkout was preserved. There are 98 commits since the
  desktop API-v2 reference `c80c5169f8e58f354fba35551e0c2bbcabb70b8a`.
- Desktop inspected: main `87010a0` plus the existing local **1.1.103** watched-state
  repair. These pre-existing dirty changes were preserved.
- Installed binary inspected read-only: **1.1.102-api-v2.2**, file version
  **1.1.102.2**. It was actively playing video; no navigation or input was sent.
  Neither the stable 1.1.102 publication nor the local 1.1.103 installer should
  be confused with that running build.
- Fetching upstream source does not establish the deployed server version.

## Verification actually performed

1. Current-source comparisons traced reachable UI entry points, API calls,
   state updates and corresponding current WebUI behavior across the former
   Substantial areas. Coverage and exact source locations are in the three
   domain reports linked below.
2. Fresh Release regression run: **1,062 passed, zero failed, zero skipped**.
   Command: `dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release
   --no-build --no-restore --blame-hang --blame-hang-timeout 90s
   --logger "trx;LogFileName=parity-audit-20260924.trx" -v:quiet`.
   Output: `.codex-tmp/parity-audit-20260924-tests.txt`; TRX remains in ignored
   `tests/SiloPlayer.Tests/TestResults/`.
3. An isolated executable linked the actual current smart-wizard and search
   view models with an in-memory HTTP handler. It reproduced two defects:
   two independently matched query groups became one all-matched group; and
   a duplicate search prevented a next-page request despite `has_more=true`.
   The coordinating audit reran and confirmed both results. No real collection
   was saved and no live request was sent.
4. Read-only installed UI observation found overlapping label/value text in
   Playback Info's Enhancement status row. The current Lua drawing code still
   has the same unbounded label/character-count truncation behavior.
5. The prior watched-state repair already has a real WinUI failure/pass comparison,
   six published-service checks and native artwork checks, recorded in
   [its repair record](../audit-gaps/2026-09-24-watched-detail-refresh.md).
   They were not rerun or recast as new whole-app visual verification here.

Passing the existing suite does not certify current parity: some tests assert
source structure or older pinned fixtures, and even behavior tests cover only
their scenarios. The newly reproduced collection/search defects passed through
that existing suite. No completion percentage is justified by the test count.

## Choose the next work package

Twenty findings are grouped into eleven implementation packages. These are
choices, not an authorization to implement every item. P1 denotes the saved-query
integrity risk; other functional gaps are P2 and minor layout/progress issues P3.

| Choice | Work package | Verified problem or gap | Evidence / priority |
|---|---|---|---|
| **1** | **Smart-collection editing** | Editing flattens mixed AND/OR groups; even a rename-only save can change membership. Preserve unscoped queries too. | **Reproduced**, P1; B1 |
| **2** | **Search completion** | Repeating the same search disables further pages; full Search never fetches people; its filters lack current guided/grouped query capabilities. | Paging **reproduced**; other gaps source-confirmed, P2; B2–B4 |
| **3** | **Watch Party controls and recovery** | Explicit room transport requests are not wired to player controls; reconnect attachment and current stall/catch-up/quality-offer policies differ. Host state reports can still propagate changes, so this is not a claim that all party control is broken. | Source-confirmed; two-client validation required, P2; PB-1–PB-2 |
| **4** | **Playback preferences and effective settings** | Missing profile-wide video/audiobook skip intervals; intro preferences lack Never/Ask/Always and undo; failed device-override clearing can still report a successful save/reset. | Source-confirmed, P2; PB-3, S2–S3 |
| **5** | **Reader progress and bookmarks** | Desktop chapter-based locations do not match WebUI CFI/whole-book locations; PDF page movement is not connected to shared reading progress. | Source-confirmed; real EPUB/PDF comparison required, P2; PB-5–PB-6 |
| **6** | **Subtitle-provider availability** | Actual native-player and detail search paths still offer online subtitle search when providers are disabled. Preserve upload and fail-open behavior on status errors. | Source-confirmed, P2; PB-4 |
| **7** | **Downloads correctness** | Save/delete failures can be silent; all non-transcoded output is suggested as MKV, regardless of actual container or server filename. | Source-confirmed native-flow defects, P2/P3; S6–S7 |
| **8** | **History-import state and progress** | Consumed Emby Connect authorization remains reusable in the UI; skipped entries are counted twice in progress. | Source-confirmed, P2/P3; S4–S5 |
| **9** | **Account password settings** | Current capability-gated WebUI password-change flow has no desktop counterpart. | Source-confirmed missing feature, P2; S1 |
| **10** | **Person-detail refresh** | An open person page does not follow completion of background metadata/photo refresh. | Source-confirmed missing flow; delayed-result fixture required, P2; B5 |
| **11** | **Small layout fixes** | Calendar week controls scroll away instead of staying available; Playback Info labels and long values overlap. | Calendar source-confirmed, panel observed installed; P3; B6, PB-7 |

Recommended order: **1, then 2**. Both include directly reproduced failures;
package 1 protects saved collection meaning. Choose the remaining packages by
the workflows you use most. None requires treating intentional native codec,
HDR or direct-play capabilities as deviations to remove.

## Verification packages still needed before closing areas

The source pass covered all principal groups below, but did not establish
complete installed visual parity. These are outstanding checks, not assertions
that every listed feature is broken or absent.

| Area | Installed acceptance checks |
|---|---|
| Shell, authentication, profiles | Login/device/OAuth and expiry paths using test identities; PIN and permissions; profile switching; Back/focus/controller; normal/narrow/high-DPI layouts. |
| Home and browsing | Shared theme/profile comparison; initial/cached/refresh states; empty and per-section errors; filters, paging and personal-list actions; large-library scrolling without blanking/freezes. |
| Detail and discovery | Movie/series/season/episode/person layouts, versions and actions; watched changes across Home/cards/detail; recommendations, requests and calendar states; dialog focus and permissions. |
| Collections | After package 1, complex rules through edit/preview/save, templates/imports, grouping/order, ownership, artwork/collages and schedule presentation. |
| Settings and integrations | Effective versus inherited values after reload and on another client; reset/partial failure; notification state; provider authorization/expiry; webhook/import outcomes and download lifecycle using controlled fixtures. |
| Audiobooks and readers | Real representative formats, cross-client resume, next chapter, unequal chapter lengths, speed/sleep timer, mini/expanded layout, PDF/comic page controls and reader focus. |
| Player controls and Watch Party | Track/quality/subtitle menus, markers/credits/autoplay/close, mouse/keyboard/controller, two actual party clients with permissions/reconnect/stalls. |

Native playback hardware checks remain a separate reliability lane: sustained
high-bitrate playback, HDR/Dolby Vision, lossless audio passthrough, and Intel/AMD
upscaling on the relevant hardware. The existing recurring-buffering investigation
remains open; these parity findings do not prove its cause or resolution.

No live settings/import/provider/password writes were attempted. Current playback
was not interrupted for screenshots of other pages. Those runtime cases must be
recorded against an exact installed build when they can be exercised.

## What is already present / outside scope

- API v2 integration shipped in 1.1.102; it is not an unimplemented future task.
- Library advanced filters, collection templates/imports/grouping, audiobook
  controls, notification APIs and provider integrations exist. Findings refer
  to specific incomplete paths, not wholesale missing areas.
- The watched-button repair is already prepared and verified locally in 1.1.103;
  its installed verification/delivery is separate from implementing it again.
- Server setup/administration were intentionally moved to the WebUI in 1.1.94.
  Old admin backlog counts are not remaining desktop parity work.
- Upstream server-only fixes do not need duplicate client implementations.

## Definition of finalized

Close a selected package only after its exact current-reference behavior is
implemented, meaningful regression checks pass, and the installed desktop result
has been compared with the WebUI for the relevant normal/error/empty/permission
and input/size cases. Record build, reference, steps and evidence. Distinguish
implemented/tested from installed-verified; a broad label or test total alone
must not mark an entire area complete.

## Detailed evidence and acceptance criteria

- [Browse, details and discovery: B1–B6](2026-09-24-browse-verification.md)
- [Settings, identity and integrations: S1–S7](2026-09-24-settings-verification.md)
- [Playback, Watch Party and readers: PB-1–PB-7](2026-09-24-playback-verification.md)

Each report includes current desktop/upstream file locations, matches that were
verified in source, proposed reproduction/acceptance steps, and evidence limits.
