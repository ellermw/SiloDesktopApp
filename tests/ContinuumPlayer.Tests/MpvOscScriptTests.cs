namespace ContinuumPlayer.Tests;

public sealed class MpvOscScriptTests
{
    [Fact]
    public void StatsOverlay_DoesNotCountDelayedFramesAsDroppedFrames()
    {
        var script = File.ReadAllText(FindOscScriptPath());
        var droppedLabelIndex = script.IndexOf("Dropped frames", StringComparison.Ordinal);
        Assert.True(droppedLabelIndex >= 0);

        var droppedCalculationStart = Math.Max(0, droppedLabelIndex - 500);
        var droppedCalculation = script[droppedCalculationStart..droppedLabelIndex];

        Assert.Contains("frame-drop-count", droppedCalculation);
        Assert.Contains("decoder-frame-drop-count", droppedCalculation);
        Assert.DoesNotContain("vo-delayed-frame-count", droppedCalculation);
        Assert.Contains("Delayed frames", script);
    }

    private static string FindOscScriptPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "libs", "mpv", "scripts", "continuum-osc.lua");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate libs/mpv/scripts/continuum-osc.lua from test output.");
    }
}
