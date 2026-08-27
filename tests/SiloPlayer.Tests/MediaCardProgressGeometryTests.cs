using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class MediaCardProgressGeometryTests
{
    [Fact]
    public void ContinueWatchingUsesTheCurrentInsetRoundedTrackGeometry()
    {
        var layout = MediaCardProgressGeometry.Calculate(315, 0.5, episodeCard: false);

        Assert.Equal(10, layout.HorizontalInset);
        Assert.Equal(8, layout.BottomInset);
        Assert.Equal(295, layout.TrackWidth);
        Assert.Equal(147.5, layout.FillWidth);
    }

    [Fact]
    public void EpisodeCardUsesItsNarrowerCurrentWebUiInsets()
    {
        var layout = MediaCardProgressGeometry.Calculate(240, 0.25, episodeCard: true);

        Assert.Equal(8, layout.HorizontalInset);
        Assert.Equal(6, layout.BottomInset);
        Assert.Equal(224, layout.TrackWidth);
        Assert.Equal(56, layout.FillWidth);
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(2, 295)]
    public void ProgressFillIsClampedToTheTrack(double ratio, double expectedFill)
    {
        var layout = MediaCardProgressGeometry.Calculate(315, ratio, episodeCard: false);

        Assert.Equal(expectedFill, layout.FillWidth);
    }
}
