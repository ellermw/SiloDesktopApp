# Admin Plugins source candidate — November 17, 2026

Source-only implementation in `C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-45896fe2-35d3a1e710c69ddf`, branch `t3/admin-plugin-management`, based on Beta `248aafd62ed65b868c0e7356926e4d83204649cd`. Version remains `1.2.1181-beta.1`; no version or release changes. The candidate is uncommitted. This record describes authored source, not observed runtime behavior.

## Scope and integration

`App` calls `AddAdminPlugins()`, registering the finished XAML-backed `AdminPluginsPage` for `/admin/plugins` and its single opaque plugin-ID descendant. Old `?configure=` links are canonicalized. Navigation uses the existing shell/Frame and suppressed transitions. The page implements the shared gutter-scroll, unsaved-change and suspension contracts. The source inventory and installer XBF allowlist include this family.

Implemented source covers Installed and Catalog tabs; URL-owned independent searches and status/job filters with counts; status sorting and source groups; bounded cursor collection reads and 25/50/100-row native paging; complete empty/no-match/loading/failure/retry states; catalog preview and installed detail; catalog install and archive-file upload; enable/disable, update policy, recorded update, supervised restart and uninstall confirmation; external repository creation/edit/enable/disable/removal, with managed repositories read-only; approved-community opt-in and hide/pause confirmation; manifest configuration forms, condition/section visibility, typed values, saved-secret presence/keep/clear, connection checks and per-section saves; sign-in binding toggles and task registration toggles preserving their other fields; registered task schedule/history links; runtime/restart, update failure, signature, source, setup, description and capability/subscription details.

The management API exposes declared capabilities/subscriptions, not a grant/revoke permission editor. No permission mutation endpoint was invented. Providers settings and the Admin Plugin Apps sidebar/host/navigation belong to other workers and are unchanged. Existing public `PluginRoutePage` is unchanged. Shared edits are limited to production registration, native dispatch, approved source/XBF inventory, a plugin-only opaque-ID validation branch, transport log redaction and this development record. No pipeline-owned PR was touched.

## Official reference and contracts

Read directly using `git show` in the supplied official source checkout `D:/SiloPlayer-work/server-upstream-nov14`, at **217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb**. No fetch, clone or checkout setup was performed.

- `web/src/pages/AdminPlugins.tsx`, `AdminPluginDetail.tsx`.
- `web/src/components/admin/plugins/{PluginSourcePanels,PluginDetailSections,PluginConfigForm,SchemaForm,configSchemaAdminForm,schemaFormUtils}.tsx/ts` and `web/src/api/v2/adminPluginUpload.ts`.
- `web/src/hooks/queries/admin/plugins.ts`, `web/src/lib/{pluginPresentation,pluginStatus,pluginStatusIndicator,pluginConfigReady,pluginCapabilities,taskTrigger}.ts`, and plugin entries in `web/src/locales/en.json`.
- `internal/apiv2/admin_plugin_{installations,lifecycle,configuration,repositories,repository_create,repository_update,repository_delete,catalog_settings,uploads}.go`, `internal/apiv2/admin_tasks.go`, and `docs/admin-plugin-catalog-api.md`.

Catalog settings alone provide a strong editable revision: GET captures ETag, PUT supplies exactly that If-Match, weak/missing/wildcard validators fail closed. A 412/428 freezes editing until explicit Refresh/review; no automatic rebase or retry occurs. Lifecycle/configuration/repository contracts provide no editable revision; no fabricated validators are attached to them. Each mutation disables authentication replay and is sent once. Install/upload completion require 201; installation assignments/update/restart and connection checks require 200; configuration/auth-binding and destructive operations require their own 204. The task-run acknowledgement is 200 and is not reported as completed work. A connection check's `success:false` is a completed failed check. Restart's returned failed runtime is not reported as healthy.

Lost responses reconcile through readback without replay and retain an explicit unconfirmed notice; a readback is not relabeled as the caller's command receipt. Configuration replacements stay write-only, blank secrets are omitted to preserve saved values, and explicit optional secret clears travel in `clear_secrets`. Typed payloads are copied at command capture and cleared after completion. Authority/page retirement cancels requests, rejects retained rows/confirmations/results, clears forms/secrets and removes active content. Plugin transport failures use a fixed diagnostic marker rather than recording arbitrary exception text.

## Bounds and native adaptations

Each collection reads at most ten 100-row pages (1,000 entries); repeated/missing continuation, duplicate identity, missing page metadata and oversize bodies fail the whole collection. Exceeding the cap is explicit, never an apparently complete partial inventory. JSON responses, including errors, are capped at 4 MiB; a page refresh has a 60-second deadline, individual reads 45 seconds, writes three minutes. Uploads accept at most 256 MiB, retain one 512-KiB chunk, and have a ten-minute total deadline. Only a first-chunk 413 may open a fresh smaller-chunk session (256/128 KiB); install completion is never replayed. Failed incomplete sessions receive bounded best-effort cancellation under the original authority. All file access is to the explicitly selected file, with no staging files.

An installed detail with presentation reads only installations; it does not wait for remote catalog indexes or unrelated repository/catalog editors. A preview or older manifest without presentation reads the catalog after the installation lookup, matching the official detail-page dependency.

Update-check polling exists only on the visible page, every two seconds for at most 60 ticks, with no overlapping task read and retirement on departure. It refreshes the inventory after an observed running/cancelling-to-idle transition, unless forms/dialogs need review. Foreground mutations suppress polling. There is no background service, media-catalog walk or persistent cache.

Native adaptations: status/job chips, wrapping action rows, responsive native tile grids, and a two-column detail page that stacks below 1,100 pixels. Local paging additionally bounds native controls. The WebUI drains up to 10,000 entries; this native candidate deliberately caps at 1,000. Setup/about Markdown uses bounded native heading/list/code text, without remote image loading or executable HTML; complex Markdown styling is simplified. Resource links omit userinfo/query/fragment URLs rather than expose signed links. Core plugin labels use the pinned English strings; translated plugin-management copy and complete Markdown styling are not certified. There is no claim of visual parity, live server interoperability, all-DPI behavior, Narrator coverage, successful compilation or passing tests.

## Authored verification, all unrun

`AdminPluginsTests`, `AdminPluginsCommandTests` and `AdminPluginConfigDraftTests` author wire/controller/form regressions for malformed/paged/duplicate/capped inventories, opaque IDs, exact ETags and conflict refusal, stale authority/retained rows, single-send lost/unauthorized writes, exact command methods/bodies/statuses, managed repository protection, typed/conditional configuration, saved-secret preservation and explicit clears, safe resource links, response bounds and chunked/lost-completion uploads.

`AdminPluginsNativeFixture` is wired as `SILO_NATIVE_TEST_ONLY=admin-plugins`. It uses synthetic HTTP through **production registration → real Admin shell → real Frame → actual page/controls**, with installed paging/search, detail navigation, secret controls/config transport, enable/disable, uninstall cancellation, managed repository controls, community confirmation/ETag conflict, held-read retirement, and wide/compact PNG captures. Teardown suspends pages and waits for pages/dialogs to unload before restoring global services. The fixture has been authored, not launched; its captures do not exist yet.

Observed source-only check: `git diff --check` returned exit 0. No .NET, restore, build, test, native/browser/app, mutex, installer, plugin installation, credential access, production operation, commit, push or PR action was performed. The installed player and other worktrees remain untouched.

## Coordinator acceptance commands

These commands are **not executed**. Run only in the coordinator's assigned runtime slot, after the current hidden-inclusive output/free-space inventory, pinned native dependency validation and deadline/containment setup. Hold one `SiloOct18Build` lease across the exact-source sequence, acquired by exactly one nonblocking `WaitOne(0)`; if unavailable, do not launch. Reuse the coordinator's approved output root and release the lease in `finally`. Never run this sequence against an installed player or a frozen earlier publish.

```powershell
$PluginArtifacts = 'D:/SiloPlayer-build/admin-completion/artifacts'
$PluginPublish = 'D:/SiloPlayer-build/admin-completion/publish'
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release -p:ArtifactsPath=$PluginArtifacts --filter 'FullyQualifiedName~AdminPlugins|FullyQualifiedName~AdminPluginConfigDraft|FullyQualifiedName~UserOnlyClientArchitectureTests|FullyQualifiedName~AdminConfigureNavigationTests'
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release -p:ArtifactsPath=$PluginArtifacts
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishReadyToRun=false -p:ArtifactsPath=$PluginArtifacts -o $PluginPublish
```

After staging the *same publish bytes* as the native reference (including PRI, loose Assets and Updates), compile `tests/SiloPlayer.NativeRegressionTests/SiloPlayer.NativeRegressionTests.csproj` with `-p:SiloPlayerAssemblyDirectory=<coordinator-staged-reference>` and the same approved artifacts root. Set `SILO_NATIVE_TEST_APP_DIRECTORY` to that publish, `SILO_NATIVE_TEST_RESULT_DIRECTORY` to the coordinator's receipt directory, `SILO_NATIVE_TEST_THEME_PATH` to this checkout's `src/SiloPlayer/Themes/DarkTheme.xaml`, `SILO_NATIVE_TEST_READER_FIXTURES` to `tests/reader-fixtures`, and `SILO_NATIVE_TEST_ONLY=admin-plugins`; launch the built host under the coordinator's existing bounded process containment. The repository's `run.ps1 -AppDirectory $PluginPublish -Only admin-plugins` documents staging semantics but generates another default output/reference root, so use the coordinator's existing single-root runner. Require numeric exit 0 and the completion sentinel, inspect every resulting wide/compact capture, and bind source/PDB/publish/host hashes before declaring runtime acceptance. Then exercise synthetic installer/XBF validation and a live approved server separately if authorized; no real plugin installation is part of this source-only handoff.
