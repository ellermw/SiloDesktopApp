# Task 2 desktop HUD completion

Worktree: `C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-705ff871-c3b1ae077e8125f0`

Branch: `t3/complete-desktop-hud-task2`

Parent: `9c18719416322d0a47a9c63840ab4af5dfd9203d`

Official reference: `Silo-Server/silo-server@0b579aa6192d49d7e8e0921dde5e955ac29ce302` (supplied read-only checkout).

## Fresh Beta integration overlap

Owner's drift update: official main `5327ba36f3b8830c200e7bba0b710d5b09cc4f9b` changes only the Vite JSON.parse build optimization (#4031); runtime/UI reference remains the pinned `0b579aa6`. Desktop Beta is now `47d5da976cc23797272b779b5519fb20315c4f92` / `1.2.1003-beta.1`. This task deliberately retains its assigned parent and older stamp; the lead will integrate fresh Beta.

Read-only inspection of local commits `10b3685b` (#138) and `b4d99bd8` (#141) identifies these integration seams:

- Preserve `PlayerService.BusyOwnership` transport pause intent and revision checks. Current viewer controls during a framed continuing reload must remain available. Task2 Next messages carry a playback-request owner in addition to content/native-load ownership, rather than adding a blanket busy-state blackout.
- Preserve fresh `NativeMenus`, `NativeChapters`, `NativeParty`, `NativeQuality` ownership, session-ID comparisons and busy-opened disabled rows/traversal. Task2 changes the shared choice menu's compact Speed presentation and HUD publication, not these newer menu policies.
- Preserve fresh `MpvPlayer`/`MpvPlayer.DirectStream` native-output ownership and property reads. This task makes no player-engine wrapper edits.
- Merge `silo-osc.lua` by behavior: retain Beta `transport_reloading`, transparent spinner, `transport_reload_paused`, reload-pause messages and control/focus availability alongside Task2 loading cover, wait labels, rail, segments, credits and owned Next intents. Opaque first load remains distinct from continuing reload.
- Preserve updater staging (#141) and its PlayerService deferral seam. No updater changes are part of this task; the final integration must rerun the new Beta transport-reload and staging checks against merged bytes. This baseline's native receipts do not certify that future merge.

This completes the applicable desktop implementation on top of partial Task2 candidate `6cb34a30` (original worker `efce4e600539d082d32037e8aeeba0f1466aa86d`) already in the parent. Version remains `1.2.998-beta.1`. Shared plan/current-development documents, installed player, real settings/accounts, production, publishing and remotes were left untouched. No delegates were used. Review here is self-review; independent integration review remains the parent's responsibility.

Evidence root below, abbreviated **E**: `D:/SiloPlayer/.codex-tmp/server-align-nov12/task-2-completion`.

## Implementation and observable verification

| Required change | Completed behavior and verification |
|---|---|
| Desktop Playing Next | Wide composition places the existing live owned video HWND in the measured left slot beside the next column. It preserves the playing media and next/shuffle actions. Compact desktop stacks the finishing preview above the scrollable next column. Tests resize the actual shell, compare native bounds, require position to advance without pausing, bring Play Now into view clear of the HWND, and verify EOF removes the video. |
| Chapter segments and lamp rail | Actual rounded segments cut background, buffered/marker/played layers; lamp fill, glow and thumb follow the readable accent. The original continuous seek rectangle still handles gaps, hover and drag. Production-Lua runtime tests exercise those inputs and segment geometry. |
| Contrast adjustment | Core and production Lua port upstream `accentColor.ts` luminance, white-mix loop, 4.5 contrast checks and label choice. Dark and mid-grey cases exercise the actual OSC and native Speed rendering. |
| Compact native speed picker | Compact horizontal preset tiles retain the existing owned native window, gate/lease, keyboard traversal, current/custom value, pending save, retry/error and room metadata. Native controls test settings-to-speed handoff, persisted custom rate, serialization, room join/leave, disabled selection and owner retirement. A rendered-pixel regression also protects live current-tile refresh. |
| Credits episode still | A separate owner key covers decode/cache/thumbnail completion. Spoiler preferences are loaded before exposure, unwatched stills are blurred, preference changes retire visible pixels, and cancellation/identity checks drop late results. Decode/crop and rounded BGRA output are bounded to 224x126 for the 112x63 card slot. Raw episode title is retained separately from the formatted navigation title. |
| Specific wait labels | Existing network outage/recovery signals drive Reconnecting; Watch Party catch-up drives Catching up with precedence. Ordinary cache waits stay unlabeled. Real production-Lua cases cover solo reconnect, party reconnect/catch-up, clearing and normal buffering. |
| OSC loading image fade | The host blends into the OSC cover for 500ms with one reusable scratch buffer, two alternating raw files and revision-scoped serialized composition. Real host/OSC native screenshots and pixels prove intermediate/final image output. First output and retirement clear retained full-size buffers. |
| Watch backdrop size | Actual service watch preparation passes the current display width as `backdrop_width`; the API preserves scope/file query parameters. Exact-URL native expectations were updated. Integrated HTTP fixture requires the query on actual watch requests. |

The earlier autoplay policy, threshold UI, finish-time clock/template, thumbhash cover and transport rail remain in the parent. Completion tests exercise them alongside these changes rather than substituting isolated policy tests for service wiring.

## Integrated unattended autoplay

`PlaybackHudCompletionNativeFixture` constructs the real MainWindow, PlayerService, PlaybackManager, fixture HTTP plans, native libmpv media and published production Lua. The credits fixture accelerates the countdown clock only after Lua has published its current content/load owner. EOF uses actual media completion and MainWindow's real countdown timer. It covers manual starts, automatic starts, default/off/threshold gate/reset, credits and EOF boundaries, viewer pause versus internal pause, failed playback preparation, shuffle start/advance/failure, cancellation-ignoring retirement and Watch Party suppression.

Those tests exposed and fixed three stale-advance paths: retired ended timers, queued service Next callbacks, and late Lua intents from an outgoing content/native load (including an older load of the same content). Production Next intents now include content, native-load and playback-request ownership, and the host checks ownership again before dispatch. `delivery-reload-next` additionally proves that a viewer can leave a continuing reload whose new native load is pending, while an outgoing intent cannot override a newer playback preparation. The idle stale-load regression remains separate and still rejects an older same-content load.

The reload-availability case injects busy/pending-load state into the real service, then uses normal intent dispatch and actual HTTP successor playback. It does not simulate the whole newer Beta transport-recovery implementation; that merge requires Beta's own actual-reload native fixtures as noted above.

## Localization seams for Task 5

No Task4 foundation or invented translations were added. `PlayerService.HudText(key, fallback)` is an explicit installable seam for `web.player.connection.reconnecting`, `web.player.room.catchingUp` and `web.player.speed.heading`, with English fallbacks. `EndTimeLabelTemplate` retains its `{time}` formatting seam from the partial task. Other existing native post-roll/menu text remains English for the separately scoped Task5 call-site pass; raw media titles are never translated or persisted as translated labels.

## Visual comparison and platform scope

Inspected actual native captures against the pinned `PlayingNextScreen.tsx`, `NextEpisodeOverlay.tsx`, `PlaybackSpeedPicker.tsx`, `VideoPlayer.tsx`, `player-hud.css` and `accentColor.ts`. Wide post-roll follows left margin 48, video width min(44vw,832), adjacent column maximum 544, artwork maximum min(30vh,300), lamp heading and light primary CTA. Compact post-roll preserves a reachable next column below the live preview. Credits retain the 384-wide card, 112x63 still slot and timeline clearance; lamp segments and central ring wait states use the existing OSC geometry.

Screenshots include wide/compact post-roll, loading midpoint/final, credits still and wait labels, compact Speed current/custom/room/error states and black/mid-grey accents. Synthetic media/artwork deliberately use simple color fields. Native HWND bounds, playback advancement and actual image pixels supplement those captures; a XAML bitmap alone cannot establish live-video continuity.

Genuine differences: WinUI/ASS typography and focus rendering, native caption/owned popup behavior, a compact desktop scroll layout and approximate ASS lamp glow. Phone-only portrait docks/sheets, physical DPI certification, live-server persistence/accounts and real Watch Party interoperability are outside this synthetic Windows verification. These limits do not remove any of the eight desktop implementation requirements.

## Retained red and intermediate evidence

Behavioral red receipts were recorded before their corresponding production fixes: `red-behavior.log`, `red-lua.log`, `red-still.log`, `red-native-accent.log`, `red-solo-outage.log`, `red-intent-owner.log`, and native directories `red-native-postroll`, `red-native-speed`, `red-native-timer*`, `red-native-credits`, `red-native-wire`, `red-postroll-cap`, `red-native-metadata`. Their companion build logs remain in E.

Original failures and investigation remain intact:

- `native-postroll-a/b` failed during synchronous WinUI/native layout resize. Removing LayoutUpdated alone did not fix it. The final implementation coalesces/defer live layout and HWND resize outside Window.SizeChanged, keeps row definitions stable, and passes the normal resize route.
- Early `native-cover-b` proved timing/buffer bounds but its screenshots lacked loaded production Lua. The corrected route explicitly loads the published script, requires the cover overlay, and checks rendered midpoint/final pixels. Only the corrected route is rendered-cover evidence.
- `verified-autoplay` accelerated the countdown before the current Lua owner arrived, correctly causing stale-intent rejection. Final fixture waits for that owner before advancing its clock.
- One speed-fixture edit briefly damaged the multiplication sign through text encoding; its original source was restored and the intended edit reapplied. Final captures/metadata preserve the proper symbol.
- `identity.log` initially expected separate Core/Player DLLs in the app publish. This app embeds those sources; final identity checks use the app plus actual unit Core/Player assemblies.
- A live Speed screenshot revealed stale WinUI template fill after a rate change despite correct control Background properties. The new rendered regression preserves this red and verifies the visible current/previous tiles after correction.
- `red-rendered-speed` fails with current tile brightness 30 and previous tile 128. The fix renders current-rate fill and label in the tile content, retaining the native item container/focus owner; `complete-speed` passes actual pixels for black and mid-grey accents. Earlier property-only checks are not claimed as sufficient rendered proof.
- `native-build-complete.log` failed because reference staging omitted `Updates/UpdateOnExit.ps1`. The missing published Updates directory was copied to reference staging; `native-build-complete-staged.log` succeeds. No production updater change was needed.
- `red-request-owner.log` fails the production-Lua expectation for playback-request ownership; `red-reload-next` fails because the early blanket busy guard blocks the viewer. The replacement retains current viewer control availability and rejects newer-preparation races. The final fixture waits for all three Lua owner fields before accelerating its countdown.
- The intermediate `complete-*` replay reached nine passing groups before the ownership follow-up changed source Lua. Its next invocation was correctly rejected with `Published Lua differs from source`; it is not counted as final verification. Final `final-*` runs use the later fresh publish and matching helper.
- The first pending `delivery-*` batch used one-second mutex retries and lost queue position during other Beta verification. Its owned waiting process tree was canceled (`delivery-native-wait-canceled.json`); no running installed player was controlled. `RunSerializedNative.ps1` instead takes one blocking native-batch turn and directly calls the same verified harness on the same managed thread. Inner mutex acquisition is reentrant; no separately acquiring process is nested. Per-run pin/identity/sentinel checks and receipts remain in place.
- The first serialized batch passed Speed and metadata, then refused to overwrite the historical `final-postroll` evidence directory. That directory remains intact; the final post-roll receipt is `final-postroll-completed`. Resumption validates and reuses the two already completed current-app receipts before running the remaining cases.

## Existing verification limits

The complete default native suite is **not** claimed green. Focused final routes cover applicable Task2 behavior; unrelated historical failures are retained:

| Route | Observed limit / preserved baseline |
|---|---|
| Default Audio | `OSC resize did not settle` at PiP width 480 versus framed client dimensions; same result in `accepted-native-audio-menu` and frozen `baseline-native-audio-menu`. |
| Chapters | `Ready thumbnail did not decode from existing cache`; same result in `accepted-native-chapters-menu` and frozen `baseline-native-chapters-menu`. Task2 chapter rail/hover/hit behavior has separate production-Lua coverage. |
| Alternate-file Audio | Replacement URL/remapped index assertion; same result in `accepted-audio-alternate-file` and frozen `baseline-audio-alternate-file`. |
| Full Speed | Existing same-owner version replacement pause/settings-read assertion in `native-speed-c`, also documented in the unchanged current-development history. The complete Task2-specific native presentation route verifies compact controls, pending/retry/room/ownership and rendered contrast. |
| Default parallel units | `full-unit-owned-final.log`: 6006 passed, one unrelated global-message-bus collision in RealtimeReadOwnershipTests (watchlist case captured another fixture's WatchedMarked event). Original log/TRX retained. Serialized full-suite outcome recorded below. |
| Additional parallel units | `full-unit-delivery.log`: 6006 passed, one updater VerifyOnly/ValidateExitGate failure reporting that another Silo process could not be checked. It launched no installer and changed no installed player. Earlier `full-unit-complete.log` and serialized run passed all 6007. Original failed log/TRX retained; final source verification is recorded below. |

## Final source and artifacts

Final verification, source manifest, PE/PDB binding, hashes and local commit are recorded in **E/completion-receipt.json**.

Fresh self-contained x64 Release app: **E/publish-delivery**, built by `publish-delivery.log`; native helper: `native-build-delivery.log`. Both use the supplied verified-dotnet wrapper. Native execution uses the task's `Verify.ps1`, checking source/published Lua, app/helper byte identity, pinned engine, explicit completion sentinel and `SiloOct18Build`. No installer was run.

| Final artifact | SHA256 |
|---|---|
| SiloPlayer.dll | `21C9B2ED8E5F9891452519EE62D7CFCC9D870BC25472CBFB4A958FBBF02608DE` |
| Native fixture DLL | `D4B9796E1E2951413FE9D7B04FC2A39855E1F28A62854A7002F19187BA45F620` |
| Production silo-osc.lua | `8C0174B77F778801C7F7F3BFD5BED3DE016F6F4FC845EF0C45699552354FC2AF` |
| Bundled libmpv | `04E4D27DC385DC18DDD93FBE69C75E635B864F3C6EC53A9122DF54C2F7842F4A` |
| Source manifest | `4D8095D12591891CD67A924CE4B1B5602EAC11830799A1F97322477F9FEC0B02` |

Final source verification completed:

- **6007/6007 full unit tests passed**, zero failures/skips, serialized collections; `full-unit-release.log` and `unit-results/full-unit-release.trx`, duration 7m23s. The two earlier parallel-run failures above remain preserved. The final ownership iteration also passed all **378 focused production-Lua/runtime tests** in `focused-reload-owner.log` before this full run.
- **16/16 final native groups passed** with exit 0 and the explicit completion sentinel: `final-speed`, `final-metadata`, `final-postroll-completed`, `final-autoplay`, `final-stale-timer`, `final-stale-credits`, `final-stale-wire`, `final-cover`, `final-still`, `final-hud-nov12`, `final-hud-completion-scope`, `final-native-quality-menu`, `final-native-subtitle-menu`, `final-osc-hud-states`, `final-audio-keyboard-click`, `final-reload-next`. See `final-native-summary.json` and each directory's `receipt.json` / `results.txt`.
- **PE/PDB and authored-source identity passed**, zero mismatches: app 782 documents, Core 379, Player 15, native fixture 199, unit fixture 444; `identity-release.json` / `.log`. App embeds Core/Player sources; their separate assemblies here are the actual unit references.
- **1429 compiled-input manifest files match current source**, including XAML, Lua and project inputs. The aggregate receipt rechecks every hash, every final native app hash/sentinel, engine pin, published/source Lua equality, final unit counters, source/PDB identity, diff whitespace and the shared-doc/version exclusions.
- Fresh final wide/compact post-roll, selected Speed tile and credits-still captures were inspected again. Final cover midpoint/final pixels and rail/wait/menu states are exercised by the final native runs. No full-default-native-suite or future fresh-Beta-merge certification is implied; the explicit baseline/platform limits above remain.

Native receipts correctly record the pre-commit parent HEAD plus dirty source. The completion receipt binds their verified manifest and byte hashes to the resulting local commit; committing the report changes no compiled inputs. Self-review found no remaining Task2 implementation work. The lead still owns independent review and integration with fresh Beta, including the overlap checks listed above.
