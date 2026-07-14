using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.ViewModels;

public sealed record SmartCollectionWizardNavigationArgs(
    bool IsAdmin = false,
    int? LibraryId = null,
    string? CollectionId = null);

public partial class SmartCollectionWizardViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> MediaScopes = ["movie", "series", "episode"];

    private readonly AdminApi _adminApi;
    private readonly CatalogApi _catalogApi;
    private readonly CollectionsApi _collectionsApi;
    private string? _collectionId;

    public SmartCollectionWizardViewModel(AdminApi adminApi, CatalogApi catalogApi, CollectionsApi collectionsApi)
    {
        _adminApi = adminApi;
        _catalogApi = catalogApi;
        _collectionsApi = collectionsApi;
    }

    public ObservableCollection<Library> Libraries { get; } = [];
    public ObservableCollection<int> SelectedLibraryIds { get; } = [];
    public ObservableCollection<QueryRule> Rules { get; } = [];
    public ObservableCollection<CollectionPreviewItem> PreviewItems { get; } = [];

    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isPreviewing;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string _mediaScope = "movie";
    [ObservableProperty] private string _matchMode = "all";
    [ObservableProperty] private string _sortField = "added_at";
    [ObservableProperty] private string _sortOrder = "desc";
    [ObservableProperty] private string? _limitText = "100";
    [ObservableProperty] private bool _isShared;
    [ObservableProperty] private bool _featured;
    [ObservableProperty] private bool _includeInServerCollections = true;
    [ObservableProperty] private string? _posterSourceUrl;
    [ObservableProperty] private int _previewTotal;

    public event Action? Saved;

    public async Task ConfigureAsync(SmartCollectionWizardNavigationArgs? args, CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        IsAdmin = args?.IsAdmin == true;
        _collectionId = args?.CollectionId;

        try
        {
            Libraries.Clear();
            var libraries = IsAdmin
                ? await _adminApi.GetAdminLibrariesAsync(ct)
                : await _catalogApi.GetLibrariesAsync(ct);
            foreach (var library in libraries)
                Libraries.Add(library);

            SelectedLibraryIds.Clear();
            if (args?.LibraryId is int libraryId)
                SelectedLibraryIds.Add(libraryId);

            if (!IsAdmin && !string.IsNullOrWhiteSpace(_collectionId))
            {
                var response = await _collectionsApi.GetCollectionsAsync(ct);
                var collection = response.Collections.FirstOrDefault(item => item.Id == _collectionId);
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
                IncludeInServerCollections = collection.IncludeInServerCollections;
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

        try
        {
            var response = await _collectionsApi.PreviewCollectionAsync(
                new CollectionPreviewRequest
                {
                    QueryDefinition = BuildQueryDefinition(),
                    Limit = 24
                },
                ct);

            PreviewTotal = response.Total;
            foreach (var item in response.Items)
                PreviewItems.Add(item);
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
            if (IsAdmin)
            {
                if (SelectedLibraryIds.Count == 0)
                {
                    ErrorMessage = "Select at least one library for an admin smart collection.";
                    return;
                }

                await _adminApi.CreateCollectionAsync(new CreateLibraryCollectionRequest
                {
                    Title = Title.Trim(),
                    Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    CollectionType = "smart",
                    Visibility = "visible",
                    LibraryId = SelectedLibraryIds[0],
                    LibraryIds = SelectedLibraryIds.Count > 0 ? [.. SelectedLibraryIds] : null,
                    Featured = Featured,
                    QueryDefinition = ToAdminQueryDefinition(query)
                }, ct);
            }
            else
            {
                var request = new CreateCollectionRequest
                {
                    Name = Title.Trim(),
                    Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    CollectionType = "smart",
                    IsShared = IsShared,
                    IncludeInServerCollections = IncludeInServerCollections,
                    PosterSourceUrl = string.IsNullOrWhiteSpace(PosterSourceUrl) ? null : PosterSourceUrl.Trim(),
                    QueryDefinition = query
                };
                if (string.IsNullOrWhiteSpace(_collectionId))
                {
                    await _collectionsApi.CreateCollectionAsync(request, ct);
                }
                else
                {
                    await _collectionsApi.UpdateCollectionAsync(_collectionId, new UpdateCollectionRequest
                    {
                        Name = request.Name,
                        Description = request.Description,
                        IsShared = request.IsShared,
                        IncludeInServerCollections = request.IncludeInServerCollections,
                        PosterSourceUrl = request.PosterSourceUrl,
                        QueryDefinition = request.QueryDefinition
                    }, ct);
                }
            }

            StatusMessage = "Smart collection saved.";
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

        if (cleanRules.Count == 0)
            cleanRules.Add(new QueryRule { Field = "type", Op = "is", Value = MediaScope });

        return new QueryDefinition
        {
            LibraryIds = [.. SelectedLibraryIds],
            MediaScope = MediaScope,
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
            Limit = int.TryParse(LimitText, out var limit) && limit > 0 ? limit : null
        };
    }

    private static object? CoerceRuleValue(QueryRule rule)
    {
        var value = rule.Value?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (rule.Field is "year" or "runtime" or "rating_imdb" && double.TryParse(value, out var number))
            return number;

        return value.Trim();
    }

    private static Dictionary<string, object> ToAdminQueryDefinition(QueryDefinition query)
    {
        var body = new Dictionary<string, object>
        {
            ["library_ids"] = query.LibraryIds,
            ["media_scope"] = query.MediaScope ?? "movie",
            ["match"] = query.Match,
            ["groups"] = query.Groups.Select(group => new Dictionary<string, object>
            {
                ["match"] = group.Match,
                ["rules"] = group.Rules.Select(rule => new Dictionary<string, object?>
                {
                    ["field"] = rule.Field,
                    ["op"] = rule.Op,
                    ["value"] = rule.Value
                }).ToList()
            }).ToList()
        };

        if (query.Sort != null)
        {
            body["sort"] = new Dictionary<string, object>
            {
                ["field"] = query.Sort.Field,
                ["order"] = query.Sort.Order
            };
        }

        if (query.Limit.HasValue)
            body["limit"] = query.Limit.Value;

        return body;
    }
}
