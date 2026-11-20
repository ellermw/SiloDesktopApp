# Configure integrations — November 17, 2026 verified increment

## Coordinator acceptance

The initial source-only handoff below is historical. The coordinator subsequently
corrected the confirmed reviews and accepted the final product through the real native
shell. Base remains Beta 248aafd62ed65b868c0e7356926e4d83204649cd; version remains
1.2.1181-beta.1 for pipeline-owned stamping. Fresh origin/beta resolved to
73f6f99d96910f88a6db8a471c966927350a1eba before submission; no rebase was performed.

Official contracts and two-axis source reviews used
217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb. Official GitHub main was fetched directly
again as c58c0bb9394868f42d058cf8749de46921886420. The intervening changes leave
these WebUI pages and wire contracts unchanged. Server commit 20238510d adds a more
specific speech-model timestamp diagnostic; the existing bounded/redacted check-message
projection displays it without a client protocol change. Other upstream topics are not
certified by this scoped increment.

### Review corrections and regression evidence

- Provider acknowledgement collections are validated before submitted-key echo checks;
  null capability/used-by members become outcome-unknown, not an ordinary retryable failure.
- Accepted managed-web identity survives both settings reload choices. Only matching
  operation ID, kind and coherent running/terminal state settles it; unrelated idle GETs
  never clear command gating. Neither reload replays a write.
- Model suggestions require the exact usable kind; manual model entry is retained.
- Failed, non-missing managed installations can be removed after confirmation, including
  damaged installations without an installed version. Dirty version/root guards remain.
- The active managed directory and generated Jellyfin server ID are read-only projections.
  Arbitrary directory override editing is removed. The dedicated transport still supports
  the official managed web_dir PATCH field: the pinned handler accepts the canonical path
  (or empty normalization), not arbitrary paths. Controller/UI edits remain excluded.
- Canonical /admin/recommendations navigation is reachable instead of being blocked by
  the legacy unsupported-pattern list.
- External browser activation uses the shared URL policy in addition to the exact issuer
  gate; recommendation timestamps use the shared local date/time display helper.

Executed unit RED checkpoints: review-red-r1 exposed six expected failures;
review-red-r6 exposed eleven expected failures after operation-settlement/navigation
coverage was added. Native review-native-red-r5 used the unchanged r2 product and
observed all seven targeted UI failures, including both reload variants, then reported
an aggregate failure. These RED captures are retained but are not final visual evidence.
The initial r7 GREEN attempt stopped with two pre-existing tests failing after the
transport allowlist was narrowed too far. Restoring the server-supported managed-path
field, without restoring the unsupported editor or weakening tests, resolved both.

The final independent delta review found no additional P1/P2 product findings. Its
only two findings described those allowlist/test contradictions. Exact before/after
hashes were preserved during review. Final r8 differs from reviewed r7 in only the
one-line CompatibilityPatchKeys restoration; all other sealed source hashes match.

### Observed final results

Label review-green-r8, one nonblocking SiloOct18Build.WaitOne(0), PID 21300:
ACQ 2026-11-17T15:11:00.6772543Z; REL 2026-11-17T15:18:50.4920140Z.
Seventeen deadline-bounded, task-contained commands passed; finally released the lease.
The immediately preceding inclusive inventory was 30.069448607042432 GiB, below the
50 GiB global cap, and both drives passed the 50 GiB free-space floor. No cleanup ran.

- Fresh nonincremental units: 87 focused, 8,084 full, zero failures and zero skips.
  These counts include the acceptance-only source-root/Core PDB checksum assertion.
- Fresh nonincremental WinUI app build, self-contained win-x64 publish and native-host
  build passed. Existing compiler/analyzer warnings remain; builds had zero errors.
- Seven actual native selectors passed: admin-configure-integrations (79 PASS lines),
  admin-manage-foundation (36), admin-configure-general (188), admin-monitor (113),
  admin-configure-email (297), admin-configure-security (374), and
  admin-configure-network-access (346). Each logged completion, zero FAIL lines.
- All seven Integrations PNGs were opened and inspected: AI wide/compact, manual
  ChatGPT dialog, Recommendations wide/compact, Compatibility wide/compact. Text,
  controls and compact layout are visible in those captured states. This is not a
  complete live-WebUI, every-state or all-DPI visual certification.

Source manifest: 2,326 files, SHA256
1f7b7ded1b18a41e8a6d026921140044cc0fd553e3a1763fafd7073f12020c27.
Published manifest: 582 files, SHA256
fc6fba7fefcbb081424da1655d92870336a5137ddd505e25ceb5f0404bb77919.
Both manifests were reverified after native execution. Product/reference/host DLL,
PDB and PRI hashes match. The post-acceptance documentation edits do not alter the
sealed build/test inputs. Receipts, source/payload manifests, TRX files, logs and PNGs
remain at D:/SiloPlayer-build/admin-completion-integrations, label review-green-r8.

Remaining limits: synthetic server/provider/account data, no real external login or
revocation, no live-server concurrency/environment test, no Jellyfin/Audiobookshelf
client interoperability, no full IME/Narrator/all-DPI or complete visual parity claim.
Installed PID 39668 remains untouched. No installer execution, merge, stable release,
production change, cleanup or pipeline-owned PR modification occurred.

## Initial source-only handoff (historical)

- Worktree: C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-42bfca08-6411f93dbe4e3df4.
- Branch: t3/admin-integrations-leaves; unchanged base/HEAD 248aafd62ed65b868c0e7356926e4d83204649cd.
- Desktop version remains 1.2.1181-beta.1. Changes are uncommitted, source-only, and not release evidence.
- Official Server/WebUI pin supplied by the coordinator: 217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb in D:/SiloPlayer-work/server-upstream-nov14. Contracts were read from pinned Git objects, never upstream working files.
- No .NET, restore, build, tests, app/native/browser launch, installer, mutex acquisition, generated runtime output, commit, push, PR, production change, or installed-player action ran in this worker lane. Existing submitted PRs remain outside this candidate.

## Reachable workflows

### AI — /admin/settings/ai

The real registered native page exposes capability-filtered text/speech provider selectors, None and Add provider choices, manual models and provider/account model suggestions. Adding from a job retains the created provider ID and selects it for that job. The provider editor supplies server-declared type/name/URL defaults, fixed or custom capabilities, add/edit/test/save/cancel, and explicit Keep/Replace/Clear key intent. Fixed provider updates omit capabilities; Keep omits the API key and Clear sends an empty key. PasswordBox contents are cleared immediately when captured and again in finally, including failed test/save actions. Provider endpoints reject credentials, queries and fragments; mutation acknowledgements cannot publish an exact submitted-key echo. Model ID/kind exact-key filtering and safe draft ToString remain intact.

Text AI uses api_key or chatgpt authentication. Viewer translation options are off/button/auto; service tier values are empty/priority/ultrafast. Reasoning and available service tiers come from capabilities for the edited model. Readiness prevents enabling unavailable text/transcription features, while already-enabled degraded features can be disabled. Explicit model clearing is not silently replaced by a fallback.

The ChatGPT plan workflow includes status, account-specific models, an account picker, connect/reconnect/disconnect and usage-paused text recheck. A server-created attempt opens a native dialog with an explicit user-owned browser button and hidden manual callback input; no browser is opened automatically, browser state is never scraped, and no actual external login was performed. Authorization is restricted to the pinned official issuer; the manually supplied callback must match the server-declared loopback endpoint. The final state/code remains server-validated. Successful callback completion is the official bodyless 204, not JSON. Account selection/disconnect reread settings without dropping unrelated drafts. Disconnect distinguishes confirmed remote revocation from local-only removal. Paused authenticated accounts remain selectable and testable, not feature-ready.

### Recommendations — /admin/recommendations

Settings commit individually on selection/toggle or text blur; an explicit footer save remains available. Unrelated dirty fields survive field-only writes and in-flight edits survive acknowledgements. The status panel includes all four jobs, running/count/total state, full and partial run timestamps, errors and numeric result counts; stale-profile sweep pending/history; cache freshness separate from job health; and the stored embedding model/dimensions lock. Four official job triggers are one-shot and described as local starts, not durable jobs. Lock conflicts and job state gate commands. Reset requires confirmation, respects the official running-job exclusions, rereads the embedding lock, and reports all four returned deletion counts, including the distinction that global cache rows remain.

### Compatibility — /admin/settings/compatibility

The eight dedicated Jellyfin fields (enabled, public_url, server_name, emulated_server_version, web_enabled, web_version, web_dir, web_install_dir) use typed PATCH /api/v2/admin/jellyfin-compat/settings with the canonical strong If-Match guard and no replay or token refresh. Session TTL settings and Audiobookshelf enablement retain the official shared settings PUT. Mixed saves PATCH first, reread the effective-settings document for a fresh canonical guard, then PUT remaining fields. An unrelated change discovered during that reread requires conflict review rather than silently rebasing the remaining write. The pinned dedicated PATCH declares but does not populate a response ETag, so the follow-up effective read is intentional.

Managed Jellyfin Web includes enable/disable, prerequisite availability, API/web state, install/update/remove, phase/progress/error, install paths, source/provenance/license/version/checksum details. Install/remove use the saved version/directory, not uncommitted editor values. Environment-owned web enablement cannot be invoked. Accepted operations retain their operation ID/state/time and remain visible/gated across stale replica reads until the matching operation is observed. If an accepted operation is never visible within 30 seconds, explicit reload/review is required. Polling observes settlement rather than treating 202 as completion, and final settlement rereads settings.

## Authority, recovery and scheduling

All domain transports use captured context, a 1 MiB response bound, 15-second read and 30-second command deadlines. GET alone may refresh an expired token. Provider DELETE remains exact-204 and one-shot; account DELETE separately reads its revocation JSON. Mutating calls never automatically retry or refresh. Malformed mutation acknowledgements and uncertain writes gate replay until explicit reload.

Destructive commands capture an action lease before showing their confirmation, recheck authority/target after it, and close dialogs on retirement. Retirement clears PasswordBoxes, callback/login state, provider editor fields, settings, account/model/domain/status state and retained private UI controls. Late noncooperative responses cannot repopulate the retired page. Visible Retry, Reload keep changes and Reload discard changes are real controls, not status text. Keep retains non-secret editor drafts but always clears passwords.

The five-second poll is a nonrepeating timer restarted only after the prior read settles; refresh requests coalesce into the same in-flight task. It never overlaps a second five-second read with a pending 15-second request. Model capabilities use a debounced edited-model refresh. Closed dialogs, unloaded pages and tracked action/read tasks are awaited by the native fixture before global services are restored; no 100 ms cleanup delay substitutes for those barriers.

## Contract self-review

Pinned WebUI references: web/src/pages/admin-settings/AISettings.tsx, AIProviders.tsx, AIModelField.tsx, ChatGPTConnection.tsx, CompatibilityProxiesSettings.tsx; web/src/pages/AdminRecommendations.tsx and AdminRecommendationsEmbedding.tsx.

Pinned wire references: internal/apiv2/admin_ai_providers.go, admin_ai_chatgpt.go, admin_recommendations.go, admin_jellyfin_compat_settings.go, admin_jellyfin_compat_status.go, admin_jellyfin_compat_web.go; internal/ai/chatgpt/service.go and internal/jellycompat/web_component.go.

Source review specifically checked api_key, off/button/auto, capability-derived reasoning/service tiers, provider create ID consumption, key omission/clear semantics, account-specific paths and manual callback 204, the dedicated compatibility PATCH body/guard, non-retry mutations, reset count names, prerequisite message/command fields, local operation IDs and full/partial recommendation runs. Registrations, the approved-source manifest and installer XBF allowlist include the actual native workflow page and browser seam. These are source checks, not a compiler, test or interoperability verdict.

## Authored acceptance, not observed passes

The three integration unit classes contain 46 statically enumerated cases: 10 transport/contracts cases (including the preserved direct corrections), 31 workflow/policy cases, and 5 controller cases. AdminConfigureNavigationTests also includes the integration registration guards. Theory counts here expand inline rows only; the runtime has not enumerated or passed them.

AdminIntegrationsNativeFixture uses real registration/shell/Frame and actual commands: provider success/failure and immediate secret clearing, created-ID selection, browser seam/manual callback/account selection/disconnect/reconnect/paused-plan recheck, coalesced reads, a six-second held read crossing the five-second cadence, recommendation trigger, Retry, conflict keep/discard, retirement during confirmation, delayed auth-demotion response, unload/late response, dedicated PATCH, staged-version install gate, stale-operation receipt, progress settlement and environment-owned toggle. The six-second wait is the tested polling behavior, not cleanup. All fixture data is synthetic. Native fixture sources and their barriers remain unexecuted.

### Coordinator commands

Run only in the assigned sequential runtime lane after a fresh inclusive storage inventory, within the current 50 GiB generated-output cap and at least 50 GiB free on C and D. Acquire SiloOct18Build with exactly one nonblocking WaitOne(0), record actual ACQ/REL, release in finally, and apply task-owned process containment/deadlines. Do not launch if the lane/mutex/storage gate is unavailable. Preserve frozen payloads, other worktrees and the installed player.

From this worktree, inside that coordinator wrapper:

~~~powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj --filter "FullyQualifiedName~AdminIntegrationsContractsTests|FullyQualifiedName~AdminIntegrationsWorkflowTests|FullyQualifiedName~AdminIntegrationSettingsControllerTests|FullyQualifiedName~AdminConfigureNavigationTests"
& .\tests\SiloPlayer.NativeRegressionTests\run.ps1 -AppDirectory $VerifiedCurrentSourcePublishedAppDirectory -Only admin-configure-integrations
~~~

The native command requires a published application bound to these exact final source files, not a historical payload. The runner builds its native fixture and writes generated outputs; it is not a source-only command. The coordinator must use its approved current-source build/publish procedure and account for runner outputs in the inventory. Inspect rendered captures and normal mouse/keyboard flows after execution.

Remaining acceptance limits: compilation, unit/native runtime outcomes, dispatcher/dialog ordering, rendered layout and visual parity, accessibility/IME/Narrator/all-DPI, live server concurrency/environment behavior, actual provider and external ChatGPT login/revocation, and Jellyfin/Audiobookshelf interoperability. No full visual parity or runtime completion is claimed by this handoff.
