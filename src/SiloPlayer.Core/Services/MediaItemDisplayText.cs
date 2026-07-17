using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public static class MediaItemDisplayText
{
    public static string BuildTitle(MediaItem item)
    {
        if (item.UpcomingEvent is not null)
            return item.Title;

        return BuildEpisodeTitle(item) is not null && !string.IsNullOrWhiteSpace(item.SeriesTitle)
            ? item.SeriesTitle
            : item.Title;
    }

    public static string? BuildEpisodeTitle(MediaItem item)
    {
        if (item.UpcomingEvent is not null)
            return null;

        if (item.Type != "episode" ||
            !item.SeasonNumber.HasValue ||
            !item.EpisodeNumber.HasValue ||
            string.IsNullOrWhiteSpace(item.Title) ||
            string.IsNullOrWhiteSpace(item.SeriesTitle) ||
            string.Equals(item.SeriesTitle, item.Title, StringComparison.Ordinal))
        {
            return null;
        }

        return item.Title;
    }

    public static string FormatUpcomingSubtitle(SectionItemUpcomingEvent upcoming)
    {
        if ((upcoming.Type == "episode" || upcoming.Type == "season_premiere") &&
            upcoming.SeasonNumber.HasValue)
        {
            if (upcoming.EpisodeNumber.HasValue)
            {
                return $"S{upcoming.SeasonNumber} \u00b7 E{upcoming.EpisodeNumber}" +
                    (string.IsNullOrWhiteSpace(upcoming.EpisodeTitle)
                        ? ""
                        : $" - {upcoming.EpisodeTitle}");
            }

            return $"Season {upcoming.SeasonNumber}" +
                (string.IsNullOrWhiteSpace(upcoming.EpisodeTitle)
                    ? ""
                    : $" \u00b7 {upcoming.EpisodeTitle}");
        }

        return upcoming.Type == "movie" ? "Movie" : "";
    }

    public static string FormatUpcomingSchedule(SectionItemUpcomingEvent upcoming)
    {
        var dateValue = string.IsNullOrWhiteSpace(upcoming.LocalAirDate)
            ? upcoming.AirDate
            : upcoming.LocalAirDate;
        var parts = new List<string>();
        if (DateTime.TryParse(dateValue, out var date))
            parts.Add(date.ToString("ddd, MMM d"));

        if (DateTimeOffset.TryParse(upcoming.AirAt, out var airAt))
        {
            parts.Add(airAt.ToLocalTime().ToString("t"));
        }
        else if (TimeSpan.TryParse(upcoming.AirTime, out var airTime))
        {
            parts.Add(DateTime.Today.Add(airTime).ToString("t"));
        }

        return string.Join("  ", parts);
    }

    public static string BuildSubtitle(MediaItem item, string? sortKey)
    {
        switch (sortKey)
        {
            case "added_at":
            case "recently_added":
                return TimeAgo(item.AddedAt) ?? BuildDefault(item);
            case "title":
            case "sort_title":
                return BuildEpisodeCode(item) ?? BuildDefault(item);
            case "year":
                return item.Year > 0 ? item.Year.ToString() : BuildDefault(item);
            case "content_rating":
                return string.IsNullOrWhiteSpace(item.ContentRating) ? BuildDefault(item) : item.ContentRating;
            case "runtime":
                return FormatRuntime(item.SortMetrics?.RuntimeMinutes ?? item.Runtime) ?? BuildDefault(item);
            case "rating_imdb":
                return item.RatingImdb is > 0 ? $"\u2605 {item.RatingImdb.Value:0.0} / 10" : BuildDefault(item);
            case "rating_tmdb":
                return item.RatingTmdb is > 0 ? $"{item.RatingTmdb.Value:0.0} / 10" : BuildDefault(item);
            case "rating_rt_critic":
                return item.RatingRtCritic.HasValue ? $"{item.RatingRtCritic}%" : BuildDefault(item);
            case "rating_rt_audience":
                return item.RatingRtAudience.HasValue ? $"{item.RatingRtAudience}%" : BuildDefault(item);
            case "release_date":
                return FormatDate(item.SortMetrics?.ReleaseDate ?? item.ReleaseDate) ?? BuildDefault(item);
            case "last_air_date":
                return FormatDate(item.LastAirDate) ?? BuildDefault(item);
            case "resolution":
                return item.SortMetrics?.Resolution ?? item.OverlaySummary?.Resolution ?? BuildDefault(item);
            case "bitrate":
                return FormatBitrate(item.SortMetrics?.BitrateKbps) ?? BuildDefault(item);
            case "progress":
                return item.SortMetrics?.ProgressRatio is { } ratio
                    ? $"{Math.Round(Math.Clamp(ratio, 0, 1) * 100):0}%"
                    : BuildDefault(item);
            case "date_viewed":
                return FormatDate(item.SortMetrics?.ViewedAt) ?? BuildDefault(item);
            case "plays":
                return item.SortMetrics?.PlayCount?.ToString() ?? BuildDefault(item);
            case "author":
                return item.SortMetrics?.Author ?? BuildDefault(item);
            case "narrator":
                return item.SortMetrics?.Narrator ?? BuildDefault(item);
            case "series":
                return item.SortMetrics?.SeriesName ?? BuildDefault(item);
            default:
                return BuildEpisodeCode(item) ?? BuildDefault(item);
        }
    }

    private static string BuildDefault(MediaItem item)
    {
        if (item.ItemSource == "next_in_series" && !string.IsNullOrWhiteSpace(item.SeriesTitle))
        {
            var bookBadge = item.Badges?.FirstOrDefault(badge =>
                badge.StartsWith("Book ", StringComparison.Ordinal));
            return string.Join(" \u00b7 ", new[] { bookBadge, item.SeriesTitle }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        var parts = new List<string>();
        if (item.Year > 0)
            parts.Add(item.Year.ToString());
        if (item.Type == "series")
            parts.Add("SERIES");
        return string.Join("  ·  ", parts);
    }

    private static string? BuildEpisodeCode(MediaItem item)
    {
        if (item.Type != "episode" || !item.SeasonNumber.HasValue || !item.EpisodeNumber.HasValue)
            return null;
        return $"S{item.SeasonNumber:00}E{item.EpisodeNumber:00}";
    }

    private static string? FormatRuntime(int? minutes)
    {
        if (minutes is null or <= 0) return null;
        var hours = minutes.Value / 60;
        var remainder = minutes.Value % 60;
        if (hours == 0) return $"{remainder}m";
        return remainder == 0 ? $"{hours}h" : $"{hours}h {remainder}m";
    }

    private static string? FormatBitrate(int? kbps)
    {
        if (kbps is null or <= 0) return null;
        return kbps >= 1000 ? $"{kbps.Value / 1000d:0.#} Mbps" : $"{kbps} kbps";
    }

    private static string? FormatDate(string? value)
    {
        if (!DateTimeOffset.TryParse(value, out var date)) return null;
        return date.ToLocalTime().ToString("MMM d, yyyy");
    }

    private static string? TimeAgo(string? value)
    {
        if (!DateTimeOffset.TryParse(value, out var date)) return null;
        var elapsed = DateTimeOffset.UtcNow - date.ToUniversalTime();
        if (elapsed < TimeSpan.Zero) return "just now";
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 30) return $"{(int)elapsed.TotalDays}d ago";
        if (elapsed.TotalDays < 365) return $"{(int)(elapsed.TotalDays / 30)}mo ago";
        return $"{(int)(elapsed.TotalDays / 365)}y ago";
    }
}
