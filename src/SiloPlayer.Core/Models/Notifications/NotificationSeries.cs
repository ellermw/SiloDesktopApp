using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Models.Notifications;

public sealed record NotificationSeriesOptions(string Q = "", string Sort = "title", string Status = "all");

public sealed class NotificationSeriesMuteResult
{
    public string SeriesId { get; set; } = "";
    public string Outcome { get; set; } = "";
}

public sealed class NotificationSeriesMuteResponse
{
    public List<NotificationSeriesMuteResult>? Results { get; set; }
}

public sealed record NotificationSeriesMuteReceipt(IReadOnlyList<NotificationSeriesMuteResult> Results)
{
    public int Count => Results.Count(result => result.Outcome != "not_found");
    public string[] AppliedIds => Results.Where(result => result.Outcome == "applied").Select(result => result.SeriesId).ToArray();
}

public sealed class NotificationSeriesReasons
{
    public bool NextUp { get; set; }
}

public sealed class NotificationEpisodeRef
{
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
}

public sealed class NotificationSeries
{
    public MediaItem Item { get; set; } = new();
    public NotificationSeriesReasons Reasons { get; set; } = new();
    public NotificationSeriesReasons ActiveReasons { get; set; } = new();
    public bool Notifies { get; set; }
    public bool Muted { get; set; }
    public NotificationEpisodeRef? NextUpAfter { get; set; }

    /// <summary>Which new episode notifies, or why the series will not notify on its next new episode (official NotifiedSeriesList triggerText and silencedText).</summary>
    public string Explanation(NotificationPreferences? prefs)
    {
        if (Notifies)
            return NextUpAfter == null
                ? AppStrings.Get("web.notifications.series.nextNewEpisode", fallback: "The next new episode")
                : AppStrings.Get("web.notifications.series.nextEpisodeAfter", new Dictionary<string, object?> { ["code"] = $"S{NextUpAfter.SeasonNumber:00}E{NextUpAfter.EpisodeNumber:00}" }, $"The next episode after S{NextUpAfter.SeasonNumber:00}E{NextUpAfter.EpisodeNumber:00}");
        if (Muted) return AppStrings.Get("web.notifications.series.muted", fallback: "Muted");
        if (prefs?.Enabled == false) return AppStrings.Get("web.notifications.series.notificationsOff", fallback: "Notifications are off");
        if (prefs is { NotifyNextUp: false })
        {
            var names = AppStrings.Get("web.notifications.preferences.nextUp", fallback: "Next Up");
            return AppStrings.Get("web.notifications.series.reasonsOff", new Dictionary<string, object?> { ["names"] = names }, names + " notifications are off");
        }
        if (ActiveReasons.NextUp) return AppStrings.Get("web.notifications.series.afterCatchUp", fallback: "After you catch up");
        return AppStrings.Get("web.notifications.series.wontNotifyNow", fallback: "Won’t notify right now");
    }
}

public sealed class NotificationSeriesPage
{
    public List<NotificationSeries>? Items { get; set; }
    public NotificationSeriesPageInfo? Page { get; set; }
}

public sealed class NotificationSeriesPageInfo
{
    public bool? HasMore { get; set; }
    public string? NextCursor { get; set; }
}
