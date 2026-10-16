using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Models.Notifications;

public sealed record NotificationSeriesOptions(string Q = "", string? Reason = null, string Sort = "title");

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
                ? "Any new episode" : $"Any episode after S{NextUpAfter.SeasonNumber:00}E{NextUpAfter.EpisodeNumber:00}";
        if (Muted) return "Muted";
        if (prefs?.Enabled == false) return "Notifications are off";
        var off = new List<string>();
        if (prefs != null)
        {
            if (Reasons.Favorite && !prefs.NotifyFavorites) off.Add("Favorites");
            if (Reasons.Watchlist && !prefs.NotifyWatchlist) off.Add("Watchlist");
            if (Reasons.ContinueWatching && !prefs.NotifyContinueWatching) off.Add("Continue Watching");
            if (Reasons.NextUp && !prefs.NotifyNextUp) off.Add("Next Up");
        }
        if (off.Count == 0) return "Won’t notify right now";
        return (off.Count == 1 ? off[0] : string.Join(", ", off.SkipLast(1)) + " and " + off[^1]) + " notifications are off";
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
