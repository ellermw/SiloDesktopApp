# October24 upstream comparison — desktop1.2.286

Implementation follow-up: the owner accepted all15 groups. They are now implemented in local1.2.304 alongside two Home/navigation correction groups and a reader-deadline correction discovered during native verification. See [304 scope and current validation status](../releases/1.2.304.md) and [the implementation plan](../superpowers/plans/2026-10-24-upstream-and-home.md). The original read-only audit below retains its findings as examined against286; it does not describe the final source as still unmodified.

Read-only source audit. Official repository `https://github.com/Silo-Server/silo-server` was fetched directly before this pass. Exact comparison: desktop286 reference `784f2e439850b25188761c56fb76ee49d31ee32f` → **`489fe4d55735f3855b2a0d6276e57d148891b456`**,129 commits. Desktop examined: `bf0281a59417c9c3e19cbe5473f46fb2b5873bbe`, source Version1.2.286. The server mirror's working-tree HEAD is historical; all comparisons used exact Git objects. Fetching the source does not update the production server.

The following15 groups warrant desktop changes for current user-facing/API alignment. They are source-level findings and proposed verification, not implemented or runtime-verified repairs. Existing local issue8 regression tests/docs and frozen installers were preserved. No product code, installed app, production settings, release version or GitHub publication changed.

## Near-term compatibility and behavior repairs

### 1. Accept current low-quality choices

The current server advertises `480p-low` and `360p`. Desktop `PlaybackQualityPreferences.Normalize` does not recognize either, so selecting one can normalize it to Auto before the replan. `MenuOptions` already includes advertised rows, making this an exposed mismatch. Preserve the exact identifiers across native, OSC and SiloCast; retain server-driven menu availability and delivered `active_quality` behavior.

Evidence: [new ladder and buffering feature](https://github.com/Silo-Server/silo-server/commit/fdbb887110b3394805aa3236b3caa5147cb022ed); desktop `src/SiloPlayer.Core/Services/PlaybackQualityPreferences.cs:17-31`, `PlaybackManager.cs:333-342`, `PlayerService.cs:5922`. Verify actual clicked labels in outgoing replans and accepted UI state, including older-server inventories.

### 2. Save/reset explicit detail audio choices and reconcile track intent

Current v2 starts/replans no longer automatically save the audio that happened to play. Explicit clients must PUT `/api/v2/audio-prefs/{series_id}` or DELETE to return to Auto. Native in-player choices already persist a signature, but detail-page audio handlers only change `_selectedAudioTrackIndex`; choosing Auto does not clear an earlier saved choice. Match current signature semantics and serialize captured-owner writes without persisting automatic fallback choices.

Related integration check: native local subtitle changes and the plan's `selected_tracks`/`desired_tracks` can diverge during quality/version changes. The quality replan echoes plan state, while a local subtitle pick mainly changes mpv and stored preferences. Native loaded initialization also does not consume the mapped server-selected subtitle ID. Reproduce quality/version/next-episode round trips before deciding the exact repair; this integration risk is not a confirmed runtime reproduction.

Evidence: [explicit and remembered track changes](https://github.com/Silo-Server/silo-server/commit/8ec2a2651d8d289d3f1dd53300ab0d9f6ca6ca79), final `docs/architecture/playback-protocol-v3.md:1065-1097`; native `ItemDetailPage.xaml.cs:5557-5565,5593-5600`, `CatalogApi.cs:402-409`, `PlaybackManager.cs:526-539`, `PlayerService.cs:1004-1073,4980-5015`. Verify preference reset, ordered writes/failure rollback, reordered tracks on another version, profile retirement and next-episode restoration.

### 3. Correct Auto subtitle language selection

Current WebUI compares actual audio with the effective **subtitle language**. Native `SubtitleAutoSelect` still compares profile language, defaults missing profile language to English and substitutes that value for unknown audio. Example: English profile/audio plus Spanish subtitle preference can suppress the desired full Spanish subtitles. Fix the shared helper used by both detail preview and actual startup, including unknown language and regional/ISO aliases.

Evidence: [current Auto semantics](https://github.com/Silo-Server/silo-server/commit/6e952e053a274f2436b2aa096d2009b314642a75), `web/src/player/utils/subtitleSort.ts:219-253`; native `SubtitleAutoSelect.cs:45-63,172-219`, `ItemDetailPage.xaml.cs:5997-6005`, `PlayerService.cs:3236-3270`. Verify matching/different/missing/unknown languages, forced-only, always/off and preview/playback agreement.

### 4. Stop detail subtitle picks pinning forced-subtitle defaults

The detail picker sends the currently resolved `ShowForcedSubtitles` back during a subtitle preference save. The server can then turn that inherited value into a series override, so later global setting changes appear ineffective. Omit this member when the user only picks a subtitle; preserve genuine overrides edited through their dedicated control. The in-player save already omits it.

Evidence: [WebUI omission](https://github.com/Silo-Server/silo-server/commit/36a80277940d044db16d5643539da9c7e3f6a630); desktop `ItemDetailPage.xaml.cs:6174-6183`, `PlaybackApi.cs:128-129`. Verify a picker action cannot create an unintended override, while an intentional override survives.

### 5. Adopt non-404 favorites/watchlist membership reads

New capability `/api/v2/capabilities/personal-lists` advertises `membership_supported`; new `/favorites/{item_id}/membership` and `/watchlist/{item_id}/membership` return200 with `is_member=false` for visible nonmembers. Native `CatalogApi.GetFavoriteItemAsync`/`GetWatchlistItemAsync` still interpret routine404s as nonmembership. Upstream documents that many such paths can trigger reverse-proxy probing/banning rules. This is a supported reliability improvement, not proof that a proxy caused any particular desktop buffering incident.

Use capability discovery and captured-owner caching; retain compatibility with servers lacking the new reads. A missing/inaccessible item still returns404, so do not indiscriminately turn every new-route404 into a legacy retry.

Evidence: [membership API implementation and rationale](https://github.com/Silo-Server/silo-server/commit/cc5f97b5f025848871e91a42966741bed8174126), `internal/apiv2/favorites.go`, `watchlist.go`; native `CatalogApi.cs:374-399`, detail parallel reads at `ItemDetailViewModel.cs:458-459`. Verify visible member/nonmember, inaccessible item, old-server discovery, failures and profile changes.

### 6. Remove the invalid lifecycle-status metadata edit

The v2 metadata `status` member is metadata match status, now restricted to `pending`, `matched`, `unmatched`. WebUI removed its Continuing/Ended lifecycle selector. Native `EditMetadataDialog` still offers those values, and `MetadataEditState` seeds `status` from `ShowStatus`; a changed selector can now produce422. The request field was **not** removed. Do not replace the lifecycle selector with match-status choices merely to make it validate.

Evidence: [match-status validation correction](https://github.com/Silo-Server/silo-server/commit/8f119945ad3cf71797e7f2d271e4e82c48171d0a), `internal/apiv2/admin_catalog_item_metadata.go`, `web/src/components/EditMetadataDialog.tsx`; native `MetadataEditState.cs:33`, `EditMetadataDialog.cs:78,109,277-283`, `MediaMaintenanceApi.cs:17`. Verify Continuing/Ended/unknown series values and title-only edits; not every unchanged-field save is established to fail. This is an existing authorized metadata-edit surface, not a proposal to add server administration.

## Larger new playback feature

### 7. Native adaptive Auto quality, connection measurement and learned limits

Current upstream implements buffering-quality decisions, connection test/report/reset APIs,24-hour learned link limits, settings, telemetry and playback information. Desktop's existing stall watchdog/reopen/replan is a separate mechanism and does not implement this feature. The setting contract moves22→23 and adds `playback.buffering_quality_mode` (`never`, `ask`, `always`; server default `always`, profile/profile-device scopes).

Implement with native measurements rather than copying browser fetch counters. Preserve explicit-quality consent: upstream always asks before lowering a viewer-picked fixed quality. Preserve Watch Party behavior, suppress false throughput readings during pauses/outages/seeks, and respect captured account/profile/device ownership. Native currently sets `allow_alternate_versions=false`; supporting a lower-version Auto recovery needs a narrowly defined policy that preserves explicitly selected source/version intent, not blanket removal of that guard.

Evidence: [implemented buffering/connection feature](https://github.com/Silo-Server/silo-server/commit/fdbb887110b3394805aa3236b3caa5147cb022ed), [alternate-version repair](https://github.com/Silo-Server/silo-server/commit/8b7d405b3fe13715e8422ef957fca2965744e18d), `docs/architecture/playback-protocol-v3.md:2118-2202`, `internal/apiv2/playback_connection.go`, `web/src/player/buffering-quality.ts`, `bandwidth-probe.ts`, `media-data-meter.ts`, `web/src/pages/settings/PlaybackConnection.tsx`. Native `PlaybackManager.cs:144,390-425`, `PlaybackApi.V2.cs:53,84-126`, `DirectPlaybackRecoveryPolicy.cs`, `PlayerService.cs:2554-2585`.

This is substantial feature alignment. It does **not** establish the cause or resolution of the historic recurring desktop buffering report, and a quality fallback is not evidence that an underlying desktop transport failure was repaired.

## User-facing parity updates

### 8. Show all Server Collections

The updated All view shows complete per-library collections, with continuation/loading, accessible Load more, deduplication and failure preservation. Native `CollectionsPage.Server.cs:48` stops at `PosterColumnCount`; All lacks the selected-library See all escape. The existing aggregate first page alone cannot supply every card.

Evidence: [complete collections view](https://github.com/Silo-Server/silo-server/commit/d08e7c9a1acb9946dbb95c81f6af05682426243e), `web/src/hooks/queries/serverCollectionCards.ts`; native `CollectionsPage.Server.cs:36-63`, `CollectionsViewModel.cs:103`. Verify multiple libraries, more than20 per library, duplicate IDs, context change and partial failures.

### 9. Episode Show title filters

Support the new `show_title` text rule and `show_title_rules` capability in episode-scoped collection/catalog/Home builders; label the existing field Episode title there. Native `QueryFieldCatalog` and search capability model lack them. Preserve unknown saved rules and gate the new editor on server/scope support.

Evidence: [Show title rules](https://github.com/Silo-Server/silo-server/commit/9f5b67b21d8573c63e8421fa1e5e2ca410394b69), `internal/catalog/query_builder.go`, `web/src/components/collections/collectionBuilderFields.ts`; native `QueryFieldCatalog.cs:14`, `CatalogApi.QueryCapabilities.cs:7`. Verify six text operators, mixed/series exclusion and older servers.

### 10. Empty same-server Home layout import/export

An exported page with empty overrides now explicitly restores server rows on the same server. Native export and import planning omit such pages, losing that intent. Cross-server empty pages must remain skipped; malformed nonempty input filtered down to zero must not become a reset.

Evidence: [round-trip server defaults](https://github.com/Silo-Server/silo-server/commit/02ea8d5a2acee2253b2d439c23c2fdcd68929ff5), `web/src/lib/homeLayoutTransfer.ts`; native `HomeLayoutTransferService.cs:29`, `HomeLayoutTransfer.cs:167`. Verify preview and actual replacement counts/warnings and safe damaged-input handling.

### 11. Provider-aware Connect Apps guidance

Native ShowSilo always gives username/password instructions. Current WebUI chooses password, directory, OAuth, network or unavailable guidance using existing identity/provider/capability APIs. Existing Jellyfin guidance already honors `password_login_available=false`, but explains every false value as an external-provider account even when the server disables password login globally. Align these instructions without changing actual native login.

Evidence: [OIDC/password-free guidance](https://github.com/Silo-Server/silo-server/commit/402a3183eefd87b16c564e499830b4be177f5f95), `web/src/pages/settings/connectApps.ts`, `ConnectAppsSettings.tsx`; native `SettingsPage.Account.cs:495,516`, reusable APIs in `SettingsPage.SignIn.cs`. Verify OIDC-only, directory/network, missing public address, unavailable/disabled identity, recovery and ordinary password cases.

### 12. Existing Play History labels and actual watched duration

Native `PlayHistoryDialog` already consumes `/api/v2/admin/playback-history` from the detail/media-card action. It displays only `media_title` and labels `watched_seconds` as time watched, although that older field is ending position. Adopt new series/episode identity fields and nullable actual `played_seconds`; use the current WebUI's bounded, explicitly estimated fallback for legacy records. A new standalone admin page is unnecessary.

Evidence: [episode history labels](https://github.com/Silo-Server/silo-server/commit/22693c2373ad105cf3387ed88b2174fd898f8861), [measured duration](https://github.com/Silo-Server/silo-server/commit/039d393e04e23d4e16349e13887f8cf585145530); native `Views/Dialogs/PlayHistoryDialog.cs:37,46-47`. Verify resumed/seeked/short and legacy sessions; show series and episode code on episode rows. The initial helper audit missed this raw-JSON consumer; that disposition was corrected after direct route inspection.

### 13. Remove obsolete Send unwatched changes control

Marking unwatched now preserves watch history and no longer removes remote provider history. Native still unconditionally offers a toggle promising that removal. Respect the current false provider capability and remove/update the misleading control; do not recreate history deletion client-side.

Evidence: [preserve watch history](https://github.com/Silo-Server/silo-server/commit/965a9a610194bca698b09fa25fb4fe962034e37f); native `SettingsPage.xaml.cs:4552`. Server mark/unmark semantics apply automatically; this is a stale desktop control rather than a need to copy server database logic.

### 14. Clamp Featured hero overviews

Native XAML starts with three lines, but `HeroCarousel.xaml.cs:177` changes MaxLines to0 at widths≥640. This retains the old unlimited desktop behavior that can push a title outside the fixed hero. Current WebUI keeps three desktop/two mobile lines.

Evidence: [long-overview repair](https://github.com/Silo-Server/silo-server/commit/5060e0c5156875ba246b6940c20bf5779921a486). Verify long translated overview/title art at narrow,1080,ultrawide and4K layouts. No completed visual pass is claimed here.

### 15. Complete Dolby Vision/HDR10+ detail wording

Native recognizes the broad Dolby Vision · HDR10+ label but omits the new `DOVIWithHDR10Plus`→HDR10 compatible entry from its detailed range dictionary. Align that small display detail. The upstream first-frame HDR10+ probe/backfill is server work, and this wording change cannot certify renderer/profile support.

Evidence: [HDR10+ discovery/detail change](https://github.com/Silo-Server/silo-server/commit/e1bbbbcdde5be35d76e40fc183bb77eedaf3b2d1), `web/src/pages/ItemDetail/components/mediaSpecSections.ts`; native `ItemDetailPage.xaml.cs:3857,3876`.

## Already handled, automatic server benefits and exclusions

- Short-play movie version/audio hints reuse existing `user_data.last_file_id`/track fields; native `VersionRanking` already consumes the version hint at zero resume position. Quality cap arithmetic, stream HEAD handling, remux/tone-map/node fixes, caption charset conversion and HDR10+ probing are implemented server-side. No separate desktop renderer/worker port is established by those fixes.
- AI subtitle translation reuses a completed result on the server. Native already eventually adopts that stored subtitle; bypassing its unnecessary roughly2-second live-translation pause is an optional UX follow-up, not missing adoption. Do not count it as a mandatory new feature without a focused check.
- Server collection visibility/counts, collection storage/order/deletion cleanup, latest-on-disk episode sorting, diacritic-insensitive search, English metadata fallback and request/database performance fixes flow through existing native reads. Do not duplicate access or database filtering client-side.
- Native household stream playback already preserves `fileId`. Native Jellyfin history-import URL already trims whitespace. Browser PWA-manifest/favicon cache changes do not require copying that browser mechanism into native content-versioned favicon loading.
- Twenty commit subjects specifically target Jellyfin compatibility. Native playback uses Silo v2, so these do not require adopting Jellyfin password/PIN workarounds or replacing our player. Apple/Android push retirement APIs concern platform push registrations the desktop does not own. Administrative setup/plugin/node/scan surfaces absent from desktop remain WebUI-only; the existing Play History consumer above is deliberately included.
- New sign-in throttling/password-hash admission, Redis/rate-limit fallback, session cleanup, scan queue, artwork storage and infrastructure fixes are server responsibilities. Schema date/URI/language format declarations do not by themselves require a new native transport/model; the concrete tightened metadata-status mismatch is separately identified above.
- Upstream `174fb9a44` removes/reorganizes many tests, causing the enormous changed-file count. It is not a directive to delete desktop verification.
- The existing browser WebGPU engine is not a replacement requirement for libmpv. Its HDR-to-SDR path differs from native HDR output; neither this audit nor the previous source comparison verifies complete Dolby Vision profile/output handling or a live buffering root cause.

## API/settings inventory and evidence limits

OpenAPI operation inventory: **880→889**, nine additions, **zero removed operations**. Additions are personal-list capability/two membership reads; playback connection/read/reset and bandwidth-probe report; one admin learned-limit reset; Apple and generic mobile push retirement. New settings revision23 adds buffering mode as described in group7. An unchanged operation path does not rule out a changed semantic contract; the audio, history and status changes were inspected separately.

Local detailed evidence: `.codex-tmp/upstream-oct24/{commits.txt,changed-paths.txt,diff-stat.txt,api-route-delta.json,playback-audit.md,ui-audit.md}`. These include exact desktop/upstream locations, per-domain commit dispositions and proposed regressions. Evidence is source comparison; no native test, network benchmark, installer build or production rollout was performed during this task. Implementation needs the original-failure and relevant regression checks before any fixed/parity-complete claim.
