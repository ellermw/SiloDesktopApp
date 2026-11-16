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

public enum MetadataRefreshToastKind { None, Info, Success, Warning, Error }

/// <summary>One toast for a refresh: a localized string key and its arguments. <see cref="MetadataRefreshToastKind.None"/> shows nothing.</summary>
public readonly record struct MetadataRefreshToast(
    MetadataRefreshToastKind Kind, string? Key = null, IReadOnlyDictionary<string, object?>? Arguments = null);

/// <summary>An item whose open detail pages reread after a finished refresh. Structural: files or versions may have changed.</summary>
public readonly record struct MetadataRefreshChange(string ContentId, bool Structural);

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

    /// <summary>The toast shown once the server accepted the refresh (official <c>onMutate</c> loading toast).</summary>
    public static MetadataRefreshToast RunningToast(string mode) => new(MetadataRefreshToastKind.Info,
        mode == "complete" ? "web.item.toast.refreshRunningComplete" : "web.item.toast.refreshRunningQuick");

    /// <summary>
    /// The toast a finished wait shows (official <c>onSuccess</c> and <c>onError</c>). A wait that
    /// ran out, or went stale because the account changed, shows nothing: the job may still be running.
    /// </summary>
    /// <summary>The server reported the job failed or canceled (not a failed read of the job).</summary>
    public static bool IsDefiniteFailure(MaintenanceJobWaitResult result)
        => result.Outcome == MaintenanceJobWaitOutcome.Canceled
           || result.Outcome == MaintenanceJobWaitOutcome.Failed && result.Error == null;

    public static MetadataRefreshToast FinishToast(MaintenanceJobWaitResult result, string mode)
    {
        // Only a job the server reports failed or canceled is a failure. A failed job read or a wait
        // that ran out leaves completion unknown: say nothing (the starting page still reloads).
        if (IsDefiniteFailure(result))
            return new(MetadataRefreshToastKind.Error, "web.item.toast.refreshFailed");
        if (result.Outcome != MaintenanceJobWaitOutcome.Succeeded)
            return new(MetadataRefreshToastKind.None);
        var item = result.Job?.ItemResult;
        var newFiles = item?.NewFiles ?? 0;
        if (item?.ArtworkCacheIncomplete == true)
            return new(MetadataRefreshToastKind.Warning, "web.item.toast.refreshArtworkWarning");
        if (mode == "complete")
            return new(MetadataRefreshToastKind.Success, "web.item.toast.completeRefreshFinished");
        if (newFiles > 0)
            return new(MetadataRefreshToastKind.Success, "web.item.toast.refreshedNewFiles",
                new Dictionary<string, object?> { ["count"] = newFiles, ["new_files"] = newFiles });
        return new(MetadataRefreshToastKind.Success, "web.item.toast.refreshed");
    }

    /// <summary>
    /// The items whose open detail pages reread after a refresh from a card, which has no page of
    /// its own to reload. Only a finished job changed anything; every ID the refresh touched is named,
    /// and the refreshed item itself may have gained files.
    /// </summary>
    public static IReadOnlyList<MetadataRefreshChange> ChangedItems(
        MaintenanceJobWaitResult result, string contentId, string mode, string? type, string? seriesId)
    {
        if (result.Outcome == MaintenanceJobWaitOutcome.Stale || IsDefiniteFailure(result)) return [];
        // As Decide: an unknown completion (a wait that ran out, a failed job read) rereads the item
        // itself without trusting any replacement or new-file count from the job.
        if (result.Outcome != MaintenanceJobWaitOutcome.Succeeded)
            return InvalidatedIds(new MaintenanceJobWaitResult(result.Outcome), contentId, type, seriesId)
                .Select(id => new MetadataRefreshChange(id, mode == "complete" && string.Equals(id, contentId, StringComparison.Ordinal)))
                .ToArray();
        var structural = mode == "complete" || result.Job?.ItemResult?.NewFiles > 0;
        return InvalidatedIds(result, contentId, type, seriesId)
            .Select(id => new MetadataRefreshChange(id, structural && string.Equals(id, contentId, StringComparison.Ordinal)))
            .ToArray();
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
