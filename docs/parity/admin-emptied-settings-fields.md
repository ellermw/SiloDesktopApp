# Admin settings: emptied fields and the text model check (official #4816 and #5082)

Scope: native Admin settings pages against silo-server `ca51b57f0` (#4816, "stop emptied settings fields from staging values they don't show") and `10e67d836` (#5082, "say why the text model check failed"), both contained in the pinned official source `c90e406db71ee468bffdfa5733cd97d1d50b4019` (read-only checkout `D:/SiloPlayer-work/server-upstream-nov14`). Branch `t3/admin-emptied-settings-fields`, started from `origin/beta` `e21546981`.

Native counterparts: the shared form is `AdminSettingsForm` (`src/SiloPlayer.Core/Services/Admin/AdminSettingsForm.cs`); each page's controller owns one form. The native secret field is each page's masked box plus its Clear/Keep action, routed through the controller's secret setter. Official `SecretField` has no single native control, so its rule lives in `AdminSettingsForm.SetSecret`.

## The six rules

### 1. Erasing an edit goes back to what is saved (official `useSettingsForm.ts` `savedValue`, lines 361-377 at the pin)

- Ordinary settings: **already matched.** `AdminSettingsForm` keeps a baseline entry for every non-secret key (a key the snapshot leaves out is saved empty), and `Set` removes the draft when it equals it (`AdminSettingsForm.cs`, constructor and `Set`). Pinned by `AdminSettingsFormTests.MissingOrdinaryValueEqualsEmptyAndSecretIntentIsRedacted` and, new, `AdminEmptiedSettingsFieldsTests.ReleasesAnEditChangedBackToItsSavedValueButNotAStagedSecretClear` (official "releases an edit changed back to its saved value, but not a staged secret clear").
- Secrets: **changed.** Before, a secret had no baseline at all, so an emptied secret with nothing saved stayed a staged empty value that Save would send. Now `ApplyConfigured` records the sensitive-status `configured` list (every controller that has secrets calls it after each status read, with `null` when the read failed) and `SavedValue` treats a secret as saved empty unless the server reports it set. For a set secret, and before the status has arrived, the saved value is unknown and an empty draft stays a staged clear, as official. An acknowledged save updates the set (`Acknowledge`). A form never told the status (Backups' S3 form, Library) behaves as before.
- Tests (fail without the change): `ReleasesAnEdit...`, `WhileTheSecretStatusHasNotArrived...`, `AnAcknowledgedSaveUpdatesWhichSecretsAreSet`, `ATypedThenErasedPasswordWithNothingSavedStagesNothingAndSendsNothing`, `ATypedThenErasedRedisUrlWithNothingSavedLeavesNothingStaged`, `AnEmptiedNumberWithNothingSavedLeavesNothingStaged`; native: General, Email, Notifications, Integrations, Search and Storage fixtures (see "Native checks").

### 2. A secret field returns to the saved state whenever it is emptied (official `SecretField.tsx` `handleChange`)

- **Changed** in `AdminSettingsForm.SetSecret` (typing stages a replacement; empty resets). Used by Email (`SetPassword`), Search (`SetApiKey`), Storage (`SetSecret`), Notifications (`Set` for a secret key). General's URL box now calls `KeepSavedRedisUrl` for any emptied value. Before, these returned to the saved state only when a saved secret was reported.
- **Already matched:** Providers (`AdminProvidersController.SetMdblistApiKey` resets on any blank value).
- **Not changed (file owned by another open pull request):** the Backups restore flow keeps its own S3 form; it never calls `ApplyConfigured`, so it behaves as before.
- Tests: `EmptyingASecretWithNothingSavedGoesBackToNothingStaged` (official "goes back to nothing staged when an input with nothing saved is emptied"), `EmptyingAReplacementTypedOverASavedSecretKeepsTheSavedSecret` (official "delegates emptying the input to onKeep ..."), `AStagedClearIsExplicitAndTypingOverItStagesAReplacement` (official "says a staged clear will be saved ..." and "stages a replacement when the admin types over a staged clear"), `OnlyTheExplicitClearOfASetPasswordIsStagedAndSentAsAnEmptyValue`.

### 3. Text and number fields no longer show a default once emptied

- **Changed:** AI Services tuning numbers (`AdminIntegrationsPage.Settings.cs` `Defaults`), Notifications numbers (`AdminNotificationSettingsController.Display`), Search index name, timeout, batch size and semantic ratio (`AdminSearchSettingsRules.Default`/`Display`, page placeholder), Storage token parameter and lifetime (`AdminStorageSettingsPage.DisplayValue`; `DisplayTokenParam`/`DisplayTokenTtl` removed). Switches and choices keep their displayed default.
- **Already matched:** Playback trickplay node capacity (`AdminPlaybackSettingsPanel.cs:210` shows the raw value).
- **Not changed (file owned by another open pull request):** Library metadata "sync node capacity" still shows `1` when empty (`AdminLibrarySettingsPage.cs:330`). Needs the same one-line change there.
- Native AI Services never had the legacy `subtitle_ai.max_concurrent_jobs` fallback that the official commit removed.
- Tests: `AnEmptyNumberShowsNothingInsteadOfADefault`, `ASwitchOrAChoiceStillShowsItsDefault`, updated `BlankSavedValuesDisplayTheOfficialDefaultsForChoicesAndSwitchesButNothingForTextAndNumberFields`; native fixtures assert empty boxes.

### 4. General: "Use Redis" resets on Save and Discard (official `GeneralSettings.tsx`, `RedisGroup.tsx`)

- **Changed** in `AdminGeneralSettingsPage.cs`: the switch's own choice ends only after an acknowledged save (`AdminGeneralSettingsController.SaveAsync` now returns whether the server acknowledged) and on Discard once the settings have loaded and no save is in flight. It no longer resets whenever the dirty count reaches zero. Also ported from the same commit: switching on with a URL saved clears the override, switching off with none saved clears it, and a staged Redis field keeps the Redis fields in view.
- Official resets after any completed `form.save()` including one with nothing to send; native resets only after an acknowledged write (a nothing-to-save Save is not offered).
- Native checks: General fixture (type and erase the URL, the database number, another setting; Save; Discard; URL saved/removed elsewhere; staged number kept in view).

### 5. AI Services validates only the values a save sends (official `AISettings.tsx` `save`)

- **Changed:** `AdminIntegrationPolicy.ValidateAi` checks a tuning value only when it is dirty, with the official messages (en.json `web.admin.settings.ai.page.*Invalid`). Before, all five were checked, with a fallback default for an empty one, so an untouched stored value a client check refuses blocked unrelated saves.
- "Settings missing" clause: **already matched.** `AdminIntegrationsPage.SaveSettingsAsync` returns while the page is not `Ready`; there is no editor without a loaded snapshot.
- Tests: `AnUntouchedTuningFieldIsNotValidated`, `ATuningValueTheSaveSendsIsCheckedWithTheOfficialMessage`, `AValidTuningValueTheSaveSendsPasses`; native Integrations fixture.

### 6. The save baseline is released whenever the staged set ends up empty

- **Already matches, by construction.** Official pins the snapshot at the first edit (`editBaseline`) and the bug was that an undo in place did not release it. The native form has no pinned baseline: `CaptureSave` guards the write with the form's current ETag, which `Reload` and `Acknowledge` always advance, so nothing stale can survive an undone edit. Evidence: `AdminSettingsForm.cs` `CaptureSave`/`Acknowledge`/`Reload`.
- Tests that pin it: `GuardsTheNextSaveWithTheCurrentSnapshotAfterAnEditIsUndoneInPlace` (official "guards the next save with the current snapshot after an edit is undone in place"), `AnEditUndoneInPlaceDoesNotLeaveTheNextSaveGuardedByAnOldSnapshot` (fixture server enforces If-Match), `ASaveOverASettingsChangeThePageNeverReadIsRefusedAsAConflict`, and the General and Email native fixtures (server answers 412 to a write not made over its current version).
- Native has no background refresh, so a change another admin makes that the page never read is still refused with a conflict (the official page would have re-read it silently). That is unchanged.

## Request bodies

All changes are to `PUT /api/v2/admin/settings`; no new key is ever sent.

- Fewer: an emptied secret with nothing saved no longer sends `"<secret>": ""` (Email password, Search API key, Storage credentials, Discord credentials), and an emptied Redis URL with none saved no longer sends `redis.url` and the paired `redis.db`.
- More saves allowed, not more keys: AI Services no longer refuses a save over an untouched out-of-range tuning value, and that value is not sent.
- Different in one case: a text or number field whose saved value is empty, where the admin types exactly the value that used to be its displayed default (Notifications, Search, Storage), is now a real edit and is sent; before it was swallowed as "no edit".
- Unchanged: the text model check request (`values`, `dirty_keys`); the explicit Clear still sends `""`.

## Text model check (#5082)

The server answers `{ success, message, message_key?, message_args? }`. `AdminIntegrationsApi.CheckModelAsync` now reads the key and arguments and `AdminConnectionCheckText.Resolve` shows the official English text (en.json `web.admin.common.connectionCheck.result.*`, 34 AI-check keys including the eleven new `textAI*` reasons and `{status}` for `textAIRejected`). A key it does not know, a missing argument, or an older server that sends no key keeps the server's `message` as sent (the generic text on an older server). The speech-to-text and embedding checks go through the same method. Tests: `TheTextModelCheckShowsTheOfficialReasonForItsMessageKey`, `ARejectedRequestNamesItsStatusFromTheMessageArguments`, `AReasonWhoseArgumentsAreMissingKeepsTheServerMessage`, `AnOlderServerThatSendsNoReasonKeyKeepsItsGenericMessage`, `AKeyThisClientDoesNotKnowKeepsTheServerMessage`, `TheSpeechCheckShowsItsOfficialReasonsThroughTheSamePath`, `AReasonIsStillRedactedLikeAnyOtherCheckMessage`; native Integrations fixture.

## Other pages with their own draft handling

Examined and unchanged: Appearance (shared form, Default accent undo is covered by rule 1), Download, Security and Playback settings (raw values, no secrets), Home sections defaults (each change commits at once, no staged draft), Requests and Autoscan settings (their own typed endpoints, no snapshot-keyed staging). Files owned by other open pull requests and not touched: Library settings page (rule 3 above), Backups (rule 2 above), Logging, Network Access, Sign-in.

## Not verified

- No server was involved: every check ran against synthetic fixtures. The #5082 server classification was read in source, not exercised.
- Rule 6 cannot fail against the old native code (no pinned baseline exists); the mutation run for it emulates the official bug.
- The Library "sync node capacity" default and the Backups S3 form are outstanding until their pull requests merge.
