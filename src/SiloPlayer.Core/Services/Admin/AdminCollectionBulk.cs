using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>How a refused or failed delete of one collection counts in a bulk delete's result.</summary>
public enum AdminBulkDeleteOutcome { Deleted, Kept, Failed }

/// <summary>
/// The rules of the Collections page's select-mode actions that are the same as the official WebUI's
/// (<c>setSelectedVisible</c> in <c>AdminCollections.tsx</c> and <c>runBulkDelete</c>), kept here so they are tested without a window.
/// </summary>
public static class AdminCollectionBulk
{
    /// <summary>
    /// Official <c>useDeleteAdminCollections</c> classification of a delete that was refused: 404 (already gone) counts as deleted, 409
    /// (a row still shows it) as kept, anything else as failed. A 412 (changed since it was read) and a transport failure are failed.
    /// </summary>
    public static AdminBulkDeleteOutcome ClassifyDeleteFailure(Exception error) => error switch
    {
        ApiException { StatusCode: 404 } => AdminBulkDeleteOutcome.Deleted,
        ApiException { StatusCode: 409 } => AdminBulkDeleteOutcome.Kept,
        _ => AdminBulkDeleteOutcome.Failed,
    };

    /// <summary>
    /// Show on tabs and Hide from tabs act only on the collections whose visibility differs from what was asked (official
    /// <c>setSelectedVisible</c>): the count, the confirmation and the requests all use this set.
    /// </summary>
    public static IReadOnlyList<AdminCollectionEntry> ChangingVisibility(IEnumerable<AdminCollectionEntry> selected, bool show) =>
        selected.Where(c => (c.Visibility != "hidden") != show).ToArray();
}
