using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Core.Api;

public sealed partial class NotificationsApi
{
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);

    public async Task<NotificationCapability> GetCapabilityAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        var result = await client.SendRequestAsync<NotificationCapability>(context, HttpMethod.Get, "/api/v2/notifications/capabilities", null, ct);
        RequireSeriesContext(context, ct); return result;
    }

    public async Task<NotificationPreferences> GetPreferencesAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        var result = await client.SendRequestAsync<NotificationPreferences>(context, HttpMethod.Get, "/api/v2/notifications/preferences", null, ct);
        RequireSeriesContext(context, ct); return result;
    }

    public async Task<NotificationSeriesPage> GetSeriesAsync(ApiRequestContext context, string status,
        NotificationSeriesOptions options, string? cursor = null, CancellationToken ct = default)
    {
        if (status is not ("active" or "inactive")) throw new ArgumentException("Unsupported Series status.", nameof(status));
        if (options.Reason is not (null or "favorite" or "watchlist" or "continue_watching" or "next_up")) throw new ArgumentException("Unsupported Series reason.");
        if (options.Sort is not ("title" or "-title" or "-latest_episode_added" or "-added_at" or "-year")) throw new ArgumentException("Unsupported Series sort.");
        var query = $"status={status}&limit=50&image_size=small";
        if (!string.IsNullOrWhiteSpace(options.Q)) query += "&q=" + Uri.EscapeDataString(options.Q.Trim());
        if (options.Reason != null) query += "&reason=" + options.Reason;
        query += "&sort=" + options.Sort;
        if (cursor != null) query += "&cursor=" + Uri.EscapeDataString(cursor);
        var result = await client.SendRequestAsync<NotificationSeriesPage>(context, HttpMethod.Get, "/api/v2/notifications/series?" + query, null, ct);
        RequireSeriesContext(context, ct);
        if (result.Items == null || result.Page?.HasMore == null || result.Page.HasMore == true &&
            (string.IsNullOrWhiteSpace(result.Page.NextCursor) || result.Page.NextCursor == cursor))
            throw new InvalidDataException("Invalid series page. Reload notifications.");
        return result;
    }

    public async Task SetSeriesMutedAsync(ApiRequestContext context, string seriesId, bool muted, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(seriesId)) throw new ArgumentException("A series is required.", nameof(seriesId));
        await client.SendNoContentRequestWithoutRefreshAsync(context, muted ? HttpMethod.Put : HttpMethod.Delete,
            $"/api/v2/notifications/series/{Uri.EscapeDataString(seriesId)}/mute", null, ct);
        RequireSeriesContext(context, ct);
    }

    private void RequireSeriesContext(ApiRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Notification context changed.", ct);
    }
}
