using System.Text.Json;

namespace SiloPlayer.Core.Services;

/// <summary>
/// One piece of toast text: a catalog <see cref="Key"/> with its arguments and English
/// fallback, or (when <see cref="Key"/> is null) server data shown exactly as sent.
/// </summary>
public sealed record NotificationToastPart(
    string? Key,
    IReadOnlyDictionary<string, object?>? Arguments,
    string Text)
{
    public static NotificationToastPart Message(string key, string fallback, IReadOnlyDictionary<string, object?>? arguments = null)
        => new(key, arguments, fallback);

    public static NotificationToastPart Literal(string text) => new(null, null, text);
}

/// <summary>
/// The official WebUI's live toast for a notification: a title and an optional description.
/// The description is the parts joined with " — " (empty = none).
/// </summary>
public sealed record NotificationToastContent(
    NotificationToastPart Title,
    IReadOnlyList<NotificationToastPart> Description)
{
    public bool HasDescription => Description.Count > 0;

    /// <summary>Title, then the description on its own line when there is one.</summary>
    public string Render(Func<NotificationToastPart, string> resolve)
    {
        var title = resolve(Title);
        if (Description.Count == 0) return title;
        var description = string.Join(" — ", Description.Select(resolve));
        return description.Length == 0 ? title : title + "\n" + description;
    }
}

/// <summary>
/// "Which toast, if any, for this notification", as the official WebUI's
/// <c>notificationToast</c> (web/src/hooks/queries/notifications.ts, silo-server 3b2b32168).
/// Reads the realtime event defensively: only the type and the fields the toast uses.
/// The official toast has no action and no click target, and it is not suppressed on the
/// Notifications page, for a read row, for a repeated id, or by the viewer's in-app setting.
/// </summary>
public static class NotificationToastRules
{
    public const string NewEpisodeKey = "web.notifications.toast.newEpisode";
    public const string SeriesAvailableKey = "web.notifications.toast.seriesAvailable";
    public const string FollowedArrivedKey = "web.notifications.toast.followedArrived";
    public const string RequestArrivedKey = "web.notifications.toast.requestArrived";
    public const string WatchlistArrivedKey = "web.notifications.toast.watchlistArrived";
    public const string RequestApprovedTitleKey = "web.notifications.toast.requestApprovedTitle";
    public const string RequestDeclinedTitleKey = "web.notifications.toast.requestDeclinedTitle";
    public const string FollowedTitleAvailableKey = "web.notifications.inbox.followedTitleAvailable";
    public const string RequestAvailableBodyKey = "web.notifications.inbox.requestAvailableBody";
    public const string WatchlistTitleAvailableKey = "web.notifications.inbox.watchlistTitleAvailable";
    public const string RequestApprovedKey = "web.notifications.inbox.requestApproved";
    public const string RequestDeclinedKey = "web.notifications.inbox.requestDeclined";
    public const string WebhookStoppedKey = "web.notifications.inbox.webhookStopped";
    public const string DestinationStoppedKey = "web.notifications.inbox.destinationStopped";
    public const string FixInSettingsKey = "web.notifications.inbox.fixInSettings";
    public const string EpisodeCodeKey = "web.notifications.queries.episodeCode";

    /// <summary>The toast for a <c>notification.created</c> payload, or null when its type has none.</summary>
    public static NotificationToastContent? For(JsonElement notification)
    {
        if (notification.ValueKind != JsonValueKind.Object) return null;
        var type = Text(notification, "type");
        var seriesTitle = Text(notification, "series_title");
        var flags = notification.TryGetProperty("reason_flags", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

        switch (type)
        {
            case "episode.available" when seriesTitle != null:
            {
                var description = new List<NotificationToastPart>();
                if (EpisodeCode(notification) is { } code) description.Add(code);
                if (Text(notification, "episode_title") is { } episodeTitle) description.Add(NotificationToastPart.Literal(episodeTitle));
                return new(
                    NotificationToastPart.Message(NewEpisodeKey, "New episode of " + seriesTitle, Args("series", seriesTitle)),
                    description);
            }
            case "request.fulfilled":
            {
                var follower = flags.ValueKind == JsonValueKind.Object
                    && flags.TryGetProperty("follower", out var f) && f.ValueKind == JsonValueKind.True;
                var title = seriesTitle != null
                    ? NotificationToastPart.Message(SeriesAvailableKey, seriesTitle + " is now available", Args("series", seriesTitle))
                    : follower
                        ? NotificationToastPart.Message(FollowedTitleAvailableKey, "A title you followed is now available")
                        : NotificationToastPart.Message(RequestAvailableBodyKey, "Your request is now available");
                var description = follower
                    ? NotificationToastPart.Message(FollowedArrivedKey, "A title you asked to hear about has arrived in the library.")
                    : NotificationToastPart.Message(RequestArrivedKey, "Your media request has arrived in the library.");
                return new(title, [description]);
            }
            case "watchlist.available":
            {
                var title = seriesTitle != null
                    ? NotificationToastPart.Message(SeriesAvailableKey, seriesTitle + " is now available", Args("series", seriesTitle))
                    : NotificationToastPart.Message(WatchlistTitleAvailableKey, "A title on your watchlist is now available");
                return new(title, [NotificationToastPart.Message(WatchlistArrivedKey, "A title on your watchlist has arrived in the library.")]);
            }
            case "request.approved" or "request.declined":
            {
                var approved = type == "request.approved";
                var title = flags.ValueKind == JsonValueKind.Object ? Text(flags, "title") : null;
                var part = title != null
                    ? approved
                        ? NotificationToastPart.Message(RequestApprovedTitleKey, "Request approved: " + title, Args("title", title))
                        : NotificationToastPart.Message(RequestDeclinedTitleKey, "Request declined: " + title, Args("title", title))
                    : approved
                        ? NotificationToastPart.Message(RequestApprovedKey, "Your request was approved")
                        : NotificationToastPart.Message(RequestDeclinedKey, "Your request was declined");
                var reason = !approved && flags.ValueKind == JsonValueKind.Object ? Text(flags, "reason") : null;
                return new(part, reason == null ? [] : [NotificationToastPart.Literal(reason)]);
            }
            case "webhook.auto_disabled":
                return new(
                    NotificationToastPart.Message(WebhookStoppedKey, "A webhook stopped working"),
                    [NotificationToastPart.Message(FixInSettingsKey, "Open notification settings to fix it.")]);
            case "plugin_destination.auto_disabled":
                return new(
                    NotificationToastPart.Message(DestinationStoppedKey, "A notification destination stopped working"),
                    [NotificationToastPart.Message(FixInSettingsKey, "Open notification settings to fix it.")]);
            default:
                return null;
        }
    }

    // formatEpisodeCode: null unless both numbers are present.
    private static NotificationToastPart? EpisodeCode(JsonElement notification)
    {
        if (Number(notification, "season_number") is not { } season || Number(notification, "episode_number") is not { } episode)
            return null;
        return NotificationToastPart.Message(
            EpisodeCodeKey,
            $"S{season}E{episode}",
            new Dictionary<string, object?> { ["season"] = season, ["episode"] = episode });
    }

    private static IReadOnlyDictionary<string, object?> Args(string name, string value)
        => new Dictionary<string, object?> { [name] = value };

    // JavaScript truthiness for a string field: absent, null, non-string and "" all read as missing.
    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static object? Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number) return null;
        return value.TryGetInt64(out var whole) ? (object)whole : value.GetDouble();
    }
}
