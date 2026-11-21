# Admin Manage > Users — Beta handoff

Status on November 16, 2026: **app/publish/host and five native groups pass.**
Focused Users/architecture tests pass 157/157. The most recent full run remains
7,624 passed / 1 failed; that unchanged Network Access test subsequently passes
alone. This is not an all-green final full-suite claim. Users is submitted as
one scoped Beta increment, separate from Libraries, Search, Storage and viewer work.

## Source and verification baseline

The candidate deliberately builds on Beta
`bdd033c1cab7cd601d6771bb853f4b0af044d67e`. The complete original candidate
was preserved in stash `admin-users-before-beta-bdd033c1-20261116`; keep it.
Version remains pipeline-controlled 1.2.1165 (file/assembly 1.2.1165.0).

Official main was fetched directly at
`8f93594ce19723dde8a204be21ba59a24ef06e3b`; its changes after the reviewed
`17d5376c` do not alter Users contracts. The later directly confirmed
`5b3dcaa2fdfec6de2a537a2e10897352a1078689` changes only Dashboard Now Playing
and belongs to a separate increment. Verification is not attributed to newer Beta.

Evidence is retained in the worktree's
`.codex-tmp/admin-users-verification/`. Runtime uses one reusable
`D:/SiloPlayer-build/admin-users-active/artifacts` directory via
`.codex-tmp/a/U173B`, under one non-waiting SiloOct18Build lease, owned process
capture, fail-stop and actual finally release. The owner's cap is 50 GiB with
at least 50 GiB free per used drive. No Requests/viewer payload was changed.

## Executed checks

- R2: 138 focused passed / 8 failed exposed reflected JSON request serialization.
  R3: the original 146 passed; two new error-size regressions failed before repair.
- R4: focused 148/148; full 7,617 passed / 5 failed. The source allowlist omission
  was corrected without weakening the architecture assertion. Four updater cases
  later passed in Users and clean Beta; correlation with an installed updater
  is not a controlled proof of cause. The installed app was not manipulated.
- R5 (19:59:23Z–20:00:02Z): the three newly added view-as cleanup cases failed
  behaviorally, 154 passed / 3 failed. After repair, R6
  (20:01:34Z–20:05:08Z) passed focused 157/157 and full 7,625/7,625, then found
  a WinUI Grid cell compile error; its app/native did not run.
- R7 (20:06:36Z–20:09:32Z): after the UI cell type repair, focused 157/157;
  full 7,624 passed / 1 failed / 0 skipped. The sole failure was
  `AdminNetworkAccessControllerTests.OverLimitHostListIsAnExplicitProviderState`.
- R8 (20:18:37Z–20:20:37Z): exact-source/PDB reuse and that isolated test passed
  1/1, followed by app build, publish, host and source binding. Native Users
  exposed missing generated WinUI activation metadata; remaining groups did not run.
- R9 stopped at a launcher source-comparison preflight before build/native.
  Its evidence remains intact.
- R10 actual ACQ `2026-11-16T20:29:18.4597831Z`, REL
  `2026-11-16T20:32:48.1897021Z`: the only four source deltas from R8 were the
  empty Users XAML companion, InitializeComponent, installer XBF approval and
  native activation assertion. All unit/Core source stayed exact. Unit PDB
  binding, nonincremental app build/publish, host and app/host PDB binding pass.
  All five native processes exit 0 with exactly one completion marker:
  Users **147**, Manage foundation **36**, General **188**, Monitor **113**,
  Dashboard **184** PASS records (including completion/teardown markers).

The released R10 audit verifies all 2,613 source entries, 666 published files,
app/host assembly and engine binding, ten matching inline/disk capture records,
empty jobs and signalled captured members. Receipt SHA256:
`643FDEAB59C383ECA42AB082C3BC37C99101997877DB90D0D95E1399B3819EAE`.
Root audit: `users-ui-activation-r10/root-released-audit.json`.
Only this document and current-development are edited after that tested source.
The ten authored semantic negative controls remain unexecuted; no observed
RED/GREEN claim is made for those controls.

## Source and ownership

- Sole writable source: C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-d553bebd-55c8a8e555d03c8d,
  branch t3/admin-users-workflows.
- Clean prepared HEAD 3350f1b90073af39762f9be849bc5949dbc79c50 was reconciled with
  git merge --ff-only origin/beta to exact
  173c37974894fe5d52a4a4437c356c84e5c897d9. No reset or shell-created worktree.
- After the reconciliation recorded above, pipeline-owned version is 1.2.1165,
  with file/assembly version 1.2.1165.0. The current
  projects target net10.0-windows10.0.22621.0; use the repository SDK, not the
  historical .NET 8 overview as the build contract.
- Reused the preserved Users source and eight regression files from read-only
  C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-076063bb-472bfa38c5dcf3fd.
  That worktree, its evidence and its original unverified handoff remain intact.
- Official Server source is the directly fetched main object
  17d5376cea9a8d2e6c69a276a5a37fd3b88f92a8 in
  D:/SiloPlayer-work/server-upstream-nov14. Inspected exact git show objects,
  including web/src/api/v2/adminUsers.ts and contracts/api/v2/openapi.json.
  Ten embedded Users/profile/invitation/code/identity/quota request schemas
  structurally match that exact OpenAPI object. The newer Owner permissions
  normalization change does not relax the desktop's Owner-only policy guard.
- Shared integration: App.xaml.cs calls AddAdminUsers(), and the installer
  approves the generated Users XBF. The native host has one admin-users dispatch. All other new production source belongs
  to Users. Access Groups, Devices and History Import are not registered or
  implemented. Group/library metadata are bounded choices, not sibling pages.
- No Requests, audio, metadata, player or settings production activity occurred.
  No installed app, account, server, installer, publication or version edit.

## Authored surface

Users list has Users / Invitations / Invite Codes tabs, bounded continuation,
local sort/filter/page controls and the server's exact identity filter.
Account details have Overview, Profiles, Access, Sign-in, Activity, Downloads
and Preferences tabs. Nested controls include profile/PIN/preset avatars,
account and effective policy, separate quota validators, external identities,
login-session revocation, account activity/download summaries, exact setting
storage identities and profile Home-section overrides.

Invitations expose link/email creation, server-chosen expiry and status,
explicit sensitive copy, resend/replacement and revoke. Invite codes expose
client-selected code creation, usage, edit, enable/disable, top-up and delete.
No invented invitation expiry or invite-code role/group/expiry write fields.

The transport gates Owner/self/target and independent capabilities, revalidates
reviewed resources, preserves actual strong If-Match validators, serializes
foreground work and cancels authority/route retirement. Response bodies are
bounded to 4 MiB, retained collections to 1,000 rows and continuation to ten
pages. Mutation requests do not refresh authentication or replay automatically.

This integration also distinguishes definitive 412/428 refusal from an unknown
write outcome. An unknown outcome cannot unlock its retained action even after
readback: cancel, reload/reconcile and choose a separate action if necessary.
Old route failures cannot replace the successor route's notice. The collection
response type is named AdminUsersCollectionPage to avoid an ambiguous native
reference to the WinUI AdminUsersPage.

## Review corrections authored November 16

The initial bounded source review identified two defects. Both corrections
compile and pass focused coverage; the real native password dialogs also pass.
Their authored negative-control patches were not executed.

- Set password now includes the current `require_password_change` selection
  whenever an update actually includes a password, even if that selection is
  unchanged. Other unchanged edit fields and create-field inclusion retain
  their existing semantics. The native fixture submits the real dialog with
  its unchanged temporary-password default and with an explicit false choice;
  its synthetic server distinguishes an omitted flag from false and mirrors
  the pinned server's `password_change_required` behavior without echoing the
  synthetic password into account readback.
- Home reordering now assigns consistent positions to every active section
  after the swap, not just the swapped pair. The new two-direction sparse-order
  regression retains the unrelated leading section, inherited hidden state,
  saved title/configuration and removed override, and checks that neither input
  is mutated.

The five pre-repair files are retained under
`.codex-tmp/admin-users-review-repairs-20261116` with matching hashes. Source-only
patch applicability and whitespace checks are not behavioral test results.

## Regression sources

- Nine Users xUnit files, including Users-only routing, Owner/self/capability
  rules, exact preference identities, quota/profile/Home guards, all 27 write
  routes, validator changes, no mutation refresh/replay, bounded collection/body
  rejection, serialized reads, retirement, avatar safety and isolated view-as.
- Production AddAdminUsers() is exercised by AdminUsersNativeFixture, dispatched
  with SILO_NATIVE_TEST_ONLY=admin-users. It enters the real shell, opens list
  and account tabs and nested dialogs, applies one guarded synthetic update,
  loses a response after a second update, checks blocked readback/replay,
  leaves/reopens through the shell, rejects detached controls and retires role
  authority, and exercises both password requirement choices through the real
  dialog submission. Avatar fetching/decoding uses only a synthetic
  HttpMessageHandler.
- Seven preserved semantic controls plus a new unknown-outcome control are in
  manage-users-negative-controls, alongside two review-fix controls. All are
  **unapplied/unexecuted**. Compilation or missing-API failures cannot count as
  behavioral RED.

## Session cleanup and native activation

Known isolated view-as sessions now retire local access immediately and make
one bounded best-effort logout attempt using only the temporary bearer. The
five-second cleanup is independent of page/authority cancellation and never
replays. Pending reads are canceled without disposing resources prematurely.
Three regressions were observed RED in R5 and pass in R6/R7. Unknown/unreceived
impersonation receipts still cannot guarantee remote session revocation.

Users now has the same empty XAML companion/InitializeComponent activation
pattern as the other navigable native Admin pages. The installer explicitly
approves its generated XBF, and the native fixture checks generated metadata
before entering through the normal Admin shell. No direct-content workaround
replaces Frame navigation.

## Remaining limits

- The original full-suite failure and isolated pass are retained separately.
  No all-green final full-suite or complete live visual-parity claim is made.
- Native labels are English. Keyboard/Narrator, focus/leave behavior, localized
  layout and visual parity at 1440/1024/800/560 widths still need native review.
- View-as is a visible isolated temporary profile/Home/library/catalog/item
  preview with explicit return/logout, not full-app impersonated playback or
  Downloads. It does not replace the persisted desktop account.
- Device/prepared-file detours reuse existing route identities; their missing
  destination implementations belong to other increments. Downloads here is
  account summary/copy/monitor presentation, not the sibling Downloads manager.
- Full-list metadata responses are deliberately bounded and fail closed above
  the cap. Local list filtering/sorting is explicitly partial until continuation
  is complete. Avatar upload and profile allowance widgets suppressed by the
  pinned upstream admin editor are not invented.
- No live server/account interoperability, installed-player exercise, installer
  or release is certified. Root owns the shared current-development record and
  final delivery evidence after runtime verification.
