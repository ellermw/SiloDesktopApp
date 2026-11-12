using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class LoadingCoverBitmapTests
{
    [Theory]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(3840, 2160, 1920, 1080)]
    [InlineData(1280, 720, 1280, 720)]
    [InlineData(2560, 1080, 1920, 810)]
    public void TargetKeepsTheFrameAspectWithinTheMaximum(int frameWidth, int frameHeight, int width, int height)
        => Assert.Equal((width, height), LoadingCoverBitmap.TargetSize(frameWidth, frameHeight));

    [Fact]
    public void CoverIsDimmedOpaqueOutsideAndClearAtTheSpinner()
    {
        // A 4x2 source: left half white, right half red. Cover-fitting it into
        // a 200x200 target crops the sides around the centre.
        var source = new byte[4 * 2 * 4];
        for (var y = 0; y < 2; y++)
        for (var x = 0; x < 4; x++)
        {
            var i = (y * 4 + x) * 4;
            if (x < 2) { source[i] = 255; source[i + 1] = 255; source[i + 2] = 255; }
            else { source[i + 2] = 255; }
            source[i + 3] = 255;
        }
        var result = LoadingCoverBitmap.Compose(source, 4, 2, 16, 200, 200, 1);
        Assert.Equal(200 * 200 * 4, result.Length);
        static (byte B, byte G, byte R, byte A) At(byte[] pixels, int x, int y)
        {
            var i = (y * 200 + x) * 4;
            return (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
        }
        var left = At(result, 2, 2);
        Assert.Equal(255, left.A);
        Assert.InRange(left.R, 54, 58); // 22% of white
        Assert.InRange(left.B, 54, 58);
        var right = At(result, 197, 197);
        Assert.Equal(255, right.A);
        Assert.InRange(right.R, 54, 58);
        Assert.Equal(0, right.B);
        var centre = At(result, 100, 100);
        Assert.Equal(0, centre.A);
        Assert.Equal(0, centre.R);
        // Premultiplied: colour never exceeds coverage across the feather.
        for (var x = 100; x < 160; x++)
        {
            var p = At(result, x, 100);
            Assert.True(p.R <= p.A && p.G <= p.A && p.B <= p.A, $"Not premultiplied at {x}");
        }
        Assert.Equal(255, At(result, 100 + 45, 100).A);
    }

    [Fact]
    public void FeatherIsMonotonic()
    {
        var previous = -1d;
        for (var d = 0d; d <= 50; d += 0.5)
        {
            var coverage = LoadingCoverBitmap.Coverage(d, 26, 44);
            Assert.InRange(coverage, previous, 1);
            previous = coverage;
        }
        Assert.Equal(0, LoadingCoverBitmap.Coverage(26, 26, 44));
        Assert.Equal(1, LoadingCoverBitmap.Coverage(44, 26, 44));
    }

    [Fact]
    public void InvalidSourceIsRejected()
        => Assert.Throws<ArgumentException>(() => LoadingCoverBitmap.Compose(new byte[3], 1, 1, 4, 10, 10, 1));
}
