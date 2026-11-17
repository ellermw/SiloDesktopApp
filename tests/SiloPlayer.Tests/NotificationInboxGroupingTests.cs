using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Tests;

public sealed class NotificationInboxGroupingTests
{
    [Fact]
    public void CalendarHeadingsKeepNewestSeriesPositionAndNonEpisodesSeparate()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var notifications = new[]
        {
            Row("bad", "invalid"),
            Row("new", "2026-11-16T10:00:00Z", "one"),
            Row("request", "2026-11-16T09:00:00Z", "one", "request.fulfilled"),
            Row("old", "2026-11-16T08:00:00Z", "one"),
            Row("yesterday", "2026-11-15T23:59:00Z", "one"),
            Row("six", "2026-11-10T12:00:00Z"),
            Row("seven", "2026-11-09T12:00:00Z")
        };
        var sections = NotificationInboxGroups.Group(notifications, now, TimeZoneInfo.Utc);
        Assert.Equal(new[] { "today", "yesterday", "week", "older" }, sections.Select(section => section.Day));
        Assert.Equal(new[] { "new", "old" }, sections[0].Entries[0].Notifications.Select(row => row.Id));
        Assert.Equal("new", sections[0].Entries[0].Latest.Id);
        Assert.Null(sections[0].Entries[1].SeriesId);
        Assert.Equal(new[] { "bad", "seven" }, sections[3].Entries.Select(entry => entry.Latest.Id));
    }

    [Fact]
    public void LocalMidnightAndDaylightSavingUseCalendarDatesNotElapsedHours()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
        var now = new DateTimeOffset(2026, 11, 2, 0, 15, 0, TimeSpan.FromHours(-6));
        var sections = NotificationInboxGroups.Group(new[]
        {
            Row("yesterday", "2026-11-01T00:01:00-05:00"),
            Row("today", "2026-11-02T00:01:00-06:00"),
            Row("future", "2026-11-03T00:01:00-06:00")
        }, now, zone);
        Assert.Equal(new[] { "today", "yesterday" }, sections.Select(section => section.Day));
        Assert.Equal("yesterday", Assert.Single(sections[1].Entries).Latest.Id);
        Assert.Equal(2, sections[0].Entries.Count);
    }

    private static AppNotification Row(string id, string created, string? series = null, string type = "episode.available")
        => new() { Id = id, CreatedAt = created, SeriesId = series, Type = type };
}
