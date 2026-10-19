# October 18 upstream alignment implementation plan

**Publication authorization:** after completing this plan, the owner installed 1.2.271, confirmed it works well and authorized publishing the matching source and exact installer on October 18. The testing-before-publication requirement below has been satisfied.

> **For agentic workers:** execute using superpowers:executing-plans. The user authorized review and implementation; keep changes uncommitted and prepare an installer for testing. Independent UI and primary-source research may run in parallel without overlapping edits.

**Goal:** update existing desktop flows affected by official Silo Server main `6ab2e85f117935776db052235efeeb149bdc81b4`, preserving native direct play, performance, playback/account ownership and the installed player. Baseline: released desktop1.2.268. Compare from the previous functional reference9cc3091, including the three notification fixes preceding the badge-only f2347ac reference.

## Scope and sequence

- [x] Quality contract: normalize retired Original/source/max to Auto; offer Auto even when the server removes its old Original menu rung. Resolve canonical quality/bitrate under captured authority before selecting a source and starting a session; preserve its hard cap on quality/seek/audio/recovery replans. Use source metadata when identifying source-limited delivered tiers, with legacy-rung fallback. Keep decoder capability independent of display size. Add revision20 cellular definitions to remote-device editors and hide deprecated choices; do not invent Windows metered-network behavior or measurements.
- [x] Detail menus: group playback/library/party actions and move multiple management actions into Manage, one inline, none omitted; retain permission checks and natural handlers. Move subtitle search to the fixed Subs footer. Verify actual native menu structure/states and accessibility.
- [x] Login: place the existing capability-gated Forgot password link below the password field, right aligned, preserving reveal and navigation. Verify native narrow/wide layout.
- [x] Verify the original failures against frozen268, focused quality/authority regression checks, the full main suite and relevant exact-payload native/playback groups. Resolve any actual failures before packaging; inspect representative native captures.
- [x] Prepare a fresh1.2.271 installer (three correction groups), receipt and current-development/release records. Preserve frozen installers and leave README/public download/main unchanged until owner testing and publication approval.

## Deliberate exclusions supported by source

The new artwork-width queries are optional, backward compatible and bind some cursors; existing server defaults continue working. Their adoption is a separate enhancement. Decoder ceiling is already native2160p and independent of screen size. Embedded HLS subtitle renditions require an explicit feature negotiation that this app does not advertise; retain its proven native/sidecar path. Source-clock subtitle correction, sendfile, search/database/recommendation query improvements, downloads persistence and notification account isolation are server-side repairs with unchanged desktop request contracts. The web tab grace-period/grid-display repairs do not require a native socket lifecycle change.

Evidence/research: `.codex-tmp/upstream-oct18/playback-audit.md` and `artwork-audit.md`. Record source links and any changed conclusions in `docs/parity/2026-10-18-upstream-delta.md`.
