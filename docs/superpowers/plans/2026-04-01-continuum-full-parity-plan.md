# Continuum Full Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the ContinuumPlayer desktop app to complete feature and visual parity with the Continuum web UI — 13 themes, all new API endpoints, all new pages/features, admin enhancements.

**Architecture:** WinUI 3 / .NET 8 / MVVM with CommunityToolkit. API clients in `ContinuumPlayer.Core/Api/`, models in `Core/Models/`, views in `ContinuumPlayer/Views/`, viewmodels in `ViewModels/`. libmpv for playback. Themes as runtime-swappable XAML resource dictionaries driven by `ThemeService`.

**Tech Stack:** .NET 8, WinUI 3, CommunityToolkit.Mvvm, System.Text.Json (snake_case), libmpv, WebView2 (for plugin pages)

**Reference:** Continuum server source cloned at `F:\continuum-server` — use `web/src/` for UI reference, `internal/api/handlers/` for API contracts, `web/src/api/types.ts` for model definitions, `web/src/lib/themes.ts` and `web/src/app.css` for theme data.

---

## Phase 1: Design System & Theme Engine

The existing `ThemeService.cs` already has all 13 themes with hex colors and runtime switching. Remaining work: bundle fonts, add font switching per theme, add missing semantic tokens, add design tokens as XAML resources.

### Task 1.1: Bundle Font Families

**Files:**
- Create: `src/ContinuumPlayer/Assets/Fonts/Outfit-Variable.ttf`
- Create: `src/ContinuumPlayer/Assets/Fonts/Manrope-Variable.ttf`
- Create: `src/ContinuumPlayer/Assets/Fonts/Sora-Variable.ttf`
- Create: `src/ContinuumPlayer/Assets/Fonts/Urbanist-Variable.ttf`
- Modify: `src/ContinuumPlayer/ContinuumPlayer.csproj`

- [ ] **Step 1: Download font files**

Download the four variable font files from Google Fonts (OFL licensed). Place them in `src/ContinuumPlayer/Assets/Fonts/`.

```bash
mkdir -p src/ContinuumPlayer/Assets/Fonts
# Download from Google Fonts API or use the font files from the Continuum web UI's public assets
# These are OFL-licensed fonts freely available from fonts.google.com
```

- [ ] **Step 2: Add font files to .csproj as Content**

Add to `ContinuumPlayer.csproj` inside an `<ItemGroup>`:

```xml
<ItemGroup>
  <Content Include="Assets\Fonts\*.ttf" />
</ItemGroup>
```

- [ ] **Step 3: Commit**

```bash
git add src/ContinuumPlayer/Assets/Fonts/ src/ContinuumPlayer/ContinuumPlayer.csproj
git commit -m "feat: bundle Outfit, Manrope, Sora, Urbanist fonts"
```

### Task 1.2: Add Font Switching to ThemeService

**Files:**
- Modify: `src/ContinuumPlayer/Services/ThemeService.cs`

- [ ] **Step 1: Add font family mapping to ThemeColors**

Add a `FontFamily` property to the `ThemeColors` class and populate it for each theme:

```csharp
public class ThemeColors
{
    // ... existing properties ...
    public string FontFamily { get; set; } = "Outfit";
    public string DisplayFontFamily { get; set; } = "Outfit";
}
```

Set per theme in the dictionary:
- Outfit: midnight-cinema, cinema-light, cobalt-studio, oxblood-noir, evergreen-studio, catppuccin, charcoal-studio
- Manrope: gruvbox, void-space
- Sora: graphite-pro
- Urbanist: obsidian-depth, ember-slate, verdant-ink

- [ ] **Step 2: Update ApplyTheme to switch fonts**

In the `ApplyTheme` method, after updating colors, update the font family resource:

```csharp
var fontPath = $"ms-appx:///Assets/Fonts/{colors.FontFamily}-Variable.ttf#{colors.FontFamily}";
var fontFamily = new Microsoft.UI.Xaml.Media.FontFamily(fontPath);
res["ThemeFontFamily"] = fontFamily;

var displayFontPath = $"ms-appx:///Assets/Fonts/{colors.DisplayFontFamily}-Variable.ttf#{colors.DisplayFontFamily}";
var displayFontFamily = new Microsoft.UI.Xaml.Media.FontFamily(displayFontPath);
res["ThemeDisplayFontFamily"] = displayFontFamily;
```

- [ ] **Step 3: Add ThemeFontFamily to DarkTheme.xaml defaults**

Add default font resources so they exist before first theme application:

```xml
<FontFamily x:Key="ThemeFontFamily">ms-appx:///Assets/Fonts/Outfit-Variable.ttf#Outfit</FontFamily>
<FontFamily x:Key="ThemeDisplayFontFamily">ms-appx:///Assets/Fonts/Outfit-Variable.ttf#Outfit</FontFamily>
```

- [ ] **Step 4: Build and verify theme switching changes fonts**

```bash
cd src/ContinuumPlayer && dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: add per-theme font family switching"
```

### Task 1.3: Add Missing Semantic Color Tokens

**Files:**
- Modify: `src/ContinuumPlayer/Services/ThemeService.cs`
- Modify: `src/ContinuumPlayer/Themes/DarkTheme.xaml`

- [ ] **Step 1: Add missing tokens to ThemeColors class**

Add these properties that exist in the web UI but not in the current ThemeColors:

```csharp
public string PopoverBackground { get; set; } = "";
public string PopoverForeground { get; set; } = "";
public string DestructiveForeground { get; set; } = "";
public string Ring { get; set; } = "";
public string Chart1 { get; set; } = "";
public string Chart2 { get; set; } = "";
public string Chart3 { get; set; } = "";
public string Chart4 { get; set; } = "";
public string Chart5 { get; set; } = "";
public string SidebarPrimary { get; set; } = "";
public string SidebarPrimaryForeground { get; set; } = "";
public string SidebarForeground { get; set; } = "";
public string SidebarRing { get; set; } = "";
public string AmbientGlowOpacity { get; set; } = "0.10";
```

- [ ] **Step 2: Populate values for all 13 themes**

Use the exact hex values from `F:\continuum-server\web\src\app.css` (lines 210-811) to set each theme's new properties. The values are documented in the design spec.

- [ ] **Step 3: Add corresponding brushes to DarkTheme.xaml**

```xml
<SolidColorBrush x:Key="PopoverBrush" Color="{StaticResource PopoverBackgroundColor}" />
<SolidColorBrush x:Key="PopoverForegroundBrush" Color="{StaticResource PopoverForegroundColor}" />
<SolidColorBrush x:Key="DestructiveForegroundBrush" Color="{StaticResource DestructiveForegroundColor}" />
<SolidColorBrush x:Key="RingBrush" Color="{StaticResource RingColor}" />
<SolidColorBrush x:Key="Chart1Brush" Color="{StaticResource Chart1Color}" />
<!-- ... Chart2-5, SidebarPrimary, SidebarPrimaryForeground, SidebarForeground, SidebarRing ... -->
```

- [ ] **Step 4: Update ApplyTheme to include new tokens**

Add `UpdateBrush`/`UpdateColor` calls for each new token.

- [ ] **Step 5: Build and verify**

```bash
cd src/ContinuumPlayer && dotnet build
```

- [ ] **Step 6: Commit**

```bash
git commit -am "feat: add missing semantic color tokens for full theme parity"
```

### Task 1.4: Add Design Token Resources

**Files:**
- Modify: `src/ContinuumPlayer/Themes/DarkTheme.xaml`

- [ ] **Step 1: Add animation duration resources**

```xml
<!-- Animation Durations -->
<x:Double x:Key="DurationFast">150</x:Double>
<x:Double x:Key="DurationNormal">250</x:Double>
<x:Double x:Key="DurationSlow">400</x:Double>
<x:Double x:Key="DurationGlacial">700</x:Double>
<x:Double x:Key="DurationCinematic">1200</x:Double>
```

- [ ] **Step 2: Add additional radii matching web UI**

```xml
<CornerRadius x:Key="RadiusPill">9999</CornerRadius>
```

- [ ] **Step 3: Commit**

```bash
git commit -am "feat: add design token resources for animation and radii"
```

### Task 1.5: Update Existing Views to Use Semantic Tokens

**Files:**
- Modify: All XAML files in `src/ContinuumPlayer/Views/` and `src/ContinuumPlayer/Controls/`

- [ ] **Step 1: Audit all views for hardcoded colors**

Search all XAML files for hardcoded hex colors (e.g., `#101722`, `#78AEFC`, `Color="#`) and replace with semantic brush references. Key patterns:
- `Color="#101722"` → `{StaticResource AppBackgroundColor}` or use brush
- `Background="#..."` → `Background="{StaticResource CardBackgroundBrush}"`
- `Foreground="#..."` → `Foreground="{StaticResource PrimaryTextBrush}"`

- [ ] **Step 2: Update text styles to use ThemeFontFamily**

Update the text styles in DarkTheme.xaml to reference `ThemeFontFamily`:

```xml
<Style x:Key="DisplayTextStyle" TargetType="TextBlock">
    <Setter Property="FontFamily" Value="{StaticResource ThemeFontFamily}" />
    <!-- ... existing setters ... -->
</Style>
```

- [ ] **Step 3: Build and test with multiple themes**

Switch between themes in the app and verify all views render correctly.

- [ ] **Step 4: Commit**

```bash
git commit -am "feat: update all views to use semantic theme tokens"
```

---

## Phase 2: API & Model Layer

Add all new API client modules, extend existing ones, and create all new model classes. This phase is pure backend plumbing — no UI changes.

### Task 2.1: New Model Classes — Auth & Catalog Extensions

**Files:**
- Create: `src/ContinuumPlayer.Core/Models/Auth/SignupRequest.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/SignupStatusResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/SetupRequest.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/SetupStatusResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/AuthProvider.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/AuthSession.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/ImpersonationResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Catalog/Person.cs`
- Create: `src/ContinuumPlayer.Core/Models/Catalog/PersonResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Catalog/HistoryEntry.cs`
- Create: `src/ContinuumPlayer.Core/Models/Catalog/HistoryResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Catalog/LibraryCollection.cs`
- Create: `src/ContinuumPlayer.Core/Models/Catalog/AudioPreference.cs`

- [ ] **Step 1: Create auth extension models**

Use `web/src/api/types.ts` as the source of truth for field names and types. All models use `[JsonPropertyName("snake_case")]` attributes. Example:

```csharp
namespace ContinuumPlayer.Core.Models.Auth;

public class AuthProvider
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "";
    [JsonPropertyName("default")]
    public bool Default { get; set; }
}

public class AuthProvidersResponse
{
    [JsonPropertyName("providers")]
    public List<AuthProvider> Providers { get; set; } = new();
}

public class AuthSession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("device_name")]
    public string DeviceName { get; set; } = "";
    [JsonPropertyName("ip_address")]
    public string IpAddress { get; set; } = "";
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";
    [JsonPropertyName("expires_at")]
    public string ExpiresAt { get; set; } = "";
    [JsonPropertyName("revoked_at")]
    public string? RevokedAt { get; set; }
}
```

Follow this pattern for all auth extension models. Reference `F:\continuum-server\internal\api\handlers\auth.go` for exact field names.

- [ ] **Step 2: Create catalog extension models**

Create Person, HistoryEntry, LibraryCollection, AudioPreference models. Reference `F:\continuum-server\internal\api\handlers\people.go`, `F:\continuum-server\internal\api\handlers\user_state.go`, `F:\continuum-server\internal\api\handlers\library_collections.go`, `F:\continuum-server\internal\api\handlers\audio_prefs.go`.

- [ ] **Step 3: Build and verify**

```bash
cd src/ContinuumPlayer.Core && dotnet build
```

- [ ] **Step 4: Commit**

```bash
git commit -am "feat: add auth and catalog extension models"
```

### Task 2.2: New Model Classes — Collections, Downloads, History Import

**Files:**
- Create: `src/ContinuumPlayer.Core/Models/Collections/Collection.cs`
- Create: `src/ContinuumPlayer.Core/Models/Collections/CollectionItem.cs`
- Create: `src/ContinuumPlayer.Core/Models/Collections/CreateCollectionRequest.cs`
- Create: `src/ContinuumPlayer.Core/Models/Collections/CollectionPreviewRequest.cs`
- Create: `src/ContinuumPlayer.Core/Models/Downloads/Download.cs`
- Create: `src/ContinuumPlayer.Core/Models/Downloads/DownloadRequest.cs`
- Create: `src/ContinuumPlayer.Core/Models/HistoryImport/HistoryImportSource.cs`
- Create: `src/ContinuumPlayer.Core/Models/HistoryImport/HistoryImportRun.cs`
- Create: `src/ContinuumPlayer.Core/Models/HistoryImport/CreateHistoryImportRunRequest.cs`
- Create: `src/ContinuumPlayer.Core/Models/HistoryImport/EmbyConnectLoginResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/HistoryImport/PlexPinResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/HistoryImport/PlexCheckResponse.cs`

- [ ] **Step 1: Create all collection models**

Reference `F:\continuum-server\internal\api\handlers\collections.go` and `web/src/api/types.ts` (search for `Collection` interfaces).

- [ ] **Step 2: Create download models**

Reference `F:\continuum-server\internal\api\handlers\downloads.go`.

- [ ] **Step 3: Create history import models**

Reference `F:\continuum-server\internal\api\handlers\history_import.go` and `web/src/api/types.ts`.

- [ ] **Step 4: Build and commit**

```bash
cd src/ContinuumPlayer.Core && dotnet build
git commit -am "feat: add collection, download, and history import models"
```

### Task 2.3: New Model Classes — Plugins & Admin Extensions

**Files:**
- Create: `src/ContinuumPlayer.Core/Models/Plugins/PluginRepository.cs`
- Create: `src/ContinuumPlayer.Core/Models/Plugins/PluginInstallation.cs`
- Create: `src/ContinuumPlayer.Core/Models/Plugins/PluginCatalogEntry.cs`
- Create: `src/ContinuumPlayer.Core/Models/Plugins/PluginCapability.cs`
- Create: `src/ContinuumPlayer.Core/Models/Plugins/PluginRoute.cs`
- Create: `src/ContinuumPlayer.Core/Models/Plugins/PluginConfigSchema.cs`
- Create: `src/ContinuumPlayer.Core/Models/Plugins/PluginBindings.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminJob.cs` (if not exists, or extend)
- Create: `src/ContinuumPlayer.Core/Models/Admin/TaskInfo.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/TriggerConfig.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/ExecutionResult.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/MetadataProvider.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/InviteCode.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/SubtitleProviderConfig.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/MatchModels.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/StaleMediaId.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/AdminPlaybackHistoryItem.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/CatalogSeedModels.cs`
- Create: `src/ContinuumPlayer.Core/Models/Admin/NodeSession.cs`
- Create: `src/ContinuumPlayer.Core/Models/Home/SectionSettings.cs`
- Create: `src/ContinuumPlayer.Core/Models/Settings/SubtitleAppearance.cs`
- Create: `src/ContinuumPlayer.Core/Models/Settings/PluginUserSettings.cs`

- [ ] **Step 1: Create plugin models**

Reference `F:\continuum-server\internal\api\handlers\plugins.go` for all plugin-related structs.

- [ ] **Step 2: Create admin extension models**

Reference the handler files for each admin feature:
- `admin_match.go` for match models
- `admin_jobs.go` for AdminJob (extend existing)
- `tasks.go` for TaskInfo, TriggerConfig, ExecutionResult
- `providers.go` for MetadataProvider
- `admin_invite_codes.go` for InviteCode
- `admin_subtitles.go` for SubtitleProviderConfig
- `catalog_seed.go` for export/import models

- [ ] **Step 3: Create home/settings extension models**

Reference `sections.go` for SectionSettings and `F:\continuum-server\web\src\api\types.ts` for SubtitleAppearance, PluginUserSettings.

- [ ] **Step 4: Build and commit**

```bash
cd src/ContinuumPlayer.Core && dotnet build
git commit -am "feat: add plugin, admin extension, and settings models"
```

### Task 2.4: New API Modules — PeopleApi, CollectionsApi, DownloadsApi

**Files:**
- Create: `src/ContinuumPlayer.Core/Api/PeopleApi.cs`
- Create: `src/ContinuumPlayer.Core/Api/CollectionsApi.cs`
- Create: `src/ContinuumPlayer.Core/Api/DownloadsApi.cs`

- [ ] **Step 1: Create PeopleApi**

```csharp
namespace ContinuumPlayer.Core.Api;

public class PeopleApi
{
    private readonly ContinuumApiClient _client;
    public PeopleApi(ContinuumApiClient client) => _client = client;

    public Task<PeopleResponse> GetPeopleAsync(string? query = null, int limit = 20, int offset = 0)
    {
        var url = $"people?limit={limit}&offset={offset}";
        if (!string.IsNullOrEmpty(query)) url += $"&q={Uri.EscapeDataString(query)}";
        return _client.GetAsync<PeopleResponse>(url);
    }

    public Task<Person> GetPersonAsync(int id) => _client.GetAsync<Person>($"people/{id}");

    public Task<PersonRefreshResponse> RefreshPersonAsync(int id) => _client.PostAsync<PersonRefreshResponse>($"people/{id}/refresh", null);
}
```

- [ ] **Step 2: Create CollectionsApi**

Follow the same pattern. Methods: GetCollections, CreateCollection, PreviewCollection, UpdateCollection, DeleteCollection, GetCollectionItems, AddCollectionItem, RemoveCollectionItem.

- [ ] **Step 3: Create DownloadsApi**

Methods: CreateDownload, GetDownloads, DeleteDownload, GetDownloadFile (returns Stream for file saving).

- [ ] **Step 4: Register in DI (App.xaml.cs)**

```csharp
services.AddSingleton<PeopleApi>(sp => new PeopleApi(sp.GetRequiredService<ContinuumApiClient>()));
services.AddSingleton<CollectionsApi>(sp => new CollectionsApi(sp.GetRequiredService<ContinuumApiClient>()));
services.AddSingleton<DownloadsApi>(sp => new DownloadsApi(sp.GetRequiredService<ContinuumApiClient>()));
```

- [ ] **Step 5: Build and commit**

```bash
cd src/ContinuumPlayer && dotnet build
git commit -am "feat: add PeopleApi, CollectionsApi, DownloadsApi"
```

### Task 2.5: New API Modules — HistoryImportApi, PluginsApi, RecommendationsApi, ApiKeysApi

**Files:**
- Create: `src/ContinuumPlayer.Core/Api/HistoryImportApi.cs`
- Create: `src/ContinuumPlayer.Core/Api/PluginsApi.cs`
- Create: `src/ContinuumPlayer.Core/Api/RecommendationsApi.cs`
- Create: `src/ContinuumPlayer.Core/Api/ApiKeysApi.cs`

- [ ] **Step 1: Create HistoryImportApi**

Methods: GetImportSources, EmbyConnectLogin, PlexAuthPin, PlexAuthCheck, GetImportRuns, CreateImportRun, GetImportRun. Reference `F:\continuum-server\internal\api\handlers\history_import.go`.

- [ ] **Step 2: Create PluginsApi (admin)**

All plugin management methods. Reference `F:\continuum-server\internal\api\handlers\plugins.go`.

- [ ] **Step 3: Create RecommendationsApi**

Methods: GetForYouMain, GetForYouRows, GetBecauseWatched, GetSimilar, GetSimilarUsers, GetTasteProfile, GetPopular, GetRecentlyAdded. Reference `F:\continuum-server\internal\api\handlers\recommendations.go`.

- [ ] **Step 4: Create ApiKeysApi (user-scoped)**

Methods: CreateApiKey, GetApiKeys, DeleteApiKey. Reference `F:\continuum-server\internal\api\handlers\api_keys.go`.

- [ ] **Step 5: Register all in DI and commit**

```bash
git commit -am "feat: add HistoryImportApi, PluginsApi, RecommendationsApi, ApiKeysApi"
```

### Task 2.6: Extend Existing API Modules

**Files:**
- Modify: `src/ContinuumPlayer.Core/Api/AuthApi.cs`
- Modify: `src/ContinuumPlayer.Core/Api/CatalogApi.cs`
- Modify: `src/ContinuumPlayer.Core/Api/HomeApi.cs`
- Modify: `src/ContinuumPlayer.Core/Api/SettingsApi.cs`
- Modify: `src/ContinuumPlayer.Core/Api/PlaybackApi.cs`
- Modify: `src/ContinuumPlayer.Core/Api/AdminApi.cs`

- [ ] **Step 1: Extend AuthApi**

Add methods: Signup, GetSignupStatus, Setup, GetSetupStatus, GetAuthProviders, Logout, GetMe, GetSessions, RevokeSession, StartImpersonation (via admin), EndImpersonation. Reference `F:\continuum-server\internal\api\handlers\auth.go`.

- [ ] **Step 2: Extend CatalogApi**

Add methods: GetItemVersions, GetLibraryCollections, GetLibraryCollectionItems, GetLibrarySections, GetLibrarySectionItems, GetHistory, GetRatingsList, GetWatchlistItem, GetFavoriteItem, GetAudioPrefs, SetAudioPrefs, DeleteAudioPrefs, SyncProgress. Reference corresponding handler files.

- [ ] **Step 3: Extend HomeApi**

Add: UndoDismissal (`DELETE /home/dismissals/{surface}/{item_id}`).

- [ ] **Step 4: Extend SettingsApi**

Add: GetPluginSettings, GetPluginSetting, UpdatePluginSetting, GetProfileSections, UpdateProfileSections, ResetProfileSections, GetProfileSectionSettings.

- [ ] **Step 5: Extend PlaybackApi**

Add: GetSubtitles, DeleteSubtitle.

- [ ] **Step 6: Extend AdminApi**

This is the largest extension. Add all admin methods from the spec section 2.2. Group by feature area:
- User detail & impersonation
- Item matching
- Catalog seed/import/export
- Jobs
- Providers (CRUD)
- Library provider chains
- Library posters
- Skipped roots, stale IDs, unmatched items
- Empty root cleanup
- Library metadata refresh
- Invite codes (CRUD)
- Subtitle providers (CRUD + test)
- Rate limits
- Admin API keys (CRUD + tier)
- Sensitive settings status
- Recommendations status + triggers (embeddings, taste profiles, cowatch, recommendations)
- Task management (get, run, cancel, update triggers, get history)
- Admin collection extensions (import MDB, import TMDB, sync, delete image)
- History import sources (admin CRUD)
- Node sessions, force reload
- Playback control (pause, resume, stop, terminate, message)

Reference each handler file in `F:\continuum-server\internal\api\handlers/`.

- [ ] **Step 7: Build and commit**

```bash
cd src/ContinuumPlayer && dotnet build
git commit -am "feat: extend all existing API modules with new endpoints"
```

---

## Phase 3: Auth & Entry Points

### Task 3.1: Signup Page

**Files:**
- Create: `src/ContinuumPlayer/Views/SignupPage.xaml`
- Create: `src/ContinuumPlayer/Views/SignupPage.xaml.cs`
- Create: `src/ContinuumPlayer/ViewModels/SignupViewModel.cs`
- Modify: `src/ContinuumPlayer/App.xaml.cs` (DI registration)

- [ ] **Step 1: Create SignupViewModel**

Properties: Username, Email, Password, ConfirmPassword, InviteCode, IsSignupEnabled, IsLoading, ErrorMessage. Commands: SignupCommand, NavigateToLoginCommand. On init, check `GET /auth/signup` to see if signup is enabled.

- [ ] **Step 2: Create SignupPage XAML**

Match the web UI's signup form layout: centered card with branding, text inputs for username/email/password/confirm/invite code, submit button, link to login page. Use semantic theme brushes.

- [ ] **Step 3: Register in DI and add navigation route**

- [ ] **Step 4: Build and commit**

```bash
git commit -am "feat: add signup page"
```

### Task 3.2: Setup Wizard Page

**Files:**
- Create: `src/ContinuumPlayer/Views/SetupWizardPage.xaml`
- Create: `src/ContinuumPlayer/Views/SetupWizardPage.xaml.cs`
- Create: `src/ContinuumPlayer/ViewModels/SetupWizardViewModel.cs`

- [ ] **Step 1: Create SetupWizardViewModel**

5-step wizard: Account → Profile → Library → Server Settings → Metadata Provider. Track current step, completed steps. Each step has its own set of properties and a Next/Back command. Reference `F:\continuum-server\web\src\pages\SetupWizard.tsx` for the exact flow and fields.

- [ ] **Step 2: Create SetupWizardPage XAML**

Step indicator at top, content area changes per step, Back/Next buttons at bottom. Step 1: username/email/password. Step 2: profile name. Step 3: library paths/type/name/scan toggle. Step 4: Redis URL, FFmpeg path, transcode dir, hardware accel, transcoding toggle, Jellyfin URL/name. Step 5: metadata provider selection.

- [ ] **Step 3: Register and add navigation from ServerSelectPage (when server needs setup)**

- [ ] **Step 4: Build and commit**

```bash
git commit -am "feat: add setup wizard page"
```

### Task 3.3: Auth Sessions Management

**Files:**
- Modify: `src/ContinuumPlayer/Views/SettingsPage.xaml` (or new settings tab)
- Modify: `src/ContinuumPlayer/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Add sessions list to SettingsViewModel**

Add: Sessions property (ObservableCollection), LoadSessionsCommand, RevokeSessionCommand. Fetch via `AuthApi.GetSessionsAsync()`.

- [ ] **Step 2: Add sessions section to Settings UI**

Table/list of active sessions showing device name, IP, created date. Revoke button per session (not for current session). Current session highlighted.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add auth sessions management to settings"
```

### Task 3.4: Impersonation Banner Control

**Files:**
- Create: `src/ContinuumPlayer/Controls/ImpersonationBanner.xaml`
- Create: `src/ContinuumPlayer/Controls/ImpersonationBanner.xaml.cs`
- Modify: `src/ContinuumPlayer/MainWindow.xaml` (add banner above content)
- Modify: `src/ContinuumPlayer/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Create ImpersonationBanner control**

Amber/yellow bar at top of window. Shows "Impersonating {username}" and "End Impersonation" button. Visibility bound to `MainViewModel.IsImpersonating`.

- [ ] **Step 2: Add impersonation state to MainViewModel**

`IsImpersonating` property, `ImpersonatedUsername` property, `EndImpersonationCommand`.

- [ ] **Step 3: Add banner to MainWindow.xaml above ContentFrame**

- [ ] **Step 4: Build and commit**

```bash
git commit -am "feat: add impersonation banner control"
```

### Task 3.5: Update Login Page with Auth Providers

**Files:**
- Modify: `src/ContinuumPlayer/Views/LoginPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/LoginViewModel.cs`

- [ ] **Step 1: Add auth providers to LoginViewModel**

Load providers via `AuthApi.GetAuthProvidersAsync()` on page load. Display as alternative login buttons below the username/password form.

- [ ] **Step 2: Add signup link if enabled**

Check signup status, show "Create Account" link if enabled.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add auth providers and signup link to login page"
```

---

## Phase 4: Settings Overhaul

### Task 4.1: Settings Shell (Tabbed Layout)

**Files:**
- Modify: `src/ContinuumPlayer/Views/SettingsPage.xaml`
- Modify: `src/ContinuumPlayer/Views/SettingsPage.xaml.cs`
- Modify: `src/ContinuumPlayer/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Restructure SettingsPage as tabbed navigation**

Replace the current single-scroll settings page with a NavigationView or Pivot/TabView with tabs: Appearance, Playback, Subtitles, Home Screen, History Import, Plugins, Sessions. Each tab loads a different content area. Can use a nested NavigationView or TabView within the settings page.

- [ ] **Step 2: Move existing settings content to appropriate tabs**

Server settings → keep as first section or separate page. Library visibility → Home Screen tab.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: restructure settings as tabbed layout"
```

### Task 4.2: Appearance Settings Tab (Theme Picker)

**Files:**
- Modify: `src/ContinuumPlayer/Views/SettingsPage.xaml` (or create sub-view)
- Modify: `src/ContinuumPlayer/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Add theme picker grid to SettingsViewModel**

Expose: AvailableThemes (list of theme objects with Id, Label, Description, PreviewAccent, PreviewBackground, IsCurated), SelectedThemeId, SelectThemeCommand.

- [ ] **Step 2: Create theme picker UI**

Grid of theme preview cards. Each card shows: background color swatch, primary accent circle, label text, description. Curated themes highlighted with separator. Active theme has check indicator. Click to apply. Reset button.

Reference `F:\continuum-server\web\src\pages\settings\AppearanceSettings.tsx` for layout.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add appearance settings with theme picker"
```

### Task 4.3: Playback Settings Tab

**Files:**
- Modify: `src/ContinuumPlayer/Views/SettingsPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Add playback settings properties**

QualityPreference (dropdown: Auto/480p/720p/1080p/4K), SpokenLanguage (dropdown), SubtitleLanguage (dropdown), SubtitleMode (Auto/Always/Off), ShowForcedSubtitles (toggle), AutoSkipIntro (toggle), AutoSkipCredits (toggle), NextUpMode (combined/separate).

- [ ] **Step 2: Build playback settings UI**

Grouped settings with labels and controls. Save on change (auto-save to profile via API). Reference `F:\continuum-server\web\src\pages\settings\PlaybackSettings.tsx`.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add playback settings tab"
```

### Task 4.4: Subtitle Appearance Settings Tab

**Files:**
- Modify: `src/ContinuumPlayer/Views/SettingsPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Add subtitle appearance properties**

FontFamily, FontSize, FontColor (preset palette), OutlineEnabled, BackgroundStyle (None/Shadow/Box), BackgroundOpacity (slider, 0-1), BackgroundColor (preset palette), SubtitlePosition (Bottom/Top). Live preview text block.

- [ ] **Step 2: Build subtitle appearance UI**

Live preview area at top showing styled sample text. Controls below for each setting. Save/Reset buttons. Reference `F:\continuum-server\web\src\pages\settings\SubtitleAppearanceSettings.tsx`.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add subtitle appearance settings tab"
```

### Task 4.5: Home Screen Settings Tab

**Files:**
- Modify: `src/ContinuumPlayer/Views/SettingsPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Add home screen customization properties**

Scope (Home/Library dropdown), Sections (ObservableCollection of section entries with position, hidden, title, type). Commands: ReorderSection, ToggleSectionVisibility, AddSection, RemoveSection, SaveSections, ResetSections.

- [ ] **Step 2: Build home screen settings UI**

Scope selector at top. Drag-and-drop list of sections (use ListView with CanReorderItems). Per row: grip handle, section title, type badge, eye toggle, delete button. Add section button at bottom. Save/Reset buttons. Reference `F:\continuum-server\web\src\pages\settings\HomeScreenSettings.tsx`.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add home screen settings tab"
```

### Task 4.6: History Import Settings Tab

**Files:**
- Modify: `src/ContinuumPlayer/Views/SettingsPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Add history import properties**

SelectedSourceType (Emby/Jellyfin/Plex), connection fields per source type, SelectedProfile, ImportRuns list, CreateImportRunCommand. Plex OAuth flow: launch browser, poll for token.

- [ ] **Step 2: Build history import UI**

Source type tabs. Per source type: connection form fields. Profile selector. Start import button. Import runs list with status (queued/running/completed/failed), progress, matched/unmatched counts, expandable unmatched samples.

Reference `F:\continuum-server\web\src\pages\settings\HistoryImportSettings.tsx`.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add history import settings tab"
```

### Task 4.7: Plugin Settings Tab

**Files:**
- Modify: `src/ContinuumPlayer/Views/SettingsPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Add plugin settings properties**

PluginInstallations list (filtered to those with user_config_schema), per-plugin config values, SavePluginConfigCommand.

- [ ] **Step 2: Build plugin settings UI**

List of plugins with config forms. Each plugin card: plugin ID, version, dynamic form fields generated from config_schema JSON. Save button per plugin. Reference `F:\continuum-server\web\src\pages\settings\PluginSettings.tsx`.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add plugin settings tab"
```

---

## Phase 5: Browse & Discovery Enhancements

### Task 5.1: Person Detail Page

**Files:**
- Create: `src/ContinuumPlayer/Views/PersonDetailPage.xaml`
- Create: `src/ContinuumPlayer/Views/PersonDetailPage.xaml.cs`
- Create: `src/ContinuumPlayer/ViewModels/PersonDetailViewModel.cs`
- Modify: `src/ContinuumPlayer/App.xaml.cs` (DI registration)

- [ ] **Step 1: Create PersonDetailViewModel**

Properties: Person (name, bio, birth_date, death_date, birthplace, photo_url), Filmography (ObservableCollection of BrowseItems), SelectedTypeFilter (All/Movies/Series), IsLoading, RefreshCommand (admin only). Load person details and filmography on navigation.

- [ ] **Step 2: Create PersonDetailPage XAML**

Header section: photo (or initials circle), name, birth/death dates with age calculation, birthplace. Bio section (TextBlock, collapsible if long). Admin: refresh metadata button. Type filter buttons (All/Movies/Series). Filmography: poster card grid (reuse PosterCard control). Reference `F:\continuum-server\web\src\pages\PersonDetail.tsx`.

- [ ] **Step 3: Add person navigation from ItemDetailPage cast/crew links**

Make cast/crew names clickable, navigate to PersonDetailPage with person ID.

- [ ] **Step 4: Register and commit**

```bash
git commit -am "feat: add person detail page"
```

### Task 5.2: Library Collections & Sections Browse

**Files:**
- Modify: `src/ContinuumPlayer/Views/LibraryPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/LibraryViewModel.cs`

- [ ] **Step 1: Add library sub-navigation**

Add tabs/pivot to LibraryPage: Browse (existing), Collections, Recommended. Collections tab loads library collections via `CatalogApi.GetLibraryCollectionsAsync(libraryId)`. Click collection → browse its items.

- [ ] **Step 2: Add library sections**

Load per-library sections via `CatalogApi.GetLibrarySectionsAsync(libraryId)`. Display as horizontal carousel rows (reuse SectionRow control).

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add library collections and sections browse"
```

### Task 5.3: Enhanced Catalog Filters

**Files:**
- Modify: `src/ContinuumPlayer/Views/LibraryPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/LibraryViewModel.cs`

- [ ] **Step 1: Extend filter support**

Add filter properties: Genre, Studio, Network, Country, ContentRating, Resolution, AudioLanguage, SubtitleLanguage. Load available values from `GET /catalog/filters`. Apply filters to catalog query.

- [ ] **Step 2: Add filter bar UI**

Horizontal bar of filter dropdowns above the grid. Active filter badges showing applied filters with X buttons to clear. Reference `F:\continuum-server\web\src\components\catalog\CatalogFilterBar.tsx`.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add enhanced catalog filters"
```

### Task 5.4: History Page Update & Recommendations Enhancements

**Files:**
- Modify: `src/ContinuumPlayer/Views/HistoryPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/HistoryViewModel.cs`
- Modify: `src/ContinuumPlayer/Views/RecommendationsPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/RecommendationsViewModel.cs`

- [ ] **Step 1: Update HistoryPage to use dedicated endpoint**

Switch from `GET /progress` to `GET /history` for the history page. Show watched items with timestamps and completion status.

- [ ] **Step 2: Add recommendation sections**

Add for-you/main hero section, similar-users section, recently-added section to RecommendationsPage. Use new API endpoints.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: update history page and add recommendation sections"
```

---

## Phase 6: User Collections

### Task 6.1: Collections Page

**Files:**
- Create: `src/ContinuumPlayer/Views/CollectionsPage.xaml`
- Create: `src/ContinuumPlayer/Views/CollectionsPage.xaml.cs`
- Create: `src/ContinuumPlayer/ViewModels/CollectionsViewModel.cs`

- [ ] **Step 1: Create CollectionsViewModel**

Properties: Collections (ObservableCollection), CreateCollectionCommand, EditCollectionCommand, DeleteCollectionCommand. Load via `CollectionsApi.GetCollectionsAsync()`.

- [ ] **Step 2: Create CollectionsPage XAML**

Grid of collection cards. Each card: name, type badge (manual/smart), shared indicator, item count. Create button (top right or empty state CTA). Edit/delete on hover or context menu. Reference `F:\continuum-server\web\src\pages\Collections.tsx`.

- [ ] **Step 3: Register and update sidebar navigation**

The sidebar already has a Collections nav item. Wire it to navigate to this page.

- [ ] **Step 4: Build and commit**

```bash
git commit -am "feat: add user collections page"
```

### Task 6.2: Collection Editor Page

**Files:**
- Create: `src/ContinuumPlayer/Views/CollectionEditorPage.xaml`
- Create: `src/ContinuumPlayer/Views/CollectionEditorPage.xaml.cs`
- Create: `src/ContinuumPlayer/ViewModels/CollectionEditorViewModel.cs`
- Create: `src/ContinuumPlayer/Controls/CollectionRulesEditor.xaml`
- Create: `src/ContinuumPlayer/Controls/CollectionRulesEditor.xaml.cs`

- [ ] **Step 1: Create CollectionEditorViewModel**

Properties: Name, CollectionType (manual/smart), IsShared, Rules (for smart), Items (for manual), PreviewItems, SaveCommand, PreviewCommand. For smart collections: query_definition (filter rules), sort_config.

- [ ] **Step 2: Create CollectionRulesEditor control**

Guided rule builder: each rule row has field dropdown (title, genre, year, studio, etc.), operator dropdown (is, contains, etc.), value input. Add rule/group buttons. Live preview via `CollectionsApi.PreviewCollectionAsync()`.

- [ ] **Step 3: Create CollectionEditorPage XAML**

Name input, type selector, sharing toggle. For smart: rules editor + preview pane. For manual: item search with add button, drag-and-drop list. Save/Cancel buttons. Reference `F:\continuum-server\web\src\pages\CollectionEditor.tsx`.

- [ ] **Step 4: Build and commit**

```bash
git commit -am "feat: add collection editor with rules builder"
```

---

## Phase 7: Power Features

### Task 7.1: Downloads

**Files:**
- Create: `src/ContinuumPlayer/Views/DownloadsPage.xaml` (or integrate into existing page)
- Create: `src/ContinuumPlayer/ViewModels/DownloadsViewModel.cs`
- Modify: `src/ContinuumPlayer/Views/ItemDetailPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/ItemDetailViewModel.cs`

- [ ] **Step 1: Add download button to ItemDetailPage**

Show download button if user has download permission (from user profile). On click, queue download via `DownloadsApi.CreateDownloadAsync()`.

- [ ] **Step 2: Create DownloadsViewModel**

Properties: Downloads list, DeleteDownloadCommand, DownloadFileCommand (saves to user-selected folder via file picker dialog).

- [ ] **Step 3: Create DownloadsPage or integrate into sidebar**

List of downloads with status, file name, delete button, save-to-disk button.

- [ ] **Step 4: Build and commit**

```bash
git commit -am "feat: add downloads support"
```

### Task 7.2: Audio Preferences & Subtitle Management

**Files:**
- Modify: `src/ContinuumPlayer/ViewModels/ItemDetailViewModel.cs`
- Modify: `src/ContinuumPlayer/Controls/PlayerOverlay.xaml.cs`

- [ ] **Step 1: Add audio preference persistence**

When user changes audio track during playback of a series, save preference via `CatalogApi.SetAudioPrefsAsync(seriesId, request)`. On next playback of same series, use saved preference to set initial audio track.

- [ ] **Step 2: Add subtitle management to ItemDetailPage**

Show downloaded subtitles for current file. Allow deleting individual downloaded subtitles via `PlaybackApi.DeleteSubtitleAsync(id)`.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add audio preferences and subtitle management"
```

### Task 7.3: Sync Progress

**Files:**
- Modify: `src/ContinuumPlayer.Core/Services/PlaybackManager.cs`

- [ ] **Step 1: Integrate sync progress endpoint**

Add `SyncProgressAsync()` call to PlaybackManager alongside existing progress reporting. Use `POST /sync/progress` for cross-device sync.

- [ ] **Step 2: Build and commit**

```bash
git commit -am "feat: add sync progress support"
```

---

## Phase 8: Admin Enhancements

### Task 8.1: Admin Plugins Page

**Files:**
- Create: `src/ContinuumPlayer/Views/Admin/AdminPluginsPage.xaml`
- Create: `src/ContinuumPlayer/Views/Admin/AdminPluginsPage.xaml.cs`
- Create: `src/ContinuumPlayer/ViewModels/Admin/AdminPluginsViewModel.cs`
- Modify: `src/ContinuumPlayer/Views/Admin/AdminShellPage.xaml` (add nav item)

- [ ] **Step 1: Create AdminPluginsViewModel**

Properties: Installations, CatalogEntries, Repositories, SelectedTab (Installed/Available). Commands: Install, Update, Enable/Disable, Delete, Configure, AddRepository, DeleteRepository, UploadPlugin. Configure opens a dialog/flyout with: global config form, auth bindings, task bindings, analyzer bindings.

Reference `F:\continuum-server\web\src\pages\AdminPlugins.tsx` for the full feature set.

- [ ] **Step 2: Create AdminPluginsPage XAML**

Tabs: Installed / Available. Installed: list of installations with enable toggle, version, configure button, delete. Available: catalog entries with install button. Repository management section at bottom. Reference web UI layout.

- [ ] **Step 3: Add plugin config dialog**

Accordion/expander sections for: Global Config (dynamic form from schema), Auth Bindings, Task Bindings, Analyzer Bindings. Each binding type has enable toggle and type-specific config.

- [ ] **Step 4: Register in DI, add nav item to AdminShellPage, commit**

```bash
git commit -am "feat: add admin plugins page"
```

### Task 8.2: Admin Task Detail Page Enhancement

**Files:**
- Modify: `src/ContinuumPlayer/Views/Admin/AdminTaskDetailPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/Admin/AdminTaskDetailViewModel.cs`

- [ ] **Step 1: Add trigger configuration**

Add trigger management: view/add/edit/remove triggers. Trigger types: interval (with ms editor), daily (with time picker), weekly (with day selector + time), startup. Max runtime input.

- [ ] **Step 2: Add execution history table**

Table showing: started time, duration, status (color-coded badge: completed/failed/cancelled), error message (expandable), result_data JSON (expandable). Run now and Cancel buttons.

Reference `F:\continuum-server\web\src\pages\AdminTaskDetail.tsx`.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: enhance admin task detail with triggers and history"
```

### Task 8.3: Admin Providers, Invite Codes, Maintenance

**Files:**
- Create: `src/ContinuumPlayer/Views/Admin/AdminProvidersPage.xaml`
- Create: `src/ContinuumPlayer/Views/Admin/AdminProvidersPage.xaml.cs`
- Create: `src/ContinuumPlayer/ViewModels/Admin/AdminProvidersViewModel.cs`
- Create: `src/ContinuumPlayer/Views/Admin/AdminInviteCodesPage.xaml`
- Create: `src/ContinuumPlayer/Views/Admin/AdminInviteCodesPage.xaml.cs`
- Create: `src/ContinuumPlayer/ViewModels/Admin/AdminInviteCodesViewModel.cs`
- Create: `src/ContinuumPlayer/Views/Admin/AdminMaintenancePage.xaml`
- Create: `src/ContinuumPlayer/Views/Admin/AdminMaintenancePage.xaml.cs`
- Create: `src/ContinuumPlayer/ViewModels/Admin/AdminMaintenanceViewModel.cs`
- Create: `src/ContinuumPlayer/Views/Admin/AdminSubtitleProvidersPage.xaml`
- Create: `src/ContinuumPlayer/Views/Admin/AdminSubtitleProvidersPage.xaml.cs`
- Create: `src/ContinuumPlayer/ViewModels/Admin/AdminSubtitleProvidersViewModel.cs`

- [ ] **Step 1: Create AdminProvidersPage**

Table of metadata providers: slug, type, enabled, settings (masked). Create/edit dialog with settings fields. Delete with confirmation. Per-library provider chain config.

- [ ] **Step 2: Create AdminInviteCodesPage**

Table: code, label, max uses, use count, enabled, created. Create dialog (code, label, max uses). Enable/disable toggle. Delete button.

- [ ] **Step 3: Create AdminMaintenancePage**

Sections: Stale IDs (list with rematch button), Skipped Roots (list), Unmatched Files (list with file details), Catalog Import/Export tools.

- [ ] **Step 4: Create AdminSubtitleProvidersPage**

Per provider: config form (API key, credentials), test button, enable toggle.

- [ ] **Step 5: Register all in DI, add nav items to AdminShellPage, commit**

```bash
git commit -am "feat: add admin providers, invite codes, maintenance, subtitle providers pages"
```

### Task 8.4: Admin User Detail Page Enhancement

**Files:**
- Modify: `src/ContinuumPlayer/Views/Admin/AdminUserDetailPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/Admin/AdminUserDetailViewModel.cs`

- [ ] **Step 1: Add tabs**

Overview (existing + expanded) / Profiles / Watch History / IP History tabs.

- [ ] **Step 2: Add profiles tab**

Load user's profiles via `AdminApi.GetUserProfilesAsync(userId)`. Display as cards.

- [ ] **Step 3: Add watch history tab**

Table: media title, profile, play method, watch time, completed status, ended time. Load via admin playback history endpoint filtered by user.

- [ ] **Step 4: Add IP history tab**

List of IPs with first/last seen. Load via `AdminApi.GetUserIpsAsync(userId)`.

- [ ] **Step 5: Add impersonate and delete actions**

Impersonate button (with confirmation dialog). Delete button (with confirmation). Reference `F:\continuum-server\web\src\pages\AdminUserDetail.tsx`.

- [ ] **Step 6: Build and commit**

```bash
git commit -am "feat: enhance admin user detail with tabs and actions"
```

### Task 8.5: Admin API Keys & Session Control

**Files:**
- Modify: `src/ContinuumPlayer/Views/Admin/AdminApiKeysPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/Admin/AdminApiKeysViewModel.cs`
- Modify: `src/ContinuumPlayer/Views/Admin/AdminActivityPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/Admin/AdminActivityViewModel.cs`

- [ ] **Step 1: Enhance AdminApiKeysPage**

Add: create dialog (label + user selection), copy key button (shown once after creation), tier dropdown (standard/elevated), pagination (25/50/100).

- [ ] **Step 2: Add session control to AdminActivityPage**

Per active session: Pause/Resume/Stop/Terminate buttons, Send Message action. Reference `F:\continuum-server\internal\api\handlers\admin_playback_control.go`.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: enhance admin API keys and add session control"
```

### Task 8.6: Item Matching Dialog

**Files:**
- Create: `src/ContinuumPlayer/Controls/MatchItemDialog.xaml`
- Create: `src/ContinuumPlayer/Controls/MatchItemDialog.xaml.cs`
- Modify: `src/ContinuumPlayer/Views/ItemDetailPage.xaml`
- Modify: `src/ContinuumPlayer/ViewModels/ItemDetailViewModel.cs`

- [ ] **Step 1: Create MatchItemDialog**

ContentDialog with search input, results list showing metadata preview (title, year, provider IDs), apply button. Admin-only visibility.

- [ ] **Step 2: Add match button to ItemDetailPage (admin only)**

Show "Match" button in admin actions area. Opens MatchItemDialog.

- [ ] **Step 3: Build and commit**

```bash
git commit -am "feat: add item matching dialog for admin"
```

---

## Phase 9: Polish & Integration

### Task 9.1: Navigation & Sidebar Updates

**Files:**
- Modify: `src/ContinuumPlayer/MainWindow.xaml`
- Modify: `src/ContinuumPlayer/MainWindow.xaml.cs`

- [ ] **Step 1: Verify sidebar navigation items**

Ensure Collections, History, and all admin sub-pages are properly wired. Add any missing nav items for new pages (Downloads if separate page).

- [ ] **Step 2: Add person search results to SearchPage**

Include people in search results. Display person results with photo/initials and name. Click → PersonDetailPage.

- [ ] **Step 3: Commit**

```bash
git commit -am "feat: finalize navigation and search integration"
```

### Task 9.2: Miscellaneous API Integration

**Files:**
- Modify: Various viewmodels

- [ ] **Step 1: Home dismissal undo**

In HomeViewModel, add undo-dismiss support. When user dismisses, show undo option that calls `HomeApi.UndoDismissalAsync()`.

- [ ] **Step 2: Individual watchlist/favorites check**

In ItemDetailViewModel, use `GetFavoriteItemAsync`/`GetWatchlistItemAsync` for precise state checking.

- [ ] **Step 3: Ratings list**

Add ratings list view (accessible from settings or profile) using `CatalogApi.GetRatingsListAsync()`.

- [ ] **Step 4: Commit**

```bash
git commit -am "feat: add dismissal undo, state checks, ratings list"
```

### Task 9.3: Final Build & Testing

- [ ] **Step 1: Full build**

```bash
cd F:/ContinuumPlayer && dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj
```

- [ ] **Step 2: Run existing tests**

```bash
dotnet test tests/ContinuumPlayer.Core.Tests/
```

- [ ] **Step 3: Manual smoke test**

Launch app, verify:
- Theme switching works for all 13 themes
- Fonts change per theme
- All new pages load without crash
- Admin pages accessible for admin users
- Settings tabs all functional
- Person detail navigable from item detail cast
- Collections CRUD works
- Plugin settings render dynamic forms

- [ ] **Step 4: Final commit**

```bash
git commit -am "feat: complete Continuum full parity update"
```
