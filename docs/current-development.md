# November 23 — Player: Next Up is timed from the end of the file (official #4502, `3b2b32168`)

Branch `t3/next-up-timing` on Beta `ecdc94c8c`; versions unchanged. Next Up opens the viewer's "Show Next Up" lead time before the end of the file
(`playback.next_up_prompt_seconds`: At end, 10 s, 30 s, 1 min, 2 min; default 30; "At end" opens it only when playback ends). It no longer opens
before the setting has loaded; a failed or unsupported read uses 30. An item with a later part holds Next Up until its last part (this used to hold
only for shuffle). The 10-second "Up next" card and countdown that began at the credits marker no longer starts and no longer advances by itself; Skip
Credits, Skip Intro and the Next Episode pill are unchanged. Settings > Playback has the "Show Next Up" choice and no longer shows "Start next at
preview" (official kept that server key and only hid it). The ended screen waits for the auto-play setting before counting down. The rule is one
function, `NextUpPromptTiming`, with the official test cases.

Left in place, unreachable: the Up next card's renderer and its still-image and autoplay publishers (Lua and C#). Removing them touches about 15 files
and many fixtures and is a separate cleanup.

Observed (worker run `nextup-r2`, one final build): 627 focused unit tests passed (`NextUpPromptTimingTests`, the `MpvOsc*` runtime tests, player,
settings and contract source tests); native `hud-completion` (default case and six further cases) and `hud-nov12` passed with no failed check; native
`settings-visual-oct24` passed its new Show Next Up check and then stopped at the Home screen heading lookup that also stops it without this change
in this thread's other runs. The lead read the timing rule and the player hook and opened `settings-next-up-1280.png`. Existing tests that encoded
the credits countdown or the removed toggle were rewritten (listed in the pull request). Not verified: real playback to the end of a file with each
setting value, a real split item, an older server, other languages. The full unit suite was not run.

# November 23 — Settings > API Keys (official #4411, `3b2b32168`)

Branch `t3/settings-api-keys` on Beta `ecdc94c8c`; versions unchanged. The user Settings gain the official "API Keys" page (personal, scoped keys),
after Connect Apps in Connections, in the settings directory and in settings search. It has the official states (loading, load failed, creation turned
off by the server, limit reached, empty), the "Your keys" list (label, profile or Whole account, scopes, created / last used, Load more, Revoke), the
Create Key dialog (label, Acts as, one checkbox per scope with its description, "choose at least one"), the one-time secret view with Copy & Close and
"How to use this key", and the Revoke confirmation. Requests: `GET /api/v2/api-keys/scopes`, `GET /api/v2/api-keys` (50 per page, stops on a repeated
cursor), `POST /api/v2/api-keys`, `DELETE /api/v2/api-keys/{id}`. Create is sent exactly once (no retry, no replay after a token refresh; new
`SiloApiClient.SendIdentityBoundRequestWithoutRefreshAsync`); an unknown outcome shows the official "uncertain" message. Replies that arrive after an
account, profile or server switch are dropped. The secret is never logged or stored, and the clipboard copy stays out of clipboard history.

Differences from the official page: after an uncertain create the dialog's Create button is disabled (official leaves it enabled); the secret view has a
Close button in place of the web dialog's X; after create or revoke the list reloads its first page only.

Observed (worker run `apikeys-r3`, one final build): 120 focused unit tests passed (`PersonalApiKeysTests` and the settings/contract source tests);
native `api-keys` passed at 1440 and 800. The lead opened `apikeys-listed-1440.png`, `apikeys-create-1440.png` and `apikeys-secret-800.png`. Not
verified: a real server, a real clipboard copy, other languages (the strings come from the catalog once the 2026.11.22 catalog PR merges; until then
the official English is used), the page inside the installed app. The full unit suite was not run.

# November 23 — Official small changes, batch 3 (official `3b2b32168`)

Branch `t3/official-3b2b321-small-changes` on Beta `ecdc94c8c` (the same source as stable 1.2.1660); versions unchanged. Official Silo Server main was
fetched at `3b2b321688ac2fbe812a5a259859a17880c43d71`, 23 commits after the last pin `c90e406db`. Every `/api/v2` path the app sends was checked
against that commit's `contracts/api/v2/openapi.json` (957 operations): none is missing or renamed; no request changed in this batch.

- #5135 metered quality: a profile with no saved metered pair now gets Auto up to 10 Mbps (settings contract revision 30) instead of 720p / 2 Mbps,
  in playback and on the Settings page. The extra 2 Mbps ceiling for the OS's Data Saver / Low Data Mode is not built: Windows reports no such mode.
- #5133 Admin > Playback: "Mark watched at" carries the official note about titles with a credits marker. The rule itself is the server's.
- #5120 library lists: the profile editor's Restrict libraries list and the access-group library list show the library type in the interface
  language instead of the stored word, and each profile-editor switch is named for its library.
- #4371 ebook search: the bundled reader's whole-book search keeps one time budget across sections (the official `view.js` change, ported by hand;
  the app's other local edits to the bundled reader are kept).
- Already matching or not applicable: #5115 (the native audio preference save and the admin profile Home sections save already show fixed sentences
  for a failed connection, never the transport error text),
  #5126, #5119, #5078, #4861 (browser layout), #5146 (web player reconnect; not compared with the native reconnect yet), and the server-only commits.

Observed (run `small-r1`, one build): 72 focused unit tests passed (`CurrentPlaybackQualityTests`, `AdminPlaybackAdministrationTests`); native
`admin-playback-administration` 281 checks, none failed. Native `settings-visual-oct24` passed its metered-defaults check (Auto / 10 Mbps shown, writes
saved) and then stopped at a Home screen heading lookup (`SettingsVisualOct24NativeFixture.cs:232`); native `reader-interop` passed its EPUB/PDF and
search-cancellation checks and then stopped at "Stale annotation revision bypassed its validator" (`ReaderInteropFixture.cs:413`). Neither line is in
code this change touches; they were not run against unchanged Beta. The lead opened `playback-settings.png` (the note is under "Mark watched at") and
`settings-playback-1280.png`. The full unit suite was not run. No ebook with many short sections was searched by hand.

# November 23 — Translation catalog refreshed to release 2026.11.22 (official `3b2b32168`)

Branch `t3/translation-catalog-2026-11-22` on Beta `ecdc94c8c`; versions unchanged. The offline interface catalogs under
`src/SiloPlayer.Core/Localization/Generated` were regenerated with `tools/SiloTranslationsImport` from official Silo Server
`3b2b321688ac2fbe812a5a259859a17880c43d71` (silo-translations web release `2026.11.22-d12da65`; the previous pin was `2026.11.16-20af7f8` at
`17d5376ce`). Viewer strings, per language: 7,074 messages (was 6,895); in English 184 added, 30 reworded, 5 removed (none of the 5 is used by the app).
`web.admin.*` stays excluded.

The release now also generates catalogs for 17 languages the WebUI does not offer (they are for other platforms). The import tool now keeps only the
languages in the WebUI's own `SHIPPED_LANGUAGES` table (`web/src/lib/appLocale.ts`), as the WebUI's pickers do, so the app still ships the same 22.

Text the app builds from its own English instead of a catalog key does not change with this: the device sign-in approval page and the Home row
gallery's "Collections & rules" / "Titles matching rules" names keep their older wording until those pages are updated.

Observed (run `catalog-r1`, one build): the full unit suite, 12,384 passed, 0 failed (including the catalog checksum and manifest tests, repinned to
the new release); native `viewer-localization` 173 checks and `shell-localization` 4 checks, none failed. Native `settings-localization` stopped in
its own fixture on both of two runs, at different places ("Collection pending status/state did not refresh" after 12 checks, then
`TargetParameterCountException` after 18); neither is a translation check, and the group was not run on unchanged Beta. The lead opened `login-pl.png`.

# November 22 — Manage › Users laid out like the official pages (official `c90e406db`)

Branch `t3/admin-users-visual-parity` (from Beta `be0254bec`); layout only, no controller, URL, poll or permission rule changed; one read added (Overview reads the access groups for an account that is in a group, so the Access card and header name it). The list (header with Access Groups and Add User, the Users / Invitations / Invite Codes strip, search, group filter, sortable table from 1024 px and compact rows with a menu below it, pager), the user detail (breadcrumb, avatar, role and status badges, View as user / Reset password / More menu, seven tabs with counts) and every tab (Overview stat strip and cards, Profiles, Access & limits cards with DEFAULT / GROUP / CUSTOM tags, Sign-in, Activity, Downloads, Preferences with the level list) use the shared Admin primitives (`AdminUsersUi`) and the official `web.admin.users.*` wording (`admin-users.en.json`). Not built because the page has no equivalent today: View as and Delete on a list row (both stay on the detail page), the Public signups badge (needs a settings read), the group name in the detail header on tabs that do not load groups (Overview and Access & limits do). Tabs are ordinary tab stops with arrow-key movement; signed-in sessions and monitored series keep a bounded load-more.
Test: `admin-users` native group, run `userslook-r14` (results.txt and PNGs: list populated, empty and error; each detail tab at 1440 and 1024; list at 560, 800 and 1280).

# November 22 — Environment-set settings on Logging, Sign-in and Providers; declared AI providers; the embedding lock message

Branch `t3/admin-env-managed-remaining` on current Beta; versions unchanged. A setting the server's environment fixes is shown read-only with the "Set by ..." note and is never sent in a save on three more pages: Logging (the server log level, `SILO_LOG_LEVEL`), Sign-in settings and Providers. On AI Services, providers declared by the environment are shown as such (official #4668), and the embedding lock conflict has its own message (official #4442). The shared mechanism from #234 is reused.
Observed (one build; only the pages touched): focused unit tests 255 passed (`envrest-lead-u1`); native groups admin-monitor-logs 303, admin-signin-security 341, admin-configure-providers 266, admin-configure-integrations 153, none failed (`envrest-lead-n1` to `-n4`). The lead opened the Sign-in page with a managed row at 1440: the switch is disabled and the note wraps. The full unit suite was not run and there was no independent review. A worker wrote the code; it was paused before its own verification and the lead finished it.

# November 22 — Activity: column headers line up with their columns

Reported by the owner with a screenshot of Admin > Activity: the User, Playback, Bitrate, Route and Time headers sat to the left of their columns, further off with each column. Cause: the header and each row are separate grids with the same column definitions except the last one, which was sized by its own content (the row's two action buttons, 56 wide; an 84 wide placeholder in the header), so the star columns before it got different widths. The last column is now a fixed 56 in both. Branch `t3/activity-header-alignment`; versions unchanged.
Observed (run `acthead-r2`, one build, the Activity group only): `admin-monitor` 1,215 checks, none failed, including a new check that every header starts where its column starts in every row. The lead opened the page at 1440: headers are over their columns. The new check was not run against the old code; the full unit suite was not run.
Review follow-up (not built or run yet): the header also gives up on its right whatever width the rows' scrollbar takes from the rows once they overflow their panel, so its columns stay the rows' widths; the `activity-scroll` case (30 streams, the panel scrolling) now checks the headers against the rows at 1440 and 1920, and logs the panel's and the rows' widths.

# November 22 — Two small admin fixes (Libraries list at a narrow window; Library settings emptied number)

Branch `t3/admin-leftovers` on current Beta; versions unchanged. (1) Libraries list: while the table is shown in a window under 1280 wide, the warning date under "Last scanned" wraps instead of holding its column wide, so library names and status pills get the room (at 1024 names were cut and pills wrapped their text). (2) Library settings: an emptied "sync node capacity" shows nothing instead of a default of 1 (official #4816: no default once emptied). The Backups S3 secret field was checked and already sends nothing when emptied.
Observed (run `leftovers-r1`, one build, the two affected page groups only): `admin-manage-library-settings` 87 checks, none failed; `admin-libraries` 907 passed and the one keyboard-focus check that already fails on unchanged Beta failed ("the editor opened on Folders holds keyboard focus"). The lead opened the Libraries list at 1024. The full unit suite was not run for this change.

# November 22 — Test stability, second pass (plugin-cutover browser loss, leaked format culture, AppUpdateExit, retired-authority read, synced-sort-direction)

Branch `t3/test-stability-2` from Beta `fb74de97c`; record in [the parity note](parity/test-stability-2.md). Five checks that failed for reasons unrelated to the change under test.
**A** `plugin-cutover`: the overlay hook `RTSSHooks64.dll` (`+0x1490AF`, read from the run's Crashpad report) crashes the web view's browser process; the group now restarts on a fresh web view, at most three starts, only when that
hook was loaded in the process and the process ended with `0xC0000005` (new `WebViewBrowserLossWatch.cs`, `AdminPluginAppsNativeFixture.cs` untouched; other WebView2 fixtures have no recorded loss and are not guarded).
**B** eleven unit tests with Arabic decimal separators: `LocaleService` keeps the culture of whichever thread first touches it, and `CalendarCultureTests` set ar-SA first; a module initializer fixes the baseline and `TestCulture.Use` is the one scoped
set-and-restore (seven sites, one never restored); proven with a probe in a fresh `AssemblyLoadContext`, the 11 failures themselves not reproduced. **C** `AppUpdateExitTests`: the exit-gate scan sees the SiloPlayer-named processes
`UpdateTransactionLifecycleTests` starts; the class joins their collection. **D** `LateReadFromRetiredAuthorityIsDropped`: the test chose the gated read by hit order; it now waits for the handler's own decision (one separate commit in the file the
open stability pull request owns; merges cleanly with it). **E** `synced-sort-direction` (`0x800F1000`, reproduced on Beta as `stab2-e-base-r1`): product ordering defect, the Direction combo was removed from an already-orphaned tree and could not be added
again on this machine; it is now detached with its siblings (`SavedSynced.cs:20`, `Synced.cs:103`), group passes (`stab2-e-fix-r1`). Final run `stab2-final-r1`: 758 focused and 12,064 full unit tests passed, native plugin-cutover 7, admin-plugins 364, synced-sort-direction 2, shell-localization 4, admin-manage-foundation 36, admin-monitor 1,151, 0 FAIL. Not established: restart path of A not exercised, B and D failures not reproduced, which process made C fail.

**Lead addition and final run (November 22).** Added: `AdminStorageSettingsControllerTests` waits for a state with a fifteen-second bound instead of two (it stopped a full run with "Timed out: open" again on November 22; the wait still returns as soon as the state arrives). Beta through #256 was merged in. Final run `stab2-lead-r1` on `db1388c6a`: 356 focused and 12,108 full unit cases passed, 0 failed; app build, publish and native host build passed; native groups plugin-cutover (7 checks), admin-plugins (364), admin-plugin-apps (104), synced-sort-direction (2), shell-localization (4), admin-manage-foundation (36) and admin-monitor (1,151) passed with no failed check, no restart note and exit code 0. No independent review; the lead read the two-line product change.

# November 22 — Official small changes, batch 2 (focus ring, Match Item Enter, tall confirmations, Max runtime, failure text, plugin check)

Six official WebUI changes compared with the native app (official source pin `c90e406db71ee468bffdfa5733cd97d1d50b4019`), on Beta `bdc249d09`,
branch `t3/official-small-changes-2`; versions unchanged, nothing pushed. Detail, evidence and limits: `docs/parity/official-small-changes-2.md`.

- #5081 red button focus ring: already matches (no red override in the destructive style or template); fixture and source test added.
- #4646 Match Item Enter: Enter now does what the Search button does under its disabled conditions (it used to queue a search while the hints loaded);
  proved failing on the old path.
- #5070 tall confirmations: the shared dialog template's body scroller could not scroll, so a long body was clipped; `Helpers/DialogBodyScroll` turns it on
  for dialogs whose content does not scroll itself. Actions were already on screen at 800x500 and 560x420.
- #5077 Max runtime: stored limit shown to five decimals, negative shows 0, leaving the box shows the 0 an entry under half a millisecond saves; 6 tests
  failed on the old draft, official test ported.
- #4537 failure text: new `ReadableErrorText` used by the pre-play audio and subtitle preference saves and the subtitle upload, detect, search and
  download errors; admin user pages already show fixed native sentences, left alone (an open pull request owns `AdminUserResource*`).
- #5103 plugin `TestConfiguration`: server only; native offers the check from `connection_check_supported`; no change.

No request body, URL or method changed. Observed, under the SiloOct18Build lease: final run `small2-final-r4` on commit `937547f00`: 110 focused and 12,044
full unit cases passed, 0 failed; app build, publish and native host build passed; native `official-small-changes-2`, `admin-configure-scheduled-tasks`,
`match-split-scope`, `conditional-editors`, `native-subtitle-dialogs`, `shared-destructive-theme`, `shared-controls`, `admin-manage-foundation` and
`admin-monitor` passed. Not passing and not caused by this work: `conditional-dialogs` (its fixture builds `MatchItemDialog` without a `SiloApiClient`
service; the constructor line is unchanged). Not verified: the rendered focus ring in the final run (the hidden window did not present its page content;
earlier run `small2-native-new-r4` did and is the source of the ring pictures), a real Tab walk through dialogs, a live server with a self-checking plugin.

**Lead check and final run (November 22).** The lead read the product diff and rewrote `Helpers/DialogBodyScroll` (`a10765d17`): the first version left a `LayoutUpdated` handler behind for the content scroller of every dialog ever opened; it now follows layout only while the dialog is open and removes its handler when the dialog closes. Beta through #254 was merged in. Final run `small2-lead-r4` on `a22563c9f`: 70 focused and 12,128 full unit cases passed, 0 failed; app build, publish and native host build passed; native groups official-small-changes-2 (93 checks), admin-configure-scheduled-tasks (206), match-split-scope (23), conditional-editors (24), native-subtitle-dialogs (5), shared-destructive-theme (96), shared-controls (3), admin-manage-foundation (36) and admin-monitor (1,151) passed with no failed check and exit code 0. The lead opened `small2-confirm-shared-560x420-window.png`: the long body scrolls and Delete (focused, with its ring) and Cancel are visible under it. Earlier runs on this head: `small2-lead-r1` stopped on the intermittent `AdminStorageSettingsControllerTests` ("Timed out: open"); `small2-lead-r2` failed only in `admin-libraries`, on two keyboard-focus checks that also fail on unchanged Beta `7629d11c6` (`betabase-lib-r1`; diagnosed separately), so that group was left out. A two-line repair of the old `conditional-dialogs` fixture (it fails on Beta with "No service for type SiloApiClient has been registered") was tried and reverted: with both missing services registered the group times out for a further reason. No independent review.

# November 22 — Network access: the Direct connections section (official `b6f104933`, PR #5047)

Branch `t3/admin-direct-connections` on Beta `e21546981`; versions unchanged. Official source read at `c90e406db71ee468bffdfa5733cd97d1d50b4019` (the feature commit and every file the section uses are unchanged between them). The parity note is `docs/parity/admin-direct-connections.md`: the official elements, states, wording, requests, polling and STUN rules, then what was built and what differs.

Delivered: below the providers, a Direct connections group that exists only while the saved `direct_connections.enabled` is `true` or any of its three keys is staged. It holds the enable switch, the readiness diagnostics (host heading and last-checked time, every feature row with its state, reason, requested/granted sizes and detail, the listeners, host tuning), a Run check button, and an Advanced disclosure with Port mapping and STUN servers. The page gained the shared save bar and leave guard for the three keys, which are one batched settings form. Status reads start when the section appears, poll every 15 s only while the page and window are in front, and a failed poll keeps the data and keeps polling. Request order decides: a poll that started before a Run check result was published is dropped. Run check sends one refresh: a 4xx answer shows the server's detail; a timeout, 408, 5xx or transport failure is an unknown outcome and the state is read again, never re-sent. The STUN list is checked with the server's rules and messages before Save and keeps the typed text.

Verification (lane A, label `directconn-final-r3`, source `HEAD` at that run): focused unit 314 passed; full unit suite 12,136 passed, 0 failed; native `admin-configure-network-access` 716 PASS, `admin-configure-general` 198, `admin-configure-security` 376, `admin-manage-foundation` 36, `admin-monitor` 1,151, all with no FAIL line (the Network access host exited cleanly). Mutation proof `directconn-mutation-r1`: with the state mapping and the STUN rules deliberately broken, 23 of the 130 Direct connections tests failed; both mutations were then restored. The screenshots the native run wrote were opened and read; two defects found in them (a long variable name cut off at 560, a stray hairline under the last advanced row) were fixed before the final run.

Not verified: a real server (all data is synthetic), displays scaled to 125%/150% (the layout rules for them are applied, not measured), and a Silo Server older or newer than the pin. Two earlier final attempts failed in the full unit suite for reasons outside this change (`directconn-final-r1`: 11 tests failing with Arabic decimal separators, a leaked locale; `directconn-final-r2`: `AppUpdateExitTests` and a timing race in one of this family's own tests, fixed by waiting for the poll answer); `directconn-final-r3` ran on unchanged source after those.

**Review and final run (lead, November 22).** An independent read-only review confirmed the settings write, the STUN validation step by step against the server's, the status reads, the visibility condition and the wording, and found two P2 and three smaller items, fixed in `b522e9ba5`: after an account or server switch during the first settings read nothing reopened the section (the page now tries again when the open ends without an owner, as General and Security do; not covered by a test); a failed first settings read showed an error naming a feature that official keeps hidden until an administrator enables it (the error with Reload now appears only for a failed re-read while the section is on screen; native checks for both cases); the message for an empty STUN entry matches the server's (no "address :" prefix; unit test); the STUN box takes a new owner's value once its form has loaded, even while focused. Beta through #251 was merged in. Final run `directconn-lead-r1` on `5e01188a7`: 222 focused and 12,138 full unit cases passed, 0 failed; app build, publish and native host build passed; native groups admin-configure-network-access (717 checks), admin-configure-general (198), admin-configure-security (376), admin-manage-foundation (36) and admin-monitor (1,151) passed with no failed check and exit code 0. The lead opened the loaded section at 1440 (top and middle) and the section at 560. The review fixes were not reviewed again.

# November 22 — Admin settings: emptied fields go back to what is saved, and the text model check says why it failed

Official silo-server #4816 (`ca51b57f0`, "stop emptied settings fields from staging values they don't show") and #5082 (`10e67d836`, "say why the text
model check failed"), both inside the pinned source `c90e406db71ee468bffdfa5733cd97d1d50b4019`. Branch `t3/admin-emptied-settings-fields` on Beta
`e21546981`; versions are unchanged. Per-rule detail, request bodies and limits: `docs/parity/admin-emptied-settings-fields.md`.

Changed: the shared `AdminSettingsForm` learns which secrets the server reports as set (`ApplyConfigured`) so an emptied secret with nothing saved
stages nothing and a set secret keeps its explicit clear (`SetSecret`); Email, Search, Storage, Notifications and General route an emptied secret to
the saved state; AI tuning, Notifications, Search and Storage numbers show what they hold instead of a default; General's Use Redis choice ends on
Save and Discard only (`SaveAsync` now returns whether the write was acknowledged); AI Services validates only the tuning values a save sends, with
the official messages; the text model check shows the official reason for its `message_key` (`AdminConnectionCheckText`), and an older server keeps
its generic message. Already matching: ordinary settings (rule 1), Providers (rule 2), Playback (rule 3), the loaded-settings clause of rule 5, and
rule 6 (the native form has no pinned baseline).

Not done, files owned by other open pull requests: the Library "sync node capacity" default (`AdminLibrarySettingsPage.cs:330`) and the Backups S3
form.

Verified: `emptied-final-r2` (`-Stage final`): focused unit filter 576 passed, full unit suite 12068 passed, 0 failed; native selectors general, email,
notifications, integrations, search, storage, providers, security, appearance, download settings, library settings, backups, foundation and monitor all
passed. Mutation runs `emptied-mutation-r1`, `-r2` and `-r6` each failed the checks they name and were restored. Not verified: any real server; the
server's own classification of a failed text model check was read in source only.

**Review and final run (lead, November 22).** An independent read-only review traced the PUT body for each secret sequence on every page, the paired Redis keys, the emptied numbers against the server's validation, every path of the Use Redis switch, the AI validation, the check-reason mapping and each existing test whose expectation changed, found no P1 or P2 and gave "safe to submit". Two P3 notes were left: the Use Redis switch keeps its choice after a Save that had nothing to send (reachable only while the secret status is unavailable; no request differs), and the app carries its own copy of the 34 check-reason strings (identical to the server's at the pin). Beta through #251 was merged in. Final run `emptied-lead-r2` on `b0003b48b`: 383 focused and 12,070 full unit cases passed, 0 failed; app build, publish and native host build passed; native groups admin-configure-general (210 checks), admin-configure-email (307), admin-configure-notifications (145), admin-configure-integrations (170), admin-configure-search (60), admin-configure-storage (101), admin-configure-providers (251), admin-configure-security (376), admin-manage-foundation (36) and admin-monitor (1,151) passed with no failed check and exit code 0. The run before it (`emptied-lead-r1`) stopped on the known intermittent unit test `AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling` (fixed in #254, not merged here) and was repeated unchanged.

# November 22 — Activity, Now Playing and household streams: an episode is led by its show; a movie has no subtitle (official c90e406db)

Official #5106 (`79e62369d`), #5105 (`72b452646`) and #5109 (`c90e406db`) changed how a playing session is titled: an episode's first line
is its show and its second line is "S1E2 · Episode title" (the code alone when the episode has no title); a movie has no second line; a
series, or an episode that lost its show or numbers, keeps its own title with "Series" under it; anything else keeps its title, else "File #id".
Native surfaces changed to that rule: Admin > Monitor > Activity (the wide table's Stream cell, the stacked card, the Stream sort key, the
link's name and tooltip), the Dashboard's Now Playing tiles, and the household streams panel in Settings. The Dashboard's Recent activity
rows are unchanged, as official. No request, URL or polling behaviour changed. Branch `t3/admin-activity-show-title` from Beta
`26f432433`, with Beta through #249 merged in; versions are unchanged. Rule with official file and line, before and after per surface:
`docs/parity/admin-activity-show-title.md`.

Two leftover scaled-display items are in the same change: `LogsWrapPanel` no longer measures a growing child a second time at its own
rounded desired width (it is measured again when it gets more, or clearly less), and the "Open ffmpeg logs" text button has padding with an
equal negative margin and a 4px radius so its corners do not clip the letters.

Review: an independent read-only review confirmed the rule case by case against official and found one regression in the first version of
the wrap-panel change (a growing child given less than it measured was not measured again, so the restart banner's text would have been cut
off instead of wrapping) and one missed surface (household streams). Both are fixed in `8c1551b18`; those fixes were not reviewed again.
The worker that wrote the first version stopped before its final run; the lead finished the unit.

Observed, under the SiloOct18Build lease: final run `acttitle-lead-r1` on `8c1551b18`: 377 focused and 12,025 full unit cases passed, 0
failed; app build, publish and native host build passed; native groups admin-monitor (1,214 checks), admin-dashboard (347),
admin-manage-foundation (36) and admin-configure-general (198) passed with no failed check and exit code 0. `household-contracts` alone on
the same build (`acttitle-lead-r1-household`): 12 checks, none failed. Proven against old code: the first wrap check and the ffmpeg button
check (worker runs `acttitle-proof-r1`, `-r2`); the "given less" wrap check ("offered 250, desired 250x40" in `acttitle-final-r1`).
Captures opened by the lead: the Activity title cases at 1440 and 800 (show first with the episode line under it, no line under a movie,
"Series" under a loose episode, long titles ending in an ellipsis, filter groups on one line, the restart banner wrapping at 800) and the
Dashboard at 1440 (no subtitle under movie tiles).

Not verified: a real server; non-English interfaces (Admin text reads its English table, as the neighbouring lines do); a display scaled to
125% or 150%; the household streams panel was not opened in a capture (unit tests and the `household-contracts` group cover it).

# November 22 — Test stability: three intermittent or failing checks on Beta traced to their causes

Branch `t3/native-test-stability` (from Beta `984044bb5`); tests only, no product source changed. Record: [Native and unit test stability](parity/native-test-stability.md).
(A) Native `admin-plugin-apps` "no valid CoreWebView2": the web view's browser process exits ~700 ms after the page is Ready with `0xC0000005`;
all nine Crashpad reports name `RTSSHooks64.dll` (the RivaTuner/MSI Afterburner overlay hook injected into `msedgewebview2.exe` on this machine).
The product handles it as designed (LoadFailed with Reload); the fixture now restarts that part of the run, at most three starts, only for that
signature, and counts the late request of the "retired page" check after the page finished loading (cause of that second mode not established).
(B) `ServerActivityStoreTests.DashboardSessionsKeepSharedReadErrorsAndAnyEarlierData`: the test cleared the synchronization context of a UI-thread
store, so the real store's thread-pool publishes ran `SyncFeed` concurrently with `Start()` (`Dictionary` insert collision); the dashboard now runs
on a serial context in the test. (C) Native `media-localization` and `media-catalog-localization` exit `0xC000027B` (stowed `0x80131509`): the
fixture's synthetic server reported requests disabled, so `RequestBrowsePage` navigated to `HomePage` in the app's frame, whose
`HomeViewModel` the fixture's container lacks; the server now reports them enabled. A rarer dialog-refresh timing failure of the catalog selector
was fixed by waiting for the condition instead of a fixed pause.

Observed under the SiloOct18Build lease, unchanged Beta: A 7 of 17 runs failed (`stab-base-a-01..09`, `stab-base-b01..08`; 4 at "refused link"
step, 3 at "retired page"); B 2 of 61 recorded runs of the test failed in the lane's history, none in my focused and looped runs; C 3 of 3 failed
(`stab-base-c-media-localization-1`, `stab-base-c-media-catalog-localization-1`, `stab-c-h1`). With the fixes: A `stab-af-01..14` 14 of 14 passed
(runs 04 and 14 hit the crash and recovered through the restart); B `stab-b-final-1..10` 10 of 10 passed; C `stab-c2-*-1..12` 24 of 24 passed.
Final run `stab-final-r1` on `5dc26ef17835679d4f5094d6773450f56d8a7996` (source commits `b33c98cfd`, `8e24013fe`, `1b758ac48`; the later commit is docs only): 15 focused and 11,547 full unit tests passed, no failures; native `admin-plugin-apps` (104 checks), `plugin-cutover` (7), `admin-plugins` (365), `media-localization` (10), `media-catalog-localization` (6), `shell-localization` (4), `admin-manage-foundation` (36) and `admin-monitor` (1,071) passed.
Not verified: the second `admin-plugin-apps` failure mode's cause; a live server or real plugin.

---

**Lead final run (November 22).** Beta through #248 was merged in (`19d5ea1ac`). Final run `stab-lead-r2` on `19d5ea1ac`: 15 focused and 12,006 full unit cases passed, 0 failed; app build, publish and native host build passed; native groups admin-plugin-apps (104 checks), plugin-cutover (7), admin-plugins (365), media-localization (10), media-catalog-localization (6), shell-localization (4), admin-manage-foundation (36) and admin-monitor (1,151) passed with no failed check and exit code 0. The run before it (`stab-lead-r1`) stopped in `plugin-cutover` on "Plugin fixture timed out waiting for the real WebView request", with a Crashpad dump in that run's web view folder: the same browser-process crash, in a group that has no restart for it (its first failure in seven runs in this lane). It was repeated unchanged. No independent review.

# November 22 — Beta test repairs (`oct20-watched`, `realtime-series-state`, `admin-configure-providers`, Logging settings, title-search kinds)

Branch `t3/beta-test-repairs-nov22` from Beta `12fa06ced`, versions unchanged; detail with causes and run labels in `docs/parity/beta-test-repairs-nov22.md`. Product changes: series/season count badges upper-case again (#145 had dropped it), Admin Logging stacked rows no longer cut at 560, collection title search names every item kind in the interface language, the `Request Seasons` menu fallback literal. Fixture changes: `oct20-watched` (a two-season series draws no episode rows; episode label), `realtime-series-state` (stale parameter list and three catalog labels), `admin-configure-providers` (waits for the painted state; 2 of 12 runs failed before, 0 of 12 after). New native check: Logging fits at 560/640/800/1024 in `admin-monitor-logs`, which fails on the old layout (`rep22-dold-r5`); new native group `collections-title-search`. Final run `rep22-final-r1` (source `b3728472c`): focused 1,718 and full 11,867 unit tests passed; native zero FAIL: oct20-watched 5, realtime-series-state 63, admin-monitor-logs 281, admin-manage-foundation 36, admin-monitor 1,071, collections-title-search 3. Not run or not claimed: `synced-sort-direction` (fails here on a missing image decoder component), the Admin shell exit crash, the groups owned by other workers.

**Lead check and final run.** The lead read the product diff (four files) and confirmed in the official stylesheet that count badges are uppercase (`.metadata-badge { text-transform: uppercase }`, `web/src/app.css:1828`), and opened `admin-logging-clean-560x900.png`: the Log level box now has its right border and chevron. Beta through #248 (`26f432433`) was merged in. Final run `rep22-lead-r1` on `a8da46d99`: 741 focused and 12,018 full unit cases passed, 0 failed; app build, publish and native host build passed; native groups oct20-watched (5 checks), realtime-series-state (63), admin-monitor-logs (281), collections-title-search (3), admin-manage-foundation (36) and admin-monitor (1,139) passed with no failed check and exit code 0. `admin-configure-providers` alone on the same build (`rep22-lead-r1-providers`): 251 checks, none failed, then exit `0xC000027B` after the completion line (fixed in #249). No independent review.

# November 22 — Seven small official changes since the pages were compared (official `93794454b`)

`t3/official-small-changes-nov22` (from beta `8c201c07d`; no version number changed; no request body, URL or write behaviour changed) brings the app up to
seven small changes the official WebUI made since the pages were last compared. Record: [docs/parity/official-small-changes-nov22.md](parity/official-small-changes-nov22.md).

- **Changed.** (1) `fd4282d0a` #4667 Backups restore: a setting the environment sets (`managed_by_environment`) reads "value (kept: environment)", has its own review
  caveat, and only a setting the restore writes (`differs`/`cleared`) carries the restart marker. (2) `5641447d6` #4741: seven Backups messages select on the exact
  amount (`AdminBackupText.Amount`/`ExactAmount`), official text verbatim. (3) `6f0cdab81` #5079: the Downloads "By worker" chip is announced "Node gpu-01 2"
  (`AdminPreparationPresentation.WorkerChipName`). (6) `63bbb96ae` #5080: the poster card's watched check and the series page's completed-season check are black
  (`CardWatchStatusIndicator`, new `SeasonCompletedBadge`). (7) `SharedNamesRow` (Add users dialog) no longer re-measures a cell at its rounded width or compares "fits" exactly (one-pixel tolerance).
- **Already matches.** (5) `3fd01aa66` #4534: the app has no queue, pause or replay of any of the four call sites' writes (account and device-override settings,
  subtitle and audio preference, pre-play track selection); a write is sent once and a failure is reported. Differences left: no "You're offline" notice.
- **Not applicable.** (4) `2c75019cb` #5074: the native Editor's Picks title search shows the title only, never the item type. (6b) `7936acbf2` #5083: native
  placeholders are `Image` bitmaps, which cannot tile into the border.

Acceptance: label `small22-final-r14` (lane D:/SiloPlayer-build/admin-completion-lane-a, task admin-families-nov20), stage final on commit `254591a7d`, build lease
2026-11-22T07:19:24Z–07:27:57Z: focused 143/143 and full 11,681/11,681 unit cases; native selectors admin-prepared-files (PASS), admin-user-resources (619 PASS, 0 FAIL),
overlay-revisions (94 PASS), admin-manage-foundation (36), admin-monitor (1,071 PASS, 0 FAIL); source and payload seals verified by the script.
`admin-configure-backups` ran 88 PASS, 0 FAIL and then exited with the known crash code `-1073741189` (0xC000027B) both inside the final run and alone
(`small22-backups-r15`, stage native); it is left out of the passing list. Earlier final runs: r7, r13 failed once in the known intermittent
`AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling` (r10, r11, r14 passed it on the same source); r10 lost the `admin-prepared-files`
host to an exit code -1 before the fixture wrote results (r8 and r14 passed it).
Not mine, failing on unchanged beta `8c201c07d` (`small22-beta-baseline-r9`, `-r12`): native `oct20-watched` ("Both native episode check badges must repaint
without navigation") and `realtime-series-state` ("Mounted series season-count badge did not refresh"); both are left out of the list.
Proved against the old code (the new check fails there): items 1, 2 and 3 in unit focused run `small22-old-focused-r3` (17 failures; the old catalog, restart marker,
chip name), item 3 natively (`small22-old-native-r5`: "was: Node synthetic-node"), item 6 natively (`small22-old-native-overlay-revisions-r6`: "was #FFFFFFFF"), item 7 natively
(`small22-old-native-admin-user-resources-r6`: the 172-wide row measured both probes at their rounded width, trimmed). Not verified: a real 125%/150% display (a fraction stands in for
the rounding), a real screen reader, a server answering `managed_by_environment`, contrast ratios (taken from the official commit message).

**Lead follow-up and final run.** Beta through #244 (`0b0771549`) was merged in. Added: the season badge's check glyph written as an escape; two scaled-display follow-ups in files that #242 and #244 had held (the Autoscan activity wrap panel allows a pixel before wrapping; the Sign-in state chip keeps its word on one line). Test correction: since #240 and #244 merged, Beta fails `admin-user-resources` on "ErrorBrush text on the page surface reads at 4.46:1" (run `small22-lead-r1`): the test host does not run the theme service and keeps a default surface (`#151E2B`) the app never paints, on which the official red `#EF4444` is 4.46:1; on the surface the app's theme paints (`#1C1C20`) it is 4.51:1, the figure official states. The check now judges the red on the theme's surface and requires it to be the theme's red (`937e31808`); the other four colours are checked as before.
Final run `small22-lead-r3` on `937e31808`: 819 focused and 11,886 full unit cases passed, 0 failed; app build, publish and native host build passed; native groups admin-user-resources (620 checks), admin-prepared-files (2), overlay-revisions (94), admin-manage-foundation (36) and admin-monitor (1,071) passed with no failed check and exit code 0. `admin-signin-security` on the same source (`small22-lead-r2`): 322 checks, none failed, then exit `0xC000027B` after the completion line (the Admin shell exit crash fixed in #249). No independent review; the lead read the product diff.

# November 22 — Autoscan activity: an awaited refresh has its next poll armed (cause of an intermittent unit test)

`AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling` failed now and then in full unit runs on several
branches ("Assert.Contains() Failure: Filter not matched in collection. Collection: []" at `AdminAutoscanActivityLifecycleTests.cs:369`),
most recently in `libtabs-lead-r1`. Branch `t3/autoscan-poll-rearm` on Beta `04e4b66b3`; versions are unchanged.

Cause, in the product: `AdminAutoscanActivityController.RefreshAsync` returned the task of its reads and armed the next 15-second poll in a
separate continuation of that task (`ScheduleAfterAsync`). For a moment after a refresh "finished" no poll was pending, and a second refresh
started in that moment moved the refresh epoch on, so the first arming was dropped as well. The test awaited a refresh and looked for the
pending poll in that moment. On the page the gap closed by itself within one continuation, so no admin-visible fault is known.

Change: `RefreshAsync` returns the task that ends after the next poll is armed (the same continuation, now the returned task). Callers
(`OpenAsync`, `ReloadHistoryAsync`, the visible-again read, the poll timer) behave as before apart from that later completion.

New unit test `AnAwaitedRefreshHasItsNextPollArmed` (300 rounds: open, await a refresh, exactly one live 15-second poll). On the old
controller it fails in round 1 (run `rearm-old-r1`, focused: 1 failed of 77).

Observed, under the SiloOct18Build lease: final run `rearm-final-r1` on commit `35facaed6` (plus an experiment commit and its revert, net
no change): 265 focused and 11,856 full unit cases passed, 0 failed; app build, publish and native host build passed; native groups
admin-manage-foundation, admin-monitor and admin-configure-general passed with no failed check and exit code 0. `admin-autoscan-activity`
alone on the same build (`rearm-final-r1-autoscan`): 64 checks, none failed; the process then exited with the known crash code `0xC000027B` after its completion line (fixed in #249).

Not verified: the change was not independently reviewed; how often the old test failed was not measured (it stopped at least five final
runs across branches on November 21 and 22).

# November 21 - Admin Manage Libraries reorganized into Libraries, Scan Activity and Catalog Health tabs (official 93794454b)

**Fix pass (November 22, branch `t3/admin-libraries-tabs`, merged forward with Beta `0b0771549`).** A review and the page's own screenshots found visible defects; fixed and each covered by a native check that fails on the old code
(`AdminLibrariesTabsLayoutNativeFixture.cs`; `CornerShapeChecks.cs` is the same file as on `t3/pill-shapes`): pills and chips follow their size (`RoundedBadge`), the table is as wide as its card at 1440/1280/1024/800/1920 with a
star Status column, wrapping warning rows and wrapping badges, the Library cell is left-aligned, Catalog Health tiles share a row height, the page scrollbar sits in the shell gutter (`IAdminGutterScrollPage`), a failed load shows no
empty message, the card Stop icon is filled. Behaviour: Scan Activity and the summary strip rebuild only when what they draw changes (progress is updated in place, focus is restored with its state), tabs and tiles are one tab stop with
a SelectionItem state, paths and names are isolated for right-to-left, leaving Catalog Health resets it, the scan pill has its tooltip, Stop does what the button showed, refresh Stops are per job (`JobsCancelling`). Weak checks
(strip width, tab height, transitions, filter, in-flight scan, "keeps the selected tile") were replaced. Details: [the parity record](parity/admin-libraries-tabs.md#fix-pass-november-22-layout-defects-and-behaviour-findings).
Observed (lane `D:/SiloPlayer-build/admin-completion-lane-lead`): `libtabsfix-final-r2` (`-Stage final`, passed) — focused `FullyQualifiedName~AdminLibrar` 281 passed / 0 failed, full unit suite 11,906 passed / 0 failed, native
`admin-libraries` 906 PASS / 0 FAIL, `admin-manage-foundation` 36, `admin-monitor` 1,071, `admin-configure-general` 193, all 0 FAIL. (`libtabsfix-final-r1` failed one unit test, the admin-surfaces manifest not listing the new
`AdminLibrariesSelectableButton.cs`; added and rerun.) `admin-autoscan-activity` was left out (known 0xC000027B host crash on Beta). Mutation: `libtabsfix-mutation-r1` ran the native selector with the four `CornerRadius = new(100)` sites and the table-width fitting
put back; the oval-corner checks on all three tabs at every width and the table checks (right edge inside the card, no horizontal scroll, "…" buttons, Actions header, cut-off text, warning wrapping) failed, then the source was restored. I opened the
list, scan-queue and diagnostics captures at 1440, 1024, 800 and 560 and the 1920 capture of `libtabsfix-final-r2-admin-libraries`. Not observed: right-to-left rendering by eye, screen-reader speech, touch, forced colors, scaled displays, an official server.

**Second review and final run (lead, November 22).** A second independent read-only review of the fix pass found no P1 or P2 and gave "safe to submit"; its two P3 items are fixed in `9c43edba9`: a tab or tile chosen with the pointer now takes focus (only the selected one is a tab stop, and a control that is not a tab stop cannot take focus), and the table's `LayoutUpdated` handler lives only while the table is in the tree. Beta through #245 (`12fa06ced`) was merged in. Final run `libtabs-lead-r2` on `36669be98`: 281 focused and 11,906 full unit cases passed, 0 failed; app build, publish and native host build passed; native `admin-libraries` 906 checks, `admin-manage-foundation` 36, `admin-monitor` 1,071, `admin-configure-general` 193, no failed check, exit code 0. The run before it (`libtabs-lead-r1`) stopped on the known intermittent unit test `AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling` and was repeated unchanged. The lead opened the list captures at 1440, 1024 and 1920, Scan Activity at 1440 and Catalog Health at 1440 and 1024: pills are pills, the table is inside its card, the warning wraps, tiles are even; at 1024 the list is cramped (names shortened, pills wrap their text) and nothing is cut off. The two P3 fixes were not reviewed again.

Branch `t3/admin-libraries-tabs` (from Beta `72208bcc4`; no version change; not pushed). The Libraries page (`/admin/libraries`) follows official
`93794454b84aa818979bee19b8e192944395635a` (#5087, the tab reorganization) and the earlier #4994 (`5e26ca784`, ambiguous root types and confidence in the interface
language). **Structure**: a tab strip (Libraries, Scan Activity, Catalog Health) with counts; the selected tab is the route's `tab` value (absent for Libraries); the scan queue
popover and the Catalog Maintenance link are gone from the header; the per-row active-work block is a pill into Scan Activity. **Libraries**: live-activity summary strip,
redesigned table (type tile, "type · folder" button that opens the editor on Folders, status pills, Idle, exact-time popover, Scan/Stop + Edit + a labeled "More actions"
menu, progress line, warning under its row) and cards. **Scan Activity**: loading, error footer, empty and populated states, library filter chips, refresh rows, per-library
cards with labeled progress bars and the collapsed queue, and the footer (Restart jobs, Load older refresh jobs) kept in the empty and error states. **Catalog Health**: five
tiles with counts (Ambiguous only for one library), the five sections embedded, one open at a time, the heavy reads waiting for their tile. **No request is new or changed.**
Not implemented: the official job `message` (the native job row has none), pulsing dots, a Radix remount's reset of section search text. See
[the parity record](parity/admin-libraries-tabs.md) (what each tab holds with official file:line, request table, what is not verified).

Rules and tests: `AdminLibraryTabs`/`AdminPathText`/`AdminRootLabels` (Core) with `AdminLibrariesTabsTests` and three controller tests; the `admin-libraries` native fixture
now drives the existing flows through the new layout, and a new block of the same selector (`AdminLibrariesTabsNativeFixture.cs`) drives each tab and state (counts, route, keyboard, loading/empty/error/populated,
pagination, an in-flight scan across tab changes, the editor, tile lazy reads and Retry, widths 1280/1024/800/640/560).

Observed (lane `D:/SiloPlayer-build/admin-completion-lane-lead`, source commit `de57ca2d9` plus the docs commit): `libtabs-final-r2` (`-Stage final`, passed) — focused filter
`FullyQualifiedName~AdminLibrar` 278 passed / 0 failed, full unit suite 11,560 passed / 0 failed, native `admin-libraries` 740 PASS / 0 FAIL (the page's existing flows on the new
layout, the new tabs block with 128 checks, the remediation and visual-matrix steps), `admin-manage-foundation` 36, `admin-monitor` 1,071, `admin-configure-general` 193, all 0 FAIL.
`admin-autoscan-activity` (not this branch's code) passed 64 checks / 0 FAIL in `libtabs-final-r1` and alone in `libtabs-autoscan-r1`, and the host then exited with the known crash code
0xC000027B (-1073741189) after "native regression run completed"; recorded, nothing changed. The first final run (`libtabs-final-r1`) listed that selector last, so its receipt reads failed
at that exit although every earlier step and selector passed. Checks proven to fail without the change (source restored afterwards): `libtabs-mutation-r1` (unit: `health` no longer selects Catalog
Health, the failed-jobs state becomes the empty state → 5 failing tests) and `libtabs-mutation-r2` (native: the selected tab is no longer kept in the route, and the empty Scan Activity drops
its jobs footer → "selecting Scan Activity puts tab=activity in the route", "choosing Libraries removes only the tab value from the route", "Catalog Health opens on Unmatched Items and puts
tab=health in the route", "End selects the last tab", "Load older refresh jobs stays reachable in the empty state (the footer is preserved)" all FAIL). Not observed: an official server or screenshots,
right-to-left, assistive technology speech, touch, forced colors, displays scaled to 125%/150%. No push, pull request, installer or release.

---

# November 22 — Pills drawn as ovals with pointed ends (Dashboard, Logging settings, Maintenance, Library editor)

Found by the lead while looking at screenshots of another change, then confirmed on Beta `8c201c07d`: the Dashboard's "1 hidden" chip was an
oval with pointed ends instead of a pill. Cause: WinUI limits an oversized corner radius on each axis by itself (CSS reduces it equally on
both), so an element written "rounded-full" with a radius of 999 is drawn as an ellipse. The viewer pages already route such radii through
`Helpers/RoundedBadge` and the Dashboard has `DashboardUi.Stadium`, both of which follow the element's size; these Admin sites did not use
them. Branch `t3/pill-shapes` on Beta `8c201c07d`; versions are unchanged.

Corrected sites: the Dashboard hidden-notices chip (`AdminDashboardPage.cs`), the grid's drop indicator, widget toolbar and size badge
(`DashboardGrid.cs`), the Watch providers error-count pill (`DashboardWidgets.Status.cs`), the chart legend line marker
(`DashboardCharts.cs`, radius 1 for its 2px height), the Logging settings Save and Discard buttons and the floating save bar
(`AdminLoggingSettingsPage.cs`), the Maintenance progress track and fill (`AdminMaintenancePage.cs`), and the Library editor's box-set
size pills and their frame (`AdminLibraryEditorView.cs`, fixed radii just under half the height).

So it is noticed next time: `tests/SiloPlayer.NativeRegressionTests/CornerShapeChecks.cs` finds laid-out, painted elements whose corner
would be drawn more than two pixels longer on one axis than the other; it runs on every Dashboard capture, on Maintenance and on every Logs
capture. It found three of the sites above that a text search for "999" had missed (a radius passed as an argument or chosen by a
condition). `tests/SiloPlayer.Tests/PillRadiusSourceTests.cs` fails when a code file writes a radius of 50 or more without a size-following
helper call, or a XAML file without `RoundedBadge.Radius`.

Fixture correction in `AdminLibrariesNativeFixture.InvokeAsync`: when a re-render replaced the button inside the helper's 100 ms pause, the
helper took the replacement before it was loaded and its check "is rendered and enabled before invocation" failed (trace run
`pills-trace-r1`: loaded=False, size 0x28, enabled, no disabled ancestor). It now waits (bounded) for the replacement to load; the check
itself is unchanged. This race showed in every run of this branch (`pills-final-r2`, `-r3`, `-r4`, `pills-libcheck-r2`) and in neither of
two runs with Beta's product files; why this change shifts the timing was not established. The lead first blamed the size-following helper
in the Library editor and replaced it with fixed radii (`f8f074da9`, whose commit message says so); that was wrong, the fixed radii were
kept because they are harmless.

Observed, under the SiloOct18Build lease: final run `pills-final-r7` on commit `40126c390`: 483 focused and 11,652 full unit cases passed,
0 failed; app build, publish and native host build passed; native groups admin-libraries (611 checks), admin-configure-maintenance (51),
admin-monitor-logs (268), admin-manage-foundation (36), admin-monitor (1,071) and admin-configure-general (193) passed with no failed check
and exit code 0. `admin-dashboard` alone on the same build (`pills-final-r7-dashboard`): 369 checks, none failed, then the known exit code
`0xC000027B` after the completion line (fixed in #249, not merged here). Before the last three sites were corrected the same Dashboard
group failed 31 and then 15 oval-corner checks (`pills-final-r5-dashboard`, `pills-final-r6-dashboard`). Captures opened by the lead:
the Dashboard header chip (now a pill) and the Logging settings save bar (now a pill).

Earlier runs: `pills-final-r1` stopped in admin-monitor-logs on "audit PIN markers use pinned safe wording" and then the process deadline;
that did not repeat in five later runs of the group and its cause was not established.

Not verified: no scaled display; the Library editor pills were not opened in a capture; other Admin pages were not scanned by the native
check (it runs only in the three groups named). Seen and not fixed here: at a 560 px window the Logging settings "Log level" box runs past
the right edge of the page (also on Beta).

# November 21 — Scaled-display layout hardening

Branch `t3/scaled-display-hardening` (from Beta `4b89ef176`) hardens layout code against two faults that only show on displays scaled to 125%
or 150%: a child measured again at exactly its own rounded desired width, or a fit/wrap decision with an exact comparison (shared `LayoutTolerance.Fit`,
1 DIP; the wrap panel arranges the rows it measured), and text clipped by a zero-padding button's rounded corners (padding with an equal negative margin
and radius 4; the shared link template's border is square). Items, outcomes, the skipped ones (owned by open pull requests #239, #240, #242, #244,
#246) and what is not verified are in `docs/parity/scaled-display-hardening.md`. New native fixture `scaled-display-layout` (50 checks; every panel
and button-helper check fails on the old code, `scalefix-mutation-r2`; `scalefix-mutation-r1` was an earlier, weaker fixture). Run `scalefix-final-r1`
(`-Stage final`) on source commit `1336be799`: 175 focused and 11,442 full unit cases passed, 0 failed; native `scaled-display-layout` 50 checks,
`admin-monitor` 1071, `admin-monitor-logs` 268, `admin-libraries` 611, `admin-collections` 203, `admin-playback-audit` 76, `admin-plugins` 365,
`admin-subtitles` 64, `admin-users` 227, `admin-configure-general` 193, `admin-configure-security` 376, `admin-configure-search` 59,
`admin-configure-network-access` 346, `admin-manage-foundation` 36, `admin-user-resources` 242, `retained-library` 70, `poster-captions` 2,
`overlay-revisions` 91, `shell-localization` 4, `oct24-ui` 2, 0 FAIL. Run alone with `-Stage native` on the same build: `admin-dashboard` 339 checks and
`admin-configure-backups` 88, 0 FAIL, "PASS: native regression run completed." present, host exit code 0xC000027B (-1073741189), the known crash on exit.
Not run at a real scaled display; no captures of the changed buttons.

**Review and final run (lead).** An independent read-only review found no P1 and two P2, both fixed in `03e7f18d8`: every log table column had been given 1 to 2 pixels at every scale, so a table that fit its viewport exactly would have gained a sideways scrollbar at 100% (now `LayoutTolerance.ColumnRoom`: one DIP only where the display scale is not a whole number; nothing at 100% and 200%); an action row's result could be arranged a pixel narrower than it measured (`AdminSettingsActionContent`). Also from the review: `NetworkAccessFlexRow` applies no tolerance (the text beside the actions would be left no width); an overlay badge with no room gets none. Added once #239 merged: the Providers action row (audit item A16) allows a pixel before wrapping. Left as reported: where a panel accepts up to one pixel of overflow at 100% the last child can sit up to one pixel past the edge; negative margins extend a text button's hover and focus area 6px past its label. Beta through #239 (`984044bb5`) is merged in. Final run `scalefix-review-r2` on `b41b8311a`: 741 focused and 11,547 full unit cases passed, 0 failed; app build, publish and native host build passed; 20 native groups passed with no failed check and exit code 0, among them scaled-display-layout (54 checks), admin-monitor (1,071), admin-libraries (611), admin-configure-network-access (346), admin-configure-security (376), admin-plugins (365), retained-library (70) and overlay-revisions (91). The run before it (`scalefix-review-r1`) stopped on one new check that tested the wrong coordinate (corrected in the fixture). The review fixes were not independently reviewed again; the table check uses the test switch `LayoutTolerance.AssumeFractionalScale` to stand in for a scaled display.

# November 21 — Admin shell: native groups that exited with 0xC000027B after passing every check

Symptom: on Beta `28f3e8bf4`, `admin-dashboard`, `admin-configure-integrations`, `admin-configure-backups` and `admin-configure-providers` passed
every check, logged "PASS: native regression run completed." and then the native host exited with `-1073741189` (`0xC000027B`, a stowed
exception). `admin-manage-foundation` and `admin-monitor` exited 0. Branch `t3/admin-shell-exit-crash` on Beta `28f3e8bf4`; versions are unchanged.

Introduced by: #227 (`2fa091b34`). Built in lane B with the harness, `admin-dashboard` alone: `8cf35edad` (the commit before #227) exit 0 with
310 checks (`exitcrash-pre227-r1`); `2fa091b34` exit -1073741189 with 310 checks (`exitcrash-at227-r1`); Beta `28f3e8bf4` exit -1073741189
(`exitcrash-base-r1`). The pipeline's repair commits inside #227 touched only `PluginRoutePage.Admin.cs`, `PluginContentPaths.cs` and
`PluginRoutePage.xaml.cs`, which these fixtures never load, so the shell hunks of the original #227 commit are the cause.

What was observed: the WER entry names `CoreMessagingXP.dll`, exception code `0xC000027B`. The minidump (`%LOCALAPPDATA%\CrashDumps`, read with
`dotnet-dump` and a small reader of the exception stream, nothing enabled or changed) shows the faulting thread is the UI thread, the stowed
exception originated on that thread with result `0x80000013` (RO_E_CLOSED, "the object has been closed"), and the frames are only
`CoreMessagingXP`/`combase` under the XAML message loop. No managed frame is involved and the originating native stack is not in a minidump,
so the exact native call is not shown; the cause below is established by the experiments, not by a stack.

Cause: the shell stayed subscribed to the singleton `AdminMonitorAuthority` after it left the visual tree without a Frame navigation (the
fixtures replace the window content; a closing window does the same). `OnAvailabilityChanged` then ran into the unloaded page. Since #227
`Retire()` called `RetirePluginApps()`, which called `RebuildSidebars()`; at desktop width that builds a whole new sidebar, including a new
`BitmapImage` for the wordmark (`ServerWordmark()`), on a page that is not in the tree, just before the process exits. Experiments on the
same fixture, each one variable: removing that `RebuildSidebars()` call: exit 0 (`exitcrash-x1-r1`, repeated `-x1-n2`, `-x1-n3`); keeping the
call but giving the wordmark `Image` no `BitmapImage`: exit 0 (`exitcrash-x2-r1`, repeated `-x2-n2`). A temporary trace of every
`RetirePluginApps` call (removed, tagged `[DEBUG-exitcrash]`) showed why exactly these groups: in all four, the last call comes from
`OnAvailabilityChanged` with `loaded=False` at `narrow=False`; `admin-monitor` and `admin-configure-general` end with their last call at
`narrow=True` (no desktop sidebar is built); `admin-manage-foundation` navigates the shell away, which unsubscribes it, so nothing fires later.

Fix (product, `AdminShellPage.cs`, `AdminShellPage.PluginApps.cs`): `RetirePluginApps` no longer rebuilds the sidebar (every caller that stays
rebuilds after `Recapture`, and `LeaveAdmin` navigates away). The subscriptions are taken by `Attach()` and released by `Depart()`;
`OnNavigatedTo`/`OnNavigatedFrom` use them, and the shell now also departs on `Unloaded` and attaches again on a `Loaded` of the same page,
as the Activity page does. Nothing is suppressed, no exit code is ignored, the harness is unchanged.

Native regression: `AdminShellUnloadNativeFixture.cs`, run by `admin-monitor`: a desktop-width shell leaves the tree without a navigation and
must hold no subscription or lease; an account change then must build no new sidebar on it; the same page loaded again attaches again; a
navigation departure stays retired after its unload. On the unfixed product the new checks fail (`exitcrash-red-r2`, 116 pass, 5 FAIL,
including "an account change after the unload builds no new sidebar on the detached page"); on the fixed product `admin-monitor` has 121 checks.

Real application: Admin is left by a Frame navigation (Back to App, the app sidebar, sign-out, server switch, role loss through `LeaveAdmin`),
so `OnNavigatedFrom` unsubscribes. Before the fix that path rebuilt a sidebar and a bitmap on the page being left; `admin-manage-foundation`
did that twice without a crash, so the page was still in the tree there. No code path found replaces the Admin shell's content without
navigating, so the fault needs the fixtures' teardown. App exit with Admin open is the only real path with an unloaded, still-subscribed shell;
a crash there was neither observed nor ruled out, and the `Unloaded` release closes it. The plugin page (`PluginRoutePage` Admin mode) was
read and not changed: `TeardownAdminContent` stops both timers, cancels the attempt, disposes the lease registration, deletes the cookies and
closes the web view from `OnNavigatedFrom`, `Unloaded` and a retired lease. The administrator's token is still used only by the launch request,
the isolated profile still holds only the launch cookie, and nothing typed or received is logged.

Before and after, every admin and viewer native selector except the nine known failures (228, each run alone, `-Stage native`, tables in
`D:\SiloPlayer-build\admin-completion-lane-b\exitcrash-selector-table-before.csv` / `-after.csv`; "clean" = exit 0, no FAIL line, completion
line logged). Admin selectors (33 with `plugin-cutover` and `shell-localization`): 27 clean before, 33 after. Viewer selectors (195): 106 clean
before and after; the differences there are timing flakes in both directions (`library-letter-feedback` failed before and passed after,
`match-split-review` passed before and failed once after on "Sequence contains no matching element"), and most viewer selectors fail checks on both
builds for reasons that predate this work. Crash exit codes before: `admin-autoscan-activity` (63 checks, completed), `admin-configure-backups`
(36), `admin-configure-integrations` (100), `admin-configure-providers` (88), `admin-dashboard` (310), and also `media-localization` and
`media-catalog-localization` (no completion line). After: only `media-localization` and `media-catalog-localization`. Those two are a
different fault: stowed result `0x80131509` (a managed `InvalidOperationException`), in fixtures that do not use the Admin shell; not
investigated further and not changed.

Final acceptance, label `exitcrash-final-r4`, commit `67d4315f3`: focused 221 and full 11,168 unit tests passed, zero failures; native selectors
`admin-dashboard` (310 checks), `admin-configure-integrations` (100), `admin-configure-backups` (36), `admin-configure-providers` (88),
`plugin-cutover` (6), `admin-plugins` (364), `admin-manage-foundation` (35), `admin-monitor` (121), `admin-configure-general` (187),
`shell-localization` (2), `admin-plugin-apps` (103) all passed with exit 0; source and payload seals verified. Earlier attempts of the same stage
stopped on intermittent tests, not on this change: `-final-r1` on `admin-plugin-apps` (below), `-final-r2` on the full-suite test
`ServerActivityStoreTests.DashboardSessionsKeepSharedReadErrorsAndAnyEarlierData`, `-final-r3` on the same test after it was added to the
focused filter (a "concurrent update on a non-concurrent collection" error inside that Core test; this change contains no Core code).

Stability on that build, `-Stage native`, six rounds: `admin-dashboard`, `admin-configure-integrations`, `admin-configure-backups` and
`admin-autoscan-activity` exited 0 in all six. `admin-configure-providers` exited 0 in rounds 4 and 5, and in rounds 1, 2, 3 and 6 it exited 1 with
86 checks passed and 3 failed ("error: the load failure is announced", "error: Reload is reachable"), never the crash code. That is a timing
race in the fixture's load-error step (it reads the error text right after the controller state changes): the same failure appears on the
unfixed product (1 of 10 runs, with the crash exit code), and 2 of 15 and 2 of 6 further runs of the fixed build. I did not establish why
it occurred in four of six stability rounds, or whether the fix changes its rate. `admin-plugin-apps` is intermittent on Beta itself: 4 failures in
22 runs of the unfixed product (three "nothing from the retired page reaches the server afterwards", one "no valid CoreWebView2") and 3 in 10 runs
of the fixed build, all "no valid CoreWebView2" at the same step, which also occurs once on the unfixed product.

Not established: the exact native call that raised RO_E_CLOSED; whether the real application ever crashed from this; the cause of the
`admin-configure-providers` and `admin-plugin-apps` flakes; the `media-localization` crash.

**Review and final run (lead).** Beta through #238 (`72208bcc4`) was merged into the branch (`f69b041d6`); run `exitcrash-merged-r1` on that head passed (11,512 unit cases; 13 native groups, exit 0 each).
An independent read-only review found no P1 and gave "safe to submit". Its P2: a shell that is unloaded and then loaded again while the Frame still shows it came back with a dead page under it (Activity, Appearance, Autoscan Activity and Marker History do not resume from the shell's suspension); no path in the app does this today (window close asks first and never reloads), only fixtures. Fixed in `3d5902b5a`: the unload is decided one dispatcher pass later (a page removed and put back in the same pass keeps everything), and a shell loaded again opens its current location as a fresh page in place (no new Back entry, Forward kept), which also restores the restart banner and logging health. The fixture now checks that the page under the shell is live after the reload and that no Back entry was added.
Final run `exitcrash-review-r1` on `3d5902b5a`: 279 focused and 11,512 full unit cases passed, 0 failed; app build, publish and native host build passed; native groups admin-dashboard 339, admin-configure-integrations 110, admin-configure-backups 88, admin-configure-providers 89, admin-autoscan-activity 64, admin-configure-storage 86, plugin-cutover 7, admin-plugins 364, admin-manage-foundation 36, admin-monitor 1,083, admin-configure-general 193, shell-localization 4, admin-plugin-apps 104, all exit code 0 with no failed check. The review fixes were not independently reviewed again, and the new reload checks were not run against the code without them.

# November 21 — Admin settings: nothing unseen is sent, the same-address stream switch, a declined departure, the Download limit memory

Four native Admin settings pages follow the newest official WebUI. **Hidden changes (official #4696 `aa06983a6`):** Storage keeps a link-mode row (CDN address, Cloudflare token secret, parameter, lifetime) in view, marked, while it holds an edit after the
mode stops using it (a typed secret is kept, not cleared); Playback does the same for Buffer ahead, the lower-version preference, the marker rows and a staged hardware device list (the picker stays under software or NVENC and can only go back toward the saved list), and drops a
draft for a row the server's capabilities remove; General's "Use Redis" switch-off with no saved URL drops the typed URL and number; Compatibility takes back Jellyfin Web's staged switch-off when no API switch-off is staged. **Compatibility (official #4466 `34931a5cc`):** the new
switch "Play without sign-in from the same address" (`jellyfin_compat.address_stream_grant_enabled`), off by default, official label and warning, one new key in the settings write, changed only by the admin acting on it. **Downloads:** the Unlimited check remembers a limit only when it
is checked (official `LimitField.tsx`), so type 250, Discard, uncheck Unlimited leaves the box empty. **Declined departure:** established that the outer Frame refusing stack edits after a cancelled navigation is WinUI behaviour that ends at the next navigation (the fixture's teardown failure
was only that window), found and fixed one product fault (`NavigationService.RefreshCurrentEntry` threw `0x800710DD` when the unsaved-changes guard cancelled its refresh).
**Review pass (nine findings, commits `3cb54ba68`, `9063dd72c`, `f589ce6fa`, `367e620f7`, `898c3cf03`):** a failed capabilities read on Playback settings is unknown, not false (no draft dropped, last known rows kept); the same-address switch has no row on a server that sends no value; an API switch-off always stages Jellyfin Web off (over a web draft of true too; flip-back returns it to its saved value, as official); each kept device row carries its own changed mark; an automatic refresh (reconnect, access or language change) of an Admin page with a draft no longer opens the unsaved-changes dialog or discards the draft: it is skipped, with nothing run later (an access change that ends the admin role is not skipped; viewer pages and clean admin pages refresh as before; a first version that ran the refresh later from a timer was removed after a second review found it could remount the shell on the Dashboard and outlive the window); the cancelled refresh is detected by a committed-navigation count and answers false; the Redis switch keeps a quick second click. Corrected statements: the hidden writes that remain are any Lazy playback draft (as official), and the request changes are listed exactly in the parity note (device list order, saved-text staging, Downloads uncheck).
Branch `t3/admin-settings-hidden-changes` from `origin/beta` `972980da6`; version unchanged. Official source `2972dc63c3291868ed5d452c9e95ad602d9ed022`. Behaviour with file and line, before and after per page, request changes, changed check expectations and what is not verified:
`docs/parity/admin-settings-hidden-changes.md`.

Observed, under the SiloOct18Build lease, in final run `hiddenset-final-r1` on commit `c0fe9a4e2a3d2f3e921d42d6a45538d13002e156`: 497 focused and 11,318 full unit cases passed, 0 failed; app build, publish and native host build passed; native groups admin-playback-administration (120 checks),
admin-configure-general (194), admin-manage-download-settings (433), admin-configure-security (375), admin-manage-foundation (35), admin-monitor (1,070) and admin-configure-storage (97) passed with no failed check and exit code 0; admin-configure-integrations alone on the same build
(`hiddenset-final-r2-integrations`, 121 checks) passed every check and then exited with the known crash code `0xC000027B`, as on beta. Baseline on unchanged beta: storage 86 checks (also exit `0xC000027B`), playback-administration 110, general 192, integrations 109 (exit `0xC000027B`), download settings 429, no failure.
Review-pass final run `hiddenfix-final-r1` on `898c3cf03c83c34b815be63e0ad0f58aa639e2e4`: 292 focused and 11,344 full unit cases passed, 0 failed; native groups admin-playback-administration 133, admin-configure-general 198, admin-manage-download-settings 434, admin-configure-security 376, admin-manage-foundation 36, admin-monitor 1,071, admin-configure-storage 101, access-navigation 21, shell-localization 4, all exit code 0 with no failed check; admin-configure-integrations alone (`hiddenfix-final-r1-integrations`) 126 checks, no failed check, exit `0xC000027B` after the completion line (known on beta). Mutation run `hiddenfix-mutation-r1`: 12 of 63 focused cases failed with the old behaviour of findings 1, 3 and 5.
Final run after the timer was replaced by the skip and Beta through #238 (`72208bcc4`) was merged in, `hiddenfix2-final-r1` on `bbd7dcd8faaf8a60ab3970f12762efb4d1abcdd6`: 341 focused and 11,579 full unit cases passed, 0 failed; app build, publish and native host build passed; native groups admin-playback-administration 281, admin-configure-general 198, admin-manage-download-settings 434, admin-configure-security 376, admin-manage-foundation 36, admin-monitor 1,071, admin-configure-storage 100, access-navigation 17, shell-localization 4, all exit code 0 with no failed check; admin-configure-integrations alone (`hiddenfix2-final-r1-integrations`) 126 checks, no failed check, exit `0xC000027B` after the completion line (known on beta). The skip itself was not independently reviewed again.
Not verified: no real server, no visual or pixel comparison, no run on a scaled display, `MainWindow` itself is not constructed (its refresh wiring is checked by a source test and reading), the native declined-departure check mirrors `MainWindow.OnAdminNavigating` instead of constructing `MainWindow`, other pages were not audited for the hidden-row pattern.

# November 21 — Scheduled Tasks refreshed against official 2972dc63c (task list and task page)

**Review follow-up (same branch, three source commits `9ca493e45`, `2580ca62c`, `c9b67356b` after `e1404ae35`):** the due-samples table has no sideways scroller and fits its panel at any page width (dates one line from 768 px, wrapping below); server titles, series titles, item types, content IDs and step names are reduced to one line without control, separator or direction characters (joiners kept) and bounded at whole characters; a malformed optional season, episode or series field no longer fails the metrics read; the failed badge reads the app's contrast state; the schedule buttons keep explicit names; `Edit Schedule` can wrap; task-link, breadcrumb and history-expand text is padded out of its rounded corners. Final run `tasksfix-final-r1` (source `c9b67356b`): focused 308 and full 11,273 unit cases, zero failures; native PASS, zero FAIL: scheduled-tasks 179, maintenance 50, general 188, manage-foundation 36, monitor 1,071; `tasksfix-stab-r1`, `-r2`: scheduled-tasks 179 PASS each. Only the item types and the season and episode code in a sample title follow the interface language; the category, task and step names, failure sentences and sentence patterns come from the English-only admin text table. Not established: real-pointer hover, 125% and 150% display scaling, contrast theme, live server, visual parity. Details: `docs/parity/admin-tasks-parity-refresh.md`.

Branch `t3/admin-tasks-parity-refresh` on beta `beeb2b449`, official Silo `2972dc63c3291868ed5d452c9e95ad602d9ed022` (page last compared at `217ff25abc`). Versions unchanged. Record: `docs/parity/admin-tasks-parity-refresh.md`.

Changes: the category badge shows its catalog label; item types, composed sample titles (series, season, episode), failed step names, the fixed failure sentences and the new `backfill_attachment_bitrate` task text follow the official client's wording and rules, with unknown values shown as sent (the wording is English except the item types and season and episode codes); the failed-run badge is the official solid destructive fill under white text (system colors in a contrast theme); links underline on hover; the history and due-samples tables no longer break "Duration" or dates at 560 px (the first pass made the due-samples table scroll sideways below 640 px; the review follow-up above replaced that); schedule rows and buttons wrap. The metrics read gains three optional sample fields; no request URL or body changed. No task action has a destructive confirmation, so the shared destructive style was not touched. Files outside the page: `AdminTasksText.cs`, `AdminTaskModels.cs`, `AdminTasksApi.cs` only.

Verification (lane `D:/SiloPlayer-build/admin-completion-lane-a`): baseline on unchanged beta `tasksref-baseline-r1` passed (55 PASS lines). `tasksref-final-r1` (`-Stage final`, source `49b79d50d`): focused 218 and full 11,226 unit cases, zero failures; native PASS lines, zero FAIL: scheduled-tasks 127, maintenance 50, general 188, manage-foundation 36, monitor 1,071. Two more runs of the scheduled-tasks group on the same build (`tasksref-stab-r1`, `-r2`): 127 PASS, 0 FAIL each. Not established: pointer hover under a real mouse, contrast-theme appearance, live-server behavior, visual or pixel parity; the official refresh page's `sm`/`xl` breakpoints are not reconciled (native uses one 640 px switch).

---
# November 21 — Activity: filter groups and column headers on scaled displays

Reported by the owner from the installed Beta with a screenshot: on Admin > Activity the Direct Play / Transcode and Movies / Series filter
groups showed their segments stacked in a box, with "Movi…" cut off, and the column header letters (Stream, User, Playback, Bitrate, Route,
Time) were clipped at their edges. The page is the Monitor refresh merged as #232. Branch `t3/admin-activity-filter-layout` on Beta
`b63de7c3f`; versions are unchanged.

Cause of the stacking: `LogsWrapPanel` measured each child that is not growing a second time, at exactly the child's own desired width. A
nested panel's desired width is rounded to the pixel grid, so that second measure can offer it a fraction less than its children need, and it
wraps them. At 100% scale every size in a filter group is a whole pixel and nothing is lost; at 125% or 150% a 1px gap or border is off the
grid. Each filter group's strip was itself a `LogsWrapPanel` inside the filter row's `LogsWrapPanel`. The development machine runs at 100%,
so no capture showed it. The display scale of the owner's machine was not confirmed; the mechanism is reproduced by a check that uses a 0.4
gap in a nested panel at 100%.

Change: `LogsWrapPanel` measures only growing children a second time and allows one pixel before wrapping (`WrapTolerance`); a filter's
segments stay a `LogsWrapPanel` strip, one line as the official inline-flex group whenever the row has room for the whole group and wrapped
only when the row is narrower than the group (review follow-up: a strip that could not wrap was clipped at the row's edge on a narrow
window, hiding its last segments); the column header buttons get padding
with an equal negative margin and a 4px radius, because a button's rounded corners clip its content and with no padding they cut into the
first and last letters. `LogsWrapPanel` is shared by many admin pages (Logs, Libraries, Collections, Plugins, Prepared files, Nodes,
Appearance, Playback history, Subtitles, Users, the Admin shell), which get the same correction.

Observed, under the SiloOct18Build lease: with the new native checks and the OLD panel code, `admin-monitor` fails "a nested wrap panel
keeps its children on one line when its rounded width is a fraction short" (runs `activityfix-old-r1`, `activityfix-mutation-r1`: the second
child at y 10). Final run `activityfix-final-r3` on commit `35aabf19c`: 269 focused and 11,272 full unit cases passed with no failures or
skips; app build, self-contained publish and native host build passed; native selectors passed with no failing check: `admin-monitor` (1,118),
`admin-monitor-logs` (268), `admin-libraries` (611), `admin-collections` (203), `admin-playback-audit` (76), `admin-playback-administration`
(111), `admin-plugins` (365), `admin-prepared-files` (2), `admin-subtitles` (64), `admin-users` (227), `admin-configure-appearance` (113),
`admin-appearance-overlays` (75), `admin-manage-foundation` (36), `admin-configure-general` (193); source seal verified. The run before it,
`activityfix-final-r2`, stopped on the known intermittent unit test `AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling`
and was repeated unchanged; `activityfix-final-r1` stopped on a new check that had measured a panel outside the page (corrected in the fixture).

Not verified: the page on a display actually scaled to 125% or 150% (the test host runs at 100%); the header clipping is fixed by
construction (padding at least as large as the corner radius, asserted) and was not compared in a capture at a scaled display. Other custom
panels in the app were not audited for the same second-measure pattern. The review follow-up (wrapping strip kept, the checks that a group
and its segments stay inside the filter row, and the narrowed-row check in `admin-monitor`) was made after `activityfix-final-r3` and has
not been run.

# November 21 — Native group `episode-overview` repaired (fixture only)

Branch `t3/episode-overview-repair` from Beta `beeb2b449` (versions unchanged). Source under `src` is unchanged; only `tests/SiloPlayer.NativeRegressionTests/EpisodeOverviewNativeFixture.cs` changed.

Cause: the failing check "A button post 503 retains the description and offers a manual retry" compared the translate button's content with the literal `Translate description`. Pull request #145 (squash commit `3e25c8641`, "Localize retained desktop interfaces and playback controls", 2026-11-14) replaced that literal in `ItemDetailPage.Overview.cs` with `AppStrings.Get("web.detail.translation.translate", fallback: "Translate description")`. The catalog (and the official WebUI English catalog at `02a9d1d3e`) says `Translate`, so the button shows `Translate`; the fixture was not updated. Fixture, not product: a temporary trace in the failing check (`epoverview-trace-r1`, removed before committing) showed the description kept (series text), pending translation language `fr`, manual-retry mode on, and only the content differing (`Translate`). A user saw no regression; the button label is the official wording. The introducing change was identified from the #145 diff and that trace, not by building an older commit.

Fixture changes: the five `Translate description` expectations now use one constant `Translate` (English catalog value of the official key; "Translating…" expectations were already correct). Second, an intermittent fixture race surfaced after the first fix: `Pixels` captured a freshly decoded spoiler still before its first composed frame and got an all-zero bitmap ("Spoiler still must actually render before comparing pixels", 2 of 6 runs, passing on rerun of the same build); it now recaptures, bounded to 20 attempts with a rendered frame between, and the check still fails if the still never renders. No assertion was weakened or removed.

Runs (lane `admin-completion-lane-a`): `epoverview-base-r1` reproduced the failure on unchanged Beta (13 earlier checks passed, failed at fixture line 502). `epoverview-final-r3` (`-Stage final`, source `7e6a3dbb5`): focused unit filter (EpisodeOverviewTests, EpisodeSpoilerTests, ItemDetail) 274 and full unit suite 11,189 passed, zero failures; native PASS lines, zero FAIL: episode-overview 24, episode-rail 2, detail-action-hover 3, viewer-localization 173, retained-library 70, poster-captions 2, admin-manage-foundation 36, admin-monitor 1,071. Stability: `epoverview-stab-r1` and `-r2` (`-Stage native`, same build) episode-overview 24 PASS each. Not included: `media-localization` ended with host crash `0xC000027B` after its 7 PASS lines in `epoverview-final-r2` (the same crash class as the known groups; product identical to Beta, its fixture untouched), so it was left out of r3.
Not established: that a build before #145 passes; the cause of the all-zero first capture beyond the timing explanation (two intermittent occurrences were seen before the retry; stability is shown by three clean runs after it, not proven).

# November 21 — Shared destructive (red) theme tokens

Branch `t3/shared-destructive-theme`, based on Beta `e02c0e2a0` (version unchanged; Beta has since moved to
`20b58086a`; merged forward with `28f3e8bf4` for the review fixes below). Official pin `02a9d1d3e1b48d86437ad10c0b66ed0e3645d098`. The WebUI has one theme
(midnight-cinema) and one high contrast mode; its red is split into `--destructive` text, icons and borders
(`#ef4444`), `--destructive-fill` (`#dc2626`, white on it 4.83:1) under `--destructive-foreground`, and
`--destructive-label`. Native had one red: `ErrorBrush` was both the text and the button fill (white on the
live `#EF4444` fill 3.76:1; the XAML default was the rose `#EF6B73`). Now `ErrorBrush` stays the text/icon/border
red, new `DestructiveFillBrush` is the button and badge fill, hover is the fill at 90% and pressed at 80% over the
surface (only darker), and `ErrorLabelBrush` and `ErrorTintBrush` cover red labels and error panels.
`ThemeService.ApplyDestructive` resolves them per theme (high contrast, default inheritance) and writes them in
place; a Windows contrast theme repaints every red role with system colors (review fix below, this sentence first said it repainted none). Shared
button style/template, the "Failed" badge, the collection discard dialog, the Prepared files danger pill, seven
sign-in style error panels and the profile editor's validation text moved to the new tokens; admin files in open
pull requests were not touched and are listed with their pull request. Record, contrast tables and captures:
[parity/shared-destructive-theme.md](parity/shared-destructive-theme.md).

Observed: acceptance run `redtheme-final-r8` on source `2b3cb4ed8`: focused unit 34/34, full unit 11176/11176
(an earlier run, `r5`, hit the known intermittent `AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling`
once; `r6`, `r7` and `r8` passed). Native, all checks passed: new `shared-destructive-theme` 40, `shared-controls`,
`shared-appearance`, `admin-configure-appearance`, `shell-localization`, `oct24-ui`, `oct24-shell`,
`retained-library`, `poster-captions`, `overlay-revisions`, `household-contracts`, `profiles-metadata`,
`admin-manage-foundation`, `admin-monitor`, `admin-configure-general/email/security/notifications`; the remaining
selectors (`admin-configure-integrations/backups/providers`, `admin-collections`, `admin-home-sections`,
`admin-signin-security`, `admin-requests-management`, `admin-manage-library-settings`, `admin-subtitles`,
`admin-dashboard`) passed every check when run one at a time against the same payload (`rt-e1` to `rt-e10`). Not
passing, not caused by this change: `subtitle-preferences` fails in its own capture helper (path COMException) on
this branch and on unchanged Beta; `admin-dashboard` ends with a native crash code (`CoreMessagingXP.dll`,
0xC000027B) after all 311 checks, also on unchanged Beta, and `admin-configure-integrations/backups/providers`
end the same way after all their checks (no Beta control run for those three). Not verified: a real Windows
contrast theme (only a simulated palette), visual or pixel parity, the four sign-in style pages' construction in a
selector, player toasts.

**Review fixes (November 21, source `e6e8f6dd2`, on beta `28f3e8bf4`, merge commit `b63eb369a`).** An independent review found no P1 and
four P2. Fixed: (1) in a Windows contrast theme the red roles are now repainted (error text and label WindowText, tint Window,
destructive fill Highlight under HighlightText, hover and pressed copied from the fill) through the public
`ThemeService.ApplySystemPalette`, which replaces the private reflection seam; checked with a dark and a light palette by reading the
live brushes, elements and pixels; (2) the error toast uses `ErrorLabelBrush`; (3) the Downloads failed badge is the label on the
tint; (4) the profile editor validation text follows the contrast theme through `ErrorBrush`. Also onto the roles: the damaged-file
notice, the Subtitle search error panel, the Requests invalid border, the overlay library error, the Prepared files pill tint and the
failed history-import badge (the other import statuses and the run error box stay literals, reasons in the parity record). The
server override input of `DestructiveTheme.Resolve` was removed (production never supplies a red). Corrected figures: the sign-in
panel text improves from 3.83/3.49 to 4.45/4.10 (page/card); native high contrast values are read from the live brushes (9.17 on
the raised surface), the official table uses lightened surfaces. Follow-ups in open pull requests: `AdminNodesPage.cs` (#238),
`AdminGeneralSettingsPage.cs`, `AdminSearchSettingsPage.cs`, `AdminStorageSettingsPage.cs` (#234), Monitor/Logs/Diagnostics/Dashboard
helpers (#232), Devices/Access groups/History import chips (#240).
Observed: `redfix-final-r4` passed: focused unit 73/73, full unit 11206/11206, native `shared-destructive-theme` (92 PASS lines),
`shared-controls`, `shared-appearance`, `shell-localization`, `oct24-ui`, `oct24-shell`, `retained-library`, `poster-captions`,
`overlay-revisions`, `household-contracts`, `profiles-metadata`, `admin-manage-foundation`, `admin-monitor`,
`admin-configure-general/email/security/notifications/appearance`, all exit 0. Alone on the same build, every check passed in
`admin-collections`, `admin-home-sections`, `admin-signin-security`, `admin-requests-management`, `admin-manage-library-settings`,
`admin-subtitles`, `admin-libraries`, `native-subtitle-dialogs`, `toast-diagnostics`, `playback-toasts`, `batch-downloads`,
`admin-prepared-files`, `admin-appearance-overlays`, `history-import-auth`, `save-picker` (exit 0), and in `admin-dashboard`,
`admin-configure-integrations`, `admin-configure-backups` and `admin-configure-providers` (exit 0xC000027B after "run completed"; rerun
once each, same). `admin-configure-providers` failed two checks once (`redfix-e25`) and passed 89/89 four more times; no cause
found. `episode-overview` fails "A button post 503 retains the description and offers a manual retry" after 13 passed checks, also with
src and tests exactly equal to beta `28f3e8bf4` (`redfix-ctl3`), so it is not from this change. Full unit runs `final-r1` and
`-r3` stopped on two updated accessibility token tests and on the known intermittent
`AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling`. Not established: a real Windows contrast
theme, visual or pixel parity, the Downloads page and the error toast outside this branch's own native fixture, why `episode-overview`
fails on beta.

# November 21 — Admin Sign-in settings: review follow-up (failed writes, plugin forms, binding writes)

`t3/admin-signin-parity-refresh`, commits `9a65f391e`, `dbc531d10`, `7a0904e8a` (source), after the independent review of `4e83e4440` (no P1, three P2). A failed write now stays editable with the draft and the
typed secret after a definite refusal that applied nothing (422 validation, 409 provider already enabled, 409 break glass, 429, the page's own break-glass/provider check), and asks for a reload (erasing a typed secret and saying why)
when the page's picture is stale (managed by environment, a failed read, a conflict, an earlier write of the same save already landed, an unknown outcome); only the Sign-in controller decides this, API Keys and Policy keep the shared
rule. A binding write changes only the field the admin touched and restates the rest from the fresh read; if another session changed one of them, nothing is written and the page shows the fresh state. A plugin form with a repeated
or empty key or null lists no longer crashes the page; a secret field is never the login-button field; the no-plugin sentence has bold plugin names. Final run `signinfix-final-r3` (source `7a0904e8a`): focused 250, full 11,051,
native `admin-signin-security` 322, `admin-configure-security` 374, `admin-configure-general` 188, `admin-manage-foundation` 36, `admin-monitor` 113, all passing (`final-r2` failed only the known intermittent
`AdminStorageSettingsControllerTests`); mutation run `signinfix-mutation-r1` 24 failures on the old code; stability `signinfix-native-r3`/`-r4` 322/323 passing. One existing assertion changed on purpose
(`RemovingBreakGlassAfterConfirmationPreventsSettingsWrite`). Corrections, per-answer table and limits: [docs/parity/admin-signin-parity-refresh.md](parity/admin-signin-parity-refresh.md).

# November 21 — Admin Sign-in settings refreshed to official `02a9d1d3e1` (provisioned providers and the rest of the page)

`t3/admin-signin-parity-refresh` (from beta `d1144b96f`) brings Configure > Sign-in settings (`/admin/settings/sign-in`) from official `217ff25abc` to
`02a9d1d3e1b48d86437ad10c0b66ed0e3645d098`. A sign-in provider the deployment provisions (`env_managed` on the capability, the binding and the installation, plus
`callback_mode` and `callback_pending`) shows "Managed by the environment", cannot be turned on or off or edited where it is owned, shows "Pending" while it waits for the public
URL, and never has an owned field in a configuration save, a connection test or a binding write; an older server that sends none of it behaves as before. The page also follows the official layout
(provider cards, numbered setup steps, connection-test result, network section, copy boxes, Retry after a failed plugin read), shows the no-break-glass warning as one sentence, reads setup values and
addresses left to right, puts the official destructive fill on the turn-off confirmations, neutralizes third-party names, and erases a typed secret after an unknown save outcome. A network binding now writes its
own saved `default_login`. Final run `signinref-final-r2` (source `d2433b896`): focused 226, full 11,027, native `admin-signin-security` 262, `admin-configure-security` 374, `admin-configure-general` 188,
`admin-manage-foundation` 36, `admin-monitor` 113, all passing; two stability runs of `admin-signin-security` passed. Environment-managed sign-in settings wait for #234. Shared files touched:
`SiloApiClient.AdminAccess.cs` (also API Keys and Policy; reads one new problem code) and `AdminText.cs`. Record, contract table, difference table and limits:
[docs/parity/admin-signin-parity-refresh.md](parity/admin-signin-parity-refresh.md).

# November 21 — Unit tests: no more frozen runs or start-up timeouts on four cores (test-only)

In the Beta pipeline's VM (four cores) about 17 unit tests failed on the first try in nearly every run and passed when run again alone
(1,373 such failures in 82 runs since November 19), and 11 unit-test runs in a week froze with no CPU use. Both had one cause. xUnit runs
one worker thread per core, and the continuations of every async test run on those same workers. Some tests hold a worker while they wait
for work that needs one (`LibraryBackgroundRefreshTests.Scrolled` uses `GetAwaiter().GetResult()`; `PlaybackDiagnosticsTests` reads the
synchronous `DirectHttpReader.Read`). On two cores, two of them at once froze the run; the stacks of that frozen process are how the cause
was found. On four cores, one or two starve the rest, and the 404 `MpvOscRuntimeTests` cases, which each start a real libmpv core, held
one worker for 24 seconds while about 13 tests a second finished instead of about 900.

Three test-only changes, no product code: `tests/SiloPlayer.Tests/xunit.runner.json` sets `maxParallelThreads` to 16;
`MpvOscRuntimeTests` runs in its own non-parallel collection, after the parallel classes; and in 15 tests that start in the first
seconds of a run, the waits for something that should happen now have the suite's usual 30-second bound instead of 2 to 10 seconds (a
passing run ends at once, and nothing that asserts a timeout was changed).

Observed on this branch at `20b58086a` plus these changes, `SiloPlayer.Tests` in Release under the SiloOct18Build lease, the test
process limited to a set of cores: before, four cores, 10 of 11,157 failed; after, four cores, three runs with 0, 0 and 1 failed (the one,
`ItemDetailWatchedMutationTests.AcceptedWriteAfterNavigationClearsGlobalCacheAndAdvancesWatchRevision`, a count assertion that also fails
now and then in the VM). Before, two cores,
the run froze; after, two cores, three runs finished with 5, 2 and 1 failures, none of them a `TimeoutException` from the changed waits
(`AdminStorageSettingsControllerTests.AcknowledgedJobSurvivesAFailedFirstReadAndIsPolledThroughCompletion` and
`RealtimeReadOwnershipTests.FailedPostFinalSaveInitialReadKeepsTheSnapshotAndInlineRetryAlsoRecoversLeaves` twice each). Not run: the
app build, native groups, and the other two unit projects; nothing they build changed. Two cores are harsher than the VM, so the VM may
still see an occasional first-try failure; the tests that block a worker were left as they are.

# November 21 — Admin Collections refreshed against Silo Server 02a9d1d3e

`t3/admin-collections-parity-refresh` (from beta `d1144b96f`) compares the merged Collections page (built against
`217ff25abc`) with official `02a9d1d3e1b48d86437ad10c0b66ed0e3645d098`. Of the 12 upstream commits over the page's files, most
do not reach it: the pin-button and drag-handle chip changes belong to the viewer's poster card (the native Arrange pin is an
always-drawn, focusable button), the Filters-sheet sort change and "collections a title is in" are viewer features, the sticky
preview panel has no WinUI counterpart, and the 37 admin collection and section operations are byte-identical in the contract.
What changed on the page: names are cut by trimming (start kept, never sliced) and URL boxes read left to right; delete, bulk
delete, delete with rows and delete shelf confirm in the official `#dc2626` fill (90% on hover) through a page-local style, with
system colors in a contrast theme; every name in a confirmation or dialog title is one plain bounded line
(`AdminCollectionNames.Plain`); delete, hide, bulk, result and shelf sentences use the official `en.json` wording. Bulk delete now
confirms and deletes the same set it counts and names the collections kept for rows (nothing opens when every selected collection is
used by a row). Request bodies and URLs are unchanged; no shared control, style or theme file was touched. Record:
[docs/parity/admin-collections-parity-refresh.md](parity/admin-collections-parity-refresh.md).

Baseline: label collref-baseline-r1 (stage publish-native, lane D:/SiloPlayer-build/admin-completion-lane-a) on unchanged beta: native
admin-collections 203 PASS, 0 FAIL. Acceptance: label collref-final-r1 (task admin-families-nov20, stage final) on source commit
4063447cb (docs only changed afterwards). Focused unit run 192/192 (filter AdminCollection, AdminManageFoundation,
UserOnlyClientArchitecture) and full unit suite 11,050/11,050 passed (0 failures, 0 skipped; the known intermittent tests did not
fail); app build, publish and native host built; native selectors admin-collections (283 PASS, 0 FAIL), admin-home-sections (139),
admin-manage-library-settings (84), admin-manage-foundation (36) and admin-monitor (113) passed; source seal and payload verified.
Earlier native runs on the way (collref-native-r1, r2) failed on the new fixture checks and were fixed in source (the shared button
templates take their hover brush from the theme, so the page carries its own template; a name inside a button must not set its own
foreground). Not observed: the official WebUI (no screenshot comparison, no visual or pixel parity claim), a Windows contrast theme,
a pointer hover (the 90% state is checked through the visual state), right-to-left text rendering, screen readers.

Second pass (review fixes, same branch): a confirmed bulk action that never starts keeps its reason instead of a result sentence; bulk Show and
Hide act on, count and send only the collections that differ (**request change: fewer PATCHes; with none to change nothing is sent**);
bulk delete counts as the official client does (404 deleted, 409 kept, 412 and transport failures failed; kept-for-rows named in the
confirmation, not counted again); names in sentences are bounded on their own, placeholders ("Untitled collection", "Untitled library",
"Untitled row", "Untitled") keep counts and names in agreement, more names pass through `Plain`, a name with a line break draws on one line;
the starter-pack block on delete is live and re-checked at the press; delete confirms follow a contrast change while open; focus now really
starts on Cancel in the single delete dialog (the new focus assertion found it on the checkbox); the select bar is docked at the foot of the
page instead of scrolling away. Record: the "Review fixes" section of the page's parity note.

Second-pass runs (task admin-families-nov20, lane D:/SiloPlayer-build/admin-completion-lane-a). Acceptance: label collfix-final-r3 (stage final) on
source commit 14c616aa2 (docs only changed afterwards): focused unit run 180/180 (filter AdminCollection), full unit suite 11,093/11,093
(0 failed, 0 skipped); app build, publish and native host built; native selectors admin-collections (395 PASS, 0 FAIL), admin-home-sections (139),
admin-manage-library-settings (84), admin-manage-foundation (36) and admin-monitor (113) passed; source seal and payload verified.
Stability: collfix-native-r1 and collfix-native-r2 (stage native, same build, admin-collections): 395 PASS, 0 FAIL each.
Revert check: with the product changes for items 1 and 2 reverted (the `started` guard and the never-empty result, and the visibility filter),
collfix-mutation-r1 (unit) fails ABulkDeleteResultIsNeverEmpty… (two cases) and ShowAndHideActOnlyOnTheCollectionsThatDiffer, and
collfix-mutation-r6 (native, admin-collections) fails "delete: the notice says why nothing happened", "the title counts the two that change",
"the confirmation says one is already hidden" and "the result counts what changed"; the code was restored and `git diff` was empty before the final run.
Runs on the way: collfix-focused-r1 (compile), r2 (a lone-surrogate Theory cannot carry a lone surrogate: made a Fact); collfix-mutation-r2 to r5
(fixture compile and cascade fixes); collfix-final-r1 failed the architecture test because the new `AdminCollectionBulk.cs` was not listed in
docs/parity/admin-monitor-surfaces.json (listed; no test was changed) and `AdminAutoscanActivityTests.FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling`;
collfix-final-r2 failed only that Autoscan test, which passes alone (collfix-unrel-r1, 62/62) and in r3 (another family's timer test, not changed).
Not observed: the official WebUI (no visual or pixel parity claim), a Windows contrast theme, a screen reader, right-to-left rendering; focus
was measured in the native test host's window.

# November 21 — Admin user resources refreshed against official `a84b2551b2`

`t3/admin-user-resources-parity-refresh` (started from beta `aca3dcb21`; no version number changed) compares Manage → Users → Access groups,
Devices and History import (merged as #218 against `217ff25abc`) with official `a84b2551b23646380e1eecef45fdbfdbeecdfed3`. Record:
[docs/parity/admin-user-resources-parity-refresh.md](parity/admin-user-resources-parity-refresh.md).

Contract and wording are unchanged for these pages: 562 catalog messages identical to the official `en.json` (six
`subtitleAppearanceDialog` keys added), the 53 operations the pages call and their 102 schemas differ only in two user-detail reads these pages never
make, and no server code changed. No request body or URL changed. Behavior: the device subtitle appearance override editor now saves as an autosaved
draft (official #4543): a refused save drops its edit from the editor and shows the server's reason, edits in flight merge into one save built from the
acknowledged value, an unknown outcome is never replayed and locks the editor and the page until reload, closing drops unsent edits, reset discards queued
edits. Presentation: whole-character avatar initials upper-cased in the interface language, names cut keeping their start (device and group rows,
cards, tabs, members, Add users shares), bounded plain-text names in confirmations and titles, the official danger fill through a page-local style,
a named segmented group with a selected bar, selected tabs/views readable on any system accent, the fleet pulse and run rows wrapping. The
shared `SubtitleAppearanceDialog` changed only behind an opt-in property that only the Devices page sets.

Acceptance: label `userresref-final-r4` (lane D:/SiloPlayer-build/admin-completion-lane-a, task admin-families-nov20), stage final on commit
`19df811ba`, build lease 2026-11-21T12:57:11Z–13:04:39Z: focused 115/115 and full 10,871/10,871 unit cases; native selectors
admin-user-resources (437 PASS, 0 FAIL), history-import-auth (25), admin-manage-foundation (36), admin-configure-general (188), admin-monitor (113);
source and payload seals verified. `final-r2` failed once in the known intermittent `LibraryBackgroundRefreshTests`; `final-r3` on the same source passed.
On unchanged beta `admin-user-resources` failed five scenarios in two runs (baseline-r1, r2) for fixture dialog timing, which the fixture now handles; `history-import-auth` passed.

Review fixes (second pass, commit `525026255`, record section "Second pass: review fixes"): the draft's view no longer loses queued edits between two saves
(the queued edits become the save in flight before notification; an edit during the merged save carries only itself); a draft retired or locked (reset with an
unknown outcome) disables the editor and says so; a write that never left says another change was being saved; a dialog requested while another is closing
waits for it instead of being dropped (the cause of the five beta failures; `AdminDialogSlot`, three pages only) and the fixture presses once per dialog;
selected options keep the platform highlight in a Windows contrast theme; invisible characters (U+200B, U+2060–2064, U+FEFF) and third-party names in member
notices, move lines, accessible names and titles are plain text; native-only wording kept out of the official catalog. Acceptance label
`userresfix-final-r3` (lane D:/SiloPlayer-build/admin-completion-lane-a, task admin-families-nov20): focused 204/204, full 10,905/10,905; native
admin-user-resources 529 PASS / 0 FAIL, history-import-auth 25, admin-manage-foundation 36, admin-configure-general 188, admin-monitor 113; stability
`userresfix-native-r3`–`r5` 529/0 each with 35 of 35 dialogs opened by one press (r1 on earlier source failed the hand-over scenario once); mutation run
`userresfix-mutation-r1` failed 10 new unit tests as intended. Viewer `subtitle-preferences` cannot finish under this lane's script (mixed-slash result path in its own
capture helper); it passed with that path normalised temporarily and reverted (`userresfix-sub-r2`).

Third pass (re-review fixes, commits `40163591d`, `549cf251c`, `da8a4a4ce`, `5ba38e722`; record section "Third pass: re-review fixes"). The re-review found no P1 and
asked for: the subtitle editor now opens through the same dialog slot as every dialog of the three pages (no `ShowAsync` on them outside the slot; a failure to open
shows on the page's error line), and stays busy (button disabled with a reason, slot held) until its last save landed and the page read again, so a reopened editor
starts from the saved value; a locked draft keeps its text when a late answer arrives; a throwing notification cannot leave the draft saving; a write that never left
(page needs a review) is a refusal, not an unknown outcome; the selected look follows the system contrast setting while the page is open; U+3164, U+115F, U+1160,
U+FFA0, U+2800, U+180E, U+00AD, U+034F and the tag characters are neutralised in names; a request that waits 3 seconds for a closing dialog now shows "The previous dialog
is still closing. Press again." Differences from the official hook: a save that throws is an unknown outcome and locks here (the official hook treats it as a failure and
still sends queued edits); Unknown, Retired, Lock and the never-started refusal are native additions. `subtitle-preferences` runs (lead commit `ea3536a30`,
`userresfix-lead-host-r1`: 5 checks). Acceptance `userresfix2-final-r3` (lane D:/SiloPlayer-build/admin-completion-lane-a, task admin-families-nov20), commit `5ba38e722`:
focused 257/257, full 10,936/10,936; native admin-user-resources 611 PASS / 0 FAIL, history-import-auth 25, subtitle-preferences 5, admin-manage-foundation 36,
admin-configure-general 188, admin-monitor 113; stability `userresfix2-native-r1`–`r3` 611/0 each, 37 of 37 dialogs opened by one press, 6 of 6 hand-over rounds pressed
with the slot occupied and not showing. Mutation `userresfix2-mutation-r1`: 18 of 31 new tests failed as intended. Earlier final runs r1 (app build) and r2 (native host
build) stopped on my own compile mistakes. Not observed: a real contrast theme, the order "Edit first, then reset" natively, the 3-second notice natively.

Not observed: a live server, real phones, all-DPI, Narrator and IME, a right-to-left interface or a real Windows contrast theme, complete visual parity
(synthetic captures at 1440/1024/800/560 were opened in part; see the record). No installer or installed-player action occurred.

# November 21 — Providers: review corrections to the subtitle tile's failed-save recovery

Corrections on branch `t3/admin-providers-webui-recovery` (commit `42cc0a11a`, on top of `ab69ec190`) after an independent review. The P1: the Save
path still read the page-wide flags, so after a 412 or unknown outcome on one provider, another provider's Save and Disconnect (drawn enabled) did
nothing; it now reads that provider's state only and every refused click says why. Also: a failed tile Reload drops the editor and the tile reads the
official "Unable to load saved configuration. Reload before editing." (typed values kept, as official); a successful save leaves the tile open on the
official success line, off until Reload; reading shows "Loading saved configuration..." instead of "Saving…"; the test result is cleared only by a
successful save; the marker Reload is marker-scoped again; the page Reload keeps the first failed read visible; a read finishing across an
authority change returns no editor. Finding 9a (controller state surviving an authority change) is not reachable. Remaining difference: the page-level
Reload and its alert are native additions. Record: [Providers failed-save recovery](parity/admin-providers-webui-recovery.md) section 9.

Observed under the SiloOct18Build lease: mutation run `providersfix-mutation-r1` (Save guard reverted) failed the new native check as intended; final run
`providersfix-final-r1` on `42cc0a11a80ba27ebd69187bd1feadba2d531055`: 79 focused and 10,868 full unit tests passed, no failures; native
`admin-configure-providers` (251 checks), `admin-configure-integrations` (101), `admin-configure-general` (188), `admin-manage-foundation` (36) and
`admin-monitor` (113) passed; `admin-configure-providers` passed twice more on the same build (`providersfix-native-r1`, `-r2`, 251 checks each).
Not verified: a live server, real accounts, rendered capture, Narrator, contrast theme.

---

# November 21 — Providers: the subtitle tile's failed-save recovery follows the official WebUI

The owner asked how the WebUI behaves after a failed subtitle-provider save and decided "that's how our app should work in that situation."
Branch `t3/admin-providers-webui-recovery` (from Beta `ae8c5950a`) makes the native tile match the official `ProvidersSettings.tsx` at
Silo Server `a84b2551b23646380e1eecef45fdbfdbeecdfed3`: after any failed save (400, 409, 412, 5xx, lost answer, timeout) the tile is
"save not confirmed" with its fields, Test and Save off, and the tile's own Reload, pressed once, re-reads only that provider, discards
the drafts (typed credentials cleared) and re-arms Save. State is per provider, so one tile's failure no longer blocks the others; the
page Reload also re-reads tiles that are not confirmed. This supersedes the two-step recovery and the retained username recorded in the
#224 fixture-repair entry below. MDBList and marker tiles differ officially (Save is available again after a failure) and are unchanged.
Both disconnect confirmations use a page-local destructive fill (#dc2626, 90% on hover; `DarkTheme.xaml` untouched) and neutralized
provider names (#4704). Environment-managed provider rows (#4665) are left for after #234; avatar initials (#4998) do not apply. Request
bodies are unchanged. Record: [Providers failed-save recovery](parity/admin-providers-webui-recovery.md).

Observed under the SiloOct18Build lease: baseline `providersref-baseline-r1` on unchanged Beta (89 checks pass); final run
`providersref-final-r1` on `ab69ec1909c9fdf72710dd234fd1afd017512fb4`: 62 focused and 10,851 full unit tests passed with no failures;
native `admin-configure-providers` (149 checks), `admin-configure-integrations` (101), `admin-configure-general` (188),
`admin-manage-foundation` (36) and `admin-monitor` (113) passed; `admin-configure-providers` passed twice more on the same build
(`providersref-native-r1`, `-r2`, 149 checks each). Not verified: a live server, real accounts, rendered capture, Narrator, contrast theme.

---

# November 21 - Admin Manage Playback > Nodes refreshed against official 8ae4f4f5a

Branch `t3/admin-nodes-parity-refresh` (from Beta `aca3dcb21`; no version change). The Nodes page (`/admin/nodes`) was built against `217ff25abc`; it now follows
official `8ae4f4f5a0b4a35503048a882695b7170a1604a3`. Upstream changes that reach the page: environment-managed nodes (#4499, `env_managed`), deployment-managed nodes
(#4654, `deployment_managed`, 409 `node_deployment_managed` on key and delete), left-to-right URLs and paths (#4727, #4766, #4753), readable status labels (#4859, #4792) and the
shared dialog and destructive-fill changes. Contract: both new fields are optional (an older server means not managed), malformed values refuse the page, the backend probe `reason`
and device `pci_address` are read under their official names, and a declared field is omitted from the update body (the official page restates it); delete and key are never sent for a
deployment-managed node. Refused writes keep the draft and show the server's reason (bounded, plain text); a lost key or delete answer blocks a repeat. Page: official card structure
(status rail, key line, 28 px actions, acceleration/load/capacity blocks with meters, group filter, banners, empty states, routing heading), managed-node wording, a page-local
`#dc2626` destructive style, neutralized names in confirmations, and key/settings copy that carries no invisible character. The text table matches the official `en.json`
(460 keys, 0 differences). The known flaky native step ("key rotation confirmation opened") was fixed in the fixture and a dialog that follows another is now retried by the page.

Observed (lane `D:/SiloPlayer-build/admin-completion-lane-b`): baseline on unchanged Beta `nodesref-baseline-r1` 99 PASS / 0 FAIL; final `nodesref-final-r2` on `72e3d0c08`: focused filter
211 passed, full unit suite 10,884 passed / 0 failed, native `admin-playback-administration` 206 PASS / 0 FAIL (107 new checks, own block in
`AdminPlaybackAdministrationNativeFixture.Nodes.cs`), `admin-dashboard` 311, `admin-configure-general` 188, `admin-manage-foundation` 36, `admin-monitor` 113, all 0 FAIL; two more
runs of `admin-playback-administration` on the same build (`nodesref-native-r3`, `-r4`) 206 PASS each. Not observed: a live server, real nodes, high contrast, right-to-left
interface, assistive technology, side-by-side visual parity. No push, pull request, installer or release. See
[the parity record](parity/admin-nodes-parity-refresh.md).

Review fixes (second pass), commits `87be653d3` (source, unit tests) and `c4842d505` (native fixture); official reference re-read at `a84b2551b23646380e1eecef45fdbfdbeecdfed3` (nothing on the Nodes page changed since
`8ae4f4f5a`). An independent review found no P1. Fixed: redaction after cleaning, a page-level render-error state with Reload, key-erasure and clipboard checks that can fail (shown by a mutation run),
a single-click dialog helper (every dialog step needed one click), the managed delete announced through UI Automation, an error that survives a managed Delete press, copy refusal reasons (Copy key
checked too), plain backend and device values, shared date formatting in the stale title, a key dialog that stays pending until built, and "nothing sent" told apart from "outcome unknown". No request body or URL changed.
Observed: `nodesfix-final-r1` on `c4842d505`: focused 133 passed, full unit suite 10,903 passed / 0 failed, native `admin-playback-administration` 247 PASS / 0 FAIL, `admin-dashboard` 311,
`admin-configure-general` 188, `admin-manage-foundation` 36, `admin-monitor` 113, all 0 FAIL; `nodesfix-native-r1`, `-r2` 247 PASS each with 15 dialog steps at 1 click each.

---

# November 21 — Admin Notifications refreshed to official 4a1ee058e

Configure > Integrations > Notifications (`/admin/settings/notifications`) is refreshed on branch
`t3/admin-notifications-parity-refresh` (from Beta `2a4c135c8`, `1.2.1324-beta.1`) against official Silo Server/WebUI
`4a1ee058eb3cfc74941f93a4aa57db6b617ca5f0` (the page was last verified against `217ff25abc`). Upstream removed the Processing section,
its paused banner and seven settings (`f42b93271`); they are gone from the page, its key list, text table and tests, no request can carry
them, and an older server that still has them is read without them (stored values keep applying there; a pipeline it reports as paused is said in a read-only notice, added in the QA fix pass). The relay-clear, Discord-clear and
channel-delete confirmations use the official destructive fill `#dc2626` (hover is the official 90 % fill, pressed is native-only, system colors in a Windows contrast theme; page-local, shared style untouched),
the relay and channel URL fields read left to right, channel names in confirmations are neutralized and bounded, and test, rotate and delete
send nothing when the listed channel changed since it was shown. Status pills, channel cards, editor rows, load-failure wording and success
toasts follow the official page. Environment-managed Discord rows are #234's. An independent review of the first pass (no P1) was
fixed in a second pass: the Discord saved toast follows the acknowledgement of that write for its owner, Discord Clear now clears the
editable (not environment-managed) keys as official does (the one request body that changed: with one managed key it sends the other two
instead of nothing), retry buttons return only when a repeat can be sent, refused Test/Rotate and an accepted-but-unreadable relay clear say so,
`Plain` keeps U+200C/U+200D, host and server text are neutralized, card buttons are named for their channel, and empty status pills are
collapsed (the lookup through `TextBlock.Parent`, null inside an unrealized Expander header, was the real cause of the old pill fault);
details and the table of findings are in `docs/parity/admin-notifications-parity-refresh.md`. Plugin destinations (`bd40bd9eb`) are the viewer's Settings page, not this one. Request bodies are unchanged; no shared file changed.
QA fix pass (not built or run): the older-server paused notice above, which adds one line to the shared `AdminConfigureServices.Keys`, and the system keyboard focus rectangle on the destructive confirm buttons.

Runs (lane `admin-completion-lane-a`): baseline `notifref-baseline-r1` on unchanged Beta passed (35 PASS lines). First-pass acceptance
`notifref-final-r1` on source commit `2d6d65f48`: 127 focused and 10,767 full unit cases passed, native `admin-configure-notifications`
(95 checks), `admin-configure-email` (297), `admin-configure-general` (188), `admin-manage-foundation` (36) and `admin-monitor` (113)
passed. Review-pass acceptance `notiffix-final-r2` (`-Stage final`) on source commit `d48704007`: 142 focused and 10,782 full unit cases
passed, 0 failed; native `admin-configure-notifications` 134 checks, `admin-configure-email` 297, `admin-configure-general` 188,
`admin-manage-foundation` 36, `admin-monitor` 113, 0 FAIL. (`notiffix-final-r1` passed on `d5691e1df`; it was repeated because a dry merge with
#234 then showed a fixture conflict and the fixture changed.) Earlier native runs of both passes failed while the work was developed; the record lists them.
The dry merge of this branch with #234 conflicts only in `docs/current-development.md`; the merged result was not built or run.
Not observed: a live server or the official WebUI (layout read from source, no visual-parity claim), pointer hover, Narrator, IME, RTL
rendering, light theme, live delivery.

# November 21 — Backups review follow-up against official 4a1ee058e

Branch `t3/admin-backups-parity-refresh` (base Beta `5b4e102b1`, not merged with newer Beta; version stamps unchanged). An independent review of the refresh below found no P1 (the restore
request, ids, digest, consent and single send are the code of the base); its P2/P3 findings were fixed in commits `2dc2517b5`, `aaaca0973` and `6e9bfe69e`, and the reference moved to
official `4a1ee058eb3cfc74941f93a4aa57db6b617ca5f0` (`en.json` identical to `8d253adbf`). Task reads are ordered by sequence number (a late older answer cannot bring back the stale next-run
forecast; ticks send nothing while a read is in flight); wide restore rows share their line 3:2 (accounts 9:11) like #4978 through a new `ShareRowPanel`, so long names and details end in an ellipsis;
a refused restore keeps keyboard focus on its next step (#4970); the report buttons wrap inside the page (#4989); the shown step resets when the restore view opens or closes; more third-party text
(library names, folders, versions, ids, object and file names) is plain; a null change entry or negative count no longer fails the preview; typed secrets are redacted before text is cleaned; and the
official-strings test renders the official compiled messages instead of comparing the catalog with a second copy. Request bodies, URLs and the restore guards are unchanged; lines pull request #234 edits
were not touched. Details, the table of later official commits (#4978 #4970 #4989 ported, #4982 #4995 already so, #4987 #4988 not applicable) and limits:
[Backups refresh](parity/admin-backups-parity-refresh.md).
Final run `backupsfix-final-r3` (stage final, lane `D:/SiloPlayer-build/admin-completion-lane-b`, source `6e9bfe69e`, source hash `2936cdefd27ad00cab41032dd43207b6994c58563ed11ee6c2fea3b2191f63da`): 242 focused
(`FullyQualifiedName~AdminBackup`) and 10,750 full unit tests passed with no failures or skips, app build, publish and native host build passed, and `admin-configure-backups` (85 PASS lines: 84 checks
and the completion record), `admin-configure-general` (188), `admin-configure-security` (374), `admin-manage-foundation` (36) and `admin-monitor` (113) passed; source and payload seals verified. The new
tests fail without their fixes (`backupsfix-neg-r1`: 13 unit tests; `backupsfix-neg-r2`: the step reset and refusal focus checks). Keyboard use was driven with key messages posted to the fixture's own window.
Not observed: a live server, pixel parity, Narrator, a physical keyboard, right-to-left; that the report buttons ever ran past the page with the old footer.

# November 21 — Backups refreshed against official main 8d253adbf

`/admin/backups` and its restore routes (branch `t3/admin-backups-parity-refresh`, base Beta `5b4e102b1`; `origin/beta` has since moved to `2a4c135c8` and was not merged;
version stamps unchanged) were rebuilt against official Silo Server `8d253adbf00dda7b9714f336dae767c7281b1081` (previous pin `92ee05187`). The one change that broke the page
against a current server was upstream #4757: restore change lists now arrive as codes (`changes` enums on home sections and template collections, plus
`missing_collection_items`), so the native restore details would have printed English words only an older server sends. The page now renders each official
code with the official English word and count, shows an unknown code or an older server's word as sent, and refuses a null entry or a negative count; request
bodies are unchanged. Also fixed from the 50 upstream commits that reach the area: the merge target named by email (#4948), email shown once on an account row
(#4928), exact-count toggle labels and singular blocked-group titles (#4737 #4693, 51 catalog strings pinned verbatim by a test), no next-run forecast while
backups are paused (#4678), the old schedule's next run hidden until the task's daily trigger holds a saved Start time (#4717), no sentence or button about
Back up now in the empty list while it cannot run (#4682), an unreadable destination no longer reads "No backups yet." (#4855), focus and scroll on a restore step
change (#4963), the saved-backups phone layout, window-width breakpoints for the header, table and summary, stacked restore rows, the official review and run width, and
own defects (content wider than the page by the shell's gutters on a narrow window, a body that shrank to its widest child, a wrapping status line). Third-party names, paths and
server messages are plain display text (control characters, separators and direction characters removed, bounded); the confirmation button is a page-local #dc2626 style
that darkens on hover. Environment-managed settings are left to pull request #234. Scope, the contract and difference tables, deliberate differences and limits:
[Backups refresh](parity/admin-backups-parity-refresh.md).
Baseline `backupsref-baseline-r1` on unchanged Beta: `admin-configure-backups` 36 checks passed. Final run `backupsref-final-r4` (stage final, committed source `a997898af`, lane
`D:/SiloPlayer-build/admin-completion-lane-b`): 226 focused and 10,734 full unit tests passed with no failures or skips, app build, publish and native host build passed, and the
`admin-configure-backups` (67 checks plus the completion record, 31 new), `admin-configure-general` (188), `admin-configure-security` (374), `admin-manage-foundation` (36) and `admin-monitor` (113)
native selectors passed; source and payload seals verified. Captures at 1440/1024/800/560 px for the list, unreadable, failed, empty, choose, review, run and report states were produced;
a subset was opened and read against the official source. Not observed: a live server, pixel parity, right-to-left, IME, Narrator, other DPI, and keyboard-only step changes.
Earlier runs: `final-r1` failed only a new focus check, `final-r2` and `final-r3` passed but preceded layout fixes found in captures.

# November 21 — Home sections refresh against official 1a93371 (review fixes)

Review fixes on the same branch after an independent review (no P1): a test that a recycled row's switch and checkbox still act on their own row, and a restore of a control whose report is rejected; official text for the rule builder's operators, Yes / No and RT scores and a text-table home for the rest, with a unit scan that fails on a display literal in the page files; a note when the collection list was cut short; the stale-save Reload row button moved under the fixed notice (and the notice keeps its sentence); header buttons rebuilt only when a breakpoint changes; keyboard focus kept through library and rail rebuilds; collection picker marks updated in place; names in confirmations as one plain bounded line; no item transitions on the page's lists. Corrected: the parity record had said the stale-save notice scrolls away; the notice is fixed and only the button scrolled. Source commits `573534d3a`, `254ce22a4`; final run `homerowsfix-final-r2` under the SiloOct18Build lease: focused 198 and full 10,514 unit cases passed; `admin-home-sections` 256, `admin-manage-foundation` 36, `admin-manage-library-settings` 84, `admin-configure-general` 188, `admin-monitor` 113 native checks passed, none failing. The mutation run for the recycled-switch guard and the details are in the record (`docs/parity/admin-home-rows-parity-refresh.md`). Not observed: a real server, pixel parity, pointer and touch, Narrator, high contrast, right to left.

## The refresh itself (November 21)

Manage > Libraries > Home sections (`/admin/sections`) was brought closer to the current official Sections admin page, on branch
`t3/admin-home-rows-parity-refresh` (from `origin/beta`, five source commits, no version change). Official source: Silo Server
`1a93371807d4762694be3fbb40611375129955b6`; the page had been built against `217ff25abc`. Record: `docs/parity/admin-home-rows-parity-refresh.md`
(upstream changes, the difference table with outcomes, deliberate differences, captures, what is not verified).

Findings first: the "Home rows" rename, the one-list-with-switches-and-row-menu redesign and the Recently added library picker commits are all
ancestors of the old pin, and the admin route (`/admin/sections`), sidebar label ("Home Sections") and page title ("Sections") did not change;
"Home rows" is the profile page under Settings. Upstream changes since the old pin that do reach this page: a Library filter and library names in
the Add row collection pickers (#4471), emphasized row text at full foreground (#4877), the custom-holiday URL left to right (#4766), and rule rows
renamed "Smart row" in the English catalog. The section operations in `openapi.json` and every request body and URL are unchanged.

Native changes: header with More and Add row icons and the under-1024px dock; page pills (replacing the ComboBox) with the row count; the list panel
with icon-tile rows, dashed off rows, hero tags, hairlines and the official emphasis; floating select bar and Select all header; failed-read panel,
warning conflict banner, library note; Next Up defaults card; a step-dialog Add row / Edit row (1000 / 880 px, rail and card picker with variant chips,
Shows summary, footer step count, bottom sheet under 1024px); collection pickers with the library, type and search filters and official strings;
library checkboxes for the From filter; the page-local destructive button style (`#dc2626`, darker on hover and press); English for rule rows;
native-only text moved from literals into `AdminHomeSectionsText.NativeOnly`. Fixed along the way: a recycled switch reported its old state while the
page set a new row's value, which turned a page switch into a conflicting write (now ignored and tested); editor switches and selects only act on a
change the admin made. No shared control, theme or catalog file changed.

Observed, under the SiloOct18Build lease, in final run homerows-final-r1 on commit 510aa0062058dd668e12a084cf80d65fa116ab01: 156 focused and 10,472 full
unit cases passed with none failing or skipped; app build, publish and native host build passed; native groups admin-home-sections (194 checks),
admin-manage-foundation (36), admin-manage-library-settings (84), admin-configure-general (188) and admin-monitor (113) passed with no failures.
Baseline: `admin-home-sections` passed on unchanged beta (homerows-baseline-r1, 139 checks).

Not observed: a real server, pixel comparison with the official page (there is none to compare with; no visual parity is claimed), pointer and touch
drag, hover states, Narrator, high contrast, Text Size, right-to-left, other DPI or languages. Not done: poster peeks, keycap chips, the pill strip's edge
fade and chevrons, a handle-only drag, and restyling the rule builder, seasonal editor, variant control, More options and preview to the official forms.

# November 21 — Admin settings fixed by the server's environment (shared mechanism)

One shared mechanism for settings the server's environment sets, on the General, Email, Downloads, Library, Search, Security, Storage, Notifications, Integrations,
Playback and Backups settings pages: the sensitive-status answer's `managed_by_env_vars` map is read (an older server that sends only the list keeps working), a managed row keeps
its description and shows the official note "Set by {env} in the server’s environment." (the official field message when no variable is named) with the note in the control's help
text, and a draft for a key that becomes managed is dropped and never staged or sent (an emptied Redis database number is the one exception, as in the official form). Branch
t3/admin-env-managed-settings from origin/beta 8ff1c34b1; version unchanged. Official source: f465d2d9af29862f3f76a0ea4e7b1d7d03eb41d0. Behavior with file and line, deliberate
differences and what is not verified: docs/parity/admin-env-managed-settings.md. Not touched (open pull requests or not on Beta): Appearance, Autoscan settings, Sign-in settings / API Keys /
Policy, Providers, Plugins, Prepared Files. Logging has no status read and was not changed.

Observed, under the SiloOct18Build lease, in final run envmanaged-final-r2 on commit 4ed8783c9b77092573aabf7ab3c5eb137c4d63a9: 843 focused and 10,486 full unit cases passed with no failures or
skips; the app build, self-contained publish and native host build passed; native selectors admin-configure-general (192 checks), admin-configure-email (300), admin-configure-backups (40),
admin-configure-security (376), admin-configure-notifications (38), admin-configure-integrations (106), admin-manage-library-settings (87), admin-playback-administration (106),
admin-manage-foundation (36) and admin-monitor (113) passed with no failed check. The first final run (envmanaged-final-r1) failed the architecture manifest test for the new source file (added to
docs/parity/admin-monitor-surfaces.json) and the known intermittent AdminStorageSettingsControllerTests "Timed out: open". Search, Storage and Downloads groups fail on Beta for other reasons
(pull request 231): in envmanaged-native-r1/r3/r4 Search failed only its three Scheduled Tasks link checks, Storage only the two token-default blur checks and its teardown barrier,
Downloads at "uncheck download.server_bandwidth_mbps"; the new environment checks in Search and Storage passed, the Downloads ones did not run. No installer, no real server and no phone was involved.

Review follow-up (same branch, 2026-11-21): a status read that fails after one answered keeps the last managed list in every controller; a `managed_by_env_vars` that is not an object reads as no names;
variable names must be plain identifiers; the General Redis switch follows the environment; the Jellyfin API switch cannot stage the web-off while managed; Search no longer guesses the API key's variable
and keeps its hint; the Backups S3 dialog applies the environment; the Playback device switches are locked and carry the note; the Email retry stays reachable. Details and the review's findings: docs/parity/admin-env-managed-settings.md.
Observed in final run envfix-final-r3 on commit bc903a47f (the later e1966bead changes only the Search native fixture): 848 focused and 10,548 full unit cases passed with no failures or skips; app build, publish and native host build passed;
native selectors admin-configure-general (193 checks), admin-configure-email (300), admin-configure-backups (40), admin-configure-security (376), admin-configure-notifications (38), admin-configure-integrations (110),
admin-manage-library-settings (87), admin-playback-administration (111), admin-manage-foundation (36) and admin-monitor (113) passed with no failed check. Earlier final runs: envfix-final-r1 failed the Email group (retry hidden, fixed),
envfix-final-r2 failed the Playback group (the device list sits in a closed Advanced disclosure; fixture fixed). Known-failing groups, run once each after the final build: Search (envfix-known-search) failed its three Scheduled Tasks link checks and once
"Meilisearch rows are shown while it is selected" (a timing race in the fixture, now a wait; a rerun, envfix-known-search-r2, failed only the three link checks); Storage failed the two token-default blur checks, its teardown barrier and the flow
failure that accompanies it (also seen on Beta, betarepair2-hn-r1); Downloads stopped at "uncheck download.server_bandwidth_mbps". The new Downloads environment checks still did not run. Not run: a real server, a screen reader, an installer.

Combined check by the lead (the native groups of Downloads, Search and Storage fail on Beta until #231 merges, so they cannot pass on this branch alone): on a local-only merge of this branch with the #231 branch (`tmp/envmanaged-with-231`, not pushed), run `envcombined3` (`-Stage host-native` on the app published from that merge): `admin-manage-download-settings` 430 PASS / 0 FAIL, `admin-configure-search` 59 / 0, `admin-configure-storage` 86 / 0; source and payload seals verified. The first combined run (`envcombined-r1`) reached, for the first time, a Downloads step from #179 that expected a failed status re-read to unlock a managed row; it was corrected to assert the kept lock (`9b6ac7ca3`, fixture only).

# November 20 — Admin Monitor parity refresh (official e130f262d) for Beta submission

Branch `t3/admin-monitor-parity-refresh`: the unmerged #207 commit `4565dcce1` merged with Beta `d51eb2d85` (normal merge, two additive conflicts), then refreshed against official `e130f262d412cc7ffc4f0e040bdaba02c132515e`. Versions unchanged (`1.2.1277-beta.1`). Record: `docs/parity/admin-monitor-parity-refresh.md`.

Source: live-row naming change (#4561) for Activity and Dashboard with older-server tolerance; Activity type filter; Concurrent streams Stacked gated on the advertised capability (the open #207 review finding, with tests); Activity realtime status, segmented filters, node menu, rows, hover, links, viewer and stacked lines, expanded panel; Server Activity count badge and live badge contrast; method-tag label colour and red fills; Dashboard widget links, header gap and phone actions; phone header typography. Shared: `AdminMonitorUi`, `ServerActivityButton` (also the main shell), `AdminShellPage`, `DashboardUi.TextLink`, `LogsPageHeader` (opt-in), new `AdminShrinkPanel`.

Review round (independent review of the branch: no P1; fourteen findings, all addressed, see "Review round" in the record): links that rendered but opened nothing (`CanOpenHref` accepted the Dashboard catch-all; `.`/`..` segments no longer resolve), the shared image download deadline (it counted the queue wait; it now starts after a slot is held), link hover fill (own template), Recent activity link underline, Activity search (profile name), search box width below 640, poster corners (8px), realtime status live region, `MonitorSourceProof` (now effective in a default harness run), the Concurrent native fixture (asserts the stored Stacked view; adds "capability advertised, points without the split"), `AdminShrinkPanel` clip and pruning, per-render href probe and row-hover cost, count badge on the live theme brush. Beta `65e9ac880` merged (normal merge; one additive conflict, `docs/current-development.md`; `admin-monitor-surfaces.json` merged without conflict). Source commit `4e50b3a90`.

Verification: `monitorfix-final-r3` (`-Stage final`, source `4e50b3a90`): focused 793 and full 10,387 unit cases passed, zero failures; native PASS lines (assertions and captures, not unit tests), zero FAIL: admin-monitor 1,036, admin-dashboard 339, admin-monitor-logs 268, admin-manage-foundation 36, admin-configure-general 188, admin-playback-administration 99, admin-requests-management 37. The same run's `artwork-first-mount` selector failed 5 overlay/badge assertions; the identical selector fails the same way on this branch with the review fixes reverted (`monitorfix-exp-r6`), so it is not caused by them. Viewer groups `retained-library` (70 PASS) and `poster-captions` (2 PASS) passed on the final source (`monitorfix-viewer-r1`, `-Stage publish-native`). Earlier runs: `monitorfix-final-r1` and `-r2` ended when the native host crashed (fail-fast `0xC000027B`) in `admin-monitor`; bisected (`monitorfix-exp-r1` to `r4`) to re-using one realtime-status element across rebuilt headers; the status is now built per render. The earlier acceptance `monitorref-final-r2` (commit `538187b80`) preceded the review round. Not verified: live server, other themes/locales/DPI, right-to-left, Narrator (the live-region event was not observed by a screen reader), pixel parity (no official server or screenshots were available), poster corners with a loaded image (the fixture's posters do not load). Not done: household streams (`PlaybackSessionSummary`) still reads the pre-#4561 names; it belongs to Settings.

Beta comparison by the lead: `artwork-first-mount` fails on unchanged Beta `8ff1c34b1` with the same five overlay and badge assertions (40 PASS, 6 FAIL lines, run `betacheck-artwork-r1`, `-Stage publish-native`), so the failure is on Beta and not from this branch. It is not repaired here. `admin-monitor-logs`, which fails on unchanged Beta `4a60a2c93` (208 PASS, 10 FAIL in two sweep runs), passes on this branch (268).

Merge with Beta after #207 merged (November 21, local branch `local/monitor-232-with-beta`, nothing pushed): normal merge of Beta `8cf35edad` (merge `a8ba38ee1`); every conflict kept this branch's side because Beta's side was the unmerged #207 commit plus two unbuilt pipeline repairs. Both repairs are ported with their tests (`e8690854d`): SVG server wordmarks decode through `SvgImageSource` by content, and stacked Activity cards show the measured bitrate. One identical duplicated `ImageServiceTests` test left by the merge was removed. Unchanged Beta's Monitor groups pass (`monitormerge-beta-r1`: 113, 311, 232). `monitormerge-final-r1` (source `57014c7cb`): focused 419 and full 11,048 unit cases, native admin-monitor 1,071, admin-dashboard 339, admin-monitor-logs 268, manage-foundation 36, configure-general 188, playback-administration 99, requests-management 37, retained-library 70, poster-captions 2, zero FAIL; `monitormerge-native-r1` repeated admin-monitor and admin-dashboard with the same counts. Record: `docs/parity/admin-monitor-parity-refresh.md`.

---
# November 21 — artwork-first-mount native group repaired (fixture only)

The viewer native group `artwork-first-mount` failed five checks on unchanged beta 8ff1c34b1 (40 PASS, then: custom PosterCard and LandscapeCard
"apply loaded overlay preferences without rebind", custom retained card "refreshes overlays on reload without rebind", "PosterCard keeps unchanged
badge elements through a state refresh and an unload/reload", "PosterCard rebuilds badges when what they show changes"). Cause: the fixture helpers
`BadgeText` and `BadgeElements` in `tests/SiloPlayer.NativeRegressionTests/ArtworkMotionNativeFixture.Overlays.cs` read only the four named corner panels
(OverlayTopLeft/TopRight/BottomLeft/BottomRight). Commit 2c4f6f26a, PR #136 (Complete native overlay presets, styles and uploaded images, 2026-11-11),
made both bottom corners render into one shared `OverlayBottomRows` panel (`CardBadgeHosts.Show`, src/SiloPlayer/Controls/CardBadges.cs) placed beside the
corner panels in the card's artwork grid, so the fixture's bottom-right year badge ("2025", custom variant only) was never found. The product is
unchanged and renders the badge; no user-visible defect, so no product change. The helpers now also read the card's `OverlayBottomRows` panel; no check
was weakened or removed, and the checks still assert "2025", unchanged elements and the rebuilt "2024". The fixture file had last been changed by #117
(2026-11-09), before #136.

Observed on lane D:/SiloPlayer-build/admin-completion-lane-a. Unchanged beta (artworkfix-base-r1, host-native base runs): artwork-first-mount 40 PASS / 5 FAIL
(same five); overlay-revisions 91 PASS / 0 FAIL; retained-library 70 / 0; poster-captions 2 / 0; advanced-overlays crashes the native host (369 PASS, 0 FAIL
lines, then `No service for type RequestsApi` at WatchlistPage.xaml.cs:104, from #184 (522007cf7) whose watchlist code needs RequestsApi that the
AdvancedOverlayNativeFixture service provider does not register); not repaired here, a separate cause. After the fix, final run artworkfix-final-r1 on
commit 93f6db69a: focused filter `FullyQualifiedName~Overlay|FullyQualifiedName~Badge|FullyQualifiedName~PosterCard` 260 passed, full unit suite
10,465 passed, 0 failed, 0 skipped; native artwork-first-mount 46 PASS, retained-library 70, poster-captions 2, overlay-revisions 91, admin-manage-foundation 36,
admin-monitor 113, all 0 FAIL. artwork-first-mount repeated on the same build twice (artworkfix-native-r1, -r2): 46 PASS / 0 FAIL each. No known-intermittent
unit test failed. advanced-overlays was not part of the final run because it fails on beta for the separate cause above.

Added afterwards on the same branch (`430c0d951`, fixture only): `advanced-overlays` is repaired too. Since #184 (`522007cf7`) the Watchlist
page resolves `RequestsApi` from the service provider for its external cards (`WatchlistPage.xaml.cs:104`); the fixture's provider did not
serve it, so the native host ended with "No service for type RequestsApi" after 369 passing checks. The provider now serves the same
`RequestsApi` its Watchlist view model uses. Run `overlayfix-host-r1` (`-Stage host-native`, host rebuilt, against the published app of
`artworkfix-final-r1`, whose product source is unchanged Beta `8ff1c34b1`): `advanced-overlays` 509 PASS / 0 FAIL, `artwork-first-mount`
46 / 0; source and payload seals verified. The 140 checks after the old crash point had not run since #184 and pass unchanged. The full
unit suite was not repeated for this commit; it changes one native fixture file.

# November 20 — Native fixture repairs for six admin groups that failed on Beta

Six admin native groups failed on Beta itself (`4a60a2c93`, still failing identically on `65e9ac880`). The review pipeline's fixer commits
without building or running anything, so each cause below is a pipeline repair that changed a step or the product without a run. Every repair is
test-only: no product source changed. The branch is `t3/admin-users-fixture-repair` (commits `35d81aba7` and `d254f4548`) merged with Beta `65e9ac880`.

- **admin-users** (227 checks): pipeline attempt 4 on #194 (`93f2a5ad4`) added a step that pressed a retired, disabled button through UI Automation
  (Invoke on a disabled button throws `COMException 0x80040200`). The group now asserts the button is disabled, force-enables it and proves the handler refuses too.
- **admin-configure-maintenance** (`AdminMaintenanceNativeFixture.cs`, "default local path restored"): a race. Pipeline attempt 2 on #203 (`f4b7acaeb`) picked a
  detected seed and immediately typed over File Path; the draft changes synchronously but the box is rewritten by a queued render, which overwrote the typed
  text. The fixture now waits for the chosen seed to appear in the box.
- **admin-configure-scheduled-tasks** (`AdminScheduledTasksNativeFixture.cs`, "detail cancellation held"): a race. Pipeline attempt 2 on #201 (`cc1b7712a`)
  pressed the detail button as soon as the controller reported the task running, while the button still read "Run Now" (the render is queued), so a second
  Run was posted and Cancel never was (the request log showed two `POST …/run`). The fixture waits for the button to read "Cancel".
- **admin-configure-search** (`AdminSearchSettingsNativeFixture.cs`): three stale expectations, not a pipeline fault. Scheduled Tasks (#201) is now registered
  in `AdminConfigureServices`, so `AdminSearchSettingsPage` enables both task links (official `SearchStatusPanel.tsx` shows them as links to
  `/admin/tasks/rebuild_catalog_search_index` and `/admin/tasks/sync_catalog_search_index`, whether or not status resolved). The checks now assert both links are
  enabled with no tooltip or help text, also after a failed status read, and a new step presses each link and checks the Scheduled Tasks detail route and
  task key opens, that no task is run, and that Back returns to Search.
- **admin-configure-storage** (`AdminStorageSettingsNativeFixture.cs`): three fixture faults. (a) Pipeline attempt 6 on #199 (`34b63b26c`) checked the restored
  default one dispatcher fence after moving focus, but LostFocus is raised after `Focus()` returns; it now waits for the box to be unfocused and show its default.
  (b) The retirement-draft step invoked Save before the queued render enabled it (the `COMException` `0x80040200`); it now waits for Save to be enabled.
  (c) Pipeline attempt 5 (`e14e5b27a`) added an outer-frame departure that the Frame's Navigating handler cancels; afterwards the outer Frame reports it is still
  navigating for the rest of the run and `BackStack.Clear()` throws `0x800710DD` at teardown. Teardown now retries for 1.5 s, logs a `NOTE:` line and drops the Frame with
  the window content. **Not established:** whether that stuck Frame state is only a test artifact or also happens in `MainWindow.OnAdminNavigating`, which cancels and re-navigates the same way.
- **admin-manage-download-settings** (`AdminDownloadSettingsNativeFixture.cs`, three steps): pipeline attempt 1 on #179 (`2d54f8239`) made the Unlimited check box restore the
  last limit the box showed (including a discarded, never-saved draft), where the earlier steps (and attempt 2's zero-spelling loop, `1d1cb7bd7`) assumed unchecking leaves the box blank.
  The steps now wait for "unchecked, editable and not Unlimited". **Product observation, not changed:** the official `LimitField.tsx` remembers a limit only at the moment Unlimited is
  checked, so after typing 250, Discard and unchecking Unlimited the official page shows a blank box where the native page shows 250. Attempt 1 chose this on purpose
  (the programmatic-Unlimited step); it needs an owner decision.

Observed, under the build lease, in final run `betarepair2-final-r1` on commit `d254f4548`: 579 focused and 10,333 full unit cases passed with no failures; app build, publish and native host build passed;
native groups passed: admin-configure-maintenance (50 checks), admin-configure-scheduled-tasks (55), admin-configure-search (55), admin-configure-storage (84),
admin-manage-download-settings (424), admin-users (227), admin-manage-foundation (36), admin-configure-general (188), admin-monitor (113). Runs `betarepair2-native-r1` and `-r2` repeated the six repaired
groups on the same build with no failures (Storage had 83 checks in r1 and 84 in r2: "forced closure retains review ownership and content until Closed" runs only when a Closing event fires).
Earlier iteration runs (`betarepair2-pn-r1`, `-hn-r1` to `-hn-r5`) found the causes above. Not run: `admin-collections`, `admin-requests-corrections`, `admin-configure-providers`, `admin-signin-security`, `admin-libraries`, `admin-monitor-logs`.

# November 21 — Household streams: the live-session fields under their current official names

Official Silo Server `c7c155927` (#4561) renamed the playback session fields with no alias: `media_item_id` (was `content_id`),
`series_title` (was `series_name`), and `media_title` now carries the episode title (`episode_name` is gone). The household streams list
(`GET /api/v2/profiles/household/sessions`, Settings > Profiles) returns the same session shape (`internal/apiv2/profiles.go`, checked at
official main `e130f262d412cc7ffc4f0e040bdaba02c132515e`). The native panel read only the old names, so on a current server an episode
lost its "S1E3 · Show" line and its title was no longer a link. Branch `t3/household-streams-session-names` on Beta `65e9ac880`; versions
are unchanged. This is viewer code (a household manager need not be an administrator).

Change: `PlaybackSessionSummary` gains `SeriesTitle` and `MediaItemId`; `SessionDisplayText` takes the current names first and the older
ones when they are absent (`GetSeries`, `GetItemId`, the episode title from `media_title` on a current server and from `episode_name` on an
older one), and shows "Series" for media type `episode` without series details, as the official `getPlaybackSessionSubtitle` does. The
title link in `SettingsPage` uses `GetItemId`. The unit fixture `list_household_sessions_ok.json` is again identical to the official
contract fixture.

Observed, under the SiloOct18Build lease, in run `householdfix-lead-final-r1` on commit `74a2cb5ab`: 55 focused and 10,342 full unit cases
passed with no failures or skips; app build, self-contained publish and native host build passed; native selectors `household-contracts`
(12 checks), `profiles-metadata` (11), `admin-manage-foundation` (36) and `admin-monitor` (113) passed; source seal verified.

Not verified: no native group serves household session rows, so the panel itself is covered by unit tests of its text and item id only;
a live Silo server; what an older server sent in `media_title` for an episode (the older path is kept as it was).

# November 20 — Playback history: the end position under its current official name

Official Silo Server `9df62cd42` (#4552) renamed a history entry's `watched_seconds` to `end_position_seconds` with no alias (the value is
where the play ended, not how long it was watched). Checked against official main `e130f262d412cc7ffc4f0e040bdaba02c132515e`. Until this
change the native Admin Playback History reader required the old name and refused every page from a current server with "Invalid playback
history page", the item "View play history" dialog showed 0 seconds for plays without a measured watch time, and the two history lists on a
user's page showed an empty value. Branch `t3/history-end-position-contract` on Beta `4a60a2c93`; versions are unchanged.

Change: every reader takes `end_position_seconds` first and `watched_seconds` when the current name is absent, so a server from before the
rename keeps working (`AdminPlaybackHistoryApi.Project`, `AdminPlaybackHistoryWireEntry.EndPositionSeconds`,
`PlaybackHistoryPresentation.Format`, and a `current|older` column form in `AdminUsersPage.AddRows` used by the two history lists, whose
label is now "End position seconds"). An entry with neither name is still refused. The account summary's `watched_seconds` total is a
different, unrenamed field and is untouched. The live-session row rename of `c7c155927` (#4561) is not part of this change; it belongs to
the Monitor refresh.

Observed, under the SiloOct18Build lease, in run `historyfix-lead-final-r1` on commit `e645b0b84`: 212 focused and 10,269 full unit cases
passed with no failures or skips; app build, self-contained publish and native host build passed; native selector `admin-playback-audit`
passed. On the same build, run `historyfix-lead-native-r2`: `oct24-ui` (the play history dialog, now fed the current name),
`admin-manage-foundation` (36 checks) and `admin-monitor` (113) passed.

Found during this run and not caused by it: the native selector `admin-users` fails on Beta itself. Its 162 checks pass and the run then
ends with `COMException 0x80040200` from `AdminUsersNativeFixture.InvokeAsync` in `PreviewInterruptionChecksAsync`
(`AdminUsersNativeFixture.cs:397`). The same failure, with the same counts, was observed on unchanged Beta `4a60a2c93` in run
`betacheck-users-r1`. It is not repaired here.

Not verified: a live Silo server (the break and the fix are established from the official contract, the official client and synthetic
fixtures); the two history lists on a user's page have no test for the renamed field.

# November 21 — Manage › Libraries: review fixes

An independent read-only review of the Libraries refresh found no behavior regression and eleven items, fixed on the same branch. Destructive badges and
buttons now use the official fixed `--destructive-fill` #dc2626 (a Core constant, `AdminLibrariesPalette`) with the page's own button style, so rest,
hover and pressed (90%) all keep at least 4.5:1 under white; the shared `DestructiveButtonStyle` is unchanged. The scan queue popover is 16 px. The touch
Stop button draws its states on a 20 px square inside its 44 px hit area. The phone editor footer has no gap without an error. The table header is 44 px
(official `h-10` is a minimum under `border-box`) and the editor close icon is centred 25 px from the dialog's top and right edges. `FlexWrapStretchPanel`
caches natural widths and counts gaps for unbounded width; `CardActionRow` keeps every child. The native fake channel now sends the scans snapshot only
when the channel's ref-count rises from 0 to 1, and a fresh page opened while the activity store holds `scans` shows its scan rows and queue from the
store (no product defect found); the visual matrix restores the server list in a `finally`. Findings, outcomes and guards are in `docs/parity/admin-manage-libraries.md`.

Observed, under the SiloOct18Build lease, in final run librariesfix-final-r4 on commit d7ecf34ac (source and payload seals verified by the script): 168
focused (filter `FullyQualifiedName~AdminLibraries`) and 10,282 full unit cases passed with no failures or skips; app build, publish and native host build
passed; native admin-libraries (611 PASS lines), admin-manage-library-settings (84), admin-manage-foundation (36), admin-configure-general (188) and
admin-monitor (113) passed with no FAIL lines. Earlier runs of this label were repeated for fixture defects only: final-r1 (the new fixture file lacked a using for
`LayoutInformation`), final-r2 (the seed scenario re-opened the controller in place without re-opening the diagnostics as the page does, so the Unmatched
section never loaded), final-r3 (one check asserted the stop button's own unused Background instead of its transparent hit-area grid); no product change
was made between them. Captures opened: 1440 × 900 list, editor General and delete-confirm (the popup layer is not rasterized by this host, so the dialog itself
is not in that capture); 800 × 600 list; 560 × 800 editor (Advanced). Not seen in any capture: the scan queue popover and card menu (popup layer) and the hover
and pressed looks (verified through the resolved colours of the real visual states, not pictures). Not established: pixel or visual parity, a live server, real pointer input.

Beta merge and final run: Beta `65e9ac880` (Autoscan Sources, #219) was merged with a normal merge (`1599224fd`); the only conflict was this
file, where both sides' entries are kept. Final run `librariesref-lead-final-r2` on `1599224fd`, under the SiloOct18Build lease: 168 focused
and 10,348 full unit cases passed with no failures or skips; app build, self-contained publish and native host build passed; native
selectors `admin-libraries` (611 checks), `admin-manage-library-settings` (84), `admin-manage-foundation` (36), `admin-configure-general`
(188) and `admin-monitor` (113) passed; source and payload seals verified. The run before it on the same commit,
`librariesref-lead-final-r1`, stopped in the full unit suite on one case of another page,
`AdminStorageSettingsControllerTests.AcknowledgedJobSurvivesAFailedFirstReadAndIsPolledThroughCompletion(previousTerminal: True)`
("Timed out: open" after 2 ms); nothing was changed and the run was repeated.

# November 20 — Manage › Libraries refreshed against official e130f262d

The Libraries page (`/admin/libraries`: list, editor, scan queue, diagnostics, dialogs) on branch `t3/admin-libraries-parity-refresh`
was compared with official Silo Server `e130f262d412cc7ffc4f0e040bdaba02c132515e` (the page was built against `17d5376cea9a`).
Versions are unchanged. **No official server was running and there are no official screenshots**: the layout work is a source comparison
plus captures that were opened and read (the parity document lists which); pixel parity is not claimed. Library settings and Autoscan are separate and were not touched.

Upstream, only two changes reach this page: matcher queue paths always left to right (already true in the native `Mono`) and the solid
`destructive-fill` under white text on red badges and buttons (done). The Libraries operations (72 paths, 157 schemas in their closure) and every
catalog string the page prints are unchanged, so no request or response shape changed. The page's differences were all in layout: the
WebUI theme radius is .75rem, so `rounded-md/lg/xl/2xl` are 10/12/16/20 px (the page used 6/8/12/16); table cell padding was 8 not 12 and the
Actions header was right aligned; secondary badges had no visible fill; buttons were 13 px text with 14 px icons; the editor dialog, tabs,
diagnostics panels, dashed empty boxes, phone footer, touch stop buttons and the flex-wrap header actions below 1024 px differed; and dates ignored the
user's date and time format settings (now the shared `DateTimeDisplay`). The full difference table, upstream list, shared-control differences that were
noted but not changed, and limits are in `docs/parity/admin-manage-libraries.md`.

The native fixture had a defect on Beta: its fake channel never delivered the scans snapshot that the controller waits for, so scan rows never
appeared and the fixture timed out at its scan-queue step. It now answers a scans subscription with the snapshot and waits for the rows. A new
visual-matrix fixture captures the page at 1440, 1024, 800 and 560 device-independent pixels in each main state (populated list with scans
running, diagnostics, scan queue and card menu, editor General/Folders/Metadata/Advanced, a destructive confirmation, empty list, failed read
with a working Retry) and asserts the geometry it is read against. Popup content cannot be rasterized by this host (the presenter capture is
empty and the content-only capture has no popup background), so the scan queue popover and card menu were checked through automation, not
pictures. Some 1024 and 560 captures taken right after a resize are wider than the window with blank margin (a stale frame width at capture
time); the page content in them is laid out at the window width.

Observed, under the SiloOct18Build lease, in final run librariesref-final-r2 on commit 3e2dd11bc7558b33b9a5f7581246c33bbdd64754 (source and
payload seals verified by the script): 156 focused (filter `FullyQualifiedName~AdminLibraries`) and 10,270 full unit cases passed with no failures
or skips; the app build, self-contained publish and native host build passed; native selectors admin-libraries (500 checks),
admin-manage-library-settings (84), admin-manage-foundation (36), admin-configure-general (188) and admin-monitor (113) passed with no failures.
The earlier final run librariesref-final-r1 passed the unit suites and then failed in the Libraries remediation scenario because that fixture's
service provider had no settings service for the shared date formatter; the fixture was corrected and the full run repeated. Not run: the
admin-collections, admin-requests-corrections, admin-configure-providers and admin-signin-security groups (known failing on Beta for reasons
repaired elsewhere). Not established: a live server, real pointer drag, the poster file picker, IME, Narrator, RTL or another locale, all-DPI, or
visual parity with the official page.

# November 20 — Admin Plugin Apps (navigation group and isolated plugin page)

Branch `t3/admin-plugin-apps`, merged with Beta `32f95df8d` (version unchanged). Official pin
`df37bfc6d57b1680c0f7a59e51e15c8d0cfa6065`. The Admin sidebar and search list "Plugin Apps" exactly as
`buildAdminPluginNavItems`; an entry launches the plugin like `navigateToPluginRoute` (`POST
/api/v2/auth/plugin-launch`) and opens the plugin page in an InPrivate WebView2 profile that holds only the
issued `silo_plugin_access` cookie (HttpOnly, plugin-content parent path) and may load only that installation's
pages and assets. No host objects, web messages, account headers, popups, downloads, permissions or external
protocols; native states with Reload for not available, 401, 403, 5xx, load failure and timeout; teardown on
leaving, removal and any authority change. The previous source-only draft (bearer proxy on a virtual origin)
was replaced. Security fix in the viewer plugin page: account headers are now attached only inside
`/api/v2/plugin-content/` instead of on every same-origin request. Details, decision table and limits:
[parity/admin-plugin-apps.md](parity/admin-plugin-apps.md).

Observed: `-Stage final` run `pluginapps-final-r1` on source `fa25ccc7c` passed: focused unit 91/91, full
unit 10351/10351, native `admin-plugin-apps` (89 checks, including a request log proving the admin token
reached only the launch request), `plugin-cutover`, `shell-localization`, `admin-manage-foundation`,
`admin-configure-general`, `admin-monitor`. Not run: other native groups. Not verified: named third-party
plugins, WebSocket/WebRTC from plugin script, printing and screen capture, a server behind a base path, visual
parity of plugin content.

Independent security review (read-only, commit `8bfe9f050`, official main `4762d23bc9ddd92c884777aee9f90122b4ac2da0`, no relevant
change since `df37bfc6d`). It verified the official model with file and line and corrected one statement: the launch cookie is not
the official page's only credential in effect, because the WebUI keeps its refresh token in browser storage on the same origin as
plugin pages, so official plugin script can read it; the native pages are stricter. No defect was found in the admin page's
credentials, navigation confinement, web view hardening, the Admin shell edit or the tests. Three defects were reported and fixed:
the viewer's account headers could follow a same-origin redirect from a plugin-content URL to another server API (a request that
leaves the plugin-content path now has every account header removed; `PluginContentPaths.AccountHeaderNames`, tied to
`SiloApiClient.AddHeaders` by a test, and a new `plugin-cutover` step where the plugin answers with a redirect to an admin API); the
viewer check refused plugin-content paths with an escaped percent sign, which the official cookie authenticates (now accepted for
the viewer check; encoded separators and dots are still refused); and a failed web view process left the admin page blank with no
Reload (it now drops the session and shows the load-failed state; no test). The redirect leak was inferred from documented web view
behavior and was not observed happening before the fix.

Final run: `pluginapps-lead-final-r2` on commit `66b3c04431eaa5082434ea0beaa4f0008921a595` (Beta `32f95df8d`): 116 focused and 10,356
full unit cases passed with no failures or skips; app build, publish and native host build passed; native selectors
`admin-plugin-apps` (89 checks), `plugin-cutover` (7), `shell-localization` (4), `admin-playback-administration` (99),
`admin-manage-foundation` (36), `admin-configure-general` (188) and `admin-monitor` (113) passed; source and payload seals verified.
The run before it, `pluginapps-lead-final-r1`, stopped on one new unit case written with a wrong expectation (an escaped ".." that
stays inside the plugin-content path after normalization is inside it); the case was corrected, no product code changed. Beta was
stamped `1.2.1277-beta.1` (`d51eb2d85`) after this branch's last merge and is not merged again.

Review fixes after that run, not yet run: Reload after the web view's browser process exits now replaces the web view (the old one
could not load again), and GPU, utility and frame process failures no longer close a working page; a shown page keeps its own later
navigations (fragment or history navigation, the plugin's own 4xx/5xx response) instead of being replaced by a failure state, except
for a 401 or a navigation with no server response. Three native `admin-plugin-apps` steps were added (fragment navigation, plugin 404
on a later navigation, killed browser process then Reload). A further review fix, also not yet run: when a plugin sets its fragment
while its page is still opening and the opening navigation completes first, its result is kept and applied instead of leaving the
page on Loading until the timeout; one more native step opens a page that sets its fragment in an inline script. A third review
fix, also not yet run: a plugin that sets its fragment twice while its page is opening no longer ends in the load-failed state or
the timeout. The opening navigation is kept through any number of same-document navigations and any completion order
(`AdminPluginPageNavigations`, with unit cases for the orders), and the native step also opens the page setting its fragment twice.

# November 17 — Admin Monitor corrections verified for Beta submission

The Monitor worktree `t3-34a4a55a-46179de38e52767b` is based on exact Beta
`248aafd62ed65b868c0e7356926e4d83204649cd`, unchanged version
`1.2.1181-beta.1` / `1.2.1180.1`. Official WebUI pin
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb` defines custom Admin branding,
Activity reasons/bitrate/details and retained IP Lookup, Concurrent streams
presentations, and the confirmed Dashboard/Logs catalog changes in this increment.
Other Admin families and pipeline-owned Now Playing/Plugin Apps workflows remain
separate; no version stamp or installed-player change is included.

Observed final verification: r7 fresh nonincremental units **562 focused / 8,057
full**, zero failures/skips; r8 **527** affected/source-reading cases, app build
and publish passed with unchanged sealed unit inputs. All eight native groups
completed in r11 against the exact rehashed r8 product: Activity corrections,
shell branding, Concurrent streams, Monitor, Dashboard, Logs, Manage foundation
and Configure General. Product/reference/host DLL/PDB/PRI equality, source/PDB
proofs and the final **2,315-file** source seal passed. Wide/compact changed-state
captures were inspected. Review corrections cover the observed Lookup COM crash,
cached route variation, menu-only termination, and bounded shared image transfers;
the final bounded independent recheck reported no findings.

Final actual lease: ACQ `2026-11-17T12:49:55.3843759Z`, REL
`2026-11-17T12:52:27.7185261Z`, PID 44140. No owned runtime or mutex waiter
remains. Inclusive preflight **27.9610 GiB** stays below the 50 GiB cap and both
drives exceed the free-space floor. Outputs/evidence reuse
`D:/SiloPlayer-build/admin-completion-monitor`; earlier payloads and receipts
remain preserved. See the [scoped verification record](parity/2026-11-17-admin-monitor-completion.md)
for per-group counts, fixture-only continuations and exact provenance.

This is a verified coordinator Beta-submission candidate, not a stable release.
Live-server interoperability, all DPI/themes/locales, IME/Narrator and complete
visual parity remain uncertified. Installed player PID 39668 and settings remain
untouched. Source/evidence are retained for the Beta pipeline handoff.

---

# November 20 — Admin Plugins (Configure > Integrations > Plugins) built and verified

`t3/admin-plugin-management` completes `/admin/plugins` and `/admin/plugins/:pluginId` against official
`3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e` (the first source was written against `217ff25abc`): the board with Installed
and Catalog tabs, searches and filters, provisioning status, the update check, the community-catalog control, install from a
file, repositories, an installed plugin's page (status banners, schema-driven settings forms with secrets and managed
fields, sign-in and scheduled-task bindings, update, restart, uninstall) and the catalog preview. Every write is sent once,
against the row that was confirmed, with an unknown outcome blocking that plugin until it is read back. Plugin notification
destinations belong to Settings > Notifications (a person's own), not to these pages; Plugin Apps are another family and
their entries stay disabled. Beta `536e4e2d7` (1.2.1257-beta.1) was merged; no version number changes. Scope, deviations
and limits: [docs/parity/admin-plugins.md](parity/admin-plugins.md).

Running the code found real defects, each now fixed with a test or a native step: a secret field filled from a server
value, `Build` on a disposed draft, a type-name collision with Providers' `AdminPluginCapability`, the C# 14 `field`
keyword in property getters, in-page writes held behind the discard dialog while a form was merely dirty, and a launcher
set after the page was drawn being ignored.

Independent review of November 20 (commit 2bba737ad, official df37bfc6d57b1680c0f7a59e51e15c8d0cfa6065, no P0) found six
defects, all confirmed and fixed with tests: typed secrets stayed in a form after an accepted or unknown save when the
re-read returned the same values (the form is now erased and drawn again; refusals and unsent saves keep what was typed;
the repository form follows the same rule); a multi-select tick deleted stored values the options do not list (one-value
add/remove, and every untouched field goes back exactly as stored); text from plugins, publishers and repositories could
forge a line or reorder text in the install, update and uninstall confirmations (one text gate for every third-party text;
the plugin ID and version stand on their own lines); a managed field failing its own rule blocked Save; a task-binding
failure used the sign-in binding message; a 10-minute deadline over a whole upload (each request now has its own limit).
Details: [docs/parity/admin-plugins.md](parity/admin-plugins.md).

Acceptance after the review: label plugins2-final-r1 (lane D:/SiloPlayer-build/admin-completion-lane-b, task
admin-families-nov20), stage final on source commit e7081f338 (docs only changed afterwards), build lease
2026-11-21T02:21:59Z-02:28:25Z. Focused unit run 249/249 (filter AdminPlugin, AdminManageNavigation, AdminBackupsNavigation)
and full unit suite 10,417/10,417 passed (0 failures, 0 skipped); app build, publish and native host built; native
selectors admin-plugins (365 PASS, 0 FAIL), admin-playback-administration (99), admin-requests-management (37),
admin-manage-foundation (36), admin-configure-general (188) and admin-monitor (113) passed; source seal and payload
unchanged. Earlier acceptance before the review: plugins-final-r2 below. Not observed: a live server, a slow real link,
all-DPI/RTL rendering, the OS file picker, screen readers, Plugin Apps.

Acceptance: label plugins-final-r2 (lane D:/SiloPlayer-build/admin-completion-lane-a, task admin-families-nov20), stage
final on the family's source (committed unchanged right after the run; only docs changed afterwards), build lease
2026-11-21T01:29:12Z-01:35:01Z. Focused unit run 213/213 (filter AdminPlugin, AdminManageNavigation, AdminBackupsNavigation)
and full unit suite 10,238/10,238 passed (0 failures, 0 skipped); app build, publish and native host built; native selectors
admin-plugins (265 PASS, 0 FAIL), admin-manage-foundation (36), admin-configure-general (188) and admin-monitor (113)
passed; source seal and payload unchanged. Not observed: a live server, all-DPI/RTL rendering, the OS file picker, screen
readers, Plugin Apps.

# November 20 — Sign-in settings no longer rebuild themselves while the admin types

The "admin-signin-security" native group failed on beta at "typed group list is kept as typed (16 characters)". Cause, from
a diagnostic run that logged why each rebuild happened: the provider choice (a RadioButtons) reports its initial selection
again after Build() has finished, and the page answered every report with `_built = false; Render()`. That built a new
form with a new RadioButtons, which reported again, so the whole form was recreated about 20 times a second for as long as
the page was open (the first rebuild was caused by the provider list, all the others by the selection report). Any box
the admin was typing in was replaced: focus, caret and text that is not a value yet (a trailing line break or space in a
list, "1." in a number) were lost. A new account list was not the trigger in that run: the page does rebuild for a new
Accounts instance, but that can only land on Open and on "Load more accounts", while inputs are disabled.
AdminSignInSettingsPage now rebuilds on a selection report only when the admin chose a different provider, and a new
account list updates just the break-glass status line and the "Load more accounts" button in place (`SyncAccounts`); the
form is rebuilt only for a new answer from the provider read: its list, the capabilities, its error, or whether the list
is known. The last three are recorded because an unavailable or failed read leaves the list the same empty instance, and
the account-driven rebuild used to be what drew their notice; all four land only on Open and after a write, while inputs
are disabled. Nothing is staged or sent by either path and request bodies are unchanged.
API Keys and Policy were checked for the same fault and have none: neither reads in the background, API Keys keeps its text
boxes in dialogs that Render never touches (its table has no inputs), and Policy rebuilds only when tab, document or
editor revision changes through an admin action, with its boxes disabled while a read runs.
Fixture: the three API Keys steps that open a dialog right after closing another (ApiKeyTier_7 twice, ApiKeyRevoke_7) now
click until a new dialog is open (`ClickForDialog`), because the page ignores a click that lands while the previous
dialog is still closing. New step: with a list longer than the page cap, the same group box stays on the page through an
idle wait and through "Load more accounts" (typed list unchanged), and the break-glass line and button update in place.
Added after review, and after the runs below (so not in their counts and not yet observed): a capabilities read that
fails, and one that reports external sign-in unavailable, each answered after the form is first shown, must show their
notice and show or hide the email-match row.
Observed, under the SiloOct18Build lease, lane D:/SiloPlayer-build/admin-completion-lane-b, source commit 32ea99e8f on
origin/beta ce9ec928d: `-Stage final` run `signinfix-final-r1`: focused unit tests 183 passed, full unit suite 10,141
passed, 0 failed; native admin-signin-security 165 passed, admin-playback-administration 99, admin-manage-foundation 36,
admin-configure-general 188, admin-monitor 113, none failed. Then admin-signin-security alone twice more
(`signinfix-native-r1`, `signinfix-native-r2`, same published payload): 165 passed each. Not verified: typing by a person
with a real keyboard or IME, and the other native groups (collections, requests-corrections, configure-providers), which
fail on beta for other reasons.

# November 20 — Three merged Admin native groups brought back in line (test-only)

On Beta `fb8cb17cf` four native regression groups of already merged Admin pages failed: `admin-collections`, `admin-configure-providers`,
`admin-requests-corrections` and `admin-signin-security` (each run on its own on a pure Beta build, labels `beta-check-r1-*`). Each page had
been changed after submission by review-repair commits that were not built or run. Branch `t3/beta-native-fixture-repairs` changes three
fixtures and no product code:

- `AdminCollectionsNativeFixture.cs`: the page drops a title the server rejects (400, 404 or 422) and names it in a "Not added:" notice,
  the rule its last repair introduced; the step still asserted the earlier rule (the title stays staged and is sent again). It now checks
  the notice and that the rejected title is not sent again.
- `AdminProvidersNativeFixture.cs`: reading one subtitle provider's editor no longer clears an unknown outcome or a conflict; only a
  page-level re-read of the provider list does. The step still expected the tile Reload alone to re-arm Save. For 503 and 412 it now
  presses the page Reload, then the tile Reload. Recovery after an unknown outcome or conflict on a subtitle provider therefore takes two
  Reload presses; that is the merged behavior and is not changed here.
- `AdminRequestsCorrectionsNativeFixture.cs`: two timing faults. After a failed Load more the row actions sit inside a re-created
  ScrollViewer and enter the visual tree one layout pass after the notice, so the step now waits for them. The "failed download poll"
  case leaves the poll at 50 ms and sampled "notice collapsed, rows shown and timer running" at one instant while rows are redrawn and
  the timer is stopped during each read; each fact is now waited for and reported on its own.

Observed, under the SiloOct18Build lease, in final run `repair-lead-final-r1` on merge commit `ce2b1102a70c9852a8185a3e4bb26b73d5319d8b` (Beta
`ce9ec928d` merged normally, no conflict): 139 focused and 10,141 full unit cases passed with no failures or skips; the app build, publish and
native host build passed; native selectors `admin-collections` (203 checks), `admin-configure-providers` (89), `admin-requests-corrections`
(135), `admin-requests-management` (37), `admin-playback-administration` (99), `admin-manage-library-settings` (84),
`admin-manage-foundation` (36), `admin-configure-general` (188) and `admin-monitor` (113) passed; source and payload seals verified. Before
that, `admin-requests-corrections` passed three times in a row on the same fixture (`repair-check-r2-reqcorr-a/b/c`).

Not covered here: `admin-signin-security` still fails on Beta at "typed group list is kept as typed". That one is a page defect (the Sign-in
form is rebuilt when the account list is re-read, replacing the box being typed in) and is being fixed in its own change. The viewer group
`advanced-overlays` was reported to end on a missing `RequestsApi` service in its fixture; that was not checked on pure Beta and is not
addressed here.

# November 20 — Admin Configure > Appearance (with card overlay editor) built and verified

`t3/admin-server-appearance` completes Configure > Server > Appearance (`/admin/settings/appearance`): logos and
icons (four branding slots), accent color, card overlay defaults with the live preview and per-badge images, the
badge-image library and the title-art default, registered through the real Admin shell. Beta `fb8cb17cf` was merged
normally at the start; this branch changes no version number. Official contracts: `66b836f82a32de2fb3e5dfae6c285c19eea1096d`
(the earlier source cited `217ff25`; the page, overlay components and strings are unchanged between the two, and the
form's new env-managed row state does not apply to any Appearance key).

The combined source had never been compiled. It was reworked to the official model: admin overlay defaults are written
as the whole canonical document at any manifest revision (the server stores `defaults.card_overlays` as any JSON), all
registry overlays are offered except RT ratings whose source the server hides, the five official positions, the
badge-image library as its own group with the inline choose/name/upload flow, Restore defaults staging the official
built-in document, toggles reacting to Toggled/Checked, failed overlay/branding/capability reads shown with Reload, a
separate unknown-image-outcome block, server refusal text for image writes, and type/size checks before a picked file is
read. Shared changes (viewer registry defaults corrected to the official registry, additive `image_uploads_available`
and show-status image lookup) and the one changed existing assertion are listed in
[docs/parity/admin-appearance.md](parity/admin-appearance.md).

Acceptance: label appearance-final-r1 (lane D:/SiloPlayer-build/admin-completion-lane-a, task admin-families-nov20),
stage final on commit 37b87e02f, build lease 2026-11-20T23:48:16Z–23:54:24Z: focused 297/297 and full 10,207/10,207
unit cases (0 failures, 0 skipped); app build, publish and native host built; native selectors
admin-configure-appearance (113 PASS lines, 0 FAIL), admin-appearance-overlays (75), overlay-revisions (91),
overlay-pill (2), admin-manage-foundation (36), admin-configure-general (188) and admin-monitor (113) passed; source and
payload seals verified.

Not in the passing list: `advanced-overlays`. In a publish-native run on the same source (appearance-native-r13) it
logged 369 PASS lines and then the host exited 0xC000027B on an unhandled `No service for type RequestsApi` from
`WatchlistPage.ExternalRepeater_ElementPrepared` (WatchlistPage gained that dependency in Beta #184; the fixture's own
service provider has no RequestsApi). Neither file is changed by this branch; the fixture was not modified.

Not observed: a live server, a real OS file picker, decoding of real uploaded branding images, all-DPI/IME/Narrator,
complete side-by-side visual parity (synthetic captures at 1440/800/480 and of the editor/library were inspected).
No installer or installed-player action occurred.

Independent review (read-only, commit `c7f1bb822`, official main `3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e`; nothing this page uses
changed since `66b836f82`, and no Appearance key can be environment-managed). The shared viewer changes were confirmed against the
official registry one by one (`web/src/lib/overlays/registry/ratings.ts` and `ribbons.ts`): no existing viewer's cards move; the only
visible difference is that Advisory Age now draws right after Age Rating, as on the web. It reported two P1 and two P2 defects, all
fixed: a settings save after an unknown image outcome hid the explanation that offers Reload, leaving image writes blocked with no way
out; every acknowledged save or image change reset loaded badge images to text without announcing the refresh, so library thumbnails
and unchanged preview badges stayed blank; the editor document was written with the default .NET encoder, which escapes "+" and
non-ASCII text, so undoing an edit left the form edited; and uploads shared the fixed 30-second admin deadline, so a large image on a
slow uplink always ended as an unknown outcome (an upload now gets 30 seconds plus 10 seconds per MiB; the shared deadline helper
gained an optional limit, default unchanged). The thumbnail correction has no test of its own.

Final run: `appearance-lead-final-r2` on merge commit `f993888e3ef03ceacc19400eacf5eadbdabbd9f0` (Beta `536e4e2d7` merged normally,
no conflict): 257 focused and 10,207 full unit cases passed with no failures or skips; app build, publish and native host build
passed; native selectors `admin-configure-appearance` (113), `admin-appearance-overlays` (75), `overlay-revisions` (91), `overlay-pill`
(2), `admin-playback-administration` (99), `admin-manage-foundation` (36), `admin-configure-general` (188) and `admin-monitor` (113)
passed; source and payload seals verified. The run before it on the same source, `appearance-lead-final-r1`, stopped on one unrelated
unit test, `LibraryBackgroundRefreshTests.ContentRefreshRejectsPreMutationViewportAndHeldWindowSnapshots` ("Collection was modified"),
which has failed and passed unchanged on other branches; it was not modified. Beta moved to `ce9ec928d` (#216, a viewer-only change to
the Playing Next screen) after that run and was not merged again.

---

# November 20 — Admin Manage Prepared Files completed against official 66b836f82

Manage > Downloads > Prepared Files (`/admin/downloads`) is complete on branch `t3/admin-prepared-file-management`, merged with
Beta `f36e3bd4a` (`1.2.1194`) and compared with official Silo Server/WebUI `66b836f82a32de2fb3e5dfae6c285c19eea1096d` (the first
draft used `217ff25abc`). Five tabs (Storage, Preparation, Prepared files, Device copies, History), the server and node location
editors, every official `web.admin.downloads.*` string, the `download_preparations` and `downloads` realtime channels with the
official 60 s polls, single-send writes with refusal versus unknown-outcome handling, and a native fixture covering failed reads and
refused, stale and unknown writes. Final run `prepfiles-final-r4` passed 150 focused and 9,899 full unit cases and the four native
selectors `admin-prepared-files`, `admin-manage-foundation`, `admin-configure-general` and `admin-monitor`.

An independent review of that commit (`c9bc4d83c`, merged with Beta through `fb8cb17cf`) found ten defects (two P1, eight P2); all ten were
confirmed and fixed with tests: tab switches no longer wait on a reload of the tab being left; the page updates rows and controls in
place instead of rebuilding tables on every progress reading, poll or selection toggle (a click on Cancel, Pause or a check box lands and
focus stays); device entry reads have one slot per device; a live refresh re-reads every loaded page; a partly failed delete shows as an
error; values in confirmations are never parsed as markup; no server timestamp can throw from a timer calculation; dismissing the node
editor mid-save is an unknown outcome, never a stuck page; every confirmation sends only under the authority it opened under; dropped
requests under `/api/v2/admin/downloads/*` and `/api/v2/admin/nodes` log one fixed category. Final run `prepfiles2-final-r5` on source
commit `82aca54fa` passed 83 focused and 10,207 full unit cases and the native selectors `admin-prepared-files`,
`admin-playback-administration`, `admin-manage-foundation`, `admin-configure-general` and `admin-monitor`. `admin-signin-security`
failed in this environment ("typed group list is kept as typed") in two full runs and two isolated runs on files this branch does not
change (the Sign-in page is exactly as merged from Beta `7b0b8012d`); `admin-playback-administration` failed once in two earlier runs at
"key rotation confirmation opened" and passed on unchanged reruns. Not observed: a live server, real realtime delivery, light theme,
every DPI/width, a native reproduction of the authority-retirement race, and pixel parity. Details, upstream differences and deliberate
deviations: [scoped note](parity/admin-manage-prepared-files.md).

Final run on current Beta: Beta `ce9ec928d` (the `1.2.1257-beta.1` stamp and #216) was merged normally (merge commit `68114501d`; only this
file conflicted) and one mis-encoded dash in this entry's heading was corrected (`3ec17ad67`). `prepfiles-lead-final-r1` on `3ec17ad67`
passed 83 focused and 10,224 full unit cases with no failures or skips, the app build, publish and native host build, and the native
selectors `admin-prepared-files`, `admin-playback-administration` (99), `admin-requests-management` (37) and
`admin-manage-library-settings` (84); the `admin-manage-foundation` group then logged its 36 checks and "run completed" and the host
process ended with an access violation in a COM finalizer on exit (0xC0000005), which stopped the run before `admin-configure-general` and
`admin-monitor`. The same crash on exit was seen once on another branch earlier today. `prepfiles-lead-native-r2` then ran
`admin-manage-foundation` (36), `admin-configure-general` (188) and `admin-monitor` (113) on the same build with the source and payload
seals verified, and all three passed. Left out because they fail on Beta itself: `admin-collections`, `admin-signin-security`,
`admin-requests-corrections` and `admin-configure-providers`.

---

# November 20 — Autoscan Activity aligned with the official inspector sheet

`/admin/autoscan/activity` (branch `t3/admin-autoscan-activity`, merged with `origin/beta` `44c8927a2`; version stamps unchanged by this branch) was
completed and rebuilt against official Silo Server `88b825d57e4b2296050fda69eaf619eefbf208d1`. The earlier source targeted `217ff25abc`; upstream
`e3887af09` (#4631) then replaced the page's inline poll details with a paginated inspector sheet, so the native page now has the poll table with
Details buttons and badges, the inspector (Paths / Scans created tabs, search, outcome and status chips, pagination, scan-id jump, bounded scrolling error,
focus return, keep-the-latest-snapshot) and `TablePagination`-style numbered paging (cursor walk, 100-page bound). Scope, deviations and limits:
[Autoscan Activity](parity/admin-autoscan-activity.md).
Final run `autoact2-final-r1` on the committed source: 59 focused and 9,896 full unit tests passed with no failures or skips, the app build, publish and
native host build passed, and the `admin-autoscan-activity` (48 checks), `admin-manage-foundation`, `admin-configure-general` and `admin-monitor` native
selectors passed; captures at 1440/1024/800/560 DIPs and the inspector were inspected. Not observed: live-server/phone interoperability, all-DPI and
screen-reader behaviour, and a fixture-driven row click (only the Details button path).
That run predates an independent review (read-only, commit `0bc5eb4fc`, official `66b836f82a`), which found nine defects. All were confirmed and fixed: the pager gap was mis-encoded text; a page or
filter change also re-read the status and the names; an older reconciliation read could overwrite a newer status; an unknown cancellation could stay blocked until the page was reopened (now
lifted by the admin's own successful status read, with both outcomes shown and tested and the cancel still never replayed); finished scans re-ran the name lookups; a trailing space in the search
rewrote the box and closed the inspector; keyboard focus was lost on inspector tabs, chips, the scan-id jump, both pagers and the 15-second refresh; inspector chip filters reset themselves; and every
row of every walked history page was validated. Events with more than 5,000 runs now keep the page and say so. One extra defect was found natively and fixed (a delayed text-changed event after a
programmatic search reset sent the inspector pager back to page one). One earlier assertion (blocking Cancel after a successful reconcile while the scan was still listed) was replaced because the rule changed.
Final run `autoact3-final-r1` (stage final, committed source `24f5cc9df`, lane `D:/SiloPlayer-build/admin-completion-lane-b`): 73 focused and 10,023 full unit tests passed with no failures or skips, app build,
publish and native host build passed, and the `admin-autoscan-activity` (62 checks), `admin-requests-management` (37), `admin-manage-foundation` (36), `admin-configure-general` (188) and `admin-monitor` (113)
native selectors passed; source and payload seals verified. Not observed: live-server/phone interoperability, all-DPI and screen-reader behaviour, a fixture-driven row click, and visual inspection of the
new pager-gap and focus states beyond the fixture checks.

Final run on current Beta: `autoact-lead-final-r5` on merge commit `f76e73d14b32279ee38a8cc34789f601166a1cad` (Beta `44c8927a2`, the `1.2.1257-beta.1` stamp on top of the
merged Collections and Sign-in pages): 73 focused and 10,197 full unit tests passed with no failures or skips; app build, publish and native host build passed; native
selectors `admin-autoscan-activity` (62 checks), `admin-playback-administration` (99), `admin-requests-management` (37), `admin-manage-library-settings` (84),
`admin-manage-foundation` (36), `admin-configure-general` (188) and `admin-monitor` (113) passed; source and payload seals verified. The merge of Beta `fb8cb17cf`
(`8ba4b10f5`) was committed with conflict markers in five files by mistake and corrected in the next commit (`1548a3552`, both sides kept); every run named here is on
source after that correction. Two earlier runs of this branch stopped on native groups of pages already merged into Beta (`admin-collections` in
`autoact-lead-final-r1`, `admin-signin-security` in `autoact-lead-final-r3`) after this page's own group had passed; on pure Beta `fb8cb17cf` four such groups fail
(`admin-collections`, `admin-signin-security`, `admin-requests-corrections`, `admin-configure-providers`), independently of this branch, so they were left out of the
final run and are being repaired separately.

# November 20 — Autoscan Settings completed for Beta review

Branch `t3/admin-autoscan-settings` (merged with current `origin/beta` `67c94ba05`, `1.2.1194`). Page
`/admin/settings/autoscan` is rebuilt against official `66b836f82a32de2fb3e5dfae6c285c19eea1096d`; the first source used `217ff25abc`, and the
settings page, connections panel, hooks, handlers and autoscan strings are identical between the two pins (only `SettingField` env-managed rows,
which this page does not use, changed). Fixed from the never-compiled source: connection bodies were reflected as dictionary properties
instead of JSON; 4xx answers were treated as unknown outcomes and a typed key was cleared on a refused save; a stored kind/URL that failed a
client check was blanked; server problem details were replaced by generic text. Added: subtitle, loading skeleton, recoverable read failures,
group-heading action, icons and badges, server check reason, redaction before truncation, official strings. Parity, safeguards and limits:
[docs/parity/admin-autoscan-settings.md](parity/admin-autoscan-settings.md).

An independent read-only review of the built branch confirmed the API key handling (an untouched key is never sent, a key typed for one
connection cannot be sent for another, the key survives a refusal and is cleared otherwise), every request body and both number ranges, Test
connection, Delete and the unknown-outcome handling. It reported two P2 defects, both fixed with tests: a dropped Autoscan request wrote the
failure text, including the host, to `api_diagnostics.txt` (requests under `/api/v2/admin/autoscan` now log the fixed category
`autoscan_transport_failed`; the key itself was never logged), and a reschedule warning from a defaults save hid later connection
created/updated/deleted notices (it now clears when another result arrives or an editor or delete confirmation opens).

Final run `autoset-lead-final-r1` on merge commit `39c9cc9e57ebfa77171798a2d06ce57a4b716fc4` (source and payload sealed): 121 focused and
10,071 full unit cases passed with no failures or skips; app build, publish, host build and native selectors `admin-autoscan-settings`
(77 checks), `admin-playback-administration` (99), `admin-manage-foundation` (36), `admin-configure-general` (188) and `admin-monitor` (113)
passed. Before the review and the merge of Beta `67c94ba05`, run `autoset-final-r2` passed 119 focused and 9,956 full unit cases with the
selectors `admin-autoscan-settings`, `admin-manage-foundation`, `admin-configure-general` and `admin-monitor`. An earlier identical run
(`autoset-final-r1`) passed everything except an access violation in a COM finalizer after `admin-manage-foundation` had logged its own
completion; the unchanged source passed on rerun. Wide, narrow and dialog captures of the Autoscan selector were produced and viewed.
Not verified: a live Silo server, all DPIs, phone or keyboard-only walkthroughs, visual parity with the web page. The installed player was not touched.

# November 20 — Manage Libraries > Home sections Beta candidate

Manage > Libraries > Home sections (/admin/sections) is implemented and registered through AddAdminHomeSections(): the row list per Home
or library page, shown switches, hero banner, drag and keyboard reorder, select mode (turn on/off, delete), Restore defaults, Add to other
libraries, Add row / Edit row (kind picker, variants, collection, rule, seasonal and per-kind fields, More options, live preview), the Next Up
defaults card, and the Collections link contract (?page=, ?add=collection:library:<id>, ?edit=<rowId>, ?return=<path>). Branch
t3/admin-home-sections, merged with origin/beta f36e3bd4a on 2026-11-20; the version is not changed by this branch. Official contracts, strings
and logic were read at 217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb; scope, write contract and native adaptations: docs/parity/2026-11-17-admin-home-sections.md.

Observed, under the SiloOct18Build lease, in final run sections-lead-final-r2 on commit 9a2a7f444dd52fdfb7593a5d767b5cd42d81677c: 121 focused and
9,958 full unit cases passed with no failures or skips; the app build, self-contained publish and native host build passed; native selectors
admin-home-sections (131 checks), admin-requests-management (37), admin-manage-foundation (36), admin-configure-general (188) and admin-monitor (113)
passed with no failures. Before the review corrections and the merge of Beta f36e3bd4a the branch passed as sections-final-r2 on b7cacefa2
(113 focused, 9,869 full, 118 native checks). The run before the final one, sections-lead-final-r1, failed on one new fixture check that counted
the editor's preview read as a write; only that check was corrected. Running the
page exposed and fixed: a ListView that kept stale switch and name values after a refused or applied change (rows are now stable views that resync
their controls), editor controls reused across editors (now created fresh per editor, which threw on the third open), a Back-to-collection link that
resolved an unsupported /admin/collections/<id> to the Dashboard, and a NullReference in a fixture gate.

An independent read-only review of the built branch confirmed the request bodies, If-Match use, no replay, unknown-outcome locking, retryable
refusals and id escaping, and reported two P1 and three P2 defects, all fixed with tests: a ?page=<library>&add= link opened Add row against Home
when the library list failed to load (a library link now waits for the list, and Reload follows it); in Add row a second kind pick kept the first
pick's name, item limit and hero choice (only Edit row's Change keeps them); the Hero, Show in apps, variant and auto-rotate controls rebuilt the
form with More options collapsed and focus on the name; value suggestions sent library_ids comma-joined instead of one key per id; and rows a
stopped Turn on/off batch never reached were reported as failed (they are now counted apart as not attempted). The new tests were not run against
reverted code.

Not observed: live-server behavior, real device drag, Narrator/IME, all DPI, and complete visual parity (only the list capture at 1440 was inspected).
Known differences: ComboBox page and variant choices, Move up/down and a row preview dialog in place of hover peeks and handle-arrow drag, the preview refreshes
on request, the settings API hides server detail on a refused Next Up default ("Settings request failed."), a failed library-list read shows Home with a
Reload banner rather than a full-page error, and Back to the collection and the addedRow
navigation state wait for the Collections page to reach Beta.

# November 20 — Manage Autoscan Sources candidate

Manage > Autoscan > Sources (`/admin/autoscan`) is implemented on `t3/admin-autoscan-sources`, merged with fresh
`origin/beta` `f36e3bd4a`; version stamps are unchanged. Official contracts were read at Silo Server
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb` (`AdminAutoscan.tsx` and `pages/admin/autoscan/*`, `internal/apiv2/admin_autoscan_*.go`), and the
independent review then compared the branch with official main `66b836f82a32de2fb3e5dfae6c285c19eea1096d`: nothing this page depends on
changed in behavior between the two (the Activity panel, which is not part of this page, was rewritten).
The page covers the global switch and Run now, the sources list, add and edit of every source type (descriptor config form,
interval, path mappings, rewrite suggestions, inline server create and test, label), webhook generate/copy/rotate with the
first-source Connect flow, and confirmed delete. Writes are sent once; a definite refusal (4xx) shows the redacted, bounded
server reason and lets the admin correct and retry, while only a timeout, transport failure or unusable acknowledgement is an
unknown outcome that blocks writes until the state is read again. Secret and dynamic fields never appear in a source form (the
server drops them), and their stored values are sent back unchanged. Shared edits are additive: `ProviderSchemaForm` gains an
overload (the two-argument form is unchanged) and `PluginAdminFormField` gains `FillFrom`. Activity and Settings are separate
families and are not in this change.

An independent read-only review of the built branch found no secret exposure and no change in behavior for the shared form's existing caller,
and reported one P1 and three P2 defects, all fixed: opening Edit marked the settings as edited (WinUI reports a text box's starting text as a
change), so a label-only save rewrote the stored configuration, dropped keys the form does not show and restarted the poll source (the form's
new overload and the editor's interval, label and mapping boxes now ignore a value equal to the one held); changing "Sent by" on a webhook source
with no settings form erased its other stored keys; a webhook source with no complete mapping showed "mapping incomplete" instead of the official
"mapping required"; and Fill from libraries rebuilt the whole editor (it now changes only its field). `ProviderSchemaForm` also gains `TrySetText`.
The merge of Beta `f36e3bd4a` also carries the Autoscan sensitivity option onto the request retried after a token refresh, as Beta does for Requests.

Observed, under the SiloOct18Build lease, in final run `autosrc-lead-final-r2` on commit `8d18c6357b5bc5f56ea6539b36c071aa0d7689b3`: 65 focused and
9,902 full unit cases passed with no failures or skips; the app build, self-contained publish and native host build passed; native selectors
`admin-autoscan-sources` (94 checks), `admin-requests-management` (37), `admin-manage-foundation` (36), `admin-configure-general` (188) and
`admin-monitor` (113) passed. The run before it, `autosrc-lead-final-r1`, failed one focused test: the first version of the stored-keys correction
read the stored configuration before the legacy alias migration and carried a migrated key back; it now reads it afterwards and the test is
unchanged. Before the review the branch passed as `autosrc-final-r5` (65 focused, 9,865 full, 92 native checks). Earlier full runs had unrelated
failures (`LibraryBackgroundRefreshTests.ContentRefreshRejectsPreMutationViewportAndHeldWindowSnapshots` in two runs and
`AppUpdateExitTests.ExitHelperVerificationAcceptsOnlyTheExpectedPackageWithoutLaunchingAnything` once) that did not recur on an unchanged rerun.
Captures were inspected at current DPI before the review corrections. Not verified: a live server, other DPI scales, screen readers, pixel parity.
Scope, write semantics and limits: [Autoscan Sources](parity/admin-autoscan-sources.md).

# November 20 — Manage › Users resource pages (Access groups, Devices, History import)

Access groups (`/admin/access-groups`, `/:id`), Devices (`/admin/devices`, `/:userId/:deviceId`) and History import
(`/admin/history-import`) are implemented in the registered native shell on branch
`t3/admin-access-groups-devices-history-import`, rebuilt from an earlier uncompiled draft against official
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb` and merged with Beta `f36e3bd4a`. Versions are unchanged.

Fixed from review of the draft: refusals (4xx other than 408) now show the server's safe problem detail and keep the draft, while only a
timeout, 5xx or lost transport after a write is an unknown outcome; a failed second request of a two-request save is reported as such and is
not repeated; read failures and later successes are tracked per section; device rows follow the official admin rules (all device-scoped keys
except the retired `ui.theme`, manifest order, platform tags, per-row anomalies, inherited sources); bitrates use Unlimited / preset / custom
Mbps; turning "All libraries" off selects every library; the 556-message catalog and the 42 device definitions were verified against the pin
by script; the app project now embeds both catalogs; a cancel answered 200 without a Location is no longer treated as unknown.

An independent read-only review of the built branch found no exposure of secrets or personal data and confirmed the refusal-text filter, the
shared edits, the group request body and the unknown-outcome handling. It reported two P1 and two P2 defects, all fixed: a refused Add server
save cleared the typed API key, so the corrected retry would have created the source without it (the key now stays until the save is accepted
or the dialog closes); a number setting could write a value the admin did not choose (number controls now write only a real change, at the
step's precision, and a fractional setting always shows its fraction); changing the list scope while another device was loading left the old
device in the detail pane (no regression test); and one device id that cannot be a URL segment failed the whole device list.

Observed, under the SiloOct18Build lease, in final run userres-lead-final-r1 on merge commit 2bf4f578e945eddb270fb0bb8995de4bad980957: 116
focused and 9,953 full unit cases passed with no failures or skips; the app build, self-contained publish and native host build passed; native
selectors admin-user-resources (230 checks), admin-requests-management (37), admin-manage-foundation (36), admin-configure-general (188) and
admin-monitor (113) passed with no failures. Before the review corrections and the merge of Beta f36e3bd4a the branch passed as userres-final-r4
on e85ed386e (114 focused, 9,870 full, 216 native checks). An earlier run, userres-final-r1, had five full-suite failures: one was this branch's
(a History import date that bypassed the shared formatter, fixed) and four were existing tests of other pages that formatted numbers with an
Arabic decimal separator in that run only; they were not changed and passed in every later run.

Scope, contracts, deliberate differences and limits: `docs/parity/admin-user-resources-2026-11-17.md`. Not verified: live-server
interoperability, all-DPI / Narrator / IME behavior, locale digit shaping and complete visual parity.

# November 20 — Playing Next and finished screens centred on ultrawide windows

Owner report: on a 21:9 monitor the "You've finished ..." screen (and Playing Next) sat against the left edge with the
right half of the window empty. The finishing picture was fixed 48 from the left edge (44% of the window, at most 832)
with a 544 column beside it, which only reads as centred near 16:9 widths. The gap between the picture and its column
is now on the window's centre line from 1200 wide up (src/SiloPlayer/Helpers/PlayingNextLayout.cs); 1024-1199 keeps the
edge margin and the compact layout is unchanged. A 1920-wide window moves 56 to the right. Official WebUI
(PlayingNextScreen.tsx, player-hud.css at 232ebad78607d75b31f7413d485a12d15f94f28f) is left-pinned in the same way, so
this is a deliberate desktop difference at the owner's request. Branch t3/fix-21-9-finished-page-centering from
origin/beta d90f4096577f17e135b117b673e5134b630c619f; the version is not changed by this branch.

Observed, under the SiloOct18Build lease: 17 new PlayingNextLayoutTests and the full unit suite, 9,773 passed with no
failures or skips; the app build, self-contained publish and native host build passed; hud-completion cases
finished-early-eof, finished-eof, finished-no-art, finished-stale and postroll passed on one payload. finished-early-eof
now resizes the real ended screen to 3440x1000 and measured the picture at 864-1696 and the column from 1744 (centre
1720); its capture and the 1440x900 capture were inspected.

Not observed: a physical 21:9 or 32:9 monitor with the installed player, other DPI scales, a live server and complete
visual parity. Evidence: D:\SiloPlayer-build\finished-centering.

# November 20 — Collections (Manage > Libraries > Collections) built and verified

`t3/admin-collections-page` completes Collections (`/admin/collections`, `/new`, `/:id/edit`): list,
Arrange board and shelves, new-collection picker, templates, starter packs, and manual/smart/synced
editors, registered through the real Admin shell. Beta was merged into the branch (normal merges, last at
`db49c4c93`, stable `1.2.1194` merged into beta); this branch changes no version number. Official contracts: `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`.

Seven review findings were fixed with regression tests, and running the code exposed more (JsonObject
request bodies serialized as node members, an editor re-render exception, Click-only checkbox handlers).
A second independent review of the final diff (November 20) found eight more defects; all eight were confirmed
against the code and the official source and fixed, each with a unit and/or native test: starter-job poll failure
and cap left the job id behind, job id stored for a retired authority, new member position colliding after a removal,
validation reasons hidden, a failed hide after import dropped, pasted list links not cleaned, template poster not
sent, and the unknown-outcome list check reading only the filtered library. Details, deviations and limits:
[docs/parity/admin-collections.md](parity/admin-collections.md).

Final acceptance after merging Beta `db49c4c93`: label collections-lead-final-r1 on merge commit 23e4d5bcb, build
lease 2026-11-20T16:17:21Z–16:23:40Z: focused 57/57 and full 9,754/9,754 unit cases (0 failures, 0 skipped); app build,
publish and native host built; native selectors admin-collections (191 PASS lines, 0 FAIL), admin-manage-foundation,
admin-configure-general and admin-monitor passed; source seal (2,489 build inputs) and 582-file payload unchanged across
the run. That merge brought only version stamps, README and release notes.

Acceptance before that merge (after the second review): label collections-fix-final-r1 (lane D:/SiloPlayer-build/admin-completion-lane-a,
task admin-families-nov20), stage final on commit 016ea09ac, build lease 2026-11-20T16:09:43Z–16:15:58Z. Focused unit
run 57/57 (filter FullyQualifiedName~AdminCollections) and full unit suite 9,754/9,754 passed (0 failures, 0 skipped);
app build, publish and native host built; native selectors admin-collections (191 PASS lines, 0 FAIL),
admin-manage-foundation (36), admin-configure-general (188) and admin-monitor (113) all passed; source and payload
seals verified. The earlier acceptance run (collections-final-r1, lane B, 31 focused, 9,728 full, 149 admin-collections
checks) predates these fixes. Before the final run, the page-level native checks for the starter-job poll failure,
member position and list check were shown to fail with those fixes temporarily reverted
(collections-fix-negative-r1; source restored from Git afterwards).

Not observed: live-server compatibility, all-DPI/IME/Narrator, physical drag-and-drop, artwork file
picker, complete visual parity. The authority-retirement race between a queued starter-pack answer and the dialog
continuation is covered by a controller unit test only, not by the real page. The final run's synthetic captures were not
reviewed again. Home Sections is a separate family and not registered here.
No push, PR, installer or installed-player action occurred.

---

# November 20 - Admin Manage Playback: Nodes and Playback Settings

Branch `t3/admin-playback-nodes-settings`, merged with Beta `d90f40965` (stable `1.2.1194` plus the merged Library settings
page; version unchanged here). Final run after that merge: playback-lead-final-r1 on merge commit 00fa14b66, build lease
2026-11-20T16:45:54Z to 16:51:32Z: 341 focused and 9,854 full unit cases passed with no failures or skips; app build,
publish and native host built; native selectors admin-playback-administration (94 PASS lines, 0 FAIL),
admin-manage-library-settings, admin-manage-foundation, admin-configure-general and admin-monitor passed; source seal
(2,503 build inputs) and 582-file payload unchanged across the run. The run named further below predates that merge. Official pin `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`. `/admin/nodes` and `/admin/settings/playback`
are registered and reachable through the real Admin navigation. Two earlier review findings (enabled switch restore,
marker-task status badges) and seven findings of a second independent review were fixed: the egress limit can no longer
overflow into an unhandled exception, the node acceleration override list matches the server's enum (no VideoToolbox), an
untouched editor no longer blanks a stored address it cannot show, an older marker-task poll cannot overwrite a newer run,
a hidden row's invalid draft no longer blocks Save, a definite 4xx refusal to run a marker task is no longer reported as
"unconfirmed", and a failed background node refresh keeps the rows and polling. Observed: one `-Stage final` run
(`playback-fix-final-r1`, lane `D:/SiloPlayer-build/admin-completion-lane-a`) on commit `cd39c93df`: focused unit tests
282/0 failed, full unit suite 9,795/0 failed, native selectors `admin-playback-administration` (94 PASS, 0 FAIL),
`admin-manage-foundation`, `admin-configure-general` and `admin-monitor` passed. One native assertion was changed (node
list refresh failure no longer demands a review lock; official react-query page keeps data and keeps polling). Not
observed: a live server, real phone/GPU/transcode node, network enrollment, all-DPI or assistive-technology runs, and
side-by-side visual parity beyond the fixture captures. No push, pull request or release was made.
See [scope, official contracts, findings and limits](parity/admin-playback.md).

---

# November 20 — Admin Requests submitted for Beta review

The family below was committed, current Beta `f35bb7e59d14f8e7f2e53bac982953fa3d5dc7da`
(`1.2.1194-beta.1`) was merged normally, and an independent recheck of the last
review corrections found two P2 defects that are fixed with regression tests.
Final run `recheck-green-r20` on the merged, corrected source passed 125 focused and
9,734 full unit cases, the app build, publish and host build, and all six native
groups. Details and receipts are in the [scoped handoff](parity/admin-manage-requests.md).
The November 17 record follows unchanged.

## November 17 — Admin Requests scoped acceptance

Prepared branch `t3/admin-request-management` remains based on
`248aafd62ed65b868c0e7356926e4d83204649cd`; app/numeric versions stay
`1.2.1181-beta.1` / `1.2.1180.1`, with stamping reserved for the pipeline.
The 25-path family implements Queue, Discover and Requests Settings, including
server/plugin editors, routing and the authority-owned sidebar count.

Official contract pin: `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`. A direct
GitHub main fetch resolved `13dec5cc2d7f7de31c019a0de51faba0f66a980a`; scoped
Requests contract paths and all 723 Requests catalog keys are unchanged.

Final review found five further defects. Seven new native RED scenarios on the
sealed r11 product reproduced saved-secret loss, fractional number/bound errors,
missing preview overrides, queued-progress starvation and reopened unresolved
mutations. Direct fixes retain private saved secrets only for unchanged
connections, preserve numeric schema semantics, render named overrides, poll
download presence and retain a canonical-review write barrier after failed reads.

Fresh `review-green-r13` verification passed 125 focused / 8,057 full unit cases
with no failures/skips, including one ignored source-root/Core-PDB proof, followed
by nonincremental x64 app/host builds, self-contained publish and all six scoped
native groups. Fixture-only viewport capture corrections then passed the same
six groups as `capture-final-r14` on identical r13 product bytes. All fourteen
corrective scenarios pass. Eleven primary captures were inspected or matched
inspected bytes. The source/582-file payload seals and app DLL/PDB/PRI host
equality passed; this is not an all-app/host-PDB source-document proof.

The broader viewer permissions selector is not accepted: r9 failed an unchanged
removal-dialog primary-button lookup; one bounded r10 continuation failed an
unchanged profile-editor viewport assertion. Both failed receipts remain
preserved; neither is reported as passing or expanded into unrelated changes.

The last lease was PID 27040, ACQ `2026-11-17T17:55:34.0002725Z`, REL
`2026-11-17T17:59:06.9558427Z`, exactly one nonblocking `WaitOne(0)`. Inclusive
preflight was 32.6739758271724 GiB, with both drive floors satisfied. No cleanup
occurred; the installed player and Requests977 remain untouched. Historical
r5/r6/r11/r12/r13 payloads and failed receipts remain preserved.

Evidence lives under `D:/SiloPlayer-build/admin-completion-requests`; the
[scoped handoff](parity/admin-manage-requests.md) records source/payload hashes,
historical failure boundaries and exact limits. Final independent read-only
reviews precede the authorized non-draft Beta submission. This entry does not
certify live-server/plugin interoperability, all DPI/accessibility, complete
visual parity, an installer or a stable release.

---

# November 20 — Configure Providers candidate

Configure > Integrations > Providers (`/admin/settings/providers`) is implemented on
`t3/admin-provider-settings`, merged with fresh `origin/beta`; version stamps are unchanged.
Official contracts were read at pinned Silo Server `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`
(`ProvidersSettings.tsx`, `MarkerProviderTiles.tsx`, `WatchProviderTiles.tsx`). The page covers
watch-provider plugin status, MDBList, subtitle providers (OpenSubtitles, SubDL, SubSource) and
marker providers, with strong-ETag subtitle editors, single-send writes and no replay of unknown
outcomes. Four review findings (Disconnect after authority replacement, truncation before
redaction, unreachable marker reload, edits lost during a pending marker save) are fixed with
regression tests. Scope, deviations and limits: [Providers](parity/admin-configure-providers.md).
An independent read-only review of the final diff then confirmed those four fixes and reported one P1 and
three P2 defects, all fixed with tests: Reload was unreachable after a failed credential-status or section
read; an erased key or failed Disconnect made Test send an empty key; a non-submitter marker provider with
stored sharing wrote contribute fields the admin never changed; and Save dropped a key typed while the
credential status was unavailable.
Final run `providers-lead-final-r1` on commit `07a7bc3c208ddbc40f14fd377194af746f114311`: 29 focused and
9,726 full unit tests pass with zero failures or skips; the app build, self-contained publish and native host
build pass; and the `admin-configure-providers` (89 checks), `admin-manage-foundation`,
`admin-configure-general` and `admin-monitor` native selectors pass.
Live-server compatibility, rendered-capture inspection and all-DPI/accessibility remain unverified.

---

# November 20 — Manage Libraries > Library settings Beta candidate

Manage > Libraries > Library settings (/admin/settings/library, official page "Library & Metadata") is implemented in
the registered native shell: General, Scanning (conditional real-time monitoring, worker counts, Advanced lookups/batch,
Restore defaults), Maturity limits, capability-gated Ratings and Subtitle sync with Advanced placement/capacity, one guarded
save, Discard, conflict/unknown-outcome handling and authority retirement. Branch t3/admin-library-settings, merged with
origin/beta f35bb7e59 on 2026-11-20; the version (1.2.1194-beta.1 from Beta) is not changed by this branch. Official contracts were read at
217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb; the page's strings (48) and server limits were compared with that pin.

Observed, under the SiloOct18Build lease, in final run library-settings-lead-final-r3 on commit
19a414df496d8a75c7e9daaa4f01ead637c9ac96: 108 focused and 9,755 full unit cases passed with no failures or skips; the app
build, self-contained publish and native host build passed; native selectors admin-manage-library-settings (84 checks),
admin-manage-foundation, admin-configure-general and admin-monitor passed on one unchanged 582-file payload. Running the
page exposed and fixed a KeyNotFoundException in Render that crashed the host on first load. An independent read-only
review of the final diff reported two P2 defects (a malformed saved rating source could not be toggled off, and one
environment-managed worker setting disabled Restore defaults for all), both fixed before that run.

Not observed: live-server behavior, all DPI, Narrator/IME, real plugin combinations and complete visual parity. Scope,
evidence and limits: docs/parity/admin-manage-library-settings.md.

# November 20 — Configure Sign-in, API Keys and Policy (Access family)

`/admin/settings/sign-in`, `/admin/api-keys` and `/admin/policy` are implemented, registered
(`AddAdminSignInSecurity()`) and reachable through the real Admin navigation. The three pages had been
listed as unsupported in `AdminRouteCatalog.UnsupportedPatterns`; `api-keys` and `policy` are removed from
it (the shared navigation test no longer lists `/admin/api-keys` as unsupported). Official source pin:
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`. Version `1.2.1181-beta.1` unchanged. Branch
`t3/admin-signin-security`, merged with `origin/beta` (25 commits) before the final run.

Review findings fixed with regression tests: the policy title/toggle/lifecycle labels now follow the adopted
snapshot (`AdminPolicyPresentation`); the override list and the API key list are re-read from the server's
canonical first page after a create; a numeric field the server holds as explicit `null` is sent back as `null`
unless the administrator changed it. Also fixed from the first compile and native run: a missing namespace,
unusable `StackPanel.IsEnabled`, anonymous request bodies replaced by dictionaries, multi-line text boxes losing
everything after the first line break (assigned before `AcceptsReturn`), a refused local value permanently blocking
later writes, the shared date/time preference formatter, clipboard history excluded for the one-time key.

Second independent review (seven findings, all confirmed and fixed with regression tests): creates whose outcome is
unknown or whose list re-read failed now block every create and row write on API Keys and Policy until the page-level
reload (opening, cancelling or saving a single-row editor no longer lifts it); a definite policy-enable 409 shows its
reason and is retryable; "All overrides" re-reads the list after editor-side writes; a revoke/tier change whose list
re-read fails is still reported as done; the save-time "no provider" refusal uses the provider text; a non-string
provider `display_name` no longer crashes the page; account pages beyond the first are read (cap 1,000) so API Keys
owners and the break-glass check do not depend on the first page, and an incomplete list leaves the break-glass
decision to the server as on the official page (a settings 409 is shown with the break-glass text).

Observed on exact source after the fixes (lane `D:/SiloPlayer-build/admin-completion-lane-a`, label
`signin-fix-final-r3`, source commit `70de5e745`): focused unit run 167 passed, full unit suite 9,835 passed/0
failed, app build, publish, native host and the native selectors `admin-signin-security`, `admin-manage-foundation`,
`admin-configure-general`, `admin-monitor` all passed, with source and payload seals verified. An earlier attempt
(`signin-fix-final-r1`) failed one unrelated test in another family (`LibraryBackgroundRefreshTests.RetainedRefreshHandbackRestartsCanceledLettersAndCount(churn: False)`)
that passed on rerun without change. Not observed: live SSO/LDAP servers, servers with more than 50 accounts, a real
clipboard, other DPIs, screen-reader or IME behaviour, installer execution. Earlier run before the fixes: lane
`admin-completion-lane-b`, `signin-final-r2`, 146 focused / 9,755 full. Scoped record and deviations:
[admin-sign-in-security-20261117.md](parity/admin-sign-in-security-20261117.md).

---

# November 17 — Configure Integrations verified Beta increment

AI, Recommendations and Compatibility are implemented in the registered native shell,
including provider/model editors, Keep/Replace/Clear credentials, manual user-owned
ChatGPT sign-in/account recovery, recommendation jobs/reset and guarded managed-web
operations. Recommendations now also resolves through its ordinary canonical href.
Branch t3/admin-integrations-leaves uses deliberately preserved Beta base
248aafd62ed65b868c0e7356926e4d83204649cd; version 1.2.1181-beta.1 remains unchanged
for pipeline stamping. Fresh origin/beta was 73f6f99d96910f88a6db8a471c966927350a1eba.

Official contracts were reviewed at 217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb.
Official GitHub main was fetched directly again at
c58c0bb9394868f42d058cf8749de46921886420: scoped WebUI/wire contracts are unchanged;
the intervening speech timestamp diagnostic is already displayed through the bounded,
redacted server-message path. No claim is made for unrelated upstream changes.

Coordinator review-green-r8 passed fresh nonincremental unit/app/native-host builds,
87 focused and 8,084 full unit cases, zero failures/skips, publish and all seven scoped
native groups. The tests include the acceptance-only physical source/Core PDB checksum
proof. Real-shell regressions cover the confirmed review gaps, accepted-operation
identity through both reload choices, malformed provider acknowledgements, ordinary
Recommendations navigation and authority/secret/dialog retirement. Seven actual PNGs
were inspected at wide/compact sizes and in the manual sign-in dialog.

Single nonblocking SiloOct18Build lease: 2026-11-17T15:11:00.6772543Z–15:18:50.4920140Z,
PID 21300; 17 contained commands passed and finally released. No owned runtime remains.
Source seal: 2,326 files, SHA256
1f7b7ded1b18a41e8a6d026921140044cc0fd553e3a1763fafd7073f12020c27.
The same 582 published files were rehashed before/after all native groups; payload SHA256
fc6fba7fefcbb081424da1655d92870336a5137ddd505e25ceb5f0404bb77919.
Product/reference/host DLL, PDB and PRI bytes match. Receipts are retained under
D:/SiloPlayer-build/admin-completion-integrations, label review-green-r8.

Source-only reports are superseded by this evidence, not erased. Full scope, RED/GREEN
history, review disposition and limits: docs/parity/admin-configure-integrations.md.
External ChatGPT/provider login, live server/Jellyfin/Audiobookshelf interoperability,
all-DPI/Narrator/IME behavior and complete live-WebUI visual parity remain unverified.
Installed player PID 39668, preserved artifacts, version stamps and pipeline-owned PRs
remain untouched. This increment is submitted only to the authorized Beta pipeline;
no merge, installer execution, stable release or production action is authorized here.
---

# November 17 — Notifications verified Beta candidate

Configure > Integrations > Notifications is implemented on
`t3/admin-notification-settings`, based on exact Beta
`248aafd62ed65b868c0e7356926e4d83204649cd`. Official GitHub main was rechecked
at `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`; version remains
`1.2.1181-beta.1` under pipeline control. This complete family includes seven
delivery channels, relay/Discord setup, processing/policy settings, server-channel
CRUD/tests/secret rotation, production registration and regression fixtures.

Final nonincremental verification: **76 focused / 8,061 full unit cases pass**,
zero failures/skips; app/publish/host build and source-root/Core PDB proof pass.
Six native groups pass: Notifications, Manage foundation, General, Monitor,
Email and Security. Seven actual Notifications wide/compact/page/dialog PNGs
were inspected. The exact 582-file product remained byte-identical throughout.
The additional unchanged Downloads selector times out on its retained-bandwidth
fixture expectation; it is not claimed passing and was not changed here.

The four independent review findings and full-suite URL/date policy failures
are corrected. Actual RED/GREEN, ACQ/REL, source/payload hashes, captures and
limits are recorded in [Notifications](parity/admin-notifications.md).
Live delivery, all-DPI/accessibility and complete visual parity remain unverified.
No direct merge, stable release, installer or installed-player action is included.

---

# November 17 - Ebook/comic protected-install startup follow-up

On fresh Beta `248aafd62ed65b868c0e7356926e4d83204649cd` (version
`1.2.1181-beta.1`, numeric `1.2.1180.1` unchanged), ebook/comic reader startup
now explicitly creates its WebView2 environment in writable per-user storage
instead of relying on the unpackaged default beside the protected executable.
The previous native fixture's SDK folder override masked this installation
failure. Normal ebook Read and comic chapter-row Read both reproduce the exact
generic error with installed bytes under equivalent protected-directory rights;
both pass with the corrected source and those same rights.

Release publish and native host build pass. The final two native runs assert
default-folder E_ACCESSDENIED, the actual selected writable directory, visible
text/images, sandboxing, progress, reconnect/access refresh and profile
retirement. Publish/host bytes match and changed production/fixture documents
are checksum-bound to their PDBs. A subsequent full-unit launch was held by a
failed finite mutex acquisition; no units ran and no retry followed. Synthetic
books/protected host only; private-book live interoperability is not certified.

Evidence and hashes: [reader startup handoff](parity/2026-11-17-reader-protected-install.md)
and `D:/SiloPlayer-build/books-reader-followup-20261117`. Last owned lease
released `2026-11-17T10:55:49.7653626Z`. Installed player/settings/files remain
untouched. This is a new Beta follow-up; earlier submitted PRs are not revisited.
After this source is submitted, the Beta pipeline owns all further work.

---

# November 16 — Configure Backups verification and Beta handoff

Configure > Server > Backups is implemented as one route family: `/admin/backups`,
`/admin/backups/:id/restore` and `/admin/backups/uploads/:uploadId/restore`, through the
production Configure registration. Tested base: Beta
`542dec13c7d45c7520116c559166023ecb4ee987`, version `1.2.1168-beta.1` unchanged.
The pipeline owns reconciliation, version assignment, QA, fixes, merge and packaging.

Contracts were implemented against official Server/WebUI
`92ee051878c2a791940ec9598e2bdd0f04890b3e` and checked against the Go handlers/OpenAPI.
Direct official-main recheck at `94797c3e2e39ef12bf2796fa08df12e3b3525b3e` found no
subsequent Backups contract changes. Desktop Beta was rechecked at
`3d9b642242b0539684fcb055b00690dbd242a93e`; the tested candidate base is retained.

Independent review corrections cover expired-token GET refresh, durable per-source/epoch
restore uncertainty across page departure, a fresh-preview gate, foreign-source conflict
jobs, coalesced capabilities/uploads reads and visibility resume, and discard-before-dispose
for known uploads under the same authority epoch. Native testing also found and fixed
late download progress overwriting the verified terminal result.

Verification (November 17 UTC): source proof 1/1, focused 226/226 and full 7852/7852;
nonincremental app/host builds, Release publish, PDB/source, payload/engine and XBF binding;
Backups native 30 PASS records and foundation 36, both including completion. Review/preview
regressions and native download completion have retained RED/GREEN evidence. Every runtime
used one nonblocking shared lease, with actual ACQ/REL relayed; final REL
`2026-11-17T01:25:12.2338806Z`. Original feature implementation was not test-first.

Limit: unchanged Downloads native fixture line 316 timed out expecting empty after unchecking
Unlimited with finite 250 remembered. That group is not passing and no clean runtime baseline
was established. Live-server, visual, DPI, IME, Narrator and real OS-picker verification remain
unperformed. Closing during unidentified upload creation may leave an upload for the server
sweep; no create replay or guessed deletion. Checklist stays In progress, not Done.

Evidence: `.codex-tmp/admin-backups-verification` in the task worktree; reusable output
`D:/SiloPlayer-build/admin-backups-active`. Measured generated output 16.658 GiB under the
owner-approved 50 GiB cap, with C/D free 169.91/461.815 GiB. No installed player/updater,
installer, release publication, cleanup or production action occurred. See the
[parity record](parity/admin-backups.md) and [contract plan](plans/2026-11-16-admin-backups.md).

---

# November 16 - Configure Maintenance bounded verification for Beta submission

Configure › Server › Maintenance (`/admin/maintenance`) implements Catalog Import & Export
and read-only Job History through the real Admin registration. Only the Maintenance unsupported
route gate changes; Backups and other Admin candidates are separate. Base Beta is
`19a66e547a78bad9fdc88925c3deae308e05d4ba`; version `1.2.1168-beta.1` stays pipeline-owned.
Official Server/WebUI was freshly fetched at `94797c3e2e39ef12bf2796fa08df12e3b3525b3e`: no
Maintenance contract changes since implementation reference `92ee051878c2a791940ec9598e2bdd0f04890b3e`.

Bounded verification: nonincremental units/app/host builds, Release publish, source/payload
binding, source-root proof (1), focused units (323) and full units (7,949) passed. Native
Maintenance (43), foundation (36), General (188), Email (297), Security (374) and Network (346)
passed. Downloads timed out at its unchanged fixture line 316; Libraries failed unchanged
active-scans/Show-all checks and timed out at line 368. These neighbors are not certified.
A foundation GC/COM finalizer access violation after its completed marker did not recur on
the unchanged rerun; its cause is unconfirmed. Evidence is retained under
`.codex-tmp/admin-maintenance-verification` (r4, r6-r8); every run kept source unchanged.
The only post-r4 runtime source change was Maintenance fixture readiness waits before editing.

Two page-departure regression cases were observed RED then GREEN. Three incomplete synchronous
import acknowledgements were also observed RED then GREEN; required result fields prevent a
malformed success clearing the per-authority/epoch unknown-outcome block. Commands never replay.
No polling timer, installed-player changes, installer execution or release publication. Visual
parity, live server, real browser/clipboard/OS dialogs, IME, Narrator and DPI remain unverified.
See `docs/parity/admin-maintenance.md` for exact evidence and intentional differences.

---

# November 16 — Configure Storage verified scoped Beta handoff

Configure > Server > Storage (C5) is ready for its separate Beta PR on deliberate base `522007cf7e20d8dbce7f72234c2f2af382d1c921` (1.2.1165; pipeline-owned). It covers local/public-private S3 storage, write-only keys and checks, asset links, transition review/queue/cancel, recovery and active-only polling. Source review repairs retain coalesced post-command refreshes and last-good capabilities, and permit review retirement while queuing.

`storage-full-r5`: proof **1**, focused **332**, full **7583** units passed; app/publish/host/source binding passed. Storage/General/Email/Security/Network native groups passed (**50/188/297/374/346** markers). Actual release **2026-11-16T21:50:35.6731539Z**; receipt SHA256 `B8F5C56E285CDA48C6FFB042C24977DA9330CC808730F9AE10ED5335DD41DCED`. Released audit verified **2582** source entries, **666** payload files and **15** exact contained captures.

The sixth Downloads selector remains failed at unchanged fixture316 (`uncheck download.server_bandwidth_mbps`). It fails identically on the non-Storage Beta212 baseline with only the independent Dashboard diff; its fixture/page/controller/rules are byte-identical. Baseline receipt `6A64DD3B1CE60CC235A9999680DBC3AC432C4C132707239A51FE79CC84652BE0`, release **21:57:42.7757669Z**. The original Storage receipt stays failed: this is scoped acceptance with a documented baseline limit, not an all-green native suite.

Only delivery docs changed afterward. Fresh official main is `92ee051878c2a791940ec9598e2bdd0f04890b3e`; Storage contracts reviewed at `8f93594c` are unchanged. No live S3/migration, installer, physical input, every-DPI, Narrator or full visual parity claim; English-only copy. Installed player/settings and Requests payload untouched. See [scope and evidence](parity/admin-storage.md).

---

# November 16 — Admin Scheduled Tasks (C8) verified Beta handoff

Configure > Server > Scheduled Tasks (`/admin/tasks`, `/admin/tasks/:key`) is verified for its separate Beta PR on deliberate base `6936ae5c956360358c454cb8af478f47e2dbd0ba` (pipeline-owned `1.2.1168-beta.1`). Official main was fetched directly unchanged at `92ee051878c2a791940ec9598e2bdd0f04890b3e`. Scope includes list/on-demand groups, run/cancel, detail and queue metrics, ETag-guarded schedules with explicit review/adoption, cursor history, hidden Search tasks and active-only realtime/clock work.

Acceptance: `tasks-full-r5` proof **1**, focused **250**, full **7724** units and app/publish pass. Final fixture-only `tasks-native-r8` retains those exact unit/product inputs and all **667** app files, rebuilds/binds the host, and passes all six native groups (**45/113/36/76/64/346** markers). Actual release **2026-11-16T23:06:18.3669255Z**; receipt SHA256 `714164EA15DC5253378D2754D3E08FC68C32C7CAA67965909D5C2B91DC3D8260`. Released audit verifies **2614** source entries and **9** final captures plus the retained successful stages. Earlier failed receipts stay failed.

The source review's sole P2 (Retry while fetching) was reproduced and corrected. Legacy task-route blocklist entries were removed, and compile/actual-button-readiness fixture repairs retain all44 Check lines. Only delivery docs changed after acceptance. No Scheduled Tasks screenshots, live-server/WebUI, installer, every-DPI, physical-input, Narrator or comprehensive visual certification; new copy is English-only. [Scope and evidence](parity/admin-tasks.md).

Libraries181, Users194, Search198, Storage199 and Dashboard200 remain separate pipeline-owned PRs. Search's preserved task destinations activate through the new registration; Search code is not changed. Installed player/settings and Requests payload remain untouched.

---

# November 16 — Admin Dashboard Now Playing row parity verified

Official #4401 (`5b3dcaa2fdfec6de2a537a2e10897352a1078689`) removes the +N more link, uses the full card viewport and clips tag rows to one 18px line. This separate two-source-file Dashboard change matches those rules on deliberate Beta base `21233391575c09c2dfd2c39a96211b5ecc10f4f7` (pipeline-owned `1.2.1168-beta.1`). Fresh official main `92ee051878c2a791940ec9598e2bdd0f04890b3e` leaves the widget unchanged.

`dashboard-full-r1`: proof **1**, focused **26**, app/publish/host and source binding pass; `admin-dashboard` passes **207** markers with one completion, numeric0. Actual release **2026-11-16T21:56:06.3893368Z**, receipt SHA256 `061933A0BA15EBB90ED0B2BA688BE5EC1BE5E2A5379511E7068A18F533008586`; released audit verifies **2574** source entries, **665** payload files and **9** contained captures. Only documentation changed after acceptance.

No full-unit repeat, live-server/installer/every-DPI/Narrator or comprehensive visual-parity claim. Installed player/settings untouched. Scope and the separate Downloads baseline limit are recorded in [Dashboard parity evidence](parity/admin-dashboard-even-rows.md).

---

# November 16 — Admin Manage Users Beta handoff

One scoped Users increment adds Users/Invitations/Invite Codes and the account
Overview, Profiles, Access, Sign-in, Activity, Downloads and Preferences surfaces,
including nested profile/PIN/avatar, policy/quota, invitation and invite-code
workflows. It is separate from Libraries PR181, Search, Storage and viewer work.

Deliberate tested base: `bdd033c1cab7cd601d6771bb853f4b0af044d67e`. Official
Users contracts were read at `17d5376c` and rechecked at `8f93594c`; the later
`5b3dcaa2f` Dashboard-only change is a separate increment. Version stamps remain
1.2.1165 / 1.2.1165.0 under pipeline control.

Actual evidence: focused Users/architecture 157/157 PASS; R6 full 7,625/7,625
PASS before the final UI-only fixes; R7 full 7,624 PASS / 1 FAIL, with the
unchanged Network Access case subsequently PASS 1/1 alone in R8. Do not present
that as an all-green final full suite. R10 nonincremental app/publish/host and
source/PDB binding pass; five complete native groups pass: Users147, foundation36,
General188, Monitor113, Dashboard184 (counts include completion/teardown markers).
Actual R10 release: `2026-11-16T20:32:48.1897021Z`. Root released audit confirms
2,613 source entries, 666 published files and ten contained numeric-zero captures.
Only documentation changes after the tested source. See `docs/parity/admin-users.md`.

Known temporary view-as sessions use one independent bounded logout on retirement;
unknown/unreceived impersonation receipts cannot guarantee remote revocation.
English localization, complete keyboard/Narrator/all-DPI/live visual parity,
full-app view-as playback/Downloads, ten authored semantic controls and live
account/server/installer acceptance remain unverified. No installed-player,
settings, installer execution, release or direct merge. The owner-authorized
feature commit/non-draft Beta PR hands subsequent review/QA/fixes to the pipeline.

---


# November 16 - Admin Manage Libraries for separate Beta submission

Manage › Libraries › Libraries (`/admin/libraries`) is implemented and registered through
`AddAdminLibraries()`: the sortable table and compact cards, scan queue popover, Scan all, row
commands (scan/stop, metadata refresh mode dialog/stop, mount check, delete and empty-root cleanup
confirmations), keyboard/pointer/card-menu reorder as serialized whole-order writes, the add/edit
editor (General, Folders with server folder browsing and path completion, Metadata with provider
chains and translation prewarm, Advanced), and the Unmatched, Matcher queue, Ambiguous roots,
Skipped roots and Stale ID sections. Official reference 17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8
(Libraries contracts unchanged from abd85d0c); tested Beta base 173c3797, version
1.2.1165-beta.1 unchanged and pipeline-owned. Writes are sent
once; lost answers are reported as unknown and reloaded; 202 receipts are never shown as completion.

Historical verification of the earlier candidate on Beta base 173c3797: 7,576 unit cases passed
(108 Libraries/folder-browser cases), including the actual source-root proof. A host-only
continuation after one fixture readiness correction passed all five selected native stages:
remediation 211, full Libraries 378, Manage foundation 36, Configure General 188, Monitor 113
PASS lines. All seven historical build/binding/native captures had numeric exit 0 and verified
containment. Source/PDB binding covered 1,007 app and 240 host documents; readback rehashed
2,233 source entries and 666 published application files for that earlier candidate only.

Later fixes c4192cde, 53fa9ca7 and afef78f9 changed production code and added regression tests,
including concurrency coverage. They address matcher summary polling, last-page ignore/restore,
scan hydration on open and controller channel locking. These fixes are source-reviewed only;
no builds or tests were run for them. The historical counts, receipts and hashes do not verify
head afef78f98544, and the 7,576 count does not include its new regression tests. Pipeline
verification of the updated source is pending.

Historical native lease: 2026-11-16T16:37:47.9426263Z-16:39:59.8164148Z. Receipt SHA256
DB4A061FE7043E50AF112175FD11FD7D01BD5FDEC556C8523434DB230F8A6216; evidence in
`.codex-tmp/admin-libraries-reliability-20261116T153001Z/beta-173c-native-r4-current-render/`.
Details and historical failures: `docs/parity/admin-manage-libraries.md`.
Remote Beta was observed at 7ba72bed (stable 1.2.1165 release/docs/version-only changes since the
tested base); this feature does not alter version stamps. Users, Search and the remaining Admin
sections are separate increments, not bundled into this Libraries change.

Limits: synthetic server and current DPI only; no live-server, real pointer-device drag, poster
file picker, IME/Narrator, all-DPI or complete visual parity certification. The installed player
and settings remain untouched. A hidden-file-inclusive storage readback found 10.191 GiB of
global generated output, over the owner's 10 GiB limit; further local builds/native execution
are on hold. Historical restore-cache cleanup was policy-denied with zero bytes removed and
was not retried. Functional test success is not a claim of storage-bound compliance.
---

# November 16 — Books reader/detail Beta handoff; local runtime held

Books changes are integrated but uncommitted in the dedicated Books worktree
on Beta `173c37974894fe5d52a4a4437c356c84e5c897d9`, version
`1.2.1165-beta.1` / numeric `1.2.1164.1`. Official Server/WebUI main remains
`17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8` on a fresh remote check. Beta
has since advanced to `7ba72bed148a29e737ec708997d8fedc259f7a0d`; that
source has not yet been integrated or verified for these changes.

Worker checks retain their original scope and payload provenance. Independent
reviews found reader lifetime/error recovery, comic ordering and additional
parity gaps; this is not an all-books parity certification. Source corrections
now separate reader identity from request retirement, limit bootstrap failure
reporting to startup, preserve recoverable saved-location errors and FB2/FBZ
overall progress, canonicalize comic volume groups/Next order, stretch comic
sections, and resize retained ebook rails. New regression sources are unexecuted.
Earlier worker payload verification does not cover these final corrections.

The owner's latest scheduling report puts shared generated outputs at
10.191 GiB, above the 10 GiB cap. Admin released at
2026-11-16T16:39:59.8164148Z, but all new runtime/build launches remain held
pending bounded recovery. Books holds no lease, starts no new test/build/browser
runtime and does not retry denied cleanup. Preserved payloads, evidence and the
installed player/settings remain untouched. See the
[delivery plan](plans/2026-11-16-books-parity.md) and three dated Books parity
handoffs for scope and outstanding corrections. The owner explicitly directs
immediate qualifying non-draft Beta PR submission despite this local hold;
pipeline review/QA must verify final source. Do not infer a successful final
build, native pass or complete parity from submission. Remaining gaps include
normal detail Read entry coverage, clickable audiobook series navigation,
library empty/no-match/Retry recovery, translated new audiobook strings, sticky
controls and legacy FB2 highlights. Browsing/card changes are a separate PR.

---

# November 16 — Books browsing/card Beta handoff

The owner explicitly directs immediate non-draft Beta submission while local
runtime/build launches remain held for shared generated-output cap recovery.
This independent browsing/card change adds audiobook author captions, listening
duration/time-left/progress, canonical group selection and reading/listening
status labels, suppresses the generic manga watched overlay, and prevents a
failed catalog from also claiming the library is empty. Reader/detail changes
are separately submitted in [PR182](https://github.com/ellermw/SiloDesktopApp/pull/182).

Original worker verification: 121 focused unit/source tests and four native
groups passed before the final empty-state guard. The final guard compiled but
has not been native-reverified. Do not claim those earlier payload results cover
the submitted final head. Source checks accompany submission; pipeline QA must
build and execute the final source. No local build/test/native/browser launch,
cleanup retry, installed-player or settings action occurs for this handoff.

Original source base is Beta 173c37974894fe5d52a4a4437c356c84e5c897d9;
current Beta 7ba72bed148a29e737ec708997d8fedc259f7a0d is merged for
submission. Official WebUI main remains 17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8.
Version stamping stays under pipeline control. Remaining parity gaps include
distinct true-empty versus filtered-no-match states, Clear Filters and failed
browse Retry, plus full live-WebUI visual comparison. This is not comprehensive
all-books parity certification. See docs/parity/2026-11-16-books-library.md.

---

# November 16 — Admin Configure Search verified Beta handoff

Configure > Server > Search is verified on deliberate Beta base `bdd033c1cab7cd601d6771bb853f4b0af044d67e`, without editing pipeline-owned version stamps (1.2.1165). Official main `5b3dcaa2fdfec6de2a537a2e10897352a1078689` retains the Search contracts inspected at `17d5376c`. The independent Search PR covers engine/connection, write-only credentials, matching/semantic controls, Advanced tuning and bounded active-only status. Provider diagnostics are strictly allowlisted; foreground invalidations coalesce. Task controls preserve official destinations and stay disabled with accessible help until Scheduled Tasks is registered; Search executes no tasks.

Acceptance: `search-full-r2` passed source proof **1**, focused **187**, full **7537** units, plus app build/publish. Final fixture-only `search-native-r6` retained the exact tested unit/product source and all **666** app payload files, rebuilt/bound the native host and passed **all eight** groups (Search/General/Email/Security/Network/Manage foundation/Monitor/Dashboard). Final release **2026-11-16T21:25:39.0469867Z**; receipt SHA256 `54E551A535135E7D4F038637D39DD0A7B0EB1E2BBBD574C777E112AB4572BAC7`. The released audit qualified **2576** source entries and **11** contained numeric captures. Earlier fixture failures remain preserved, not green evidence.

Only documentation changed after acceptance. No screenshots, live Meilisearch, installer, physical input, Narrator, every-DPI or full visual-parity claim; new copy is English-only. Health dots and Scheduled Tasks are separate. Installed player/settings and Requests payload stay untouched. See [scope and evidence](parity/admin-search.md).

---

# November 16 — Preserve legacy HTML chapter doctypes

Reader HTML fallback now serializes the complete existing doctype before the
HTML document element. Legacy public/system identifiers survive, alongside the
inline CSS child selectors and ampersands preserved by PR177. The synthetic
chapter remains in the script-disabled reader sandbox.

This source-only asset follow-up is based on Beta 40ab7889ef1d015a8ee07ef1962870763f95f66c;
version 1.2.1151-beta.1 stays under pipeline control. The new renderer assertion
failed on PR177 bytes and passes after the one-line serializer correction.
Verification: 42 Chromium EPUB/PDF renderer checks and 26 Node reader checks
passed. No .NET build, native WinUI fixture, installer, installed-player or
settings action occurred for this increment. Shared output was not touched.
See [scope and evidence](parity/2026-11-16-reader-doctype.md).

---

# November 16 — Credits follow-up verified for PR176

Official Server main was fetched directly again unchanged at
`abd85d0cadac28f4ab7db6821dad9634fc114790`. Fresh Beta is
`40ab7889ef1d015a8ee07ef1962870763f95f66c` (pipeline stamp 1.2.1151-beta.1).
This Credits follow-up was verified as source atop pipeline Beta-merge head
`12e510bd7918cc437da95e60945be78e137f5169`. Its version remains
1.2.1145-beta.1 / numeric 1.2.1144.1; the Beta pipeline owns subsequent stamping.

Credits Up next now respects autoplay and pause, survives routine event-socket
reconnects, and uses WebUI's enabled default after bounded optional settings-read
failures. Genuine account/profile/device/access/playback/file retirement still
rejects stale responses. Native tests exercise actual Settings writes in both
directions, initializer cancellation, access-change retry and successor playback.

Both reconnect/failure regressions first failed against prior native bytes.
The 06:49:17.4418184Z–06:51:48.1016177Z lease then passed nonincremental
unit/app/host builds, actual xUnit source-root proof, PDB document/checksum and
publish/reference/host byte binding, and all five native cases (19 PASS, zero
FAIL): credits-autoplay, credits-autoplay-reconnect, credits-autoplay-failure,
autoplay and postroll. Product SHA256:
`F979769FD851C41F9ED83583A88CD06987539375145EE809B5828EA6AC8B9A32`.

That chain's focused units stopped on a new HTTP test returning `settings`
instead of the contract's `items`. Only that fixture JSON changed afterward.
The assigned final unit-only lease ran 06:58:27.6808250Z–07:01:37.7719166Z:
fresh nonincremental units, source/PDB proofs, 727 focused tests and all 7,239
full-suite tests passed, zero failures/skips. Guards confirm product/native
sources still exactly match the five passing native runs. Independent final
source review found no blocking findings. Receipts:
`.codex-tmp/credits-final-units-20261116T0658-receipt.json` and
`.codex-tmp/credits-review-green-20261116T0650-stopped-receipt.json`.

Reader doctype follow-up is separately submitted in qualifying Beta PR178,
head `2809dcd1d50e4a6a0b1cf0b06d94cfccbd12fb48`, with an executed red then
42 renderer and 26 Node checks passing. No shared .NET output was touched.

Credits has released its lease with no waiter or requeue. All .NET/native work
uses shared `D:/SiloPlayer-build/{artifacts,publish,native-reference}` under
`SiloOct18Build`; native stages retain continuous ownership and source/byte
binding. The installed player/settings remain preserved. No direct merge,
release or installer action occurred. Synthetic tests do not certify live
household, Narrator, every DPI or full visual parity.

Admin Manage/Configure remains with its existing lead. Monitor/shared shell,
request permissions, audiobooks, localization, metadata and the remaining
viewer increments continue independently; this is not whole-rollout completion.

---

November 15 - Server review and credits autoplay increment

Reviewed official Silo Server main `abd85d0cadac28f4ab7db6821dad9634fc114790`
against Beta `2c33599f751e1bb7e18a56ee378ffe918710d74d`. The 222-commit
inventory and remaining work are recorded in
[the upstream review](parity/2026-11-15-server-abd85d0.md). Request restrictions,
Activity and audiobook changes are independent work; Configure Security and
Network access remain with the existing Configure lead.

The first increment aligns production credits Up next with autoplay and pause:
unresolved/off waits for manual Play now, eligible elapsed time survives holds,
range exit/reentry and cancellation behave correctly, and stale preference reads
cannot affect a successor. Initial reads share the current settings request;
live refreshes cancel superseded HTTP work. Version remains pipeline-owned
1.2.1145-beta.1 / 1.2.1144.1.

Full main suite: 7,180 passed. Final 443 OSC/localization checks, Release publish,
matching native host and credits-autoplay/autoplay/postroll native groups pass.
The three original Lua regressions and a subsequent native cancellation
regression failed before their corrections. Final app DLL SHA256
`018293C7D8633F04D4EEAE352DCBA1645DFC688CDD83331DB8A5666FCAF0EEE0`.
See [scope and exact verification](parity/2026-11-15-credits-autoplay.md).
PR176 review identified a missing notification from the actual Settings writer.
An added native property-toggle regression failed before correction; the writer
now captures profile authority and notifies after device-override cleanup. Final
publish/host, 443 focused checks and all three native groups passed again; the
preceding full 7,180 run retains its original scope. Latest evidence is task-local
`.codex-tmp/credits-settings-save-final-receipt.json` and adjacent logs. The prior
`credits-final` candidate/receipt remains historical evidence.

All later .NET/native runs hold `SiloOct18Build`; shared Configure binaries are
untouched. Reusable task output is about 1 GiB. No installed-player/settings,
installer, direct merge or release action occurred. This is synthetic/offscreen
verification, not complete live/visual/physical-device parity certification.

---

# November 15 - Configure Security delivered in Beta PR175

Security alone is replayed onto Beta 2c33599f751e1bb7e18a56ee378ffe918710d74d in branch t3/configure-security-beta. Original commits 1e71c820 and fe5a29b4 remain preserved at preserve/configure-security-fe5a29b4; their receipt and logs remain unchanged. General PR173 and Email PR174 are merged and included in v1.2.1145-beta.1. Version remains 1.2.1145-beta.1 under pipeline control.

Official main abd85d0cadac28f4ab7db6821dad9634fc114790 was fetched directly. Independent Claude standards/spec review found no blocking correctness bug or Security upstream drift. Lead reproduced and fixed stale shared settings on entry and unsaveable rate edits after a failed shared settings read. Both added regressions failed before the fixes; 723 focused Admin/Architecture tests pass afterward. Final Beta-based verification passes: 7,226 full unit tests (0 skipped), 723 focused tests, Release publish and matching native host; Security 374, Email 297, General 188, foundation 36, Subtitles 64, Monitor 113 and Dashboard 184 native PASS checks. Product DLL SHA256 5DECA87ED788CE0D07DCE133C545128BB952D44803A1544F5C55EF6536808BD1. The initial full run hit a Library count-restoration assertion and stopped progressing; both isolated variants and the complete rerun pass. Synthetic server/current DPI only, with 35 hashed captures at four window sizes; no live-server/all-DPI/complete visual-parity claim. See D:/SiloPlayer-build/server-update-nov14/configure-security/takeover-receipt.json. Security is Done in qualifying non-draft Beta PR175 (https://github.com/ellermw/SiloDesktopApp/pull/175), separately unmerged/unreleased. Automatic approval review rejected removal of generated build/publish outputs as blocked by policy; no retry was attempted and recovered space is 0 GiB on C: and D:. Active shared output remains about 4.56 GiB with over 50 GiB free on both drives. The older receipt below covers only its original base.

---
# November 15 - Configure Security implemented (In progress, awaiting PR)

Security (C2b) is implemented and verified locally at code 1e71c820a7c7deb1d91226fb5d47d8698c9c1409. It is based on final Email PR174 4958d221 (runtime 3ab6d459), which contains General PR173 905f9ba2, Beta a06cc8b3, PR167 3f0fe220 and PR171 3331916b. The lead owns independent review, push and the Beta PR. It stays In progress until it is pushed to a PR. General (PR173) and Email (PR174) are Done; the remaining 16 leaves are unstarted. PR173/PR174 were open and unmerged when last observed. No Configure change is merged or released.

`/admin/settings/security` is registered under Sign-in & Security with all 32 settings. The local-network key uses the shared settings form/store/API. The 31 rate-limit values use a separate `AdminRateLimitApi`, draft and controller, with their own strong config validator and status hints, and never go through the batched PUT. One Save runs the PUT, then one captured PATCH. A failed PUT skips the PATCH, partial success stays truthful, and the validator-less receipt is followed by a config re-read. A 412 needs an explicit Reload that rebases only the edited fields. An unknown outcome is never replayed and is reconciled on Reload. Lead decisions Q1–Q9 are applied: the failed rate group shows an inline error with Retry, rate editors freeze from the Save click, numbers have distinct accessible names, there are no dirty dots, and number entry accepts plain ASCII integers only.

Official main was freshly fetched at c11b0a968f0d5980b7005579fb95acc2d11d8321 (unchanged). Version remains 1.2.1081-beta.1 (numeric 1.2.1080.1); the pipeline owns stamping and packaging. Final checks on 1e71c820 passed:

- 663 focused Admin/architecture tests and 7,118 full unit tests.
- Release publish with a matching native host. The product DLL F2609157… is identical in publish, native-reference and host.
- Native groups on the same bytes: admin-configure-security 374 PASS, admin-configure-email 297, admin-configure-general 183, admin-manage-foundation 36, admin-subtitles 64, admin-monitor 113 and admin-dashboard 184.

A mutation run turned the 8 targeted unit tests red. A native red/green pair covers the full-width settings column and the unclipped unit slot.

See docs/parity/admin-security.md, docs/parity/admin-configure-checklist.json and D:/SiloPlayer-build/server-update-nov14/configure-security/final-receipt.json for exact hashes, captures and limits. Verification used a synthetic server at the current DPI only. No real IME, toast display, high-contrast run, live server, all-DPI or complete visual-parity claim is made. Shared output is about 4.55 GiB; C: 184.69 GiB and D: 474.84 GiB free. No install, restart, push, PR, merge or release action was taken. The installed player and settings are untouched.

---

# November 15 - Neutral opening for playback More

The playback ellipsis menu no longer preselects Playback info (or another
action) when opened. The shared menu's first-enabled-row fallback caused the
misleading selection even with the info panel hidden. Only More opts out:
current-choice menus retain their defaults. First Down/Up reaches the first/last
enabled action, Enter still invokes the first enabled action from neutral,
pointer selection still works, and actual toggle state remains labeled.

Baseline Beta: 6cdb2db93561c4ad957765d6376794d842b647ff. Version remains
1.2.1081-beta.1 / 1.2.1080.1; pipeline owns stamping. The real OSC/native
regression failed on the original initial selection and passes after the fix.
Final Release publish/native build, 424 related tests, native More keyboard and
pointer variants, and native Speed presentation pass. The neutral native menu
capture was visually inspected. Independent static review found no actionable
issues. Final app DLL SHA256:
A9C5710F350F91C70C44D67FBF520994963D764DAB4A7C67C6CC9B360802AFC7.

Broader native Chapters fails at thumbnail decoding and full native Speed fails
at same-owner replacement speed/pause state. Both reproduce with the two
product files restored to unchanged Beta, and remain outside this fix. Checks
use synthetic media and offscreen windows; live-account, physical keyboard and
Narrator/UIA focus verification are not claimed. The installed player remains
open; no install, restart, settings change or direct release was performed.

Evidence and review: D:/SiloPlayer-build/playback-info-preselection/, including
results/final-more, final-more-pointer, final-speed-presentation, baseline-chapters
and baseline-speed. Automatic approval review blocked removal of superseded
unit output; cleanup was not retried and recovered 0 GiB. Task outputs remain
within the 10 GiB limit with over 50 GiB free on both drives.

---

# November 15 - Reader cooperative search and inline CSS increment

Independent Beta baseline `2c33599f751e1bb7e18a56ee378ffe918710d74d`, unchanged
1.2.1145-beta.1 / 1.2.1144.1. Ports official #4331's segment checkpoints and 25 ms
search-yield budget, preserving bounded results and the desktop overlap fix.
Ports #4325's applicable HTML chapter serialization so CSS containing `>` and
`&` survives invalid-XHTML fallback. Official reference is
`abd85d0cadac28f4ab7db6821dad9634fc114790`.

The three Node regressions and native CSS-selector regression failed before
their corrections. Final 26 Node search/location checks, 43 related main tests,
Release publish/matching host and native WebView2 reader interoperability pass.
Packaged runtime files match source; app SHA256
`9C26BA933CF59CBF313F1177F597206239D606B77A93B9EC31C6AA1C10B82297`.
See [scope and exact evidence](parity/2026-11-15-reader-responsiveness.md) and
task-local `.codex-tmp/reader-final-receipt.json`. All .NET/native runs held
`SiloOct18Build`; shared Configure outputs and the installed player were
untouched. No direct merge, release or installer action.

---

# November 15 - Configure Email delivered in Beta PR174

Email is implemented, independently reviewed and verified at code 3ab6d459a3c06fe7844cc5baaa83723d67cf001a, based on General PR173 905f9ba2 and Beta a06cc8b3. All eight Email settings, the password Clear/Keep/save states and the saved-settings test-email action are reachable through production Configure navigation. Lead review reproduced and fixed an authority-retirement race: password input awaiting PasswordChanged now clears when the owner retires. The added native regression failed on prior product bytes and passes on final bytes. Shared settings dialog/alert/skeleton/select primitives are also covered by General regression checks.

Final checks pass: 7,071 main unit tests, 616 focused Admin/architecture tests, Release publish/matching native host, and native Email 297 PASS records, General 183, foundation 36, Subtitle Files 64, Monitor 113 and Dashboard 184. Source/payload/log/capture hashes are in D:/SiloPlayer-build/server-update-nov14/configure-email/final-receipt.json; parity details and known failed candidates are in docs/parity/admin-email.md. Layout/state captures cover 1440x900, 1440x600, 1920x600 and 800x600. Synthetic server/current DPI only: no live SMTP, all-DPI/full visual parity, Narrator, wheel-distance or measured performance certification.

Delivery status: General Done in open PR173; Email Done in open non-draft Beta [PR174](https://github.com/ellermw/SiloDesktopApp/pull/174); the other 17 leaves remain unfinished. No Configure changes are merged or released. Official main re-fetched at c11b0a968f0d5980b7005579fb95acc2d11d8321. Version remains 1.2.1081-beta.1 (numeric 1.2.1080.1); pipeline owns stamping/packaging/reconciliation/merge. Final storage preflight: 4.517 GiB generated, C184.87/D474.87 GiB free. Installed player/settings remain untouched.

---

# November 15 - Configure Email implemented (in progress)

Email (Configure > Integrations, C2a) is implemented on General PR173 head 905f9ba2 and registered through the production Configure entry point. It reuses General's guarded settings API, shared snapshot store, form and settings UI. All eight email.* keys stay visible whatever the enable toggle says. Encryption shows STARTTLS for an empty saved value without staging an edit. The write-only SMTP password supports erase-to-keep, explicit Clear/Keep and a freeze during saves. Environment-managed keys are protected, and a failed password-status read offers Retry instead of being treated as no password. Send test is a one-shot POST /api/v2/admin/email/test with only the trimmed recipient; it uses saved settings and the official lib/email.ts validation. An ambiguous or malformed acknowledgement is reported as unknown and never resent.

Status: **In progress** until the lead verifies it and pushes it to a Beta PR; General remains Done in PR173 (open, unmerged). Security and the other 17 leaves are unfinished. Nothing is released. Official main is unchanged at c11b0a968f0d5980b7005579fb95acc2d11d8321 and Beta at a06cc8b3. Version stays 1.2.1081-beta.1; the pipeline owns stamping.

Code e7727bbf passes 7,071 full units and 616 focused Admin/architecture tests. Release publish and the matching host build succeed. Native admin-configure-email records 296 PASS and was captured at 1440x900, 1440x600, 1920x600 and 800x600. The same bytes pass General 183, foundation 36, Subtitle Files 64, Monitor 113 and Dashboard 184. Product DLL E529A1C9…; one earlier full run had an unrelated DirectHttpReader temp-file lock and is recorded separately. See docs/parity/admin-email.md and D:/SiloPlayer-build/server-update-nov14/configure-email/implementation-report.md. All checks used a synthetic server at the current DPI; no live mail server, all-DPI, Narrator or performance claim. Shared output is about 4.5 GiB, with C 185/D 474 GiB free. Installed player and settings are untouched.

---

# November 15 - Configure General verified for Beta submission

General is implemented and registered through production Admin navigation, search and Back. It includes Identity, Network, Database and Redis, guarded ETag saves, secret keep/clear semantics, managed/protected-state recovery, connection checks, dirty/restart indicators and Cancel/Discard departure. The full Configure inventory remains 3 topics, 19 leaves and 88 nested feature entries. General is Done by the owner's delivery definition: verified and pushed to non-draft Beta [PR173](https://github.com/ellermw/SiloDesktopApp/pull/173). The other 18 leaves remain unstarted at this delivery checkpoint. PR173 is open and unmerged; no Configure change is released. Email is the next independent group.

Official main freshly fetched at c11b0a968f0d5980b7005579fb95acc2d11d8321; Beta a06cc8b3 (Subtitle Files PR169) and required PR167 3f0fe220 / PR171 3331916b are reconciled. Version remains 1.2.1081-beta.1 (numeric 1.2.1080.1); the pipeline owns stamping/packaging. Final code 54afa5d4 passes 7,019 full units and 564 focused Admin/architecture tests, Release publish/matching host, native General 183 PASS records, Manage foundation 36, Subtitle Files 64, Monitor 113 and Dashboard 184. Captures cover 1440x900, 1440x600, 1920x600 and 800x600, real controls/dialogs, alignment and gutter hit-testing. Independent Claude findings are fixed; new native Redis erasure and Reload focus/error regressions failed on prior bytes and pass on final bytes. Dynamic status texts raise explicit automation events; Narrator itself was not certified.

See docs/parity/admin-general.md, docs/parity/admin-configure-checklist.json and D:/SiloPlayer-build/server-update-nov14/configure-general/final-receipt.json for exact source/payload/capture hashes and limits. Synthetic backend/current DPI only; no complete visual/all-DPI parity, live-server, measured wheel delta or performance claim. Historical Monitor Logs failures remain separate. Shared output about 4.47 GiB, C185/D474 GiB free. No cleanup deletion, install, restart, manual PR merge or release action. Installed player/settings remain untouched.

---

# November 15 - Configure General backend (intermediate)

Intermediate C1 backend on inherited dd96e055, version 1.2.1081-beta.1 unchanged, pinned to official server/WebUI c11b0a968f0d5980b7005579fb95acc2d11d8321. Adds guarded settings transport, authority-scoped single-flight snapshots, a redacted draft and General controller. General is not yet registered or visually verified. Lead is integrating the native page, Configure catalog, shell/cache refresh and production registration; no push, release, installer or installed-player action was performed. See docs/parity/admin-general-backend.md and D:/SiloPlayer-build/server-update-nov14/configure-general/implementation-report.md for scope and verification.

---

# November 15 — Admin Playback History and Skip Markers

Adds native read-only finalized Playback History and marker edit history on
PR167's Manage foundation (4b962d5d79c42d0ea27e0c45fd418a14ec54c815).
Reference: official Silo e61187daaf72e8accad486eb2f632c753ae211d5; this is
an explicit reference pin, not a claim that every newer change is aligned.
Version remains 1.2.1064; the Beta pipeline owns stamping and packaging.

History reads one bounded 100-row window, pages locally at 25/50/100, bounds
account/profile pickers, joins in-flight refreshes, polls only while open and
visible, and retires/clears on authority changes. Exact positive Int64 account
IDs are preserved. Unchanged refreshes retain table focus/scroll and update
relative times in place. Skip Markers is read-only, capped at 100 with explicit
Refresh. No mutation actions or hidden page loading are introduced.

Final checks: 198 focused checks and 6,793 full units passed (zero failed or
skipped), Release publish/native host passed, and admin-playback-audit,
admin-manage-foundation and admin-monitor native groups passed. Reviewed
captures cover desktop/narrow layouts; synthetic-only limits and review
resolutions are in docs/parity/admin-manage-playback-audit.md and the delivery
receipt under D:/SiloPlayer-build/server-update-nov14/manage-playback-audit/.
No live-server, full visual/DPI/multilingual, performance or newest-server claim.
Installed player/settings untouched. Shared generated payloads remain active;
cleanup was not retried after the prior automatic approval rejection and
0 GiB recovered is claimed on C: and D:.

---

# November 15 — Share library detail reads and bound detail speculation

Library detail navigation now shares item-and-library-scoped requests with card
intent. Unclaimed detail speculation is limited to one live request, replaces
older intent, and respects the existing 140 ms hover dwell, 32-entry retention
and two-minute expiry. Foreground reads adopt matching work or start immediately.
Saved library state and ratings-capability prerequisites start concurrently.

Scoped detail pages retain ownership of realtime and progress-save reconciliation:
the cache supplies the initial shared snapshot without issuing a second set of
corrective reads. Save settlement invalidates synchronously, with cancellation
callbacks outside the cache lock. Cold reads start without waiting for the UI
queue. Existing unscoped cache freshness and final-save rules are preserved.

Verification: 490 focused tests; 6,621 full unit tests; Release app publish and
native host build; retained-library-review and oct20-detail-cards native groups.
Evidence: D:/SiloPlayer-build/server-update-nov14/background-loading/ and
D:/SiloPlayer-build/results/*-bounded-detail-reviewed/. Source baseline is
f1a98408e2865302432dfe2a058f1702ea516b23; version stays 1.2.1064 under pipeline control.

These checks establish sharing, request bounds and freshness with synthetic data.
They do not measure real-server latency, scrolling traffic, HTTP/1.1 cancellation
cost or total process memory. Watch-detail speculation remains a separate,
uncapped path and needs its own follow-up; this is not a claim that all background
work or full WebUI parity is complete. Shared generated outputs remain in use by
the ongoing parity task; no cleanup was attempted here, and recovered space is
0 GiB on C: and D:. The installed player and settings were not changed.

---

# November 15 - Admin Manage Subtitle Files

Adds the registered Subtitle Files page on PR167's Manage foundation: bounded cursor lists,
URL filters, uploader discovery, guarded metadata editing and deletion, and bounded subtitle
downloads. The pager remains mounted across reads; row actions bind directly to their row,
queued renders log failures, and download/uploader-only updates retain rows and keyboard focus.

Official reference: ad7fe63a3428be01bf3da56738ce0e861c871393. Subtitle Files contracts
and WebUI are unchanged from fd0d8c6c. Base: PR167 head 4b962d5d79c42d0ea27e0c45fd418a14ec54c815;
version remains 1.2.1064 under Beta pipeline control. This increment depends on PR167.

Verification passed 184 focused tests, 6,785 full unit tests, Release publish/native host build,
admin-manage-foundation, admin-monitor and three consecutive admin-subtitles native runs,
including narrow viewport checks. Inspected list and conflict-sheet captures. Evidence:
D:/SiloPlayer-build/server-update-nov14/manage-subtitle-files/lead-delivery-receipt.json.
See docs/parity/admin-manage-subtitle-files.md for scope and remaining review limits.
No live-server, real save-picker, multilingual, all-DPI or complete WebUI parity claim.
Installed player/settings untouched. Shared reusable build outputs remain active for the
ongoing parity task (about 1.48 GiB); no cleanup deletion was retried, 0 GiB recovered on C:/D:.

---

# November 14 - Admin Manage navigation and bounded-data foundation

Adds six Manage topics and 21 route identities, immutable nested/query state,
registered-page-only navigation/search, shared authority and dirty guards,
request-bound cursor pagination and scope-bound complete collection snapshots.
This foundation registers no production Manage page. Monitor remains available.

Reference: official WebUI 1452ea2d10efbe7d3ea5ce7140fb49756b493670. A fresh
fetch at fd0d8c6c09af11aa2b165034e6c553a45334f2fd adds watchsync and player
marker changes; foundation reference files are unchanged. Version 1.2.1064
remains under Beta pipeline control. Independent review findings were resolved;
see docs/parity/admin-manage-foundation.md for the frozen domain contracts.

Focused checks passed 157 tests. Initial full verification passed 6,746 unit
tests, Release publish/native build, admin-manage-foundation and admin-monitor.
The native fixture verifies URL replacement/Back, registered badges, generic
leave guards and outer-page suspension with synthetic data. Final dependency
reconciliation verification is recorded in the delivery receipt under
D:/SiloPlayer-build/server-update-nov14/manage-foundation/.
No finished Manage domain, live-server or full visual/DPI parity claim.
Installed player/settings untouched. Shared reusable generated payloads remain
active; cleanup was not retried after the prior automatic approval rejection,
with 0 GiB recovered on C: and D:.

---

# November 14 — Preserve stored subtitle identity after deletion

A same-file recovery or queued subtitle selection now resolves a stored subtitle by
its unique stable Id/SyncKey before considering positional track IDs. Deleting an
earlier subtitle can no longer redirect the selection to the row that reused its
position. Missing, duplicate, conflicting or unplayable identities are refused.
Identityless and cross-file behavior, wire contracts and retained-unavailable
selection policy are preserved. Older plans that omit stored keys may rebind by
unique, available TrackId only when that row has no stored identity and no stored
identity match exists. A row carrying a different stored identity is still refused.

Reference: official Silo Server 1452ea2d10efbe7d3ea5ce7140fb49756b493670;
subtitle deletion behavior audited from #4080. Baseline 6caafd79, version 1.2.1064
unchanged; the Beta pipeline owns stamping.

The older-plan compatibility follow-up adds unit and native regression coverage;
builds and tests for that follow-up are deferred to the pipeline.

Verification before the compatibility follow-up: 6,618 unit tests passed, Release publish
and native host build passed, focused reload-identity native cases and the broad
subtitle-identity native group passed. The 17 new identity regressions were first
shown failing against baseline resolvers. Independent static review found no
blocking defect. This does not certify live-server or full playback parity.

Two stale native fixture seams were corrected: reflection now supplies all five
refresh-method arguments, and the cast fallback gate holds the managed read used
by production. The prior cast-gate timeout reproduced against baseline product;
the corrected broad fixture passes on baseline and fixed product.

Evidence: D:/SiloPlayer-build/server-update-nov14/subtitle-identity/ final-publish.log,
final-host.log, final-all-units.log, independent-review.md; native receipts under
D:/SiloPlayer-build/results/subtitle-identitysubtitle-final-reload-identity/ and
subtitle-identitysubtitle-final-broad/. The installed player was not touched.
Shared generated output remains in active use; no cleanup deletion was retried.


---

# November 14 — Bound and retire ebook searches

Ebook search follows official Silo Server `7115ee7028c7c9bde9e201fd5f54e87ea70de3a7`: a whole-book 1,000-result limit, cooperative cancellation and corrected cross-node excerpts. New queries, clearing, file changes and leaving retire pending work. Native result adoption checks search, open and file identities. The native list joins excerpt parts as readable text, uses the current eight-language truncation message, and reports current renderer search failures. The relevant upstream vendor search files are unchanged through `1452ea2d10efbe7d3ea5ce7140fb49756b493670`.

Final validation: all 6,601 unit tests passed, 18 Node reader tests passed, Release app publish and native host build succeeded, and the published `reader-interop` native group passed. Regression tests reproduced the raw-JSON excerpt and rejected-search failures before their fixes. Static independent review is retained at `D:/SiloPlayer-build/server-update-nov14/ebook-search/independent-review.md`; missing catalog and excerpt findings were corrected. Existing shared-reader script-execution and fully rendered EPUB/PDF/FB2 interaction/visual coverage remain limited; fixture and synthetic-DOM checks do not establish complete visual parity. Product version stays 1.2.1064; Beta owns stamping. Translation dependency: PR #162. No live server request, install, restart or settings change.

---

# November 14 - Invitation and password-reset server alignment

Aligns account links with official Silo Server/WebUI
1452ea2d10efbe7d3ea5ce7140fb49756b493670. Fragment and legacy links are
recognized; current servers receive tokens in request bodies. Capability and
405 compatibility rules retain older-server transport without replaying an
acceptance or completion. Unknown invitation outcomes require explicit reload;
only confirmed accepted responses show account creation. Invitation notes,
optional email, editing and neutral success presentation follow the reference.

Isolated baseline 6caafd79129daa36f4aedb290c1720b7cde8ac64, version 1.2.1064
unchanged; Beta pipeline owns stamping. Verification: 146 focused tests and all
6,731 unit tests passed, Release publish and native host build passed, and the
security-origins native fixture passed including malformed-success recovery,
email editing, invitation notes and confirmed-creation presentation. Five new
malformed-acceptance wire cases failed before response validation and passed
with it. Independent review findings were addressed before final verification.

Evidence: D:/SiloPlayer-build/server-update-nov14/auth-links/ and
D:/SiloPlayer-build/results/security-origins-auth-links-reviewed/. No live old
server, OS protocol handoff or complete authentication visual parity is claimed.
HTTP links require a launcher forwarding them; installer registration remains
silo://. The new server-mismatch message has an English fallback. Installed
player/settings are untouched. Shared generated outputs remain in active use;
cleanup was not retried after the earlier automatic approval rejection, with
0 GiB recovered on C: and D:.

---

# November 14 - Admin Manage navigation and bounded-data foundation

Adds six Manage topics and 21 route identities, immutable nested/query state,
registered-page-only navigation/search, shared authority and dirty guards,
request-bound cursor pagination and scope-bound complete collection snapshots.
This foundation registers no production Manage page. Monitor remains available.

Reference: official WebUI 1452ea2d10efbe7d3ea5ce7140fb49756b493670. A fresh
fetch at fd0d8c6c09af11aa2b165034e6c553a45334f2fd adds watchsync and player
marker changes; foundation reference files are unchanged. Version 1.2.1064
remains under Beta pipeline control. Independent review findings were resolved;
see docs/parity/admin-manage-foundation.md for the frozen domain contracts.

Focused checks passed 157 tests. Initial full verification passed 6,746 unit
tests, Release publish/native build, admin-manage-foundation and admin-monitor.
The native fixture verifies URL replacement/Back, registered badges, generic
leave guards and outer-page suspension with synthetic data. Final dependency
reconciliation verification is recorded in the delivery receipt under
D:/SiloPlayer-build/server-update-nov14/manage-foundation/.
No finished Manage domain, live-server or full visual/DPI parity claim.
Installed player/settings untouched. Shared reusable generated payloads remain
active; cleanup was not retried after the prior automatic approval rejection,
with 0 GiB recovered on C: and D:.

---

# November 15 — Admin Activity page scrollbar and side-gutter wheel scrolling

Built on Beta 8f01cf8be7f7dc271b5d70294ac6ce8e23ffa66a (1.2.1081-beta.1; pipeline owns
stamping). Official WebUI reference c11b0a968f0d5980b7005579fb95acc2d11d8321: AdminLayout
pads the scrolling document with the page gutters, so its scrollbar is at the viewport edge.

Cause, measured natively: Activity's page ScrollViewer lived inside the shell's centred
max-1560 column, whose margins are the gutters. Its overlay scrollbar therefore sat on the
column's right edge (1387–1399 at 1440 wide) over the rightmost text (1395.7) and on top of
the nested streams scrollbar (1386–1398). The gutters hit-tested to the shell root Grid, outside
any scroller, so wheel input there did nothing. Activity now opts into
`IAdminGutterScrollPage`: the shell gives its scroller the full content area and applies the
same gutters/max width to the scroll content. Other Admin pages keep the existing column.
Content, table and nested-scrollbar bounds are unchanged; no requests, timers or transitions added.

The admin-monitor native fixture's new `activity-scroll` case (also in the default run) measures
1440x600, 1920x600 and 800x600. It went red on unchanged Beta and passes after the fix. It checks
page and nested scrollbar separation, rightmost-text clearance, preserved centred layout and nested
bar placement. It also checks gutter hit-test ownership and that probes across an unstriped nested
row stay with the nested panel, plus Logs/Activity route restoration. Wheel routing is evidenced by
hit-test ownership. WinAppSDK input did not consume posted or sent WM_MOUSEWHEEL, and global
SendInput was not used, so no real wheel delta was measured.

Final: 356 focused Admin/architecture units passed; Release publish (SiloPlayer.dll
089E6363302C1F102F2E3F631A15CD1B3DD05A0B442E812649D1FE16BC2572B9), native host, admin-monitor
(all cases) and admin-dashboard passed. admin-monitor-logs fails identically on unchanged Beta
(Logging restart-save/profile-verification and dialog-collision checks; 208 passes, 10 failures);
not addressed here. Evidence: D:/SiloPlayer-build/server-update-nov14/activity-scrolling/report.md.
Installed player and settings untouched; no cleanup deletion attempted.

---

# November 14 — Dashboard layout intent and current server presentation

Official source pin: 1452ea2d10efbe7d3ea5ce7140fb49756b493670. The relevant
Dashboard source and API contracts are unchanged from the initial 7115ee70 audit.
Built on ff109bfe (Admin shell/Activity PR #160, dependent on Dashboard PR #159).
Version 1.2.1064 unchanged; pipeline owns stamping.

Dashboard layout saves preserve the Egress Total/Nodes view. Editing before the
first server read compares the starting layout with the server rather than
replacing a changed copy. Pending edits survive leaving during the initial read,
scoped to account/server/profile; retired authority cannot save or erase them.
Reload/Reset resolve the pending marker; local metadata never enters server saves.
Now Playing shows position/duration, uses Activity's episode subtitle, and switches
between one/two columns at its own 672-DIP width rather than viewport width.

Verification: 152 focused Dashboard tests and 6,665 full unit tests passed.
Release publish, native host build and the reviewed admin-dashboard native run
passed. Independent review found no product P0/P1; its four fixture findings
were corrected (real time-label bounds, per-action DELETE count, cleared shared
layout cache and DPI-aware sizing in the new cases). Two initial unit failures
used an invalid nine-column Users layout despite the eight-column cap; fixture
inputs were corrected without changing the product clamp.

Evidence: D:/SiloPlayer-build/server-update-nov14/dashboard-current-ui/ contains
lead-focused-corrected.log, lead-publish.log, review-host.log, review-all-units.log
and independent-review.md. Native receipts/captures are under
D:/SiloPlayer-build/results/admin-dashboard-dashboard-current-ui-reviewed/.
Synthetic geometry/state verification does not certify complete visual parity,
all DPI settings or live-server behavior. Installed player/settings untouched.
Generated shared payload remains actively used; no cleanup deletion retried.

---

# November 14 — Refresh offline translations for current server changes

Regenerated the eight shipped offline catalogs with the existing SiloTranslationsImport tool from official Silo Server commit `1452ea2d10efbe7d3ea5ce7140fb49756b493670`, whose vendored translation release is `2026.11.15-5201582`. Each language now contains 6,858 non-admin messages (76 additional keys); the generator records exact source and output checksums. Existing admin-key exclusion and supported language list are retained. The locale source was read from a clean sparse reference; no generated message was edited by hand.

Focused catalog-integrity, reader-key and library-refresh checks: 70 passed. This supplies current message keys; it does not implement all features that consume those keys or establish visual translation parity. Product version remains 1.2.1064, with stamping owned by the Beta pipeline. Evidence: `D:/SiloPlayer-build/server-update-nov14/ebook-search/catalog-import.log` and `review-focused.log`. The installed player and settings were not changed.

---

# November 14 — Admin Monitor visual follow-up verification

Official WebUI main rechecked at 1a99f7beffcea3850f067df000c319f2704fd23b.
Follow-ups integrate PR156 source 31bc6b9b, merged to Beta as 74f14630; version remains
1.2.1056-beta.1 / numeric 1.2.1055.1, with pipeline stamping unchanged.
The original PR/branch was not pushed by this work.

Final integrated checks: 6,591/6,591 main units, 77/77 focused Logs checks,
Release publish/native build, and Admin shell/Activity, all-widget Dashboard and
baseline Logs native groups pass. Across bounded runs all 75 expanded Logs
scenarios pass, with named functional evidence in 79/81 families. The 162-row
ledger separates captures/assertions from visual comparison and records gaps.

Changes are grouped into theme tokens, Logs/foundation, Dashboard, and
shell/Activity follow-ups. PR156 is still a dependency for the Admin groups;
Logs is aligned to Beta after PR156 merged; Dashboard and shell/Activity remain stacked until their bases reach Beta.
Final per-cut validation: all three Admin native groups pass; 6,592/6,593 main tests pass on the full run, with the one updater-helper failure passing all three cases on isolated rerun; 77/77 Logs tests pass. The earlier 6,591 result above remains historical integrated evidence. See [verification and limits](parity/2026-11-14-admin-visual-followups.md).
No complete-parity, live-server, physical-input, all-theme/DPI/locale or full
playback-overlay certification is claimed. Installed player/settings untouched.
Earlier automatic approval review rejected cleanup; no deletion was retried and
0 GiB recovery is claimed on each drive. Local receipt:
D:/SiloPlayer-build/admin-visual-parity/final-visual-followup-receipt.json.

---

# November 14 — Preserve updater Details across helper handoff

The first Details click during Preparing now carries its expanded state into the
separate updater helper along with the native window bounds. The helper measures
against its actual client size, so transferred expanded bounds cannot retain
collapsed content and leave a black lower rectangle. Version remains 1.2.1064;
Beta pipeline owns stamping. Source baseline: 6caafd79129daa36f4aedb290c1720b7cde8ac64.

The cross-process regression failed before the fix with expanded=False,
body=Collapsed/0, root=95, client=221; after the fix it reports expanded=True,
body=Visible/112, root=221, client=221. Seven final app-updates native runs passed:
expanded and collapsed transfer, preparation Details, helper Details, cancellation,
failure, and automatic consent. Physical pointer/pixel checks exercise three
expand/collapse cycles before progress. Full unit suite: 6,601 passed, zero failed
or skipped. Evidence: D:/SiloPlayer-build/server-update-nov14/updater-final-*.log
and D:/SiloPlayer-build/results/app-updates-details-final-*.

Checks use fixture-owned windows and a synthetic installer; no real installation
or installed-player restart occurred. Physical multi-monitor/DPI combinations are
not certified. Generated output cleanup remains blocked by the earlier automatic
policy rejection and was not retried; recovered 0 GiB on C: and D:.

---

# November 14 — Admin Monitor implemented for Beta

Native Admin Monitor adds an acting-admin-only footer entry above Profile,
Dashboard (all 28 widgets), Activity, Server Log, Client Diagnostics and Logging
Settings. The shared Server Activity indicator follows the official WebUI's
visibility rules. Admin navigation swaps in place without slides.

Official reference: 1a99f7beffcea3850f067df000c319f2704fd23b. Started from Beta
8f080836 and integrated d96759b4. Version remains 1.2.1017-beta.1 / 1.2.1016.1;
pipeline owns stamping. Branch codex/admin-monitor-nov14.

Full integrated units: 6,367 passed, zero failed/skipped (2m22s); final focused
Admin/source/architecture checks: 268 passed. Release publish
and native build passed. Admin Monitor, all-widget Dashboard, Logs, profile-menu
navigation and playback-scope native groups passed. Five-size synthetic WebUI
and native captures informed layout corrections. Review findings and native
chart, lifetime and false-draft defects were corrected.

The adjacent upstream-shell fixture still fails on narrow pane resizing with
COM 0x80004005. Exact Beta shell code with new Admin hooks disabled reproduces
that failure; the source was restored and hash-checked. No full-suite native or
live-server/pixel-equivalence certification is claimed. Admin text catalogs
currently resolve English; the separate localization foundation is not imported.

See [scope, evidence and limits](parity/2026-11-14-admin-monitor.md), the scoped
[Monitor allowlist](parity/admin-monitor-surfaces.json), and the final local
receipt D:/SiloPlayer-build/admin-monitor-final-receipt.json. The installed player
and application settings remain untouched. Cleanup was not retried after the
earlier automatic policy rejection: recovered 0 GiB on C: and D:. The receipt
records final free space and reusable output size.

---

# November 14 - Mini-player native popup during replacement preparation

Independent increment from Beta cbafac58cdd4987323d36031b7881e2f2fc98e54;
version 1.2.1017-beta.1 / 1.2.1016.1 unchanged. Stopping a mini video left
its 200x112 native popup over Preparing Playback; queued resize/restore could
re-show it after a one-time hide. Preparation owns suppression of native
ShowWindow and SWP_SHOWWINDOW until the matching ready playback releases it.
Suppression hides idle, mini and PiP surfaces; expanded/fullscreen replacements
retain their outgoing surface so fullscreen preparation does not expose the
desktop. First native initialization is gated too. Delayed fullscreen position
reassertion dispatches to the UI queue.

The implementation run passed 6,107 units and six new native preparation cases,
plus post-roll, autoplay and five adjacent native groups. Independent review
found that blanket suppression hid fullscreen playback. Lead reproduced that
failure, scoped suppression, and added expanded/fullscreen/PiP controls. Lead's
final Release publish and host build pass; all nine preparation cases plus
post-roll/autoplay pass on the final product. The lead reran 112 focused tests.
Published/reference/host product SHA256:
F760F9E715896334D40CBD5BBF3A2412DAC0C5F576208435C089440C0313DDC1.
Earlier full-suite/adjacent results predate the final scope correction and are
not claimed as fresh full-suite verification of that correction.

Evidence: D:/SiloPlayer-build/mini-prepare-delivery-receipt.json,
mini-prepare-lead-focused.log, mini-prepare-verified-source-publish.log,
mini-prepare-verified-source-native-build.log and
results/hud-completion-*-verified-source/results.txt. Original red receipts and
mini-prepare-receipt.json retain their original hashes and scope.

The installed player PID 40372 remains open. Native checks use held synthetic
HTTP preparation and real offscreen HWND visibility/geometry, not live-account
visual verification. No installed-player change, installer or direct release.
Earlier automatic approval review blocked cleanup; no deletion was retried,
0 GiB recovered on C: and D:, about 1.93 GiB shared generated output retained.

---

# November 13 - Mounted library scan-refresh increment

Canonical/legacy catalog changes now coalesce in place, preserving authorized
cards and both letter landing boundaries; reverse-order clicks and failed/churned
refreshes hand back safely. Access hides old cards immediately. Final138 focused
units, fresh publish/native build and library-scan-refresh/library-reconnect
native groups passed. Initial full6115 preceded review corrections and remains
historical only. See [scope/evidence](parity/2026-11-13-upstream-4448-library.md).

Base6f9f87f7;1.2.1017-beta.1 /1.2.1016.1 unchanged. At submission, RankingPR148
was separate and metered quality was pending; both have since landed on Beta.
MarkerPR149 has landed on Beta.
PR145 remains pipeline-owned. Existing output area is reused within10GiB and
both drives remain above50GiB; no cleanup retried after the prior policy block,
recovered0GiB C:/D:. Installed player remains open.

---

# November 14 - In-place page changes (no navigation slide)

Branch t3/remove-sidebar-slide starts at Beta cbafac58; version 1.2.1017-beta.1
unchanged. Per the owner, sidebar route changes and Back no longer rise/slide:
`PageTransitionHelper.ForPageChange()` is now `SuppressNavigationTransitionInfo`
for every route and reduced-motion setting, and eight direct Frame calls that fell
back to the Frame's default theme transition now name it (Taste Seed, detail retry,
collection Back, Watch Party). Retained pages, scroll and soft state, the in-place
refresh, skeletons and artwork reveal are unchanged; no whole-page opacity was
added. A new source guard rejects any app Frame navigation without it.

New tests were red on Beta (2 failures, 8 offending call sites) and green after;
the implementation run passed all 6,108 units. The Debug x64 app and native host
compiled against a worktree-local build. Lead verification passed 14 focused
source tests and the `upstream-shell` fixture with `SILO_SHELL_NAVIGATION_ONLY=1`:
three navigation/Back rounds retained the same page instance and marker; every
observed navigation and current-entry refresh used the suppressed transition,
refresh preserved history length, and the incoming root had identity transform
and opacity 1. The broader upstream-shell run passed the transition check but
failed later with COMException 0x80004005 during responsive pane resize; no full
shell or visual parity pass is claimed. Windows animation settings were not
toggled; the transition has no animation-setting branch.

No navigation-speed claim is made. Background page-data warmup was not added:
top-level view models are transient per page with private freshness windows, so
warmup needs a shared context-scoped, strictly bounded store first. A follow-up
should measure cold navigation, reuse only lightweight first-page JSON, bound
memory and concurrency, retire on server/profile/access/language changes, and
skip playback, full Library catalogs, cursor enumeration and artwork crawling.

Evidence: `D:/SiloPlayer-build/no-slide-lead-focused.log`,
`no-slide-native-build.log`, and `results/no-slide-navigation-verified/results.txt`.
The installed player and version stamps are unchanged. Worktree-local output is
about 0.69 GiB; earlier automatic approval review blocked generated cleanup, so
no deletion was retried and 0 GiB was recovered on C: and D:.

---
# November 13 - Finishing artwork on the ended screen

Owner report: after the last Cops episode ended, "You've finished Cops" and On
Deck were right but the finishing slot beside them was a large empty grey
rectangle. True EOF hides the native preview and the slot had no picture of its
own. It now shows the finished title's watch backdrop (a series backdrop for an
episode: never an episode still or anything of the next item), decoded under
the live preview and revealed only at true EOF; with no usable picture the slot
leaves the layout. Closing Playing Next, a newer playback and a profile change
each retire it. A second defect found on the way is fixed: any window move or
resize on the ended screen re-showed the finished video over it.

Branch codex/finished-preview-nov13 on Beta cbafac58; version 1.2.1017-beta.1 /
1.2.1016.1 unchanged. Four new hud-completion cases (finished-early-eof,
finished-eof, finished-no-art, finished-stale) failed against the unfixed
product at an actual libmpv EOF and pass after it, with the nine existing
hud-completion cases, hud-completion-scope and hud-nov12; 6,108/6,108 units.
Product DLL SHA256 E553BDE9D1DDB6B86B36ECDD08C1A3D35C79B88F9DACE725601C1B83D70919D7.
Receipt: D:/SiloPlayer-build/finished-preview-receipt.json.
November 14 lead review: residual mini-bar/PiP resize re-show reproduced and
corrected. Final lead verification passed the four new EOF cases, live
post-roll, and 91 related source/API tests; delivery hashes are recorded in
D:/SiloPlayer-build/finished-preview-delivery-receipt.json. See
[scope and review limits](audit-gaps/2026-11-14-finished-preview.md).

Limits: synthetic solid-colour backdrops and fixture HTTP, not a live server or
the owner's library; shuffle and physical DPI were not exercised. The WebUI
fades its finished mini player out and leaves that column empty; showing
artwork there is a deliberate desktop difference. hud-completion `still` and
hud-nov12 fail when rerun in a result directory that holds an earlier run's
cached image or saved threshold, and pass in a fresh one; the `still` held URL
is now unique per run. Output reuses the same payload (1.4 GiB); cleanup was
not attempted after the earlier policy rejection, recovered 0 GiB on both
drives. Installed player untouched.

---
# November 13 - Continue Watching stale/duplicate-card repair

Independent branch codex/continue-watching-duplicate-nov13 starts at Beta
cbafac58, version 1.2.1017-beta.1 / 1.2.1016.1 unchanged. Unique server data
could still render an old card after Move then Replace. RepeaterItemsSource
now coalesces Move batches into its existing owned deferred Reset; model
membership/order stay authoritative. This is separate from the blank finished
player preview and previous alignment PRs.

Baseline native reproduction failed. Final Release publish/native build,
214 focused Home/media reconciliation units, home-continue-duplicate and
home-reconcile-data passed. Native assertions cover every initial episode
card/title, repeated reorders, insertion before Move, retained horizontal
scroll and detached/reloaded rows. Two of three mid-layout rejection scenarios
were reached, including shrink; the reloaded mid-layout trigger was skipped.
Keyboard focus was unavailable in the hidden host; strict home-reconcile did
not pass. Independent review's P2 fixture finding is corrected; no product
P0/P1/P2 was found, and no second independent review is claimed.

See [incident and limits](audit-gaps/2026-11-13-continue-watching-duplicate.md).
Receipt: D:/SiloPlayer-build/continue-duplicate-receipt.json. One reusable output
payload; prior cleanup rejection was not retried, 0 GiB recovered on C: and D:.
The installed player was not changed or restarted; pipeline owns packaging.

---
# November 13 - Metered quality alignment verified for Beta

Branch codex/metered-quality-4448-nov13 starts at Beta 6f9f87f7 with version
1.2.1017-beta.1 / 1.2.1016.1 unchanged. Shared connection quality uses cellular
limits on identified metered links. Detail observes network/settings changes,
retaining edition/pin and request ownership. Native Settings exposes both limits.
Unknown/offline links keep main quality; older-server 422 uses contract defaults.

Final verification: 6,113 full units passed, zero failures/skips (2m23s).
After review corrections, 60 focused quality/settings/connection tests passed.
Release publish/native build and actual oct24-track-choices plus
settings-visual-oct24 passed, including separate writes, No limit and responsive
geometry at 600-3840 pixels. The P1/two P2 independent review findings were
corrected by the lead; no second independent review is claimed.
See [the complete parity/audit record](parity/2026-11-13-upstream-4448-metered.md).

Official main was rechecked at 4448e401. All 43 commits since 7edef3dd were
classified. Separate PRs #148 (ranking/multipart), #149 (marker cache), and
#150 (scan refresh) are pipeline-owned; this metered increment is separate.
No combined integration, physical-network or live-server certification is claimed.

Output: 1.569 GiB. Free C: 192.290 GiB; D: 480.387 GiB. Earlier automatic
cleanup rejection remains in force; no retry, 0 GiB recovered on each drive.
The installed player was not installed, restarted or modified.
---

# November 13 - Marker-save watch-cache invalidation

Independent upstream4448e401 marker increment: accepted edits/redetection publish
the existing context-scoped metadata invalidation, retiring pre-save /watch
prefetches.44 related units and the actual marker-cache native fixture passed;
Release publish/native build passed. See
[scope and evidence](parity/2026-11-13-upstream-4448-markers.md).
Base6f9f87f7; version1.2.1017-beta.1 /1.2.1016.1 unchanged. At submission, ranking was separately
submitted in PR148; metered quality and library scan refresh remained pending.
Output reuses the same sub-1.3GiB payload; free C:192.84GiB, D:480.83GiB.
Cleanup remains unattempted after the prior policy rejection; recovered0GiB on
both drives. Installed player remains open and PR145 is untouched.

---
# November 13 - Upstream 4448e401 Auto ranking and multipart increment

Reviewed Beta base is 6f9f87f7, version 1.2.1017-beta.1 / 1.2.1016.1. The first
independent increment of the 43-commit update implements server-order detail Auto,
saved later-part Resume, first-part Start Over and ordered multipart downloads.
67 related units, Release publish, native host build and both track-choices and
detail-action-hover native fixtures passed. Independent review found no P0-P2.
See [scope and limits](parity/2026-11-13-upstream-4448-ranking.md).

Marker invalidation is covered by the increment above; connection-quality and
mounted-library scan refresh remain separate pending increments. Pipeline owns
review/fixes/QA/delivery after each qualifying Beta submission. PR145 is untouched;
versions remain pipeline-owned.
One existing output/publish/native payload is reused. Generated data stays below
1.3 GiB, free C: 192.83 GiB and D: 480.84 GiB. Cleanup is not retried after the
previous automatic approval block; recovered 0 GiB on each drive. Installed
player PID38020 remains open.

---

# November 13 - PR #145 section titles and retained dialog review fixes

The seven findings on 6b02f142 are corrected on top of pipeline commit
8868311cb2bd04939dc767835807be5e4bbe3f3d. Requests section/window titles
share the translated heading and retired pages cannot overwrite a replacement
window title. Failed Home heroes use DisplayTitle and refresh their failure
heading without rebuilding the mounted carousel. Metadata close accessibility
and tooltip keys refresh through the dialog lifetime. Image tabs, notices,
current/empty/error labels, provider warnings and idle/pending Apply text refresh
without refetching cards or losing selection/drafts. Collection saving status,
history loading/retry/recovery and Home-row add/edit commands retain their
localized state. Home-row callers supply title/action callbacks.

Release publish and native host build passed. All 6,231 unit tests passed,
zero failed/skipped (2.4310 minutes), including the pipeline Playback Info row-ID
fix. Full settings-localization, media-localization and shell-localization
native groups passed on the same published DLL. Tests cover en/de/ar/ja, actual
Home-row forms, held writes, gallery selection/apply failure and history retry.
All 1,995 recorded source/test inputs match. Product DLL SHA-256:
62C26003B58414D7EF4014C6C91A20C7FD963F261236CDCCF8B87F0EC2BADD4A.
Receipt: D:/SiloPlayer-build/review-thirteenth-receipt.json. Manifest:
D:/SiloPlayer-build/review-thirteenth-source-inputs.json
(EEF314294E8DA58AC14BBAF38995C0B474FAFF9C52EEC57D0FC12C9EC917D1E4).

Native verification reproduced child refresh subscriptions lost during modal
template relocation. Dialog-owned callbacks now retain image Apply/provider
text, and keyed descendant refresh includes tooltips. Fixture corrections wait
for actual popup fields and preserve the established English server-text
fallback; assertions remain language/state specific. Original failure text is
retained in the reused results directory. Product/unit-test inputs covered by
the unit project did not change during the later native-only corrections.

The independent contracts/hero PR #143 and HUD/autoplay PR #144 are merged.
These changes are review corrections to the existing localization PR #145;
new independent work belongs in a separate Beta PR. Pipeline owns versioning,
QA, merge and release. Earlier VM subtitle-timeout and live/visual-parity limits
remain unverified. Source stamps stay 1.2.1015-beta.1 / 1.2.1014.1.

Task output is 1.247 GiB; measured free C: 189.46 GiB, D: 481.26 GiB.
Installed player PID 38020 remains open. Owner AGENTS.md changes stay uncommitted.
No cleanup deletion was retried after the earlier automatic approval rejection;
recovered space is 0 GiB on C: and D:. Final delivery cleanup remains pending.

# November 13 - PR #145 recency, Favorites, seek units and language badges

The four findings on 64c7edba are addressed. Added/Released recency selectors
refresh the remaining 90d/1y/5y options and their closed selection using shipped
day/year unit labels with culture-formatted counts. Unit-first labels avoid
assuming English singular/plural grammar; API tags and selections stay unchanged.
Favorites formats its full failure prefix with the translated Favorites title;
structured problem text still translates and English preserves raw server detail.
Original-language badges resolve language.<code>, preserving unknown raw codes.
All four seek selectors render translated seconds while retaining integer values.
Their template also refreshes on DataContextChanged, so recycled selection text
cannot keep the previous interval.

Release publish and native host build passed. All 6,227 unit tests passed,
zero failures/skips (2.4150 minutes). Full settings-localization,
media-localization, shell-localization and subtitle-sidecar-ownership groups
passed against the same published product DLL. Native checks cover en/de/ar/ja,
unopened recency choices/retained selection, real rejected Favorites requests,
known/unknown language badges, all seven seek options and unchanged wire values.
All 1,994 frozen source/test inputs match. Product DLL SHA-256:
C45E83BCDFBB08D200A0D2D7F2EECDBBD0761E7515EF58A313DBAB90DBF325E7.
Receipt: D:/SiloPlayer-build/review-twelfth-receipt.json. Source manifest:
D:/SiloPlayer-build/review-twelfth-source-inputs.json (95C08174FBE58516E9482BA0B7BC072D72A17D2BB73870FF3B20850CB2C9E9F8).

VM QA on 64c7edba again timed out in subtitle ownership after 76 checks; the
fixer made no changes. Test-only scenario-start and applied-preference phase
traces now identify session setup, native initialization, selection/save and
cleanup boundaries without changing assertions, deadlines or playback behavior.
The local ownership pass does not establish the VM cause or certify that run.

Initial native verification exposed recycled seek-template text and now covers
that correction. Fixture-only followups preserve English raw API detail, drain
Settings unload before service disposal and apply closed recency templates before
inspecting presentation. Original native failure result text remains archived.
The unit-tested product and unit-test sources did not change during those
native-fixture-only corrections.

Task output is 1.233 GiB; free C: 190.39 GiB, D: 481.27 GiB. The installed
player (PID 38020) stays open and the owner's AGENTS.md edit stays uncommitted.
Earlier automatic approval rejection still blocks delivery cleanup; no deletion
was retried and recovered space is 0 GiB on C: and D:. Pipeline QA, merge and
Beta publication remain pending, with earlier evidence limits preserved below.

# November 13 - PR #145 collection allowances and cache failure resilience

The four findings on 6f584a4a are addressed. Collection allowance headings use
web.profiles.allow.title with the raw name and retain the Done command through
open-dialog language changes. Collection menu retries resolve common.retry and
retain state-aware text/accessibility through the menu-item lifetime. A successful
retry restores the localized allowance action. Watch Tonight binds both close
accessibility and tooltip to web.ui.close.

The locale coordinator treats IOException/UnauthorizedAccessException from its
launch-cache write as best-effort persistence. It still applies the authoritative
server locale, updates the loaded owner/support state and emits Loaded. Other
failures are not swallowed. Six unit cases use real locked/denied writes for an
explicit language, Match device and an unsupported server, then verify restored
persistence after the blocker is removed. Native checks open actual collection
allowances and Watch Tonight, change en/de/ar/ja, fail/retry actual allowance
availability and preserve raw collection names/identifiers.

Release publish and native host build passed. All 6,227 unit tests passed with
zero failures/skips (2m24s). Seven native groups passed on the same payload:
settings-localization, media-localization, shell-localization, app-language,
app-language-catch-up, profile-menu-navigation and subtitle-sidecar-ownership.
All 1,993 frozen source/test inputs match. Product DLL SHA-256:
38BDC71277B53EE5FE28DA79868EB4C5BC60B92C76B0624D37DA4BA7F5F3E7E1.
Receipt: D:/SiloPlayer-build/review-eleventh-receipt.json. Source manifest:
D:/SiloPlayer-build/review-eleventh-source-inputs.json (A5FD9DDC2894A0A130F8629A41BB85CF0DB15E2ED68DB762932925B0359B3A02).
Task output: 1.224 GiB; measured free C: 190.48 GiB, D: 481.29 GiB.

VM QA on earlier head 6f584a4a failed profile-menu with COMException 0x8000FFFF
and no actionable stack, plus subtitle-sidecar timeout after 76 checks. The
fixer made no changes. The local passes above use this new source/payload and
do not establish the cause or certify the earlier VM runs.

The existing Watch Tonight source contract now requires the retained localized
close name/tooltip instead of a literal English accessibility string. Actual
translated values are independently checked in the native dialog.

A detailed unit run exposed the existing Library reconnect test asserting its
rail position after HTTP idle but before the view model applied the response.
It now joins the existing letter-index task and asserts no extra request, while
retaining the original position/grid assertions. The earlier navigation/write
ownership assertion passed isolated and in the detailed full run; its original
intermittent cause remains unestablished. Failure logs are retained.

The first full native attempt exposed a fixture lookup before the Watch Tonight
visual template had materialized. The fixture now waits for the actual keyed
close control before inspecting it; the original failing result text is retained.

The installed player remained open and the owner's AGENTS.md edit stays outside
the commit. The same build/publish/native payload directories are reused.
Earlier automatic approval rejection still blocks delivery cleanup; no deletion
was retried and recovered space is 0 GiB on C: and D:. Pipeline QA, merge and
Beta publication remain pending. Earlier VM timeout, live interoperability and
visual-parity limitations keep their original scope in the records below.

# November 13 - PR #145 retained dialog metadata and command labels

The five findings on 45f26756 are addressed. Match Item keeps dialog-owned
refresh callbacks for generated native/original/alias and score labels, clearing
callbacks when results retire. Candidate rows and selection stay in place.
Edit Person commands retain Save/Cancel keys and change the primary key for
pending save and failure recovery. Title allowances format the full
heading through web.profiles.allow.title with the raw name argument. Generated
profile hints, More options (including its accessible name), and upload-disabled
copy refresh through the dialog owner. Metadata AI controls refresh their
language header/placeholder, force label/accessibility and state-aware Translate
command without changing selected language, force choice or a running job.

Release publish and native host build passed. Targeted tenth-review checks and
full settings-localization, media-localization and shell-localization native
groups passed on the same payload. All 6,221 unit tests passed, zero failures or
skips (2m23s). Native tests keep generated candidate rows/selection, profile and
person drafts, mixed-script allowance arguments and AI language/force selections
through en/de/ar/ja. Actual pending/failed person writes and running/completed
translation jobs verify localized busy and recovery states. All 1,992 frozen
source/test inputs match. Product DLL SHA-256:
0DAC795F51D07D9A8444D54709BF976E420EC378949C2FFD0A0B003D4A0035C8.
Receipt: D:/SiloPlayer-build/review-tenth-receipt.json; source manifest:
D:/SiloPlayer-build/review-tenth-source-inputs.json (CA63BB1A10AD83E86E0395DEF0F00F6B34AEDE4C3977368446E7DACC74587B81).
Task output: 1.197 GiB; measured free C: 190.80 GiB, D: 481.32 GiB.

The installed player remained open. One reusable payload is retained under
D:/SiloPlayer-build. Earlier cleanup was rejected by automatic approval review;
no filesystem deletion was retried and recovered space remains 0 GiB on C: and
0 GiB on D:. Pipeline QA, merge and Beta installer/release delivery remain pending.
Earlier VM timeout and interoperability/visual-parity limits remain scoped to
the records below; these dialog checks do not establish their cause or outcome.

# November 13 - PR #145 episode context, status counts and metadata labels

The five findings on f1902cff are addressed. Continue Watching/Next Up uses the
label without a separator when the episode title is missing or whitespace.
Loaded Requests uses the shipped pluralized result-count message, including
culture-formatted numbers, instead of an English request(s) suffix. The generated
Match Item heading and both allowance-list retry buttons retain catalog keys.
A death date with no usable positive age uses the translated Death Date label
and culturally formatted date (or the raw unparseable date), without inventing
an age. Known positive ages retain the original catalog sentence. Generated
catalogs and raw request/person/media values remain unchanged.

Release publish and native host build passed. Targeted and full native
settings-localization, media-localization and shell-localization passed. New
checks exercise empty/whitespace/raw episode titles, real Requests loads with
0/1/2/5/11/23 rows, an open Match Item heading, both actual allowance-read failures
and retry actions, and missing/invalid/zero/known-age birth/death data through
en/de/ar/ja. All 6,221 unit tests passed with zero failures/skips (2m24s).
All 1,991 frozen source/test inputs match. App DLL SHA-256:
49F0A62EF71D911891CC0EE806D8891E9FFFC092538F9490781C56FDB2D3D5EB.
Evidence: D:/SiloPlayer-build/review-ninth-{receipt,source-inputs}.json,
review-ninth-{publish,native-build,units}.log and native result text.
Pipeline QA, merge and Beta publication remain pending. Version stamps stay
pipeline-owned; the installed player stays open. Earlier verification limits
and the cleanup approval blocker remain; shared output is 1.185 GiB, with
C: 190.00 GiB and D: 481.32 GiB free. No cleanup was retried and zero GiB
was reclaimed on either drive.

# November 13 - PR #145 picker recovery and retained confirmations

Addresses the seven findings on 56965f64. Initial language capabilities or
profile-value read failures keep the group visible with a translated error and
read-only retry; the picker stays disabled until the read succeeds. Unsupported
servers still hide the group. Request cancellation and download headings retain
raw title arguments. The party chooser, guest-pause label and Create Watch Party
action refresh without clearing selections; the create key accurately describes
the native flow, which does not copy an invite. Discord unlink, Home/library row
reset and Your Data PIN confirmations retain localized headings, bodies and
actions. Discord uses the existing translated disconnect heading and account-wide
DM explanation because the shipped catalog has no dedicated unlink description.
PIN drafts, raw names, request identifiers and server mutations remain intact.

Release publish and native host build passed. All 6,221 unit tests passed with
zero failures/skips (2m23s). Full settings-localization, media-localization and
shell-localization native groups passed. Added native checks open all six actual
dialogs and change de/ar/ja in place, verify party choices before opening their
dropdown and preserve PIN/party drafts and raw titles. Both initial language read
failures recover with zero writes. The first native attempt exposed a stale PIN
child description; owning child refresh from the dialog lifetime fixed it before
the passing targeted and full runs. Original failing result text is retained.
All 1,990 frozen source/test inputs match. App DLL SHA-256:
D95DA89728E63BDFB4154FD3BE4BDB9982F59341C7A695745C934398A48F28EA.

Evidence: D:/SiloPlayer-build/review-eighth-{receipt,source-inputs}.json,
review-eighth-{publish,native-build,units}.log and native result text.
The pipeline stopped its prior head after a native-subtitle-sidecar-ownership
timeout; its fixer made no changes because the diagnostics did not identify
the blocked operation. These verified review fixes are submitted on a new head.
Pipeline QA, merge and Beta publication remain pending.
Version stamps remain pipeline-owned. Installed player stays open. Shared output
is 1.157 GiB; C: 190.10 GiB free, D: 481.36 GiB free. No cleanup was retried;
zero GiB reclaimed on either drive. Prior cleanup approval rejection and all
previous live-server, visual and broader suite limitations remain applicable.

# November 13 - PR #145 retained rows, filters and password outcomes

Integrated review fix 1e7ccbc3 with pipeline attempt 6, a4d67a8d, through
merge 9fefb061. The pipeline playback correction retains outgoing direct-stream
relays until the replacement input reports FILE_LOADED, preventing obsolete
relay errors from affecting the accepted replacement plan. It is preserved.

All four new GitHub findings are addressed. Continue Watching/Listening/Reading
and Next Up dismissal menus and success notifications resolve their specific
catalog keys. Subtitle results refresh labels, localized language, download
counts/actions, score metadata and accessible names in place. Expanded release
variants, raw release names and disabled download controls remain intact.
Generated advanced-filter fields/operators resolve catalog text at creation
and refresh under event guards; raw field/operator values and text/boolean
drafts survive without rebuilding rule controls. Required-password failures
resolve validation or change/settlement messages from transition state, retain
their catalog key for later refresh, and offer a translated retry after a saved
password fails to finish sign-in. Raw exception messages are not shown for
non-API transition failures.

Verification on the integrated source: Release publish and native host build
passed; all 6,221 unit tests passed with zero failures/skips (2m23s). Four native
groups passed: relay-load-ownership, settings-localization, media-localization
and shell-localization. Actual checks cover retained subtitle results and
variant expansion, disabled download state, all advanced field/operator choices
before opening dropdowns, text/boolean filter drafts, retained dismissal menus,
invalid password inputs and a real successful password write followed by failed
sign-in refresh. Locale checks exercise de/ar/ja; raw names and identifiers stay
unchanged. The relay fixture retained plan B and pause intent with zero playback
errors or recovery attempts. All 1,989 frozen source/test inputs match.
App DLL SHA-256: 5FEA2E2C242AE1DF048EC8087EAF40CA2C0CE1D28CC1117B3853E8B801982620.

The initial media run exposed unopened generated choices starting in English,
including Year instead of Jahr; visible-sheet reproduction confirmed it.
Construction-time catalog resolution corrected that gap before the final pass.
Original failing result text is retained. Pre-integration verification passed
three localization groups and five targeted required-password transition tests;
its receipt keeps its own payload/source scope.

Evidence: D:/SiloPlayer-build/review-seventh-receipt.json,
review-seventh-integrated-source-inputs.json,
review-seventh-integrated-{publish,native-build,units}.log,
review-seventh-preintegration-receipt.json and native result text.
QA, merge and Beta publication remain pending; version stamps remain pipeline-owned.
The installed player remains open. Generated output is 1.138 GiB; free space is
C: 189.77 GiB and D: 481.37 GiB. Prior automatic approval review denied cleanup;
no deletion was retried and 0 GiB reclaimed on either drive. Earlier live-server,
visual and broad personal-lists/default-suite limitations remain.

# November 13 - PR #145 language recovery and subtitle review fixes

Follow-up to ae3a28c6 addresses all five new review findings. Player-mode
subtitle search now uses the catalog empty-state message; external-watchlist
load errors re-evaluate both the translated prefix and Problem Details text.
Subtitle color keys append the parenthesized switch result, restoring translated
tooltips and accessible names for all 14 font/background colors.

After a successful language save, a failed effective-setting read now shows a
translated load error with an owner-scoped refresh-only retry. Retry reads and
applies the saved locale without repeating the profile write or device deletion.
Pending device clears retain their existing recovery priority. Unloading the
cached picker invalidates its owner/load generation so returning reloads the
current profile value even after a same-profile update while detached.

Verification: both picker failures reproduced on the prior published task
payload before the product fix; red result text is preserved. Release publish,
native host build and the full settings-localization native group passed on the
new source. Checks include read-only refresh recovery, a detached same-owner
update, both actual subtitle-search modes returning no results, every color
swatch tooltip/accessibility name in the open dialog, and a retained external
watchlist network error with localized Problem Details through de/ar/ja.
All 1,989 frozen source/test inputs match. Product DLL SHA-256:
E640E861FF211266B2A0F64EB8197842E92AF09FA6B073FF3A3C9FFE7993F38C.
No unit-suite rerun for this UI-only follow-up; previous unit counts retain their
source scope and retry qualification. Initial fixture compilation errors were
corrected before the passing native build; they were not product regressions.

Evidence: D:/SiloPlayer-build/review-sixth-receipt.json,
review-sixth-source-inputs.json, review-sixth-publish.log,
review-sixth-native-build-final.log and native results (red text archived).
QA, merge and Beta delivery remain pending; version stamps remain pipeline-owned.
The installed player remains open. Generated output is 1.127 GiB; C: has
189.80 GiB free and D: 481.39 GiB. Prior automatic approval review denied cleanup;
no deletion was retried and 0 GiB reclaimed on either drive. Earlier live-server,
visual and broad personal-lists/default-suite limitations remain.

# November 13 - PR #145 history, invitation and missing-state review fixes

Follow-up to reviewed head d2047c1c. Webhook deletion uses the catalog title
with the raw name argument and refreshes title, body and command text while
open. Invitation pages have a localized document-title mapping. Completed
History cards use the catalog Play action; incomplete cards retain Resume.
The Play History dialog uses the translated View Play History heading and
refreshes while open. Request browse 404 and invalid navigation states use
the catalog Not found message. Unsupported external watchlists use the
translated server-capability message. Existing generated catalogs remain intact.

Verification: Release publish and native host build passed. Full Settings and
shell localization native groups passed, including actual completed/incomplete
history cards, a 404 browse session, unsupported external watchlist, open
play-history and webhook delete dialogs, and the invitation title map. Follow-up
cases switch through German, Arabic and Japanese where applicable, preserve raw
titles/names, and cancel the deletion. All 1,989 frozen source/test inputs match.
App DLL SHA-256: 1B0A4175848946A453BB9748D1BEE06433540F7E70A149E4DA39E6C673B00B2A.
The unit suite was not rerun for these UI text bindings; earlier unit results
below retain their source scope and initial failure qualification.

Evidence: D:/SiloPlayer-build/review-fifth-receipt.json,
review-fifth-source-inputs.json, review-fifth-publish-final.log,
review-fifth-native-build.log and native results (older result text retained).
The initial publish caught a missing helper import, corrected before the passing
publish. QA, merge and Beta publication remain pending. Version stamps remain
pipeline-owned. The installed player remains open. Generated output is 1.126 GiB;
C: has 189.69 GiB free and D: 481.39 GiB. The earlier automatic approval review
rejected cleanup; no deletion was retried and 0 GiB reclaimed on either drive.
Earlier default-suite, personal-lists, live-server and visual limitations remain.

# November 13 - PR #145 remaining dialog and reader review fixes

Candidate integrates local review commit `a7bd6f2c` with pipeline attempt 5, `5f8abf06`, through merge `76380c46`. The pipeline fix transfers localized toggle headers into retained Settings row labels and accessible names; its new full Settings native cases pass on the integrated payload.
The Add to Collection primary action is keyed. Split Versions fields carry
placeholder/accessibility keys. A native test reproduced placeholders and
accessible names remaining German after switching to Arabic despite the fields
being loaded: modal template relocation detached child subscriptions, and the
existing dialog-level fallback refreshed only text/content. That fallback now
also refreshes keyed TextBox placeholders/headers and accessible names without
changing editable values. The same draft-preserving test passes after the fix.
Settings resolves its generic subtitle on both detail navigation and live locale
changes. Reader cached-settings/server-sync warnings use the shared catalog;
local disk-save failure uses the existing generic translated save-failure text
and never claims settings were saved. Inactive audiobook interval buttons omit
redundant unselected help; active buttons retain translated Selected help.

Release publish and native host build succeeded. Three native groups pass on the
final payload: settings-localization (full group), reader-party-language and
playback-localization. New checks cover real open Add/Split dialogs across
de/ar/ja, raw split-title drafts, Settings detail subtitles, an actual forced
reader-config failure with a local cache, an obstructed local-save directory,
and audiobook interval accessibility text. Reader fixtures write only beneath
the task result directory. The installed player remains open.
App DLL SHA-256: `818A0300A957877D565947F1379F3B1A2B5E9A48F078F7A6A4C391B28DB16349`.
The first full unit run had 6,220 passes and one failure in unchanged
RealtimeReadOwnershipTests.CurrentViewerWritePublishesItsCapturedItemAndContext
(watchlist removal). All six cases passed in isolation without changing any
assertion. The full pre-integration retry passed 6,221/6,221 (2m25s), and the final integrated suite also passed 6,221/6,221 (2m24s), both with zero failures/skips; the initial failure
log is retained and its cause is not established.

Evidence: `D:/SiloPlayer-build/review-fourth-receipt.json`,
`review-fourth-integrated-source-inputs.json`, `review-fourth-integrated-{publish,native-build,units}.log`,
`review-fourth-units{,-isolated,-retry}.log` and native result text, including
archived Split-dialog red results. Earlier receipts retain their original
source/payload scope and limits. Pipeline review, QA, merge and Beta publication
remain required, and version stamps remain pipeline-owned. No installer was run.
Generated output remains in the reusable task directory. Cleanup is still blocked
by the earlier automatic approval review rejection; no deletion was retried and
0 GiB was reclaimed on C: or D: during this follow-up.

# November 12 ? PR #145 retained locale review follow-ups

Candidate based on pipeline head `787456b0822a697d86f226e392f663572661ded9`;
this preserves pipeline attempts 3 and 4. Nine outstanding GitHub review findings
are addressed together: region-only Settings changes refresh culture-sensitive
text; non-player subtitle empty results resolve their catalog key; person-editor
and Refresh Metadata labels refresh while open; Allowed Titles uses attached
keys and retained result-button bindings; Requests restores eight Auto rows;
Calendar resolves the active empty-state variant; global search retains its
request-discovery hint; Playing Next refreshes its finished description and scope.
No playback transition, query, draft, saved language tag or API identifier is
rewritten by these text refreshes.

Verification: Release publish and native host build succeeded. Three native
groups passed on the new payload: settings-localization (including the full
existing group), media-localization and shell-localization. Follow-up cases
exercise de/ar/ja on actual open dialogs, an actual empty subtitle-search response,
region-only de-DE to de-CH Settings refresh, Calendar variants, request-guide row
structure, and visible Playing Next text. Person-editor drafts are preserved.
The full unit suite passed 6,221/6,221, with zero failures or skips (2m23s).
App DLL SHA-256: `7CE95E6D21AAADEA097617EE6528421A527B9CD40066CA11063E227C0055C3D3`.
All 1,989 captured source/test inputs match. Native test setup corrections were
required because German ?Name? equals English and the status guide is retained
outside the page namescope; neither correction relaxed the product expectations.

Evidence: `D:/SiloPlayer-build/review-ui-receipt.json`,
`review-ui-source-inputs.json`, `review-ui-{publish,units,native-build}.log`
and the three native results. Earlier receipts below retain their original
source/payload scope and limitations. This remains a candidate awaiting pipeline
review, QA, merge and Beta publication. Version stamps remain pipeline-owned;
no installer was installed and the owner's running player remains open.
The personal-lists/default-suite and live/visual limitations below remain.
Generated output is reused in the existing directory. Cleanup is still blocked
by the earlier automatic approval review rejection; no deletion was retried,
and 0 GiB has been reclaimed on either drive during this follow-up.

# November 12 — PR #145 regional picker and dialog follow-up

Based on integrated review head `159f31654a7400f761e594b4d6efe25cf0fab2c6`.
Supported regional profile tags now select their shipped language option without
adding a duplicate or rewriting storage: de-DE→de, es-MX→es, pt-PT→pt-BR.
Unsupported zh-TW retains its separate option and warning. Activate Device uses
its bundled document-title key. The webhook editor resolves its title with the
raw name argument and refreshes it while open without discarding drafts.

Native testing also reproduced a real Settings → Notifications construction
failure: NotificationSettingsControl required SettingsGroupStyle from the
SettingsPage-only scope. The control now owns that style and border brush with
the same five setter values as SettingsPage. The normal Settings entry point
failed before the fix and passes after it; no test-only style is injected.
The carousel review finding identified redundant English assignments that were
immediately overwritten by ConfigureEpisodeCarouselText. Removed those dead
assignments; the actual numbered episode and runtime path was already localized
and is now covered through German, Arabic and Japanese refreshes with raw media
titles preserved.

Current verification: fresh Release publish, **6,220/6,220 units**, no failed or
skipped tests, and **three passing native groups**: settings-localization,
shell-localization and media-localization. All 2,008 frozen implementation/test
inputs match. Final app SHA-256:
`BC96921E802897F8A451B25BDB55A55CA6E6884347B0730FAC53B34A103B92CB`.
Evidence: `D:/SiloPlayer-build/review-third-receipt.json`,
`review-third-source-inputs.json`, `review-third-{publish,units,native-build}.log`
and native result text. Earlier seven-/23-group receipts below retain their
original source/payload scope, with superseded text results timestamp-archived.
The broader personal-lists overlay limitation and live/visual verification
limits below remain unchanged. This is still a candidate awaiting pipeline
review, QA, merge and Beta delivery; inherited version stamps remain pipeline
owned. The installed player stays open. Cleanup remains blocked by the earlier
automatic approval rejection; no deletion was retried and recovered space is
still 0 GiB on both C: and D:. Generated output is about 1.1 GiB.

# November 12 — PR #145 integrated review corrections

The candidate integrates pipeline `c8f5fb28b9652c220be521eba6031ccaa855c5e6`
with the local section-navigation and legacy-series corrections. All **35**
XAML-defined ComboBoxes with localized inline items now have explicit owner
refreshes under selection-event guards, including notification-series filters,
Catalog, Search, Settings, library, personal lists and Requests browse. No locale
refresh treats relabeling as a user option change.

Ordinary unloads retain localization generations, preserving cached pages.
Only changed keys on detached controls invalidate their registration. The new
unchanged-reload regression failed before this correction and passes after it;
the existing detached-key and open-dialog regressions still pass.

Current verification: successful Release publish; **6,220/6,220 units**, no
failures/skips; **seven passing native groups**: localized-properties,
media-localization, media-catalog-localization, oct20-home-rows,
settings-localization, calendar-culture and notification-series. App SHA-256:
`5D94A9FF697A2C178269C0AFF3570F325189C1576F573DD30EBB3F373B79633E`.
All 2,008 frozen implementation/verification inputs match. Receipts and logs:
`D:/SiloPlayer-build/combined-review-receipt.json`,
`combined-review-source-inputs.json`, `inline-choice-owner-audit.json`,
`combined-review-{units,publish,native-build}.log` and `results/`.

The broader personal-lists fixture did not pass: its standalone overlay setup
omits the capability revision required to offer request-status badges. A
separate diagnostic supplied revision 26, then exposed an inner-badge alignment
expectation; that experiment was restored without changing product geometry or
relaxing assertions. Original/diagnostic failures remain in results. Direct
Favorites/Watchlist dropdown checks pass within media-localization. This is not
a passing full personal-lists or default-native-suite claim. Earlier 23-group
results below remain historical baselines. Live interoperability and full visual
or physical DPI parity remain unverified.

Version stamps remain inherited **1.2.1015-beta.1 / 1.2.1014.1**; pipeline owns
the candidate's next release. The already published 1015 is the earlier source,
not this localization candidate. PR #145 still requires review, QA, merge and
Beta publication. The installed player (PID 38020) remains open.

Cleanup remains blocked by the prior automatic approval rejection. No deletion
was retried; recovered space remains **0 GiB C: / 0 GiB D:**. Reused generated
output is about **1.08 GiB**, with approximately **192 GiB free C:** and
**481 GiB free D:**. Source, receipts and owner AGENTS.md edits are preserved.

# November 12 — PR #145 subtitle appearance review correction

PR #145 remains the localization delivery candidate. Its review found that
`Localized.Header` did not support Slider, which prevented the subtitle
appearance dialog from opening during XAML construction. The attached property
now maps Slider to `Slider.HeaderProperty`. The native regression reproduced
`Localized.Header is not supported on Slider` before the fix, then passed with
German and detached/reloaded Japanese headers while preserving the slider value.
The subtitle-preferences fixture now registers the language and PlaybackApi
services required by the current Settings page.

The earlier 23-group verification below belongs to the pre-review candidate.
The correction has a new successful 6,219/6,219 unit run, with no failed/skipped
tests. Targeted native checks cover localized properties, subtitle preferences
(including the real dialog and contract revisions 18/19), and playback
localization. Final payload hashes and source binding are recorded separately
in `D:/SiloPlayer-build/slider-review-receipt.json`; earlier receipts are retained.

Beta **1.2.1015-beta.1** was published at **2026-11-13 01:18:24 UTC** from
`a82fc9e50ce0e17b4b81caa1c552497c74620ed9`. It includes #144, while #145
still requires pipeline review, merge and its own Beta release. Version stamps
remain pipeline owned. This record does not certify release delivery for #145.

Cleanup remains blocked by the previously reported automatic approval rejection;
no deletion was retried in this review correction. Recovered space remains
**0 GiB on C: and 0 GiB on D:**. One reusable build/publish payload is retained.
The installed player remains running. Existing live-interoperability and visual
verification limits below still apply.

# November 12 — Interface localization candidate on Beta 1015

The server-alignment branch integrates Beta `a82fc9e5` with the reviewed shell,
Settings, Collections, Reader/Watch Party, media and playback localization.
Source stamps are inherited **1.2.1015-beta.1 / 1.2.1014.1**; 1015 is not yet
a published release. Latest observed published tags are Stable **1.2.1007**
and Beta **1.2.1007-beta.1**. #143 and #144 are included dependencies.

Final product verification: **6,219/6,219 units**, no failures/skips; successful
Release publish; **23 native groups completed** on app SHA-256
`A10DB7A2769098DDC0572EEC6D46CBB477C7E59091DDBF671986AA2E514CB687`.
The Collections scope-label assertion failed in the combined batch and passed
an isolated rerun with unchanged assertions. Its cause remains unestablished;
this is retry-passing evidence, not an all-first-pass result. The new detached
message-key regression failed before the localization reload correction and
passes after it. The metadata translation row also uses stored layout ownership
instead of depending on WinUI assigning visual parents first.

All 2,009 frozen authored inputs were checked. Only extra failure diagnostics
in the Collections native fixture changed after the full run; no product input
changed. Evidence: `D:/SiloPlayer-build/verification-receipt.json`,
`verified-source-inputs.json`, `verified-units.log`, `detached-publish.log` and
`results/`. Engine remains pinned to `04E4D27D…`.

Official GitHub main was fetched directly at
`fb165fa931ea0f9e99b9a3a6f339af3322bc5524`; subsequent scanner/matching and
contributor-guidance changes add no Desktop viewer/API work. See
[scope, corrections and limits](research/2026-11-12-localization-delivery.md).
Synthetic native checks do not certify live accounts/room/phone interoperability
or exhaustive visual/DPI parity. The installed player remains open.

Cleanup is **blocked**: automatic approval review rejected both the validated
batch and the narrower deletion of `D:/SiloPlayer-build/artifacts`, without a
detailed reason. No cleanup command executed, so recovered space from this
delivery cleanup is **0 GiB on C: and 0 GiB on D:**. About 1.1 GiB of generated
task output remains, below the session's 4 GiB limit. Source, receipts and
screenshots remain intact. No installer or installed-player action ran.

---

# November 13 - Edition-scoped detail Auto verified locally

Branch codex/edition-auto-nov13 starts at Beta e438c30d. Official upstream
7edef3ddc4cdd5c12f26c23a895b41f31782f286 (#4053) is implemented through a detail-only
Auto policy, retaining card/next-up Resume history. Edition changes clear pins,
quality updates retain the edition, downloads follow the selected presentation,
and file changes retire pre-play track choices. See
[the parity record](parity/2026-11-13-edition-auto.md) for behavior and limits.

Release publish/native build passed, all 6,103 units passed (2.3822 minutes),
and actual oct24-track-choices/detail-action-hover native fixtures passed.
Product DLL SHA256: 8033D5599A402C9B7175344475DD1712B9E94BAE2415B5FC2E3E1612C9293AAD.
Independent review findings are corrected, including same-file subtitle inventory,
playback-return track retirement and owned settings-response behavior. Pipeline-owned
version remains 1.2.1016-beta.1 / 1.2.1015.1. PR #145 was untouched.

Task output is 1.226 GiB; free C: 194.76 GiB, D: 480.98 GiB. Cleanup was not
retried after automatic approval review blocked generated-test deletion;
recovered 0 GiB on each drive. The installed player remains untouched.

---
# November 13 - Unavailable download row reconciliation for Beta

New independent branch codex/download-delete-404-nov13 starts from Beta
02d007297c923f529f4ebf282429dd3a62301ec1. Delete 404 now retires the matching
local Downloads row and updates the empty state without claiming server deletion.
The new cases reproduced both last-row and multi-row failures before the fix;
all 67 related download tests then passed, zero failed/skipped. Native/live
verification was not run for this command-only change. See
[the reconciliation](parity/2026-11-13-server-alignment-reconciliation.md) for
exact upstream inputs, original finding dispositions and verification limits.

Official upstream rechecked at 0387303d96618a9b8bda44333a6c2e60de37a6fb;
the four commits since fb165fa9 need no further native-client implementation.
PRs #143/#144 are merged; submitted localization PR #145 remains pipeline-owned.
Six uncommitted localization review corrections and the owner's AGENTS.md edit
remain preserved in the original task worktree, outside this independent branch.
Version stays 1.2.1015-beta.1 / 1.2.1014.1; pipeline owns QA, fixes and packaging.

Task output is 1.238 GiB; measured free C: 189.43 GiB, D: 481.11 GiB.
Automatic approval review rejected removal of the disposable unit-test output
with "blocked by policy". No deletion ran: cleanup recovered 0 GiB on both
drives. Receipt: D:/SiloPlayer-build/download-delete-receipt.json.

---

# November 12 — Playback HUD final Beta stamp refresh

Verified source commit: `88a9041bd2ec020b5f3fc1e5ea57c8f27049eec7` on `codex/player-hud-server-nov12`, merged with Beta `1a341042bb2e64ee37f6a065726776af08acf285` (including #143 and the pipeline stamp), after preserving #138 transport controls and #141 updater staging. Source **1.2.1007-beta.1**, numeric **1.2.1006.1**, inherited unchanged. Final documentation/PR head is bound in `D:/SiloPlayer/.codex-tmp/server-align-nov12/task-2-beta-delivery/beta-1a341042/delivery-receipt.json`.

The native HUD now keeps the finishing video beside Playing Next, uses segmented chapter rails and a readable lamp accent, compact owned Speed controls, spoiler-aware credits stills, condition-specific wait labels, a 500ms loading-cover fade, screen-sized watch backdrops and a configurable unattended-autoplay gate across credits/EOF/shuffle. Beta continuing reload remains transparent and controllable with owned pause intent and native event guards; updater stages before Restart. Official runtime reference `0b579aa6` plus narrow `d9a6cbb9` #4033 marker palette update. Contract/hero changes match Beta and are outside this HUD diff. Localization foundation/screen work remains separate; message-key/template seams and English fallbacks remain.

Final verification: **6067/6067 serialized full units**, **477 focused tests**, **107 current source/API/runner guards**, fresh self-contained x64 Release, and **18 actual native groups including all 23 real transport-reload cases**, zero failures/skips in these final runs. **1901 tracked inputs** match; app/Core/Player/native/unit PE/PDB/source bindings have zero mismatches. App SHA256 `D7DC32AAEB36546C10712B55B8DC91CC8813181797578D3B3D3D832DD8864577`. Exact receipts, captures, source hashes and retained original failures: `D:/SiloPlayer/.codex-tmp/server-align-nov12/task-2-beta-delivery/beta-1a341042`; detailed report: `.codex-tmp/server-align-nov12/task-2-completion-report.md`.

Independent Task2 approval applies to c28705ac (no P0–P2); the refreshed delivery enters pipeline review. Nonblocking Speed text scaling, fade I/O/refade, reconnect cosmetics, still debug URL, post-roll/EOF and late-Next advisories remain in the report, along with historical full Audio/Chapters/Speed fixture limits. Scoped checks do not certify full parity, physical DPI, live accounts/server persistence or real room/phone interoperability. Versioning and packaging remain pipeline-owned; the installed player and production controls were untouched.

---

# November 12 — Playback HUD and unattended autoplay Beta submission

Verified source commit: `c9504d1fb30b19bed90c3c19270362da6c717711` on `codex/player-hud-server-nov12`, merged with Beta `f22764d957e7e07401a06a9dc60311d838942c5c` (#143), after preserving #138 transport controls and #141 updater staging. Source **1.2.1003-beta.1**, numeric **1.2.1002.1**, inherited unchanged. Final documentation/PR head is bound in `D:/SiloPlayer/.codex-tmp/server-align-nov12/task-2-beta-delivery/beta-f227/delivery-receipt.json`.

The native HUD now keeps the finishing video beside Playing Next, uses segmented chapter rails and a readable lamp accent, compact owned Speed controls, spoiler-aware credits stills, condition-specific wait labels, a 500ms loading-cover fade, screen-sized watch backdrops and a configurable unattended-autoplay gate across credits/EOF/shuffle. Beta continuing reload remains transparent and controllable with owned pause intent and native event guards; updater stages before Restart. Official runtime reference `0b579aa6` plus narrow `d9a6cbb9` #4033 marker palette update. Contract/hero changes match Beta and are outside this HUD diff. Localization foundation/screen work remains separate; message-key/template seams and English fallbacks remain.

Final verification: **6067/6067 serialized full units**, **477 focused tests**, **107 current source/API/runner guards**, fresh self-contained x64 Release, and **18 actual native groups including all 23 real transport-reload cases**, zero failures/skips in these final runs. **1901 tracked inputs** match; app/Core/Player/native/unit PE/PDB/source bindings have zero mismatches. App SHA256 `2156C99C67B46059ABD491950B0E121174B251709DB54A47EC190AE584C7E39E`. Exact receipts, captures, source hashes and retained original failures: `D:/SiloPlayer/.codex-tmp/server-align-nov12/task-2-beta-delivery/beta-f227`; detailed report: `.codex-tmp/server-align-nov12/task-2-completion-report.md`.

Independent Task2 approval applies to c28705ac (no P0–P2); the refreshed delivery enters pipeline review. Nonblocking Speed text scaling, fade I/O/refade, reconnect cosmetics, still debug URL, post-roll/EOF and late-Next advisories remain in the report, along with historical full Audio/Chapters/Speed fixture limits. Scoped checks do not certify full parity, physical DPI, live accounts/server persistence or real room/phone interoperability. Versioning and packaging remain pipeline-owned; the installed player and production controls were untouched.

---

# November 12 — Beta 1007 and localization integration in progress

Published Beta **1.2.1007-beta.1** (`1a341042bb2e64ee37f6a065726776af08acf285`) includes household contracts and Featured hero PR **#143**, merged as `f22764d957e7e07401a06a9dc60311d838942c5c` and published November 12 at 17:00:32 UTC. The pipeline's final PR head was `420a43cc568e66babb912b96f320074fa276fcdf`. Its corrections preserve committed webhook/reset results through transport-only catch-up and keep reset confirmation usable while capability reloads. The earlier updater fix (#141) remains included. This publication was observed through authenticated GitHub PR/ref/release reads; no local installer or installed-player action ran.

The working integration merges that exact Beta into local head `5feed4841dd50cf120a49885ada57a87edd67fec` and applies shell candidate `874eeb805f5d17ffe6960442a6044a9059038eb2` plus lead review corrections. The merge is resolved and deliberately uncommitted pending final combined verification. Pipeline-owned source version is **1.2.1007-beta.1**, numeric **1.2.1006.1**. The staged updater and continuing playback controls are preserved. Remaining media, settings, collections, reader and playback localization work is still being integrated; retained Favorites/Watchlist/History titles remain an explicit open integration item.

Historical checkpoint `5feed484` passed **6,149/6,149 units**, a fresh Release publish, five transport/HUD native groups and the actual post-roll case. All 1,564 recorded inputs matched after the T3 restart. Those receipts remain frozen under `D:/SiloPlayer/.codex-tmp/server-align-nov12/task-2-parent-integration/`; they do **not** verify the newer working source. Six remaining HUD completion cases will run on the final combined build instead of another intermediate build. The cancelled recovery was stopped only between cases, with no native child interrupted.

Shell review regressions reproduced typed-problem summary loss, uppercase-label loss and regional-culture casing. Lead correction `128f7ab12586e0a80ced7c8507d3c03e54750655` on shell candidate `874eeb80` passes **12/12 focused units**, a fresh Release publish and both `shell-localization` and `localized-properties` native groups. All 1,564 frozen inputs match, with published app SHA-256 `E6E126B81682AF58264AEFC2F4EFFCF992EDAAFFFE4958E52D7B103CB6D4EE65`. Evidence is `D:/SiloPlayer/.codex-tmp/server-align-nov12/task-5-shell/lead-review-r2/`; original failures remain in r1/r2. Header text now follows interface-language casing and refreshes from the retained shell root. Those receipts verify the isolated shell correction, not this combined integration. The password-session native fixture adds only its two required localization-service registrations; its original missing-service failures remain preserved, and its final native pass is still required.

Settings `6b0df091`, Collections `ff578688` and Reader/Watch Party `60ff09fb` candidates have been applied to this working integration. Their independent source reviews are pending. Test selectors are composed so each original group remains reachable. The owner clarified that server-update passes cover all customer-facing WebUI/API additions, changes and removals while preserving Desktop soft navigation, retained state and native improvements. The remaining parity work is active under that scope. No complete localization, visual parity, live interoperability or final delivery claim is made by this checkpoint.

# November 12 — Current Beta integrated with server-alignment foundation

Integrated Beta `47d5da976cc23797272b779b5519fb20315c4f92` into the local server-alignment branch after `5a056300`, preserving the staged updater from PR #141 and continuing transport controls from PR #138 alongside the reviewed household/hero/localization foundation and original HUD work. Pipeline-owned source version remains **1.2.1003-beta.1** (numeric **1.2.1002.1**). This is a local integration checkpoint; the remaining localization rollout and HUD completion are still separate work.

Verification on the combined product source: **6,135/6,135 full unit tests**, a fresh self-contained Release publish, the native `transport-reload-controls` group, all **23** `transport-reload-actual` scenarios, and the `hud-nov12` group including the real Settings threshold UI and production libmpv/Lua loading/transport pixels. All completed with exit 0 and pass sentinels. Final manifest rechecked **1,557** source inputs with zero mismatches. Published app SHA-256: `4813C49647532FA80D6D2BC8097FBF77E746B522480A4782B96E621117E137EE`; pinned engine SHA-256: `04E4D27DC385DC18DDD93FBE69C75E635B864F3C6EC53A9122DF54C2F7842F4A`.

Evidence: `D:/SiloPlayer/.codex-tmp/server-align-nov12/beta-refresh/final-native-r1/final-manifest.json` links the full-unit, Release, controls, 23-case actual transport and HUD logs. Original failures remain in the earlier `beta-refresh` evidence directories. The actual-recovery fixture now releases its real HTTP source as 503 instead of double-reporting a failure through private injection and native disposal; its pause/intent assertions are retained. The HUD Settings fixture adds only the two localization service registrations needed by the real SettingsPage. These two native-fixture-only corrections were checked on the same published app and do not change the verified product/unit inputs. The prior spinner assertion was narrowed to a fullscreen black rectangle and actual native video/disc/ring pixels, retaining the intended rebuffer disc.

Focused delivery **PR #143** (`codex/server-contracts-hero-nov12`, head `b90c3f69d136b1b596a372498277f8a0c0364365`) is open/non-draft to Beta for the reviewed household contracts and Featured hero. Its separate delivery record has 5,992 units and 15 native groups. A live read confirmed both GitHub automated reviews complete and no code-review findings; pipeline acknowledgement/merge/release was not observed. That PR excludes unfinished localization and HUD completion.

Official server recheck: newest successful Docker build **2212**, `d9a6cbb9f53cb7a0b444d70a12f526fba70c9254`, completed November 12, 2026 at 15:14:02 UTC. The only change beyond the previously checked `5327ba36` is WebUI seek-bar marker-region colors (#4033); no API drift. The runtime/API audit remains pinned to `0b579aa6`. Exact marker palette alignment is with the focused HUD delivery. Receipts for builds 2211 and 2212 are preserved under `.codex-tmp/upstream-review-2026-11-12/`.

Compiler/analyzer warnings and known historical fixture limitations remain recorded. This checkpoint does not certify live accounts/server playback, SiloCast phone or Watch Party interoperability, OS text scaling/high DPI, complete visual parity, installers, or installed-app behavior. The installed player and real settings remain untouched.
# November 12 — Focused household contracts and Featured hero Beta submission

Selected Beta base `47d5da976cc23797272b779b5519fb20315c4f92`; verified product-source snapshot `1fd6db5e30cc93fcd4acee29e683db389b3e90dc` on `codex/server-contracts-hero-nov12`. Integrated commits `bd45bebc7a1f5c904cede5d85058328c568ddce6`, `e50337d0abe7c335fcbe90d29ed327723abc47b2`, and `1fd6db5e30cc93fcd4acee29e683db389b3e90dc` reproduce reviewed workers `7caf93e7`, `47b4f6f8`, and `d7369cee`. The 17-file source/test diff matches reviewed worker blobs except for the two native Program selector integrations. Existing product `1.2.1003-beta.1` and file/assembly `1.2.1002.1` stamps are unchanged. The documentation commit adds this receipt without changing tested product source. Official reference remains the supplied, unmodified `0b579aa6192d49d7e8e0921dde5e955ac29ce302`.

Scope: household-manager gating across Webhook Sync entry points, captured request ownership and secret retirement; capability-bound atomic metadata reset with accurate target-lock/descendant semantics and labelled legacy unlock/quick-refresh; idle reconnect draft preservation, curator reset and item-specific copy; content-sized Featured heroes with Home/Library minimums, wrapping actions, retained navigation/counter and obsolete eyebrow removal. This submission excludes unfinished localization and playback/HUD work. Independent Task1 re-review and Task3 review approved with no P0–P2 findings.

Fresh verification on that product-source snapshot: **5,992/5,992 full unit tests**, zero failures/skips; fresh self-contained win-x64 Release publish; **15/15 actual native groups**, each exit 0 plus completion sentinel: all 11 household-contract groups (including four review follow-ups), profiles-metadata, current-caption-metadata in metadata mode, oct24-hero (22 Home/Library sizing cases plus four existing overview cases), and home-row-window. A generated hero screenshot was inspected. All dotnet/publish/native runs held `SiloOct18Build` and verified the bundled engine; native launches additionally verified the published engine and identical host-side product DLL. Compiler/analyzer warnings remain in receipts: nullable, unreachable-code, MVVMTK and xUnit warnings; native build reports 37 warnings and zero errors. No product verification run failed. A temporary PowerShell harness-edit parse error occurred before any write/invocation, was corrected, and is recorded in `harness-preparation-note.txt`. No repeated full-suite run was needed because product/test source remained unchanged.

Evidence root: `D:/SiloPlayer/.codex-tmp/server-align-nov12/first-beta-delivery/` (full unit TRX/log, publish/native logs and screenshots, source/artifact manifests, source receipt and copies of independent reviews). Product DLL SHA256 `CA558E95ABA20C845C2CBB27694EF646FED1036B351F1333D86D31EFCC67B059`; the native host copy is identical. `engine.json` SHA256 `DAEC33798AAB83FDA80112ADEDFEB8C95BACAFF4806C78AAE3719D13FA6E2E76`; source and published libmpv SHA256 `04E4D27DC385DC18DDD93FBE69C75E635B864F3C6EC53A9122DF54C2F7842F4A`. Source hashes were checked against the tested snapshot. No new complete PDB document-binding audit is claimed.

Review follow-up after that snapshot (its source, tests, hashes and receipts above predate this change; the Beta pipeline owns its verification): a committed household or reset write that crosses a transport catch-up is now published while identity and access authority are unchanged, a legacy reset finishes its refresh after the committed unlock, Webhook Sync reads retired by catch-up are reloaded, and the Reset confirmation is re-enabled after its request.

Retained review limits: a household read cancelled without a catch-up (for example a timeout) can still leave a loading state; switch tabs or reopen to reload. Failed capability reload requires reopening; atomic-path 422 wording may misidentify unrelated validation failures. Idle reconnect preservation is tested separately and does not certify those in-flight cases. Other original P3 advisories remain in the preserved review. Hero narrow Library/counter clearance intentionally adds height relative to the web; text enlargement is synthetic, not physical DPI/OS text-scale certification. The original broader collection route passed hero effects/layout before an unrelated `CardMenuSharesTheActionsAsync` menu failure, also reproduced on its untouched baseline; this submission does not claim that whole collection route passes. Existing red and diagnostic failures remain in the original Task1/Task3 evidence directories. No live provider persistence, real PIN/Plex/account testing, completed server-job or complete pixel-parity certification is implied. The installed player and real settings remain untouched. The Beta pipeline owns exact-head reviews, QA, merge, stamps and packaging.

# November 12 — Beta 981 updater focus and completed-installation readiness

Reconciles Beta `f3a463074023bf636037a6f4fb01fc1b4d7f8218` with pending PR #141 at `97528979a4d04e48f9873d0f7a14929d93b2bc2e`, including its pipeline fixes, where source was **1.2.981-beta.1** (Windows file version **1.2.980.1**) when this entry was written; since merged with newer Beta. No stamps changed. Beta 981 did not contain the still-open PR. The sole merge conflict was this document; both sets of historical records are retained below.

Source version: `1.2.1660`; numeric version: `1.2.1660.0`.

These are the existing stamps after that merge; this change alters none. The verification below was recorded against the earlier base and does not verify the merged source; no builds or tests were run for the merge.

A real two-process native test reproduces Silo briefly covering its update helper on 12/12 focus returns, followed by the old event/timer repair. Native ownership in the pending fix keeps the helper above Silo immediately on 12/12 returns; ordinary other applications can cover it and it is not globally topmost. The previously passing settled-only focus test missed this transient gap. The probe checks HWND order, not sub-frame DWM compositing.

Update now keeps the app open, presents the small panel before dismissing the large dialog, and downloads/stages/verifies/authorizes the installation before offering Restart now or Later. Restart only saves/closes/activates/relaunches, without copying installation files again. Later retains the completed staging and persisted next-start record, then closes the helper. Its successful save/read-back now also shows the next-start confirmation in Silo, where it remains after the helper closes; failure never shows that confirmation. The pipeline fixes releasing the install mutex while awaiting choice, revalidating under the activation lock, preserving abandonment as deferred, and handling staged journals are retained.

Fresh verification: **5,816/5,816 full unit tests**, **38/38 side-by-side installation tests**, **52/52 update protocol tests**, self-contained Release app/helper, and **13/13 native cases** with exit 0 and the completion sentinel. Cases include immediate focus return, Later confirmation, focus/minimize/helper-owner lifetime, first-render-before-dialog-close, preparation Cancel/transfer, five save/drain outcomes, and explicit full-installer consent. App/helper/native/main-unit/side-by-side/protocol PE/PDB/source bindings have zero mismatches; all **1,856** recorded product/test inputs match current bytes. Independent Claude review found no actionable bugs. All builds/tests were serialized through `SiloOct18Build`.

App DLL SHA256 `F77623EFF7E90940F920AFB677E5B4DC61BEC5821A18E8E90DBB0D10649B89EE`; native fixture SHA256 `D6736EFBCFDA5224EA6A4B81975D46BA5404A9BBD7BFC1D1D3531B07FFB431D1`. Evidence, preserved red cases and review: `D:/SiloPlayer/.codex-tmp/updater-focus-nov12/`. The existing isolated checkout was reused; no additional worktree. No real UAC, registered-installation update, installed-player closure/restart, live account/settings access or physical-input certification. Synthetic filesystem transactions and isolated native windows establish the stated coverage. Beta pipeline owns subsequent QA, integration and packaging.

# Continuing playback transport controls — November 11, 2026

Official Server/WebUI main rechecked directly at `4a9e527a432e8ef3a15dcd568802a45c15169039`. Reconciled with fetched Beta `97cc284a53f94adc7d1f675a6294fda828c41640`, where source was **1.2.970** when this entry was written; since merged with newer Beta. Stamps and packaging remain owned by the Beta pipeline.

Source version: `1.2.1660`; numeric version: `1.2.1660.0`.

These are the existing stamps after that merge; this change alters none. The verification below was recorded against the earlier base and does not verify the merged source; no builds or tests were run for the merge.

Continuing same-session seeks/replans use the transparent OSC spinner and keep native menus reachable. Initial and replacement sessions retain an opaque cover until their own first output. Framing is bound to the accepted session/authority/playback lifetime; a submitted load must have its own FILE_LOADED before output can frame it. Latest explicit pause/resume intent is nullable and busy-owner scoped, and is retired on failure/replacement. A framed load that fails before FILE_LOADED hands the pause state it would have restored to recovery, which shows, toggles and restores that state instead of the load freeze (not built or run when written). Audio presentations retire when the accepted track changes; Audio/Quality/Chapters/Party presentations retire on the busy/idle transition so stale disabled choices cannot survive completion. Audio rows opened during a continuing reload are disabled with the same in-progress reason instead of dropping the selection. Action guards still require transport/room authority.

Lead regression evidence at `D:/SiloPlayer/.codex-tmp/server-review-nov10/playback-lead-corrections/`: red-all-a reproduced ten expected failures against the previous AA2A7150 app; green-all-a passed all20 actual-path scenarios with exit0 and the literal completion sentinel. Final integrated source `bbc67a08` passed 5,360/5,360 full unit tests, the actual transport route and existing native Chapters/Party/Quality routes (each exit 0 with the completion sentinel). Fresh Release app SHA256 `F49DF938DC474271A725259AB1AFB35A7FCE5EE133AE38AE9069D1D15E954019`; final app/native/unit/Core/Player PDB identity and authored-source checks have zero mismatches. `beta-binding-c` selects dependencies alongside the executed test assembly; the earlier ambiguous-copy binding failure remains preserved. Independent review approved with no P0-P2 findings. Earlier worker claims/receipts retain their original source boundaries and are not relabeled final.

Synthetic HTTP/media/native fixtures do not certify a live room/server or full visual parity. Installed player, real settings/accounts and installer remain untouched. Verified for submission through the existing Beta pipeline; pipeline owns review, packaging and version stamps.

---

# November 11 — Next Episode above the timeline

Based on Beta `16f43b3a24a0372e2b85bf1c059ea3462b14690a`, where source was **1.2.973-beta.1** when this entry was written; since merged with newer Beta. Version stamps remain pipeline-owned.

Source version: `1.2.1660`; numeric version: `1.2.1660.0`.

These are the existing stamps after that merge; this change alters none. The verification below was recorded against the earlier base and does not verify the merged source; no builds or tests were run for the merge.

The end-of-episode floating Next Episode pill now anchors 12 scaled pixels above the entire timeline seek target. Rendering and pointer hit testing use the same freshly computed layout, including after a resize. Its right-side placement, late-episode visibility threshold and advance action are preserved.

The credits countdown card uses the same anchor, so the card and its Play Now and Cancel buttons no longer cover the seek target. This follow-up and its runtime regression were added after the verification below and are not covered by it.

Verification: **376 OSC tests passed**, zero failures/skips, including real bundled-libmpv execution of the production Lua, rendered ASS and pointer routing across 1920×1080, 960×550, 640×360, 3840×2160, 1280×720 and a return to 1920×1080. The new regression failed before the fix when a timeline gesture did not enter seek handling; final checks require a gap above the complete seek target and successful timeline/button-bottom-edge clicks. Receipts and the preserved failed run are in `D:/SiloPlayer/.codex-tmp/next-episode-nov11/`. All checks hold `SiloOct18Build` and verify the engine pin.

This Lua-only layout fix does not claim a full unit/native suite, physical pointer/display certification, new app publish or installer run. The installed player and real settings/accounts remain untouched. The Beta pipeline owns integration, packaging and publication.

---

# Personal profile data transfer — November 11, 2026

Official Server/WebUI main rechecked at `4a9e527a432e8ef3a15dcd568802a45c15169039`. Reconciled with fresh Desktop Beta `97cc284a53f94adc7d1f675a6294fda828c41640`, where source was **1.2.970** when this entry was written; since merged with newer Beta. Stamps and packaging remain owned by the Beta pipeline.

Source version: `1.2.1660`; numeric version: `1.2.1660.0`.

These are the existing stamps after that merge; this change alters none. The verification below was recorded against the earlier base and does not verify the merged source; no builds or tests were run for the merge.

Settings now offers Your Data export/import through the capability-gated v2 profile endpoints. PIN verification remains bound to the initiating authority, proof adoption checks and returns the full request context atomically, and navigation/authority retirement dismisses pending dialogs and drops late completions; an import the server already committed still refreshes caches for the unchanged profile and is reported as finished without its summary (added after the verification below; not covered by those receipts). Imports enforce the size limit before and during file reading, invalidate dependent caches, and reload imported customization before publishing Changed. Export preserves exact server bytes, avoiding Unicode expansion beyond the reimport limit.

Final integrated product source `035036d9`: 50 focused transfer tests and 5,409/5,409 full units passed; fresh self-contained win-x64 Release publish; all 15 actual Your Data native scenarios passed with exit 0 and the completion sentinel. App SHA256 `86AEB0F75713F64D94B7060D9100DBE2CFBD6E68C5787F4A81C255DADFD74FA8`. Final app/native/unit/Core/Player PE/PDB identity and authored-source checks: 743/182/423/362/15 documents, zero mismatches. Evidence: `D:/SiloPlayer/.codex-tmp/server-review-nov10/profile-transfer-lead-corrections/`.

The independent final review found two P2s; the lead reproduced both before fixing them. `lead-red-proof` rejects the old same-profile proof behavior; `lead-red-native` detects stale imported customization. Final tests verify owner retirement before adoption and after adoption, and native cache-refresh requires automatic reload without a fixture EnsureLoaded call. Earlier passing fixtures and worker full-suite claims retain their original limits; final receipts replace them for submission. No live-account/server persistence, real file-picker interaction or complete visual-parity certification is claimed. Installed player and real settings/accounts remain untouched.

---

# November 11 — Fit OSC popups to content and remove native frames

Based on Beta `94687096195771dbfa62d0b82d3d3004a09cf1a0`, where source was **1.2.973-beta.1** (Windows file version **1.2.972.1**) when this entry was written; since merged with newer Beta. Version stamps remain pipeline-owned. This deliberately supersedes the older context below.

Source version: `1.2.1660`; numeric version: `1.2.1660.0`.

These are the existing stamps after that merge; this change alters none. The verification below was recorded against the earlier base and does not verify the merged source; no builds or tests were run for the merge.

The shared native OSC popup now measures its real wrapped rows and visible footer instead of reserving 102 pixels per choice. Quality's collapsed error footer no longer selects the taller Subtitle cap. Loaded typography and live list-size changes trigger coalesced placement; the existing playback/work-area clamp remains in force. Residual classic window-frame styles and the DWM outline are removed, leaving the app's single rounded outline. A two-choice popup shrank from 256 to 163 pixels, and the unwanted 3-pixel frame is absent.

Verification: fresh Release app/helper, **99/99 focused unit tests**, and five native groups passed with exit 0 and the completion marker: eight-case popup layout/frame matrix, actual OSC Quality, actual Subtitle selection/footer, live menu growth, and resize/wheel selection. Matrix covers 1/2/7/24 choices, 460×300 playback, 1.25× text, and a partially offscreen owner. App/helper/native/unit PE/PDB/source bindings have zero mismatches. Independent Claude review found no actionable issues in the product and popup fixture. A later one-line Speed fixture correction reads the current string-ID wire contract; the lead verified it.

Verification limit: the extra Speed regression stopped first on its outdated numeric-ID fixture; after that test-only correction, its same-owner version/pause assertion fails identically against both untouched Beta and the corrected app. Neither run is counted as passing. The full unit suite was not rerun for this scoped UI change. No installed-player, live account/settings, physical input or system DPI changes occurred; 1.25× is text scaling, not simulated monitor DPI.

Receipts, original failures, baseline comparison, screenshots and review: `D:/SiloPlayer/.codex-tmp/playback-popup-nov11/`. App DLL SHA256 `3D77C5B39E32DF2986C9FE43EF0782DA78BEA53B37BFDFD2F09B900C40C59626`. The existing isolated checkout was reused; no new worktree was created. Beta pipeline owns integration and packaging.

---

# November 11 — Align viewer actions and smart collection behavior with Server main (Beta candidate)

Based on freshly fetched Beta `370ada819d2675a9e98f76a6aff7fe3994b67407`; official Server/WebUI main reviewed through `4a9e527a432e8ef3a15dcd568802a45c15169039`, following prior scoped reference `bb1ee4352b1bec38b4a6441ba81e61a92da81d54`. The Beta pipeline owns integration, versioning and publication.

Source version: `1.2.1660`; numeric version: `1.2.1660.0`.

These are the existing app, updater, launcher and installer-default stamps; this change alters no version stamps. Version statements in the older entries below, including `1.2.970`, are historical: each records the source at the time that entry was written, not the current stamps.

Dedicated Continue Watching/Next Up pages remove eligible cards and update counts. Complete-rule validation gates smart editor/wizard previews and saves; Trending has a Show selector. Calendar labels/palettes, library sidebar icon aliases, failed-sync status refresh with draft preservation and episode library scope match current upstream. Dismissal retires any pending continuation and re-seeks the shortened live row while preserving the already-loaded prefix.

Verification: Final full unit suite 5,352/5,352; fresh Release publish; expanded viewer-actions native route passes every scoped scenario. Both paging response orderings retain all 129 remaining titles exactly once and the correct count. The race-after native case reproduces a missing title/duplicate against the frozen earlier candidate.

Independent Codex review confirmed the dismissal/paging race. The fix cancels and retires the old load generation, clears its snapshot and resumes at the corrected prefix offset; cancellation-ignoring held responses cannot append. The initial test-edit command failed before saving, but the corrected source was saved before the queued red fixture compiled; its result records the expected race-after defect. Earlier worker source/test-manifest mismatch is superseded by final PDB binding and source manifests.

App DLL SHA256: `FA7601A51FB4EAEC8B361154DCDF107ADA3D0751265797D094A45B03CD9A6A84`; all 741 authored app PDB documents match final source. Selected native DLL/PDB identities and source documents also match. Evidence: `D:/SiloPlayer/.codex-tmp/server-review-nov10/viewer-actions/review-final-binding`, the original `viewer-actions-implementation.md`/review, and `viewer-actions-review-fixes-note.md`.

Later QA fix, not built or run in that pass (pipeline verification pending; the suite count, DLL hash and PDB binding above predate it): the smart-collection wizard now offers a stored sort field that its own list does not name, so a metadata-only save resends the stored sort; the viewer-actions `smart-metadata` route gains that case.

Limits: Existing sidebar-publication IsPaneOpen failure reproduces on baseline and candidate; four icon assertions before that failure passed, so no completed sidebar route is claimed. Existing skipped-sync-count retention remains a nonblocking pre-existing gap. Dedicated page has no Home-style Undo. SVG visual parity and live-server interoperability are unverified. Installed player remains running and untouched.

---

# Collection viewport loading and hero effects - final Beta follow-up

Official Server/WebUI pin4a9e527a432e8ef3a15dcd568802a45c15169039. Includes current Beta97cc284a, collection foundation PR128 atf7df8846 and viewer foundation PR131 at98fa4dc8. Historical: source was 1.2.970 when this entry was written (current stamps are in the newest entry above); pipeline owns stamps and release. Adds measured near-viewport/short-wall sequential loading with explicit keyboard action and stale-owner/error guards, plus motion-aware collection hero pan, text-mask shadow and native backdrop glass/fallback.

Integrated full suite5,543/5,543 passes. Fresh Release and six native groups (presentation, server, layout, ownership, owner-menu, creation) exit0 with exact completion sentinels; five acceptance cases reuse the same compiled fixture to avoid duplicate outputs. Final app754/native184 authored PDB documents match with zero mismatches. App DLL SHA2562F36832CC81A05C86EE5C70A53706B8C244975FFEC24A6096CDF10438FBC3802. Independent feature and successor source reviews find no P0-P2 issue. Evidence: D:/SiloPlayer/.codex-tmp/server-review-nov10/reopened-collections/lead-final-{full,publish,native-c,binding-c} and lead-final-replay-c-*.

The two intermediate native failures remain failed: the group fixture assumed five capped cards although automatic loading correctly added the sixth; it now asserts the exact complete unique set. A fake revision GET invented a name different from its list row, correctly tripping the new ETag guard; it now returns the matching fixture collection. Product behavior and conflict protection were preserved. Native compositor effects approximate CSS; synthetic tests do not certify live interoperability, physical input or full pixel parity. No installed-player/settings/account, installer or version changes.

---

# November 11 ? Preserve playback intent across policy and subtitle updates (Beta candidate)

Based on freshly fetched Beta `370ada819d2675a9e98f76a6aff7fe3994b67407`; official Server/WebUI main reviewed through `4a9e527a432e8ef3a15dcd568802a45c15169039`, following prior scoped reference `bb1ee4352b1bec38b4a6441ba81e61a92da81d54`. Historical: source was **1.2.970** when this entry was written (current stamps are in the newest entry above); the Beta pipeline owns integration, versioning and publication.

Watch Party creation reads the remembered guest-control policy and writes it only for an explicit change. Session-scoped subtitle announcements preserve canonical stored identity without selecting additions, falling back to an authoritative replan on ordinal collisions. Expired Skip Intro offers return with controls without restarting the timer; Escape closes an open menu before dismissing the timed prompt.

Verification: 635/635 focused tests; fresh Release publish; four final native routes: remembered party policy, canonical subtitle-ready, unrestricted subtitle adoption and intro reoffer. All include the completion sentinel. Two stored-identity regression cases and three corrected Lua menu-priority cases fail against the respective earlier implementations and pass after the fixes.

Independent Claude review identified the positional TrackId collision and Escape precedence; both were resolved. The first Lua regression fixture interpolated braces into state keys; corrected fixtures were rerun RED against frozen pre-review Lua, then GREEN. An inherited subtitle-only environment flag narrowed review-native-adoption; review-native-adoption-unrestricted explicitly clears it and runs the original adoption scope. Earlier failures and receipts remain preserved.

App DLL SHA256: `5EA2E5BA5A9A9A94E96B4FFF860F6C81E572C2FEC207106A6B5DDAD67ED9092B`; all 738 authored app PDB documents match final source. Selected native DLL/PDB identities and source documents also match. Evidence: `D:/SiloPlayer/.codex-tmp/server-review-nov10/playback-worker-2239/review-final-binding-b`, the original `playback-implementation.md`/review, and `playback-review-fixes-note.md`.

Limits: Synthetic API, real libmpv/Lua and native controls verified. No live server/room, subtitle-provider persistence, physical input or complete visual parity certified. No full unit/native suite or installer run claimed. Installed player remains running and untouched.

---

## November 11 — version-aware batch download candidate

Native series, season and Specials download actions now support official batch-version templates and optional bulk quality. Aggregate items need no Versions list: templates come from readable episode versions, with automatic per-episode selection available. Cursor creation retains one batch id and owner; closing the picker cancels future pages, duplicate activation is single flight, and effective capability refusals remain authoritative. A partial failure directs the viewer to Downloads for reconciliation.

Source is Beta `3f098c88ca4770db6f71e84e0e2290df42f8ec2c`, version 1.2.970, compared with official Server `4a9e527a432e8ef3a15dcd568802a45c15169039`. Verification: 41 focused and 5,327 full unit tests; fresh Release publish; six passing native groups for series/season/Specials selection, cancellation, duplicate activation and effective/legacy capabilities. App DLL SHA256 `B0727098C6FCBCED87D281E02677F017B1CC53E734139FA023C410861307492E`; 739 app and 181 fixture authored PDB documents match source. Independent review accepts with no P0–P2 findings; pathological template discovery may require many reads.

Evidence: `D:/SiloPlayer/.codex-tmp/server-review-nov10/reopened-batch-fixes/`, `reopened-batch-corrections.md`, `reopened-batch-final-review.md`. The reviewer’s pending-full-suite note is superseded by `full-final-a`; its table incorrectly marks `native-green-a` as passing—the preserved run failed on a WinRT callback defect, corrected before publish-c/native-green-b. The native route checks real More-menu availability and invokes the picker method; it does not certify physical pointer/keyboard input or live-server interoperability. No installed player, account, settings, installer or version stamp was changed. Beta pipeline owns subsequent QA and delivery.

---
---

# November 11 — Finish update installation before offering Restart now or Later

Based on fresh Beta `16f43b3a24a0372e2b85bf1c059ea3462b14690a`; source remains **1.2.973-beta.1** (Windows file version **1.2.972.1**). These identify the same Beta build; the installed DLL confirmed both values. Version stamps remain pipeline-owned.

The update panel uses ordinary native ownership to stay above Silo without becoming globally topmost, including while Silo is inactive. Normal applications can cover it, minimize/restore follows Silo, and a separate-process helper survives the original window closing. A real rendering-frame acknowledgement now precedes dismissal of the large update dialog, keeping one painted surface through preparation and authenticated helper transfer.

Restart now / Later are offered only after the independently authenticated privileged worker has staged and verified the installation and authorized the new client. Restart enters finishing rather than returning to installation. Later explicitly preserves that completed transaction; resumption fetches fresh release authority, requires an identical manifest and re-verifies retained files/bootstrap before activation. It reuses the transaction without copying the installation again. Existing close/save, parent-exit, ownership, rollback, package and pipe-identity checks remain required. The staging deadline is suspended only while waiting for the user's answer.

Verification: **418 focused update tests**, **5,472/5,472 full unit tests**, **52/52 update-protocol tests**, fresh app/helper self-contained Release publishes, and **11 native scenarios** passed with exit 0 and the completion sentinel. The native cases cover first-render-before-dialog-close, normal focus/cover/minimize behavior, separate-process owner destruction, authenticated handoff, cancellation, five playback-save drain outcomes and full-installer consent. Final app/helper/native/unit/protocol PE/PDB identities and authored-source checks have zero mismatches (751 app, 183 native, 6 helper, 365 Core, 5 protocol, 427 unit, 15 Player documents).

App DLL SHA256: `6B60137E847DAA690813CEC7AE74BFDDBAB8070441AFED8D6D251B60626EC367`. Receipts, baseline failures, manifests and review: `D:/SiloPlayer/.codex-tmp/updater-ready-nov11/`. Baseline regressions fail for missing native ownership, the dialog closing before the first preparation frame, and discarded completed staging. The initial paint test observed Closed after its animation and therefore passed; the stronger test observes Closing before dismissal and reproduces the gap. Failed bring-up/unit expectation runs remain recorded.

No real UAC prompt, registered-installation update, installed-player closure/restart, live account/settings access or physical-input certification occurred. Filesystem transaction tests use isolated synthetic installations; native tests use owned fixture windows and authenticated synthetic peers. Independent Claude review approved the final source with verification limits and two nonblocking observations; no corrections were required. The lead checked final full/native/binding receipts after review. Beta submission is recorded in the adjacent delivery report. The existing Beta pipeline owns combined QA, integration, versioning and packaging.

# November 11, 2026 — device-login session binding

Official Server/WebUI reviewed at 4a9e527a432e8ef3a15dcd568802a45c15169039; fresh Beta base 3f098c88ca4770db6f71e84e0e2290df42f8ec2c, unchanged version 1.2.970. Native login negotiates session binding with one random in-memory setup secret, uses the exact SHA256/base64url start and poll fields, retires changed owners, and preserves manual approval and temporary SiloCast handoff behavior. Secrets are neither persisted nor exposed in transport diagnostics.

QA correction: native phone sign-in retains support for configured plain-HTTP servers, including servers advertising session binding. Bound start/poll requests use the same setup secret on either transport without downgrading; binding protects setup ownership, not the transport. HTTPS remains necessary to protect credentials and tokens from network observers. Legacy servers without binding retain the existing unbound behavior; password login and temporary SiloCast handoff are unchanged. Transient poll responses (408/425/429/5xx) retain the setup and retry no sooner than the poll interval or Retry-After, whichever is longer; terminal refusals still retire it. Pending polls now pace the next poll from the server's `poll_after` when present, as the SiloCast handoff does, and every server pacing value (start interval, `poll_after`, Retry-After) is bounded to 300 seconds so an oversized hint keeps the setup instead of overflowing the wait and ending sign-in. A capability/start response that stalls into an HTTP timeout on the current attempt now reports that phone sign-in could not start instead of returning silently to idle; user cancels and replaced or changed owners stay silent. Unit coverage checks HTTP binding, stable secrets and retry metadata, and native coverage checks actual retries, Retry-After, `poll_after` pacing, an oversized Retry-After and HTTP QR/code setup rotation. This correction was source-inspected only; builds and tests are deferred to the pipeline. The verification receipts below predate this correction.

Server contract (confirmed November 12, 2026 against official Silo Server main 0b579aa6192d49d7e8e0921dde5e955ac29ce302, which implements binding): `GET /api/v2/auth/device/capability` reports the boolean `session_binding` (`DeviceLoginCapability` in `internal/apiv2/device_login.go`); `POST /api/v2/auth/device/start` takes `binding`, "base64url without padding of SHA-256 of a 32-byte secret the device keeps" (pattern `^[A-Za-z0-9_-]{43}$`); every poll of a bound request sends that secret as `binding_secret`, base64url without padding. `deviceLoginBindingMatches` in `internal/auth/device_login.go` decodes `binding_secret`, takes SHA-256 of the raw bytes, encodes it as unpadded base64url and compares it with the stored binding in constant time; a missing or different secret is 403 `device_binding_mismatch`. `DeviceLoginSetup` matches this exactly (32 random bytes; start `Encode(SHA256.HashData(secret))`; poll `Encode(secret)`; snake_case JSON). The QA note below predates this check; its "unverified" reading is superseded.

QA contract status (superseded by "Server contract" above): the binding wire contract is not established by QA's pinned upstream. The names and encoding used here (capability `session_binding`; start `binding` = unpadded base64url SHA-256 of the 32-byte setup secret; poll `binding_secret` = unpadded base64url of that secret) come from the review cited above and were not confirmed against the audit's pinned Server revision 3543933a11bf80d721a354ae7751b91e373457dd: its `internal/api/handlers/auth_device.go` start, poll, lookup and capability types carry none of these fields, and the handler or schema that would define them was not available to QA. Read "the exact SHA256/base64url start and poll fields" above as the desktop's implementation, not a verified server contract, and do not treat setup-ownership protection as established until the fields are confirmed against the revision that implements them. As implemented: a server that does not advertise `session_binding` (the pinned revision, or one that names the capability differently) still gets the existing unbound flow with no binding protection, but no longer silently: the sign-in panel states that the server does not support setup binding and that the sign-in is not tied to this app, and `DeviceLoginSetup.Binding` keeps an absent flag (or a 404/405 capability route) apart from an explicit `false`; a server that advertises binding and then refuses the bound start as invalid (400/422) ends the attempt with a distinct "did not accept phone sign-in from this app" message instead of the generic retry prompt; a bound poll refused with `device_binding_mismatch` keeps its distinct terminal message. No bound request is retried unbound; whether any downgrade is acceptable remains an owner decision. The unit and native fixtures encode the desktop's own names and encoding and cannot detect a server mismatch. This note, the start-refusal message and its unit check, and the unbound notice with its unit and native checks, were source-inspected only.

QA correction: an `approved` poll opens a session only when it carries both tokens, a positive `expires_in` and a nonblank account id. An omitted `user` deserializes as an empty account, so the earlier non-null check accepted it and opened a session with no account identity and no scheduled refresh. An incomplete approval now ends the attempt with the generic "Phone sign-in failed" message instead of signing in or polling on. The unit check on the poll model and the native incomplete-approval cases were source-inspected only.

Verification: 59 focused and 5,315 full unit tests passed; fresh Release publish and actual LoginPage native flows passed. App DLL SHA256 459C6BF489734F0D1FFB13204AEA6621B8DACECC9551718D1BB177816BAC40DA. All 739 authored app and 181 native-test PDB documents match source. Lead rechecked 2,131 source inputs, the DLL and native completion sentinel before submission; this documentation note was added afterward and does not change compiled inputs. Independent Claude review found no high/medium findings; an advisory notes a harmless extra retirement delay in a narrow cancellation race.

Evidence: this worktree's ignored .codex-tmp/device-binding, plus D:/SiloPlayer/.codex-tmp/server-review-nov10/reopened-device-binding-implementation.md and reopened-device-binding-review.md. Earlier intermittent failures and fixture bring-up failures remain documented. Synthetic native/API evidence does not certify real-phone/server interoperability or complete visual parity. Installed player untouched; Beta pipeline owns downstream review, QA, merge and packaging.

# November 11 ? collection wall, hero and actions for Beta

Based on freshly fetched Beta `370ada819d2675a9e98f76a6aff7fe3994b67407`; official Server/WebUI main reviewed through `4a9e527a432e8ef3a15dcd568802a45c15169039` (previous verified reference `bb1ee4352b1bec38b4a6441ba81e61a92da81d54`). Historical: source was **1.2.970** when this entry was written (current stamps are in the newest entry above); the Beta pipeline owns integration, stamps and publication.

Collections now use one responsive poster wall with ownership/library filters and loaded-only empty states, plus a backdrop hero with description, ownership, Shuffle, Edit/Add Home and More actions. Owner Sync now/Delete share policy and confirmation with the wall cards; shared/server collections offer library-page placement. Delete carries the revision. Pending actions retire on navigation or authority changes. Administrator collection management, including Open in, remains outside Desktop's available access.

Historical implementation report (not verification of the current head): Independent Claude review, **5,330/5,330 full unit tests**, **222 focused tests**, Release publish and six scoped native runs (presentation/actions, owner menu, ownership, server, layout, creation) were reported. QA found the presentation/actions fixture's list and detail snapshots disagreed, preventing its card-menu delete from reaching the server; its claimed completion sentinel therefore does not establish a pass for this head. The stub now uses the matching list item for detail reads. QA fixes also protect group/order baselines from late pre-write reads and remove deleted personal collections from cached server cards. Builds and tests were not run during this fix pass; pipeline verification, including the collections-presentation native fixture, is pending. The previously reported app DLL SHA256 `9C3196BD27FDB6145AF18D6EFCA380E3E4A8C312FA21F46B95380D3140D9A587` and 30 changed-source hash checks are historical, not receipts for these fixes. Earlier full5,316 and other native runs also remain historical.

Evidence: `D:/SiloPlayer/.codex-tmp/server-review-nov10/collections{,-actions}-implementation.md`, `collections-review.md`, `collections/evidence/collections/lead-final-full`, `act-presentation-seven`, and `collections-lead-binding.json`. Baseline browse-parity/ImportedDraft failures are preserved separately. No live-server persistence or complete visual parity certified; wall pagination retains Load more, and backdrop effects use native approximations. No installer or installed-player changes.

---

# November 11 ? Show update preparation immediately and retain panel visibility (Beta candidate)

Based on freshly fetched Beta `370ada819d2675a9e98f76a6aff7fe3994b67407`; official Server/WebUI main reviewed through `4a9e527a432e8ef3a15dcd568802a45c15169039`, following prior scoped reference `bb1ee4352b1bec38b4a6441ba81e61a92da81d54`. Historical: source was **1.2.970** when this entry was written (current stamps are in the newest entry above); the Beta pipeline owns integration, versioning and publication.

Update Now paints the corner panel immediately with Preparing? and animated indeterminate progress, then atomically transfers presentation to the authenticated helper and normal update progress. Ordinary owned-window stacking keeps the panel above Silo on focus loss, allows other apps to cover it and follows minimize/restore. Restart now/Later stay until selected. The helper retains ownership while Silo may survive staging; elevation launch runs off the UI thread.

Verification: 220/220 focused unit tests, 49/49 protocol tests, fresh app and helper Release publishes, and all ten final native scenarios. The separate-process handoff validates the production HWND/PID guard, rejects a foreign PID, and checks staged-failure owner reactivation and minimize/restore. Normal cancellation, fallback consent and successful/faulted playback-save drains pass.

Independent Claude review found the early detach lifecycle defect plus input-queue/coverage concerns. Removed the early detach entirely and moved Process.Start off the interface thread. Expanded cross-process fixtures exercise the actual published shared view and production window validation. Peer logs are isolated. Bring-up failures from a reflected wire-type identity mismatch and an unsupported synthetic acknowledgement are preserved; final uses the exact published type and existing visible frame. Apphost EXE bytes are shared across builds, so final binding includes the native DLL/PDB. Nine valid native cases passed in an earlier attempt; only review-final-native-wire-corrected is claimed as the complete final ten-case pass. Wrongly named earlier scenarios are not counted.

App DLL SHA256: `922A5DF807D816713F0E36111D16EFA28D407E4BF80C8FAB7171A64E5013CA78`; all 739 authored app PDB documents match final source. Helper DLL SHA256: `90BAA4AF4673476874E4149B6475A6457614C02FD448385F988141C26263336E`; all 6 helper documents match. Selected native DLL/PDB identities and source documents also match. Evidence: `D:/SiloPlayer/.codex-tmp/server-review-nov10/updater-impl-c601198f20524c6ea075c30808a234fc/review-final-binding-b`, the original `updater-implementation.md`/review, and `updater-review-fixes-note.md`.

Limits: No actual UAC prompt, installation, restart or registered-installation update ran. Synthetic installers/peers leave the installed player and real settings untouched. Signature, package and pipe/process identity checks remain intact. Installed player remains running and untouched.

---

# November 10 — request removal and library-link alignment (Beta candidate)

Deliberate base: Beta `370ada819d2675a9e98f76a6aff7fe3994b67407`, version `1.2.970`. Reviewed official Silo main through `4a0aba6167265f13d60e7dc6db50428fa78ec0bd`; the fresh `4a9e527a432e8ef3a15dcd568802a45c15169039` follow-up changes WebUI localization without request/server contract changes. The commit containing this record binds the scoped source; version assignment, review, QA, merge and packaging remain with the Beta pipeline.

My Requests now confirms and removes failed, undelivered requests only when the server advertises support, using bodyless DELETE/204 separately from cancellation. Explicit request-to-library links check the current viewer's read access before navigating. Independent review found three ownership/duplicate-submit issues; corrected continuations retain the initiating identity, access and page visit through refresh and feedback, and hold the pending id until refresh settles.

Final correction verification: **62 focused unit tests**, fresh x64 Release publish and the expanded **request-removal** actual-control route pass. The native route covers confirmation departure, post-DELETE refresh departure/profile change, retained duplicate guard, and retired access reads on both pages. Final DLL SHA256: `0E0DAECE6DA66917FFF19B3A8F7AE52BB087311CB4B5671242D2CC953F385B68`. Earlier implementation verification (5,300 unit tests and four request native routes) applies to the earlier candidate, before these final guard corrections. No full native suite or live-server certification is claimed.

Evidence: `D:/SiloPlayer/.codex-tmp/server-review-nov10/requests-implementation.md`, `requests-review.md`, `requests-review-fixes-green.log` and `request-review-fixes/`. The pre-existing media-parity/request-actions failure was reproduced on baseline and kept separate; the shared ExternalTitleCard chip remains outside this change. Existing 403-as-inaccessible policy is retained. No installer, release, installed-player restart or real account/settings action occurred.

---

# Dedicated section dismissal Undo for Beta

Official Server/WebUI reference `4a9e527a432e8ef3a15dcd568802a45c15169039`; source version remains 1.2.970. Dedicated Continue Watching/Next Up dismissal now offers a five-second Undo. Paging is retired before the owner-bound DELETE and blocked until an authoritative server-prefix reconciliation completes. Banner expiry/replacement cannot discard a pending accepted mutation; unmatched events cannot replace the target. Navigation, authority changes, duplicate activation and reset races are guarded. A dismissal re-reads the live row from its start, so the server rather than the number of removed cards decides the paging offset and total; loaded cards that still match stay in place. A failed or timed-out re-read after a dismissal or Undo shows Retry and marks the loaded cards stale, so Retry reloads the row from its start instead of appending (not yet built or run). The Undo DELETE is bound to its captured owner rather than the page visit, so leaving the page no longer cancels it; a failure that arrives after leaving is reported by toast (not yet built or run). A reconnect catch-up no longer retires a dismissal or its Undo: the dismissal PUT is bound to its captured context and, once accepted, is published under the same identity's current context; the Undo DELETE, the Home notification and the row re-read follow the same identity and access authority across the reconnect, and an Undo accepted under an authority that was then retired is reported by toast (not yet built or run). Failed collection sync clears the previous skipped count without replacing unsaved editor drafts.

Verified on the reviewed dependency foundation: 5,443/5,443 full units, Release publish-b, native-green-b exit0 plus completion sentinel, app744/native182 authored PDB documents match. App DLL SHA256 AE162B630EF13FA6FAB3CF5FE1F702D72C0D6C1B6A662816F5C1560411FEB27E. Independent review approves all three corrected P2 findings with no new correctness finding. Evidence: D:/SiloPlayer/.codex-tmp/server-review-nov10/reopened-undo-fixes and reopened-undo-final-review.md. Earlier failed native teardown is retained as failed, not acceptance.

Integrated current Beta97cc284a and PR131 successor98fa4dc8. Final integrated source passes5,452/5,452 full units; integration-native-b exits0 with the exact completion marker, including Undo races, off-prefix count/TV-only identity, and current smart-wizard guidance. Integration-final-binding confirms app744/native182 authored documents with zero mismatches. The prior integration-native-a had passing behavior but exit1 on teardown; it remains failed. The fixture now closes its owned Window and clears Frame history before draining callbacks/restoring services. Synthetic verification does not certify live-server interoperability or complete pixel parity. No installed-player/settings/account control, installer or version stamping occurred.
# Server alignment — profiles and metadata (November 10, 2026)

Base: freshly fetched Beta `370ada819d2675a9e98f76a6aff7fe3994b67407`, version `1.2.970`; stamps remain pipeline-owned. Official Server/WebUI reviewed at `4a0aba6167265f13d60e7dc6db50428fa78ec0bd`, rechecked at `4a9e527a432e8ef3a15dcd568802a45c15169039` (additional WebUI localization only).

Aligns primary/first-profile restrictions and household profile limits, season/episode metadata locks/reset, and scope-safe Match Item hints. Independent review corrections remove an admin-only hints gate, preserve parent image locks, restore default Switch Profile visibility while a fresh read is pending/failed, and refresh refused Add flows.

Final verification: 69 focused tests, Release self-contained win-x64 publish, and four targeted native routes passed. App DLL SHA256 `A5CBF4076C0FFB8A84ACD6625B93DADF1F643B8D0B33631A168A8F0235F583A6`; all 738 authored app PDB documents match current source. Receipts, source and artifact manifests, original failed runs and corrected fixture evidence are retained at `D:/SiloPlayer/.codex-tmp/server-review-nov10/profiles-review-fixes/`; review/correction notes are adjacent.

Synthetic fixtures do not establish live-server persistence or complete visual parity. The installed player remained open and untouched. Beta pipeline owns combined-source verification and packaging.

# Advanced overlays Beta submission branch

This branch starts directly at fetched Beta `97cc284a53f94adc7d1f675a6294fda828c41640`; only the scoped advanced overlay commit is applied. The earlier dependency-base verification below remains historical. Delivery-branch full suite passed5,396/5,396; after the final NumberBox callback correction,143 affected evaluator/wire/revision/source tests and the fresh Release advanced-overlays native route pass. Border width uses integer steps and normalizes typed fractions in the original callback because WinUI suppresses reentrant ValueChanged. Final app745/native182 authored PDB documents match with zero mismatches. App DLL SHA2569C59435CF7059A4E00480F3910C94D316D53BBB85D19ADDD8E3585C1350554D9. Receipts: D:/SiloPlayer/.codex-tmp/server-review-nov10/advanced-overlays-beta/normalization-*; prior failed boundary/binding runs are preserved, not counted green. Version remains 1.2.970. Installed app untouched.

# Reopened A1 — advanced card overlays (November 11, 2026)

Local uncommitted candidate in `t3/complete-a1-card-overlays`, deliberately retaining dependency base `90aa029a3d2969be7b10ef25c0b12dd52f6dd279`. Official Server/WebUI main rechecked directly at `4a9e527a432e8ef3a15dcd568802a45c15169039`. Source stamps remain **1.2.970** / **1.2.970.0**; no rebase onto the lead's newer fetched Beta or delivery action occurred.

Completes the earlier deferred plate/band/source-chip rendering, style overrides, uploaded source/value images, shared bottom-row wrapping and previews across all card hosts. Native editor verification now mounts the actual controls and awaits TextChanged; the previous unmounted fill assertion and interrupted run remain preserved. All seven preset buttons, complete look controls/reset, image selection, raw-value assignment, fallback and stale-owner behavior are exercised. Image wire coverage includes profile/account/server replacement and fresh-read recovery. Revision, opaque-field preservation, capability failure and existing watched rollback remain covered.

Final verification: **5,475 full unit tests passed**, zero failed/skipped; expanded **advanced-overlays native route passed with exit 0 and the literal completion sentinel**, including existing overlay-revisions and pill checks. Inspected representative plate/band/host/settings/fallback captures. The existing successful Release publish was reused after all **1,112 product inputs** matched unchanged, avoiding duplicate payloads. App DLL SHA256 **D443BCBC12A9F74DC5381FC36DCCD8E8172FDDBD62DACD57882AAC9200A862EE**. Actual DLL/PDB identities and authored documents match final source: app749, native183, unit424, unit Core366, unit Player15; zero mismatches. All .NET/native/build/binding operations were serialized under `SiloOct18Build` with the pinned engine and explicit PowerShell7.

Evidence: `D:/SiloPlayer/.codex-tmp/server-review-nov10/reopened-advanced-overlays/` (`units-resumed-final`, `native-editor-controls`, `binding-resumed-final`, original `release-verified`). Full files, commands, failure history and limits: adjacent `reopened-advanced-overlays-implementation.md`. This synthetic evidence does not certify live-server persistence, physical input or complete WebUI pixel parity; the unrelated historical default-native detail fixture limitation was not exercised or fixed. Installed player/settings/accounts remain untouched. No install, restart, version change, commit, push or PR; lead owns independent review, Beta reconciliation and delivery.


# Household title allowances for Beta

Official Server/WebUI reviewed at `4a9e527a432e8ef3a15dcd568802a45c15169039`. The verified title-allowance implementation is integrated with current Beta `97cc284a53f94adc7d1f675a6294fda828c41640`; version remains 1.2.970. All nine v2 operations, household-primary authority, movie/series and owned-collection menus, immediate-save dialogs, and limited-profile Access management are implemented. Unknown standing fails closed; captured authority/navigation owns requests and publication. One-time collection copy is never automatically repeated after an uncertain outcome.

Original candidate checks:45 focused and5,327 full units, fresh Release and native dialogs/menus, independently reviewed with zero P0-P2 findings. Those receipts predate this Beta integration. Final integrated verification is recorded under `.codex-tmp/title-allowances-beta/review-fixed-*`: 5,387/5,387 full units, fresh Release, native route exit0 with completion sentinel (including held-read revoke/add/collection races), and matching app749/native183 authored PDB documents with zero mismatches. App DLL SHA256 A027FDCAA6BEAF4A88E388205440B94F52DB20D5478116D3F0CB0F517A7C0485. The post-write read ownership correction is independently source-accepted; its earlier native red reproduction is preserved. Reports: `D:/SiloPlayer/.codex-tmp/server-review-nov10/reopened-title-allowances-{implementation,review}.md`. Evidence is synthetic; live persistence and complete visual parity are not certified. Installed app and real settings/accounts untouched; pipeline owns versioning and release.

# Overlay review corrections — November 10, 2026

Official main rechecked at `4a9e527a432e8ef3a15dcd568802a45c15169039`; Beta base remains `370ada819d2675a9e98f76a6aff7fe3994b67407`. The revision 25/26 implementation below now also maps catalog watchlist cards to effective rails/corners, clears optimistic episode counts with rejected-write rollback, preserves stored positions and badges when capabilities fail (a save retries capabilities before serializing), and recomputes status reservations/legacy corner shifts on quick actions.

Final source: 107 focused tests, 5,306 full unit tests, Release publish, expanded overlay-revisions and artwork-first-mount native routes passed. The expanded route covers external cards at revisions 24/25/26 and quick-action geometry; it also has a new isolated receipt retaining the exact fixture host, app and fixture source hashes. App DLL SHA256 `FBBE856CEC8D76E081D9F0DE3819AC71E3563899B7F5D3CF66D2F88A538535D5`; all 739 authored app PDB documents match. Evidence: worktree `.codex-tmp/overlay-revisions/review-*` and `D:/SiloPlayer/.codex-tmp/server-review-nov10/overlay-review-fixes/`. Original failure receipts remain; `review-fixes-red` predates the new tests and is not red evidence. `review-regressions-red`, `review-external-red-b` and `review-quick-red` reproduce the review findings. The first external native attempt failed on a missing fixture NavigationService, then the corrected fixture reproduced the product issue.

The deferred rendering/style/image/bottom-row items below remain disclosed. No live-server or complete visual-parity claim, version stamps, installation or installed-player changes.

# November 10 — card overlays revisions 25/26 (local, uncommitted candidate)

Base: Beta `370ada819d2675a9e98f76a6aff7fe3994b67407`, branch `t3/card-overlay-revisions`. Source remains `1.2.970` / `1.2.970.0`, targeting .NET 10; no version, README or installer stamps changed. Official Silo main is `4a0aba6167265f13d60e7dc6db50428fa78ec0bd`, confirmed directly with GitHub `git ls-remote` and against the supplied read-only official clone. No commit, push, PR, release, installer, installed-player or production action occurred.

Desktop saves retain plate/band/future presets and opaque root/item fields, style and uploaded-image references. Revision-aware positions preserve legacy writes below 25 and migrate top-right badges to centered right-edge rails from 25. Rails hold four badges, corners three. The editor and preview use the same revision semantics. Revision 26 offers the combined resolution/HDR/audio badge, with duplicate standalone badges suppressed without changing their saved enabled flags. Poster and Library cards show an independent watched/remaining-episodes status corner, reserve its measured width, and retain quick actions. The catalog state now carries watched/unplayed counts and count changes invalidate mounted-card comparisons. Episode/detail and external watchlist badge hosts also honor edge positions.

Verified with SDK `10.0.403`, x64, engine-pin checks and `SiloOct18Build`: **35 new unit cases**, **104 focused tests**, **5,303 full unit tests**, all passing with zero skipped; fresh Release publish; passing actual-control `overlay-revisions`, existing `overlay-pill` and `artwork-first-mount` native groups, all with the completion sentinel. Final app DLL SHA256: `AF7A94A29DA6F8ECA61BF7EBE9BB574732584E0E1C26E31901C068E8F69FE21A`. Native screenshots were inspected. Commands, red/green evidence, source/artifact bindings and native receipts are under this worktree's `.codex-tmp/overlay-revisions/`; see `report.md` there. Existing compiler/analyzer warnings remain.

Dedicated plate/band rendering, style override rendering and uploaded badge images remain outside this candidate; those values survive saves and plate/band currently use the classic look. Shared bottom-corner row wrapping is not implemented. This synthetic fixture evidence does not certify complete visual parity or physical pointer interaction. The unrelated historical default-native `DetailPresentationNativeFixture.cs:55` limitation is not fixed by this task.
# November 11 ? Align viewer actions and smart collection behavior with Server main (Beta candidate)

Based on freshly fetched Beta `370ada819d2675a9e98f76a6aff7fe3994b67407`; official Server/WebUI main reviewed through `4a9e527a432e8ef3a15dcd568802a45c15169039`, following prior scoped reference `bb1ee4352b1bec38b4a6441ba81e61a92da81d54`. Source remains **1.2.970**; the Beta pipeline owns integration, versioning and publication.

Dedicated Continue Watching/Next Up pages remove eligible cards and update counts. Complete-rule validation gates smart editor/wizard previews and saves; Trending has a Show selector. Calendar labels/palettes, library sidebar icon aliases, failed-sync status refresh with draft preservation and episode library scope match current upstream. Dismissal retires any pending continuation and re-seeks the shortened live row while preserving the already-loaded prefix.

Verification: Final full unit suite 5,352/5,352; fresh Release publish; expanded viewer-actions native route passes every scoped scenario. Both paging response orderings retain all 129 remaining titles exactly once and the correct count. The race-after native case reproduces a missing title/duplicate against the frozen earlier candidate.

Independent Codex review confirmed the dismissal/paging race. The fix cancels and retires the old load generation, clears its snapshot and resumes at the corrected prefix offset; cancellation-ignoring held responses cannot append. The initial test-edit command failed before saving, but the corrected source was saved before the queued red fixture compiled; its result records the expected race-after defect. Earlier worker source/test-manifest mismatch is superseded by final PDB binding and source manifests.

App DLL SHA256: `FA7601A51FB4EAEC8B361154DCDF107ADA3D0751265797D094A45B03CD9A6A84`; all 741 authored app PDB documents match final source. Selected native DLL/PDB identities and source documents also match. Evidence: `D:/SiloPlayer/.codex-tmp/server-review-nov10/viewer-actions/review-final-binding`, the original `viewer-actions-implementation.md`/review, and `viewer-actions-review-fixes-note.md`.

Limits: Existing sidebar-publication IsPaneOpen failure reproduces on baseline and candidate; four icon assertions before that failure passed, so no completed sidebar route is claimed. Existing skipped-sync-count retention remains a nonblocking pre-existing gap. Dedicated page has no Home-style Undo. SVG visual parity and live-server interoperability are unverified. Installed player remains running and untouched.

---

# Current source identity — native Chapters and compact More (PR #91)

Source head: `01bd0534a533f33392ad4e52ac2a701a67049a0a` (reviewed PR #91 input); integrated with Beta's native Audio and Watch Party changes below.

Source version: `1.2.1660`; numeric version: `1.2.1660.0`.

These are the existing app, updater, launcher and installer-default stamps; this merge changes no version stamps. The input identities and earlier verification records below are historical and do not verify the merged source. No builds or tests were run for this merge; fresh pipeline verification and final commit binding are required.

## PR #90 — historical source identity (QA correction)

Source head: `2e359e6055440789dfc7e42b171519e5e9c58ee3`.

Source version: `1.2.1660`; numeric version: `1.2.1660.0`.

The app, update host, launcher and installer defaults already carry this identity; no version stamps were changed for this QA correction. This is an inspected source-identity record, not a build, test or release receipt. Builds and tests were not run for this correction; the pipeline must verify the corrected source. Earlier candidate and PR #86 identities and verification receipts below are historical and do not certify PR #90.

## PR #87 — historical source identity (QA correction)

Source head: `9f0495bbc2990b45ea0c7b1d6a69c1687c6e6c4e`.

Source version: `1.2.1660`; numeric version: `1.2.1660.0`.

These values match the existing app, updater, launcher and installer-default stamps; this correction changes no version stamps. Earlier source identities and verification receipts below describe historical candidates, not this head. No builds or tests were run for this correction; the pipeline must verify the corrected source.

## November 3 — safe retained Library activation (local Beta candidate)

This task deliberately starts from Beta `f6442fd8de1b2b015d2d45a15681e20ba30de025`; source version remains **1.2.544-beta.1** and the pipeline owns stamps/delivery. Official Silo main for this pass is `bb1ee4352b1bec38b4a6441ba81e61a92da81d54` (parent's direct fetch, verified with pinned `git show`). No installer, installed-player restart, push, release or production action occurred.

Valid retained Library activation now uses its current tab, filters, audiobook axis/search and position without another blocking settings restore. One confirmed settings document belongs to the actual injected SettingsApi and current profile/device/session owner. Relevant realtime/local mutations and access/reconnect/language changes retire that shortcut; indistinguishable events use coalesced authoritative reads, pending local writes settle first, and equal own echoes do not reapply the view. Serialized writes preserve other libraries and skip unchanged documents. Opt-out preserves current visible input, prevents saves and uses defaults on the next unqualified activation. Explicit tab/genre navigation wins and reloads its actual catalog query. Visit/edit guards retire obsolete reads, failures and malformed saved queries; metadata still revalidates.

Independent review of `ca1aa138df7737dd118b3699613f3175d8e691ce` found two additional orderings, reproduced against protected app `67D4…`: failed revalidation lost a known opt-out, and a held save could report an error after a newer edit. The correction preserves known false through repeated failed reads for the same owner, retires that knowledge on owner change, and requires the producing edit to own a save-error notification. Explicit tab/genre still wins, unknown successor preference does not inherit the old owner's opt-out, and current save failures still notify.

Final round-1 verification: **25 focused cases**, **3,479 full unit tests**, successful fresh Release publish, **30 actual-control conditions** (original21 plus9 review regressions), existing saved-query/author-axis/profile/drawer native regression and cursor checks at 1920×1080, 3440×1440 and 3840×2160. Passing native routes contain the literal completion sentinel. One full attempt was interrupted without TRX/receipt or surviving test process; its preserved log is not a pass or a proven test hang. The single bounded recovery completed3479/3479 normally. Existing alphabet route previously failed its Z-clamp/current-letter condition at `LibraryLettersNativeFixture.cs:86` identically on original candidate67D4 and preserved baseline528721. That separate alphabet limitation was not re-run or fixed in this review round; no combined runtime claim with the companion alphabet correction is made.

Final app DLL SHA256: `EF8F37B3FF513A8168B81EA64C83E5F3413CB3C71716B3ADA6C4408162C5B1EF`. DLL/PDB identities match; all677 authored product PDB documents match the final source. Evidence, red/green commands, original/final fixture hashes, exact source/compiler/artifact bindings, final commit and remaining limits: `D:/SiloPlayer/.codex-tmp/browse-polish-nov02/retained-library-task-report.md`. Existing compiler/analyzer warnings remain. This is synthetic owned-window evidence, not a claim about physical input, observed frame pacing or complete WebUI visual parity. Parent owns scoped re-review and Beta delivery.

## November 3 — immediate Library alphabet feedback candidate

Focused Task4a branch `codex/nov02-letter-feedback` starts from Beta `ac51554902c252814191dd88cf4b1d991efdfea6`; source remains `1.2.544-beta.1`, numeric assembly/file version `1.2.543.1`. Parent's official server main reference is `bb1ee4352b1bec38b4a6441ba81e61a92da81d54`. This is a local candidate, not a release or installer. Retained Library activation is the separate Task4b.

Letter choices now show immediate selected/pending feedback, retain current query/context/language ownership through target publication and queued native navigation, and offer scoped Retry. A missing target revalidates its optional index; failed revalidation keeps Retry while unsupported indexes in another scope remain quiet. Manual movement, including0.5px native scrolling, retires pending/error/queued operations. Accepted `ChangeView` no longer republishes its preceding observed offset, preserving final-Z selection at the clamped bottom.

Review round1 corrects signed-cursor letter recovery and the second deferred native scroll boundary. A rejected signed scope discards its cards/cursors/index before transferring only the current semantic letter to a fresh exact index; failed or missing fresh targets cannot restore old-scope cards. Scoped Retry survives automatic debounce/fill/resize. Deferred scroll requests retain visit, request, query/context and native-origin authority until `ChangeView` dispatches.

Synthetic verification through the pinned-engine/mutex parent runner: focused **115/115**, full **3,466/3,466**, fresh Release publish, actual signed letter recovery and both-dispatch-boundary checks, expanded letter-feedback and original letter controls at1280×1000 /960×550, and existing cursor controls at1920×1080 /3440×1440 /3840×2160. All selected native groups log the completion sentinel. Final app DLL SHA256: `D9B9F81F01E99F2803C762D21E83311E3227525DCE569FB988C594A0971FC4AA`. The original E0C999 candidate and its evidence remain historical. Pending/error screenshots remain readable; the synthetic audiobook item-type mismatch and invalid-cursor problem-URI correction are fixture-only records.

Evidence and exact committed source binding: `D:/SiloPlayer/.codex-tmp/browse-polish-nov02/letters-task-report.md`, `letters/review-r1-bindings.json` and historical `letters/final-bindings.json`. The first full run's24 unrelated updater Win32 path failures remain preserved; parent corrected its deep D: TEMP path and demonstrated unchanged updater/protocol inputs passing68/68 before the passing full rerun. Existing compiler/analyzer warnings remain. No complete visual parity, hardware input or all-native-suite claim; the inherited Home-row completion baseline is outside4a. Parent owns independent Standards/Spec reviews and Beta delivery. No version stamps, installed player, real account, updater product code, installer or production state changed.

## Current source identity — native Quality menu (PR #89)

Source version: `1.2.1660`; numeric version: `1.2.1660.0`.

Source head: `5f38c219a0b5f53b32446e0d936631ef6fa99642`. This records the existing app, updater, launcher and installer-default stamps without changing them. No builds or tests were run for this documentation correction; pipeline verification is required. The PR #86 review and verification records below remain historical and do not verify this head.

# Historical development — OSC feedback and input ownership (PR #86)

Reviewed source head: `96e6011aa9957717749ec51c88f27ca7b9a427fe`; Beta base: `aeebcf4bf9e15c61d29931ec730bd412ac1c8665`.

Source version: `1.2.634-beta.1`; numeric version: `1.2.633.1`.

These are the app, updater, launcher and installer-default stamps in the reviewed head. They changed relative to the original OSC base `d37eae35a2e1b6c4f4c26dde6c22713aa12c478f` (`1.2.549-beta.1` / `1.2.548.1`); the current PR diff against the Beta base above does not change stamps. This QA correction updates the identity records, not the stamps. The PR polishes the active Lua/ASS HUD; the correction bounds idle controller focus, preserves it on zero-delta mouse messages, retires focus/menus on content replacement or disable, isolates compact pointer actions from buffering-prompt activation, and retains accepted Exit/Minimize presses through fade-out.

The [OSC verification record](research/2026-11-03-osc-hud-states-verification.md) distinguishes historical local evidence from this source identity. No builds or tests were run for this correction; pipeline verification is required. The pipeline owns the final release version: if it stamps another identity, refresh the current identity records and repeat version/build-input and artifact checks after stamping. Historical test counts and hashes do not verify this head or the correction.

## Historical synced collection direction Beta candidate

The earlier synced-collection candidate started from Beta `6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af` (source `1.2.544-beta.1`), with official server/WebUI reference `bf53b65450244751013a2965b39da4111d9a0f72`. Its unchanged-stamp statement describes that earlier candidate only. Native new/saved synced editors now separate Sort, Direction and Max titles, retain stored reverse/unknown fields, use current alphabetical/date/numeric labels, and disable source-order or unsafe future-field direction changes. Field changes use normal defaults; existing API serialization and untouched legacy config behavior remain.

Actual native RED against exact544 bytes reproduces the missing Direction control. Initial D Release/native and3,058 full tests pass, then strengthened real keyboard-focus checks reproduce order controls hidden by the floating actions. A scoped queued focus-centering handler fixes keyboard visibility without moving pointer focus. Final D Release/native uses DLL `666258849B4B66BE74CACF37AB796E3CC4AFA60B16A1F19140DA5F48985CFCFF`; four actual new/saved1280/460px configurations pass field/direction/default/source-order, unknown-field retention, save guards and keyboard/footer bounds. Screenshots were inspected and the Order section fits in both layouts. Final D: full re-verification passes3,058/3,058, zero failed/skipped (`collection-direction/d-year-full-green`). Independent Standards/Spec review and scoped Year correction reviews have zero remaining findings. Independent Spec review found Year was incorrectly classified numeric. Actual native d-year-red reproduces the wrong labels; the one-line date classification correction passes all four native configurations in d-year-native-green, with a separate runtime numeric-label check. Source hashes in `year-inputs.json` remain exact.

Evidence: `D:/SiloPlayer/.codex-tmp/server-align-nov02/collection-direction`, including scoped original red and focus red, preserved first/final publishes, native receipts and screenshots. Every .NET command holds SiloOct18Build after pinned engine verification. New source/builds stay below D:/SiloPlayer as requested; C originals/frozen evidence were preserved and copied input hashes verified. All accounts/APIs/data are synthetic; no setup, installed player, user credentials or production were controlled. This is local candidate verification, not a published release or full visual-parity certification. Independent reviews and the Beta PR receipt are recorded before final delivery. Original handoffs below preserve their historical context.

## November 2 — account playback-limit Beta candidate

Fresh Beta base `6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af` (source `1.2.544-beta.1`) and official server/WebUI `bf53b65450244751013a2965b39da4111d9a0f72`. This local candidate distinguishes account streaming/conversion allowances from server capacity using current WebUI titles and trimmed viewer guidance, with the existing retryability intact. It adds no automatic retry or version-stamp change. A new start after either account terminal uses a different playback attempt ID.

Test-first: eight presentation assertions fail against the unchanged base; both synthetic start-after-limit tests already pass. Corrected focused tests pass 45/45 and full suite 3,068/3,068, zero failed/skipped. Evidence: `D:/SiloPlayer/.codex-tmp/server-align-nov02/limits/{focused-red,focused-green,full-green}`; exact verified input hashes in `verified-inputs.json`. All .NET work holds SiloOct18Build after pinned native-engine hash verification. Generated outputs were moved to D with their original paths junctioned back after C filled; failed unrelated build evidence is preserved. No installer or installed app was used. This pure Core presentation change awaits independent source review/Beta PR delivery; pipeline owns application build, stamps and packaging. Historical handoffs below retain their original context.

# Current development — 1.2.519

## November 2 — episode series-overview Beta candidate

Approved PR4 source `7154651d4f6a4a0cb88ff6411b6bcda894ecd0fc` on `codex/nov02-episode-overview` starts from freshly fetched Beta `6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af`. Its deliberately newer source version remains **1.2.544-beta.1**, with unchanged FileVersion/AssemblyVersion **1.2.543.1**. Official server/WebUI reference is `bf53b65450244751013a2965b39da4111d9a0f72`. This is an unpublished focused candidate; the pipeline owns review integration, version stamps, packaging and Beta publication.

An episode with a blank/whitespace overview now shows its series description in the captured library/profile/metadata-language scope. Text, machine-translation provenance, manual/automatic translation and scoped polling follow that series source; episode identity and playback remain unchanged. Series fallback stays visible under episode spoiler protection, while an episode's own plot retains reveal behavior. Missing/failed series remains optional. Item/library/profile/language/navigation ownership retires delayed reads, status and translation callbacks. Same-page reload resolves the source again; provenance and the reveal action share the description's hero column.

Final synthetic verification: **3,077/3,077 unit tests**, successful self-contained Release publish, and complete actual native `episode-overview` fixture PASS against the same DLL bytes. Native checks cover fallback/reload/source labeling, manual and automatic series translation/polling, own overview/reveal/episode translation, absent/failed series, stale read/status/write/config ownership, cancellation restoration and 460px/1280px geometry. Both screenshots were visually inspected. Initial native RED reproduced absent fallback; follow-up RED reproduced reload clearing and detached compact provenance before their fixes. Existing compiler/analyzer warnings remain.

Evidence: `D:/SiloPlayer/.codex-tmp/server-align-nov02/overview/{d-final-full,d-final-publish-two,d-final-native}`; full report `D:/SiloPlayer/.codex-tmp/server-align-nov02/episode-overview-report.md`; frozen product hashes `overview/d-final-source-inputs.json`. Final app directory is `.codex-tmp/overview-d-final-publish-two-client` under the D: task worktree. DLL SHA256: `C59398DF9071EDA8E73C9B6BBCDF0D5E9C5A3FB3E93A9ACD0447FBC37F4C9716`; PRI SHA256: `4493960CADFDB276BB8E2FE225B227B8E4805E0A9242D6E87AD0AD4A6D7406F1`. All 1,035 product hashes match the tested source.

The owner requested continuing under the established D: project; root's `d-source-copy-receipt.json` preserves the byte-identical relocation and earlier C: evidence. All subsequent source/build work uses `D:/SiloPlayer/.codex-tmp/server-align-nov02/worktrees/episode-overview`. Every .NET invocation held `SiloOct18Build`; pinned mpv was hydrated/hash-verified. No installer, installed-player control, real credentials/account/provider, production action, push or release occurred. Synthetic controls/source comparison do not establish complete visual parity or real-server translation interoperability. Parent owns independent Standards/Spec review and Beta PR delivery.

## November 1 desktop follow-up

Verified remote stable release is `v1.2.519`, source `3415f0720538bf7217e194bc84ac38098404361f`; main `a26ff559de24fa654e21a11e3132a1b18295c7d8` adds README download links. Beta base `a15621a0d6d5960d6b31a620a23ae259d734dedb` has the same tree. The owner authorizes focused PRs into Beta; the pipeline owns review, merge, version stamping and beta publication. Stable/main promotion still requires approval.

Updater wording is separate [PR #64](https://github.com/ellermw/SiloDesktopApp/pull/64), head `8563dbbb22bf8f52ef43001d8f1f89bb40132b53`: window title and heading say **Updating Silo Desktop Player**. Its safe isolated startup smoke check fails against preserved 1.2.516 and passes against the newly built helper; independent Standards/Spec reviews found no issues. It does not exercise installation or the user's installed helper. Updates use the helper from the currently installed version, so new wording is observed on updates started after that helper has been delivered.

The current branch adds the owner-approved Library alphabet rail. Official server/WebUI main is pinned to `63ef34b236b88dc77acca1a9a3ed90235377691f`. See the [comparison and proposed PR boundaries](research/2026-11-01-server-desktop-gap-analysis.md), [design](superpowers/specs/2026-11-01-library-letter-navigation-design.md) and [plan](superpowers/plans/2026-11-01-library-letter-navigation.md). This is a focused source comparison, not a claim of complete visual parity or that every follow-up is implemented. The alphabet uses the capability-gated server index for exact positions, keeps query/profile/language ownership, and preserves bounded card virtualization.

Verified product head: `e344727663e8aa47a707a41b2ef3075aebfd3783`. Final main suite: **2,817 passed**, including 19 letter-navigation cases. Final isolated Release publish succeeds; five native cases pass: alphabet interaction/layout at 1280×1000 and 960×550, plus existing cursor scrolling at 1920×1080, 3440×1440 and 3840×2160. These exercise synthetic data and actual native controls. Independent Standards/Spec re-review has zero remaining findings. Existing nullable/analyzer warnings remain. Pre-review regressions reproduced slow-index blocking and independent count races; focused red/green receipts and the full rerun are preserved.

Diagnostic client `.codex-tmp/library-client-r3` remains version 1.2.519; it is not a release package and must not be uploaded. Its app SHA256 is `7647C4A35BAE68987B324B9E668358BD660DA997C65F5C2523B46893B9A153F2`. Final receipt: `D:\SiloPlayer\.codex-tmp\desktop-nov01\library-final3-receipt.json`; unit TRX: `library-tests-final2-results/main.trx`. Native receipts are under this worktree's `.codex-tmp/native-artwork-tests/2cdcc4d8990347e4a71c5a31ff19d18f` (letters) and `cf5978cde4fe43f18637b6a29468df3e` (cursor). The native runner stages the actual published Updates content in its own reference folder to satisfy WinUI content imports; original/final runner hashes and failed/corrected fixtures remain in `native-runner-correction.json`. Product publish outputs were not modified by fixtures. Subsequent documentation-only commits do not change these tested inputs.

Local evidence is under `D:\SiloPlayer\.codex-tmp\desktop-nov01`. An initial full test attempt used an external assembly output path; source-inspection fixtures walked up to the historical canonical checkout and read the wrong source. That run was stopped and preserved as invalid evidence; rerun outputs live beneath this worktree so source lookup resolves correctly. Installed Silo, user data, previous frozen packages and source worktrees are preserved. No production setup or app control is authorized by this work.

## Historical 1.2.517 channel implementation

## Password session revocation — 2026-11-01 task handoff

Task branch `codex/server-password-sessions` deliberately starts from the approved newer source `a15621a0d6d5960d6b31a620a23ae259d734dedb`, stamped **1.2.519**. This section is a task handoff, not a new release or installer receipt. Official server/WebUI reference is `63ef34b236b88dc77acca1a9a3ed90235377691f` (`internal/apiv2/account.go` and `web/src/pages/settings/AccountSettings.tsx`).

Password settings now offer an unchecked **Sign out of other devices** checkbox only when the server advertises support and permits password changes. Requests omit the member unless selected; the server preserves this sign-in and API keys. Selection follows existing page/profile ownership and resets on reload, departure and success. Success feedback uses the captured request choice.

Fresh synthetic verification for this source: focused account-password **29/29**, full unit suite **2813/2813**, successful self-contained Release `win-x64` app publish, and successful `password-sessions` native fixture checking actual visibility/default/toggle/payload/pending/reset/current-session behavior and compact wrapped layout. All .NET commands held `SiloOct18Build`; pinned mpv hash matched `engine.json`. Existing unit, app and native compiler warnings remain documented in the logs. Native `run.ps1` remains unchanged; the external task runner staged published references including `Updates`.

Evidence: `D:/SiloPlayer/.codex-tmp/server-align-nov01/password/{focusedgreen2,fullmain,releaseapp,nativecontrols}` and `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-a-report.md`. Owned publish: `.codex-tmp/password-releaseapp-client`, `SiloPlayer.dll` SHA256 `5F2C544FF5D67497A7C1B378D99EE54FF8F06AEB5A9CDEED1EC7536EA87B9CF2`. Tests/publish preceded the implementation commit `fdfa908a0651c14c2ea883252fa2ccc8301ef8ea`; their baseline HEAD receipts accompany the working-tree changes subsequently committed there. The final fixture-only service-resolution adjustment was verified by the native run.

No release stamps changed, installer ran, installed player was controlled, real account was accessed, or release was published by this task. Live multi-device revocation and complete WebUI visual parity are outside this synthetic evidence. Parent task owns independent review and Beta PR delivery.

Independent review follow-up: source `be578992694b3e58e77712d3ca8dbac462d09dbe` shares the visible session explanation with automation help through an ElementName binding. The native fixture changes the visible explanation to a synthetic alternate string and verifies help follows it; this failed on the original publish and passed on the new publish. A direct consumer test of the existing four-argument `ChangePasswordAsync(..., default)` compiled and passed against the unchanged API, disproving the reported overload ambiguity, so the overloads remain unchanged. Focused verification is now **30/30**; the earlier full **2813/2813** remains the full-suite baseline because password/API logic is unchanged. Review evidence: `password/{reviewred,reviewcopyred,reviewgreen,reviewpublish,reviewnative}` under the same external evidence root.

The fresh reviewed-source Release publish `.codex-tmp/password-reviewpublish-client` and native fixture both passed with receipts recording source `be57899`. Final `SiloPlayer.dll` SHA256 is `88900EC28E064D219B6582C343943D93582E7E985AAD011EC554074E19596589`; final `SiloPlayer.pri` SHA256 is `07A117640252516213C0D6B1410FC84CB6E9FB12F68DB4E3667F7A92EAA5640A`. The original publish remains frozen at DLL `5F2C544FF5D67497A7C1B378D99EE54FF8F06AEB5A9CDEED1EC7536EA87B9CF2` and PRI `35131778B2CB8E8A80569F3EB6AAEB039AF4658FFE6FF5CD2601065CD6ADA873`. Existing warnings and verification limits remain as above; no release publication is implied.

P2 Spec review correction: runtime source `63df522fa7814939b9d5b67119be33c110af782c` starts sign-in/password refreshes together under the existing visit ownership check after credential/network link, browser-link completion and unlink. A delayed canceled sign-in refresh therefore cannot start password loading for a newer or departed visit. The native regression reproduced the original stale network callback against the frozen `be57899` publish, then passed for network/credentials across newer choices, pending opted-in submissions with request-specific success, and departure without reentry. The fixture now registers its own synthetic AuthApi for those flows.

Fresh P2 verification: focused **30/30**, full unit suite **2814/2814**, successful Release publish and expanded `password-sessions` native fixture, plus existing `account-latest` identities/unlink and OAuth acceptance cases. Evidence is `password/{lifetimered,lifetimered2,lifetimefocused,lifetimefull,lifetimepublish,lifetimenative,lifetimeidentities,lifetimeoauth}` under the external evidence root above. `lifetimered` was a fixture setup failure; `lifetimered2` reproduced the actual product defect. Publish and all final native receipts identify exact committed source `63df522`. Final candidate `.codex-tmp/password-lifetimepublish-client` has DLL SHA256 `048774326B32E6ACB299DECA4394BFC0E5F0A754452C7E8C74FDF8AD5304C7FF`; PRI remains `07A117640252516213C0D6B1410FC84CB6E9FB12F68DB4E3667F7A92EAA5640A`. Prior evidence and artifacts remain frozen. All verification remained synthetic and mutex guarded; existing warnings and live-interoperability limits remain unchanged. No push, release, installation or installed-player control occurred.

## Opt-in Beta update channel (PR #62)

PR #62 adds Stable-default, opt-in Beta updates. The source is stamped **1.2.517**, with numeric AssemblyVersion/FileVersion **1.2.517.0**, in the app and update helper projects; the installer defaults match. This is a stable source identity, not a `1.2.523-beta.1` package. The PR base is `fdccad18` (abbreviated commit ID).

The implementation includes persisted channel preferences, semantic release ordering and identity validation, bounded GitHub catalog discovery, same-MAJOR offers without silent downgrades, channel cancellation/cache retirement, direct skipped-version hash reuse, explicitly verified full migration and semantic version/channel presentation.

See the [release contract](update-release-contract.md) for canonical tags, GitHub flags, the five public assets, semantic-to-Windows version mapping and rollout requirements. Source stamps do not establish publication status or prove that an installer has been built, sealed or installed. Build/test results for this PR must come from its pipeline; machine-local candidate receipts are not evidence for this source.

## Compatibility and rollout

Stable remains the default for existing and fresh preferences. Installing a Beta manually does not select the Beta channel. Existing clients need a channel-capable client and matching version-local helper before opting in. Compatible incremental updates keep the Protocol1 root bootstrap immutable, preserve prior versions and user data, and reuse only hash-verified files. Full migration requires the existing verification and manual-consent gates.

The update dialog retains its bounded scrolling body and fixed actions. Channel changes retire prior offers and prepared packages while preserving shared authority cooldowns. Checks wait for overlapping checks and other short operations, but return immediately without changing state when a download is active or pending. Canceling a queued check leaves the active operation untouched; checks read the channel epoch after acquiring the operation gate. Switching from an ahead-of-Stable Beta never downgrades the installation.

## Historical release documentation

The [1.2.516 release ledger](releases/1.2.516.md), [1.2.515 handoff](archive/2026-10-29-release-1.2.515-handoff.md), [1.2.514 handoff](archive/2026-10-29-release-1.2.514-handoff.md) and [earlier handoffs](archive/2026-10-29-development-handoffs.md) describe historical releases, not this PR's source identity or verification status.

## November 1 — cross-device series/season state refresh (task B)

This task deliberately starts from Beta source `a15621a0d6d5960d6b31a620a23ae259d734dedb`, version **1.2.519**, newer than the historical 1.2.517 context above. It is an isolated implementation handoff, not a published release or installer receipt. Official server/WebUI main was inspected at `63ef34b236b88dc77acca1a9a3ed90235377691f`, including originating series-event change `20b1d359c95d73ca17720ed95013c268c667fdd1`.

Watched/progress `user_state.changed` now captures profile authority before dispatch, immediately retires cached detail/watch reads, and coalesces visible refreshes after 200ms with a one-second deadline. Batches retain all named series even if later events omit them. Mounted related series/season details refresh authoritative user data, detail episode/season totals, loaded season membership and episode counts, and explicit server play targets; an unknown episode's watched/progress scalar never cascades to siblings. Episode companion reads update user data only on already-loaded episode rows; navigation or season selection still refreshes episode list membership. Navigation, profile departure, subscription retirement, window close and newer local watched changes reject stale work. Existing reconnect catch-up refreshes detail state without remounting the page or issuing playback/room commands.

Synthetic verification receipts live outside source under `D:/SiloPlayer/.codex-tmp/server-align-nov01/realtime`. Initial `red-detail` reproduced two stale aggregate failures; `red-target` reproduced two stale explicit-target failures. Lifecycle regressions also reproduced missing read cancellation, queued-input deadline starvation, premature initial-list retries and lost accepted feedback for a pending local watched write. `green-local-feedback` passed all 88 focused tests after correction. The final full suite passed 2,830 and failed one unchanged update-cleanup fixture: `UpdateTransactionLifecycleTests.ConfirmedUpdateRetainsActivePreviousAndForeignFilesWhileCleanupCanDefer(inventory: "unknown")`, line 215, initial synthetic update `Succeeded` was false. The earlier full suite passed 2,830 before the final local-feedback correction; no update-code changes or root-cause claims are included. Exact results and the scoped rerun are preserved in the task report. Final isolated Release/native verification remains assigned to the parent delivery pass. The dedicated native route is `realtime-series-state`; it uses synthetic HTTP and actual mounted detail controls. No installer was run, installed player was controlled, real account was read, or production endpoint was contacted. Phone interoperability, historical buffering and complete visual parity remain outside this verification.

The isolated update-cleanup rerun passed all four variants, including `unknown`; the full-suite failure remains disclosed. Parent Release publish `realtime/release-final-app` succeeded against final unchanged product inputs: app DLL SHA256 `1E4BF6311780D62BEC54A160A5ECF8D5459054A78E0D33889C6515FDDB0996F0`. This is an isolated build receipt, not an installer/release publication. Final native checks and their receipts remain part of the parent delivery pass.

Task B parent verification: final Release app DLL1E4BF6311780D62BEC54A160A5ECF8D5459054A78E0D33889C6515FDDB0996F0 passes actual native realtime-series-state and existing oct20-watched routes (mounted series/season, watched/unwatched/rejection across series/season/episode). Receipts under D:/SiloPlayer/.codex-tmp/server-align-nov01/realtime/native-final-series and native-final-watched. Source binding recorded separately; no product changes after Release build began. Full-suite intermittent update-cleanup setup failure and passing isolated4case rerun remain disclosed above. Independent review and Beta submission pending; no release or setup is claimed.

Task B final sustained-input correction (supersedes the earlier candidate receipts): raw progress ticks continue invalidating caches, while only another coalesced sweep supersedes the mounted detail snapshot. A new three-snapshot delayed-read regression reproduced starvation and now passes. Product commit bc6e466087b88aaac4cc0d3cfde0b4e35a486cad passes89 focused cases and full2832/2832, zero failures/skips, including the previously intermittent unchanged update fixture. New Release DLL66CF8CA5A4C953EA6A7F92C15244C1449887E21E94D011E15B0687798F5C541B passes native-sustained-series and native-sustained-watched against the same output. Receipts under D:/SiloPlayer/.codex-tmp/server-align-nov01/realtime. Previous1E4B... package is preserved pre-correction evidence. Source1.2.519 and published Stable are unchanged; independent review and Beta submission remain next. No setup or installed-player control.

Task B independent review follow-up (base 2431f182; supersedes the candidate readiness statement above): production cache registration still allowed raw ticks to starve shared-prefetch mounted reads, while coalesced generations could discard slow owned reads and lose active episode/catch-up scope. Seven backend-controlled regressions reproduced those cases plus initial scoped null-Item event loss, local final-progress regression and sibling /watch exhaustion. Mounted refresh now owns its catalog transport and may finish while unioned trailing work waits; initial invalidations are retained, and local watched/progress ownership still retires stale snapshots. Aggregate series naming is separated from playable-leaf revisions. Immediate global speculative cache invalidation remains enabled. The preserved 66CF candidate also failed a new actual-control season target availability test; mounted season actions now recompute null/playable transitions. `review-green-focused` passed 122 affected tests after earlier cache-only fixtures were migrated to HTTP backend ownership. Fresh full/Release/native results are recorded in the task report; previous outputs and receipts remain frozen historical evidence. Version stays 1.2.519 and no installation, installed app control or publication is authorized by this verification.

Final review correction verification: `review-full-unit` passes all 2,839 tests, zero failures/skips. New Release DLL `637F11276FDA9055728ED073B76BB9CD317EAA44B210C268F8B73F6A9202DA88` passes both `review-green-season-native` (real series/season controls, including season target null→playable→null→playable) and `review-native-watched` (existing series/season/episode local mark, unmark and rejection). All use the same new isolated app output. `review-final-source-binding.json` records 1,006 tracked product input hashes unchanged since Publish began at 10:02:52Z; receipts retain the then-uncommitted base HEAD2431f182 and the final commit is bound in that manifest. The prior 66CF and 1E4B app outputs remain preserved; no installer or release publication is claimed.

Material barrier follow-up from dc8a7c13: a failed local final-save task remained faulted inside Task.WhenAll and blocked every later remote read, even after a newer successful save. `barrier-red` reproduced canonical aggregate2 remaining0 with actual registrations, both local saves, a raw/coalesced event and an older held episode response. The read barrier now observes write settlement independently of success, retires completed predecessor barriers and rechecks when a newer save joins during an await. Write failure reporting remains with the write owner; local generation/context/navigation guards still reject old snapshots. `barrier-green-focused` passes123 and `barrier-full-unit` passes2,840, zero failures/skips. Fresh app/native/source binding evidence follows in the task report; the 637F output and all earlier evidence remain frozen. Version remains 1.2.519; no installer, installed-player action or publication.

Final barrier source verification: Release DLL `5169E2C32429414CACC83E9BADE6D1D56DD88E54CC106953BC8C304D9DF6810C` passes `barrier-native-series` and `barrier-native-watched` against the same new isolated output. `barrier-final-source-binding.json` records all1,006 tracked product inputs unchanged since Publish began at10:15:36Z (completed10:16:06Z), and binds the final commit after commit. Focused123/full2,840 are current. Older637F/66CF/1E4B outputs remain preserved; no publication or installer is claimed.

## Approved Beta Task E — bounded Home rows (2026-11-01)

Task E deliberately uses Beta base `a15621a0d6d5960d6b31a620a23ae259d734dedb`, whose unchanged source version is **1.2.519**, newer than the historical 1.2.517 context above. Branch `codex/server-home-window` implements advertised first-20 Home row reads and complete recovery against official Silo main `63ef34b236b88dc77acca1a9a3ed90235377691f`. Featured and expanded rows read whole. Stable completion preserves order, removals, duplicate priority, cache ownership and Undo boundaries.

The final full unit suite passed **2,817/2,817**, the fresh Release app publish passed, and final native verification passed. Candidate `SiloPlayer.dll` SHA256: `EAD30AC02DAA9FCF86B32D1724E8D5CF95CBDA35A37A300F03F87FB428BF648B`. The final `home-row-window` runner exercised the new actual carousel/Dismiss/Undo cases and existing Home ownership, hero and presentation regressions against those same published bytes. Receipts are in `task-e/final-full-green`, `task-e/final-release-publish` and `task-e/final-native-control` below the evidence root.

See [Task E handoff](superpowers/handoffs/2026-11-01-bounded-home-rows.md). Detailed receipts and exact committed source identity are under `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-e-report.md`. This candidate has not been pushed, packaged as an installer, published, installed or used to restart the installed player. Historical release receipts remain unchanged.

### Task E review corrections — current local candidate

Independent review of `6a5c331b` identified queued-read ownership, cached hero promotion, stale Undo refresh, mixed metadata-language completion, capability timeout fallback and unseen series-member dismissal gaps. All were reproduced on the frozen earlier candidate, then corrected without changing source version 1.2.519. Queued reads carry their initiating API context and language through context-bound sends; the layout owns featured reads; Undo retires earlier section generations; completion replaces stale-language text and filters the whole-show dismissal predicate across pending reads.

Current verification: **2,819/2,819 unit tests**, fresh Release publish, eight review-path native cases (including the same-account token-refresh negative control), and existing row/current-Home/real Next Up dismissal native suites passed. Current `SiloPlayer.dll` SHA256: `B803FC4EDF0BF79A374D62E71118675B9A92FF72770679B7293D903C4B25F3F3`. Receipts: `task-e/review-full-green`, `task-e/review-final-release-publish`, `task-e/review-session-green`. Earlier `EAD30AC0…` artifacts and receipts remain frozen historical evidence. No remote mutation, installer or installed-player control occurred.

### PR68 cloud follow-up — current local candidate

The four exact cloud findings on reviewed head `abb6223c` were reproduced and corrected: JSON null section responses preserve mounted row/hero fallback, null optional capability documents use uncapped reads, transient capability fallback is shared across pending/queued rows until the next layout refresh, and queued mounted-collection checks run on the UI dispatcher. The existing owner, language, cancellation, generation and reconciliation guards remain in force. Source remains **1.2.519**; parent owns the Beta PR's remote delivery.

Fresh verification passed **2,825/2,825 unit tests**, Release publish and the native `home-row-review` run (11 review cases plus existing carousel, Home ownership/hero and real menu/Undo suites). Current app DLL SHA256: `EBD01E208EAA49DC20869316306EC7C232C51A702400735C63F81F4B4FC26CFA`. Receipts are `home/cloud-full-green`, `home/cloud-release-publish` and `home/cloud-native-green` below `D:/SiloPlayer/.codex-tmp/server-align-nov01`. The same published bytes are verified in the native runner; final source/product fingerprints still match. Frozen `B803FC4E…` and `EAD30AC0…` apps and receipts remain unchanged. Detailed thread dispositions, red evidence, original/final fixture hashes and final commit binding are in `task-e-report.md`. No installer, startup/receiver/updater, real account, user data or installed-player control was used.

## November 1 episode still quick-play candidate

Independent `codex/server-episode-quickplay` candidate starts from newer Beta `5a6555492801aa0172d2559257ef8711a8e0efe4`, with pipeline-owned source stamp **1.2.519**. Official Silo server/WebUI main was freshly confirmed at `63ef34b236b88dc77acca1a9a3ed90235377691f`. The episode grids and sibling cards gain a separate accessible Play/Resume action based on authorized readable-file fields. It reuses normal scoped playback/resume preparation and rejects departed card/navigation/API owners across queued playback gates.

Synthetic local verification: main suite **2,799/2,799**, Release publish successful, and actual-control native run `quickplay/final-native` passed for series/season/sibling builders, keyboard invocation, separate detail navigation, library/profile/resume requests, omitted zero override, readable/missing files, 128/220/400px non-overlapping action rectangles, spoiler blur/overview, stale queued scopes and existing room transport behavior. Release/native app DLL SHA256 is `6D088A603C6C798EDC2296CBEAEA63E2EC09819DBDB7C188B8AC34709C4C1470`; receipts/logs/screenshots are under `D:/SiloPlayer/.codex-tmp/server-align-nov01/quickplay`. Full task report is `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-f-report.md`.

This is a Beta source candidate awaiting independent review and parent-managed delivery, not a release or installer receipt. No installer was built/executed and the installed player was not controlled. Native requests use synthetic in-memory responses and stop at an intentional server denial before media load; real account/media playback and complete visual parity remain unverified.

### Episode quick-play review corrections

The correction pass stacked on `8e6226c31ddc700e8cf28b9a43ebd739cc9dec41` carries the captured playback owner across the transport drain and preparation into context-bound capability/start requests and retries, retires superseded viewer starts, and cleans up accepted cancellation with explicit loading/error surface ownership. Active-room clicks use existing room transport or detail staging/suggestion authority, preserving attachment and guest permissions. Pointer/focus visibility uses their union, and positive rewatch progress is labeled Resume.

Frozen corrected source passed **2,800/2,800** unit tests (`quickplay/reviewed-final-unit`), Release publish (`reviewed-final-app`) and the combined native suite (`reviewed-final-native`, exit0). Actual checks include held transport/watch/capability/start boundaries, resize/navigation cancellation, successor loading/error ownership, an actual manager-created/coordinator-attached room session, guest/host/lobby/vote paths and canceled staging, plus the original grid/resume/geometry/spoiler checks. Corrected app DLL SHA256: `0BD4733B60C71F8BA631773FDF756C6761D422092D81CB55E2FD588CB2C61305`; all managed artifact hashes/native-reference equality are recorded in `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-f-reviewed-artifacts.json`, with exact product source fingerprints in `task-f-frozen-product.json`. Earlier `6D088...` baseline evidence remains frozen and is superseded for the current candidate.

All .NET work used the external mutex runner with fresh worktree-local outputs. The isolated native shell used synthetic in-memory APIs, result-root settings, disabled SiloCast discovery and no updater/installer service. Intermediate failures, including the untouched synthetic updater parent-identity check, remain in the task report; the final complete suite passed. Review P3 click-to-first-frame timing is deferred (no authorization/resume/room-flow impact). Source stamp remains pipeline-owned 1.2.519; no release, installer, production or installed-player action occurred.

## 2026-11-01 — Match / Split library scope source handoff

Task C retains source **1.2.519**, deliberately newer than the historical release context above, on `codex/server-match-scope` from `a15621a0d6d5960d6b31a620a23ae259d734dedb`. Official server/WebUI reference: `63ef34b236b88dc77acca1a9a3ed90235377691f`, including the library-scope behavior introduced by `cb73e114bd09ba0bc748a156f0f129b4bd12822e`. See the [scope specification](superpowers/specs/2026-11-01-match-split-scope.md) and [implementation plan](superpowers/plans/2026-11-01-match-split-scope.md).

Detail Match launches now pass their library. Administrator Match and Split derive choices from complete item file inventory, carry one captured scope through search/selection/apply or preview/commit, and retire stale candidates/plans on membership, selection, profile/account, permission or navigation departure. Curator Match retains supplied/unscoped server resolution without reading administrator inventory; Split requires acting-admin, matching the authoritative endpoint policy. Successful empty inventory preserves the server's unscoped default behavior; failed reads and explicitly departed page scopes block operations. Inventory is revalidated at operation boundaries and through an explicit Refresh command; there is no new periodic poll.

Fresh verification under `SiloOct18Build`: **2,815/2,815 unit tests passed** (`match/full-unit-final`); Release app publish succeeded (`match/publish-final`); actual-control `match-split-scope` plus existing media-tools native fixtures passed (`match/native-final-02`). The native run includes delayed replies/permission revoke-and-restore, Split commit owner departure, 2,500-file virtualization and narrow footer bounds (Split shell 428×626). The same published `SiloPlayer.dll` SHA256 was verified by publish and native receipts: `D92E323C876ECE88FDD0C514B85BCEC064A9460FC310C9E7AACC8C71A2513CB1`.

Full local evidence and the preserved red/green history are in `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-c-report.md`, with receipts below its `match` directory. An earlier full run had an unrelated synthetic update-fixture “Wrong parent identity” failure; the final full run passed it. Existing nullable/compiler analyzer warnings remain documented in the logs. These are local synthetic source checks, not a release or real-server interoperability certification. No installer, real metadata operation, installed-player control, push, merge or publication occurred.

## 2026-11-01 — Match / Split independent review corrections

The first source handoff above remains historical. Independent review at `4a4736b6` identified five supported behavior issues, reproduced by six actual-control cases against that first published app. The corrections separate unmatched Split inventory/selection validity from identity-search eligibility; bind card Match to its launching surface through delayed reads and Apply; use the definitive detail reload after mutation; expose library choices only where they can affect scope; and preserve initial inventory retry with readiness recovery. Split now reuses the canonical captured-context Match search entry point rather than duplicating its route.

Current corrected source has **40/40 focused tests** and **2,817/2,817 full unit tests passing**, zero skipped (`match/review-focused`, `match/review-full-unit`). Its fresh Release publish succeeded (`match/review-publish`), with app DLL SHA256 `BDED834862A2DB9FD4870B6C72B8BDC61D60DB485937A00BD51B354942CF856D`. Product inputs have not changed since that publish; fingerprints are recorded in `D:/SiloPlayer/.codex-tmp/server-align-nov01/match/review-source-inputs.json`.

The full native rerun **passed**: `match/review-native-02`, against `.codex-tmp/match-review-publish-client`. It passed the original scope cases, all six review cases and existing media-tools virtualization/footer checks, with the completion sentinel and the identical `BDED834862A2DB9FD4870B6C72B8BDC61D60DB485937A00BD51B354942CF856D` app DLL hash. The prior recovery-test realization failure was corrected with a bounded row wait in test code only. The native run completed during parent handoff; no further verification was queued. The runner/receipts and detailed supported-finding responses remain in `D:/SiloPlayer/.codex-tmp/server-align-nov01/task-c-report.md`. No further checks are queued by this implementer. No push, release, installer or installed-player action occurred.

## 2026-11-01 — Match / Split spec-axis corrections

The independent spec review at `8a325bd1` identified three further supported lifecycle boundaries: Search could adopt a library/selection changed during its initial inventory GET; card launch could survive same-user curation revocation/restoration during the pre-dialog GET; and inventory pagination could continue after admin demotion. The focused correction captures and validates the click scope before preflight, observes card authorization before dispatch, and cancels permanently retired dialog lifetimes so existing per-page cancellation checks stop the next admin GET. Only MatchItemDialog, SplitVersionsDialog and MediaItemMenu product code changed.

Fresh verification: **40/40 focused tests**, **2,817/2,817 full unit tests** (zero skipped), successful Release publish (`match/spec-publish`), and complete native PASS (`match/spec-native-02`). The native log explicitly contains all six new spec PASS cases, all six prior review PASS cases, the original scope PASS, media-tools PASS and completion sentinel. Publish and native verified the identical app DLL SHA256 `F6AF58939D128D610DA2F2AC7F1F1C5326852BD3D14DFD8466AEB019488DB5E1`. Product inputs are frozen in `D:/SiloPlayer/.codex-tmp/server-align-nov01/match/spec-source-inputs.json`; first and second artifact generations remain preserved. Source version stays 1.2.519. This is synthetic lifecycle evidence; no real metadata operation, publication, installer or installed-player control occurred. Parent reviews this correction source and red/green evidence without another generic review loop.

## 2026-11-01 — PR #67 external inline corrections

All three external inline findings at `27bd4ca2` were reproduced and corrected. Successful empty/no-readable-library inventory now permits unscoped Match even from a scoped detail page; positive membership only in other libraries still blocks that departed scope, and failed reads/permission gates remain blocked. Match also permanently retires when a loaded LandscapeCard or PosterCard changes its bound item, including replacement and restoration during the initial read or Apply. Inventory revisions now compare all six fields of the pinned upstream AdminItemFile contract, so a same-ID/library path, root or episode change updates file rows and retires Split's accepted preview before commit.

Test-first evidence against the preserved `F6AF...` app: five unit regressions and all six new actual-control cases failed for the intended behaviors. Fresh verification via the external `SiloOct18Build` runner: **51/51 focused checks**, **2,823/2,823 full unit tests**, successful Release publish (`match/online-publish`) and complete native PASS (`match/online-native`). Native explicitly passed all six online cases plus the earlier scope/review/spec/media-tools cases and completion sentinel. Publish/native app DLL SHA256: `BF238A9C6D7D9E9ACD8ACE3B7F44F6E9B1B1B451AC55FD571130D18E859DC625`. Only pre-existing compiler/analyzer warnings remain.

Remote Task C head was checked before mutation; official GitHub main remained `63ef34b236b88dc77acca1a9a3ed90235377691f`. Source version remains 1.2.519. Product inputs are frozen in `D:/SiloPlayer/.codex-tmp/server-align-nov01/match/online-source-inputs.json`; report and all earlier artifact generations remain preserved. These synthetic checks do not represent real-server metadata operations or a published release. The implementer commits locally only; parent owns final source review, PR replies/resolution and remote delivery. No installer or installed-player action occurred.

## 2026-11-01 — Concurrent Task C pipeline reconciliation

The remote branch advanced independently to `a1c23df2b5d74b19380ff956540a9e76922498d9` while the verified external correction was local at `0b079803d725a1f4c9df253188f2de41aa9530d6`. Both histories were preserved with a merge. Complete inventory comparison now consumes the pipeline's readable unique snapshot; loaded-card item ownership remains alongside pipeline series/context invalidation. A dispatched Match finishes independently of surface cancellation and invalidates shared source/replacement detail while departed local UI stays retired. Pipeline timeout reporting, Split HTTP search/page fallback, duplicate-row normalization and unmatched preview recovery were retained and verified. No separate seven-finding reviewer discussion was available beyond the pipeline commit message/source/tests and saved three-thread GitHub snapshot.

Fresh merged-source checks: **57/57 focused tests**, **2,829/2,829 full unit tests**, successful Release (`match/merge-publish`) and complete native PASS (`match/merge-native-02`). Native includes the pipeline timeout/unreadable-inventory/repreview cases, original scope/review/spec/online cases, strengthened actual loaded-card rebind cache assertions and media-tools/footer checks. Its first attempt exposed only a fixture querying controls before realization; bounded test waits fixed that without changing published product bytes. Publish/native DLL SHA256: `F95ABBE5B8A9A20154C53873AE82806169951A4B92789E46F561DD7083501600`. Existing warnings only. Product inputs and final source binding: `D:/SiloPlayer/.codex-tmp/server-align-nov01/match/merge-source-inputs.json`.

Source remains 1.2.519. Prior BF238/F6AF and earlier evidence remain frozen. No push/reset/rebase, PR replies/resolution, production metadata, installer, installed-app or real account action occurred in this reconciliation. Parent owns the final remote-head gate and delivery; local synthetic checks establish only this combined source's tested behavior.

## November 1 — marker source/confidence Beta PR candidate

Independent candidate on `codex/server-marker-details`, based on Beta `a15621a0` and official server/WebUI `63ef34b236b88dc77acca1a9a3ed90235377691f`. Source stamps remain 1.2.519; the Beta pipeline owns release stamping. This is not a published release.

Current watch `start_seconds/end_seconds` now deserialize alongside legacy ranges; optional source/provider/confidence/algorithm survive watch and live replacement. Revision24 typed settings expose two default-off switches. Active profile/device/authority reads and serialized setting writes reject retired owners; failed queued changes restore the last accepted value per key. Native skip controls and OSC share the official label vocabulary. A separate escaped cosmetic OSC message preserves countdown, pending seek and undo ownership.

Verification under `D:/SiloPlayer/.codex-tmp/server-align-nov01/markers`: actual wire red/green; Lua3 missing-handler/raw-markup red then escaped rendering/clear-state green; focused18 passed; full-main-final **2817 passed, zero failed/skipped**. Release app publish `corrected-native-app` passed, DLL SHA256 `E2429EC86563B580B441E9FAA29B65AA06C48FABA239930AFC1382BE5D888BF6`. Actual synthetic native controls in `native-corrected` pass setting persistence, queued same/different-key failure rollback, repeated/manual/absent marker data, profile retirement and label rendering. The native screenshot was visually inspected: the details line fits above Skip Intro. The initial native fixture's authoritative-position setup was corrected; its original/final hashes and the reproduced rapid-toggle defect are retained in the task report. No product input changed after the corrected Release build; only fixture checks and documentation followed.

All .NET commands held SiloOct18Build and used fresh isolated outputs after pinned native dependency verification. Known existing warnings remain. Independent Standards/Spec review and Beta submission are next. No installer, installed app, real account/media or production endpoint was used; this does not certify complete visual parity or physical-device behavior.
## November 1 — marker-details review corrections

This follow-up supersedes the earlier task-D readiness statement. Independent Standards/Spec review found cached Settings visits retaining another profile's controls, early edits being dropped under unrelated bulk loading, cosmetic preference reads delaying skip initialization, nested multipart files using the primary file's metadata, timing-only live snapshots losing provenance, and cosmetic republishes clearing undo. These were reproduced against the preserved E2429 candidate and corrected. Settings now owns a separate visit, accepted writes may finish and notify the same current profile after navigation, queued retired-visit writes stop, and optional preference reads do not gate playback. Marker timing and display ownership use the actual selected multipart file; unchanged timing-only broadcasts preserve provenance, while explicit replacements and withdrawals still apply. OSC validates content/file/load/preference generations and compares timing independently of cosmetic payload changes, preserving pending skip/countdown/undo.

Final local source remains version 1.2.519, based on a15621a0 with official server/WebUI 63ef34b236b88dc77acca1a9a3ed90235377691f. `markers/review-full-final` passes 2,823 tests, zero failures/skips. Fresh isolated `review-app-final` publishes DLL SHA256 `9808190E5A84299BDD37067F1324CF22B1B45EABB0C650BB4463F520C3826700`; final native receipts and committed input binding are recorded in the external task report before Beta submission. Earlier E2429 and FD2F3 candidates and all red evidence remain preserved. All .NET commands hold SiloOct18Build, use synthetic fixtures and fresh outputs, and never run setup or control the installed player.

Production video uses the existing Lua OSC. The legacy WinUI PlayerOverlay remains deliberately dormant in normal video playback; its fixture verifies compatibility rendering, not production reachability or complete visual parity. A low-priority English-label-to-kind coupling cleanup remains deferred because no current copy or functional mismatch was found. Beta pipeline stamping, combined QA and prerelease packaging are separate from these local checks; no Stable/main release is claimed.

Final marker review verification: all three fresh native routes passed against the same 980819... app: review-native-final (actual cached Settings controls, nested selected file, nonblocking cosmetic reads, early edits, retired queued writes and accepted off-page notification), review-native-original-final (existing marker preference/rendering compatibility) and review-playback-final (actual libmpv Lua/seek/quality/room-intent checks). The final 2,823-test suite includes all six new timing/provenance/OSC ownership regressions. review-final-source-binding.json records all 1,011 tracked product inputs unchanged since Publish began at 10:17:58Z; its exact final commit is bound after committing. Native fixture-only corrections are separately fingerprinted in the task report. Source self-review and diff checks passed; delivery gates and PR receipt remain in the external report.

## Approved Beta PR3 — bounded busy subtitle reads (November 2, 2026)

Local product source 9e05c6a2fa2b1ae5e19fb21eaa57757a51ef4e4e on codex/nov02-subtitle-busy deliberately starts from freshly selected Beta 6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af. Inherited source version 1.2.544-beta.1 and File/Assembly 1.2.543.1 are unchanged. This is a local source handoff, not a release or installer receipt.

Subtitle HTTP 503 reads honor delta/date Retry-After with at most 4 attempts and an 8-second asynchronous read budget. Busy history never falls through to synchronous remote sub-add. Manual selection and per-file read owners are separate: failed attempts retain the actual applied choice/preferences, compatible reloads re-resolve the latest pending choice, and newer choices including same-track ABA retire older completions. Text readiness precedes burn-in removal; same-file/stable-track/sync-key/context local bytes can cross that specific load once. Manager convergence retains original recorded identity across changed-file grants.

The server may commit a changed-file replacement and retire its predecessor before returning its grant. Preserve that new server revision; its actual captions are Off until its sidecar is applied, busy failure offers retry guidance, and an unapplied choice never reports preference success. This does not certify old burned-stream survival across a server-side replacement. See the scoped plan and design implementation note.

Fresh verification: focused 95/95; full 3075/3075; successful self-contained Release publish; native busy 13, ownership 29, accepted-reload 4, burn-readiness/changed-grant 4, and existing multi-step actual-SID subtitle-adoption fixture passed. Every final native receipt targets SiloPlayer.dll SHA256 04512FDFB136C2B138506E1725662F206F011C9D8229179DF32F9E3085E4D5E8 from .codex-tmp/subtitle-busy-converged-publish-client. Evidence: D:/SiloPlayer/.codex-tmp/server-align-nov02/subtitle-busy-report.md and subtitle-busy/{converged-publish,converged-focused,converged-full,converged-busy,converged-ownership-two,converged-accepted-two,converged-readiness,converged-continuity}. Existing compiler/analyzer warnings remain. All .NET verification held SiloOct18Build after pinned mpv hash 04E4D27DC385DC18DDD93FBE69C75E635B864F3C6EC53A9122DF54C2F7842F4A verification.

Official playback source remains pinned to bf53b65450244751013a2965b39da4111d9a0f72. Root later observed 90f88f2d6076daefb8b5ae77893a41e9de6e20d9 calendar-only drift with unchanged playback/subtitle contracts; the refresh receipt is preserved. Identity PR75 concurrently advanced to 09572257348eb6237eca6e78e0fe48894f1c6683 and has not been imported. Independent Standards/Spec review and combined validation against the corrected identity source that actually reaches Beta remain required before pipeline readiness. Tests use synthetic data/local HTTP and actual libmpv/UI dispatcher, not live server/account interoperability or complete WebUI visual parity. No push, merge, installer, setup, installed-player control, production change or version stamp occurred. Earlier outputs/evidence and canonical dirty source remain preserved.


## November 2 subtitle identity candidate — 1.2.544-beta.1

Focused product/test source `803cf6ab2f9ad94bd2ea4956fe59f50a73db395d` on `codex/nov02-subtitle-identity` starts from freshly fetched Beta `6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af`; source version remains **1.2.544-beta.1**, owned by the Beta pipeline. All final source/build work uses `D:/SiloPlayer/.codex-tmp/server-align-nov02/worktrees/subtitle-identity`. Official server/WebUI main was pinned and rechecked at `bf53b65450244751013a2965b39da4111d9a0f72`. See the [approved scope](superpowers/specs/2026-11-02-server-parity-design.md) and [focused execution record](superpowers/plans/2026-11-02-subtitle-identity.md).

The player retains absolute subtitle identity and unavailable metadata, maps unique loaded container IDs or trustworthy FFmpeg indices to actual mpv SIDs, and uses a unique legacy metadata match or existing sidecar when identity is absent. Unavailable rows remain visible and refuse automatic/manual/service/Lua actions. An unresolved native choice without a supported sidecar keeps the previous SID, active highlight and preference and displays actionable feedback. No native/HLS capabilities are newly advertised. Busy sidecar503 recovery remains the separate PR3 scope.

Historical synthetic verification for source `803cf6ab` reported **3,074/3,074 unit tests**, **105/105 focused tests**, a self-contained Release publish, six combined native fixture groups (identity, no-route refusal, subtitle adoption/source restoration, burn-in switching and room authority/timing), and **27/27 sidecar ownership scenarios**. The sidecar result is superseded by the later pipeline-baseline comparison below; it is not a passing result for the current PR head. Actual bundled Lua tests check rendered Unavailable text and keyboard/pointer refusal at 960x550 and 1920x1080, plus server-choice caption cycling. Native media tests exercise an index-only gap, actual Matroska IDs, external/downloaded sidecars, forged callbacks, retained highlight feedback, canonical position and viewer pause. The final focused run includes the final test-only rendered-label assertion. Existing compiler/analyzer warnings remain.

Final diagnostic app: `.codex-tmp/subtitles-d-final-release-client`; `SiloPlayer.dll` SHA256 **A9C7A3E564E5BAC7EB19006A30ACB175168AF8289BF960D777D9CFAAC1835785**, PRI **4493960CADFDB276BB8E2FE225B227B8E4805E0A9242D6E87AD0AD4A6D7406F1**. Both final native runs verify those same app bytes. Receipts: `D:/SiloPlayer/.codex-tmp/server-align-nov02/subtitles/{d-final-full,d-final-focused,d-final-release,d-final-native,d-final-sidecar}`; source binding, red/green commands and exact final head are in `D:/SiloPlayer/.codex-tmp/server-align-nov02/subtitle-identity-report.md` and the adjacent source fingerprints. Every .NET command held `SiloOct18Build`, and the hydrated pinned engine SHA256 matched `engine.json`.

Earlier source-switch filtering initially lost the viewer choice when returning from an alternate edition; full-suite and frozen native regressions reproduced it before correction. The first D diagnostic app (`6ED836E6...`) and all prior receipts remain frozen historical evidence. Disk-full, interrupted queued jobs and fixture setup failures remain explicitly recorded rather than counted as product verification. Original C source/evidence was preserved, with source-copy hashes recorded before resuming on D.

This is a local candidate awaiting parent-managed independent review and Beta PR delivery. No version stamp, installer, release publication, real account, production state or installed player was changed. There is no forced injection of rejection for an otherwise valid native SID; the code checks the resulting SID and has supported sidecar fallback, while actual missing-identity fallback is exercised. Synthetic evidence does not certify real-server/hardware interoperability, the historic buffering issue or complete visual parity.

Independent PR2 review at 31abdab: Standards **0 findings**, Spec **0 findings**. The reachable production Lua menu consumes complete subtitle inventory; the historical collapsed WinUI overlay is not claimed as a newly verified production surface. Product source remains 803cf6ab; all final Release/native checks use A9C7A3E564E5BAC7EB19006A30ACB175168AF8289BF960D777D9CFAAC1835785. Busy-sidecar recovery remains separate PR3. Beta pipeline owns subsequent corrections/merge/stamping/packaging.

## November 2 PR75 language-identity companion

Local companion product/test source `6a7119527b241371393e763d06e610da747c3c26` on `codex/nov02-identity-companion` starts from exact pipeline source `09572257348eb6237eca6e78e0fe48894f1c6683`, with unchanged version **1.2.544-beta.1**. It corrects the pipeline Spec review's P2: legacy native matching now normalizes the primary ISO alias while retaining explicit BCP47 region/script/other subtags. Requests such as pt-BR versus pt-PT, zh-Hant versus zh-Hans, and en-GB versus unqualified eng refuse native substitution and use an authorized sidecar or retain the previous route. Global language preference normalization and authoritative container/FFmpeg-ID matching remain unchanged; the P3 pending-preference duplication advisory is deferred.

Six pure mismatch cases and actual native en-GB-to-eng substitution reproduced before correction. Fresh focused **56/56**, three maintained source-inspection fixtures **3/3**, full unit **3,096/3,096**, Release publish and seven combined actual native fixture groups pass, including pipeline unavailable/deferred execution/failed-sidecar/SiloCast, regional sidecar fallback/no-route retention, adoption/burn-in and room authority/timing. Three source-inspection expectations were updated to the pipeline's new boolean/executed-selection signatures and restoration guard; no additional product behavior changed.

Final diagnostic app `.codex-tmp/identity-companion-corrected-release-client` under `D:/SiloPlayer/.codex-tmp/server-align-nov02/worktrees/identity-pipeline-review` has DLL SHA256 **7E9E70DCD60A3D6ACBC6EE29A0966F7CE3356F435302B0C7C4D34F7EF544BBE0** and PRI **4493960CADFDB276BB8E2FE225B227B8E4805E0A9242D6E87AD0AD4A6D7406F1**. Every .NET operation held SiloOct18Build after hydration/hash verification of the pinned engine. Product input remains frozen; subsequent changes are test expectations and documentation.

The historical companion comparison recorded **22/27 on both** frozen pipeline095 baseline (DLL97D6473D...) and corrected bytes, with exactly the same five failures: reload-gap, manual-reload-gap, loaded-settings-cached-track, loaded-settings-off and load-start-retired-bitmap. Four failed to establish the intended timing/private-pending precondition; the retired bitmap settled on the correct latest Off/native/manager state but expected a preference for an unapplied retired choice. This supersedes the earlier 27/27 claim for that comparison, not a result for subsequent PR heads.

PR75 fixture reconciliation: all five cases remain enabled in `tests/SiloPlayer.NativeRegressionTests/SubtitleSidecarOwnershipNativeFixture.cs`, without skips or expected-fail exclusions. The gap cases now hold a real replacement media response before FILE_LOADED, check that retired work cannot add/register/publish/save, then require the replacement to execute the retained choice (saving only a current manual preference). The retired-bitmap case requires the latest Off preference and explicitly forbids saving the unexecuted bitmap choice. The loaded-settings cases now establish the prior choice through the real picker/manager and wait for its actual SID while the optional settings response remains held; entering that concurrent settings request alone is not proof of subtitle execution. Unit source-inspection expectations also follow concurrent settings publication and the bounded streaming sidecar downloader rather than the obsolete serial order/client/buffer. Pipeline reruns of the unit suite and the complete sidecar-ownership fixture are still required; no build or test was run for this reconciliation, and no current green native result is claimed.

Receipts, red/green commands, exact source/artifact binding and baseline classification are in `D:/SiloPlayer/.codex-tmp/server-align-nov02/identity-pipeline-companion-report.md` and `identity-companion/ownership-baseline-classification.md`. Official local source is `90f88f2d6076daefb8b5ae77893a41e9de6e20d9`; playback inventory/policy/protocol have no diff from pinned bf53b654. No remote operation, version/installer/publication, production/account, installed-player or original PR2 artifact change occurred. Parent owns scoped rereview and a fresh pipeline-head/thread gate before any FF-only PR75 update; changed remote source must be preserved. Synthetic evidence does not establish live interoperability or complete visual parity.

## November 2 combined subtitle busy / corrected identity candidate

The locally authorized reconciliation combines original busy head `5f7497fab06e1480f6ccb886d259b19de47e2919` with full corrected identity head `0c3fc46241b344fb5dd519c0b6848fc23cd8c585` (pipeline `0957225`, companion product `6a7119527b241371393e763d06e610da747c3c26`) on the D-only `codex/nov02-subtitle-busy` branch. Inherited version **1.2.544-beta.1** / FileAssembly **1.2.543.1** stays unchanged. Both original candidates and all their evidence remain historical; this section records combined verification rather than relabeling either earlier result.

One manual operation now survives accepted queued returns, with a single preference claim consumed only after verified actual native/sidecar/Off/burned execution. Immediate selection and FILE_LOADED share completion; a newer manual owner supersedes either callback, including ABA. File reads cancel independently while compatible latest intent survives. Pending choices do not establish the active native highlight. Full corrected PR75 actual SID, namespace, unavailable, regional, Lua and SiloCast refusal behavior is retained alongside bounded503 reads, same-file one-use ready carry and original-identity replan convergence. An exhausted initializer retires only its manual save authority; a later automatic selection from the still-valid manager grant can apply without saving, while a fresh explicit retry owns its own successful preference.

Fresh combined verification passed **3,113/3,113 full unit tests**, **287/287 focused tests**, self-contained Release publish and six final native routes: compatibility (seven groups), sidecar ownership (29 cases), busy (13), actual loading/ABA/missing-native503 lifecycle (five), burn readiness/changed-file owner (four) and accepted reload/newer-choice (four). The29-case suite establishes real pre-FILE_LOADED media boundaries where PR75 changed optional-settings ordering; all ownership assertions remain. The accepted negative control now issues newer Off before actual execution, keeping its strict single-write assertion. The old standalone22/27 result above remains preserved with its original scope.

All final native runs bind the same app DLL SHA256 **51ADDDB8C52D2155D0950B320A2311D280BFA082F63257E61D7EFDF160ED6272**, PRI **4493960CADFDB276BB8E2FE225B227B8E4805E0A9242D6E87AD0AD4A6D7406F1**, with1035 product/dependency inputs unchanged since the prepublish fingerprint. Native fixture-only completion controls have separate final hashes. Every .NET command used `SiloOct18Build` and fresh outputs after pinned engine verification. Full source/parent/receipt binding and red history are in `D:/SiloPlayer/.codex-tmp/server-align-nov02/subtitle-busy-reconciliation-report.md` and `subtitle-busy/reconciliation-source-binding.json`.

The server grant-boundary limitation in the approved spec still applies: a committed alternate-file replacement may retire its predecessor; keep its valid revision, expose actual Off on busy failure and retain old saved preferences, without promising old burned pixels survive that commit. Existing warnings and synthetic/live-server/hardware/complete-visual-parity limits remain. Independent combined Standards/Spec review and remote-head reconciliation before delivery belong to the parent. No push, PR, installer, publication, account/production or installed-player action occurred in this local reconciliation.

### Combined busy-subtitle candidate: independent review complete

Reviewed code head af10fa8969dfa59ba9b98d4a220aa020eb5eb284 contains corrected identity0c3fc46241b344fb5dd519c0b6848fc23cd8c585. Independent combined Standards and Spec both report0findings. Final local verification:3113full/287focused tests, fresh Release and all six relevant native routes, including29ownership/13busy/5actual-loading/4readiness/4accepted controls plus7identity compatibility groups. Native runs bind identical DLL51ADDDB8C52D2155D0950B320A2311D280BFA082F63257E61D7EFDF160ED6272. Product1035input hashes remain unchanged. The accepted-newerOff fixture timing correction retains the strict[-1]assertion and is scoped/reverified separately; original/final hashes and all failed/diagnostic runs remain preserved. The old standalone identity22/27 limitation is historical, not relabeled green; the combined actual-load ownership29/29 is separate evidence.

Parent may submit this complete tested stack as a separate Beta PR dependent on75 without waiting for the pipeline to merge75. Pipeline owns freshBeta reconciliation, re-review, merge, stamps and packaging. No directBeta/mainmerge, installer or installed-player control is performed. Exact receipts and refs: D:/SiloPlayer/.codex-tmp/server-align-nov02/subtitle-busy-reconciliation-report.md and subtitle-busy/reconciliation-source-binding.json. Synthetic checks do not certify live hardware, historical buffering or complete visual parity.

Independent review and correction: Spec **0 findings** at b6a7833e; Standards' sole P3 duplicated spoiler predicate is consolidated at 0f17cf7bec051ca4b2b2e213b0883148d4ade8f2 and its scoped re-review has **0 remaining findings**. Fresh corrected full suite **3077/3077**, Release and complete episode-overview native checks all pass. Final app SHA256 **1547F5A9EAC3CB7D8F640F617639E3B04A896CD6B994096D8FF4D93F7688289C**; compact/wide captures inspected. Evidence overview/{d-review-full,d-review-publish,d-review-native,review-source-inputs.json}; original/final source hashes retain the earlier C59398 candidate separately. Version stamps and verification limits above remain unchanged; Beta pipeline owns subsequent merge, versioning and packaging.

## Approved PR7 — grouped Library local candidate (2026-11-02)

Branch `codex/nov02-grouped-library` deliberately uses fresh Beta `6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af` plus independently reviewed foundation PR74 `abba1d7655128f090f96a26685ef114b7f07b706`, combined locally at `48e04f74ca58347c43e1a0d23432689cff7af2ca`. The scheduling ruling permits local preparation; submission/readiness remains blocked until foundation actually reaches Beta and parent reconciles fresh Beta. Official GitHub main was fetched directly and remains `bf53b65450244751013a2965b39da4111d9a0f72`. Version stamps remain **1.2.544-beta.1**, File/Assembly **1.2.543.1**.

Enabled movie/mixed libraries with advertised `box_sets` use the server's `group=box_set` across GET, large POST, counts, cursors/seeks, query/window identity and matching alphabet requests. Grouped letters require both `box_sets` and `letter_index`. Typed `MediaItem.BoxSet` already exists in the foundation and is verified through actual wire reads; this source has no separate `CatalogItemV2` adapter. The custom virtual Library poster grid now uses the reusable aggregate card, returned-library detail navigation, fallback artwork/progress and bounded responsive slots. Ordinary title menus, watched/favorite mutations and playback intent cannot receive aggregate IDs. Grouped windows retire on library disable/replacement and refresh through existing catalog/watched/final-write mechanisms.

Test-first evidence is preserved under `D:/SiloPlayer/.codex-tmp/server-align-nov02/grouped-library`: request/model RED **7 failed / 6 passed**, actual virtual-card RED against frozen foundation DLL `6CBA330…959FB`, and refresh RED **1 failed / 13 passed**. Focused GREEN **107/107** includes existing alphabet, window, cursor and prefetch cases. Fresh Release publish succeeds; candidate `.codex-tmp/grouped-library-release-one-client` DLL SHA256 is `051BFFA816563C632821B7D559D88FCFFCBA4D46B6B3141D08AE40CB1AD751A4`, PRI `A73C052A29E7A1DDA777A9E97044D90CA264C3EE1158F05729507C0B212047AF`. That initial publish remains frozen historical evidence; the final verified candidate and source binding are recorded below and in `grouped-library-report.md` outside the checkout.

All .NET checks hold `SiloOct18Build` and use the pinned `04E4D27D…` engine with fresh isolated D: outputs and synthetic fixtures. Earlier artifacts, canonical dirty source and the installed player are preserved. No installer, remote submission, release publication, real account/media or installed-player control occurs in this task. Parent owns independent review and delivery gates; this is not release or real-server interoperability evidence.

Final PR7 verification: **3,108/3,108 full unit tests**, zero failures/skips, successful fresh Release publish and complete combined native PASS. Final app is `.codex-tmp/grouped-library-release-pixel-final-client`; DLL SHA256 `C4C79475EB6A3BA658A58F4123CDA00AA42238C09F3409531D41F7902CBBC790`, PRI `A73C052A29E7A1DDA777A9E97044D90CA264C3EE1158F05729507C0B212047AF`. `full-pixel-final`, `release-pixel-final` and `native-pixel-final` receipts bind those results. `pixel-final-source-inputs.json` and the full published-file manifest are preserved externally; product and fixture fingerprints match after verification.

The final native run includes aggregate keyboard/destination/fallback/recycling and120/170/240-DIP slot bounds, grouped movie1280×1000/mixed960×550 exact distant and bottom-letter jumps, reverse filtered requests, settled watched/final-write counts, library disable, old ordinary movie/audiobook letter cases and three existing cursor geometries. Both captures were inspected after all card bindings settled. Two source corrections were test-first: same-object library disable during capability loading, and an inherited asynchronous ChangeView stale-offset read that lost the final chosen letter. Compact inspection also reproduced fractional row-boundary mis-highlighting; only current-letter row selection tolerates **0.5 logical WinUI DIP**. Virtualization, server seek positions and scroll geometry do not use that tolerance. Native evidence covers the current host configuration, without certifying alternate DPI behavior. All earlier failed/intermediate candidates and fixture generations remain frozen and explained in the task report. Parent independent review, actual foundation-in-Beta and fresh reconciliation gates remain required before submission.

### PR7 independent Spec correction — grouped cursor recovery

The reviewed `c9d269a4` / `C4C79475…` candidate above remains frozen. Independent Standards review found no breaches; Spec identified invalid-cursor recovery starting a new query and retrying the core loader before restoring grouping. The focused correction resolves grouping and checks the recovery query owner before the existing one-bound retry. A retired window releases its loading reservation while the optional grouping preflight runs. No other product code or version stamps changed.

Real signed HTTP RED reproduced missing group on GET/large POST retries and counts, plus premature dispatch before held preflight ownership checks. The actual native RED reproduced the grouped-to-flat query fingerprint after a retired cursor. Final verification: **115/115 scoped**, **3,115/3,115 full units**, successful fresh Release and complete native PASS, including injected cursor retirement followed by grouped count/index and far-letter/continuation paths in both layouts. The prior ordinary letters/cursor matrix remains passing. Original fixture/setup-error receipts are preserved and distinguished in the external task report.

Corrected publish `.codex-tmp/grouped-library-recovery-publish-client` has DLL SHA256 `713FEA7F392106741640B81094DA28C36FD4F468A29224ECE7F2AA310E3E294C`. Evidence: `grouped-library/{recovery-red-two,recovery-native-red-two,recovery-green-two,recovery-full,recovery-publish,recovery-native-green}` below the November2 evidence root. `recovery-source-inputs.json` freezes all product/fixture inputs; final committed binding and app manifest are recorded externally. These checks remain synthetic, mutex guarded, local only and subject to parent scoped re-review and the unchanged foundation-in-Beta/fresh Beta delivery gate. No installed-player, setup, production or remote action occurred.

### PR7 corrected-foundation reconciliation

The verified grouped cursor-recovery head `d97666f22e44987c65ba78f066888cb4f892d9cd` locally incorporates complete foundation companion `7617ddf2d5a90b5ea0a0e9792c3e8e95326a8123`. The two merge conflicts retain both appended development histories and separate native fixture dispatch routes. Grouped Library production files are unchanged from d97666f2; incoming unscoped discovery and lifecycle/capability guards remain intact. Official GitHub main was freshly fetched at `90f88f2d6076daefb8b5ae77893a41e9de6e20d9`.

Fresh combined verification: **181/181 focused**, **3,146/3,146 full units**, successful Release and complete native `grouped-library`, `boxsets` (including unscoped) and `boxset-room` passes. All native suites use the same `.codex-tmp/grouped-library-reconcile-publish-client`, DLL SHA256 `259A93A16CB30FE0FD6F2E5D6266581FC7E9A4B0C650B3AFE684700702CA13CD`. Wide/compact grouped captures retain settled counts, correct M highlight and fitting cards/rail. External `reconcile-source-inputs.json`, `reconcile-app-manifest.json` and committed binding record exact source/artifact equality; all earlier evidence remains frozen.

Parent permits a complete independently reviewed/tested local stack before foundation reaches Beta and owns independent review, pending additional Play from Beginning library-owner dependency and remote delivery. This reconciliation does not authorize a push, PR, installer, release or installed-player action. Source stamps and synthetic/native interoperability limits remain unchanged.

### PR7 final restart-menu dependency reconciliation

The grouped branch now incorporates foundation restart-menu product `64bd63e00231fa93e5d23ea33d282f3be01679a5` and fixture-only follow-up `620cd53660b80b4200751023e4c91ad4cd4dca1d`. Its only product change above0db939a1 is MediaItemMenu.cs: Play/Listen from Beginning preserves the captured optional member library and rejects a retired viewer, unloaded/re-scoped owner or recycled item. Grouped scope, cursor recovery and lifecycle files remain unchanged. Native dispatch retains the grouped and both new menu routes.

Final combined verification: **192/192 focused**, **3,146/3,146 full units**, fresh Release and complete native grouped, menu-scope, menu-owner and foundation/unscoped passes. The actual popups preserve dedicated/membership library73 and unscoped omission, authorized file/profile and explicit zero restart. Recycled/re-scoped/departed/profile-changed menus dispatch no preparation/start; same-viewer access refresh remains usable. The grouped matrix retains exact far-letter/cursor recovery/count/index, settled counts/highlight, disable and ordinary alphabet/cursor cases. Owned app `.codex-tmp/grouped-library-menu-publish-client` DLL SHA256 is `810A5C4192CE51115DE352A94F66721727137D9A555BAEB6AA00535E692F2D44`.

The first menu full run's sole obsolete call-text assertion failed and is preserved; dependency620 adjusts that assertion and native test timing/access-refresh coverage only. Product fingerprints remain identical to the successful fresh publish, so no duplicate app build follows this fixture-only change. `menu-final-source-inputs.json`, `menu-app-manifest.json` and final committed binding record source/artifact equality and original/final fixture hashes. Existing room-native evidence is retained specifically from the prior259A app, with exact unchanged route/fixture hashes in `menu-room-route-reuse.json`; no room-native rerun on810A is claimed. Earlier candidates, failures and frozen evidence remain preserved.

All checks use the pinned engine, shared build mutex, isolated D: outputs and synthetic fixtures. Source stamps remain1.2.544-beta.1 /1.2.543.1. Parent owns final foundation review, independent reconciliation review and remote delivery. No remote, installer, credentials, production or installed-player action occurs.

### PR7 captured restart identity admission reconciliation

Clean product integration `649729ed3ad4b3dadcce50bd09d0ca44f1400f64` combines grouped head7e031914 with reviewed foundation product `37cc69ef46c60bdd5fd166e4c50575a81e1db97f`. The incoming menu and PlayerService changes retain the accepted ordinary restart's viewer identity across authority/request admission and preparation while keeping ordinary queue/close/active-room behavior separate from scoped quick-play supersession. Both appended documentation histories and native dispatch intents are preserved. Grouped query, recovery and lifecycle inputs remain unchanged.

Fresh combined verification passes **538/538 focused**, **3,146/3,146 full units**, Release and all eight native routes on identical DLL SHA256 `433429F51ACFF06A54F3A784BFA7743FB9C3EEDBB7DA84A3BAD444B354D2BA3D`, owned `.codex-tmp/grouped-library-admission-publish-client`. Routes cover grouped/ordinary alphabets and cursors, new menu admission and lifecycle controls, existing menu scope/owner checks, foundation/unscoped navigation, full episode quick-play lifecycle/room transport and box-set room actions. All five held admission boundaries reject departed viewers; same-viewer access refresh, accepted unload, active room, queue behind scoped preparation and ordinary close retain their expected behavior. Fresh wide/compact grouped captures retain activeM, current watched counts and fitting cards/rail. Earlier room evidence does not substitute after the PlayerService change; both relevant room routes ran on4334.

The first full quick-play run was blocked in five fixture setup calls using nonpublic reflection for the already-public SetState method, also confirmed in the original Beta source. Central fixture-only `abc34b8b8cbaa353b332213404a00e6af0f28031` replaces those five calls with direct public setup; no assertions change. Corrected full quick-play passes on the unchanged app. Original failed run/fixture, raw hashes0A4388FB...→97D036D5... and Git blobs are preserved externally. `admission-fixed-source-inputs.json` proves one changed native fixture and no product/libs changes; the seven unaffected native routes and unit results remain valid without repeats. Final committed binding and full app manifest record exact equality after commit.

All original candidates, receipts and source copies remain frozen. Version stamps remain **1.2.544-beta.1** / **1.2.543.1**; official GitHub main was freshly confirmed at90f88f2d6076daefb8b5ae77893a41e9de6e20d9. Verification uses the pinned engine, shared mutex, isolated D: outputs and synthetic APIs/settings; no release, setup, real credentials/media, production, remote or installed-player control occurs. Parent owns final independent integration/dependency review and delivery gates; synthetic/host-DPI limitations remain.

### Final grouped-library candidate: independent review complete

Reviewed integration649729ed3ad4b3dadcce50bd09d0ca44f1400f64 and fixture-inclusive checkpointd1ebb3591a4c17edc4159c110ad655a0e8a1907d retain all grouped cursor recovery, scoped alphabet/filter/count and aggregate navigation behavior over corrected PR74 product37cc69ef46c60bdd5fd166e4c50575a81e1db97f. Both scoped source integration reviews report0findings; the inherited queued restart identity gap is resolved by exactly that reviewed dependency. Shared fixtureabc34b8b8cbaa353b332213404a00e6af0f28031 has0sourcefindings on both axes and preserves every assertion.

Final538focused/3146full units, fresh Release and all eight targeted native routes pass on identical DLL433429F51ACFF06A54F3A784BFA7743FB9C3EEDBB7DA84A3BAD444B354D2BA3D. Routes cover grouped/cursor/alphabet controls, restart admission/lifecycle/scope/retired owners, complete foundation/unscoped detail, full quick-play and room primary behavior. The corrected full quick-play rerun executes all17lifecycle regressions; original setup failures and exact original/final fixture hashes remain preserved. Every final mode uses the new4334app, including room; earlier room reuse is historical only. Parent independently rehashed1650source/test inputs and574published files without drift.

Final reviewed delivery binding and exact heads: D:/SiloPlayer/.codex-tmp/server-align-nov02/grouped-library/final-reviewed-binding.json; full history/receipts grouped-library-report.md. This complete tested branch can queue after PR74; Beta pipeline owns fresh-base reconciliation/re-review/merge/stamps/packages. Source versions unchanged, no Stable/main merge, installer, production account or installed-player control. Synthetic tests do not certify live interoperability or complete visual parity.

### Account-limit decision adaptation review correction

The independent Spec review reproduced a pre-existing adapter substitution that changed missing/blank account-limit server text into generic route advice before the new mapper saw it. Sixteen actual synthetic HTTP start/replan cases now cover null, empty, whitespace and padded server messages for both codes; twelve fail for the intended fallback before the correction and four custom-message cases pass. The adapter now retains an absent message for these two reasons, preserving other terminal behavior. Corrected focused suite is 61/61 and fresh D: full suite is 3,084/3,084 with zero failed/skipped (limits/d-full-final). Independent Standards and Spec correction reviews both have zero remaining findings. The earlier3,068 result above belongs to the pre-review candidate, not this corrected source. No app version changes, installer or installed-player control.

## November 2 — motion-aware artwork candidate (Task 3)

Branch `codex/nov02-artwork-motion` starts from Beta `ac51554902c252814191dd88cf4b1d991efdfea6`, retaining pipeline-owned version **1.2.544-beta.1** / Windows **1.2.543.1**. A per-viewport policy pauses new library/shared-card artwork work during rapid movement, preserves cheap decoded hits, and resumes after slow movement or settling. Library slots and background warmup retire on hidden-tab/departure; retained DTO artwork keeps its original profile owner across unload/reload. Direct audiobook group return resumes the existing books-grid slots. Home first-content publication gains no await or barrier. See the [Task 3 specification](superpowers/specs/2026-11-02-artwork-motion.md) and [plan](superpowers/plans/2026-11-02-artwork-motion.md).

Final verification: **3,449/3,449 unit tests**, zero failed/skipped (`artwork/completed-full`), successful fresh Release publish (`completed-publish`), complete new artwork/group-return native suite (`completed-artwork-motion`) and existing movie/series/audiobook cursor suite (`completed-library-cursor`). Final `SiloPlayer.dll` SHA256: `8E93E5357C2719E9E04B4092B89F01462CD6E26F75FA559B8B24E4C120EEA275`. All 1,623 source/test inputs remain unchanged through final native verification; 804 published PDB source checksums match. Exact source/artifact/fixture binding and commands are in `D:/SiloPlayer/.codex-tmp/browse-polish-nov02/artwork-task-report.md` and `artwork/completed-bindings.json`.

Broader existing detail-card, shared-control and Home lifecycle/cache suites passed on the preserved `AC9ECB0D...` candidate. Its only product difference from the final candidate is the one-line audiobook group-return activity update; those broader receipts are not claimed as reruns on final bytes. Existing `LibraryLettersNativeFixture.cs:86` and `HomeRowWindowNativeFixture.cs:464` failures reproduce identically on the unchanged Beta baseline and are disclosed, with original fixtures intact. The default native suite was not claimed passing.

Observed fast-motion measures are HTTP requests, new `Image.Source` assignments and `ImageOpened` events. They do not establish fresh CPU/GPU decoder invocation counts, physical-input smoothness, FPS or a historic buffering fix. PR80 `11058ededd75a8e4b4dcd5c20132dc130835b3bf` uses bespoke sibling episode still cards under its `ScrollView`; those remain outside this shared-card scheduler. No version stamp, installer, installed-player control, production change, push or release publication occurred. Parent owns independent reviews and Beta delivery.

### Task3 artwork review corrections — local verification

Fix round1 against6e905f6e adds retained Recommended shared-card visibility/navigation cancellation and current-bind resume, plus context/cancellation/motion gates between spoiler file/native processing stages. A native IsLoaded guard covers the queued-Unloaded interval; Loaded retries a delayed initial missing source with its original DTO owner. Version remains 1.2.544-beta.1 /1.2.543.1; no push/publication/installed-player action.

Final fresh Release DLL SHA256 A900E4CD065D2D4D80742DC8EA3E77E591032A79721D76C7EE45B4AA43618BFE. Exact frozen app: D:/SiloPlayer/.codex-tmp/browse-polish-nov02/worktrees/artwork/.codex-tmp/artwork-review-ready-publish-client. Five review-ready native groups pass: artwork-first-mount, artwork-motion, oct24-home, shared-controls, oct20-detail-cards. review-final-full passed3449/0/0 before only the final two-control Loaded retry and native-fixture additions; Core/Player/unit inputs unchanged. The earlier0E4E Home line177 failure is a corrected first-mount regression, distinct from the two historical baseline failures recorded above.

Source/artifact/fixture hashes, exact commands, red/green receipts and limits: D:/SiloPlayer/.codex-tmp/browse-polish-nov02/artwork-task-report.md and artwork/review-ready-bindings.json.1625 source/test inputs unchanged through final native checks;808 local PDB documents match; all final native reference bytes match. Observed native API-completion continuations are not CPU/GPU decode counts or FPS evidence. Independent scoped re-review pending; P3 helper naming suggestion deferred.

## Approved PR6 — box-set foundation (2026-11-02)

Branch `codex/nov02-boxset-foundation` starts from freshly fetched Beta `6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af`. The inherited header above is historical: this deliberately newer source retains **1.2.544-beta.1**, File/Assembly **1.2.543.1**. No version stamps, installer or release assets change here. Official viewer reference is `bf53b65450244751013a2965b39da4111d9a0f72`.

PR6 adds typed box-set reader contracts and viewer flags, a dedicated native destination, reusable aggregate cards, and the independent movie membership row. The server's release order and returned library own member navigation and first-unplayed/all-played normal movie playback. Aggregate IDs have no title menu, personal-list or watched mutation path. Optional row failures remain hidden; profile/item/library/language departure retires pending results. Watched-state, completed playback writes, library flag and metadata-language changes refresh the relevant surfaces. Grouped library requests and search discovery remain PR7/PR8.

The active checkout and all new outputs now live under `D:/SiloPlayer/.codex-tmp/server-align-nov02/worktrees/boxset-foundation`. Earlier C: source and outputs were preserved when the owner requested the established D: project location. Byte-for-byte copy verification is in `D:/SiloPlayer/.codex-tmp/server-align-nov02/d-source-copy-receipt.json`. Original behavioral red and intermediate receipts remain under `D:/SiloPlayer/.codex-tmp/server-align-nov02/nov02-boxsets`; final D: verification is recorded in the task report at `D:/SiloPlayer/.codex-tmp/server-align-nov02/boxset-foundation-report.md`.

This is a local source handoff, not release or live-server interoperability evidence. All verification uses the pinned native dependency, `SiloOct18Build`, isolated outputs and synthetic account/settings data. No setup, real credentials, production access or installed-player control is authorized or performed. Parent task owns independent review and Beta PR delivery.

Final review corrections and verification: product source is `b02085084225e12952184b9e62c50bd1ceeee6ed`; fixture source is `eb4f104c46b363630a3555012a96db0ce8a062d3`. A real native `Frame.Navigate` regression exposed missing code-only page activation metadata; the minimal XAML-backed page now generates its WinUI activator and actual Frame navigation passes. A separate held-read regression exposed playback messages dropped while the public row/detail was temporarily null. Both surfaces retain visit-owned known membership through refresh and requery after the final write, with retirement on departure. Model and native surfaces share the same valid-movie member predicate.

The primary action now uses a focused room-aware movie entry: host lobby stages the returned-library movie; guest, playing-room and vote-mode intents use server suggestions; attached matching playback retains room transport; standalone delegates to ordinary scoped playback. A pending response cannot mutate or navigate a replaced room. The existing ordinary item-detail primary path was traced separately and was not broadened by this PR.

Final corrected verification: **3,093/3,093 full units**, **35/35 focused box-set cases**, successful fresh Release publish, and successful `boxsets` plus `boxset-room` native suites against the same publish bytes. Native evidence exercises actual hero/member controls, fallback/progress and keyboard/automation, wide/compact captures, row order/current-film initial position/marker, View all/member/aggregate destinations, card recycling, actual Frame activation, real messenger delivery during a held detail read, host/guest/lobby/playing/vote routing and a late response after room replacement. Full/native receipts: `nov02-boxsets/{d-review-full,d-review-focused-final,d-review-publish,d-review-loaded-green,d-room-green}` beneath the task evidence root. Existing compiler/analyzer warnings remain.

The final owned diagnostic app is `.codex-tmp/nov02-boxsets-d-review-publish-client` inside the D: task checkout. `SiloPlayer.dll` SHA256 is `6CBA330BA8F169931BDB0B6C27736D668ACE976A3D643DE487C811B0D18959FB`. It is not an installer or published release. Complete app/source/reference hash manifests, the frozen red cases and exact verification limits are linked in the task report. Native room API tests use synthetic room state; standalone playback is captured through its normal-player delegate, without starting real media or certifying live room interoperability. Original candidates, earlier evidence, source copies and the installed player remain preserved.

Independent corrected-source review: Standards **0 remaining findings** and Spec **0 remaining findings** over a68a53a through fixture head eb4f104. The shared eligibility advisory, actual Frame activation failure, post-write invalidation during a held read and room-action routing are resolved. Final product remains b020850; final Release/native SHA256 is 6CBA330BA8F169931BDB0B6C27736D668ACE976A3D643DE487C811B0D18959FB. Parent submission retains these source fingerprints; Beta pipeline owns subsequent changes, merge and packaging.

## Approved PR8 — optional full and quick box-set search (2026-11-02)

Local preparation on codex/nov02-boxset-search deliberately starts from Beta6d3ac20fa114458be3ec8a21bf882d3a1dc3f7af with the exact independently reviewed foundationabba1d7655128f090f96a26685ef114b7f07b706 merged locally at4de014edc0d7439abff0a272df899b277d42e166. The parent explicitly allows preparation while the foundation queues externally. This is not a Beta-ready or published candidate: foundation must reach Beta, then the parent must reconcile fresh Beta and perform independent review before submission. Stamps remain1.2.544-beta.1 / File-Assembly1.2.543.1. Implementation reference is bf53b65450244751013a2965b39da4111d9a0f72. Final direct official fetch advanced to 90f88f2d6076daefb8b5ae77893a41e9de6e20d9 (movie calendar digital/disc releases); the PR8 box-set/search source files remain unchanged. Exact drift is recorded in the external report.

Full and global quick search now show optional server-ranked box-set summaries before ordinary title results. Reads use trimmed q, limit5, optional library_id, capability/enabled-viewer-library/movie-scope gates and captured profile/language/query ownership. The server owns matching and admin search policy. A six-second independent optional deadline cannot block ordinary results; same-scope60-second cache data survives an optional refresh failure after policy validation. Disabled policy and query/library/profile/page/dialog departure retire pending reads and aggregate callbacks. Normal title totals, seek and pagination remain separate. Aggregate cards/rows have native fallback art and open the dedicated destination with the returned library; they have no item menu or aggregate playback path.

A review regression reproduced selected box-set loss during same-query watched refresh against the first publish. The corrected state retains same-scope keys and atomically publishes snapshots, without saving/restoring a selection over newer keyboard input. Actual owned-HWND native checks verify both retention and the newer-title-selection negative control. Watched/catalog and final-persisted playback refresh optional summary counts.

Final local verification:3123/3123 main unit tests, zero failures/skips, including30PR8 cases; successful fresh Release publish; clean native-full-c, native-quick-c and native-selection-c runs with actual controls and completion sentinels. Native evidence includes independent delayed reads, all quick keyboard group destinations/exact-people order, stale callbacks, profile/query/library/page/dialog retirement, disabled/failure empty states, fallback art and matching wide/compact card geometry. Final captures were visually inspected. The initial invalid setup/build attempts and earlier native teardown/repeated-dialog fixture failures remain preserved and are distinguished in the report.

Owned diagnostic app: D:/SiloPlayer/.codex-tmp/server-align-nov02/worktrees/boxset-search/.codex-tmp/boxset-search-publish-c-client. SiloPlayer.dll SHA25603E80255B994F38C3799AE0F6FC4813871E3A3617EB40547C499534AE707ABDE; PRI15BBF5780B2B5059D247116722EB1AD1F7E51E8CE46A1051A2F889BAF2D39D94. All574 published files and1634 source/test/build inputs are hashed in the external evidence. Every .NET command used the pinned04E4D27D... engine and SiloOct18Build runner; source/test bytes remained frozen through final verification. Exact committed source binding and receipts: D:/SiloPlayer/.codex-tmp/server-align-nov02/boxset-search-report.md.

These are synthetic transport/native controls, not live-server/account/media interoperability or complete WebUI visual parity. No setup, installer, release, production, credentials/settings values, installed-player control or remote delivery occurred. Canonical dirty source, other worktrees and frozen earlier outputs remain preserved. The shared BoxSetCard.SetCatalogLayout helper is identical to the concurrently prepared grouped-library helper; the parent owns their dependency reconciliation.

Independent PR8 review at4390d26a: Standards **0 findings**, Spec **0 findings**. Parent rechecked all1634 source inputs,574 published files and five receipt digests; full3123 and three native suites remain bound to app03E80255B994F38C3799AE0F6FC4813871E3A3617EB40547C499534AE707ABDE. Compact full-section and wide quick-search captures inspected. This candidate is locally reviewed, **not Beta-ready**: foundation74 must reach current Beta and the resulting source must be reconciled/verified before push. Source stamps unchanged; no publication or installed-player action.

### PR74 pipeline companion — unscoped movie membership

This focused correction starts from pipeline head `8071bdb32483ca05146b96eed1f7408106ea2c47`, retaining its source/version stamps **1.2.544-beta.1** / **1.2.543.1**. The pipeline's null-library rejection removed the optional row from ordinary Home/global-search detail routes. Real HTTP/view-model and actual native PosterCard → Frame → ItemDetail navigation reproduce that regression on a fresh unchanged 807 publish. The correction preserves the absent query, gates discovery on eligible viewer libraries and validates the response against its own enabled accessible library. Explicit scopes still require exact equality; current profile/access, item, library, language and departure guards remain in force.

Fresh focused tests pass **66/66**, full units **3,124/3,124**, zero failed/skipped. Product source `091defe98c0ea49604fe2839236de4fb1451f2de` has a successful fresh Release publish and passing foundation/unscoped plus room native suites against identical bytes. DLL SHA256 is `865962D457262443F308C0D8CFBAD992B243D4BE9B7443F373C3FA34928E7DBD`. Final hashes, fixture commit, red/green receipts and verification limits are recorded in `D:/SiloPlayer/.codex-tmp/server-align-nov02/foundation-pipeline-companion-report.md`. A preserved native diagnostic found the pipeline capability-error check traversed an unloaded page despite a correct Transient model; the fixture now observes Loaded/layout before asserting error/retry controls. The new fixture also observes the queued movie-row render before checking its native cards.

Official main was freshly checked at `90f88f2d6076daefb8b5ae77893a41e9de6e20d9`; both selected box-set contract files match its Git objects and bf53b654 byte-for-byte. All .NET work uses `SiloOct18Build`, isolated D: outputs and synthetic APIs/settings. Earlier foundation/807/abba evidence remains frozen. No installer, credentials, installed-player control or remote mutation occurred; parent owns scoped review and delivery gates. Synthetic controls/layout and HTTP tests do not certify live server or room interoperability or complete pixel parity.

### PR8 corrected-foundation reconciliation checkpoint

The earlier literal foundation-in-Beta submission hold above is superseded by the owner-authorized complete stacked-head scheduling rule: include the reviewed dependency, verify the complete head and record dependency commits before submitting to Beta. Pipeline owns fresh-base reconciliation/re-review/merge/stamps. No direct Beta or Stable merge is authorized here.

Local reconciliation incorporates corrected PR74 foundation7617ddf2d5a90b5ea0a0e9792c3e8e95326a8123 into prior reviewed search213e36374e4106aebceb8325c1191c7bb9a538b1. Detail discovery preserves eligible hidden-library membership while optional search retains the WebUI visible-library list through an explicitly named search reader; the new dual-policy HTTP regression verifies both query paths. Focused97/97 and full3155/3155 pass with unchanged source/build inputs. Both native dispatch branches and both handoffs survive conflict resolution. This is a checkpoint, not final readiness: the additional PR74 scoped restart-menu correction and fresh Release/native/review gates remain pending. Frozen prior search bytes and all intermediate evidence remain preserved under search-reconcile.

### PR74 restart menu scope correction

Product `64bd63e00231fa93e5d23ea33d282f3be01679a5` and fixtures `620cd53660b80b4200751023e4c91ad4cd4dca1d` correct the additional menu finding after the 7617 review. Actual More → Play from Beginning commands now retain the member card's captured returned library. Retained commands reject changed item/scope, departure or viewer identity before entering the existing ordinary playback path. Menu closure does not cancel accepted playback; fromStart and room authority policy remain unchanged.

Fresh focused units **414/414**, corrected full **3,124/3,124**, Release publish and four native routes pass against the same diagnostic DLL SHA256 `BB82FAFBC565F7A169EEA7AA9ACCE75A4C2BCCE845951659B51B1C5C3268C8E0`. Actual dedicated/membership/unscoped popup requests verify library, authorized file/profile and explicit zero restart. Retained-menu negatives cover recycled/scope-changed/departed/other-profile owners; a same-viewer access refresh still works. Existing foundation/unscoped and room suites also pass. The initial complete run's obsolete exact-call source assertion is replaced by this actual native behavior coverage; its failed receipt remains preserved.

New receipts are `foundation-companion/menu-*` under `D:/SiloPlayer/.codex-tmp/server-align-nov02`; final refs/source/artifact hashes, commands and synthetic/live verification limits are in `foundation-pipeline-companion-report.md`. Earlier packages, reports and receipts remain frozen, including the pre-menu report snapshot. Source stamps remain **1.2.544-beta.1** / **1.2.543.1**. No release, installer, installed-player or remote action occurred. Parent owns scoped review of this additional delta and final delivery gates.

### Final PR8 box-set search candidate

Reviewed product integration fcfd0323b3fe6ba6d53841a3fe459c6b9a0a4540 contains the corrected PR74 product37cc69ef46c60bdd5fd166e4c50575a81e1db97f. Both scoped integration review axes report0sourcefindings; captured restart identity is now validated through admission/preparation without changing ordinary room/queue/close semantics. Search retains its visible-library policy separately from hidden-library detail discovery. All other search production files remain unchanged from the prior reviewed candidate.

Final verification:3155/3155full units; fresh Release DLL EECE923707A4B55FB32EB39E905261E2B353EA1E72F627DBFAD4A51DF6490CB0; ten native modes passed on those identical bytes (full/quick/selection search, complete foundation/unscoped navigation, scoped/pre-click restart menus, queued admission/ordinary lifecycle, existing full quick-play and room primary). Current widequick/compactfull captures inspected. All1047product/build inputs and574published files rehashed unchanged. The original fullquickplay run failed five inherited setup reflections of public SetState; fixture-onlyabc34b8b8cbaa353b332213404a00e6af0f28031 replaces those with direct public calls, with every assertion retained. Corrected fullquickplay passes all17review regressions; original/final fixture hashes and its scoped rerun are preserved. No app rebuild followed that fixture-only correction, and no failed receipt is counted green.

Exact source, artifacts, receipts and source-only/fixture-only review boundaries are in D:/SiloPlayer/.codex-tmp/server-align-nov02/search-reconcile-report.md and search-reconcile/final-reviewed-binding.json. This complete tested stack can queue after PR74 in the Beta pipeline, which owns fresh-base reconciliation/re-review/merge/stamps/packages. Versions remain unchanged; no installer, Stable/main merge, live account or installed-player control occurred. Synthetic tests do not establish live interoperability or complete visual parity.

### PR74 ordinary restart admission ownership

The subsequent Spec review found that the menu's pre-click identity guard did not survive queued playback admission. An actual popup regression on frozen BB82 held the authority gate, switched profile after clicking and reproduced old-card playback preparation under the replacement viewer. Product/build `37cc69ef46c60bdd5fd166e4c50575a81e1db97f` carries captured identity through the authority/request gates, transport drain, watch/settings/capability preparation and session creation using the focused `RestartAsync` interface. The existing public `PlayAsync` signature remains compatible; private ownership policy separates identity checks from quick-play supersession, room and close-generation behavior.

Fresh focused **466/466**, full **3,124/3,124**, Release and **seven native routes** pass against the same DLL SHA256 `53DC2DBE6AEE834064F782F4B9D2455731243EF4E9D8558297903FEFA1E4922B`. Actual menu tests retire changed viewers across both gates and held preparation reads while allowing same-viewer access refresh. Ordinary accepted-unload, active-room, queue-behind-scoped and intervening actual CloseAsync controls pass on final and BB82 baseline apps. Menu scope/pre-click, foundation/unscoped and room primary suites also pass.

Final fixture `abc34b8b8cbaa353b332213404a00e6af0f28031` corrects five inherited quick-play setup lookups for public SetState using direct calls, without changing assertions. The original full quick-play failure and both fixture hashes remain preserved; the corrected full suite passes on final and BB82 apps, including all 17 review regressions. The fixture change did not rebuild or modify either app. Admission receipts/manifests are `foundation-companion/admission-*` beneath the same external evidence root, with exact source, artifact and fixture binding in the companion report.

Source stamps remain **1.2.544-beta.1** / **1.2.543.1**. All verification is mutex guarded, isolated under D: and synthetic. Earlier apps and reports remain frozen; no installer, installed-player, credentials, production or remote action occurred. Live interoperability and complete visual parity remain unverified. Parent owns scoped re-review and final delivery gates.

## November 8 — library box-set grouping removed

The official server's catalog accepts only `group=work` (silo-server `internal/apiv2/catalog_items.go`; Granary's contract agrees), so PR7's `group=box_set` library browse could never activate there. Each session paid one refused 422 read before #100/#101 fell back to ungrouped browsing. The owner chose removal on 2026-11-08.

Removed: the `group` parameter on catalog GET, large POST and letter requests; the grouping decision, 422 refusal fallback, grouped refresh and its range-read retirement in `LibraryViewModel`; the page's grouped refresh triggers; and the aggregate card inside `LibraryGridCard`. Box-set libraries now browse their movies individually.

Kept: box-set detail pages, the movie-detail membership row, box-set search results and `BoxSetCard` on Home and Search posters. Also kept: the library-flags revalidation (now `LibraryPage.LibraryFlags.cs`) and the catalog scope check for library type, enabled state and browse type. A box-set toggle no longer retires a loaded catalog. Tests: `LibraryCatalogScopeTests` replaces the grouped unit tests, and native `library-policy` replaces `grouped-library` and `grouped-post-save`.
