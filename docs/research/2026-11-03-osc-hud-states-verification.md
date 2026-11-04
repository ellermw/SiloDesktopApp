# Active OSC feedback and input ownership

This is the first focused PR of the owner's approved staged native-menu work. It changes the active Lua/ASS HUD, not the dormant XAML overlay. Native audio/subtitle/quality menus remain later deliveries. Renderer policy, startup/playback loading icons and Dashboard are unchanged.

## Behavior

- Shared hover, held press and focus feedback for top chrome, transport and utility controls; hit rectangles remain stable.
- Exit/Minimize own their complete down/up/video-click sequence, emitting one original action without a pause intent.
- Held feedback retires on release, leave, disable, content replacement and menu takeover.
- Both ordinary and pointer-leave auto-hide timers respect focus, held controls and open menus, rearming while owned.
- Audio with no alternative and empty Chapters remain disabled across appearance, navigation and desktop/compact activation. Compact Audio/Chapters anchor to the visible More control.
- Invisible/covered Fit and More controls cannot activate underneath a menu or hidden HUD. Existing active indicators, quality pending text and authority/track-intent routes are retained.

## Verification and provenance

Base Beta: `d37eae35a2e1b6c4f4c26dde6c22713aa12c478f`. No application version stamps changed; source remains `1.2.549-beta.1` / numeric `1.2.548.1`. The pipeline owns the next version and combined release checks.

Actual production Lua runs inside bundled pinned libmpv. Focused tests compare emitted ASS, dispatched intent sequences, menu/focus ownership, disabled actions and both timer callbacks. They do not substitute source-string assertions for those behaviors.

The initial regression run had a fixture-clock error and is not product red evidence. Corrected runs reproduced unchanged top feedback, extra pause intent and disabled-hover mismatch. Review regressions separately reproduced hidden/covered Fit activation, inconsistent keyboard Audio, missing compact anchors and pointer-leave ownership. A zero-track test was corrected to preserve the already-hidden Audio button; it is compatibility coverage, not a newly fixed product defect.

The native preserved baseline compiles the same fixture but fails actual Exit hover/press pixels with `0/0`. Its screenshot confirms the real OSC was present. Native green verification must use the fresh current-source publish, compare actual rendered button pixels over bright/dark synthetic media and retain pause/resume intent. Physical mouse/keyboard/controller/display behavior and full visual parity are outside this offscreen/scripted evidence.

Final verification passes: **3,496 unit tests**, including **104 actual OSC runtime cases**, zero failed/skipped; successful fresh self-contained Release publish; explicit native `osc-hud-states` group with eight gesture configurations and32 real rendered captures (Exit/Minimize ×960/1920 ×dark/bright ×normal/hover/pressed/released). Actual hover/press pixel differences are1489–3147 pixels per button; released states return to hover. Exactly one intended top action is observed, genuine pause is retained and video resumes afterward. Existing compiler/analyzer warnings remain.

Independent Standards and Spec source reviews of tree `f568dcfd7d4fdc5a66b5401ab5e58f17470636d1` report zero remaining actionable findings. Final diagnostic app SHA256 `31966C3F77DD4EA874F46D3D3A5CBB1076FA685A423A44DFD61A1ED660365A09`; shipped OSC SHA256 `3B6AAE08C9EBF130CADCFB2FC8EBFF27BDCBDBEF48204F6368DD2F6B2F54AB3E`. All1035 tracked product/build input hashes remain unchanged through verification; native fixtures operate against the actual fresh payload. The earlier client-final output predates the last pointer-leave correction and is superseded diagnostic evidence, not the accepted candidate.

Final receipt groups: `green-leave-owner`, `full-final-owner`, `client-final-owner`, `native-current`; preserved native red is `native-red-baseline`. Unit tests and publishes preceded the implementation commit and their receipts name the unchanged Beta HEAD plus separately recorded working-source hashes. They do not establish pipeline integration or a published release.

Local evidence: `D:/SiloPlayer/.codex-tmp/osc-menus-nov03/hud-states`; isolated worktree/source and approved design are under that root. Every .NET run verifies engine SHA256 `04E4D27DC385DC18DDD93FBE69C75E635B864F3C6EC53A9122DF54C2F7842F4A` and holds `SiloOct18Build`. Only generated local media/fixture data is executed; no setup, installed-player control or real account access occurred.
