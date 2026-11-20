# Admin Dashboard — Now Playing even-row parity (#4401)

Verified for a separate Beta PR on deliberate base `21233391575c09c2dfd2c39a96211b5ecc10f4f7`; version `1.2.1168-beta.1` remains pipeline-owned. Official source change: `5b3dcaa2fdfec6de2a537a2e10897352a1078689`, `web/src/components/admin/dashboard/widgets/NowPlayingWidget.tsx`. A fresh direct official-main fetch is `92ee051878c2a791940ec9598e2bdd0f04890b3e`; its later server encryption diagnostics do not change this widget.

## Scope

- Remove the `+N more` link from both fixed-height and stacked layouts; the header retains View all and the full stream count.
- Fit whole cards against the entire viewport height, without the old 24px link reservation. Keep one card when even that card exceeds the viewport.
- Clip the tag strip to one 18px line. Pills use the official 12px inherited line height; wrapped tags fall wholly below the clip, keeping card rows even.
- The native fixture gives one card enough tags to wrap, checks equal card heights/first-line fit/clipping, confirms the missing more link, and checks full-height card fitting across widths.

No other Admin section, viewer feature, routing or task behavior is bundled.

## Verification

`dashboard-full-r1` actual lease: **2026-11-16T21:53:23.6059317Z** to **2026-11-16T21:56:06.3893368Z**. Receipt SHA256 `061933A0BA15EBB90ED0B2BA688BE5EC1BE5E2A5379511E7068A18F533008586`.

- Nonincremental unit build/source binding passed; source proof **1/1** and `AdminDashboardNowPlayingTests` **26/26**, zero failures/skips. A full unit-suite repeat was not run for this narrow widget change.
- App build/publish, native-host build and app/host source binding passed.
- `admin-dashboard` passed **207** PASS markers, one completion, numeric exit **0**; rendered captures include full/half-width Now Playing layouts and long/wrapped tags. The full-width synthetic capture was inspected.
- Released audit verified **2574/2574** source entries, **665/665** app payload files, and all **9** exact inline/disk captures with valid numeric exits and contained cleanup. Local evidence: `.codex-tmp/admin-dashboard-verification/dashboard-full-r1/root-released-audit.json`.

Only delivery documentation changed afterward. The authored native assertions were not observed RED before this implementation; passing results are not a claim of test-first development. No live-server, installer, physical-input, Narrator, every-DPI or comprehensive visual-parity certification. Installed player/settings and Requests output remained untouched.

## Independent baseline use

The unchanged qualified app/host was subsequently reused only to classify the unrelated Storage regression run: `admin-manage-download-settings` reproduces its existing fixture316 timeout (`uncheck download.server_bandwidth_mbps`). That separate `downloads-baseline-r2` failed receipt is not counted as Dashboard acceptance or relabelled green; no Downloads changes are part of this PR.
