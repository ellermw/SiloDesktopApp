namespace SiloPlayer.Tests;

public sealed class MpvPlayerSourceTests
{
    [Fact]
    public void EofReachedPropertyRaisesDedicatedEventWithoutTreatingItAsNaturalEnd()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer.Player",
            "MpvPlayer.cs"));

        Assert.Contains("public event Action? EofReached;", source);
        Assert.Contains("EofReached?.Invoke();", source);

        var caseStart = source.IndexOf("case UD_EOF_REACHED:", StringComparison.Ordinal);
        var caseEnd = source.IndexOf("case UD_PAUSED_FOR_CACHE:", StringComparison.Ordinal);
        Assert.True(caseStart >= 0);
        Assert.True(caseEnd > caseStart);

        var eofCase = source[caseStart..caseEnd];
        Assert.DoesNotContain("PlaybackEnded?.Invoke();", eofCase);
    }

    [Fact]
    public void CommandFailureLoggingRedactsQuerySecrets()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer.Player",
            "MpvPlayer.cs"));

        Assert.Contains("RedactCommandArgument", source);
        Assert.Contains("room_token", source);
        Assert.Contains("api_key", source);
        Assert.DoesNotContain(@"(?:token|access_token|refresh_token|profile_token)", source);
    }

    [Fact]
    public void GpuPlaybackUsesStableAudioClockAcrossWindowFocusChanges()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer.Player",
            "MpvPlayer.cs"));

        Assert.True(source.Split("SetOption(\"video-sync\", \"audio\")", StringSplitOptions.None).Length >= 3);
        Assert.DoesNotContain("SetOption(\"video-sync\", \"display-resample\")", source, StringComparison.Ordinal);
        Assert.Contains("late video frames are dropped", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SiloOscProtectsFloatingActionButtonsFromVideoClickPause()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "libs",
            "mpv",
            "scripts",
            "silo-osc.lua"));

        Assert.Contains("local function point_on_skip_button", source);
        Assert.Contains("local function point_on_next_episode_button", source);
        Assert.Contains("local function point_on_floating_action_button", source);

        var mouseDownStart = source.IndexOf("local function handle_mouse_down()", StringComparison.Ordinal);
        var mouseDownEnd = source.IndexOf("local function handle_mouse_down_right()", StringComparison.Ordinal);
        Assert.True(mouseDownStart >= 0);
        Assert.True(mouseDownEnd > mouseDownStart);
        var mouseDown = source[mouseDownStart..mouseDownEnd];
        Assert.Contains("point_on_skip_button(mx, my)", mouseDown);
        Assert.Contains("point_on_next_episode_button(mx, my)", mouseDown);

        var videoClickStart = source.IndexOf("mp.register_script_message(\"osc-video-click\"", StringComparison.Ordinal);
        var videoClickEnd = source.IndexOf("-- Initialization", StringComparison.Ordinal);
        Assert.True(videoClickStart >= 0);
        Assert.True(videoClickEnd > videoClickStart);
        var videoClick = source[videoClickStart..videoClickEnd];
        Assert.Contains("point_on_floating_action_button(mx, my)", videoClick);
        Assert.Contains("return", videoClick);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
