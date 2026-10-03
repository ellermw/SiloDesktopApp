# Autoplay controls-script crash

Status: root cause evidenced in installed 1.2.0 logs, reproduced in bundled libmpv, repaired in candidate source. Not installed or released. Broader visual parity remains incomplete.

## Observed failure

The user reported that a successor episode loses mouse-hover controls, fullscreen and Escape while video keeps playing. Installed `SiloPlayer.exe` metadata reports 1.2.0.0 / 1.2.0+3488a4942ee0d03448bd609d04334e74b963bc7d.

The retained `mpv_log.txt.1` gives a concrete Game of Thrones S3:E3 sequence (monotonic seconds within that mpv instance):

```text
3235.149 osc-set-markers: successor content episode-tvdb-121361-3-3, intro 0..113.032
3235.150 silo-seek-absolute: 113.032, automatic intro skip
3235.715 osc-set-intro-mode: always (delayed host preference response)
3240.169 silo_osc: Lua error: silo-osc.lua:3770: table index is nil
3240.170 silo_osc: Destroying client handle...
```

No stream URLs, credentials or subtitle URL payloads are retained here. The installed line3770 is the intro undo expiry write, `state.intro_resolved_keys[state.intro_prompt_key] = true`. Candidate line numbers differ because of other pending work.

## Cause and repair

The reused Lua VM can auto-skip an intro using the previous episode's already valid preference before an asynchronous host settings read finishes. It creates a keyed pending seek, then a five-second Watch Intro undo action. The intro-mode message unconditionally cleared `intro_prompt_key`, even for the same mode, while keeping the pending seek/undo alive. Its completion or expiry then wrote a table entry with a nil key. That uncaught error terminates the entire controls script, including mouse/keyboard bindings, while mpv continues video playback. Further host visibility messages cannot restore a destroyed script.

Repair the state transition itself: unchanged valid preference messages are idempotent and preserve the live action/key. A genuinely changed mode clears the pending seek and undo together with the key and starts a consistent fresh prompt state. Invalid mode values normalize to Ask. No exception swallowing, periodic script restart or playback restart is used as the repair.

## Verification

`MpvOscRuntimeTests.AutoplayIntroPreferenceRefreshPreservesTheLiveControlsScript` replays the logged marker/automatic skip/delayed Always response through real Lua handlers in bundled libmpv, before and after seek completion. Both variants failed before the repair with `table index is nil` (candidate lines3786 and3810), and both pass after it. The assertions also require nonempty rendered controls, mouse visibility, fullscreen command and Escape command.

Four additional runtime cases cover changing to Ask/Never while a seek or undo is active. The combined OSC runtime/script, playback-marker and next-episode regression run passed 73 tests, zero failures. The full Release suite subsequently passed1,349 tests with zero failures, and all six published PlayerService transport/recovery checks passed. An independent scoped review found no actionable state-transition defect. Logs: ignored `.codex-tmp/autoplay-osd-red.log`, `autoplay-osd-green.log`, `autoplay-osd-regressions.log`, `autoplay-published-service19.log`, `parity-unit-oct2-final.log`.

These tests establish this specific script-state failure and repair. Installer and final whole-candidate release verification remain required; no repaired build has been installed or published. They do not claim that every possible future controls or buffering fault is resolved.

Fresh confirmation: `.codex-tmp/autoplay-osd-confirmation-oct2.log` re-ran both failure-timing cases after the later parity edits: two passed, zero failed. Published candidate25 builds successfully with this repair; this remains a local candidate, not an installed or released fix.

## October 3 interim installer

At the user's explicit request for an interim usable build, packaged frozen successful published candidate25 as `D:\SiloPlayer\installer\output\SiloInstaller-1.2.0-OSD-Repair-Setup.exe`. This remains an interim1.2.0 build; final distinct-correction version1.2.N, complete parity release, main push and README public download update are pending. Unfinished newer auth/catalog edits were excluded by using the already compiled payload. No app installation, restart or production change was performed.

Fresh verification: `interim-osd-regressions-oct3.log`73 passed/zero failed, including the actual crash timings and control/fullscreen/Escape assertions. `interim-published-playback-oct3.log`all six actual published PlayerService transport/recovery checks passed. Frozen payload Lua SHA256 E168A582F4B6E1742BF4908ADF525C9F9FF763FFD67FA8737A3268376ED6FB8B matches the tested source; bundled libmpv SHA2565825FFD2BEF6FD6A4B78016D5890C6C9DEDB16A31828A1C749BE89EACF9E20AE also matches. Inno completed successfully; immutable540-file payload manifest and final installer checksum are retained in ignored `.codex-tmp/interim-osd-payload-manifest.json` and `interim-osd-build.json`. This does not claim full parity or resolution of unrelated streaming failures.
