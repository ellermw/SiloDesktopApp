using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Api;

public partial class CatalogApi(SiloApiClient client)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(ApiRequestContext Context, string Scope), (DateTime LoadedAt, CatalogFiltersResponse Value)> _filterScopes = new();
    private int _filterCacheGeneration;
    private readonly object _filterScopesGate = new();
    public int FilterCacheGeneration => Volatile.Read(ref _filterCacheGeneration);
    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);

    public void InvalidateFilterCache()
    {
        lock (_filterScopesGate)
        {
            Interlocked.Increment(ref _filterCacheGeneration);
            _filterScopes.Clear();
        }
    }

    public async Task<MetadataAiStatus> GetMetadataAiStatusAsync(CancellationToken ct = default)
    {
        var capability = await client.GetAsync<MetadataAiCapability>("/api/v2/capabilities/metadata-ai", ct);
        return new() { Enabled = capability.State == "available" && capability.Allowed, OnView = capability.OnView };
    }

    public Task TranslateItemDescriptionAsync(
        string contentId,
        string targetLanguage,
        CancellationToken ct = default)
        => client.PostNoContentAsync(
            $"/api/v2/catalog/items/{Uri.EscapeDataString(contentId)}/translate-description",
            new Dictionary<string, object?> { ["target_language"] = targetLanguage },
            ct);
    private List<Library>? _librariesCache;
    private DateTime _librariesCachedAt = DateTime.MinValue;
    private ApiRequestContext? _librariesContext;
    private static readonly TimeSpan LibraryCacheDuration = TimeSpan.FromMinutes(5);

    public async Task<List<Library>> GetLibrariesAsync(CancellationToken ct = default, bool includeHidden = false)
    {
        var context = client.CaptureContext();
        if (!includeHidden && _librariesCache != null && _librariesContext == context && DateTime.UtcNow - _librariesCachedAt < LibraryCacheDuration)
            return _librariesCache;

        var libraries = (await client.GetAsync<BrowseCollection<Library>>("/api/v2/user/libraries" + (includeHidden ? "?include_hidden=true" : ""), ct)).Items;
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Library context changed.", ct);
        if (includeHidden) return libraries;
        _librariesCache = libraries;
        _librariesContext = context;
        _librariesCachedAt = DateTime.UtcNow;
        return _librariesCache;
    }

    public void InvalidateLibraryCache()
    {
        _librariesCache = null;
        _librariesCachedAt = DateTime.MinValue;
    }

    public Task<HomeSectionsResponse> GetLibrarySectionsAsync(int libraryId, CancellationToken ct = default)
        => client.GetAsync<HomeSectionsResponse>($"/api/v2/library/{libraryId}/sections", ct);

    public Task<HomeLayoutResponse> GetLibraryLayoutAsync(int libraryId, CancellationToken ct = default)
        => client.GetAsync<HomeLayoutResponse>($"/api/v2/library/{libraryId}/layout", ct);

    public Task<CatalogResponse> GetCatalogAsync(
        int? libraryId,
        string? sort = null, string? order = null,
        string? genre = null, string? studio = null, string? contentRating = null,
        string? country = null, string? resolution = null, string? audioLanguage = null,
        string? yearMin = null, string? yearMax = null,
        string? q = null, string? type = null, int limit = 40, int offset = 0,
        bool includeTotal = true, string? snapshot = null,
        string? source = null, string? scope = null, string? sectionId = null,
        IReadOnlyList<QueryRule>? extraRules = null,
        string extraRulesMatch = "all",
        IReadOnlyList<QueryGroup>? queryGroups = null,
        string queryGroupsMatch = "all",
        string? collectionId = null,
        CancellationToken ct = default,
        int? queryLimit = null,
        string? nextCursor = null)
    {
        var query = $"/api/v2/catalog?limit={Math.Clamp(limit, 1, 200)}";
        if (string.IsNullOrWhiteSpace(nextCursor)) query += $"&seek={Math.Max(0, offset)}";
        if (!string.IsNullOrWhiteSpace(source)) query += $"&source={Uri.EscapeDataString(source)}";
        if (!string.IsNullOrWhiteSpace(scope)) query += $"&scope={Uri.EscapeDataString(scope)}";
        if (!string.IsNullOrWhiteSpace(sectionId)) query += $"&section_id={Uri.EscapeDataString(sectionId)}";
        if (!string.IsNullOrWhiteSpace(collectionId)) query += $"&collection_id={Uri.EscapeDataString(collectionId)}";
        if (libraryId is > 0) query += $"&library_id={libraryId.Value}";
        if (sort != null) query += $"&sort={Uri.EscapeDataString(BrowseV2.Sort(sort, order))}";
        if (genre != null) query += $"&genre={Uri.EscapeDataString(genre)}";
        if (contentRating != null) query += $"&content_rating={Uri.EscapeDataString(contentRating)}";
        if (yearMin != null) query += $"&year_min={Uri.EscapeDataString(yearMin)}";
        if (yearMax != null) query += $"&year_max={Uri.EscapeDataString(yearMax)}";
        if (q != null) query += $"&q={Uri.EscapeDataString(q)}";
        if (type != null) query += $"&type={Uri.EscapeDataString(type)}";
        if (!includeTotal) query += "&skip_total=true";
        if (queryLimit is > 0) query += $"&query_limit={queryLimit.Value}";
        var cursor = string.IsNullOrWhiteSpace(nextCursor) ? snapshot : nextCursor;
        if (!string.IsNullOrWhiteSpace(cursor)) query += $"&cursor={Uri.EscapeDataString(cursor)}";

        // V2 accepts structured JSON rule groups, not bracketed query keys.
        var groups = new List<QueryGroup>();
        if (queryGroups != null)
        {
            groups.AddRange(queryGroups.Where(group => group.Rules.Count > 0));
            query += $"&match={Uri.EscapeDataString(queryGroupsMatch == "any" ? "any" : "all")}";
        }
        else
        {
            var rules = new List<QueryRule>();
            if (!string.IsNullOrWhiteSpace(studio)) rules.Add(new QueryRule { Field = "studio", Op = "is", Value = studio });
            if (!string.IsNullOrWhiteSpace(country)) rules.Add(new QueryRule { Field = "country", Op = "is", Value = country });
            if (!string.IsNullOrWhiteSpace(resolution)) rules.Add(new QueryRule { Field = "resolution", Op = "is", Value = resolution });
            if (!string.IsNullOrWhiteSpace(audioLanguage)) rules.Add(new QueryRule { Field = "audio_language", Op = "is", Value = audioLanguage });
            if (extraRules != null) rules.AddRange(extraRules);
            if (rules.Count > 0)
                groups.Add(new QueryGroup { Match = extraRulesMatch == "any" ? "any" : "all", Rules = rules });
        }

        if (groups.Count > 0)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(groups, BrowseV2.Json);
            // The pinned v2 contract explicitly limits GET groups to 32768
            // characters; the JSON endpoint runs the same catalog engine.
            if (json.Length > 32768)
                return GetLargeCatalogQueryAsync(groups, queryGroups != null ? queryGroupsMatch : "all", libraryId,
                    sort, order, genre, contentRating, yearMin, yearMax, q, type, limit, offset, includeTotal,
                    snapshot, source, scope, sectionId, collectionId, queryLimit, ct, nextCursor);
            query += "&groups=" + Uri.EscapeDataString(json);
        }
        return client.GetAsync<CatalogResponse>(query, ct);
    }

    public async Task<CatalogFiltersResponse> GetFiltersAsync(
        int? libraryId = null,
        CancellationToken ct = default,
        string? source = null,
        string? scope = null,
        string? sectionId = null,
        string? q = null,
        string? type = null,
        string? collectionId = null,
        string? personId = null,
        bool includeTechnical = true)
    {
        var parameters = new List<string>();
        if (libraryId is > 0) parameters.Add($"library_id={libraryId.Value}");
        if (!string.IsNullOrWhiteSpace(source)) parameters.Add($"source={Uri.EscapeDataString(source)}");
        if (!string.IsNullOrWhiteSpace(scope)) parameters.Add($"scope={Uri.EscapeDataString(scope)}");
        if (!string.IsNullOrWhiteSpace(sectionId)) parameters.Add($"section_id={Uri.EscapeDataString(sectionId)}");
        if (!string.IsNullOrWhiteSpace(type)) parameters.Add($"type={Uri.EscapeDataString(type)}");
        if (!string.IsNullOrWhiteSpace(collectionId)) parameters.Add($"collection_id={Uri.EscapeDataString(collectionId)}");
        if (!string.IsNullOrWhiteSpace(personId)) parameters.Add($"person_id={Uri.EscapeDataString(personId)}");
        if (!includeTechnical) parameters.Add("include_technical=false");
        var query = "/api/v2/catalog/filters" + (parameters.Count > 0 ? "?" + string.Join("&", parameters) : "");
        ct.ThrowIfCancellationRequested();
        var context = client.CaptureContext();
        var generation = Volatile.Read(ref _filterCacheGeneration);
        var key = (context, query);
        lock (_filterScopesGate)
        {
            if (client.IsCurrentContext(context) && generation == Volatile.Read(ref _filterCacheGeneration)
                && _filterScopes.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.LoadedAt < TimeSpan.FromMinutes(5))
                return cached.Value;
        }

        var filters = await client.SendRequestAsync<CatalogFiltersResponse>(context, HttpMethod.Get, query, null, ct);
        ct.ThrowIfCancellationRequested();
        lock (_filterScopesGate)
        {
            if (!client.IsCurrentContext(context) || generation != Volatile.Read(ref _filterCacheGeneration))
                throw new OperationCanceledException("Catalog metadata scope changed.", ct);
            // Failed or canceled requests never populate the successful metadata cache.
            foreach (var entry in _filterScopes)
                if (entry.Key.Context != context || DateTime.UtcNow - entry.Value.LoadedAt >= TimeSpan.FromMinutes(5))
                    _filterScopes.TryRemove(entry.Key, out _);
            if (_filterScopes.Count >= 64)
                foreach (var entry in _filterScopes.OrderBy(entry => entry.Value.LoadedAt).Take(_filterScopes.Count - 63))
                    _filterScopes.TryRemove(entry.Key, out _);
            _filterScopes[key] = (DateTime.UtcNow, filters);
        }
        return filters;
    }

    public async Task<AudiobookGroupsResponse> GetAudiobookGroupsAsync(
        int libraryId,
        string groupBy,
        string sort = "name",
        string? search = null,
        int limit = 60,
        int offset = 0,
        bool includeTotal = true,
        CancellationToken ct = default)
    {
        var query = $"/api/v2/catalog/audiobook-groups?library_id={libraryId}" +
            $"&group_by={Uri.EscapeDataString(groupBy)}" +
            $"&sort={Uri.EscapeDataString(sort)}&skip_total={(!includeTotal).ToString().ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(search))
            query += $"&q={Uri.EscapeDataString(search.Trim())}";
        var page = await BrowseV2.WindowAsync<AudiobookGroup>(client, query, limit, offset, ct);
        return new() { Groups = page.Items, Total = page.Total, TotalExact = page.TotalExact, HasMore = page.Page?.HasMore == true };
    }

    public Task<CatalogResponse> SearchAsync(string query, int limit = 40, string? type = null, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(query);
        var path = $"/api/v2/catalog?q={encoded}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(type) && type != "all") path += $"&type={Uri.EscapeDataString(type)}";
        return client.GetAsync<CatalogResponse>(path, ct);
    }

    public Task<MediaItemDetail> GetItemDetailAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<MediaItemDetail>($"/api/v2/catalog/items/{Uri.EscapeDataString(contentId)}", ct);

    public Task<ItemListResponse> GetFavoritesAsync(CancellationToken ct = default)
        => GetFavoritesAsync(limit: 50, offset: 0, ct);

    public async Task<ItemListResponse> GetFavoritesAsync(int limit, int offset, CancellationToken ct = default)
    {
        var page = await BrowseV2.WindowAsync<MediaItem>(client, "/api/v2/favorites", limit, offset, ct);
        return new() { Items = page.Items, HasMore = page.Page?.HasMore == true };
    }

    public Task<ItemListResponse> GetWatchlistAsync(CancellationToken ct = default)
        => GetWatchlistAsync(limit: 50, offset: 0, ct);

    public async Task<ItemListResponse> GetWatchlistAsync(int limit, int offset, CancellationToken ct = default)
    {
        var page = await BrowseV2.WindowAsync<MediaItem>(client, "/api/v2/watchlist", limit, offset, ct);
        return new() { Items = page.Items, HasMore = page.Page?.HasMore == true };
    }

    public async Task<ProgressResponse> GetProgressAsync(CancellationToken ct = default)
        => new() { Progress = (await client.GetAsync<BrowseCollection<ProgressItem>>("/api/v2/progress?status=in_progress&limit=50", ct)).Items };

    public Task<RecommendationsResponse> GetRecommendationsAsync(CancellationToken ct = default)
        => new RecommendationsApi(client).GetForYouRowsAsync(ct);

    public Task AddFavoriteAsync(string contentId, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v2/favorites/{Uri.EscapeDataString(contentId)}", null, ct);

    public Task RemoveFavoriteAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/favorites/{Uri.EscapeDataString(contentId)}", ct);

    public Task AddToWatchlistAsync(string contentId, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v2/watchlist/{Uri.EscapeDataString(contentId)}", null, ct);

    public Task RemoveFromWatchlistAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/watchlist/{Uri.EscapeDataString(contentId)}", ct);

    public async Task<SeasonsResponse> GetSeasonsAsync(string seriesId, CancellationToken ct = default)
        => new() { Seasons = (await client.GetAsync<BrowseCollection<Season>>($"/api/v2/catalog/series/{Uri.EscapeDataString(seriesId)}/seasons", ct)).Items };

    public async Task<EpisodesResponse> GetEpisodesAsync(string seriesId, int seasonNumber, CancellationToken ct = default)
        => new() { Episodes = (await client.GetAsync<BrowseCollection<Episode>>($"/api/v2/catalog/series/{Uri.EscapeDataString(seriesId)}/seasons/{seasonNumber}/episodes", ct)).Items };

    /// <summary>
    /// Fetches the episodes that belong to a season item. This is the canonical
    /// endpoint used by the current WebUI and, unlike the legacy series/number
    /// route, also handles Specials (season zero) without discarding them.
    /// </summary>
    public async Task<EpisodesResponse> GetItemEpisodesAsync(string seasonContentId, CancellationToken ct = default)
        => new() { Episodes = (await client.GetAsync<BrowseCollection<Episode>>($"/api/v2/catalog/items/{Uri.EscapeDataString(seasonContentId)}/episodes", ct)).Items };

    // ===== Watched State =====

    public Task MarkWatchedAsync(string contentId, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v2/watched/{Uri.EscapeDataString(contentId)}", new { }, ct);

    public Task MarkUnwatchedAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/watched/{Uri.EscapeDataString(contentId)}", ct);

    // ===== Ratings =====

    public async Task<int?> GetRatingAsync(string contentId, CancellationToken ct = default)
    {
        try
        {
            var r = await client.GetAsync<RatingResponse>($"/api/v2/ratings/{Uri.EscapeDataString(contentId)}", ct);
            return r.Rating;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public Task SetRatingAsync(string contentId, int rating, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v2/ratings/{Uri.EscapeDataString(contentId)}", new { rating }, ct);

    public Task DeleteRatingAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/ratings/{Uri.EscapeDataString(contentId)}", ct);

    // ===== Recommendations =====

    public Task<SimilarResponse> GetSimilarAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<SimilarResponse>($"/api/v2/recommendations/similar/{Uri.EscapeDataString(contentId)}?limit=12", ct);

    // ===== Item Versions =====

    public async Task<List<FileVersion>> GetItemVersionsAsync(string contentId, CancellationToken ct = default)
        => (await client.GetAsync<BrowseCollection<FileVersion>>($"/api/v2/catalog/items/{Uri.EscapeDataString(contentId)}/versions", ct)).Items;

    public Task<MangaSeriesFiles> GetMangaSeriesFilesAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<MangaSeriesFiles>($"/api/v2/catalog/items/{Uri.EscapeDataString(contentId)}/manga-files", ct);

    // ===== Library Collections =====

    public Task<LibraryTabResponse> GetLibraryCollectionsAsync(int libraryId, CancellationToken ct = default)
        => client.GetAsync<LibraryTabResponse>($"/api/v2/library/{libraryId}/collections", ct);

    public async Task<CatalogResponse> GetLibraryCollectionItemsAsync(int libraryId, string collectionId, CancellationToken ct = default)
    {
        var items = await BrowseV2.AllAsync<MediaItem>(client, $"/api/v2/library/{libraryId}/collections/{Uri.EscapeDataString(collectionId)}/items", ct);
        return new() { Items = items, Total = items.Count, TotalExact = true };
    }

    // ===== Library Sections =====

    public async Task<HomeSectionItemsResponse> GetLibrarySectionItemsAsync(int libraryId, string sectionId, CancellationToken ct = default)
        => new() { Section = await client.GetAsync<HomeSectionWithItems>($"/api/v2/library/{libraryId}/sections/{Uri.EscapeDataString(sectionId)}/items", ct) };

    // ===== History =====

    public async Task<HistoryResponse> GetHistoryAsync(int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        var page = await BrowseV2.WindowAsync<MediaItem>(client, "/api/v2/history", limit ?? 50, offset ?? 0, ct);
        return new() { Items = page.Items, HasMore = page.Page?.HasMore == true };
    }

    public Task RemoveHistoryAsync(IEnumerable<HistoryRemovalTarget> targets, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v2/history/remove", new RemoveHistoryRequest
        {
            Targets = targets.ToList()
        }, ct);

    // ===== Ratings List =====

    public async Task<RatingListResponse> GetRatingsListAsync(CancellationToken ct = default)
        => new() { Ratings = await BrowseV2.AllAsync<RatingListItem>(client, "/api/v2/ratings", ct) };

    // ===== Watchlist / Favorites Check =====

    public async Task<bool> GetWatchlistItemAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            await client.GetAsync<object>($"/api/v2/watchlist/{Uri.EscapeDataString(itemId)}", ct);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return false;
        }
    }

    public async Task<bool> GetFavoriteItemAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            await client.GetAsync<object>($"/api/v2/favorites/{Uri.EscapeDataString(itemId)}", ct);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return false;
        }
    }

    // ===== Audio Preferences =====

    public Task<AudioPreferenceResponse> GetAudioPrefsAsync(string seriesId, CancellationToken ct = default)
        => client.GetAsync<AudioPreferenceResponse>($"/api/v2/audio-prefs/{Uri.EscapeDataString(seriesId)}", ct);

    public Task SetAudioPrefsAsync(string seriesId, AudioPreference request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v2/audio-prefs/{Uri.EscapeDataString(seriesId)}", request, ct);

    public Task DeleteAudioPrefsAsync(string seriesId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/audio-prefs/{Uri.EscapeDataString(seriesId)}", ct);

    // ===== Person Filmography =====

    // B33: personId is a string — supports non-numeric IDs from third-party providers.
    public Task<CatalogResponse> GetPersonFilmographyAsync(string personId, string? type = null, int limit = 60, int offset = 0, CancellationToken ct = default)
    {
        var query = $"/api/v2/catalog?source=person&person_id={Uri.EscapeDataString(personId)}&limit={Math.Clamp(limit, 1, 200)}&seek={Math.Max(0, offset)}&sort=-year";
        if (!string.IsNullOrEmpty(type) && type != "all")
            query += $"&type={Uri.EscapeDataString(type)}";
        return client.GetAsync<CatalogResponse>(query, ct);
    }

    // ===== Collection Browse (B40 + B41) =====

    /// <summary>
    /// Browse a user-defined collection's items as a catalog page. Mirrors
    /// the webui <c>buildCollectionCatalogHref("user_collection", id)</c>.
    /// </summary>
    public Task<CatalogResponse> BrowseUserCollectionAsync(string collectionId, string? sort = null, string? order = null, int limit = 60, int offset = 0, CancellationToken ct = default)
    {
        var query = $"/api/v2/catalog?source=user_collection&collection_id={Uri.EscapeDataString(collectionId)}&limit={Math.Clamp(limit, 1, 200)}&seek={Math.Max(0, offset)}";
        if (sort != null) query += $"&sort={Uri.EscapeDataString(BrowseV2.Sort(sort, order))}";
        return client.GetAsync<CatalogResponse>(query, ct);
    }

    /// <summary>
    /// Browse a server-discovered library collection (aka smart collection)
    /// by its library collection ID.
    /// </summary>
    public Task<CatalogResponse> BrowseLibraryCollectionAsync(string collectionId, string? sort = null, string? order = null, int limit = 60, int offset = 0, CancellationToken ct = default)
    {
        var query = $"/api/v2/catalog?source=library_collection&collection_id={Uri.EscapeDataString(collectionId)}&limit={Math.Clamp(limit, 1, 200)}&seek={Math.Max(0, offset)}";
        if (sort != null) query += $"&sort={Uri.EscapeDataString(BrowseV2.Sort(sort, order))}";
        return client.GetAsync<CatalogResponse>(query, ct);
    }

    // ===== Sync =====

    public Task SyncProgressAsync(object request, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v2/sync/progress", request, ct);

    // ===== Calendar =====

    /// <summary>
    /// Fetches calendar events for a date range. Mirrors the web client's
    /// <c>useCalendarWeek</c> hook which calls
    /// <c>GET /api/v2/calendar?start=&amp;end=&amp;filter=&amp;library_id=</c>.
    /// </summary>
    /// <param name="start">Inclusive start date (YYYY-MM-DD).</param>
    /// <param name="end">Inclusive end date (YYYY-MM-DD), max 31 days after start.</param>
    /// <param name="filter"><c>all</c>, <c>favorites</c>, or <c>watchlist</c>.</param>
    /// <param name="libraryId">Optional library scope.</param>
    public Task<CalendarResponse> GetCalendarAsync(
        string start,
        string end,
        string filter = "all",
        int? libraryId = null,
        string? timezone = null,
        CancellationToken ct = default)
    {
        var query = $"/api/v2/calendar?start={Uri.EscapeDataString(start)}" +
                    $"&end={Uri.EscapeDataString(end)}" +
                    $"&filter={Uri.EscapeDataString(filter)}";
        if (!string.IsNullOrWhiteSpace(timezone))
            query += $"&timezone={Uri.EscapeDataString(timezone)}";
        if (libraryId.HasValue)
            query += $"&library_id={libraryId.Value}";
        return client.GetAsync<CalendarResponse>(query, ct);
    }
}

internal sealed class MetadataAiCapability
{
    public string State { get; set; } = "";
    public bool Allowed { get; set; }
    public string OnView { get; set; } = "off";
}
