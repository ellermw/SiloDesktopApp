using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Models.Notifications;

public sealed record NotificationSeriesOptions(string Q = "", string? Reason = null, string Sort = "title", string Status = "all");

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
    public bool Favorite { get; set; }
    public bool Watchlist { get; set; }
    public bool ContinueWatching { get; set; }
    public bool NextUp { get; set; }
    public IEnumerable<(string Label, bool Active)> Labels(NotificationSeriesReasons active, bool muted)
    {
        if (Favorite) yield return ("Favorite", active.Favorite && !muted);
        if (Watchlist) yield return ("Watchlist", active.Watchlist && !muted);
        if (ContinueWatching) yield return ("Continue Watching", active.ContinueWatching && !muted);
        if (NextUp) yield return ("Next Up", active.NextUp && !muted);
    }
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

    public string Explanation(NotificationPreferences? prefs)
    {
        if (Notifies)
            return ActiveReasons.Favorite || ActiveReasons.Watchlist || ActiveReasons.ContinueWatching || NextUpAfter == null
                ? AppStrings.Get("web.notifications.series.anyNewEpisode", fallback: "Any new episode")
                : AppStrings.Get("web.notifications.series.anyEpisodeAfter", new Dictionary<string, object?> { ["code"] = $"S{NextUpAfter.SeasonNumber:00}E{NextUpAfter.EpisodeNumber:00}" }, $"Any episode after S{NextUpAfter.SeasonNumber:00}E{NextUpAfter.EpisodeNumber:00}");
        if (Muted) return AppStrings.Get("web.notifications.series.muted", fallback: "Muted");
        if (prefs?.Enabled == false) return AppStrings.Get("web.notifications.series.notificationsOff", fallback: "Notifications are off");
        var off = new List<string>();
        if (prefs != null)
        {
            if (Reasons.Favorite && !prefs.NotifyFavorites) off.Add(AppStrings.Get("web.notifications.preferences.favorites", fallback: "Favorites"));
            if (Reasons.Watchlist && !prefs.NotifyWatchlist) off.Add(AppStrings.Get("web.notifications.preferences.watchlist", fallback: "Watchlist"));
            if (Reasons.ContinueWatching && !prefs.NotifyContinueWatching) off.Add(AppStrings.Get("web.notifications.preferences.continueWatching", fallback: "Continue Watching"));
            if (Reasons.NextUp && !prefs.NotifyNextUp) off.Add(AppStrings.Get("web.notifications.preferences.nextUp", fallback: "Next Up"));
        }
        if (off.Count == 0) return AppStrings.Get("web.notifications.series.wontNotifyNow", fallback: "Won’t notify right now");
        var names = off.Count == 1 ? off[0] : AppStrings.Get("web.notifications.series.nameList", new Dictionary<string, object?> { ["names"] = string.Join(", ", off.SkipLast(1)), ["last"] = off[^1] }, string.Join(", ", off.SkipLast(1)) + " and " + off[^1]);
        return AppStrings.Get("web.notifications.series.reasonsOff", new Dictionary<string, object?> { ["names"] = names }, names + " notifications are off");
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
