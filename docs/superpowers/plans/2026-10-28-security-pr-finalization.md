# Security PR34/35 finalization

**Goal:** verify both security reports, correct incomplete changes and the owner's updater visibility report, then merge and publish matching355 installer/README/checksum while preserving339 and the installed player. Version355 records16 concrete behavior changes over339; see docs/releases/1.2.355.md.

**Constraints:** separate clean checkout from current339; synthetic accounts and local archives only; pinned dependencies; SiloOct18Build for all .NET work; independent Standards/Spec reviews and exact-head non-force pushes; no installation or production changes.

- [x] Review34 first and35 next with independent Standards/Spec agents and actual callers.
- [x] Reproduce34's parameterless login and accessible-name failures in native WinUI; reproduce top-level SVG active content in actual WebView2 against the preserved baseline.
- [x] Fix34 using the authoritative configured credential target and text-derived accessible names; verify desktop/narrow actual pages and fake password target.
- [x] Verify35's collision/centering failures; choose collision-free wrapper paths and centered inert image layout; commit meaningful archive and WebView regressions.
- [x] Replace six-hour checks with startup/15-minute discovery and five-minute error retries; show native setup progress rather than very-silent hidden setup. Verify using virtual-time checks, synthetic native notices and actual Windows PowerShell launch configuration without setup execution.
- [x] Verify current-main combination, build355, run full/relevant native/published checks and seal exact installer bytes.
- [ ] Merge corrected heads, publish355, verify public assets/updater discovery, record receipts and PR results; release integration lock.

Scope: destination visibility does not block unknown servers; native origin text replaces the browser address bar's trust signal. SVG tests must establish actual script isolation and image rendering, not merely assert generated strings. Historical full parity/hardware and elevated update installation require their own evidence.
