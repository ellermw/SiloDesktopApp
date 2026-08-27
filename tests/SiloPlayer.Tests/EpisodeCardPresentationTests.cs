using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class EpisodeCardPresentationTests
{
    [Fact]
    public void WatchedEpisodeUsesInlineIndicatorAndSuppressesProgress()
    {
        var state = EpisodeCardPresentation.Create(
            watched: true,
            positionSeconds: 900,
            durationSeconds: 1800);

        Assert.True(state.ShowWatchedIndicator);
        Assert.False(state.ShowProgress);
        Assert.Equal(0, state.ProgressRatio);
        Assert.Equal("Mark Episode Unwatched", state.WatchedActionLabel);
    }

    [Fact]
    public void PartiallyWatchedEpisodeShowsClampedInsetProgress()
    {
        var state = EpisodeCardPresentation.Create(
            watched: false,
            positionSeconds: 2400,
            durationSeconds: 1800);

        Assert.False(state.ShowWatchedIndicator);
        Assert.True(state.ShowProgress);
        Assert.Equal(1, state.ProgressRatio);
        Assert.Equal("Mark Episode Watched", state.WatchedActionLabel);
    }

    [Theory]
    [InlineData(0, 1800)]
    [InlineData(100, 0)]
    public void MissingPartialProgressHidesTrack(double position, double duration)
    {
        var state = EpisodeCardPresentation.Create(false, position, duration);

        Assert.False(state.ShowProgress);
        Assert.Equal(0, state.ProgressRatio);
    }
}
