# Native Audio menu — verification

Status: verified local Beta candidate. The foreground investigation below separates the explicit UIA activation request from ordinary menu behavior; UIA command routing is tested under fixture-only activation isolation.

The first native-menu stage connects the active Lua OSC to a menu-sized owned WinUI Audio window. It retains the native video renderer and sends the original zero-based audio source index through the existing replan service. Startup/playback loading icons and Dashboard are outside this change.

Product commits: `dcc1c693454e20287cf5766318a8995636d514f4` and pointer/fixture correction `7fd34c897053d53245076476d8d3c622c0590afd`; Beta base: `d37eae35a2e1b6c4f4c26dde6c22713aa12c478f`. Official server/WebUI reference, fetched directly: `bb1ee4352b1bec38b4a6441ba81e61a92da81d54`, especially `web/src/player/components/AudioTrackMenu.tsx`. Subsequent verification-document commits do not change these product inputs. Source remains stamped `1.2.549-beta.1` / numeric `1.2.548.1`; the Beta pipeline owns release stamps and publication.

## Behavior

- Pointer, compact-menu and controller Audio entry points request one scoped native presenter. The legacy menu remains available without the host capability.
- Rows retain source order and show title, language/technical metadata, codec/channel/default badges, and the current-track check. Native scrolling, keyboard/controller navigation, full accessible labels and tooltips preserve access to long labels.
- A token and increasing request sequence bind open/input/close messages. Opening transfers Lua pointer/focus/press ownership; pending input cannot reach the underlying transport. Video activation/down/up dismissal consumes its gesture before mpv routing.
- The presenter owns one shared dialog-gate lease and scoped native hook. Owner loss, hide, replacement and teardown close it once. Deactivation does not restore foreground; explicit cancel/selection can restore only the still-current owner.
- Selection validates the captured playback/API/inventory before acceptance and preserves operation ownership across asynchronous work. Actual file generations are distinct from watchdog cancellation and presentation lifetime: an accepted selection survives menu retirement, handles its own native load failure, and rejects retired completion/error publication.

## Branch verification

Evidence root: `D:/SiloPlayer/.codex-tmp/osc-menus-nov03/native-audio`.

| Check | Accepted evidence |
| --- | --- |
| Full unit suite | `full-final-eight`: **3,490 passed**, zero failed/skipped, including **82 actual mpv/Lua cases** |
| Native handoff Lua cases | 18 cases, including pointer/controller ownership transfer and idle/leave/ack ownership; `lua-hud-transfer-red` fails both rendered-state comparisons before correction, `lua-hud-transfer-green` passes its original 17; `pointer-all-paths-green` passes all 82 Lua cases after the captured-button correction |
| Fresh Release publish | `client-final-eight`; app DLL SHA256 `2021697F48E4D1844777715E0675392F33A06FF6DE38F18777E44FCD96490783` |
| Product fingerprints | 1,040 inputs unchanged through verification; `inputs-final-eight.json` |
| Frozen payload | 574 files, 264,538,615 bytes; `native-eight-isolated-uia/seal-after.json` records each length/hash |
| Actual native scenarios | `native-eight-isolated-uia`: all four cases pass with the completion sentinel |
| Fixture identity | `native-fixture-summary.json` and per-run compiler-PDB bindings; final fixture SHA256 `3AC7018A6F6DF3D03574C13B139BF7A155FADA70C24185F983C5EB7674E1F8D0` |

All .NET work held `SiloOct18Build`, after verifying pinned libmpv SHA256 `04E4D27DC385DC18DDD93FBE69C75E635B864F3C6EC53A9122DF54C2F7842F4A`. Runs use fresh isolated outputs and short D: temporary paths. Existing compiler/analyzer warnings are retained in logs.

The native selector `native-audio-menu` executes four scenarios with fresh synthetic services and owned video windows:

1. Actual OSC → PlayerService → native rows → UIA source-index 71 → real synthetic replan, with only the explicit UIA activation request isolated inside the test process. It checks 72-row scrolling, current metadata, 900×540/460×700/1920×1080 geometry, stale scope/file/profile callbacks, competing dialog ownership, native pointer dismissal, once-only close, subscription cleanup, genuine pause/playing intent and foreground ownership. The final own-process WinEvent hook and HWND/PID checkpoints observe zero fixture-owned activations; only synchronous UIA Invoke has an exact-menu activation veto. Other interactions have no veto. Actual narrow/long-label captures were inspected.
2. A held HTTP failure after profile replacement cannot publish the old error/notice, load another file or publish a new capability. The observation covers response completion, a bounded 500 ms continuation window and an actual Lua round trip.
3. A held valid replan continues after the exact presentation-retirement helper, clears loading/busy and retains playing intent. This exercises the helper used by mode changes; it does not claim a physical fullscreen/PiP transition.
4. A controlled, positively observed real libmpv duplicate-reader registration error inside the accepted load clears busy/loading and shows a current error. Original and attempted callbacks remain rooted/restored through native disposal. This injected fault is not a claim about a production failure cause.

Meaningful preserved reds include missing native presentation on the prior app (`red-native-host`), actual UIA `ClickedItem` being the row Content Grid (`native-uia-payload`), retired-profile error publication (`red-retired-replan`), presentation retirement stranding accepted work (`red-retired-presentation`), and an owned load exception being mistaken for retirement (`red-own-load-ranges`). Invalid fixture syntax, deep-path/build errors, an origin that initially ignored Range requests and an asynchronous Play-readiness assertion are recorded separately; they are not product defects. Fixture-only changes retain original/final source hashes and scoped reruns.

The fresh publish receipt records build HEAD `dcc1c693` plus its working-tree Lua correction; the sealed product inputs match final product commit `7fd34c89`. Fixture-only commit `d582c4e169912ee94bb5d027f7704eabf2b08dbc` preserves the same product inputs; their exact compiler-bound source hash is recorded above. The four captured-pointer entry guards are in loose Lua asset SHA256 `A3A5E2A7B966A0AE4ED9C9595ABEE8A3DEF409447264EC04046BA305DEE26407`; `pointer-all-paths-red` fails both actual rendering/ownership cases before correction.

## Foreground verification boundary

An earlier terminal foreground-HWND equality failure (`native-final-eight`) remains unclassified because it recorded no HWND/PID values. Its replacement observer initially rechecked current HWND ownership, which could discard a queued event after a menu closed. That false-negative was corrected and its original/final compiler-bound source retained.

The strict process-filtered hook then caught an Audio-window event with external endpoint snapshots. Paired timing probes establish its path: UIA-on `foreground-uia-timing` records activation inside Invoke; the same selection through keyboard Input in `foreground-keyboard-timing` records no such event. The actual Window.Activated(CodeActivated) and event timestamp align with Invoke, before ItemClick. Microsoft's [WinUI implementation](https://github.com/microsoft/microsoft-ui-xaml/blob/d338d4ec689167845af1ff75f9c148ab1aa3a850/dxaml/xcp/dxaml/lib/ListViewBase_Partial_Interaction.cpp) focuses the item before firing its click. The pinned public source is explanatory; the paired experiment uses the actual installed SDK binary. Full source/version caveats remain in the local `uia-primary-source.md`.

A controlled `foreground-uia-veto` experiment positively blocks one exact-menu activation while the real UIA ItemClick and source71 replan still succeed. The permanent fixture therefore isolates only that explicit synchronous Invoke with an own-UI-thread CBT hook targeting the exact menu HWND and current process. It unhooks before backend awaits, leaves other windows/interactions untouched, and retains the strict raw foreground observer. All four final scenarios pass with zero owned foreground events/checkpoints. It allows zero veto attempts if a future SDK removes the activation request.

This verifies UIA command routing **under test isolation**, not natural UIA foreground behavior. No production activation hook was added. The raw failed runs, paired controls, positive veto, source/PDB bindings and untouched app hashes remain preserved. The isolation prevents test automation from activating an offscreen fixture on the owner's desktop.

The existing `oct20-playback` group also passes on the same final app (`existing-room-loads`): actual BeginMpvLoad/FileLoaded room pause authority, standalone autoplay, and native quality/SiloCast selection checks.

## Standards

Independent final source review: **0 remaining findings** on tree `0d230f31ece78136b5f46b5ec9ed45a1255eaf88`. Prior ownership findings were reproduced and corrected. A scoped follow-up on tree `8647a82f1172f35ead0419b463e633965040caa4` reviews the final four pointer-entry guards with zero findings. The review confirms the stable file-generation handoff, preserved existing callers and scoped Lua restoration; it makes no runtime certification beyond the recorded evidence.

## Spec

Independent final source review: **0 remaining findings** on the full tree and scoped `8647a82f` pointer follow-up. No missing requirement or scope creep remains within this Audio stage. Subtitle, quality, chapter and speed menu stages remain separate work under the approved design.

Physical input/controller hardware, audible audio decoding, foreground behavior on the owner's machine, real monitor/DPI transitions, complete modal-dialog handoff and full visual parity require their own observed evidence. The fixture uses generated silent video, synthetic API data and owned offscreen HWND messages. It never starts setup or controls the installed player. Generation checks do not serialize concurrent native load commands.

## HUD compatibility

A separate D: diagnostic checkout merges exact Audio product commit `dcc1c693` with PR86 head `7211fb64b9c3b8228704c1054a8f444bc479ca6b` at `ff08d381e94cee33e1f2082871bff2e7855834e8`. Local companions `c84479ec` and `391190c1` carry the exact timer and pointer-entry corrections; later companions change only the foreground fixture. The original merged suite passes **3,529 tests**; the final changed Lua scope passes **122 actual runtime cases**. Do not infer a full 3,530-test rerun from those separate receipts.

The combined native checks cover four Audio scenarios and eight HUD configurations/32 captures. Only the loose Lua asset changed after the fresh combined publish. A fresh derived layout preserves all 573 other payload files and all 1,039 other product inputs, with DLL SHA256 `9C9A9E214D711A1021ADDBA317D3D0E4A6011163971CC0A5F0A15E6852D1B6FC`; final combined Lua SHA256 `935C3864FDD24961F61A8A79E3E2A875F69CD81067AB46516DD89C57122182F3`. This is a diagnostic composition with unchanged compiled C#; old outputs remain frozen. The final foreground fixture is reverified separately on these same bytes: local companion `c55070c57f26cdcf5d7f4a4effceadc0126b03b4`, run `native-audio-isolated-uia-final`, all four cases passing with strict monitoring and the documented synchronous UIA isolation. Exact merge resolutions, fixture bindings and derivation receipts: `D:/SiloPlayer/.codex-tmp/osc-menus-nov03/audio-combined/report.md`.

PR86 remains pipeline-owned and has two separate unresolved review threads: [Fit pointer focus](https://github.com/ellermw/SiloDesktopApp/pull/86#discussion_r4452963076) and [top-chrome action timing](https://github.com/ellermw/SiloDesktopApp/pull/86#discussion_r4452963092). These scoped composition checks do not resolve those findings or clear PR86's integration gate. Audio is independently based on Beta and does not include PR86's commits.

## Concurrent target refresh

At final submission refresh, Beta advanced from the tested branch base `d37eae35` to `a6144b4aacea7472603dfa0a1fd0ad9493eaaf8a` through merged PR73 (synced collection sort direction). Its product files do not overlap this Audio change. The only shared paths are this development handoff and the native Program selector: the pipeline's normal Beta refresh must retain both `synced-sort-direction` and `native-audio-menu` entries and both handoff sections. This PR submits the exact reviewed Audio source; no claim is made that its local checks tested the later collection merge. The authorized Beta pipeline owns refreshed-source review, merged verification, version stamping and delivery.