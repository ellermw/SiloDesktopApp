using SiloPlayer.Core.Models.MediaMaintenance;

namespace SiloPlayer.Core.Services;

public enum MetadataRefreshFollowAction
{
    /// <summary>Nothing to do on the page: the job failed/canceled, the identity changed, or the starting visit ended.</summary>
    None,
    /// <summary>Reload the starting visit in place.</summary>
    Reload,
    /// <summary>The refresh rebuilt the item under another ID: replace the starting visit with it.</summary>
    FollowReplacement,
}

public readonly record struct MetadataRefreshFollowDecision(
    MetadataRefreshFollowAction Action,
    bool Structural = false,
    string? ReplacementContentId = null);

/// <summary>
/// What a finished Refresh Metadata job does (official #4355, #4187): the
/// caches drop every ID the refresh touched whatever page is open, and only
/// the detail visit that started it reloads or follows a replacement ID.
/// </summary>
public static class MetadataRefreshFollow
{
    public static MetadataRefreshFollowDecision Decide(
        MaintenanceJobWaitResult result, string contentId, string mode, DetailMetadataWatch? startingVisit)
    {
        if (startingVisit is not { IsActive: true }
            || !string.Equals(startingVisit.ContentId, contentId, StringComparison.Ordinal))
            return new(MetadataRefreshFollowAction.None);
        // A successful POST can outlive job visibility (curator permissions,
        // pruning or an unreadable proxy response). Reload, but never infer a
        // replacement ID or success from a failed/incomplete job read.
        if (result.Outcome == MaintenanceJobWaitOutcome.TimedOut
            || result.Outcome == MaintenanceJobWaitOutcome.Failed && result.Error != null)
            return new(MetadataRefreshFollowAction.Reload, Structural: mode == "complete");
        if (result.Outcome != MaintenanceJobWaitOutcome.Succeeded)
            return new(MetadataRefreshFollowAction.None);
        var item = result.Job?.ItemResult;
        if (item?.DetailContentId is { Length: > 0 } replacement
            && !string.Equals(replacement, contentId, StringComparison.Ordinal))
            return new(MetadataRefreshFollowAction.FollowReplacement, ReplacementContentId: replacement);
        // A complete refresh rescans files; new files add versions to the leaf.
        return new(MetadataRefreshFollowAction.Reload, Structural: mode == "complete" || item?.NewFiles > 0);
    }

    /// <summary>The detail IDs whose cached reads predate the refresh. Empty for a wait that went stale.</summary>
    public static IReadOnlyList<string> InvalidatedIds(
        MaintenanceJobWaitResult result, string contentId, string? type, string? seriesId)
    {
        if (result.Outcome == MaintenanceJobWaitOutcome.Stale) return [];
        var item = result.Job?.ItemResult;
        return new[]
            {
                contentId,
                item?.RefreshContentId,
                item?.DetailContentId,
                type is "season" or "episode" ? seriesId : null,
            }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
