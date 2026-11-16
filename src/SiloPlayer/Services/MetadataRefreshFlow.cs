using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.MediaMaintenance;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

/// <summary>A Refresh Metadata job the server accepted, bound to the identity that started it.</summary>
public readonly record struct MetadataRefreshStart(ApiIdentityContext Identity, MetadataRefreshReceipt Receipt);

/// <summary>
/// The refresh flow the item detail page and the card menu share (official
/// <c>useRefreshItemMetadata</c>, #4355): post, running toast, bounded job
/// follow, cache invalidation, finish toast. What a finished refresh does to
/// a page is the caller's: the detail page reloads itself, a card notifies the
/// detail pages that are open.
/// </summary>
public static class MetadataRefreshFlow
{
    /// <summary>
    /// Posts the refresh and shows the running toast. Null when the account or profile changed
    /// while the request was out: nothing failed, so nothing is said.
    /// </summary>
    public static async Task<MetadataRefreshStart?> StartAsync(
        MediaMaintenanceApi api, ToastService toast, string contentId, string mode)
    {
        var identity = api.CaptureIdentityContext();
        var receipt = await api.RefreshMetadataAsync(contentId, mode);
        if (!api.IsCurrentIdentityContext(identity)) return null;
        Show(toast, MetadataRefreshFollow.RunningToast(mode));
        return new MetadataRefreshStart(identity, receipt);
    }

    /// <summary>
    /// Follows the job (bounded, see <see cref="MaintenanceJobFollower"/>), drops every cached detail
    /// the refresh touched, and shows the finish toast. Returns the wait's result, or null when
    /// nothing more should happen: the identity changed, the follow was canceled, or the job read
    /// threw. A wait that ran out, or failed, still returns its result so the caller sees the outcome.
    /// </summary>
    public static async Task<MaintenanceJobWaitResult?> FollowAsync(
        MediaMaintenanceApi api, ToastService toast, ItemDetailPrefetchCache? cache, MetadataRefreshStart start,
        string contentId, string? type, string? seriesId, string mode, CancellationToken ct = default)
    {
        var (identity, receipt) = (start.Identity, start.Receipt);
        MaintenanceJobWaitResult result;
        try
        {
            result = string.IsNullOrWhiteSpace(receipt.Id)
                ? new MaintenanceJobWaitResult(MaintenanceJobWaitOutcome.TimedOut)
                : await new MaintenanceJobFollower(
                    token => api.GetJobAsync(identity, receipt.Id, token),
                    () => api.IsCurrentIdentityContext(identity)).WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            LocalLog.AppendException("state_trace.txt", ex, "metadata_refresh_follow");
            return null;
        }

        // Signed out or switched account or profile while it ran: nothing failed, so say nothing.
        if (result.Outcome == MaintenanceJobWaitOutcome.Stale || !api.IsCurrentIdentityContext(identity)) return null;
        // Every later read of the item, from any page, must skip detail cached before the refresh.
        if (cache != null)
            foreach (var id in MetadataRefreshFollow.InvalidatedIds(result, contentId, type, seriesId))
                cache.Invalidate(id);

        if (result.Error != null)
            LocalLog.AppendException("state_trace.txt", result.Error, "metadata_refresh_job_read");
        if (MetadataRefreshFollow.IsDefiniteFailure(result))
            LocalLog.AppendLine("state_trace.txt",
                $"metadata_refresh_job {result.Outcome}: {result.Job?.Failure?.Title} {result.Job?.Failure?.Detail}".TrimEnd());
        else if (result.Outcome != MaintenanceJobWaitOutcome.Succeeded)
            // The POST succeeded but completion is unknown (still running, an older server with no
            // job, or an unreadable job): the cache is already dropped and no toast is shown.
            LocalLog.AppendLine("state_trace.txt", "metadata_refresh_job wait ended before the job finished");
        Show(toast, MetadataRefreshFollow.FinishToast(result, mode));
        return result;
    }

    private static void Show(ToastService toast, MetadataRefreshToast plan)
    {
        if (plan.Key is not { } key) return;
        var text = AppStrings.Get(key, plan.Arguments);
        switch (plan.Kind)
        {
            case MetadataRefreshToastKind.Info: toast.Info(text); break;
            case MetadataRefreshToastKind.Success: toast.Success(text); break;
            case MetadataRefreshToastKind.Warning: toast.Warning(text); break;
            case MetadataRefreshToastKind.Error: toast.Error(text); break;
        }
    }
}
