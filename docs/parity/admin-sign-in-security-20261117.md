# Configure: Sign-in, API Keys and Policy — source candidate

Prepared on `t3/admin-signin-security` in
`C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-a2ef360c-3e9abf935f484b11`.
Base: `248aafd62ed65b868c0e7356926e4d83204649cd` (Beta).
Project version remains `1.2.1181-beta.1`; numeric versions are unchanged.
Official reference: **217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb**, read with
`git show` from `D:/SiloPlayer-work/server-upstream-nov14`. That checkout's
working HEAD is newer and was not used as the contract baseline.

## Delivered source

- `/admin/settings/sign-in`: Silo passwords, public signups, self-service reset,
  break-glass status and invite-code navigation; provider selection including
  None; OIDC registration addresses, LDAP/OIDC manifest fields, conditional and
  advanced sections, secret keep/replace/clear, group/role arrays, first-sign-in
  account creation, staged connection tests and test-only lookup username;
  network sign-in and role/sharing guidance; email matching and user API keys.
  One native SaveBar stages settings and provider configuration. Provider
  configuration saves before local passwords can be disabled. Provider switches
  and network account creation apply immediately after confirmation.
- `/admin/api-keys`: capability states, bounded metadata table, account picker,
  full/selected scopes, create confirmation, one-time secret display/copy,
  canonical rate-limit editor with optional configured rate hints, conflict
  reload retaining the selected tier, and conditional revoke confirmation.
  Labels and scopes are immutable after creation in the official API; “edit”
  changes `rate_tier` only. There is no invented label/scope edit endpoint.
- `/admin/policy`: capability gate and degraded state; Overrides, Baseline and
  Decision Log tabs; domain cards, document creation, Rego draft and comments,
  validation diagnostics, immutable version save/history/source, explicit
  current-revision read/adoption, guarded activation/enable/disable/deletion,
  test input/result, baseline source/rank ladders, filtered decision pages and
  individual samples. Saving never activates. A persisted change reports local
  apply/publication results separately from cluster application.
- Actual App DI/route registrations, three XAML pages and installer XBF allowlist
  entries are present. Existing Security #175 files and registration are kept.
  Shell gutter/in-place navigation APIs are reused; no slide is added.

## Official contracts

All paths below are relative to `/api/v2`.

| Surface | Operations and guards | Pinned source |
|---|---|---|
| Sign-in settings | `GET admin/settings/effective`, `sensitive-status`, `restart-keys`; dirty-key `PUT admin/settings` with strong `If-Match`. Editable keys: `auth.local_password_login`, `signup.enabled`, `password_reset.self_service_enabled`, `auth.email_auto_match`, `auth.user_api_keys`. `server.public_url` is read-only here. | `web/src/pages/admin-settings/SignInSettings.tsx`; existing native settings API |
| Providers | `GET auth/external-sign-in/capabilities`, `GET admin/plugins/installations`; `PUT admin/plugins/installations/{id}/config` with `{key,value,clear_secrets}`; `PUT .../auth-binding` with whole `{capability_id,enabled,display_order,auto_provision,default_login}`. **Both PUTs return 204, no JSON.** No invented ETag guard. Binding/config PUTs are naturally idempotent on the server; this client still sends once and never automatically replays them. | `internal/apiv2/admin_plugin_configuration.go`; `admin_plugin_installations.go`; `web/src/hooks/queries/admin/externalSignIn.ts` |
| Provider test | `POST admin/plugins/installations/{id}/auth-binding/test`, `{capability_id,config:[{key,value,clear_secrets}]}`. Both server capability and plugin `metadata.connection_test` must permit it. `connection_test_username_field` identifies the test-only lookup. Failed steps are 200 results, not saved configuration. | `internal/apiv2/external_sign_in.go`; `web/src/pages/admin-settings/signInSetup.ts`; `SignInProviderSlot.tsx`; `useSignInProviderDrafts.ts` |
| Accounts | Bounded `GET admin/users?limit=50&cursor=…`; canonical `GET admin/users/{id}` before key writes and before disabling passwords. Self may manage self; another owner is locked; another admin requires the owner. Server still enforces final policy. | `web/src/lib/accountOwner.ts`; `externalSignInAdmin.ts`; `SignInSettings.tsx` |
| API keys | `GET admin/api-keys/capabilities`, collection `?limit=50&cursor=…`, canonical `GET .../{id}` with strong ETag; `POST` `{label,user_id,scopes}` returns 201 with key once; `PUT .../{id}/tier` `{rate_tier}` plus exact `If-Match`; `DELETE .../{id}` plus exact `If-Match` returns 204. Supported scopes/tiers come from capabilities; empty scopes means full access. | `internal/apiv2/admin_api_keys.go`; `web/src/api/v2/adminApiKeys.ts`; `web/src/pages/AdminApiKeys.tsx`; `docs/api-keys-api.md` |
| Policy | `GET policy/capability` gates all editor/storage/log reads; `GET admin/policy/vendor`; paged documents/versions; canonical document GET with strong ETag; document POST `{domain,name}`; versions POST `{source,comment?}`; single-version GET includes source. Source ≤262144, comment ≤10000, trimmed name 1–500; domains `scope`, `permission`, `action`. | `internal/apiv2/{admin_policy.go,admin_policy_types.go,policy_capability.go}`; `web/src/pages/admin-policy/*` |
| Policy mutations/test/log | Exact document `If-Match` on active-version PUT `{version_id}`, document PATCH `{enabled}` and DELETE. A document with any active version cannot be deleted, including when disabled. Validation POST `{domain,source}`; simulate POST `{domain,source?,input}` preserves arbitrary JSON including null; decisions GET uses `decision_name,user_id,allowed,from,to,limit,cursor`; single decision GET obtains samples. | Same native handlers; `web/src/hooks/queries/admin/policy.ts`; `PolicyRevisionReview.tsx`; `PolicyDecisionLogTable.tsx` |

The 384 added official messages are derived from the pinned FormatJS English
catalog. Native controls render the rich text as plain/accessibility text.

## Authority, lifetime and bounded work

Each page uses the existing acting-admin authority and a page-owned lease.
Every transport checks before sending and after reads. Retirement cancels work,
closes dialogs, clears controls/drafts and rejects late results. There is one
in-flight operation per page; writes are not queued, refreshed or resent.
Uncertain writes and failed guarded mutations block another write until explicit
reload/review. Confirmations retain the page owner and reviewed resource.

The new security domain transport caps JSON at 4 MiB with a 20-second deadline;
the existing shared settings transport retains its 8 MiB/15-second bounds. List pages
are 50 rows (decisions: 25); retained key/account/document/version lists cap at
1,000; decision history caps at 100 cursors and retains only the current page.
Plugin inventory is one official unpaged response capped at 500 installations;
vendor modules cap at 100. No polling or background catalog walk is added.
Additional client input caps: key labels 4,096 characters and plugin fields
65,536 or their smaller declared maximum. These are client resource limits,
not claimed server validation limits.

New secrets live only in page-owned memory; the created key never enters list,
editor, settings store, docs or logs. Only explicit Copy places it on the
clipboard. Blank provider secrets keep the saved secret; `clear_secrets` is
explicit. Staged-test echoes are redacted before retention. Policy diagnostic
samples scrub credential fields and URL credentials/queries. A focused request
option prevents shared HTTP exception diagnostics from logging a transport's
credential echo. Clearing references is not a claim of cryptographic memory
erasure.

## Authored checks and RED checkpoint

**No .NET, restore, build, test, native, app or browser runtime was run. No mutex
was acquired. RED and GREEN remain unobserved.**

The candidate changes 32 files. `AdminAccessSecurityTests.cs` authors 47 unit
cases (25 facts and 22 inline theory cases across 8 theories); none were run.
`tests/SiloPlayer.Tests/AdminAccessSecurityTests.cs` contains behavior/wire tests
for exact bodies/statuses, strong guards, malformed pages, no authentication
replay, late-result retirement, double-click serialization, owner restrictions,
uncertain-write blocks, policy capability/validation/review lifecycle, local
password safety, ordered provider/settings saves, staged-secret redaction and
diagnostic-log secrecy. Reflection deliberately lets this test-only checkpoint
compile on the Beta base and fail assertions for absent production types.

`AdminSignInSecurityNativeFixture` enters the production registration, Admin
shell and nested Frame. It authors create/show-once/tier/revoke actions, manifest
group controls and connection test, policy validation/simulation, baseline and
decision detail, authority retirement, capture generation and page/dialog
unload before restoring global services. Selection mode:
`SILO_NATIVE_TEST_ONLY=admin-signin-security`.

Preserved patch artifacts are under
`.codex-tmp/admin-signin-security-20261117/`:

- `01-tests-only.patch`: initial test-first checkpoint before production edits.
- `01b-tests-only-final.patch`: expanded final tests/native registration only.
- `02-production.patch`: all production, App/route and XBF changes; no tests.
- `03-docs.patch`: this scoped record and current-development entry.
- `review-*.patch`: separable review slices of the production candidate. These
  are review slices, not alternate disconnected deliveries: foundation comes
  first, API Keys/Sign-in/Policy depend on it, and wiring depends on all three.
  Apply `02-production.patch` for the concrete integrated candidate.

The complete working tree already contains both tests and production. To
observe RED in this same prepared checkout, first preserve the candidate and
reverse only `02-production.patch`, run the focused command below under the
coordinator's runtime lease, then reapply that patch and repeat. Do not create
another worker checkout or remove user work. Alternatively apply
`01b-tests-only-final.patch` before `02-production.patch` in the coordinator's
acceptance checkout.

## Exact acceptance commands (coordinator runtime slot only)

Use the coordinator's deadline/process-tree containment harness. Before launching
any command, remeasure inclusive generated output (≤50 GiB) and free C:/D:
space (≥50 GiB each), take `SiloOct18Build` with **one** `WaitOne(0)` attempt,
record ACQ/REL and release in `finally`. Do not queue/retry an occupied lease.
These are command arguments for that harness, not permission for this source
worker to run them. Reuse one output root; do not install or launch the installed
player.

```powershell
Set-Location -LiteralPath 'C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-a2ef360c-3e9abf935f484b11'
$acceptRoot = 'D:/SiloPlayer-build/admin-signin-security-20261117'

# RED and then GREEN on the identical test sources; deadline 900 seconds each.
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path "$acceptRoot/artifacts" --filter 'FullyQualifiedName~AdminAccessSecurityTests' --logger 'trx;LogFileName=admin-access-focused.trx' --results-directory "$acceptRoot/results"

# Complete unit suite after GREEN; deadline 1800 seconds.
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path "$acceptRoot/artifacts" --logger 'trx;LogFileName=admin-access-full.trx' --results-directory "$acceptRoot/results"

# Exact candidate app and host; deadline 1800 seconds / 900 seconds.
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -r win-x64 --self-contained true --artifacts-path "$acceptRoot/artifacts" -o "$acceptRoot/app" -p:Platform=x64 -p:PublishReadyToRun=false
dotnet build tests/SiloPlayer.NativeRegressionTests/SiloPlayer.NativeRegressionTests.csproj -c Release --artifacts-path "$acceptRoot/artifacts" -p:SiloPlayerAssemblyDirectory="$acceptRoot/app" -o "$acceptRoot/native"

# Run this built host through the containment harness, deadline 300 seconds.
$env:SILO_NATIVE_TEST_APP_DIRECTORY = "$acceptRoot/app"
$env:SILO_NATIVE_TEST_RESULT_DIRECTORY = "$acceptRoot/results/native-access"
$env:SILO_NATIVE_TEST_ONLY = 'admin-signin-security'
# The harness creates the result directory, then launches this exact executable:
& "$acceptRoot/native/SiloPlayer.NativeRegressionTests.exe"
```

Use separate result directories and repeat the same host with
`SILO_NATIVE_TEST_ONLY=admin-configure-security`, `admin-configure-general` and
`admin-configure-email` to check the preserved Security and shared infrastructure.
Restore environment variables in the runtime harness's `finally`. Inspect every
capture (`access-api-keys`, `access-sign-in`, `access-sign-in-provider`, `access-policy-editor`, `access-policy-test`,
`access-policy-baseline`, `access-policy-decisions`) at wide and compact sizes.
Compare controls, states and interactions with the pinned official WebUI; build
success or source-text assertions do not establish visual parity.

## Self-review and remaining acceptance limits

Source review corrected official 204 plugin acknowledgements, document deletion
with active versions, stale ownership, a shared transport exception logging
escape, secret-echo handling, partial-save ordering and explicit post-save policy
revision adoption. Reads remain bounded and execution guards use current
authority. No existing submitted PR was edited, rebased, monitored or fixed.

This is a complete source candidate, not an observed working binary. Compilation,
all authored tests, native capture rendering/inspection, live SSO/LDAP servers,
clipboard failures, all DPI/IME/Narrator, live policy enforcement/cluster reloads
and complete visual parity still require acceptance evidence. Rich-text widgets,
syntax coloring and browser focus behavior use native equivalents; those visual
differences must be assessed in the native pass. Navigation to Users/Plugins
reuses the route catalog and available registrations; this family does not
reimplement those pipeline-owned/other-worker pages. No commit, push, PR,
installer, installation, restart, publication or production action occurred.
