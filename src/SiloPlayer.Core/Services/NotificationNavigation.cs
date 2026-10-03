using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Core.Services;

public sealed record NotificationDestination(string? ContentId, string? MediaType, int? TmdbId);

public static class NotificationNavigation
{
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
