using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Api;

public class CatalogApi(SiloApiClient client)
{
    private List<Library>? _librariesCache;
    private DateTime _librariesCachedAt = DateTime.MinValue;
    private static readonly TimeSpan LibraryCacheDuration = TimeSpan.FromMinutes(5);

    public async Task<List<Library>> GetLibrariesAsync(CancellationToken ct = default)
    {
        if (_librariesCache != null && DateTime.UtcNow - _librariesCachedAt < LibraryCacheDuration)
            return _librariesCache;

        _librariesCache = await client.GetAsync<List<Library>>("/api/v1/user/libraries", ct);
        _librariesCachedAt = DateTime.UtcNow;
        return _librariesCache;
    }

    public void InvalidateLibraryCache()
    {
        _librariesCache = null;
        _librariesCachedAt = DateTime.MinValue;
    }

    public Task<HomeSectionsResponse> GetLibrarySectionsAsync(int libraryId, CancellationToken ct = default)
        => client.GetAsync<HomeSectionsResponse>($"/api/v1/library/{libraryId}/sections", ct);

    public Task<CatalogResponse> GetCatalogAsync(
        int? libraryId,
        string? sort = null, string? order = null,
        string? genre = null, string? studio = null, string? contentRating = null,
        string? country = null, string? resolution = null, string? audioLanguage = null,
        string? yearMin = null, string? yearMax = null,
        string? q = null, string? type = null, int limit = 40, int offset = 0,
        bool includeTotal = true, string? snapshot = null,
        string? source = null,
        CancellationToken ct = default)
    {
        var query = $"/api/v1/catalog?limit={limit}&offset={offset}";
        if (!string.IsNullOrWhiteSpace(source)) query += $"&source={Uri.EscapeDataString(source)}";
        if (libraryId is > 0) query += $"&library_id={libraryId.Value}";
        if (sort != null) query += $"&sort={Uri.EscapeDataString(sort)}";
        if (order != null) query += $"&order={Uri.EscapeDataString(order)}";
        if (genre != null) query += $"&genre={Uri.EscapeDataString(genre)}";
        if (studio != null) query += $"&studio={Uri.EscapeDataString(studio)}";
        if (contentRating != null) query += $"&content_rating={Uri.EscapeDataString(contentRating)}";
        if (country != null) query += $"&country={Uri.EscapeDataString(country)}";
        if (resolution != null) query += $"&resolution={Uri.EscapeDataString(resolution)}";
        if (audioLanguage != null) query += $"&audio_language={Uri.EscapeDataString(audioLanguage)}";
        if (yearMin != null) query += $"&year_min={Uri.EscapeDataString(yearMin)}";
        if (yearMax != null) query += $"&year_max={Uri.EscapeDataString(yearMax)}";
        if (q != null) query += $"&q={Uri.EscapeDataString(q)}";
        if (type != null) query += $"&type={Uri.EscapeDataString(type)}";
        if (!includeTotal) query += "&include_total=false";
        if (!string.IsNullOrWhiteSpace(snapshot)) query += $"&snapshot={Uri.EscapeDataString(snapshot)}";

        // The current catalog API represents these facets as query-definition
        // rules. Keep the legacy scalar parameters above for older servers,
        // and also send the authoritative rule form used by the WebUI.
        var rules = new List<(string Field, string Value)>();
        if (!string.IsNullOrWhiteSpace(studio)) rules.Add(("studio", studio));
        if (!string.IsNullOrWhiteSpace(country)) rules.Add(("country", country));
        if (!string.IsNullOrWhiteSpace(resolution)) rules.Add(("resolution", resolution));
        if (!string.IsNullOrWhiteSpace(audioLanguage)) rules.Add(("audio_language", audioLanguage));
        if (rules.Count > 0)
        {
            query += "&groups%5B0%5D%5Bmatch%5D=all";
            for (var i = 0; i < rules.Count; i++)
            {
                var prefix = $"groups%5B0%5D%5Brules%5D%5B{i}%5D";
                query += $"&{prefix}%5Bfield%5D={Uri.EscapeDataString(rules[i].Field)}";
                query += $"&{prefix}%5Bop%5D=is";
                query += $"&{prefix}%5Bvalue%5D={Uri.EscapeDataString(rules[i].Value)}";
            }
        }
        return client.GetAsync<CatalogResponse>(query, ct);
    }

    public Task<CatalogFiltersResponse> GetFiltersAsync(int? libraryId = null, CancellationToken ct = default, string? source = null)
    {
        var parameters = new List<string>();
        if (libraryId is > 0) parameters.Add($"library_id={libraryId.Value}");
        if (!string.IsNullOrWhiteSpace(source)) parameters.Add($"source={Uri.EscapeDataString(source)}");
        var query = "/api/v1/catalog/filters" + (parameters.Count > 0 ? "?" + string.Join("&", parameters) : "");
        return client.GetAsync<CatalogFiltersResponse>(query, ct);
    }

    public Task<CatalogResponse> SearchAsync(string query, int limit = 40, string? type = null, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(query);
        var path = $"/api/v1/catalog?q={encoded}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(type) && type != "all") path += $"&type={Uri.EscapeDataString(type)}";
        return client.GetAsync<CatalogResponse>(path, ct);
    }

    public Task<MediaItemDetail> GetItemDetailAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<MediaItemDetail>($"/api/v1/catalog/items/{contentId}", ct);

    public Task<ItemListResponse> GetFavoritesAsync(CancellationToken ct = default)
        => client.GetAsync<ItemListResponse>("/api/v1/favorites", ct);

    public Task<ItemListResponse> GetWatchlistAsync(CancellationToken ct = default)
        => client.GetAsync<ItemListResponse>("/api/v1/watchlist", ct);

    public Task<ProgressResponse> GetProgressAsync(CancellationToken ct = default)
        => client.GetAsync<ProgressResponse>("/api/v1/progress?status=in_progress&limit=50", ct);

    public Task<RecommendationsResponse> GetRecommendationsAsync(CancellationToken ct = default)
        => client.GetAsync<RecommendationsResponse>("/api/v1/recommendations/for-you/rows", ct);

    public Task AddFavoriteAsync(string contentId, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/favorites/{contentId}", null, ct);

    public Task RemoveFavoriteAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/favorites/{contentId}", ct);

    public Task AddToWatchlistAsync(string contentId, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/watchlist/{contentId}", null, ct);

    public Task RemoveFromWatchlistAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/watchlist/{contentId}", ct);

    public Task<SeasonsResponse> GetSeasonsAsync(string seriesId, CancellationToken ct = default)
        => client.GetAsync<SeasonsResponse>($"/api/v1/catalog/series/{seriesId}/seasons", ct);

    public Task<EpisodesResponse> GetEpisodesAsync(string seriesId, int seasonNumber, CancellationToken ct = default)
        => client.GetAsync<EpisodesResponse>($"/api/v1/catalog/series/{seriesId}/seasons/{seasonNumber}/episodes", ct);

    // ===== Watched State =====

    public Task MarkWatchedAsync(string contentId, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/watched/{contentId}", new { }, ct);

    public Task MarkUnwatchedAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/watched/{contentId}", ct);

    // ===== Ratings =====

    public async Task<int?> GetRatingAsync(string contentId, CancellationToken ct = default)
    {
        try
        {
            var r = await client.GetAsync<RatingResponse>($"/api/v1/ratings/{contentId}", ct);
            return r.Rating;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public Task SetRatingAsync(string contentId, int rating, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/ratings/{contentId}", new { rating }, ct);

    public Task DeleteRatingAsync(string contentId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/ratings/{contentId}", ct);

    // ===== Recommendations =====

    public Task<SimilarResponse> GetSimilarAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<SimilarResponse>($"/api/v1/recommendations/similar/{contentId}", ct);

    // ===== Item Versions =====

    public Task<List<FileVersion>> GetItemVersionsAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<List<FileVersion>>($"/api/v1/catalog/items/{Uri.EscapeDataString(contentId)}/versions", ct);

    // ===== Library Collections =====

    public Task<LibraryTabResponse> GetLibraryCollectionsAsync(int libraryId, CancellationToken ct = default)
        => client.GetAsync<LibraryTabResponse>($"/api/v1/library/{libraryId}/collections", ct);

    public Task<CatalogResponse> GetLibraryCollectionItemsAsync(int libraryId, string collectionId, CancellationToken ct = default)
        => client.GetAsync<CatalogResponse>($"/api/v1/library/{libraryId}/collections/{Uri.EscapeDataString(collectionId)}/items", ct);

    // ===== Library Sections =====

    public Task<HomeSectionsResponse> GetLibrarySectionItemsAsync(int libraryId, string sectionId, CancellationToken ct = default)
        => client.GetAsync<HomeSectionsResponse>($"/api/v1/library/{libraryId}/sections/{Uri.EscapeDataString(sectionId)}/items", ct);

    // ===== History =====

    public Task<HistoryResponse> GetHistoryAsync(int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        var path = "/api/v1/history";
        var queryParts = new List<string>();
        if (limit.HasValue) queryParts.Add($"limit={limit.Value}");
        if (offset.HasValue) queryParts.Add($"offset={offset.Value}");
        if (queryParts.Count > 0) path += "?" + string.Join("&", queryParts);
        return client.GetAsync<HistoryResponse>(path, ct);
    }

    public Task RemoveHistoryAsync(IEnumerable<HistoryRemovalTarget> targets, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/history/remove", new RemoveHistoryRequest
        {
            Targets = targets.ToList()
        }, ct);

    // ===== Ratings List =====

    public Task<RatingListResponse> GetRatingsListAsync(CancellationToken ct = default)
        => client.GetAsync<RatingListResponse>("/api/v1/ratings", ct);

    // ===== Watchlist / Favorites Check =====

    public async Task<bool> GetWatchlistItemAsync(string itemId, CancellationToken ct = default)
    {
        try
        {
            await client.GetAsync<object>($"/api/v1/watchlist/{Uri.EscapeDataString(itemId)}", ct);
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
            await client.GetAsync<object>($"/api/v1/favorites/{Uri.EscapeDataString(itemId)}", ct);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return false;
        }
    }

    // ===== Audio Preferences =====

    public Task<AudioPreferenceResponse> GetAudioPrefsAsync(string seriesId, CancellationToken ct = default)
        => client.GetAsync<AudioPreferenceResponse>($"/api/v1/audio-prefs/{Uri.EscapeDataString(seriesId)}", ct);

    public Task SetAudioPrefsAsync(string seriesId, AudioPreference request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/audio-prefs/{Uri.EscapeDataString(seriesId)}", request, ct);

    public Task DeleteAudioPrefsAsync(string seriesId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/audio-prefs/{Uri.EscapeDataString(seriesId)}", ct);

    // ===== Person Filmography =====

    // B33: personId is a string — supports non-numeric IDs from third-party providers.
    public Task<CatalogResponse> GetPersonFilmographyAsync(string personId, string? type = null, int limit = 60, int offset = 0, CancellationToken ct = default)
    {
        var query = $"/api/v1/catalog?source=person&person_id={Uri.EscapeDataString(personId)}&limit={limit}&offset={offset}&sort=year&order=desc";
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
        var query = $"/api/v1/catalog?source=user_collection&collection_id={Uri.EscapeDataString(collectionId)}&limit={limit}&offset={offset}";
        if (sort != null) query += $"&sort={Uri.EscapeDataString(sort)}";
        if (order != null) query += $"&order={Uri.EscapeDataString(order)}";
        return client.GetAsync<CatalogResponse>(query, ct);
    }

    /// <summary>
    /// Browse a server-discovered library collection (aka smart collection)
    /// by its library collection ID.
    /// </summary>
    public Task<CatalogResponse> BrowseLibraryCollectionAsync(string collectionId, string? sort = null, string? order = null, int limit = 60, int offset = 0, CancellationToken ct = default)
    {
        var query = $"/api/v1/catalog?source=library_collection&collection_id={Uri.EscapeDataString(collectionId)}&limit={limit}&offset={offset}";
        if (sort != null) query += $"&sort={Uri.EscapeDataString(sort)}";
        if (order != null) query += $"&order={Uri.EscapeDataString(order)}";
        return client.GetAsync<CatalogResponse>(query, ct);
    }

    // ===== Sync =====

    public Task SyncProgressAsync(object request, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/sync/progress", request, ct);

    // ===== Calendar =====

    /// <summary>
    /// Fetches calendar events for a date range. Mirrors the web client's
    /// <c>useCalendarWeek</c> hook which calls
    /// <c>GET /api/v1/calendar?start=&amp;end=&amp;filter=&amp;library_id=</c>.
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
        CancellationToken ct = default)
    {
        var query = $"/api/v1/calendar?start={Uri.EscapeDataString(start)}" +
                    $"&end={Uri.EscapeDataString(end)}" +
                    $"&filter={Uri.EscapeDataString(filter)}";
        if (libraryId.HasValue)
            query += $"&library_id={libraryId.Value}";
        return client.GetAsync<CalendarResponse>(query, ct);
    }
}
