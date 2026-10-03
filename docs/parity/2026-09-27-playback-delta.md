# Playback, Watch Party and reader delta audit — September 27, 2026

Source-only comparison of desktop **3488a4942ee0d03448bd609d04334e74b963bc7d** (v1.1.105) with official public Silo Server GitHub `main` **5e49cc8d376d2aaa1a7b4601ff876895f8d82efe**, freshly fetched by the parent audit. Upstream checkout: `D:\SiloPlayer\.codex-tmp\silo-server-audit-20260927-5e49cc8d`. The previous September 24 reference was `d4e35ba9df416e747822c6c9f2193b89c6b7e9fb`; this pass inspected relevant changes between those revisions and re-traced the outstanding findings in `2026-09-24-playback-verification.md`.

`desktop:` paths are relative to `D:\SiloPlayer`; `upstream:` paths are relative to that exact checkout. All line numbers below refer to current audited source. No app control, live API, production access, credentials, builds or runtime tests were used. Only this report was written. Source-confirmed gaps are not reproduced runtime incidents or certification of visual parity.

## New upstream changes with concrete desktop gaps

### PB-N1 — P2: Opt into original SRT sidecars after validating native rendering

Upstream #1358 adds `subrip_sidecar_v1` specifically for native `/api/v2` clients: `upstream:internal/playback/protocol_v3.go:72`, `:154`. It changes external/downloaded SRT delivery to original SRT, preserving features such as placement; embedded SRT retains its existing path (`upstream:internal/playback/subtitle_inventory_v3.go:238`, `:244`). The representation is attempt-sticky (`upstream:internal/playback/protocol_v3.go:1397`). A bare `.srt` extension is not sufficient; the server builds the negotiated original URL (`upstream:internal/playback/subtitle_inventory_v3.go:27`, `:165`).

Desktop sends only `playback_plan_v3` and `plan_invalidated_v1` (`desktop:src/SiloPlayer.Core/Services/PlaybackManager.cs:117`; the API forwards this list at `desktop:src/SiloPlayer.Core/Api/PlaybackApi.V2.cs:66`). Therefore it continues receiving the default converted representation despite using a native subtitle engine. The relevant external-track path already passes the advertised URL to mpv without a hardcoded VTT parser (`desktop:src/SiloPlayer/Services/PlayerService.cs:4677`); inventory URL mapping is at `desktop:src/SiloPlayer.Core/Services/PlaybackManager.cs:291`.

**Scope:** This is missing capability negotiation, not absent subtitle support or the already repaired provider gate. Keep old-server fallback and embedded delivery intact. Verify actual SubRip placement/styling in bundled mpv, then negotiate the token at attempt start and retain the server-returned URLs across replan/seek. Check external and downloaded SRT, embedded SRT, disabled subtitles, old servers, and seek/recovery within the same attempt. Do not rewrite signed URLs locally.

### PB-N2 — P2: Observe the room's playing-to-lobby transition

Upstream #1489 now automatically finishes a room when its clock reaches the file end (`upstream:internal/watchtogether/item_end.go:105`; `upstream:internal/watchtogether/runtime.go:354`). WebUI observes a room that was playing leaving that phase, flushes progress and exits playback, with a finished-versus-host-stopped notice (`upstream:web/src/player/components/VideoPlayer.tsx:1202`, `:1216`, `:1223`, `:1234`). It ignores an initial lobby snapshot.

Desktop `OnRoomChanged` only marks a new playing selection for startup (`desktop:src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs:193`, `:203`). Its coordinator refreshes the overlay, starts new selections and reports waiting readiness, with no playing-to-lobby close branch (`desktop:src/SiloPlayer/Services/WatchTogetherCoordinator.cs:133`, `:140`, `:156`). The room page responds to snapshots by updating the room UI (`desktop:src/SiloPlayer/Views/WatchTogetherRoomPage.xaml.cs:113`), while the player overlay clears only for null/ended rooms (`desktop:src/SiloPlayer/Services/PlayerService.cs:982`). Natural series end still enters the ordinary post-roll path (`desktop:src/SiloPlayer/Services/PlayerService.cs:3696`, `:3714`).

**Impact:** The new server completion snapshot does not itself return a desktop viewer to the room lobby. A trailing player or series post-roll can remain open independently of room completion. This also exposes a pre-existing missing response to host-stop snapshots; the automatic end trigger is new. Do not claim every end hangs: normal standalone natural-end cleanup still exists (`desktop:src/SiloPlayer/Services/PlayerService.cs:3726`).

**Acceptance:** Two real clients, host stop and natural movie/episode end, a slightly trailing guest, paused end, and an initial lobby snapshot. Preserve room membership, flush final progress, close only the matching room playback, and prevent independent next-episode autoplay after the room returns to its lobby.

### PB-N3 — P3: First-frame telemetry is not wired

Upstream #1379 measures viewer intent through the first visible frame, once per playback attempt. Automatic starts/reloads without a viewer mark still report the event but omit the duration (`upstream:web/src/player/first-frame.ts:1`, `:27`, `:36`; `upstream:web/src/player/hooks/usePlaybackSession.ts:1338`). The server consumes `diagnostics.first_frame_ms` (`upstream:internal/playback/route_event_metrics.go:34`).

Desktop emits `plan_selected` after start/replan (`desktop:src/SiloPlayer.Core/Services/PlaybackManager.cs:162`, `:552`); its route-event API always creates empty diagnostics (`desktop:src/SiloPlayer.Core/Api/PlaybackApi.V2.cs:178`, `:193`). Searching desktop `src` and `libs` found no first-frame event wiring. Existing local diagnostics are not equivalent to server press-to-frame telemetry.

**Acceptance:** Carry a monotonic viewer-intent timestamp through preparation; report once only after native evidence of a presented frame, associated with the adopted attempt. Test paused starts, replacement attempts, replans and automatic next episode. Telemetry failure must never delay playback. This is observability, not evidence of a startup-speed regression.

### PB-N4 — P3: Hide the quality selector when it has no choice

Upstream #1408 renders the quality/version control only for more than one quality or an allowed alternative version; version-only menus are labeled Version (`upstream:web/src/player/components/QualityMenu.tsx:96`, `:100`, `:118`). Desktop always allocates its quality button (`desktop:libs/mpv/scripts/silo-osc.lua:1280`) and renders the tiers, including a singleton list (`:3000`, `:3006`, `:3087`).

**Acceptance:** Compare one quality/one version, multiple qualities, version-only, and Watch Party version-lock cases, including keyboard focus. Preserve native diagnostics and valid native quality choices. This is a small source-confirmed interaction/layout delta, not a broad player visual-parity claim.

## Carried findings re-confirmed in current source

### PB-C1 — P2: Watch Party transport intents and reconnect reattachment

`rg -n 'RequestTransport\(' src` still returns only the coordinator wrapper (`desktop:src/SiloPlayer/Services/WatchTogetherCoordinator.cs:126`, `:128`) and ViewModel definition (`desktop:src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs:376`). Lua pause changes mpv directly (`desktop:libs/mpv/scripts/silo-osc.lua:4498`, `:4753`), with local relative-seek messages at `:417`. WebUI validates room permission and requests scheduled transport (`upstream:web/src/player/components/VideoPlayer.tsx:1063`, `:2752`, `:2762`). Host state reports can still be adopted by the server (`upstream:internal/watchtogether/service.go:834` onward); this does not mean all desktop host actions fail. The missing explicit path chiefly affects permission enforcement, guest requests and scheduled coordination.

Desktop reconnect sets connected and starts receiving (`desktop:src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs:572`, `:574`). Coordinator connection changes only update the overlay (`desktop:src/SiloPlayer/Services/WatchTogetherCoordinator.cs:135`), and same-session notification returns early (`:107`). WebUI reattaches on connected transitions (`upstream:web/src/player/hooks/useWatchTogetherPlaybackSync.ts:361`, `:368`). Reconnect exists; active-session reattachment is the gap. Verify permitted/denied host/guest controls, one request per intent, no feedback loop, and socket recovery with the same session ID.

### PB-C2 — P2: Room readiness, buffering grace and catch-up policies

Desktop immediately relays buffering and recovery (`desktop:src/SiloPlayer/Services/WatchTogetherCoordinator.cs:334`, `:344`), declares ready from duration/cache status (`:156`) and applies corrections as ordinary seeks (`:279`). WebUI retains a two-second stall grace (`upstream:web/src/player/hooks/useWatchTogetherPlaybackSync.ts:64`, `:619`), target-position readiness (`:414`), rate convergence and bounded correction reloads (`upstream:web/src/player/utils/roomSyncCatchup.ts:13`, `:87`, `:105`). It offers a lower-quality step after repeated sustained stalls (`upstream:web/src/player/components/VideoPlayer.tsx:253`, `:3215`, `:3231`) and delays reconnect notices (`:264`, `:809`). These policies remain absent from the reachable native room coordinator. Manual quality changes and native recovery are implemented.

New #1345 plays through a rebuilt progressive stream's short pre-roll while a room is waiting (`upstream:web/src/player/hooks/useWatchTogetherPlaybackSync.ts:65`, `:231`, `:401`). Desktop has neither that special path nor an equivalent position-aware ready check. **Do not blindly port the browser's muted 4x algorithm:** mpv's seek/offset behavior is different. Test a remux keyframe before the requested room target and demonstrate correct native landing/readiness before deciding which adaptation is required. Preserve user mute/rate/pause choices. Brief stalls, sustained stalls, ignored slow guests and repeated corrections also need two-client checks.

### PB-C3 — P2: Profile seek intervals and three-way intro behavior

Upstream four video/audiobook preference keys remain at `upstream:web/src/lib/seekIntervals.ts:6`; effective reads and profile writes/reset are at `upstream:web/src/hooks/queries/seekPreferences.ts:32`, `:51`, `:60`. Desktop video skips remain fixed -10/+30 (`desktop:libs/mpv/scripts/silo-osc.lua:4496`, `:4500`, `:4759`, `:4771`), while audiobook skips are local settings (`desktop:src/SiloPlayer.Core/Models/ServerConfig.cs:42`; `desktop:src/SiloPlayer/Controls/AudiobookNowListening.xaml.cs:237`). Local audiobook configurability already exists.

Intro is still driven by legacy booleans in the player (`desktop:src/SiloPlayer/Services/PlayerService.cs:31`, `:5173`) and immediate Lua auto-skip (`desktop:libs/mpv/scripts/silo-osc.lua:3718`). The registered settings key at `desktop:src/SiloPlayer.Core/Api/SettingsV2Values.cs:37` alone does not wire behavior. WebUI `never` suppresses the prompt, `ask` uses a five-second prompt, and `always` supplies an undo action after an accepted skip (`upstream:web/src/player/hooks/useIntroSkipPrompt.ts:5`, `:261`, `:282`, `:350`). This is the same carried S2 finding in the settings audit, not a second implementation package.

Verify four distinct interval values across profile changes and mini/expanded/keyboard controls, save/reset propagation, and Never/Ask/Always with pause, buffering, rejected room seeks and undo. Keep the already working transport-aware marker skip and native playback advantages.

### PB-C4 — P2: Shared EPUB/annotation location semantics and PDF progress

Desktop still saves chapter-index/fraction strings (`desktop:src/SiloPlayer/Views/EbookReaderPage.xaml.cs:510`, `:609`), restores numeric progress by equal chapter weighting (`:248`, `:462`), and defaults an unrecognized bookmark location to chapter zero (`:619`). WebUI uses renderer CFI or whole-book fraction (`upstream:web/src/reader/FoliateBookReader.tsx:233`, `:244`, `:258`; `upstream:web/src/pages/EbookReader.tsx:403`, `:425`). Cross-client navigation can therefore land at different text; no new reader implementation arrived in this upstream delta.

PDF remains one Document chapter (`desktop:src/SiloPlayer/Services/EbookPackageExtractor.cs:24`) opened directly in WebView2 (`desktop:src/SiloPlayer/Views/EbookReaderPage.xaml.cs:345`); its progress sampler returns for non-HTML (`:450`). Upstream PDF uses the common renderer (`upstream:web/src/reader/readest/libs/document.ts:398`). Rendering PDF does not establish shared page progress. Verify unequal-length EPUB chapters, CFI and fraction annotations both directions, and multi-page PDF progress/bookmark/reopen across clients. Preserve/migrate existing desktop locations rather than losing them.

## Server/browser changes that do not establish missing native implementation

| Upstream change | Native assessment and exact source |
|---|---|
| #1519 native Dolby Vision failure → HDR10 strip | Server planner now tries the validated source-preserving HDR10 strip after native DV remux candidates fail (`upstream:internal/playback/plan_v3.go:573`, `:579`). Desktop already recovers through protocol-v3 failure replan (`desktop:src/SiloPlayer/Services/PlayerService.cs:2354`, `:2364`) and includes attempted plan keys (`desktop:src/SiloPlayer.Core/Services/PlaybackManager.cs:518`, `:528`). No new wire capability is introduced by this fix. Runtime-test the eligible Profile 8.1/native-DV-failure case; do not add browser codec restrictions or claim every DV/output combination is validated. Bundled native profile deliberately advertises DV 5/8 and omits unverified dual-layer 7 (`desktop:src/SiloPlayer.Core/Services/MpvNativePlaybackCapabilities.cs:65`, `:84`). |
| #1435 subtitle variant across formats on edition swaps | Server subtitle matching/deliverability changes, including `upstream:internal/playback/subtitle_policy_v3.go:175`; not proof of a missing desktop matching algorithm. Desktop adopts returned selected tracks/inventory (`desktop:src/SiloPlayer.Core/Services/PlaybackManager.cs:278`, `:291`). Test an edition swap with equivalent subtitles encoded differently, including forced/HI variants. |
| #1499 cancellation during transcode startup | Server waits now observe request cancellation (`upstream:internal/playback/transcode_startup.go:126`). No native implementation requirement established. Desktop already passes cancellation to replans (`desktop:src/SiloPlayer.Core/Services/PlaybackManager.cs:539`). |
| #1486 room correction/sync logging | Server observability (`upstream:internal/watchtogether/service.go:665`, `:920`, `:2274`); no new client contract. Useful future incident evidence, not a local playback fix. |
| #1509 fullscreen across episode transitions | Browser fullscreen-root retention (`upstream:web/src/player/components/VideoPlayer.tsx:2499`; `upstream:web/src/player/context/PlayerFullscreenContext.ts:1`). Desktop owns a native fullscreen state and post-roll restore flag (`desktop:src/SiloPlayer/Services/PlayerService.cs:62`, `:1437`). Test the native transition; no DOM fullscreen port is needed. |
| Audiobook mini-bar content clearance | Browser observes its bar height (`upstream:web/src/pages/audiobooks/player/MiniBar.tsx:34`). Native mini/expanded listening controls exist (`desktop:src/SiloPlayer/Controls/AudiobookNowListening.xaml.cs:93`, `:234`). Check native bottom-content clearance and window scaling; no new audio protocol or missing audiobook feature established. |

Local theme music and series play-target changes are covered by the browse audit, including their playback entry points; they are not duplicated here.

## Closed prior source findings and remaining runtime boundary

- Prior PB-4 provider-disabled search is repaired: provider status controls visibility/enabled state and the actual search handler is gated (`desktop:src/SiloPlayer/Controls/SubtitleSearchDialog.xaml.cs:98`, `:102`, `:380`). Do not reopen it as missing. Runtime enabled/disabled/error and upload behavior still merits visual acceptance.
- Prior PB-7 Playback Info collision is repaired in source: rows calculate width and stack/wrap long pairs (`desktop:libs/mpv/scripts/silo-osc.lua:2323`, `:2325`, `:2397`). No current overlap was observed in this source-only pass.
- Standalone buffering remains separate. Parent-supplied September 27 00:29 local logs show 61 failed signed direct requests returning 404 after prior successful traffic. This pass neither proves stale desktop URLs nor proves server unavailability, and none of these parity changes fixes or closes that incident.
- Native HEVC/HDR/Dolby Vision, lossless audio/passthrough, long high-bitrate streams, remux/HLS seek, SRT placement, two-client room recovery, real reader files and side-by-side scaled UI remain runtime checks. Retain native codec/direct-play advantages; source inspection does not certify them or visual parity.

Recommended order: finish the Watch Party transport/reconnect/lifecycle package (PB-C1/C2/N2), then native original-SRT negotiation (PB-N1), profile playback settings (PB-C3), reader interoperability (PB-C4), first-frame observability (PB-N3) and selector polish (PB-N4). Diagnose standalone 404/buffering independently and ahead of treating any parity work as its remedy.
