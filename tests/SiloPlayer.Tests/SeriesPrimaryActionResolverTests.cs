using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SeriesPrimaryActionResolverTests
{
    [Fact]
    public void NewSeriesStartsAtFirstEpisodeOfFirstSeason()
    {
        var result = SeriesPrimaryActionResolver.Resolve([
            Season(2, 10),
            Season(1, 8),
        ]);

        Assert.Equal("Start From Episode 1", result.Label);
        Assert.Equal("season-1", result.TargetSeasonId);
        Assert.Equal(1, result.TargetEpisodeNumber);
    }

    [Fact]
    public void StartedSeasonTargetsEpisodeAfterWatchedCount()
    {
        var result = SeriesPrimaryActionResolver.Resolve([
            Season(1, 10, watched: 4, inProgress: 1, unplayed: 5),
            Season(2, 8),
        ]);

        Assert.Equal("Play Latest", result.Label);
        Assert.Equal("season-1", result.TargetSeasonId);
        Assert.Equal(5, result.TargetEpisodeNumber);
    }

    [Fact]
    public void CompletedSeasonAdvancesToNextSeason()
    {
        var result = SeriesPrimaryActionResolver.Resolve([
            Season(1, 10, watched: 10, unplayed: 0, played: true),
            Season(2, 8),
        ]);

        Assert.Equal("season-2", result.TargetSeasonId);
        Assert.Equal(1, result.TargetEpisodeNumber);
    }

    [Fact]
    public void EmptySeriesOffersBrowseOnly()
    {
        var result = SeriesPrimaryActionResolver.Resolve([]);

        Assert.Equal("Browse Series", result.Label);
        Assert.Null(result.TargetSeasonId);
        Assert.Null(result.TargetEpisodeNumber);
    }

    private static Season Season(
        int number,
        int episodes,
        int watched = 0,
        int inProgress = 0,
        int? unplayed = null,
        bool played = false) => new()
    {
        ContentId = $"season-{number}",
        SeasonNumber = number,
        EpisodeCount = episodes,
        UserData = new SeasonUserData
        {
            Played = played,
            WatchedCount = watched,
            InProgressCount = inProgress,
            UnplayedCount = unplayed ?? episodes,
        },
    };
}
