using System.Diagnostics;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class StartupFrameTelemetryTests
{
    [Fact]
    public void First_output_is_once_per_attempt_and_rebuffers_cannot_overwrite_startup()
    {
        var timer = new StartupFrameTelemetry(); timer.Reset(100);
        Assert.Equal("2000", timer.Observe("a", 100 + 2 * Stopwatch.Frequency)!["first_frame_ms"]);
        Assert.Null(timer.Observe("a", 100 + 7 * Stopwatch.Frequency));
        Assert.Empty(timer.Observe("replan", 100 + 9 * Stopwatch.Frequency)!);
    }
    [Fact]
    public void Automatic_start_has_no_invented_viewer_latency()
    {
        var timer = new StartupFrameTelemetry(); timer.Reset(null);
        Assert.Empty(timer.Observe("auto", Stopwatch.GetTimestamp())!);
    }
}
