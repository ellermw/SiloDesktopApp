using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Api;

public class RequestsApi(SiloApiClient client)
{
    private ApiRequestContext? _discoverContext;
    private bool _discoverRowsSupported;
    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);
    public Task<MediaRequest> GetAsync(string id, CancellationToken ct = default)
        => client.GetAsync<MediaRequest>($"/api/v2/requests/{Uri.EscapeDataString(id)}", ct);

    public Task FollowAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => client.PutAsync<object>($"/api/v2/requests/follows/{Uri.EscapeDataString(mediaType)}/{tmdbId}", new Dictionary<string, object?>(), ct);

    public Task UnfollowAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/requests/follows/{Uri.EscapeDataString(mediaType)}/{tmdbId}", ct);

    public Task<List<WatchlistTitle>> GetWatchlistTitlesAsync(CancellationToken ct = default)
        => BrowseV2.AllAsync<WatchlistTitle>(client, "/api/v2/watchlist/titles", ct, 200);

    public Task<WatchlistTitleEntry> AddWatchlistTitleAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => client.PutAsync<WatchlistTitleEntry>($"/api/v2/watchlist/titles/{Uri.EscapeDataString(mediaType)}/{tmdbId}", new Dictionary<string, object?>(), ct);

    public Task RemoveWatchlistTitleAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/watchlist/titles/{Uri.EscapeDataString(mediaType)}/{tmdbId}", ct);

    public async Task<RequestFeatureStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var status = await client.GetAsync<RequestFeatureStatus>("/api/v2/requests/status", ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Request viewer changed.", ct);
        _discoverContext = context; _discoverRowsSupported = status.DiscoverRowsSupported;
        return status;
    }

    public async Task<RequestDiscoveryResponse> GetDiscoveryAsync(CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var page = await client.GetAsync<DiscoverCollection>("/api/v2/requests/discover", ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Request viewer changed.", ct);
        if (page.RowOrder != null) { _discoverContext = context; _discoverRowsSupported = true; }
        return new() { Sections = page.Items, RowOrder = page.RowOrder ?? (UsesDiscoverRows ? [] : null) };
    }

    private bool UsesDiscoverRows => _discoverRowsSupported && _discoverContext is { } context && client.IsCurrentContext(context);
    private sealed class DiscoverCollection
    {
        public List<RequestDiscoverySection> Items { get; set; } = [];
        public List<RequestDiscoverRow>? RowOrder { get; set; }
    }

    public Task<RequestDiscoverySection> GetDiscoverySectionAsync(string section, int page = 1, CancellationToken ct = default)
        => client.GetAsync<RequestDiscoverySection>(
            $"/api/v2/requests/discover/{(UsesDiscoverRows ? "rows/" : "")}{Uri.EscapeDataString(section)}?page={page}", ct);

    public async Task<DiscoverStudiosResponse> GetDiscoverStudiosAsync(CancellationToken ct = default)
        => new() { Studios = (await client.GetAsync<BrowseCollection<DiscoverBrandCard>>("/api/v2/requests/discover/studios", ct)).Items };

    public async Task<DiscoverNetworksResponse> GetDiscoverNetworksAsync(CancellationToken ct = default)
        => new() { Networks = (await client.GetAsync<BrowseCollection<DiscoverBrandCard>>("/api/v2/requests/discover/networks", ct)).Items };

    public async Task<DiscoverGenresResponse> GetDiscoverGenresAsync(CancellationToken ct = default)
        => new() { Genres = (await client.GetAsync<BrowseCollection<DiscoverBrandCard>>("/api/v2/requests/discover/genres", ct)).Items };

    public Task<DiscoverBrowseResponse> BrowseDiscoverAsync(
        string kind,
        string slug,
        string? mediaType = null,
        string sort = "popularity",
        int page = 1,
        CancellationToken ct = default)
    {
        var qs = new List<string>
        {
            $"sort={Uri.EscapeDataString(sort)}",
            $"page={page}"
        };
        if (kind == "genre" && !string.IsNullOrWhiteSpace(mediaType))
            qs.Add($"media_type={Uri.EscapeDataString(mediaType)}");
        return client.GetAsync<DiscoverBrowseResponse>(
            $"/api/v2/requests/discover/browse/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(slug)}?{string.Join("&", qs)}",
            ct);
    }

    public Task<RequestMediaDetail> GetDetailAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => client.GetAsync<RequestMediaDetail>(
            $"/api/v2/requests/detail/{Uri.EscapeDataString(mediaType)}/{tmdbId}", ct);

    public Task<RequestMediaPage> SearchAsync(
        string mediaType,
        string query,
        int page = 1,
        CancellationToken ct = default)
    {
        var qs = new List<string>
        {
            $"q={Uri.EscapeDataString(query)}",
            $"media_type={Uri.EscapeDataString(mediaType)}",
            $"page={page}"
        };
        return client.GetAsync<RequestMediaPage>($"/api/v2/requests/search?{string.Join("&", qs)}", ct);
    }

    public Task<MediaRequest> CreateAsync(CreateMediaRequestInput request, CancellationToken ct = default)
        => client.PostAsync<MediaRequest>("/api/v2/requests", System.Text.Json.JsonSerializer.SerializeToElement(request, V2Json.Options), ct);

    public async Task<MediaRequestsListResponse> GetMineAsync(
        string? status = null,
        string? outcome = null,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default)
        => new() { Requests = (await BrowseV2.WindowAsync<MediaRequest>(client,
            $"/api/v2/requests/mine{BuildListQuery(status, outcome)}", limit, offset, ct, maxPageSize: 50)).Items };

    public Task<MediaRequest> CancelAsync(string id, CancellationToken ct = default)
        => client.PostAsync<MediaRequest>($"/api/v2/requests/{Uri.EscapeDataString(id)}/cancel", new Dictionary<string, object?>(), ct);

    private static string BuildListQuery(string? status, string? outcome)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            qs.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(outcome) && !string.Equals(outcome, "all", StringComparison.OrdinalIgnoreCase))
            qs.Add($"outcome={Uri.EscapeDataString(outcome)}");
        return qs.Count == 0 ? "" : "?" + string.Join("&", qs);
    }
}
