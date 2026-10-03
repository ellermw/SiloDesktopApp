using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Resolves the episode collection used by a series' primary action. This
/// mirrors the current WebUI fallback after its direct Continue Watching match:
/// continue the latest started season, advance to the next completed season,
/// or begin at episode one of the first season.
/// </summary>
public static class SeriesPrimaryActionResolver
{
    public sealed record Action(string Label, string? TargetSeasonId, int? TargetEpisodeNumber);

    public sealed record DetailAction(string Label, string? ContentId);

    public static DetailAction ResolveDetail(MediaItemDetail item)
    {
        if (string.IsNullOrWhiteSpace(item.PlayContentId)) return new("Browse Series", null);
        var rollup = item.UserData;
        var label = rollup?.InProgressCount > 0 ? "Resume"
            : rollup?.WatchedCount > 0 && !rollup.Played ? "Play Next"
            : "Start From Episode 1";
        return new(label, item.PlayContentId);
    }

    public static Action Resolve(IReadOnlyCollection<Season> seasons)
    {
        var sorted = seasons.OrderBy(season => season.SeasonNumber).ToList();
        var latestStarted = sorted
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(season =>
                season.UserData != null &&
                (season.UserData.InProgressCount > 0 ||
                 season.UserData.WatchedCount > 0 ||
                 season.UserData.UnplayedCount < season.EpisodeCount));

        if (latestStarted != null)
        {
            var nextSeason = sorted.FirstOrDefault(season => season.SeasonNumber > latestStarted.SeasonNumber);
            var targetSeason = latestStarted.UserData?.Played == true && nextSeason != null
                ? nextSeason
                : latestStarted;
            var watchedCount = targetSeason.UserData?.WatchedCount ?? 0;
            var episodeNumber = ReferenceEquals(targetSeason, latestStarted)
                ? Math.Min(watchedCount + 1, Math.Max(targetSeason.EpisodeCount, 1))
                : 1;
            return new Action("Play Latest", targetSeason.ContentId, episodeNumber);
        }

        var firstSeason = sorted.FirstOrDefault();
        return firstSeason == null
            ? new Action("Browse Series", null, null)
            : new Action("Start From Episode 1", firstSeason.ContentId, 1);
    }
}
