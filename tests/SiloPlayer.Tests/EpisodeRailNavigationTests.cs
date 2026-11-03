using SiloPlayer.Helpers;

namespace SiloPlayer.Tests;

public sealed class EpisodeRailNavigationTests
{
    [Theory]
    [InlineData(-30)]
    [InlineData(-120)]
    [InlineData(-960)]
    [InlineData(120)]
    public void UprightWheelLeavesEveryDeltaForTheContainingPage(int delta)
        => Assert.Equal(0, EpisodeRailNavigation.HorizontalWheelDelta(delta, false, false, 252));

    [Theory]
    [InlineData(120, true, false, 252)]
    [InlineData(-120, true, false, -252)]
    [InlineData(30, true, false, 63)]
    [InlineData(-120, false, true, 252)]
    [InlineData(120, false, true, -252)]
    [InlineData(120, true, true, 252)]
    public void DeliberateHorizontalIntentPreservesMagnitudeAndDeviceDirection(int delta, bool horizontal, bool shift, double expected)
        => Assert.Equal(expected, EpisodeRailNavigation.HorizontalWheelDelta(delta, horizontal, shift, 252));
}
