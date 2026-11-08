using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class UpdateProgressPresentationTests
{
    [Fact]
    public void Window_text_shows_downloaded_counts_and_sizes()
    {
        Assert.Equal("Downloading updates…", UpdateProgressPresentation.DownloadingText);
        Assert.Equal("37 / 61", UpdateProgressPresentation.Files(37, 61));
        Assert.Equal("61 / 61", UpdateProgressPresentation.Files(62, 61));
        Assert.Equal("4.2 / 11.8 MB", UpdateProgressPresentation.Size(4404019, 12373197));
        Assert.Equal("0.0 / 0.0 MB", UpdateProgressPresentation.Size(-5, 0));
        Assert.Equal("8.4 MB/s", UpdateProgressPresentation.Speed(8.4 * 1048576));
        Assert.Equal("512 KB/s", UpdateProgressPresentation.Speed(512 * 1024));
    }

    [Fact]
    public void Speed_waits_for_half_a_second_then_averages_the_recent_window()
    {
        var meter = new UpdateSpeedMeter(TimeSpan.FromSeconds(3));
        Assert.Null(meter.Add(TimeSpan.Zero, 0));
        Assert.Null(meter.Add(TimeSpan.FromMilliseconds(250), 1_000_000));
        Assert.Equal(2_000_000, meter.Add(TimeSpan.FromSeconds(1), 2_000_000)!.Value, 1);
        // Old samples leave the window, so a slowdown shows within a few seconds.
        meter.Add(TimeSpan.FromSeconds(4), 8_000_000);
        var slow = meter.Add(TimeSpan.FromSeconds(7), 8_300_000)!.Value;
        Assert.InRange(slow, 99_000, 101_000);
    }

    [Fact]
    public void Speed_restarts_when_the_byte_count_goes_back()
    {
        var meter = new UpdateSpeedMeter(TimeSpan.FromSeconds(3));
        meter.Add(TimeSpan.Zero, 0);
        meter.Add(TimeSpan.FromSeconds(1), 5_000_000);
        Assert.Null(meter.Add(TimeSpan.FromSeconds(2), 100));
        Assert.Equal(1000, meter.Add(TimeSpan.FromSeconds(3), 1100)!.Value, 1);
    }
}
