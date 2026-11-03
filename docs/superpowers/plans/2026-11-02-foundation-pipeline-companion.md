# Foundation pipeline companion

Approved scope: restore optional box-set membership on ordinary unscoped movie detail routes while preserving pipeline head `8071bdb32483ca05146b96eed1f7408106ea2c47` protections. No version or delivery changes.

- [x] Reproduce enabled unscoped discovery through real HTTP and `LoadInLibraryAsync(movie, null)` before product changes.
- [x] Retain exact scoped matching and disabled-library, profile, language and departure guards; validate an unscoped response against its own eligible viewer library.
- [ ] Exercise actual unscoped card navigation and native movie-detail row, including returned-library member actions.
- [ ] Run focused/full unit tests, fresh Release publish and native foundation/room suites via the shared mutex runner.
- [ ] Freeze source/artifact hashes and red/green receipts in the external companion report; commit locally for parent review.

Reference: `nov02-boxsets/pipeline-spec-review.md`, `pipeline-standards-review.md`, approved foundation design and official `useItemBoxSet` / `catalog_box_sets.go`. The action-text duplication is advisory and outside this focused correction.
