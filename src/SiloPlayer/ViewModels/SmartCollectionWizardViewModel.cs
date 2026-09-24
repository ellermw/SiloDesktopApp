using System.Collections.ObjectModel;
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
    public ObservableCollection<CollectionPreviewItem> PreviewItems { get; } = [];
    public ObservableCollection<MediaItem> PreviewMediaItems { get; } = [];
    public ObservableCollection<Profile> Profiles { get; } = [];
    public ObservableCollection<string> AllowedProfileIds { get; } = [];

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
            }

            if (!string.IsNullOrWhiteSpace(_collectionId))
            {
                var collection = await _collectionsApi.GetCollectionAsync(_collectionId, ct);
                if (collection == null)
                    throw new InvalidOperationException("The selected collection could not be loaded.");

                Title = collection.Name;
                Description = collection.Description;
                MediaScope = collection.QueryDefinition?.MediaScope ?? "movie";
                MatchMode = collection.QueryDefinition?.Match ?? "all";
                SortField = collection.QueryDefinition?.Sort?.Field ?? "added_at";
                SortOrder = collection.QueryDefinition?.Sort?.Order ?? "desc";
                LimitText = collection.QueryDefinition?.Limit?.ToString() ?? "100";
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
                foreach (var rule in collection.QueryDefinition?.Groups.SelectMany(group => group.Rules) ?? [])
                    Rules.Add(new QueryRule { Field = rule.Field, Op = rule.Op, Value = rule.Value });
            }

            if (Rules.Count == 0)
                Rules.Add(new QueryRule { Field = "genre", Op = "contains", Value = "" });
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

    public void AddRule(string field = "genre", string op = "contains", object? value = null)
        => Rules.Add(new QueryRule { Field = field, Op = op, Value = value ?? "" });

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
    }

    public async Task PreviewAsync(CancellationToken ct = default)
    {
        IsPreviewing = true;
        ErrorMessage = null;
        PreviewItems.Clear();
        PreviewMediaItems.Clear();

        try
        {
            var query = BuildQueryDefinition();
            var pageSize = Math.Clamp(query.Limit ?? 100, 1, 100);
            var response = await _catalogApi.GetCatalogAsync(
                libraryId: query.LibraryIds.FirstOrDefault() is > 0 ? query.LibraryIds[0] : null,
                sort: query.Sort?.Field,
                order: query.Sort?.Order,
                type: string.IsNullOrWhiteSpace(query.MediaScope) || query.MediaScope == "video"
                    ? null
                    : query.MediaScope,
                limit: pageSize,
                offset: 0,
                includeTotal: true,
                source: "query",
                queryGroups: query.Groups,
                queryGroupsMatch: query.Match,
                ct: ct);

            PreviewTotal = query.Limit is > 0
                ? Math.Min(response.Total, query.Limit.Value)
                : response.Total;
            foreach (var item in response.Items)
                PreviewMediaItems.Add(item);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
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
        var cleanRules = Rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Field)
                && !string.IsNullOrWhiteSpace(rule.Op)
                && !string.IsNullOrWhiteSpace(rule.Value?.ToString()))
            .Select(rule => new QueryRule
            {
                Field = rule.Field,
                Op = rule.Op,
                Value = CoerceRuleValue(rule)
            })
            .ToList();

        if (cleanRules.Count == 0 && !string.IsNullOrWhiteSpace(MediaScope))
            cleanRules.Add(new QueryRule { Field = "type", Op = "is", Value = MediaScope });

        return new QueryDefinition
        {
            LibraryIds = [.. SelectedLibraryIds],
            MediaScope = string.IsNullOrWhiteSpace(MediaScope) ? null : MediaScope,
            Match = MatchMode,
            Groups =
            [
                new QueryGroup
                {
                    Match = MatchMode,
                    Rules = cleanRules
                }
            ],
            Sort = new QuerySort { Field = SortField, Order = SortOrder },
            Limit = int.TryParse(LimitText, out var limit) ? Math.Clamp(limit, 1, 500) : 100
        };
    }

    private static object? CoerceRuleValue(QueryRule rule)
    {
        if (rule.Value is bool boolean)
            return boolean;

        var value = rule.Value?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (rule.Op == "between")
        {
            var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length == 2)
            {
                if (rule.Field is "year" or "rating_imdb" or "bitrate"
                    && double.TryParse(values[0], out var start)
                    && double.TryParse(values[1], out var end))
                    return new[] { start, end };
                return values;
            }
        }

        if (rule.Field is "watched" or "favorited" or "in_watchlist" or "in_progress" or "hdr" or "dolby_vision"
            && bool.TryParse(value, out var parsedBoolean))
            return parsedBoolean;

        if (rule.Field is "year" or "rating_imdb" or "bitrate" && double.TryParse(value, out var number))
            return number;

        return value.Trim();
    }

}
