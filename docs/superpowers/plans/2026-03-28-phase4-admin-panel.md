# Phase 4: Admin Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the complete admin panel matching the Continuum web UI, including all 19 admin pages, admin API layer, admin navigation, and admin models.

**Architecture:** The admin panel lives as a sub-navigation within the existing app. Clicking "Admin" in the sidebar navigates to an AdminShellPage that contains its own sidebar (matching the web UI's AdminSidebar) and a content Frame for admin sub-pages. Each admin page follows the established MVVM pattern with a ViewModel + XAML page. A new `AdminApi.cs` wraps all `/admin/*` endpoints.

**Tech Stack:** WinUI 3, C# 12, MVVM Community Toolkit, System.Text.Json, HttpClient

**Reference:** All web UI source is in `continuum-webui-ref/src/pages/` — read each `Admin*.tsx` file for exact layout details.

---

## File Structure

### API Layer (`src/ContinuumPlayer.Core/Api/`)
- **Create:** `AdminApi.cs` — All admin API endpoint wrappers (stats, users, libraries, tasks, nodes, settings, collections, logs, history, API keys, sessions, recommendations)

### Models (`src/ContinuumPlayer.Core/Models/Admin/`)
- **Create:** `AdminStats.cs` — Dashboard statistics
- **Create:** `AdminUser.cs` — User management types (AdminUser, CreateUserRequest, UpdateUserRequest)
- **Create:** `AdminSession.cs` — Live session types
- **Create:** `AdminTask.cs` — Task/scheduler types (TaskInfo, TriggerConfig, ExecutionResult)
- **Create:** `AdminNode.cs` — Proxy/transcode node types (StreamNode, CreateNodeRequest)
- **Create:** `AdminLog.cs` — Log types (OperationalLogEntry, AuditLogEntry)
- **Create:** `AdminHistory.cs` — Playback history types
- **Create:** `AdminApiKey.cs` — API key management types
- **Create:** `AdminCollection.cs` — Library collection types
- **Create:** `AdminIP.cs` — IP tracking types

### ViewModels (`src/ContinuumPlayer/ViewModels/Admin/`)
- **Create:** `AdminDashboardViewModel.cs`
- **Create:** `AdminActivityViewModel.cs`
- **Create:** `AdminLogsViewModel.cs`
- **Create:** `AdminUsersViewModel.cs`
- **Create:** `AdminUserDetailViewModel.cs`
- **Create:** `AdminLibrariesViewModel.cs`
- **Create:** `AdminTasksViewModel.cs`
- **Create:** `AdminTaskDetailViewModel.cs`
- **Create:** `AdminNodesViewModel.cs`
- **Create:** `AdminSettingsViewModel.cs`
- **Create:** `AdminApiKeysViewModel.cs`
- **Create:** `AdminSectionsViewModel.cs`
- **Create:** `AdminCollectionsViewModel.cs`
- **Create:** `AdminHistoryViewModel.cs`
- **Create:** `AdminRecommendationsViewModel.cs`
- **Create:** `AdminMaintenanceViewModel.cs`

### Views (`src/ContinuumPlayer/Views/Admin/`)
- **Create:** `AdminShellPage.xaml` — Admin layout with sidebar + content frame
- **Create:** `AdminDashboardPage.xaml`
- **Create:** `AdminActivityPage.xaml`
- **Create:** `AdminLogsPage.xaml`
- **Create:** `AdminUsersPage.xaml`
- **Create:** `AdminUserDetailPage.xaml`
- **Create:** `AdminLibrariesPage.xaml`
- **Create:** `AdminTasksPage.xaml`
- **Create:** `AdminTaskDetailPage.xaml`
- **Create:** `AdminNodesPage.xaml`
- **Create:** `AdminSettingsPage.xaml`
- **Create:** `AdminApiKeysPage.xaml`
- **Create:** `AdminSectionsPage.xaml`
- **Create:** `AdminCollectionsPage.xaml`
- **Create:** `AdminHistoryPage.xaml`
- **Create:** `AdminRecommendationsPage.xaml`
- **Create:** `AdminMaintenancePage.xaml`

### Modified Files
- **Modify:** `src/ContinuumPlayer/App.xaml.cs` — Register AdminApi + all admin ViewModels in DI
- **Modify:** `src/ContinuumPlayer/MainWindow.xaml.cs` — Navigate to AdminShellPage instead of PlaceholderPage

---

## Sub-Project Breakdown

This plan is broken into 6 batches that can be implemented sequentially. Each batch produces working, testable software.

### Batch A: Foundation (AdminApi + Models + Shell)
### Batch B: Dashboard + Activity (Overview section)
### Batch C: Users + Logs (User management)
### Batch D: Libraries + Collections + Sections (Content section)
### Batch E: Tasks + Nodes + Settings (Server section)
### Batch F: API Keys + Recommendations + Maintenance + History

---

## Batch A: Foundation

### Task 1: Create Admin Models

**Files:**
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminStats.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminUser.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminSession.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminTask.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminNode.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminLog.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminHistory.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminApiKey.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminCollection.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminIP.cs`

- [ ] **Step 1: Create all model files**

Create each model file matching the types from the web UI's `types.ts`. Use C# records or classes with `{ get; set; }` properties. Use snake_case JSON naming (handled by the existing `JsonSerializerOptions`). All properties should use PascalCase in C# — the serializer handles conversion.

Key types per file:

**AdminStats.cs**: `AdminStats` (TotalItems, TotalFiles, TotalUsers, TotalMovies, TotalShows, ActiveStreams, TotalStorageBytes)

**AdminUser.cs**: `AdminUser` (Id, Username, Email, Role, Enabled, LibraryIds, MaxPlaybackQuality, MaxStreams, MaxTranscodes, DownloadAllowed, DownloadTranscodeAllowed, CreatedAt, UpdatedAt), `CreateUserRequest`, `UpdateUserRequest`, `AdminUserProfile`

**AdminSession.cs**: `AdminSession` (SessionId, UserId, Username, ProfileId, ProfileName, MediaFileId, MediaTitle, MediaType, SeriesName, SeasonNumber, EpisodeNumber, PosterUrl, PlayMethod, ReportingNode, NodeDisplayName, FileDuration, StartedAt, UpdatedAt, IsPaused, ClientIp, video/audio codec fields, decision fields, bitrate fields)

**AdminTask.cs**: `TaskInfo` (Key, Name, Description, Category, State, Progress, ProgressMessage, LastExecution, Triggers, NextRunAt), `TriggerConfig` (Type, IntervalMs, TimeOfDay, DayOfWeek, MaxRuntimeMs), `ExecutionResult` (Id, TaskKey, StartedAt, CompletedAt, Status, ErrorMessage, ResultData, DurationMs)

**AdminNode.cs**: `StreamNode` (Id, Name, Url, Type, Enabled, Healthy, LastHealthCheck, ActiveJobs), `CreateNodeRequest`

**AdminLog.cs**: `OperationalLogEntry` (Id, Timestamp, Level, Component, Message, RequestId, UserId, SessionId, PlaybackSessionId, ClientIp, NodeId, Attrs), `AuditLogEntry` (Id, Timestamp, ClientIp, UserId, SessionId, PlaybackSessionId, RequestId, NodeId, Method, Path, StatusCode, DurationMs), `OperationalLogListResponse`, `AuditLogListResponse`

**AdminHistory.cs**: `AdminPlaybackHistoryItem` (SessionId, UserId, Username, ProfileId, ProfileName, MediaItemId, MediaFileId, MediaTitle, MediaType, PlayMethod, StartedAt, EndedAt, WatchedSeconds, DurationSeconds, Completed)

**AdminApiKey.cs**: `AdminAPIKey` (Id, UserId, Username, Label, Key, RateTier, CreatedAt, LastUsedAt), `AdminCreateAPIKeyRequest`

**AdminCollection.cs**: `LibraryCollection` (Id, LibraryId, LibraryIds, Slug, Title, Description, CollectionType, Visibility, SortOrder, Featured, PosterUrl, BackdropUrl, SourceUrl, LastSyncStatus, LastSyncMessage, LastSyncAt, ItemCount, CreatedAt, UpdatedAt), `CreateLibraryCollectionRequest`, `LibraryCollectionSyncRun`

**AdminIP.cs**: `UserIPEntry` (ClientIp, FirstSeen, LastSeen, RequestCount), `IPUserEntry` (UserId, Username, FirstSeen, LastSeen, RequestCount)

- [ ] **Step 2: Build to verify models compile**

Run: `dotnet build src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj`

- [ ] **Step 3: Commit**

```
git add src/ContinuumPlayer.Core/Models/Admin/
git commit -m "feat: add admin panel data models"
```

### Task 2: Create AdminApi

**Files:**
- Create: `src/ContinuumPlayer.Core/Api/AdminApi.cs`

- [ ] **Step 1: Create AdminApi with all endpoint methods**

The AdminApi wraps all `/admin/*` and related endpoints. Group methods by domain. Every method follows the existing pattern in AuthApi/CatalogApi — thin wrappers around `_client.GetAsync<T>()`, `_client.PostAsync<T>()`, etc.

API endpoints to implement (reference: `hooks/queries/admin/*.ts`):

```
// Stats
GET  /api/v1/admin/stats → AdminStats
GET  /api/v1/admin/sessions → List<AdminSession>

// Users
GET  /api/v1/admin/users → List<AdminUser>
GET  /api/v1/admin/users/{id} → AdminUser
POST /api/v1/admin/users → AdminUser (body: CreateUserRequest)
PUT  /api/v1/admin/users/{id} → AdminUser (body: UpdateUserRequest)
DELETE /api/v1/admin/users/{id}
GET  /api/v1/admin/users/{userId}/profiles → List<AdminUserProfile>

// Libraries (admin management)
GET  /api/v1/libraries → List<Library> (reuse existing)
POST /api/v1/libraries → Library
PUT  /api/v1/libraries/{id} → Library
DELETE /api/v1/libraries/{id}
POST /api/v1/scan → ScanResponse (body: {library_id})
POST /api/v1/admin/tasks/scan_libraries/run
POST /api/v1/libraries/{id}/check-mount
POST /api/v1/libraries/{id}/refresh-metadata
GET  /api/v1/libraries/skipped-roots

// Tasks
GET  /api/v1/admin/tasks → List<TaskInfo>
GET  /api/v1/admin/tasks/{key} → TaskInfo
GET  /api/v1/admin/tasks/{key}/history → List<ExecutionResult>
POST /api/v1/admin/tasks/{key}/run
POST /api/v1/admin/tasks/{key}/cancel
PUT  /api/v1/admin/tasks/{key}/triggers → TaskInfo (body: List<TriggerConfig>)

// Nodes
GET  /api/v1/admin/nodes → List<StreamNode>
POST /api/v1/admin/nodes → StreamNode (body: CreateNodeRequest)
PUT  /api/v1/admin/nodes/{id} → StreamNode
DELETE /api/v1/admin/nodes/{id}
POST /api/v1/admin/nodes/{id}/check

// Settings
GET  /api/v1/admin/settings → Dictionary<string, string>
PUT  /api/v1/admin/settings/{key} (body: {value})
GET  /api/v1/admin/settings/sensitive-status

// API Keys
GET  /api/v1/admin/api-keys → List<AdminAPIKey>
POST /api/v1/admin/api-keys → AdminAPIKey (body: AdminCreateAPIKeyRequest)
DELETE /api/v1/admin/api-keys/{id}
PUT  /api/v1/admin/api-keys/{id}/tier (body: {rate_tier})

// Playback History
GET  /api/v1/admin/playback-history → List<AdminPlaybackHistoryItem>

// Logs
GET  /api/v1/admin/logs/app → OperationalLogListResponse
GET  /api/v1/admin/logs/audit → AuditLogListResponse

// IPs
GET  /api/v1/admin/users/{userId}/ips → List<UserIPEntry>
GET  /api/v1/admin/ips?ip={ip} → List<IPUserEntry>

// Collections
GET  /api/v1/admin/collections → List<LibraryCollection>
POST /api/v1/admin/collections → LibraryCollection
PUT  /api/v1/admin/collections/{id} → LibraryCollection
DELETE /api/v1/admin/collections/{id}
POST /api/v1/admin/collections/{id}/sync → LibraryCollectionSyncRun

// Sections
GET  /api/v1/admin/sections?scope={scope} → List<Section>
POST /api/v1/admin/sections → Section
PUT  /api/v1/admin/sections/{id} → Section
DELETE /api/v1/admin/sections/{id}
PUT  /api/v1/admin/sections/reorder (body: List<string> ids)
POST /api/v1/admin/sections/restore-defaults

// Recommendations
GET  /api/v1/admin/recommendations/status
POST /api/v1/admin/recommendations/embeddings/run
POST /api/v1/admin/recommendations/taste-profiles/run
POST /api/v1/admin/recommendations/cowatch/run
POST /api/v1/admin/recommendations/generate/run
```

- [ ] **Step 2: Build to verify**
- [ ] **Step 3: Commit**

### Task 3: Create AdminShellPage with sidebar navigation

**Files:**
- Create: `src/ContinuumPlayer/Views/Admin/AdminShellPage.xaml`
- Create: `src/ContinuumPlayer/Views/Admin/AdminShellPage.xaml.cs`
- Modify: `src/ContinuumPlayer/MainWindow.xaml.cs` — Update Admin_Click
- Modify: `src/ContinuumPlayer/App.xaml.cs` — Register AdminApi and admin ViewModels

- [ ] **Step 1: Create AdminShellPage XAML**

Layout: A Grid with two columns — sidebar (240px) and content frame (star).

The sidebar matches the web UI's `AdminSidebar.tsx` exactly:
- Logo/branding header (same as main sidebar)
- 4 navigation sections with headers: OVERVIEW, CONTENT, USERS, SERVER
- Navigation items with icons matching web UI (use Segoe MDL2 Assets glyphs):
  - **OVERVIEW**: Dashboard (E80F), Activity (EA62), Logs (E8FD)
  - **CONTENT**: Libraries (E8F1), Collections (E8FD), Sections (E80A)
  - **USERS**: Users (E716), Playback History (E81C)
  - **SERVER**: Scheduled Tasks (E787), Nodes (E770), Maintenance (E90F), Settings (E713), Recommendations (E735), API Keys (E8D7)
- "Back to App" button at footer that navigates back to Home
- Active item indicator (accent left bar + accent background)
- Same dark theme colors as main sidebar

- [ ] **Step 2: Create AdminShellPage code-behind**

Handle navigation item clicks to navigate the inner content Frame to the correct admin sub-page. Start by navigating all items to PlaceholderPage with the section name — individual pages will be created in subsequent batches.

- [ ] **Step 3: Register AdminApi in DI (App.xaml.cs)**

Add `services.AddSingleton<AdminApi>(sp => new AdminApi(sp.GetRequiredService<ContinuumApiClient>()));`

- [ ] **Step 4: Update MainWindow Admin_Click**

Change from `Navigate<PlaceholderPage>("Admin")` to `Navigate<AdminShellPage>()`.

- [ ] **Step 5: Build and verify**
- [ ] **Step 6: Commit**

---

## Batch B: Dashboard + Activity

### Task 4: AdminDashboardPage

**Files:**
- Create: `src/ContinuumPlayer/ViewModels/Admin/AdminDashboardViewModel.cs`
- Create: `src/ContinuumPlayer/Views/Admin/AdminDashboardPage.xaml`
- Create: `src/ContinuumPlayer/Views/Admin/AdminDashboardPage.xaml.cs`

**Reference:** `continuum-webui-ref/src/pages/AdminDashboard.tsx`

- [ ] **Step 1: Create ViewModel**

Properties: IsLoading, Stats (AdminStats), Sessions (ObservableCollection<AdminSession>), Libraries, Users. Commands: LoadCommand, RefreshCommand, ScanAllCommand, ScanLibraryCommand.

- [ ] **Step 2: Create XAML page**

Layout matching web UI:
1. **Header**: Title "Dashboard" + subtitle + "Refresh" and "Scan All Libraries" buttons (top right)
2. **Stats row**: 5 cards in horizontal StackPanel/Grid (Active Streams, Total Movies, Total Shows, Users, Storage) — each card is a bordered surface panel with icon, label, and large bold value
3. **Now Playing section** (visible when sessions exist): Grid of StreamCards showing poster, title, play method badge, user avatar, elapsed time
4. **Two-column section**: Libraries card (list with scan buttons) + Users card (table with role/status badges)
5. **Recent Activity card** (visible when sessions exist): List of recent activity items

- [ ] **Step 3: Wire up and register in DI**
- [ ] **Step 4: Build and verify**
- [ ] **Step 5: Commit**

### Task 5: AdminActivityPage

**Files:**
- Create: `src/ContinuumPlayer/ViewModels/Admin/AdminActivityViewModel.cs`
- Create: `src/ContinuumPlayer/Views/Admin/AdminActivityPage.xaml`
- Create: `src/ContinuumPlayer/Views/Admin/AdminActivityPage.xaml.cs`

**Reference:** `continuum-webui-ref/src/pages/AdminActivity.tsx`

- [ ] **Step 1: Create ViewModel**

Properties: Sessions, FilteredSessions, SearchText, PlayMethodFilter, NodeFilter, SortField, SortAscending, IPLookupText, IPLookupResults. Commands: RefreshCommand, LookupIPCommand, ClearFiltersCommand.

- [ ] **Step 2: Create XAML page**

Layout matching web UI:
1. **Header**: Title "Activity" + live session count badge + "Refresh" button
2. **IP Lookup panel**: TextBox + "Lookup" button, results table below
3. **Play method distribution**: Horizontal colored bar + method buttons with counts
4. **Filter/Search**: Search TextBox + Movies/Series toggle buttons + Clear filters
5. **Streams table**: Grid with columns User | Stream | Video | Audio | Node | Time — each row shows avatar, username, media title, codec details, decision badges (Direct=green, Remux=blue, Transcode=amber), elapsed time

- [ ] **Step 3: Wire up and register in DI**
- [ ] **Step 4: Build and verify**
- [ ] **Step 5: Commit**

---

## Batch C: Users + Logs

### Task 6: AdminUsersPage

**Reference:** `continuum-webui-ref/src/pages/AdminUsers.tsx`

Create/edit user dialog with Account/Access/Limits tabs. Users table with role badges, status badges, action buttons (history, edit, delete). "User Defaults" dialog. "Invite Codes" tab (placeholder initially).

### Task 7: AdminUserDetailPage

**Reference:** `continuum-webui-ref/src/pages/AdminUserDetail.tsx`

Detail page with Overview/Profiles/Watch History/IP History tabs. Account info panel, permissions panel, profile cards grid, history table, IP table.

### Task 8: AdminLogsPage

**Reference:** `continuum-webui-ref/src/pages/AdminLogs.tsx`

Two tabs: Application logs and Audit logs. Each has filter bar (request ID, message search, component) and scrollable log table. Row click opens detail panel (Sheet/Flyout). Playback session filter at top.

---

## Batch D: Libraries + Collections + Sections

### Task 9: AdminLibrariesPage

**Reference:** `continuum-webui-ref/src/pages/AdminLibraries.tsx`

Library table with paths, type badges, status, last scanned, action buttons (check mount, scan, refresh metadata, edit, delete). Create/edit dialog with name, enabled toggle, paths (add/remove), type dropdown, poster upload, provider chain management. Skipped roots troubleshooting section at bottom.

### Task 10: AdminCollectionsPage

**Reference:** `continuum-webui-ref/src/pages/AdminCollections.tsx`

Library picker dropdown, collections table with title, source, items, sync status, actions (sync, edit, delete). Add collection button.

### Task 11: AdminSectionsPage

**Reference:** `continuum-webui-ref/src/pages/AdminSections.tsx`

Scope tabs (Home/Library), library picker. Sections table with drag handles for reorder, title, type badge, items, featured star, enabled toggle, edit/delete actions. Add section dialog. Restore defaults button.

---

## Batch E: Tasks + Nodes + Settings

### Task 12: AdminTasksPage

**Reference:** `continuum-webui-ref/src/pages/AdminTasks.tsx`

Task rows grouped by category (Library/Metadata/System). Each shows name, last execution time, next run, state badge, progress bar when running, Run/Cancel button. Rows link to task detail page.

### Task 13: AdminTaskDetailPage

**Reference:** `continuum-webui-ref/src/pages/AdminTaskDetail.tsx`

Task detail with progress bar, schedule section showing triggers (interval/daily/weekly/startup), edit schedule dialog with add/remove trigger support. Execution history table with started, duration, status, error, result data.

### Task 14: AdminNodesPage

**Reference:** `continuum-webui-ref/src/pages/AdminNodes.tsx`

Two sections: Proxy Nodes and Transcode Nodes. Each has a table with name, URL, status, health, active jobs, last check, actions (check health, edit, delete, enable/disable toggle). Add node dialog.

### Task 15: AdminSettingsPage

**Reference:** `continuum-webui-ref/src/pages/admin-settings/AdminSettingsLayout.tsx` + 9 tab files

9 tabs: General, Playback, Scanner & Matcher, Rate Limiting, Integrations, Jellyfin Compat, Database, Storage, Log Retention. Each tab shows setting fields (text, number, toggle, duration, password, dropdown) grouped in surface-panel cards. Save bar at bottom with dirty tracking, save/discard buttons.

---

## Batch F: API Keys + Recommendations + Maintenance + History

### Task 16: AdminApiKeysPage

**Reference:** `continuum-webui-ref/src/pages/AdminApiKeys.tsx`

Table with label, user, masked key, rate tier dropdown, created/last used dates, delete action. Create dialog with label + user selector. Post-creation dialog showing full key with copy button.

### Task 17: AdminRecommendationsPage

**Reference:** `continuum-webui-ref/src/pages/AdminRecommendations.tsx`

4 job status cards (Embeddings, Taste Profiles, Co-Watch, Recommendations) with progress bars and run buttons. Embedding lock info card. Collapsible settings sections with various field types.

### Task 18: AdminMaintenancePage

**Reference:** `continuum-webui-ref/src/pages/AdminMaintenance.tsx`

Catalog export/import workflows. Export job creation with library selection, import with source selection, path rewrites, conflict mode. Job status tracking.

### Task 19: AdminHistoryPage

**Reference:** `continuum-webui-ref/src/pages/AdminPlaybackHistory.tsx`

Filter controls (user dropdown, profile dropdown, completion status, media item ID search). 3 info cards (Visible Rows, Completed, Partial). History table with media, user, profile, method badge, watch time, status badge, ended time, log links.

---

## Implementation Notes

### Styling
- All pages use the existing DarkTheme.xaml resources (AppBackgroundBrush, CardBackgroundBrush, SurfaceBrush, AccentBrush, etc.)
- Card style: `SettingsGroupStyle` pattern from SettingsPage (rounded-[1.5rem] surface panels)
- Tables: Use Grid with column definitions, alternating row backgrounds, hover states
- Badges: Bordered pills with color coding (green=success/enabled, red=error/disabled, amber=warning, blue=info)
- Headers: Same pattern as Library/Search/Favorites pages (42px bold title + 14px subtitle)

### Navigation
- AdminShellPage has its own Frame for sub-page navigation
- Back to App button returns to Home via MainWindow navigation
- Detail pages (UserDetail, TaskDetail) navigate within the admin Frame

### Data Loading Pattern
- Each ViewModel follows the existing pattern: `[RelayCommand] private async Task LoadAsync()`
- Loading state via `[ObservableProperty] private bool _isLoading`
- Error state via `[ObservableProperty] private string? _errorMessage`
- Collections via `ObservableCollection<T>`

### API Error Handling
- All admin API calls should catch `ApiException` and display error messages
- 403 errors should show "Admin access required" and navigate back
