using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Api;

/// <summary>Discovery authority survives transport catch-up, but not identity or access changes.</summary>
public readonly record struct RequestDiscoveryOwner(ApiIdentityContext Identity, long AccessGeneration);

public class RequestsApi(SiloApiClient client, SiloPlayer.Core.Services.AuthService? auth = null, TimeProvider? clock = null)
{
    private RequestDiscoveryOwner? _discoverContext;
    private bool _discoverRowsSupported;
    private readonly object _statusGate = new();
    private StatusSnapshot? _status;
    private StatusFlight? _statusFlight;
    private TimeProvider Clock => clock ?? TimeProvider.System;
    private sealed record StatusKey(RequestDiscoveryOwner Owner, ApiRequestContext Request, long Revision);
    private sealed record StatusSnapshot(StatusKey Key, RequestFeatureStatus Value, long ReadAt);
    private sealed class StatusFlight(StatusKey key)
    {
        public StatusKey Key { get; } = key;
        public CancellationTokenSource Cancellation { get; } = new(TimeSpan.FromSeconds(30));
        public TaskCompletionSource<RequestFeatureStatus> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Waiters;
    }
    public event Action? StatusChanged;
    // Read live profile metadata so retained callbacks respect editor saves
    // without another status/profile request. Account denial still permits discovery.
    public bool ProfileRequestsAllowed => auth?.SelectedProfile?.RequestsAllowed != false;
    public bool DiscoveryAllowed => ProfileRequestsAllowed && CurrentStatus?.RequestsEnabled != false;
    public bool SubmissionAllowed
    {
        get
        {
            var status = CurrentStatus;
            return ProfileRequestsAllowed && status?.RequestsEnabled != false && status?.Allowed != false;
        }
    }
    // Cache validation must not call IsCurrentDiscoveryOwner: that predicate reads DiscoveryAllowed.
    private RequestFeatureStatus? CurrentStatus => Volatile.Read(ref _status) is { } status && HasDiscoveryAuthority(status.Key.Owner) ? status.Value : null;
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
        await InvalidateAfterAsync(write, CaptureIdentityContext());
    }
    private async Task<T> MutateAsync<T>(Func<Task<T>> write, bool submission = false, bool settled = false)
    {
        if (!DiscoveryAllowed || submission && !SubmissionAllowed)
            throw new OperationCanceledException("Request action is unavailable for this profile.");
        return await InvalidateAfterAsync(write, settled);
    }
    private async Task<T> InvalidateAfterAsync<T>(Func<Task<T>> write, bool settled, ApiIdentityContext? owner = null)
    {
        var identity = owner ?? CaptureIdentityContext();
        var success = false;
        try
        {
            var result = await write().ConfigureAwait(false);
            success = true;
            return result;
        }
        finally
        {
            if ((success || settled) && IsCurrentIdentityContext(identity)) InvalidateStatus();
        }
    }
    private async Task InvalidateAfterAsync(Func<Task> write, ApiIdentityContext owner)
    {
        try { await write().ConfigureAwait(false); }
        finally { if (IsCurrentIdentityContext(owner)) InvalidateStatus(); }
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
        => InvalidateAfterAsync(() => client.SendIdentityBoundNoContentAsync(owner, HttpMethod.Delete,
            $"/api/v2/requests/follows/{Uri.EscapeDataString(mediaType)}/{tmdbId}", null, ct), owner);

    public Task<List<WatchlistTitle>> GetWatchlistTitlesAsync(CancellationToken ct = default)
        => DiscoverAsync(() => BrowseV2.AllAsync<WatchlistTitle>(client, "/api/v2/watchlist/titles", ct, 200));

    public Task<WatchlistTitleEntry> AddWatchlistTitleAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => MutateAsync(() => client.PutAsync<WatchlistTitleEntry>($"/api/v2/watchlist/titles/{Uri.EscapeDataString(mediaType)}/{tmdbId}", new Dictionary<string, object?>(), ct), settled: true);

    public Task RemoveWatchlistTitleAsync(string mediaType, int tmdbId, CancellationToken ct = default)
        => MutateAsync(() => client.DeleteAsync($"/api/v2/watchlist/titles/{Uri.EscapeDataString(mediaType)}/{tmdbId}", ct));

    public async Task<RequestFeatureStatus> GetStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = new StatusKey(CaptureDiscoveryOwner(), client.CaptureContext(), client.RequestStatusRevision);
        StatusFlight flight;
        StatusFlight? superseded = null;
        var start = false;
        lock (_statusGate)
        {
            if (!StatusKeyIsCurrent(key)) throw new OperationCanceledException("Request viewer changed.", ct);
            if (_status is { } snapshot && snapshot.Key == key && Clock.GetElapsedTime(snapshot.ReadAt) < TimeSpan.FromSeconds(30))
                return snapshot.Value.Copy();
            if (_status is { } old && old.Key.Owner != key.Owner) Volatile.Write(ref _status, null);
            if (_statusFlight is { } pending && pending.Key == key) flight = pending;
            else
            {
                superseded = _statusFlight;
                _statusFlight = flight = new(key);
                start = true;
            }
            flight.Waiters++;
        }

        if (superseded != null) Cancel(superseded);
        if (start) _ = FetchStatusAsync(flight);
        try
        {
            var status = await flight.Result.Task.WaitAsync(ct).ConfigureAwait(false);
            if (!StatusKeyIsCurrent(key)) throw new OperationCanceledException("Request viewer changed.", ct);
            return status.Copy();
        }
        finally
        {
            var cancel = false;
            lock (_statusGate)
            {
                if (--flight.Waiters == 0 && ReferenceEquals(_statusFlight, flight) && !flight.Result.Task.IsCompleted)
                {
                    _statusFlight = null;
                    cancel = true;
                }
            }
            if (cancel) Cancel(flight);
        }
    }

    /// <summary>Retires freshness while retaining the last known permissions until the next read.</summary>
    public void InvalidateStatus() => client.InvalidateRequestStatus();

    private bool StatusKeyIsCurrent(StatusKey key) => HasDiscoveryAuthority(key.Owner) &&
        client.IsCurrentContext(key.Request) && client.RequestStatusRevision == key.Revision;

    private async Task FetchStatusAsync(StatusFlight flight)
    {
        var token = flight.Cancellation.Token;
        try
        {
            var result = await client.SendRequestAsync<RequestFeatureStatus>(flight.Key.Request, HttpMethod.Get, "/api/v2/requests/status", body: null, ct: token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            lock (_statusGate)
            {
                if (!ReferenceEquals(_statusFlight, flight) || !StatusKeyIsCurrent(flight.Key))
                    throw new OperationCanceledException("Request viewer changed.", token);
                _discoverContext = flight.Key.Owner; _discoverRowsSupported = result.DiscoverRowsSupported;
                Volatile.Write(ref _status, new(flight.Key, result, Clock.GetTimestamp()));
            }
            StatusChanged?.Invoke();
            flight.Result.TrySetResult(result);
        }
        catch (OperationCanceledException error) { flight.Result.TrySetCanceled(error.CancellationToken); }
        catch (Exception error)
        {
            flight.Result.TrySetException(error);
            _ = flight.Result.Task.Exception;
        }
        finally
        {
            lock (_statusGate) { if (ReferenceEquals(_statusFlight, flight)) _statusFlight = null; }
            flight.Cancellation.Dispose();
        }
    }

    private static void Cancel(StatusFlight flight)
    {
        try { flight.Cancellation.Cancel(); }
        catch (ObjectDisposedException) { }
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
        => CancelAsync(id, CaptureIdentityContext(), ct);

    /// <summary>Cancels under the confirming account/profile, independent of read-context invalidation.</summary>
    public Task<MediaRequest> CancelAsync(string id, ApiIdentityContext owner, CancellationToken ct = default)
        => InvalidateAfterAsync(() => client.SendIdentityBoundRequestAsync<MediaRequest>(owner, HttpMethod.Post,
            $"/api/v2/requests/{Uri.EscapeDataString(id)}/cancel", new Dictionary<string, object?>(), ct), settled: true, owner: owner);

    /// <summary>
    /// Deletes the viewer's failed, undelivered request (<c>removeFailedRequest</c>). Distinct from
    /// <see cref="CancelAsync(string, CancellationToken)"/>: this is a bodyless DELETE answering 204, and the server refuses
    /// with 409 when the request was retried, completed or delivered a copy before failing. The
    /// request is bound to the initiating account/profile so a switch cannot re-author the deletion.
    /// </summary>
    public Task RemoveFailedAsync(string id, ApiIdentityContext owner, CancellationToken ct = default)
        => InvalidateAfterAsync(() => client.SendIdentityBoundNoContentAsync(owner, HttpMethod.Delete,
            $"/api/v2/requests/{Uri.EscapeDataString(id)}", null, ct), owner);

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
