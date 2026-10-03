# Settings, identity and integrations delta — September 27, 2026

## Scope and reference

Source-only comparison of desktop **1.1.105**, commit
`3488a4942ee0d03448bd609d04334e74b963bc7d`, with official
`https://github.com/Silo-Server/silo-server` main at
**`5e49cc8d376d2aaa1a7b4601ff876895f8d82efe`**, freshly fetched by the coordinating
audit. Upstream checkout:
`D:\SiloPlayer\.codex-tmp\silo-server-audit-20260927-5e49cc8d`.
The previous reference, `d4e35ba9df416e747822c6c9f2193b89c6b7e9fb`, is 132 commits
behind this reference. `web/`, `internal/` and `contracts/` citations below are
relative to that exact upstream checkout; `src/` citations are desktop files.
Line numbers refer to these audited sources.

Read the September 24 settings verification and first-six implementation reports.
Previously fixed Account password, history import and download findings are not
reopened. Admin management intentionally opens WebUI and is outside native scope.
No app changes, installation, live API calls, credentials, production operations,
or test-suite runs were performed. This is not installed reproduction, proof of
visual parity, or identification of the deployed server revision.

## New concrete gaps

### SD1 — P1: Saving a profile can remove a newly supported rating ceiling

- Upstream `web/src/lib/profile-management.ts:51` now offers `6`, `12`, `15`
  and `16` alongside US ceilings. Its profile editor uses those options at
  `web/src/components/profiles/ProfileEditorDialog.tsx:576`.
- Desktop `src/SiloPlayer/Views/Dialogs/ProfileEditorDialog.cs:25` has only
  empty/G/PG/PG-13/R. Initialization at `:173` maps every unrecognized saved
  value to index zero, **Any content**. Save at `:557` serializes that selection;
  `src/SiloPlayer.Core/Api/AuthApi.cs:98` always includes
  `max_content_rating` in the profile body.
- Concrete trigger: create a profile with ceiling `12` in WebUI, then open it
  in desktop and save only a name/avatar change. Desktop sends an empty ceiling.
  Upstream `internal/apiv2/profiles.go:871` describes the empty clearing value
  and `:889` forwards the present field to the update service. This is an
  unintended access-policy relaxation, not merely a missing label.
- Acceptance: expose the current ceiling choices; preserve any unknown future
  value unless explicitly changed. A name-only save must preserve `6/12/15/16`
  and an unknown fixture value. Verify the resulting server policy with a
  disposable profile. No live profile was altered in this audit.

### SD2 — P1: Temporary-password accounts cannot complete native sign-in

- Upstream `internal/apiv2/account.go:29` defines
  `password_change_required`. The middleware at
  `internal/api/middleware/auth.go:151` rejects ordinary operations until the
  password is changed; `:170` permits only account/password/logout routes for
  v2. WebUI's `web/src/pages/ChoosePassword.tsx:76` changes the temporary
  password, then `:56` settles the session; token refresh is implemented at
  `web/src/hooks/useAuth.tsx:595`.
- Desktop `src/SiloPlayer.Core/Models/Auth/LoginResponse.cs:9` has no property
  for this restriction. `src/SiloPlayer/ViewModels/LoginViewModel.cs:368`
  treats login as ordinary success and
  `src/SiloPlayer/Views/LoginPage.xaml.cs:53` unconditionally navigates to
  profile selection. That page requires an operation outside the restricted
  allowlist. A source-wide search found no temporary-password route/handler.
- The Account settings form added in 1.1.104 is real, but does not solve a
  pre-profile login restriction. Add a restricted sign-in state and reachable
  choose-password flow, then refresh the tokens before normal navigation.
  Cover fresh login, restored restricted sessions, password-change failure and
  successful change followed by refresh failure without resubmitting the old
  temporary password. Source confirms the incompatible flow; exact installed
  error presentation remains untested.

### SD3 — P2: Self-service password recovery has no desktop entry point

- WebUI checks reset availability and local-provider eligibility at
  `web/src/pages/Login.tsx:106` and `:257`, and renders Forgot password at
  `:318`. `web/src/hooks/queries/passwordReset.ts:7` reads
  `/api/v2/capabilities/password-reset`.
- The actual contracts are implemented in
  `web/src/api/v2/publicPasswordResets.ts:8` (lookup), `:34` (complete), and
  `:68` (request). Completion distinguishes signed-in from sign-in-required
  outcomes and does not replay a consumed link.
- Desktop login controls at `src/SiloPlayer/Views/LoginPage.xaml:167` and the
  auth API at `src/SiloPlayer.Core/Api/AuthApi.cs:10` contain ordinary login,
  signup, invitations, OAuth and device auth, but no reset capability/request/
  lookup/completion methods or recovery control; source search confirms absence.
- Acceptance: provide capability-aware recovery, either a deliberate WebUI
  handoff or native flow with matching generic acknowledgment, expired/used-link
  states and safe completion. This is distinct from changing a known password
  while already signed in, which is implemented.

### SD4 — P2: Advisory-age access and display preferences are absent

- Upstream `internal/apiv2/profiles.go:28` adds nullable `max_advisory_age`
  (1–21), and `:29` adds `require_advisory_age`; capabilities are reported at
  `:124` and `:128`. WebUI sends them only when supported
  (`web/src/lib/profile-management.ts:152`, `:191`) and describes restrictions
  in its summary (`:222`). No advisory-age limit and no requirement are distinct
  states. An advisory-age limit alone does not exclude titles with unknown age.
- Desktop profile response/request models at
  `src/SiloPlayer.Core/Models/Auth/ProfilesResponse.cs:3` and
  `src/SiloPlayer.Core/Models/Auth/CreateProfileRequest.cs:13`, the body builder
  `src/SiloPlayer.Core/Api/AuthApi.cs:92`, and editor
  `src/SiloPlayer/Views/Dialogs/ProfileEditorDialog.cs:25` do not represent these
  capabilities or values. Existing profile summaries at
  `src/SiloPlayer/Views/SettingsPage.xaml.cs:6362` omit the advisory restriction.
  These missing fields are omitted on PATCH, so this finding does **not** claim
  that editing a name clears an existing advisory-age limit.
- Separately, WebUI exposes `catalog.show_advisory_age` at
  `web/src/pages/settings/PlaybackSettings.tsx:593`; desktop settings have no
  corresponding key/control. Item-detail display is owned by the browse report.
- Acceptance: capability-aware create/edit/clear, both independent capability
  flags, all valid ages, truthful access summaries, unknown-age behavior and
  persisted display preference. Keep display preference distinct from access
  enforcement, which remains server-side.

### SD5 — P2: Desktop still offers retired profile themes and misses the shared theme

- Upstream explicitly deprecated `ui.theme`, `ui.custom_theme_vars` and
  `ui.custom_css`: `contracts/settings/v1/manifest.json:854`, `:933`, `:949`.
  Stored values are deleted by `internal/userdb/migrate.go:293`; the definitions
  remain accepted for stale clients but WebUI no longer consumes them.
- The current WebUI uses Cinema Dark plus admin server-wide customization
  (`web/src/contexts/CustomThemeProvider.tsx:25`), fetched through
  `/api/v2/theme/admin-css` (`web/src/hooks/queries/theme.ts:11`). The admin
  editor's keys are `web/src/pages/admin-settings/AppearanceSettings.tsx:68`.
  Accessibility still has profile text/contrast/date/time settings; date/time
  moved to `web/src/pages/settings/AccessibilitySettings.tsx:147`.
- Desktop still exposes Appearance and Theme Editor in
  `src/SiloPlayer/Views/SettingsPage.xaml:180` and `:208`, a multi-theme catalog
  in `src/SiloPlayer/Services/ThemeService.cs:635`, profile theme reads at
  `src/SiloPlayer/ViewModels/SettingsViewModel.cs:450`, custom overrides at
  `:498`, and profile theme writes at `:963`. Date/time remains under Appearance
  (`src/SiloPlayer/Views/SettingsPage.xaml:562`). The desktop theme API methods
  at `src/SiloPlayer.Core/Api/SettingsApi.cs:407` and `:513` cover branding and
  catalog, with no admin-css consumer found in native source.
- Effect: changes advertised as profile theme preferences no longer roam to
  current WebUI, and desktop does not follow current shared token customization.
  Reconcile the native base/theme policy and retain supported accessibility
  controls. Admin editing itself stays in WebUI; arbitrary browser CSS cannot
  be claimed to style WinUI. Establish which shared tokens map to native
  resources and verify contrast/text scale visually.

### SD6 — P2: Watch-provider ratings and dropped-show sync controls are missing

- WebUI now exposes capability-gated Import ratings, Send ratings and Sync
  dropped shows at `web/src/pages/settings/WatchProvidersSettings.tsx:807`,
  `:817`, and `:827`; imported/exported rating metrics are included at `:339`
  and `:347`. Dropped sync hides dropped shows from Home surfaces, sends drops
  after removing episodes, and watching again undrops a show (UI description
  at `:831`). Sync execution remains a server responsibility.
- Desktop `src/SiloPlayer.Core/Models/WatchProviders/WatchProviderModels.cs:23`
  lacks these capabilities, `:35` lacks their setting values and `:109` lacks
  the new run counters. `src/SiloPlayer.Core/Api/WatchProvidersApi.cs:23`
  applies only the older settings. The reachable toggle builder ends with
  scrobbling at `src/SiloPlayer/Views/SettingsPage.xaml.cs:4767`; run statistics
  at `:5027` do not account for ratings.
- Acceptance: add supported toggles/readback and accurate run totals while
  preserving the current revision-guarded PATCH path
  (`src/SiloPlayer.Core/Api/WatchProvidersApi.cs:77`). Do not infer unsupported
  capabilities as enabled or reset settings omitted by an older provider.
  Home/drop action consumers belong to the browse report.

## Carryover, kept separate from new upstream deltas

- September 24 **S2**, three-way intro skip: the desktop still reads the old
  boolean (`src/SiloPlayer/ViewModels/SettingsViewModel.cs:538`) and exposes
  the switch (`src/SiloPlayer/Views/SettingsPage.xaml:741`). The playback audit
  owns detailed mode/undo/seek behavior. This is not newly introduced here.
- September 24 **S3**, failed device-override clearing: profile save/reset still
  catch all device DELETE failures and report success
  (`src/SiloPlayer/ViewModels/SettingsViewModel.cs:866`, `:881`). The upstream
  shared writer still distinguishes missing overrides from real failure
  (`web/src/hooks/queries/profileDefaults.ts:44`, `:65`).
- Existing watch-provider coverage was overstated in the old coverage table:
  desktop has watchlist setting fields but the capability model at
  `src/SiloPlayer.Core/Models/WatchProviders/WatchProviderModels.cs:23` and
  toggle builder at `src/SiloPlayer/Views/SettingsPage.xaml.cs:4745` do not
  expose the watchlist controls that already existed before this delta.
  Upstream `web/src/pages/settings/WatchProvidersSettings.tsx:764` has them.
  Include them when completing SD6, but do not count them as new upstream work.

## Already handled, server-only, and excluded changes

- **Account password is implemented:** capability/context validation and
  submission are in `src/SiloPlayer/ViewModels/AccountPasswordViewModel.cs:26`
  and `:92`, with native form handling in
  `src/SiloPlayer/Views/SettingsPage.AccountPassword.cs:42`. No reopened S1.
- **History import fixes remain:** successful consumed-session clearing is at
  `src/SiloPlayer/ViewModels/SettingsViewModel.cs:1956`; processed display uses
  `run.Processed` at `src/SiloPlayer/Views/SettingsPage.xaml.cs:3959`.
  Upstream address-policy and sanitized run-error changes are server-side
  (`internal/apiv2/history_imports.go:520`, `:592`); the personal WebUI history
  import page is unchanged between references. No new native import defect was
  established by this bounded pass.
- **Downloads fixes remain:** metadata-derived name and staged transfer are at
  `src/SiloPlayer/Views/DownloadsPage.xaml.cs:272`, `:301`, visible save errors
  at `:316`, and delete errors at
  `src/SiloPlayer/ViewModels/DownloadsViewModel.cs:82`. Upstream artifact-link
  reconciliation (`internal/downloads/service.go:584`) and monitor exclusion
  behavior (`internal/apiv2/download_subscription_mutations.go:54`) are server
  changes, not evidence that the fixed native save path regressed. Real
  preparing/ready/transfer/monitor behavior remains runtime verification.
- **Owner is not a new role-string regression:**
  `internal/apiv2/account.go:26` still declares `admin,user`;
  `internal/apiv2/admin_users.go:55` adds a separate `is_owner` flag to admin
  account resources. The owner display label is derived at
  `web/src/lib/accountOwner.ts:50`. Desktop's acting-admin check at
  `src/SiloPlayer.Core/Services/AuthorizationPolicy.cs:26` therefore is not
  broken merely by an Owner account. Owner account protection, transfers and
  admin password resets stay in the deliberately external admin WebUI scope.
- Theme-music enable/loop preferences are new in
  `web/src/pages/settings/PlaybackSettings.tsx:526` and `:541`, absent from
  desktop. They are part of the browse report's missing detail-theme-music
  feature, not an additional independent implementation package here.

## Runtime verification still required

Use disposable accounts/profiles and controlled fixtures for rating preservation,
temporary-password recovery, reset expiry/replay, advisory capability variants,
provider toggles/revision conflicts and partial settings-save errors. Verify
effective persisted values after reload and in the other client. Compare current
settings navigation, dialogs, focus, narrow/high-DPI layout and theme/accessibility
rendering side by side. The prior Account/import/download fixes retain their
documented installed acceptance work; this source audit neither reran their test
suite nor declares those entire areas visually complete.

