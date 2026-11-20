# Admin user resources — source-only candidate, November 17

The candidate contains 30 changed files (including new files), below the 200-file limit; no commits were created.

This uncommitted family adds Manage → Users → Access Groups, Devices and History
Import, including their descendants, editors, confirmations and server operations.
It does not register, copy or modify pending Users PR194. Existing submitted PRs
remain pipeline-owned. No commit, push, PR, version stamp, installer, installed
player, updater, settings, production or sibling-worktree action occurred.

Source: branch `t3/admin-access-groups-devices-history-import`, worktree
`C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-646d3972-071737f2cfe17c9c`, exact base
`248aafd62ed65b868c0e7356926e4d83204649cd`. Project version stays
`1.2.1181-beta.1` (file/assembly `1.2.1180.1`). Official pin is
`217ff25abc5b9d0d3dc359de63349b7dc3d1d2eb`, read with `git show` from
`D:/SiloPlayer-work/server-upstream-nov14` as the coordinator instructed.

## Official contract references

All paths below are relative to the official repository at that exact pin.

| Leaf | Official source and behavior implemented |
| --- | --- |
| Access Groups | `web/src/App.tsx`, `pages/AdminAccessGroups.tsx`, `components/admin/access-groups/{AccessGroupEditor,AccessGroupMembers,AccessGroupPermissionsSection,accessGroupDraft}`, `api/v2/accessGroups.ts`, `api/v2/adminUsers.ts`, `api/v2/adminRequests.ts`, `lib/{permissions,playback-quality,accessGroupPolicyChanges,requestAccess}.ts`; `internal/apiv2/{admin_access_groups,admin_access_group_inputs,admin_request_groups}.go` |
| Devices | `pages/AdminDevices.tsx`, `components/admin/{DeviceConnections,deviceOverrides}.tsx`, `lib/{deviceSettingGroups,inheritedSettingValue}.ts`, `hooks/queries/admin/users.ts`, `api/v2/{adminDevices,adminAccountSettings}.ts`; `internal/apiv2/{admin_devices,admin_account_settings,playback_connection}.go`; `contracts/settings/v1/manifest.json` revision 29 |
| History Import | `pages/AdminHistoryImport.tsx`, `api/v2/adminHistoryImports.ts`, `hooks/queries/admin/{history-import-admin,history-import-sources}.ts`; `internal/apiv2/{admin_history_imports,admin_history_import_lists,admin_history_import_mappings,admin_history_import_runs,history_imports}.go` |
| Text | 509 relevant ICU messages printed from the pinned `web/src/locales/en.json` AST; scoped native failure/bounds wording is separate. The 42 device-scoped manifest definitions are a scoped copy of the pin, not a replacement of the viewer's existing definitions. |

Access Groups uses `/admin/access-groups` and `/:id`. Its capability comes from
`GET /api/v2/admin/users/capabilities`, `access_groups=true`. List rows never supply
write validators. Detail GET/PUT/DELETE use `/api/v2/admin/access-groups/{id}`;
creation is POST to the collection. A default group cannot be deleted or unset;
deletion names its default destination and warns that members move immediately
while account overrides remain. `library_ids:null` means all libraries and `[]`
means none; the same distinction applies to assignable permissions. Permissions
are `metadata_curation` and `marker_edit`. Converted downloads are clamped off
when downloads are off. Profiles have minimum 1; stream/transcode/bitrate limits
use 0 for unlimited. The request approval/quota record has its own validator at
`/api/v2/admin/request-groups/{group_id}/limit` and is saved separately, after a
changed group. A limit-only retry does not resend a successful group save.
Members/add-user pickers exclude admins, preserve selection across search and
rendering pages, show inherited policy changes before a move, and use a fresh
account detail ETag for the minimal `{access_group_id}` PUT. Changed membership or
request override rejects the move. Destination/source group and request-limit
validators are rechecked; per-account failures do not silently count as moved.

Devices owns `/admin/devices` and `/:userId/:deviceId`, plus `?profile=`. Fleet
filters, saved views, account/platform/activity grouping, anomaly rules, recency,
override scopes and the profile selection derive from `AdminDevices.tsx`.
Registration metadata comes from `/api/v2/admin/devices/{user_id}/{device_id}`.
Its legacy `settings` array is never the override authority. Canonical values
come from `/api/v2/admin/users/{id}/settings/values`; their revision must stay
consistent across a bounded walk. Writes target
`.../settings/values/{key}?scope=profile_device&profile_id=...&device_id=...`
without changing the signed-in profile header. Missing device overrides display
profile/default inheritance with an explicit varying-scope hint. Stored keys
remain inspectable/resettable even if this client does not offer them as new
settings. The subtitle appearance editor reuses the existing rich native panel
with `ApplyToLocalPlayer=false`. Reset-one and reset-profile have confirmations;
profile reset sends one DELETE per captured existing key, tolerates 404, stops
at failure/unknown, and reports its completed prefix. There is no invented bulk
reset endpoint. Learned limits use the official connection DELETE route;
measurements and plan summaries remain visible. Unknown-profile registrations
are display-only; canonical device writes respect its 128-character device ID
limit, while registration detail supports the official 256-character limit.

History Import requires `available`, `guarded_configuration` and `durable_runs`
from `/api/v2/admin/history-imports/capabilities`. Source configuration is under
`/api/v2/admin/history-import-sources`; credentials, discovery, mappings and runs
are under `/api/v2/admin/history-imports`. Source and mapping editors use their
own canonical detail ETags. Source credentials are write-only: omission preserves,
empty string explicitly clears, and nonempty replaces. Changing an address with
a credential requires replacement or explicit clear. Source addresses exclude
embedded credentials, query and fragments. Plex sign-in, token replacement and
clear are reachable; credentials are cleared after attempted submission and
on close/retirement. The Plex admin-import limitations are displayed.

Discovery excludes already-mapped external users and binds the selected Silo
profile to its account, refreshing that account's profiles before creating the
mapping. Removal uses the canonical mapping and confirmation. Run/import-all
uses the official mapping/source routes, with full history by default and
Jellyfin-only 24/72/168/720-hour lookbacks, filtered by the capability maximum
(server maximum 8760 hours). A mapping's source/account/profile identity is
rechecked before starting it. Accepted runs require the exact local Location
path; responses and Retry-After drive monitoring. Accepted/active/failed bulk
outcomes remain distinct. Canceling is not terminal completion. All/admin/user
run filters, expanded counters, warnings and unmatched samples are reachable.

## Ownership, bounds and self-review

`AdminUserResourceController` uses the released shared authority/lifetime. Reads
supersede and cancel by channel; every response and subsequent UI publication
checks its original owner. Editor documents additionally bind the originating
authority instance, account, epoch and exact resource path. A second authority
at another server cannot reuse an editor merely because account/epoch match.
Writes do not queue, refresh authentication, retry or replay. 412/428 and unknown
outcomes preserve a review barrier until explicit Reload. A reload never resends
an uncertain write. Source dialogs retain non-secret fields after a rejected
save. Source transport errors suppress untrusted exception text in diagnostics.

Each response is bounded to 2 MiB and each request to 30 seconds. Configuration,
accounts, canonical settings and fleet walks stop at 20 × 100 rows / 2,000 rows;
incomplete or inconsistent walks fail instead of publishing a partial whole-set
view. Repeated cursors and duplicate identities across pages fail. Group cards
render 60 at a time; optional request summaries read at most four validators
concurrently and discard on departure or a superseding list render. Fleet anomaly
medians are computed once per read rather than sorting peers per row. Members render
10 at a time; picker/discovery/mapping rows render 50 at a time and fleet rows
render 100 at a time, with explicit Show more controls. Runs read 50 at a time,
retain at most 500, and expose explicit older-page controls. Monitoring visits at
most five active runs per tick in round-robin order, with bounded 5–60 second
Retry-After, no overlapping tick or polling during writes/review/source loading.
Departure and authority retirement close owned dialogs, clear data/secrets, stop
timers, cancel reads and detach authority subscriptions on unload. Existing shell
navigation/scrollbar/gutter behavior is reused; no slide transition is introduced.

Source self-review corrected explicit nonsatellite catalog embedding,
cross-page duplicates, canceled-read UI publication,
cross-server editor reuse, stale device/profile callbacks, partial reset counts,
separate group/limit saves, stale mapping identity before run, polling starvation,
dialog teardown and transport error redaction. This is self-review, not independent
review or runtime verification.

Native adaptations/limits: finite fleet/account/configuration bounds are tighter
than the WebUI's larger/unbounded walks; more than 2,000 entries fails explicitly.
WinUI cards, native fields/combos and responsive panes replace CSS controls. UI
layout/copy/interaction parity still requires inspection of rendered captures
against the pin; source checks cannot establish it. No live-server, all-DPI,
Narrator, phone or installed-player evidence is claimed. Future server schemas
can be displayed/reset as stored overrides; new rich controls require a later
manifest reconciliation. Existing Users links stay disabled unless a separately
integrated Users registration is present.

## Authored versus observed verification

57 focused xUnit cases are authored in `AdminUserResourcesTests`,
`AdminUserResourceControllerTests` and `AdminHistoryImportContractTests`. They cover
wire methods/paths/validators, null/empty policy, error separation, single-send
unknown outcomes, cursor/identity failure, source credentials, lookbacks,
mapping/profile binding, cancellation/bulk states, revision drift, supersession,
concurrent writes, review barriers and retirement.

`admin-user-resources` is registered in the native host. Its authored fixture
enters actual production registration → Admin shell → Frame pages; exercises a
group 412/draft/default-delete guard, canonical device/profile reads and canceled
reset confirmation, source-editor hydration/write-only fields, authority
retirement and full page/dialog unload before restoring globals. It authors six
wide/compact PNG captures (Access Groups, Devices and History Import).

**All .NET builds, unit/native tests, RED/GREEN outcomes and rendered captures are
UNOBSERVED.** This worker ran no .NET/build/test/app/native/browser runtime and
acquired no mutex. Static inspection observed a clean tracked `git diff --check`,
valid JSON resources, no missing literal domain keys and complete approved-file
entries for the new source files; these checks do not prove compilation.

The initial test-only checkpoint is preserved at
`.codex-tmp/admin-user-resources-20261117/tests-only.patch`. It predates production
source. The complete final source (including updated/additional tests) is supplied
as `complete-candidate.patch` in the same task-local directory. Neither patch is a
commit. Apply/reverse the initial checkpoint only in the coordinator's controlled
acceptance source, not over this candidate's later files.

## Coordinator acceptance commands (not executed here)

Before running anything: measure inclusive outputs/free space, preserve existing
receipts, hold `SiloOct18Build` with exactly one nonblocking `WaitOne(0)`, record
ACQ/REL and release in `finally`. Use the coordinator's task-owned process/job
containment and deadlines for every command. No waiter/retry, no installed-app
shutdown, and no installer. Reuse its single existing build/publish/host roots.

On exact base, apply `tests-only.patch`, run the focused command below, retain its
RED log, then reverse that patch. New production symbols do not exist on the base,
so this first checkpoint is expected to fail compilation; that result has not
been observed here. Apply `complete-candidate.patch`, then run the final focused
suite, full suite, nonincremental publish and matching native host. The coordinator
must bind final source/PDB and published bytes to its receipts.

```powershell
git apply --check C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-646d3972-071737f2cfe17c9c/.codex-tmp/admin-user-resources-20261117/tests-only.patch
git apply C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-646d3972-071737f2cfe17c9c/.codex-tmp/admin-user-resources-20261117/tests-only.patch
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path D:/SiloPlayer-build/artifacts --filter 'FullyQualifiedName~AdminUserResourcesTests|FullyQualifiedName~AdminUserResourceControllerTests|FullyQualifiedName~AdminHistoryImportContractTests'
git apply --reverse C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-646d3972-071737f2cfe17c9c/.codex-tmp/admin-user-resources-20261117/tests-only.patch
git apply --check C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-646d3972-071737f2cfe17c9c/.codex-tmp/admin-user-resources-20261117/complete-candidate.patch
git apply C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-646d3972-071737f2cfe17c9c/.codex-tmp/admin-user-resources-20261117/complete-candidate.patch
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path D:/SiloPlayer-build/artifacts --filter 'FullyQualifiedName~AdminUserResourcesTests|FullyQualifiedName~AdminUserResourceControllerTests|FullyQualifiedName~AdminHistoryImportContractTests'
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --artifacts-path D:/SiloPlayer-build/artifacts
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -r win-x64 --self-contained -p:Platform=x64 -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false --artifacts-path D:/SiloPlayer-build/artifacts -o D:/SiloPlayer-build/publish
dotnet build tests/SiloPlayer.NativeRegressionTests/SiloPlayer.NativeRegressionTests.csproj -c Release --artifacts-path D:/SiloPlayer-build/artifacts -p:SiloPlayerAssemblyDirectory=D:/SiloPlayer-build/native-reference -o D:/SiloPlayer-build/native-host
```

Prepare `native-reference` from those same publish bytes and the repository's
fixture Assets/Updates using the established native staging procedure. Launch
the resulting contained host with these environment values (absolute paths):

```powershell
$env:SILO_NATIVE_TEST_APP_DIRECTORY = 'D:/SiloPlayer-build/publish'
$env:SILO_NATIVE_TEST_RESULT_DIRECTORY = 'D:/SiloPlayer-build/admin-user-resources-20261117-receipts'
$env:SILO_NATIVE_TEST_THEME_PATH = 'C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-646d3972-071737f2cfe17c9c/src/SiloPlayer/Themes/DarkTheme.xaml'
$env:SILO_NATIVE_TEST_READER_FIXTURES = 'C:/Users/Michael/.t3/worktrees/SiloPlayer/t3-646d3972-071737f2cfe17c9c/tests/reader-fixtures'
$env:SILO_NATIVE_TEST_ONLY = 'admin-user-resources'
# Execute this host only through the coordinator's deadline/job containment.
& 'D:/SiloPlayer-build/native-host/SiloPlayer.NativeRegressionTests.exe'
```

Require numeric process exit 0, zero FAIL lines and the final
`PASS: native regression run completed.` sentinel. Inspect all six PNGs against
the official source at current DPI; then exercise nonempty members/add/move,
guarded delete conflicts, two-record partial save/review, device inherited/known/
unknown/structured setting edits, reset-prefix failures, source create/edit/token/
Plex login, discovery/account-profile changes, mapping removal, bulk mixed results,
run terminal/canceling/unknown/read-failure states, retained commands after reload,
authority replacement and unload. Re-run the existing Admin foundation and
architecture suites, including the XBF/approved-file inventory. Report every
observed result and limitation before accepting or submitting this source.
