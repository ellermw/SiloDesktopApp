using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Notifications;

public sealed class NotificationReasonFlags
{
    public string? Title { get; set; }
    public string? Reason { get; set; }
    public bool Favorite { get; set; }
    public bool Watchlist { get; set; }
    public bool ContinueWatching { get; set; }
    public bool NextUp { get; set; }
    public string? MediaType { get; set; }
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
            return Type switch
            {
                "episode.available" => SeriesTitle ?? "New episode available",
                "request.fulfilled" => SeriesTitle ?? "Request available",
                "request.approved" or "request.declined" => ReasonFlags.Title ?? "Media request",
                _ => "Notification",
            };
        }
    }

    [JsonIgnore]
    public string Subtitle
    {
        get
        {
            if (Type == "episode.available")
            {
                var code = SeasonNumber.HasValue && EpisodeNumber.HasValue
                    ? $"S{SeasonNumber.Value}E{EpisodeNumber.Value}"
                    : null;
                var text = string.Join(" — ", new[] { code, EpisodeTitle }.Where(value => !string.IsNullOrWhiteSpace(value)));
                return string.IsNullOrWhiteSpace(text) ? "New episode available" : text;
            }
            if (Type == "request.fulfilled")
                return ReasonFlags.MediaType switch
                {
                    "movie" => "Your requested movie is now available",
                    "series" => "Your requested series is now available",
                    _ => "Your request is now available"
                };
            if (Type == "request.approved") return "Your request was approved";
            if (Type == "request.declined")
                return string.IsNullOrWhiteSpace(ReasonFlags.Reason)
                    ? "Your request was declined"
                    : $"Your request was declined — {ReasonFlags.Reason}";
            return Type;
        }
    }

    [JsonIgnore]
    public string RelativeTime
    {
        get
        {
            if (!DateTimeOffset.TryParse(CreatedAt, out var created)) return "";
            var age = DateTimeOffset.UtcNow - created;
            if (age < TimeSpan.FromMinutes(1)) return "Just now";
            if (age < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)Math.Round(age.TotalMinutes))}m ago";
            if (age < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)Math.Round(age.TotalHours))}h ago";
            if (age < TimeSpan.FromDays(7)) return $"{Math.Max(1, (int)Math.Round(age.TotalDays))}d ago";
            return created.LocalDateTime.ToString("MMM d");
        }
    }

    [JsonIgnore]
    public List<string> ReasonLabels
    {
        get
        {
            var labels = new List<string>();
            if (ReasonFlags.Favorite) labels.Add("Favorite");
            if (ReasonFlags.Watchlist) labels.Add("Watchlist");
            if (ReasonFlags.ContinueWatching) labels.Add("Continue Watching");
            if (ReasonFlags.NextUp) labels.Add("Next Up");
            return labels;
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

    [JsonIgnore]
    public string TypeLabel => string.Equals(Type, "generic", StringComparison.OrdinalIgnoreCase)
        ? "Generic"
        : "Discord";

    [JsonIgnore]
    public string StateLabel => Enabled ? "Enabled" : "Disabled";

    [JsonIgnore]
    public string EnabledReasonsText
    {
        get
        {
            var values = new List<string>();
            if (NotifyFavorites) values.Add("Favorites");
            if (NotifyWatchlist) values.Add("Watchlist");
            if (NotifyContinueWatching) values.Add("Continue Watching");
            if (NotifyNextUp) values.Add("Next Up");
            if (NotifyRequests) values.Add("Requests");
            if (values.Count == 5) return "All reasons";
            return values.Count == 0 ? "No reasons selected" : string.Join(" · ", values);
        }
    }

    [JsonIgnore]
    public string? DeliveryStatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisabledReason)) return $"Disabled: {DisabledReason}";
            if (!string.IsNullOrWhiteSpace(LastFailureAt) || ConsecutiveFailures > 0)
            {
                var detail = !string.IsNullOrWhiteSpace(LastFailureMessage)
                    ? LastFailureMessage
                    : LastFailureStatus is int status ? $"HTTP {status}" : "Delivery failed";
                return $"Last failure: {detail}. Check the destination URL.";
            }
            return !string.IsNullOrWhiteSpace(LastSuccessAt)
                ? $"Last success: {FormatRelativeTime(LastSuccessAt)}"
                : null;
        }
    }

    [JsonIgnore]
    public bool CanRotateSecret => string.Equals(Type, "generic", StringComparison.OrdinalIgnoreCase);

    private static string FormatRelativeTime(string value)
    {
        if (!DateTimeOffset.TryParse(value, out var timestamp)) return value;
        var age = DateTimeOffset.UtcNow - timestamp;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)Math.Round(age.TotalMinutes))}m ago";
        if (age < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)Math.Round(age.TotalHours))}h ago";
        if (age < TimeSpan.FromDays(7)) return $"{Math.Max(1, (int)Math.Round(age.TotalDays))}d ago";
        return timestamp.LocalDateTime.ToString("MMM d");
    }
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
