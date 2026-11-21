# Configure > Providers: failed-save recovery of the official WebUI

Owner decision (asked how the WebUI behaves after a failed subtitle-provider save): "that's how our app should work in that situation."
This unit makes the native subtitle provider tile recover from a failed save exactly as the official tile does, including where that
differs from what the native page did before. The page record is [Providers](admin-configure-providers.md).

Official reference: Silo Server/WebUI `a84b2551b23646380e1eecef45fdbfdbeecdfed3` (read with `git show` / `git grep` on the blobless
clone; never checked out). The page was built against `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`. Native base: `origin/beta`
`ae8c5950a` (after #224).

## 1. The official flow (a84b2551b, `web/src/pages/admin-settings/ProvidersSettings.tsx` unless noted)

| Question | Official behavior | Where |
|---|---|---|
| State | Per tile: `editor`, `loadingEditor`, `saveAttempted`, `feedback`, `enabledDraft`, `username`, `password`, `apiKey`, `loadGeneration`. | 186-195 |
| What Save does | `save()` returns when `!editor`, `saveAttempted`, loading or pending; otherwise sets `saveAttempted = true` first and sends once (`retry: false`, `retryAuthentication: false`, `If-Match` strong ETag). | 269-290; `adminSubtitleProviderConfiguration.ts` 71-79; `hooks/queries/admin/subtitles.ts` 65-79 |
| Any failed save (400, 412, 5xx, lost answer, timeout, unusable acknowledgement) | One path: `onError` shows `web.admin.settings.providers.subtitles.saveNotConfirmed` ("Save not confirmed. Your draft is retained. Reload saved configuration to review it before making another change."). It does not branch on the status. `saveAttempted` stays `true`. | 284-287; `locales/en.json` |
| What stays disabled | The enable switch (364), the whole `<fieldset>` (376: fields, Save, Test, Disconnect, because `saveAttempted`), so Save and Test cannot be pressed. Close and Reload stay enabled (431-442). The typed values stay visible, disabled ("Your draft is retained"). | 364, 376-430 |
| Reload | The button reads "Reload saved configuration (discard draft)" and calls `loadEditor()`: new `loadGeneration`, one `getProviderEditor` GET of that one provider, then `setEditor(loaded)`, `resetDrafts()` (enable draft, username, password, API key cleared), `setSaveAttempted(false)`. No list read, no page-level reload. Same for every failure kind. A superseded or authority-stale read applies nothing. | 248-267, 436-442 |
| A failed Reload | `editor` becomes `null`, `feedback` "Unable to load saved configuration. Reload before editing."; `saveAttempted` and the drafts are untouched; Reload stays reachable. | 259-263 |
| Does a successful save clear anything | It runs `resetDrafts()`, clears the test result and shows the save message; `saveAttempted` is NOT reset, so the tile also needs Reload before another save. (The list is refetched by `invalidateQueries`, but the tile keeps its old `editor` body until Reload.) | 278-283 |
| Other tiles | Every tile owns its own `saveAttempted`/`editor`; one tile's failure, Reload or draft touches no other. Only `expandedTile` and `tests` are page state. | 639-730 |
| Typed secret | Held only in the tile's `useState`; sent in the one PUT; cleared by `resetDrafts()` (Reload or success). The server never returns it. | 187-189, 219-224 |

Official tiles that do not share the pattern: MDBList (`MDBListTile`, 454-633) saves through `useUpdateServerSettings`; a failure shows
a toast, the typed key is kept and Save is enabled again as soon as the request settles (`disabled={updateSettings.isPending}`), so a
retry sends again. Marker tiles (`MarkerProviderTiles.tsx` 223-249, 480-490) send changed fields only through `useUpdateMarkerProvider`
(`onError` toast); the draft stays and Save is enabled again after the request settles (`disabled={!dirty || ... || isPending}`).
Watch-provider tiles (`WatchProviderTiles.tsx`) save nothing; their only recovery is the `Retry` of the plugin list read (87-95).

## 2. Native flow before this unit (beta `ae8c5950a`)

Code: `AdminProvidersController.SaveSubtitleAsync` / `GetSubtitleEditorAsync` / `LoadAsync` (page-wide `SubtitleConflict`,
`SubtitleOutcomeUnknown`), `AdminProvidersApi.SaveSubtitleProviderAsync` (editor consumed before the request),
`AdminSubtitleProviderDraft` (`SaveAttempted`, `RearmAfterReload`), `AdminProvidersPage.Cards.cs` `SubtitleTile` /
`ExpandSubtitleAsync`, fixture `SubtitlesAsync`.

| Failure | Shown | Enabled | What recovers it | Kept |
|---|---|---|---|---|
| 400 (and other definite 4xx) | page feedback "Provider settings were not saved: <code>" | fields, Test and Save off (`draft.SaveAttempted`); Test on | tile Reload alone (re-reads one provider, `RearmAfterReload`) | typed username, password, enable choice |
| 412 | "Provider settings changed on the server. Reload and review your draft." and the page Reload alert | same, for ALL subtitle tiles (page-wide `SubtitleConflict`) | page Reload (list read), THEN the tile Reload: two steps | typed username (password retained in the draft too) |
| 408 / 5xx / lost answer / timeout / unusable acknowledgement | "The provider save outcome is unknown. Reload and review before saving again." and the page Reload alert | same, for ALL subtitle tiles (`SubtitleOutcomeUnknown`) | page Reload, THEN the tile Reload: two steps | typed username and password |

Reading one editor never cleared another tile's flags (a property the earlier reviews established and #224's fixture repair
documented), but because the flags were page-wide a failure on one provider also blocked Save on the others.

## 3. Native flow after this unit

Controller state is per provider: `AdminProvidersState.SubtitleSaveFailures` (provider to `Refused` / `Conflict` / `OutcomeUnknown`).
`SubtitleSaveNotConfirmed(provider)` is the tile's "save not confirmed" state. `SubtitleConflict` and `SubtitleOutcomeUnknown` remain as
derived read-only properties.

| Failure | Shown | Enabled | What recovers it | Kept |
|---|---|---|---|---|
| 400, 409, other definite 4xx | tile line "Save not confirmed. Your draft is retained. ..." (same text for every kind, as official) | enable switch, fields, Test, Disconnect, Save off; Close and Reload on | the tile's own Reload, one press | typed values stay visible, disabled, until Reload |
| 412 | same | same | same; the page Reload alert also appears (as before) and its press recovers the tile too | same |
| 408 / 5xx / lost answer / timeout / unusable acknowledgement | same | same | same | same |
| After Reload | notice gone | all on; Save armed with the new editor and ETag | | nothing: username, password and the enable choice are discarded; the switch follows the server |

Reload (`AdminSubtitleProviderDraft.ResetToSavedConfiguration`) sends exactly one `GET` of that provider (no list read, no write), replaces the
editor and ETag and discards the drafts. The page Reload re-reads the page's lists and then re-reads every subtitle tile that is not
confirmed, so one press recovers the page. Reading one provider clears only that provider's state; the list read only drops entries for
providers that are no longer listed.

Safety properties kept: a save is sent once and the editor is consumed before the request; after an unknown outcome nothing is re-sent
and Save stays off until that provider has been read again; typed secrets are never logged, shown back (the password box is a
`PasswordBox`; its content is cleared by Reload) or kept after Reload, leave, sign-out or an account or server change; one provider's
read never clears another tile's state; the page Reload still recovers everything.

Case checked against the official flow: Reload after an unknown outcome. If the lost request did land, the re-read shows the server's
current state (credential configured, enable value) and a new ETag; because the drafts are discarded the admin cannot resend the old
credentials by accident, and any further save carries the new `If-Match`, so a change that lands late or elsewhere in the meantime is
refused with 412 rather than overwritten. The cost of the official flow is that the admin retypes credentials after a failed save.
If the lost request is still in flight when the tile is re-read, the read can show the old state; a later save with the old ETag then
fails with 412. Nothing is overwritten silently.

## 4. Other tiles (official comparison)

| Tile | Official failed-save recovery | Native | Outcome |
|---|---|---|---|
| MDBList | toast, typed key kept, Save available again after the request settles (retry sends again) | typed key kept; a 412 or unknown outcome keeps Save off until the page Reload; a definite refusal keeps Save on | differs officially from the subtitle pattern; native left as is |
| Marker providers | toast, draft kept, Save available again after the request settles | draft kept; a definite refusal keeps Save on; an unknown outcome keeps Save off until Reload | differs officially; native left as is |
| Watch providers | no save; plugin-list `Retry` | page Reload | no change |

No other tile shares the subtitle tile's `saveAttempted` + tile Reload pattern, so only the subtitle tile changed.

## 5. Upstream deltas since the page's pin (`217ff25abc..a84b2551b`)

| Change | Applies to | Outcome |
|---|---|---|
| `160a4425e` (#4704) delete and disconnect confirmations in the destructive style | `DisconnectButton` (MDBList and subtitle) | Done. Both confirmations use a page-local style with the official `--destructive-fill` `#dc2626`, hover fill at 90% (`#E6DC2626`), white label, system colors in a contrast theme. `DarkTheme.xaml` and the shared `DestructiveButtonStyle` are untouched. |
| `43d38e533` (#4998) whole first character in avatar initials, `5d27f225c` (#5027) upper-casing by interface language | `presentationFor` monograms of tiles | Not applicable: the native tiles show no monogram or initials. |
| `11693425e` (#4665) MDBList and other keys from the environment (`managedByEnv`), `e8de1117e` (#4498), `6b57a44be` (#4673), `managed_by_env_vars` in the sensitive-status schema | MDBList tile and its Save/Disconnect/Test | Not in this unit: environment-managed provider rows: after #234. Nothing of it was added. |
| `13dec5cc2` (#4430) connection check results carry `message_key`/`message_args` | MDBList Test result text | Not changed: the native page shows the server's English `message`, as plain text, through the shared settings API. |
| `aa06983a6` (#4696) settings pages stop saving changes the admin cannot see | `SecretField` `dirty`, Playback, Redis, Storage | Page unaffected (the Providers tiles save only what is typed). |
| `9820f17e1`, `6ddee8d39`, `5537df46a`, `d9515d397` status-line icon, long paths and URL direction in `SettingField` | shared layout | Not applicable to native controls; no URL or path is shown by this page. |
| `alert-dialog.tsx` title and description wrapping | confirmations | ContentDialog wraps by default; titles are bounded and neutralized (below). |
| Subtitle provider, marker provider and sensitive-status schemas, endpoints and `ProviderTile` | contracts | Compared schema blocks and `git log` of the handlers: no change other than the environment fields above. |

Third-party text: provider names and server messages are plain text. For confirmations the provider name goes through
`AdminProviderPresentation.DialogText`: control characters, line and paragraph separators become a space, direction controls
(`U+061C`, `U+200E/F`, `U+202A-E`, `U+2066-9`) are dropped, joiners (`U+200C/D`) that join characters are kept, and the length is bounded
(64 characters for a name, 120 for a title) without cutting a surrogate pair.

## 6. Checks whose expectation changed (reason: the owner's decision above)

Every change below is made because of the owner's decision (the official tile's recovery); no other check was weakened or removed.

Native, `AdminProvidersNativeFixture.cs`, scenario "subtitles" (the loop over 400, 503, 412):

- "<kind>: sent once and Save stays blocked until the editor is reloaded" kept in substance (sent once, Save, Test and fields disabled), now asserted for 400, 412, 503 and a lost answer.
- "the typed username is retained" after a failed save: kept as "the draft is retained and shown disabled" (the official tile shows it until Reload).
- The feedback texts "Provider settings were not saved", "outcome is unknown" and "changed on the server": replaced by the official "Save not confirmed..." text in the tile, the same for every kind.
- The two-step recovery (page Reload until `SubtitleOutcomeUnknown`/`SubtitleConflict` clear, then the tile Reload) for 503 and 412: replaced by one press of the tile's own Reload.
- "the draft survives the reload" (username kept): replaced by empty username and password boxes after Reload, the enable switch following the server, one GET of that provider, no list read and no write.
- Added: lost-answer case, "clearing the boxes was not read back as an edit", page Reload recovery with another tile open, provider-name neutralization in the confirmation, destructive fill and hover of both confirmations.

Unit, `AdminProvidersTests.cs`:

- `SubtitleReloadRebasesCanonicalStateAndRetainsTheCredentialDraft` is now `SubtitleReloadDiscardsTheDraftAndFollowsTheServerAsTheOfficialTileDoes`: it asserted that Reload kept the typed key (`RearmAfterReload`); it now asserts the draft is cleared and follows the server (`ResetToSavedConfiguration`).
- `SubtitleEditorOpenedWhileAnotherSaveIsPendingIsRefusedAndNeverSettlesItsOutcome`: its last step asserted that the page list read (`ReloadAsync`) clears the unknown outcome. The state is now per provider and only that provider's own read clears it; the step asserts the list read leaves the tile unconfirmed and the tile's read clears it. Its other assertions (busy owner, another provider's read changes nothing, one write) are unchanged.
- `SubtitleConflictRetainsDraftAndCannotReplayTheWrite` (API level) is unchanged and still passes: the draft is retained and disabled until Reload.

## 7. Deliberate differences and not verified

- A successful subtitle save keeps today's native behavior (the tile closes and the list is refreshed); the official tile stays open with
  `saveAttempted` still set until Reload. The owner's decision concerned failed saves only.
- The tile shows the official "Save not confirmed" text for every failure kind; the server's problem detail and the earlier distinct
  page messages for 400 and 5xx are no longer shown for subtitle saves.
- The page-level Reload alert still appears for a 412 or an unknown outcome (as before) and is cleared when its tiles are re-read.
- Not verified: a live Silo server, real provider accounts, a real lost or timed-out connection (the lost answer is a thrown transport
  exception in the fixture; the 30-second write timeout is covered by a unit test that throws a cancellation), Narrator reading of the new
  status line, a rendered capture of the destructive confirmation, hover with a real pointer (the visual state is driven by code), the
  contrast theme (the page-local style is skipped there and the system default button is used), and all-DPI layout. This record claims no
  visual or pixel parity.

## 8. Verification (lane `admin-completion-lane-b`, build lease `SiloOct18Build`)

- Baseline on unchanged beta `ae8c5950a`: `providersref-baseline-r1` (`publish-native`), `admin-configure-providers` 89 checks, 0 failures.
- Final run `providersref-final-r1` on commit `ab69ec1909c9fdf72710dd234fd1afd017512fb4`: focused (`FullyQualifiedName~AdminProviders`) 62 of 62 and full unit suite 10,851 of 10,851 passed, 0 failed, 0 skipped; app build, publish and native host build passed; native `admin-configure-providers` 149 checks, `admin-configure-integrations` 101, `admin-configure-general` 188, `admin-manage-foundation` 36, `admin-monitor` 113, all 0 failures. Source seal 2,740 files `d5afa97463e7cee31cc618362f61f2a6d29b1f86aa907275d6421af7b4684dde`; payload 582 files `3b65ba5bdb8c5765892248ebec7889a96d09eefcccd4015e47081dc943417592`.
- Stability, same build (`-Stage native`): `providersref-native-r1` and `providersref-native-r2`, `admin-configure-providers` 149 checks each, 0 failures.
- Development runs: `providersref-focused-r1` (62 of 62), `providersref-dev-r1` (the native host build failed on a duplicate local name in the new fixture code; nothing ran), `providersref-dev-r2` (`host-native`, 149 checks, 0 failures).
- Request bodies are unchanged: the save still sends `enabled` plus only the typed credentials, or `enabled:false` + `clear_credentials:true`, with the strong `If-Match`.
