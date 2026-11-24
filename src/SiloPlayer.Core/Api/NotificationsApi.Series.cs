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
        if (status is not ("all" or "muted" or "active" or "inactive")) throw new ArgumentException("Unsupported Series status.", nameof(status));
        if (options.Sort is not ("title" or "-title" or "-latest_episode_added" or "-added_at" or "-year")) throw new ArgumentException("Unsupported Series sort.");
        var query = $"status={status}&limit=50&image_size=small";
        if (!string.IsNullOrWhiteSpace(options.Q)) query += "&q=" + Uri.EscapeDataString(options.Q.Trim());
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

    public async Task<List<NotificationSeriesMuteResult>> SetManySeriesMutedAsync(ApiRequestContext context,
        IEnumerable<string> seriesIds, bool muted, CancellationToken ct = default)
    {
        var ids = seriesIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("A series is required.", nameof(seriesIds));
        RequireSeriesContext(context, ct);
        var results = new List<NotificationSeriesMuteResult>();
        foreach (var batch in ids.Chunk(500))
        {
            RequireSeriesContext(context, ct);
            var response = await client.SendRequestWithoutRefreshAsync<NotificationSeriesMuteResponse>(context, HttpMethod.Post,
                muted ? "/api/v2/notifications/series/mute" : "/api/v2/notifications/series/unmute",
                new Dictionary<string, object?> { ["series_ids"] = batch }, ct);
            RequireSeriesContext(context, ct);
            if (response.Results == null || response.Results.Count != batch.Length)
                throw new InvalidDataException("Invalid series mute receipt.");
            var receipts = new Dictionary<string, NotificationSeriesMuteResult>(StringComparer.Ordinal);
            foreach (var result in response.Results)
            {
                if (result == null || result.Outcome is not ("applied" or "unchanged" or "not_found") ||
                    !batch.Contains(result.SeriesId, StringComparer.Ordinal) || !receipts.TryAdd(result.SeriesId, result))
                    throw new InvalidDataException("Invalid series mute receipt.");
            }
            results.AddRange(batch.Select(id => receipts[id]));
        }
        RequireSeriesContext(context, ct);
        return results;
    }

    private void RequireSeriesContext(ApiRequestContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Notification context changed.", ct);
    }
}
