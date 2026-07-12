namespace SiloPlayer.Tests;

public sealed class PlaybackSeekBehaviorTests
{
    [Fact]
    public void MpvPlayer_ExposesFastKeyframeSeekAndStartPositionLoad()
    {
        var source = File.ReadAllText(FindRepoFile("src", "SiloPlayer.Player", "MpvPlayer.cs"));

        Assert.Contains("public void SeekFast(double seconds)", source);
        Assert.Contains("\"absolute+keyframes\"", source);
        Assert.Contains("startSeconds", source);
        Assert.Contains("\"start={FormatSeconds(startSeconds)}\"", source);
        Assert.Contains("Command(\"loadfile\", url, \"replace\", \"-1\",", source);
    }

    [Fact]
    public void OscSeekBar_UsesFastSeekAndResumeHelper()
    {
        var script = File.ReadAllText(FindRepoFile("libs", "mpv", "scripts", "silo-osc.lua"));
        var seekReleaseIndex = script.IndexOf("local target_time = state.seek_drag_pos * state.duration", StringComparison.Ordinal);
        Assert.True(seekReleaseIndex >= 0);

        var releaseBlock = script[seekReleaseIndex..Math.Min(script.Length, seekReleaseIndex + 240)];
        Assert.Contains("seek_and_resume(target_time, \"absolute+keyframes\")", releaseBlock);
        Assert.DoesNotContain("mp.commandv(\"seek\", tostring(target_time), \"absolute\")", releaseBlock);
    }

    [Fact]
    public void PlayerService_UsesConcreteTransportPlanForResume()
    {
        var source = File.ReadAllText(FindRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("PlaybackTransportPlanner.Plan(session)", source);
        Assert.Contains("PlaybackTransportKind.DirectProgressive", source);
        Assert.Contains("MpvLoadStartSeconds: mediaStartSeconds", source);
        Assert.Contains("TimelineOffsetSeconds: mediaStartSeconds", source);
        Assert.Contains("BeginMpvLoad(prepared, restorePaused: false);", source);
        Assert.Contains("Task.Delay(TimeSpan.FromSeconds(30), ownerCts.Token)", source);
        Assert.Contains("Position = mediaPosition;", source);
        Assert.Contains("Duration = mediaDuration;", source);
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(pathParts)} from test output.");
    }
}
