using System.Globalization;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Api;

public partial class CatalogApi
{
    /// <summary>Library-scoped item details used by the recommendation listening deck.</summary>
    public async Task<MediaItemDetail> GetItemDetailAsync(string contentId, int? libraryId, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var path = $"/api/v2/catalog/items/{Uri.EscapeDataString(contentId)}";
        if (libraryId.HasValue)
            path += "?library_id=" + Uri.EscapeDataString(libraryId.Value.ToString(CultureInfo.InvariantCulture));
        var result = await client.SendRequestAsync<MediaItemDetail>(context, HttpMethod.Get, path, null, ct);
        if (!client.IsCurrentContext(context))
            throw new OperationCanceledException("Hero detail authority changed.", ct);
        return result;
    }
}
