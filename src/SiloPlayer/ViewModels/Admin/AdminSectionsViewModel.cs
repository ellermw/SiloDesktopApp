using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminSectionsViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;
    private readonly SettingsApi _settingsApi;
    private readonly CollectionsApi _collectionsApi;
    private int _loadVersion;
    private bool _userCollectionsLoaded;

    public AdminSectionsViewModel(AdminApi adminApi, SettingsApi settingsApi, CollectionsApi collectionsApi)
    {
        _adminApi = adminApi;
        _settingsApi = settingsApi;
        _collectionsApi = collectionsApi;
    }

    public ObservableCollection<AdminSection> Sections { get; } = [];
    public ObservableCollection<Library> Libraries { get; } = [];

    /// <summary>Admin collections loaded once for the collection picker + badge labels.</summary>
    public List<LibraryCollection> Collections { get; private set; } = [];
    public List<Collection> UserCollections { get; private set; } = [];

    /// <summary>collection_id -> title. Used to display actual collection names in row badges.</summary>
    public Dictionary<string, string> CollectionLabels { get; private set; } = new();
    public RecipeCatalogResponse? RecipeCatalog { get; private set; }
    public Dictionary<string, string> RecipeLabels { get; private set; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _scope = "home";
    [ObservableProperty] private int? _selectedLibraryId;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var loadVersion = Interlocked.Increment(ref _loadVersion);
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var librariesTask = Libraries.Count == 0 ? _adminApi.GetAdminLibrariesAsync() : null;
            var recipesTask = RecipeCatalog == null ? LoadRecipeCatalogSafeAsync() : null;
            var collectionsTask = Collections.Count == 0 ? LoadCollectionsSafeAsync() : null;
            var userCollectionsTask = !_userCollectionsLoaded ? LoadUserCollectionsSafeAsync() : null;
            var sectionsTask = Scope == "library" && SelectedLibraryId.HasValue
                ? _adminApi.GetSectionsAsync("library", SelectedLibraryId.Value)
                : _adminApi.GetSectionsAsync("home");
            var requests = new List<Task> { sectionsTask };
            if (librariesTask != null) requests.Add(librariesTask);
            if (recipesTask != null) requests.Add(recipesTask);
            if (collectionsTask != null) requests.Add(collectionsTask);
            if (userCollectionsTask != null) requests.Add(userCollectionsTask);
            await Task.WhenAll(requests);
            if (loadVersion != _loadVersion) return;

            if (librariesTask != null)
            {
                Libraries.Clear();
                foreach (var library in await librariesTask) Libraries.Add(library);
            }
            if (recipesTask != null)
            {
                RecipeCatalog = await recipesTask;
                RecipeLabels = RecipeCatalog.Categories
                    .SelectMany(category => category.Value)
                    .GroupBy(definition => definition.Type)
                    .ToDictionary(
                        group => group.Key,
                        group => group.SelectMany(definition => definition.Presets)
                            .Select(preset => preset.DisplayName)
                            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key);
            }
            if (collectionsTask != null)
            {
                Collections = await collectionsTask;
            }
            if (userCollectionsTask != null)
            {
                UserCollections = await userCollectionsTask;
                _userCollectionsLoaded = true;
            }
            CollectionLabels = Collections.ToDictionary(c => c.Id, FormatCollectionLabel);
            foreach (var collection in UserCollections)
                CollectionLabels[collection.Id] = collection.Name;

            var sections = await sectionsTask;
            Sections.Clear();
            foreach (var s in sections) Sections.Add(s);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally
        {
            if (loadVersion == _loadVersion) IsLoading = false;
        }
    }

    private async Task<RecipeCatalogResponse> LoadRecipeCatalogSafeAsync()
    {
        try { return await _settingsApi.GetRecipeCatalogAsync(); }
        catch { return new RecipeCatalogResponse(); }
    }

    private async Task<List<LibraryCollection>> LoadCollectionsSafeAsync()
    {
        try { return (await _adminApi.GetCollectionsAsync()).Collections; }
        catch { return []; }
    }

    private async Task<List<Collection>> LoadUserCollectionsSafeAsync()
    {
        try { return (await _collectionsApi.GetCollectionsAsync()).Collections; }
        catch { return []; }
    }

    private string FormatCollectionLabel(LibraryCollection collection)
    {
        var library = Libraries.FirstOrDefault(entry => entry.Id == collection.LibraryId);
        return library == null ? collection.Title : $"{collection.Title} ({library.Name})";
    }

    public async Task<int> BulkCreateSectionsAsync(
        IReadOnlyCollection<int> libraryIds,
        string title,
        string sectionType,
        int itemLimit,
        bool featured,
        bool enabled,
        Dictionary<string, object?> config)
    {
        var body = new Dictionary<string, object?>
        {
            ["scope"] = "library",
            ["library_ids"] = libraryIds,
            ["section_type"] = sectionType,
            ["title"] = title,
            ["item_limit"] = itemLimit,
            ["featured"] = featured,
            ["enabled"] = enabled,
            ["config"] = config,
        };
        var result = await _adminApi.BulkCreateSectionsAsync(body);
        await LoadAsync();
        StatusMessage = $"Created {result.Created} section{(result.Created == 1 ? "" : "s")}.";
        return result.Created;
    }

    public Task CreateLibrarySectionAsync(
        int libraryId,
        string title,
        string sectionType,
        int itemLimit,
        bool featured,
        bool enabled,
        Dictionary<string, object?> config)
        => _adminApi.CreateSectionAsync(new Dictionary<string, object?>
        {
            ["scope"] = "library",
            ["library_id"] = libraryId,
            ["section_type"] = sectionType,
            ["title"] = title,
            ["item_limit"] = itemLimit,
            ["featured"] = featured,
            ["enabled"] = enabled,
            ["config"] = config,
        });

    public async Task RefreshAfterGalleryMutationAsync(string statusMessage)
    {
        await LoadAsync();
        StatusMessage = statusMessage;
    }

    public async Task<LibraryCollection> EnsureManagedTraktCollectionAsync(
        int libraryId,
        string title,
        string preset,
        string mediaType,
        int limit,
        bool featured)
    {
        var key = $"trakt:{preset}:{mediaType}:library:{libraryId}";
        var existing = Collections.FirstOrDefault(collection =>
            collection.LibraryId == libraryId &&
            collection.CollectionType == "trakt" &&
            collection.ManagementMode == "section" &&
            collection.ManagementKey == key &&
            GetDictionaryString(collection.SourceConfig, "preset") == preset &&
            GetDictionaryString(collection.SourceConfig, "media_type") == mediaType);
        if (existing != null) return existing;

        var imported = await _adminApi.ImportTraktCollectionAsync(new ImportTraktCollectionRequest
        {
            LibraryId = libraryId,
            Title = title,
            Preset = preset,
            MediaType = mediaType,
            Limit = limit,
            Featured = featured,
            ManagementMode = "section",
            ManagementSource = "recipe_gallery",
            ManagementKey = key,
        });
        Collections.Add(imported.Collection);
        CollectionLabels[imported.Collection.Id] = FormatCollectionLabel(imported.Collection);
        return imported.Collection;
    }

    private static string? GetDictionaryString(IReadOnlyDictionary<string, object>? values, string key)
    {
        if (values == null || !values.TryGetValue(key, out var value) || value == null) return null;
        if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.String)
            return element.GetString();
        return value.ToString();
    }

    [RelayCommand]
    public async Task CreateSectionAsync(object body)
    {
        try
        {
            await _adminApi.CreateSectionAsync(body);
            await LoadAsync();
            StatusMessage = "Section created.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task UpdateSectionAsync((string Id, object Body) args)
    {
        try
        {
            await _adminApi.UpdateSectionAsync(args.Id, args.Body);
            await LoadAsync();
            StatusMessage = "Section updated.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task DeleteSectionAsync(string id)
    {
        try
        {
            await _adminApi.DeleteSectionAsync(id);
            await LoadAsync();
            StatusMessage = "Section deleted.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task RestoreDefaultsAsync(bool resetProfiles)
    {
        try
        {
            int? libraryId = Scope == "library" ? SelectedLibraryId : null;
            await _adminApi.RestoreSectionDefaultsAsync(Scope, libraryId, resetProfiles);
            await LoadAsync();
            StatusMessage = "Default sections restored.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    public async Task ReorderSectionsAsync(List<string> orderedIds)
    {
        try
        {
            var entries = orderedIds
                .Select((id, index) => new Dictionary<string, object?>
                {
                    ["id"] = id,
                    ["sort_order"] = index,
                })
                .ToList();
            await _adminApi.ReorderSectionsAsync(new Dictionary<string, object?>
            {
                ["sections"] = entries,
            });
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    public async Task MoveSectionAsync(AdminSection section, int direction)
    {
        // direction: -1 = up, +1 = down
        var list = Sections.ToList();
        int idx = list.IndexOf(section);
        int newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= list.Count) return;

        // Swap in the observable collection
        Sections.Move(idx, newIdx);

        // Persist new order
        try
        {
            var ids = Sections.Select(s => s.Id).ToList();
            var entries = ids
                .Select((id, i) => new Dictionary<string, object?>
                {
                    ["id"] = id,
                    ["sort_order"] = i,
                })
                .ToList();
            await _adminApi.ReorderSectionsAsync(new Dictionary<string, object?>
            {
                ["sections"] = entries,
            });
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    public async Task MoveSectionToAsync(string sourceId, string targetId)
    {
        var sourceIndex = Sections.ToList().FindIndex(section => section.Id == sourceId);
        var targetIndex = Sections.ToList().FindIndex(section => section.Id == targetId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;

        Sections.Move(sourceIndex, targetIndex);
        await ReorderSectionsAsync(Sections.Select(section => section.Id).ToList());
    }

    public async Task ToggleEnabledAsync(AdminSection section)
    {
        try
        {
            bool newEnabled = !section.Enabled;
            // NOTE: Dictionary<string, object> instead of anonymous type —
            // .NET 8 Release publish enables trimming, which strips anonymous
            // type property names and silently serializes them as {}. See
            // feedback_build_release memory. Anonymous types here produced
            // empty PUT bodies, which is why "featured toggle doesn't save".
            var body = new Dictionary<string, object>
            {
                ["title"] = section.Title,
                ["section_type"] = section.SectionType,
                ["item_limit"] = section.ItemLimit,
                ["featured"] = section.Featured,
                ["enabled"] = newEnabled,
            };
            await _adminApi.UpdateSectionAsync(section.Id, body);
            section.Enabled = newEnabled;
            StatusMessage = newEnabled ? "Section enabled." : "Section disabled.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    public object BuildCreateBody(string title, string sectionType, int itemLimit, bool featured, bool enabled,
        Dictionary<string, object?>? config = null)
    {
        // Same trimming concern — use a dictionary so the JSON body survives
        // .NET 8 Release publish with trimming enabled.
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["section_type"] = sectionType,
            ["item_limit"] = itemLimit,
            ["featured"] = featured,
            ["enabled"] = enabled,
            ["scope"] = Scope,
            ["library_id"] = Scope == "library" ? SelectedLibraryId : null,
        };
        if (config != null && config.Count > 0)
            body["config"] = config;
        return body;
    }

    // ===== Config extraction helpers (for populating edit form from existing section) =====

    public static string? GetConfigString(AdminSection section, string key)
    {
        if (section.Config == null || !section.Config.TryGetValue(key, out var val)) return null;
        if (val is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.String)
            return je.GetString();
        return val?.ToString();
    }

    public static List<int> GetConfigLibraryIds(AdminSection section)
    {
        if (section.Config == null || !section.Config.TryGetValue("library_ids", out var val)) return [];
        if (val is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
            return je.EnumerateArray()
                .Where(e => e.ValueKind == System.Text.Json.JsonValueKind.Number)
                .Select(e => e.GetInt32())
                .ToList();
        return [];
    }

    public static string? GetConfigMediaScope(AdminSection section)
        => GetConfigString(section, "media_scope");

    public static string? GetConfigCollectionId(AdminSection section)
        => GetConfigString(section, "library_collection_id");

    public static string? GetConfigUserCollectionId(AdminSection section)
        => GetConfigString(section, "user_collection_id");
}
