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
    [ObservableProperty] private bool _isLoadUnavailable;
    [ObservableProperty] private bool _isNotFound;
    private readonly CollectionsApi _collectionsApi;
    private readonly CatalogApi _catalogApi;
    private readonly SettingsApi _settingsApi;
    private readonly SiloPlayer.Core.Services.AuthService _authService;
    private readonly HashSet<string> _originalManualItemIds = new(StringComparer.Ordinal);
    private bool _itemReorderSupported;
    [ObservableProperty] private bool _isManualMutationPending;
    [ObservableProperty] private bool _canReorderManualItems;
    private string _initialDefaultSortValue = "";

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
    private string _collectionType = "smart";

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
    public string SourceKind { get; private set; } = "";
    public bool HasEditableSourceUrl => SourceKind is "mdblist" or "tmdb_list";

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
        OnPropertyChanged(nameof(PosterFileBytes));
        OnPropertyChanged(nameof(PosterFileName));
    }

    public void ClearPosterFile()
    {
        PosterFileBytes = null; PosterFileName = null;
        OnPropertyChanged(nameof(PosterFileBytes)); OnPropertyChanged(nameof(PosterFileName));
    }

    [ObservableProperty]
    private string _watchFilter = "all";

    [ObservableProperty]
    private string _mediaFilter = "all";

    [ObservableProperty]
    private string _defaultSortValue = "";

    [ObservableProperty]
    private string? _lastSyncSummary;

    [ObservableProperty]
    private string _sourcePresetSummary = "Source-managed collection";
    [ObservableProperty] private string? _sourceLastNote;

    [ObservableProperty]
    private string _sourceProviderLabel = "SOURCE";

    [ObservableProperty]
    private string _sourceItemCountText = "0";

    [ObservableProperty]
    private string _createdDisplayText = "Unknown";

    public ObservableCollection<Library> AvailableLibraries { get; } = [];
    public ObservableCollection<Profile> AvailableProfiles { get; } = [];
    public ObservableCollection<int> SelectedLibraryIds { get; } = [];
    public ObservableCollection<string> AllowedProfileIds { get; } = [];

    // Smart collection rules
    public ObservableCollection<QueryRule> Rules { get; } = [];
    public QueryDefinition RuleDefinition { get; private set; } = new();
    private string _initialWatchFilter = "all", _initialMediaFilter = "all";

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
        ErrorMessage = null; IsLoadUnavailable = false; IsNotFound = false;
        Task<Collection>? collectionTask = null;

        try
        {
            // Load the editor contract in parallel, matching the WebUI's
            // collections/libraries/profiles queries.
            var collectionsTask = collectionTask = _collectionsApi.GetCollectionAsync(collectionId);
            var librariesTask = _catalogApi.GetLibrariesAsync();
            var profilesTask = _settingsApi.GetProfilesAsync();
            var capabilitiesTask = _collectionsApi.GetCollectionCapabilitiesAsync();
            await Task.WhenAll(collectionsTask, librariesTask, profilesTask, capabilitiesTask);

            _itemReorderSupported = capabilitiesTask.Result.ItemReorder;
            CanReorderManualItems = false;
            var collection = collectionsTask.Result;
            if (collection == null)
            {
                IsNotFound = true; ErrorMessage = "Collection not found.";
                return;
            }

            IsEditing = true;
            IsReadOnly = !string.IsNullOrWhiteSpace(_authService.SelectedProfileId) &&
                !string.Equals(collection.CreatorProfileId, _authService.SelectedProfileId, StringComparison.Ordinal);
            CollectionId = collection.Id;
            Name = collection.Name;
            Description = collection.Description;
            CollectionType = collection.CollectionType;
            SourceKind = CollectionType == "tmdb" && ReadSourceConfigValue(collection.SourceConfig, "mode") == "tmdb_list"
                ? "tmdb_list" : CollectionType;
            IsShared = collection.IsShared;
            SourceUrl = collection.SourceUrl;
            MaxItemsText = ReadSourceConfigValue(collection.SourceConfig, "max_items")
                ?? ReadSourceConfigValue(collection.SourceConfig, "limit");
            SyncSchedule = FormatSyncSchedule(collection.SyncSchedule);
            IncludeInServerCollections = collection.IncludeInServerCollections;
            PosterSourceUrl = "";
            ClearPosterFile();
            CurrentPosterUrl = collection.PosterUrl;
            LastSyncSummary = BuildLastSyncSummary(collection);
            SourceProviderLabel = SourceKind switch { "tmdb_list" => "TMDB List", "tmdb" => "TMDB", "trakt" => "Trakt", "mdblist" => "MDBList", _ => SourceKind };
            SourceLastNote = collection.LastSyncMessage;
            SourcePresetSummary = BuildSourcePresetSummary(collection);
            SourceItemCountText = collection.ItemCount.ToString("N0");
            CreatedDisplayText = FormatCreatedAt(collection.CreatedAt);

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
            _initialWatchFilter = WatchFilter; _initialMediaFilter = MediaFilter;
            DefaultSortValue = ReadCollectionSortValue(collection.SortConfig);
            _initialDefaultSortValue = DefaultSortValue;

            // Load rules from query definition
            RuleDefinition = SiloPlayer.Core.Services.QueryEditing.Clone(collection.QueryDefinition);
            Rules.Clear();
            foreach (var rule in RuleDefinition.Groups.FirstOrDefault()?.Rules ?? []) Rules.Add(rule);

            // Load manual items
            if (collection.CollectionType == "manual")
            {
                var itemsResponse = await _collectionsApi.GetCollectionItemsAsync(collectionId);
                ManualItems.Clear();
                _originalManualItemIds.Clear();
                foreach (var item in itemsResponse.Items)
                {
                    ManualItems.Add(item);
                    _originalManualItemIds.Add(item.MediaItemId);
                }
                CanReorderManualItems = !IsReadOnly && _itemReorderSupported && await _collectionsApi.IsCompleteItemOrderAsync(collectionId, ManualItems.Select(item => item.MediaItemId).ToList());
            }
        }
        catch (Exception ex)
        {
            IsNotFound = collectionTask?.Exception?.Flatten().InnerExceptions.OfType<ApiException>().Any(error => error.StatusCode == 404) == true;
            IsLoadUnavailable = !IsNotFound;
            ErrorMessage = IsNotFound ? "Collection not found." : $"Failed to load collection: {ex.Message}";
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
        var keepEditorOpen = false;

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

                if (CollectionType == "smart")
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

                    if (SourceKind == "tmdb_list" && !SiloPlayer.Core.Services.CollectionImportPolicy.IsTMDBListUrl(SourceUrl))
                    { ErrorMessage = "Enter a public TMDB list URL or numeric list ID."; return; }
                    if (HasEditableSourceUrl)
                        request.SourceUrl = string.IsNullOrWhiteSpace(SourceUrl) ? null : SourceUrl.Trim();

                    request.MaxItems = int.TryParse(MaxItemsText, out var maxItems) && maxItems > 0
                        ? maxItems
                        : 0;
                    request.IncludeInServerCollections = IncludeInServerCollections;
                    request.LibraryIds = [.. SelectedLibraryIds];
                    if (WatchFilter != _initialWatchFilter || MediaFilter != _initialMediaFilter)
                        request.DisplayQueryDefinition = BuildDisplayQueryDefinition();
                    if (!string.Equals(DefaultSortValue, _initialDefaultSortValue, StringComparison.Ordinal))
                        request.SortConfig = BuildCollectionSortConfig(DefaultSortValue);
                }

                if (PosterFileBytes is { Length: > 0 } poster && !string.IsNullOrWhiteSpace(PosterFileName))
                    await _collectionsApi.UpdateCollectionAsync(CollectionId, request, PosterFileName, poster, PosterContentType);
                else
                    await _collectionsApi.UpdateCollectionAsync(CollectionId, request);

                if (CollectionType == "manual")
                    await SaveManualItemsAsync(CollectionId);
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

                if (CollectionType == "smart")
                {
                    request.QueryDefinition = BuildQueryDefinition();
                }

                var created = PosterFileBytes is { Length: > 0 } poster && !string.IsNullOrWhiteSpace(PosterFileName)
                    ? await _collectionsApi.CreateCollectionAsync(request, PosterFileName, poster, PosterContentType)
                    : await _collectionsApi.CreateCollectionAsync(request);

                // Creation is already durable at this point. Switch to edit mode before
                // any follow-up item request so a retry cannot create a second collection.
                CollectionId = created.Id;
                IsEditing = true;
                _originalManualItemIds.Clear();

                // For manual collections, add items after creation
                if (CollectionType == "manual" && ManualItems.Count > 0)
                {
                    var addedIds = new List<string>(ManualItems.Count);
                    var failed = 0;
                    foreach (var item in ManualItems)
                    {
                        try
                        {
                            await _collectionsApi.AddCollectionItemAsync(created.Id, item.MediaItemId);
                            addedIds.Add(item.MediaItemId);
                            _originalManualItemIds.Add(item.MediaItemId);
                        }
                        catch
                        {
                            failed++;
                        }
                    }

                    if (addedIds.Count > 0)
                        await _collectionsApi.ReorderCollectionItemsAsync(created.Id, addedIds);
                    if (failed > 0)
                    {
                        ErrorMessage = $"{failed} item(s) could not be added to the collection. Retry to add the remaining items.";
                        keepEditorOpen = true;
                    }
                }
            }

            PosterFileBytes = null;
            PosterFileName = null;
            if (!keepEditorOpen)
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

    private static string ReadCollectionSortValue(Dictionary<string, object>? config)
    {
        if (config == null || !config.TryGetValue("field", out var fieldValue)) return "";
        var field = JsonValueText(fieldValue).Trim();
        if (field.Length == 0) return "";
        var order = config.TryGetValue("order", out var orderValue)
            ? JsonValueText(orderValue).Trim().ToLowerInvariant()
            : "";
        if (order is not ("asc" or "desc"))
            order = field is "title" or "content_rating" or "author" or "narrator" or "series" ? "asc" : "desc";
        return $"{field}:{order}";
    }

    private static Dictionary<string, object> BuildCollectionSortConfig(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var parts = value.Split(':', 2);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) return [];
        var order = parts.Length > 1 && parts[1] is "asc" or "desc"
            ? parts[1]
            : parts[0] is "title" or "content_rating" or "author" or "narrator" or "series" ? "asc" : "desc";
        return new Dictionary<string, object>
        {
            ["field"] = parts[0],
            ["order"] = order,
        };
    }

    private static string JsonValueText(object? value)
        => value is JsonElement element
            ? element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.ToString()
            : value?.ToString() ?? "";

    private async Task SaveManualItemsAsync(string collectionId)
    {
        var currentIds = ManualItems
            .Select(item => item.MediaItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        var currentSet = currentIds.ToHashSet(StringComparer.Ordinal);

        foreach (var removed in _originalManualItemIds.Where(id => !currentSet.Contains(id)).ToList())
            await _collectionsApi.RemoveCollectionItemAsync(collectionId, removed);

        foreach (var added in currentIds.Where(id => !_originalManualItemIds.Contains(id)))
            await _collectionsApi.AddCollectionItemAsync(collectionId, added);

        if (!IsEditing && _itemReorderSupported && currentIds.Count > 0)
            await _collectionsApi.ReorderCollectionItemsAsync(collectionId, currentIds);

        _originalManualItemIds.Clear();
        foreach (var id in currentIds) _originalManualItemIds.Add(id);
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
        ErrorMessage = null;
        var deleted = false;
        try
        {
            await _collectionsApi.DeleteCollectionImageAsync(CollectionId);
            deleted = true;
            CurrentPosterUrl = null; PosterFileBytes = null; PosterFileName = null;
            // The refreshed viewer read may supply generated artwork rather
            // than leaving the editor permanently blank after poster removal.
            CurrentPosterUrl = (await _collectionsApi.GetCollectionAsync(CollectionId)).PosterUrl;
            PosterFileBytes = null;
            PosterFileName = null;
        }
        catch (Exception ex) { ErrorMessage = deleted ? $"Poster removed; generated artwork could not be refreshed: {ex.Message}" : $"Failed to remove poster: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!RuleDefinition.Groups.Any(group => group.Rules.Count > 0))
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
        if (RuleDefinition.Groups.Count == 0) RuleDefinition.Groups.Add(new());
        var rule = new QueryRule
        {
            Field = "genre",
            Op = "is",
            Value = ""
        };
        RuleDefinition.Groups[0].Rules.Add(rule); Rules.Add(rule);
    }

    [RelayCommand]
    private void RemoveRule(QueryRule rule)
    {
        Rules.Remove(rule);
        foreach (var group in RuleDefinition.Groups) group.Rules.Remove(rule);
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
    private async Task AddManualItemAsync(MediaItem item)
    {
        if (IsReadOnly || IsManualMutationPending || ManualItems.Any(m => m.MediaItemId == item.ContentId || m.ContentId == item.ContentId)) return;
        IsManualMutationPending = true; ErrorMessage = null;
        try
        {
            if (IsEditing && !string.IsNullOrWhiteSpace(CollectionId)) await _collectionsApi.AddCollectionItemAsync(CollectionId, item.ContentId);
            ManualItems.Add(new CollectionItem { MediaItemId = item.ContentId, ContentId = item.ContentId, Title = item.Title, Year = item.Year, Type = item.Type, PosterUrl = item.PosterUrl, Position = ManualItems.Count });
            if (IsEditing) _originalManualItemIds.Add(item.ContentId);
            await RefreshManualOrderEligibilityAsync();
        }
        catch (Exception ex) { ErrorMessage = $"Could not add item: {ex.Message}"; }
        finally { IsManualMutationPending = false; }
    }

    [RelayCommand]
    private async Task RemoveManualItemAsync(CollectionItem item)
    {
        if (IsReadOnly || IsManualMutationPending) return;
        IsManualMutationPending = true; ErrorMessage = null;
        try
        {
            if (IsEditing && !string.IsNullOrWhiteSpace(CollectionId)) await _collectionsApi.RemoveCollectionItemAsync(CollectionId, item.MediaItemId);
            ManualItems.Remove(item); _originalManualItemIds.Remove(item.MediaItemId);
            await RefreshManualOrderEligibilityAsync();
        }
        catch (Exception ex) { ErrorMessage = $"Could not remove item: {ex.Message}"; }
        finally { IsManualMutationPending = false; }
    }

    private async Task RefreshManualOrderEligibilityAsync()
    {
        CanReorderManualItems = false;
        if (IsEditing && !IsReadOnly && _itemReorderSupported && CollectionId != null)
            CanReorderManualItems = await _collectionsApi.IsCompleteItemOrderAsync(CollectionId, ManualItems.Select(item => item.MediaItemId).ToList());
    }

    public async Task MoveManualItemAsync(int oldIndex, int newIndex)
    {
        if (IsReadOnly || IsManualMutationPending || !CanReorderManualItems || CollectionId == null || oldIndex < 0 || newIndex < 0 || oldIndex >= ManualItems.Count || newIndex >= ManualItems.Count) return;
        IsManualMutationPending = true; ErrorMessage = null;
        var ordered = ManualItems.Select(item => item.MediaItemId).ToList();
        var id = ordered[oldIndex]; ordered.RemoveAt(oldIndex); ordered.Insert(newIndex, id);
        try { await _collectionsApi.ReorderCollectionItemsAsync(CollectionId, ordered); ManualItems.Move(oldIndex, newIndex); await RefreshManualOrderEligibilityAsync(); }
        catch (Exception ex) { CanReorderManualItems = false; ErrorMessage = $"Could not reorder items. Reload the collection and try again: {ex.Message}"; }
        finally { IsManualMutationPending = false; }
    }

    [RelayCommand] private Task MoveItemUpAsync(CollectionItem item) => MoveManualItemAsync(ManualItems.IndexOf(item), ManualItems.IndexOf(item) - 1);
    [RelayCommand] private Task MoveItemDownAsync(CollectionItem item) => MoveManualItemAsync(ManualItems.IndexOf(item), ManualItems.IndexOf(item) + 1);

    private QueryDefinition BuildQueryDefinition()
    {
        return SiloPlayer.Core.Services.QueryEditing.Clone(RuleDefinition);
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
        => CollectionType is "mdblist" or "tmdb" or "tmdb_list" or "trakt";

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
        if (string.IsNullOrWhiteSpace(collection.LastSyncAt)) return "Not yet synced";
        var status = string.IsNullOrWhiteSpace(collection.LastSyncStatus) ? "Last synced" : collection.LastSyncStatus;
        return string.IsNullOrWhiteSpace(collection.LastSyncMessage)
            ? $"{status} · {collection.LastSyncAt}"
            : $"{status} · {collection.LastSyncMessage}";
    }

    private static string BuildSourcePresetSummary(Collection collection)
    {
        var preset = ReadSourceConfigValue(collection.SourceConfig, "preset");
        var mediaType = ReadSourceConfigValue(collection.SourceConfig, "media_type");
        var timeWindow = ReadSourceConfigValue(collection.SourceConfig, "time_window");
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(preset))
            parts.Add(System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(preset.Replace('_', ' ')));
        if (!string.IsNullOrWhiteSpace(mediaType))
            parts.Add(mediaType switch { "all" => "Movies + TV", "movie" => "Movies", "tv" or "series" => "TV", _ => mediaType });
        if (!string.IsNullOrWhiteSpace(timeWindow))
            parts.Add(timeWindow switch { "day" => "this day", "week" => "this week", _ => timeWindow });
        return parts.Count > 0 ? string.Join(" · ", parts) : collection.CollectionType switch { "tmdb" => "TMDB", "trakt" => "Trakt", "mdblist" => "MDBList", _ => collection.Name };
    }

    private static string FormatCreatedAt(string? value)
        => DateTimeOffset.TryParse(value, out var created)
            ? SiloPlayer.Helpers.DateTimeDisplay.FormatDate(created.ToLocalTime())
            : "—";

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
