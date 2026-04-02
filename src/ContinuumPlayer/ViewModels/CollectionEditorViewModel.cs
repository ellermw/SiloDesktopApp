using System.Collections.ObjectModel;
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
            CollectionType = collection.CollectionType;
            IsShared = collection.IsShared;

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
                    IsShared = IsShared
                };

                if (CollectionType == "smart" && Rules.Count > 0)
                {
                    request.QueryDefinition = BuildQueryDefinition();
                }

                await _collectionsApi.UpdateCollectionAsync(CollectionId, request);
            }
            else
            {
                var request = new CreateCollectionRequest
                {
                    Name = Name.Trim(),
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
}
