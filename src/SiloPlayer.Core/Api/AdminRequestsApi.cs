using System.Text.Json.Nodes;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>Official Admin Requests contracts at 217ff25. This API never uses viewer permission/cache state.</summary>
public sealed class AdminRequestsApi(SiloApiClient client)
{
    private const string Root = "/api/v2/admin/";
    public const int QueuePageSize = 50;
    public const int RetainedQueueLimit = 500;
    public const int LookupLimit = 500;

    public async Task<JsonObject> GetCapabilitiesAsync(MonitorAuthorityLease owner, CancellationToken ct = default) =>
        (await client.AdminRequestsSendAsync(owner, HttpMethod.Get, Root + "requests/capabilities", ct: ct).ConfigureAwait(false)).Body;

    public async Task<JsonObject> GetCountsAsync(MonitorAuthorityLease owner, CancellationToken ct = default)
    {
        var counts = (await client.AdminRequestsSendAsync(owner, HttpMethod.Get, Root + "requests/counts", ct: ct).ConfigureAwait(false)).Body;
        foreach (var name in AdminRequestRules.Views)
            if (counts[name] is not JsonValue value || !value.TryGetValue<int>(out var number) || number < 0)
                throw new InvalidDataException("Invalid request counts.");
        return counts;
    }

    public async Task<JsonObject> ListQueueAsync(MonitorAuthorityLease owner, string view, string search = "", string mediaType = "",
        string? userId = null, string? cursor = null, CancellationToken ct = default)
    {
        if (!AdminRequestRules.Views.Contains(view)) throw new ArgumentException("Invalid queue view.");
        if (mediaType.Length > 0) Media(mediaType);
        if (userId != null) DecimalId(userId);
        if (search.Length > 200) throw new ArgumentException("Search must be 200 characters or fewer.");
        var path = Root + "requests?view=" + view;
        if (search.Trim().Length > 0) path += "&q=" + Uri.EscapeDataString(search.Trim());
        if (mediaType.Length > 0) path += "&media_type=" + mediaType;
        if (userId != null) path += "&requested_by_user_id=" + userId;
        var body = (await client.AdminRequestsSendAsync(owner, HttpMethod.Get, AdminPaging.WithPage(path, QueuePageSize, cursor), ct: ct).ConfigureAwait(false)).Body;
        ValidatePage(body, QueuePageSize, cursor);
        return body;
    }

    public async Task<JsonArray> GetEventsAsync(MonitorAuthorityLease owner, string id, CancellationToken ct = default)
    {
        OpaqueId(id);
        var body = (await client.AdminRequestsSendAsync(owner, HttpMethod.Get, Root + "requests/" + Uri.EscapeDataString(id) + "/events", ct: ct).ConfigureAwait(false)).Body;
        return CompleteItems(body, 200);
    }

    public async Task<JsonObject> ActionAsync(MonitorAuthorityLease owner, string id, string action, string? reason = null, CancellationToken ct = default)
    {
        OpaqueId(id);
        if (action is not ("approve" or "decline" or "retry" or "cancel")) throw new ArgumentException("Invalid request action.");
        var body = new JsonObject();
        if (action is "decline" or "cancel" && !string.IsNullOrWhiteSpace(reason)) body["reason"] = reason.Trim();
        try
        {
            var result = (await client.AdminRequestsSendAsync(owner, HttpMethod.Post, Root + "requests/" + Uri.EscapeDataString(id) + "/" + action, body, ct: ct).ConfigureAwait(false)).Body;
            if (AdminRequestJson.Text(result, "id") != id) throw new InvalidDataException("The server answered for another request.");
            return result;
        }
        finally { if (owner.IsCurrent) client.InvalidateRequestStatus(); }
    }

    public Task<AdminRequestDocument> GetSettingsAsync(MonitorAuthorityLease owner, CancellationToken ct = default) => ReadDocumentAsync(owner, "request-settings", ct);
    public Task<AdminRequestDocument> GetLayoutAsync(MonitorAuthorityLease owner, CancellationToken ct = default) => ReadDocumentAsync(owner, "request-discover/layout", ct);
    public Task<AdminRequestDocument> GetRoutingAsync(MonitorAuthorityLease owner, CancellationToken ct = default) => ReadDocumentAsync(owner, "request-routing", ct);
    public Task<AdminRequestDocument> GetIntegrationAsync(MonitorAuthorityLease owner, string id, CancellationToken ct = default) { OpaqueId(id); return ReadDocumentAsync(owner, "request-integrations/" + Uri.EscapeDataString(id), ct); }
    public Task<AdminRequestDocument> GetRouteAsync(MonitorAuthorityLease owner, string id, CancellationToken ct = default) { OpaqueId(id); return ReadDocumentAsync(owner, "request-routes/" + Uri.EscapeDataString(id), ct); }

    private async Task<AdminRequestDocument> ReadDocumentAsync(MonitorAuthorityLease owner, string resource, CancellationToken ct)
    {
        var response = await client.AdminRequestsSendAsync(owner, HttpMethod.Get, Root + resource, ct: ct).ConfigureAwait(false);
        AdminValidators.RequireStrong(response.ETag);
        ValidateDocument(resource, response.Body);
        if (resource.StartsWith("request-integrations/", StringComparison.Ordinal) || resource.StartsWith("request-routes/", StringComparison.Ordinal))
            if (AdminRequestJson.Text(response.Body, "id") != Uri.UnescapeDataString(resource[(resource.IndexOf('/') + 1)..]))
                throw new InvalidDataException("The editor response names another record.");
        if (resource == "request-discover/layout" && (response.Body["rows"] is not JsonArray rows || rows.Count > 30 || AdminRequestJson.Number(response.Body, "max_rows") is < 1 or > 30))
            throw new InvalidDataException("Invalid Discover layout.");
        return new(response.Body, response.ETag!, resource, owner);
    }

    public async Task<AdminRequestDocument> SaveDocumentAsync(MonitorAuthorityLease owner, AdminRequestDocument current, JsonObject changes, CancellationToken ct = default)
    {
        current.RequireOwner(owner);
        var body = AdminRequestRules.WriteBody(current.Resource, changes);
        var response = await client.AdminRequestsSendAsync(owner, HttpMethod.Put, Root + current.Resource, body, current.ETag, ct).ConfigureAwait(false);
        if (current.Resource == "request-settings" || current.Resource.StartsWith("request-integrations/", StringComparison.Ordinal))
            client.InvalidateRequestStatus();
        AdminValidators.RequireStrong(response.ETag);
        ValidateDocument(current.Resource, response.Body);
        if (current.Value["id"] != null && AdminRequestJson.Text(current.Value, "id") != AdminRequestJson.Text(response.Body, "id"))
            throw new InvalidDataException("The server answered for another record. Reload before saving again.");
        return new(response.Body, response.ETag!, current.Resource, owner);
    }

    public async Task DeleteDocumentAsync(MonitorAuthorityLease owner, AdminRequestDocument current, CancellationToken ct = default)
    {
        current.RequireOwner(owner);
        if (current.Resource != "request-discover/layout" && !current.Resource.StartsWith("request-integrations/", StringComparison.Ordinal) &&
            !current.Resource.StartsWith("request-routes/", StringComparison.Ordinal)) throw new ArgumentException("This record cannot be deleted.");
        if (AdminRequestJson.Flag(current.Value, "is_fallback")) throw new ArgumentException("Everything else cannot be deleted.");
        await client.AdminRequestsSendAsync(owner, HttpMethod.Delete, Root + current.Resource, etag: current.ETag, ct: ct).ConfigureAwait(false);
        if (current.Resource.StartsWith("request-integrations/", StringComparison.Ordinal)) client.InvalidateRequestStatus();
    }

    public async Task<AdminRequestDocument> CreateIntegrationAsync(MonitorAuthorityLease owner, JsonObject body, CancellationToken ct = default) =>
        await CreateAsync(owner, "request-integrations", AdminRequestRules.WriteBody("request-integrations/new", body), ct).ConfigureAwait(false);
    public async Task<AdminRequestDocument> CreateRouteAsync(MonitorAuthorityLease owner, JsonObject body, CancellationToken ct = default) =>
        await CreateAsync(owner, "request-routes", AdminRequestRules.WriteBody("request-routes/new", body), ct).ConfigureAwait(false);
    private async Task<AdminRequestDocument> CreateAsync(MonitorAuthorityLease owner, string resource, JsonObject body, CancellationToken ct)
    {
        var response = await client.AdminRequestsSendAsync(owner, HttpMethod.Post, Root + resource, body, ct: ct).ConfigureAwait(false);
        if (resource == "request-integrations") client.InvalidateRequestStatus();
        var id = AdminRequestJson.Text(response.Body, "id");
        // The POST was accepted, so the record may exist: an unusable reply is an unconfirmed result, not a pre-send validation error.
        try { OpaqueId(id); }
        catch (ArgumentException error) { throw new InvalidDataException("The server did not return a usable identity for the new record.", error); }
        AdminValidators.RequireStrong(response.ETag);
        return new(response.Body, response.ETag!, resource + "/" + Uri.EscapeDataString(id), owner);
    }

    /// <summary>At most ten pages / 500 servers. A truncated picker is refused, never used for routing decisions.</summary>
    public async Task<IReadOnlyList<AdminRequestDocument>> ListIntegrationsAsync(MonitorAuthorityLease owner, CancellationToken ct = default)
    {
        var items = await BoundedCollectionAsync(owner, "request-integrations", LookupLimit, ct).ConfigureAwait(false);
        var rows = new List<AdminRequestDocument>();
        foreach (var item in items) rows.Add(await GetIntegrationAsync(owner, AdminRequestJson.Text(item, "id"), ct).ConfigureAwait(false));
        return rows;
    }

    /// <summary>Canonical per-rule reads supply validators; the complete snapshot owns reordering.</summary>
    public async Task<AdminRequestRoutes> ListRoutesAsync(MonitorAuthorityLease owner, CancellationToken ct = default)
    {
        var body = (await client.AdminRequestsSendAsync(owner, HttpMethod.Get, Root + "request-routes", ct: ct).ConfigureAwait(false)).Body;
        var rows = new List<AdminRequestDocument>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in CompleteItems(body, 202))
        {
            var id = AdminRequestJson.Text(item, "id"); if (!seen.Add(id)) throw new InvalidDataException("The routing list repeats a rule.");
            rows.Add(await GetRouteAsync(owner, id, ct).ConfigureAwait(false));
        }
        return new(rows, owner);
    }

    public async Task ReorderRoutesAsync(MonitorAuthorityLease owner, AdminRequestRoutes snapshot, string mediaType, IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        Media(mediaType); snapshot.RequireOwner(owner);
        var expected = snapshot.Rows.Where(r => AdminRequestJson.Text(r.Value, "media_type") == mediaType && !AdminRequestJson.Flag(r.Value, "is_fallback"))
            .Select(r => AdminRequestJson.Text(r.Value, "id")).ToHashSet(StringComparer.Ordinal);
        if (ids.Count > 100 || ids.Count != expected.Count || ids.Distinct().Count() != ids.Count || !ids.All(expected.Contains)) throw new ArgumentException("Order must contain every rule exactly once, excluding Everything else.");
        await client.AdminRequestsSendAsync(owner, HttpMethod.Post, Root + "request-routes/order",
            new JsonObject { ["media_type"] = mediaType, ["ids"] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()) }, ct: ct).ConfigureAwait(false);
    }

    public async Task<JsonObject> LoadOptionsAsync(MonitorAuthorityLease owner, string id, JsonObject body, CancellationToken ct = default)
    {
        OpaqueId(id);
        var input = AdminRequestJson.Pick(body, "base_url", "api_key_ref", "capability_id", "installation_id", "plugin_config");
        if (input.ContainsKey("base_url")) { var url = AdminRequestJson.Text(input, "base_url").Trim(); input["base_url"] = url.Length > 0 && !url.Contains("://", StringComparison.Ordinal) ? "http://" + url : url; }
        return (await client.AdminRequestsSendAsync(owner, HttpMethod.Post, Root + "request-integrations/" + Uri.EscapeDataString(id) + "/options", input, ct: ct).ConfigureAwait(false)).Body;
    }

    public async Task<JsonObject> PreviewRowAsync(MonitorAuthorityLease owner, JsonObject row, CancellationToken ct = default) =>
        (await client.AdminRequestsSendAsync(owner, HttpMethod.Post, Root + "request-discover/preview", AdminRequestRules.RowBody(row), ct: ct).ConfigureAwait(false)).Body;

    public async Task<JsonObject> PreviewRouteAsync(MonitorAuthorityLease owner, string mediaType, int tmdbId, string? userId = null, CancellationToken ct = default)
    {
        Media(mediaType); if (tmdbId <= 0) throw new ArgumentException("Choose a title."); if (userId != null) DecimalId(userId);
        var body = new JsonObject { ["media_type"] = mediaType, ["tmdb_id"] = tmdbId };
        if (userId != null) body["requester_user_id"] = userId;
        return (await client.AdminRequestsSendAsync(owner, HttpMethod.Post, Root + "request-routes/preview", body, ct: ct).ConfigureAwait(false)).Body;
    }

    public async Task<JsonArray> SearchRouteTitlesAsync(MonitorAuthorityLease owner, string mediaType, string query, CancellationToken ct = default)
    {
        Media(mediaType); if (string.IsNullOrWhiteSpace(query) || query.Length > 200) throw new ArgumentException("Enter a title, up to 200 characters.");
        return CompleteItems((await client.AdminRequestsSendAsync(owner, HttpMethod.Get, Root + "request-routes/titles?media_type=" + mediaType + "&q=" + Uri.EscapeDataString(query.Trim()), ct: ct).ConfigureAwait(false)).Body, 100);
    }

    public async Task<JsonArray> WatchProvidersAsync(MonitorAuthorityLease owner, string mediaType, string region, CancellationToken ct = default)
    {
        Media(mediaType); if (region.Length != 2 || !region.All(char.IsAsciiLetter)) throw new ArgumentException("Enter a two-letter region.");
        return CompleteItems((await client.AdminRequestsSendAsync(owner, HttpMethod.Get, Root + "request-discover/watch-providers?media_type=" + mediaType + "&region=" + region.ToUpperInvariant(), ct: ct).ConfigureAwait(false)).Body, 1000);
    }

    public Task<JsonArray> ListUsersAsync(MonitorAuthorityLease owner, CancellationToken ct = default) => BoundedCollectionAsync(owner, "users", LookupLimit, ct);
    public Task<JsonArray> ListPluginsAsync(MonitorAuthorityLease owner, CancellationToken ct = default) => BoundedCollectionAsync(owner, "plugins/installations", 200, ct);

    public async Task<JsonObject> ListSourcesAsync(MonitorAuthorityLease owner, string kind, string query = "", CancellationToken ct = default)
    {
        var path = kind switch
        {
            "capabilities" => "/api/v2/admin/collections/capabilities",
            "templates" => "/api/v2/admin/collections/templates",
            "top" => "/api/v2/collections/import/mdblist/top",
            "search" => "/api/v2/collections/import/mdblist/search?q=" + Uri.EscapeDataString(query.Trim()),
            _ => throw new ArgumentException("Invalid list source."),
        };
        return (await client.AdminRequestsSendAsync(owner, HttpMethod.Get, path, ct: ct).ConfigureAwait(false)).Body;
    }

    private async Task<JsonArray> BoundedCollectionAsync(MonitorAuthorityLease owner, string resource, int maximum, CancellationToken ct)
    {
        var all = new JsonArray(); string? cursor = null; var seen = new HashSet<string>(StringComparer.Ordinal); var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < 10; page++)
        {
            var body = (await client.AdminRequestsSendAsync(owner, HttpMethod.Get, AdminPaging.WithPage(Root + resource, 50, cursor), ct: ct).ConfigureAwait(false)).Body;
            ValidatePage(body, 50, cursor);
            foreach (var row in body["items"]!.AsArray())
            {
                if (all.Count >= maximum || !ids.Add(AdminRequestJson.Text(row, "id"))) throw new InvalidDataException("This list exceeds the native editor's safe bound or repeats records.");
                all.Add(row!.DeepClone());
            }
            var next = NextCursor(body); if (next == null) return all;
            if (!seen.Add(next)) throw new InvalidDataException("The list repeated its continuation.");
            cursor = next;
        }
        throw new InvalidDataException("This list exceeds the native editor's safe bound. Narrow it on the server before editing here.");
    }

    public static string? NextCursor(JsonObject page) => AdminRequestJson.Flag(page["page"], "has_more") ? AdminRequestJson.Text(page["page"], "next_cursor") : null;
    private static void ValidatePage(JsonObject body, int maximum, string? cursor)
    {
        var items = body["items"] as JsonArray ?? throw new InvalidDataException("Missing list items.");
        if (body["page"] is not JsonObject page || page["has_more"] == null || items.Count > maximum) throw new InvalidDataException("Invalid queue page.");
        var next = NextCursor(body);
        if (next != null && (items.Count == 0 || !AdminPaging.IsValidCursor(next) || next == cursor)) throw new InvalidDataException("Invalid list continuation.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in items) if (!ids.Add(AdminRequestJson.Text(row, "id")) || AdminRequestJson.Text(row, "id").Length == 0) throw new InvalidDataException("Missing or duplicate record identity.");
    }
    private static JsonArray CompleteItems(JsonObject body, int maximum)
    {
        var items = body["items"] as JsonArray ?? throw new InvalidDataException("Missing list items.");
        if (items.Count > maximum || AdminRequestJson.Flag(body["page"], "has_more")) throw new InvalidDataException("The server returned an incomplete list.");
        return items;
    }
    private static void ValidateDocument(string resource, JsonObject body)
    {
        if (resource != "request-settings") return;
        foreach (var name in new[] { "requests_enabled", "global_auto_approval_enabled", "force_dual_quality" })
            if (body[name] is not JsonValue value || !value.TryGetValue<bool>(out _)) throw new InvalidDataException("Incomplete request settings. Reload before editing.");
        if (AdminRequestJson.Number(body, "global_max_requests", -1) < 0 || AdminRequestJson.Number(body, "global_window_days") < 1 ||
            body.ContainsKey("watchlist_requests") && (body["watchlist_requests"] is not JsonValue watchlist || !watchlist.TryGetValue<bool>(out _))) throw new InvalidDataException("Invalid request settings. Reload before editing.");
    }
    private static void Media(string value) { if (value is not ("movie" or "series")) throw new ArgumentException("Choose movies or series."); }
    private static void DecimalId(string id) { if (!long.TryParse(id, out var number) || number <= 0 || id != number.ToString(System.Globalization.CultureInfo.InvariantCulture)) throw new ArgumentException("Invalid record identity."); }
    private static void OpaqueId(string id) { if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || id.Length > 200 || id.Any(char.IsControl)) throw new ArgumentException("Invalid record identity."); }
}
