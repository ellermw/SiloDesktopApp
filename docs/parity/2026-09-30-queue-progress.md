# Queue implementation evidence

The current local candidate is **1.2.0**, containing this feature batch plus the Requests poster-hover and Back-navigation repairs. October 1 verification passed all 1,267 automated tests, the full native regression host and six published playback-service checks. The [request interaction diagnosis](../audit-gaps/2026-09-30-request-poster-and-back.md) records the current installer path and fingerprint; 1.1.107 evidence below remains the earlier batch checkpoint.

Reference remains official GitHub main `8e2e840474a085c6df6571a5a2850f7eb996810c`, fetched again before implementation.

## 4 — Requests and titles outside the library

Implemented capability fields, season choices on external and catalog series details, shared native season dialog (missing aired/latest/upcoming), authoritative viewer state/outcome/season progress/download phases, ownership-aware follow, server withdrawal rules for Cancel, and complete external watchlist cursor traversal. External watchlist has native library/external tabs, sort/count/remove/open, status captions and configurable request badges. Profile auto-request control is under Settings > Requests, gated by manifest 15, and can restore inheritance after opting out. Unknown overlay IDs, order, document fields and item attributes survive editing supported badges.

Tests: 14 new focused request/preservation cases pass; 119 request/overlay/personal contract and existing source checks passed at the earlier checkpoint. Native host passed real season selection/default/empty/upcoming checks, follow and watchlist writes, external tab/count, and profile opt-in restoration. Latest native checkpoint: `.codex-tmp/native-artwork-tests/55d64947d62f4907b2a590577f35a0fd/results.txt`; full native host passed, including existing reader, party, appearance, subtitle, watched and artwork cases.

No installed app was changed. Existing 1.1.106 installer predates this queue work. New source is in the existing managed worktree; isolated publish is `.codex-tmp/parity-queue-publish`.

Acceptance still needed: current WebUI/native side-by-side visuals at normal/high DPI and authorized live multi-profile request/download/realtime convergence. These source and fixture checks establish implementation behavior, not production fulfillment or visual 1:1 completion.

## 5 — Detail pages

Implemented distinct missing/denied/transient failures, cache invalidation and Retry, race protection against an old failed read replacing a new item, accessible-library promotion for external titles, and a shared measured three-line More/Less overview. External series now use a poster season rail. Eight new detail/promotion behavior cases and 37 existing focused detail cases pass. Native overview/error/access checks passed (`.codex-tmp/detail-native.log`).

## 6–7 — Search and Collections

Search preserves paging on duplicate queries, publishes catalog independently of optional people/discovery reads, uses access-scoped people capabilities, remembers separate external pages on Back, exposes Discover Explore, and keeps keyboard selection by identity across all groups. Search and both collection editors share typed AND/OR groups. Wizard and general editor preserve mixed trees, null scope, false/numeric/array values and future fields. Imports use current capabilities, reject retired stale Trakt creation, and support TMDB lists including the real `collection_type=tmdb` / `source_config.mode=tmdb_list` representation. Poster removal reloads generated artwork.

91 focused Search/Collection checks passed before the final TMDB-mode correction; the additional real TMDB-mode save check passed afterward. Full native Search/Collection host passed (`.codex-tmp/search-collections-native.log`), including selected-row retention, independent page controls and typed range validation. Source/fixture checks do not establish live provider import or visual 1:1 acceptance.

## 8 — Account access and recovery

Added the authoritative required-password flag, fresh/restored sign-in routing before profiles, one-write password transition with refresh/account confirmation and settlement-only Retry. Public native recovery supports capability reads, generic email acknowledgment, scoped-link lookup, safe completion and explicit reload after an unknown outcome; no automatic spent-link replay. Imports restrict target selection to the acting profile unless admin/primary, guard late responses and display actual target names. 23 focused authentication/recovery checks passed, including session replacement and failed settlement. Native required-password save/settlement-only Retry and Account/import controls passed.

## 9–10 — Provider sync and profile limits

Added advertised watchlist/rating/drop controls and rating counters on the existing ETag path. Drop show snapshots both Home rows and exposes Undo on the actual card-menu path. Profile editing retains unknown rating ceilings and adds current national limits, independent advisory-age capabilities and truthful summaries. Advisory display and manifest-13 badges are separate from access policy. Eleven focused provider/profile/snapshot cases passed. Native account/profile checks passed, including unknown ceilings and independently unsupported advisory controls. Live provider synchronization and dismissal/Undo across clients remain acceptance work.

## 11 — Home layout transfer

Added current recipe permission checks, hide-watched preferences, stable section IDs and safe legacy Trakt preservation. Layout export/import has a 5 MiB limit, validation, preview, unique cross-server library mapping, skip reasons and context-bound writes. Import preserves saved legacy/admin rows, merges same-server IDs and retries a rejected same-server replacement with saved rows retained. One shared mutation gate serializes read/merge/write against ordinary edits/reset; the section editor stays disabled throughout import and server reload. Nine policy/merge cases passed before two additional malformed-file cases passed in final polish checks. Native file-picker/live import acceptance remains.

## 12 — Theme audio

Added opt-in detail-owner theme audio with capability/grant reads, supported-format declarations, cancellation, profile/account guards and playback interruption. Original delivery loops locally; converted delivery requests a fresh grant. Repeated interruption retains suppression; pending requests cannot revive stopped owners. Two lifecycle tests passed, including repeated interruption. Windows audio-device/live-grant acceptance and WebUI fade presentation remain.

## 13 — Seek and intro preferences

Video and audiobook seek intervals use effective profile settings across OSC, keyboard, now-listening and mini-player controls. Explicit legacy import and override/save errors are exposed. Intro Never/Ask/Always drives the actual OSC: Ask timing pauses with playback, expired offers return on re-entry, Undo appears only after confirmed movement and returns to intro start. Focused preference/marker checks and bundled OSC runtime cases passed.

## 14 — Subtitle appearance and startup diagnostics

Added independent text opacity, Gray, current box default and native previews. Original SRT support is advertised only after bundled libmpv decoding verified alignment, bold and italic preservation. Startup telemetry emits once per playback attempt, with viewer-intent latency only when known. The native playback-restart event is an output-ready boundary, not a measured physical display presentation. It does not invent latency for automatic starts or rebuffering. Focused opacity/telemetry and real decoder checks passed.

## 15 — Live markers

Changed/withdrawn file markers update the active player and structured OSC ranges immediately. Explicit empty ranges remain authoritative; omitted kinds survive partial updates. Core replacement/withdrawal cases and actual OSC skip-button withdrawal passed.

## 16 — Series playback target

Current detail `play_content_id` and watch rollup determine target and Resume/Play Next/Start labels. Both null and omitted targets mean unavailable, matching the server's `omitempty` serialization and current WebUI. No cached/inferred episode can revive that action. Series actions no longer perform the independent Continue Watching lookup or infer the next episode from watched counts. Season targets are honored when advertised. Five wire/rollup cases and native detail-action checks cover these cases.

## 17 — Presentation

Added For You labels, descriptive subtitle-source labels with collision/accessibility handling, Calendar legacy-preset normalization and alternate-view empty actions, and quality menus that respect authoritative empty tiers and party version restrictions. Two source-label cases, real OSC quality-menu behavior and native Calendar controls at narrow/wide sizes are checked. Quick search additionally orders exact person matches first, reports unavailable optional sources truthfully and offers discovery-page Retry.

## Final verification and acceptance

Current candidate is 1.1.107. Full automated suite: **1,267 passed, zero failed**. After final UI polish, 45 relevant source/surface checks passed. Clean x64 Release publish and all six published PlayerService checks passed. One final reviewer identified five issues (Home merge/write ordering, theme suppression, intro Undo destination and Ask re-entry); all were addressed with focused checks. The final native host passed: `.codex-tmp/native-artwork-tests/e568f509c71b4ba9b8c09d4c1c988679/results.txt`. It covers prior reader/Watch Party/appearance/Home/artwork regressions plus request/search/detail/password/profile and Calendar behavior, including final exact-person ordering and both null/omitted unavailable series targets.

Local installer: `D:\SiloPlayer\installer\output\SiloInstaller-1.1.107-Setup.exe`, 169,477,015 bytes. SHA-256: `1AA5AA06CEA1F0E40BD1D243759A8C2C3855554038D46508C238A7E973C9A5F5`. Inno Setup compilation passed after the final series correction. Published executable reports 1.1.107.0. Windows App Runtime dependency SHA-256 remains `8E21F22BF1191D7E347F6718BA8251D30B1E1C01775B9943C8E6239B7AF95EA7`. Installer is unsigned. No installation or GitHub publication was performed.

This is implementation/regression evidence. Whole-app visual parity, authorized live latest-server/provider/import/multi-client workflows, representative real books, theme audio and sustained playback remain acceptance work. No production change or installed-app replacement is part of this work. Recurring buffering remains a separate open investigation. Public main/download remain 1.1.105 until publication is authorized.
