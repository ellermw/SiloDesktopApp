using System.Text.Json.Serialization;

namespace ContinuumPlayer.Core.Models.Notifications;

public sealed class NotificationReasonFlags
{
    public string? Title { get; set; }
    public string? Reason { get; set; }
}

public sealed class AppNotification
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public int? LibraryId { get; set; }
    public string? SeriesId { get; set; }
    public string? EpisodeId { get; set; }
    public string? SeriesTitle { get; set; }
    public string? EpisodeTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? PosterPath { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public NotificationReasonFlags ReasonFlags { get; set; } = new();
    public string CreatedAt { get; set; } = "";
    public string? ReadAt { get; set; }

    [JsonIgnore]
    public bool IsUnread => string.IsNullOrWhiteSpace(ReadAt);

    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ReasonFlags.Title)) return ReasonFlags.Title!;
            if (!string.IsNullOrWhiteSpace(EpisodeTitle) && !string.IsNullOrWhiteSpace(SeriesTitle))
                return $"{SeriesTitle} - {EpisodeTitle}";
            if (!string.IsNullOrWhiteSpace(SeriesTitle)) return SeriesTitle!;
            return Type switch
            {
                "episode.available" => "New episode available",
                "request.fulfilled" => "Request fulfilled",
                "request.approved" => "Request approved",
                "request.declined" => "Request declined",
                _ => "Notification",
            };
        }
    }

    [JsonIgnore]
    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (SeasonNumber.HasValue && EpisodeNumber.HasValue)
                parts.Add($"S{SeasonNumber.Value}E{EpisodeNumber.Value}");
            if (!string.IsNullOrWhiteSpace(ReasonFlags.Reason))
                parts.Add(ReasonFlags.Reason!);
            parts.Add(Type.Replace('.', ' '));
            return string.Join(" · ", parts);
        }
    }
}

public sealed class NotificationListResponse
{
    public List<AppNotification> Notifications { get; set; } = [];
    public string? NextCursor { get; set; }
}

public sealed class NotificationSyncResponse
{
    public List<AppNotification> Notifications { get; set; } = [];
    public string? NextCursor { get; set; }
    public int UnreadCount { get; set; }
}

public sealed class NotificationUnreadCountResponse
{
    public int Count { get; set; }
}

public sealed class NotificationPreferences
{
    public string ProfileId { get; set; } = "";
    public bool Enabled { get; set; }
    public bool NotifyFavorites { get; set; }
    public bool NotifyWatchlist { get; set; }
    public bool NotifyContinueWatching { get; set; }
    public bool NotifyNextUp { get; set; }
}

public sealed class NotificationReadEventPayload
{
    public string ProfileId { get; set; } = "";
    public string? Id { get; set; }
    public bool All { get; set; }
}

public sealed class NotificationAccountChannelCapability
{
    public bool Available { get; set; }
    public List<string> Modes { get; set; } = [];
    public int DigestHour { get; set; }
}

public sealed class NotificationCapability
{
    public NotificationInAppCapability InApp { get; set; } = new();
    public NotificationPushCapability ApplePush { get; set; } = new();
    public NotificationPushCapability AndroidPush { get; set; } = new();
    public NotificationWebPushCapability WebPush { get; set; } = new();
    public NotificationWebhookCapability Webhooks { get; set; } = new();
    public NotificationAccountChannelCapability Email { get; set; } = new();
    public NotificationAccountChannelCapability Discord { get; set; } = new();
}

public sealed class NotificationInAppCapability
{
    public bool Enabled { get; set; }
}

public sealed class NotificationPushCapability
{
    public bool Available { get; set; }
    public string Provider { get; set; } = "";
    public List<string> SupportedModes { get; set; } = [];
}

public sealed class NotificationWebPushCapability
{
    public bool Available { get; set; }
    public string? PublicKey { get; set; }
}

public sealed class NotificationWebhookCapability
{
    public bool Available { get; set; }
    public int MaxPerProfile { get; set; }
    public List<string> SupportedTypes { get; set; } = [];
}

public sealed class NotificationEmailPreferences
{
    public string Mode { get; set; } = "off";
    public string CustomEmail { get; set; } = "";
    public string PendingEmail { get; set; } = "";
    public bool CanEditAddress { get; set; }
}

public sealed class NotificationEmailPreferencesUpdate
{
    public string Mode { get; set; } = "off";
}

public sealed class NotificationDiscordPreferences
{
    public bool Linked { get; set; }
    public string? DiscordUsername { get; set; }
    public string Mode { get; set; } = "off";
    public string? LinkFailure { get; set; }
}

public sealed class NotificationDiscordLinkInit
{
    public string Url { get; set; } = "";
}

public sealed class NotificationWebhook
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "discord";
    public string UrlHost { get; set; } = "";
    public bool Enabled { get; set; }
    public bool NotifyFavorites { get; set; }
    public bool NotifyWatchlist { get; set; }
    public bool NotifyContinueWatching { get; set; }
    public bool NotifyNextUp { get; set; }
    public bool NotifyRequests { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? DisabledReason { get; set; }
    public string? LastSuccessAt { get; set; }
    public string? LastFailureAt { get; set; }
    public int? LastFailureStatus { get; set; }
    public string? LastFailureMessage { get; set; }
    public string? SigningSecret { get; set; }
}

public sealed class NotificationWebhookInput
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? Type { get; set; }
    public bool? Enabled { get; set; }
    public bool? NotifyFavorites { get; set; }
    public bool? NotifyWatchlist { get; set; }
    public bool? NotifyContinueWatching { get; set; }
    public bool? NotifyNextUp { get; set; }
    public bool? NotifyRequests { get; set; }
}

public sealed class NotificationWebhookListResponse
{
    public List<NotificationWebhook> Webhooks { get; set; } = [];
}

public sealed class NotificationWebhookTestResult
{
    public bool Ok { get; set; }
    public int? HttpStatus { get; set; }
    public int DurationMs { get; set; }
    public string? Message { get; set; }
}

public sealed class ServerNotificationChannel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "discord";
    public string UrlHost { get; set; } = "";
    public bool Enabled { get; set; }
    public bool NotifyNewMovies { get; set; }
    public bool NotifyNewEpisodes { get; set; }
    public bool NotifyRequestSubmitted { get; set; }
    public bool NotifyRequestApproved { get; set; }
    public bool NotifyRequestDeclined { get; set; }
    public bool NotifyRequestFulfilled { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? DisabledReason { get; set; }
    public string? LastSuccessAt { get; set; }
    public string? LastFailureAt { get; set; }
    public int? LastFailureStatus { get; set; }
    public string? LastFailureMessage { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? SigningSecret { get; set; }
}

public sealed class ServerNotificationChannelInput
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? Type { get; set; }
    public bool? Enabled { get; set; }
    public bool? NotifyNewMovies { get; set; }
    public bool? NotifyNewEpisodes { get; set; }
    public bool? NotifyRequestSubmitted { get; set; }
    public bool? NotifyRequestApproved { get; set; }
    public bool? NotifyRequestDeclined { get; set; }
    public bool? NotifyRequestFulfilled { get; set; }
}

public sealed class ServerNotificationChannelsResponse
{
    public List<ServerNotificationChannel> Channels { get; set; } = [];
}

public sealed class SigningSecretResponse
{
    public string SigningSecret { get; set; } = "";
}

public sealed class WebPushSubscriptionView
{
    public string Id { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string? DeviceName { get; set; }
    public bool Enabled { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? LastSuccessAt { get; set; }
    public string? LastFailureAt { get; set; }
}

public sealed class WebPushSubscriptionsResponse
{
    public List<WebPushSubscriptionView> Subscriptions { get; set; } = [];
}
