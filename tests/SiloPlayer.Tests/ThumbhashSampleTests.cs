using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ThumbhashSampleTests
{
    // The sample the native placeholder fixture binds (the ThumbHash project's published example).
    [Fact]
    public void FixtureSampleDecodesToASmallPlaceholder()
    {
        var image = ThumbhashDecoder.Decode("1QcSHQRnh493V4dIh4eXh1h4kJUI");
        Assert.InRange(image.Width, 1, 32);
        Assert.InRange(image.Height, 1, 32);
        Assert.Equal(image.Width * image.Height * 4, image.Rgba.Length);
    }
}
