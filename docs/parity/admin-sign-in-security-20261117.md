# Configure: Sign-in, API Keys and Policy (Access family)

Branch `t3/admin-signin-security`, merged with `origin/beta` on 2026-11-20. Project version
`1.2.1181-beta.1` is unchanged. Official reference: Silo server commit
**217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb**, read with `git show` from
`D:/SiloPlayer-work/server-upstream-nov14` (pages `SignInSettings.tsx`, `AdminApiKeys.tsx`,
`admin-policy/*`, `signInSetup.ts`, `schemaFormUtils.ts`). Configure › Security (#175) is a separate,
already merged page and is not part of this family.

## What is delivered

| Route | Page | Registration |
|---|---|---|
| `/admin/settings/sign-in` | `AdminSignInSettingsPage` + `AdminSignInController`/`AdminSignInApi` | `AddAdminSignInSecurity()` (called from `App.xaml.cs`) |
| `/admin/api-keys` | `AdminApiKeysPage` + `AdminApiKeysController`/`AdminApiKeysApi` | same |
| `/admin/policy` (`?tab=`, `?document=`) | `AdminPolicyPage` + `AdminPolicyController`/`AdminPolicyApi` | same |

All three are opened by the real Admin sidebar, search and `AdminShellPage.TryOpenHref`. `api-keys` and `policy`
were previously listed in `AdminRouteCatalog.UnsupportedPatterns`, so they could never open; they are removed
(`plugins` stays unsupported). `AdminManageNavigationTests` no longer lists `/admin/api-keys` as unsupported and
`AccessPagesAreReachableThroughTheRouteCatalog` covers the three routes. Installer approved resources
(`AdminApiKeysPage.xbf`, `AdminSignInSettingsPage.xbf`, `AdminPolicyPage.xbf`) and
`docs/parity/admin-monitor-surfaces.json` list the family.

- **Sign-in**: Silo passwords (with break-glass guard), public signups, self-service reset, locked-out note,
  invite-code link; one external provider slot (OIDC/LDAP/None) with guided setup from the plugin manifest
  (sections, conditions, Advanced, select/multi-select/array/number/switch fields, write-only secrets with
  Keep/Replace/Clear), OIDC registration addresses, "create accounts on first sign-in", staged connection test with
  test-only look-up username, turn on/off with confirmation; network sign-in; email matching; user API keys.
  One save bar saves provider configuration first, then settings, one write at a time.
- **API Keys**: availability states, bounded key table, create dialog (owner/user picker, full or selected scopes,
  label), one-time key display with Copy & Close, rate-limit editor with limiter hints and conflict reload,
  revoke confirmation, load more keys/accounts.
- **Policy**: capability gate and degraded status; Overrides (domain cards, create, per-row enable/disable
  confirmation, editor), Baseline (per-domain summaries, rating/quality ladders, Rego source), Decision Log
  (filters, paging, sample details). Editor: Rego draft, comment, validate, save as version, version history and
  source view, review/adopt newer revision, activate, enable/disable, delete, simulate, lifecycle rail.

## Review findings fixed (each has a regression test)

1. Policy labels/titles follow the adopted snapshot: title, domain note, Enable/Disable label and lifecycle rail are
   re-derived from `Controller.Snapshot` on every render through `AdminPolicyPresentation`
   (`EditorLabelsAndTitleFollowTheAdoptedSnapshot`; the native run adopts a renamed, enabled revision and asserts
   the visible title and toggle label).
2. Lists are reconciled after a create: a created policy override re-reads the canonical first page of documents
   (`CreatedPolicyRemainsInTheCollectionAfterReturningFromItsEditor`) and a created API key re-reads the first page of
   keys; if that re-read fails the one-time secret is still returned and writes stay blocked until reload
   (`CreatedKeyListIsReconciledWithTheServerCanonicalFirstPage`,
   `FailedListRefreshAfterCreateKeepsTheOneTimeSecretAndBlocksFurtherWrites`).
3. Explicit numeric `null` round-trips as `null` unless edited; a missing key still takes the declared default
   (`OptionalNumericDefaultsDoNotReplaceExplicitClears`; native run saves `{timeout:null,page_size:40}`).

## Credential and write rules (checked in code, covered by tests)

- The created key is shown once, only in the create dialog; it never enters list/editor/controller state, logs,
  exception messages or diagnostics (`AdminAccessSensitiveOption` keeps shared HTTP diagnostics from recording
  transport text), is placed on the clipboard only on Copy (excluded from clipboard history and roaming), and is
  cleared when the dialog closes, the page is left or the authority (server/account) changes.
- Error/diagnostic text is redacted before it is truncated; policy samples scrub credential fields and URL
  credentials/queries.
- Every mutation is sent once. No token refresh, no retry: unknown outcomes block further writes until the
  canonical state is re-read; a create with an unknown outcome cannot be repeated. Strong `If-Match` guards for key
  tier/revoke, policy activate/enable/delete and settings.
- Confirmations hold the lease they were opened under; a retired lease closes dialogs and never acts.
- Requests use explicit dictionary bodies (no anonymous-type reflection), bounded reads (4 MiB, 20 s), bounded lists.
- A locally rejected value (for example an empty label) never reaches the server and does not block later writes.

## Observed verification

Lane `D:/SiloPlayer-build/admin-completion-lane-b`, label `signin-final-r2`, exact committed source: focused
(`AdminAccessSecurityTests`, `AdminManageNavigationTests`) 146 passed; full unit suite 9,755 passed, 0 failed;
app build, publish, native host, native selectors `admin-signin-security` (loading, empty, unavailable, error,
populated, every dialog, create/tier/revoke/save/activate/enable/delete, failed, refused-412 and unknown-outcome
writes, authority retirement), `admin-manage-foundation`, `admin-configure-general`, `admin-monitor` all passed;
source and payload seals verified. Capture PNGs (`access-*.png`, 1440×900) were produced; the API Keys, Sign-in, Policy overrides and Policy editor captures were viewed, the others were not individually inspected.
An earlier full unit run on the same code before a fix failed three tests: `DateTimePreferenceSourceTests`
(real, fixed by using `DateTimeDisplay`) and two timing-sensitive tests in other families
(`AdminStorageSettingsControllerTests.DismissHidesOnlyTheCurrentFailedJob`,
`AdminDashboardStoreTests.LibraryActionsAllowIndependentLibrariesAndCoalesceSameLibrary`) that passed on the final run;
they were not touched.

## Deviations and limits (not claimed as parity)

- Native shell conveniences: a Reload button on each page; Policy tabs are buttons with a selected state; the
  Overrides "New override" uses a dialog where the web uses an inline form.
- Policy: the Rego editor is a plain monospace text box (no syntax colouring); version history is a button list with
  a source/activate dialog rather than the web's table with author/comment columns; the decision log uses the web's
  columns in a horizontally scrolling grid.
- API Keys: scopes are plain text, not badges; the "Copied" toast is replaced by closing the dialog; availability
  failures show the shared error with the page's Reload action instead of an inline retry link.
- Sign-in: provider choice is a radio group, not the web's radio cards; no skeleton, a "Loading…" line instead.
- Visual parity at other widths/DPIs, live OIDC/LDAP/network sign-in servers, live policy enforcement or cluster
  reload, real clipboard failure, Narrator/IME were not observed.
- Plugin page links (`/admin/plugins/...`) remain unsupported routes owned by another family.
