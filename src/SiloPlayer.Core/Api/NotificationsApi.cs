using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Core.Api;

public sealed class NotificationsApi(SiloApiClient client)
{
    public Task<NotificationListResponse> GetNotificationsAsync(
        string status = "all",
        string? before = null,
        int limit = 25,
        CancellationToken ct = default)
    {
        var qs = new List<string> { $"limit={limit}" };
        if (string.Equals(status, "unread", StringComparison.OrdinalIgnoreCase))
            qs.Add("status=unread");
        if (!string.IsNullOrWhiteSpace(before))
            qs.Add($"before={Uri.EscapeDataString(before)}");

        return client.GetAsync<NotificationListResponse>(
            $"/api/v1/notifications?{string.Join("&", qs)}",
            ct);
    }

    public Task<NotificationSyncResponse> SyncNotificationsAsync(string? before = null, int limit = 25, CancellationToken ct = default)
    {
        var qs = new List<string> { $"limit={limit}" };
        if (!string.IsNullOrWhiteSpace(before))
            qs.Add($"before={Uri.EscapeDataString(before)}");
        return client.GetAsync<NotificationSyncResponse>($"/api/v1/notifications/sync?{string.Join("&", qs)}", ct);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default)
        => (await client.GetAsync<NotificationUnreadCountResponse>("/api/v1/notifications/unread-count", ct)).Count;

    public Task<NotificationPreferences> GetPreferencesAsync(CancellationToken ct = default)
        => client.GetAsync<NotificationPreferences>("/api/v1/notifications/preferences", ct);

    public Task<NotificationPreferences> UpdatePreferencesAsync(NotificationPreferences preferences, CancellationToken ct = default)
        => client.PutAsync<NotificationPreferences>("/api/v1/notifications/preferences", preferences, ct);

    public Task MarkReadAsync(string id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/notifications/{Uri.EscapeDataString(id)}/read", new Dictionary<string, object?>(), ct);

    public Task MarkAllReadAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/notifications/read-all", new Dictionary<string, object?>(), ct);

    public Task<NotificationCapability> GetCapabilityAsync(CancellationToken ct = default)
        => client.GetAsync<NotificationCapability>("/api/v1/notifications/capability", ct);

    public Task<NotificationEmailPreferences> GetEmailPreferencesAsync(CancellationToken ct = default)
        => client.GetAsync<NotificationEmailPreferences>("/api/v1/notifications/email-preferences", ct);

    public Task<NotificationEmailPreferences> UpdateEmailPreferencesAsync(NotificationEmailPreferencesUpdate update, CancellationToken ct = default)
        => client.PutAsync<NotificationEmailPreferences>("/api/v1/notifications/email-preferences", update, ct);

    public Task<NotificationEmailPreferences> RequestEmailAddressAsync(string email, CancellationToken ct = default)
        => client.PutAsync<NotificationEmailPreferences>(
            "/api/v1/notifications/email-preferences/address",
            new Dictionary<string, object?> { ["email"] = email },
            ct);

    public Task<NotificationEmailPreferences> ClearEmailAddressAsync(CancellationToken ct = default)
        => client.DeleteReturningAsync<NotificationEmailPreferences>("/api/v1/notifications/email-preferences/address", ct);

    public Task<NotificationDiscordPreferences> GetDiscordPreferencesAsync(CancellationToken ct = default)
        => client.GetAsync<NotificationDiscordPreferences>("/api/v1/notifications/discord-preferences", ct);

    public Task<NotificationDiscordPreferences> UpdateDiscordPreferencesAsync(string mode, CancellationToken ct = default)
        => client.PutAsync<NotificationDiscordPreferences>(
            "/api/v1/notifications/discord-preferences",
            new Dictionary<string, object?> { ["mode"] = mode },
            ct);

    public Task<NotificationDiscordLinkInit> StartDiscordLinkAsync(CancellationToken ct = default)
        => client.PostAsync<NotificationDiscordLinkInit>("/api/v1/notifications/discord/link/init", new Dictionary<string, object?>(), ct);

    public Task UnlinkDiscordAsync(CancellationToken ct = default)
        => client.DeleteAsync("/api/v1/notifications/discord-link", ct);

    public async Task<List<NotificationWebhook>> GetWebhooksAsync(CancellationToken ct = default)
        => (await client.GetAsync<NotificationWebhookListResponse>("/api/v1/notifications/webhooks", ct)).Webhooks;

    public Task<NotificationWebhook> CreateWebhookAsync(NotificationWebhookInput input, CancellationToken ct = default)
        => client.PostAsync<NotificationWebhook>("/api/v1/notifications/webhooks", input, ct);

    public Task<NotificationWebhook> UpdateWebhookAsync(string id, NotificationWebhookInput input, CancellationToken ct = default)
        => client.PutAsync<NotificationWebhook>($"/api/v1/notifications/webhooks/{Uri.EscapeDataString(id)}", input, ct);

    public Task DeleteWebhookAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/notifications/webhooks/{Uri.EscapeDataString(id)}", ct);

    public Task<SigningSecretResponse> RotateWebhookSecretAsync(string id, CancellationToken ct = default)
        => client.PostAsync<SigningSecretResponse>(
            $"/api/v1/notifications/webhooks/{Uri.EscapeDataString(id)}/rotate-secret",
            new Dictionary<string, object?>(),
            ct);

    public Task<NotificationWebhookTestResult> TestWebhookAsync(string id, CancellationToken ct = default)
        => client.PostAsync<NotificationWebhookTestResult>(
            $"/api/v1/notifications/webhooks/{Uri.EscapeDataString(id)}/test",
            new Dictionary<string, object?>(),
            ct);

    public async Task<List<WebPushSubscriptionView>> GetWebPushSubscriptionsAsync(CancellationToken ct = default)
        => (await client.GetAsync<WebPushSubscriptionsResponse>("/api/v1/notifications/web-push/subscriptions", ct)).Subscriptions;

    public Task DeleteWebPushSubscriptionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/notifications/web-push/subscriptions/{Uri.EscapeDataString(id)}", ct);

}
