# November 12 official Server build / Desktop comparison

## Sources and scope

Official Server main was fetched directly from `https://github.com/Silo-Server/silo-server` and pinned to **`0b579aa6192d49d7e8e0921dde5e955ac29ce302`**. Its [Docker Image workflow #2210](https://github.com/Silo-Server/silo-server/actions/runs/43156210306) completed successfully on November 12, 2026 at 04:45:42 UTC for that exact SHA. The workflow's zero offset makes this **build-2210**. The server currently publishes ordered container builds rather than numbered GitHub Releases; the releases collection was empty. This review pulled source and checked published build metadata; it did not download, execute or deploy the container. [Build identity policy][U-builds], [workflow][U-build-workflow].

Desktop comparison target is **Beta `6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460` / `1.2.998-beta.1`**, published November 12 at 10:46:12 UTC. [Published Beta][D-release], [version][D-version]. The task's checkout is main `427b3107adce4166e8bba9b3302dbadcc2c5fc48`, product version `1.2.970`; its README also links newer Beta releases. The canonical `D:/SiloPlayer` checkout has older preserved local work and was not changed. Findings below are checked against the newer Beta, avoiding false gaps caused by main's older product source.

The last full November 1 comparison pinned upstream `63ef34b236b88dc77acca1a9a3ed90235377691f`; there are **311 commits** from there to this pin. Later scoped Desktop work already reviewed `4a9e527a432e8ef3a15dcd568802a45c15169039`; there are **56 additional commits** after that reference. These ranges overlap; they are not additive. Earlier reports were reconciled against current source, rather than reused as a missing-feature list. [Recent integration context][D-context].

This is a source/contract review, not a new application build, runtime test, visual-parity certification or installer receipt. The running installed player, real account/settings, production services and existing development changes were left untouched. No product changes, version changes, commit, push or release were made.

## API version answer

**API v1 has been retired, rather than merely deprecated. Desktop already uses API v2.** The retirement landed in upstream [#3200 / `7bb66f9a`](https://github.com/Silo-Server/silo-server/commit/7bb66f9a). Current routing returns **410 Gone / `client_upgrade_required`** for `/api/v1` and its descendants, without redirecting or authenticating. The only retained v1 probes are **GET `/api/v1/health`** and **GET `/api/v1/ready`**. The October 27 date in the scope document concerns the maintainer's waiver of the planned bridge releases; the retirement commit itself landed November 11. [Router][U-router], [tombstone][U-tombstone], [scope][U-v1-scope].

Desktop's login/profile/catalog/Home/settings/playback/download APIs, event/playback/Watch Party sockets, SiloCast bootstrap and embedded plugin launch have v2 producers. The source audit found no product producer targeting a retired v1 route; live traffic still needs the smoke test below. The prior October 27 plugin, saved-base and asset/delivery hardening work is already present; that report's original findings should not be proposed again. API v1 references in incoming-URL guards, log redaction, historical comments or tests do not establish active product traffic. [Earlier audit and implementation](../parity/2026-10-27-v1-cutover-audit.md), [auth][D-auth], [plugin][D-plugin], [retirement guard][D-retirement].

API v2 and **playback protocol v3** are different version axes. Feature names ending `_v1`, `contracts/settings/v1`, local relay URLs and worker `/stream/*` routes must not be changed to guessed v2 paths. Preserve server-issued worker URLs and signed bytes.

## Recommended Desktop updates

### 1. Match Webhook Sync's household-manager access rule (P2)

**Confirmed source/contract mismatch; runtime symptom not reproduced in this review.** Upstream [#3966](https://github.com/Silo-Server/silo-server/commit/1224daad49eef106f3c9b8dfa54fc2fdb178e5c9) now limits all Webhook Sync management reads and writes to the account's household manager. A secondary profile receives `403 permission_denied`, even on an admin account; a locked primary needs valid PIN proof. Beta still exposes Webhook Sync through its settings directory, search/navigation and load path without its existing household-management gate. A secondary profile can therefore enter a surface whose load is refused by the new server. [Server gate][U-webhook], [Desktop directory][D-webhook-nav], [Desktop load][D-webhook-load].

Gate every entry point and the actual load/mutation ownership, clear retained connection details when authority retires, and show actionable primary-profile/PIN guidance for authoritative refusals. Use the current primary-profile/acting-admin policy where applicable, while preserving the server's narrower profile-less admin exception. Request headers are already present; this needs UI/access-state alignment rather than another authentication protocol. Check directory, search, direct navigation and profile changes during reads/writes.

### 2. Use the new atomic Reset to Provider operation (P2)

**Confirmed source/contract mismatch; persistence behavior not reproduced here.** Upstream [#3939](https://github.com/Silo-Server/silo-server/commit/b75484418107761cfc1c420964f7feed1223fb78) adds capability `item_metadata_reset` and `mode:"reset"` to item metadata refresh. The operation clears the target's locks and provider-empty fields together, and specified provider failures leave existing values and locks unchanged. Beta still performs a metadata PATCH to unlock, followed by `mode:"quick"`; its generic refresh helper rejects `reset`. That older sequence can retain a stale value when a provider has no replacement, and can unlock before a subsequent refresh fails. [Capability][U-reset-cap], [request/semantics][U-reset-contract], [server implementation][U-reset], [Desktop sequence][D-reset].

Capability-gate the new operation and send `reset` without a preliminary unlock. The recommended parity choice is to match the server's target-wide lock reset and update the confirmation to describe it accurately. In particular, Desktop currently promises that picked season artwork stays locked, whereas the new reset clears that target's locks; a request substitution alone would violate the promise. Preserve independently locked descendant seasons/episodes when resetting their series. Do not promise the old unlock/quick fallback is equivalent, or blindly restore locks after an asynchronous job. For older servers retain explicitly described legacy behavior or hide the new semantics based on capability. Verify provider-empty values, provider outage, target/descendant locks, old-server fallback and stale job completion. [Desktop confirmation][D-reset-dialog], [Web operation][U-reset-ui].

### 3. Add unattended-autoplay protection

**Confirmed missing behavior; self-contained feature.** Upstream [#3917](https://github.com/Silo-Server/silo-server/commit/0fe6c54a882d21bc2f6327a9c6bd4ed3165359bf) adds **Still watching prompt**: a client-local threshold, default **3**, range **0–20**, with **0 = never ask**. N automatic advances are allowed; the next waits for the viewer. Manual starts and playback interaction reset the in-memory streak. The gate applies to credits-region advancement and the ended-video countdown, including shuffle, while leaving the viewer's saved autoplay switch enabled. [Setting][U-threshold], [streak and gate][U-still], [ended screen][U-next], [credits gate][U-credits].

Beta has `playback.auto_play_next` and its countdown, but no threshold/streak implementation in authored C#/XAML/Lua. [Settings][D-auto-setting], [countdown][D-auto-countdown]. Add a local preference and session-owned streak, distinguish manual/automatic advances, and apply the gate at both boundaries. Keep Watch Party advancement owned by the room. Do not write this client-local preference through the server settings API.

Future checks: threshold 0/default/max, automatic versus manual next, pause/seek resets, shuffle, credits then EOF, cancellation/profile departure, and no duplicate advance.

### 4. Update playback presentation to the new HUD

**Confirmed source deltas; native visual work needs its own verification.** November 11 [#3993](https://github.com/Silo-Server/silo-server/commit/e58c82566ea506dcdfe94603267b9596604aba1d) replaces the Web player HUD: seek/chapter presentation, labelled utilities, unified menus, compact speed picker and a redesigned Playing Next screen. Desktop already has corresponding playback menus, chapter previews and post-roll. Reconcile the existing native/Lua components with this new design; do not treat those controls as entirely absent.

Two concrete missing additions:

- **Estimated finish time**, such as “Ends 2:38 AM.” Upstream uses remaining time and playback rate, updates during pause and scrub, follows profile 12/24-hour preferences, uses 1x for Watch Party and hides the label for unknown duration. No corresponding wall-clock finish-time implementation was found in Beta's authored product code or OSC Lua. [Calculation][U-end-time], [placement][U-player-controls].
- **Artwork behind the loading spinner** during plan loading and first frame, with thumbhash then image fade. Beta's OSC loading surface explicitly paints a solid black frame; the shell uses its app background. [Web loading cover][U-loading], [native OSC][D-loading], [shell][D-shell-loading].

Phone sheets, portrait controls and ambient letterbox glow are Web/touch layout choices; assess their native relevance separately. Existing Desktop menu measurement, keyboard ownership, subtitle details and Next Episode/timeline clearance remain important while changing the HUD. Validate against the pinned official reference at small/large windows and text scales, through loading/first-frame/buffering/error/menu/Playing Next states. Source inspection alone cannot certify visual parity.

### 5. Bring Featured hero sizing and text up to date

**Confirmed smaller visual gap.** Upstream #3984 changes Home/Library hero fixed heights to minimum heights so wrapped titles and other content can grow the hero. #3995 removes the **FEATURED — No. NN** eyebrow. Beta still assigns fixed `Height = Math.Clamp(...)` and renders that eyebrow; its title-width balancing does not grow the container. [Current Web sizing][U-hero-size], [Web hero][U-hero], [native sizing/eyebrow][D-hero].

Use measured content and required clearance above a viewport-based minimum; remeasure on width/title/language/text-scale changes. Remove the eyebrow and its margin. Keep the separate carousel navigation/counter, which upstream retains. Desktop's Featured backdrop is already static, so the upstream removal of Featured panning is not an additional gap. Collection hero motion belongs to another component.

### 6. Add interface localization as one coordinated initiative

**Largest remaining feature area; not an English-only API compatibility failure.** Upstream now ships interface catalogs for English, Spanish, German, French, Brazilian Portuguese, Japanese, Simplified Chinese and Arabic, with profile app language, locale formatting and RTL. Metadata/audio/subtitle language are separate preferences. Beta has metadata-language support but no app-language resource/message layer. [Shared catalog architecture][U-i18n], [effective language][U-language], [picker][U-language-picker], [Desktop metadata-only invalidation][D-localized].

Deliver this through related, reviewable work packages:

1. **Effective language and native catalogs.** Implement nullable `ui.app_language` (“Match device”), profile-device/profile/default resolution, unsupported-tag preservation, profile-safe loading/saving and realtime settings changes. Use the authoritative translations with a deliberate Windows resource-generation policy; there is no Windows output promised by the current generator. Offer only languages actually shipped in Desktop.
2. **RTL and display formatting.** Apply direction and culture together; review shell, rows, forms, menus, focus and directional browsing. Format dates, decimals, counts/plurals and relative text through the selected display locale while respecting explicit date/time preferences. Preserve identifiers, wire formats and Latin episode codes as required. Beta has no app-level RTL application and formats through process/invariant cultures. [Web locale/direction][U-locale], [Desktop date helper][D-date].
3. **Server-written title keys.** Retain optional `title_key` / `title_args`, section default-title metadata and recipe display/description keys through DTOs and row reconciliation. Translate at display time; preserve custom titles and raw values used for editing/saving. Unknown keys/missing arguments fall back to server text. Beta's Home/section/recipe DTOs omit these fields. Do not save translated recipe labels as accidental custom names. [Section contract][U-section-contract], [display resolver][U-section-title], [Home model][D-home-model], [recipe model][D-recipe-model].
4. **Structured problem messages.** Retain field error codes/details and resolve translated messages at the UI boundary, preserving machine codes, status, retry and PIN-verification behavior. Beta retains the first error location and machine problem type, but not the full field metadata, and displays English exception messages. Headers alone cannot translate these server responses. [Web problem handling][U-problem], [Desktop decoding][D-problem].

Language/profile transitions, unsupported/old-server fields, unknown keys, user-written titles, mixed-direction media titles and validation errors need explicit coverage. The four packages are one localization gap, not four separate transport regressions.

## Current compatibility checks to perform before an updated Beta

These are verification requirements, not claims of reproduced failures:

- Exercise a current server with v1 tombstones through fresh/resumed login, profile/PIN switching, Home/Library/detail/search/artwork, plugins, downloads/ebook, all sockets and SiloCast. Inspect actual outgoing URLs, not only static route assertions; the server's `streamapp_apiv1_tombstone_requests_total` counter can identify stray requests. Preserve version-neutral workers and signed URL bytes.
- Check current viewer access rules with primary/secondary and restricted profiles: missing non-curator file names/paths, hidden account-level downloads, quality-ceiling episode filtering, and account identity/webhook-management refusals. Most changes are enforced by the server; avoid reproducing its access policy in a separate client catalog.
- Exercise direct play, progressive remux, HLS, nonzero resume/seek, quality/audio/subtitle changes, subtitle-ready events, route/node loss and playback interruption. Current Beta already has subtitle-ready/intent work; these new-server checks do not imply that work is absent.

**Progressive-remux distinction:** `progressive_range_refusal_v1` is an optional browser-oriented negotiation feature. Upstream explicitly says native players retain HTTP200 and skip already-read bytes; only an opted-in client gets range refusal and must handle HTTP409 by reopening. Do not add that opt-in just to match a capability list. #4004 changes the Web reconnect policy to remux `seek_reanchor` and preserves failed-route history. Desktop has its own bounded direct-stream recovery and different handling for other transports; test native remux interruptions before deciding a policy change is needed. This review does not reproduce the browser issue in libmpv. [Delivery contract][U-delivery], [Web reconnect correction](https://github.com/Silo-Server/silo-server/commit/aa82760293de924cc43e553d28357210f8c11c8c), [native recovery][D-recovery].

## Already present in Beta

Source inspection found these earlier/newer features implemented, so they are not new missing-work recommendations:

| Area | Evidence in pinned Beta |
| --- | --- |
| Password change with optional other-session sign-out | `AccountPasswordViewModel.cs`, `AccountPasswordApi.cs` |
| Series/season cross-device watched/progress refresh | `Services/RealtimeMediaStateRefresh.cs` |
| Scoped Match/Split and season/episode metadata locks | `MatchItemDialog.xaml.cs`, `SplitVersionsDialog.cs`, `EditMetadataDialog.cs` |
| Marker provenance, capped Home rows, alphabet index, episode overview fallback | `WatchDetailResponse.cs`, `HomeApi.cs`, `CatalogApi.Letters.cs`, `ItemDetailViewModel.cs` |
| Remembered Watch Party guest-control policy and subtitle-ready/intent handling | `ItemDetailPage.xaml.cs`, `PlayerService.SubtitleReady.cs`, `PlaybackManager.cs` |
| Bound device setup/login, title/collection allowances, personal data transfer | `DeviceLoginSetup.cs`, `TitleAllowancesApi.cs`, `ProfileTransferApi.cs` |
| Dedicated Continue Watching/Next Up removal and Undo | `CatalogPage.xaml.cs`, `HomeDismissalPolicy.cs` |
| Collection wall/hero/viewport loading, overlays, smart-rule validation, Trending Show | `CollectionsPage.Loading.cs`, `CollectionBrowsePage.Header.cs`, `CardOverlayService.cs`, editor/wizard view models |
| Next Episode above the timeline and content-sized native playback popups | Production `silo-osc.lua`, `PlaybackChoiceMenu.cs` |

Presence is not fresh runtime certification. Older handoff text such as “Dedicated page has no Home-style Undo” is superseded by current code. Full pinned references and reconciliation are retained in the review evidence.

## Server-owned work and optional additions

Scanner/database/memory/dependency tuning, quality-ceiling episode filtering, smart-collection Home limit/watch-state fixes, hidden download rows, stream-token privacy and Jellyfin/Audiobookshelf compatibility changes primarily belong to the server. Desktop benefits from the corrected responses; no general reimplementation is indicated. Browser-specific open-GOP/laced-audio decoder repairs do not establish libmpv defects.

Lower-priority maintenance: correct four stale v1 comments and ordinary transport fixture URLs while retaining deliberate legacy-input/redaction tests. Consider a narrowly allow-listed product-route invariant after the real tombstone smoke. Account-identity disconnect already uses the server's `can_unlink`/profile gates; PIN refusal messaging can be improved. The API worker's concern about external image URLs containing `/api/v1/` has no identified triggering producer and is not a confirmed defect or reason to weaken the current retirement guards.

The API audit calls localization fields additive and therefore not transport blockers. That is compatible with section 6: optional fields do not break English Desktop reads, but consuming and rendering them is necessary for current multilingual WebUI feature parity.

Optional future feature: native consent-based diagnostics uploads. Upstream #1986 extends the existing service with hang reports, occurrence counts and playback summaries. Desktop has local logs/incidents but no diagnostics discovery/upload client. Adding one would require capability/source/size checks, redaction, consent and correct uncertain-upload handling; it is not required for v1 retirement. [Upload contract][U-diagnostics].

## Verification and retained evidence

Fresh source-identity verification checked **2,207** files in the extracted Beta snapshot against the pinned Git tree, including the expanded libmpv LFS object against its pointer's SHA256; zero mismatches. The official server reference checkout was clean. A fresh remote-ref check still returned the same server/main/Beta SHAs. GitHub metadata confirmed the successful matching server build and the published Desktop versions. No product tests or builds were run for this documentation-only review; older test receipts do not verify these exact merged bytes.

Evidence directory: `.codex-tmp/upstream-review-2026-11-12/`, containing the official source clone, Beta snapshot, upstream history, `snapshot-verification.json`, `reference-verification.json`, `api-audit.md`, `viewer-audit.md` and `playback-audit.md`. The lead re-read the two P2 findings against pinned server and Beta code, reconciled the API-versus-feature localization distinction, and retained the native-remux limitation. Nothing in this report establishes live-server persistence, hardware input, phone interoperability or complete visual parity.

## Pinned source links

[U-builds]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/docs/release-versioning.md#L50
[U-build-workflow]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/.github/workflows/docker.yml#L55
[U-router]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/internal/api/router.go#L3381
[U-tombstone]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/internal/api/v1_tombstone.go#L11
[U-v1-scope]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/docs/architecture/v1-scope.md#L6
[U-threshold]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/lib/settingsContract.ts#L1117
[U-still]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/player/stillWatching.ts#L9
[U-next]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/player/components/PlayingNextScreen.tsx#L60
[U-credits]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/player/hooks/useNextEpisode.ts
[U-end-time]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/player/hooks/usePlayerEndTime.ts#L13
[U-player-controls]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/player/components/PlayerControls.tsx#L238
[U-loading]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/player/components/PlayerLoadingCover.tsx#L21
[U-hero-size]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/lib/design-system.ts#L590
[U-hero]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/components/HeroBanner.tsx#L324
[U-i18n]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/docs/architecture/i18n.md
[U-language]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/hooks/useAppLocale.tsx#L99
[U-language-picker]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/pages/settings/AccessibilitySettings.tsx#L46
[U-locale]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/lib/appLocale.ts#L64
[U-section-contract]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/internal/apiv2/library_views.go#L77
[U-section-title]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/lib/sectionTitle.ts#L19
[U-problem]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/api/v2/request.ts#L254
[U-delivery]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/docs/playback-api.md#L390
[U-diagnostics]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/docs/diagnostics-upload-api.md#L6
[U-webhook]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/internal/apiv2/webhook_sync.go#L173
[U-reset-cap]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/internal/apiv2/admin_catalog_items.go#L104
[U-reset-contract]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/internal/apiv2/admin_catalog_item_metadata.go#L63
[U-reset]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/internal/metadata/reset_to_provider.go#L9
[U-reset-ui]: https://github.com/Silo-Server/silo-server/blob/0b579aa6192d49d7e8e0921dde5e955ac29ce302/web/src/components/EditMetadataDialog.tsx#L191
[D-release]: https://github.com/ellermw/SiloDesktopApp/releases/tag/v1.2.998-beta.1
[D-version]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer/SiloPlayer.csproj#L31
[D-context]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/docs/current-development.md#L53
[D-auth]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer.Core/Api/AuthApi.cs#L30
[D-plugin]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer/Views/PluginRoutePage.xaml.cs
[D-retirement]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer.Core/Services/NativeApiRetirement.cs
[D-auto-setting]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer/ViewModels/SettingsViewModel.cs#L896
[D-auto-countdown]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer/MainWindow.xaml.cs#L772
[D-loading]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/libs/mpv/scripts/silo-osc.lua#L4445
[D-shell-loading]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer/MainWindow.xaml#L622
[D-hero]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer/Controls/HeroCarousel.xaml.cs#L142
[D-localized]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer.Core/Services/LocalizedMediaReads.cs#L14
[D-date]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer/Helpers/DateTimeDisplay.cs#L9
[D-home-model]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer.Core/Models/Home/HomeSectionsResponse.cs#L10
[D-recipe-model]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer.Core/Models/Home/RecipeCatalog.cs#L18
[D-problem]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer.Core/Api/SiloApiClient.cs#L1094
[D-recovery]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer.Core/Services/PlaybackManager.cs#L626
[D-webhook-nav]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer/Views/SettingsPage.xaml.cs#L1020
[D-webhook-load]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer/Views/SettingsPage.xaml.cs#L5100
[D-reset]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer.Core/Api/MediaMaintenanceApi.cs#L15
[D-reset-dialog]: https://github.com/ellermw/SiloDesktopApp/blob/6a8ef0b5cd5ad8d2fbfb662c5ac8b9b115c92460/src/SiloPlayer/Views/Dialogs/EditMetadataDialog.cs#L127
