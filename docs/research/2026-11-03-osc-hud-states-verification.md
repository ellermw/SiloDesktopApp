# Active OSC feedback and input ownership

## Current integrated source identity — PR #94 merge

Native Chapters source head: `01bd0534a533f33392ad4e52ac2a701a67049a0a` (reviewed PR #91 input); integrated with Beta PR #90 input `2e359e6055440789dfc7e42b171519e5e9c58ee3`. These are historical input identities, not the final PR #94 merged head.

Source version: `1.2.1015-beta.1`; numeric version: `1.2.1014.1`.

The app, update host, launcher and installer defaults already carry these stamps; this merge changes none. PR #94's native Subtitles and composed OSC menu flows are combined with Beta's owned native Audio, Quality, Watch Party, Chapters, compact More, Playback info and Playback speed controls. These input identities are not a build, test or release receipt for the merged source. No builds or tests were run for this merge; fresh pipeline verification and final commit binding are required. The remaining PR #86 scope, identities, counts and hashes below are historical and do not verify this merge.

## Historical PR #86 scope

This is the first focused PR of the owner's approved staged native-menu work. It changes the active Lua/ASS HUD, not the dormant XAML overlay. Native audio/subtitle/quality menus remain later deliveries. Renderer policy, startup/playback loading icons and Dashboard are unchanged.

## Behavior

- Shared hover, held press and focus feedback for top chrome, transport and utility controls; hit rectangles remain stable.
- Exit/Minimize own their complete down/up/video-click sequence, emitting one original action without a pause intent.
- Held feedback retires on release, leave, disable, content replacement and menu takeover. Accepted Exit/Minimize presses reverse a pending fade and retain the surface until release.
- Both ordinary and pointer-leave auto-hide timers respect recent focus, held controls and open menus. Transport focus expires after the normal input idle timeout; file load and disable retire focus and keyboard menus immediately.
- Compact pointer clicks dispatch the hit-tested control without invoking the buffering prompt's keyboard/controller action.
- Audio with no alternative and empty Chapters remain disabled across appearance, navigation and desktop/compact activation. Compact Audio/Chapters anchor to the visible More control.
- Invisible/covered Fit and More controls cannot activate underneath a menu or hidden HUD. Existing active indicators, quality pending text and authority/track-intent routes are retained.

## Historical source identities — native Audio (PR #87) and Quality (PR #89)

Integrated Audio source head (PR #87): `9f0495bbc2990b45ea0c7b1d6a69c1687c6e6c4e`.

Reviewed Quality source head (PR #89): `5f38c219a0b5f53b32446e0d936631ef6fa99642`.

Source version: `1.2.1015-beta.1`; numeric version: `1.2.1014.1`.

These values record the existing app, updater, launcher and installer-default stamps without changing them. Neither source identity is a verification receipt for the combined merge. No builds or tests were run for this correction or merge; pipeline re-verification must record the final integrated head. The PR #86 identities, reviews and receipts below remain historical and do not verify either head or the combined merge.

## Historical PR #86 source identity, verification and provenance

Base Beta: `aeebcf4bf9e15c61d29931ec730bd412ac1c8665`. Reviewed PR #86 head: `96e6011aa9957717749ec51c88f27ca7b9a427fe`.

Source version: `1.2.634-beta.1`; numeric version: `1.2.633.1`.

The app, updater, launcher and installer-default stamps above changed relative to the original OSC base `d37eae35a2e1b6c4f4c26dde6c22713aa12c478f` (`1.2.549-beta.1` / numeric `1.2.548.1`); the current PR diff against the Beta base above does not change stamps. This QA correction updates the identity records, not the stamps. The historical receipts below cover neither this head nor these stamps and cannot establish verification of its stamped build inputs or these corrections. No builds or tests were run for this correction; the pipeline must verify it. Final pipeline stamping has not been confirmed; a subsequent stamp requires updated identity records and fresh verification, not reuse of the old receipt.

## Historical local verification (not the current head)

Actual production Lua runs inside bundled pinned libmpv. Focused tests compare emitted ASS, dispatched intent sequences, menu/focus ownership, disabled actions and both timer callbacks. They do not substitute source-string assertions for those behaviors.

The initial regression run had a fixture-clock error and is not product red evidence. Corrected runs reproduced unchanged top feedback, extra pause intent and disabled-hover mismatch. Review regressions separately reproduced hidden/covered Fit activation, inconsistent keyboard Audio, missing compact anchors and pointer-leave ownership. A zero-track test was corrected to preserve the already-hidden Audio button; it is compatibility coverage, not a newly fixed product defect.

The native preserved baseline compiles the same fixture but fails actual Exit hover/press pixels with `0/0`. Its screenshot confirms the real OSC was present. Native green verification must use the fresh current-source publish, compare actual rendered button pixels over bright/dark synthetic media and retain pause/resume intent. Physical mouse/keyboard/controller/display behavior and full visual parity are outside this offscreen/scripted evidence.

Historical final verification passed: **3,496 unit tests**, including **104 actual OSC runtime cases**, zero failed/skipped; successful fresh self-contained Release publish; explicit native `osc-hud-states` group with eight gesture configurations and32 real rendered captures (Exit/Minimize ×960/1920 ×dark/bright ×normal/hover/pressed/released). Actual hover/press pixel differences are1489–3147 pixels per button; released states return to hover. Exactly one intended top action is observed, genuine pause is retained and video resumes afterward. Existing compiler/analyzer warnings remain.

Independent Standards and Spec source reviews of tree `f568dcfd7d4fdc5a66b5401ab5e58f17470636d1` report zero remaining actionable findings. Final diagnostic app SHA256 `31966C3F77DD4EA874F46D3D3A5CBB1076FA685A423A44DFD61A1ED660365A09`; shipped OSC SHA256 `3B6AAE08C9EBF130CADCFB2FC8EBFF27BDCBDBEF48204F6368DD2F6B2F54AB3E`. All1035 tracked product/build input hashes remain unchanged through verification; native fixtures operate against the actual fresh payload. The earlier client-final output predates the last pointer-leave correction and is superseded diagnostic evidence, not the accepted candidate.

Final receipt groups: `green-leave-owner`, `full-final-owner`, `client-final-owner`, `native-current`; preserved native red is `native-red-baseline`. Unit tests and publishes preceded the implementation commit and their receipts name the unchanged Beta HEAD plus separately recorded working-source hashes. They do not establish pipeline integration or a published release.

Local evidence: `D:/SiloPlayer/.codex-tmp/osc-menus-nov03/hud-states`; isolated worktree/source and approved design are under that root. Every .NET run verifies engine SHA256 `04E4D27DC385DC18DDD93FBE69C75E635B864F3C6EC53A9122DF54C2F7842F4A` and holds `SiloOct18Build`. Only generated local media/fixture data is executed; no setup, installed-player control or real account access occurred.

## Required pipeline re-verification

- After final stamping, check the semantic/numeric mapping and agreement across the app, updater, launcher, installer defaults and both current source-identity records. `CurrentOscReceiptMatchesAllSourceVersionStamps` guards this source/document agreement automatically; `installer/build.ps1` retains its build-stamp preflight. If the pipeline chooses another version, update the identity records and rerun these checks rather than attributing older evidence to new inputs.
- Record the exact final commit, tracked product/build-input hashes, bundled engine hash and fresh artifact hashes with the test/publish receipt; recheck inputs after verification. Verify published binary, installer and manifest identities against that receipt before release.
- Run the OSC runtime regressions for controller-only pause/resume and idle expiry, zero-delta mouse messages after controller focus/fullscreen activation/subtitle-dialog closure (both host/mpv delivery orders), file/disable focus/menu retirement, compact pointer clicks with both buffering pills, and Exit/Minimize presses during fade-out. Run the native file-replacement focus/auto-hide case and retain the existing gesture/pixel and pause/resume checks. These new cases are pending pipeline execution.
