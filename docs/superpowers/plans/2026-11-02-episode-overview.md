# Episode series overview — approved PR4 execution

Owner approved the parent design and plan. Scope is PR4 only, targeting Beta.
Base: freshly fetched `origin/beta`, `6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af`.
Version remains `1.2.544-beta.1`; pipeline owns stamping and delivery.
Official reference: `bf53b65450244751013a2965b39da4111d9a0f72`,
`web/src/pages/ItemDetail/EpisodeContent.tsx` and its regression tests.

1. Write an actual native-control regression: whitespace episode overview resolves series in the current library, keeps series translation label visible under episode spoilers. Publish unchanged product and observe behavioral RED.
2. Add a view-model overview source resolver with captured item/library/API/language/load ownership. Failed or missing series stays optional. The page renders that source and uses it for manual/automatic translation and refresh; episode own overview retains reveal behavior.
3. Add behavioral unit coverage for own/blank/missing/failed overview and retired item/library/profile/language/page reads, plus native translation target, spoiler reveal, stale callbacks and compact/wide geometry.
4. Run focused/full tests, fresh Release publish and native fixture through the external `Invoke-Verification.ps1` runner, all under `SiloOct18Build` with verified pinned mpv.
5. Record receipts and limits in current-development and external report, commit focused changes locally. Parent owns independent review and Beta PR delivery.

Pre-flight: the selected overview source feeds text, machine-translation label, action target and refresh. A fallback must never replace episode identity, navigation or playback. Source reads and every delayed translation stage must check the same owner. Native fixtures use synthetic APIs and isolated settings only.

Evidence root: `D:/SiloPlayer/.codex-tmp/server-align-nov02/overview`.
No installed-player control, installer, real credentials, production access, release stamping or publication is authorized here.

Execution ledger:
- Baseline/native dependency hydrated; SHA256 matches engine.json (`04E4D27D...F4A`).
- Behavioral RED: frozen Beta native controls failed to show a whitespace episode's series overview (`overview/fallback-red`).
- Focused GREEN: 17/17; after two scope-retirement additions full unit GREEN was 3,077/3,077 (`overview/full-green`).
- Owner requested moving development under the established D: project location. Root copied all ten changed/new files with byte-identical hashes (`d-source-copy-receipt.json`); the preserved C: source and frozen outputs remain historical evidence. Active branch is `codex/nov02-episode-overview` under `D:/SiloPlayer/.codex-tmp/server-align-nov02/worktrees/episode-overview`.
- D: dependency hash verified; D: Release succeeded (`d-source-publish`). Native initial fallback/spoiler/source-label behavior passed; same-page reload reproduced an empty description (`d-native-controls`). Configure-on-owner-change now re-resolves after reload without repeating requests on unchanged enrichment paints. Final verification follows this new source.
- Native reload now passes; compact screenshot/geometry reproduced provenance below the hero (`d-reload-native`). Moved provenance and the own-overview reveal action into the same hero column as the description/translation button. Final source awaits full/Release/native checks.
- Final D: Release `d-final-publish-two` and complete `d-final-native` pass against DLL C59398DF9071EDA8E73C9B6BBCDF0D5E9C5A3FB3E93A9ACD0447FBC37F4C9716. Compact 460px and wide 1280px screenshots inspected; provenance is beside description/action. Fixture Load helper was corrected to wait for real UpdateUI-owned automatic translation instead of forcing a second configuration/cancel. All 1,035 frozen product hashes remain unchanged.
- Final D: full unit suite `d-final-full` passes 3,077/3,077, zero failed/skipped. Product hashes still match final Release/native. Implementation ready for parent independent Standards/Spec review; no generic reruns required for subsequent documentation-only records.
