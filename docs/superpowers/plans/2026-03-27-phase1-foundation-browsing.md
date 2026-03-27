# Phase 1: Foundation & Browsing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a working WinUI 3 desktop app that can connect to a Continuum server, authenticate, select a profile, display the home screen with hero carousel and section rows, and browse media libraries with filtering/sorting.

**Architecture:** WinUI 3 + .NET 8 with MVVM pattern. Three-project solution: `ContinuumPlayer` (UI), `ContinuumPlayer.Core` (business logic/API), `ContinuumPlayer.Core.Tests` (unit tests). Services injected via `Microsoft.Extensions.DependencyInjection`. All API communication through a typed `ContinuumApiClient`.

**Tech Stack:** .NET 8, WinUI 3 (Windows App SDK), CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Http, System.Text.Json, xUnit, NSubstitute

---

## Prerequisites

Before starting, ensure the following are installed:
- Visual Studio 2022 (17.8+) with ".NET desktop development" and "Windows application development" workloads
- .NET 8 SDK
- Windows App SDK (installed via VS workload)

---

## File Structure

```
src/
  ContinuumPlayer/                          # WinUI 3 app
    App.xaml / App.xaml.cs                   # Entry point, DI container
    MainWindow.xaml / MainWindow.xaml.cs     # Navigation shell with sidebar
    Views/
      ServerSelectPage.xaml(.cs)             # Add/select server
      LoginPage.xaml(.cs)                    # Username/password login
      ProfileSelectPage.xaml(.cs)            # Profile picker + PIN
      HomePage.xaml(.cs)                     # Hero carousel + section rows
      LibraryPage.xaml(.cs)                  # Poster grid with filters
    ViewModels/
      ServerSelectViewModel.cs
      LoginViewModel.cs
      ProfileSelectViewModel.cs
      HomeViewModel.cs
      LibraryViewModel.cs
      MainViewModel.cs
    Controls/
      HeroCarousel.xaml(.cs)                # Featured section carousel
      SectionRow.xaml(.cs)                  # Horizontal poster row
      PosterCard.xaml(.cs)                  # Single poster with badges
    Themes/
      DarkTheme.xaml                        # Dark color resources
    Converters/
      BoolToVisibilityConverter.cs
      ThumbhashToImageConverter.cs
    Helpers/
      NavigationService.cs                  # Frame navigation helper
    Assets/
      continuum-logo.png                    # App branding

  ContinuumPlayer.Core/                     # Class library
    Models/
      ServerConfig.cs                       # Server list + app settings
      ApiError.cs                           # Error response DTO
      Auth/
        LoginRequest.cs
        LoginResponse.cs
        RefreshRequest.cs
        RefreshResponse.cs
        ProfilesResponse.cs
        VerifyPinRequest.cs
        VerifyPinResponse.cs
      Home/
        HomeLayoutResponse.cs
        HomeSectionsResponse.cs
      Catalog/
        CatalogResponse.cs
        CatalogFiltersResponse.cs
        Library.cs
    Api/
      ContinuumApiClient.cs                # Base HTTP client with auth
      AuthApi.cs                            # Auth endpoint methods
      HomeApi.cs                            # Home endpoint methods
      CatalogApi.cs                         # Catalog/library endpoints
    Services/
      SettingsService.cs                    # JSON config in AppData
      CredentialStore.cs                    # Windows Credential Manager
      AuthService.cs                        # Login flow, token refresh
      ImageService.cs                       # LRU + disk cache
      ThumbhashDecoder.cs                   # Thumbhash to bitmap

tests/
  ContinuumPlayer.Core.Tests/
    Models/
      DeserializationTests.cs              # Verify DTOs match live API
    Api/
      ContinuumApiClientTests.cs           # Auth header injection
    Services/
      SettingsServiceTests.cs
      AuthServiceTests.cs
      ImageServiceTests.cs
```

---

### Task 1: Solution Scaffolding

**Files:**
- Create: `ContinuumPlayer.sln`
- Create: `src/ContinuumPlayer/ContinuumPlayer.csproj`
- Create: `src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj`
- Create: `tests/ContinuumPlayer.Core.Tests/ContinuumPlayer.Core.Tests.csproj`

- [ ] **Step 1: Create the solution and WinUI 3 project**

```bash
cd F:/ContinuumPlayer
dotnet new winui3 -n ContinuumPlayer -o src/ContinuumPlayer --framework net8.0-windows10.0.22621.0
```

- [ ] **Step 2: Create the Core class library**

```bash
dotnet new classlib -n ContinuumPlayer.Core -o src/ContinuumPlayer.Core --framework net8.0
```

- [ ] **Step 3: Create the test project**

```bash
dotnet new xunit -n ContinuumPlayer.Core.Tests -o tests/ContinuumPlayer.Core.Tests --framework net8.0
```

- [ ] **Step 4: Create the solution file and add all projects**

```bash
dotnet new sln -n ContinuumPlayer
dotnet sln add src/ContinuumPlayer/ContinuumPlayer.csproj
dotnet sln add src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj
dotnet sln add tests/ContinuumPlayer.Core.Tests/ContinuumPlayer.Core.Tests.csproj
```

- [ ] **Step 5: Add project references**

```bash
dotnet add src/ContinuumPlayer/ContinuumPlayer.csproj reference src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj
dotnet add tests/ContinuumPlayer.Core.Tests/ContinuumPlayer.Core.Tests.csproj reference src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj
```

- [ ] **Step 6: Add NuGet packages to Core**

```bash
dotnet add src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj package Microsoft.Extensions.Http
dotnet add src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj package CommunityToolkit.Mvvm
```

- [ ] **Step 7: Add NuGet packages to UI**

```bash
dotnet add src/ContinuumPlayer/ContinuumPlayer.csproj package Microsoft.Extensions.DependencyInjection
dotnet add src/ContinuumPlayer/ContinuumPlayer.csproj package CommunityToolkit.Mvvm
dotnet add src/ContinuumPlayer/ContinuumPlayer.csproj package CommunityToolkit.WinUI.Controls.Primitives
```

- [ ] **Step 8: Add NuGet packages to Tests**

```bash
dotnet add tests/ContinuumPlayer.Core.Tests/ContinuumPlayer.Core.Tests.csproj package NSubstitute
dotnet add tests/ContinuumPlayer.Core.Tests/ContinuumPlayer.Core.Tests.csproj package Microsoft.Extensions.Http
```

- [ ] **Step 9: Create directory structure**

```bash
cd src/ContinuumPlayer.Core
mkdir -p Models/Auth Models/Home Models/Catalog Api Services

cd ../ContinuumPlayer
mkdir -p Views ViewModels Controls Themes Converters Helpers Assets

cd ../../tests/ContinuumPlayer.Core.Tests
mkdir -p Models Api Services
```

- [ ] **Step 10: Verify solution builds**

Run: `dotnet build ContinuumPlayer.sln`
Expected: Build succeeded with 0 errors.

- [ ] **Step 11: Commit**

```bash
git init
echo "bin/\nobj/\n.vs/\n*.user\n.superpowers/" > .gitignore
git add -A
git commit -m "feat: scaffold solution with WinUI 3, Core library, and test project"
```

---

### Task 2: Core API Models

**Files:**
- Create: `src/ContinuumPlayer.Core/Models/ApiError.cs`
- Create: `src/ContinuumPlayer.Core/Models/ServerConfig.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/LoginRequest.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/LoginResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/RefreshRequest.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/RefreshResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/ProfilesResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/VerifyPinRequest.cs`
- Create: `src/ContinuumPlayer.Core/Models/Auth/VerifyPinResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Catalog/Library.cs`
- Create: `src/ContinuumPlayer.Core/Models/Home/HomeLayoutResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Home/HomeSectionsResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Catalog/CatalogResponse.cs`
- Create: `src/ContinuumPlayer.Core/Models/Catalog/CatalogFiltersResponse.cs`
- Test: `tests/ContinuumPlayer.Core.Tests/Models/DeserializationTests.cs`

- [ ] **Step 1: Write deserialization tests using real API JSON**

Create `tests/ContinuumPlayer.Core.Tests/Models/DeserializationTests.cs`:

```csharp
using System.Text.Json;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Models;

namespace ContinuumPlayer.Core.Tests.Models;

public class DeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public void Deserialize_LoginResponse()
    {
        var json = """
        {
            "access_token": "jwt-abc",
            "refresh_token": "jwt-xyz",
            "expires_in": 86400,
            "user": {
                "id": 1,
                "username": "mike",
                "email": "mike@example.com",
                "role": "admin"
            }
        }
        """;
        var result = JsonSerializer.Deserialize<LoginResponse>(json, JsonOptions)!;
        Assert.Equal("jwt-abc", result.AccessToken);
        Assert.Equal("jwt-xyz", result.RefreshToken);
        Assert.Equal(86400, result.ExpiresIn);
        Assert.Equal(1, result.User.Id);
        Assert.Equal("mike", result.User.Username);
        Assert.Equal("admin", result.User.Role);
    }

    [Fact]
    public void Deserialize_ProfilesResponse()
    {
        var json = """
        {
            "profiles": [{
                "id": "b49b0a6a-d185-4a98-b1de-753735f1919e",
                "name": "Mike",
                "has_pin": false,
                "is_child": false,
                "quality_preference": "auto",
                "subtitle_language": "en",
                "subtitle_mode": "always",
                "auto_skip_intro": true,
                "auto_skip_credits": true,
                "show_forced_subtitles": true,
                "library_restrictions_enabled": false,
                "allowed_library_ids": null,
                "max_playback_quality": "",
                "created_at": "2026-03-19T03:23:23Z",
                "updated_at": "2026-03-27T00:08:00Z"
            }]
        }
        """;
        var result = JsonSerializer.Deserialize<ProfilesResponse>(json, JsonOptions)!;
        Assert.Single(result.Profiles);
        Assert.Equal("Mike", result.Profiles[0].Name);
        Assert.False(result.Profiles[0].HasPin);
        Assert.True(result.Profiles[0].AutoSkipIntro);
    }

    [Fact]
    public void Deserialize_Library()
    {
        var json = """
        [
            {"id": 20, "name": "Movies", "type": "movies", "poster_url": "https://example.com/poster.jpg"},
            {"id": 19, "name": "TV Shows", "type": "series", "poster_url": "https://example.com/tv.jpg"}
        ]
        """;
        var result = JsonSerializer.Deserialize<List<Library>>(json, JsonOptions)!;
        Assert.Equal(2, result.Count);
        Assert.Equal("Movies", result[0].Name);
        Assert.Equal("movies", result[0].Type);
    }

    [Fact]
    public void Deserialize_HomeLayout()
    {
        var json = """
        {
            "sections": [
                {
                    "id": "118166621620535300",
                    "section_type": "collection",
                    "title": "Popular Movies this Week",
                    "featured": false,
                    "item_limit": 50,
                    "is_custom": false,
                    "customized": false
                },
                {
                    "id": "117998017897824260",
                    "section_type": "continue_watching",
                    "title": "Continue Watching",
                    "featured": true,
                    "item_limit": 20,
                    "is_custom": false,
                    "customized": true
                }
            ]
        }
        """;
        var result = JsonSerializer.Deserialize<HomeLayoutResponse>(json, JsonOptions)!;
        Assert.Equal(2, result.Sections.Count);
        Assert.False(result.Sections[0].Featured);
        Assert.True(result.Sections[1].Featured);
        Assert.Equal("continue_watching", result.Sections[1].SectionType);
    }

    [Fact]
    public void Deserialize_HomeSectionItem()
    {
        var json = """
        {
            "sections": [{
                "id": "118166621620535300",
                "section_type": "collection",
                "title": "Popular Movies this Week",
                "featured": false,
                "item_limit": 50,
                "total_count": 35,
                "is_custom": false,
                "customized": false,
                "items": [{
                    "content_id": "115561387572356099",
                    "type": "movie",
                    "title": "Peaky Blinders: The Immortal Man",
                    "year": 2026,
                    "genres": ["Crime", "Drama"],
                    "status": "matched",
                    "overview": "Tommy Shelby must choose.",
                    "poster_url": "https://example.com/poster.jpg",
                    "poster_thumbhash": "iBgGDQAbpriMVqroOGBLh5zfiPda",
                    "backdrop_url": "https://example.com/backdrop.jpg",
                    "backdrop_thumbhash": "xigCDIA2Un39QYi3V2dH/HeWTw==",
                    "logo_url": "https://example.com/logo.png",
                    "overlay_summary": {
                        "resolution": "2160p",
                        "audio": "EAC3",
                        "release_type": "WEB-DL"
                    },
                    "user_state": {
                        "played": false,
                        "is_favorite": false,
                        "in_watchlist": false
                    }
                }]
            }]
        }
        """;
        var result = JsonSerializer.Deserialize<HomeSectionsResponse>(json, JsonOptions)!;
        var item = result.Sections[0].Items[0];
        Assert.Equal("115561387572356099", item.ContentId);
        Assert.Equal("Peaky Blinders: The Immortal Man", item.Title);
        Assert.Equal(2026, item.Year);
        Assert.Equal("2160p", item.OverlaySummary.Resolution);
        Assert.False(item.UserState.Played);
    }

    [Fact]
    public void Deserialize_ApiError()
    {
        var json = """{"error": "unauthorized", "message": "Invalid token"}""";
        var result = JsonSerializer.Deserialize<ApiError>(json, JsonOptions)!;
        Assert.Equal("unauthorized", result.Error);
        Assert.Equal("Invalid token", result.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests --filter DeserializationTests -v n`
Expected: FAIL -- types don't exist yet.

- [ ] **Step 3: Create ApiError model**

Create `src/ContinuumPlayer.Core/Models/ApiError.cs`:

```csharp
namespace ContinuumPlayer.Core.Models;

public class ApiError
{
    public string Error { get; set; } = "";
    public string Message { get; set; } = "";
}
```

- [ ] **Step 4: Create ServerConfig model**

Create `src/ContinuumPlayer.Core/Models/ServerConfig.cs`:

```csharp
namespace ContinuumPlayer.Core.Models;

public class ServerEntry
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime LastUsed { get; set; }
}

public class AppSettings
{
    public List<ServerEntry> Servers { get; set; } = [];
    public string? LastProfileId { get; set; }
}
```

- [ ] **Step 5: Create Auth models**

Create `src/ContinuumPlayer.Core/Models/Auth/LoginRequest.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Auth;

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
```

Create `src/ContinuumPlayer.Core/Models/Auth/LoginResponse.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Auth;

public class LoginResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
    public UserInfo User { get; set; } = new();
}

public class UserInfo
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
}
```

Create `src/ContinuumPlayer.Core/Models/Auth/RefreshRequest.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Auth;

public class RefreshRequest
{
    public string RefreshToken { get; set; } = "";
}
```

Create `src/ContinuumPlayer.Core/Models/Auth/RefreshResponse.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Auth;

public class RefreshResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
}
```

Create `src/ContinuumPlayer.Core/Models/Auth/ProfilesResponse.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Auth;

public class ProfilesResponse
{
    public List<Profile> Profiles { get; set; } = [];
}

public class Profile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool HasPin { get; set; }
    public bool IsChild { get; set; }
    public string QualityPreference { get; set; } = "";
    public string SubtitleLanguage { get; set; } = "";
    public string SubtitleMode { get; set; } = "";
    public bool AutoSkipIntro { get; set; }
    public bool AutoSkipCredits { get; set; }
    public bool ShowForcedSubtitles { get; set; }
    public bool LibraryRestrictionsEnabled { get; set; }
    public List<int>? AllowedLibraryIds { get; set; }
    public string MaxPlaybackQuality { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}
```

Create `src/ContinuumPlayer.Core/Models/Auth/VerifyPinRequest.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Auth;

public class VerifyPinRequest
{
    public string Pin { get; set; } = "";
}
```

Create `src/ContinuumPlayer.Core/Models/Auth/VerifyPinResponse.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Auth;

public class VerifyPinResponse
{
    public bool Valid { get; set; }
    public string ProfileToken { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
}
```

- [ ] **Step 6: Create Library model**

Create `src/ContinuumPlayer.Core/Models/Catalog/Library.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Catalog;

public class Library
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? PosterUrl { get; set; }
}
```

- [ ] **Step 7: Create Home models**

Create `src/ContinuumPlayer.Core/Models/Home/HomeLayoutResponse.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Home;

public class HomeLayoutResponse
{
    public List<HomeSection> Sections { get; set; } = [];
}

public class HomeSection
{
    public string Id { get; set; } = "";
    public string SectionType { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Featured { get; set; }
    public int ItemLimit { get; set; }
    public bool IsCustom { get; set; }
    public bool Customized { get; set; }
}
```

Create `src/ContinuumPlayer.Core/Models/Home/HomeSectionsResponse.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Home;

public class HomeSectionsResponse
{
    public List<HomeSectionWithItems> Sections { get; set; } = [];
}

public class HomeSectionWithItems
{
    public string Id { get; set; } = "";
    public string SectionType { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Featured { get; set; }
    public int ItemLimit { get; set; }
    public int TotalCount { get; set; }
    public bool IsCustom { get; set; }
    public bool Customized { get; set; }
    public List<MediaItem> Items { get; set; } = [];
}

public class MediaItem
{
    public string ContentId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public List<string> Genres { get; set; } = [];
    public string Status { get; set; } = "";
    public string Overview { get; set; } = "";
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public string? BackdropUrl { get; set; }
    public string? BackdropThumbhash { get; set; }
    public string? LogoUrl { get; set; }
    public OverlaySummary? OverlaySummary { get; set; }
    public UserState? UserState { get; set; }
}

public class OverlaySummary
{
    public string Resolution { get; set; } = "";
    public string Audio { get; set; } = "";
    public string ReleaseType { get; set; } = "";
}

public class UserState
{
    public bool Played { get; set; }
    public bool IsFavorite { get; set; }
    public bool InWatchlist { get; set; }
}
```

- [ ] **Step 8: Create Catalog models**

Create `src/ContinuumPlayer.Core/Models/Catalog/CatalogResponse.cs`:
```csharp
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Core.Models.Catalog;

public class CatalogResponse
{
    public List<MediaItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}
```

Create `src/ContinuumPlayer.Core/Models/Catalog/CatalogFiltersResponse.cs`:
```csharp
namespace ContinuumPlayer.Core.Models.Catalog;

public class CatalogFiltersResponse
{
    public List<string> Genres { get; set; } = [];
    public List<string> Studios { get; set; } = [];
    public List<string> Resolutions { get; set; } = [];
    public List<string> AudioLanguages { get; set; } = [];
    public List<string> ContentRatings { get; set; } = [];
    public List<string> Countries { get; set; } = [];
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests --filter DeserializationTests -v n`
Expected: All 6 tests PASS.

- [ ] **Step 10: Commit**

```bash
git add src/ContinuumPlayer.Core/Models/ tests/ContinuumPlayer.Core.Tests/Models/
git commit -m "feat: add API response models with deserialization tests"
```

---

### Task 3: Settings Service

**Files:**
- Create: `src/ContinuumPlayer.Core/Services/SettingsService.cs`
- Test: `tests/ContinuumPlayer.Core.Tests/Services/SettingsServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/ContinuumPlayer.Core.Tests/Services/SettingsServiceTests.cs`:

```csharp
using ContinuumPlayer.Core.Models;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Core.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ContinuumPlayerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _service = new SettingsService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void LoadSettings_NoFile_ReturnsDefaults()
    {
        var settings = _service.Load();
        Assert.NotNull(settings);
        Assert.Empty(settings.Servers);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var settings = new AppSettings
        {
            Servers = [new ServerEntry { Url = "https://example.com", Name = "Test", LastUsed = DateTime.UtcNow }],
            LastProfileId = "abc-123"
        };
        _service.Save(settings);

        var loaded = _service.Load();
        Assert.Single(loaded.Servers);
        Assert.Equal("https://example.com", loaded.Servers[0].Url);
        Assert.Equal("abc-123", loaded.LastProfileId);
    }

    [Fact]
    public void AddServer_AddsToList()
    {
        _service.AddServer("https://server1.com", "Server 1");
        _service.AddServer("https://server2.com", "Server 2");

        var settings = _service.Load();
        Assert.Equal(2, settings.Servers.Count);
    }

    [Fact]
    public void RemoveServer_RemovesByUrl()
    {
        _service.AddServer("https://server1.com", "Server 1");
        _service.AddServer("https://server2.com", "Server 2");
        _service.RemoveServer("https://server1.com");

        var settings = _service.Load();
        Assert.Single(settings.Servers);
        Assert.Equal("https://server2.com", settings.Servers[0].Url);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests --filter SettingsServiceTests -v n`
Expected: FAIL -- `SettingsService` doesn't exist.

- [ ] **Step 3: Implement SettingsService**

Create `src/ContinuumPlayer.Core/Services/SettingsService.cs`:

```csharp
using System.Text.Json;
using ContinuumPlayer.Core.Models;

namespace ContinuumPlayer.Core.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;

    public SettingsService(string appDataDir)
    {
        Directory.CreateDirectory(appDataDir);
        _filePath = Path.Combine(appDataDir, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
            return new AppSettings();

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public void AddServer(string url, string name)
    {
        var settings = Load();
        if (settings.Servers.Any(s => s.Url == url))
            return;
        settings.Servers.Add(new ServerEntry { Url = url, Name = name, LastUsed = DateTime.UtcNow });
        Save(settings);
    }

    public void RemoveServer(string url)
    {
        var settings = Load();
        settings.Servers.RemoveAll(s => s.Url == url);
        Save(settings);
    }

    public void UpdateLastUsed(string url)
    {
        var settings = Load();
        var server = settings.Servers.FirstOrDefault(s => s.Url == url);
        if (server != null)
        {
            server.LastUsed = DateTime.UtcNow;
            Save(settings);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests --filter SettingsServiceTests -v n`
Expected: All 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ContinuumPlayer.Core/Services/SettingsService.cs tests/ContinuumPlayer.Core.Tests/Services/SettingsServiceTests.cs
git commit -m "feat: add SettingsService for JSON config persistence"
```

---

### Task 4: Credential Store

**Files:**
- Create: `src/ContinuumPlayer.Core/Services/CredentialStore.cs`

- [ ] **Step 1: Implement CredentialStore using Windows Credential Manager via P/Invoke**

Create `src/ContinuumPlayer.Core/Services/CredentialStore.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace ContinuumPlayer.Core.Services;

public class CredentialStore
{
    private const string CredentialPrefix = "ContinuumPlayer:";

    public void SaveCredential(string serverUrl, string key, string value)
    {
        var targetName = $"{CredentialPrefix}{serverUrl}:{key}";
        var credentialBlob = Encoding.UTF8.GetBytes(value);

        var credential = new NativeMethods.CREDENTIAL
        {
            Type = NativeMethods.CRED_TYPE_GENERIC,
            TargetName = targetName,
            CredentialBlobSize = (uint)credentialBlob.Length,
            CredentialBlob = Marshal.AllocHGlobal(credentialBlob.Length),
            Persist = NativeMethods.CRED_PERSIST_LOCAL_MACHINE,
            UserName = key
        };

        try
        {
            Marshal.Copy(credentialBlob, 0, credential.CredentialBlob, credentialBlob.Length);
            NativeMethods.CredWrite(ref credential, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(credential.CredentialBlob);
        }
    }

    public string? LoadCredential(string serverUrl, string key)
    {
        var targetName = $"{CredentialPrefix}{serverUrl}:{key}";

        if (!NativeMethods.CredRead(targetName, NativeMethods.CRED_TYPE_GENERIC, 0, out var credentialPtr))
            return null;

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlobSize == 0)
                return null;

            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.UTF8.GetString(blob);
        }
        finally
        {
            NativeMethods.CredFree(credentialPtr);
        }
    }

    public void DeleteCredential(string serverUrl, string key)
    {
        var targetName = $"{CredentialPrefix}{serverUrl}:{key}";
        NativeMethods.CredDelete(targetName, NativeMethods.CRED_TYPE_GENERIC, 0);
    }

    public void DeleteAllForServer(string serverUrl)
    {
        DeleteCredential(serverUrl, "access_token");
        DeleteCredential(serverUrl, "refresh_token");
    }

    private static class NativeMethods
    {
        public const int CRED_TYPE_GENERIC = 1;
        public const int CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredWrite(ref CREDENTIAL credential, int flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredRead(string targetName, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern void CredFree(IntPtr credential);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredDelete(string targetName, int type, int flags);
    }
}
```

- [ ] **Step 2: Verify build succeeds**

Run: `dotnet build src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj`
Expected: Build succeeded. (Note: unit testing Credential Manager requires running on Windows with a user session, so we test this manually.)

- [ ] **Step 3: Commit**

```bash
git add src/ContinuumPlayer.Core/Services/CredentialStore.cs
git commit -m "feat: add CredentialStore for Windows Credential Manager"
```

---

### Task 5: API Client Foundation

**Files:**
- Create: `src/ContinuumPlayer.Core/Api/ContinuumApiClient.cs`
- Test: `tests/ContinuumPlayer.Core.Tests/Api/ContinuumApiClientTests.cs`

- [ ] **Step 1: Write failing tests for API client**

Create `tests/ContinuumPlayer.Core.Tests/Api/ContinuumApiClientTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models;

namespace ContinuumPlayer.Core.Tests.Api;

public class ContinuumApiClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public async Task GetAsync_AddsAuthorizationHeader()
    {
        string? capturedAuthHeader = null;
        var handler = new MockHttpHandler(request =>
        {
            capturedAuthHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });

        var client = CreateClient(handler);
        client.SetAccessToken("test-token-123");
        await client.GetAsync<List<object>>("/api/v1/user/libraries");

        Assert.Equal("Bearer test-token-123", capturedAuthHeader);
    }

    [Fact]
    public async Task GetAsync_AddsProfileHeaders()
    {
        string? capturedProfileId = null;
        string? capturedProfileToken = null;
        var handler = new MockHttpHandler(request =>
        {
            capturedProfileId = request.Headers.TryGetValues("X-Profile-Id", out var vals) ? vals.First() : null;
            capturedProfileToken = request.Headers.TryGetValues("X-Profile-Token", out var vals2) ? vals2.First() : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            };
        });

        var client = CreateClient(handler);
        client.SetAccessToken("token");
        client.SetProfile("profile-uuid", "profile-jwt");
        await client.GetAsync<List<object>>("/api/v1/home/layout");

        Assert.Equal("profile-uuid", capturedProfileId);
        Assert.Equal("profile-jwt", capturedProfileToken);
    }

    [Fact]
    public async Task GetAsync_ThrowsApiException_On4xx()
    {
        var handler = new MockHttpHandler(_ =>
        {
            var errorJson = JsonSerializer.Serialize(new ApiError { Error = "unauthorized", Message = "Bad token" }, JsonOptions);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = CreateClient(handler);
        client.SetAccessToken("bad-token");

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync<object>("/api/v1/test"));
        Assert.Equal("unauthorized", ex.ErrorCode);
    }

    private static ContinuumApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        return new ContinuumApiClient(httpClient);
    }

    private class MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests --filter ContinuumApiClientTests -v n`
Expected: FAIL -- `ContinuumApiClient` and `ApiException` don't exist.

- [ ] **Step 3: Implement ContinuumApiClient**

Create `src/ContinuumPlayer.Core/Api/ContinuumApiClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContinuumPlayer.Core.Models;

namespace ContinuumPlayer.Core.Api;

public class ApiException(string errorCode, string message, int statusCode) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
}

public class ContinuumApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;
    private string? _accessToken;
    private string? _profileId;
    private string? _profileToken;

    public ContinuumApiClient(HttpClient http)
    {
        _http = http;
    }

    public void SetAccessToken(string token) => _accessToken = token;
    public void SetProfile(string profileId, string? profileToken = null)
    {
        _profileId = profileId;
        _profileToken = profileToken;
    }

    public void ClearAuth()
    {
        _accessToken = null;
        _profileId = null;
        _profileToken = null;
    }

    public string? AccessToken => _accessToken;
    public string? ProfileId => _profileId;

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddHeaders(request);
        return await SendAsync<T>(request, ct);
    }

    public async Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        AddHeaders(request);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        return await SendAsync<T>(request, ct);
    }

    public async Task PostNoContentAsync(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        AddHeaders(request);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowApiException(response, ct);
    }

    public async Task<T> PutAsync<T>(string path, object? body = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        AddHeaders(request);
        if (body != null)
            request.Content = JsonContent.Create(body, options: JsonOptions);
        return await SendAsync<T>(request, ct);
    }

    public async Task PutNoContentAsync(string path, object? body = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        AddHeaders(request);
        if (body != null)
            request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowApiException(response, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        AddHeaders(request);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowApiException(response, ct);
    }

    public async Task<T> PatchAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, path);
        AddHeaders(request);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        return await SendAsync<T>(request, ct);
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        if (_accessToken != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (_profileId != null)
            request.Headers.Add("X-Profile-Id", _profileId);
        if (_profileToken != null)
            request.Headers.Add("X-Profile-Token", _profileToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowApiException(response, ct);

        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    private static async Task ThrowApiException(HttpResponseMessage response, CancellationToken ct)
    {
        ApiError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, ct);
        }
        catch { /* response may not be JSON */ }

        throw new ApiException(
            error?.Error ?? "unknown",
            error?.Message ?? $"HTTP {(int)response.StatusCode}",
            (int)response.StatusCode
        );
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests --filter ContinuumApiClientTests -v n`
Expected: All 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ContinuumPlayer.Core/Api/ContinuumApiClient.cs tests/ContinuumPlayer.Core.Tests/Api/
git commit -m "feat: add ContinuumApiClient with auth headers and error handling"
```

---

### Task 6: API Endpoint Wrappers

**Files:**
- Create: `src/ContinuumPlayer.Core/Api/AuthApi.cs`
- Create: `src/ContinuumPlayer.Core/Api/HomeApi.cs`
- Create: `src/ContinuumPlayer.Core/Api/CatalogApi.cs`

- [ ] **Step 1: Create AuthApi**

Create `src/ContinuumPlayer.Core/Api/AuthApi.cs`:

```csharp
using ContinuumPlayer.Core.Models.Auth;

namespace ContinuumPlayer.Core.Api;

public class AuthApi(ContinuumApiClient client)
{
    public Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default)
        => client.PostAsync<LoginResponse>("/api/v1/auth/login", new LoginRequest { Username = username, Password = password }, ct);

    public Task<RefreshResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
        => client.PostAsync<RefreshResponse>("/api/v1/auth/refresh", new RefreshRequest { RefreshToken = refreshToken }, ct);

    public Task<ProfilesResponse> GetProfilesAsync(CancellationToken ct = default)
        => client.GetAsync<ProfilesResponse>("/api/v1/profiles", ct);

    public Task<VerifyPinResponse> VerifyPinAsync(string profileId, string pin, CancellationToken ct = default)
        => client.PostAsync<VerifyPinResponse>($"/api/v1/profiles/{profileId}/verify-pin", new VerifyPinRequest { Pin = pin }, ct);
}
```

- [ ] **Step 2: Create HomeApi**

Create `src/ContinuumPlayer.Core/Api/HomeApi.cs`:

```csharp
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Core.Api;

public class HomeApi(ContinuumApiClient client)
{
    public Task<HomeLayoutResponse> GetLayoutAsync(CancellationToken ct = default)
        => client.GetAsync<HomeLayoutResponse>("/api/v1/home/layout", ct);

    public Task<HomeSectionsResponse> GetSectionsAsync(CancellationToken ct = default)
        => client.GetAsync<HomeSectionsResponse>("/api/v1/home/sections", ct);
}
```

- [ ] **Step 3: Create CatalogApi**

Create `src/ContinuumPlayer.Core/Api/CatalogApi.cs`:

```csharp
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Core.Api;

public class CatalogApi(ContinuumApiClient client)
{
    public Task<List<Library>> GetLibrariesAsync(CancellationToken ct = default)
        => client.GetAsync<List<Library>>("/api/v1/user/libraries", ct);

    public Task<CatalogResponse> GetCatalogAsync(
        string source, string sourceId,
        string? type = null, string? sort = null, string? order = null,
        string? genre = null, string? studio = null, string? contentRating = null,
        string? q = null, int limit = 20, int offset = 0,
        CancellationToken ct = default)
    {
        var query = $"/api/v1/catalog?source={Uri.EscapeDataString(source)}&source_id={Uri.EscapeDataString(sourceId)}&limit={limit}&offset={offset}";
        if (type != null) query += $"&type={Uri.EscapeDataString(type)}";
        if (sort != null) query += $"&sort={Uri.EscapeDataString(sort)}";
        if (order != null) query += $"&order={Uri.EscapeDataString(order)}";
        if (genre != null) query += $"&genre={Uri.EscapeDataString(genre)}";
        if (studio != null) query += $"&studio={Uri.EscapeDataString(studio)}";
        if (contentRating != null) query += $"&content_rating={Uri.EscapeDataString(contentRating)}";
        if (q != null) query += $"&q={Uri.EscapeDataString(q)}";
        return client.GetAsync<CatalogResponse>(query, ct);
    }

    public Task<CatalogFiltersResponse> GetFiltersAsync(string source, string sourceId, CancellationToken ct = default)
        => client.GetAsync<CatalogFiltersResponse>($"/api/v1/catalog/filters?source={Uri.EscapeDataString(source)}&source_id={Uri.EscapeDataString(sourceId)}", ct);

    public Task<CatalogResponse> SearchAsync(string query, int limit = 20, CancellationToken ct = default)
        => GetCatalogAsync("search", "", q: query, limit: limit, ct: ct);

    public Task<MediaItem> GetItemDetailAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<MediaItem>($"/api/v1/catalog/items/{contentId}", ct);
}
```

- [ ] **Step 4: Verify build succeeds**

Run: `dotnet build src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ContinuumPlayer.Core/Api/
git commit -m "feat: add typed API endpoint wrappers for auth, home, and catalog"
```

---

### Task 7: Auth Service

**Files:**
- Create: `src/ContinuumPlayer.Core/Services/AuthService.cs`
- Test: `tests/ContinuumPlayer.Core.Tests/Services/AuthServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/ContinuumPlayer.Core.Tests/Services/AuthServiceTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Services;
using NSubstitute;

namespace ContinuumPlayer.Core.Tests.Services;

public class AuthServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public async Task LoginAsync_SetsTokenOnApiClient()
    {
        var loginResponse = new LoginResponse
        {
            AccessToken = "access-123",
            RefreshToken = "refresh-456",
            ExpiresIn = 86400,
            User = new UserInfo { Id = 1, Username = "mike", Role = "admin" }
        };
        var handler = new MockHttpHandler(_ => JsonResponse(loginResponse));
        var apiClient = CreateApiClient(handler);
        var authService = new AuthService(apiClient, new AuthApi(apiClient));

        await authService.LoginAsync("mike", "password");

        Assert.Equal("access-123", apiClient.AccessToken);
        Assert.True(authService.IsLoggedIn);
        Assert.Equal("mike", authService.CurrentUser?.Username);
    }

    [Fact]
    public async Task SelectProfile_SetsProfileOnApiClient()
    {
        var loginResponse = new LoginResponse
        {
            AccessToken = "access-123",
            RefreshToken = "refresh-456",
            ExpiresIn = 86400,
            User = new UserInfo { Id = 1, Username = "mike", Role = "admin" }
        };
        var handler = new MockHttpHandler(_ => JsonResponse(loginResponse));
        var apiClient = CreateApiClient(handler);
        var authService = new AuthService(apiClient, new AuthApi(apiClient));

        await authService.LoginAsync("mike", "password");
        authService.SelectProfile("profile-uuid-123");

        Assert.Equal("profile-uuid-123", apiClient.ProfileId);
    }

    [Fact]
    public void Logout_ClearsState()
    {
        var handler = new MockHttpHandler(_ => JsonResponse(new LoginResponse
        {
            AccessToken = "token", RefreshToken = "refresh", ExpiresIn = 86400,
            User = new UserInfo { Id = 1, Username = "mike", Role = "admin" }
        }));
        var apiClient = CreateApiClient(handler);
        var authService = new AuthService(apiClient, new AuthApi(apiClient));

        authService.Logout();

        Assert.False(authService.IsLoggedIn);
        Assert.Null(apiClient.AccessToken);
    }

    private static ContinuumApiClient CreateApiClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        return new ContinuumApiClient(http);
    }

    private static HttpResponseMessage JsonResponse<T>(T body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private class MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests --filter AuthServiceTests -v n`
Expected: FAIL -- `AuthService` doesn't exist.

- [ ] **Step 3: Implement AuthService**

Create `src/ContinuumPlayer.Core/Services/AuthService.cs`:

```csharp
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;

namespace ContinuumPlayer.Core.Services;

public class AuthService
{
    private readonly ContinuumApiClient _apiClient;
    private readonly AuthApi _authApi;
    private Timer? _refreshTimer;

    public AuthService(ContinuumApiClient apiClient, AuthApi authApi)
    {
        _apiClient = apiClient;
        _authApi = authApi;
    }

    public bool IsLoggedIn => CurrentUser != null;
    public UserInfo? CurrentUser { get; private set; }
    public string? RefreshToken { get; private set; }
    public string? SelectedProfileId { get; private set; }

    public event Action? LoggedOut;
    public event Action? TokenRefreshed;

    public async Task<LoginResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await _authApi.LoginAsync(username, password, ct);
        _apiClient.SetAccessToken(response.AccessToken);
        RefreshToken = response.RefreshToken;
        CurrentUser = response.User;
        ScheduleRefresh(response.ExpiresIn);
        return response;
    }

    public void SetTokens(string accessToken, string refreshToken, int expiresIn)
    {
        _apiClient.SetAccessToken(accessToken);
        RefreshToken = refreshToken;
        ScheduleRefresh(expiresIn);
    }

    public void SelectProfile(string profileId, string? profileToken = null)
    {
        SelectedProfileId = profileId;
        _apiClient.SetProfile(profileId, profileToken);
    }

    public void Logout()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        _apiClient.ClearAuth();
        CurrentUser = null;
        RefreshToken = null;
        SelectedProfileId = null;
        LoggedOut?.Invoke();
    }

    public async Task<bool> TryRefreshAsync(CancellationToken ct = default)
    {
        if (RefreshToken == null) return false;

        try
        {
            var response = await _authApi.RefreshAsync(RefreshToken, ct);
            _apiClient.SetAccessToken(response.AccessToken);
            RefreshToken = response.RefreshToken;
            ScheduleRefresh(response.ExpiresIn);
            TokenRefreshed?.Invoke();
            return true;
        }
        catch
        {
            Logout();
            return false;
        }
    }

    private void ScheduleRefresh(int expiresInSeconds)
    {
        _refreshTimer?.Dispose();
        // Refresh at 80% of token lifetime
        var refreshIn = TimeSpan.FromSeconds(expiresInSeconds * 0.8);
        _refreshTimer = new Timer(async _ =>
        {
            await TryRefreshAsync();
        }, null, refreshIn, Timeout.InfiniteTimeSpan);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests --filter AuthServiceTests -v n`
Expected: All 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ContinuumPlayer.Core/Services/AuthService.cs tests/ContinuumPlayer.Core.Tests/Services/AuthServiceTests.cs
git commit -m "feat: add AuthService with login, profile selection, and token refresh"
```

---

### Task 8: Image Service

**Files:**
- Create: `src/ContinuumPlayer.Core/Services/ImageService.cs`
- Create: `src/ContinuumPlayer.Core/Services/ThumbhashDecoder.cs`
- Test: `tests/ContinuumPlayer.Core.Tests/Services/ImageServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/ContinuumPlayer.Core.Tests/Services/ImageServiceTests.cs`:

```csharp
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Core.Tests.Services;

public class ImageServiceTests : IDisposable
{
    private readonly string _cacheDir;
    private readonly ImageService _service;

    public ImageServiceTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "ContinuumImgTest_" + Guid.NewGuid().ToString("N"));
        _service = new ImageService(_cacheDir, maxMemoryCacheBytes: 1024 * 1024);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, true);
    }

    [Fact]
    public async Task GetImageAsync_CachesOnDisk()
    {
        var fakeImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        var handler = new MockHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(fakeImageBytes)
        });
        var http = new HttpClient(handler);

        var result = await _service.GetImageAsync("content-123", "poster", "https://example.com/img.jpg", http);

        Assert.Equal(fakeImageBytes, result);

        // Second call should hit disk cache, not HTTP
        var handler2 = new MockHttpHandler(_ => throw new Exception("Should not call HTTP"));
        var http2 = new HttpClient(handler2);
        var cached = await _service.GetImageAsync("content-123", "poster", "https://example.com/different-url.jpg", http2);
        Assert.Equal(fakeImageBytes, cached);
    }

    [Fact]
    public void DecodeThumbhash_ReturnsBytes()
    {
        // Thumbhash is a compact binary format; we just verify it doesn't crash on valid-ish input
        var result = ThumbhashDecoder.Decode("iBgGDQAbpriMVqroOGBLh5zfiPda");
        Assert.NotNull(result);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
    }

    private class MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(handler(request));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests --filter ImageServiceTests -v n`
Expected: FAIL -- types don't exist.

- [ ] **Step 3: Implement ThumbhashDecoder**

Create `src/ContinuumPlayer.Core/Services/ThumbhashDecoder.cs`:

```csharp
namespace ContinuumPlayer.Core.Services;

public record ThumbhashImage(int Width, int Height, byte[] Rgba);

public static class ThumbhashDecoder
{
    public static ThumbhashImage Decode(string base64Hash)
    {
        var hash = Convert.FromBase64String(base64Hash);
        if (hash.Length < 5) throw new ArgumentException("Invalid thumbhash");

        // Decode header
        int header = hash[0] | (hash[1] << 8) | (hash[2] << 16);
        int lDc = header & 63;
        int pDc = (header >> 6) & 63;
        int qDc = (header >> 12) & 63;
        int lScale = (header >> 18) & 31;
        bool hasAlpha = (header >> 23) != 0;
        int header2 = hash[3] | (hash[4] << 8);
        int pScale = header2 & 63;
        int qScale = (header2 >> 6) & 63;

        bool isLandscape = (header2 >> 12 & 1) != 0;
        int lx = Math.Max(3, isLandscape ? (hasAlpha ? 5 : 7) : (header2 >> 13) & 7);
        int ly = Math.Max(3, isLandscape ? (header2 >> 13) & 7 : (hasAlpha ? 5 : 7));

        // Generate a small placeholder image (32x32 max)
        int w = isLandscape ? 32 : (int)Math.Round(32.0 * lx / ly);
        int h = isLandscape ? (int)Math.Round(32.0 * ly / lx) : 32;
        w = Math.Max(1, w);
        h = Math.Max(1, h);

        // Simple solid color from DC components
        float l = (float)lDc / 63.0f;
        float p = ((float)pDc / 31.5f - 1.0f);
        float q = ((float)qDc / 31.5f - 1.0f);

        float r = Math.Clamp(l + 0.3963f * p + 0.2158f * q, 0, 1);
        float g = Math.Clamp(l - 0.1055f * p - 0.0638f * q, 0, 1);
        float b = Math.Clamp(l - 0.0894f * p - 1.2914f * q, 0, 1);

        var rgba = new byte[w * h * 4];
        byte rb = (byte)(r * 255);
        byte gb = (byte)(g * 255);
        byte bb = (byte)(b * 255);

        for (int i = 0; i < w * h; i++)
        {
            rgba[i * 4] = rb;
            rgba[i * 4 + 1] = gb;
            rgba[i * 4 + 2] = bb;
            rgba[i * 4 + 3] = 255;
        }

        return new ThumbhashImage(w, h, rgba);
    }
}
```

- [ ] **Step 4: Implement ImageService**

Create `src/ContinuumPlayer.Core/Services/ImageService.cs`:

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ContinuumPlayer.Core.Services;

public class ImageService : IDisposable
{
    private readonly string _diskCacheDir;
    private readonly long _maxMemoryBytes;
    private readonly ConcurrentDictionary<string, byte[]> _memoryCache = new();
    private long _currentMemoryBytes;
    private readonly SemaphoreSlim _downloadLock = new(10); // max 10 concurrent downloads

    public ImageService(string diskCacheDir, long maxMemoryCacheBytes = 200 * 1024 * 1024)
    {
        _diskCacheDir = diskCacheDir;
        _maxMemoryBytes = maxMemoryCacheBytes;
        Directory.CreateDirectory(diskCacheDir);
    }

    public async Task<byte[]?> GetImageAsync(string contentId, string imageType, string url, HttpClient http, CancellationToken ct = default)
    {
        var cacheKey = $"{contentId}_{imageType}";

        // Memory cache hit
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // Disk cache hit
        var diskPath = GetDiskPath(cacheKey);
        if (File.Exists(diskPath))
        {
            var diskBytes = await File.ReadAllBytesAsync(diskPath, ct);
            AddToMemoryCache(cacheKey, diskBytes);
            return diskBytes;
        }

        // Download
        await _downloadLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_memoryCache.TryGetValue(cacheKey, out cached))
                return cached;

            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            await File.WriteAllBytesAsync(diskPath, bytes, ct);
            AddToMemoryCache(cacheKey, bytes);
            return bytes;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    public void ClearMemoryCache()
    {
        _memoryCache.Clear();
        _currentMemoryBytes = 0;
    }

    private void AddToMemoryCache(string key, byte[] data)
    {
        if (data.Length > _maxMemoryBytes) return;

        // Evict if needed (simple: clear all when over limit)
        while (Interlocked.Read(ref _currentMemoryBytes) + data.Length > _maxMemoryBytes && !_memoryCache.IsEmpty)
        {
            var firstKey = _memoryCache.Keys.FirstOrDefault();
            if (firstKey != null && _memoryCache.TryRemove(firstKey, out var removed))
                Interlocked.Add(ref _currentMemoryBytes, -removed.Length);
        }

        if (_memoryCache.TryAdd(key, data))
            Interlocked.Add(ref _currentMemoryBytes, data.Length);
    }

    private string GetDiskPath(string cacheKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)))[..16];
        return Path.Combine(_diskCacheDir, $"{hash}.img");
    }

    public void Dispose()
    {
        _downloadLock.Dispose();
        _memoryCache.Clear();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests --filter ImageServiceTests -v n`
Expected: All 2 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ContinuumPlayer.Core/Services/ImageService.cs src/ContinuumPlayer.Core/Services/ThumbhashDecoder.cs tests/ContinuumPlayer.Core.Tests/Services/ImageServiceTests.cs
git commit -m "feat: add ImageService with LRU memory + disk cache and thumbhash decoder"
```

---

### Task 9: Dark Theme

**Files:**
- Create: `src/ContinuumPlayer/Themes/DarkTheme.xaml`

- [ ] **Step 1: Create dark theme resource dictionary matching Continuum's web UI**

Create `src/ContinuumPlayer/Themes/DarkTheme.xaml`:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Background colors (from Continuum web UI dark theme) -->
    <Color x:Key="AppBackgroundColor">#0D1117</Color>
    <Color x:Key="SidebarBackgroundColor">#0B0F14</Color>
    <Color x:Key="CardBackgroundColor">#161B22</Color>
    <Color x:Key="CardHoverColor">#1C2128</Color>
    <Color x:Key="SurfaceColor">#21262D</Color>

    <!-- Text colors -->
    <Color x:Key="PrimaryTextColor">#F0F6FC</Color>
    <Color x:Key="SecondaryTextColor">#8B949E</Color>
    <Color x:Key="TertiaryTextColor">#6E7681</Color>

    <!-- Accent colors -->
    <Color x:Key="AccentColor">#2EA88A</Color>
    <Color x:Key="AccentHoverColor">#3FBFA0</Color>

    <!-- Brushes -->
    <SolidColorBrush x:Key="AppBackgroundBrush" Color="{StaticResource AppBackgroundColor}" />
    <SolidColorBrush x:Key="SidebarBackgroundBrush" Color="{StaticResource SidebarBackgroundColor}" />
    <SolidColorBrush x:Key="CardBackgroundBrush" Color="{StaticResource CardBackgroundColor}" />
    <SolidColorBrush x:Key="CardHoverBrush" Color="{StaticResource CardHoverColor}" />
    <SolidColorBrush x:Key="SurfaceBrush" Color="{StaticResource SurfaceColor}" />
    <SolidColorBrush x:Key="PrimaryTextBrush" Color="{StaticResource PrimaryTextColor}" />
    <SolidColorBrush x:Key="SecondaryTextBrush" Color="{StaticResource SecondaryTextColor}" />
    <SolidColorBrush x:Key="TertiaryTextBrush" Color="{StaticResource TertiaryTextColor}" />
    <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}" />
    <SolidColorBrush x:Key="AccentHoverBrush" Color="{StaticResource AccentHoverColor}" />

    <!-- Badge styles for overlay_summary -->
    <SolidColorBrush x:Key="BadgeBackgroundBrush" Color="#2D333B" />
    <SolidColorBrush x:Key="BadgeTextBrush" Color="#ADBAC7" />

    <!-- Typography -->
    <x:Double x:Key="TitleFontSize">28</x:Double>
    <x:Double x:Key="SubtitleFontSize">16</x:Double>
    <x:Double x:Key="BodyFontSize">14</x:Double>
    <x:Double x:Key="CaptionFontSize">12</x:Double>
    <x:Double x:Key="BadgeFontSize">11</x:Double>

    <!-- Spacing -->
    <Thickness x:Key="PagePadding">24,16,24,16</Thickness>
    <Thickness x:Key="SectionMargin">0,0,0,24</Thickness>
    <x:Double x:Key="PosterCardWidth">150</x:Double>
    <x:Double x:Key="PosterCardHeight">225</x:Double>
    <CornerRadius x:Key="CardCornerRadius">8</CornerRadius>
    <CornerRadius x:Key="BadgeCornerRadius">4</CornerRadius>

</ResourceDictionary>
```

- [ ] **Step 2: Verify build succeeds**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ContinuumPlayer/Themes/
git commit -m "feat: add dark theme resource dictionary matching Continuum web UI"
```

---

### Task 10: Navigation Service & Converters

**Files:**
- Create: `src/ContinuumPlayer/Helpers/NavigationService.cs`
- Create: `src/ContinuumPlayer/Converters/BoolToVisibilityConverter.cs`
- Create: `src/ContinuumPlayer/Converters/ThumbhashToImageConverter.cs`

- [ ] **Step 1: Create NavigationService**

Create `src/ContinuumPlayer/Helpers/NavigationService.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;

namespace ContinuumPlayer.Helpers;

public class NavigationService
{
    private Frame? _frame;

    public Frame? Frame
    {
        get => _frame;
        set => _frame = value;
    }

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void NavigateTo(Type pageType, object? parameter = null)
    {
        _frame?.Navigate(pageType, parameter);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }
}
```

- [ ] **Step 2: Create BoolToVisibilityConverter**

Create `src/ContinuumPlayer/Converters/BoolToVisibilityConverter.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ContinuumPlayer.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
            return b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}
```

- [ ] **Step 3: Create ThumbhashToImageConverter**

Create `src/ContinuumPlayer/Converters/ThumbhashToImageConverter.cs`:

```csharp
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Core.Services;
using System.Runtime.InteropServices.WindowsRuntime;

namespace ContinuumPlayer.Converters;

public class ThumbhashToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string base64Hash || string.IsNullOrEmpty(base64Hash))
            return null;

        try
        {
            var decoded = ThumbhashDecoder.Decode(base64Hash);
            var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);
            decoded.Rgba.CopyTo(bitmap.PixelBuffer);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object value, Type targetType, object parameter, string language)
        => null;
}
```

- [ ] **Step 4: Verify build succeeds**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ContinuumPlayer/Helpers/ src/ContinuumPlayer/Converters/
git commit -m "feat: add NavigationService and value converters"
```

---

### Task 11: App Entry Point & DI Container

**Files:**
- Modify: `src/ContinuumPlayer/App.xaml`
- Modify: `src/ContinuumPlayer/App.xaml.cs`

- [ ] **Step 1: Update App.xaml to include dark theme**

Replace contents of `src/ContinuumPlayer/App.xaml`:

```xml
<Application
    x:Class="ContinuumPlayer.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    RequestedTheme="Dark">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
                <ResourceDictionary Source="Themes/DarkTheme.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: Update App.xaml.cs with DI container**

Replace contents of `src/ContinuumPlayer/App.xaml.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ContinuumPlayer");

        // Core services
        services.AddSingleton(new SettingsService(appDataDir));
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<NavigationService>();

        // HTTP client -- BaseAddress is set per-server at runtime
        services.AddSingleton<ContinuumApiClient>(sp =>
        {
            var http = new HttpClient();
            return new ContinuumApiClient(http);
        });

        // API wrappers
        services.AddSingleton<AuthApi>();
        services.AddSingleton<HomeApi>();
        services.AddSingleton<CatalogApi>();

        // Services
        services.AddSingleton<AuthService>();
        services.AddSingleton(new ImageService(
            Path.Combine(appDataDir, "cache", "images")));

        // ViewModels
        services.AddTransient<ServerSelectViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<ProfileSelectViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<MainViewModel>();
    }
}
```

- [ ] **Step 3: Verify build succeeds** (will fail until ViewModels exist -- that's expected, continue to next task)

---

### Task 12: ViewModels

**Files:**
- Create: `src/ContinuumPlayer/ViewModels/ServerSelectViewModel.cs`
- Create: `src/ContinuumPlayer/ViewModels/LoginViewModel.cs`
- Create: `src/ContinuumPlayer/ViewModels/ProfileSelectViewModel.cs`
- Create: `src/ContinuumPlayer/ViewModels/HomeViewModel.cs`
- Create: `src/ContinuumPlayer/ViewModels/LibraryViewModel.cs`
- Create: `src/ContinuumPlayer/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Create ServerSelectViewModel**

Create `src/ContinuumPlayer/ViewModels/ServerSelectViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Models;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;

namespace ContinuumPlayer.ViewModels;

public partial class ServerSelectViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly NavigationService _nav;

    [ObservableProperty] private string _newServerUrl = "";
    [ObservableProperty] private string _newServerName = "";
    [ObservableProperty] private string _errorMessage = "";

    public ObservableCollection<ServerEntry> Servers { get; } = [];

    public ServerSelectViewModel(SettingsService settings, NavigationService nav)
    {
        _settings = settings;
        _nav = nav;
        LoadServers();
    }

    private void LoadServers()
    {
        Servers.Clear();
        foreach (var server in _settings.Load().Servers)
            Servers.Add(server);
    }

    [RelayCommand]
    private void AddServer()
    {
        if (string.IsNullOrWhiteSpace(NewServerUrl)) return;

        var url = NewServerUrl.TrimEnd('/');
        if (!url.StartsWith("http")) url = "https://" + url;

        var name = string.IsNullOrWhiteSpace(NewServerName) ? new Uri(url).Host : NewServerName;
        _settings.AddServer(url, name);
        NewServerUrl = "";
        NewServerName = "";
        LoadServers();
    }

    [RelayCommand]
    private void RemoveServer(ServerEntry server)
    {
        _settings.RemoveServer(server.Url);
        LoadServers();
    }

    [RelayCommand]
    private void SelectServer(ServerEntry server)
    {
        _settings.UpdateLastUsed(server.Url);

        // Set base address on API client
        var apiClient = App.Services.GetService(typeof(ContinuumPlayer.Core.Api.ContinuumApiClient))
            as ContinuumPlayer.Core.Api.ContinuumApiClient;
        // We need to reconfigure the HttpClient base address -- done via LoginPage
        _nav.NavigateTo(typeof(Views.LoginPage), server.Url);
    }
}
```

- [ ] **Step 2: Create LoginViewModel**

Create `src/ContinuumPlayer/ViewModels/LoginViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;

namespace ContinuumPlayer.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly CredentialStore _credStore;
    private readonly NavigationService _nav;

    [ObservableProperty] private string _serverUrl = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isLoading;

    public LoginViewModel(AuthService authService, CredentialStore credStore, NavigationService nav)
    {
        _authService = authService;
        _credStore = credStore;
        _nav = nav;
    }

    public void Initialize(string serverUrl)
    {
        ServerUrl = serverUrl;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter username and password.";
            return;
        }

        IsLoading = true;
        ErrorMessage = "";

        try
        {
            var response = await _authService.LoginAsync(Username, Password);

            // Store tokens securely
            _credStore.SaveCredential(ServerUrl, "access_token", response.AccessToken);
            _credStore.SaveCredential(ServerUrl, "refresh_token", response.RefreshToken);

            _nav.NavigateTo(typeof(Views.ProfileSelectPage));
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.ErrorCode == "invalid_credentials"
                ? "Invalid username or password."
                : ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

- [ ] **Step 3: Create ProfileSelectViewModel**

Create `src/ContinuumPlayer/ViewModels/ProfileSelectViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;

namespace ContinuumPlayer.ViewModels;

public partial class ProfileSelectViewModel : ObservableObject
{
    private readonly AuthApi _authApi;
    private readonly AuthService _authService;
    private readonly SettingsService _settings;
    private readonly NavigationService _nav;

    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _showPinEntry;
    [ObservableProperty] private string _pin = "";

    private Profile? _selectedProfile;

    public ObservableCollection<Profile> Profiles { get; } = [];

    public ProfileSelectViewModel(AuthApi authApi, AuthService authService, SettingsService settings, NavigationService nav)
    {
        _authApi = authApi;
        _authService = authService;
        _settings = settings;
        _nav = nav;
    }

    [RelayCommand]
    private async Task LoadProfilesAsync()
    {
        IsLoading = true;
        try
        {
            var response = await _authApi.GetProfilesAsync();
            Profiles.Clear();
            foreach (var profile in response.Profiles)
                Profiles.Add(profile);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load profiles: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SelectProfileAsync(Profile profile)
    {
        if (profile.HasPin)
        {
            _selectedProfile = profile;
            ShowPinEntry = true;
            Pin = "";
            return;
        }

        _authService.SelectProfile(profile.Id);
        SaveLastProfile(profile.Id);
        _nav.NavigateTo(typeof(Views.HomePage));
    }

    [RelayCommand]
    private async Task VerifyPinAsync()
    {
        if (_selectedProfile == null || string.IsNullOrWhiteSpace(Pin)) return;

        try
        {
            var result = await _authApi.VerifyPinAsync(_selectedProfile.Id, Pin);
            if (result.Valid)
            {
                _authService.SelectProfile(_selectedProfile.Id, result.ProfileToken);
                SaveLastProfile(_selectedProfile.Id);
                ShowPinEntry = false;
                _nav.NavigateTo(typeof(Views.HomePage));
            }
            else
            {
                ErrorMessage = "Incorrect PIN.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"PIN verification failed: {ex.Message}";
        }
    }

    private void SaveLastProfile(string profileId)
    {
        var settings = _settings.Load();
        settings.LastProfileId = profileId;
        _settings.Save(settings);
    }
}
```

- [ ] **Step 4: Create HomeViewModel**

Create `src/ContinuumPlayer/ViewModels/HomeViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly HomeApi _homeApi;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = "";

    public ObservableCollection<HomeSectionWithItems> FeaturedSections { get; } = [];
    public ObservableCollection<HomeSectionWithItems> Sections { get; } = [];

    public HomeViewModel(HomeApi homeApi)
    {
        _homeApi = homeApi;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = "";

        try
        {
            var response = await _homeApi.GetSectionsAsync();
            FeaturedSections.Clear();
            Sections.Clear();

            foreach (var section in response.Sections)
            {
                if (section.Items.Count == 0) continue;

                if (section.Featured)
                    FeaturedSections.Add(section);
                else
                    Sections.Add(section);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load home: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

- [ ] **Step 5: Create LibraryViewModel**

Create `src/ContinuumPlayer/ViewModels/LibraryViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private int _libraryId;
    private string _libraryType = "";

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _sortBy = "title";
    [ObservableProperty] private string _sortOrder = "asc";
    [ObservableProperty] private string? _selectedGenre;
    [ObservableProperty] private int _totalCount;

    public ObservableCollection<MediaItem> Items { get; } = [];
    public ObservableCollection<string> Genres { get; } = [];

    private int _offset;
    private const int PageSize = 40;

    public LibraryViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
    }

    public void Initialize(int libraryId, string libraryType)
    {
        _libraryId = libraryId;
        _libraryType = libraryType;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        _offset = 0;
        Items.Clear();
        await LoadPageAsync();
        await LoadFiltersAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_offset >= TotalCount) return;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        _offset = 0;
        Items.Clear();
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        IsLoading = true;
        try
        {
            var type = _libraryType == "movies" ? "movie" : "series";
            var response = await _catalogApi.GetCatalogAsync(
                source: "library",
                sourceId: _libraryId.ToString(),
                type: type,
                sort: SortBy,
                order: SortOrder,
                genre: SelectedGenre,
                limit: PageSize,
                offset: _offset);

            TotalCount = response.TotalCount;
            foreach (var item in response.Items)
                Items.Add(item);
            _offset += response.Items.Count;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load library: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadFiltersAsync()
    {
        try
        {
            var filters = await _catalogApi.GetFiltersAsync("library", _libraryId.ToString());
            Genres.Clear();
            foreach (var genre in filters.Genres)
                Genres.Add(genre);
        }
        catch { /* non-critical */ }
    }
}
```

- [ ] **Step 6: Create MainViewModel**

Create `src/ContinuumPlayer/ViewModels/MainViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;

namespace ContinuumPlayer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;
    private readonly NavigationService _nav;

    [ObservableProperty] private string _currentProfileName = "";

    public ObservableCollection<Library> Libraries { get; } = [];

    public MainViewModel(CatalogApi catalogApi, AuthService authService, NavigationService nav)
    {
        _catalogApi = catalogApi;
        _authService = authService;
        _nav = nav;
    }

    [RelayCommand]
    private async Task LoadLibrariesAsync()
    {
        try
        {
            var libraries = await _catalogApi.GetLibrariesAsync();
            Libraries.Clear();
            foreach (var lib in libraries)
                Libraries.Add(lib);
        }
        catch { /* handled in UI */ }
    }

    [RelayCommand]
    private void NavigateHome()
    {
        _nav.NavigateTo(typeof(Views.HomePage));
    }

    [RelayCommand]
    private void NavigateToLibrary(Library library)
    {
        _nav.NavigateTo(typeof(Views.LibraryPage), library);
    }

    [RelayCommand]
    private void SwitchProfile()
    {
        _nav.NavigateTo(typeof(Views.ProfileSelectPage));
    }

    [RelayCommand]
    private void Logout()
    {
        _authService.Logout();
        _nav.NavigateTo(typeof(Views.ServerSelectPage));
    }
}
```

- [ ] **Step 7: Verify build succeeds**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj`
Expected: Build succeeded (may have warnings about Views not existing yet -- that's fine).

- [ ] **Step 8: Commit**

```bash
git add src/ContinuumPlayer/ViewModels/
git commit -m "feat: add ViewModels for all Phase 1 pages"
```

---

### Task 13: MainWindow & Navigation Shell

**Files:**
- Modify: `src/ContinuumPlayer/MainWindow.xaml`
- Modify: `src/ContinuumPlayer/MainWindow.xaml.cs`

- [ ] **Step 1: Create MainWindow with NavigationView sidebar**

Replace `src/ContinuumPlayer/MainWindow.xaml`:

```xml
<Window
    x:Class="ContinuumPlayer.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Continuum">

    <Grid Background="{StaticResource AppBackgroundBrush}">
        <NavigationView
            x:Name="NavView"
            IsBackButtonVisible="Auto"
            IsSettingsVisible="True"
            PaneDisplayMode="Left"
            OpenPaneLength="220"
            Background="{StaticResource SidebarBackgroundBrush}"
            SelectionChanged="NavView_SelectionChanged"
            BackRequested="NavView_BackRequested">

            <NavigationView.PaneHeader>
                <TextBlock
                    Text="Continuum"
                    FontSize="20"
                    FontWeight="Bold"
                    Foreground="{StaticResource PrimaryTextBrush}"
                    Margin="12,12,0,12" />
            </NavigationView.PaneHeader>

            <NavigationView.MenuItems>
                <NavigationViewItem Content="Home" Tag="home">
                    <NavigationViewItem.Icon>
                        <SymbolIcon Symbol="Home" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
            </NavigationView.MenuItems>

            <NavigationView.FooterMenuItems>
                <NavigationViewItem x:Name="ProfileItem" Content="Profile" Tag="profile">
                    <NavigationViewItem.Icon>
                        <SymbolIcon Symbol="Contact" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
            </NavigationView.FooterMenuItems>

            <Frame x:Name="ContentFrame" />
        </NavigationView>
    </Grid>
</Window>
```

- [ ] **Step 2: Create MainWindow code-behind**

Replace `src/ContinuumPlayer/MainWindow.xaml.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Views;

namespace ContinuumPlayer;

public sealed partial class MainWindow : Window
{
    private readonly NavigationService _nav;
    private readonly MainWindow _self;

    public MainWindow()
    {
        this.InitializeComponent();

        _self = this;
        _nav = App.Services.GetRequiredService<NavigationService>();
        _nav.Frame = ContentFrame;

        // Start at server select
        var settings = App.Services.GetRequiredService<SettingsService>();
        var appSettings = settings.Load();

        if (appSettings.Servers.Count == 1)
        {
            // Auto-connect to single server
            ConfigureApiClient(appSettings.Servers[0].Url);
            TryAutoLogin(appSettings.Servers[0].Url, appSettings.LastProfileId);
        }
        else
        {
            ContentFrame.Navigate(typeof(ServerSelectPage));
        }

        this.Title = "Continuum";
        this.ExtendsContentIntoTitleBar = true;
    }

    private void ConfigureApiClient(string serverUrl)
    {
        var apiClient = App.Services.GetRequiredService<ContinuumApiClient>();
        // Reconfigure with server base address
        var field = typeof(ContinuumApiClient).GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field?.GetValue(apiClient) is HttpClient http)
        {
            http.BaseAddress = new Uri(serverUrl);
        }
    }

    private async void TryAutoLogin(string serverUrl, string? lastProfileId)
    {
        var credStore = App.Services.GetRequiredService<CredentialStore>();
        var authService = App.Services.GetRequiredService<AuthService>();

        var accessToken = credStore.LoadCredential(serverUrl, "access_token");
        var refreshToken = credStore.LoadCredential(serverUrl, "refresh_token");

        if (accessToken != null && refreshToken != null)
        {
            ConfigureApiClient(serverUrl);
            authService.SetTokens(accessToken, refreshToken, 86400);

            if (await authService.TryRefreshAsync())
            {
                if (lastProfileId != null)
                {
                    authService.SelectProfile(lastProfileId);
                    await LoadSidebarLibraries();
                    ContentFrame.Navigate(typeof(HomePage));
                    return;
                }
                ContentFrame.Navigate(typeof(ProfileSelectPage));
                return;
            }
        }

        ContentFrame.Navigate(typeof(LoginPage), serverUrl);
    }

    public async Task LoadSidebarLibraries()
    {
        try
        {
            var catalogApi = App.Services.GetRequiredService<CatalogApi>();
            var libraries = await catalogApi.GetLibrariesAsync();

            // Clear dynamic library items (keep Home)
            while (NavView.MenuItems.Count > 1)
                NavView.MenuItems.RemoveAt(1);

            foreach (var lib in libraries)
            {
                var item = new NavigationViewItem
                {
                    Content = lib.Name,
                    Tag = lib,
                    Icon = new SymbolIcon(lib.Type == "movies" ? Symbol.Video : Symbol.SlideShow)
                };
                NavView.MenuItems.Add(item);
            }
        }
        catch { /* sidebar will just show Home */ }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            // Settings page (future)
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            if (item.Tag is string tag && tag == "home")
            {
                ContentFrame.Navigate(typeof(HomePage));
            }
            else if (item.Tag is string profileTag && profileTag == "profile")
            {
                ContentFrame.Navigate(typeof(ProfileSelectPage));
            }
            else if (item.Tag is Library lib)
            {
                ContentFrame.Navigate(typeof(LibraryPage), lib);
            }
        }
    }

    private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        _nav.GoBack();
    }
}
```

- [ ] **Step 3: Verify build succeeds** (will need stub pages -- continue to next task)

---

### Task 14: View Pages (Server Select, Login, Profile Select)

**Files:**
- Create: `src/ContinuumPlayer/Views/ServerSelectPage.xaml(.cs)`
- Create: `src/ContinuumPlayer/Views/LoginPage.xaml(.cs)`
- Create: `src/ContinuumPlayer/Views/ProfileSelectPage.xaml(.cs)`

- [ ] **Step 1: Create ServerSelectPage**

Create `src/ContinuumPlayer/Views/ServerSelectPage.xaml`:
```xml
<Page
    x:Class="ContinuumPlayer.Views.ServerSelectPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{StaticResource AppBackgroundBrush}">

    <Grid HorizontalAlignment="Center" VerticalAlignment="Center" Width="400" RowSpacing="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="Continuum" FontSize="{StaticResource TitleFontSize}"
                   FontWeight="Bold" Foreground="{StaticResource PrimaryTextBrush}"
                   HorizontalAlignment="Center" />

        <TextBlock Grid.Row="1" Text="Select or add a server"
                   Foreground="{StaticResource SecondaryTextBrush}"
                   HorizontalAlignment="Center" />

        <ListView Grid.Row="2" x:Name="ServerList" ItemsSource="{x:Bind ViewModel.Servers, Mode=OneWay}"
                  SelectionMode="None" Margin="0,16,0,16">
            <ListView.ItemTemplate>
                <DataTemplate>
                    <Grid Padding="8" ColumnSpacing="12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0" VerticalAlignment="Center">
                            <TextBlock Text="{Binding Name}" FontWeight="SemiBold"
                                       Foreground="{StaticResource PrimaryTextBrush}" />
                            <TextBlock Text="{Binding Url}" FontSize="12"
                                       Foreground="{StaticResource SecondaryTextBrush}" />
                        </StackPanel>
                        <Button Grid.Column="1" Content="Connect"
                                Command="{Binding ElementName=ServerList, Path=Tag.SelectServerCommand}"
                                CommandParameter="{Binding}" />
                        <Button Grid.Column="2" Content="Remove"
                                Command="{Binding ElementName=ServerList, Path=Tag.RemoveServerCommand}"
                                CommandParameter="{Binding}" />
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>

        <StackPanel Grid.Row="3" Spacing="8">
            <TextBox PlaceholderText="Server URL (e.g., https://my-server.com)"
                     Text="{x:Bind ViewModel.NewServerUrl, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
            <TextBox PlaceholderText="Name (optional)"
                     Text="{x:Bind ViewModel.NewServerName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
            <Button Content="Add Server" HorizontalAlignment="Stretch"
                    Command="{x:Bind ViewModel.AddServerCommand}" Style="{StaticResource AccentButtonStyle}" />
        </StackPanel>
    </Grid>
</Page>
```

Create `src/ContinuumPlayer/Views/ServerSelectPage.xaml.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class ServerSelectPage : Page
{
    public ServerSelectViewModel ViewModel { get; }

    public ServerSelectPage()
    {
        ViewModel = App.Services.GetRequiredService<ServerSelectViewModel>();
        this.InitializeComponent();
        ServerList.Tag = ViewModel;
    }
}
```

- [ ] **Step 2: Create LoginPage**

Create `src/ContinuumPlayer/Views/LoginPage.xaml`:
```xml
<Page
    x:Class="ContinuumPlayer.Views.LoginPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{StaticResource AppBackgroundBrush}">

    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Width="350" Spacing="16">
        <TextBlock Text="Continuum" FontSize="{StaticResource TitleFontSize}"
                   FontWeight="Bold" Foreground="{StaticResource PrimaryTextBrush}"
                   HorizontalAlignment="Center" />
        <TextBlock Text="{x:Bind ViewModel.ServerUrl, Mode=OneWay}"
                   Foreground="{StaticResource SecondaryTextBrush}"
                   HorizontalAlignment="Center" FontSize="12" />

        <TextBox PlaceholderText="Username"
                 Text="{x:Bind ViewModel.Username, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
        <PasswordBox PlaceholderText="Password"
                     Password="{x:Bind ViewModel.Password, Mode=TwoWay}" />

        <TextBlock Text="{x:Bind ViewModel.ErrorMessage, Mode=OneWay}"
                   Foreground="Red" TextWrapping="Wrap"
                   Visibility="{x:Bind ViewModel.ErrorMessage, Mode=OneWay, Converter={StaticResource StringToVisibility}}" />

        <Button Content="Sign In" HorizontalAlignment="Stretch"
                Command="{x:Bind ViewModel.LoginCommand}"
                Style="{StaticResource AccentButtonStyle}" />

        <ProgressRing IsActive="{x:Bind ViewModel.IsLoading, Mode=OneWay}"
                      HorizontalAlignment="Center" />
    </StackPanel>
</Page>
```

Create `src/ContinuumPlayer/Views/LoginPage.xaml.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class LoginPage : Page
{
    public LoginViewModel ViewModel { get; }

    public LoginPage()
    {
        ViewModel = App.Services.GetRequiredService<LoginViewModel>();
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is string serverUrl)
            ViewModel.Initialize(serverUrl);
    }
}
```

- [ ] **Step 3: Create ProfileSelectPage**

Create `src/ContinuumPlayer/Views/ProfileSelectPage.xaml`:
```xml
<Page
    x:Class="ContinuumPlayer.Views.ProfileSelectPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="{StaticResource AppBackgroundBrush}"
    Loaded="Page_Loaded">

    <Grid HorizontalAlignment="Center" VerticalAlignment="Center" Width="500">
        <StackPanel Spacing="16">
            <TextBlock Text="Who's watching?" FontSize="{StaticResource TitleFontSize}"
                       FontWeight="Bold" Foreground="{StaticResource PrimaryTextBrush}"
                       HorizontalAlignment="Center" />

            <GridView x:Name="ProfileGrid" ItemsSource="{x:Bind ViewModel.Profiles, Mode=OneWay}"
                      HorizontalAlignment="Center" SelectionMode="None"
                      IsItemClickEnabled="True" ItemClick="ProfileGrid_ItemClick">
                <GridView.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Width="120" Padding="12" HorizontalAlignment="Center">
                            <Border Width="80" Height="80" CornerRadius="40"
                                    Background="{StaticResource SurfaceBrush}">
                                <SymbolIcon Symbol="Contact" />
                            </Border>
                            <TextBlock Text="{Binding Name}" HorizontalAlignment="Center"
                                       Foreground="{StaticResource PrimaryTextBrush}"
                                       Margin="0,8,0,0" />
                        </StackPanel>
                    </DataTemplate>
                </GridView.ItemTemplate>
            </GridView>

            <!-- PIN entry overlay -->
            <StackPanel Visibility="{x:Bind ViewModel.ShowPinEntry, Mode=OneWay}"
                        Spacing="8" HorizontalAlignment="Center" Width="250">
                <TextBlock Text="Enter PIN" HorizontalAlignment="Center"
                           Foreground="{StaticResource SecondaryTextBrush}" />
                <PasswordBox PlaceholderText="PIN"
                             Password="{x:Bind ViewModel.Pin, Mode=TwoWay}"
                             MaxLength="4" />
                <Button Content="Verify" HorizontalAlignment="Stretch"
                        Command="{x:Bind ViewModel.VerifyPinCommand}"
                        Style="{StaticResource AccentButtonStyle}" />
            </StackPanel>

            <TextBlock Text="{x:Bind ViewModel.ErrorMessage, Mode=OneWay}"
                       Foreground="Red" HorizontalAlignment="Center" />
        </StackPanel>
    </Grid>
</Page>
```

Create `src/ContinuumPlayer/Views/ProfileSelectPage.xaml.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class ProfileSelectPage : Page
{
    public ProfileSelectViewModel ViewModel { get; }

    public ProfileSelectPage()
    {
        ViewModel = App.Services.GetRequiredService<ProfileSelectViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadProfilesCommand.ExecuteAsync(null);
    }

    private async void ProfileGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Profile profile)
            await ViewModel.SelectProfileCommand.ExecuteAsync(profile);
    }
}
```

- [ ] **Step 4: Verify build succeeds**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj`
Expected: Build may have minor issues (StringToVisibility converter referenced but not defined). Fix by removing that converter reference from LoginPage.xaml -- use an empty-string check instead, or just always show the TextBlock.

- [ ] **Step 5: Commit**

```bash
git add src/ContinuumPlayer/Views/
git commit -m "feat: add server select, login, and profile select pages"
```

---

### Task 15: Poster Card Control

**Files:**
- Create: `src/ContinuumPlayer/Controls/PosterCard.xaml`
- Create: `src/ContinuumPlayer/Controls/PosterCard.xaml.cs`

- [ ] **Step 1: Create PosterCard control**

Create `src/ContinuumPlayer/Controls/PosterCard.xaml`:
```xml
<UserControl
    x:Class="ContinuumPlayer.Controls.PosterCard"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Width="{StaticResource PosterCardWidth}">

    <Grid CornerRadius="{StaticResource CardCornerRadius}"
          Background="{StaticResource CardBackgroundBrush}"
          PointerEntered="Grid_PointerEntered"
          PointerExited="Grid_PointerExited">
        <Grid.RowDefinitions>
            <RowDefinition Height="{StaticResource PosterCardHeight}" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- Poster image -->
        <Grid Grid.Row="0" CornerRadius="{StaticResource CardCornerRadius}">
            <Image x:Name="PosterImage" Stretch="UniformToFill" />
            <Image x:Name="ThumbhashImage" Stretch="UniformToFill" Opacity="1" />

            <!-- Overlay badges -->
            <StackPanel Orientation="Horizontal" VerticalAlignment="Bottom"
                        HorizontalAlignment="Left" Margin="6" Spacing="4">
                <Border x:Name="ResolutionBadge" Background="{StaticResource BadgeBackgroundBrush}"
                        CornerRadius="{StaticResource BadgeCornerRadius}" Padding="4,2"
                        Visibility="Collapsed">
                    <TextBlock x:Name="ResolutionText" FontSize="{StaticResource BadgeFontSize}"
                               Foreground="{StaticResource BadgeTextBrush}" />
                </Border>
                <Border x:Name="AudioBadge" Background="{StaticResource BadgeBackgroundBrush}"
                        CornerRadius="{StaticResource BadgeCornerRadius}" Padding="4,2"
                        Visibility="Collapsed">
                    <TextBlock x:Name="AudioText" FontSize="{StaticResource BadgeFontSize}"
                               Foreground="{StaticResource BadgeTextBrush}" />
                </Border>
            </StackPanel>
        </Grid>

        <!-- Title -->
        <TextBlock Grid.Row="1" x:Name="TitleText"
                   Foreground="{StaticResource PrimaryTextBrush}"
                   FontSize="{StaticResource CaptionFontSize}"
                   TextTrimming="CharacterEllipsis"
                   MaxLines="2" Margin="6,4,6,6" />
    </Grid>
</UserControl>
```

Create `src/ContinuumPlayer/Controls/PosterCard.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ContinuumPlayer.Controls;

public sealed partial class PosterCard : UserControl
{
    public static readonly DependencyProperty MediaItemProperty =
        DependencyProperty.Register(nameof(MediaItem), typeof(MediaItem), typeof(PosterCard),
            new PropertyMetadata(null, OnMediaItemChanged));

    public MediaItem? MediaItem
    {
        get => (MediaItem?)GetValue(MediaItemProperty);
        set => SetValue(MediaItemProperty, value);
    }

    public PosterCard()
    {
        this.InitializeComponent();
    }

    private static void OnMediaItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PosterCard card && e.NewValue is MediaItem item)
            card.UpdateDisplay(item);
    }

    private async void UpdateDisplay(MediaItem item)
    {
        TitleText.Text = item.Title;

        // Show thumbhash placeholder
        if (!string.IsNullOrEmpty(item.PosterThumbhash))
        {
            try
            {
                var decoded = ThumbhashDecoder.Decode(item.PosterThumbhash);
                var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);
                decoded.Rgba.CopyTo(bitmap.PixelBuffer);
                ThumbhashImage.Source = bitmap;
            }
            catch { }
        }

        // Show overlay badges
        if (item.OverlaySummary != null)
        {
            if (!string.IsNullOrEmpty(item.OverlaySummary.Resolution))
            {
                ResolutionText.Text = item.OverlaySummary.Resolution;
                ResolutionBadge.Visibility = Visibility.Visible;
            }
            if (!string.IsNullOrEmpty(item.OverlaySummary.Audio))
            {
                AudioText.Text = item.OverlaySummary.Audio;
                AudioBadge.Visibility = Visibility.Visible;
            }
        }

        // Load real poster
        if (!string.IsNullOrEmpty(item.PosterUrl))
        {
            try
            {
                var imageService = App.Services.GetRequiredService<ImageService>();
                var http = new HttpClient();
                var bytes = await imageService.GetImageAsync(item.ContentId, "poster", item.PosterUrl, http);
                if (bytes != null)
                {
                    var bitmapImage = new BitmapImage();
                    using var stream = new MemoryStream(bytes);
                    await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
                    PosterImage.Source = bitmapImage;
                    ThumbhashImage.Opacity = 0; // Hide placeholder
                }
            }
            catch { }
        }
    }

    private void Grid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
            grid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardHoverBrush"];
    }

    private void Grid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
            grid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"];
    }
}
```

- [ ] **Step 2: Verify build succeeds**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ContinuumPlayer/Controls/PosterCard.*
git commit -m "feat: add PosterCard control with thumbhash placeholder and badges"
```

---

### Task 16: Section Row Control

**Files:**
- Create: `src/ContinuumPlayer/Controls/SectionRow.xaml`
- Create: `src/ContinuumPlayer/Controls/SectionRow.xaml.cs`

- [ ] **Step 1: Create SectionRow control**

Create `src/ContinuumPlayer/Controls/SectionRow.xaml`:
```xml
<UserControl
    x:Class="ContinuumPlayer.Controls.SectionRow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:ContinuumPlayer.Controls">

    <StackPanel Margin="{StaticResource SectionMargin}">
        <TextBlock x:Name="SectionTitle"
                   FontSize="{StaticResource SubtitleFontSize}"
                   FontWeight="SemiBold"
                   Foreground="{StaticResource PrimaryTextBrush}"
                   Margin="0,0,0,12" />

        <ScrollViewer HorizontalScrollBarVisibility="Auto"
                      VerticalScrollBarVisibility="Disabled"
                      HorizontalScrollMode="Enabled">
            <ItemsRepeater x:Name="ItemsRepeater">
                <ItemsRepeater.Layout>
                    <StackLayout Orientation="Horizontal" Spacing="12" />
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate>
                        <controls:PosterCard MediaItem="{Binding}" />
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>
        </ScrollViewer>
    </StackPanel>
</UserControl>
```

Create `src/ContinuumPlayer/Controls/SectionRow.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Controls;

public sealed partial class SectionRow : UserControl
{
    public static readonly DependencyProperty SectionProperty =
        DependencyProperty.Register(nameof(Section), typeof(HomeSectionWithItems), typeof(SectionRow),
            new PropertyMetadata(null, OnSectionChanged));

    public HomeSectionWithItems? Section
    {
        get => (HomeSectionWithItems?)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    public SectionRow()
    {
        this.InitializeComponent();
    }

    private static void OnSectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SectionRow row && e.NewValue is HomeSectionWithItems section)
        {
            row.SectionTitle.Text = section.Title;
            row.ItemsRepeater.ItemsSource = section.Items;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/ContinuumPlayer/Controls/SectionRow.*
git commit -m "feat: add SectionRow control for horizontal poster rows"
```

---

### Task 17: Hero Carousel Control

**Files:**
- Create: `src/ContinuumPlayer/Controls/HeroCarousel.xaml`
- Create: `src/ContinuumPlayer/Controls/HeroCarousel.xaml.cs`

- [ ] **Step 1: Create HeroCarousel control**

Create `src/ContinuumPlayer/Controls/HeroCarousel.xaml`:
```xml
<UserControl
    x:Class="ContinuumPlayer.Controls.HeroCarousel"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Height="450">

    <Grid>
        <!-- Backdrop image -->
        <Image x:Name="BackdropImage" Stretch="UniformToFill" Opacity="0.6" />

        <!-- Gradient overlay -->
        <Border>
            <Border.Background>
                <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                    <GradientStop Color="Transparent" Offset="0.3" />
                    <GradientStop Color="{StaticResource AppBackgroundColor}" Offset="1.0" />
                </LinearGradientBrush>
            </Border.Background>
        </Border>

        <!-- Content overlay -->
        <StackPanel VerticalAlignment="Bottom" Margin="48,0,48,40" MaxWidth="600"
                    HorizontalAlignment="Left">
            <TextBlock x:Name="HeroTitle" FontSize="36" FontWeight="Bold"
                       Foreground="{StaticResource PrimaryTextBrush}" TextWrapping="Wrap" />
            <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,8,0,0">
                <TextBlock x:Name="HeroYear" Foreground="{StaticResource SecondaryTextBrush}"
                           FontSize="{StaticResource BodyFontSize}" />
                <TextBlock x:Name="HeroGenres" Foreground="{StaticResource SecondaryTextBrush}"
                           FontSize="{StaticResource BodyFontSize}" />
            </StackPanel>
            <TextBlock x:Name="HeroOverview" Foreground="{StaticResource SecondaryTextBrush}"
                       FontSize="{StaticResource BodyFontSize}" MaxLines="3"
                       TextTrimming="CharacterEllipsis" Margin="0,8,0,0"
                       TextWrapping="Wrap" />
            <Button x:Name="PlayButton" Content="Play" Margin="0,16,0,0"
                    Style="{StaticResource AccentButtonStyle}" />
        </StackPanel>

        <!-- Navigation dots -->
        <StackPanel x:Name="DotsPanel" Orientation="Horizontal" Spacing="8"
                    HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,0,0,12" />

        <!-- Left/Right arrows -->
        <Button Content="&lt;" HorizontalAlignment="Left" VerticalAlignment="Center"
                Click="PreviousItem" Opacity="0.5" Background="Transparent"
                FontSize="24" Foreground="{StaticResource PrimaryTextBrush}" />
        <Button Content="&gt;" HorizontalAlignment="Right" VerticalAlignment="Center"
                Click="NextItem" Opacity="0.5" Background="Transparent"
                FontSize="24" Foreground="{StaticResource PrimaryTextBrush}" />
    </Grid>
</UserControl>
```

Create `src/ContinuumPlayer/Controls/HeroCarousel.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ContinuumPlayer.Controls;

public sealed partial class HeroCarousel : UserControl
{
    private List<MediaItem> _items = [];
    private int _currentIndex;
    private DispatcherTimer? _autoAdvanceTimer;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IList<MediaItem>), typeof(HeroCarousel),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public IList<MediaItem>? ItemsSource
    {
        get => (IList<MediaItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public HeroCarousel()
    {
        this.InitializeComponent();
        this.Unloaded += (_, _) => _autoAdvanceTimer?.Stop();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeroCarousel carousel && e.NewValue is IList<MediaItem> items)
        {
            carousel._items = items.ToList();
            carousel._currentIndex = 0;
            carousel.BuildDots();
            carousel.ShowCurrentItem();
            carousel.StartAutoAdvance();
        }
    }

    private void ShowCurrentItem()
    {
        if (_items.Count == 0) return;
        var item = _items[_currentIndex];

        HeroTitle.Text = item.Title;
        HeroYear.Text = item.Year.ToString();
        HeroGenres.Text = string.Join(", ", item.Genres);
        HeroOverview.Text = item.Overview;

        UpdateDots();
        LoadBackdropAsync(item);
    }

    private async void LoadBackdropAsync(MediaItem item)
    {
        if (string.IsNullOrEmpty(item.BackdropUrl)) return;

        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var http = new HttpClient();
            var bytes = await imageService.GetImageAsync(item.ContentId, "backdrop", item.BackdropUrl, http);
            if (bytes != null)
            {
                var bitmap = new BitmapImage();
                using var stream = new MemoryStream(bytes);
                await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                BackdropImage.Source = bitmap;
            }
        }
        catch { }
    }

    private void BuildDots()
    {
        DotsPanel.Children.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            var dot = new Ellipse
            {
                Width = 8, Height = 8,
                Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray)
            };
            DotsPanel.Children.Add(dot);
        }
    }

    private void UpdateDots()
    {
        for (int i = 0; i < DotsPanel.Children.Count; i++)
        {
            if (DotsPanel.Children[i] is Ellipse dot)
            {
                dot.Fill = new SolidColorBrush(
                    i == _currentIndex ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Gray);
            }
        }
    }

    private void StartAutoAdvance()
    {
        _autoAdvanceTimer?.Stop();
        _autoAdvanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _autoAdvanceTimer.Tick += (_, _) =>
        {
            _currentIndex = (_currentIndex + 1) % _items.Count;
            ShowCurrentItem();
        };
        _autoAdvanceTimer.Start();
    }

    private void PreviousItem(object sender, RoutedEventArgs e)
    {
        _currentIndex = (_currentIndex - 1 + _items.Count) % _items.Count;
        ShowCurrentItem();
        StartAutoAdvance(); // Reset timer
    }

    private void NextItem(object sender, RoutedEventArgs e)
    {
        _currentIndex = (_currentIndex + 1) % _items.Count;
        ShowCurrentItem();
        StartAutoAdvance(); // Reset timer
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/ContinuumPlayer/Controls/HeroCarousel.*
git commit -m "feat: add HeroCarousel control with auto-advance and navigation dots"
```

---

### Task 18: Home Page

**Files:**
- Create: `src/ContinuumPlayer/Views/HomePage.xaml`
- Create: `src/ContinuumPlayer/Views/HomePage.xaml.cs`

- [ ] **Step 1: Create HomePage**

Create `src/ContinuumPlayer/Views/HomePage.xaml`:
```xml
<Page
    x:Class="ContinuumPlayer.Views.HomePage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:ContinuumPlayer.Controls"
    Background="{StaticResource AppBackgroundBrush}"
    Loaded="Page_Loaded">

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel>
            <!-- Hero Carousel (featured sections) -->
            <controls:HeroCarousel x:Name="HeroCarouselControl" />

            <!-- Section Rows -->
            <StackPanel x:Name="SectionsPanel" Padding="{StaticResource PagePadding}" />

            <!-- Loading indicator -->
            <ProgressRing x:Name="LoadingRing" HorizontalAlignment="Center"
                          Margin="0,40,0,40" />

            <!-- Error message -->
            <TextBlock x:Name="ErrorText" Foreground="Red"
                       HorizontalAlignment="Center" Margin="0,20,0,20" />
        </StackPanel>
    </ScrollViewer>
</Page>
```

Create `src/ContinuumPlayer/Views/HomePage.xaml.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ContinuumPlayer.ViewModels;
using ContinuumPlayer.Controls;

namespace ContinuumPlayer.Views;

public sealed partial class HomePage : Page
{
    private readonly HomeViewModel _viewModel;

    public HomePage()
    {
        _viewModel = App.Services.GetRequiredService<HomeViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        LoadingRing.IsActive = true;
        await _viewModel.LoadCommand.ExecuteAsync(null);
        LoadingRing.IsActive = false;

        if (!string.IsNullOrEmpty(_viewModel.ErrorMessage))
        {
            ErrorText.Text = _viewModel.ErrorMessage;
            return;
        }

        // Bind hero carousel with items from all featured sections
        var featuredItems = _viewModel.FeaturedSections.SelectMany(s => s.Items).ToList();
        if (featuredItems.Count > 0)
            HeroCarouselControl.ItemsSource = featuredItems;

        // Build section rows
        SectionsPanel.Children.Clear();
        foreach (var section in _viewModel.Sections)
        {
            var row = new SectionRow { Section = section };
            SectionsPanel.Children.Add(row);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/ContinuumPlayer/Views/HomePage.*
git commit -m "feat: add HomePage with hero carousel and section rows"
```

---

### Task 19: Library Page

**Files:**
- Create: `src/ContinuumPlayer/Views/LibraryPage.xaml`
- Create: `src/ContinuumPlayer/Views/LibraryPage.xaml.cs`

- [ ] **Step 1: Create LibraryPage**

Create `src/ContinuumPlayer/Views/LibraryPage.xaml`:
```xml
<Page
    x:Class="ContinuumPlayer.Views.LibraryPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:ContinuumPlayer.Controls"
    Background="{StaticResource AppBackgroundBrush}"
    Loaded="Page_Loaded">

    <Grid Padding="{StaticResource PagePadding}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Header -->
        <TextBlock Grid.Row="0" x:Name="LibraryTitle"
                   FontSize="{StaticResource TitleFontSize}" FontWeight="Bold"
                   Foreground="{StaticResource PrimaryTextBrush}" Margin="0,0,0,16" />

        <!-- Filters bar -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="12" Margin="0,0,0,16">
            <ComboBox x:Name="SortCombo" Header="Sort by" Width="150"
                      SelectionChanged="SortCombo_SelectionChanged">
                <ComboBoxItem Content="Title" Tag="title" IsSelected="True" />
                <ComboBoxItem Content="Year" Tag="year" />
                <ComboBoxItem Content="IMDB Rating" Tag="rating_imdb" />
                <ComboBoxItem Content="Date Added" Tag="added_at" />
            </ComboBox>
            <ComboBox x:Name="OrderCombo" Header="Order" Width="120"
                      SelectionChanged="SortCombo_SelectionChanged">
                <ComboBoxItem Content="Ascending" Tag="asc" IsSelected="True" />
                <ComboBoxItem Content="Descending" Tag="desc" />
            </ComboBox>
            <ComboBox x:Name="GenreCombo" Header="Genre" Width="180"
                      PlaceholderText="All genres"
                      SelectionChanged="GenreCombo_SelectionChanged" />
        </StackPanel>

        <!-- Poster grid -->
        <ScrollViewer Grid.Row="2" VerticalScrollBarVisibility="Auto"
                      ViewChanged="ScrollViewer_ViewChanged">
            <ItemsRepeater x:Name="PosterGrid" ItemsSource="{x:Bind _viewModel.Items, Mode=OneWay}">
                <ItemsRepeater.Layout>
                    <UniformGridLayout MinItemWidth="{StaticResource PosterCardWidth}"
                                       MinItemHeight="280"
                                       MinRowSpacing="12" MinColumnSpacing="12" />
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate>
                        <controls:PosterCard MediaItem="{Binding}" />
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>
        </ScrollViewer>

        <ProgressRing x:Name="LoadingRing" Grid.Row="2" HorizontalAlignment="Center"
                      VerticalAlignment="Center" />
    </Grid>
</Page>
```

Create `src/ContinuumPlayer/Views/LibraryPage.xaml.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class LibraryPage : Page
{
    private readonly LibraryViewModel _viewModel;

    public LibraryPage()
    {
        _viewModel = App.Services.GetRequiredService<LibraryViewModel>();
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is Library lib)
        {
            LibraryTitle.Text = lib.Name;
            _viewModel.Initialize(lib.Id, lib.Type);
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        LoadingRing.IsActive = true;
        await _viewModel.LoadCommand.ExecuteAsync(null);
        LoadingRing.IsActive = false;

        // Populate genre filter
        GenreCombo.Items.Clear();
        GenreCombo.Items.Add(new ComboBoxItem { Content = "All genres", Tag = (string?)null });
        foreach (var genre in _viewModel.Genres)
            GenreCombo.Items.Add(new ComboBoxItem { Content = genre, Tag = genre });
    }

    private async void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortCombo.SelectedItem is ComboBoxItem sortItem)
            _viewModel.SortBy = sortItem.Tag as string ?? "title";
        if (OrderCombo.SelectedItem is ComboBoxItem orderItem)
            _viewModel.SortOrder = orderItem.Tag as string ?? "asc";

        await _viewModel.ApplyFiltersCommand.ExecuteAsync(null);
    }

    private async void GenreCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GenreCombo.SelectedItem is ComboBoxItem genreItem)
        {
            _viewModel.SelectedGenre = genreItem.Tag as string;
            await _viewModel.ApplyFiltersCommand.ExecuteAsync(null);
        }
    }

    private async void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            if (sv.VerticalOffset >= sv.ScrollableHeight - 200)
            {
                await _viewModel.LoadMoreCommand.ExecuteAsync(null);
            }
        }
    }
}
```

- [ ] **Step 2: Verify full solution builds**

Run: `dotnet build ContinuumPlayer.sln`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ContinuumPlayer/Views/LibraryPage.*
git commit -m "feat: add LibraryPage with poster grid, sorting, filtering, and infinite scroll"
```

---

### Task 20: Run All Tests & Final Build Verification

- [ ] **Step 1: Run all unit tests**

Run: `dotnet test tests/ContinuumPlayer.Core.Tests -v n`
Expected: All tests PASS.

- [ ] **Step 2: Build the full solution in Release mode**

Run: `dotnet build ContinuumPlayer.sln -c Release`
Expected: Build succeeded.

- [ ] **Step 3: Run the app to verify it launches**

Run: `dotnet run --project src/ContinuumPlayer/ContinuumPlayer.csproj`
Expected: App window opens showing the server select page.

- [ ] **Step 4: Commit any remaining fixes**

```bash
git add -A
git commit -m "fix: resolve build issues from integration"
```

---

### Task 21: Add .gitignore and Final Commit

- [ ] **Step 1: Ensure .gitignore covers all build artifacts**

Verify `F:/ContinuumPlayer/.gitignore` contains:
```
bin/
obj/
.vs/
*.user
.superpowers/
```

- [ ] **Step 2: Final commit with clean state**

```bash
git add -A
git commit -m "feat: complete Phase 1 - foundation and browsing"
```
