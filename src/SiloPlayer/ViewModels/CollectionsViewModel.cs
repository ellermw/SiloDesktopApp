using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public partial class CollectionsViewModel : ObservableObject
{
    private readonly CollectionsApi _collectionsApi;
    private readonly CatalogApi _catalogApi;
    private bool _loadInProgress;
    private long _loadGeneration;
    private ApiRequestContext? _loadContext;
    private ApiRequestContext? _displayContext;
    private Task<CollectionsApi.PersonalOrderSnapshot>? _dragSnapshot;
    [ObservableProperty] private bool _isCollectionMutationPending;
    private readonly AuthApi? _authApi;
    private readonly SiloApiClient? _requestClient;
    public ObservableCollection<Profile> Profiles { get; } = [];
    public CollectionCapabilitiesResponse? Capabilities { get; private set; }

    public CollectionsViewModel(CollectionsApi collectionsApi, CatalogApi catalogApi, AuthApi? authApi = null, SiloApiClient? requestClient = null)
    {
        _collectionsApi = collectionsApi;
        _catalogApi = catalogApi;
        _authApi = authApi; _requestClient = requestClient;
    }

    public ObservableCollection<Collection> Collections { get; } = [];

    public ObservableCollection<CollectionGroup> Groups { get; } = [];

    public ObservableCollection<ServerCollectionsLibrary> ServerLibraries { get; } = [];

    public ObservableCollection<CollectionTemplateCategory> TemplateGroups { get; } = [];

    public ObservableCollection<Library> Libraries { get; } = [];

    public ObservableCollection<MDBListListSummary> MdblistResults { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingServerCollections;

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

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadCollectionsAsync()
    {
        var context = _requestClient?.CaptureContext();
        if (_loadInProgress && _loadContext == context) return;
        var generation = ++_loadGeneration;
        bool Current() => generation == _loadGeneration && (context == null || _requestClient!.IsCurrentContext(context.Value));
        if (_displayContext != context)
        {
            Collections.Clear(); Groups.Clear(); ServerLibraries.Clear(); Profiles.Clear();
            Capabilities = null; OnPropertyChanged(nameof(Capabilities));
            _displayContext = context;
        }
        _loadContext = context;

        _loadInProgress = true;
        // Preserve the previous page while refreshing. Skeletons are useful
        // only for the first visit; hiding populated collections on every
        // navigation makes the route look as though it was torn down.
        IsLoading = Collections.Count == 0 && Groups.Count == 0;
        IsLoadingServerCollections = ServerLibraries.Count == 0;
        ErrorMessage = null;
        Task<ServerCollectionsResponse>? serverCollectionsTask = null;
        var referenceTask = LoadViewerReferencesAsync();

        try
        {
            var collectionsTask = _collectionsApi.GetCollectionsAsync();
            serverCollectionsTask = _collectionsApi.GetServerCollectionsAsync();
            var response = await collectionsTask;
            if (Current())
            {
                Collections.Clear();
                foreach (var c in response.Collections) Collections.Add(c);
                Groups.Clear();
                foreach (var group in response.Groups.OrderBy(group => group.SortOrder)) Groups.Add(group);
                IsEmpty = Collections.Count == 0;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (Current()) ErrorMessage = $"Failed to load collections: {ex.Message}";
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (Current()) IsLoading = false;
        }

        try
        {
            if (serverCollectionsTask != null)
            {
                var serverResponse = await serverCollectionsTask;
                if (Current())
                {
                    ServerLibraries.Clear();
                    foreach (var library in serverResponse.Libraries) ServerLibraries.Add(library);
                }
            }
        }
        catch
        {
            // Server collections are an independent, optional surface. A
            // failure here must not hide the user's editable collections.
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                IsLoadingServerCollections = false;
                IsLoading = false;
                _loadInProgress = false;
            }
        }
        await referenceTask;
    }

    private async Task LoadViewerReferencesAsync()
    {
        var context = _requestClient?.CaptureContext();
        bool Current() => context == null || _requestClient!.IsCurrentContext(context.Value);
        async Task ProfilesAsync()
        {
            if (_authApi == null) return;
            try { var response = await _authApi.GetProfilesAsync(); if (!Current()) return; Profiles.Clear(); foreach (var profile in response.Profiles) Profiles.Add(profile); }
            catch { /* Optional profile names must not hide the collection list. */ }
        }
        async Task CapabilitiesAsync()
        {
            try { var response = await _collectionsApi.GetCollectionCapabilitiesAsync(); if (!Current()) return; Capabilities = response; OnPropertyChanged(nameof(Capabilities)); }
            catch { if (Current()) { Capabilities = null; OnPropertyChanged(nameof(Capabilities)); } }
        }
        await Task.WhenAll(ProfilesAsync(), CapabilitiesAsync());
    }

    public async Task<bool> CreateGroupAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            await _collectionsApi.CreateCollectionGroupAsync(name.Trim(), Slugify(name));
            await LoadCollectionsAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to add group: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> RenameGroupAsync(CollectionGroup group, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            await _collectionsApi.UpdateCollectionGroupAsync(group.Id, name.Trim());
            await LoadCollectionsAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to rename group: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> DeleteGroupAsync(CollectionGroup group)
    {
        try
        {
            await _collectionsApi.DeleteCollectionGroupAsync(group.Id);
            await LoadCollectionsAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete group: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> MoveCollectionToGroupAsync(Collection collection, string? groupId)
    {
        try
        {
            await _collectionsApi.MoveCollectionToGroupAsync(collection.Id, groupId);
            await LoadCollectionsAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to move collection: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> MoveCollectionAsync(Collection collection, int offset)
    {
        var scope = Collections
            .Where(item => string.Equals(item.GroupId, collection.GroupId, StringComparison.Ordinal))
            .OrderBy(item => item.SortOrder)
            .ToList();
        var index = scope.FindIndex(item => item.Id == collection.Id);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= scope.Count) return false;

        (scope[index], scope[target]) = (scope[target], scope[index]);
        try
        {
            await _collectionsApi.ReorderCollectionsAsync(scope.Select(item => item.Id).ToList(), collection.GroupId);
            await LoadCollectionsAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to reorder collections: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> DropCollectionAsync(string sourceId, string? targetId, string? targetGroupId)
    {
        if (IsCollectionMutationPending || Capabilities?.ItemReorder != true || _requestClient == null) return false;
        var scope = Collections.Where(item => PersonalCollectionOwnership.IsOwn(item, _requestClient.ProfileId)).ToList();
        var source = scope.FirstOrDefault(item => item.Id == sourceId);
        if (source == null) return false;
        var oldIndex = scope.FindIndex(item => item.Id == sourceId);
        var newIndex = targetId == null
            ? scope.Count - 1
            : scope.FindIndex(item => item.Id == targetId);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return false;

        scope.RemoveAt(oldIndex);
        scope.Insert(Math.Min(newIndex, scope.Count), source);
        IsCollectionMutationPending = true;
        try
        {
            var snapshot = _dragSnapshot == null ? await _collectionsApi.PreparePersonalOrderAsync(Collections.Where(item => PersonalCollectionOwnership.IsOwn(item, _requestClient.ProfileId)).Select(item => item.Id).ToArray()) : await _dragSnapshot;
            await _collectionsApi.ReorderPersonalCollectionsAsync(snapshot, scope.Select(item => item.Id).ToList());
            await LoadCollectionsAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to reorder collections: {ex.Message}";
            return false;
        }
        finally { _dragSnapshot = null; IsCollectionMutationPending = false; }
    }

    public Task BeginCollectionDragAsync()
    {
        if (Capabilities?.ItemReorder != true || _requestClient == null) return Task.CompletedTask;
        _dragSnapshot = _collectionsApi.PreparePersonalOrderAsync(Collections.Where(item => PersonalCollectionOwnership.IsOwn(item, _requestClient.ProfileId)).Select(item => item.Id).ToArray());
        // A canceled drag has no drop consumer. Observe its failed snapshot.
        return ObserveAsync(_dragSnapshot);
        static async Task ObserveAsync(Task snapshot) { try { await snapshot; } catch { } }
    }
    public void CancelCollectionDrag() => _dragSnapshot = null;

    public async Task<bool> SetSharedAsync(Collection collection, bool shared)
    {
        if (IsCollectionMutationPending || _requestClient == null || !PersonalCollectionOwnership.IsOwn(collection, _requestClient.ProfileId)) return false;
        IsCollectionMutationPending = true; ErrorMessage = null;
        try
        {
            var updated = await _collectionsApi.UpdateCollectionAsync(collection.Id, new() { IsShared = shared });
            UpsertCollection(updated); return true;
        }
        catch (Exception ex) { ErrorMessage = $"Could not change sharing: {ex.Message}"; return false; }
        finally { IsCollectionMutationPending = false; }
    }

    public async Task<bool> MoveGroupAsync(CollectionGroup group, int offset)
    {
        var ordered = Groups.OrderBy(item => item.SortOrder).ToList();
        var index = ordered.FindIndex(item => item.Id == group.Id);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= ordered.Count) return false;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        try
        {
            await _collectionsApi.ReorderCollectionGroupsAsync(ordered.Select(item => item.Id).ToList());
            await LoadCollectionsAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to reorder groups: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> DropGroupAsync(string sourceId, string targetId)
    {
        var ordered = Groups.OrderBy(item => item.SortOrder).ToList();
        var oldIndex = ordered.FindIndex(item => item.Id == sourceId);
        var newIndex = ordered.FindIndex(item => item.Id == targetId);
        if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex) return false;

        var source = ordered[oldIndex];
        ordered.RemoveAt(oldIndex);
        ordered.Insert(Math.Min(newIndex, ordered.Count), source);
        try
        {
            await _collectionsApi.ReorderCollectionGroupsAsync(ordered.Select(item => item.Id).ToList());
            await LoadCollectionsAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to reorder collection groups: {ex.Message}";
            return false;
        }
    }

    private static string Slugify(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
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
        if (IsCollectionMutationPending || (_requestClient != null && !Collections.Any(item => item.Id == id && PersonalCollectionOwnership.IsOwn(item, _requestClient.ProfileId)))) return;
        IsCollectionMutationPending = true;
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
        finally { IsCollectionMutationPending = false; }
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
            var capabilitiesTask = _collectionsApi.GetCollectionCapabilitiesAsync();

            await Task.WhenAll(catalogTask, librariesTask, capabilitiesTask);

            TemplateGroups.Clear();
            foreach (var group in catalogTask.Result.Categories)
            {
                var supported = group.Templates.Where(template => CollectionImportPolicy.CanCreateTemplate(capabilitiesTask.Result, template)).ToList();
                if (supported.Count > 0) TemplateGroups.Add(new() { Category = group.Category, Label = group.Label, Templates = supported });
            }

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

    private int _mdblistSearchGeneration;

    public async Task SearchMDBListAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        var generation = ++_mdblistSearchGeneration;
        IsSearchingMdblist = true;
        TemplateErrorMessage = null;

        try
        {
            var response = await _collectionsApi.SearchMDBListAsync(query.Trim());
            if (generation != _mdblistSearchGeneration) return;
            IsMDBListConfigured = response.Configured;
            MdblistResults.Clear();
            foreach (var list in response.Lists)
                MdblistResults.Add(list);
        }
        catch (Exception ex)
        {
            if (generation == _mdblistSearchGeneration) TemplateErrorMessage = $"MDBList search failed: {ex.Message}";
        }
        finally
        {
            if (generation == _mdblistSearchGeneration) IsSearchingMdblist = false;
        }
    }

    public async Task LoadTopMDBListAsync()
    {
        var generation = ++_mdblistSearchGeneration;
        IsSearchingMdblist = true;
        TemplateErrorMessage = null;

        try
        {
            var response = await _collectionsApi.GetTopMDBListAsync();
            if (generation != _mdblistSearchGeneration) return;
            IsMDBListConfigured = response.Configured;
            MdblistResults.Clear();
            foreach (var list in response.Lists)
                MdblistResults.Add(list);
        }
        catch (Exception ex)
        {
            if (generation == _mdblistSearchGeneration) TemplateErrorMessage = $"MDBList top lists failed: {ex.Message}";
        }
        finally
        {
            if (generation == _mdblistSearchGeneration) IsSearchingMdblist = false;
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
            var capabilities = await _collectionsApi.GetCollectionCapabilitiesAsync();
            if (!CollectionImportPolicy.CanCreateTemplate(capabilities, template))
            {
                TemplateErrorMessage = "This source is no longer available for new collections. Return to templates to choose a supported source. Existing collections can still sync.";
                return null;
            }
            var request = CreateBaseImportRequest<ImportUserCollectionRequest>(draft);
            ImportUserCollectionResponse response;

            switch (template.Source)
            {
                case "tmdb_list":
                    var listUrl = draft.TMDBListUrl?.Trim() ?? template.TmdbList?.Url;
                    if (!CollectionImportPolicy.IsTMDBListUrl(listUrl)) { TemplateErrorMessage = "Enter a public TMDB list URL or numeric list ID."; return null; }
                    response = await _collectionsApi.ImportUserTMDBListCollectionAsync(new()
                    {
                        Title = request.Title, Description = request.Description, Limit = request.Limit, SyncSchedule = request.SyncSchedule,
                        IsShared = request.IsShared, LibraryIds = request.LibraryIds, PosterUrl = request.PosterUrl,
                        DisplayQueryDefinition = request.DisplayQueryDefinition, SortConfig = request.SortConfig, Url = listUrl!
                    });
                    break;
                case "mdblist":
                    var mdblistUrl = !string.IsNullOrWhiteSpace(draft.MDBListUrl)
                        ? draft.MDBListUrl.Trim()
                        : template.Mdblist?.Url;

                    if (!CollectionImportPolicy.IsMDBListUrl(mdblistUrl))
                    {
                        TemplateErrorMessage = "Enter an MDBList list link, for example https://mdblist.com/lists/name/list.";
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
                        PosterUrl = request.PosterUrl,
                        DisplayQueryDefinition = request.DisplayQueryDefinition,
                        SortConfig = request.SortConfig,
                        Url = CollectionImportPolicy.CleanMDBListLink(mdblistUrl)
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
                        PosterUrl = request.PosterUrl,
                        DisplayQueryDefinition = request.DisplayQueryDefinition,
                        SortConfig = request.SortConfig,
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
                        PosterUrl = request.PosterUrl,
                        DisplayQueryDefinition = request.DisplayQueryDefinition,
                        SortConfig = request.SortConfig,
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
            LibraryIds = draft.LibraryIds.Count == 0 ? null : [.. draft.LibraryIds],
            PosterUrl = string.IsNullOrWhiteSpace(draft.PosterUrl) ? null : draft.PosterUrl.Trim(),
            DisplayQueryDefinition = draft.DisplayQueryDefinition,
            SortConfig = draft.SortConfig,
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
    public string? TMDBListUrl { get; set; }
    public string? PosterUrl { get; set; }
    public DisplayQueryDefinition? DisplayQueryDefinition { get; set; }
    public Dictionary<string, object>? SortConfig { get; set; }
}
