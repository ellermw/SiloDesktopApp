using System.Text.Json;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>Home section ordering against the acting-admin v2 contract.</summary>
public sealed partial class AdminHomeSectionsApi(SiloApiClient client)
{
    // The server accepts at most 10,000 IDs in a whole-order write.
    private const int MaxRows = 10_000;

    public async Task<AdminHomeSectionsSnapshot> ReadAsync(MonitorAuthorityLease lease, CancellationToken ct = default, AdminHomeSectionScope? scope = null)
    {
        scope ??= AdminHomeSectionScope.Home;
        var before = await ReadOrderCoreAsync(lease, scope, ct).ConfigureAwait(false);
        var list = await SendAsync<AdminHomeSectionList>(lease, HttpMethod.Get, Root + scope.Query, ct: ct).ConfigureAwait(false);
        if (list.Body.Page?.HasMore == true || !string.IsNullOrEmpty(list.Body.Page?.NextCursor) || list.Body.Items is null || list.Body.Items.Count > MaxRows)
            throw new InvalidDataException("The Home section list was incomplete.");
        var after = await ReadOrderCoreAsync(lease, scope, ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        if (!string.Equals(before.ETag, after.ETag, StringComparison.Ordinal))
            throw new InvalidDataException("Home sections changed while loading. Reload and review them.");
        var byId = new Dictionary<string, AdminHomeSection>(StringComparer.Ordinal);
        foreach (var row in list.Body.Items)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Id) || !scope.Matches(row) || !byId.TryAdd(row.Id, row))
                throw new InvalidDataException("The Home section list was inconsistent.");
        }
        if (byId.Count != after.Body.OrderedIds.Count || after.Body.OrderedIds.Any(id => !byId.ContainsKey(id)))
            throw new InvalidDataException("The Home section membership changed while loading.");
        return new AdminHomeSectionsSnapshot(Array.AsReadOnly(after.Body.OrderedIds.Select(id => byId[id]).ToArray()), after.ETag!, lease, scope);
    }

    public async Task ReorderAsync(MonitorAuthorityLease lease, AdminHomeSectionsSnapshot snapshot, IReadOnlyList<string> orderedIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(orderedIds);
        if (!ReferenceEquals(snapshot.Lease, lease) || !lease.IsCurrent) throw new MonitorAuthorityRetiredException();
        var known = snapshot.OrderedIds.ToHashSet(StringComparer.Ordinal);
        if (orderedIds.Count != known.Count || orderedIds.Distinct(StringComparer.Ordinal).Count() != known.Count || orderedIds.Any(id => !known.Contains(id)))
            throw new ArgumentException("The order must contain every Home section exactly once.", nameof(orderedIds));
        await MutationAsync(async () => await SendAsync<AdminHomeSectionOrder>(lease, HttpMethod.Put, Root + "/order" + snapshot.Scope.Query,
            new Dictionary<string, object?> { ["ordered_ids"] = orderedIds }, snapshot.ETag, ct).ConfigureAwait(false), lease).ConfigureAwait(false);
    }

    public Task<ApiResponse<AdminHomeSectionOrder>> ReadOrderAsync(MonitorAuthorityLease lease, AdminHomeSectionScope scope, CancellationToken ct = default) => ReadOrderCoreAsync(lease, scope, ct);

    private async Task<ApiResponse<AdminHomeSectionOrder>> ReadOrderCoreAsync(MonitorAuthorityLease lease, AdminHomeSectionScope scope, CancellationToken ct)
    {
        var response = await SendAsync<AdminHomeSectionOrder>(lease, HttpMethod.Get, Root + "/order" + scope.Query, ct: ct).ConfigureAwait(false);
        var order = response.Body;
        if (!AdminValidators.IsStrong(response.ETag) || order.Scope != scope.Kind || order.LibraryId != scope.LibraryId || order.OrderedIds is null ||
            order.OrderedIds.Count > MaxRows || order.OrderedIds.Any(string.IsNullOrWhiteSpace) || order.OrderedIds.Distinct(StringComparer.Ordinal).Count() != order.OrderedIds.Count)
            throw new InvalidDataException("The Home section order or its version was invalid.");
        return response;
    }
}

public sealed class AdminHomeSectionsSnapshot
{
    internal AdminHomeSectionsSnapshot(IReadOnlyList<AdminHomeSection> rows, string etag, MonitorAuthorityLease lease, AdminHomeSectionScope scope)
    { Rows = rows; OrderedIds = Array.AsReadOnly(rows.Select(row => row.Id).ToArray()); ETag = etag; Lease = lease; Scope = scope; }
    public AdminHomeSectionScope Scope { get; }
    public IReadOnlyList<AdminHomeSection> Rows { get; }
    internal IReadOnlyList<string> OrderedIds { get; }
    public string ETag { get; }
    internal MonitorAuthorityLease Lease { get; }
}

public sealed class AdminHomeSection
{
    public string Id { get; set; } = "";
    public string Scope { get; set; } = "";
    public string? LibraryId { get; set; }
    public int Position { get; set; }
    public string SectionType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? TitleKey { get; set; }
    public Dictionary<string, string>? TitleArgs { get; set; }
    public bool Featured { get; set; }
    public int ItemLimit { get; set; }
    public JsonElement Config { get; set; }
    public bool Enabled { get; set; }
    public bool WebOnly { get; set; }
}

public sealed class AdminHomeSectionOrder
{
    public string Scope { get; set; } = "";
    public string? LibraryId { get; set; }
    public List<string>? OrderedIds { get; set; }
}

public sealed class AdminHomeSectionList
{
    public List<AdminHomeSection>? Items { get; set; }
    public ApiPageInfo? Page { get; set; }
}
