namespace SiloPlayer.Tests;

public sealed class PlayerServiceSourceTests
{
    [Fact]
    public void HlsAndRemuxEmbeddedSubtitlesUseSlidingSidecarsWhileDirectPlayStaysNative()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("CanUseNativeEmbeddedSubtitleTrack()", source, StringComparison.Ordinal);
        Assert.Contains("TransportKind == PlaybackTransportKind.DirectProgressive", source, StringComparison.Ordinal);
        Assert.Contains("SelectEmbeddedSubtitleSidecar(track)", source, StringComparison.Ordinal);
        Assert.Contains("AppendPositionDuration(pair.Value.FullUrl, windowStart, SubtitleWindowDurationSeconds)", source, StringComparison.Ordinal);
        Assert.Contains("ServerTrackIndex = track.Index", source, StringComparison.Ordinal);
        Assert.Contains("AddSubtitle(newUrl, win.Label, win.Language, select: true)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BitstreamPassthroughUsesAnAudioClockWithoutResamplingAtmos()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("bitstream ? \"ac3,eac3,dts-hd,truehd\" : \"\"", source, StringComparison.Ordinal);
        Assert.Contains("mpv.SetProperty(\"video-sync\", \"audio\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetProperty(\"video-sync\", bitstream ? \"audio\" : \"display-resample\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailPlaybackReusesWatchDataAndDoesNotWaitBeforeSessionReplacement()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var detail = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs"));

        Assert.Contains("WatchDetailResponse? prefetchedWatchDetail = null", service, StringComparison.Ordinal);
        Assert.Contains("string.Equals(prefetchedWatchDetail.ContentId, contentId, StringComparison.Ordinal)", service, StringComparison.Ordinal);
        Assert.Contains("prefetchedWatchDetail:", detail, StringComparison.Ordinal);

        var methodStart = detail.IndexOf("private void NavigateToPlayer", StringComparison.Ordinal);
        var methodEnd = detail.IndexOf("private void SetNextEpisodeHintIfApplicable", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = detail[methodStart..methodEnd];
        Assert.DoesNotContain("Task.Delay(300)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseAsync()", method, StringComparison.Ordinal);
    }

    [Fact]
    public void RemuxAndHlsSeekRestartDoesNotWaitForProgressPersistence()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var methodStart = source.IndexOf("private async Task RestartTransportForSeekAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task ReportSeekProgressAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("_ = ReportSeekProgressAsync(mediaPosition, seekPaused)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("await ReportSeekProgressAsync(mediaPosition, seekPaused)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitPrePlaySubtitleSelectionMapsAcrossDifferentInventoryOrdering()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var methodStart = source.IndexOf("private void ResolveInitialSubtitleSelection", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void ApplyPendingSubtitleSelection", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("requested.External == true ? \"external\" : \"embedded\"", method, StringComparison.Ordinal);
        Assert.Contains("sourceInventory.IndexOf(requested)", method, StringComparison.Ordinal);
        Assert.Contains("ElementAtOrDefault(sourceOrdinal)", method, StringComparison.Ordinal);
        Assert.Contains("string.Equals(track.Source, requestedSource", method, StringComparison.Ordinal);
    }





    [Fact]
    public void LiveSubtitleTranslationStreamsCuesWithoutRestartingPlayback()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var mpv = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Player", "MpvPlayer.cs"));

        Assert.Contains("subtitle_translation_started", source);
        Assert.Contains("subtitle_translation_cues", source);
        Assert.Contains("subtitle_translation_completed", source);
        Assert.Contains("subtitle_translation_failed", source);
        Assert.Contains("WEBVTT", source);
        Assert.Contains("ReloadSubtitle(_liveSubtitleSid)", source);
        Assert.Contains("RefreshSubtitlesAfterAiAsync", source);
        Assert.Contains("osc-set-translation-buffering", source);
        Assert.Contains("public void ReloadSubtitle(int sid)", mpv);

        var refreshStart = source.IndexOf("public async Task RefreshSubtitlesAfterAiAsync", StringComparison.Ordinal);
        var refreshEnd = source.IndexOf("private void WriteLiveSubtitleFileLocked", refreshStart, StringComparison.Ordinal);
        Assert.True(refreshStart >= 0 && refreshEnd > refreshStart);
        Assert.DoesNotContain("SwitchVersionAsync", source[refreshStart..refreshEnd]);
    }

    [Fact]
    public void RequiredTranscodeStartupFailuresDoNotFallThroughToStreamEndpoint()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("Failed to start transcode playback.", source);
        Assert.DoesNotContain(
            """
            LogToFile("player_transcode_error.txt", ex.ToString());
                        return (null, null);
            """,
            source);
    }

    [Fact]
    public void LogicalNaturalEndDoesNotDrivePlayerExitOrPostroll()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.DoesNotContain("PlaybackNaturalEndDetector", source);
        Assert.DoesNotContain("HandleNaturalPlaybackEnded", source);
        Assert.DoesNotContain("Logical natural end detected", source);
        Assert.Contains("ShowPlayingNextRequested?.Invoke();", source);
        Assert.Contains("PlaybackEnded?.Invoke();", source);
    }

    [Fact]
    public void PlayingNextCountdownIsOnlyRequestedFromMpvEndSignal()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var methodStart = source.IndexOf("private void HandleMpvEndSignal", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void WireMpvEvents", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);

        var method = source[methodStart..methodEnd];
        Assert.Contains("ShowPlayingNextRequested?.Invoke();", method);
        Assert.Contains("IsAtMediaEnd(pos, dur)", method);
        Assert.DoesNotContain("dur * 0.95", method);
    }

    [Fact]
    public void MpvEofReachedFeedsPrematureStreamRecoveryPath()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("private Action? _mpvEofReachedHandler;", source);
        Assert.Contains("_mpv.EofReached += _mpvEofReachedHandler;", source);
        Assert.Contains("_mpv.EofReached -= _mpvEofReachedHandler;", source);
        Assert.Contains("HandleMpvEndSignal(\"eof-reached\")", source);
        Assert.Contains("HandleMpvEndSignal(\"end-file\")", source);
        Assert.Contains("RecoverInterruptedStreamAsync(pos, trigger)", source);
        Assert.DoesNotContain("_mpvPlaybackEndedHandler?.Invoke();", source);
    }

    [Fact]
    public void NativeOscChapterPayloadIncludesCurrentWebUiThumbnailUrl()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("thumbnail_url = chapter.ThumbnailUrl", source);
        Assert.Contains("osc-set-chapters", source);
        Assert.Contains("LoadChapterThumbnailForOscAsync", source);
        Assert.Contains("CanvasBitmap.LoadAsync", source);
        Assert.Contains("osc-set-chapter-thumbnail", source);
    }

    [Fact]
    public void ProgressKeepaliveFailureAttemptsSessionRecoveryBeforeClosingPlayer()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var methodStart = source.IndexOf("private void OnProgressReportingFailed", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void StartPlaybackStallWatchdog", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);

        var method = source[methodStart..methodEnd];
        Assert.Contains("RecoverInterruptedStreamAsync(CurrentMediaPosition, \"progress-reporting-failed\")", method);
        Assert.Contains("Reconnecting playback", method);
        Assert.DoesNotContain("CloseAsync", method);
        Assert.DoesNotContain("ShowPlaybackError", method);
    }

    [Fact]
    public void StreamRecoveryPreservesUserPauseButNotNetworkBufferingPause()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var methodStart = source.IndexOf("private async Task RecoverInterruptedStreamCoreAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private double _resumePosition", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);

        var method = source[methodStart..methodEnd];
        Assert.Contains("var restorePaused = _mpv.IsPaused && !_mpv.IsBufferingForCache;", method);
        Assert.Contains("BeginMpvLoad(prepared, restorePaused);", method);
        Assert.Contains("IsPaused = restorePaused;", method);
    }

    [Fact]
    public void MpvLoadFailureRetriesBeforeCleanlyClosingTheSession()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var methodStart = source.IndexOf("private void HandleMpvPlaybackError", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("// ── Content switching", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);

        var method = source[methodStart..methodEnd];
        Assert.Contains("RecoverInterruptedStreamAsync(mediaPosition, \"file-load-error\")", method);
        Assert.Contains("if (attempt == 1)", method);
        Assert.Contains("await CloseAsync();", method);
        Assert.DoesNotContain("SetState(PlayerState.Idle)", method);
        Assert.DoesNotContain("_videoWindow?.Hide()", method);
    }

    [Fact]
    public void QualitySwitchFailureKeepsOrRecoversTheExistingPlaybackSession()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var methodStart = source.IndexOf("private async Task SwitchQualityTierAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task ShowSubtitleSearchDialogAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);

        var method = source[methodStart..methodEnd];
        Assert.Contains("if (!transportReplaced)", method);
        Assert.Contains("RecoverInterruptedStreamAsync(currentPos, \"quality-switch-failed\")", method);
        Assert.DoesNotContain("ShowPlaybackError(\"Transcode failed\"", method);
    }

    [Fact]
    public void NativeOscSubtitleSelectionPersistsTheWebUiPreferenceShape()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var methodStart = source.IndexOf("private async Task SelectSubtitleByServerIndexAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void SelectSubtitleTrack", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);

        var method = source[methodStart..methodEnd];
        Assert.Contains("SetSubtitleTrackAndPersistAsync(0, null, null)", method);
        Assert.Contains("SetSubtitleTrackAndPersistAsync(mpvTrackIndex, track.Language, track)", method);
        Assert.Contains("osc-set-active-subtitle", method);
    }

    [Fact]
    public void CardLaunchedPlaybackResolvesEffectiveSubtitlePreferences()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("ResolveInitialSubtitleSelection(", source);
        Assert.Contains("SubtitleAutoSelect.BuildCandidates(session.SubtitleUrls)", source);
        Assert.Contains("PreferredTrackSignature: watchDetail.EffectiveSubtitleTrackSignature", source);
        Assert.Contains("SelectSubtitleByServerIndexAsync(initialSubtitleIndex, persist: false)", source);
    }

    [Fact]
    public void PlaybackWebSocketHandlesRealtimeMarkerUpdates()
    {
        var playerService = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));
        var websocket = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlaybackWebSocket.cs"));

        Assert.Contains("markers_updated", playerService);
        Assert.Contains("EventReceived", websocket);
        Assert.Contains("MarkersChanged", playerService);
        Assert.Contains("ApplyRealtimeMarkersUpdated", playerService);
    }

    [Fact]
    public void PlaybackWebSocketReconnectsWithTheCurrentAccessToken()
    {
        var playerService = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));
        var websocket = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlaybackWebSocket.cs"));

        Assert.Contains("ReconnectDelays", websocket);
        Assert.Contains("while (!ct.IsCancellationRequested)", websocket);
        Assert.Contains("_tokenProvider()", websocket);
        Assert.Contains("_seenCommandIds", websocket);
        Assert.Contains("() => _apiClient.AccessToken", playerService);
    }

    [Fact]
    public void RemotePositiveVolumeCommandAlsoUnmutesLikeCurrentWebUi()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("case \"set_volume\":", source);
        Assert.Contains("var normalizedVolume = Math.Min(1, Math.Max(0, vol.Value));", source);
        Assert.Contains("if (normalizedVolume > 0)", source);
        Assert.Contains("_mpv?.SetMute(false);", source);
    }

    [Fact]
    public void PlayerOverlayUsesActiveVersionMarkers()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Controls",
            "PlayerOverlay.xaml.cs"));

        Assert.Contains("ActiveIntro", source);
        Assert.Contains("ActiveCredits", source);
        Assert.Contains("MarkersChanged", source);
    }

    [Fact]
    public void PlayerOverlayHonorsAutoSkipProfilePreferences()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Controls",
            "PlayerOverlay.xaml.cs"));

        Assert.Contains("playback.auto_skip_intro", source);
        Assert.Contains("playback.auto_skip_credits", source);
        Assert.Contains("RefreshAutoSkipSettingsAsync", source);
        Assert.Contains("HasDeviceOverride", source);
        Assert.Contains("ApplyAutoSkipMarkers(pos, dur)", source);
        Assert.Contains("SeekAndResume(intro.End)", source);
        Assert.Contains("SeekAndResume(Math.Min(credits.End, dur))", source);
        Assert.Contains("NextEpisodeContentId", source);
    }

    [Fact]
    public void PlayerOverlayAllowsPgsAndHonorsRecapMarkers()
    {
        var overlay = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Controls",
            "PlayerOverlay.xaml.cs"));
        var playerService = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("playback.auto_skip_recap", overlay);
        Assert.Contains("ActiveRecap", overlay);
        Assert.Contains("SeekAndResume(recap.End)", overlay);
        Assert.Contains("IsUnsupportedBitmapSubtitle", overlay);
        Assert.Contains("IsUnsupportedBitmapSubtitle", playerService);
        Assert.DoesNotContain("return codec is \"pgs\" or \"pgssub\" or \"dvdsub\" or \"vobsub\";", playerService);
        Assert.DoesNotContain("c is \"pgs\" or \"pgssub\" or \"dvdsub\" or \"vobsub\"", overlay);
    }

    [Fact]
    public void PlaybackSettingsAutoSkipTogglesAreEnabled()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Views",
            "SettingsPage.xaml"));

        var introStart = source.IndexOf("x:Name=\"AutoSkipIntroToggle\"", StringComparison.Ordinal);
        var creditsStart = source.IndexOf("x:Name=\"AutoSkipCreditsToggle\"", StringComparison.Ordinal);
        Assert.True(introStart >= 0);
        Assert.True(creditsStart >= 0);

        var introBlock = source[introStart..Math.Min(source.Length, introStart + 300)];
        var creditsBlock = source[creditsStart..Math.Min(source.Length, creditsStart + 300)];

        Assert.DoesNotContain("IsEnabled=\"False\"", introBlock);
        Assert.DoesNotContain("IsEnabled=\"False\"", creditsBlock);
        Assert.DoesNotContain("Coming soon", source);
    }

    [Fact]
    public void NextItemStartupDoesNotWaitForSlowSessionFinalization()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var start = source.IndexOf("private async Task PlayCoreAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private static (string Title, string Detail) DescribePlaybackError", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        Assert.Contains("retiringSessionTask = retiringManager.StopSessionAsync", method);
        Assert.Contains("_ = FinishClosingSessionAsync(retiringManager, retiringSessionTask)", method);
        Assert.Contains("ex.ErrorCode == \"too_many_streams\"", method);
        Assert.Contains("await retiringSessionTask.WaitAsync(requestToken)", method);
        var startupHandoffEnd = method.IndexOf("ErrorMessage = null;", StringComparison.Ordinal);
        Assert.True(startupHandoffEnd > 0);
        Assert.DoesNotContain(
            "await _playbackManager.StopSessionAsync();",
            method[..startupHandoffEnd]);
    }

    [Fact]
    public void NativeMarkerEditorPersistsEveryCurrentWebUiMarkerKind()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("case \"silo-marker-save\"", source);
        Assert.Contains("SaveMarkerEditsFromOscAsync", source);
        Assert.Contains("[\"intro\"]", source);
        Assert.Contains("[\"recap\"]", source);
        Assert.Contains("[\"credits\"]", source);
        Assert.Contains("[\"preview\"]", source);
        Assert.Contains("SetFileMarkersAsync(session.MediaFileId, changes)", source);
        Assert.Contains("ApplyMarkerEdits(intro, recap, credits, preview)", source);
    }

    [Fact]
    public void PlayerHudUsesCurrentDynamicRangeVocabulary()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("FormatVideoRangeForHud(version, videoTrack)", source);
        Assert.Contains("MediaVideoRange.Label(requested)", source);
        Assert.Contains("Dolby Vision {track.DolbyVision}", source);
        Assert.DoesNotContain("requested.Hdr ? \"HDR\"", source);
    }

    [Fact]
    public void ProgressivePlaybackUsesBitrateAwareMpvBuffering()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var mpv = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Player", "MpvPlayer.cs"));

        Assert.Contains("prepared.Plan.IsHls", service);
        Assert.Contains("MpvNetworkBufferSizing.ForBitrateKbps(ActiveVersion?.Bitrate ?? 0)", service);
        Assert.Contains("mpv.ConfigureNetworkBuffer", service);
        Assert.Contains("SetProperty(\"demuxer-max-bytes\"", mpv);
        Assert.Contains("SetProperty(\"stream-buffer-size\"", mpv);
    }

    [Fact]
    public void AudioOutputPreferenceCannotRestoreFocusSensitiveDisplayClock()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("mpv.SetProperty(\"video-sync\", \"audio\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("bitstream ? \"audio\" : \"display-resample\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NativePlayerIsPrewarmedAfterAuthenticatedNavigationAtLowPriority()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var window = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));

        Assert.Contains("public void Prewarm()", service);
        Assert.Contains("ResetFailedMpvInitialization", service);
        Assert.Contains("DispatcherQueuePriority.Low", window);
        Assert.Contains("() => _playerService.Prewarm()", window);
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
