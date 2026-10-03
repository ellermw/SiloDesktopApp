# Settings and integrations delta — September 30, 2026

Compared official Silo Server Git objects from `ad899be9d4fd9f33d4b9e9ac6873166026661c6d`
to freshly fetched `8e2e840474a085c6df6571a5a2850f7eb996810c`. Desktop comparison
uses the uncommitted 1.1.106 candidate at
`C:\Users\Michael\.codex\worktrees\parity-top-three\SiloPlayer`, whose first three
September 28 packages have recorded automated/native verification. Released main
remains 1.1.105. This is source inspection, not live server or installed testing.

## Home customization needs expansion

- Upstream adds profile Home/library layout JSON export/import, including
  `home.hide_watched_items`, same-server references, cross-server library matching,
  capability/permission filtering and import previews. It has a versioned
  `silo-home-layout` format and a 5 MiB limit. Sources:
  [transfer model](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/lib/homeLayoutTransfer.ts),
  [transfer UI](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/components/sections/HomeLayoutTransfer.tsx).
- Desktop `SettingsViewModel.SaveHomeSectionsAsync` and `SettingsApi` offer
  existing override read/save/reset; source inspection found no layout transfer
  model or reachable export/import controls. Add this to previous package 11;
  it is not an administration feature.
- Current upstream preserves override IDs and gives new admin-section overrides
  stable IDs. It avoids rewriting an unchanged legacy Trakt section, which the
  source policy can reject. Desktop currently creates new admin overrides with
  `Id = null`, serializes every visible row, and does not special-case this
  legacy source. Hiding a legacy section or saving unrelated changes can therefore
  produce a refused payload. This is a source-confirmed mismatch, not an installed
  reproduction. Verify IDs, allowed hides, held positions and save failures as
  part of Home editing. Sources:
  [save builder](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/pages/settings/HomeScreenSettings.tsx),
  [source policy helper](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/lib/sectionTypes.ts),
  desktop `src/SiloPlayer/ViewModels/SettingsViewModel.cs:1475`.

## New settings require capability-aware badge preservation

- Settings manifest advances from **revision 12 to 15**. Revision 13 adds
  `advisory_age`; revision 14 adds subtitle `textOpacity` (1–100, default 100);
  revision 15 adds `request_status` and profile
  `requests.watchlist_auto_request`. New badges must not be offered/saved against
  older unsupported manifests. The automatic-request setting is shown only
  when request status allows it. Source:
  [manifest](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/contracts/settings/v1/manifest.json).
- Desktop `CardOverlayService.OverlayRegistry` has neither badge.
  `NormalizeDocument` retains only registry-known IDs, and `SerializePrefs`
  serializes that registry. Editing another badge can discard a stored new badge.
  Add preservation and version gating before expanding the UI. Wire advisory
  data to the profile/advisory-age package and request status to the new external
  watchlist/request package. Sources:
  [upstream support filtering](https://github.com/Silo-Server/silo-server/blob/8e2e840474a085c6df6571a5a2850f7eb996810c/web/src/hooks/useOverlayPrefs.ts),
  desktop `src/SiloPlayer/Services/CardOverlayService.cs:108`, `:390`, `:413`.

## Trakt changes are primarily server-owned

Token rotation serialization, migration-era invalid credentials, identifying
User-Agent, minute-resolution history matching and paginated-read retry belong
to the server. Desktop already uses Silo's watch-provider API, exposes connection
errors and supports reconnect/device authorization. Do not build a second Trakt
token manager in desktop. Verify stale-credential/reconnect presentation alongside
the older ratings/dropped-show gaps. Sources:
[Trakt update](https://github.com/Silo-Server/silo-server/commit/abc752bdbea00e97a92a1f13ab1f139d04056c01),
[paginated-read retry](https://github.com/Silo-Server/silo-server/commit/8e2e840474a085c6df6571a5a2850f7eb996810c),
desktop `src/SiloPlayer.Core/Api/WatchProvidersApi.cs`,
`src/SiloPlayer.Core/Models/WatchProviders/WatchProviderModels.cs`,
`src/SiloPlayer/Views/SettingsPage.xaml.cs:4082`.

## Completed appearance package remains implemented

No changes in this delta to `internal/apiv2/branding.go`,
`web/src/contexts/CustomThemeProvider.tsx` or the vendored Foliate renderer.
The first three candidate packages must not be relabeled absent because main is
still 1.1.105. Their recorded live/hardware acceptance boundaries remain. Retired
profile theme editing does not return; new poster badges are a different surface.

No product code, server settings, credentials or running application were changed.
