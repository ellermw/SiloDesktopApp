namespace SiloPlayer.Tests;

public sealed class FullscreenStateSyncSourceTests
{
    [Fact]
    public void EveryPlayerStateTransitionPublishesFullscreenVisualState()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs");
        var overlay = ReadRepoFile("src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs");

        Assert.Contains("PublishFullscreenVisualState(newState == PlayerState.Fullscreen)", service);
        Assert.Contains("SendScriptMessage(\"osc-fullscreen-state\"", service);
        Assert.Contains("PublishFullscreenVisualState(false);", service);
        Assert.Contains("SyncFullscreenIcon();", overlay);
    }

    [Fact]
    public void SameStateAutoplayTransitionReenablesTheOsc()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs")
            .ReplaceLineEndings("\n");
        var sameStateStart = service.IndexOf("if (State == newState)", StringComparison.Ordinal);
        var stateAssignment = service.IndexOf("State = newState;", sameStateStart, StringComparison.Ordinal);

        Assert.True(sameStateStart >= 0 && stateAssignment > sameStateStart);
        var sameStatePath = service[sameStateStart..stateAssignment];
        Assert.Contains("_videoWindow?.Show();", sameStatePath);
        Assert.Contains("_mpv?.SendScriptMessage(\"osc-set-visibility\", \"true\");", sameStatePath);
    }

    [Fact]
    public void HiddenPlaybackPopupDropsStalePointerTrackingBeforeReuse()
    {
        var videoWindow = ReadRepoFile("src", "SiloPlayer", "Services", "MpvVideoWindow.cs")
            .ReplaceLineEndings("\n");
        var hideStart = videoWindow.IndexOf("public void Hide()", StringComparison.Ordinal);
        var nextMethod = videoWindow.IndexOf("public void PositionAt", hideStart, StringComparison.Ordinal);

        Assert.True(hideStart >= 0 && nextMethod > hideStart);
        var hideMethod = videoWindow[hideStart..nextMethod];
        Assert.Contains("_mouseTracking = false;", hideMethod);
        Assert.Contains("ReleaseCapture();", hideMethod);
        Assert.Contains("_lbuttonDownTicks = 0;", hideMethod);
    }

    [Fact]
    public void AutoplayOwnsTheContentSwitchBeforeRetiringOldEpisodeState()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs")
            .ReplaceLineEndings("\n");
        var coreStart = service.IndexOf("private async Task PlayCoreAsync(", StringComparison.Ordinal);
        var loadingStart = service.IndexOf("CancelPendingFileLoadTimeout();", coreStart, StringComparison.Ordinal);
        Assert.True(coreStart >= 0 && loadingStart > coreStart);

        var transitionPrefix = service[coreStart..loadingStart];
        Assert.True(
            transitionPrefix.IndexOf("_switchingContent = true;", StringComparison.Ordinal) <
            transitionPrefix.IndexOf("PrepareEpisodeNavigationForContent(contentId);", StringComparison.Ordinal));

        var continueStart = service.IndexOf("public async Task ContinuePlayingNextAsync()", StringComparison.Ordinal);
        var previousStart = service.IndexOf("public Task PlayPreviousEpisodeAsync()", continueStart, StringComparison.Ordinal);
        var continueMethod = service[continueStart..previousStart];
        Assert.DoesNotContain("ClearNextEpisodeHint();", continueMethod);
        Assert.Contains("await PlayAsync(nextId, libraryId: ActiveLibraryId);", continueMethod);
        Assert.Contains("await ContinueShuffleAsync();", continueMethod);
    }

    [Fact]
    public void StalePlayingNextPresentationCannotHideTheSuccessorOsc()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs");
        var mainWindow = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");

        Assert.Contains("private volatile bool _switchingContent;", service);
        Assert.Contains("public bool IsSwitchingContent => _switchingContent;", service);

        var handlerStart = mainWindow.IndexOf(
            "private void OnShowPlayingNextRequested(bool videoEnded)",
            StringComparison.Ordinal);
        var handlerEnd = mainWindow.IndexOf(
            "private void OnPostRollReturnRequested()",
            handlerStart,
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);

        var handler = mainWindow[handlerStart..handlerEnd];
        Assert.Contains("var ownerContentId = _playerService.ContentId;", handler);
        Assert.Contains("_playerService.IsSwitchingContent", handler);
        Assert.Contains(
            "!string.Equals(ownerContentId, _playerService.ContentId, StringComparison.Ordinal)",
            handler);
        Assert.True(
            handler.IndexOf("_playerService.IsSwitchingContent", StringComparison.Ordinal) <
            handler.IndexOf("_playerService.EnterPostRollPreview();", StringComparison.Ordinal));
    }

    [Fact]
    public void OscFullscreenIconIsStateDriven()
    {
        var osc = ReadRepoFile("libs", "mpv", "scripts", "silo-osc.lua");
        Assert.Contains("mp.register_script_message(\"osc-fullscreen-state\"", osc);
        Assert.Contains("local fullscreen = (val == \"true\")", osc);
        Assert.Contains("state.fullscreen = fullscreen", osc);
        Assert.Contains("if state.fullscreen ~= fullscreen then", osc);
        Assert.Contains("draw_fullscreen_icon", osc);
    }

    [Fact]
    public void PictureInPictureStateAndEpisodeActionsRoundTripThroughHost()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs");

        Assert.Contains("PublishPictureInPictureVisualState", service);
        Assert.Contains(
            "if (newState != PlayerState.PictureInPicture)\n            _videoWindow?.ExitPictureInPicture();",
            service.ReplaceLineEndings("\n"));
        Assert.Matches(
            "(?s)HandleUnhandledPlaybackEscape\\(\\).*?State == PlayerState\\.PictureInPicture.*?SetState\\(PlayerState\\.Expanded\\)",
            service);
        Assert.Contains("case \"silo-pip-toggle\"", service);
        Assert.Contains("case \"silo-prev-episode\"", service);
        Assert.Contains("PlayPreviousEpisodeAsync", service);
        Assert.Contains("osc-set-episode-navigation", service);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? "";
        if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("Could not find repository root.");
        return File.ReadAllText(Path.Combine([dir, .. parts]));
    }
}
