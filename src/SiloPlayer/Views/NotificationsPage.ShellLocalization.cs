using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class NotificationsPage
{
    private static readonly ConditionalWeakTable<Grid, IDisposable> LocalizedRows = new();

    private static void RegisterNotificationRow(Grid row) => LocalizedRows.GetValue(row,
        element => AppLocalization.Register(element, () => UpdateNotificationRow(element)));

    private void RefreshNotificationRows()
    {
        void Refresh(DependencyObject element)
        {
            if (element is Grid row && row.DataContext is AppNotification) UpdateNotificationRow(row);
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
                Refresh(VisualTreeHelper.GetChild(element, index));
        }
        Refresh(NotificationsList);
    }

    private static string NotificationTitle(AppNotification item) => item.Type switch
    {
        "episode.available" => item.SeriesTitle ?? AppStrings.Get("web.notifications.inbox.newEpisode"),
        "request.fulfilled" => item.SeriesTitle ?? AppStrings.Get("web.notifications.inbox.requestAvailable"),
        "request.approved" or "request.declined" => item.ReasonFlags.Title ?? AppStrings.Get("web.notifications.inbox.mediaRequest"),
        _ => AppStrings.Get("web.notifications.inbox.genericTitle"),
    };

    private static string NotificationSubtitle(AppNotification item)
    {
        if (item.Type == "episode.available")
        {
            var code = item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue
                ? AppStrings.Get("web.notifications.queries.episodeCode", new Dictionary<string, object?>
                { ["season"] = item.SeasonNumber.Value.ToString(AppStrings.FormatCulture), ["episode"] = item.EpisodeNumber.Value.ToString(AppStrings.FormatCulture) }) : "";
            if (code.Length > 0 && !string.IsNullOrWhiteSpace(item.EpisodeTitle))
                return AppStrings.Get("web.notifications.inbox.episodeLine", new Dictionary<string, object?> { ["code"] = code, ["title"] = item.EpisodeTitle });
            return code.Length > 0 ? code : item.EpisodeTitle ?? AppStrings.Get("web.notifications.inbox.newEpisode");
        }
        return item.Type switch
        {
            "request.fulfilled" => AppStrings.Get(item.ReasonFlags.MediaType switch
            {
                "movie" => "web.notifications.inbox.requestedMovieAvailable",
                "series" => "web.notifications.inbox.requestedSeriesAvailable",
                _ => "web.notifications.inbox.requestAvailableBody"
            }),
            "request.approved" => AppStrings.Get("web.notifications.inbox.requestApproved"),
            "request.declined" => string.IsNullOrWhiteSpace(item.ReasonFlags.Reason)
                ? AppStrings.Get("web.notifications.inbox.requestDeclined")
                : AppStrings.Get("web.notifications.inbox.requestDeclinedReason", new Dictionary<string, object?> { ["reason"] = item.ReasonFlags.Reason }),
            _ => item.Type
        };
    }

    private static string NotificationTime(AppNotification item)
    {
        if (!DateTimeOffset.TryParse(item.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var created)) return "";
        var age = DateTimeOffset.UtcNow - created;
        if (age < TimeSpan.FromMinutes(1)) return AppStrings.Get("web.notifications.inbox.justNow");
        var (key, argument, count) = age < TimeSpan.FromHours(1) ? ("web.notifications.inbox.minutesAgo", "minutes", age.TotalMinutes)
            : age < TimeSpan.FromDays(1) ? ("web.notifications.inbox.hoursAgo", "hours", age.TotalHours)
            : ("web.notifications.inbox.daysAgo", "days", age.TotalDays);
        return age < TimeSpan.FromDays(7)
            ? AppStrings.Get(key, new Dictionary<string, object?> { [argument] = Math.Max(1, (int)Math.Round(count)).ToString(AppStrings.FormatCulture) })
            : SiloPlayer.Helpers.DateTimeDisplay.FormatDate(created, medium: true);
    }

    private static List<string> NotificationReasons(AppNotification item)
    {
        var labels = new List<string>();
        if (item.ReasonFlags.Favorite) labels.Add(AppStrings.Get("web.notifications.reason.favorite"));
        if (item.ReasonFlags.Watchlist) labels.Add(AppStrings.Get("web.notifications.reason.watchlist"));
        if (item.ReasonFlags.ContinueWatching) labels.Add(AppStrings.Get("web.notifications.reason.continueWatching"));
        if (item.ReasonFlags.NextUp) labels.Add(AppStrings.Get("web.notifications.reason.nextUp"));
        return labels;
    }
}
