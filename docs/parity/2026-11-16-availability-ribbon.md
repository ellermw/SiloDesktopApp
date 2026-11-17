# Series/season availability ribbon (official #2454)

## Reference

- Official repository: `https://github.com/Silo-Server/silo-server`
- Official main read: `8f93594ce19723dde8a204be21ba59a24ef06e3b` (2026-11-16). The feature commit is `2c7578324` "feat(web): show whether a series or season is complete on its poster (#2454)".
- Desktop baseline: `origin/beta` at `bdd033c1c`.
- Upstream paths read:
  - `web/src/lib/overlays/registry/ribbons.ts` (the `availability` entry, `availabilityState`, `COMPLETE_ACCENT` `#21c452`, `ATTENTION_ACCENT` `#f59e0b`)
  - `web/src/lib/overlays/types.ts`, `extractors.ts` (`availability_have`/`availability_total`; `overlayDataFromSeason`), `sample-data.ts` (show sample 2/3), `schema.ts` (`isOverlaySupportedBy`)
  - `web/src/components/overlays/CardOverlays.tsx` (accent `config.accentColor ?? getAccent(data)`, icon only for `getIcon`, `hasProgressBar`, the overlay layer is `dir="ltr"`)
  - `web/src/pages/ItemDetail/SeasonCarousel.tsx` (season cards draw the badge and pass `hasProgressBar`)
  - `internal/apiv2/catalog_types.go`, `catalog_items.go`, `web/src/api/v2/schema.ts` (`CatalogAvailability`), `internal/api/handlers/card_availability.go`, `internal/access/viewer_preferences.go`, `docs/catalog-api.md` "Series and season completion", `docs/architecture/card-overlays.md` "Compatibility".

## Upstream behavior

- API: a v2 catalog item (home/library section cards, catalog listings and search, box sets) and a v2 season (season list and season detail) can carry `availability: { have, total }`. On a series `have` is complete released seasons out of released seasons; on a season it is episodes in the library out of known episodes. It is absent for other types, specials and incomplete episode metadata. Item detail, favorites, watchlist, collections, history and Watch Tonight cards do not carry it.
- The server computes and sends it only while the acting profile's badges are on and its `ui.card_overlays` document (or the server default) enables `availability`. The badge is off by default, so most reads carry no field. A client that turns the badge on reads its cards again; the next read carries the field.
- Overlay: id `availability`, category ribbons, default position `top-left`, default off, icon capable, `introducedInManifest: 29`. No value images.
  - No badge unless both counts exist and `total > 0`.
  - `have >= total`: text "Complete" (`web.settings.overlays.badge.availability.value.complete`), check icon, `#21c452`.
  - Otherwise `have/total` (`...value.fraction`), no icon, `#f59e0b`.
  - A profile accent colour wins over the data accent. The icon follows the profile's show-icon choice or the preset default.
- A server below settings revision 29 rejects a document that holds the id, so a client offers the badge only from revision 29.
- The overlay layer lays out left to right in every language: corners are positions on the artwork, which does not mirror.

## Desktop behavior

- Models: `MediaItem.Availability` and `Season.Availability` read `availability` as the new `CatalogAvailability { Have, Total }`. The repository's snake_case naming policy needs no attribute. A missing or null field leaves the property null, which is no ribbon. `MediaItemCollectionReconciler` treats a change in the counts as a render difference, so mounted cards update.
- Registry: `OverlayRegistry.All` gains `availability` (top-left, default off). `OverlayData` gains `AvailabilityHave`/`AvailabilityTotal`, filled by `FromMediaItem` and `FromSeason`. `OverlayRegistry.AvailabilityState`, `LocalizeAvailability`, `IsAvailabilityComplete` and `DataAccent` hold the testable rules. Badge text stays the stable `Complete` or `have/total`; display text is localized at render time, so equal badges still compare equal and cards keep their XAML.
- Manifest: `IntroducedInRevision("availability") == 29`. Below 29 the entry is not offered (settings row, card rendering and saved document), a stored entry is preserved, and a save that edits it is refused the way every other newer badge is. An unreadable manifest offers it provisionally, as for the other newer badges; a save then reports an unsupported server below 29.
- Rendering: `OverlayBadgeRenderer` takes the data accent when the profile chose none, shows the check glyph only for a complete title and only when the icon is on, and localizes the text through the shipped keys.
- Settings: Settings → Card overlays → Ribbons lists Availability with the shipped label and description keys. The preview's show sample is 2/3.
- Season cards (series detail): `ItemDetailPage.UpdateSeasonPosterState` draws the ribbon from the season list's own `availability` field, with no extra request, and lifts bottom corners clear of the season progress bar (`CardOverlayGeometry.ProgressClearance`). Top corners never move. Poster and library cards use the existing `CardBadgeHosts.For` path, which already limits each corner and lifts bottom corners for their own bars.
- RTL: `CardBadgeHosts` now lays its hosts and layout panels out left to right inside a right-to-left page, so `top-left` is the left corner of the artwork in every language, as upstream. This applies to every overlay badge, not only availability.
- Performance: no new network reads. The field rides on the existing catalog and season reads.

## Files changed

- `src/SiloPlayer.Core/Models/Home/HomeSectionsResponse.cs` (`CatalogAvailability`, `MediaItem.Availability`)
- `src/SiloPlayer.Core/Models/Catalog/SeasonsResponse.cs` (`Season.Availability`)
- `src/SiloPlayer.Core/Services/MediaItemCollectionReconciler.cs`
- `src/SiloPlayer.Core/Services/CardOverlayGeometry.cs` (`ProgressBarClearance`, `ProgressClearance`)
- `src/SiloPlayer/Services/CardOverlayService.cs` (registry entry, revision 29, availability rules)
- `src/SiloPlayer/Controls/OverlayBadgeRenderer.cs` (accent, glyph, localized text)
- `src/SiloPlayer/Controls/CardBadges.cs` (left-to-right hosts)
- `src/SiloPlayer/Views/SettingsPage.xaml.cs` (Ribbons group, offered rule, label, description, preview sample)
- `src/SiloPlayer/Views/ItemDetailPage.xaml.cs` (season card ribbon)
- `tests/SiloPlayer.Tests/CardOverlayAvailabilityTests.cs`

## Verification

Local results on this source:

- **Unit tests:** all 7,530 pass, including `CardOverlayAvailabilityTests`.
- **Native, passing:** `overlay-revisions` (50 checks), `advanced-overlays`
  (468 checks), `overlay-pill` and `home-row-window`.
- **`media-localization`:** passed twice on rerun. Its first run on this
  source hit an intermittent German Add to Collection dialog failure, which
  also showed up once on an unrelated change and does not involve card
  overlays.

Not certified:

- a live server at settings revision 29 or newer: Complete, partial, a season
  card with a progress bar, and a bottom corner beside the bar;
- RTL placement on real cards in Arabic or Hebrew;
- a server below revision 29.

Known limitation: after the badge is turned on, already-mounted cards update
on their next read, because the server sends the field only once the saved
setting is on.
