namespace ContinuumPlayer.Tests;

public sealed class PlaybackSeekBehaviorTests
{
    [Fact]
    public void MpvPlayer_ExposesFastKeyframeSeekAndStartPositionLoad()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ContinuumPlayer.Player", "MpvPlayer.cs"));

        Assert.Contains("public void SeekFast(double seconds)", source);
        Assert.Contains("\"absolute+keyframes\"", source);
        Assert.Contains("startSeconds", source);
        Assert.Contains("\"start={FormatSeconds(startSeconds)}\"", source);
        Assert.Contains("Command(\"loadfile\", url, \"replace\", \"-1\",", source);
    }

    [Fact]
    public void OscSeekBar_UsesFastSeekAndResumeHelper()
    {
        var script = File.ReadAllText(FindRepoFile("libs", "mpv", "scripts", "continuum-osc.lua"));
        var seekReleaseIndex = script.IndexOf("local target_time = state.seek_drag_pos * state.duration", StringComparison.Ordinal);
        Assert.True(seekReleaseIndex >= 0);

        var releaseBlock = script[seekReleaseIndex..Math.Min(script.Length, seekReleaseIndex + 240)];
        Assert.Contains("seek_and_resume(target_time, \"absolute+keyframes\")", releaseBlock);
        Assert.DoesNotContain("mp.commandv(\"seek\", tostring(target_time), \"absolute\")", releaseBlock);
    }

    [Fact]
    public void PlayerService_UsesLoadFileStartForDirectResume()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ContinuumPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("var mpvStartPosition = session.PlayMethod != \"transcode\" ? startPosition : 0;", source);
        Assert.Contains("_mpv!.LoadFile(streamUrl!, null, mpvStartPosition);", source);
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
