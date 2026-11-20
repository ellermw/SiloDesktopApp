using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>Official 217ff25a admin collection contracts; separate from viewer/personal collection APIs.</summary>
public sealed partial class AdminCollectionsApi(SiloApiClient client)
{
    private const string Root = "/api/v2/admin/collections";
    public const int MaxCollections = 5000;
    public const int PageSize = 50;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public static string Id(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl)) throw new ArgumentException("Invalid collection identity.");
        return Uri.EscapeDataString(id);
    }
    public static string Strong(string value) => EntityTagHeaderValue.TryParse(value, out var tag) && !tag.IsWeak && tag.Tag != "*"
        ? value : throw new InvalidDataException("A strong collection revision is required. Reload the page.");
    /// <summary>The outer v2 library_ids are strings; the raw catalog query definition retains numeric ids.</summary>
    public static JsonArray RuleLibraryIds(JsonNode? selected)
    {
        var result = new JsonArray();
        foreach (var value in selected as JsonArray ?? [])
        {
            if (!int.TryParse(value?.ToString(), out var id) || id <= 0) throw new ArgumentException("Choose valid libraries.");
            result.Add(id);
        }
        return result;
    }
    /// <summary>The v2 identity of a collection: a non-empty string without control characters. A number or any other shape is malformed.</summary>
    public static string? CanonicalId(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var id) && !string.IsNullOrWhiteSpace(id) && id.Length <= 256 && !id.Any(char.IsControl) ? id : null;
    private static void Check(MonitorAuthorityLease lease, AdminCollectionSnapshot snapshot, string path)
    {
        lease.ThrowIfRetired(); snapshot.Owner.ThrowIfRetired();
        if (snapshot.Path != path || snapshot.Owner.Epoch != lease.Epoch || snapshot.Owner.AccountId != lease.AccountId ||
            snapshot.Owner.Context.BaseUrl != lease.Context.BaseUrl) throw new MonitorAuthorityRetiredException();
        Strong(snapshot.ETag);
    }
    internal Task<AdminCollectionSnapshot> ReadAsync(MonitorAuthorityLease lease, string path, bool guarded = false, CancellationToken ct = default) => ReadCoreAsync(lease, path, guarded, ct);
    private async Task<AdminCollectionSnapshot> ReadCoreAsync(MonitorAuthorityLease lease, string path, bool guarded, CancellationToken ct)
    {
        var value = await client.AdminCollectionSendAsync(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false);
        if (guarded) Strong(value.ETag);
        return value;
    }
    internal Task<AdminCollectionSnapshot> WriteAsync(MonitorAuthorityLease lease, HttpMethod method, string path, JsonObject? body = null,
        AdminCollectionSnapshot? observed = null, CancellationToken ct = default)
    {
        if (observed != null) Check(lease, observed, path);
        return client.AdminCollectionSendAsync(lease, method, path, body, observed?.ETag, ct);
    }
    public async Task<AdminCollectionsCapabilities> CapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        (await ReadAsync(lease, Root + "/capabilities", ct: ct).ConfigureAwait(false)).Value.Deserialize<AdminCollectionsCapabilities>(Json)!;
    public async Task<AdminCollectionsList> ListAsync(MonitorAuthorityLease lease, string? library = null, CancellationToken ct = default)
    {
        var value = (await ReadAsync(lease, Root + (library == null ? "" : "?library_id=" + Id(library)), ct: ct).ConfigureAwait(false)).Value.Deserialize<AdminCollectionsList>(Json)!;
        if (value.Page?.HasMore == true || value.Items.Count > MaxCollections || value.Items.Any(c => string.IsNullOrEmpty(c.Id)) ||
            value.Items.Select(c => c.Id).Distinct().Count() != value.Items.Count) throw new InvalidDataException("Collections list is incomplete or too large. Choose a library and reload.");
        return value;
    }
    public async Task<AdminCollectionSnapshot> GetAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default)
    {
        var result = await ReadAsync(lease, Root + "/" + Id(id), true, ct).ConfigureAwait(false);
        if (result.Value["id"]?.GetValue<string>() != id) throw new InvalidDataException("Collection identity changed.");
        return result;
    }
    public async Task<AdminCollectionSnapshot> SaveAsync(MonitorAuthorityLease lease, AdminCollectionSnapshot? snapshot, JsonObject body)
    {
        var result = await WriteAsync(lease, snapshot == null ? HttpMethod.Post : HttpMethod.Patch, snapshot?.Path ?? Root, body, snapshot).ConfigureAwait(false);
        var id = CanonicalId(result.Value["id"]);
        if (id == null || snapshot != null && snapshot.Value["id"]?.ToString() != id)
            throw new InvalidDataException("The save answer did not identify the collection. Reload and review the unknown outcome.");
        return result;
    }

    public async Task<AdminCollectionsBoard> BoardAsync(MonitorAuthorityLease lease, string library, CancellationToken ct = default)
    {
        var groupPath = "/api/v2/admin/libraries/" + Id(library) + "/collection-groups/order";
        // Both definitions and every member order live inside the same library-revision bracket.
        var before = await ReadAsync(lease, groupPath, true, ct).ConfigureAwait(false);
        RequireCompleteOrder(before);
        var groups = await ReadAsync(lease, "/api/v2/admin/libraries/" + Id(library) + "/collection-groups", ct: ct).ConfigureAwait(false);
        var list = await ListAsync(lease, library, ct).ConfigureAwait(false);
        list.Groups = groups.Value["items"]?.Deserialize<List<AdminCollectionShelf>>(Json) ?? [];
        var ids = Order(before);
        if (ids.Count > 200 || !ids.Contains("ungrouped") || !ids.Where(id => id != "ungrouped").ToHashSet().SetEquals(list.Groups.Select(g => g.Id)))
            throw new AdminCollectionChangedException();
        var orders = new Dictionary<string, AdminCollectionSnapshot>();
        // At most four requests in flight; no catalog traversal.
        foreach (var batch in ids.Chunk(4))
        {
            var snapshots = await Task.WhenAll(batch.Select(async id => (id, snapshot: await ReadAsync(lease,
                GroupItemsPath(id, library), true, ct).ConfigureAwait(false)))).ConfigureAwait(false);
            foreach (var (id, snapshot) in snapshots) { RequireCompleteOrder(snapshot); orders.Add(id, snapshot); }
        }
        var after = await ReadAsync(lease, groupPath, true, ct).ConfigureAwait(false);
        if (before.ETag != after.ETag) throw new AdminCollectionChangedException();
        var memberIds = orders.Values.SelectMany(Order).ToArray();
        if (memberIds.Length != memberIds.Distinct().Count() || !memberIds.ToHashSet().SetEquals(list.Items.Select(c => c.Id)))
            throw new AdminCollectionChangedException();
        return new(library, list, before, orders);
    }
    public static List<string> Order(AdminCollectionSnapshot snapshot) => snapshot.Value["ordered_ids"]?.Deserialize<List<string>>(Json) ?? [];
    public static void RequireCompleteOrder(AdminCollectionSnapshot snapshot)
    {
        var ids = Order(snapshot);
        if (snapshot.Value["ordered_ids"] is not JsonArray || snapshot.Value["has_more"] == null ||
            snapshot.Value["has_more"]!.GetValue<bool>() || ids.Any(string.IsNullOrWhiteSpace) || ids.Count > MaxCollections || ids.Distinct().Count() != ids.Count)
            throw new InvalidOperationException("A partial order cannot be saved. Reload to review the complete order.");
    }
    private static JsonObject OrderBody(IEnumerable<string> ids) => new() { ["ordered_ids"] = JsonSerializer.SerializeToNode(ids) };
    private static string GroupItemsPath(string group, string library) => "/api/v2/admin/collection-groups/" + Id(group) + "/collections/order?library_id=" + Id(library);
    public Task<AdminCollectionSnapshot> ReorderShelvesAsync(MonitorAuthorityLease lease, AdminCollectionsBoard board, IReadOnlyList<string> ids)
    {
        RequirePermutation(board.GroupOrder, ids);
        return WriteAsync(lease, HttpMethod.Put, board.GroupOrder.Path, OrderBody(ids), board.GroupOrder);
    }
    public Task<AdminCollectionSnapshot> MoveAsync(MonitorAuthorityLease lease, AdminCollectionsBoard board, string id, string destination, int index)
    {
        if (!board.List.Items.Any(c => c.Id == id) || !board.Orders.TryGetValue(destination, out var order) ||
            board.List.Groups.Any(g => g.Id == destination && g.Kind != "regular")) throw new InvalidOperationException("Choose an editable shelf.");
        RequireCompleteOrder(order);
        var ids = Order(order); ids.Remove(id); ids.Insert(Math.Clamp(index, 0, ids.Count), id);
        // Destination membership is complete and guarded; omitted members are never implicitly removed.
        return WriteAsync(lease, HttpMethod.Put, order.Path, OrderBody(ids), order);
    }
    private static void RequirePermutation(AdminCollectionSnapshot order, IReadOnlyList<string> ids)
    {
        RequireCompleteOrder(order);
        if (ids.Count != ids.Distinct().Count() || !Order(order).ToHashSet().SetEquals(ids)) throw new InvalidOperationException("The complete observed order is required.");
    }
    public Task<AdminCollectionSnapshot> ItemOrderAsync(MonitorAuthorityLease lease, string id) => ReadAsync(lease, Root + "/" + Id(id) + "/items/order", true);
    public Task<AdminCollectionSnapshot> ReorderItemsAsync(MonitorAuthorityLease lease, string id, AdminCollectionSnapshot observed, IReadOnlyList<string> ids)
    {
        RequirePermutation(observed, ids);
        return WriteAsync(lease, HttpMethod.Put, Root + "/" + Id(id) + "/items/order", OrderBody(ids), observed);
    }
    public async Task<JsonObject> ItemsAsync(MonitorAuthorityLease lease, string id, string? cursor = null) =>
        (await ReadAsync(lease, Root + "/" + Id(id) + "/items?limit=" + PageSize + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor))).ConfigureAwait(false)).Value;
    public Task<AdminCollectionSnapshot> MembershipAsync(MonitorAuthorityLease lease, string id, string item, bool add, int position = 0) =>
        WriteAsync(lease, add ? HttpMethod.Put : HttpMethod.Delete, Root + "/" + Id(id) + "/items/" + Id(item), add ? new JsonObject { ["position"] = position } : null);
    public const int PreviewLimit = 24;
    /// <summary>The admin preview answers { items: [{ content_id, title, type, poster_url? }], page?, total }. Anything else is rejected, never shown as an empty match.</summary>
    public static AdminCollectionPreview ParsePreview(JsonObject body)
    {
        if (body["items"] is not JsonArray items || body["total"] is not JsonValue total || !total.TryGetValue<int>(out var count) || count < 0)
            throw new InvalidDataException("The preview answer was not recognized. Nothing was changed.");
        var titles = new List<AdminCollectionPreviewItem>();
        foreach (var node in items.Take(PreviewLimit))
        {
            if (node is not JsonObject item || item["title"] is not JsonValue title || !title.TryGetValue<string>(out var text))
                throw new InvalidDataException("The preview answer was not recognized. Nothing was changed.");
            titles.Add(new(CanonicalId(item["content_id"]) ?? "", text, item["type"]?.ToString() ?? ""));
        }
        return new(titles, count);
    }
    /// <summary>A preview is a read-only question the server answers with POST: it never changes a collection.</summary>
    public async Task<AdminCollectionPreview> PreviewAsync(MonitorAuthorityLease lease, JsonObject rules, CancellationToken ct = default) =>
        ParsePreview((await client.AdminCollectionSendAsync(lease, HttpMethod.Post, Root + "/preview",
            new() { ["query_definition"] = rules.DeepClone(), ["limit"] = PreviewLimit }, ct: ct).ConfigureAwait(false)).Value);
    public Task<AdminCollectionSnapshot> SyncAsync(MonitorAuthorityLease lease, string id) => WriteAsync(lease, HttpMethod.Post, Root + "/" + Id(id) + "/sync");
    public async Task<JsonObject> SearchAsync(MonitorAuthorityLease lease, string query, string library, string? cursor = null)
    {
        if (query.Trim().Length < 2) throw new ArgumentException("Enter at least two characters.");
        return (await ReadAsync(lease, "/api/v2/catalog?q=" + Uri.EscapeDataString(query.Trim()) + "&library_id=" + Id(library) + "&limit=30" +
            (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor))).ConfigureAwait(false)).Value;
    }
    public Task<AdminCollectionSnapshot> ShelfAsync(MonitorAuthorityLease lease, string id) => ReadAsync(lease, "/api/v2/admin/collection-groups/" + Id(id), true);
    public Task<AdminCollectionSnapshot> SaveShelfAsync(MonitorAuthorityLease lease, string library, AdminCollectionSnapshot? shelf, JsonObject body) =>
        WriteAsync(lease, shelf == null ? HttpMethod.Post : HttpMethod.Patch, shelf?.Path ?? "/api/v2/admin/libraries/" + Id(library) + "/collection-groups", body, shelf);
    public Task<AdminCollectionSnapshot> DeleteShelfAsync(MonitorAuthorityLease lease, AdminCollectionSnapshot shelf) => WriteAsync(lease, HttpMethod.Delete, shelf.Path, observed: shelf);
    public async Task<JsonObject> BundlesAsync(MonitorAuthorityLease lease) => (await ReadAsync(lease, Root + "/template-bundles").ConfigureAwait(false)).Value;
    public async Task<JsonObject> TemplatesAsync(MonitorAuthorityLease lease) => (await ReadAsync(lease, Root + "/templates").ConfigureAwait(false)).Value;
    public Task<byte[]> ArtworkBytesAsync(MonitorAuthorityLease lease, string url, CancellationToken ct) => client.AdminCollectionImageAsync(lease, url, ct);
    public async Task<JsonObject> SavedPreviewAsync(MonitorAuthorityLease lease, string id, string library) =>
        (await ReadAsync(lease, "/api/v2/library/" + Id(library) + "/collections/" + Id(id) + "/items?limit=24").ConfigureAwait(false)).Value;
    public async Task<AdminCollectionSnapshot> ApplyBundleAsync(MonitorAuthorityLease lease, string id, IEnumerable<string> libraries, bool preview, bool replace, JsonObject? featured = null)
    {
        var body = new JsonObject { ["library_ids"] = JsonSerializer.SerializeToNode(libraries), ["dry_run"] = preview, ["delete_existing"] = replace };
        if (featured != null) body["featured"] = featured.DeepClone();
        var result = await WriteAsync(lease, HttpMethod.Post, Root + "/template-bundles/" + Id(id) + (preview ? "/apply" : "/apply-job"), body).ConfigureAwait(false);
        if (!preview && CanonicalId(result.Value["id"]) == null) throw new InvalidDataException("The server did not identify the starter-pack job. Reload and review before trying again.");
        return result;
    }
    public Task<AdminCollectionSnapshot> JobAsync(MonitorAuthorityLease lease, string id) => ReadAsync(lease, "/api/v2/admin/collection-jobs/" + Id(id));
    public async Task<JsonObject> RecentJobsAsync(MonitorAuthorityLease lease) =>
        (await ReadAsync(lease, "/api/v2/admin/jobs?kind=template_bundle_apply&limit=10").ConfigureAwait(false)).Value;
    public async Task<AdminCollectionSnapshot> ImportAsync(MonitorAuthorityLease lease, string source, JsonObject body)
    {
        if (source is not ("mdblist" or "tmdb" or "tmdb_list")) throw new ArgumentException("Unsupported collection source.");
        var result = await WriteAsync(lease, HttpMethod.Post, Root + "/import/" + source.Replace('_', '-'), body).ConfigureAwait(false);
        if (CanonicalId(result.Value["collection"]?["id"]) == null) throw new InvalidDataException("The server did not identify the imported collection. Reload and review before trying again.");
        return result;
    }
    public Task<AdminCollectionSnapshot> ArtworkAsync(MonitorAuthorityLease lease, string id, string slot, string? source = null, byte[]? bytes = null, string mediaType = "image/png")
    {
        if (slot is not ("poster" or "backdrop")) throw new ArgumentException("Invalid artwork slot.");
        if (bytes == null && string.IsNullOrWhiteSpace(source)) return WriteAsync(lease, HttpMethod.Delete, Root + "/" + Id(id) + "/image?type=" + slot);
        if (bytes is { Length: > 10485760 } || bytes is { Length: 0 } || mediaType is not ("image/png" or "image/jpeg" or "image/webp")) throw new ArgumentException("Use a PNG, JPEG or WebP image up to 10 MiB.");
        var form = new MultipartFormDataContent();
        if (bytes != null) { var image = new ByteArrayContent(bytes); image.Headers.ContentType = new(mediaType); form.Add(image, "image", "collection-image"); }
        else form.Add(new StringContent(source!), "source_url");
        return client.AdminCollectionSendAsync(lease, HttpMethod.Put, Root + "/" + Id(id) + "/" + slot, form: form);
    }
}
