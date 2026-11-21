# Configure › Sign-in: refresh to official `02a9d1d3e1` (provisioned providers and the rest of the page)

Branch `t3/admin-signin-parity-refresh`, started from `origin/beta` (the page as merged in #215 and repaired in #225,
built against official `217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`). Official reference for this refresh: Silo
server commit **02a9d1d3e1b48d86437ad10c0b66ed0e3645d098**, read with `git show` from
`D:/SiloPlayer-work/server-upstream-nov14` (nothing fetched, checked out or modified there). Project version unchanged.
The earlier record for this page is `admin-sign-in-security-20261117.md`; it stays as the history of the first merge.

## Upstream changes that reach this page (`git log 217ff25abc..02a9d1d3e` over the page's files and imports)

| Commit | Change | Handling |
|---|---|---|
| `2c2dc7c00` #4602 | A deployment can **provision** a sign-in plugin: `env_managed` on the capability, on a binding, and on the installation (`config_fields`, `binding_fields`, `version_pin`); a binding gains `callback_mode` and `callback_pending`. The page shows a managed note, disables owned controls, shows a "Pending" state, drops owned fields from every write. The network sign-in write stops forcing `default_login: false`. | **Done** (this unit). |
| `49367f9e0` #4681 | The no-break-glass warning is one flex item (text, link and full stop were separate items). | **Done**: one text element with an inline link. |
| `7e536cb10` #4841, `5537df46a` #4766 | Setup values, the look-up user and URLs read left to right on a right-to-left page (`textDirection`, `dir="ltr"` on the copy box). | **Done** (`AdminSignInText.DirectionFor`, applied to every setup box, the look-up user, the address boxes and masked boxes). |
| `b5c19aba8` #4897 | A secret field also marked multiline is a masked single-line input (`rendersMultiline` excludes secrets). | Already true natively (the secret branch came first); now **pinned** by `AdminSignInSchemaRules.EditorOf` + a unit test and a native check (a PasswordBox, no plain box). |
| `e8de1117e` #4498 | Settings that the environment manages (`managedByEnv` on settings rows). | **Not in this unit.** Environment-managed sign-in settings: **after #234**. The existing "Managed by environment" line on settings rows is untouched. |
| `aa06983a6` #4696, `6b57a44be` #4673, `11693425e` #4665 | `useSettingsForm` / `SettingField` changes for hidden or environment-managed settings pages. | Same overlap as #4498: after #234. |
| `f6ed24869` #4700 | Red buttons use `--destructive-fill` `#dc2626`. | **Done** page-locally for the two "Turn off" confirmations (see below). |
| `4cf51bb51`, `dbceadcaf`, `f465d2d9a`, `fae464e73`, `9820f17e1`, `6ddee8d39`, `43d38e533`, `980e4555b` | Min font sizes on inputs, hover label colour on primary buttons, playback bar offset, glass blur, status icon alignment, path wrapping, avatar initials, RTL name cuts. | Web-only presentation with no counterpart in the native controls (not this page's content); recorded, not ported. |

## Contract: what the native client reads and sends, compared with the official client at `02a9d1d3e1`

| Item | Official | Native before | Native now |
|---|---|---|---|
| `AdminPluginCapability.env_managed` (`string[]`, binding fields the deployment owns; may appear before a binding row exists) | `authBindingFieldIsManaged` reads it from the capability | not read | `AdminSignInCapability.EnvManaged`; `AdminSignInProvider.BindingFieldManaged(field)` |
| `AdminPluginAuthBinding.env_managed`, `callback_mode` (`installation`/`stable`), `callback_pending` | `callback_pending` gives the "Pending" state and hides the redirect URI | not read | `AdminSignInBinding.EnvManaged / CallbackMode / CallbackPending` |
| `AdminPluginInstallation.env_managed` (`config_fields` keyed by configuration entry, `binding_fields`, `version_pin`) | `managedFields = env_managed.config_fields[schema.key]` | not read | `AdminSignInOwnership`; `ManagedConfigFields(key)` |
| `presentation.display_name`, `capability.metadata.display_name` | `authProviderName` / `authProviderLabel` | only the saved `display_name`, the capability name and the plugin id | `PluginName` (presentation → capability → plugin id) and `Name` (saved → metadata → capability → `PluginName`) |
| Older server | fields absent | – | Absent, `null` or empty ownership reads as "nothing managed" (unit tests, six shapes) |
| Binding write body | `capability_id, enabled, display_order, auto_provision, default_login` (no `callback_mode`) | the same, but a network write forced `default_login: false` | the saved row is written back with only the chosen change (`default_login` is the saved one); `callback_mode` stays omitted so the server keeps the stored mode |
| Config write body | `{key, value, clear_secrets}`; `stagedEntry` deletes owned fields, owned `clear_secrets` are filtered | owned fields would have been sent | `AdminSignInSchemaRules.Build` leaves owned fields out of both a save and a connection test |
| Connection-test body | `{capability_id, config:[…]}`; the look-up user is not injected when owned | injected | not injected when owned |
| Redirect address | `capability.callback_url \|\| binding.callback_url \|\| testResult.callback_url`, empty while pending | `capability ?? binding` (an empty string never fell through) | the three-way fallback, empty while pending |
| Refusal of a managed change | 422 `validation_failed`, `errors[].code = managed_by_environment`, text "Set by the environment. Change it there and restart the affected service." | generic "server rejected these values" | `SiloApiClient.AdminAccess` reads only the code identifier `managed_by_environment` from the problem and shows the server's sentence from the native table (no server text crosses) |

### What a provisioned binding allows, and what the server answers to a write

Server rules (`internal/plugins/runtime_config.go: requireUnchangedOwnedBinding`, `provision_ownership.go`):
a binding write is refused with 422 `managed_by_environment` (`body.auth_binding.<field>`) when an **owned field's value differs from the
stored row, or when no row exists yet**; a config write is refused when an owned top-level field differs (`body.config.<key>.<field>`).
An unchanged value is accepted, because the API replaces the whole row.

| Native action on a provisioned binding | Sent? | Server would answer |
|---|---|---|
| Turn on / Turn off a provider whose `enabled` is owned | **No.** The button is disabled with the note "Managed by the environment"; `SetBindingAsync` also refuses on the fresh read | (422 if it were sent) |
| Change "Create accounts on first sign-in" when `auto_provision` is owned | **No** (switch disabled; never staged; never in a save) | (422) |
| Edit an owned configuration field | **No** (input disabled; `SetField` ignores it; never in a save or a test) | (422) |
| Type or clear an owned secret | **No** (input disabled, no Clear button) | (422) |
| Edit any other field, or the one not owned binding field | Yes, the saved row with only the change (owned fields carry their stored value) | 204 |
| An owned field and no binding row yet | The write is sent as the official page would send it | 422 (shown with the server's sentence; writes then wait for a reload, drafts stay) |
| Ownership arrives while an edit of that field is staged | The edit is dropped at once and does not return if ownership is released | – |

## Differences between the official page at `02a9d1d3e1` and the native page, and what was done

"Fix" = changed in this unit. "Right" = already matched. "Cannot" = not possible in WinUI (reason given). "Deliberate" = kept on purpose.

| Area | Official | Native on beta | Outcome |
|---|---|---|---|
| Section order | Silo passwords → Single sign-on → Network sign-in → API keys | same | Right |
| Page title, group titles, every string of `web.admin.settings.signIn.*` (122 keys) | `en.json` | native table | **Right** (mechanical comparison, 0 differences); added 25 keys of `web.admin.plugins.provisioning.*` and `web.admin.settings.lib.externalSignIn.*` and 16 native-only keys (`signIn.native.*`) |
| "Manage invite codes" | arrow link in the group title row | a bordered button row inside the group | **Fix**: link-style action with an arrow in the title row (`SetAction`) |
| Break-glass status | muted/warn status under the toggle; the warning is one sentence with an inline link | a separate text element, no link | **Fix**: one text element, amber, inline link to Users; names neutralized |
| Passwords toggle guards (no break-glass admin, no provider on) | `changeLocalLogin` | same | Right (unchanged) |
| Provider choice | card buttons with title, state chip and detail (protocol examples), "None" card | plain radio buttons | **Fix**: cards inside the existing `RadioButtons` (kept for the #225 echo guard) |
| Provider title | protocol name if unique for its mode, else the plugin's name (`authProviderName`) | provider label | **Fix** |
| Loading / failed read of the plugin list | skeleton; "Couldn't read the installed plugins. Reload the page to try again." | an empty "no plugins" sentence while loading; the shared error line | **Fix**: "Loading settings" while unknown; the official failure sentence with a working **Retry** (asks before discarding staged edits) |
| No plugin installed | sentence with "plugin catalog" link | sentence + separate button | **Fix** inline link |
| Provider header | name, state dot + word, "· shown as …", Plugin page, Turn on/off | one line "title · state", buttons below | **Fix** |
| Provider states | plugin off / needs setup / off / on / on needs setup / **pending** (callback pending) | all but pending | **Fix** |
| Turn on blocked reasons | plugin off (link), other provider on, unsaved changes | disabled button, no reason | **Fix**: the reason is stated (`blocked.*`) |
| "Needs setup" line | amber, `joinList` of the missing fields | grey, joined | **Fix** colour; join is `, ` in both (`Intl.ListFormat` unit/short) = Right |
| Setup steps | numbered steps with 22px badge, heading, description, rows indented | headings without numbers | **Fix**; step order: register → plugin steps → accounts (only when none) → test → login button |
| Redirect / post-logout addresses | read-only wrapping box (`dir=ltr`, selects on focus), Copy button "Copy redirect uri", "Copied"/"Couldn't copy…" | text + Copy button, no feedback | **Fix**; the address with credentials or a signed query is still refused (native safety rule); copy puts exactly the visible text on the clipboard, out of history and roaming |
| Public URL missing | warning with link to General settings | text without link | **Fix** inline link; also shown while pending |
| Schema-driven fields | `SignInConfigField`: switch, select, textarea, masked input, number, text; saved-secret hint; "Saved." / "Will be cleared when you save." + Keep/Clear; dirty dot | all controls; Clear button only | **Fix**: status text, hint follows Clear; managed state; direction |
| Secret + multiline | masked single-line | masked (already) | **Right**, now pinned |
| Advanced | heading + description; each collapsible group with a count; open when it has an unsaved or missing field | Expanders always in the panel, open by default unless `collapsed_default` | **Fix** |
| Auto-provision row | last step (or "Choose who can sign in" step) | below the form | **Fix** |
| Test step | description (saved/unsaved), look-up user prefilled from the saved value, "Testing...", heading "Every check passed." / "N of M checks failed." / "The test did not pass.", each step with ✓/✗, inline error "The connection test didn't run." | a text blob | **Fix** |
| Login-button step | "Provider name"/"Directory name" field, preview "Sign in with …" | inline | **Fix** (own step, live preview) |
| Unsupported entries | one note with a plugin-page link | per-entry text + button | **Fix** |
| Network sign-in | two switches (Sign in with X; Create accounts on first sign-in), applied at once, "How roles and sharing work" | the full provider panel | **Fix**: official layout; the confirmations stay (deliberate, below) |
| Feedback after a binding change | per-panel line (`turnedOn`, `turnedOff`, `turnedOffBreakGlass`, network texts) | page-level generic line | **Fix**: the provider says it in its own words (the controller's generic line is unchanged: an existing test pins it) |
| Turn-off confirmation | destructive `ConfirmDialog`; description depends on whether Silo passwords are on | ordinary dialog | **Fix**: official texts and destructive fill (`#dc2626`, white text, 90% on hover, system colours in a contrast theme) |
| Third-party text | React text nodes | plain `TextBlock` text | **Fix**: names in titles, sentences, the status line and test steps are neutralized and bounded (`AdminSignInText.Plain`) |
| Provisioned bindings | notes, disabled controls, `Pending` | – | **Fix** (above) |
| Left-to-right values | `dir` on setup boxes, the look-up user, the copy box | – | **Fix** |
| `default_login` of a network write | binding's own | forced `false` | **Fix** |
| Save bar, dirty counts, discard | `SaveBar` | `AdminSettingsSaveBar` | Right |
| Save order | provider first, then settings; a 409 `break_glass_required` shows the break-glass text | same | Right (unchanged) |
| Unknown outcome / refused write | n/a (web retries) | writes block until reload; drafts kept | Right, kept |
| States: loading, empty, failed read, saving | skeleton, text | loading line, text | Right / **Fix** (above) |
| Widths | one fluid column | 1120 max, rows reflow at 640/520 | Right (existing); captures at 1440/1024/800/560 |
| "Plugin page" arrow icon, spinner on Turn on | lucide icons | text only | **Cannot (not done)**: no matching icon in the native icon set and no reason to add shared icons for this page; the pending state is the disabled button |
| Focus moves to the feedback line after Turn on/off | `requestAnimationFrame(focus)` | live region (polite) | **Deliberate**: the line is announced, focus is not moved |
| Skeleton shimmer | `Skeleton` | text "Loading settings" | **Cannot**: no shared skeleton for this layout |
| Rich-text `code` styling in the network explanation | `<code>` | plain | **Cannot** (no inline code run in the shared text helper); the grant example is monospace |

## Deliberate differences (kept, with the reason)

- **Turning a provider on asks first** and **the network switches ask first**; the official page applies both at once. These confirmations existed on beta
  and guard lock-out and open-sign-in risks; they are kept. Turning off asks on both pages (official wording, destructive fill).
- The shared access client keeps its earlier sentence for 409 `break_glass_required` ("Keep an enabled break-glass administrator …"): an existing regression test
  pins that wording. The sentence for `provider_already_enabled` and the new `managed_by_environment` follow the official text.
- Binding-change confirmation line in the controller keeps its earlier generic wording ("Sign-in provider turned off."), pinned by an existing test; the panel
  also shows the official sentence.
- 412/429/403/503 texts come from the shared access client, not from `adminSignInErrorText`.
- Environment-managed **settings** rows (#4498 family): after #234.

## Shared code touched

- `src/SiloPlayer.Core/Api/SiloApiClient.AdminAccess.cs` (also used by API Keys and Policy): reads the identifier `managed_by_environment` from a 422 problem and
  maps it to a sentence; the text of `provider_already_enabled` follows the official catalog. API Keys and Policy never produce either code.
- `src/SiloPlayer.Core/Services/Admin/AdminText.cs`: 41 keys added (25 official, 16 native-only under `signIn.native.*`).
- `tests/SiloPlayer.NativeRegressionTests/Program.cs`: one line runs the new fixture block after the existing one under the same selector.
- No shared control, style, `DarkTheme.xaml` or navigation file was changed. The destructive fill lives in the page's file.

## Verification

Lane `D:/SiloPlayer-build/admin-completion-lane-b`; every run through the build-lease script.

- **Baseline on unchanged beta** (`signinref-baseline-r1`, `-Stage publish-native`, selector `admin-signin-security`): 172 checks passed, 0 failed (source
  `d1144b96f`).
- **Final** `signinref-final-r2` (`-Stage final`, source commit `d2433b896`): focused unit tests (`AdminSignInProvisioningTests`, `AdminAccessSecurityTests`,
  `NetworkSignInContractTests`, `AdminManageNavigationTests`) 226 passed, 0 failed; full unit suite 11,027 passed, 0 failed; native groups `admin-signin-security` 262 checks,
  `admin-configure-security` 374, `admin-configure-general` 188, `admin-manage-foundation` 36, `admin-monitor` 113, all 0 failed; payload and source seals verified.
  `signinref-final-r1` (source `256e6d9ee`, 223 focused, 11,024 full, `admin-signin-security` 254) also passed; the final run was repeated only because a safety fix (a typed
  secret erased after an unknown save outcome) was added afterwards.
- **Stability**: `signinref-native-r1` (263 checks) and `signinref-native-r2` (262) of `admin-signin-security` on the same build, both 0 failed. The one-check
  difference is the shared `ClickForDialog` helper logging an "enabled" line on a retry path of an existing step; no assertion differs.
- The #225 checks ("an idle page does not rebuild its form", "a typed list stays in the same box", "an account re-read updates the status line and the button in place",
  the typed-group-list steps) are unchanged and passed in every run. Nothing existing was weakened.
- Unit tests added (`AdminSignInProvisioningTests`): every ownership shape present and absent, the older-server and malformed-ownership answers, label order, a
  managed field never staged or sent (config save, connection test; a managed binding field is never CHANGED, see the correction below), the binding body (saved row, no `callback_mode`, a network binding keeps its `default_login`),
  the refusal sentence and one-shot send, a deployment claim dropping a staged edit, a secret that is also multiline is masked, secrets (untouched not sent; erased after an accepted save and after an unknown
  outcome; kept after a definite refusal; a staged clear is kept), a doubled binding change sent once, a failed test shows no server or typed text, the layout split, third-party text neutralization and bounding, direction rules.
- Native block `AdminSignInProvisioningNativeFixture` (about 90 checks, run after the existing block under the same selector): loading, the break-glass sentence as one text element, each provider state on its card, a provisioned binding
  (note, Turn on disabled, managed fields read-only, masked secret, nothing sent for the binding, no owned field in a save or a test), pending, needs setup, plugin off, none, a hostile name neutralized in a dialog title, the destructive fill,
  a doubled activation sending one write, a network sign-in with an owned switch, an unknown-outcome write blocking until reload, a refused save keeping the draft and showing the reason without server text, an unknown-outcome save erasing a typed
  secret, copy putting exactly the visible address on the clipboard (read back, then cleared), no rebuild while idle or typing, and left-to-right values on a right-to-left page.
- **Captures** (PNG, in the lane's `results/<label>-admin-signin-security`, produced by the fixture at 1440, 1024, 800 and 560 px): opened and compared with the official layout read from `SignInSettings.tsx` /
  `SignInProviderSlot.tsx`: `provisioning-overview-1440`, `provisioning-provisioned-1440`, `provisioning-provisioned-test-1440`, `provisioning-provisioned-800`, `provisioning-provisioned-560`, `provisioning-top-1024`
  (from `signinref-dev-r9`, `dev-r10` and `final-r1`; the page's layout code is the same in `final-r2`). Not opened: `provisioning-provisioned-1024`, `provisioning-top-800`, `provisioning-top-560`, and the captures of the other
  Access pages (unchanged). Dialogs and the ComboBox popup layer cannot be captured by `RenderTargetBitmap`: the dialogs were checked through automation (title, button fill and text, one write).

## Not verified

- No official server or screenshot exists to compare with: nothing here claims visual or pixel parity. Layout was compared with the official component source only.
- A real deployment-provisioned server (a live `env_managed` payload), a real clipboard on other desktops, a real contrast theme switched while a dialog is open (the code follows
  `HighContrastChanged`; it was not exercised), a live OIDC/LDAP provider, Narrator and an input method editor were not exercised. The right-to-left check forces the frame's flow direction; no Arabic or Hebrew catalog
  is shipped.
- The web-only presentation details listed under "Cannot" (icons, skeleton, inline code styling) and the focus move after a binding change are not reproduced.
- Environment-managed sign-in **settings** (#4498 and its follow-ups): **after #234**.
- The groups named in the brief as failing on beta (`admin-libraries`, `admin-users`, `admin-configure-maintenance`, `admin-configure-scheduled-tasks`, `admin-configure-search`, `admin-configure-storage`,
  `admin-manage-download-settings`, `admin-monitor-logs`, `artwork-first-mount`, `advanced-overlays`) were not run.

## Review follow-up (November 21): corrections and what changed

An independent review of `4e83e4440` found no P1 and three P2. Fixed in `9a65f391e`, `dbc531d10` and `7a0904e8a`.

**Corrections to this record.** (1) "A managed field is never staged or sent … in a binding write" was inaccurate: all five binding fields (`capability_id, enabled, display_order, auto_provision, default_login`)
are always sent, and an owned field restates its saved value, which the server accepts (it refuses only a changed value). A write now restates every field the admin did not touch from the **fresh** read of the
binding, not from the page's older copy (before: `SetBindingAsync` re-read the providers but used the `enabled` the page passed in). (2) The "Native before / now" table row for the binding body stands, with that
change: the touched field is the admin's, the other four come from the fresh read; if another session changed one of those since the page drew the provider, nothing is written, the admin is told it changed elsewhere
and the page shows the fresh state (unit tests both directions, `default_login` and `display_order`; native step on a second network provider).

**Deliberate difference from official: the look-up user.** When the connection test's look-up user is owned by the deployment, native sends no entry for that configuration schema when nothing else is staged for it;
official still sends one (the saved values minus the owned fields). Reason: native builds a test entry only for a staged edit or for the look-up user it may type; with the field owned there is nothing of the admin's to
send, and an entry made of saved values only restates what the server already has.

**Shared with official (limitation, not fixed).** A connection test for a provisioned provider with a staged edit omits the owned non-secret fields (official `stagedEntry` does the same), so the server may fail
validation of a required owned field that the test body lacks. Native could only avoid this by sending the owned fields, which the server refuses for a changed value and the page must never send; it does not. The
owned values are the deployment's, so a failed test there says the test body was incomplete, not that the provider is broken. Not observed against a real provisioned server.

### A failed write: stay editable, or read again? (finding 1)

Only the Sign-in controller decides this (`AdminSignInController.RequiresReview` / `ReviewMessage`, hooks added to `AdminAccessController` whose defaults are the previous rule verbatim, so API Keys and Policy are unchanged:
their controllers do not override them and `AdminAccessSecurityTests` runs unchanged except one Sign-in assertion named below). Official keeps the drafts after any failed write (React state), re-reads the installations after
every write, and drops edits to a field the deployment now owns; native cannot re-read automatically without losing the draft, so it separates the answers.

| Answer | What official does | What native does now |
|---|---|---|
| 422 `validation_failed` (config, binding, or settings write), nothing applied earlier | draft kept, error text, editable | editable, draft and typed secret kept, "The server rejected these values…"; a corrected save is sent and then erases the secret |
| 409 `provider_already_enabled` | error text, editable | editable, staged edits kept, official sentence |
| 409 `break_glass_required` from the settings write, nothing applied earlier | break-glass text, editable | editable, draft kept, pinned break-glass sentence |
| Local `break_glass_required` / `provider_required` (nothing sent; a fresh read showed the page's list is out of date) | no local check against a fresh read | editable, draft and secret kept, message adds "…out of date: reload the page to see the current one"; every save reads again |
| 429 | error text, editable | editable, secret kept |
| 422 `managed_by_environment` (config write), or the local one on a binding write | edit dropped when ownership arrives, installations re-read | **reload needed**; typed secret erased; message: "…the environment now manages something this page showed as editable. Reload the page to read the current state." |
| A read the write relied on failed (pre-check or fresh binding read) | n/a | reload needed; secret erased; "The current state could not be read before writing." |
| 409 `conflict`, settings conflict | re-read | reload needed; secret erased; says the resource changed |
| Any answer after an earlier write of the same save already landed | partial result reported | reload needed (the page shows the older values); "Part of this change was already saved…" |
| Timeout, transport failure, unreadable answer, 5xx | – | reload needed (unchanged); secret erased |
| Anything else (401, 403, 404, 412, other 4xx) | error text | reload needed (unchanged), secret erased |

A typed secret is erased exactly when the page locks (the same function decides both). The visible draft stays on screen until the admin reloads or discards. An existing assertion was changed deliberately:
`AdminAccessSecurityTests.RemovingBreakGlassAfterConfirmationPreventsSettingsWrite` pinned a lock after the local break-glass refusal; it now pins "editable, still dirty, says the list is out of date" (its sibling
`RefusedLocalPasswordDisableWritesNoProviderConfigurationFirst` already pinned that both staged changes stay). The wrong comment in `SaveAsync` ("nothing typed stays staged") and the test
`ATypedSecretSurvivesADefiniteRefusal` (now asserts the page is editable and the secret is sent by the next save) were corrected.

### Other findings

- **Plugin forms (2).** `AdminSignInLayout.Of` keeps the first field of a repeated key (one row per key), tolerates an empty key, `null` form lists (`AdminSignInForm`, `AdminSignInSection` now read `null` as empty) and a null
  schema key. Every `Single`/`SingleOrDefault`/`First` over plugin- or server-supplied lists in the controller (config entries, providers, the look-up entry) is now tolerant; a draft for an entry a refreshed list no longer has is dropped.
- **Preview (4).** A `display_name.value` field that is secret (flag or PASSWORD control) is an ordinary masked step, never the login-button field; the preview cannot read it.
- **Text (7).** The no-plugin sentence is one text block with bold runs for the two plugin names (plain text) and the catalog link.
- **Tests (6).** The connection-test unit test now stages an edit so its assertion runs over a real body; `default_login` cases added (absent from the answer sends false, saved false on a network binding, turn-off with saved true,
  owned on a network binding restated and nothing refused). Native: the refusal path reads the PasswordBox; Retry (asks, declining keeps the edit and reads nothing, accepting discards); the live preview follows the login-name
  box and never shows another field; typing goes through a FOCUSED box at the caret, one character at a time, and the same box, text, caret and focus are checked after each (the page exposes `FormBuildCount` for the
  rebuild checks); the fixed delays were replaced by waits on conditions and an idle page is watched for a bounded 500 ms against the build counter.
- **Not possible in the native host.** Real keystrokes were not injected (global input would reach the owner's desktop): text boxes are typed with `SelectedText` at the caret in a focused box. A focused PasswordBox ignores a
  programmatically assigned Password in the test host, so the masked box is extended while unfocused (same box, value, no rebuild), then focused and checked for value and focus while idle; its caret is not observable.

### Verification of this follow-up

Lane `D:/SiloPlayer-build/admin-completion-lane-b`. (The labels `signinfix-final-r1`, `signinfix-native-r1/-r2` had been used by an earlier fix on this page, so `-final-r2/-r3` and `-native-r3/-r4` are used here.)

- **Mutation** `signinfix-mutation-r1` (focused unit run, source `7a0904e8a` with `AdminSignInController.cs`, `AdminSignInLayout.cs` and `AdminSignInModels.cs` restored to `4e83e4440`): 24 of 250 failed (the new checks for
  findings 1, 2 and 3, and the one changed existing assertion); restored, `git diff` empty.
- `signinfix-final-r2` (source `7a0904e8a`): focused 250/250; the full suite failed only `AdminStorageSettingsControllerTests.DismissHidesOnlyTheCurrentFailedJob` ("Timed out: open", known intermittent); 11,050 passed. Rerun unchanged as
  **`signinfix-final-r3`** (same source): focused 250/250, full 11,051/11,051 (0 failed, 0 skipped); native `admin-signin-security` 322, `admin-configure-security` 374, `admin-configure-general` 188,
  `admin-manage-foundation` 36, `admin-monitor` 113, all 0 failed; payload and source seals verified.
- **Stability**: `signinfix-native-r3` (322) and `signinfix-native-r4` (323) of `admin-signin-security` on the same build, 0 failed (the one-check difference is the shared `ClickForDialog` retry log line).
- Not run, as the brief says: the native groups that fail on beta (`admin-libraries`, `admin-users`, `admin-configure-maintenance`, `-scheduled-tasks`, `-search`, `-storage`, `admin-manage-download-settings`,
  `artwork-first-mount`, `advanced-overlays`); the two intermittent groups were not part of this run.
- Not established: a real provisioned server, real keyboard input, the caret of the masked box, visual or pixel parity.
