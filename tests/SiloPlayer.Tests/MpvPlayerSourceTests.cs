namespace SiloPlayer.Tests;

public sealed class MpvPlayerSourceTests
{
    [Fact]
    public void WindowedEscapeClosesOscMenusInsteadOfPlayback()
    {
        var window = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "MpvVideoWindow.cs"));

        Assert.Contains("if (_isFullscreen)", window, StringComparison.Ordinal);
        Assert.Contains("_mpv?.SendKeypress(\"ESC\")", window, StringComparison.Ordinal);
    }

    [Fact]
    public void EofReachedPropertyRaisesDedicatedEventWithoutTreatingItAsNaturalEnd()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer.Player",
            "MpvPlayer.cs"));

        Assert.Contains("public event Action? EofReached;", source);
        Assert.Contains("InvokeSafely(EofReached, nameof(EofReached));", source);

        var caseStart = source.IndexOf("case UD_EOF_REACHED:", StringComparison.Ordinal);
        var caseEnd = source.IndexOf("case UD_PAUSED_FOR_CACHE:", StringComparison.Ordinal);
        Assert.True(caseStart >= 0);
        Assert.True(caseEnd > caseStart);

        var eofCase = source[caseStart..caseEnd];
        Assert.DoesNotContain("InvokeSafely(PlaybackEnded", eofCase);
    }

    [Fact]
    public void PlaybackRestartEventExposesNativeFirstFrameBoundary()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Player", "MpvPlayer.cs"));
        var interop = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Player", "MpvInterop.cs"));

        Assert.Contains("MPV_EVENT_PLAYBACK_RESTART  = 21", interop, StringComparison.Ordinal);
        Assert.Contains("public event Action? PlaybackRestarted;", source, StringComparison.Ordinal);
        Assert.Contains("case MPV_EVENT_PLAYBACK_RESTART:", source, StringComparison.Ordinal);
        Assert.Contains("InvokeSafely(PlaybackRestarted, nameof(PlaybackRestarted));", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeEventAndRenderCallbacksCannotTerminateTheMpvPumps()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer.Player",
            "MpvPlayer.cs"));

        Assert.Contains("private void InvokeSafely(Action? handler, string eventName)", source);
        Assert.Contains("private void ReportErrorSafely(string message)", source);
        Assert.Contains("handler.GetInvocationList()", source);
        Assert.Contains("InvokeFrameReadySafely(buffer, w, h, (int)stride);", source);
        Assert.Contains("mpv event {ev.EventId} failed", source);
        Assert.Contains("FrameReady callback failed", source);
        Assert.Contains("nameof(PlaybackError));", source);
        Assert.Contains("InvokeSafely(PlaybackEnded", source);
        Assert.Contains("PlaybackError = null;", source);
        Assert.Contains("BufferingChanged = null;", source);
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
    public void NativeVideoWindowKeepsItsRegisteredCallbackAliveAcrossRecreation()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "MpvVideoWindow.cs"));

        Assert.Contains("private static readonly WndProcDelegate s_wndProc = StaticWndProc;", source);
        Assert.Contains("ConcurrentDictionary<IntPtr, MpvVideoWindow> s_windows", source);
        Assert.Contains("s_windows[hwnd] = this;", source);
        Assert.Contains("s_windows.TryRemove(hwnd, out _);", source);
        Assert.Contains("lock (s_classRegistrationLock)", source);
        Assert.Contains("Managed exceptions must never cross the native callback boundary.", source);
        Assert.Contains("native_window_error.txt", source);
        Assert.DoesNotContain("GCHandle _wndProcHandle", source);
        Assert.DoesNotContain("GetFunctionPointerForDelegate(WndProcInstance)", source);
    }

    [Fact]
    public void DelayedFullscreenCorrectionDoesNotStealFocusBackFromOtherApps()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "MpvVideoWindow.cs"));

        var delayStart = source.IndexOf("Task.Delay(200)", StringComparison.Ordinal);
        var nextMember = source.IndexOf("        });", delayStart, StringComparison.Ordinal);
        Assert.True(delayStart >= 0);
        Assert.True(nextMember > delayStart);

        var delayedCorrection = source[delayStart..(nextMember + "        });".Length)];
        Assert.Contains("PositionFullscreen(preserveZOrder: true);", delayedCorrection);
        Assert.DoesNotContain("SetForegroundWindow", delayedCorrection);
        Assert.DoesNotContain("HWND_TOP", delayedCorrection);
    }

    [Fact]
    public void PopupFullscreenAndPictureInPictureTransitionsAreStableAcrossInputPaths()
    {
        var root = FindRepositoryRoot();
        var window = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "MpvVideoWindow.cs"));
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));

        var fullscreenStart = window.IndexOf("public void EnterFullscreen()", StringComparison.Ordinal);
        var fullscreenEnd = window.IndexOf("private RECT _fullscreenRect", fullscreenStart, StringComparison.Ordinal);
        Assert.True(fullscreenStart >= 0 && fullscreenEnd > fullscreenStart);
        Assert.Contains("ExitPictureInPicture();", window[fullscreenStart..fullscreenEnd], StringComparison.Ordinal);
        Assert.Contains("GetDpiForWindow(_hwnd) / 96d", window, StringComparison.Ordinal);
        Assert.Contains("bool isRepeat =", window, StringComparison.Ordinal);
        Assert.Contains("double-click preserves play/pause", window, StringComparison.Ordinal);
        Assert.Contains("PublishFullscreenVisualState(true);", service, StringComparison.Ordinal);
        Assert.Contains("PublishFullscreenVisualState(false);", service, StringComparison.Ordinal);
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
