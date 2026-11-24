using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Core.Services;

public sealed record NotificationDestination(string? ContentId, string? MediaType, int? TmdbId);

public static class NotificationNavigation
{
    /// <summary>A webhook or plugin destination was turned off after repeated failed deliveries; the notice leads to notification settings, where it is fixed.</summary>
    public static bool IsDestinationDisabledNotice(AppNotification notification)
        => notification.Type is "webhook.auto_disabled" or "plugin_destination.auto_disabled";

    public static NotificationDestination? Resolve(AppNotification notification)
    {
        if (!string.IsNullOrWhiteSpace(notification.EpisodeId)) return new(notification.EpisodeId, null, null);
        if (!string.IsNullOrWhiteSpace(notification.SeriesId)) return new(notification.SeriesId, null, null);
        if (notification.Type is "request.approved" or "request.declined" &&
            notification.ReasonFlags is { MediaType: "movie" or "series", TmdbId: > 0 } flags)
            return new(null, flags.MediaType, flags.TmdbId);
        return null;
    }
}
