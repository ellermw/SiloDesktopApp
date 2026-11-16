using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Api;

/// <summary>Discovery authority survives transport catch-up, but not identity or access changes.</summary>
public readonly record struct RequestDiscoveryOwner(ApiIdentityContext Identity, long AccessGeneration);

public class RequestsApi(SiloApiClient client, SiloPlayer.Core.Services.AuthService? auth = null)
{
    private RequestDiscoveryOwner? _discoverContext;
    private bool _discoverRowsSupported;
    private RequestFeatureStatus? _status;
    private RequestDiscoveryOwner? _statusContext;
    public event Action? StatusChanged;
    // Read live profile metadata so retained callbacks respect editor saves
    // without another status/profile request. Account denial still permits discovery.
    public bool ProfileRequestsAllowed => auth?.SelectedProfile?.RequestsAllowed != false;
    public bool DiscoveryAllowed => ProfileRequestsAllowed && (!HasCurrentStatus || _status!.RequestsEnabled);
    public bool SubmissionAllowed => DiscoveryAllowed && (!HasCurrentStatus || _status!.Allowed != false);
    // Cache validation must not call IsCurrentDiscoveryOwner: that predicate reads DiscoveryAllowed.
    private bool HasCurrentStatus => _status != null && _statusContext is { } owner && HasDiscoveryAuthority(owner);
    private bool HasDiscoveryAuthority(RequestDiscoveryOwner owner) =>
        client.IsCurrentIdentityContext(owner.Identity) && client.CaptureContext().AccessGeneration == owner.AccessGeneration;
    public RequestDiscoveryOwner CaptureDiscoveryOwner() => new(client.CaptureIdentityContext(), client.CaptureContext().AccessGeneration);
    public bool IsCurrentDiscoveryOwner(RequestDiscoveryOwner owner) => HasDiscoveryAuthority(owner) && DiscoveryAllowed;

    private async Task<T> DiscoverAsync<T>(Func<Task<T>> read)
    {
        var owner = CaptureDiscoveryOwner();
        if (!DiscoveryAllowed) throw new OperationCanceledException("Request discovery is unavailable for this profile.");
        var result = await read();
        if (!IsCurrentDiscoveryOwner(owner)) throw new OperationCanceledException("Request viewer changed.");
        return result;
    }
    private async Task MutateAsync(Func<Task> write, bool submission = false)
    {
        if (!DiscoveryAllowed || submission && !SubmissionAllowed)
            throw new OperationCanceledException("Request action is unavailable for this profile.");
        await write();
    }
    private async Task<T> MutateAsync<T>(Func<Task<T>> write, bool submission = false)
    {
        if (!DiscoveryAllowed || submission && !SubmissionAllowed)
            throw new OperationCanceledException("Request action is unavailable for this profile.");
        return await write();
    }
    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);
    public Task<MediaRequest> GetAsync(string id, CancellationToken ct = default)
        => client.GetAsync<MediaRequest>($"/api/v2/requests/{Uri.EscapeDataString(id)}", ct);

    // Like the WebUI toggle, Follow uses the normal server-authorized route under account denial.
    // Discovery/profile gates still apply, and the server refusal is returned to the active caller.
    public Task FollowAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => MutateAsync(() => client.PutAsync<object>($"/api/v2/requests/follows/{Uri.EscapeDataString(mediaType)}/{tmdbId}", new Dictionary<string, object?>(), ct));

    public Task UnfollowAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => UnfollowAsync(mediaType, tmdbId, CaptureIdentityContext(), ct);

    // Unfollow is permitted by the server even after requests are disabled or denied.
    public Task UnfollowAsync(string mediaType, int tmdbId, ApiIdentityContext owner, CancellationToken ct = default)
        => client.SendIdentityBoundNoContentAsync(owner, HttpMethod.Delete,
            $"/api/v2/requests/follows/{Uri.EscapeDataString(mediaType)}/{tmdbId}", null, ct);

    public Task<List<WatchlistTitle>> GetWatchlistTitlesAsync(CancellationToken ct = default)
        => DiscoverAsync(() => BrowseV2.AllAsync<WatchlistTitle>(client, "/api/v2/watchlist/titles", ct, 200));

    public Task<WatchlistTitleEntry> AddWatchlistTitleAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => MutateAsync(() => client.PutAsync<WatchlistTitleEntry>($"/api/v2/watchlist/titles/{Uri.EscapeDataString(mediaType)}/{tmdbId}", new Dictionary<string, object?>(), ct));

    public Task RemoveWatchlistTitleAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => MutateAsync(() => client.DeleteAsync($"/api/v2/watchlist/titles/{Uri.EscapeDataString(mediaType)}/{tmdbId}", ct));

    public async Task<RequestFeatureStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var context = CaptureDiscoveryOwner();
        var status = await client.GetAsync<RequestFeatureStatus>("/api/v2/requests/status", ct);
        if (!HasDiscoveryAuthority(context)) throw new OperationCanceledException("Request viewer changed.", ct);
        _discoverContext = context; _discoverRowsSupported = status.DiscoverRowsSupported; _status = status; _statusContext = context;
        StatusChanged?.Invoke();
        return status;
    }

    /// <summary>Blocked profiles see the sidebar entry only when they have their own history.</summary>
    public async Task<bool> ShouldShowNavigationAsync(RequestFeatureStatus status, CancellationToken ct = default)
    {
        var owner = CaptureDiscoveryOwner();
        if (!status.RequestsEnabled) return false;
        if (ProfileRequestsAllowed) return true;
        var history = await GetMineAsync(limit: 1, ct: ct);
        return HasDiscoveryAuthority(owner) && history.Requests.Count > 0;
    }

    public async Task<RequestDiscoveryResponse> GetDiscoveryAsync(CancellationToken ct = default)
    {
        var context = CaptureDiscoveryOwner();
        var page = await DiscoverAsync(() => client.GetAsync<DiscoverCollection>("/api/v2/requests/discover", ct));
        if (!HasDiscoveryAuthority(context)) throw new OperationCanceledException("Request viewer changed.", ct);
        if (page.RowOrder != null) { _discoverContext = context; _discoverRowsSupported = true; }
        return new() { Sections = page.Items, RowOrder = page.RowOrder ?? (UsesDiscoverRows ? [] : null) };
    }

    private bool UsesDiscoverRows => _discoverRowsSupported && _discoverContext is { } owner && HasDiscoveryAuthority(owner);
    private sealed class DiscoverCollection
    {
        public List<RequestDiscoverySection> Items { get; set; } = [];
        public List<RequestDiscoverRow>? RowOrder { get; set; }
    }

    public Task<RequestDiscoverySection> GetDiscoverySectionAsync(string section, int page = 1, CancellationToken ct = default)
        => DiscoverAsync(() => client.GetAsync<RequestDiscoverySection>(
            $"/api/v2/requests/discover/{(UsesDiscoverRows ? "rows/" : "")}{Uri.EscapeDataString(section)}?page={page}", ct));

    public async Task<DiscoverStudiosResponse> GetDiscoverStudiosAsync(CancellationToken ct = default)
        => new() { Studios = (await DiscoverAsync(() => client.GetAsync<BrowseCollection<DiscoverBrandCard>>("/api/v2/requests/discover/studios", ct))).Items };

    public async Task<DiscoverNetworksResponse> GetDiscoverNetworksAsync(CancellationToken ct = default)
        => new() { Networks = (await DiscoverAsync(() => client.GetAsync<BrowseCollection<DiscoverBrandCard>>("/api/v2/requests/discover/networks", ct))).Items };

    public async Task<DiscoverGenresResponse> GetDiscoverGenresAsync(CancellationToken ct = default)
        => new() { Genres = (await DiscoverAsync(() => client.GetAsync<BrowseCollection<DiscoverBrandCard>>("/api/v2/requests/discover/genres", ct))).Items };

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
        return DiscoverAsync(() => client.GetAsync<DiscoverBrowseResponse>(
            $"/api/v2/requests/discover/browse/{Uri.EscapeDataString(kind)}/{Uri.EscapeDataString(slug)}?{string.Join("&", qs)}", ct));
    }

    public Task<RequestMediaDetail> GetDetailAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => DiscoverAsync(() => client.GetAsync<RequestMediaDetail>(
            $"/api/v2/requests/detail/{Uri.EscapeDataString(mediaType)}/{tmdbId}", ct));

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
        return DiscoverAsync(() => client.GetAsync<RequestMediaPage>($"/api/v2/requests/search?{string.Join("&", qs)}", ct));
    }

    public Task<MediaRequest> CreateAsync(CreateMediaRequestInput request, CancellationToken ct = default)
        => MutateAsync(() => client.PostAsync<MediaRequest>("/api/v2/requests", System.Text.Json.JsonSerializer.SerializeToElement(request, V2Json.Options), ct), submission: true);

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

    /// <summary>Cancels under the confirming account/profile, independent of read-context invalidation.</summary>
    public Task<MediaRequest> CancelAsync(string id, ApiIdentityContext owner, CancellationToken ct = default)
        => client.SendIdentityBoundRequestAsync<MediaRequest>(owner, HttpMethod.Post, $"/api/v2/requests/{Uri.EscapeDataString(id)}/cancel", new Dictionary<string, object?>(), ct);

    /// <summary>
    /// Deletes the viewer's failed, undelivered request (<c>removeFailedRequest</c>). Distinct from
    /// <see cref="CancelAsync(string, CancellationToken)"/>: this is a bodyless DELETE answering 204, and the server refuses
    /// with 409 when the request was retried, completed or delivered a copy before failing. The
    /// request is bound to the initiating account/profile so a switch cannot re-author the deletion.
    /// </summary>
    public Task RemoveFailedAsync(string id, ApiIdentityContext owner, CancellationToken ct = default)
        => client.SendIdentityBoundNoContentAsync(owner, HttpMethod.Delete, $"/api/v2/requests/{Uri.EscapeDataString(id)}", null, ct);

    public ApiIdentityContext CaptureIdentityContext() => client.CaptureIdentityContext();
    public bool IsCurrentIdentityContext(ApiIdentityContext context) => client.IsCurrentIdentityContext(context);

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
