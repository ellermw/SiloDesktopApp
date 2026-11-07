# Foundation pipeline companion

Approved scope: restore optional box-set membership on ordinary unscoped movie detail routes while preserving pipeline head `8071bdb32483ca05146b96eed1f7408106ea2c47` protections. No version or delivery changes.

- [x] Reproduce enabled unscoped discovery through real HTTP and `LoadInLibraryAsync(movie, null)` before product changes.
- [x] Retain exact scoped matching and disabled-library, profile, language and departure guards; validate an unscoped response against its own eligible viewer library.
- [x] Exercise actual unscoped card navigation and native movie-detail row, including returned-library member actions.
- [x] Run focused/full unit tests, fresh Release publish and native foundation/room suites via the shared mutex runner.
- [x] Freeze source/artifact hashes and red/green receipts in the external companion report; commit locally for parent review.

Product commit `091defe98c0ea49604fe2839236de4fb1451f2de`: focused 66 and full 3,124 pass; fresh Release DLL SHA256 `865962D457262443F308C0D8CFBAD992B243D4BE9B7443F373C3FA34928E7DBD`. Final native foundation (including the real unscoped route) and room suites pass against those same bytes. Pipeline capability-error fixture now waits for actual Loaded/layout before traversing its visual tree; the initial diagnostic showed a correct Transient model and logical error/retry controls on an unloaded page. The unscoped fixture observes the queued row render before checking controls. All failures/diagnostics and original bytes remain preserved in the external report.

Reference: `nov02-boxsets/pipeline-spec-review.md`, `pipeline-standards-review.md`, approved foundation design and official `useItemBoxSet` / `catalog_box_sets.go`. The action-text duplication is advisory and outside this focused correction.

## Additional PR74 menu-scope correction

Thread `PRRT_kwDOS5hFiM6y7E74`, comment 4449386207 reports restart menus bypassing their member card's returned library. Continue from clean `7617ddf2`; preserve all prior packages and receipts.

- [x] Reproduce the actual PosterCard menu's restart/watch/start scope on the frozen 807 app using synthetic playback APIs.
- [x] Pass the owning card's captured library into the existing restart path without changing its fromStart or playback lifecycle semantics; reject retained menus whose item, scope, Loaded state or viewer identity has departed.
- [x] Verify dedicated-page members, membership-row members and unscoped cards; run focused/full units, fresh Release and relevant native menu/foundation/room verification.
- [x] Commit locally and append exact refs, hashes, red/green and limits to the companion report for new scoped review.

Product `64bd63e00231fa93e5d23ea33d282f3be01679a5`, fixtures `620cd53660b80b4200751023e4c91ad4cd4dca1d`: focused **414/414**, corrected full **3,124/3,124**, fresh Release and all four native routes pass. New DLL SHA256 is `BB82FAFBC565F7A169EEA7AA9ACCE75A4C2BCCE845951659B51B1C5C3268C8E0`. Actual popup commands retain library/file/profile/zero restart; recycled/scope-changed/departed/other-profile menus do not prepare playback, while the same viewer's access refresh remains valid. Initial full failure was a source-text assertion requiring exactly two playback arguments; actual native behavior now verifies that contract. All original evidence remains frozen. Parent owns new scoped review and remote gates.

## Captured identity through ordinary restart admission

The subsequent scoped Spec review found that the pre-click guard did not cross the authority/request gates or preparation awaits. Continue from clean `6398d021`; preserve the BB82 candidate and all previous evidence.

- [x] Reproduce actual held-authority popup → profile-change playback on BB82, and capture baseline ordinary menu accepted-unload, active-room and queue-behind-scoped controls.
- [x] Add a focused ordinary restart interface carrying captured identity through admission/preparation, independent of quick-play supersession and no-active-room policy.
- [x] Verify authority/request/watch/settings/capability retirement, same-viewer access refresh and ordinary controls; full units, fresh Release, actual menu/foundation/room and existing quick-play lifecycle regressions.
- [x] Freeze source/app/evidence, commit locally and return for scoped re-review.

Product/build `37cc69ef46c60bdd5fd166e4c50575a81e1db97f`, final fixture `abc34b8b8cbaa353b332213404a00e6af0f28031`: **466 focused**, **3,124 full**, fresh Release and **seven native routes** pass on DLL `53DC2DBE6AEE834064F782F4B9D2455731243EF4E9D8558297903FEFA1E4922B`. Ordinary accepted-unload, active-room, queue-behind-scoped and actual CloseAsync controls pass on both BB82 and final source. The full existing quick-play suite also passes on both apps, including all 17 review regressions. Its initial run exposed five inherited fixture lookups treating public SetState as non-public; the fixture-only correction uses direct calls with assertions unchanged. Original/final fixture hashes and all failed/intermediate runs remain preserved. No republish followed that fixture correction; all product inputs remain frozen.
