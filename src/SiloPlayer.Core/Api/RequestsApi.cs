using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Api;

public class RequestsApi(SiloApiClient client)
{
    public Task<RequestFeatureStatus> GetStatusAsync(CancellationToken ct = default)
        => client.GetAsync<RequestFeatureStatus>("/api/v1/requests/status", ct);

    public Task<RequestDiscoveryResponse> GetDiscoveryAsync(CancellationToken ct = default)
        => client.GetAsync<RequestDiscoveryResponse>("/api/v1/requests/discover", ct);

    public Task<RequestDiscoverySection> GetDiscoverySectionAsync(string section, int page = 1, CancellationToken ct = default)
        => client.GetAsync<RequestDiscoverySection>(
            $"/api/v1/requests/discover/{Uri.EscapeDataString(section)}?page={page}", ct);

    public Task<DiscoverStudiosResponse> GetDiscoverStudiosAsync(CancellationToken ct = default)
        => client.GetAsync<DiscoverStudiosResponse>("/api/v1/requests/discover/studios", ct);

    public Task<DiscoverNetworksResponse> GetDiscoverNetworksAsync(CancellationToken ct = default)
        => client.GetAsync<DiscoverNetworksResponse>("/api/v1/requests/discover/networks", ct);

    public Task<DiscoverGenresResponse> GetDiscoverGenresAsync(CancellationToken ct = default)
        => client.GetAsync<DiscoverGenresResponse>("/api/v1/requests/discover/genres", ct);

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
        if (!string.IsNullOrWhiteSpace(mediaType))
            qs.Add($"media_type={Uri.EscapeDataString(mediaType)}");
        return client.GetAsync<DiscoverBrowseResponse>(
            $"/api/v1/requests/discover/browse/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(slug)}?{string.Join("&", qs)}",
            ct);
    }

    public Task<RequestMediaDetail> GetDetailAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => client.GetAsync<RequestMediaDetail>(
            $"/api/v1/requests/detail/{Uri.EscapeDataString(mediaType)}/{tmdbId}", ct);

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
        return client.GetAsync<RequestMediaPage>($"/api/v1/requests/search?{string.Join("&", qs)}", ct);
    }

    public Task<MediaRequest> CreateAsync(CreateMediaRequestInput request, CancellationToken ct = default)
        => client.PostAsync<MediaRequest>("/api/v1/requests/", request, ct);

    public Task<MediaRequestsListResponse> GetMineAsync(
        string? status = null,
        string? outcome = null,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default)
        => client.GetAsync<MediaRequestsListResponse>($"/api/v1/requests/mine{BuildListQuery(status, outcome, limit, offset)}", ct);

    public Task<MediaRequest> CancelAsync(string id, CancellationToken ct = default)
        => client.PostAsync<MediaRequest>($"/api/v1/requests/{Uri.EscapeDataString(id)}/cancel", new Dictionary<string, object?>(), ct);

    private static string BuildListQuery(string? status, string? outcome, int limit, int offset)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            qs.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(outcome) && !string.Equals(outcome, "all", StringComparison.OrdinalIgnoreCase))
            qs.Add($"outcome={Uri.EscapeDataString(outcome)}");
        if (limit > 0) qs.Add($"limit={limit}");
        if (offset > 0) qs.Add($"offset={offset}");
        return qs.Count == 0 ? "" : "?" + string.Join("&", qs);
    }
}
