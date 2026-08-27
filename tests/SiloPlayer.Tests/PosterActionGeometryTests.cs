using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PosterActionGeometryTests
{
    [Theory]
    [InlineData("favorites")]
    [InlineData("watchlist")]
    public void Create_UsesNarrowGeometryForPersonalPosterGrids(string source)
    {
        var geometry = PosterActionGeometryFactory.Create(source, "standard", 1280);

        Assert.Equal(24, geometry.TriggerSize);
        Assert.Equal(12, geometry.IconSize);
        Assert.Equal(6, geometry.EdgeInset);
        Assert.Equal(2, geometry.Gap);
    }

    [Fact]
    public void Create_UsesCompactDesktopGeometryForCompactPosterPreference()
    {
        var geometry = PosterActionGeometryFactory.Create("library", "compact", 1280);

        Assert.Equal(28, geometry.TriggerSize);
        Assert.Equal(14, geometry.IconSize);
        Assert.Equal(8, geometry.EdgeInset);
        Assert.Equal(4, geometry.Gap);
    }

    [Fact]
    public void Create_UsesStandardDesktopGeometryForStandardPosterPreference()
    {
        var geometry = PosterActionGeometryFactory.Create("library", "standard", 1280);

        Assert.Equal(32, geometry.TriggerSize);
        Assert.Equal(16, geometry.IconSize);
        Assert.Equal(10, geometry.EdgeInset);
        Assert.Equal(6, geometry.Gap);
    }

    [Fact]
    public void Create_UsesSmallViewportGeometryBelowWebUiBreakpoint()
    {
        var geometry = PosterActionGeometryFactory.Create("library", "standard", 639);

        Assert.Equal(24, geometry.TriggerSize);
        Assert.Equal(12, geometry.IconSize);
        Assert.Equal(6, geometry.EdgeInset);
        Assert.Equal(2, geometry.Gap);
    }
}
