using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Services;

public sealed record ExternalTitleResolution(MediaItemDetail? LibraryItem, string? LibraryLink)
{
    public static async Task<ExternalTitleResolution> ResolveAsync(RequestMediaDetail title, ItemDetailPrefetchCache cache, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title.LibraryContentId)) return new(null, null);
        try { return new(await cache.GetAsync(title.LibraryContentId, ct), title.LibraryContentId); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new(null, DetailFailurePolicy.Classify(ex) is DetailFailureKind.NotFound or DetailFailureKind.AccessDenied ? null : title.LibraryContentId); }
    }
}
