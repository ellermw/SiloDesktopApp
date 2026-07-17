using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AudiobookPlaybackPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"silo-audiobook-prefs-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(0.1, 0.5)]
    [InlineData(0.74, 0.75)]
    [InlineData(1.23, 1.25)]
    [InlineData(4, 3)]
    public void ClampAudiobookPlaybackRate_UsesCurrentWebUiRangeAndStep(double value, double expected)
    {
        Assert.Equal(expected, SettingsService.ClampAudiobookPlaybackRate(value), 6);
    }

    [Fact]
    public void RememberAudiobookPlaybackRate_IsPerBookAndPersists()
    {
        var settings = new SettingsService(_directory);

        Assert.Equal(1, settings.GetAudiobookPlaybackRate("book-a"));
        Assert.Equal(1.25, settings.RememberAudiobookPlaybackRate("book-a", 1.23));
        Assert.Equal(1.25, new SettingsService(_directory).GetAudiobookPlaybackRate("book-a"));
        Assert.Equal(1, settings.GetAudiobookPlaybackRate("book-b"));
    }

    [Fact]
    public void RememberAudiobookPlaybackRate_RetainsOnlyFiftyMostRecentBooks()
    {
        var settings = new SettingsService(_directory);
        for (var index = 0; index < 51; index++)
            settings.RememberAudiobookPlaybackRate($"book-{index:00}", 1 + index * 0.01);

        Assert.Equal(50, settings.Load().AudiobookPlaybackRates.Count);
        Assert.DoesNotContain("book-00", settings.Load().AudiobookPlaybackRates.Keys);
        Assert.Contains("book-50", settings.Load().AudiobookPlaybackRates.Keys);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
