# Current objectives and execution order — September 30, 2026

Execution update: packages 4–17 have implementation and regression evidence in local candidate **1.1.107**. The order and source-comparison snapshot below are retained for traceability; see [queue progress](2026-09-30-queue-progress.md) for current verification and remaining live/visual acceptance.

## Reference and desktop state

Fetched `main` directly from the authoritative public
[Silo Server repository](https://github.com/Silo-Server/silo-server) on September 30.
Exact reference:
[`8e2e840474a085c6df6571a5a2850f7eb996810c`](https://github.com/Silo-Server/silo-server/commit/8e2e840474a085c6df6571a5a2850f7eb996810c),
commit timestamp `2026-09-30T11:50:24-04:00`. It is **50 commits** beyond the
September 28 reference `ad899be9d4fd9f33d4b9e9ac6873166026661c6d`, including merges.
The reference checkout was not moved; comparison used exact Git objects.

Public desktop main still contains 1.1.105 at
`3488a4942ee0d03448bd609d04334e74b963bc7d`. The latest prepared desktop is the
**1.1.106 local candidate**, with uncommitted implementation in managed worktree
`C:\Users\Michael\.codex\worktrees\parity-top-three\SiloPlayer` on
`codex/parity-top-three`. Its installer is
`D:\SiloPlayer\installer\output\SiloInstaller-1.1.106-Setup.exe`.
The comparison uses that candidate for implemented behavior, rather than
mistaking released main's older source for the whole current project.

This pass updates objectives using source evidence. It does not establish the
deployed server revision or current installed version. Existing 1.1.106 verification
remains historical evidence; no app build, whole-suite test or production operation
was repeated for this documentation pass.

## Implemented first three

| Execution number | Package | Current status |
|---|---|---|
| 1 | Watch Party correctness and recovery | Implemented in 1.1.106 with deterministic two-client and published native integration checks. New socket-renewal/episode-end changes are covered structurally; host pause-report fix is server-owned. Live two-device/latest-server acceptance remains. |
| 2 | Reader progress compatibility | Implemented in 1.1.106: shared EPUB/PDF locations, bookmarks/resume and lifecycle guards; 29 renderer checks and actual-page saves/restores passed. Representative real books and live cross-device acceptance remain. |
| 3 | Shared appearance policy | Implemented in 1.1.106: shared native color tokens, retired profile theme editing, accessibility/date-time retained. No new shared-theme contract delta identified. Whole-app visual acceptance remains. |

See the [implementation and verification record](2026-09-28-top-three-implementation.md).
These packages are not returned to the unimplemented queue. Their remaining live
acceptance is a validation track, not a request to redo their implementation.

## Remaining execution order

Retains the user's **broadest/largest to smallest** ordering. New viewer requests
and external watchlist behavior form one substantial package because they share
title identity, request capabilities, statuses and detail/card entry points.
Home moves ahead of optional theme music because layout transfer expands its scope.
Numbers below supersede the old remaining numbers; the previous number is retained
for traceability. Relative scope is an estimate, not a completion-time promise.

| Next number | Objective | Prior number | Acceptance focus |
|---|---|---|---|
| 4 | **Requests and watchlist titles outside the library** | New, integrates relevant old 4/8 surfaces | Current request capabilities/statuses, discovery/details/cards, season selection including missing seasons of existing series, follows/cancellation/limits/download state, external watchlist tabs/title details, auto-request preference and request-status badge. Server routing/admin configuration stays in WebUI. |
| 5 | **Detail pages and meaningful failure states** | 4 | TV layout, reachable actions, expandable descriptions, absent versus transient errors, Retry and cache removal; Latest/Upcoming season shortcuts and current availability. Reuse title/request surfaces from 4. |
| 6 | **Search completion** | 5 | Repair paging, people in full/quick search, advanced filters, keyboard access to all result rows and stale-request cancellation; current row alignment/scroll restoration. |
| 7 | **Collections: safe editing and current imports** | 6 | Preserve mixed AND/OR trees, null scope and typed values; current capability-filtered personal templates, TMDB-list import/source editing/resync, retired-Trakt stale-draft handling while preserving existing sync/edit, refreshed artwork and accessible generated posters. Fresh server templates already hide retired Trakt creation. |
| 8 | **Account access, recovery and import permissions** | 7 | Required-password-change login/restored sessions, safe token transition/recovery, acting-profile/admin import scope and truthful target names. |
| 9 | **Provider sync and dropping shows** | 8 | Ratings/watchlist/dropped-show controls, conditional saves, whole-show dismissal in both Home rows and undo; current Trakt stale-credential/reconnect presentation. Token refresh/pagination fixes remain server-owned. |
| 10 | **Profile-limit preservation and advisory ages** | 9 | Preserve unknown/new rating ceilings on unrelated edits; capability-aware advisory access/display and age badges. Preserve unrecognized stored badge settings and gate new choices by manifest revision. |
| 11 | **Home customization and layout transfer** | 11, expanded | Hide watched, recent-row library selection, allowed recipes, override IDs/legacy Trakt rows, useful save/reset errors, export/import preview and safe same-/cross-server mapping. |
| 12 | **Optional detail-page theme music** | 10 | Metadata/grants/preferences, cancellation/ownership, navigation and interruption by every playback surface. |
| 13 | **Playback preferences** | 12 | Profile-wide video/audiobook seek intervals, Intro Never/Ask/Always and undo, accurate override/reset errors. |
| 14 | **Native subtitles and startup diagnostics** | 13, expanded | Preserve/render independent text opacity and Gray color, capability-aware writes/defaults; validate original SRT before advertising it and once-per-attempt first-frame timing. Current bitrate ladder/download changes require native acceptance, not a desktop encoder. |
| 15 | **Live intro/credits updates** | 14 | Reproduce stale markers, then propagate changed/withdrawn markers to active playback and OSC. Detection/admin controls stay server/WebUI. |
| 16 | **Correct series Play/Resume target** | 15 | Authoritative `play_content_id`, noncontiguous watched episodes, fully watched/unavailable cases and party defaults. |
| 17 | **Small presentation fixes** | 16 | Subtitle translation source labels, empty-calendar navigation, “For You” naming, meaningful quality menus and isolated polish. |

There are **14 remaining implementation packages**, plus live acceptance for 1–3.
Before widening UI in an affected package, fix its destructive save paths: profile
rating ceilings (10), collection rules (7), new badge fields (4/10) and subtitle
text opacity (14). Those bounded preservation repairs may be taken ahead of the
full packages; size ordering is not permission to keep losing saved preferences.

## What changed since the last list

- Added package 4 for the substantial new **viewer** requests/external-watchlist
  contracts. Existing request/discovery pages are present; their old DTOs and
  behavior need migration rather than another duplicate implementation.
- Expanded Home with layout transfer and safe override IDs. Expanded profiles/
  badges and subtitles for settings manifest **revision 15** (previously 12).
- Expanded detail acceptance for Latest/Upcoming seasons, search keyboard/scroll
  behavior, and collection template/import/artwork policies.
- Kept 1–3 implemented, with latest-server acceptance only where new upstream
  corrections interact with them. Do not attribute standalone buffering to these
  commits without incident evidence.

## Separate playback reliability work

Recurring standalone buffering remains open and takes priority when a reproducible
incident is available. This source refresh supplies no new evidence proving its
root cause or resolution. Keep the preserved September 27 range-delivery/404
incident and previous diagnostics as evidence; source freshness is not a fix.

Prepared download bytes now retain all audio tracks server-side. The current
desktop file saver receives those bytes, but has no offline manifest importer,
ASS/PGS sidecar importer or create-quality selector. Record those as a future
offline-download scope decision rather than claiming this server change adds
those desktop features or breaks an existing importer. Current save/status/
failure acceptance remains in package 4 and the playback delta report.

## Supporting evidence

- [Browse, requests and watchlist delta](2026-09-30-browse-delta.md).
- [Playback, subtitles and downloads delta](2026-09-30-playback-delta.md).
- [Settings, Home and integrations delta](2026-09-30-settings-delta.md).
- [Previous ranked list](2026-09-28-ranked-work.md) and
  [September 27 consolidated audit](2026-09-27-fresh-audit.md).

Preserved unrelated dirty changes. No app implementation, installation, user
playback control, commit/push, or production infrastructure change in this pass.
