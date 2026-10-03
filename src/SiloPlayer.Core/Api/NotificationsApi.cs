using SiloPlayer.Core.Models.Notifications;
using System.Collections.Concurrent;
using System.Text.Json;

namespace SiloPlayer.Core.Api;

public sealed class NotificationsApi(SiloApiClient client)
{
    private readonly ConcurrentDictionary<string, (ApiRequestContext Context, string ETag)> _webhookRevisions = new();
    private readonly ConcurrentDictionary<(ApiRequestContext Context, string Email), string> _emailIntents = new();
    private (ApiRequestContext Context, string Cutoff)? _readCutoff;
    public bool CanMarkAllRead => _readCutoff is { } snapshot && client.IsCurrentContext(snapshot.Context);

    public async Task<NotificationListResponse> GetNotificationsAsync(
        string status = "all",
        string? before = null,
        int limit = 25,
        CancellationToken ct = default)
    {
        var qs = new List<string> { $"limit={limit}" };
        if (string.Equals(status, "unread", StringComparison.OrdinalIgnoreCase))
            qs.Add("status=unread");
        if (!string.IsNullOrWhiteSpace(before))
            qs.Add($"cursor={Uri.EscapeDataString(before)}");

        var context = client.CaptureContext();
        var result = await client.GetAsync<NotificationListResponse>(
            $"/api/v2/notifications?{string.Join("&", qs)}",
            ct);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Notification context changed.", ct);
        _readCutoff = string.IsNullOrEmpty(result.ReadCutoff) ? null : (context, result.ReadCutoff);
        return result;
    }

    public Task<NotificationSyncResponse> SyncNotificationsAsync(string? before = null, int limit = 25, CancellationToken ct = default)
    {
        var qs = new List<string> { $"limit={limit}" };
        if (!string.IsNullOrWhiteSpace(before))
            qs.Add($"cursor={Uri.EscapeDataString(before)}");
        return client.GetAsync<NotificationSyncResponse>($"/api/v2/notifications/sync?{string.Join("&", qs)}", ct);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default)
        => (await client.GetAsync<NotificationUnreadCountResponse>("/api/v2/notifications/unread-count", ct)).Count;

    public Task<NotificationPreferences> GetPreferencesAsync(CancellationToken ct = default)
        => client.GetAsync<NotificationPreferences>("/api/v2/notifications/preferences", ct);

    public Task<NotificationPreferences> UpdatePreferencesAsync(NotificationPreferences preferences, CancellationToken ct = default)
        => client.PutAsync<NotificationPreferences>("/api/v2/notifications/preferences", new Dictionary<string, object?>
        {
            ["enabled"] = preferences.Enabled,
            ["notify_favorites"] = preferences.NotifyFavorites,
            ["notify_watchlist"] = preferences.NotifyWatchlist,
            ["notify_continue_watching"] = preferences.NotifyContinueWatching,
            ["notify_next_up"] = preferences.NotifyNextUp
        }, ct);

    public Task MarkReadAsync(string id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v2/notifications/{Uri.EscapeDataString(id)}/read", new Dictionary<string, object?>(), ct);

    public Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var snapshot = _readCutoff;
        if (snapshot == null || !client.IsCurrentContext(snapshot.Value.Context))
            throw new InvalidOperationException("Reload notifications before marking them all read.");
        return client.PostNoContentAsync("/api/v2/notifications/read-all", new Dictionary<string, object?> { ["through"] = snapshot.Value.Cutoff }, ct);
    }

    public Task<NotificationCapability> GetCapabilityAsync(CancellationToken ct = default)
        => client.GetAsync<NotificationCapability>("/api/v2/notifications/capabilities", ct);

    public Task<NotificationEmailPreferences> GetEmailPreferencesAsync(CancellationToken ct = default)
        => client.GetAsync<NotificationEmailPreferences>("/api/v2/notifications/email-preferences", ct);

    public Task<NotificationEmailPreferences> UpdateEmailPreferencesAsync(NotificationEmailPreferencesUpdate update, CancellationToken ct = default)
        => client.PutAsync<NotificationEmailPreferences>("/api/v2/notifications/email-preferences", update, ct);

    public async Task<NotificationEmailPreferences> RequestEmailAddressAsync(string email, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var key = (context, email);
        var intent = _emailIntents.GetOrAdd(key, _ => Guid.NewGuid().ToString());
        await client.PutAsync<JsonElement>("/api/v2/notifications/email-preferences/address",
            new Dictionary<string, object?> { ["verification_id"] = intent, ["email"] = email }, ct);
        _emailIntents.TryRemove(key, out _);
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Notification context changed.", ct);
        return await GetEmailPreferencesAsync(ct);
    }

    public Task<NotificationEmailPreferences> ClearEmailAddressAsync(CancellationToken ct = default)
        => client.DeleteReturningAsync<NotificationEmailPreferences>("/api/v2/notifications/email-preferences/address", ct);

    public Task<NotificationDiscordPreferences> GetDiscordPreferencesAsync(CancellationToken ct = default)
        => client.GetAsync<NotificationDiscordPreferences>("/api/v2/notifications/discord-preferences", ct);

    public Task<NotificationDiscordPreferences> UpdateDiscordPreferencesAsync(string mode, CancellationToken ct = default)
        => client.PutAsync<NotificationDiscordPreferences>(
            "/api/v2/notifications/discord-preferences",
            new Dictionary<string, object?> { ["mode"] = mode },
            ct);

    public Task<NotificationDiscordLinkInit> StartDiscordLinkAsync(CancellationToken ct = default)
        => client.PostAsync<NotificationDiscordLinkInit>("/api/v2/notifications/discord/link/init", new Dictionary<string, object?>(), ct);

    public Task UnlinkDiscordAsync(CancellationToken ct = default)
        => client.DeleteAsync("/api/v2/notifications/discord-link", ct);

    public async Task<List<NotificationWebhook>> GetWebhooksAsync(CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var rows = await BrowseV2.AllAsync<NotificationWebhook>(client, "/api/v2/notifications/webhooks", ct);
        foreach (var row in rows) RememberWebhook(row, context, ct);
        return rows;
    }

    public Task<NotificationWebhook> CreateWebhookAsync(NotificationWebhookInput input, CancellationToken ct = default)
        => client.PostAsync<NotificationWebhook>("/api/v2/notifications/webhooks", JsonSerializer.SerializeToElement(input, V2Json.Options), ct);

    public async Task<NotificationWebhook> UpdateWebhookAsync(string id, NotificationWebhookInput input, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var saved = await client.PutWithETagAsync<NotificationWebhook>($"/api/v2/notifications/webhooks/{Uri.EscapeDataString(id)}",
            JsonSerializer.SerializeToElement(input, V2Json.Options), WebhookRevision(id), ct);
        RememberWebhook(saved, context, ct);
        return saved;
    }

    public async Task DeleteWebhookAsync(string id, CancellationToken ct = default)
    {
        await client.DeleteWithETagAsync($"/api/v2/notifications/webhooks/{Uri.EscapeDataString(id)}", WebhookRevision(id), ct);
        _webhookRevisions.TryRemove(id, out _);
    }

    public Task<SigningSecretResponse> RotateWebhookSecretAsync(string id, CancellationToken ct = default)
        => client.PostAsync<SigningSecretResponse>(
            $"/api/v2/notifications/webhooks/{Uri.EscapeDataString(id)}/rotate-secret",
            new Dictionary<string, object?>(),
            ct);

    public Task<NotificationWebhookTestResult> TestWebhookAsync(string id, CancellationToken ct = default)
        => client.PostAsync<NotificationWebhookTestResult>(
            $"/api/v2/notifications/webhooks/{Uri.EscapeDataString(id)}/test",
            new Dictionary<string, object?>(),
            ct);

    public async Task<List<WebPushSubscriptionView>> GetWebPushSubscriptionsAsync(CancellationToken ct = default)
        => await BrowseV2.AllAsync<WebPushSubscriptionView>(client, "/api/v2/notifications/web-push/subscriptions", ct);

    public Task DeleteWebPushSubscriptionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/notifications/web-push/subscriptions/{Uri.EscapeDataString(id)}", ct);

    private string WebhookRevision(string id)
        => _webhookRevisions.TryGetValue(id, out var revision) && client.IsCurrentContext(revision.Context)
            ? revision.ETag : throw new InvalidOperationException("Reload notification destinations before saving changes.");

    private void RememberWebhook(NotificationWebhook row, ApiRequestContext context, CancellationToken ct)
    {
        if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Notification context changed.", ct);
        if (!string.IsNullOrEmpty(row.ETag)) _webhookRevisions[row.Id] = (context, row.ETag);
        else _webhookRevisions.TryRemove(row.Id, out _);
    }

}
