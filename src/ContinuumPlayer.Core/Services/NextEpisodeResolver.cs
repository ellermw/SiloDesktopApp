using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.Core.Services;

public static class NextEpisodeResolver
{
    public static Episode? FindNextEpisode(
        IEnumerable<Episode> episodes,
        int currentSeason,
        int currentEpisode,
        string? currentContentId = null)
    {
        var ordered = episodes
            .Where(ep => ep.SeasonNumber >= 0 && ep.EpisodeNumber >= 0)
            .OrderBy(ep => ep.SeasonNumber)
            .ThenBy(ep => ep.EpisodeNumber)
            .ToList();

        if (ordered.Count == 0)
            return null;

        var currentIndex = -1;
        if (!string.IsNullOrWhiteSpace(currentContentId))
            currentIndex = ordered.FindIndex(ep => ep.ContentId == currentContentId);

        if (currentIndex < 0)
            currentIndex = ordered.FindIndex(ep =>
                ep.SeasonNumber == currentSeason &&
                ep.EpisodeNumber == currentEpisode);

        if (currentIndex < 0 || currentIndex >= ordered.Count - 1)
            return null;

        return ordered[currentIndex + 1];
    }
}
