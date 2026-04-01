# Continuum Player — Full Feature Parity with Continuum Server

**Date:** 2026-04-01
**Goal:** Bring the WinUI 3 desktop player to complete feature and visual parity with the Continuum web UI, matching all API integrations, UI features, and the 13-theme design system.

---

## 1. Design System & Theme Engine

### 1.1 Theme Architecture

Replace the single `DarkTheme.xaml` with a runtime-switchable theme engine. Each of the 13 Continuum themes becomes a XAML `ResourceDictionary` defining the same semantic color tokens used by the web UI.

**Theme Engine (`ThemeEngine.cs`):**
- Static class with `CurrentTheme` property and `ThemeChanged` event
- `ApplyTheme(string themeId)` swaps the merged `ResourceDictionary` at runtime
- Persists selected theme in `SettingsService` (key: `theme`)
- Falls back to `midnight-cinema` if no theme is set

**Per-theme ResourceDictionary pattern:**
Each theme XAML file defines these `SolidColorBrush` resources (37 color tokens + variants):

| Token | Purpose |
|-------|---------|
| `BackgroundBrush` | Page background (darkest layer) |
| `ForegroundBrush` | Primary text |
| `CardBrush` / `CardForegroundBrush` | Card backgrounds and text |
| `PopoverBrush` / `PopoverForegroundBrush` | Dropdown/flyout backgrounds |
| `SurfaceBrush` / `SurfaceHoverBrush` / `SurfaceRaisedBrush` | Layered surface hierarchy |
| `PrimaryBrush` / `PrimaryForegroundBrush` | Primary action color |
| `SecondaryBrush` / `SecondaryForegroundBrush` | Secondary elements |
| `MutedBrush` / `MutedForegroundBrush` | Subdued backgrounds and helper text |
| `AccentBrush` / `AccentForegroundBrush` | Active/highlighted elements |
| `DestructiveBrush` / `DestructiveForegroundBrush` | Danger/delete actions |
| `BorderBrush` | Default borders |
| `InputBrush` | Input field backgrounds |
| `RingBrush` | Focus rings |
| `Chart1Brush` through `Chart5Brush` | Chart/graph colors |
| `SidebarBrush` / `SidebarForegroundBrush` | Sidebar navigation |
| `SidebarPrimaryBrush` / `SidebarPrimaryForegroundBrush` | Active sidebar item |
| `SidebarAccentBrush` / `SidebarAccentForegroundBrush` | Sidebar hover |
| `SidebarBorderBrush` / `SidebarRingBrush` | Sidebar chrome |
| `AmbientBrush` | Dynamic artwork-extracted color (falls back to Primary) |

Each theme also defines:
- `ThemeRadius` (`CornerRadius`) — 0.5rem or 0.75rem base
- `ThemeFontFamily` (`FontFamily`) — Outfit, Manrope, Sora, or Urbanist
- `ThemeDisplayFontFamily` (`FontFamily`) — display font (same or different from body)
- `AmbientGlowOpacity` (`double`) — opacity for ambient artwork glow

### 1.2 All 13 Themes

| ID | Label | Font | Primary | Background | Curated |
|----|-------|------|---------|------------|---------|
| `midnight-cinema` | Cinema Dark | Outfit | `#e8e8ec` | `#141417` | Yes (default) |
| `cinema-light` | Cinema Light | Outfit | `#1a1a1e` | `#f4f4f6` | Yes |
| `cobalt-studio` | Cobalt | Outfit | `#78aefc` | `#101722` | Yes |
| `oxblood-noir` | Oxblood | Outfit | `#d16a78` | `#171113` | Yes |
| `evergreen-studio` | Evergreen | Outfit | `#5bc39d` | `#101715` | Yes |
| `ember-slate` | Ember | Urbanist | `#f07b62` | `#151213` | No |
| `verdant-ink` | Verdant Ink | Urbanist | `#86d4b6` | `#0d1513` | No |
| `catppuccin` | Catppuccin | Outfit | `#cba6f7` | `#1e1e2e` | No |
| `gruvbox` | Gruvbox | Manrope | `#fabd2f` | `#282828` | No |
| `void-space` | Void Space | Manrope | `#58a6ff` | `#0d1117` | No |
| `charcoal-studio` | Charcoal | Outfit | `#0a84ff` | `#1c1c1e` | No |
| `graphite-pro` | Graphite | Sora | `#a855f7` | `#18181b` | No |
| `obsidian-depth` | Obsidian | Urbanist | `#00d4aa` | `#0f0f0f` | No |

Full hex values for all 37+ tokens per theme are documented in the Continuum server source (`web/src/app.css` lines 210-811).

### 1.3 Design Tokens

Port these from the web UI's `design-system.ts`:

**Typography scale:** 2xs(10px), xs(12px), sm(14px), base(16px), lg(18px), xl(20px), 2xl(24px), 3xl(30px), 4xl(36px), 5xl(48px)

**Font weights:** Normal(400), Medium(500), SemiBold(600), Bold(700)

**Border radii:** sm(4px), md(6px), lg(8px), xl(12px), 2xl(16px), pill(9999px)

**Animation durations:** Fast(150ms), Normal(250ms), Slow(400ms), Glacial(700ms), Cinematic(1200ms)

**Easing:** Default `cubic-bezier(0.4, 0, 0.2, 1)`, Spring `cubic-bezier(0.34, 1.56, 0.64, 1)`

### 1.4 Font Bundling

Bundle four font families as application assets:
- **Outfit** — Used by 8 themes (including default)
- **Manrope** — Used by Gruvbox, Void Space
- **Sora** — Used by Graphite Pro
- **Urbanist** — Used by Obsidian Depth, Ember Slate, Verdant Ink

### 1.5 Existing View Updates

All existing views and controls must be updated to use the new semantic brush tokens instead of hardcoded colors. Every `Color=` and `Background=` reference in XAML must be replaced with `{StaticResource BackgroundBrush}` etc.

---

## 2. API & Model Layer

### 2.1 New API Modules

**`PeopleApi.cs`**
- `GetPeopleAsync(query, limit, offset)` — `GET /api/v1/people`
- `GetPersonAsync(id)` — `GET /api/v1/people/{id}`
- `RefreshPersonAsync(id)` — `POST /api/v1/people/{id}/refresh`

**`CollectionsApi.cs`** (User collections)
- `GetCollectionsAsync()` — `GET /api/v1/collections`
- `CreateCollectionAsync(request)` — `POST /api/v1/collections`
- `PreviewCollectionAsync(request)` — `POST /api/v1/collections/preview`
- `UpdateCollectionAsync(id, request)` — `PUT /api/v1/collections/{id}`
- `DeleteCollectionAsync(id)` — `DELETE /api/v1/collections/{id}`
- `GetCollectionItemsAsync(id)` — `GET /api/v1/collections/{id}/items`
- `AddCollectionItemAsync(collectionId, itemId)` — `PUT /api/v1/collections/{id}/items/{item_id}`
- `RemoveCollectionItemAsync(collectionId, itemId)` — `DELETE /api/v1/collections/{id}/items/{item_id}`

**`DownloadsApi.cs`**
- `CreateDownloadAsync(request)` — `POST /api/v1/downloads`
- `GetDownloadsAsync()` — `GET /api/v1/downloads`
- `DeleteDownloadAsync(id)` — `DELETE /api/v1/downloads/{id}`
- `GetDownloadFileAsync(id)` — `GET /api/v1/downloads/{id}/file`

**`HistoryImportApi.cs`**
- `GetImportSourcesAsync()` — `GET /api/v1/history-imports/sources`
- `EmbyConnectLoginAsync(request)` — `POST /api/v1/history-imports/emby-connect/login`
- `PlexAuthPinAsync(request)` — `POST /api/v1/history-imports/plex/auth/pin`
- `PlexAuthCheckAsync(request)` — `POST /api/v1/history-imports/plex/auth/check`
- `GetImportRunsAsync()` — `GET /api/v1/history-imports/runs`
- `CreateImportRunAsync(request)` — `POST /api/v1/history-imports/runs`
- `GetImportRunAsync(id)` — `GET /api/v1/history-imports/runs/{id}`

**`PluginsApi.cs`** (Admin plugin management)
- `GetRepositoriesAsync()` — `GET /api/v1/admin/plugins/repositories`
- `CreateRepositoryAsync(request)` — `POST /api/v1/admin/plugins/repositories`
- `UpdateRepositoryAsync(id, request)` — `PUT /api/v1/admin/plugins/repositories/{id}`
- `DeleteRepositoryAsync(id)` — `DELETE /api/v1/admin/plugins/repositories/{id}`
- `GetCatalogAsync()` — `GET /api/v1/admin/plugins/catalog`
- `GetInstallationsAsync()` — `GET /api/v1/admin/plugins/installations`
- `CreateInstallationAsync(request)` — `POST /api/v1/admin/plugins/installations`
- `UploadPluginAsync(request)` — `POST /api/v1/admin/plugins/uploads`
- `UpdateInstallationAsync(id, request)` — `PUT /api/v1/admin/plugins/installations/{id}`
- `TriggerUpdateAsync(id)` — `POST /api/v1/admin/plugins/installations/{id}/update`
- `UpdateConfigAsync(id, request)` — `PUT /api/v1/admin/plugins/installations/{id}/config`
- `UpdateAuthBindingAsync(id, request)` — `PUT /api/v1/admin/plugins/installations/{id}/auth-binding`
- `UpdateTaskBindingAsync(id, capabilityId, request)` — `PUT /api/v1/admin/plugins/installations/{id}/task-bindings/{capability_id}`
- `UpdateAnalyzerBindingsAsync(id, request)` — `PUT /api/v1/admin/plugins/installations/{id}/analyzer-bindings`
- `DeleteInstallationAsync(id)` — `DELETE /api/v1/admin/plugins/installations/{id}`

### 2.2 Extended API Modules

**`AuthApi.cs`** — Add:
- `SignupAsync(username, email, password, inviteCode)` — `POST /api/v1/auth/signup`
- `GetSignupStatusAsync()` — `GET /api/v1/auth/signup`
- `SetupAsync(request)` — `POST /api/v1/auth/setup`
- `GetSetupStatusAsync()` — `GET /api/v1/auth/setup`
- `GetAuthProvidersAsync()` — `GET /api/v1/auth/providers`
- `LogoutAsync()` — `POST /api/v1/auth/logout`
- `GetMeAsync()` — `GET /api/v1/auth/me`
- `GetSessionsAsync()` — `GET /api/v1/auth/sessions`
- `RevokeSessionAsync(id)` — `DELETE /api/v1/auth/sessions/{id}`
- `StartImpersonationAsync(userId)` — `POST /api/v1/admin/users/{id}/impersonate`
- `EndImpersonationAsync()` — `POST /api/v1/auth/impersonation/end`

**`CatalogApi.cs`** — Add:
- `GetItemVersionsAsync(contentId)` — `GET /api/v1/catalog/items/{id}/versions`
- `GetLibraryCollectionsAsync(libraryId)` — `GET /api/v1/library/{id}/collections`
- `GetLibraryCollectionItemsAsync(libraryId, collectionId)` — `GET /api/v1/library/{id}/collections/{collection_id}/items`
- `GetLibrarySectionsAsync(libraryId)` — `GET /api/v1/library/{id}/sections`
- `GetLibrarySectionItemsAsync(libraryId, sectionId)` — `GET /api/v1/library/{id}/sections/{sectionId}/items`
- `GetHistoryAsync(limit, offset)` — `GET /api/v1/history`
- `GetRatingsListAsync()` — `GET /api/v1/ratings`
- `GetWatchlistItemAsync(itemId)` — `GET /api/v1/watchlist/{item_id}`
- `GetFavoriteItemAsync(itemId)` — `GET /api/v1/favorites/{item_id}`
- `GetAudioPrefsAsync(seriesId)` — `GET /api/v1/audio-prefs/{series_id}`
- `SetAudioPrefsAsync(seriesId, request)` — `PUT /api/v1/audio-prefs/{series_id}`
- `DeleteAudioPrefsAsync(seriesId)` — `DELETE /api/v1/audio-prefs/{series_id}`
- `SyncProgressAsync(request)` — `POST /api/v1/sync/progress`

**`HomeApi.cs`** — Add:
- `UndoDismissalAsync(surface, itemId)` — `DELETE /api/v1/home/dismissals/{surface}/{item_id}`

**`SettingsApi.cs`** — Add:
- `GetPluginSettingsAsync()` — `GET /api/v1/settings/plugins`
- `GetPluginSettingAsync(installationId)` — `GET /api/v1/settings/plugins/{installation_id}`
- `UpdatePluginSettingAsync(installationId, request)` — `PUT /api/v1/settings/plugins/{installation_id}`
- `GetProfileSectionsAsync()` — `GET /api/v1/profile/sections`
- `UpdateProfileSectionsAsync(request)` — `PUT /api/v1/profile/sections`
- `ResetProfileSectionsAsync()` — `DELETE /api/v1/profile/sections/reset`
- `GetProfileSectionSettingsAsync()` — `GET /api/v1/profile/sections/settings`

**`PlaybackApi.cs`** — Add:
- `GetSubtitlesAsync(mediaFileId)` — `GET /api/v1/subtitles/{media_file_id}`
- `DeleteSubtitleAsync(id)` — `DELETE /api/v1/subtitles/{id}`

**`AdminApi.cs`** — Add:
- `GetUserDetailAsync(id)` — `GET /api/v1/admin/users/{id}`
- `ImpersonateUserAsync(id)` — `POST /api/v1/admin/users/{id}/impersonate`
- `GetUserProfilesAsync(id)` — `GET /api/v1/admin/users/{id}/profiles`
- `GetUserIpsAsync(id)` — `GET /api/v1/admin/users/{id}/ips`
- `GetAllIpsAsync()` — `GET /api/v1/admin/ips`
- `MatchSearchAsync(itemId, request)` — `POST /api/v1/admin/items/{id}/match/search`
- `MatchApplyAsync(itemId, request)` — `POST /api/v1/admin/items/{id}/match/apply`
- `RefreshItemMetadataAsync(itemId)` — `POST /api/v1/admin/items/{id}/refresh-metadata`
- `UpdateItemMetadataAsync(itemId, request)` — `PATCH /api/v1/admin/items/{id}/metadata`
- `ExportCatalogAsync(request)` — `POST /api/v1/admin/catalog/export`
- `CreateExportJobAsync(request)` — `POST /api/v1/admin/catalog/export-jobs`
- `PublishExportJobAsync(id)` — `POST /api/v1/admin/catalog/export-jobs/{id}/publish`
- `CreateImportJobAsync(request)` — `POST /api/v1/admin/catalog/import-jobs`
- `GetImportSourcesAsync()` — `GET /api/v1/admin/catalog/import-sources`
- `GetLocalImportSourcesAsync()` — `GET /api/v1/admin/catalog/local-import-sources`
- `ImportCatalogAsync(request)` — `POST /api/v1/admin/catalog/import`
- `GetJobsAsync()` — `GET /api/v1/admin/jobs`
- `GetJobAsync(id)` — `GET /api/v1/admin/jobs/{id}`
- `GetProvidersAsync()` — `GET /api/v1/admin/providers`
- `CreateProviderAsync(request)` — `POST /api/v1/admin/providers`
- `UpdateProviderAsync(id, request)` — `PUT /api/v1/admin/providers/{id}`
- `DeleteProviderAsync(id)` — `DELETE /api/v1/admin/providers/{id}`
- `GetLibraryProvidersAsync(libraryId)` — `GET /api/v1/admin/libraries/{id}/providers`
- `UpdateLibraryProvidersAsync(libraryId, request)` — `PUT /api/v1/admin/libraries/{id}/providers`
- `SetLibraryPosterAsync(libraryId, request)` — `PUT /api/v1/admin/libraries/{id}/poster`
- `DeleteLibraryPosterAsync(libraryId)` — `DELETE /api/v1/admin/libraries/{id}/poster`
- `GetSkippedRootsAsync()` — `GET /api/v1/admin/libraries/skipped-roots`
- `GetStaleIdsAsync()` — `GET /api/v1/admin/libraries/stale-ids`
- `RematchStaleIdAsync(contentId)` — `POST /api/v1/admin/libraries/stale-ids/{contentID}/rematch`
- `GetUnmatchedItemsAsync()` — `GET /api/v1/admin/libraries/unmatched-items`
- `ConfirmEmptyRootCleanupAsync(libraryId)` — `POST /api/v1/admin/libraries/{id}/confirm-empty-root-cleanup`
- `RefreshLibraryMetadataAsync(libraryId)` — `POST /api/v1/admin/libraries/{id}/refresh-metadata`
- `GetInviteCodesAsync()` — `GET /api/v1/admin/invite-codes`
- `CreateInviteCodeAsync(request)` — `POST /api/v1/admin/invite-codes`
- `UpdateInviteCodeAsync(id, request)` — `PUT /api/v1/admin/invite-codes/{id}`
- `DeleteInviteCodeAsync(id)` — `DELETE /api/v1/admin/invite-codes/{id}`
- `GetSubtitleProvidersAsync()` — `GET /api/v1/admin/subtitle-providers`
- `UpdateSubtitleProviderAsync(provider, request)` — `PUT /api/v1/admin/subtitle-providers/{provider}`
- `TestSubtitleProviderAsync(provider)` — `POST /api/v1/admin/subtitle-providers/{provider}/test`
- `GetRateLimitsAsync()` — `GET /api/v1/admin/rate-limits/config`
- `UpdateRateLimitsAsync(request)` — `PUT /api/v1/admin/rate-limits/config`
- `GetAdminApiKeysAsync()` — `GET /api/v1/admin/api-keys`
- `CreateAdminApiKeyAsync(request)` — `POST /api/v1/admin/api-keys`
- `DeleteAdminApiKeyAsync(id)` — `DELETE /api/v1/admin/api-keys/{id}`
- `UpdateApiKeyTierAsync(id, request)` — `PUT /api/v1/admin/api-keys/{id}/tier`
- `GetUserApiKeysAsync(userId)` — `GET /api/v1/admin/users/{userId}/api-keys`
- `GetSensitiveSettingsStatusAsync()` — `GET /api/v1/admin/settings/sensitive-status`
- `GetRecommendationsStatusAsync()` — `GET /api/v1/admin/recommendations/status`
- `TriggerEmbeddingsAsync()` — `POST /api/v1/admin/recommendations/trigger/embeddings`
- `TriggerTasteProfilesAsync()` — `POST /api/v1/admin/recommendations/trigger/taste-profiles`
- `TriggerCowatchAsync()` — `POST /api/v1/admin/recommendations/trigger/cowatch`
- `TriggerRecommendationsAsync()` — `POST /api/v1/admin/recommendations/trigger/recommendations`
- `GetTasksAsync()` — `GET /api/v1/admin/tasks`
- `GetTaskAsync(key)` — `GET /api/v1/admin/tasks/{key}`
- `RunTaskAsync(key)` — `POST /api/v1/admin/tasks/{key}/run`
- `CancelTaskAsync(key)` — `POST /api/v1/admin/tasks/{key}/cancel`
- `UpdateTaskTriggersAsync(key, request)` — `PUT /api/v1/admin/tasks/{key}/triggers`
- `GetTaskHistoryAsync(key)` — `GET /api/v1/admin/tasks/{key}/history`
- Admin collection endpoints (import MDB List, import TMDB, sync, delete image)
- `GetHistoryImportSourcesAsync()` — `GET /api/v1/admin/history-import-sources` (+ CRUD)
- `GetNodeSessionsAsync()` — `GET /api/v1/admin/node-sessions`
- `ForceReloadNodesAsync()` — `POST /api/v1/admin/nodes/force-reload`
- `ForceReloadNodeAsync(id)` — `POST /api/v1/admin/nodes/{id}/force-reload`
- `CheckNodeAsync(id)` — `POST /api/v1/admin/nodes/{id}/check`
- Playback control: `PauseSessionAsync`, `ResumeSessionAsync`, `StopSessionAsync`, `TerminateSessionAsync`, `MessageSessionAsync`

**`RecommendationsApi.cs`** (new dedicated module) — Add:
- `GetForYouMainAsync()` — `GET /api/v1/recommendations/for-you/main`
- `GetSimilarUsersAsync()` — `GET /api/v1/recommendations/similar-users`
- `GetRecentlyAddedAsync()` — `GET /api/v1/recommendations/recently-added`

**`ApiKeysApi.cs`** (user-scoped) — New:
- `CreateApiKeyAsync(request)` — `POST /api/v1/api-keys`
- `GetApiKeysAsync()` — `GET /api/v1/api-keys`
- `DeleteApiKeyAsync(id)` — `DELETE /api/v1/api-keys/{id}`

### 2.3 New Models

All new C# model classes in `ContinuumPlayer.Core/Models/`:

**Auth/:**
- `SignupRequest`, `SignupStatusResponse`
- `SetupRequest`, `SetupStatusResponse`
- `AuthProvider` (id, display_name, mode, default)
- `AuthSession` (id, device_name, ip_address, created_at, expires_at, revoked_at)
- `AuthSessionsResponse`
- `ImpersonationResponse`

**Catalog/:**
- `Person` (id, name, bio, birth_date, death_date, birthplace, homepage, photo_url, photo_thumbhash, tmdb_id, imdb_id)
- `PersonRefreshResponse` (status, person_id)
- `HistoryEntry` (content_id, type, title, watched_at, position_seconds, duration_seconds, completed, poster_url)
- `HistoryResponse` (items, total, has_more)
- `LibraryCollectionResponse` (collections[])
- `LibraryCollection` (id, library_id, slug, title, description, collection_type, visibility, poster_url, backdrop_url, item_count, last_sync_status)
- `AudioPreference` (audio_track_index, audio_language)

**Collections/:**
- `Collection` (id, profile_id, creator_profile_id, name, collection_type, is_shared, query_definition, sort_config)
- `CollectionItem` (collection_id, media_item_id, position)
- `CreateCollectionRequest`, `UpdateCollectionRequest`
- `CollectionPreviewRequest`, `CollectionPreviewResponse`
- `CollectionsResponse`

**Downloads/:**
- `Download` (id, status, media_file_id, file_name, created_at)
- `DownloadRequest`, `DownloadsResponse`

**HistoryImport/:**
- `HistoryImportSource` (id, name, source_type, base_url, enabled)
- `HistoryImportRun` (id, profile_id, source_type, status, fetched, matched, unmatched, progress_updated, history_created, warnings, error_message, timestamps)
- `CreateHistoryImportRunRequest`
- `EmbyConnectLoginResponse`, `PlexPinResponse`, `PlexCheckResponse`

**Plugins/:**
- `PluginRepository` (id, url, name, enabled)
- `PluginInstallation` (id, repository_id, plugin_id, version, enabled, capabilities, config_schemas, routes, assets, bindings)
- `PluginCatalogEntry` (repository_id, plugin_id, version, capabilities)
- `PluginCapability` (type, id, display_name, description, config_schema)
- `PluginRoute` (id, method, path, access, navigable, navigation_label)
- `PluginConfigSchema` (JSON schema for dynamic forms)
- `PluginAuthBinding`, `PluginTaskBinding`, `PluginAnalyzerBinding`

**Admin/:**
- `AdminUserDetail` (extends AdminUser: profiles, created_at, updated_at)
- `AdminJob` (id, job_type, status, progress_current, progress_total, message, error_message, timestamps, download_url)
- `TaskInfo` (key, name, description, category, state, progress, triggers, next_run_at)
- `TriggerConfig` (type, interval_ms, time_of_day, day_of_week, max_runtime_ms)
- `ExecutionResult` (id, task_key, started_at, completed_at, status, error_message, result_data, duration_ms)
- `MetadataProvider` (id, slug, provider_type, enabled, settings)
- `LibraryProviderChain` (entries[])
- `InviteCode` (id, code, label, max_uses, use_count, enabled, created_at)
- `SubtitleProviderConfig` (provider_name, enabled, has_api_key, has_credentials)
- `MatchSearchRequest`, `MatchSearchResponse`, `MatchApplyRequest`
- `StaleMediaId`, `SkippedRoot`, `UnmatchedFile`
- `AdminIpEntry` (ip_address, first_seen, last_seen, user_id, username)
- `AdminPlaybackHistoryItem` (session details, media info, timestamps, completion)
- `CatalogExportRequest`, `CatalogImportRequest`
- `RecommendationsStatus` (embeddings, taste_profiles, cowatch, recommendations job statuses)
- `NodeSession` (live session info from Redis)
- `AdminApiKey` (id, user_id, username, label, key, rate_tier, created_at, last_used_at)

**Home/:**
- `SectionSettings` (sections with position, hidden, config overrides)
- `SectionOverride` (id, section_id, position, hidden, section_type, config, removed)
- `ProfileSectionsResponse`

**Settings/:**
- `PluginUserSettings` (installation_id, config)
- `SubtitleAppearance` (font_family, font_size, font_color, outline, background_style, background_opacity, background_color, position)

---

## 3. New Pages & Views

### 3.1 Auth & Entry Points

**`SignupPage`**
- Form: username, email, password, confirm password, invite code (optional)
- Checks signup status first (`GET /auth/signup`)
- Redirects to profile select on success
- Link to login page

**`SetupWizardPage`**
- Multi-step wizard (5 steps):
  1. Account creation (username, email, password)
  2. Profile creation (name)
  3. Library creation (paths, type, name, scan option)
  4. Server settings (Redis URL, FFmpeg path, transcode dir, hardware accel, transcoding toggle, Jellyfin URL/name)
  5. Metadata provider selection (plugin or built-in)
- Step indicator with progress
- Back/Next navigation
- Persists completed steps locally

**`AuthSessionsPage`** (or tab within Settings)
- Table of active auth sessions (device name, IP, created, expires)
- Revoke button per session
- Current session indicator

**`ImpersonationBanner` (control)**
- Yellow/amber banner shown at top of window when admin is impersonating a user
- Shows impersonated username
- "End Impersonation" button

### 3.2 Settings Overhaul

Replace single `SettingsPage` with a tabbed layout (`SettingsShellPage` + sub-pages):

**Appearance Tab (`AppearanceSettingsPage`)**
- Theme picker grid: 5 curated + 8 additional themes
- Each theme shown as a preview card with sample colors (background + primary swatch)
- Hover to preview theme, click to apply
- Active theme check indicator
- Theme description text
- Reset to Cinema Dark button

**Playback Tab (`PlaybackSettingsPage`)**
- Video quality preference dropdown (Auto, 480p, 720p, 1080p, 4K)
- Spoken language dropdown
- Subtitle language dropdown (None + languages)
- Subtitle mode (Auto / Always / Off)
- Show forced subtitles toggle
- Auto-skip intro toggle
- Auto-skip credits toggle
- Next-up episodes mode (combined with continue watching / separate section)

**Subtitle Appearance Tab (`SubtitleAppearanceSettingsPage`)**
- Live preview area showing sample subtitle text
- Font family picker
- Font size picker
- Font color palette (preset colors)
- Text outline toggle
- Background style (None / Shadow / Box)
- Background opacity slider (visible when style = Box)
- Background color palette (visible when style = Box)
- Subtitle position (Bottom / Top)
- Save / Reset buttons

**Home Screen Tab (`HomeScreenSettingsPage`)**
- Scope selector: Home screen or per-library
- Drag-and-drop section list (reorder via grip handle)
- Per-section: visibility toggle (eye icon), edit button, delete button
- Add section button → flyout with section type options
- Save / Reset buttons

**History Import Tab (`HistoryImportSettingsPage`)**
- Source type selector (Emby / Jellyfin / Plex)
- Emby: Connect mode (email+password → server picker) or saved source
- Jellyfin: Direct URL + username + password
- Plex: OAuth mode (sign-in button → browser → callback) or saved source with token
- Profile selector for import target
- Import run list showing status, progress, results
- Unmatched items sample display

**Plugin Settings Tab (`PluginSettingsPage`)**
- List of installed plugins with user config schemas
- Per-plugin: dynamic form generated from JSON schema
- Save button per plugin
- Links to plugin-hosted account pages

**Sessions Tab (`SessionsSettingsPage`)**
- Active session list with device, IP, dates
- Revoke button, current session highlight

### 3.3 Browse & Discovery

**`PersonDetailPage`**
- Header: photo (or initials), name, birth/death dates with age, birthplace
- Bio section (collapsible if long)
- Admin: refresh metadata button
- Filmography: grid of movies/series featuring this person
- Type filter buttons (All / Movies / Series)
- Virtual scrolling for large filmographies

**`LibraryCollectionsPage`**
- Grid of collection cards per library
- Each card: poster/backdrop, title, item count, type badge
- Click → browse collection items in catalog view

**`LibrarySectionsPage`** (or integrated into LibraryPage)
- Per-library section rows (same pattern as home page sections)
- Each section: horizontal carousel of items

**Enhanced `LibraryPage`**
- Sub-navigation: Browse / Collections / Recommended / Sections
- Enhanced filter bar with genre, studio, network, country, content rating, resolution, audio language, subtitle language
- Active filter badges showing applied filters with clear buttons
- Filter sheet/flyout for grouped filter UI

**`HistoryPage`**
- Uses `GET /api/v1/history` endpoint
- List/grid of watched items with timestamp, progress, completion status

**Recommendations enhancements:**
- `for-you/main` hero section on recommendations page
- Similar users section
- Recently added section

### 3.4 User Collections

**`CollectionsPage`**
- Grid of user-created collections
- Create collection button
- Per-collection card: name, type badge (manual/smart), shared indicator
- Edit/delete actions on hover

**`CollectionEditorPage`**
- Name input
- Type: manual or smart (rule-based)
- Sharing toggle and access control (which profiles)
- For smart collections:
  - Guided rule builder: field dropdown → operator dropdown → value input
  - Add rule / Add group buttons
  - Preview pane showing matched items in real-time
- For manual collections:
  - Item search and add
  - Drag-and-drop reordering
- Save / Cancel buttons

### 3.5 Power Features

**Downloads** (integrated into item detail or separate page)
- Download button on item detail page (if download_allowed)
- Downloads list page showing queued/completed downloads
- Progress indicators
- File save to user-selected local folder

**Audio Preferences**
- Per-series audio track memory (like existing subtitle prefs)
- Stored via `PUT /api/v1/audio-prefs/{series_id}`
- Applied automatically on playback start

**Subtitle Management**
- In item detail or playback: list downloaded subtitles
- Delete individual downloaded subtitles

### 3.6 Admin Enhancements

**`AdminPluginsPage`**
- Tabs: Installed / Available
- Installed tab: list of plugin installations with enable/disable toggle, configure button, delete button
- Available tab: plugin catalog from repositories, install buttons
- Configure dialog:
  - Global config form (generated from config_schema)
  - Auth bindings section (enable, display order, auto-provision)
  - Task bindings section (enable, trigger config)
  - Analyzer bindings section (per-library enable)
- Repository management section: add/edit/delete repos
- Manual plugin upload button

**`AdminItemMatchPage`** (dialog on item detail)
- Search match candidates dialog
- Results list with metadata preview
- Apply match button

**`AdminCatalogSeedPage`** (or tab in Maintenance)
- Export: create export job, track progress, publish
- Import: browse sources, select, run import job

**`AdminTaskDetailPage`**
- Task info: name, description, category, current state
- Trigger config editor:
  - Add/remove triggers
  - Trigger types: interval, daily, weekly, startup
  - Interval editor (ms), time picker, day-of-week selector, max runtime
- Execution history table: started, duration, status (color-coded), error, expandable result data
- Run now / Cancel buttons
- Progress bar for running tasks

**`AdminProvidersPage`**
- Table of metadata providers (slug, type, enabled, settings)
- Create/edit/delete providers
- Per-library provider chain configuration

**`AdminInviteCodesPage`**
- Table: code, label, max uses, use count, enabled, created date
- Create dialog (code, label, max uses)
- Enable/disable toggle
- Delete with confirmation

**`AdminMaintenancePage`**
- Stale IDs: list with rematch button
- Skipped roots: list of unmatchable directories
- Unmatched files: list with file details
- Catalog import/export tools

**`AdminJobsPage`** (or integrated into tasks)
- List of admin jobs with type, status, progress, timestamps
- Download artifacts when available

**`AdminSubtitleProvidersPage`**
- Config per provider: API key, credentials
- Test connection button
- Enable/disable toggle

**`AdminUserDetailPage`**
- Tabs: Overview / Profiles / Watch History / IP History
- Overview: account details, permissions, library access, stream/transcode limits, download permissions
- Profiles tab: list of user profiles
- Watch history tab: table with media, profile, method, watch time, status
- IP history tab: list of IPs with first/last seen
- Actions: edit user (dialog), impersonate (confirmation), delete (confirmation)

**`AdminApiKeysPage`**
- Table: label, user, key (masked), tier, created, last used
- Create dialog (label, user selection)
- Copy key button (shown once on creation)
- Tier dropdown (standard/elevated)
- Revoke button
- Pagination (25/50/100)

**`AdminIpTrackingPage`** (or tab within user detail)
- All IP entries across users
- Filter by user

**Admin session enhancements:**
- Pause/resume/stop/terminate controls per active session
- Send message to session
- Live node sessions view

---

## 4. Updated Existing Features

### 4.1 Navigation Updates

- Add Collections to sidebar navigation
- Add Person search result type
- Library sub-nav: Browse / Collections / Recommended
- Settings page restructure (tabbed shell)
- Admin sub-nav additions: Plugins, Maintenance, API Keys, Invite Codes (under Settings)

### 4.2 Item Detail Page

- Add person links (click cast/crew → PersonDetailPage)
- Add match/edit metadata buttons (admin only)
- Add download button (if user has download permission)
- Add audio preference memory indication
- Show library collection membership

### 4.3 Home Page

- Support undo-dismiss (DELETE endpoint)
- Handle new section types from profile customization

### 4.4 Search

- Include people results in global search
- Display person results with photo and name

### 4.5 Player Overlay

- Support audio preference save on track switch
- Show impersonation banner during playback

---

## 5. Implementation Phases

### Phase 1: Design System & Theme Engine
- ThemeEngine class
- 13 XAML resource dictionaries with all color tokens
- Bundle fonts (Outfit, Manrope, Sora, Urbanist)
- Design token resources (spacing, radii, shadows, animations)
- Update all existing views to use semantic brush tokens
- Theme persistence in settings

### Phase 2: API & Model Layer
- All new model classes (~40 files)
- New API modules (PeopleApi, CollectionsApi, DownloadsApi, HistoryImportApi, PluginsApi, RecommendationsApi, ApiKeysApi)
- Extended API modules (AuthApi, CatalogApi, HomeApi, SettingsApi, PlaybackApi, AdminApi)

### Phase 3: Auth & Entry Points
- SignupPage + ViewModel
- SetupWizardPage + ViewModel
- Auth sessions management
- ImpersonationBanner control
- Auth provider display on login

### Phase 4: Settings Overhaul
- SettingsShellPage (tabbed layout)
- AppearanceSettingsPage (theme picker)
- PlaybackSettingsPage
- SubtitleAppearanceSettingsPage
- HomeScreenSettingsPage (drag-and-drop sections)
- HistoryImportSettingsPage
- PluginSettingsPage
- SessionsSettingsPage

### Phase 5: Browse & Discovery
- PersonDetailPage + ViewModel
- Library collections browse
- Library sections
- Enhanced catalog filters (filter bar, badges, sheet)
- HistoryPage
- Recommendations enhancements

### Phase 6: User Collections
- CollectionsPage + ViewModel
- CollectionEditorPage + ViewModel
- Collection rules editor control
- Collection preview pane
- Manual collection ordering

### Phase 7: Power Features
- Downloads (UI + API integration)
- History import (Emby/Jellyfin/Plex flows)
- Audio preferences (per-series)
- Subtitle management (list/delete)
- Sync progress

### Phase 8: Admin Enhancements
- AdminPluginsPage + ViewModel
- AdminTaskDetailPage + ViewModel
- AdminProvidersPage + ViewModel
- AdminInviteCodesPage + ViewModel
- AdminMaintenancePage + ViewModel
- AdminSubtitleProvidersPage + ViewModel
- AdminUserDetailPage + ViewModel
- AdminApiKeysPage + ViewModel
- AdminJobsPage + ViewModel
- Item matching dialog
- Catalog import/export
- IP tracking
- Session control actions
- Node management enhancements

### Phase 9: Polish & Integration
- Home dismissal undo
- Individual watchlist/favorites check
- Ratings list view
- Search with people results
- Person links from item detail
- Impersonation flow (admin → user → end)
- Error handling alignment
- Full end-to-end testing

---

## 6. Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Theme switching | Runtime `ResourceDictionary` swap via `Application.Current.Resources.MergedDictionaries` | Standard WinUI pattern; all views react immediately |
| Font bundling | Embedded `.ttf` in app assets with `ms-appx:///` URIs | Must work offline; web fonts not available |
| Plugin UI pages | WebView2 for plugin-hosted HTML routes | Plugin routes serve web content; config forms are native XAML from JSON schemas |
| Drag-and-drop sections | `ListView` with `CanReorderItems=true` + `AllowDrop` | Built-in WinUI reorder; no external library needed |
| Collection rules editor | Custom XAML UserControl with field/operator/value dropdowns | No off-the-shelf equivalent; matches web's guided builder pattern |
| History import OAuth | Launch system browser, listen on localhost for callback | Standard desktop OAuth2 pattern for Plex/Emby |
| Downloads | `HttpClient.GetStreamAsync` → `FileStream` with progress reporting | Simple, handles large files, supports progress UI |
| Dynamic plugin config forms | Generate XAML controls at runtime from JSON schema | Matches web's approach of rendering forms from `config_schema` |
| Cinema Light theme | Same theme engine, light color values | Only light theme; no special Mica/Acrylic — stays consistent with web |
