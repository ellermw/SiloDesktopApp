namespace SiloPlayer.Tests;

public sealed class MpvPlayerLoggingTests
{
    [Fact]
    public void MpvPlayer_RotatesAndLimitsNoiseInMpvLog()
    {
        var source = File.ReadAllText(FindRepoFile("src", "SiloPlayer.Player", "MpvPlayer.cs"));

        Assert.Contains("RotateLogIfNeeded(logPath", source);
        Assert.Contains("SetOption(\"msg-level\", \"all=warn\")", source);
        // One player serves the whole session: the open log is also rotated before each new load.
        Assert.Contains("RotateLiveLogIfNeeded();\n        ResetUpscalingForLoad();", source.ReplaceLineEndings("\n"));
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

// Real bundled libmpv with an isolated log folder (serialized with the other native mpv tests,
// which also redirect SILOPLAYER_LOG_DIRECTORY for the process).
[Collection("Native streaming")]
public sealed class MpvPlayerLogRotationTests
{
    [Fact]
    public void OpenPlayerLogRotatesBeforeTheNextLoadInTheSameSession()
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-mpv-log-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY");
        Environment.SetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY", directory);
        try
        {
            var log = Path.Combine(directory, "mpv_log.txt");
            using (var player = new SiloPlayer.Player.MpvPlayer())
            {
                player.Initialize(16, 16);
                player.SetProperty("ao", "null");
                player.SetProperty("video", "no");
                Assert.True(SpinWait.SpinUntil(() => File.Exists(log), 10000), "mpv did not open its log.");
                // A long session: the log mpv still holds open has grown past its 2 MB bound.
                using (var grow = new FileStream(log, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                { grow.Seek(0, SeekOrigin.End); grow.Write(new byte[2 * 1024 * 1024 + 1]); }
                player.LoadFile(Path.Combine(directory, "missing-media.mkv"));
                Assert.True(SpinWait.SpinUntil(() => File.Exists(log + ".1"), 10000), "The open log was not rotated.");
                Assert.True(new FileInfo(log + ".1").Length > 2 * 1024 * 1024);
                // mpv reopened a new, small log and keeps writing to it.
                Assert.True(SpinWait.SpinUntil(() => File.Exists(log) && new FileInfo(log).Length < 2 * 1024 * 1024, 10000));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("SILOPLAYER_LOG_DIRECTORY", previous);
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
