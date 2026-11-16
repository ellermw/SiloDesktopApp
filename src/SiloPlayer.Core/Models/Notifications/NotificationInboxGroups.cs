using System.Globalization;

namespace SiloPlayer.Core.Models.Notifications;

public sealed record NotificationInboxEntry(string Key, AppNotification Latest, List<AppNotification> Notifications, string? SeriesId);
public sealed record NotificationInboxSection(string Day, List<NotificationInboxEntry> Entries);

public static class NotificationInboxGroups
{
    public static IReadOnlyList<NotificationInboxSection> Group(IEnumerable<AppNotification> notifications,
        DateTimeOffset? now = null, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var today = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.Now, zone).Date;
        var sections = new Dictionary<string, NotificationInboxSection>(StringComparer.Ordinal);
        var bySeries = new Dictionary<string, NotificationInboxEntry>(StringComparer.Ordinal);
        foreach (var notification in notifications)
        {
            var day = "older";
            if (DateTimeOffset.TryParse(notification.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
            {
                var days = (today - TimeZoneInfo.ConvertTime(created, zone).Date).Days;
                day = days <= 0 ? "today" : days == 1 ? "yesterday" : days < 7 ? "week" : "older";
            }
            if (!sections.TryGetValue(day, out var section)) sections[day] = section = new(day, []);
            var seriesId = notification.Type == "episode.available" && !string.IsNullOrWhiteSpace(notification.SeriesId) ? notification.SeriesId : null;
            var key = seriesId == null ? notification.Id : day + ":" + seriesId;
            if (seriesId != null && bySeries.TryGetValue(key, out var group)) group.Notifications.Add(notification);
            else
            {
                var entry = new NotificationInboxEntry(key, notification, [notification], seriesId);
                section.Entries.Add(entry);
                if (seriesId != null) bySeries[key] = entry;
            }
        }
        return new[] { "today", "yesterday", "week", "older" }.Where(sections.ContainsKey).Select(day => sections[day]).ToArray();
    }
}
