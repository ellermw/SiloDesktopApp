# Current upstream delta and ranked desktop work — September 28, 2026

## Source and scope

Fetched official [Silo Server main](https://github.com/Silo-Server/silo-server)
directly on September 28 using `git fetch https://github.com/Silo-Server/silo-server.git main`.
The exact reference is
[`ad899be9d4fd9f33d4b9e9ac6873166026661c6d`](https://github.com/Silo-Server/silo-server/commit/ad899be9d4fd9f33d4b9e9ac6873166026661c6d),
dated September 28, 2026, 00:20:46 -04:00. It contains **78 commits** beyond
the September 27 audit's `5e49cc8d376d2aaa1a7b4601ff876895f8d82efe`.
Merge commits are included in that count; it is not a count of new features.

Compared exact Git objects without moving the existing reference worktree.
Desktop source remains **1.1.105**, commit
`3488a4942ee0d03448bd609d04334e74b963bc7d`. The earlier audit/README edits were
preserved. No installed application, playback or production server was changed.
This establishes latest fetched source, not the deployed server version.

This is a bounded delta review building on the
[September 27 audit](2026-09-27-fresh-audit.md). Previously confirmed gaps remain
open because desktop source is unchanged. No whole-app tests or visual comparison
were rerun for this documentation-only pass.

## Ordering method

The requested ordering combines breadth of user impact with implementation and
verification scope, descending toward isolated fixes. Size estimates are relative
engineering judgments, not promised durations. A small destructive defect can be
more urgent than a large feature: profile-limit preservation and smart-collection
rule preservation should be fixed promptly even if their package ranks below a
larger project. Each package needs focused verification before being called done.

## Ranked list: largest/broadest to smallest

These **16 packages replace yesterday's numbering**. The previous 13 remain
covered; Home controls and live-marker handling are added, and small presentation
fixes are separated from the larger detail-page package. New scope below means
newly identified in this delta review, not necessarily newly introduced in the
desktop. Within similarly sized packages, reach and functional impact guide the
order.

| Rank | Package | Relative scope | Work and acceptance focus |
|---|---|---|---|
| 1 | **Watch Party correctness and recovery** | Very large | Explicit controls, reconnect/reattachment, readiness and buffering coordination, catch-up, quality changes and room-end transitions. Requires multi-client failure/recovery verification. |
| 2 | **Reader progress compatibility** | Large | EPUB/CFI location mapping and PDF page progress, with actual documents and cross-client resume. |
| 3 | **Shared appearance policy** | Large | Map current shared theme tokens into native WinUI, reconcile obsolete profile themes and retain accessibility across pages. |
| 4 | **Detail pages and meaningful failure states** | Large | TV layout, reachable controls, expandable descriptions, missing-content versus transient errors, Retry and cached-detail removal; responsive/DPI checks. Small label/menu changes are rank 16. |
| 5 | **Search completion** | Large | Paging repair, people results and advanced filters with stale-request cancellation. **New scope:** people in the quick-search dialog as well as full results. |
| 6 | **Collections: safe editing and current imports** | Medium–large | Preserve mixed AND/OR groups, null scope and typed values. **New scope:** capability-gated public TMDB-list import and source editing/resync, alongside existing sources. |
| 7 | **Account access, recovery and import permissions** | Medium–large | Required-password-change login/restored sessions, password recovery and token transition. **New scope:** constrain history import targets to current profile unless the actor is admin/primary, and show target names where appropriate. |
| 8 | **Provider sync and dropping shows** | Medium | Ratings/watchlist/dropped-show controls, conditional saves, truthful whole-show dismissal, both Home rows and undo. |
| 9 | **Profile-limit preservation and advisory ages** | Medium; urgent integrity fix | Preserve existing unknown rating ceilings before any unrelated profile edit; add current choices, advisory-age restrictions and optional badges. The small preservation repair should not wait for the entire feature package. |
| 10 | **Optional detail-page theme music** | Medium | Theme metadata/grants and preferences, ownership/cancellation, navigation and interruption by every playback surface. |
| 11 | **Home customization** | Medium; new package | Hide-watched preference, recent-row library selection and generated ownership, consistent role/flag-aware section choices, and useful save failures. Server-side filtering already works when configured elsewhere; the native controls are missing. |
| 12 | **Playback preferences** | Medium | Profile-wide video/audiobook seek intervals, Intro Never/Ask/Always and undo, and accurate override-save/reset errors. |
| 13 | **Native subtitle delivery and startup diagnostics** | Small–medium | Validate original SRT rendering before advertising capability; emit real first-frame timing once per attempt without delaying playback. Existing HEVC/HLS declarations appear compatible with the new planner; validate HEVC/H.264 recovery rather than inventing another capability. |
| 14 | **Live intro/credits updates** | Small–medium; newly identified existing risk | Reproduce stale marker-segment state after realtime updates, then propagate detected/changed/withdrawn markers into active playback and the OSC. New server detection makes this path more relevant; no observed user incident is attributed to it yet. Detection/admin configuration remains in WebUI. |
| 15 | **Correct series Play/Resume target** | Small | Consume authoritative `play_content_id` with appropriate compatibility handling; verify noncontiguous watched episodes, fully watched/unavailable series and party defaults. |
| 16 | **Small presentation fixes** | Small | Distinct subtitle-translation source labels, empty-calendar navigation links, “For You” naming, and hiding/relabeling a quality menu with no meaningful choices. These can be delivered independently. |

This is the requested size/impact ordering, not a recommendation to postpone
small data-integrity fixes. The most urgent bounded repairs remain profile-rating
preservation (9) and collection-rule preservation (6), plus required-password
sign-in (7) for affected accounts. Recurring buffering remains the highest-impact
separate reliability work, with unbounded diagnosis effort until its cause is
established.

## Newly relevant upstream changes

- **Home:** profile-specific hide-watched preference, library choices on Recently
  Added/Released rows, and permission-aware section recipes.
- **Collections:** public TMDB lists are now an advertised import source.
- **Discovery/account:** quick-search people results; history-import target/run
  scope now depends on acting profile/admin status.
- **Playback:** explicit HEVC HLS negotiation and DV8 preservation, better quality
  ladders, expanded local credits detection, and clearer subtitle translation
  source labels. Most encoder/detection work runs on the server; the native
  declaration and live-marker consumption paths were checked separately.
- **Polish:** “For You” naming and links between empty calendar views.

Automatic library monitoring, encoder fixes, visible collection counts,
recommendation quota handling, chapter/credits detection algorithms, Jellyfin
compatibility and administrative log filtering are primarily server/admin work.
They are not duplicate desktop implementation tasks. New marker administration
does not reverse the existing decision to delegate server management to WebUI.
Actual new-server playback and metadata-event behavior still need runtime
acceptance; fetching source cannot establish deployed behavior or fix buffering.

## Evidence

- [New browsing/settings changes](2026-09-28-browse-settings-delta.md).
- [New playback and marker changes](2026-09-28-playback-delta.md).
- [Prior consolidated findings and acceptance criteria](2026-09-27-fresh-audit.md).

## Separate highest-impact reliability issue

Recurring buffering is still open and is not made safe by fetching newer server
source. The preserved September 27 incident shows successful range delivery
followed by repeated HTTP 404 responses and failed recovery. Its root cause still
needs evidence that distinguishes invalid client URL/session reuse from loss of
the serving resource. None of the new upstream changes establishes that this
incident is fixed. See `.codex-tmp/buffering-2026-09-27` and the prior audit.
