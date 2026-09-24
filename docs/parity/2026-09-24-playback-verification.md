# Playback, Watch Party and reader verification — September 24, 2026 UTC

Bounded, audit-only source comparison of the current desktop working tree against official public Silo Server GitHub `main` at **d4e35ba9df416e747822c6c9f2193b89c6b7e9fb**. The root audit fetched that revision and provided the read-only checkout `.codex-tmp/silo-server-audit-d4e35ba9`. References below use `desktop:` for this repository and `upstream:` for paths relative to that checkout. Line numbers refer to the audited files, not an older release.

No implementation changes, production requests, player input, builds or tests were performed by this pass. Source-confirmed means the reachable implementation differs; it does not claim a reproduced live incident. The root audit owns installed-app observations. The already corrected local 1.1.103 watched-state work is not reopened. Recurring buffering remains an incident investigation; synthetic recovery tests do not prove its root cause or resolution.

## Prioritized choices

### PB-1 — Watch Party local controls and reconnect attachment need integration (P2, source-confirmed)

**Evidence:** Desktop `libs/mpv/scripts/silo-osc.lua:4448` and `:4703` toggle mpv pause directly; `:415` sends relative seeks to the host. `src/SiloPlayer/Services/PlayerService.cs:5406` and `:5419` execute those seeks locally. Repository-wide caller search finds the outbound `RequestTransport` defined at `src/SiloPlayer/Services/WatchTogetherCoordinator.cs:126` forwarding to `src/SiloPlayer/ViewModels/WatchTogetherViewModels.cs:376`, but no UI/player caller. The coordinator subscribes to session/buffering/content/party-management events at `:51`, not local transport intents.

The exact check `rg -n 'RequestTransport\(' src/SiloPlayer` returned only those two definitions and the forwarding call at `WatchTogetherCoordinator.cs:128`. `src/SiloPlayer.Player/MpvPlayer.cs:320`–`:328` disables built-in OSC and loads this custom script, establishing the native entry point.

Upstream `web/src/player/components/VideoPlayer.tsx:1041` checks host seeking permission, `:1052` sends a room seek request, and `:2710`–`:2725` validates guest transport permission and requests scheduled play/pause. The server handles `transport_request` separately from `state_report` at `internal/api/handlers/watch_together.go:925` and `:936`.

**Important limit:** Upstream `internal/watchtogether/service.go:856` can adopt a host's state report as authority. Therefore this is not proof that every desktop host control fails. The confirmed difference is bypassing the explicit permission/scheduling path: allowed guest pause/resume cannot request a room action, while host actions depend on later polling/reconciliation.

Reconnect also lacks active-session reattachment. Desktop `WatchTogetherViewModels.cs:572` connects, sets `connected` at `:574`, then receives frames; snapshot handling at `:662` only assigns `Room`. The coordinator's connection property handler at `WatchTogetherCoordinator.cs:135` only updates the overlay; `NotifyPlaybackStarted` at `:107` rejects the same session ID. Upstream `web/src/player/hooks/useWatchTogetherPlaybackSync.ts:161`–`:166` sends `attach_session` on every connected transition. Desktop has a real reconnect loop (`WatchTogetherViewModels.cs:526`); reconnect itself is not absent.

**Impact:** Guest controls can change only the local player and be corrected back; socket recovery can leave a continuing playback session outside the intended attachment handshake. The exact server recovery behavior needs actual clients.

**Acceptance:** Use two clients and both host-only/guest-play-pause policies. Confirm each permitted action produces one room request and a scheduled command, denied guest seeks do not locally move, and no feedback loop occurs. Disconnect the room socket while retaining playback, reconnect with the same session, verify attachment acknowledgement, continued room state and subsequent host/guest controls. Repeat a scheduled action while leaving the room to ensure stale commands remain rejected.

### PB-2 — Current room stall/catch-up/quality policies are not present (P2, source-confirmed policy gap; runtime effect unproven)

Desktop `WatchTogetherCoordinator.cs:334` immediately reports every mpv buffering transition and `:344` immediately reports ready. `:279` applies every incoming seek through `PlayerService.SeekTo`, without correction-specific rate convergence or room reload budgeting. `:156` marks a waiting client ready from duration/cache state. Its polling at `:325` sends position/paused state. The mpv buffering event is forwarded directly at `PlayerService.cs:3824`; Lua's 500 ms spinner debounce at `libs/mpv/scripts/silo-osc.lua:5135` is visual only and does not debounce the room report.

Upstream `web/src/player/hooks/useWatchTogetherPlaybackSync.ts:55` and `:377` delay a sustained-stall report by two seconds; `:83` tracks catching up even when the room did not wait for this viewer. `web/src/player/utils/roomSyncCatchup.ts:14`–`:19`, `:73` and `:113`–`:118` define a two-second correction band, rate convergence, and correction-driven reload backoff/load-time lead. These are WebUI policies, not a requirement to reproduce browser transport internals in mpv.

Upstream `web/src/player/components/VideoPlayer.tsx:248` and `:3178` offer one lower-quality step after two sustained stalls in five minutes, once per quality, preserving the shared room source. Desktop has manual quality switching (`PlayerService.cs:5500` onward) and native recovery; no room stall history/offer exists in the reachable coordinator/player path. Upstream `VideoPlayer.tsx:765`–`:801` delays persistent reconnect warnings by two seconds; desktop only supplies connection state (`PlayerService.cs:993`) and renders the status (`libs/mpv/scripts/silo-osc.lua:1957`). Do not describe reconnect display as wholly absent.

**Impact:** Brief native cache pauses can enter room waiting sooner; repeated room corrections may produce disruptive local seeks instead of gentle convergence. Users experiencing repeated room stalls have no contextual lower-quality offer. These source differences do not establish the cause of the user's recurring standalone buffering.

**Acceptance:** With two actual clients, induce sub-two-second and sustained stalls, one continuous stall versus repeated recoveries, and a viewer ignored by the room waiting policy. Verify no repeated readiness/seek loop, bounded correction loading, convergence without unnecessary reloads, one eligible quality offer per quality and no source replacement. Confirm routine reconnects remain unobtrusive and longer outages expose controls' availability. Preserve direct play unless the user accepts an available lower-quality option.

### PB-3 — Profile-backed seek intervals remain missing (P2, confirmed existing gap)

Upstream `web/src/lib/seekIntervals.ts:6` names four profile setting keys and derives choices from the settings contract. `web/src/hooks/queries/seekPreferences.ts:32` reads capabilities/effective settings, `:51` saves in profile scope and `:60` clears overrides. Desktop video buttons remain -10/+30 (`libs/mpv/scripts/silo-osc.lua:4446`, `:4450`, `:4709`, `:4721`) and keyboard arrows remain fixed (`:5470`). Audiobook settings remain local (`src/SiloPlayer.Core/Models/ServerConfig.cs:42`; `src/SiloPlayer/Controls/AudiobookNowListening.xaml.cs:199`, `:221`, `:236`). Search across desktop `src` and `libs` found no canonical video/audiobook seek-setting keys.

**Impact:** Profile changes made in the WebUI do not affect desktop skips; desktop audiobook changes do not follow the profile to another client. Configurable local audiobook skips already exist.

**Acceptance:** Set four distinct supported values in a profile, verify settings, video buttons/controller/keyboard and mini/expanded audiobook controls all consume them; save/reset from desktop and observe another client; switch profiles with different values; validate unsupported/unreachable server fallback without silently importing local preferences.

### PB-4 — Provider-disabled online subtitle search is still reachable (P2, source-confirmed)

The native player dispatches `silo-subtitle-search` (`libs/mpv/scripts/silo-osc.lua:4233`; `src/SiloPlayer/Services/PlayerService.cs:5363`) to `ShowSubtitleSearchDialogAsync` (`:5753`, `:5766`). Detail always adds its search entry (`src/SiloPlayer/Views/ItemDetailPage.xaml.cs:2888`) and opens the same dialog (`:5295`). `src/SiloPlayer/Controls/SubtitleSearchDialog.xaml.cs:47`–`:82` has no provider query or availability parameter, and `:343`–`:358` always permits search. In contrast, the separate older `PlayerOverlay.xaml.cs:1347`/`:1459` path does check status; endpoint existence and that older branch do not cover the reachable Lua/detail paths.

Upstream `web/src/pages/ItemDetail/components/SubtitleSearchDialog.tsx:87`–`:89` hides online search only on explicit `enabled:false`; `:256` gates the search section. `web/src/player/components/SubtitleMenu.tsx:96`–`:111` queries the status and fails open on error, passing availability into the dialog at `:392`.

**Impact:** Disabled providers still present a nonfunctional online search path in active desktop playback and detail. Upload is a separate available capability and should remain accessible.

**Acceptance:** Exercise detail and active mpv player with enabled, explicitly disabled and failed status responses. Disabled hides only online search/results; upload remains available. Status failure fails open. Reopen/switch server without retaining the previous server's disabled state; verify keyboard focus lands on a visible input. Existing upload/download refresh and playback-state restoration must continue working (`PlayerService.cs:5787`–`:5800`).

### PB-5 — Shared ebook positions/annotations use incompatible location semantics (P2, source-confirmed)

Desktop `src/SiloPlayer/Views/EbookReaderPage.xaml.cs:507` saves `chapter:<index>;fraction:<within chapter>`; `:246`–`:248` restores by dividing numeric progress equally among extracted chapters, ignoring the saved location. Bookmarks use the same chapter format (`:603`–`:610`), and navigation only parses that format, defaulting unrecognized locations to chapter zero (`:615`–`:622`). Highlights associate with desktop chapter locations (`:366`, `:1027`).

Upstream `web/src/reader/FoliateBookReader.tsx:233`–`:245` derives progress from renderer locations and writes CFI or `fraction:<whole book>`. Its parser at `:258` recognizes whole-book fractions or passes other strings as renderer locations, and restore at `:827`–`:836` uses them directly. `web/src/pages/EbookReader.tsx:403` creates CFI/fraction bookmarks and `:423` navigates parsed locations.

**Impact:** A WebUI fraction bookmark is treated as a fraction of desktop chapter zero; CFI bookmarks also default to chapter zero. Desktop chapter strings are not the WebUI's CFI/href/fraction contract. Cross-client progress can resume at different text because desktop chapter counts and WebUI rendered location weighting differ. Native rendering itself is not the gap; shared state must retain a compatible location meaning.

**Acceptance:** Use an EPUB with very unequal chapter lengths. Create bookmarks/highlights and progress on each client and reopen/navigate on the other, including a font/flow change. Verify the same text, no accidental chapter-zero jumps, and migration of existing desktop locations. Test same-content file-version changes separately.

### PB-6 — PDF page progress is not connected to shared reader state (P2, source-confirmed)

Desktop `src/SiloPlayer/Services/EbookPackageExtractor.cs:20`–`:24` produces one PDF `Document` chapter; `EbookReaderPage.xaml.cs:345` navigates WebView2 directly to it. Its appearance/selection/restore bridge runs only for HTML (`:353`), and progress sampling immediately returns for non-HTML (`:448`–`:450`). Overall progress is computed from the single chapter index/fraction (`:462`), and writes occur at `:503`. There is no PDF page-position bridge in this page. The PDF viewer can still render and provide its own controls; that does not update the Silo progress value.

Upstream loads PDFs through the common reader document implementation (`web/src/reader/readest/libs/document.ts:398`) and tracks/restores renderer locations with the common path (`web/src/reader/FoliateBookReader.tsx:233`, `:827`).

**Impact:** Reading to another PDF page does not update desktop shared progress or a page-specific bookmark/restore location. Existing numeric progress may remain unchanged rather than representing the displayed page.

**Acceptance:** Open a multi-page PDF, go to a middle page using normal PDF controls, wait for progress save, leave/reopen and switch to the WebUI. Verify page and progress, bookmarks, final-page completion and keyboard next/previous. Do not mark this complete from a PDF rendering screenshot alone.

### PB-7 — Playback Info text collision (P3, installed visual observation supplied by root)

Root observed the installed `1.1.102-api-v2.2` player during this audit without interaction: Enhancement status label and value collided. Current `libs/mpv/scripts/silo-osc.lua:2352` draws an unbounded left label; `:2357` truncates the right value by 38 characters instead of available column width. This is a native panel layout defect, not a request to remove native enhancement diagnostics or a claimed WebUI-only feature. Root retains the screenshot evidence; this subtask did not independently capture it.

**Acceptance:** Render long enhancement status values at the observed window size and supported DPI/scales. Verify label/value never overlap and full status remains discoverable. Keep readable diagnostics for native GPU features.

## Coverage and confirmed matches

| Surface | Source coverage / matches | Remaining evidence |
|---|---|---|
| Native playback, tracks, quality, recovery | Reachable mpv host/script controls inspected. Manual quality switching, subtitle selection, separate pause/buffering events and native recovery exist. Direct play/HEVC/HDR/lossless support is an intentional native advantage, not missing browser parity. | Hardware HDR/Dolby Vision/audio passthrough, sustained high-bitrate playback and real buffering recurrence. No live proof in this pass. |
| Watch Party | Room registration (`Views/WatchTogetherRoomPage.xaml.cs:69`), inbound scheduled commands/session guards (`WatchTogetherCoordinator.cs:224`), 1.5 s reports, buffering relay and reconnect loop exist. | PB-1/PB-2 plus actual host/guest clients and scaled overlay layout. Source cannot certify room synchronization. |
| Video seeks/settings | Fixed video controls and local audio skip settings confirmed. | PB-3. Other settings/intro-skip-mode comparison is owned by the settings audit; not duplicated here. |
| Subtitles | Search/upload/download dialog exists; focus is set on open (`SubtitleSearchDialog.xaml.cs:77`), cancellation on close (`:78`), upload/download selection refresh and playback snapshot restoration exist (`PlayerService.cs:5753`–`:5800`). | PB-4 and actual dialog focus/scale/track rendering. SRT placement conversion exists upstream in `internal/playback/subtitles.go:408`; server conversion must not be counted as a new desktop implementation requirement. Native ASS/VTT/SRT placement needs representative media. |
| Audiobooks | Chapter controls (`Controls/AudiobookNowListening.xaml.cs:109`, `:164`), speed (`:149`), sleep/end-of-chapter (`:174`), local skips (`:212`) and canonical progress sync (`Services/PlayerService.cs:1323`) exist. Upstream counterparts include `web/src/pages/audiobooks/player/useAudiobookPlayback.ts:240`, `:707`, `:1038`. | PB-3 plus real multi-file chapter transitions, resume, speed-adjusted sleep behavior and mini/expanded visual/input comparison. No broad missing-audiobook claim. |
| EPUB/readers | Reader navigation, preferences/reset, search, annotations, speech, reading ruler and progress APIs are implemented (`EbookReaderPage.xaml.cs:603`, `:773`, `:829`, `:847`, `:880`; `Core/Api/EbooksApi.cs:12`). | PB-5/PB-6, real format files and WebView keyboard focus/layout. Existence does not prove equivalent reader behavior. |
| Kindle formats | Desktop lists Kindle formats (`Core/Services/EbookReaderFormat.cs:7`) and can extract converted EPUB. Official server explicitly converts Kindle for read delivery (`internal/api/handlers/ebook_reader.go:99`; `internal/api/handlers/ebook_file_service.go:28`; `internal/apiv2/ebook_file.go:45`). | Do not call Kindle unsupported merely because desktop lacks a MOBI parser. Test conversion-success and raw fallback separately; WebUI has a raw MOBI reader (`web/src/reader/readest/libs/document.ts:401`). |
| Manga/comics | Archive image extraction (`Services/EbookPackageExtractor.cs:181`), next manga chapter action (`EbookReaderPage.xaml.cs:943`) and format-specific chrome exist. | Real CBZ/CBR ordering, end-page progress, RTL/flow, next chapter and cross-client resume. No claim of absent manga support. |
| Playback Info | Root observed one concrete installed collision, matching current draw code. | PB-7; no claim that all player visual parity was verified. |

## Verification boundary

This pass used targeted file reads and repository searches to trace entry points and counterpart policies. No full suite or synthetic playback scenario was rerun. The findings above are choices for a later authorized implementation pass, with explicit acceptance criteria. They do not establish the deployed server revision, replace the root installed-app evidence, or close the ongoing standalone buffering investigation.
