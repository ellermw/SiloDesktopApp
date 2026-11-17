# Books parity delivery — November 16, 2026

## Baselines and scope

- Desktop Beta: `173c37974894fe5d52a4a4437c356c84e5c897d9`, version `1.2.1165-beta.1`.
- Official Server/WebUI: `17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8`, fetched directly from GitHub main.
- Existing WebUI is the design and behavior specification; preserve native retained navigation and security boundaries.
- Owner authorizes focused commits/PRs to Beta; pipeline owns stamping, merging and packaging. Do not install/restart the installed player.

## Delivery groups

1. Reader opening/rendering: reproduce normal Read failures, repair format routing/bootstrap errors, preserve profile lifetime and script-disabled content; verify extraction/API, packaged engine and native reader navigation.
2. Book details: implement upstream responsive audiobook hero, progress/actions, chapters, narration picker and related rails; preserve ebook/comic behavior; verify presentation rules, native widths and playback interactions.
3. Book browsing: align captions/progress, status labels, group filtering and failed/empty states; preserve windowing/retention; verify query/card rules and native library states.

## Integration and verification

### Source-only checkpoint after independent review

The owner reports Admin released its bounded runtime window at
2026-11-16T16:39:59.8164148Z. Requests reported shared generated output of
10.191 GiB at 16:39:55Z, exceeding the 10 GiB cap. All new runtime/build
launches are on hold pending bounded recovery. Books holds no runtime lease.
No denied cleanup was retried; no generated payload or evidence was removed.

Regression source is prepared, not executed, for startup-versus-runtime script
errors, unresolved saved-location recovery, FB2/FBZ legacy overall-progress
restore, mixed-case comic page order, reader reconnect/access-refresh survival,
comic Next order, canonical volume grouping/full-width sections, and retained
ebook rail resizing. These additions are not red/green evidence. The owner now
directs immediate Beta submission without waiting for a local runtime slot.
Source corrections for those cases accompany the reader/detail PR, with final
execution explicitly left to pipeline QA rather than claimed as locally passing.

Source reconciliation covers reader identity/fresh operation contexts, comic
reading-order normalization, nonfatal restore/bootstrap errors and responsive
ebook rails. Pipeline validation remains required. Still reconcile library
empty/no-match/retry recovery, audiobook series navigation, translations,
locale-aware names, sticky controls, legacy FB2 highlights and the remaining
lower-priority findings before claiming comprehensive parity. Normal detail-page
Read handoff still needs coverage beyond direct reader navigation.

A fresh read-only remote check finds official Server/WebUI main unchanged at
`17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8`; Beta has advanced to
`7ba72bed148a29e737ec708997d8fedc259f7a0d`. Reconcile that Beta source before
final builds or PR submission. Package reader/detail/shared comic ordering as
one cohesive PR and browsing/cards as an independent PR. Each PR must state
the local runtime hold and distinguish earlier worker results from final source.

- One writer per worktree; reconcile additive DTO/native-selector changes explicitly.
- Review final diffs against upstream behavior and repository standards before submission.
- Preserve red/green evidence; distinguish final-source checks from earlier payloads.
- Shared output cap: 10 GiB. Respect `SiloOct18Build`; Admin has priority while Books builds are held. Cleanup was policy-denied: no retry/bypass.
- Native verification requires matching source/product bytes. Synthetic checks do not certify live interoperability or every visual state.
- Submit open, non-draft, same-repository PRs authored by `ellermw` into `beta`; each below 200 files/250 commits. Record heads/dependencies.
