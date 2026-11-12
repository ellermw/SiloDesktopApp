using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Services;

public sealed record ExternalTitleResolution(MediaItemDetail? LibraryItem, string? LibraryLink)
{
    public static Task<ExternalTitleResolution> ResolveAsync(RequestMediaDetail title, ItemDetailPrefetchCache cache, CancellationToken ct)
        => ResolveContentAsync(title.LibraryContentId, cache, ct);

    /// <summary>
    /// Reads the server-matched library copy with the current viewer. A global match is not proof of access:
    /// a readable copy is returned, authoritative not-found/forbidden drops the link, and any other failure
    /// keeps the link because it says nothing about access (the item page answers).
    /// </summary>
    public static async Task<ExternalTitleResolution> ResolveContentAsync(string? libraryContentId, ItemDetailPrefetchCache cache, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(libraryContentId)) return new(null, null);
        try { return new(await cache.GetAsync(libraryContentId, ct), libraryContentId); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new(null, DetailFailurePolicy.Classify(ex) is DetailFailureKind.NotFound or DetailFailureKind.AccessDenied ? null : libraryContentId); }
    }
}

public enum RequestLibraryAccess
{
    /// <summary>The viewer can read the copy, or the check failed for another reason: navigate.</summary>
    Open,
    /// <summary>The server authoritatively answered not-found/forbidden for this viewer: stay and explain.</summary>
    Unavailable,
    /// <summary>The page, profile or navigation that asked has moved on: do nothing.</summary>
    Retired,
}

/// <summary>Access check before an explicit request-to-library link navigates (official <c>LibraryItemLink</c>).</summary>
public static class RequestLibraryLinks
{
    public const string UnavailableNotice = "This title is in a library this profile can't open.";

    public static async Task<RequestLibraryAccess> CheckAsync(string? libraryContentId, ItemDetailPrefetchCache cache,
        Func<bool> isCurrent, CancellationToken ct)
    {
        try
        {
            var resolution = await ExternalTitleResolution.ResolveContentAsync(libraryContentId, cache, ct);
            if (!isCurrent()) return RequestLibraryAccess.Retired;
            return resolution.LibraryLink == null ? RequestLibraryAccess.Unavailable : RequestLibraryAccess.Open;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return RequestLibraryAccess.Retired; }
    }
}
