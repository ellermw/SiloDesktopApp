using SiloPlayer.Core.Models.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;

namespace SiloPlayer.Core.Api;

public class CollectionsApi(SiloApiClient client)
{
    private readonly ConcurrentDictionary<string, (ApiRequestContext Context, string ETag)> _revisions = new();
    private readonly ConcurrentDictionary<string, (ApiRequestContext Context, string Fingerprint)> _baselines = new();

    public async Task<CollectionsResponse> GetCollectionsAsync(CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var result = await client.GetAsync<CollectionList>("/api/v2/collections", ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Collection context changed.", ct);
        foreach (var collection in result.Items) RememberBaseline("/api/v2/collections/" + Uri.EscapeDataString(collection.Id), collection, context);
        foreach (var group in result.Groups) RememberBaseline("/api/v2/collections/groups/" + Uri.EscapeDataString(group.Id), group, context);
        RememberBaseline("/api/v2/collections/groups/order", new { ordered_ids = result.Groups.OrderBy(g => g.SortOrder).Select(g => g.Id).ToArray() }, context);
        foreach (var group in result.Groups.Select(g => g.Id).Prepend(null))
            RememberBaseline(OrderPath(group), new { ordered_ids = result.Items.Where(c => c.GroupId == group).OrderBy(c => c.SortOrder).Select(c => c.Id).ToArray() }, context);
        return new() { Collections = result.Items, Groups = result.Groups };
    }

    public async Task<Collection> GetCollectionAsync(string id, CancellationToken ct = default)
    {
        var path = "/api/v2/collections/" + Uri.EscapeDataString(id);
        var context = client.CaptureContext();
        var result = await client.GetWithETagAsync<Collection>(path, ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Collection context changed.", ct);
        if (result.ETag != null) _revisions[path] = (context, result.ETag);
        return result.Body;
    }

    private sealed class CollectionList { public List<Collection> Items { get; set; } = []; public List<CollectionGroup> Groups { get; set; } = []; }

    public Task<ServerCollectionsResponse> GetServerCollectionsAsync(CancellationToken ct = default)
        => client.GetAsync<ServerCollectionsResponse>("/api/v2/collections/server", ct);

    public Task<CollectionTemplateCatalog> GetCollectionTemplatesAsync(CancellationToken ct = default)
        => client.GetAsync<CollectionTemplateCatalog>("/api/v2/collections/templates", ct);

    public Task<CollectionCapabilitiesResponse> GetCollectionCapabilitiesAsync(CancellationToken ct = default)
        => client.GetAsync<CollectionCapabilitiesResponse>("/api/v2/collections/capabilities", ct);

    public Task SetCollectionSortPreferenceAsync(
        string collectionKind,
        string collectionId,
        string field,
        string order,
        CancellationToken ct = default)
        => client.PutNoContentAsync(
            "/api/v2/collections/sort-preference",
            new Dictionary<string, object?>
            {
                ["collection_kind"] = collectionKind,
                ["collection_id"] = collectionId,
                ["field"] = field,
                ["order"] = order,
            },
            ct);

    public Task<MDBListDiscoveryResponse> SearchMDBListAsync(string query, CancellationToken ct = default)
        => ReadDiscoveryAsync($"/api/v2/collections/import/mdblist/search?q={Uri.EscapeDataString(query)}", ct);

    public Task<MDBListDiscoveryResponse> GetTopMDBListAsync(CancellationToken ct = default)
        => ReadDiscoveryAsync("/api/v2/collections/import/mdblist/top", ct);

    public async Task<Collection> CreateCollectionAsync(CreateCollectionRequest request, CancellationToken ct = default)
    {
        var body = JsonNode.Parse(V2Json.Body(request).GetRawText())!.AsObject();
        body.Remove("description"); body.Remove("group_id");
        var created = await client.PostAsync<Collection>("/api/v2/collections", JsonSerializer.SerializeToElement(body), ct);
        if (request.Description != null || request.GroupId != null)
            created = await UpdateCollectionAsync(created.Id, new() { Description = request.Description, GroupId = request.GroupId }, ct);
        return created;
    }

    public async Task<Collection> CreateCollectionAsync(
        CreateCollectionRequest request, string posterFileName, byte[] posterBytes,
        string contentType, CancellationToken ct = default)
    {
        var created = await CreateCollectionAsync(request, ct);
        return await client.PutMultipartAsync<Collection>($"/api/v2/collections/{Uri.EscapeDataString(created.Id)}/poster", "poster", posterFileName, posterBytes, contentType, ct);
    }

    public Task<ImportUserCollectionResponse> ImportUserMDBListCollectionAsync(ImportUserMDBListCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<ImportUserCollectionResponse>("/api/v2/collections/import/mdblist", V2Json.Body(request), ct);

    public Task<ImportUserCollectionResponse> ImportUserTMDBCollectionAsync(ImportUserTMDBCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<ImportUserCollectionResponse>("/api/v2/collections/import/tmdb", V2Json.Body(request), ct);

    public Task<ImportUserCollectionResponse> ImportUserTraktCollectionAsync(ImportUserTraktCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<ImportUserCollectionResponse>("/api/v2/collections/import/trakt", V2Json.Body(request), ct);

    public Task<CollectionPreviewResponse> PreviewCollectionAsync(CollectionPreviewRequest request, CancellationToken ct = default)
        => client.PostAsync<CollectionPreviewResponse>("/api/v2/collections/preview", V2Json.Body(request), ct);

    public async Task<Collection> UpdateCollectionAsync(string id, UpdateCollectionRequest request, CancellationToken ct = default)
    {
        var path = $"/api/v2/collections/{Uri.EscapeDataString(id)}";
        var body = JsonNode.Parse(V2Json.Body(request).GetRawText())!.AsObject();
        body.Remove("poster_source_url");
        var result = body.Count > 0
            ? await PatchAsync<Collection>(path, JsonSerializer.SerializeToElement(body), ct)
            : await GetCollectionAsync(id, ct);
        if (!string.IsNullOrWhiteSpace(request.PosterSourceUrl))
            result = await client.PutMultipartFieldsAsync<Collection>(path + "/poster",
                new Dictionary<string, string> { ["source_url"] = request.PosterSourceUrl }, ct);
        return result;
    }

    public async Task<Collection> UpdateCollectionAsync(
        string id, UpdateCollectionRequest request, string posterFileName,
        byte[] posterBytes, string contentType, CancellationToken ct = default)
    {
        await UpdateCollectionAsync(id, request, ct);
        return await client.PutMultipartAsync<Collection>($"/api/v2/collections/{Uri.EscapeDataString(id)}/poster", "poster", posterFileName, posterBytes, contentType, ct);
    }

    public Task DeleteCollectionAsync(string id, CancellationToken ct = default)
        => DeleteAsync($"/api/v2/collections/{Uri.EscapeDataString(id)}", ct);

    public Task<CollectionGroup> CreateCollectionGroupAsync(string name, string slug, CancellationToken ct = default)
        => client.PostAsync<CollectionGroup>(
            "/api/v2/collections/groups",
            new Dictionary<string, object?> { ["name"] = name, ["slug"] = slug },
            ct);

    public Task<CollectionGroup> UpdateCollectionGroupAsync(string id, string name, CancellationToken ct = default)
        => PatchAsync<CollectionGroup>(
            $"/api/v2/collections/groups/{Uri.EscapeDataString(id)}",
            new Dictionary<string, object?> { ["name"] = name },
            ct);

    public Task<Collection> MoveCollectionToGroupAsync(string id, string? groupId, CancellationToken ct = default)
        => PatchAsync<Collection>(
            $"/api/v2/collections/{Uri.EscapeDataString(id)}",
            new Dictionary<string, object?> { ["group_id"] = groupId },
            ct);

    public Task DeleteCollectionGroupAsync(string id, CancellationToken ct = default)
        => DeleteAsync($"/api/v2/collections/groups/{Uri.EscapeDataString(id)}", ct);

    public async Task ReorderCollectionsAsync(IReadOnlyList<string> orderedIds, string? groupId, CancellationToken ct = default)
    {
        const string path = "/api/v2/collections/order";
        var readPath = OrderPath(groupId);
        var tag = await RevisionAsync(readPath, ct);
        await client.PutWithETagAsync<JsonElement>(path, V2Json.Body(new ReorderCollectionsRequest { OrderedIds = [.. orderedIds], GroupId = groupId }), tag, ct);
    }

    public Task ReorderCollectionGroupsAsync(IReadOnlyList<string> orderedIds, CancellationToken ct = default)
        => PutAsync(
            "/api/v2/collections/groups/order",
            new ReorderCollectionGroupsRequest { OrderedIds = [.. orderedIds] },
            ct);

    public Task DeleteCollectionImageAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/collections/{Uri.EscapeDataString(id)}/image?type=poster", ct);

    public Task<UserCollectionSyncResult> SyncCollectionAsync(string id, CancellationToken ct = default)
        => client.SendRequestAsync<UserCollectionSyncResult>(HttpMethod.Post,
            $"/api/v2/collections/{Uri.EscapeDataString(id)}/sync",
            null, null,
            ct);

    public async Task<CollectionItemsResponse> GetCollectionItemsAsync(string id, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var items = await client.GetAllItemsAsync<CollectionItem>($"/api/v2/collections/{Uri.EscapeDataString(id)}/items", ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Collection context changed.", ct);
        RememberBaseline($"/api/v2/collections/{Uri.EscapeDataString(id)}/items/order", new { ordered_ids = items.OrderBy(i => i.Position).Select(i => i.MediaItemId).ToArray() }, context);
        return new() { Items = items, Total = items.Count };
    }

    public async Task AddCollectionItemAsync(string collectionId, string itemId, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var existing = await GetCollectionItemsAsync(collectionId, ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Collection context changed.", ct);
        var position = existing.Items.Count == 0 ? 0 : existing.Items.Max(item => item.Position) + 1;
        await client.PutNoContentAsync($"/api/v2/collections/{Uri.EscapeDataString(collectionId)}/items/{Uri.EscapeDataString(itemId)}", new { position }, ct);
        RememberBaseline($"/api/v2/collections/{Uri.EscapeDataString(collectionId)}/items/order", new { ordered_ids = existing.Items.OrderBy(i => i.Position).Select(i => i.MediaItemId).Where(id => id != itemId).Append(itemId).ToArray() }, context);
    }

    public async Task RemoveCollectionItemAsync(string collectionId, string itemId, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var orderPath = $"/api/v2/collections/{Uri.EscapeDataString(collectionId)}/items/order";
        _baselines.TryGetValue(orderPath, out var baseline);
        await client.DeleteAsync($"/api/v2/collections/{Uri.EscapeDataString(collectionId)}/items/{Uri.EscapeDataString(itemId)}", ct);
        if (baseline.Context == context && baseline.Fingerprint != null)
            RememberBaseline(orderPath, new { ordered_ids = JsonSerializer.Deserialize<string[]>(baseline.Fingerprint)!.Where(id => id != itemId).ToArray() }, context);
    }

    private static string OrderPath(string? groupId) => "/api/v2/collections/order" + (groupId == null ? "" : "?group_id=" + Uri.EscapeDataString(groupId));
    private void RememberBaseline(string path, object body, ApiRequestContext context)
        => _baselines[path] = (context, Fingerprint(path, JsonSerializer.SerializeToElement(body, V2Json.Options)));
    private static string Fingerprint(string path, JsonElement body)
    {
        string Value(string name) => body.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";
        if (path.Contains("/order", StringComparison.Ordinal))
            return JsonSerializer.Serialize(body.GetProperty("ordered_ids").EnumerateArray().Select(v => v.GetString()).ToArray());
        if (path.Contains("/groups/", StringComparison.Ordinal))
            return JsonSerializer.Serialize(new[] { Value("id"), Value("name"), Value("slug"), Value("default_sort_mode"), Value("sort_order") });
        return JsonSerializer.Serialize(new[] { Value("id"), Value("updated_at"), Value("name"), Value("group_id") });
    }

    public Task ReorderCollectionItemsAsync(string collectionId, IReadOnlyList<string> orderedIds, CancellationToken ct = default)
        => PutAsync(
            $"/api/v2/collections/{Uri.EscapeDataString(collectionId)}/items/order",
            new ReorderCollectionsRequest { OrderedIds = [.. orderedIds] },
            ct);

    private async Task<string> RevisionAsync(string path, CancellationToken ct)
    {
        var context = client.CaptureContext();
        if (_revisions.TryGetValue(path, out var saved) && saved.Context == context) return saved.ETag;
        var result = await client.GetWithETagAsync<JsonElement>(path, ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Collection context changed.", ct);
        var comparison = result.Body;
        if (path.EndsWith("/items/order", StringComparison.Ordinal) && result.Body.TryGetProperty("has_more", out var hasMore) && hasMore.ValueKind == JsonValueKind.True)
        {
            var items = await client.GetAllItemsAsync<CollectionItem>(path[..^"/order".Length], ct);
            comparison = JsonSerializer.SerializeToElement(new { ordered_ids = items.OrderBy(i => i.Position).Select(i => i.MediaItemId).ToArray() });
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Collection context changed.", ct);
        }
        if (_baselines.TryGetValue(path, out var baseline) && (baseline.Context != context || baseline.Fingerprint != Fingerprint(path, comparison)))
            throw new ApiException("precondition_failed", "Collections changed elsewhere. Reload before saving.", 412);
        return result.ETag ?? throw new InvalidDataException("The server did not return the collection revision.");
    }
    private async Task<T> PatchAsync<T>(string path, object body, CancellationToken ct)
    {
        var tag = await RevisionAsync(path, ct);
        var result = await client.PatchWithETagAsync<T>(path, body, tag, ct);
        _revisions.TryRemove(path, out _);
        return result;
    }
    private async Task PutAsync(string path, object body, CancellationToken ct)
    {
        var tag = await RevisionAsync(path, ct);
        await client.PutWithETagAsync<JsonElement>(path, V2Json.Body(body), tag, ct);
        _revisions.TryRemove(path, out _);
    }
    private async Task DeleteAsync(string path, CancellationToken ct)
    {
        var tag = await RevisionAsync(path, ct);
        await client.DeleteWithETagAsync(path, tag, ct);
        _revisions.TryRemove(path, out _);
    }
    private async Task<MDBListDiscoveryResponse> ReadDiscoveryAsync(string path, CancellationToken ct)
    {
        var body = await client.GetAsync<JsonElement>(path, ct);
        return new() { Configured = body.GetProperty("configured").GetBoolean(), Lists = body.GetProperty("items").Deserialize<List<MDBListListSummary>>(V2Json.Options) ?? [] };
    }
}
