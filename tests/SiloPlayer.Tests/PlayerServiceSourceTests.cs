namespace SiloPlayer.Tests;

public sealed class PlayerServiceSourceTests
{
    [Fact]
    public void NativeOscReceivesTheActiveApplicationAccent()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("SendThemeToOsc();", service, StringComparison.Ordinal);
        Assert.Contains("\"osc-set-theme\"", service, StringComparison.Ordinal);
        Assert.Contains("\"AccentColor\"", service, StringComparison.Ordinal);
        Assert.Contains("\"AccentBrush\"", service, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackInfoReceivesTheCurrentWebUiSourceFields()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("[\"video_bitrate\"] = videoTrack?.Bitrate", service, StringComparison.Ordinal);
        Assert.Contains("[\"video_range\"] = FormatVideoRangeForHud", service, StringComparison.Ordinal);
        Assert.Contains("[\"color_range\"] = videoTrack?.ColorRange", service, StringComparison.Ordinal);
        Assert.Contains("[\"audio_bitrate\"] = audioTrack?.Bitrate", service, StringComparison.Ordinal);
        Assert.Contains("[\"audio_sample_rate\"] = audioTrack?.SampleRate", service, StringComparison.Ordinal);
    }

    [Fact]
    public void FileLoadedInitialization_CancelsStaleReloadWork()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("StartLoadedMediaInitialization();", service, StringComparison.Ordinal);
        Assert.Contains("CancelPendingLoadedMediaInitialization();", service, StringComparison.Ordinal);
        Assert.Contains("IsLoadedMediaInitializationCurrent(generation, ownerCts)", service, StringComparison.Ordinal);
        Assert.Contains("Loaded-media initialization failed", service, StringComparison.Ordinal);
    }

    [Fact]
    public void SubtitleSlidingWindowState_IsProtectedAcrossMpvAndWorkerThreads()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("private readonly object _subtitleTrackStateLock = new();", service, StringComparison.Ordinal);
        Assert.Contains("lock (_subtitleTrackStateLock)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayableHomeCardsPrefetchAndReuseWatchDetailBeforePlay()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var manager = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Services", "PlaybackManager.cs"));
        var card = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "LandscapeCard.xaml.cs"));
        var posterCard = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "PosterCard.xaml.cs"));
        var libraryCard = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "LibraryGridCard.cs"));
        var hero = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "HeroCarousel.xaml.cs"));
        var detail = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs"));

        Assert.Contains("public void PrefetchWatchDetail(string? contentId)", service, StringComparison.Ordinal);
        Assert.Contains("public async Task<WatchDetailResponse> GetOrFetchWatchDetailAsync", service, StringComparison.Ordinal);
        Assert.Contains("await GetOrFetchWatchDetailCoreAsync(", service, StringComparison.Ordinal);
        Assert.Contains("consumePrefetch: true", service, StringComparison.Ordinal);
        Assert.Contains("PrefetchWatchDetail(contentId);", service, StringComparison.Ordinal);
        Assert.Contains("_playbackManager.UseWatchDetail(watchDetail);", service, StringComparison.Ordinal);
        Assert.Contains("public WatchDetailResponse UseWatchDetail", manager, StringComparison.Ordinal);
        Assert.Contains("QueuePlaybackPrefetch();", card, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(140, ct);", card, StringComparison.Ordinal);
        Assert.Contains("QueuePlaybackPrefetch();", posterCard, StringComparison.Ordinal);
        Assert.Contains("QueuePlaybackPrefetch();", libraryCard, StringComparison.Ordinal);
        Assert.Contains(".PrefetchWatchDetail(item.ContentId);", hero, StringComparison.Ordinal);
        Assert.Contains("GetOrFetchWatchDetailAsync(contentId)", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("playbackApi.GetWatchDetailAsync(contentId)", detail, StringComparison.Ordinal);

        var playPrefetchIndex = service.IndexOf("// Prefetch owns and observes the network task", StringComparison.Ordinal);
        var mpvInitIndex = service.IndexOf("EnsureMpvInitialized();", playPrefetchIndex, StringComparison.Ordinal);
        var watchAwaitIndex = service.IndexOf("watchDetail = await GetOrFetchWatchDetailCoreAsync(", playPrefetchIndex, StringComparison.Ordinal);
        Assert.True(playPrefetchIndex >= 0 && mpvInitIndex > playPrefetchIndex && watchAwaitIndex > mpvInitIndex,
            "First-play libmpv initialization should overlap the in-flight watch-detail request.");
        Assert.Contains("mpv.SendScriptMessage(\"osc-set-loading\", \"true\")", service, StringComparison.Ordinal);
        Assert.Contains("_mpv?.SendScriptMessage(\"osc-set-loading\", \"false\")", service, StringComparison.Ordinal);
        Assert.Contains("_mpv?.SendScriptMessage(\"osc-set-buffering\", buffering ? \"true\" : \"false\")", service, StringComparison.Ordinal);

        var fileLoadedHandler = service.IndexOf("_mpvFileLoadedHandler = () =>", StringComparison.Ordinal);
        var firstFrameHandler = service.IndexOf("_mpvPlaybackRestartedHandler = () =>", fileLoadedHandler, StringComparison.Ordinal);
        var loadingDismissal = service.IndexOf("_mpv?.SendScriptMessage(\"osc-set-loading\", \"false\")", fileLoadedHandler, StringComparison.Ordinal);
        Assert.True(fileLoadedHandler >= 0 && firstFrameHandler > fileLoadedHandler && loadingDismissal > firstFrameHandler,
            "The startup surface must remain visible through FILE_LOADED and dismiss at PLAYBACK_RESTART (first output frame)." );
    }

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
    public void LiveSubtitleTranslationPreservesPauseIntentWithoutRestartingPlayback()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var mpv = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Player", "MpvPlayer.cs"));
        var aiDialog = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "SubtitleAiDialog.xaml.cs"));
        var overlay = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs"));

        Assert.Contains("subtitle_translation_started", source);
        Assert.Contains("subtitle_translation_cues", source);
        Assert.Contains("subtitle_translation_completed", source);
        Assert.Contains("subtitle_translation_failed", source);
        Assert.Contains("WEBVTT", source);
        Assert.Contains("ReloadSubtitle(_liveSubtitleSid)", source);
        Assert.Contains("RefreshSubtitlesAfterAiAsync", source);
        Assert.Contains("osc-set-translation-buffering", source);
        Assert.Contains("if (_resumeAfterLiveSubtitleBuffer)", source);
        Assert.Contains("_mpv.Pause();", source);
        Assert.Contains("if (_resumeAfterLiveSubtitleBuffer && _mpv?.IsPaused == true)", source);
        Assert.Contains("public void ReloadSubtitle(int sid)", mpv);

        var refreshStart = source.IndexOf("public async Task RefreshSubtitlesAfterAiAsync", StringComparison.Ordinal);
        var refreshEnd = source.IndexOf("private void WriteLiveSubtitleFileLocked", refreshStart, StringComparison.Ordinal);
        Assert.True(refreshStart >= 0 && refreshEnd > refreshStart);
        Assert.DoesNotContain("SwitchVersionAsync", source[refreshStart..refreshEnd]);
        Assert.Contains(
            "await SelectSubtitleByServerIndexAsync(preferred.Index).ConfigureAwait(false);",
            source[refreshStart..refreshEnd],
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RestorePlaybackStateAfterSubtitleChange(wasPaused, position",
            source[refreshStart..refreshEnd],
            StringComparison.Ordinal);

        var beginStart = source.IndexOf("private void BeginLiveSubtitleTranslation", StringComparison.Ordinal);
        var beginEnd = source.IndexOf("private void ApplySubtitleTranslationCues", beginStart, StringComparison.Ordinal);
        Assert.True(beginStart >= 0 && beginEnd > beginStart);
        var begin = source[beginStart..beginEnd];
        Assert.Contains("if (_resumeAfterLiveSubtitleBuffer)", begin, StringComparison.Ordinal);
        Assert.Contains("_mpv.Pause();", begin, StringComparison.Ordinal);
        Assert.Contains("Playback pauses while the first lines are translated", aiDialog, StringComparison.Ordinal);
        Assert.Contains("MediaLanguageCatalog.Normalize(selection.SourceLanguage)", overlay, StringComparison.Ordinal);
        Assert.Contains("MediaLanguageCatalog.Normalize(selection.TargetLanguage)", overlay, StringComparison.Ordinal);
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

        Assert.Contains("Failed to prepare HLS playback.", source);
        Assert.DoesNotContain(
            """
            LogToFile("player_transcode_error.txt", ex.ToString());
                        return null;
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
        Assert.Contains("InvokeSubscribersSafely(ShowPlayingNextRequested, true", source);
        Assert.Contains("InvokeSubscribersSafely(ShowPlayingNextRequested, false", source);
        Assert.Contains("InvokeSubscribersSafely(PlaybackEnded", source);
    }

    [Fact]
    public void SeriesPostRollEntersEarlyButMarksTrueEndSeparately()
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
        Assert.Contains("InvokeSubscribersSafely(ShowPlayingNextRequested, true", method);
        Assert.Contains("IsAtMediaEnd(pos, dur)", method);
        Assert.DoesNotContain("dur * 0.95", method);

        var wireStart = methodEnd;
        var wireEnd = source.IndexOf("private void HandleMpvPlaybackError", wireStart, StringComparison.Ordinal);
        Assert.True(wireEnd > wireStart);
        var playerEvents = source[wireStart..wireEnd];
        Assert.Contains("CurrentMediaDuration - mediaPosition <= 30", playerEvents);
        Assert.Contains("InvokeSubscribersSafely(ShowPlayingNextRequested, false", playerEvents);
        Assert.Contains("PrefetchQueuedEpisodeWatchDetail();", playerEvents);
        Assert.Contains("PrefetchWatchDetail(NextEpisodeContentId);", source);
        Assert.Contains("SendScriptMessage(\"osc-set-post-roll\", \"true\")", source);
        Assert.Contains("SendScriptMessage(\"osc-set-post-roll\", \"false\")", source);
    }

    [Fact]
    public void EpisodeNavigationMetadataDoesNotSerializeIndependentSeasonRequests()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var methodStart = source.IndexOf("private async Task AutoDetectNextEpisodeAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void ResolveInitialSubtitleSelection", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("var episodeTasks = seasonNumbers", method, StringComparison.Ordinal);
        Assert.Contains("await Task.WhenAll(episodeTasks)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.Delay(100", method, StringComparison.Ordinal);
    }

    [Fact]
    public void EpisodeNavigationIsScopedToTheCurrentContentAndRejectsLateLookups()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("PrepareEpisodeNavigationForContent(contentId);", source);
        Assert.Contains("_episodeNavigation.PrepareFor(contentId);", source);
        Assert.Contains("IsEpisodeNavigationLookupCurrent(ownerManager, ownerContentId, ct)", source);
        Assert.Contains("_episodeNavigation.TrySetResolved(ownerContentId, previousContentId, nextTarget)", source);
        Assert.Contains("HasNextEpisodeForCurrentPlayback", source);
        Assert.Contains(
            "string.Equals(detail.Type, \"episode\", StringComparison.OrdinalIgnoreCase)",
            source);
    }

    [Fact]
    public void SubtitleSearchRefreshDoesNotRestoreTheStaleDialogOpenPosition()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var methodStart = source.IndexOf("private async Task ShowSubtitleSearchDialogAsync()", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task ShowSubtitleAppearanceDialogAsync()", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("await RefreshSubtitlesAfterAiAsync(session.MediaFileId, downloadedSubtitleId);", method, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RestorePlaybackStateAfterSubtitleChange(snapshot.WasPaused, snapshot.Position, allowSeek: true)",
            method,
            StringComparison.Ordinal);
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
    public void ActivePlaybackOwnsAndReleasesANativeWindowsDisplayWakeRequestOnTheUiThread()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.DoesNotContain("DisplayRequest", source);
        Assert.Contains("SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired)", source);
        Assert.Contains("SetThreadExecutionState(EsContinuous)", source);
        Assert.Contains("if (!dispatcher.HasThreadAccess)", source);
        Assert.Contains("dispatcher.TryEnqueue(() => UpdateDisplayWakeLock(active))", source);
        Assert.Contains("UpdateDisplayWakeLock(!paused", source);
        Assert.Contains("if (newState == PlayerState.Idle)", source);
        Assert.Contains("UpdateDisplayWakeLock(false);", source);

        var disposeStart = source.IndexOf("public void Dispose()", StringComparison.Ordinal);
        var disposeEnd = source.IndexOf("// ── WebSocket session control", disposeStart, StringComparison.Ordinal);
        Assert.True(disposeStart >= 0 && disposeEnd > disposeStart);
        var dispose = source[disposeStart..disposeEnd];
        Assert.Contains("UpdateDisplayWakeLock(false);", dispose, StringComparison.Ordinal);
        Assert.Contains("DisconnectWebSocket();", dispose, StringComparison.Ordinal);
        Assert.Contains("_playbackCts?.Cancel();", dispose, StringComparison.Ordinal);
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
        Assert.Contains("StorageFile.GetFileFromPathAsync", source);
        Assert.Contains("BitmapDecoder.CreateAsync", source);
        Assert.Contains("BitmapPixelFormat.Bgra8", source);
        Assert.Contains("pixelData.DetachPixelData()", source);
        Assert.DoesNotContain("CanvasBitmap.LoadAsync", source);
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
        Assert.Contains("_recoveryPauseIntentOverride", method);
        Assert.Contains("_mpv.IsPaused && !_mpv.IsBufferingForCache", method);
        Assert.Contains("BeginMpvLoad(prepared, restorePaused);", method);
        Assert.Contains("IsPaused = restorePaused;", method);
    }

    [Fact]
    public void MpvLoadFailureRetriesBeforeWaitingForExplicitViewerAction()
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
        Assert.Contains("EnterPlaybackTerminalState", method);
        Assert.DoesNotContain("CloseAsync()", method);
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
    public void AcceptedVersionAndBitmapTransportSwitchesRecoverInsteadOfClosingPlayback()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("var replacementAccepted = false;", source, StringComparison.Ordinal);
        Assert.Contains("RecoverInterruptedStreamAsync(currentPos, \"version-switch-failed\")", source, StringComparison.Ordinal);
        Assert.Contains("RecoverInterruptedStreamAsync(currentPos, \"quality-reset-failed\")", source, StringComparison.Ordinal);
        Assert.Contains("var transportMutationAttempted = false;", source, StringComparison.Ordinal);
        Assert.Contains("RecoverInterruptedStreamAsync(CurrentMediaPosition, \"subtitle-switch-failed\")", source, StringComparison.Ordinal);
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
        Assert.Contains("HasNextEpisodeForCurrentPlayback", source);
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
    public void AcceptedHlsAudioSwitchRecoversInsteadOfLeavingAnInvalidatedStream()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));
        var methodStart = source.IndexOf("public async Task SwitchAudioTrackAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("// ── Subtitles", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("var serverTransportChanged = false;", method, StringComparison.Ordinal);
        Assert.Contains("manager.ApplyAudioChange(response);", method, StringComparison.Ordinal);
        Assert.Contains("serverTransportChanged = true;", method, StringComparison.Ordinal);
        Assert.Contains("RecoverInterruptedStreamAsync(currentPos, \"audio-switch-failed\")", method, StringComparison.Ordinal);
        Assert.Contains("if (serverTransportChanged && !_closing", method, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepaliveFailureMintsAReplacementInsteadOfReopeningAReapedSession()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var policy = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Services", "PlaybackRecoveryPolicy.cs"));

        Assert.DoesNotContain("\"progress-reporting-failed\"", policy, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(reason, \"progress-reporting-failed\"", service, StringComparison.Ordinal);
        Assert.Contains("manager.StartReplacementSessionAsync", service, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerCallbacksIsolateSubscribersSoUiFailuresCannotStopPlaybackStateUpdates()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("private static void InvokeSubscribersSafely(Action? handlers", source, StringComparison.Ordinal);
        Assert.Contains("handlers.GetInvocationList().Cast<Action>()", source, StringComparison.Ordinal);
        Assert.Contains("handlers.GetInvocationList().Cast<Action<T>>()", source, StringComparison.Ordinal);
        Assert.Contains("InvokeSubscribersSafely(PositionChanged, mediaPosition", source, StringComparison.Ordinal);
        Assert.Contains("InvokeSubscribersSafely(PauseChanged, paused", source, StringComparison.Ordinal);
        Assert.Contains("InvokeSubscribersSafely(ContentLoaded", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PositionChanged?.Invoke", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostRollPreviewSurvivesWindowChangesAndDisposeAlwaysRetiresTheSession()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("else if (_postRollActive && !_postRollVideoEnded)", source, StringComparison.Ordinal);
        Assert.Contains("_videoWindow?.EnterPostRollPreview();", source, StringComparison.Ordinal);
        Assert.Contains("_closing = true;", source, StringComparison.Ordinal);
        Assert.Contains("_playbackManager.ProgressReportingFailed -= OnProgressReportingFailed;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (State != PlayerState.Idle)\n        {\n            _playbackManager?.Dispose();", source.ReplaceLineEndings("\n"), StringComparison.Ordinal);
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
        Assert.Contains("compactRange.StartsWith(\"DV\"", source);
        Assert.Contains("Current Silo probes may identify Dolby Vision through dv_profile", source);
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
    public void NativeOscReceivesProfileAndDeviceEffectiveAutoSkipSettings()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("SendAutoSkipSettingsToOscAsync(ct)", source, StringComparison.Ordinal);
        Assert.Contains("GetProfilesAsync(ct)", source, StringComparison.Ordinal);
        Assert.Contains("GetEffectiveSettingsAsync(", source, StringComparison.Ordinal);
        Assert.Contains("osc-set-auto-skip", source, StringComparison.Ordinal);
        Assert.Contains("AutoSkipIntroSettingKey", source, StringComparison.Ordinal);
        Assert.Contains("AutoSkipRecapSettingKey", source, StringComparison.Ordinal);
        Assert.Contains("AutoSkipCreditsSettingKey", source, StringComparison.Ordinal);
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

    [Fact]
    public void SubtitleSelectionRestoresPreviousPlaybackStateInsteadOfPausing()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var methodStart = source.IndexOf(
            "public async Task SetSubtitleTrackAndPersistAsync(int mpvTrackIndex, string? language, SubtitleTrackInfo? track)",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void RestorePlaybackStateAfterSubtitleChange", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("var wasPaused = CaptureUserPausedState();", method, StringComparison.Ordinal);
        Assert.Contains("var position = CurrentMediaPosition;", method, StringComparison.Ordinal);
        Assert.Contains("RestorePlaybackStateAfterSubtitleChange(wasPaused, position, allowSeek: true);", method, StringComparison.Ordinal);
        Assert.Contains(
            "=> (_mpv?.IsPaused ?? IsPaused) && _mpv?.IsBufferingForCache != true;",
            source,
            StringComparison.Ordinal);

        var restoreStart = source.IndexOf("private void RestorePlaybackStateAfterSubtitleChange", StringComparison.Ordinal);
        var restoreEnd = source.IndexOf("/// <summary>", restoreStart, StringComparison.Ordinal);
        Assert.True(restoreStart >= 0 && restoreEnd > restoreStart);
        var restore = source[restoreStart..restoreEnd];
        Assert.Contains("_mpv.Play();", restore, StringComparison.Ordinal);
        Assert.Contains("_mpv.Pause();", restore, StringComparison.Ordinal);
        Assert.Contains("PlaybackTimeline.ToPlayerTime(", restore, StringComparison.Ordinal);
        Assert.Contains("QueueTransportRestartForSeek(position, forceResume: !wasPaused);", restore, StringComparison.Ordinal);
        Assert.Contains("IsPaused = wasPaused;", restore, StringComparison.Ordinal);
    }

    [Fact]
    public void LatestRemuxOrHlsSeekCanSupersedeAnInFlightRestart()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var methodStart = source.IndexOf("private void SeekCore(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void QueueTransportRestartForSeek", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains(
            "var seekRestartInProgress = Volatile.Read(ref _seekRestartCts) != null;",
            method,
            StringComparison.Ordinal);
        Assert.Contains(
            "(_switchingContent && !seekRestartInProgress)",
            method,
            StringComparison.Ordinal);
        Assert.Contains("QueueTransportRestartForSeek(mediaPosition, forceResume);", method, StringComparison.Ordinal);
    }

    [Fact]
    public void GrowingCopyHlsNeverNativeSeeksInsideAnIncompleteProducedWindow()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var methodStart = source.IndexOf("private void SeekCore(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private void QueueTransportRestartForSeek",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains(
            "(plan.IsHls && _canSeekAnywhere && !targetPrecedesTransportWindow)",
            method,
            StringComparison.Ordinal);
        Assert.Contains("var targetPrecedesTransportWindow = transportPosition < 0;", method);
        Assert.DoesNotContain(
            "PlaybackTimeline.IsInsideExposedWindow",
            method,
            StringComparison.Ordinal);
        Assert.Contains(
            "QueueTransportRestartForSeek(mediaPosition, forceResume);",
            method,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PreparedHlsAndAudioRestartsHonorServerLocalStartAndOrigin()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains(
            "MpvLoadStartSeconds: Math.Max(0, transcodeResponse.PlayerStartSeconds)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "MpvLoadStartSeconds: transcodeResponse.CanSeekAnywhere",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "response.StreamOriginSeconds",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "response.TimelineOffsetSeconds",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "var canSeekAnywhere = response.CanSeekAnywhere ?? !copyWindow;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "QueueTransportRestartForSeek(position, forceResume: !wasPaused);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "PlaybackTimeline.ToPlayerTime(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "MpvLoadStartSeconds: playerStart",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NewPlaybackDoesNotInheritThePreviousSessionsManualQualityTier()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.True(
            source.Split("_activeQualityTier = \"original\";", StringSplitOptions.None).Length - 1 >= 3,
            "The quality tier must be initialized and reset at both playback start and teardown.");
        Assert.Contains(
            "TranscodeQualityPolicy.ResolveInitialVideoTier(",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SubtitleDialogsRestorePlaybackSurfaceWithoutRestartingCurrentStream()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("private sealed record PlaybackUiSnapshot(bool WasFullscreen, bool WasPaused, double Position);", source, StringComparison.Ordinal);
        Assert.Contains("private PlaybackUiSnapshot CapturePlaybackUiSnapshot()", source, StringComparison.Ordinal);
        Assert.Contains("private void RestorePlaybackUiSnapshot(PlaybackUiSnapshot snapshot, bool activatePlaybackSurface)", source, StringComparison.Ordinal);

        var searchStart = source.IndexOf("private async Task ShowSubtitleSearchDialogAsync()", StringComparison.Ordinal);
        var searchEnd = source.IndexOf("private async Task ShowSubtitleAppearanceDialogAsync()", searchStart, StringComparison.Ordinal);
        Assert.True(searchStart >= 0 && searchEnd > searchStart);
        var search = source[searchStart..searchEnd];
        Assert.Contains("var snapshot = CapturePlaybackUiSnapshot();", search, StringComparison.Ordinal);
        Assert.Contains("RestorePlaybackUiSnapshot(snapshot, restorePlayerInput);", search, StringComparison.Ordinal);
        Assert.Contains("await RefreshSubtitlesAfterAiAsync(session.MediaFileId, downloadedSubtitleId);", search, StringComparison.Ordinal);
        Assert.DoesNotContain("RestorePlaybackStateAfterSubtitleChange(snapshot.WasPaused, snapshot.Position, allowSeek: true);", search, StringComparison.Ordinal);
        Assert.DoesNotContain("SwitchVersionAsync", search, StringComparison.Ordinal);
        Assert.DoesNotContain("_videoWindow?.Hide()", search, StringComparison.Ordinal);
        Assert.Contains("PlaybackDialogHost.ShowAsync", search, StringComparison.Ordinal);
        Assert.Contains("osc-subtitle-dialog-closed", search, StringComparison.Ordinal);

        var appearanceStart = source.IndexOf("private async Task ShowSubtitleAppearanceDialogAsync()", StringComparison.Ordinal);
        var appearanceEnd = source.IndexOf("private async Task ShowSubtitleAiDialogAsync()", appearanceStart, StringComparison.Ordinal);
        Assert.True(appearanceStart >= 0 && appearanceEnd > appearanceStart);
        var appearance = source[appearanceStart..appearanceEnd];
        Assert.Contains("var snapshot = CapturePlaybackUiSnapshot();", appearance, StringComparison.Ordinal);
        Assert.Contains("RestorePlaybackUiSnapshot(snapshot, restorePlayerInput);", appearance, StringComparison.Ordinal);
        Assert.DoesNotContain("_videoWindow?.Hide()", appearance, StringComparison.Ordinal);
        Assert.Contains("PlaybackDialogHost.ShowAsync", appearance, StringComparison.Ordinal);
        Assert.Contains("osc-subtitle-dialog-closed", appearance, StringComparison.Ordinal);

        var aiStart = source.IndexOf("private async Task ShowSubtitleAiDialogAsync()", StringComparison.Ordinal);
        var aiEnd = source.IndexOf("private sealed record PlaybackUiSnapshot", aiStart, StringComparison.Ordinal);
        Assert.True(aiStart >= 0 && aiEnd > aiStart);
        var ai = source[aiStart..aiEnd];
        Assert.Contains("var snapshot = CapturePlaybackUiSnapshot();", ai, StringComparison.Ordinal);
        Assert.Contains("RestorePlaybackUiSnapshot(snapshot, restorePlayerInput);", ai, StringComparison.Ordinal);
        Assert.DoesNotContain("_videoWindow?.Hide()", ai, StringComparison.Ordinal);
        Assert.Contains("PlaybackDialogHost.ShowAsync", ai, StringComparison.Ordinal);
        Assert.Contains("osc-subtitle-dialog-closed", ai, StringComparison.Ordinal);

        var markerStart = source.IndexOf("private async Task ShowMarkerEditDialogAsync()", StringComparison.Ordinal);
        var markerEnd = source.IndexOf("private void SendMarkerEditAvailabilityToOsc()", markerStart, StringComparison.Ordinal);
        Assert.True(markerStart >= 0 && markerEnd > markerStart);
        var marker = source[markerStart..markerEnd];
        Assert.Contains("var snapshot = CapturePlaybackUiSnapshot();", marker, StringComparison.Ordinal);
        Assert.Contains("RestorePlaybackUiSnapshot(snapshot, restorePlayerInput);", marker, StringComparison.Ordinal);
        Assert.DoesNotContain("_videoWindow?.Hide()", marker, StringComparison.Ordinal);
        Assert.Contains("PlaybackDialogHost.ShowAsync", marker, StringComparison.Ordinal);

        var host = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlaybackDialogHost.cs"));
        Assert.Contains("SetWindowLongPtrW(hostHwnd, GwlpHwndParent, videoHwnd);", host, StringComparison.Ordinal);
        Assert.Contains("presenter.SetBorderAndTitleBar(false, false);", host, StringComparison.Ordinal);
        Assert.Contains("SetForegroundWindow(hostHwnd);", host, StringComparison.Ordinal);
        Assert.Contains("public static async Task<bool> ShowAsync(", host, StringComparison.Ordinal);
        Assert.Contains("restorePlayerInput = GetForegroundWindow() == hostHwnd;", host, StringComparison.Ordinal);
        Assert.Contains("_videoWindow?.EnterFullscreen(activate: activatePlaybackSurface);", source, StringComparison.Ordinal);
        Assert.Contains("if (restorePlayerInput)", host, StringComparison.Ordinal);
        Assert.Contains("SetForegroundWindow(videoHwnd);", host, StringComparison.Ordinal);
        Assert.Contains("CoverPlaybackSurface(videoHwnd, hostHwnd);", host, StringComparison.Ordinal);
        Assert.Contains("ColorHelper.FromArgb(178, 0, 0, 0)", host, StringComparison.Ordinal);

        var aiDialogXaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Controls",
            "SubtitleAiDialog.xaml"));
        var aiDialogCode = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Controls",
            "SubtitleAiDialog.xaml.cs"));
        Assert.Contains("MaxWidth=\"440\"", aiDialogXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"From subtitles\"", aiDialogXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"From audio\"", aiDialogXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ErrorPanel\"", aiDialogXaml, StringComparison.Ordinal);
        Assert.Contains("await _submitAsync(selection);", aiDialogCode, StringComparison.Ordinal);
        Assert.Contains("ErrorPanel.Visibility = Visibility.Visible;", aiDialogCode, StringComparison.Ordinal);
    }

    [Fact]
    public void EpisodeHudUsesTheCurrentWebUiTitleHierarchy()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.Contains("Title = watchDetail.SeriesTitle ?? watchDetail.Title;", source, StringComparison.Ordinal);
        Assert.Contains("$\"S{watchDetail.SeasonNumber.Value} · E{watchDetail.EpisodeNumber.Value}\"", source, StringComparison.Ordinal);
        Assert.Contains(": $\" \\u2014 {watchDetail.Title}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SubtitleAiEntryRequiresACompatibleCurrentMediaSource()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));
        var overlay = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SiloPlayer",
            "Controls",
            "PlayerOverlay.xaml.cs"));

        Assert.Contains("session?.SubtitleUrls.Any(IsTranslatableSubtitleSource) == true", service);
        Assert.Contains("currentVersion?.AudioTracks?.Count > 0", service);
        Assert.Contains(".Where(IsTranslatableSubtitleSource)", overlay);
        Assert.Contains("\"pgs\" or \"hdmv_pgs_subtitle\" or \"sup\"", service);
        Assert.Contains("\"srt\" or \"subrip\" or \"vtt\" or \"webvtt\"", overlay);
    }

    [Fact]
    public void LateProgressAndRewatchProgressAlwaysRemainResumePoints()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var methodStart = service.IndexOf("private double DetermineStartPosition", StringComparison.Ordinal);
        var methodEnd = service.IndexOf("private sealed record PreparedPlaybackTransport", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);

        var method = service[methodStart..methodEnd];
        Assert.Contains("!fromStart && watchDetail.UserData?.PositionSeconds > 0", method, StringComparison.Ordinal);
        Assert.DoesNotContain("Played != true", method, StringComparison.Ordinal);
        Assert.Contains("any nonzero position is an active resume point", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ExhaustedRecoveryStaysInThePlayerUntilTheViewerExplicitlyExits()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var osc = File.ReadAllText(Path.Combine(root, "libs", "mpv", "scripts", "silo-osc.lua"));

        var recoveryStart = service.IndexOf("private async Task RecoverInterruptedStreamAsync", StringComparison.Ordinal);
        var recoveryEnd = service.IndexOf("private double _resumePosition", recoveryStart, StringComparison.Ordinal);
        Assert.True(recoveryStart >= 0 && recoveryEnd > recoveryStart);
        var recovery = service[recoveryStart..recoveryEnd];

        Assert.Contains("EnterPlaybackTerminalState", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = CloseAsync()", recovery, StringComparison.Ordinal);
        Assert.Contains("osc-show-playback-failure", service, StringComparison.Ordinal);
        Assert.Contains("osc-clear-playback-failure", service, StringComparison.Ordinal);
        Assert.Contains("case \"silo-playback-retry\"", service, StringComparison.Ordinal);
        Assert.Contains("silo-playback-retry", osc, StringComparison.Ordinal);
        Assert.Contains("osc-show-playback-failure", osc, StringComparison.Ordinal);
    }

    [Fact]
    public void RealtimePlanInvalidationUsesTheProtocolV3RecoveryPath()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("case \"plan_invalidated\"", source, StringComparison.Ordinal);
        Assert.Contains("PlaybackPlanInvalidation.TryCreate", source, StringComparison.Ordinal);
        Assert.Contains("ReplanInvalidatedPlanAsync", source, StringComparison.Ordinal);
        Assert.Contains("HandlePlanInvalidationAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLoadedSuccessorRestoresOscAndReconcilesActualFullscreenState()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var handlerStart = source.IndexOf("_mpvFileLoadedHandler = () =>", StringComparison.Ordinal);
        var handlerEnd = source.IndexOf("_mpv.FileLoaded += _mpvFileLoadedHandler", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var handler = source[handlerStart..handlerEnd];

        Assert.Contains("ReconcilePlaybackSurfaceStateAfterLoad", handler, StringComparison.Ordinal);
        var reconcileStart = source.IndexOf("private void ReconcilePlaybackSurfaceStateAfterLoad", StringComparison.Ordinal);
        var reconcileEnd = source.IndexOf("private void HandleMpvPlaybackError", reconcileStart, StringComparison.Ordinal);
        Assert.True(reconcileStart >= 0 && reconcileEnd > reconcileStart);
        var reconcile = source[reconcileStart..reconcileEnd];
        Assert.Contains("osc-set-visibility", reconcile, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFullscreenState", reconcile, StringComparison.Ordinal);
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
