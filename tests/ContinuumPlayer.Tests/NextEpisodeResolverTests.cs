using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Tests;

public sealed class NextEpisodeResolverTests
{
    [Fact]
    public void FindNextEpisode_ReturnsSameSeasonNextEpisode()
    {
        var episodes = new[]
        {
            Episode("s1e1", 1, 1),
            Episode("s1e2", 1, 2),
        };

        var next = NextEpisodeResolver.FindNextEpisode(episodes, currentSeason: 1, currentEpisode: 1);

        Assert.NotNull(next);
        Assert.Equal("s1e2", next.ContentId);
    }

    [Fact]
    public void FindNextEpisode_ReturnsFirstEpisodeOfNextSeason()
    {
        var episodes = new[]
        {
            Episode("s1e10", 1, 10),
            Episode("s2e1", 2, 1),
        };

        var next = NextEpisodeResolver.FindNextEpisode(episodes, currentSeason: 1, currentEpisode: 10);

        Assert.NotNull(next);
        Assert.Equal("s2e1", next.ContentId);
    }

    [Fact]
    public void FindNextEpisode_ReturnsNullAtEndOfAvailableEpisodes()
    {
        var episodes = new[]
        {
            Episode("s1e1", 1, 1),
        };

        var next = NextEpisodeResolver.FindNextEpisode(episodes, currentSeason: 1, currentEpisode: 1);

        Assert.Null(next);
    }

    private static Episode Episode(string contentId, int season, int episode)
        => new()
        {
            ContentId = contentId,
            SeasonNumber = season,
            EpisodeNumber = episode,
            Title = $"Episode {episode}",
        };
}
