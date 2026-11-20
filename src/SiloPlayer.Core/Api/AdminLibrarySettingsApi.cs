using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>Bounded auxiliary reads for Manage &gt; Libraries &gt; Settings.</summary>
public sealed class AdminLibrarySettingsApi(SiloApiClient client)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
    public const int MaxRatingSources = 512;

    public async Task<bool> SupportsPluginRatingSourcesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        using var timeout = Timeout(lease, ct);
        try
        {
            var response = await client.MonitorLogsSendAsync<CapabilitiesResponse>(lease, HttpMethod.Get,
                "/api/v2/admin/rating-sources/capabilities", ct: timeout.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            return response.Body.PluginDeclaredSources
                ?? throw new InvalidDataException("Invalid rating source capability response.");
        }
        catch (OperationCanceledException) { throw; }
        catch (ApiException error) { throw AdminServerSettingsApi.Sanitize(error); }
    }

    public async Task<IReadOnlyList<AdminRatingSource>> ListRatingSourcesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        using var timeout = Timeout(lease, ct);
        CollectionResponse response;
        try
        {
            var wire = await client.MonitorLogsSendAsync<CollectionResponse>(lease, HttpMethod.Get,
                "/api/v2/admin/rating-sources", ct: timeout.Token).ConfigureAwait(false);
            response = wire.Body;
        }
        catch (OperationCanceledException) { throw; }
        catch (ApiException error) { throw AdminServerSettingsApi.Sanitize(error); }
        lease.ThrowIfRetired();
        if (response.Items == null || response.Items.Count > MaxRatingSources)
            throw new InvalidDataException("Invalid rating source collection.");
        var result = new List<AdminRatingSource>(response.Items.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in response.Items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Source) || item.Source.Length > 32 ||
                string.IsNullOrWhiteSpace(item.Label) || item.Label.Length > 200 ||
                string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 80 ||
                item.Provider is { Length: > 200 } || !seen.Add(item.Source))
                throw new InvalidDataException("Invalid rating source collection.");
            result.Add(new(item.Source, item.Label, item.Name, item.AlwaysShown, string.IsNullOrWhiteSpace(item.Provider) ? null : item.Provider));
        }
        return result.AsReadOnly();
    }

    private static CancellationTokenSource Timeout(MonitorAuthorityLease lease, CancellationToken ct)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        timeout.CancelAfter(Deadline);
        return timeout;
    }

    private sealed record CapabilitiesResponse { public bool? PluginDeclaredSources { get; init; } }
    private sealed record CollectionResponse { public List<RatingSourceResponse?>? Items { get; init; } }
    private sealed record RatingSourceResponse
    {
        public string Source { get; init; } = "";
        public string Label { get; init; } = "";
        public string Name { get; init; } = "";
        public bool AlwaysShown { get; init; }
        public string? Provider { get; init; }
    }
}
