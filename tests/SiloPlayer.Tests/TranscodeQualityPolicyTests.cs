using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class TranscodeQualityPolicyTests
{
    [Theory]
    [InlineData(null, "2160p", "1080p-high")]
    [InlineData("", "2160p", "1080p-high")]
    [InlineData("auto", "2160p", "1080p-high")]
    [InlineData("auto", "1080p", "720p-high")]
    [InlineData("auto", "720p", "480p")]
    [InlineData("1080p", "2160p", "1080p-high")]
    [InlineData("720p", "2160p", "720p-high")]
    [InlineData("4k", "2160p", null)]
    [InlineData("original", "2160p", null)]
    [InlineData("auto", "480p", "420p")]
    [InlineData("auto", "420p", null)]
    public void InitialVideoTierMatchesCurrentWebUi(
        string? preference,
        string sourceResolution,
        string? expectedTier)
    {
        var result = TranscodeQualityPolicy.ResolveInitialVideoTier(
            sourceResolution,
            preference);

        Assert.Equal(expectedTier, result?.Id);
    }

    [Theory]
    [InlineData("1080p-high", "1080p", 10_000)]
    [InlineData("1080p", "1080p", 6_000)]
    [InlineData("720p-high", "720p", 4_000)]
    [InlineData("720p", "720p", 2_000)]
    [InlineData("480p", "480p", 1_500)]
    [InlineData("420p", "420p", 720)]
    public void ManualTierRecipesMatchCurrentWebUi(
        string tierId,
        string expectedResolution,
        int expectedBitrate)
    {
        var result = TranscodeQualityPolicy.Find(tierId);

        Assert.NotNull(result);
        Assert.Equal(expectedResolution, result.Resolution);
        Assert.Equal(expectedBitrate, result.BitrateKbps);
    }

    [Theory]
    [InlineData("2160p", "auto", "2160p")]
    [InlineData("2160p", "original", "2160p")]
    [InlineData("2160p", "4k", "2160p")]
    [InlineData("2160p", "1080p", "1080p")]
    [InlineData("2160p", "720p", "720p")]
    [InlineData("1080p", "4k", "1080p")]
    public void MaximumResolutionNeverExceedsNativeCapability(
        string capability,
        string preference,
        string expected)
    {
        Assert.Equal(
            expected,
            TranscodeQualityPolicy.ResolveMaximumResolution(capability, preference));
    }
}
