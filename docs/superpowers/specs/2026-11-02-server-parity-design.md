# November 2 server/WebUI alignment — proposed scope

Status: owner approved the full plan on November 2, 2026. Implementation and Beta PR delivery are authorized; Stable/main promotion is not.

## Intent and sources

Bring the native desktop viewer in line with the new official server/WebUI behavior, using focused PRs through the existing Beta pipeline. Fetch `origin/beta` before starting each PR and branch from that exact current source. The comparison began at Desktop `6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af` (`1.2.544-beta.1`). Official server main was fetched directly and pinned to `bf53b65450244751013a2965b39da4111d9a0f72`, 35 commits after the prior `63ef34b236b88dc77acca1a9a3ed90235377691f` comparison. Later Beta changes must be retained and checked for overlap before implementation.

## Approaches

1. **Full new viewer parity in independent PRs — recommended.** Implement the concrete compatibility corrections and the new movie-box-set viewer surfaces. This gives the desktop the same browsing concepts as the current WebUI, but requires native layout and lifecycle verification.
2. **Compatibility first.** Deliver the smaller protocol/presentation corrections and defer box sets to a subsequent feature pass. Lower immediate UI scope, but the desktop would continue browsing these libraries differently from the WebUI.

The owner selected approach 1 and approved this full design. Native implementation details remain builder decisions within these requirements.

## Independent changes

### Playback limits

Mirror current viewer-facing titles for `stream_limit_reached` and `transcode_limit_reached`. Use the server's trimmed nonblank message, otherwise the WebUI guidance to stop another playing or paused session and retry. Keep actual server-capacity errors distinct. Preserve the terminal's retryability and verify that a user retry creates a fresh playback attempt rather than replaying a stored terminal.

### Embedded subtitle inventory compatibility

Preserve the server's current embedded stream identity and unavailable-track metadata through response adaptation to actual selection controls. The new server can list index-only WebVTT subtitles which the desktop demuxer does not enumerate. Never guess the selected native SID from the combined inventory ordinal when that can select a different track. Resolve a track using the actual loaded native inventory where identity is demonstrable, otherwise use its supported sidecar/server route. An explicitly unplayable track stays visible as unavailable and cannot be selected through any player menu or callback. Retain older-server compatibility, subtitle-off behavior, latest manual choice, timeline, actual viewer pause intent and room authority. Do not advertise HLS/native capabilities without implementing and verifying their actual route. No Roku-specific renderer limitations are copied to libmpv.

Negotiated-audio enforcement changed server FFmpeg/Jellyfin internals; it is not a new native request field. A separate desktop hardening change is justified only if a regression distinguishes legitimate silent media from missing negotiated audio. Do not infer this caused the owner's buffering incident.

### Busy subtitle-sidecar recovery

In a separate focused PR, honor the subtitle-read route's retryable `503` and `Retry-After` without blocking the native UI thread. Keep retries bounded, cancel on a newer selection/load/page owner, retain size and URL-authority checks, and leave the previous selection intact on an exhausted attempt. A busy prefetch must not fall through to a synchronous remote `sub-add` that repeats the same blocking request. Other failures retain actionable feedback and existing supported fallback behavior. Do not apply this rule to HLS segment `404` responses, whose retry semantics differ. This correction does not opt into all-track HLS renditions.

### Episode descriptions

When an episode has no nonblank overview, show the series overview resolved in the current library/language scope. Render the series translation status and translate that series description, rather than writing it to the episode. A series overview reveals no episode-specific plot and remains visible under episode spoiler protection. An episode's own overview retains reveal/spoiler behavior: hide an unwatched episode's hero plot while the preference is unknown or its initial read is in flight. A definitively failed optional read without a known hide value, or an older unsupported server, may use the visible default; retain known protection on same-viewer retries. Episode-card policy is unchanged. Missing/failed fallback must not block the detail page. Retire delayed reads/translations when the item, library, profile, language or page owner changes.

### Synced collection sort direction

Expose the existing supported ascending/descending choice independently of the selected default-sort field. Use appropriate alphabetical/date/numeric labels; source order has no direction. Preserve untouched unknown/legacy sort configuration, explicit source-order clearing, read-only/save gates and compact responsive layout. No new API endpoint or forced collection rewrite.

### Movie box sets

Follow the official viewer behavior; leave server administration and TMDB synchronization on the server.

- Add typed box-set summary/detail and library flags. Use the current capability/library gates and ordinary captured API context. Older servers keep the existing movie browser with no unsupported requests.
- Add a dedicated native box-set detail page with title/artwork/overview, available and played movie counts, release-ordered movies, ordinary movie navigation and normal scoped playback. Its primary action follows the WebUI: first unplayed movie in collection order, or first movie if all are played; use the returned library and existing resume/room/authorization path. Preserve the WebUI's loading, retryable read failure and unavailable states. A box set itself is not a movie file or a curation target.
- Add the movie-detail box-set row in release order, with the current movie marked inside its own card bounds and a link to the complete box set. Optional read failure hides the row without failing the movie page. Hide cached rows after their library disables box sets.
- In enabled movie libraries, request server grouping rather than grouping downloaded cards locally. Preserve exact server totals, cursor/seek ownership and filters. Send the identical grouping scope to the alphabet index; the rail must target the grouped list or be hidden until that index is supported. Box-set cards open box-set details, ordinary cards retain normal actions, and missing artwork has a native fallback.
- Add box-set results to the existing full and quick search surfaces, using the server's limit and query/library gates. Do not block ordinary movie results or retain results from an older query/profile. Disabled/unavailable box-set search remains optional.
- Refresh affected counts/rows on the existing watched/catalog refresh mechanisms; preserve in-progress PR #69 and let the pipeline reconcile concurrent changes against newer Beta.

Suggested PR sequence: (1) typed reads, detail page and movie row; (2) grouped Library cards and matching letter requests; (3) full/quick search. Every submitted PR must be useful and safe independently. The pipeline handles dependency reconciliation; never submit an incomplete grouped browser whose cards have no destination.

These follow the five independent compatibility/presentation PRs above: playback limits, subtitle identity/unavailable rows, busy sidecar recovery, episode descriptions and synced-list direction. Negotiated-audio hardening is excluded unless a separate demonstrated defect warrants it. HLS all-track negotiation and continuous seek-preview sprites remain explicitly outside this new-contract pass; current native sidecars remain supported, and the external trickplay endpoint does not change the wire shape.

## Changes consumed without desktop code

Server scanner/artwork/storage/auth robustness, MPTCP default, catalog grace-period counts, progress-list access cleanup, plugin resilience, notification registration and Jellyfin compatibility remain server behavior. Existing native marker-grant checks and shared recommendation badge fields must be checked against the new source before any duplicate implementation. The new external trickplay endpoint changes server URL delivery; continuous seek-preview sprites are a separate existing desktop feature gap, not evidence that this endpoint requires a new protocol. Record any such gap explicitly rather than claiming it implemented.

## Verification and delivery

Clean beta baseline: 3,058 unit tests pass, zero failures/skips. The pinned native engine was hydrated and its SHA256 checked before that run. Record each PR's exact source, upstream contract, red/green evidence and independent Standards/Spec review. Test real native controls with synthetic accounts/data, including narrow/wide geometry, keyboard access and stale completion ownership. Source comparison and synthetic screenshots do not establish full visual parity or real-server/hardware interoperability.

Hold `SiloOct18Build` for every .NET build/test; use fresh isolated outputs and preserve all prior evidence. Do not read credentials, change production, launch setup or control the installed player. Push same-repository branches and open PRs against `beta`, leaving version stamps, merge, installer and prerelease publication to the pipeline. Do not promote Stable or update `main`. Recheck remote heads/discussion before delivery and preserve concurrent pipeline corrections.

## Design review

A separate builder check found no significant unanswered product decisions beyond the full-box-set versus compatibility-first choice. Its busy-sidecar scope caveat is included explicitly above. Playback and non-playback researchers checked the corresponding design claims against pinned primary source. Detailed findings are in `docs/research/2026-11-02-playback-delta.md`, `2026-11-02-boxsets-delta.md` and `2026-11-02-other-server-delta.md`. These reviews are source/design evidence; implementation and its independent Standards/Spec gates remain to be done after the owner's design decision.

## PR3 implementation note — server grant boundary

Non-busy text prefetch failures (including the eight-second managed-read budget, the 16 MiB buffering cap, and temporary-file write failures) use an asynchronous, owner-cancellable managed URL download streamed to disk. Known PGS sidecars take this route directly. Its separate five-minute total deadline includes waiting for extraction headers, independent of mpv's shorter per-file network timeout. Declared and streamed bodies remain bounded to 16 MiB for text/unknown formats and 256 MiB for PGS, without buffering a second bitmap copy. Only a complete, current-owner local file may be added, verified by its actual current-file SID, selected and persisted. A loading notice explains the wait; newer selection/load/profile/page ownership cancels it, and failure retains the applied choice and saved preference. Any busy history suppresses this fallback; a 503 on the fallback itself also reports busy without escaping its retry budget. Deferred picks on a burned-in plan must pass the same readiness check before initiating a burn-in-removal replan.

Busy refusal preserves the last actually applied subtitle and saved preference while the loaded transport owner is still current. Text readiness is checked before a burned-to-text replan; authorized local bytes can cross that one replacement only for the same effective file, stable track identity, sync key, initiating manual owner and API context.

The pinned server can select an alternate file for a track-change refusal. It commits the replacement and can retire the old transcode/proxy grant before returning the response; there is no client-load acknowledgement or general predecessor lease. A server-selected alternate is therefore a new transport owner. Keep its valid grant, resolve the requested identity in its inventory, expose actual captions Off until its text read succeeds, preserve existing preferences on busy failure, and show retry guidance. Do not roll the manager back to an obsolete server revision or claim old burned-transport survival across that server commit. No new server API is introduced.
