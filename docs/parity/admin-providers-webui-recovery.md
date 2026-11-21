# Configure > Providers: failed-save recovery of the official WebUI

Owner decision (asked how the WebUI behaves after a failed subtitle-provider save): "that's how our app should work in that situation."
This unit makes the native subtitle provider tile recover from a failed save exactly as the official tile does, including where that
differs from what the native page did before. The page record is [Providers](admin-configure-providers.md).
A review of the first version of this unit (commit `ab69ec190`) found that the Save path still read the page-wide flags, so a second tile's Save
and Disconnect did nothing; the corrections are in section 9 and the sections below describe the state after them.

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

Controller state is per provider: `AdminProvidersState.SubtitleSaveFailures` (provider to `Refused` / `Conflict` / `OutcomeUnknown`),
`SubtitleLoadFailures` (a read of that provider failed), `SubtitleSaveMessages` (the official success line of that provider) and
`ReadingProvider` (the provider whose editor is being read; reading is not saving). `SubtitleSaveNotConfirmed(provider)` is the tile's
"save not confirmed" state. `SubtitleConflict` and `SubtitleOutcomeUnknown` remain as derived read-only properties across all providers;
see section 9 for every remaining read of them.

| Event | Shown in the tile | Enabled | What recovers it | Kept |
|---|---|---|---|---|
| 400, 409, other definite 4xx | "Save not confirmed. Your draft is retained. ..." (same text for every kind, as official) | enable switch, fields, Test, Disconnect, Save off; Close and Reload on | the tile's own Reload, one press | typed values stay visible, disabled, until Reload |
| 412 | same | same | same; the page-level Reload alert also appears (a native addition) and its press recovers the tile too | same |
| 408 / 5xx / lost answer / timeout / unusable acknowledgement | same | same | same | same |
| Successful save | the official success line ("Settings saved and applied on this server. ...", or the different-revision or not-applied line) | the tile **stays open**, everything off as after a failure, until Reload | the tile's own Reload | typed credentials are cleared; the switch shows the value that was saved |
| Reload that fails (5xx, lost answer) | "Unable to load saved configuration. Reload before editing." (replaces every other line, as the official single feedback string) | the editor is dropped, so the whole tile is off; Reload stays on | Reload again | the typed values stay visible and disabled, as official (only a successful read resets them); the not-confirmed state of that provider is unchanged |
| First read of a never-opened tile fails | the same text, the official Reload label and Close; no fields | Reload, Close | Reload | nothing was typed |
| While a read is pending | "Loading saved configuration..."; Save reads "Save", never "Saving…" | everything off | | |
| After a successful Reload | notice gone | all on; Save armed with the new editor and ETag | | nothing: username, password and the enable choice are discarded; the switch follows the server |

Reload (`AdminSubtitleProviderDraft.ResetToSavedConfiguration`) sends exactly one `GET` of that provider (no list read, no write), replaces the
editor and ETag and discards the drafts. The page Reload re-reads the page's lists and then every opened subtitle tile that waits for a read
(not confirmed, could not be loaded, or saved), in list order, so one press recovers the page. The first failed read of that loop stays on
the page line (the next read starts by clearing it) and each failure stays in its own tile. Reading one provider clears only that provider's
state; the list read only drops entries for providers that are no longer listed. The marker tile's Reload is unchanged from before this unit:
it reads the page lists only and never touches a subtitle tile.

Safety properties kept: a save is sent once and the editor is consumed before the request; after an unknown outcome nothing is re-sent
and Save stays off until that provider has been read again; a tile that was saved keeps its spent ETag off until Reload; typed secrets are
never logged, shown back (the password box is a `PasswordBox`; its content is cleared by Reload or by a successful save) or kept after
Reload, leave, sign-out or an account or server change; one provider's read never clears another tile's state; the page Reload still recovers
everything; every handler ignores a control value equal to the draft, so a programmatic set is never read back as an edit.

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
- Corrections (section 9): "saved and collapsed" became "stays open on the official success line, off until Reload"; the page text "Provider settings saved and applied." became the three official success lines; "the enable switch shows the server's value" now turns the switch off before the failed save; the destructive-style check covers pressed and disabled; the polling `Task.Delay` after a synchronous `GoToState` was replaced by a bounded wait.

Unit, `AdminProvidersTests.cs`:

- `SubtitleReloadRebasesCanonicalStateAndRetainsTheCredentialDraft` is now `SubtitleReloadDiscardsTheDraftAndFollowsTheServerAsTheOfficialTileDoes`: it asserted that Reload kept the typed key (`RearmAfterReload`); it now asserts the draft is cleared and follows the server (`ResetToSavedConfiguration`).
- `SubtitleEditorOpenedWhileAnotherSaveIsPendingIsRefusedAndNeverSettlesItsOutcome`: its last step asserted that the page list read (`ReloadAsync`) clears the unknown outcome. The state is now per provider and only that provider's own read clears it; the step asserts the list read leaves the tile unconfirmed and the tile's read clears it. Its other assertions (busy owner, another provider's read changes nothing, one write) are unchanged.
- `SubtitleConflictRetainsDraftAndCannotReplayTheWrite` (API level) is unchanged and still passes: the draft is retained and disabled until Reload.

## 7. What remains different from the official tile, and not verified

- The page-level Reload button and its alert (shown after a 412 or an unknown outcome, or when a section failed to load) are native
  additions: the official page has no page-level reload. The first failed read of a page Reload is also written to the page line.
- The tile shows the official "Save not confirmed" text for every failure kind; the server's problem detail and the earlier distinct
  page messages for 400 and 5xx are no longer shown for subtitle saves.
- After a successful save the native header pill, the password placeholder and the Disconnect button follow the refreshed provider list,
  and the enable switch shows the value that was saved. The official tile keeps its old editor body, so its switch and Disconnect still show
  the pre-save values until Reload. Native is the more truthful display; it is a difference.
- A failed read keeps the typed values in memory exactly as official does; in the "could not be loaded" state of a tile that already had
  a draft they are drawn disabled, and in the state of a tile that never loaded no fields are drawn at all (the official tile draws empty
  disabled fields).
- A switch edit clears the displayed connection-test result here; the official tile clears it only when a credential field is edited.
- Not verified: a live Silo server, real provider accounts, a real lost or timed-out connection (the lost answer is a thrown transport
  exception in the fixture; the 30-second write timeout is covered by a unit test that throws a cancellation), Narrator reading of the new
  status lines, a rendered capture of the destructive confirmation, hover with a real pointer (the visual states are driven by code),
  the contrast theme (the page-local style is skipped there and the system default button is used), and all-DPI layout. This record claims no
  visual or pixel parity.

## 8. Verification of the first version (lane `admin-completion-lane-b`, build lease `SiloOct18Build`)

- Baseline on unchanged beta `ae8c5950a`: `providersref-baseline-r1` (`publish-native`), `admin-configure-providers` 89 checks, 0 failures.
- Final run `providersref-final-r1` on commit `ab69ec1909c9fdf72710dd234fd1afd017512fb4`: focused 62 of 62, full unit suite 10,851 of 10,851,
  native `admin-configure-providers` 149 checks, `admin-configure-integrations` 101, `admin-configure-general` 188, `admin-manage-foundation` 36,
  `admin-monitor` 113, all 0 failures; stability `providersref-native-r1`/`-r2`, 149 checks each.
- Request bodies are unchanged: the save still sends `enabled` plus only the typed credentials, or `enabled:false` + `clear_credentials:true`, with the strong `If-Match`.

## 9. Review corrections (commit `42cc0a11a`)

| # | Finding | Fix | Guard (fails without the fix) |
|---|---|---|---|
| 1 | P1: `SaveSubtitleAsync` returned early on the page-wide `SubtitleConflict`/`SubtitleOutcomeUnknown`, so a second tile's Save and Disconnect (drawn enabled) did nothing and said nothing | The guard reads this provider only (`SubtitleSaveNotConfirmed(editor.Provider)`); every early return now says why through `controller.NotifyProvider` | native "subtitle second tile after a conflict / a server error / a lost answer" (Save of B: one PUT, to B, success line; Disconnect of C: one PUT); unit `ThePageNeverReadsThePageWideSubtitleFlagsAndEveryRefusedClickSaysWhy`, `AnotherProvidersFailedSaveNeverBlocksThisProvidersSaveOrDisconnect`; native `ClickActs` check that an enabled control sends a request or shows a message |
| 2 | P2: a failed tile Reload kept the old editor and left the tile editable | A failed read marks the provider in `SubtitleLoadFailures` and the page drops that tile's editor; official text "Unable to load saved configuration. Reload before editing." and official Reload label; typed values kept (the official tile does not discard them on a failed load: only a successful read resets them) | unit `AFailedSubtitleReadMarksOnlyThatTileNotLoadedAndALaterReadNeverClearsIt` (503, lost answer); native "subtitle reload failure" (503 and lost answer, with and without a prior failed save, first-open failure) |
| 3 | P2: the switch check could not fail | the switch is turned off before the failed save and must be back on after Reload; the reverse for a provider that is off | native "subtitles" loop and "subtitle reload failure" (SubDL) |
| 4 | P3: page Reload loop hid a failed read | each failure is per tile; the first failed read of the loop is written back to the page line after the loop | native "subtitle page reload failure"; unit (a later read, and the list read, never clear another tile's failure) |
| 5 | P3: marker Reload also re-read subtitle tiles | the marker Reload is `ReloadAsync(preserveDraft: true)` again; only the page-level Reload re-reads tiles | unit `TheMarkerTileReloadReadsTheMarkerScopeOnlyAndNeverTheSubtitleTiles` |
| 6 | P3: "Saving…" during a Reload | `ReadingProvider` distinguishes a read; the tile shows "Loading saved configuration..." | unit `ReadingAProviderIsNeverReportedAsSavingIt`; native check while a read is gated |
| 7 | P3: test result cleared on every save attempt | cleared only after a successful save, and only for that provider | unit `ASubtitleTestResultSurvivesAFailedSaveAndIsClearedOnlyByASuccessfulOne`; native |
| 8 | P3: success closed the tile | the tile stays open on the official success line, drafts cleared (`ResetAfterSave`), off until Reload. No safety conflict: the editor's ETag is spent and `SaveAttempted` stays set, which is safer than reusing it | unit `ASuccessfulSubtitleSaveKeepsTheOfficialMessageUntilTheProviderIsReadAgain` (4 acknowledgements), `ASavedDraftIsClearedKeepsTheSavedSwitchAndStaysOffUntilReloaded`; native "subtitles" and the second-tile scenarios |
| 9a | `OnAuthorityChanged` clears the page's editors but not the controller | Not reachable. The controller's lifetime subscribes to the authority first and its `Retired` handler runs `Clear()`, which replaces the state with a new one (empty failures, load failures and messages); a late update from a retired lease is refused by `Update`. No fix needed | unit `AnAuthorityChangeRetiresEveryPerProviderState` |
| 9b | A read that completes across an authority change still returned its editor | `GetSubtitleEditorAsync` returns null when its lease is no longer current; the page also refuses to store an editor when the controller has no current owner; should a stale editor reach Save anyway, `SaveSubtitleProviderAsync` still refuses it before anything is sent and the tile is now left "save not confirmed" with the notice | unit `AReadThatFinishesAfterTheAuthorityChangedReturnsNoEditorAndSetsNothing`, `AnEditorFromAnOlderAuthorityLeavesTheTileNotConfirmedAndSendsNothing` |
| 10 | tests: `Task.Delay(60)` after `GoToState`; `Task.Delay(300)` negative checks; destructive style only hover | bounded polling; request/write counts before and after both waits; pressed and disabled states asserted | native |

Remaining reads of the page-wide flags: `SubtitleConflict` and `SubtitleOutcomeUnknown` are read only inside `AdminProvidersState.ReloadRequired`
(`AdminProvidersController.cs`), and `ReloadRequired` is read only by `AdminProvidersPage.Render` to show the page-level Reload alert. That use is
genuinely page-wide (any tile's conflict or unknown outcome keeps a Reload reachable) and no action is gated by it. No click handler reads either.

Page rule from now on: a control that is drawn enabled must act or say why it did not. Unit test `ThePageNeverReadsThePageWideSubtitleFlagsAndEveryRefusedClickSaysWhy`
fails on a bare early return in the subtitle Save handler; the native fixture's `ClickActs` fails when an enabled control produces neither a request
nor a message. The MDBList and marker click handlers were read for the same pattern: their Save controls are drawn enabled only when their handler's
own guards pass, and were not changed.

## 10. Verification of the corrections (same lane)

- Mutation: `providersfix-mutation-r1` (`publish-native`) with only the Save guard reverted to the old page-wide test (a silent `return`):
  native `admin-configure-providers` failed: "412 conflict: SubDL Save: the enabled control acted or said why" and "Timed out: 412 conflict: SubDL saved". The line was restored; `git diff` was empty before the final run.
- Final run `providersfix-final-r1` on commit `42cc0a11a80ba27ebd69187bd1feadba2d531055`: focused (`FullyQualifiedName~AdminProviders`) 79 of 79 and full unit suite
  10,868 of 10,868 passed, 0 failed, 0 skipped; app build, publish and native host build passed; native `admin-configure-providers` 251 checks,
  `admin-configure-integrations` 101, `admin-configure-general` 188, `admin-manage-foundation` 36, `admin-monitor` 113, all 0 failures.
  Source seal 2,740 files `d4b0355c435d8ab52ab2c463b7be151c88dc396101358b227d2e12755612e8ac`; payload 582 files `13777204f602a3b8ce56565d75f1cf42e723ba0397f8dc2e22ca3246046c1397`.
- Stability, same build (`-Stage native`): `providersfix-native-r1` and `providersfix-native-r2`, `admin-configure-providers` 251 checks each, 0 failures.
- Development runs: `providersfix-focused-r1` (79 of 79), `providersfix-dev-r1` (`publish-native`, 251 checks, 0 failures).
- Request bodies and URLs are unchanged; `AdminIntegrationsPage*.cs` was not touched.
