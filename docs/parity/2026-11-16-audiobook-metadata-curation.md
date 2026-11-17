# Audiobook metadata curation

## References and base

- Official Silo Server [#4026](https://github.com/Silo-Server/silo-server/pull/4026)
  (`b681339d5`), checked against official main `8f93594ce`. Sources: the WebUI's
  `EditMetadataDialog.form.ts`, `EditMetadataDialog.tsx`,
  `MatchItemDialog.tsx`, `MediaItemMenu.tsx`, `lib/permissions.ts`,
  `ItemDetail/AudiobookContent.tsx` and `hooks/queries/items.ts`.
- Desktop base: Beta `3444a6e0c`. It already includes Edit Metadata ownership
  (#190) and the Books audiobook detail layout (#182), whose curator menu
  this change builds on. Version numbers are left to the pipeline.

## Behavior

Metadata actions require an acting admin or the explicit `metadata_curation`
permission. A selected profile must resolve first, including for explicitly
granted curators. Non-primary admin profiles retain explicit grants only;
profileless accounts retain the existing server-checked permission policy.

For an authorized curator:

| Type | Card Edit | Detail Edit | Match | Refresh |
| --- | --- | --- | --- | --- |
| movie, series, audiobook | Yes | Yes | Yes | Yes |
| season, episode | No | Yes | No | Yes |
| ebook, manga | No | No | No | Yes |
| box_set | No | No | No | No |

The aggregate box-set exclusion is existing desktop behavior. The audiobook
detail More button is now reachable even without playable files and contains
Edit Metadata, Match Item and Refresh Metadata, in that order. It remains hidden
for listeners. The compact TV menu does not inject unrelated favorite actions
into this audiobook curation menu.

The audiobook editor uses these fields:

- General: title, sort title, original title, overview, tagline, content rating.
- Dates & Ratings: year and release date. No video rating or runtime inputs.
- Tags & Genres: genres, studios and countries, with the existing tag-entry UI.
  These are the upstream tag section's fields; there is no new `tags` API property.

Edits use the same lock groups as upstream movies (title 0, overview/tagline 1,
genres 2, studios 3, countries 8, content rating 9, year/release date 13), preserving
server locks outside the edited groups. Clearing a release date sends null.
Audiobooks have no External IDs or Images section and no Reset to Provider.
Reset submission is also guarded by type; hidden controls do not read image,
reset-capability or metadata-AI endpoints. Translation stays on video editors.

Match reuses the existing non-video dialog and transport contract: title/year,
optional library scope, and editable generic `provider_ids` rows (for example
`isbn`). Provider names are trimmed/lowercased and values trimmed. Video IDs are
omitted. Search posts to `/api/v2/admin/items/{id}/match/search` with limit 500;
Apply posts the selected candidate's `provider_ids` and the same scope to
`/api/v2/admin/items/{id}/match/apply`. V2 library IDs serialize as strings.
Existing candidate-scope guards and replacement-content-ID handling are reused.
The audiobook cover changes through Match, as upstream specifies.

All newly used labels already exist in the shipped `Generated/en.json`.
Existing AppStrings/Localized bindings are reused, including
`web.detail.hero.typeAudiobook`; no generated localization catalog was changed.

## Files changed

- `src/SiloPlayer.Core/Services/AuthorizationPolicy.cs`: resolved-profile curation gate.
- `src/SiloPlayer.Core/Services/ItemMaintenanceActionPolicy.cs`: shared card/detail Edit matrix and audiobook Match.
- `src/SiloPlayer.Core/Services/MetadataEditorFormPolicy.cs`: visible editor fields and image/reset eligibility.
- `src/SiloPlayer.Core/Services/MetadataEditState.cs`: audiobook field locks.
- `src/SiloPlayer/Controls/MediaItemMenu.cs`: restrict card Edit to the upstream types.
- `src/SiloPlayer/Views/ItemDetailPage.xaml.cs`: audiobook Edit and reachable curation-only More menu.
- `src/SiloPlayer/Views/Dialogs/EditMetadataDialog.cs`: consume the field policy, hide unsupported controls, guard Reset/translation.
- `src/SiloPlayer/Views/Dialogs/EditMetadataDialog.Localization.cs`: audiobook type badge key.
- `tests/SiloPlayer.Tests/AudiobookMetadataCurationTests.cs`: type/role/profile matrix, field set, hidden controls, locks/diffs and surface-policy wiring.
- `tests/SiloPlayer.Tests/ItemMaintenanceActionPolicyTests.cs`: audiobook Match and ebook refresh-only case.
- `tests/SiloPlayer.Tests/MediaMaintenanceApiTests.cs`: audiobook provider search/apply, library scope and replacement identity.
- `tests/SiloPlayer.NativeRegressionTests/ConditionalEditorsNativeFixture.cs`: audiobook form/hidden-control inspection, owned Save payload, real generic-provider Match dialog flow and planned captures.
- This parity note.

## Audiobook detail page after #182

The redesigned audiobook detail page has its own curator menu
(`BuildAudiobookMoreFlyout`: Edit, Match, Refresh). The generic `MoreButton` stays
hidden there, so this change adds no audiobook entries to the generic menu.
Edit Metadata in both menus now opens through one method,
`OpenFreshEditMetadataAsync`. It is #190's fresh detail read with its
navigation, identity, authority and pending-write guards, so audiobook edits
open on current data like the other item types.

## Verification

Local results on this source:

- **Unit tests:** all 8,011 pass. They include `AudiobookMetadataCurationTests`,
  `ItemMaintenanceActionPolicyTests` and #190's
  `EditMetadataOwnershipContractTests`, now pointed at the shared method.
- **Native, passing:**
  - `conditional-editors` (24 checks, including the audiobook form, hidden
    controls and provider search/match);
  - `profiles-metadata` (11);
  - `audiobook-detail-interactions`;
  - `book-detail-parity`.
- **Already failing on Beta:** `household-contracts` (9 passes, then
  `CuratorReset` clicks an element without a XamlRoot at
  `HouseholdContractsNativeFixture.cs:119`) fails identically on clean Beta
  `3444a6e0c`. This change does not cause it.

Not certified: a real server's audiobook Match providers, and live curator
permissions.
