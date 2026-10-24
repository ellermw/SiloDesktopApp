# Dolby Vision and personal Settings implementation plan

**Goal:** Enable verified Profile7 playback, verify Profile8 on the actual bundled Windows rendering path, repair the profile footer hover and personal Settings layout/speed test, and deliver an owner-test installer without publication.

**Baseline:** Local1.2.305 media-action hover candidate on public304; preserve its uncommitted source and frozen installer. Official Silo main fetched directly for this pass: `d2c12dbb0f8f40f0a33304f3032f2104c5ee9674`. Read current-development.md before builds. Source remains uncommitted. Keep the installed app open; no real-account preference mutations or infrastructure changes.

**Design:** Retain native WinUI/libmpv. Inspect the shipped engine before upgrading; use a reproducible x64 engine/dependency build linked from mpv's official installation documentation, retain hashes/source provenance, and test real P7 enhancement-layer processing rather than merely adding7 to capability JSON. Use the existing gpu-next/D3D11 path; distinguish processed Dolby Vision source from the display's actual Windows HDR output. Match current personal Settings structure, widths, breakpoint rules and actions while preserving native accessibility and account ownership guards. Connection probing must follow the official signed media redirect without forwarding credentials to a media host.

**Execution:** Independent Settings/profile-layout and connection-test work runs in scoped parallel agents; root owns engine investigation/integration, final source/engine pin, version/release documentation and packaging. Shared native/build/test resources use `SiloOct18Build`. No agent rewrites another agent's shared files.

- [x] Reproduce each reported Settings issue against frozen305 and current WebUI source; keep original failures.
- [x] Probe current and candidate engine versions/features; obtain small provenance-recorded P7 MEL/FEL and P8 samples and demonstrate the responsible capability gap.
- [x] Upgrade only as needed, add actual playback diagnostics and capability tests, verify decoded metadata/enhancement-layer processing, seeks and native output; retain an honest hardware/output limitation if unavailable.
- [x] Correct compact profile hover geometry and personal Settings layout/rows/breakpoints; compare rendered reference and actual native pages at narrow/1080/ultrawide/4K sizes.
- [x] Repair the connection probe's actual redirect/lifecycle/persistence behavior and match WebUI controls/messages; verify on isolated HTTP services and native controls.
- [x] Review integrated changes, run relevant and full regression checks, preserve hover305 behavior, build through installer/build.ps1, verify exact packaged engine/source/payload and create a receipt.
- [x] Deliver the installer and precise evidence/remaining limits for owner testing. Do not push or publish until subsequent authorization.
- [x] Subsequent authorization received: the owner installed 1.2.309, confirmed it is much better and requested publication to main, the installer release and updated README/download link. Publish the exact approved bytes without rebuilding.

**Versioning:** Four correction groups anticipated beyond305: Dolby Vision engine/profile verification, compact profile backdrop, Settings layout, connection speed-test behavior/presentation. Final version and counts follow implemented scope, rather than file/function counts.
