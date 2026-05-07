using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Collections;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public partial class CollectionEditorViewModel : ObservableObject
{
    private readonly CollectionsApi _collectionsApi;
    private readonly CatalogApi _catalogApi;

    public CollectionEditorViewModel(CollectionsApi collectionsApi, CatalogApi catalogApi)
    {
        _collectionsApi = collectionsApi;
        _catalogApi = catalogApi;
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

    [RelayCommand]
    private async Task LoadExistingAsync(string collectionId)
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // Load collection details -- the GetCollections response includes full data
            var response = await _collectionsApi.GetCollectionsAsync();
            var collection = response.Collections.FirstOrDefault(c => c.Id == collectionId);
            if (collection == null)
            {
                ErrorMessage = "Collection not found.";
                return;
            }

            IsEditing = true;
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
                    Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    IsShared = IsShared
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
                    request.PosterSourceUrl = string.IsNullOrWhiteSpace(PosterSourceUrl) ? null : PosterSourceUrl.Trim();
                }

                await _collectionsApi.UpdateCollectionAsync(CollectionId, request);
            }
            else
            {
                var request = new CreateCollectionRequest
                {
                    Name = Name.Trim(),
                    Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    CollectionType = CollectionType,
                    IsShared = IsShared
                };

                if (CollectionType == "smart" && Rules.Count > 0)
                {
                    request.QueryDefinition = BuildQueryDefinition();
                }

                var created = await _collectionsApi.CreateCollectionAsync(request);

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
