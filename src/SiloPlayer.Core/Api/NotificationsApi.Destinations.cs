using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Plugin-provided notification destinations (official silo-server 3b2b32168, #3550).
/// Every call is bound to the identity it started under and is dropped, never replayed,
/// after an account or profile switch. Writes never refresh authentication and never
/// retry: a create, test send or delete reaches the server at most once.
/// </summary>
public sealed partial class NotificationsApi
{
    private const string PluginChannelsPath = "/api/v2/notifications/plugin-channels";
    internal const string PluginDestinationsPath = "/api/v2/notifications/plugin-destinations";
    private const int DestinationPageLimit = 100;
    private const int DestinationPageCap = 100;

    public async Task<List<NotificationPluginChannel>> GetPluginChannelsAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        RequireSeriesContext(context, ct);
        var result = await client.SendRequestAsync<NotificationPluginChannelCollection>(context, HttpMethod.Get, PluginChannelsPath, null, ct);
        RequireSeriesContext(context, ct);
        return result.Items ?? throw new InvalidDataException(AppStrings.Get("web.common.errors.notificationDestinationPageInvalid", fallback: "Invalid notification destination page."));
    }

    /// <summary>The profile's whole destination list, 100 a page, at most 100 pages, refusing a page or cursor the server repeats.</summary>
    public async Task<List<NotificationPluginDestination>> GetPluginDestinationsAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        var items = new List<NotificationPluginDestination>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var page = 0; page < DestinationPageCap; page++)
        {
            RequireSeriesContext(context, ct);
            var path = $"{PluginDestinationsPath}?limit={DestinationPageLimit}" + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var result = await client.SendRequestAsync<NotificationPluginDestinationCollection>(context, HttpMethod.Get, path, null, ct);
            RequireSeriesContext(context, ct);
            if (result.Items == null || result.Page == null)
                throw new InvalidDataException(AppStrings.Get("web.common.errors.notificationDestinationPageInvalid", fallback: "Invalid notification destination page."));
            items.AddRange(result.Items);
            if (!result.Page.HasMore) return items;
            var next = result.Page.NextCursor;
            if (string.IsNullOrEmpty(next) || !seen.Add(next))
                throw new InvalidDataException(AppStrings.Get("web.common.errors.notificationDestinationContinuationInvalid", fallback: "Invalid notification destination continuation."));
            cursor = next;
        }
        throw new InvalidDataException(AppStrings.Get("web.common.errors.notificationDestinationsTooMany", fallback: "Too many notification destinations to display. Reload the list."));
    }

    /// <summary>POST once. A replay answers 422 for the taken name, so this never refreshes authentication or retries.</summary>
    public async Task<NotificationPluginDestination> CreatePluginDestinationAsync(ApiRequestContext context,
        string pluginId, string capabilityId, string name, Dictionary<string, object?> settings,
        bool notifyNextUp, bool notifyRequests, CancellationToken ct = default)
    {
        RequireSeriesContext(context, ct);
        var created = await client.SendRequestWithoutRefreshAsync<NotificationPluginDestination>(context, HttpMethod.Post, PluginDestinationsPath,
            new Dictionary<string, object?>
            {
                ["plugin_id"] = pluginId,
                ["capability_id"] = capabilityId,
                ["name"] = name,
                ["settings"] = settings,
                ["notify_next_up"] = notifyNextUp,
                ["notify_requests"] = notifyRequests,
            }, ct);
        RequireSeriesContext(context, ct);
        return created;
    }

    /// <summary>PUT with the validator of the row the person saw. A blank or wildcard validator is refused before anything is sent.</summary>
    public async Task<NotificationPluginDestination> UpdatePluginDestinationAsync(ApiRequestContext context,
        string id, string etag, Dictionary<string, object?> body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(etag) || etag == "*")
            throw new InvalidOperationException(AppStrings.Get("web.settings.notifications.destinations.reloadBeforeEdit", fallback: "Reload the destination list before editing this destination."));
        RequireSeriesContext(context, ct);
        var response = await client.PutWithETagWithoutRefreshAsync<NotificationPluginDestination>(context,
            $"{PluginDestinationsPath}/{Uri.EscapeDataString(id)}", body, etag, ct);
        RequireSeriesContext(context, ct);
        return response.Body;
    }

    public async Task DeletePluginDestinationAsync(ApiRequestContext context, string id, string etag, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(etag) || etag == "*")
            throw new InvalidOperationException(AppStrings.Get("web.settings.notifications.destinations.reloadBeforeDelete", fallback: "Reload the destination list before deleting this destination."));
        RequireSeriesContext(context, ct);
        await client.DeleteWithETagWithoutRefreshAsync(context, $"{PluginDestinationsPath}/{Uri.EscapeDataString(id)}", etag, ct);
        RequireSeriesContext(context, ct);
    }

    /// <summary>One test message. A failed send is a 200 with ok false; it is not retried.</summary>
    public async Task<NotificationWebhookTestResult> TestPluginDestinationAsync(ApiRequestContext context, string id, CancellationToken ct = default)
    {
        RequireSeriesContext(context, ct);
        var result = await client.SendRequestWithoutRefreshAsync<NotificationWebhookTestResult>(context, HttpMethod.Post,
            $"{PluginDestinationsPath}/{Uri.EscapeDataString(id)}/test", null, ct);
        RequireSeriesContext(context, ct);
        return result;
    }
}
