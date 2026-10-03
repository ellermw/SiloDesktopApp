using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public sealed record SmartCollectionWizardNavigationArgs(
    int? LibraryId = null,
    string? CollectionId = null);

public partial class SmartCollectionWizardViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> MediaScopes =
        ["", "video", "movie", "series", "episode", "audiobook", "ebook", "manga"];

    private readonly CatalogApi _catalogApi;
    private readonly CollectionsApi _collectionsApi;
    private readonly AuthApi _authApi;
    private readonly AuthService _authService;
    private string? _collectionId;

    public SmartCollectionWizardViewModel(
        CatalogApi catalogApi,
        CollectionsApi collectionsApi,
        AuthApi authApi,
        AuthService authService)
    {
        _catalogApi = catalogApi;
        _collectionsApi = collectionsApi;
        _authApi = authApi;
        _authService = authService;
    }

    public ObservableCollection<Library> Libraries { get; } = [];
    public ObservableCollection<int> SelectedLibraryIds { get; } = [];
    public ObservableCollection<QueryRule> Rules { get; } = [];
    public QueryDefinition RuleDefinition { get; private set; } = new();
    public ObservableCollection<CollectionPreviewItem> PreviewItems { get; } = [];
    public ObservableCollection<MediaItem> PreviewMediaItems { get; } = [];
    public ObservableCollection<Profile> Profiles { get; } = [];
    public ObservableCollection<string> AllowedProfileIds { get; } = [];
    private QueryDefinition? _previewQuery;
    private string? _previewSnapshot;
    private int _previewGeneration;
    private bool _failedPreviewContinuation;
    [ObservableProperty] private bool _previewHasMore;

    public async Task LoadMorePreviewAsync(CancellationToken ct = default)
    {
        if (IsPreviewing || !PreviewHasMore || _previewQuery == null) return;
        await LoadPreviewWindowAsync(_previewQuery, reset: false, _previewGeneration, ct);
    }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isPreviewing;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string _mediaScope = "";
    [ObservableProperty] private string _matchMode = "all";
    [ObservableProperty] private string _sortField = "added_at";
    [ObservableProperty] private string _sortOrder = "desc";
    [ObservableProperty] private string? _limitText = "100";
    [ObservableProperty] private bool _isShared;
    [ObservableProperty] private bool _includeInServerCollections = true;
    [ObservableProperty] private string? _posterSourceUrl;
    [ObservableProperty] private string? _currentPosterUrl;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private int _previewTotal;

    public byte[]? PosterFileBytes { get; private set; }
    public string? PosterFileName { get; private set; }
    public string PosterContentType { get; private set; } = "image/jpeg";
    public event Action? Saved;

    public async Task ConfigureAsync(SmartCollectionWizardNavigationArgs? args, CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        _collectionId = args?.CollectionId;

        try
        {
            Libraries.Clear();
            var librariesTask = _catalogApi.GetLibrariesAsync(ct);
            var profilesTask = _authApi.GetProfilesAsync(ct);
            await Task.WhenAll(librariesTask, profilesTask);
            var libraries = librariesTask.Result;
            foreach (var library in libraries)
                Libraries.Add(library);

            Profiles.Clear();
            foreach (var profile in profilesTask.Result.Profiles)
                Profiles.Add(profile);
            AllowedProfileIds.Clear();
            IsReadOnly = false;
            CurrentPosterUrl = null;
            PosterFileBytes = null;
            PosterFileName = null;

            SelectedLibraryIds.Clear();
            if (args?.LibraryId is int libraryId)
                SelectedLibraryIds.Add(libraryId);

            if (string.IsNullOrWhiteSpace(_collectionId))
            {
                Title = "";
                Description = null;
                MediaScope = "";
                MatchMode = "all";
                SortField = "added_at";
                SortOrder = "desc";
                LimitText = "100";
                IsShared = false;
                IncludeInServerCollections = true;
                PosterSourceUrl = null;
                Rules.Clear();
                RuleDefinition = new();
            }

            if (!string.IsNullOrWhiteSpace(_collectionId))
            {
                var collection = await _collectionsApi.GetCollectionAsync(_collectionId, ct);
                if (collection == null)
                    throw new InvalidOperationException("The selected collection could not be loaded.");

                Title = collection.Name;
                Description = collection.Description;
                RuleDefinition = QueryEditing.Clone(collection.QueryDefinition);
                MediaScope = collection.QueryDefinition?.MediaScope ?? "";
                MatchMode = collection.QueryDefinition?.Match ?? "all";
                SortField = collection.QueryDefinition?.Sort?.Field ?? "added_at";
                SortOrder = collection.QueryDefinition?.Sort?.Order ?? "desc";
                LimitText = collection.QueryDefinition?.Limit?.ToString();
                IsShared = collection.IsShared;
                IsReadOnly = !string.IsNullOrWhiteSpace(_authService.SelectedProfileId)
                    && !string.Equals(collection.CreatorProfileId, _authService.SelectedProfileId, StringComparison.Ordinal);
                IncludeInServerCollections = collection.IncludeInServerCollections;
                CurrentPosterUrl = collection.PosterUrl;
                foreach (var profileId in collection.AllowedProfileIds)
                    AllowedProfileIds.Add(profileId);
                SelectedLibraryIds.Clear();
                foreach (var id in collection.QueryDefinition?.LibraryIds ?? [])
                    SelectedLibraryIds.Add(id);
                Rules.Clear();
                foreach (var rule in RuleDefinition.Groups.FirstOrDefault()?.Rules ?? [])
                    Rules.Add(rule);
            }

            if (Rules.Count == 0)
                AddRule();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load wizard data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SetPosterFile(string fileName, byte[] bytes, string? contentType)
    {
        PosterFileName = fileName;
        PosterFileBytes = bytes;
        PosterContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        PosterSourceUrl = "";
    }

    public void ClearPosterFile()
    {
        PosterFileName = null;
        PosterFileBytes = null;
    }

    public async Task DeletePosterAsync(CancellationToken ct = default)
    {
        if (IsReadOnly || IsSaving || string.IsNullOrWhiteSpace(_collectionId)) return;
        IsSaving = true; ErrorMessage = null;
        try
        {
            await _collectionsApi.DeleteCollectionImageAsync(_collectionId, ct);
            CurrentPosterUrl = null;
            ClearPosterFile();
            CurrentPosterUrl = (await _collectionsApi.GetCollectionAsync(_collectionId, ct)).PosterUrl;
        }
        catch (Exception ex) { ErrorMessage = $"Failed to refresh collection artwork: {ex.Message}"; }
        finally { IsSaving = false; }
    }

    public void AddRule(string field = "genre", string op = "contains", object? value = null)
    {
        if (RuleDefinition.Groups.Count == 0) RuleDefinition.Groups.Add(new());
        var rule = new QueryRule { Field = field, Op = op, Value = value ?? "" };
        RuleDefinition.Groups[0].Rules.Add(rule); Rules.Add(rule);
    }

    public void RemoveRule(QueryRule rule)
    {
        if (Rules.Count <= 1)
        {
            rule.Field = "genre";
            rule.Op = "contains";
            rule.Value = "";
            return;
        }

        Rules.Remove(rule);
        foreach (var group in RuleDefinition.Groups) group.Rules.Remove(rule);
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        var generation = ++_previewGeneration;
        _previewQuery = BuildQueryDefinition();
        _previewSnapshot = null;
        PreviewHasMore = false;
        PreviewItems.Clear(); PreviewMediaItems.Clear();
        await LoadPreviewWindowAsync(_previewQuery, reset: true, generation, ct);
    }

    public Task RetryPreviewAsync(CancellationToken ct = default)
    {
        // Only an unchanged failed continuation owns the retained window.
        // First-load failures and edited filters start a fresh preview.
        if (_failedPreviewContinuation && _previewQuery != null
            && PreviewMediaItems.Count > 0 && PreviewHasMore
            && JsonSerializer.Serialize(_previewQuery) == JsonSerializer.Serialize(BuildQueryDefinition()))
            return LoadMorePreviewAsync(ct);
        return PreviewAsync(ct);
    }

    private async Task LoadPreviewWindowAsync(QueryDefinition query, bool reset, int generation, CancellationToken ct)
    {
        IsPreviewing = true; ErrorMessage = null; _failedPreviewContinuation = false;
        var offset = PreviewMediaItems.Count;
        var remaining = (query.Limit ?? int.MaxValue) - offset;
        if (remaining <= 0) { PreviewHasMore = false; IsPreviewing = false; return; }
        try
        {
            var response = await _catalogApi.GetCatalogAsync(
                libraryId: query.LibraryIds.FirstOrDefault() is > 0 ? query.LibraryIds[0] : null,
                sort: query.Sort?.Field, order: query.Sort?.Order,
                type: string.IsNullOrWhiteSpace(query.MediaScope) ? null : query.MediaScope,
                limit: Math.Min(100, remaining), offset: offset, includeTotal: reset,
                snapshot: _previewSnapshot, source: "query", queryGroups: query.Groups,
                queryGroupsMatch: query.Match, queryLimit: query.Limit, ct: ct);
            if (ct.IsCancellationRequested || generation != _previewGeneration) return;
            if (reset) PreviewTotal = query.Limit is > 0 ? Math.Min(response.Total, query.Limit.Value) : response.Total;
            _previewSnapshot ??= response.Snapshot;
            var knownIds = PreviewMediaItems.Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
            foreach (var item in response.Items.Take(remaining)) if (knownIds.Add(item.ContentId)) PreviewMediaItems.Add(item);
            PreviewHasMore = response.Items.Count > 0 && PreviewMediaItems.Count < (query.Limit ?? int.MaxValue)
                && (response.HasMore || PreviewMediaItems.Count < PreviewTotal);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (generation == _previewGeneration)
            {
                _failedPreviewContinuation = !reset && PreviewMediaItems.Count > 0 && PreviewHasMore;
                ErrorMessage = $"Preview failed: {ex.Message}";
            }
        }
        finally { if (generation == _previewGeneration) IsPreviewing = false; }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (IsReadOnly)
        {
            ErrorMessage = "Only the profile that created this collection can edit it.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Collection title is required.";
            return;
        }

        IsSaving = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var query = BuildQueryDefinition();
            var request = new CreateCollectionRequest
            {
                Name = Title.Trim(),
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                CollectionType = "smart",
                IsShared = IsShared,
                AllowedProfileIds = IsShared ? [.. AllowedProfileIds] : [],
                IncludeInServerCollections = IncludeInServerCollections,
                PosterSourceUrl = string.IsNullOrWhiteSpace(PosterSourceUrl) ? null : PosterSourceUrl.Trim(),
                QueryDefinition = query
            };
            if (string.IsNullOrWhiteSpace(_collectionId))
            {
                if (PosterFileBytes is { Length: > 0 } poster && !string.IsNullOrWhiteSpace(PosterFileName))
                    await _collectionsApi.CreateCollectionAsync(request, PosterFileName, poster, PosterContentType, ct);
                else
                    await _collectionsApi.CreateCollectionAsync(request, ct);
            }
            else
            {
                var update = new UpdateCollectionRequest
                {
                    Name = Title.Trim(),
                    Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    IsShared = IsShared,
                    AllowedProfileIds = IsShared ? [.. AllowedProfileIds] : [],
                    IncludeInServerCollections = IncludeInServerCollections,
                    PosterSourceUrl = string.IsNullOrWhiteSpace(PosterSourceUrl) ? null : PosterSourceUrl.Trim(),
                    QueryDefinition = query
                };
                if (PosterFileBytes is { Length: > 0 } poster && !string.IsNullOrWhiteSpace(PosterFileName))
                    await _collectionsApi.UpdateCollectionAsync(_collectionId, update, PosterFileName, poster, PosterContentType, ct);
                else
                    await _collectionsApi.UpdateCollectionAsync(_collectionId, update, ct);
            }

            StatusMessage = "Smart collection saved.";
            PosterFileBytes = null;
            PosterFileName = null;
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save smart collection: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    public QueryDefinition BuildQueryDefinition()
    {
        var query = QueryEditing.Clone(RuleDefinition);
        query.LibraryIds = [.. SelectedLibraryIds];
        query.MediaScope = string.IsNullOrWhiteSpace(MediaScope) ? null : MediaScope;
        query.Match = MatchMode;
        query.Groups = QueryEditing.PopulatedGroups(query);
        query.Sort = new QuerySort { Field = SortField, Order = SortOrder };
        query.Limit = int.TryParse(LimitText, out var limit) ? Math.Clamp(limit, 1, 500) : null;
        return query;
    }


}
