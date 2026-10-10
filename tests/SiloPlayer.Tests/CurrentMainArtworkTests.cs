using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class CurrentMainArtworkTests
{
    [Theory]
    [InlineData(null, "film")]
    [InlineData("movie", "film")]
    [InlineData("unknown", "film")]
    [InlineData("series", "tv")]
    [InlineData("season", "tv")]
    [InlineData("episode", "tv")]
    [InlineData("audiobook", "headphones")]
    [InlineData("book", "headphones")]
    [InlineData("books", "headphones")]
    [InlineData("podcast", "headphones")]
    [InlineData("podcasts", "headphones")]
    [InlineData("ebook", "book")]
    [InlineData("ebooks", "book")]
    [InlineData("manga", "book")]
    [InlineData("comic", "book")]
    [InlineData("comics", "book")]
    public void ArtworkMarkUsesTheCurrentServerMediaType(string? type, string expected)
    {
        Assert.Equal(expected, DefaultArtworkStyle.Icon(type));
    }

    [Theory]
    [InlineData(40, 60, 14, 30)]
    [InlineData(147, 221, 35.28, 110.5)]
    [InlineData(184, 184, 40, 92)]
    [InlineData(315, 177, 40, 157.5)]
    public void ArtworkUsesClampedMarkAndCircularGlowAcrossAspectRatios(double width, double height, double mark, double radius)
    {
        Assert.Equal(mark, DefaultArtworkStyle.MarkSize(width), 5);
        Assert.Equal(radius, DefaultArtworkStyle.GlowRadius(width, height, .5), 5);
    }
}
