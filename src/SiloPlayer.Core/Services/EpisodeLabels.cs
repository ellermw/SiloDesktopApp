using System.Globalization;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services;

/// <summary>Episode identity for the viewer and for shared Watch Together subtitles.</summary>
public static class EpisodeLabels
{
    public static bool UsesAirDate(string? mode) => mode == "air_date";

    public static string? AirDate(string? mode, string? airDate, Func<DateTime, string> formatDate)
        => ParseAirDate(mode, airDate) is { } day ? formatDate(day) : null;

    public static string Label(string? mode, string? airDate, int season, int episode, Func<DateTime, string> formatDate)
        => AirDate(mode, airDate, formatDate) ?? Code(season, episode);

    private static string Code(int season, int episode) => AppStrings.Get("web.detail.episodeCode",
        new Dictionary<string, object?>
        {
            ["season"] = season.ToString("D2", CultureInfo.InvariantCulture),
            ["episode"] = episode.ToString("D2", CultureInfo.InvariantCulture)
        }, $"S{season:D2}E{episode:D2}");

    public static string WithTitle(string dateLabel, string? title)
        => string.IsNullOrEmpty(title) ? dateLabel : $"{dateLabel} •\u00a0{title}";

    public static string PartySubtitle(string? mode, string? airDate, int season, int episode)
        => ParseAirDate(mode, airDate) is { } day
            ? day.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US"))
            : FormattableString.Invariant($"S{season} E{episode}");

    private static DateTime? ParseAirDate(string? mode, string? value)
        => UsesAirDate(mode) && DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var day) ? day : null;
}
