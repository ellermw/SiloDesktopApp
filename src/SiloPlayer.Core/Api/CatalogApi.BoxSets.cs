using SiloPlayer.Core.Models.Catalog;
namespace SiloPlayer.Core.Api;
public partial class CatalogApi
{
    private sealed class BoxSetCapabilities { public bool BoxSets { get; set; } }
    private async Task<bool> ReadBoxSetPolicyAsync(ApiRequestContext context, CancellationToken ct)
    {
        // Unlike static query features, this viewer policy can change at any time.
        var response = await client.SendRequestAsync<BoxSetCapabilities>(context, HttpMethod.Get, "/api/v2/catalog/search/capabilities", null, ct);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Box-set policy context changed.", ct);
        return response.BoxSets;
    }
    public long MetadataLanguageGeneration => client.LocalizedReads.Generation;
    public async Task<bool> BoxSetsEnabledAsync(int? libraryId, CancellationToken ct = default, bool throwOnCapabilityFailure = false)
    {
        if (libraryId is <= 0) return false;
        var libraries = await GetBoxSetLibraryIdsAsync(ct, throwOnCapabilityFailure);
        return libraryId.HasValue ? libraries.Contains(libraryId.Value) : libraries.Count > 0;
    }
    public async Task<IReadOnlySet<int>> GetBoxSetLibraryIdsAsync(CancellationToken ct = default, bool throwOnCapabilityFailure = false)
    {
        var context = client.CaptureContext();
        bool supported;
        try { supported = await ReadBoxSetPolicyAsync(context, ct); }
        catch (OperationCanceledException) { throw; }
        catch (ApiException ex) when (ex.StatusCode == 404) { return new HashSet<int>(); }
        catch when (!throwOnCapabilityFailure) { return new HashSet<int>(); }
        if (!supported) return new HashSet<int>();
        var libraries = await client.SendRequestAsync<BrowseCollection<Library>>(context, HttpMethod.Get, "/api/v2/user/libraries?include_hidden=true", null, ct);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Box-set library authority changed.", ct);
        return libraries.Items.Where(x => x.Id > 0 && x.Enabled && x.BoxSetsEnabled).Select(x => x.Id).ToHashSet();
    }
    // Search follows the WebUI's visible-library list; detail discovery may include hidden libraries.
    public async Task<HashSet<string>> GetSearchBoxSetLibraryIdsAsync(int? libraryId = null, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        bool supported;
        try { supported = await ReadBoxSetPolicyAsync(context, ct); }
        catch (ApiException ex) when (ex.StatusCode == 404) { return []; }
        if (!supported) return [];
        var libraries = await client.SendRequestAsync<BrowseCollection<Library>>(context, HttpMethod.Get, "/api/v2/user/libraries", null, ct);
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Box-set library authority changed.", ct);
        return libraries.Items.Where(x => x.Enabled && x.BoxSetsEnabled && (!libraryId.HasValue || x.Id == libraryId))
            .Select(x => x.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
    }
    public Task<BoxSetDetail> GetBoxSetAsync(string id, string libraryId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        return ReadBoxSetAsync<BoxSetDetail>($"/api/v2/catalog/box-sets/{Uri.EscapeDataString(id)}?library_id={Uri.EscapeDataString(libraryId)}", ct);
    }
    public Task<ItemBoxSetResponse> GetItemBoxSetAsync(string contentId, int? libraryId = null, CancellationToken ct = default)
        => ReadBoxSetAsync<ItemBoxSetResponse>($"/api/v2/catalog/items/{Uri.EscapeDataString(contentId)}/box-set" +
            (libraryId.HasValue ? "?library_id=" + libraryId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""), ct);
    public Task<BoxSetList> SearchBoxSetsAsync(string query, int? libraryId = null, int limit = 5, CancellationToken ct = default)
        => ReadBoxSetAsync<BoxSetList>("/api/v2/catalog/box-sets?q=" + Uri.EscapeDataString(query.Trim()) + "&limit=" + Math.Clamp(limit, 1, 20) +
            (libraryId.HasValue ? "&library_id=" + libraryId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : ""), ct);
    private async Task<T> ReadBoxSetAsync<T>(string path, CancellationToken ct)
    {
        var context = client.CaptureContext(); var language = client.LocalizedReads.Generation;
        var result = await client.SendRequestAsync<T>(context, HttpMethod.Get, path, null, ct);
        ct.ThrowIfCancellationRequested(); client.LocalizedReads.ThrowIfChanged(language);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Box-set authority changed.", ct);
        return result;
    }
}
