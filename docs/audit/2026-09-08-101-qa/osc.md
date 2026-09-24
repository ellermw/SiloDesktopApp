# Player OSC, shader assets, and native-player tests — full-source supplement

Baseline: installed 1.1.101-multigpu-preview.1 plus current working tree. This supplements backend.md. The entire 5,804-line Lua script, shader source and notices, and five test files were read, including all trained numeric weights. Ten complete files, 8,490 physical lines; exact coverage in osc-coverage.json. Native host mouse routing was additionally inspected at MpvVideoWindow.cs:628–678. No source edits, application interaction, production requests, or test execution were performed by this reviewer. Findings are source-derived, not claims of observed installed-app reproduction.

## Rebuild decision

Keep libmpv, the host-owned playback/navigation commands, the replaceable optional neural shader, timeline offsets for restarted transports, lazy chapter thumbnail requests, layout caching, explicit failure/recovery affordances, keyboard shortcuts, and bounded track/chapter menu windows. These are useful behavior and seams.

Replace the monolithic OSC implementation as an architectural unit. Rendering, pointer hit testing, menus, focus, timelines, artwork overlays, timers, playback actions, and application messages share one mutable Lua state across 5,804 lines. Several concrete defects below result from those ownership boundaries. A rebuild should give one component ownership of each input gesture and each overlay lifetime, derive rendering from one state snapshot, and expose semantic accessible controls through a composition-compatible Windows HUD. If ASS remains necessary for the video composition path, keep a small renderer with a tested state reducer and accessible host representation. This is a recommendation about the control surface, not a reason to replace the decoder or direct-play foundation.

## Findings

### O01 — P1: Selecting a popup item can also pause or resume video

High confidence. D:/SiloPlayer/libs/mpv/scripts/silo-osc.lua:4544, 4561, 4580, 4603, 4620, 5695, 5708; D:/SiloPlayer/src/SiloPlayer/Services/MpvVideoWindow.cs:638, 648, 655, 660.

The native host sends osc-mouse-down, then on a quick release sends osc-mouse-up and osc-video-click. Audio, chapter, subtitle selection/off, and quality items close their menu on mouse-down without consume_video_click(). The later generic video-click checks the current menu flags, which are already false. A selected item above the bar/gradient hit area therefore falls through to cycle pause. Choosing subtitles while playing can pause; choosing a chapter that explicitly resumes can immediately pause again. Closing stats has the same pattern at :4529. The effect depends on the clicked row being outside the bar's broad hit rectangle; lower rows inside it are protected. No asynchronous host reply is necessary for the subtitle-off case.

Make the gesture's handled result survive down/up/click, or process the complete gesture once. Add an executable Lua test sending the exact host sequence for an upper popup row and asserting one intended action and zero pause toggles. Existing text assertions and single-message runtime cases do not establish that invariant.

### O02 — P2: Fade cleanup stops at the first uncreated popup overlay

High confidence. D:/SiloPlayer/libs/mpv/scripts/silo-osc.lua:2418; lazy construction :2905 and :3059.

dismiss_transport_menus_for_fade uses ipairs over a table of nullable overlay handles. Lua ipairs stops at the first nil. Open quality before ever opening subtitles: subtitle_menu_overlay is nil, so the loop never reaches the quality overlay. Visibility flags are cleared, but its already-rendered ASS remains on screen. The periodic tick renders the OSC, not hidden menus, so the stale popup can persist. MpvOscScriptTests.cs:661 checks the flags and function call as source strings and misses the actual overlay cleanup.

Use an explicit registered overlay collection or individually clear each optional handle. Test every popup as the first popup opened, then fade.

### O03 — P1: Center transport and utility controls overlap at normal widths

High confidence in geometry and hit-order consequence; no screenshot reproduction. D:/SiloPlayer/libs/mpv/scripts/silo-osc.lua:1209, 1234, 1254, 1280, 4655.

The transport is centered independently of the expanding right utility rail. At width 1280 and scale 1, with audio, chapters, and next episode available, the next-episode button lies approximately x=736–780, while the volume slider occupies x=760–856 and its hit region extends four pixels further. Their vertical centers match. A click in the overlapping center strip reaches the volume handler first and changes volume instead of advancing. The volume icon overlaps the next button's left portion too. The 640-pixel compact breakpoint only hides volume; it does not solve collisions between the remaining rail and centered controls. This affects an ordinary windowed player, not only an extreme minimum size.

Budget horizontal space for all groups, progressively collapse secondary actions into a menu, and validate non-overlap across widths, scales, and optional-control combinations. Use actual rectangle intersections as tests, plus visual verification.

### O04 — P2: Next-episode countdown survives seeking back out of credits

High confidence in implemented behavior; desired pause semantics should be confirmed against current product behavior. D:/SiloPlayer/libs/mpv/scripts/silo-osc.lua:3755, 3768, 3776, 3786.

The position-versus-credits check runs only when starting the countdown. Once started, seeking to an earlier scene does not cancel it: elapsed wall time still triggers next episode after ten seconds. Pausing also does not freeze the countdown. The former can interrupt someone who seeks back to rewatch the end. Include position/seek state in the active countdown eligibility and treat cancellation/expiry as an explicit state transition. Test enter credits, seek earlier, advance clock beyond ten seconds.

### O05 — P2: Disabling controls does not retire raw thumbnail overlays

Medium confidence on user-visible reachability; clear ownership gap established. D:/SiloPlayer/libs/mpv/scripts/silo-osc.lua:4937, 2473, 1458.

osc-set-visibility=false clears ASS control/menu overlays, but not raw bitmap chapter/hover overlays. tick returns early while disabled, skipping the normal render path that removes hidden thumbnails. A disable transition while a chapter/seek thumbnail is displayed can therefore leave its bitmap visible unless the host separately clears that specific resource. Centralize all ASS and bitmap cleanup in the same visibility/lifetime transition and exercise transitions from thumbnail-visible to mini/post-roll/hidden modes. Parent reviewer owns the host's final reachability assessment.

### O06 — P2: Metadata is inconsistently escaped before ASS rendering

High confidence. D:/SiloPlayer/libs/mpv/scripts/silo-osc.lua:627, 643, 2778, 2796, 2998, 3041, 2348.

draw_text appends text as ASS markup. Titles and notices use ass_escape_text, but several subtitle labels/languages, quality labels, and stats values do not. A media/server label containing ASS override syntax can alter formatting or placement of the HUD. This is rendering injection, not demonstrated arbitrary code execution. Some truncation also uses byte-based string.sub (:2794, :2347, :863), despite an existing UTF-8 helper, and can cut a multibyte character. Escape text at the primitive boundary, reserve an explicit trusted-markup path, and use Unicode-aware truncation everywhere.

### O07 — P2: Quality menu has no overflow/navigation window

High confidence. D:/SiloPlayer/libs/mpv/scripts/silo-osc.lua:2949, 2965, 4799.

Quality-menu height includes every quality tier and version. Only its top is clamped; unlike subtitle/audio/chapter menus it has no scrolling window. Enough editions/tiers in a short window extend past the bottom edge, making rows inaccessible. Wheel handling has no quality branch and can change volume instead. Reuse a shared bounded menu model and scroll/focus behavior.

### O08 — Architectural: Full HUD redraw runs continuously at 30 Hz

High confidence about work frequency; magnitude not profiled. D:/SiloPlayer/libs/mpv/scripts/silo-osc.lua:5753, 2497, 1463.

A permanent 1/30-second timer renders the complete OSC, including ASS geometry, even when its visual state is unchanged. Hidden render sends an empty overlay update repeatedly. Layout caching and 1Hz stats refresh help, but do not remove string construction and overlay churn while the HUD is visible and paused. Use invalidation-driven render, a temporary animation timer, one-second countdown/stats clocks, and no idle overlay updates. Profile CPU allocations and native render cost before assigning a performance severity.

### O09 — Architectural: Drawn controls lack a complete semantic/accessibility model

High confidence. D:/SiloPlayer/libs/mpv/scripts/silo-osc.lua:4313, 5486.

ASS drawing has no UI Automation peers or accessible names by itself. The custom focus list only covers part of the controls; exit, minimize, seek/volume ranges, and floating countdown/watch-party actions are not a complete keyboard traversal surface. Tab cycles forward only. The host's lowercase alphabet mapping does not establish a missing feature: current script registers matching upper/lower variants for its relevant letter shortcuts and has no advertised brackets/comma/period actions. Build a shared action/control model consumed by pointer, keyboard, controller, and accessibility routes rather than adding independent bindings.

### O10 — P2: Right-click captions bypasses the host subtitle selection path

High confidence in route divergence; consequence depends on current transport. D:/SiloPlayer/libs/mpv/scripts/silo-osc.lua:4753.

Normal selection sends silo-subtitle-select to the host, which owns server subtitle inventory and any required transport/burn-in change. Right-click captions instead cycles mpv sub directly. That can choose a different embedded track without updating active_subtitle or the server selection state, and cannot perform the same external/burn-in handling. Route every selection input through the same host-owned action; retain direct native operations only behind that action's policy.

## Shader and notices

D:/SiloPlayer/libs/mpv/shaders/FSRCNNX_x2_8-0-4-1.glsl contains 426 physical lines, the LGPL notice, all convolution weights, intermediate feature/mapping/sub-band passes, and final 2x luma subpixel aggregation (:413–426). All passes are conditional on both output/source dimensions exceeding 1.3. It is a trained portable luma shader, not an AMD driver upscaler or FSR. The reviewed local SHA256 is E800DBC5C1C95185CC82216C597724533FF5F2880179F256EEF600F03E8DC2AE, matching the bundled README. No source-level resource/security defect was identified. This is structural/source review, not mathematical validation of trained weights, image-quality benchmarking, or GPU timing.

Keep this optional capability, its upstream version/hash record, complete editable shader source, and both license notice files. The 17-line README documents provenance, replacement and scope accurately relative to the source. COPYING (674 lines) and COPYING.LESSER (165) were read in full. Their presence is verified; this report does not purport to establish legal compliance of the complete release. Packaging and native GPU eligibility/fallback are covered by the parent reviewer.

## Test assessment and explicit file coverage

| File | Lines | Review and rebuild decision |
| --- | ---: | --- |
| libs/mpv/scripts/silo-osc.lua | 5804 | Entire file read; O01–O10. Replace the monolithic control surface; preserve behavior through explicit actions, state, renderer and input boundaries. |
| libs/mpv/shaders/FSRCNNX_x2_8-0-4-1.glsl | 426 | Entire source and numeric weights read; keep optional external asset and provenance. |
| libs/mpv/shaders/README.md | 17 | Entire file read; keep version/hash/license/use documentation. |
| libs/mpv/shaders/COPYING | 674 | Entire notice read; keep. |
| libs/mpv/shaders/COPYING.LESSER | 165 | Entire notice read; keep. |
| tests/SiloPlayer.Tests/MpvOscRuntimeTests.cs | 254 | Entire file read; useful isolated Lua execution harness; broaden behavioral state/gesture/lifetime cases rather than text-only assertions. |
| tests/SiloPlayer.Tests/MpvOscScriptTests.cs | 696 | Entire file read; extensive source substring contracts can pass broken behavior (notably O02). Keep a small architectural subset, replace behavior assertions with runtime state/command/overlay checks. |
| tests/SiloPlayer.Tests/MpvPlayerLoggingTests.cs | 28 | Entire file read; keep focused logging/redaction contract, expand URL shape coverage only for demonstrated gaps. |
| tests/SiloPlayer.Tests/MpvPlayerSourceTests.cs | 325 | Entire file read; assertions guard important native implementation choices but cannot validate actual event ordering, HWND lifecycle, GPU paths, or recovery. Keep minimal configuration contracts; add executable lifecycle tests where seams permit. |
| tests/SiloPlayer.Tests/VideoUpscalingTests.cs | 101 | Entire file read; useful pure eligibility/profile tests; no real GPU/image quality guarantee. Keep and add a small driver/multi-GPU playback acceptance matrix outside unit tests. |

No unreadable files in this supplement. Inspection of relevant native mouse-routing lines is cross-reference coverage, not a second claim to full-read MpvVideoWindow.cs; parent owns full native source review. Core and its 74 directly matching tests are listed separately in backend-coverage.json.
