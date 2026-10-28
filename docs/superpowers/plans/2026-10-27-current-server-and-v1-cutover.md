# Current Silo alignment and API v1 cutover implementation

**Goal:** Bring local desktop 1.2.312 into alignment with the user-facing changes at official Silo main `564b906b8eb529c77c5e0b5c640aa13c1133c7f1`, and verify native client functionality without the legacy API bridge before the owner's announced Friday retirement.

**Spec:** `docs/parity/2026-10-27-v1-cutover-audit.md`, `docs/parity/2026-10-27-user-facing-delta.md`, and the core contract audit in this directory's parity collection. These are source-audit findings, not claims of successful runtime verification. Last complete 309 alignment reference is `d2c12dbb0f8f40f0a33304f3032f2104c5ee9674` (73 commits).

**Architecture:** Keep the existing WinUI/.NET/native libmpv architecture. Use the current v2 native contract, retain documented version-neutral worker delivery paths and signed URL bytes, and match the current WebUI's viewer behavior. Do not mechanically rewrite all v1-looking or signed URLs. New admin-only server management surfaces and optional artwork-presentation redesigns are outside this viewer alignment pass.

## Constraints

- Preserve every local 310/311/312 change, all frozen installers and all diagnostic evidence. Work in the canonical checkout; no reset, stash, automatic commit, push, release publication, production change or installed-player interaction.
- Existing installed player stays open. All UI/playback verification uses isolated hidden fixtures and synthetic identities/content.
- Never expose credentials or signed URLs. Diagnostic tests use synthetic credentials only.
- Official source is pinned above; today's source still exposes the v1 bridge, so passing against it alone is insufficient. Use strict fixtures/guards that reject retiring legacy native requests.
- Nine correction groups beyond312 target **1.2.321**, unless verification reveals an additional distinct correction group; keep the count documented.
- All builds/tests share named mutex `SiloOct18Build`. Preserve red and intermediate results. Use `installer/build.ps1` with a fresh explicitly bounded publish directory and existing font/native/signing checks.
- Main/native/published tests must execute final packaged code. Fingerprint inputs and package files, and separately identify any post-package fixture-only adjustment.

## Review focus

- V1 refusal must cover plugin WebView navigation/resources and returned URLs, not just Core API literal prefixes. Preserve valid distributed/worker URLs and fail safely on retired local native resources.
- Account/profile/PIN retirement cannot authorize a stale plugin page or media request under a new owner.
- Requests custom/hidden/interleaved rows, opaque IDs, old servers, missing/invalid continuation, trimmed one-page rows and partial failures retain correct navigation/pagination.
- Existing user-selected/saved audio intent must survive while null Auto permits the server to choose a compatible delivered track; never carry a source-default index into another selected version as explicit intent.
- Media bearer credentials and signed tokens must not leak through absolute subtitle URLs or transport exception logging. Keep same-server account authentication where required.
- Metadata reset must unlock before refresh, preserve failures, and serialize image/save/reset writes; uploaded artwork identity is opaque and must not be fetched as a URL.
- Retired caption values must not hide titles, shrink metadata rows or persist the obsolete preference. Request removal must leave detail and watchlist request workflows intact.

## Tasks / correction groups

### 1. API v1 cutoff and stale native adapters
- [x] Move plugin page/resource launch to `/api/v2/plugin-content`, preserving descriptor normalization, directory assets and same-server authority.
- [x] Add meaningful cutover tests/native fixture with legacy routes returning410 and v2 page/assets succeeding, plus profile/owner boundary checks.
- [x] Review incoming delivery/asset URL compatibility and saved API-suffixed bases. Only apply documented migrations; preserve signed distributed and retained worker paths. Detect/refuse unsupported retired native routes rather than silently depending on the bridge.
- [x] Remove unused adapters/tests advertising removed theme catalog/download/refresh operations; distinguish harmless historical comments and protocol feature names from HTTP routes.

### 2. Requests Discover layout and pagination
- [x] Consume capability, row order and custom row IDs; render interleaved title/brand rows and skip unknown/disabled rows without resurrecting them.
- [x] Page custom IDs through `/requests/discover/rows/{row_id}`, preserve built-in/old-server behavior and correct absent/invalid cursor handling.
- [x] Include Explore All for rows truncated below total_results even with a single source page; retain failure/context/deduplication behavior.
- [x] Test current/older contracts, custom/hidden/empty/unknown rows, opaque IDs, source totals and continuation edge cases.

### 3. Retired media-card caption option
- [x] Remove settings controls and preview/search entries; normalize/read/save to title_metadata while preserving poster sizes and required wire shape.
- [x] Remove old conditional render/height branches throughout cards and media surfaces. Verify older stored artwork/title values retain visible title/details.

### 4. Request action contexts
- [x] Remove direct Request actions from Discover/search/brand/external recommendation cards to match current WebUI. Preserve navigation, watchlist actions, detail-page season/movie requests and the separate watchlist Request action.

### 5. Metadata locks/reset
- [x] Map tagline to overview lock1. Reset unlocks first, refreshes only on success and gates concurrent save/image/reset writes.
- [x] Verify failure preserves edits and no late apply re-locks after a reset.

### 6. Existing uploaded-artwork display
- [x] Recognize the current uploaded choice by its returned opaque upload identity/current fields in the existing image editor; do not request upload:// URLs.
- [x] Keep server authoritative for image locks and propagate existing surface-change refreshes. New admin upload creation itself is not required for current viewer API compatibility.

### 7. Premiere labels
- [x] Match Premiere/New Season/Finale consistently across Calendar/poster/landscape accessibility and visible labels; retain wire codes/colors.

### 8. Compatible Auto audio
- [x] Stop turning routine effective/default audio into explicit start intent; preserve actual viewer picks and remembered server choices.
- [x] Verify outgoing starts and actual native track selection for Auto/explicit/lowered-source cases with no new engine/forced-remux changes.

### 9. Media credential and diagnostic boundary
- [x] Protect absolute signed/distributed subtitle URLs from account-token attachment while preserving required account-side subtitle auth.
- [x] Redact signed path/query credentials (including st/dt and encoded paths) in transport failures and ordinary log writes. Use structured/safe exception logging where needed.
- [x] Add regression cases for signed URLs, benign diagnostics, redirect/origin handling and nested exception chains.

## Final verification and delivery
- [x] Record exact upstream contract delta, route inventory, implemented/automatic/excluded dispositions and remaining limits in consolidated audit.
- [x] Run fresh main suite and relevant API/cutover/URL tests; preserve failing baseline proof.
- [x] Build a fresh versioned installer, verify native dependencies/fonts, then run exact-payload native plugin/Requests/settings/card/metadata/playback regressions plus existing312 startup and311 profile-menu checks.
- [x] Run published-service playback/SiloCast/authority checks against the same payload. Verify all source/package hashes and record receipt/release/current-development notes.
- [x] Give the owner the local installer to test. Do not install, commit, push or publish without their separate authorization.
