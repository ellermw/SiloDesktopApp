using System.Globalization;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Controls;

public static partial class MediaItemCardActions
{
    internal static string Runtime(int minutes) => minutes >= 60
        ? AppStrings.Get(minutes % 60 == 0 ? "web.item.card.runtimeHours" : "web.item.card.runtimeHoursMinutes",
            new Dictionary<string, object?> { ["hours"] = minutes / 60, ["minutes"] = minutes % 60 })
        : AppStrings.Get("web.item.card.runtimeMinutes", new Dictionary<string, object?> { ["minutes"] = minutes });

    internal static string CardSubtitle(MediaItem item, string? sort)
    {
        string Fallback() => item.ItemSource == "next_in_series" || item.Type == "episode"
            ? MediaItemDisplayText.BuildSubtitle(item, "title")
            : string.Join("  ·  ", new[] { item.Year > 0 ? item.Year.ToString(AppStrings.FormatCulture) : "",
                item.Type == "series" ? AppStrings.Get("web.item.card.series") : "" }.Where(value => value.Length > 0));
        string? Date(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateTimeDisplay.FormatDate(date, medium: true) : null;
        return sort switch
        {
            "added_at" or "recently_added" => RelativeDate(item.AddedAt) ?? Fallback(),
            "title" or "sort_title" => Fallback(),
            "year" => item.Year > 0 ? item.Year.ToString(AppStrings.FormatCulture) : Fallback(),
            "runtime" => (item.SortMetrics?.RuntimeMinutes ?? item.Runtime) is int runtime && runtime > 0 ? Runtime(runtime) : Fallback(),
            "release_date" => Date(item.SortMetrics?.ReleaseDate ?? item.ReleaseDate) ?? Fallback(),
            "last_air_date" => Date(item.LastAirDate) ?? Fallback(),
            "date_viewed" => Date(item.SortMetrics?.ViewedAt) ?? Fallback(),
            "rating_imdb" when item.RatingImdb is > 0 => "★ " + item.RatingImdb.Value.ToString("0.0", AppStrings.FormatCulture) + " / 10",
            "rating_tmdb" when item.RatingTmdb is > 0 => item.RatingTmdb.Value.ToString("0.0", AppStrings.FormatCulture) + " / 10",
            "rating_rt_critic" when item.RatingRtCritic.HasValue => (item.RatingRtCritic.Value / 100d).ToString("P0", AppStrings.FormatCulture),
            "rating_rt_audience" when item.RatingRtAudience.HasValue => (item.RatingRtAudience.Value / 100d).ToString("P0", AppStrings.FormatCulture),
            "progress" when item.SortMetrics?.ProgressRatio is { } ratio => Math.Clamp(ratio, 0, 1).ToString("P0", AppStrings.FormatCulture),
            "plays" when item.SortMetrics?.PlayCount is { } count => count.ToString(AppStrings.FormatCulture),
            "bitrate" when item.SortMetrics?.BitrateKbps is { } rate && rate > 0 => AppStrings.Get(rate >= 1000 ? "web.common.format.bitrate.mbps" : "web.common.format.bitrate.kbps",
                new Dictionary<string, object?> { ["rate"] = (rate >= 1000 ? rate / 1000d : rate).ToString("0.#", AppStrings.FormatCulture) }),
            "content_rating" => string.IsNullOrWhiteSpace(item.ContentRating) ? Fallback() : item.ContentRating,
            "resolution" => item.SortMetrics?.Resolution ?? item.OverlaySummary?.Resolution ?? Fallback(),
            "author" => item.SortMetrics?.Author ?? Fallback(),
            "narrator" => item.SortMetrics?.Narrator ?? Fallback(),
            "series" => item.SortMetrics?.SeriesName ?? Fallback(),
            _ => Fallback()
        };
    }

    internal static string? RelativeDate(string? value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        var elapsed = DateTimeOffset.UtcNow - date;
        if (elapsed.TotalMinutes < 1) return AppStrings.Get("web.common.date.justNow");
        var key = elapsed.TotalHours < 1 ? "web.common.date.minutesAgo" : elapsed.TotalDays < 1 ? "web.common.date.hoursAgo" : "web.common.date.daysAgo";
        var count = (int)(elapsed.TotalHours < 1 ? elapsed.TotalMinutes : elapsed.TotalDays < 1 ? elapsed.TotalHours : elapsed.TotalDays);
        return AppStrings.Get(key, new Dictionary<string, object?> { ["count"] = count, ["count_text"] = count.ToString("N0", AppStrings.FormatCulture) });
    }

    internal static string UpcomingSubtitle(SectionItemUpcomingEvent upcoming)
    {
        if ((upcoming.Type is "episode" or "season_premiere") && upcoming.SeasonNumber.HasValue)
        {
            var label = upcoming.EpisodeNumber.HasValue ? $"S{upcoming.SeasonNumber} · E{upcoming.EpisodeNumber}"
                : AppStrings.Get("web.detail.seasons.numbered", new Dictionary<string, object?> { ["season"] = upcoming.SeasonNumber });
            return label + (string.IsNullOrWhiteSpace(upcoming.EpisodeTitle) ? "" : " · " + upcoming.EpisodeTitle);
        }
        return upcoming.Type == "movie" ? AppStrings.Get("web.search.typeMovie") : "";
    }

    internal static string UpcomingSchedule(SectionItemUpcomingEvent upcoming)
    {
        var parts = new List<string>();
        var value = string.IsNullOrWhiteSpace(upcoming.LocalAirDate) ? upcoming.AirDate : upcoming.LocalAirDate;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            parts.Add(DateTimeDisplay.FormatDate(date, medium: true));
        if (DateTimeOffset.TryParse(upcoming.AirAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            parts.Add(DateTimeDisplay.FormatTime(at));
        else if (TimeSpan.TryParse(upcoming.AirTime, CultureInfo.InvariantCulture, out var time))
            parts.Add(DateTimeDisplay.FormatTime(DateTime.Today.Add(time)));
        return string.Join("  ", parts);
    }
}
