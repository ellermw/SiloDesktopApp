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
        Assert.Contains("prepared.Plan.TransportKind == PlaybackTransportKind.DirectProgressive", source);
        Assert.Contains("? TimeSpan.FromSeconds(15)", source);
        Assert.Contains("await Task.Delay(loadDeadline, ownerCts.Token)", source);
        Assert.Contains("Position = mediaPosition;", source);
        Assert.Contains("Duration = mediaDuration;", source);
    }

    [Fact]
    public void TransportReplacementStaysPausedUntilTheNewFileIsLoaded()
    {
        var source = File.ReadAllText(FindRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs"));
        var methodStart = source.IndexOf("private void BeginMpvLoad", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task MonitorFileLoadAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);

        var method = source[methodStart..methodEnd];
        var pauseIndex = method.IndexOf("mpv.Pause();", StringComparison.Ordinal);
        var loadIndex = method.IndexOf("mpv.LoadFile(", StringComparison.Ordinal);
        Assert.True(pauseIndex >= 0 && loadIndex > pauseIndex);
        Assert.DoesNotContain("mpv.Play();", method, StringComparison.Ordinal);
        Assert.Contains("FileLoaded is the single authority", method, StringComparison.Ordinal);
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
