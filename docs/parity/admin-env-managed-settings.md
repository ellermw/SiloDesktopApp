# Admin settings fixed by the server's environment

Scope: one shared mechanism for a setting that the server's environment sets, used by every native Admin settings page that is on Beta.
Official reference: Silo server `f465d2d9af29862f3f76a0ea4e7b1d7d03eb41d0` (newest official main when this work was done). Paths below are in
that tree. This note states what was read and what was built; it does not claim visual or pixel parity.

## Official behavior

1. **Response.** `GET /api/v2/admin/settings/sensitive-status` answers `{configured, managed_by_env, managed_by_env_vars}`; all three are
   required, present when empty (`contracts/api/v2/openapi.json:21581-21600`, `internal/apiv2/admin_settings_inspection.go:33-40` and `:92-101`,
   `docs/admin-settings-api.md:12-45`). `managed_by_env_vars` is an object from setting key to **one** variable name
   (`map[string]string`). The server fills it only for a key with a non-empty name (`internal/api/handlers/admin_settings_inspection.go:62-76`), so
   it is a subset of `managed_by_env`. `redis.url` and `redis.db` both map to `REDIS_URL`; a secret read from a file names the `_FILE` variable.
2. **Validation.** None at run time. `decodeV2Response` casts the parsed JSON to the generated type (`web/src/api/v2/request.ts:648-656`); a
   wrong-shaped map is not refused, it is simply read (`web/src/hooks/useSettingsForm.ts:65`, `:320`).
3. **Presentation.** Every field takes `managedByEnv?: string` (`web/src/pages/admin-settings/SettingField.tsx:91-96`, `:219-220`;
   `SecretField.tsx:46-47`, `LimitField.tsx:31`, `PathSettingField.tsx:24-25`, `AdminRecommendations.tsx:58`). A non-null value **disables** the control
   (`disabled = disabledProp || managedByEnv != null`, `SettingField.tsx:245`, `SecretField.tsx:78`); a truthy value adds a muted line under the
   description (`SettingField.tsx:160-166`): `web.admin.settings.fields.managedByEnv` = "Set by {env} in the server’s environment."
   (`web/src/locales/en.json:60810`). The control's `aria-describedby` points at the description only, not at the note. A secret that is managed keeps
   no "saved" hint (`SecretField.tsx:89-94`). The note interpolates the one string the server sent; the client does not split or join names, so
   there is no multi-variable rendering (two keys that share `REDIS_URL` each say so, `RedisGroup.tsx:114-148`).
4. **Drafts and saves.** When the map names a key that has a draft, `useSettingsForm` drops the draft (dirty flag and local value), so it can be neither
   saved nor undone (`useSettingsForm.ts:59-85`); exception: an empty `redis.db` draft stays, because the server still accepts clearing it under
   `REDIS_URL` (`:62-64`, `:69-70`; `web/src/hooks/queries/admin/settingsWrite.test.ts:218-266`). A save sends only dirty keys (`:245-249`), so a
   dropped key is never sent. A secret has no value in the settings snapshot, so after a drop its field is empty (`:80`, `:282`). A status read that fails
   after it once succeeded keeps the last list (`:25-27`); only a list that never loaded is an error (`:325`).
5. **Page-specific.** Redis: the "Use Redis" switch and both fields follow `redis.url`'s variable, an env override of the switch is cleared, and
   the connection check stays available (`RedisGroup.tsx:53-56`, `:114-160`, test `RedisGroup.test.tsx:68-91`: both Redis drafts go, the other stays).
   Compatibility proxies: the staged web-off that rides with an API switch-off is taken back when the API draft is gone, the environment's takeover included
   (`CompatibilityProxiesSettings.tsx:186-201`, test `CompatibilityProxiesSettings.test.tsx:51-65`). Recommendations: a managed key takes the server's value
   over any local draft, and the connection check ignores the draft (`AdminRecommendations.tsx:379-392`, `:478-483`).

## Native mechanism

| Part | File | What it does |
|---|---|---|
| Contract | `src/SiloPlayer.Core/Api/AdminServerSettingsApi.cs` | `AdminSettingsSensitiveStatus` gains `Environment`; `ReadEnvVars` reads the map. |
| Model | `src/SiloPlayer.Core/Services/Admin/AdminEnvManagedSettings.cs` | Keys (list ∪ map), name per key, the note text. |
| Page filter | `AdminServerSettingsStore.GetSensitiveStatusAsync` | Restricts the answer to the page's keys, names included. |
| Draft rule | `AdminSettingsForm.ApplyEnvironment` | Drops drafts of managed keys, refuses to stage them again, never puts them in `CaptureSave`. |
| Presentation | `AdminSettingsRow.SetEnvironmentNote` / `RefreshHelp` / `HelpTarget` (`src/SiloPlayer/Controls/AdminSettingsUi.cs`) | A muted line under the description; the same text is added to the control's help text. |
| Text | `AdminText.cs` | `web.admin.settings.fields.managedByEnv` and `error.field.managed_by_environment` from the official catalog (neither existed). |

Contract details that differ from "none at run time":

- An absent or `null` `managed_by_env_vars` is an older server: the list alone decides, notes carry no variable name.
- A map that is not an object (an array, a string, a number, a boolean) is read like an absent one: the list alone decides, notes carry no name (official client:
  no run-time check, `useSettingsForm.ts:65`). A malformed or missing `managed_by_env` list is still an error. (Before the review below this threw and locked or blocked pages.)
- Inside the map a value that is not a usable name leaves the key managed with no name instead of refusing the answer. A usable name is a plain identifier,
  `^[A-Za-z_][A-Za-z0-9_]{0,127}$`, as every official variable name is (`internal/config/env_settings.go`); anything else (spaces, commas, a line or paragraph separator,
  a bidirectional override, a zero-width character, a non-ASCII letter, a longer name) is not shown. A key present in the map is managed even if the list omits it.
- A status read that fails after one answered keeps the last list (official `useSettingsForm.ts:25-27`); a page's first read that fails keeps that page's own "status
  unavailable" behavior. The list is the page state's `EnvManaged` (`AdminEnvManagedSettings.Resolve`); opening a page starts a new state, so a list never carries to
  another server or account.
- A key counts as managed when the list **or** the map names it. The official client reads only the map, so a key that is listed but unnamed stays editable
  there; here it stays locked, as it has been, and the server would refuse a write of it anyway.

Note text:

- Named: "Set by SILO_PUBLIC_URL in the server’s environment." (official `managedByEnv`).
- A value that lists several names ("REDIS_URL, REDIS_URL_FILE") is not a plain identifier and gets the unnamed note. The server sends one name per key (`map[string]string`;
  a secret read from a file names the `_FILE` variable), so this does not occur against it.
- Not named (older server): "Set by the environment. Change it there and restart the affected service." (official `error.field.managed_by_environment`, the text the
  official page shows when the server refuses a write of such a key). The official client shows nothing here because it leaves the field editable; showing the
  reason for a locked control is a deliberate difference.

Control state: the control is **disabled**, as the official one is. A disabled control cannot take focus or have text copied from it; the official page has
the same limit and this change does not add a read-only variant. Unlike the official row, the note is reachable by a screen reader: it is part of the editor's
`AutomationProperties.HelpText` (description plus note), and the visible line is a `TextBlock`.

## Native pages changed

| Page (controller) | Before | Now |
|---|---|---|
| General (`AdminGeneralSettingsController`, `AdminGeneralSettingsPage`) | Managed keys disabled; Redis rows and trusted proxies replaced their description with an older text ("Set by REDIS_URL", `.managed`); the rest had no note; draft of a managed key stayed dirty. | Every row keeps its description and adds the note; the Redis switch, URL and database number name `REDIS_URL`; drafts dropped; an emptied `redis.db` draft survives and is sent (official exception). |
| Email | Managed password/port/security/user/etc. replaced description by "Managed by a server environment variable."; password draft kept. | Note with the variable name for all eight keys; the typed password is dropped; the password hint is not shown while managed. |
| Downloads (settings) | Managed text appended to the description. | Note under the description. |
| Library settings | Same pattern. | Note under the description. |
| Search | Page-owned table of three variable names; other managed keys (for example the index name) showed no variable. | The server's name wins for any key; a server that names none keeps a fallback only for the provider (`SILO_SEARCH_PROVIDER`) and the URL (`SILO_MEILISEARCH_URL`), which one variable sets. The API key has none (the server may read it from `SILO_MEILISEARCH_API_KEY_FILE`), so it gets the unnamed note. The API key keeps the page's hint while managed (official `SecretField` shows `hint`; only its "saved" line is withheld). |
| Security (local network) | Description replaced. | Note under the description. |
| Storage | Description replaced; drafts of managed keys were already reset. | Note under the description; the reset now goes through the shared form rule; a managed secret shows no "saved" hint. |
| Notifications (Discord and others) | Description replaced. | Note under the description; a typed Discord secret is dropped. |
| Integrations (AI, Recommendations, Jellyfin, Audiobookshelf) | Managed fields were disabled with no explanation. | Note under the field and in its help text; the staged Jellyfin web-off goes with a dropped API-switch draft. When the status read fails the fields stay locked as before, with no note (the variable is unknown). |
| Playback | " Set by the server environment." appended to the field's error line. | Note under the description (no longer in the error line). |
| Backups (settings) | Managed fields disabled with no note. | Note under the description. |

Request bodies and URLs are unchanged, with one exception that follows the official client: a save with an emptied `redis.db` under a managed `REDIS_URL`
now sends `{"redis.db": ""}` where the old code withheld every Redis key.

## Not changed (pages in open pull requests or not on Beta)

To do after their pull requests merge: Appearance, Autoscan settings, Sign-in settings / API keys / Policy (`AdminSignInSettingsPage`), Providers, Plugins,
Prepared Files. Providers (`AdminProvidersController`) and the Sign-in controller read the same status and keep their own handling until then.

## Not established

- Logging (`server.log_level`): the official page names its variable; the native Logging page has its own settings stack and no status read, so a managed log level
  is not shown. Adding it needs a new request on that page and is left for its own change.
- Official Recommendations ignores a managed key's draft in its connection check and takes the server value over a local draft. The native Recommendations route (inside the
  Integrations page) gets the shared draft rule and the note; whether its connection check sends a managed key's value was not compared.
- Native checks for Downloads, Storage and Search sit in groups that fail on Beta for other reasons (open pull request 231). The new Storage and Search checks ran and passed
  before those known failures; the new Downloads checks sit after a known failure ("uncheck download.server_bandwidth_mbps") and did not run. Downloads is covered by unit tests
  only: the form-level tests and, since the review below, one controller test (a reload that finds a key managed and a reload whose status read fails). The earlier statement here
  that every page was covered against a real controller was wrong: only General and Integrations were, before the review.
- Opening the page again after a server or account change starts from nothing in every controller (checked by one unit test each); the shared store's snapshot is cleared by the
  authority's availability change, which the unit tests simulate with `Store.Invalidate()`, not through a real account switch.
- `web.admin.settings.fields.envStatusUnknown` exists in the catalog but no official page uses it; the native text `configure.native.settings.managedStatusUnavailable` is unchanged.
- No pixel or visual comparison with the WebUI was made. No test was run against a real server.

## Independent review and what changed (2026-11-21)

An independent review of the first version found no P1 and confirmed the shared form's guards, the Redis exception against the server, the secret handling and the note text. Its
findings, and what was done (source `f465d2d9`; tests in `tests/SiloPlayer.Tests` and the native fixtures):

| # | Finding | Result |
|---|---|---|
| 1 | A failed status re-read cleared the managed list, so managed keys became editable and were sent. | Fixed: `AdminEnvManagedSettings.Resolve(status, _state.EnvManaged)` in all eleven controllers; Integrations also records whether any read answered (`StatusKnown`) so a first failure still locks every field. One reload test per controller (status fails after an answer: key stays managed, `Set` ignored, PUT omits it) and one "opens from nothing" test per controller. |
| 2 | General: a staged Redis switch-off survived the environment taking Redis over while another draft kept the form dirty. | Fixed in `AdminGeneralSettingsPage.Render` (the override is cleared while Redis is managed, before the switch is computed). Native step in the General fixture. |
| 3 | A `managed_by_env_vars` that is not an object failed the whole status read. | Fixed in `AdminServerSettingsApi.ReadEnvVars`; the theory test now asserts the list still decides. |
| 4 | "Draft dropped on reload and the save body omits it" ran against a real controller only for General and Integrations. | Added for Email, Search, Storage, Notifications, Downloads, Library settings, Security, Playback and Backups (typed secret where the page has one). Corrected statement above. |
| 5 | Integrations: a late `Toggled` from the locked API switch could stage the web-off. | Guard added either way: `AdminIntegrationSettingsController.SetCompatibilityApi` does nothing while the API switch is managed and never stages a managed web switch; the page handler ignores events from a disabled switch or that do not change the shown value. **Whether the late event occurs was established by a native step run against the old handler (probe `envfix-probe-r2`): it did not occur when the page was opened with the environment holding the switch Off; nothing was staged.** The step stays in the fixture. |
| 6 | Variable name filter let U+2028/U+2029, bidi overrides and zero-width characters through. | Fixed: plain identifiers only (see above); a test per rejected class. |
| 7 | Search fallback named a possibly wrong variable for the API key; the hint was hidden while managed. | Fixed (see the table row for Search); unit test for the fallback, native step for the hint and both notes. |
| 8 | Backups S3 dialog built an unmanaged form. | `ApplyEnvironment(_state.EnvManaged)` added; a unit test. Note: at `f465d2d9` the environment table does set `backup.enabled`, `backup.time`, `backup.keep`, `backup.include_subtitles` and `backup.include_intro_fingerprints` (`env_settings.go`); none of them is written by the S3 dialog, and the S3 keys are not environment-settable, so the guard stays latent. |
| 9 | Native note assertion used plain `Visibility` on any TextBlock in the page. | `EnvironmentNoteProbe` now looks in the control's own row, requires the note and every ancestor up to the page visible and the note laid out with a size, and logs the reason when it fails. |
| 10 | Fixed `Task.Delay` before checks in the Downloads, Email, General, Search, Storage and Security steps. | Replaced by waits on the rendered condition. |
| 11 | Composite editors with no `HelpTarget`: does the note reach a screen reader? | Established by reading the code: `AdminSettingsRow.RefreshHelp` returns early for a composite editor without a `HelpTarget`. The Backups location rows hold no environment-settable key, so nothing is lost there. The Playback device list (`playback.hw_device`, `SILO_HW_DEVICE`) is a real case: its editor is a panel, so only hit-testing was disabled and each switch stayed enabled and unlabelled by the note. Fixed: each device switch is disabled while the key is managed and carries the row's help text (`AdminSettingsRow.HelpText`); native step in the Playback fixture. What a screen reader speaks was not run; the help text on the focusable switch was checked. |

One further defect found while running the groups: the Email page hid its password-status retry button when the last known list said the password was managed; with a kept list that
removed the only recovery after a failed read. The retry now shows whenever the read has failed.

Still open and not changed here: Logging (`server.log_level`: no status read on that page; a follow-up unit), and the pages in open pull requests (Appearance, Autoscan Settings,
Sign-in / API Keys / Policy, Providers, Plugins, Prepared Files).
