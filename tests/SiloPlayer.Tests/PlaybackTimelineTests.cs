using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackTimelineTests
{
    [Fact]
    public void CopyHlsTimeline_MapsPlayerZeroToRequestedMediaPosition()
    {
        Assert.Equal(615.25, PlaybackTimeline.ToMediaTime(0, 615.25), 3);
        Assert.Equal(0, PlaybackTimeline.ToPlayerTime(615.25, 615.25), 3);
        Assert.Equal(14.75, PlaybackTimeline.ToPlayerTime(630, 615.25), 3);
    }

    [Fact]
    public void EncodedTimeline_HasIdentityMapping()
    {
        Assert.Equal(615.25, PlaybackTimeline.ToMediaTime(615.25, 0), 3);
        Assert.Equal(615.25, PlaybackTimeline.ToPlayerTime(615.25, 0), 3);
    }

    [Fact]
    public void AuthoritativeDuration_WinsOverWindowDuration()
    {
        Assert.Equal(7200, PlaybackTimeline.ResolveMediaDuration(90, 615.25, 7200), 3);
        Assert.Equal(705.25, PlaybackTimeline.ResolveMediaDuration(90, 615.25, null), 3);
    }

    [Theory]
    [InlineData(615.25, 615.25, 90, true)]
    [InlineData(700, 615.25, 90, true)]
    [InlineData(706, 615.25, 90, false)]
    [InlineData(600, 615.25, 90, false)]
    public void ExposedWindowDetection_IsOffsetAware(
        double mediaSeconds,
        double offsetSeconds,
        double windowDurationSeconds,
        bool expected)
    {
        Assert.Equal(expected, PlaybackTimeline.IsInsideExposedWindow(
            mediaSeconds,
            offsetSeconds,
            windowDurationSeconds));
    }
}
