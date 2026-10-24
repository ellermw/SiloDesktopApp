# Personal Settings and connection test alignment

Official Silo main was fetched directly before this pass: `d2c12dbb0f8f40f0a33304f3032f2104c5ee9674`. Read alongside the final309 release/receipt for exact execution results. Public main remains304; local305's hover repair is preserved in this candidate. No installed-player replacement, real-account preference writes, production changes or publication occurred.

## Reported visual causes

The compact profile button inherited a100px minimum width while the code requested40px; hiding the name by opacity retained its layout space. The actual hit target measured100x52 with its avatar centered near26 rather than50. The repair removes that inherited minimum, collapses hidden content and anchors the avatar slot. Expanded avatar paint is separately checked: a28px circle cannot be drawn inside an18px clipping slot.

Current WebUI personal Settings uses a260px replacement navigation rail,1120px content column in a1560px frame,48px desktop gutter and64px top padding. Its cardless sections use hairlines and a256px heading column plus48px gap when wide; rows stack at viewport768 and small selectors fill the row below640. The desktop retained raised rounded cards, an816px cap and a1024px row breakpoint. Native ScrollViewer desired-size centering also shifted the actual page gutter; setting a MaxWidth alone did not fix its actual origin.

The repair preserves existing controls/bindings and reflows their containers. It aligns section/page headings, profile/search/navigation order, available Connection layout, Playback group/row order and intro labels. Empty XAML Tags arrive as null, which explained the blank No limit selection. Settings temporarily expands its replacement rail without overwriting the saved browsing-rail preference. Native hamburger reservation is suppressed while that rail owns the header. Accessible names are explicit after replacing string item content with icon/text panels.

The current WebUI font is Plus Jakarta Sans. Runtime ThemeService already selected midnight-cinema and its correct colors; the cobalt colors seen in uninitialized fixtures did not prove a runtime theme defect. The actual outdated Outfit font was replaced with the upstream variable TTF and bundled OFL license. Shared radio controls retain native keyboard/selection behavior with Settings-scoped current-theme checked colors.

## Connection causes and repair

1. The hardened shared HTTP handler deliberately disables automatic redirects. The official speed probe redirects307 to a signed media proxy; the old desktop probe rejected that response. Explicit bounded redirects now rebuild credential-free media requests, reject downgrade/userinfo/non-HTTP targets and retain current-owner guards. Account refresh is allowed once only on the initial authenticated endpoint, never on a signed media host.
2. Same-link refresh used the same cancellation source as the manual test and could cancel it. Manual work is preserved through benign refresh and cancelled on account/network retirement or active playback. A paused loaded title no longer blocks the test; loading and cache stalls remain active, and genuine resume cancels an in-flight transfer.
3. A missing optional buffering preference or a failed post-report refresh could turn an available/successful test into a failure. Probe availability is now independent of optional preference loading; measurement-save and refresh outcomes remain distinct.
4. Controls/messages now follow the WebUI's immediate profile-default selector save, device-override clearing, feature gating, Testing… state, measured speed/transport/age, Auto budget, learned-limit Reset and retry/error behavior. Race/owner guards are retained.

Focused connection tests and offscreen native HTTP fixtures reproduce the original307/401/pause failures, including a real account-endpoint307 to an alternate-host signed-media response. No credentials are forwarded to that media target. These are isolated fixture identities/servers, not a claim of measured throughput against the owner's live server.

## Why final screenshots matter

Initial309 state/geometry tests passed while final PNG inspection exposed additional real visual defects: blank group headings from unsupported NavigationView header content, expanded avatar clipping, unused12px/48px Grid-column gaps, and Home's ContentControl being missed by the shared presentation traversal. Their original screenshots, failures and first309 installer/payload are preserved. Tests were strengthened to inspect realized text/ellipse and widths against the section rather than only a nested row; corrections are verified against a separately identified final payload.

References: `.codex-tmp/settings-reference-oct24` contains source-rendered official Playback screenshots/geometry at1920/1280/900/600 with actual font loading verified and nine rendered source files checked against the fetched commit. It uses read-only synthetic data and is not an authenticated production build; tooling dependency versions differ slightly as disclosed in its REFERENCE.md. Native evidence additionally checks3440/3840, compact/expanded profiles, real Settings rail and six personal sections. `.codex-tmp/settings-visual-oct24` and `.codex-tmp/connection-test-oct24` preserve the original/intermediate failures and scoped implementation records.

Native Windows chrome, scrollbar/glyph rasterization and conditional/dynamic states are not certified pixel-identical to every browser/server state. No complete visual parity claim or actual phone/HDR hardware interoperability claim follows from these focused checks.

Final font-specific finding: makepri dump proves the font candidate is a Path resource, so the raw TTF must ship. The old wildcard Content entry did not copy it; native host source assets masked that defect. All five TTF files now copy to output/publish, installer/build.ps1 validates every file/hash, and the native fixture checks the real published font. Final exact-payload Settings and both connection groups pass; all16 final Settings images were checked. See309 for complete counts and source/package hashes.
