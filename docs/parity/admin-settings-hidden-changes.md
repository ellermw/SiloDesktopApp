# Admin settings: changes the admin cannot see, the same-address stream switch, a declined departure, the Download limit memory

Scope: four native Admin settings pages brought in line with behaviour changes in the official Silo WebUI: Storage, Playback settings, General (Redis group) and
Compatibility (Item A, official `aa06983a6`, #4696); the new Compatibility switch for serving a credential-less stream (Item B, official `34931a5cc`, #4466); whether a
declined departure from an Admin page can leave the app in a broken navigation state (Item C); and the remembered limit of the Download settings page (Item D).
Official reference: Silo server `2972dc63c3291868ed5d452c9e95ad602d9ed022` (newest official main when this work was done); paths below are in that tree. This note states what
was read, built and run. It does not claim visual or pixel parity. Native source: branch `t3/admin-settings-hidden-changes` from `origin/beta` `972980da6` (1.2.1405-beta.1).

## Item A: what official #4696 does

The commit message says "stop settings pages from saving changes the admin can't see". **It does not drop, reset or refuse the hidden edit on most pages: it keeps the row in
view, marked with the existing unsaved-change dot, while it holds an edit**, so Save only ever sends what is on screen. Two pages drop instead (Redis, Compatibility).
`SecretField` gains a `dirty` prop for that mark (`web/src/components/settings/SecretField.tsx:43`, `:72`, `:114`). The setup-wizard files in the commit were not read for this note.

| Page | Situation | What the official client does | When | What the admin sees | Source |
|---|---|---|---|---|---|
| Storage | Public link mode (`s3.public_url_auth`) no longer uses a row that holds an edit: CDN address, token secret, token parameter, token lifetime | Row stays in the Advanced tier, marked changed; the tier's count counts shown rows; the tier is forced open while any of its keys is dirty. A typed token secret keeps its value, it is **not** cleared | At render, while the row is dirty | The row, its value and the dot, until the edit is taken back or saved | `web/src/pages/admin-settings/StorageSettings.tsx:272-286` (`linkModeUses`, `keptForEdit`, `showsAdvanced`, `advancedCount`), `:436-500` (rows), test `StorageSettings.test.tsx` "keeps a typed token secret in view after switching back to signed links" |
| Playback | Throttling off while Buffer ahead is edited; 4K off while the lower-version preference is edited | Row stays, marked changed | At render, while dirty | The row and the dot | `PlaybackSettings.tsx:162-167`, `:312-317`, `:353-358` |
| Playback | Hardware device picker under a mode it does not apply to (acceleration "none", or NVENC) while the staged device list is dirty | Picker stays; rows come from the saved list first; under such a mode a device can only be switched off, or on if it is saved (so the saved list is always reachable and no render path is added for NVENC); a toggle that lands on the saved text stages the saved text itself; rows whose switch differs carry the dot; a list that differs only in order marks its selected rows | At render; on each toggle | The picker with usable switches only toward the saved list | `PlaybackSettings.tsx:81-115`, `:392-470`; helpers `web/src/lib/hwDevices.ts`; tests `PlaybackSettings.test.tsx` |
| General | "Use Redis" switched off with no saved URL | The typed URL and database number are dropped (`resetValue`), nothing is staged | At the switch | Both fields hide with the switch, nothing left staged | `RedisGroup.tsx:102-109`, test `RedisGroup.test.tsx` |
| General | `REDIS_URL` takes the connection over while a switch-off is staged | The switch override is cleared so the locked switch follows the environment | At render | The managed switch reads On | `RedisGroup.tsx:53-55` (already in the shared environment-managed mechanism, #234) |
| Compatibility | A staged API switch-off stages Jellyfin Web off with it (no field of its own) | That web-off is taken back whenever an API switch-off is not staged (flip back, switching off an API that is already off, the environment taking the API switch over) | A layout effect, before the save bar paints | The extra count never shows | `CompatibilityProxiesSettings.tsx:186-201`, test `CompatibilityProxiesSettings.test.tsx` |

Still hidden-and-sent in official at this commit (not changed by it): the marker rows (Detect intros/credits, Detection workers, Online storage, Lazy playback) when the marker
mode hides them (`PlaybackSettings.tsx:682-750`). Choosing "on demand" online storage stages Lazy playback on with it (`:733-738`) and that row has no field under that choice.
**Correction (review):** the hidden write that remains is not only that pair. Any Lazy playback draft is hidden and sent: the pair staged by "on demand" storage, and also the admin's own edit of the Lazy playback row made under local mode followed by a switch to online with on-demand storage already saved
(the row has no field under that choice, so it is kept out of view although dirty: `AdminPlaybackSettingsRules.cs:76-77`). Official does the same, because it hides that row under the same condition and keeps its form value.

## Item A: the native pages, before and after

"Before" is `origin/beta` `972980da6`. A hidden dirty row was sent unless noted.

| Page | Before | After | Where |
|---|---|---|---|
| Storage | `RenderAdvanced` counted rows by link mode only (`AdminStorageSettingsPage.S3.cs:278`, `:286` on beta): with signed links the Advanced tier hid, so an edited CDN address, token secret, token parameter or lifetime stayed dirty and was sent while hidden | Same rule as official: `AdminStorageRules.LinkModeUses`, `ShowsAdvanced`, `KeptForEdit`, `PublicAdvancedCount(draft)` (`AdminStorageRules.cs:169-184`); the page shows a row while it is the mode's or dirty (`AdminStorageSettingsPage.S3.cs:279-286`), marks a kept row (`:262`, secret included), counts shown rows (`:314`). A typed secret is **kept** with the row, as official keeps it; it is never shown back | `AdminStorageRules.cs`, `AdminStorageSettingsPage.S3.cs` |
| Playback | `Visible` hid the row; the controller reset only an *invalid* hidden draft and said "a valid hidden draft stays, as on the official page" (`AdminPlaybackSettingsController.cs:135-139` on beta), so valid hidden drafts (Buffer ahead, lower-version preference, marker rows, a device list under software/NVENC, `hw_device` picker row hidden at `AdminPlaybackSettingsPanel.Conditions.cs:78`) were sent unseen | `AdminPlaybackSettingsRules.IsShown` keeps a conditionally hidden row in view while dirty (`:63-81`), including the marker rows (an extension beyond official, which still hides them; the Lazy playback value staged by "on demand" storage keeps no row, as official). A row the server's capabilities remove (`CapabilityHidden`) cannot be shown, so its draft is reset and never sent (`HiddenDirtyKeys`; controller `AdminPlaybackSettingsController.Update`), **but only on a definite false from a successful capabilities read**: a read that fails is unknown, not false (`AdminPlaybackSupport.TrickplayRead`/`DetectionKindsRead`, `KeepingLastKnown`; `SetVisibilityContext(bool?, bool?)`), so a failed Refresh keeps the last known capability, the rows and every draft, and a first read that fails drops nothing (official builds its keys from the last good capabilities data). Each device row that is kept in view only for an edit carries its own changed mark (`DeviceKeptForEdit`, `AdminPlaybackSettingsPanel.Conditions.cs`; official `dirty={deviceKeptForEdit(row.path)}`); the whole-picker mark stays. The device picker follows the official rules (`DeviceRows`, `DeviceOrder`, `ShowsDevicePicker`, `DeviceSwitchLocked`, `NextDeviceDraft`, `:129-170`; page `AdminPlaybackSettingsPanel.Conditions.cs:73-115`, `AdminPlaybackSettingsPanel.cs:36-45`). A switch's late echo can no longer stage a list: `StageDevice` ignores a state equal to the staged one | `AdminPlaybackSettingsRules.cs`, `AdminPlaybackSettingsController.cs`, `AdminPlaybackSettingsPanel*.cs` |
| General | The switch always staged the empty URL (`AdminGeneralSettingsPage.cs:105` on beta): with no saved URL that sent `redis.url: ""` (a clear of a credential that does not exist) and the database number | `AdminGeneralSettingsController.DisableRedis` (`:153`): no saved URL → reset both drafts; saved URL → stage the empty URL and the number as before. The typed URL is cleared from the box and the draft at the switch. The switch ignores an echo equal to the state shown | `AdminGeneralSettingsController.cs`, `AdminGeneralSettingsPage.cs` |
| Compatibility | Flipping the API switch back left Jellyfin Web's staged off in place, and Save sent `web_enabled: false` in the compatibility PATCH | The web-off that rides with an API switch-off is taken back when no API switch-off is staged; a web-off the admin chose on the web player's own switch is left alone (`AdminIntegrationSettingsController.cs`, `SetCompatibilityApi`). **Review fix:** switching the API off always stages the web player off, also over a web player draft of true, as official `setJellyfinAPIEnabled` does (the earlier version left such a draft and Save sent `enabled=false` with `web_enabled=true`). **What flip-back restores (official):** `webOffAlone` resets whatever web draft remains to its saved value, so the earlier explicit "true" draft is not restored; a web switch-off the admin staged themselves before the API switch-off is the one draft kept (official has none, its web switch writes at once). Switching off an API that is already off stages nothing and returns the web player to its saved value. The environment-takeover rule was already there | `AdminIntegrationSettingsController.cs` |

Environment-managed rows compose with this: a managed key is never staged (`AdminSettingsForm.ApplyEnvironment`, unchanged), a managed row stays disabled with its note, and a managed
hidden row stays unstaged and unsent with no error (unit tests below: Playback Buffer ahead, Storage token lifetime, Compatibility same-address switch, Redis under `REDIS_URL`).

Request changes, stated exactly (the earlier wording "everything else is byte-identical" was not accurate):
- Redis switched off with no saved URL: `redis.url` and `redis.db` are no longer sent.
- A draft for a row the server's capabilities remove (and only a definite false from a successful capabilities read removes it): not sent. A failed capabilities read drops nothing.
- An API flip-back takes the web-off that rode with it back: `web_enabled: false` is no longer sent. Switching the API off over a web player draft of true now sends `enabled: false` with no `web_enabled` (before the review fix it sent `web_enabled: true` with it).
- The hardware device list: the picker rows are now ordered saved-first (saved devices in their saved order, then the others), so a toggled list can be sent in a different order than before; a toggle that lands on the saved devices stages the saved text itself (so an undone edit is not sent at all, however the saved text is spaced).
- Downloads: unchecking Unlimited now stages an empty value where it used to stage the remembered limit (the remembered value is only what Unlimited's check recorded).
- A kept row is sent exactly as before, now while it is visible. No URL changed.

## Item B: the same-address stream switch (official `34931a5cc`, #4466)

Official: setting `jellyfin_compat.address_stream_grant_enabled` (`internal/config/admin_settings.go:147-159`: key, default `false`, hot-reloaded; `restart_keys.go`), a plain
toggle in the Jellyfin group's Advanced section, shown whatever the other Jellyfin settings are (`CompatibilityProxiesSettings.tsx:577-586`, listed in `JELLYFIN_ADVANCED_KEYS` `:44`). The
12 strings per locale are two entries of six lines each: label "Play without sign-in from the same address" and the description, which is the warning ("... Anyone who shares that address, such as
people on the same home network, mobile carrier or VPN, can then play that file too. Turn it on only if you use one of those apps."). There is no confirmation dialog and no dependency.
It is saved by the page's ordinary settings write; the compatibility PATCH body (`AdminJellyfinCompatSettingsPatch`) does not carry it.

**Review fix:** when the server sent no value for the key (a server from before #4466 applied the rule unconditionally and its own page has no such switch) there is no row, the controller refuses to stage it, and nothing is written (`AdminIntegrationsSettings.AddressStreamGrantShown`); the local "false" default is gone. Native: `AdminIntegrationsSettings.AddressStreamGrant` joins `CompatibilityKeys` (not the patch keys), so it goes in the existing `PUT /api/v2/admin/settings` write. The row sits in the
"Jellyfin advanced" group with the official label and warning from the text table (`AdminText.cs:834-835`, `AdminIntegrationsPage.Settings.cs:77`), default `false`. It changes only
through the page's shared toggle handler, which ignores `_syncing`, a locked switch and a state equal to the value shown, so realization and re-render echoes stage nothing. Request change: one new key in
that existing write, only when the admin turns the switch on or off.

## Item C: a declined departure

Question: after an administrator declines to leave an Admin page with unsaved changes (`MainWindow.OnAdminNavigating`, `MainWindow.AdminMonitor.cs:93`: cancel the Frame's navigation, ask,
navigate again on Discard), can the real app be left broken (Back dead, next navigation ignored, an exception)?

**Established (reading, then runs `hiddenset-dev3-storage` and `hiddenset-dev4-storage`, and the final run):**

1. The Frame's report of a navigation in progress is WinUI behaviour for any cancelled navigation, not something the shell or the page does. A detached `Frame` with a handler that only sets `Cancel` refuses stack edits
   (`ForwardStack.Clear` throws `0x800710DD`) at once and 300 ms later, an `async void` cancelling handler does the same, and `Navigate` itself still answers `true` (native check "a Frame whose navigation was cancelled navigates again at once...").
   The window ends at the next navigation, which succeeds; `GoBack` works too (native check "Back works on a Frame whose navigation was cancelled ...").
2. On the outer Frame, with the Admin shell mounted and a draft open, after a declined departure the shell, page and draft are intact, the next departure asks again and is declined again, and an ordinary navigation afterwards
   replaces the shell, leaves Back available and lets stack edits through again (native checks in `admin-configure-storage`). The fixture's teardown failure (`BackStack.Clear` throwing `0x800710DD`) is
   therefore only the window before the next navigation; the fixture now ends with that navigation, and its comment says so.
3. **One product fault found and fixed (first pass).** `NavigationService.RefreshCurrentEntry` (called by the shell for access changes, reconnect catch-up and localized-read refreshes) restores the forward stack right after `Frame.Navigate` answers `true`. When the Admin guard
   cancels that refresh because a draft is open, nothing was committed and the Frame refuses stack edits, so `ForwardStack.Clear` threw `COMException 0x800710DD` (the callers' `TryShellAction` logged it as a navigation failure) and the replacement recorded for the refresh stayed armed for a later,
   unrelated navigation. Evidence before the fix: run `hiddenset-dev3-storage`, "refreshing the current route while a draft is unsaved: accepted=False, exception=COMException 0x800710DD". After: `hiddenset-dev4-storage` and `hiddenset-final-r1`, "exception=none" and the check "a refresh ... throws nothing and keeps the shell, the page and the draft".
   The first fix caught that exact HRESULT at the stack edit. **Review fix:** the cancelled refresh is now detected directly: `NavigationService` counts committed navigations (`OnNavigated`), and when `Frame.Navigate` commits nothing the stacks are not touched, the replacement is dropped and `RefreshCurrentEntry` answers false; the HRESULT catch stays only as a last resort. Native checks: `access-navigation` ("a refresh cancelled by a Navigating handler throws nothing, answers false, commits nothing and leaves the stacks alone", "the replacement of a cancelled refresh does not wait for a later navigation") and the Storage fixture.
4. **Fixed in the review pass (was observed, not fixed):** a refresh the guard cancelled opened the unsaved-changes dialog without any action by the admin, and its Discard threw the draft away without rereading (`NavigateImmediately` refuses a navigation to the entry that is already current). Now `NavigationService.RefreshCurrentEntry` asks `CurrentPageHoldsUnsavedEdits`, which the shell answers with `MainWindow.AdminPageHoldsUnsavedEdits` (the Admin shell is on screen, its page reports unsaved edits for a current owner, and the admin authority is available: the same condition `OnAdminNavigating` uses). When true, the refresh is skipped: it does not navigate, shows no dialog and `RefreshCurrentEntry` answers false. Nothing is remembered and nothing runs later (no timer); the page rereads when the admin saves, reloads or leaves it. An access change that ends the account's admin role is not skipped: the authority is no longer available, so the condition is false and the refresh runs and takes the admin away as before. A language change refreshes the current route through the same call, so on an Admin page with a draft the page's own text keeps the earlier language until the page is next opened (the shell chrome relocalizes on its own paths; nothing is discarded). A viewer page, or an admin page without unsaved changes, is refreshed at once exactly as before.
   **Second review (replaced the first version of this fix):** the first version owed the refresh and ran it from a 400 ms timer once the draft was gone. The reviewer showed two faults: moving to another admin page inside the shell, or saving, let the timer remount the shell on its original entry (usually the Dashboard), because navigation inside the shell does not raise the outer Frame's `Navigated`; and the timer was not stopped when the window closed. The timer, `RefreshDeferral` and its unit tests are removed. The cost of the simpler rule: data that changed on the server while a draft was open is not reread by itself afterwards.

Limits: the native checks mirror `OnAdminNavigating` step by step on the real outer Frame, `NavigationService` and shell; it does not instantiate `MainWindow`, so the production handler itself is covered by reading, not by that run. Back on the outer Frame itself was
not exercised (the fixture's outer Frame has no earlier entry); Back is shown on the detached Frame only.

## Item D: the Download limit memory (official `web/src/components/settings/LimitField.tsx`)

Official: `lastLimit` starts as the fallback (empty on every Downloads row), is set only in `toggleUnlimited(true)` to the stored value at that moment (the fallback when the value already is Unlimited), and unchecking restores it, never the sentinel
(`LimitField.tsx:88`, `:100-108`). A value the box merely showed is not remembered.
Before: `RenderLimit` copied every finite stored value into `LastLimit` (`AdminDownloadSettingsPage.cs:430` on beta), so type 250, Discard, uncheck Unlimited showed 250. After: `AdminLimitMemory` (`AdminDownloadSettingsRules.cs:334`)
is the only place the limit is recorded, at the check (`AdminDownloadSettingsPage.cs:249`); the same case shows an empty box. Request bodies unchanged.

## Tests and checks

Unit (`tests/SiloPlayer.Tests`): `AdminHiddenChangesTests` (Storage rules and an exhaustive no-hidden-dirty-row sweep, Playback rows, the three official device-picker cases, the staged saved text, a managed device list and a managed hidden row, Redis cases with and without a saved URL, a typed
secret, an unrelated edit, managed Redis), `AdminStorageSettingsControllerTests` (a token secret and lifetime kept after the mode goes back to signed links keep their drafts and are sent once with the mode, and nothing is left staged after the save; a managed hidden token lifetime is neither sent nor blocking), `AdminCompatibilitySettingsHiddenTests`
(default, nothing staged or sent until the admin acts, one PUT with one key, not in the PATCH, managed, the web-off pairing cases), `AdminLimitMemoryTests` (the official cases).
Native (each page's existing group): Storage kept rows and the declined-departure/refresh/ordinary-navigation checks; Playback kept Buffer ahead and the device picker under software encoding; General Redis switch-off with no saved URL; Integrations same-address switch (off on open, official warning,
no write on reload, one explicit write) and the API flip-back; Downloads official limit steps.

Checks whose expectation changed (official changed them; nothing was weakened or deleted):
- `AdminPlaybackSecondReviewTests.RulesNameInvalidDraftsOfHiddenRows` is `RulesKeepTheRowOfAnInvalidDraftInViewSoItsErrorIsSeen`; `SwitchingThrottlingOffDropsAnInvalidBufferAheadDraftSoSaveIsNotBlocked` and `ChangingTheMarkerModeDropsAnInvalidWorkerCountButKeepsAValidHiddenDraft`
  now assert the draft and its error stay visible (they asserted it was dropped silently).
- Playback native: "hiding the row drops its unseen error so the throttle change can be saved" now asserts the row stays with its error and Save stays blocked until the edit is taken back.
- Downloads native: four steps accepted "unchecked, editable and not Unlimited" or a restored remembered value; they assert an empty box, and one step is added for type, Discard, uncheck.

## Not verified / not established

- No run against a real server; no visual or pixel comparison; no screen-reader run (the kept rows reuse the existing row chrome and help text).
- The marker rows are kept in view although official still hides them (an extension of the same rule, stated above); the hidden writes that remain are any Lazy playback draft (the official pair, and the admin's own edit made under local mode before a switch to online with on-demand storage), as official.
- `MainWindow` itself is not constructed by any native check; its wiring of the skip is covered by the unit source checks in `NavigationRefreshSkipTests` (the shell's condition, the skip before any navigation, and no timer in `NavigationService`) and by reading. The native checks stand in for the shell's answer with a flag (in the Storage fixture a flag that stays true, so the later steps keep their shell).
- Scaled displays (125%/150%): not run (the machine is 100%); checked by reading the code this branch added (below).
- Other pages with conditionally hidden rows were not audited for this pattern (for example AI settings' service tier row, Security, Network); only the pages in this change were.
- The access-navigation native group is now run (selector `access-navigation`, added to `Program.cs` beside the existing environment switch).
- Commit `b16d0fdb4`'s last message line ("fewer keys only, never more") is imprecise: a kept row is sent as before; the exact request changes are the ones listed above.
- Finding 9 (environment status re-read), established by one unit test per page (`AfterAFailedStatusReReadTheLastManagedListStaysAndAManagedKeptRowIsNeverSent` for Playback, `AfterAFailedStatusReReadAManagedKeptLinkModeRowStaysManagedAndIsNeverSent` for Storage): after a failed status re-read `AdminSettingsForm.ApplyEnvironment` keeps the last answered managed list, a managed row that is hidden or kept in view is never staged or sent, and on Storage Save stays blocked while the status read is in error.

## Observed (under the SiloOct18Build lease, lane `D:/SiloPlayer-build/admin-completion-lane-lead`)

Baseline, unchanged beta `972980da6` (each group alone, labels `hiddenset-base-*`; a check count is the number of PASS lines without the final completion line):
admin-configure-storage 86 checks, no failure, host exit code `0xC000027B` after the completion line; admin-playback-administration 110, exit 0; admin-configure-general 192, exit 0;
admin-configure-integrations 109, exit `0xC000027B` after the completion line; admin-manage-download-settings 429, exit 0. Storage therefore joins the groups on Beta that pass every check and then crash at exit; it did
not crash in two later runs, so the crash is intermittent (not investigated here).

Final run `hiddenset-final-r1` on commit `c0fe9a4e2a3d2f3e921d42d6a45538d13002e156` (source only; this note and the entry in `docs/current-development.md` came after): 497 focused and 11,318 full unit cases passed,
0 failed, 0 skipped. An earlier attempt to start it failed on how its selector list was passed on the command line, before any command ran, and a later mistaken start used up the label `hiddenset-final-r1-integrations`.
App build, self-contained publish and native host build passed; payload and source seals verified. Native groups, each host exit code 0: admin-playback-administration 120 checks, admin-configure-general 194,
admin-manage-download-settings 433, admin-configure-security 375, admin-manage-foundation 35, admin-monitor 1,070, admin-configure-storage 97; no failed check.
admin-configure-integrations alone on the same build (`-Stage native`, label `hiddenset-final-r2-integrations`): 121 checks, no failed check, `PASS: native regression run completed.` present, host exit code `0xC000027B` as on
beta (not investigated here). Earlier development runs of the same groups on intermediate source (`hiddenset-dev1-*` to `hiddenset-dev4-*`) are in the lane; the Item C evidence quoted above is `hiddenset-dev3-storage` (before the fix) and
`hiddenset-dev4-storage` (after).

## Review pass (findings 1 to 9): scaled-display check of what this branch added

Rule: no second measure of a child at exactly its own desired width, at least 1 pixel of tolerance in fit or wrap comparisons, no nested wrapping panel for a strip that must stay on one line, padding at least as large as the corner radius around button text. Read, not run (the machine runs at 100%):
- Kept-in-view rows and their marks: they reuse `AdminSettingsRow` and its dot (`AdminSettingsLabel`); this branch added no layout of its own. **Reported, not edited (shared):** `AdminSettingsLabel.MeasureOverride` (`Controls/AdminSettingsUi.cs`) decides its wrap with `gap + label + restart > available`, an exact comparison with no tolerance (audit item A14).
- The same-address switch row: the page's `Field` grid (star text column, auto switch column), no second measure, no button.
- The device rows: each is a switch whose header is a two-column grid (auto dot, star name); the name wraps in the star column, the dot takes no room while collapsed; no second measure, no wrapping panel, no button.
- The Storage Advanced disclosure (already there; only its count source changed) has a template with no corner binding, so its text is not clipped by its 6 px radius.

## Review pass: observed (lane `D:/SiloPlayer-build/admin-completion-lane-lead`)

Mutation run `hiddenfix-mutation-r1` (focused; product behaviour of findings 1, 3 and 5 put back to the old semantics, then restored with `git checkout` and `git diff` confirmed empty): 12 of 63 cases failed, among them the failed-read cases of finding 1, the web-player-draft cases of finding 3 and the deferral cases of finding 5.
Final run `hiddenfix-final-r1` on commit `898c3cf03c83c34b815be63e0ad0f58aa639e2e4` (source only; these docs came after): 292 focused and 11,344 full unit cases passed, 0 failed; native groups (host exit code 0, no failed check): admin-playback-administration 133 checks, admin-configure-general 198,
admin-manage-download-settings 434, admin-configure-security 376, admin-manage-foundation 36, admin-monitor 1,071, admin-configure-storage 101, access-navigation 21, shell-localization 4. admin-configure-integrations alone on the same build (`-Stage native`, label `hiddenfix-final-r1-integrations`): 126 checks, no failed check,
`PASS: native regression run completed.` present, host exit code `0xC000027B` (-1073741189) as on beta (not investigated here). Development runs: `hiddenfix-dev1` (focused unit), `hiddenfix-dev2` (access-navigation, playback, general, storage native on the same source).
