# Autoscan Sources candidate — November 17, 2026

Source-only implementation in
`C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-19374284-ebfef51653ca8ba9`,
branch `t3/admin-autoscan-sources`, base
`248aafd62ed65b868c0e7356926e4d83204649cd` (Beta).
Version remains `1.2.1181-beta.1`; stamping belongs to the pipeline.
All changes are uncommitted. No push, PR, installer, installed-player or settings action.

The registered production destination is Manage › Autoscan › Sources,
`AdminRoute.AutoscanSources`, `/admin/autoscan`. `App.xaml.cs` calls
`AddAdminAutoscanSources()`. It includes the XAML activator, installer XBF allowlist,
approved administration-source entries, real-shell native fixture dispatch, controller,
bounded APIs, typed config drafts, and official English message patterns.
Activity and Settings are explicitly outside this candidate and remain queued.

Official source is pinned to
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb` in
`D:/SiloPlayer-work/server-upstream-nov14`; contracts and UI were read with `git show`.
Primary references:

- `web/src/pages/AdminAutoscan.tsx` and `pages/admin/autoscan/{SourcesPanel,SourceList,SourceDialog,SourceConfigForm,InlineConnectionPicker,PathMappingEditor,RewriteSuggestions,WebhookEndpoint,WebhookSetupStep}.tsx`.
- The same directory's `runNow.ts`, `sourceDescriptor.ts`, `sourceForm.ts`, `sourceDisplay.ts`, `sourceTargets.ts`, `webhookSetup.ts`, `useWebhookEndpoint.ts`, `webhookURL.ts`.
- `web/src/hooks/queries/useAutoscan.ts`, `web/src/api/v2/adminAutoscanSources.ts`, `adminRequests.ts`, plugin schema-form helpers and `web/src/locales/en.json`.
- `internal/apiv2/admin_autoscan_{sources,source_write,source_delete,available_sources,connections,connection_create,connection_check,settings_update,trigger,webhook_lifecycle,rewrites}.go`, the task receipt definition in `admin_tasks.go`, Requests integration projections in `admin_requests.go`, and `internal/api/handlers/autoscan_source_write.go`.

Implemented workflows:

- Global enable resends both current defaults and reports all three reschedule outcomes.
  Run now uses official visibility and a validated `autoscan_poll` / `process` task receipt;
  it never reports media scan completion.
- Source list with table/cards, naming, feeds, health, sanitized error detail, per-source
  enable, refresh/retry, true empty state, stale-data retention and explicit list caps.
- Descriptor-driven source choice and delivery choice; required/optional/no-connection
  conditions; typed schema controls, sections, conditions, defaults, validation and
  enabled-library path fills. Edit keeps type/delivery fixed and uses persistent tabs.
- Filtered saved-server picker and inline server creation/testing, including eligible
  enabled Requests servers using canonical reads and no credential recopying.
- Full-state create/edit/delete, interval/label validation, scoped legacy CephFS config
  migration, blank versus incomplete mapping validation, seeded/splittable webhook roots,
  manual mappings, and selectable provider rewrite previews with unmatched/ambiguous/covered details.
- First-source webhook creation stays open for Connect it and generates its endpoint once.
  Existing endpoints support explicit generation, copy and confirmed rotation; source
  deletion confirms separately. Instructions list only the supported Sonarr/Radarr triggers.
- Page/editor retirement cancels reads, discards late replies and clears drafts. Actions
  retain the exact page-owner lease. Writes stay serialized through reconciliation and
  are never automatically replayed, including authentication failures and ambiguous outcomes.
  A failed editor submission requires closing/reviewing before another submission.

Sources have no server ETag/revision guard: their contract is last-write-wins. Native
edit checks its detached baseline against the latest observed row and asks to reopen
when it changed. This cannot detect another admin's unobserved concurrent write; no
optimistic-concurrency guarantee is claimed. Current source readback settles state,
not the receipt of an uncertain write. Every source write uses the official seven fields;
create adds only plugin/capability identity. No whole-library scan endpoint is used.

Limits: source/connection/descriptor walks are 100 rows × 10 pages, at most 1,000 retained
rows per collection. Library configuration is bounded by the existing 1,000-library reader;
there are no media catalog reads or polling timers. Reusable Requests pickers allow at most
100 canonical rows and have a 20-second aggregate deadline. Page reload has a 45-second
aggregate deadline; individual transport calls have the existing 30-second bound and
4-MiB response cap. Writes additionally enforce the source contract's 64-KiB body limit.

Deliberate native safety adaptations: credential-bearing webhook URLs stay masked and
are exported only by explicit Copy. Secret config fields start blank, preserve saved
values unless replaced/explicitly cleared, and clear on retirement. Provider errors use
diagnostic redaction plus known-secret replacement. Autoscan transport exceptions log
only a fixed failure category. Shared schema changes preserve the two-argument constructor,
add an optional domain validator and label controls for accessibility.

The stopped writer's ten untracked files in `t3-0ba0dc5e-e66189d5b5297a58` were read, not
modified. Eight selected API/DTO/draft/visibility/test files were ported with `apply_patch`
and then corrected/extended. Its incomplete controller and report were not copied.

Authored tests, all **UNRUN**:

- Six `AdminAutoscan*Tests.cs` unit files cover pagination/caps, exact methods/bodies/statuses,
  task and settings receipts, no write replay, authority retirement, stale-read recovery,
  admission through readback, typed/conditional config, write-only secrets, aliases, mapping
  validation, provider seeding, path boundaries and URL projection/redaction.
- `AdminAutoscanSourcesNativeFixture{,.Server}.cs`, selected with
  `SILO_NATIVE_TEST_ONLY=admin-autoscan-sources`, enters through the production registration,
  real Admin shell and nested Frame. It authors global/source flows, native schema controls,
  inline connection testing/creation, mapping preview/save, first-source webhook setup,
  rotate/delete confirmations, empty/stale/retry, leave guard and authority retirement;
  wide/compact/Connect it captures are requested by the fixture.

No build, restore, unit/native test, browser/app launch, mutex acquisition or generated
output occurred. `git diff --check` is a source-only whitespace check, not runtime evidence.
Compilation, XAML activation, fixture outcomes, captures, all-DPI/accessibility and live-server
interoperability must be observed by the coordinator before acceptance. No visual parity
certification is claimed.

Acceptance commands (not executed here): inside the coordinator's authorized, deadline-bound
runtime lane, after its storage check and exactly one nonblocking `SiloOct18Build.WaitOne(0)`
with release in `finally`, build this exact source using the existing supported publish workflow.
Use one assigned output root, preserve receipts and do not run/install the user's player.

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --filter 'FullyQualifiedName~AdminAutoscan'
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --filter 'FullyQualifiedName~UserOnlyClientArchitectureTests|FullyQualifiedName~AdminManageNavigationTests|FullyQualifiedName~AccountParityBehaviorTests'
# Against the coordinator's fresh publish of this exact candidate:
pwsh -NoProfile -File tests/SiloPlayer.NativeRegressionTests/run.ps1 -AppDirectory $candidateClientDirectory -Only admin-autoscan-sources
```

Inspect every emitted capture, including the actual editor, and retain the source-to-payload
binding. The native runner itself needs the coordinator's external containment/deadline;
these commands do not grant a runtime slot or installation/publication authorization.
