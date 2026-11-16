# Credits Up next: autoplay and pause

Reference: official Silo Server main `abd85d0cadac28f4ab7db6821dad9634fc114790`,
including #4278 `87d6425`. Original Beta baseline `2c33599f`; task now
includes pipeline merge `12e510bd` of Beta `aa6b8b86`. Version remains
1.2.1145-beta.1 / numeric 1.2.1144.1 pending pipeline stamping.

## Current verified review follow-up

The reconnect and HTTP503 fallback regressions actually
failed on the earlier source-bound native payload (evidence under
`.codex-tmp/native-results/credits-review-red-9719-retry1/`). Source now uses a
settings identity owned by account/profile/device rather than the event request
context. Genuine access revisions still retire responses and re-arm the read.
The existing three-attempt/five-second-per-attempt budget is retained, and
exhausted optional errors use WebUI's enabled default. Retired account, profile,
device, authority, file or playback owners cannot publish. No poll or setting
write is added by the read path.

The latest bounded green chain ran 06:49:17.4418184Z–06:51:48.1016177Z on
November 16 UTC. Nonincremental unit/app/host builds, actual xUnit source-root
proof, PDB document paths/checksums, publish/reference and byte binding passed.
All five native cases completed with 19 PASS records and zero FAIL:
credits-autoplay, credits-autoplay-reconnect, credits-autoplay-failure, autoplay
and postroll. Product SHA256 is
`F979769FD851C41F9ED83583A88CD06987539375145EE809B5828EA6AC8B9A32`.

Focused units stopped at 726 passed / one failed; full units were not reached.
The failing new captured-profile/device test returned a synthetic `settings`
array where the current contract requires `items`. That fixture JSON is now
corrected, without changing product or native-fixture source. Six additional
ownership cases passed in this run. The fixture-only correction was then verified in the assigned final unit-only
lease (06:58:27.6808250Z–07:01:37.7719166Z): nonincremental build, PDB/source
proofs, all 727 focused tests and all 7,239 full-suite tests passed, zero
failures/skips. Exact runtime/native-source guards passed before and after;
no app/publish/reference/native output was rewritten in this second turn.
Independent final source review found no blocking findings. Final unit receipt:
`.codex-tmp/credits-final-units-20261116T0658-receipt.json`.
Evidence: `.codex-tmp/credits-review-green-20261116T0650-stopped-receipt.json`,
per-stage logs and matching native-results directory. Earlier stopped/passing
receipts retain their exact historical source scope.

The credits card counts only while the effective preference is resolved/enabled
and playback is running. Unknown/off shows the localized heading and manual
Play now with no numeric timer or stripe. Pause and setting changes preserve
eligible elapsed time. Seeking out closes the card; reentry starts ten seconds
unless cancelled. Still-watching and content/load/playback guards remain.

## Historical verification (original candidate only)

The receipts below predate the current review follow-up and do not certify it.

- All three new production-Lua runtime regressions failed against the original source: unknown autoplay consumed time, pause consumed time, and seeking out left the countdown open. All pass after the fix.
- Fresh full main unit suite: 7,180 passed, zero failed/skipped. The later explicit localized-heading assertion was covered by the final 443 passing OSC/localization tests.
- Final Release publish and matching native host build passed. Native `hud-completion` cases `credits-autoplay`, `autoplay` and `postroll` all passed with their explicit completion sentinels.
- The added cancellation assertion failed on the earlier app bytes (`FC768AB3...`) before the final bounded-read correction. The final native check verifies actual effective-settings HTTP, unresolved/off suppression, cancellation, stale response rejection, paused suppression and exactly one enabled successor plan.
- PR review identified that the real Settings writer did not emit the preference notification. The added native regression changes the actual `SettingsViewModel.AutoPlayNext` property, verifies its PUT/device-override DELETE, and fails on the prior writer because the OSC retains true. The corrected writer uses a captured profile context and notifies only after override cleanup; both disable and re-enable reach active playback. It also retires feedback/cleanup on a profile change.
- Final published/reference/host app DLL SHA256: `018293C7D8633F04D4EEAE352DCBA1645DFC688CDD83331DB8A5666FCAF0EEE0`. Native fixture DLL: `37D43E45038F7F011C77AF506E58FCCBDE304E3CA42D5842B716202559CD428C`. The repository-pinned engine and source/published Lua hashes match. The superseded `2AEE3E1F...` candidate retains its original logs/receipt and narrower save-path coverage.

Evidence is under this task worktree's `.codex-tmp`: original `credits-{red,green,full}.log`, `credits-cancellation-red.log` and `credits-final-*`; review correction `credits-settings-save-{red,final}-*`, `credits-settings-save-final-receipt.json` and `native-results/credits-settings-save-final/`. Final Release publish, matching host, 443 OSC/localization tests and all three native groups passed again after the writer correction. The preceding full 7,180-test run retains its earlier scope. Receipts bind the changed source/runtime/test hashes to the executed bytes; the reusable publish directory now holds only the latest candidate.

All verification after the owner's coordination update held `SiloOct18Build`. Task-generated reusable binaries are about 1 GiB; no shared Configure payload was replaced or cleaned. Tests use synthetic HTTP and offscreen native windows, not a live account or physical remote/phone. Existing post-roll captures and six-size runtime geometry assertions cover adjacent layouts; complete visual parity, Narrator and physical-device interoperability are not claimed. The installed player/settings were untouched; no installer, direct merge or release action ran.
