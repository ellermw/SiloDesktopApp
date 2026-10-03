# Fresh desktop parity audit — September 27, 2026

## Exact references and scope

- Official source: [Silo Server](https://github.com/Silo-Server/silo-server).
- Fetched official GitHub `main` directly for this pass:
  [`5e49cc8d376d2aaa1a7b4601ff876895f8d82efe`](https://github.com/Silo-Server/silo-server/commit/5e49cc8d376d2aaa1a7b4601ff876895f8d82efe).
  Commit time: September 27, 2026, 00:48:08 -04:00.
- Previous audited reference: `d4e35ba9df416e747822c6c9f2193b89c6b7e9fb`.
  The new reference contains **132 additional commits**.
- Clean reference checkout: `.codex-tmp/silo-server-audit-20260927-5e49cc8d`.
  Existing reference checkouts were preserved. No legacy GitLab source was used.
- Desktop: released **1.1.105**, main commit
  `3488a4942ee0d03448bd609d04334e74b963bc7d`; clean before audit documentation.

This is a fresh source and contract comparison, with a focused offline replay of
the two earlier browse defects. It is not a live-server deployment check or
installed side-by-side visual signoff. Fetching source does not establish which
version is deployed on the user's server. No application changes, installation,
publication, production writes or playback controls were performed.

## Already delivered; do not reopen as missing

The first six selected packages shipped in 1.1.104: calendar/Playback Info layout,
subtitle-provider availability, person refresh, download correctness, history
import state/progress and Account password settings. The watched-button repair
also shipped. Version 1.1.105 added the Home metadata-refresh flashing repair.
New password-reset/login restrictions and new provider-sync options below are
additional upstream changes, not claims that those earlier fixes were absent.

Real-server acceptance for password changes, imports and provider operations still
remains distinct from the controlled tests already recorded for those releases.

## Updated work list

There are **eight additional work packages** below, plus the **five remaining
packages** from September 24. These are grouped implementation scopes, not counts
of individual defects or an effort ranking. Watch Party's newer behavior expands
its existing package rather than being counted twice. Priorities describe impact:
P1 is state integrity or a blocked workflow; P2 is a functional mismatch; P3 is
presentation or observability. None of these packages is implemented by this audit.

### Additions from the fresh comparison

| ID | Work package | Concrete difference and completion criteria | Evidence |
|---|---|---|---|
| N1 | **P1: Profile limits and advisory-age support** | The editor maps an unrecognized rating such as `12` to “Any content,” then sends that empty value even on a name-only save. Preserve existing and future unknown values; add current rating choices. Separately implement capability-gated advisory-age restrictions, preferences and badges. Missing advisory fields are currently omitted, not cleared. | Settings SD1/SD4; Browse N4 |
| N2 | **P1: Required password changes and account recovery** | Temporary-password login goes to profile selection even though the server requires a password change first. Handle restricted login and restored sessions, update tokens after completion, then navigate normally. Add capability-gated Forgot password or a deliberate WebUI handoff. Existing Account password settings do not cover this pre-profile flow. | Settings SD2/SD3 |
| N3 | **P2: Correct series Play/Resume target** | Consume `play_content_id` instead of guessing from watched counts. Noncontiguous watched episodes can currently select an already-watched episode. Match server primary-action semantics and party defaults, including unavailable and fully watched series. | Browse N1 |
| N4 | **P2: Provider sync and show-drop behavior** | Add supported ratings and dropped-show sync options, plus the older missing watchlist toggles, preserving conditional updates. Home must describe episode/series dismissal as a whole-show action and reconcile both rows and undo. The server already performs the drop; desktop wording understates it. | Settings SD6; Browse N2 |
| N5 | **P2: Current appearance policy** | Adapt to Cinema Dark and supported server-wide theme tokens; retire or reconcile obsolete profile-specific theme controls. Keep native accessibility and independent formatting controls. Arbitrary WebUI CSS cannot be applied directly to WinUI; server administration remains in the WebUI. | Settings SD5 |
| N6 | **P2: Optional detail-page theme music** | Add available-theme data, capability/preference support and scoped playback grants. Stop or suspend correctly across navigation, profile changes, trailers and media playback. Preserve opt-in behavior. | Browse N3; Settings theme preferences |
| N7 | **P2/P3: Current detail, error and control presentation** | Distinguish missing content from transient failures and provide Retry; replace cached detail after an authoritative 404. Match the new TV detail layout and More/Less overview at relevant window sizes. Hide a quality selector with no choice and label a version-only selector appropriately. | Browse N5/N6; Playback PB-N4 |
| N8 | **P2/P3: Native subtitle delivery and startup diagnostics** | Validate and negotiate original SRT sidecars through `subrip_sidecar_v1`, preserving older-server and embedded-subtitle behavior. Separately report first-frame timing once per playback attempt from actual frame evidence. Missing timing telemetry is not proof of a startup-speed defect. | Playback PB-N1/PB-N3 |

### Original five packages still open

The numbers below preserve the previous list's 7–11 identifiers. They are not
re-estimated effort rankings after this audit's added scope.

| Previous ID | Work package | Fresh status / acceptance |
|---|---|---|
| 7 | **P1: Smart-collection rule preservation** | Offline reproduction still flattens mixed AND/OR groups. Preserve groups, per-group match, outer match, null media scope and typed values during metadata-only edits, preview and save. |
| 8 | **P2: Playback preferences** | Integrate profile-wide video/audiobook seek intervals and Intro Never/Ask/Always with undo. Surface failed device-override deletion instead of reporting a successful save/reset. |
| 9 | **P2: Search completion** | Duplicate submission still disables paging; the focused fixture reproduced it. Connect people search and support the current guided/advanced query groups while preserving cancellation, scope and paging. |
| 10 | **P2: Reader progress compatibility** | Reconcile EPUB/CFI positions with desktop chapter/fraction positions and implement PDF page progress. Verify resume with actual documents and cross-client positions. |
| 11 | **P2: Watch Party controls and recovery** | Wire explicit transport actions, restore the same-session attachment after reconnect, and verify readiness/buffering/catch-up/quality behavior. New scope: handle playing-to-lobby transitions when the server ends an item, without treating the initial lobby snapshot as a playback stop. Assess preroll behavior for the native engine rather than copying browser-only workarounds. |

**Recommended first batch:** protect profile limits (N1), unblock required
password changes (N2), and preserve collection definitions (7). These can change
saved user policy/data or prevent access. Then fix deterministic selection and
paging defects (N3 and 9) before larger presentation and multi-client packages.
Recurring buffering remains a separate reliability investigation below.

## Verification performed and limits

The existing offline browse harness linked the current smart-wizard and Search
view models, intercepted HTTP in memory, and rejected non-GET requests. It
reconfirmed two defects: two input rule groups became one, and repeating a search
prevented a second page request despite `has_more=true`. No collection was saved.
The harness's exit code 0 means its expected defect reproductions succeeded; it
does **not** mean the affected application behavior passed.

New findings are source-confirmed contract/behavior differences with concrete
paths and acceptance cases in the domain reports. They still require focused
implementation tests and installed verification. No new whole-app test pass or
pixel-level parity claim is made. Existing release verification remains historical
evidence for its stated scope, not certification against this newer reference.

## Upstream changes that do not automatically require desktop work

- Server-side Dolby Vision failure-to-HDR10 planning, subtitle variant matching
  and transcode cancellation are not newly missing client APIs. The desktop
  already participates in protocol-v3 failure replanning; hardware acceptance
  remains outstanding.
- MDBList paging, recommendation selection, maturity enforcement, show-drop
  persistence/provider dispatch and download reconciliation are server logic.
  Their existence does not justify duplicating them in the desktop.
- Browser fullscreen, chunk loading and browser-specific preroll workarounds
  require equivalent user outcomes, not literal native copies.
- Owner status does not replace the existing `admin`/`user` API roles. Server
  administration remains intentionally delegated to the WebUI.

These changes are inherited only when the corresponding server version is
deployed; this audit did not check or update production.

## Detailed evidence

- [Browse, Home, details, search and collections](2026-09-27-browse-delta.md).
- [Identity, profiles, appearance and integrations](2026-09-27-settings-delta.md).
- [Playback, Watch Party, subtitles and readers](2026-09-27-playback-delta.md).

## Separate reliability issue

Recurring playback buffering remains open. The September 27 incident recorded
successful direct-stream range responses followed by 61 HTTP 404 responses and
unsuccessful recovery. That establishes delivery failure; it does not decide
whether the app retained an invalid URL or the serving infrastructure lost the
resource. Neither a newer upstream commit nor this parity audit proves it fixed.
Preserved incident captures: `.codex-tmp/buffering-2026-09-27`.
