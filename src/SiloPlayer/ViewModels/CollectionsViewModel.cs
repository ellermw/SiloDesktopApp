using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.ViewModels;

public partial class CollectionsViewModel : ObservableObject
{
    private readonly CollectionsApi _collectionsApi;
    private readonly CatalogApi _catalogApi;

    public CollectionsViewModel(CollectionsApi collectionsApi, CatalogApi catalogApi)
    {
        _collectionsApi = collectionsApi;
        _catalogApi = catalogApi;
    }

    public ObservableCollection<Collection> Collections { get; } = [];

    public ObservableCollection<CollectionTemplateCategory> TemplateGroups { get; } = [];

    public ObservableCollection<Library> Libraries { get; } = [];

    public ObservableCollection<MDBListListSummary> MdblistResults { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoadingTemplateFlow;

    [ObservableProperty]
    private bool _isSearchingMdblist;

    [ObservableProperty]
    private bool _isImportingTemplate;

    [ObservableProperty]
    private bool _isMDBListConfigured = true;

    [ObservableProperty]
    private string? _templateErrorMessage;

    [ObservableProperty]
    private string? _lastImportMessage;

    [RelayCommand]
    private async Task LoadCollectionsAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var response = await _collectionsApi.GetCollectionsAsync();
            Collections.Clear();
            foreach (var c in response.Collections)
                Collections.Add(c);
            IsEmpty = Collections.Count == 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load collections: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync(string id)
    {
        try
        {
            await _collectionsApi.DeleteCollectionAsync(id);
            // Remove from local list
            var item = Collections.FirstOrDefault(c => c.Id == id);
            if (item != null)
                Collections.Remove(item);
            IsEmpty = Collections.Count == 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete collection: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SyncCollectionAsync(string id)
    {
        try
        {
            var result = await _collectionsApi.SyncCollectionAsync(id);
            var response = await _collectionsApi.GetCollectionsAsync();
            var updated = response.Collections.FirstOrDefault(c => c.Id == id);
            var existing = Collections.FirstOrDefault(c => c.Id == id);
            if (updated != null && existing != null)
            {
                var index = Collections.IndexOf(existing);
                Collections[index] = updated;
            }

            ErrorMessage = result.Status == "failed"
                ? $"Collection sync failed: {result.Message}"
                : null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to sync collection: {ex.Message}";
        }
    }

    public async Task LoadTemplateFlowAsync()
    {
        if (IsLoadingTemplateFlow) return;

        IsLoadingTemplateFlow = true;
        TemplateErrorMessage = null;
        LastImportMessage = null;

        try
        {
            var catalogTask = _collectionsApi.GetCollectionTemplatesAsync();
            var librariesTask = _catalogApi.GetLibrariesAsync();

            await Task.WhenAll(catalogTask, librariesTask);

            TemplateGroups.Clear();
            foreach (var group in catalogTask.Result.Categories)
                TemplateGroups.Add(group);

            Libraries.Clear();
            foreach (var library in librariesTask.Result)
                Libraries.Add(library);
        }
        catch (Exception ex)
        {
            TemplateErrorMessage = $"Failed to load collection templates: {ex.Message}";
        }
        finally
        {
            IsLoadingTemplateFlow = false;
        }
    }

    public async Task SearchMDBListAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        IsSearchingMdblist = true;
        TemplateErrorMessage = null;

        try
        {
            var response = await _collectionsApi.SearchMDBListAsync(query.Trim());
            IsMDBListConfigured = response.Configured;
            MdblistResults.Clear();
            foreach (var list in response.Lists)
                MdblistResults.Add(list);
        }
        catch (Exception ex)
        {
            TemplateErrorMessage = $"MDBList search failed: {ex.Message}";
        }
        finally
        {
            IsSearchingMdblist = false;
        }
    }

    public async Task LoadTopMDBListAsync()
    {
        IsSearchingMdblist = true;
        TemplateErrorMessage = null;

        try
        {
            var response = await _collectionsApi.GetTopMDBListAsync();
            IsMDBListConfigured = response.Configured;
            MdblistResults.Clear();
            foreach (var list in response.Lists)
                MdblistResults.Add(list);
        }
        catch (Exception ex)
        {
            TemplateErrorMessage = $"MDBList top lists failed: {ex.Message}";
        }
        finally
        {
            IsSearchingMdblist = false;
        }
    }

    public async Task<Collection?> ImportTemplateAsync(TemplateImportDraft draft)
    {
        if (draft.Template == null)
        {
            TemplateErrorMessage = "Choose a collection template first.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            TemplateErrorMessage = "Collection title is required.";
            return null;
        }

        IsImportingTemplate = true;
        TemplateErrorMessage = null;
        LastImportMessage = null;

        try
        {
            var template = draft.Template;
            var request = CreateBaseImportRequest<ImportUserCollectionRequest>(draft);
            ImportUserCollectionResponse response;

            switch (template.Source)
            {
                case "mdblist":
                    var mdblistUrl = !string.IsNullOrWhiteSpace(draft.MDBListUrl)
                        ? draft.MDBListUrl.Trim()
                        : template.Mdblist?.Url;

                    if (string.IsNullOrWhiteSpace(mdblistUrl))
                    {
                        TemplateErrorMessage = "MDBList imports need a list URL.";
                        return null;
                    }

                    response = await _collectionsApi.ImportUserMDBListCollectionAsync(new ImportUserMDBListCollectionRequest
                    {
                        Title = request.Title,
                        Description = request.Description,
                        Limit = request.Limit,
                        SyncSchedule = request.SyncSchedule,
                        IsShared = request.IsShared,
                        LibraryIds = request.LibraryIds,
                        Url = mdblistUrl
                    });
                    break;

                case "tmdb":
                    if (template.Tmdb == null)
                    {
                        TemplateErrorMessage = "TMDB template metadata is missing.";
                        return null;
                    }

                    response = await _collectionsApi.ImportUserTMDBCollectionAsync(new ImportUserTMDBCollectionRequest
                    {
                        Title = request.Title,
                        Description = request.Description,
                        Limit = request.Limit,
                        SyncSchedule = request.SyncSchedule,
                        IsShared = request.IsShared,
                        LibraryIds = request.LibraryIds,
                        Preset = template.Tmdb.Preset,
                        MediaType = template.Tmdb.MediaType,
                        TimeWindow = template.Tmdb.TimeWindow
                    });
                    break;

                case "trakt":
                    if (template.Trakt == null)
                    {
                        TemplateErrorMessage = "Trakt template metadata is missing.";
                        return null;
                    }

                    response = await _collectionsApi.ImportUserTraktCollectionAsync(new ImportUserTraktCollectionRequest
                    {
                        Title = request.Title,
                        Description = request.Description,
                        Limit = request.Limit,
                        SyncSchedule = request.SyncSchedule,
                        IsShared = request.IsShared,
                        LibraryIds = request.LibraryIds,
                        Preset = template.Trakt.Preset,
                        MediaType = template.Trakt.MediaType
                    });
                    break;

                default:
                    TemplateErrorMessage = $"Unsupported template source: {template.Source}.";
                    return null;
            }

            UpsertCollection(response.Collection);
            IsEmpty = Collections.Count == 0;
            LastImportMessage = response.Sync == null
                ? "Collection created."
                : $"Collection created. Sync {response.Sync.Status}.";
            return response.Collection;
        }
        catch (Exception ex)
        {
            TemplateErrorMessage = $"Failed to import collection: {ex.Message}";
            return null;
        }
        finally
        {
            IsImportingTemplate = false;
        }
    }

    private static T CreateBaseImportRequest<T>(TemplateImportDraft draft)
        where T : ImportUserCollectionRequest, new()
    {
        return new T
        {
            Title = draft.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(draft.Description) ? null : draft.Description.Trim(),
            Limit = draft.MaxItems > 0 ? draft.MaxItems : null,
            SyncSchedule = string.Equals(draft.SyncSchedule, "none", StringComparison.OrdinalIgnoreCase)
                ? null
                : draft.SyncSchedule,
            IsShared = draft.IsShared,
            LibraryIds = draft.LibraryIds.Count == 0 ? null : [.. draft.LibraryIds]
        };
    }

    private void UpsertCollection(Collection collection)
    {
        if (string.IsNullOrWhiteSpace(collection.Id))
            return;

        var existing = Collections.FirstOrDefault(c => c.Id == collection.Id);
        if (existing == null)
        {
            Collections.Insert(0, collection);
            return;
        }

        var index = Collections.IndexOf(existing);
        Collections[index] = collection;
    }
}

public sealed class TemplateImportDraft
{
    public CollectionTemplate? Template { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? MaxItems { get; set; }
    public string? SyncSchedule { get; set; }
    public bool IsShared { get; set; }
    public List<int> LibraryIds { get; set; } = [];
    public string? MDBListUrl { get; set; }
}
