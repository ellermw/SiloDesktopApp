# Credits Up next: autoplay and pause

Reference: official Silo Server main `abd85d0cadac28f4ab7db6821dad9634fc114790`, including #4278 `87d6425`. Desktop baseline: Beta `2c33599f751e1bb7e18a56ee378ffe918710d74d`, 1.2.1145-beta.1 / numeric 1.2.1144.1, unchanged for pipeline stamping.

The production libmpv credits card now counts only while the effective autoplay preference is resolved/enabled and playback is running. Unknown/off keeps a localized Up Next heading and manual Play now, with no numeric countdown or progress stripe. Pausing or toggling autoplay preserves eligible elapsed time. Seeking outside the credits range closes the card; reentry starts a fresh ten seconds unless the viewer cancelled it. Still-watching protection and content/load/playback-owned navigation remain intact.

The initial preference shares the existing auto-skip effective-settings read. A failed read leaves credits waiting for the viewer; a successful response omitting the setting retains the established enabled default. Current profile-setting changes refresh the preference, cancel superseded reads and reject responses from retired read/playback/file/authority contexts. Lua also rejects preference messages from a different load owner. There is no new poll, whole-library read or account-setting write.

## Verification

- All three new production-Lua runtime regressions failed against the original source: unknown autoplay consumed time, pause consumed time, and seeking out left the countdown open. All pass after the fix.
- Fresh full main unit suite: 7,180 passed, zero failed/skipped. The later explicit localized-heading assertion was covered by the final 443 passing OSC/localization tests.
- Final Release publish and matching native host build passed. Native `hud-completion` cases `credits-autoplay`, `autoplay` and `postroll` all passed with their explicit completion sentinels.
- The added cancellation assertion failed on the earlier app bytes (`FC768AB3...`) before the final bounded-read correction. The final native check verifies actual effective-settings HTTP, unresolved/off suppression, cancellation, stale response rejection, paused suppression and exactly one enabled successor plan.
- Final published/reference/host app DLL SHA256: `2AEE3E1F224B01CE89EB43D2E40277D5BD6C715E5EE4BD8FA6CE6D3F5CB57678`. Native fixture DLL: `E2199330D6180EB6CD9A62FA3FBDF28E0CD1B169A1C8805D3D2508CCCF1ACE16`. The repository-pinned engine and source/published Lua hashes match.

Evidence is under this task worktree's `.codex-tmp`: `credits-{red,green,full}.log`, `credits-cancellation-red.log`, `credits-final-{publish,native-build,focused,native,chain}.log`, `credits-final-receipt.json` and `native-results/final/`. The receipt binds the changed source/runtime/test hashes to the executed bytes. Earlier logs/results retain their original scope; the reusable publish directory now holds only the final candidate.

All verification after the owner's coordination update held `SiloOct18Build`. Task-generated reusable binaries are about 1 GiB; no shared Configure payload was replaced or cleaned. Tests use synthetic HTTP and offscreen native windows, not a live account or physical remote/phone. Existing post-roll captures and six-size runtime geometry assertions cover adjacent layouts; complete visual parity, Narrator and physical-device interoperability are not claimed. The installed player/settings were untouched; no installer, direct merge or release action ran.
