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
    /// A failure after which the server may or may not have applied the change (a lost reply, a timeout, an unreadable answer, a server
    /// fault): the same rule as the controller's unknown-outcome gate. A select-mode action stops there instead of counting it as failed.
    /// </summary>
    public static bool IsUncertain(Exception error) =>
        error is HttpRequestException or TaskCanceledException or InvalidDataException or System.Text.Json.JsonException || error is ApiException { StatusCode: >= 500 };

    /// <summary>
    /// Show on tabs and Hide from tabs act only on the collections whose visibility differs from what was asked (official
    /// <c>setSelectedVisible</c>): the count, the confirmation and the requests all use this set.
    /// </summary>
    public static IReadOnlyList<AdminCollectionEntry> ChangingVisibility(IEnumerable<AdminCollectionEntry> selected, bool show) =>
        selected.Where(c => (c.Visibility != "hidden") != show).ToArray();
}
