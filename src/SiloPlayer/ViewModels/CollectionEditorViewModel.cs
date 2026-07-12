using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.ViewModels;

public partial class CollectionEditorViewModel : ObservableObject
{
    private readonly CollectionsApi _collectionsApi;
    private readonly CatalogApi _catalogApi;
    private readonly SettingsApi _settingsApi;
    private readonly SiloPlayer.Core.Services.AuthService _authService;

    public CollectionEditorViewModel(
        CollectionsApi collectionsApi,
        CatalogApi catalogApi,
        SettingsApi settingsApi,
        SiloPlayer.Core.Services.AuthService authService)
    {
        _collectionsApi = collectionsApi;
        _catalogApi = catalogApi;
        _settingsApi = settingsApi;
        _authService = authService;
    }

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string? _collectionId;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string _collectionType = "manual";

    [ObservableProperty]
    private bool _isShared;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private bool _isPreviewing;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _previewTotal;

    [ObservableProperty]
    private string? _searchQuery;

    [ObservableProperty]
    private string? _sourceUrl;

    [ObservableProperty]
    private string? _maxItemsText;

    [ObservableProperty]
    private string? _syncSchedule;

    [ObservableProperty]
    private bool _includeInServerCollections;

    [ObservableProperty]
    private string? _posterSourceUrl;

    [ObservableProperty]
    private string? _currentPosterUrl;

    public byte[]? PosterFileBytes { get; private set; }
    public string? PosterFileName { get; private set; }
    public string PosterContentType { get; private set; } = "image/jpeg";

    public void SetPosterFile(string fileName, byte[] bytes, string? contentType)
    {
        PosterFileName = fileName;
        PosterFileBytes = bytes;
        PosterContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        PosterSourceUrl = "";
    }

    [ObservableProperty]
    private string _watchFilter = "all";

    [ObservableProperty]
    private string _mediaFilter = "all";

    [ObservableProperty]
    private string? _lastSyncSummary;

    public ObservableCollection<Library> AvailableLibraries { get; } = [];
    public ObservableCollection<Profile> AvailableProfiles { get; } = [];
    public ObservableCollection<int> SelectedLibraryIds { get; } = [];
    public ObservableCollection<string> AllowedProfileIds { get; } = [];

    // Smart collection rules
    public ObservableCollection<QueryRule> Rules { get; } = [];

    // Manual collection items
    public ObservableCollection<CollectionItem> ManualItems { get; } = [];

    // Preview results for smart collections
    public ObservableCollection<CollectionPreviewItem> PreviewItems { get; } = [];

    // Search results for adding manual items
    public ObservableCollection<MediaItem> SearchResults { get; } = [];

    /// <summary>
    /// Raised when the collection is saved successfully so the page can navigate back.
    /// </summary>
    public event Action? Saved;
    public event Action? Deleted;

    [RelayCommand]
    private async Task LoadReferenceDataAsync()
    {
        try
        {
            var librariesTask = _catalogApi.GetLibrariesAsync();
            var profilesTask = _settingsApi.GetProfilesAsync();
            await Task.WhenAll(librariesTask, profilesTask, _collectionsApi.GetCollectionCapabilitiesAsync());
            AvailableLibraries.Clear();
            foreach (var library in librariesTask.Result) AvailableLibraries.Add(library);
            AvailableProfiles.Clear();
            foreach (var profile in profilesTask.Result.Profiles) AvailableProfiles.Add(profile);
        }
        catch (Exception ex) { ErrorMessage = $"Failed to load collection options: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task LoadExistingAsync(string collectionId)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // Load the editor contract in parallel, matching the WebUI's
            // collections/libraries/profiles queries.
            var collectionsTask = _collectionsApi.GetCollectionsAsync();
            var librariesTask = _catalogApi.GetLibrariesAsync();
            var profilesTask = _settingsApi.GetProfilesAsync();
            var capabilitiesTask = _collectionsApi.GetCollectionCapabilitiesAsync();
            await Task.WhenAll(collectionsTask, librariesTask, profilesTask, capabilitiesTask);

            var response = collectionsTask.Result;
            var collection = response.Collections.FirstOrDefault(c => c.Id == collectionId);
            if (collection == null)
            {
                ErrorMessage = "Collection not found.";
                return;
            }

            IsEditing = true;
            IsReadOnly = !string.IsNullOrWhiteSpace(_authService.SelectedProfileId) &&
                !string.Equals(collection.CreatorProfileId, _authService.SelectedProfileId, StringComparison.Ordinal);
            CollectionId = collection.Id;
            Name = collection.Name;
            Description = collection.Description;
            CollectionType = collection.CollectionType;
            IsShared = collection.IsShared;
            SourceUrl = collection.SourceUrl;
            MaxItemsText = ReadSourceConfigValue(collection.SourceConfig, "max_items")
                ?? ReadSourceConfigValue(collection.SourceConfig, "limit");
            SyncSchedule = FormatSyncSchedule(collection.SyncSchedule);
            IncludeInServerCollections = collection.IncludeInServerCollections;
            PosterSourceUrl = "";
            CurrentPosterUrl = collection.PosterUrl;
            LastSyncSummary = BuildLastSyncSummary(collection);

            AvailableLibraries.Clear();
            foreach (var library in librariesTask.Result) AvailableLibraries.Add(library);
            AvailableProfiles.Clear();
            foreach (var profile in profilesTask.Result.Profiles) AvailableProfiles.Add(profile);
            SelectedLibraryIds.Clear();
            foreach (var id in ReadSourceConfigIntList(collection.SourceConfig, "library_ids"))
                SelectedLibraryIds.Add(id);
            AllowedProfileIds.Clear();
            foreach (var id in collection.AllowedProfileIds) AllowedProfileIds.Add(id);
            ReadDisplayFilters(collection.DisplayQueryDefinition);

            // Load rules from query definition
            if (collection.QueryDefinition?.Groups?.Count > 0)
            {
                Rules.Clear();
                foreach (var group in collection.QueryDefinition.Groups)
                {
                    foreach (var rule in group.Rules)
                        Rules.Add(rule);
                }
            }

            // Load manual items
            if (collection.CollectionType == "manual")
            {
                var itemsResponse = await _collectionsApi.GetCollectionItemsAsync(collectionId);
                ManualItems.Clear();
                foreach (var item in itemsResponse.Items)
                    ManualItems.Add(item);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load collection: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsReadOnly) return;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Collection name is required.";
            return;
        }

        IsSaving = true;
        ErrorMessage = null;

        try
        {
            if (IsEditing && !string.IsNullOrEmpty(CollectionId))
            {
                var request = new UpdateCollectionRequest
                {
                    Name = Name.Trim(),
                    Description = Description?.Trim() ?? "",
                    IsShared = IsShared,
                    AllowedProfileIds = [.. AllowedProfileIds],
                    PosterSourceUrl = string.IsNullOrWhiteSpace(PosterSourceUrl) ? null : PosterSourceUrl.Trim()
                };

                if (CollectionType == "smart" && Rules.Count > 0)
                {
                    request.QueryDefinition = BuildQueryDefinition();
                }

                if (IsImportedCollection)
                {
                    if (!string.IsNullOrWhiteSpace(MaxItemsText) && !int.TryParse(MaxItemsText, out _))
                    {
                        ErrorMessage = "Max items must be a number.";
                        return;
                    }

                    if (CollectionType == "mdblist")
                        request.SourceUrl = string.IsNullOrWhiteSpace(SourceUrl) ? null : SourceUrl.Trim();

                    request.MaxItems = int.TryParse(MaxItemsText, out var maxItems) && maxItems > 0
                        ? maxItems
                        : 0;
                    request.IncludeInServerCollections = IncludeInServerCollections;
                    request.LibraryIds = [.. SelectedLibraryIds];
                    request.DisplayQueryDefinition = BuildDisplayQueryDefinition();
                }

                if (PosterFileBytes is { Length: > 0 } poster && !string.IsNullOrWhiteSpace(PosterFileName))
                    await _collectionsApi.UpdateCollectionAsync(CollectionId, request, PosterFileName, poster, PosterContentType);
                else
                    await _collectionsApi.UpdateCollectionAsync(CollectionId, request);
            }
            else
            {
                var request = new CreateCollectionRequest
                {
                    Name = Name.Trim(),
                    Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    CollectionType = CollectionType,
                    IsShared = IsShared,
                    AllowedProfileIds = [.. AllowedProfileIds],
                    PosterSourceUrl = string.IsNullOrWhiteSpace(PosterSourceUrl) ? null : PosterSourceUrl.Trim()
                };

                if (CollectionType == "smart" && Rules.Count > 0)
                {
                    request.QueryDefinition = BuildQueryDefinition();
                }

                var created = PosterFileBytes is { Length: > 0 } poster && !string.IsNullOrWhiteSpace(PosterFileName)
                    ? await _collectionsApi.CreateCollectionAsync(request, PosterFileName, poster, PosterContentType)
                    : await _collectionsApi.CreateCollectionAsync(request);

                // For manual collections, add items after creation
                if (CollectionType == "manual" && ManualItems.Count > 0)
                {
                    foreach (var item in ManualItems)
                    {
                        try
                        {
                            await _collectionsApi.AddCollectionItemAsync(created.Id, item.MediaItemId);
                        }
                        catch
                        {
                            // Continue adding remaining items even if one fails
                        }
                    }
                }
            }

            PosterFileBytes = null;
            PosterFileName = null;
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save collection: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (IsReadOnly || string.IsNullOrWhiteSpace(CollectionId) || !IsImportedCollection) return;
        IsSaving = true;
        ErrorMessage = null;
        try
        {
            var result = await _collectionsApi.SyncCollectionAsync(CollectionId);
            LastSyncSummary = string.IsNullOrWhiteSpace(result.Message)
                ? $"Sync {result.Status}"
                : result.Message;
        }
        catch (Exception ex) { ErrorMessage = $"Sync failed: {ex.Message}"; }
        finally { IsSaving = false; }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (IsReadOnly || string.IsNullOrWhiteSpace(CollectionId)) return;
        IsSaving = true;
        ErrorMessage = null;
        try
        {
            await _collectionsApi.DeleteCollectionAsync(CollectionId);
            Deleted?.Invoke();
        }
        catch (Exception ex) { ErrorMessage = $"Failed to delete collection: {ex.Message}"; }
        finally { IsSaving = false; }
    }

    [RelayCommand]
    private async Task RemovePosterAsync()
    {
        if (IsReadOnly || string.IsNullOrWhiteSpace(CollectionId)) return;
        try
        {
            await _collectionsApi.DeleteCollectionImageAsync(CollectionId);
            CurrentPosterUrl = null;
            PosterFileBytes = null;
            PosterFileName = null;
        }
        catch (Exception ex) { ErrorMessage = $"Failed to remove poster: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (Rules.Count == 0)
        {
            ErrorMessage = "Add at least one rule to preview.";
            return;
        }

        IsPreviewing = true;
        ErrorMessage = null;
        PreviewItems.Clear();

        try
        {
            var request = new CollectionPreviewRequest
            {
                QueryDefinition = BuildQueryDefinition(),
                Limit = 20
            };

            var response = await _collectionsApi.PreviewCollectionAsync(request);
            foreach (var item in response.Items)
                PreviewItems.Add(item);
            PreviewTotal = response.Total;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Preview failed: {ex.Message}";
        }
        finally
        {
            IsPreviewing = false;
        }
    }

    [RelayCommand]
    private void AddRule()
    {
        Rules.Add(new QueryRule
        {
            Field = "genre",
            Op = "is",
            Value = ""
        });
    }

    [RelayCommand]
    private void RemoveRule(QueryRule rule)
    {
        Rules.Remove(rule);
    }

    [RelayCommand]
    private async Task SearchItemsAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        SearchResults.Clear();

        try
        {
            // Use the catalog search with the query parameter
            var response = await _catalogApi.GetCatalogAsync(
                libraryId: 0, q: query, limit: 20);
            foreach (var item in response.Items)
                SearchResults.Add(item);
        }
        catch
        {
            // Search failure is non-fatal
        }
    }

    [RelayCommand]
    private void AddManualItem(MediaItem item)
    {
        // Don't add duplicates
        if (ManualItems.Any(m => m.MediaItemId == item.ContentId || m.ContentId == item.ContentId))
            return;

        ManualItems.Add(new CollectionItem
        {
            MediaItemId = item.ContentId,
            ContentId = item.ContentId,
            Title = item.Title,
            Year = item.Year,
            Type = item.Type,
            PosterUrl = item.PosterUrl,
            Position = ManualItems.Count
        });
    }

    [RelayCommand]
    private void RemoveManualItem(CollectionItem item)
    {
        ManualItems.Remove(item);
    }

    [RelayCommand]
    private void MoveItemUp(CollectionItem item)
    {
        int index = ManualItems.IndexOf(item);
        if (index > 0)
            ManualItems.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveItemDown(CollectionItem item)
    {
        int index = ManualItems.IndexOf(item);
        if (index < ManualItems.Count - 1)
            ManualItems.Move(index, index + 1);
    }

    private QueryDefinition BuildQueryDefinition()
    {
        return new QueryDefinition
        {
            Match = "all",
            Groups =
            [
                new QueryGroup
                {
                    Match = "all",
                    Rules = [.. Rules]
                }
            ]
        };
    }

    private DisplayQueryDefinition BuildDisplayQueryDefinition()
    {
        var rules = new List<QueryRule>();
        if (WatchFilter == "watched") rules.Add(new QueryRule { Field = "watched", Op = "is", Value = true });
        else if (WatchFilter == "unwatched") rules.Add(new QueryRule { Field = "watched", Op = "is", Value = false });
        if (MediaFilter == "movie") rules.Add(new QueryRule { Field = "type", Op = "is", Value = "movie" });
        else if (MediaFilter == "series") rules.Add(new QueryRule { Field = "type", Op = "is", Value = "series" });
        return new DisplayQueryDefinition
        {
            Match = "all",
            Groups = rules.Count == 0 ? [] : [new QueryGroup { Match = "all", Rules = rules }]
        };
    }

    private void ReadDisplayFilters(DisplayQueryDefinition? definition)
    {
        WatchFilter = "all";
        MediaFilter = "all";
        foreach (var rule in definition?.Groups.SelectMany(group => group.Rules) ?? [])
        {
            if (rule.Field == "watched" && rule.Op == "is" && TryReadBool(rule.Value, out var watched))
                WatchFilter = watched ? "watched" : "unwatched";
            else if (rule.Field == "type" && rule.Op == "is")
            {
                var value = ReadObjectString(rule.Value);
                if (value is "movie" or "series") MediaFilter = value;
            }
        }
    }

    public bool IsImportedCollection
        => CollectionType is "mdblist" or "tmdb" or "trakt";

    private static string? ReadSourceConfigValue(Dictionary<string, object>? sourceConfig, string key)
    {
        if (sourceConfig == null || !sourceConfig.TryGetValue(key, out var value) || value == null)
            return null;

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue.ToString() : element.ToString(),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => element.ToString()
            };
        }

        return value.ToString();
    }

    private static List<int> ReadSourceConfigIntList(Dictionary<string, object>? sourceConfig, string key)
    {
        if (sourceConfig == null || !sourceConfig.TryGetValue(key, out var raw) || raw == null) return [];
        if (raw is JsonElement { ValueKind: JsonValueKind.Array } array)
            return array.EnumerateArray().Where(value => value.TryGetInt32(out _)).Select(value => value.GetInt32()).ToList();
        if (raw is IEnumerable<object> values)
            return values.Select(value => int.TryParse(value?.ToString(), out var id) ? id : 0).Where(id => id > 0).ToList();
        return [];
    }

    private static bool TryReadBool(object? value, out bool result)
    {
        if (value is bool boolean) { result = boolean; return true; }
        if (value is JsonElement { ValueKind: JsonValueKind.True }) { result = true; return true; }
        if (value is JsonElement { ValueKind: JsonValueKind.False }) { result = false; return true; }
        return bool.TryParse(value?.ToString(), out result);
    }

    private static string? ReadObjectString(object? value)
        => value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : value?.ToString();

    private static string BuildLastSyncSummary(Collection collection)
    {
        if (string.IsNullOrWhiteSpace(collection.LastSyncAt)) return "Not synced yet";
        var status = string.IsNullOrWhiteSpace(collection.LastSyncStatus) ? "Last synced" : collection.LastSyncStatus;
        return string.IsNullOrWhiteSpace(collection.LastSyncMessage)
            ? $"{status} · {collection.LastSyncAt}"
            : $"{status} · {collection.LastSyncMessage}";
    }

    private static string FormatSyncSchedule(string? schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
            return "Manual only";

        return schedule.Trim() switch
        {
            "daily" => "Daily",
            "weekly" => "Weekly",
            "monthly" => "Monthly",
            "none" => "Manual only",
            var raw => raw
        };
    }
}
