# Finished-player preview after true EOF

Owner report: the installed 1.2.1017-beta.1 showed a large empty dark rectangle beside "You've finished Cops" after the latest available episode ended. This independent increment starts at Beta cbafac58cdd4987323d36031b7881e2f2fc98e54; version remains 1.2.1017-beta.1 / 1.2.1016.1. Other alignment and Continue Watching PRs are separate and pipeline-owned.

## Result

While credits still play, the native video remains live in its measured finishing slot. At true EOF, the native window stays hidden and the slot reveals the completed title's own watch backdrop. It does not use artwork from the next episode. A missing, refused or undecodable image removes the empty rectangle; pending artwork reserves a transparent slot until the request settles. Close, successor playback and identity departure retire stale image results. The loader shares the existing loading-cover cache entry.

This is an intentional native presentation improvement. At official reference 4448e401 the WebUI fades the finished mini-player away, leaving its column empty. Showing the completed title's backdrop addresses the owner's reported empty rectangle without changing playback transport, autoplay or On Deck behavior. The wide no-art layout retains the column position; no layout redesign is included.

Move/restore and resize paths keep the ended native popup hidden in expanded, mini-bar and picture-in-picture states. The independent review identified the missing resize guard for mini-bar/PiP. A native regression reproduced the mini-bar failure, then the lead added the guard and verifies all three states.

## Evidence and limits

All four new native EOF cases failed before implementation at actual libmpv EOF. On the implementation build, four new and nine existing hud-completion cases, hud-completion-scope and hud-nov12 passed; the full unit suite passed 6,108 tests, zero failed/skipped. The final lead verification reruns the four new cases, live post-roll and related source/API guards after the resize correction; all five native runs and 91 related source/API tests passed. Exact results, source and DLL hashes are in D:/SiloPlayer-build/finished-preview-delivery-receipt.json.

Native coverage includes early-overlay-to-EOF, direct EOF with/without next episode, wide/compact layout, no artwork/HTTP500/corrupt image, stale close/content/profile results, and ended resize/restore in expanded/mini-bar/PiP. Backdrops are synthetic colored images over fixture HTTP, not the owner's library or a live server. Real pointer input, shuffle post-roll and physical display DPI are unverified. A hanging fetch can reserve a transparent compact slot until its request settles.

An independent review found no P0/P1; its residual P2 resize finding was corrected by the lead and exercised natively. No second independent review is claimed. Earlier reused-result-directory failures remain recorded: the still fixture reused a previously cached held URL (now unique per run), and hud-nov12 inherited a saved threshold of 20 (passed with fresh fixture state; product unchanged).

No installation, restart or production change was performed. Beta pipeline owns review, fixes, merging, version assignment and packaging. One reusable artifact/publish/native payload was used. Prior automatic cleanup rejection ("blocked by policy") was not retried or bypassed; recovered space remains 0 GiB on C: and D:.