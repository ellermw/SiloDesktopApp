using SiloPlayer.Core.Models.Catalog;
namespace SiloPlayer.Core.Api;
public partial class CatalogApi
{
    /// <summary>
    /// GET /api/v2/catalog/items/{id}/collections (listCatalogItemCollections): the collections a movie or series
    /// page lists under "Also in". <paramref name="libraryId"/> is the library the page was opened from; it only picks
    /// the box set whose synced copy is left out. A reply that outlives its account, profile or language is dropped
    /// as cancelled, as the box set read is.
    /// </summary>
    public async Task<ItemCollections> GetItemCollectionsAsync(string contentId, int? libraryId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        var context = client.CaptureContext(); var language = client.LocalizedReads.Generation;
        var path = $"/api/v2/catalog/items/{Uri.EscapeDataString(contentId)}/collections"
            + (libraryId.HasValue ? "?library_id=" + libraryId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
        var result = await client.SendRequestAsync<ItemCollections>(context, HttpMethod.Get, path, null, ct);
        ct.ThrowIfCancellationRequested(); client.LocalizedReads.ThrowIfChanged(language);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Item collections authority changed.", ct);
        return result ?? new ItemCollections();
    }
}
