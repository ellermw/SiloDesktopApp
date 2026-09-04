# User-Only Desktop Client and Missing-File Playback Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the desktop Admin application surface while preserving permission-gated Match/Fix Match and Quick/Complete Refresh Metadata, and align initial missing-file playback behavior with the current WebUI.

**Architecture:** Delete the Admin shell and its dependencies instead of hiding them. Extract the two retained item-maintenance workflows behind a narrow `MediaMaintenanceApi`, keep ordinary role/capability parsing, and make `PlayerService` treat initial missing-file 404s as a terminal WebUI-style attempt while evicting stale watch-detail prefetch state for a later manual retry.

**Tech Stack:** C# 12, .NET 8, WinUI 3, xUnit, protocol-v3 Silo API, libmpv, Inno Setup.

**Spec:** `docs/superpowers/specs/2026-09-03-user-only-client-admin-removal-design.md`

## Global Constraints

- Preserve all pre-existing uncommitted installer, OSC, auto-skip, and playback changes.
- The official WebUI/server reference is GitHub commit `7c1cb2d3f34e7a37d2864b63735386300e81ef31`.
- No automatic alternate-file retry may be added for an initial missing-source failure.
- Keep server-owned adaptation for valid sources unchanged.
- Keep Match/Fix Match and Quick/Complete Refresh Metadata; remove every other desktop administration action.
- Do not change the established multi-file installer architecture.
- Do not push to `main`; open an unmerged PR from `codex/user-only-desktop-client`.

---

### Task 1: Lock Down Missing-File WebUI Parity

**Files:**
- Modify: `tests/SiloPlayer.Tests/PlaybackFailureDescriptionTests.cs`
- Create: `tests/SiloPlayer.Tests/WatchDetailPrefetchCacheTests.cs`
- Create: `src/SiloPlayer.Core/Services/WatchDetailPrefetchCache.cs`
- Modify: `src/SiloPlayer/Services/PlayerService.cs`
- Modify: `src/SiloPlayer.Core/Services/PlaybackFailureDescription.cs`

**Interfaces:**
- Produces: `WatchDetailPrefetchCache<T>` with `Store`, `TryTake`, `Invalidate`, and bounded expiration behavior.
- Produces: initial HTTP 404 presentation matching the WebUI transport-level copy.
- Consumes: existing `PlaybackApi.GetWatchDetailAsync` tasks and `PlayerService` playback-start error flow.

- [ ] **Step 1: Write failing transport-copy and cache-invalidation tests**

Add a literal expectation for HTTP 404 `not_found`:

```csharp
Assert.Equal("This item is no longer available", result!.Title);
Assert.Equal(
    "The file needed to play this item can't be found right now. Go back and try another version if one is available.",
    result.Message);
Assert.False(result.CanRetry);
```

Add cache tests proving invalidation removes only the failed content and that a later fetch cannot consume the stale task:

```csharp
var cache = new WatchDetailPrefetchCache<string>(capacity: 8, lifetime: TimeSpan.FromMinutes(2));
cache.Store("movie-1", "profile-1", Task.FromResult("stale"), now);
cache.Store("movie-2", "profile-1", Task.FromResult("keep"), now);
cache.Invalidate("movie-1");
Assert.Null(await cache.TryTakeAsync("movie-1", "profile-1", now));
Assert.Equal("keep", await cache.TryTakeAsync("movie-2", "profile-1", now));
```

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PlaybackFailureDescriptionTests|FullyQualifiedName~WatchDetailPrefetchCacheTests" -nologo
```

Expected: the 404 title differs and `WatchDetailPrefetchCache<T>` does not exist.

- [ ] **Step 3: Extract the prefetch cache and integrate invalidation**

Move the private prefetch dictionary’s bounded lifetime/profile behavior into the tested cache. In the initial start catch path, when `ApiException.StatusCode == 404`, call `Invalidate(contentId)` before showing the error. Do not add a second `StartSessionAsync` call. Keep the existing `too_many_streams` retirement retry unchanged.

- [ ] **Step 4: Update the HTTP 404 presentation**

Remove the desktop-only special case that maps `Source media file is missing` to “This video…”. All HTTP 404 start failures use the WebUI transport wording; protocol-v3 terminal `source_unavailable` retains its separate terminal wording.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: PASS.

### Task 2: Extract the Retained Media-Maintenance API

**Files:**
- Create: `src/SiloPlayer.Core/Api/MediaMaintenanceApi.cs`
- Create: `src/SiloPlayer.Core/Models/MediaMaintenance/MatchModels.cs`
- Create: `src/SiloPlayer.Core/Models/MediaMaintenance/MetadataRefreshReceipt.cs`
- Create: `tests/SiloPlayer.Tests/MediaMaintenanceApiTests.cs`
- Modify: `src/SiloPlayer/Controls/MatchItemDialog.xaml.cs`
- Modify: `src/SiloPlayer/Views/ItemDetailPage.xaml.cs`
- Modify: `src/SiloPlayer/App.xaml.cs`

**Interfaces:**
- Produces: `SearchMatchesAsync(string itemId, ItemMatchSearchRequest request, CancellationToken ct)`.
- Produces: `ApplyMatchAsync(string itemId, ItemMatchApplyRequest request, CancellationToken ct)`.
- Produces: `RefreshMetadataAsync(string itemId, string mode, CancellationToken ct)` where mode is exactly `quick` or `complete`.
- Consumes: the current server endpoints already used by `AdminApi`.

- [ ] **Step 1: Write failing real HTTP-boundary tests**

Use the test project’s recording HTTP handler to assert literal method/path/body contracts:

```csharp
await api.SearchMatchesAsync("movie/1", new ItemMatchSearchRequest { Query = "Arrival" });
Assert.Equal(HttpMethod.Post, request.Method);
Assert.Equal("/api/v1/admin/items/movie%2F1/match/search", request.RequestUri!.AbsolutePath);
```

Cover apply, quick refresh, complete refresh, and propagation of a 403 `ApiException`.

- [ ] **Step 2: Run `MediaMaintenanceApiTests` and verify RED**

Run:

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~MediaMaintenanceApiTests -nologo
```

Expected: compile failure because the focused API/models do not exist.

- [ ] **Step 3: Implement the focused API and neutral DTO namespace**

Copy only the three approved endpoint contracts from `AdminApi`. Validate refresh mode before sending:

```csharp
if (mode is not ("quick" or "complete"))
    throw new ArgumentOutOfRangeException(nameof(mode));
```

- [ ] **Step 4: Migrate the two retained UI workflows and DI**

Replace `AdminApi` in `MatchItemDialog` and both metadata refresh call sites with `MediaMaintenanceApi`. Replace `Core.Models.Admin.MatchCandidate` and related match DTOs with the neutral namespace. Register only `MediaMaintenanceApi` in `App.xaml.cs`.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the Step 2 command plus `MatchItemDialogParitySourceTests`. Expected: PASS after updating that test to validate the focused API behavior rather than `AdminApi` source text.

### Task 3: Reduce Item Detail to the Approved Maintenance Exception

**Files:**
- Modify: `src/SiloPlayer/Views/ItemDetailPage.xaml.cs`
- Modify: `src/SiloPlayer/Views/ItemDetailPage.xaml`
- Delete or simplify: `src/SiloPlayer/Controls/EditMetadataDialog.cs`
- Modify: `tests/SiloPlayer.Tests/ItemDetailCurrentParityTests.cs`
- Modify: `tests/SiloPlayer.Tests/MatchItemDialogParitySourceTests.cs`
- Create: `tests/SiloPlayer.Tests/ItemMaintenanceActionPolicyTests.cs`
- Create: `src/SiloPlayer.Core/Services/ItemMaintenanceActionPolicy.cs`

**Interfaces:**
- Produces: `ItemMaintenanceActions Resolve(bool canCurateMetadata, string itemType)` returning only `CanMatch` and `CanRefreshMetadata`.
- Consumes: `AuthorizationPolicy.CanCurateMetadata`.

- [ ] **Step 1: Write failing policy tests**

```csharp
Assert.Equal(
    new ItemMaintenanceActions(CanMatch: true, CanRefreshMetadata: true),
    ItemMaintenanceActionPolicy.Resolve(true, "movie"));
Assert.Equal(
    new ItemMaintenanceActions(CanMatch: false, CanRefreshMetadata: true),
    ItemMaintenanceActionPolicy.Resolve(true, "episode"));
Assert.Equal(default, ItemMaintenanceActionPolicy.Resolve(false, "movie"));
```

- [ ] **Step 2: Run policy tests and verify RED**

Expected: missing type.

- [ ] **Step 3: Implement the policy and rebuild the More menu from it**

Keep ordinary actions unchanged. Remove View Play History, Re-detect Intro Markers, Edit Metadata, Edit Markers, Split Versions, and other administrator mutations. Retain Match Item for supported types and Refresh Metadata for curatable items, including manga.

- [ ] **Step 4: Delete orphaned dialog/action code**

Delete code and controls only after `rg` proves no retained caller. Preserve read-only Media Info only when it is part of the ordinary server-permitted detail contract; remove administrator-only media-location management.

- [ ] **Step 5: Run item-detail and maintenance tests**

Run:

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ItemDetail|FullyQualifiedName~ItemMaintenance|FullyQualifiedName~MatchItem" -nologo
```

Expected: PASS.

### Task 4: Delete the Admin Shell, Pages, View Models, and Main-Shell Hooks

**Files:**
- Delete: `src/SiloPlayer/Views/Admin/**`
- Delete: `src/SiloPlayer/ViewModels/Admin/**`
- Delete: `src/SiloPlayer/Controls/AdminCommandPaletteDialog.cs`
- Delete: `src/SiloPlayer/Controls/ServerActivityButton.xaml`
- Delete: `src/SiloPlayer/Controls/ServerActivityButton.xaml.cs`
- Modify: `src/SiloPlayer/MainWindow.xaml`
- Modify: `src/SiloPlayer/MainWindow.xaml.cs`
- Modify: `src/SiloPlayer/Helpers/DocumentTitle.cs`
- Modify: `src/SiloPlayer/App.xaml.cs`
- Delete: `tests/SiloPlayer.Tests/Admin*Tests.cs`
- Modify: `tests/SiloPlayer.Tests/MainWindowSourceTests.cs`

**Interfaces:**
- Produces: a single user-facing main shell for all authenticated accounts.
- Preserves: ordinary Notifications navigation and unread count.

- [ ] **Step 1: Add a failing project-inventory architecture test**

The xUnit project intentionally references only `SiloPlayer.Core`, not the WinUI executable, so it cannot safely load exported UI types. Add a repository-rooted architecture test that asserts the Admin page/view-model/control source trees are absent and the app project has no Admin `Page`/`Compile` items. Pair this with the compiled XAML resource inventory check after publish in Task 8.

- [ ] **Step 2: Run the architecture test and verify RED**

Expected: current Admin page/view-model types are present.

- [ ] **Step 3: Remove Admin UI trees and registrations**

Delete all Admin pages/view models and their dedicated controls. Remove DI registrations, `DocumentTitle` entries, keyboard-command palette handling, deep-link routing, Admin button layout code, Admin-shell chrome ownership branches, and activity-popover callbacks.

- [ ] **Step 4: Simplify the main shell**

Remove the Admin button, administrator server-activity overlays, and their event subscriptions/polling. Keep Notifications and user profile controls unchanged. The sidebar open/closed state must remain controlled only by the existing explicit user interaction.

- [ ] **Step 5: Remove obsolete Admin parity tests and update shell tests**

Delete tests whose product requirement has been removed. Rewrite retained shell tests around observable user navigation and absence of exported Admin types, not exact source strings.

- [ ] **Step 6: Build and run shell/architecture tests**

Run the focused tests, then:

```powershell
dotnet build src/SiloPlayer/SiloPlayer.csproj -c Release -p:Platform=x64 -nologo
```

Expected: build succeeds and architecture tests pass.

### Task 5: Remove Desktop Server Setup and Impersonation

**Files:**
- Delete: `src/SiloPlayer/Views/SetupWizardPage.xaml`
- Delete: `src/SiloPlayer/Views/SetupWizardPage.xaml.cs`
- Delete: `src/SiloPlayer/ViewModels/SetupWizardViewModel.cs`
- Delete: `src/SiloPlayer/Controls/ImpersonationBanner.xaml`
- Delete: `src/SiloPlayer/Controls/ImpersonationBanner.xaml.cs`
- Create: `src/SiloPlayer/Views/ServerSetupRequiredPage.xaml`
- Create: `src/SiloPlayer/Views/ServerSetupRequiredPage.xaml.cs`
- Create: `src/SiloPlayer.Core/Services/ServerWebUiUri.cs`
- Create: `tests/SiloPlayer.Tests/ServerWebUiUriTests.cs`
- Modify: `src/SiloPlayer.Core/Services/AuthService.cs`
- Modify: `src/SiloPlayer/MainWindow.xaml`
- Modify: `src/SiloPlayer/MainWindow.xaml.cs`
- Modify: `src/SiloPlayer/App.xaml.cs`

**Interfaces:**
- Produces: `ServerWebUiUri.FromApiBase(string apiBaseUrl)` returning the connected server origin.
- Produces: setup-required UI with Open WebUI and Retry actions.
- Removes: impersonation begin/end/persistence/restore APIs and UI.

- [ ] **Step 1: Write failing URL normalization tests**

Use literal cases for `/api/v1`, trailing slashes, and invalid input:

```csharp
[InlineData("https://silo.example/api/v1", "https://silo.example/")]
[InlineData("https://silo.example/", "https://silo.example/")]
```

- [ ] **Step 2: Verify RED, implement normalization, verify GREEN**

Run `ServerWebUiUriTests` before and after implementation.

- [ ] **Step 3: Replace setup-wizard routing**

Route setup-required state to the new page. Open the URI with Windows launcher APIs and expose Retry through the existing setup-status/auth check. Do not carry server-setting fields into the page.

- [ ] **Step 4: Remove impersonation state and UI**

Delete impersonation credential keys, persistence, restoration, return paths, banner state, and Admin return navigation. Preserve normal login, refresh-token persistence, logout, and profile selection.

- [ ] **Step 5: Run authentication, login, profile, and setup tests**

Expected: ordinary auth tests pass and no test requires impersonation or desktop server setup.

### Task 6: Remove Dedicated Admin APIs, Models, and Shared Admin Methods

**Files:**
- Delete: `src/SiloPlayer.Core/Api/AdminApi.cs`
- Delete: `src/SiloPlayer.Core/Models/Admin/**` except DTOs deliberately moved in Task 2
- Delete: `src/SiloPlayer.Core/Services/AdminLogStreamClient.cs`
- Modify: `src/SiloPlayer.Core/Api/SettingsApi.cs`
- Modify: `src/SiloPlayer.Core/Api/RequestsApi.cs`
- Delete or reduce: `src/SiloPlayer.Core/Api/PluginsApi.cs`
- Modify: `src/SiloPlayer.Core/Api/NotificationsApi.cs`
- Modify: `src/SiloPlayer.Core/Api/PlaybackApi.cs`
- Modify: user-facing view models/pages that still import `Models.Admin`
- Modify: affected tests

**Interfaces:**
- Preserves: public/user request APIs, user notification APIs, playback APIs, ordinary settings APIs, plugin-provided user navigation, and server DTO fields required for those responses.
- Removes: all unreferenced `/api/v1/admin/**` methods except the three operations encapsulated by `MediaMaintenanceApi`.

- [ ] **Step 1: Generate a reference inventory before deleting**

Use `rg` excluding `bin` and `obj` to classify every `AdminApi`, `Models.Admin`, and `/api/v1/admin/` reference as retained maintenance, deleted administration, or shared user contract.

- [ ] **Step 2: Delete dedicated dead code and prune shared APIs**

Delete only after the inventory identifies the surviving user caller. Move neutral DTOs out of `Models.Admin` where a user-facing feature genuinely requires them.

- [ ] **Step 3: Compile after each API family**

Run `dotnet build src/SiloPlayer/SiloPlayer.csproj -c Release -p:Platform=x64 -nologo` after Settings, Requests, Plugins, Notifications, and Playback cleanup so missing user dependencies are isolated.

- [ ] **Step 4: Run the full test suite**

Run:

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --no-restore -nologo
```

Expected: all retained tests pass.

### Task 7: Update Product Documentation and Add Final Architecture Guards

**Files:**
- Modify: `README.md`
- Create: `tests/SiloPlayer.Tests/UserOnlyClientArchitectureTests.cs`
- Modify or delete: current parity/audit tests that claim Admin support

**Interfaces:**
- Produces: executable guard coverage for forbidden runtime types/resources and retained maintenance actions.
- Produces: current README product boundary.

- [ ] **Step 1: Add final guard tests and verify they catch a controlled violation**

The guard enumerates source/project inventory and API registrations; Task 8 separately inventories the compiled XAML resources. Temporarily point it at one known retained path to prove it can fail, then restore the real forbidden predicates before implementation completion.

- [ ] **Step 2: Update README**

State that administration happens in the WebUI. List Match/Fix Match and Refresh Metadata as the two permission-gated exceptions. Remove current Admin parity tables/claims while leaving historical release notes historical.

- [ ] **Step 3: Run the full suite**

Expected: all tests pass with no source-presence Admin tests remaining.

### Task 8: Full Verification, Packaging, Commit, Push, and PR

**Files:**
- Modify: verification notes only if the repository already maintains them
- Build artifact: `installer/output/SiloInstaller-<version>-Setup.exe` (ignored)

**Interfaces:**
- Produces: an unmerged GitHub PR from `codex/user-only-desktop-client` to `main`.

- [ ] **Step 1: Run final tests**

```powershell
dotnet test tests/SiloPlayer.Tests/SiloPlayer.Tests.csproj -c Release --no-restore -nologo
```

- [ ] **Step 2: Publish x64 Release**

```powershell
dotnet publish src/SiloPlayer/SiloPlayer.csproj -c Release -p:Platform=x64 -nologo
```

- [ ] **Step 3: Build the established QA installer**

Run the existing `installer/build.ps1` workflow without changing its multi-file layout. Verify Authenticode status and SHA-256 for the generated installer.

- [ ] **Step 4: Inspect published resources**

Assert no `Views/Admin/*.xbf` artifacts exist. Record publish and installer sizes for comparison without claiming a predetermined gain.

- [ ] **Step 5: Review the complete diff and preserve unrelated work**

Confirm the pre-existing installer, OSC, and auto-skip changes are either deliberately included and tested or remain intact. Scan for secrets and accidental build artifacts.

- [ ] **Step 6: Commit coherent changes**

Use scoped commits for playback parity, maintenance extraction, Admin removal, and documentation where the working-tree dependency graph permits.

- [ ] **Step 7: Push and open the requested PR**

Push `codex/user-only-desktop-client`, open a PR against `main`, and do not merge it. Return the PR URL, head SHA, test count, build result, installer path/hash, and explicit runtime-QA limitations.
