using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class CardOverlayGeometryTests
{
    [Theory]
    [InlineData(185, 1.0)]
    [InlineData(148, 0.8)]
    [InlineData(96, 0.5189189189)]
    public void PosterGeometryScalesFromCurrentWebUiReferenceWidth(
        double cardWidth,
        double expectedScale)
    {
        var geometry = CardOverlayGeometry.ForPoster(cardWidth);

        Assert.Equal(expectedScale, geometry.Scale, precision: 6);
        Assert.Equal(8 * expectedScale, geometry.EdgeInset, precision: 6);
        Assert.Equal(4 * expectedScale, geometry.StackGap, precision: 6);
    }

    [Fact]
    public void WideGeometryRemainsFixed()
    {
        var geometry = CardOverlayGeometry.ForWideCard();

        Assert.Equal(1, geometry.Scale);
        Assert.Equal(8, geometry.EdgeInset);
        Assert.Equal(4, geometry.StackGap);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(-50)]
    public void InvalidPosterWidthFallsBackToReferenceGeometry(double cardWidth)
    {
        Assert.Equal(1, CardOverlayGeometry.ForPoster(cardWidth).Scale);
    }
}
