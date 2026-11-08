namespace SiloPlayer.Tests;

public sealed class PlayerServiceSourceTests
{
    [Fact]
    public void AudioAndQualityControlsCaptureTheirOwnerBeforeDrainingTransportWork()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        foreach (var signature in new[] { "private async Task SwitchAudioTrackCoreAsync(int trackIndex", "private async Task SwitchQualityTierCoreAsync(string tierId," })
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing owner-scoped control: {signature}");
            var drain = source.IndexOf("await CancelAndDrainTransportRestartsAsync(ct);", start, StringComparison.Ordinal);
            var capturedScope = source.IndexOf("var authority = _activeAuthority;", start, StringComparison.Ordinal);
            var capturedManager = source.IndexOf("var manager = _playbackManager;", start, StringComparison.Ordinal);
            Assert.True(capturedScope > start && capturedManager > start && capturedScope < drain && capturedManager < drain);
            Assert.Contains("ReferenceEquals(authority, _activeAuthority)", source[capturedScope..drain]);
            Assert.Contains("ReferenceEquals(manager, _playbackManager)", source[capturedScope..drain]);
            Assert.StartsWith("\n        if (!CurrentSwitch()", source[(drain + "await CancelAndDrainTransportRestartsAsync(ct);".Length)..].Replace("\r", ""));
        }
    }

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
    public void OscSubtitleSelectionUsesTheUiDispatcher()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("case \"silo-subtitle-select\":", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("case \"silo-subtitle-search\":", start, StringComparison.Ordinal);
        Assert.True(end > start);
        Assert.Contains("dispatch.TryEnqueue(() => _ = SelectSubtitleByServerIndexAsync(subIdx));", source[start..end]);
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

        Assert.Contains("public void PrefetchWatchDetail(string? contentId,", service, StringComparison.Ordinal);
        Assert.Contains("public async Task<WatchDetailResponse> GetOrFetchWatchDetailAsync", service, StringComparison.Ordinal);
        Assert.Contains("await GetOrFetchWatchDetailCoreAsync(", service, StringComparison.Ordinal);
        Assert.Contains("consumePrefetch: true", service, StringComparison.Ordinal);
        Assert.Contains("PrefetchWatchDetailCore(contentId, _activeAuthority, libraryId, fileId);", service, StringComparison.Ordinal);
        Assert.Contains("_playbackManager.UseWatchDetail(watchDetail);", service, StringComparison.Ordinal);
        Assert.Contains("public WatchDetailResponse UseWatchDetail", manager, StringComparison.Ordinal);
        Assert.Contains("QueuePlaybackPrefetch();", card, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(140, ct);", card, StringComparison.Ordinal);
        Assert.Contains("QueuePlaybackPrefetch();", posterCard, StringComparison.Ordinal);
        Assert.Contains("QueuePlaybackPrefetch();", libraryCard, StringComparison.Ordinal);
        Assert.Contains(".PrefetchWatchDetail(item.ContentId", hero, StringComparison.Ordinal);
        Assert.Contains("GetOrFetchWatchDetailAsync(contentId", detail, StringComparison.Ordinal);
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
    public void HlsAndRemuxEmbeddedSubtitlesLoadTheCompleteSidecarWhileDirectPlayStaysNative()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));

        Assert.Contains("CanUseNativeEmbeddedSubtitleTrack()", source, StringComparison.Ordinal);
        Assert.Contains("TransportKind == PlaybackTransportKind.DirectProgressive", source, StringComparison.Ordinal);
        Assert.Contains("SelectSubtitleSidecar(track, CurrentSelection)", source, StringComparison.Ordinal);
        // The server windows embedded text extraction only for an explicit
        // position/duration, and nothing slides a window during playback
        // (the XAML overlay tick that once did is never activated). A windowed
        // sidecar therefore lost every cue ~10 minutes after selection.
        // The complete track is downloaded first; a failed prefetch must not
        // repeat the same remote request synchronously in sub-add.
        Assert.Contains("if (!_prefetchedSubtitleFiles.TryGetValue(track.Index, out var local)) return false;", source, StringComparison.Ordinal);
        Assert.Contains("source = local;", source, StringComparison.Ordinal);
        var sidecar = source[source.IndexOf("private bool SelectSubtitleSidecar(", StringComparison.Ordinal)..];
        sidecar = sidecar[..sidecar.IndexOf("private long CurrentSubtitleSidecarGeneration()", StringComparison.Ordinal)];
        var add = sidecar.IndexOf("mpv.AddSubtitle(source, label, track.Language, select: false)", StringComparison.Ordinal);
        var ownerCheck = sidecar.IndexOf("if (!currentSelection() || !added || newIds.Length != 1)", add, StringComparison.Ordinal);
        var retain = sidecar.IndexOf("var retainedSid = (int)Math.Round(mpv.GetPropertyDouble(\"sid\"));", ownerCheck, StringComparison.Ordinal);
        var cleanup = sidecar.IndexOf("foreach (var addedSid in newIds) mpv.RemoveSubtitle(addedSid);", ownerCheck, StringComparison.Ordinal);
        var restore = sidecar.IndexOf("mpv.SetSubtitleTrack(retainedSid);", cleanup, StringComparison.Ordinal);
        var select = sidecar.IndexOf("mpv.SetSubtitleTrack(sid);", restore, StringComparison.Ordinal);
        Assert.True(add >= 0 && ownerCheck > add && retain > ownerCheck && cleanup > retain && restore > cleanup && select > restore,
            "A complete sidecar add must verify its owner and actual new native ID, removing every retired addition while preserving the latest SID.");
        Assert.Contains("fileLoadGeneration == Volatile.Read(ref _fileLoadGeneration)", sidecar[ownerCheck..retain]);
        Assert.DoesNotContain("if (added && newIds.Length == 1)", sidecar[ownerCheck..select]);
        Assert.DoesNotContain("if (_activeSubtitleServerIndex < 0)", sidecar[cleanup..select]);
        Assert.Contains("if (!File.Exists(source)) return false;", sidecar);
        Assert.DoesNotContain("? local : pair.Value.FullUrl", sidecar);
        Assert.DoesNotContain("AppendPositionDuration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("&duration=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TickSubtitleWindows", source, StringComparison.Ordinal);
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
        Assert.DoesNotContain("sourceInventory.IndexOf(requested)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementAtOrDefault(sourceOrdinal)", method, StringComparison.Ordinal);
        Assert.Contains("string.Equals(track.Source, requestedSource", method, StringComparison.Ordinal);
        Assert.Contains("track.StreamIndex == streamIndex", method, StringComparison.Ordinal);
        Assert.Contains("identities.Length == 1 && !identities[0].Unplayable", method, StringComparison.Ordinal);
        Assert.Contains("if (matches.Length == 1) selectedTrack = matches[0];", method, StringComparison.Ordinal);
        Assert.Contains("track.Forced == (requested.Forced == true)", method, StringComparison.Ordinal);
        Assert.Contains("track.HearingImpaired == (requested.HearingImpaired == true)", method, StringComparison.Ordinal);
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
        var resumeStart = source.IndexOf("private void ResumeAfterLiveSubtitleBuffer(long jobId)", StringComparison.Ordinal);
        var resumeEnd = source.IndexOf("private bool TryRetainLiveSubtitleTranslationLocked", resumeStart, StringComparison.Ordinal);
        Assert.True(resumeStart >= 0 && resumeEnd > resumeStart);
        var resume = source[resumeStart..resumeEnd];
        Assert.Contains("if (_resumeAfterLiveSubtitleBuffer)", resume);
        Assert.Contains("if (_mpv?.IsPaused == true) _mpv.Play();", resume);
        Assert.Contains("_resumeAfterLiveSubtitleBuffer = false;", resume);
        Assert.Contains("public void ReloadSubtitle(int sid)", mpv);

        var refreshStart = source.IndexOf("public async Task RefreshSubtitlesAfterAiAsync", StringComparison.Ordinal);
        var refreshEnd = source.IndexOf("private void WriteLiveSubtitleFileLocked", refreshStart, StringComparison.Ordinal);
        Assert.True(refreshStart >= 0 && refreshEnd > refreshStart);
        Assert.DoesNotContain("SwitchVersionAsync", source[refreshStart..refreshEnd]);
        Assert.Contains(
            "await TrySetSubtitleTrackAndPersistForOwnerAsync(",
            source[refreshStart..refreshEnd],
            StringComparison.Ordinal);
        Assert.Contains("preferred.Index + 1, preferred.Language, preferred, currentOwner)", source[refreshStart..refreshEnd]);
        Assert.Contains("=> await TrySetSubtitleTrackAndPersistForOwnerAsync(mpvTrackIndex, language, track, null);", source);
        var refusalGuard = source.IndexOf("if (!selected || !Current()) return;", refreshStart, StringComparison.Ordinal);
        var committedResume = source.IndexOf("ResumeAfterLiveSubtitleBuffer(liveJobId)", refusalGuard, StringComparison.Ordinal);
        var cleanup = source.IndexOf("ClearLiveSubtitleTranslationLocked(restorePreviousSubtitle: false);", committedResume, StringComparison.Ordinal);
        Assert.True(refusalGuard >= refreshStart && committedResume > refusalGuard && cleanup > committedResume);
        Assert.Contains("_executedSubtitleTrack?.Id != preferred.Id", source[refusalGuard..committedResume]);
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
        Assert.Contains("PrefetchWatchDetailCore(NextEpisodeContentId, _activeAuthority, ActiveLibraryId);", source);
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
        Assert.Contains("RecoverInterruptedStreamAsync(mediaPosition, directTransport ? \"direct-transport-error\" : \"file-load-error\")", method);
        Assert.Contains("if (attempt == 1)", method);
        Assert.Contains("EnterPlaybackTerminalState", method);
        Assert.DoesNotContain("CloseAsync()", method);
        Assert.DoesNotContain("SetState(PlayerState.Idle)", method);
        Assert.DoesNotContain("_videoWindow?.Hide()", method);
    }

    [Fact]
    public void QualitySwitchFailureKeepsTheExistingPlaybackSession()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var methodStart = source.IndexOf("private async Task SwitchQualityTierCoreAsync(string tierId,", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodEnd = source.IndexOf("private async Task ShowSubtitleSearchDialogAsync", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart);

        var method = source[methodStart..methodEnd];
        Assert.Contains("ShowNotice(title, detail, \"error\");", method);
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
        Assert.Contains("var transportMutationAttempted = false;", source, StringComparison.Ordinal);
        Assert.Contains("RunStreamRecoveryAsync(CurrentMediaPosition, \"subtitle-switch-failed\", manager, PauseIntent(), ct)", source, StringComparison.Ordinal);
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

        var methodStart = source.IndexOf("private async Task<bool> SelectSubtitleByServerIndexAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private bool SelectSubtitleTrack", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);

        var method = source[methodStart..methodEnd];
        Assert.Contains("TrySetSubtitleTrackAndPersistAsync(serverTrackIndex < 0 ? 0 : 1, track?.Language, track)", method);
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
    public void PlaybackWebSocketReconnectsWithFreshTicketsUnderCapturedAuthority()
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
        Assert.Contains("while (!ct.IsCancellationRequested && _apiClient.IsCurrentContext(_authority))", websocket);
        Assert.Contains("CreateControlTicketAsync(_sessionId, ct)", websocket);
        Assert.Contains("ws.Options.AddSubProtocol(\"silo.ticket.\" + ticket.Ticket)", websocket);
        Assert.DoesNotContain("AppendToken", websocket);
        Assert.Contains("_seenCommandIds", websocket);
        Assert.Contains("new PlaybackWebSocket(_apiClient, _playbackApi, sessionId)", playerService);
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

        var introStart = source.IndexOf("x:Name=\"IntroSkipModeCombo\"", StringComparison.Ordinal);
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

        Assert.Contains("retiringSessionTask = retiringManager.TryRetireSessionAsync", method);
        Assert.Contains("_ = FinishClosingSessionAsync(retiringManager, retiringSessionTask)", method);
        Assert.Contains("PlaybackFailureDescription.IsAccountLimitReason(api.ErrorCode, includeTranscode: retiringTranscode)", method);
        Assert.Contains("await retiringSessionTask.WaitAsync(requestToken)", method);
        var startupHandoffEnd = method.IndexOf("ErrorMessage = null;", StringComparison.Ordinal);
        Assert.True(startupHandoffEnd > 0);
        Assert.DoesNotContain(
            "await _playbackManager.StopSessionAsync();",
            method[..startupHandoffEnd]);
    }

    [Fact]
    public void AccountLimitDuringRetirementRetriesOnceOnlyAfterStopAndOwnerCheck()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task PlayCoreAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private static (string Title, string Detail) DescribePlaybackError", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];

        var transcodeSnapshot = method.IndexOf("var retiringTranscode = _playbackManager?.CurrentSession?.PlayMethod == \"transcode\";", StringComparison.Ordinal);
        var stop = method.IndexOf("retiringSessionTask = retiringManager.TryRetireSessionAsync", StringComparison.Ordinal);
        Assert.True(transcodeSnapshot >= 0 && stop > transcodeSnapshot);

        var firstStart = method.IndexOf("session = await _playbackManager.StartSessionAsync(", StringComparison.Ordinal);
        var guard = method.IndexOf("catch (Exception ex) when (", firstStart, StringComparison.Ordinal);
        var wait = method.IndexOf("var retired = await retiringSessionTask.WaitAsync(requestToken);", guard, StringComparison.Ordinal);
        var ownerCheck = method.IndexOf("EnsureCurrentOwner();", wait, StringComparison.Ordinal);
        var retry = method.IndexOf("session = await _playbackManager.StartSessionAsync(", ownerCheck, StringComparison.Ordinal);
        Assert.True(firstStart >= 0 && guard > firstStart && wait > guard && ownerCheck > wait && retry > ownerCheck);

        var filter = method[guard..wait];
        Assert.DoesNotContain("retiringSessionTask.IsCompleted", filter);
        var confirmed = method.IndexOf("if (!retired)", ownerCheck, StringComparison.Ordinal);
        Assert.True(confirmed > ownerCheck && retry > confirmed);
        Assert.Contains("throw;", method[confirmed..retry]);
        Assert.Contains("ex is ApiException api && api.StatusCode == 429", filter);
        Assert.Contains("PlaybackFailureDescription.IsAccountLimitReason(api.ErrorCode, includeTranscode: retiringTranscode)", filter);
        Assert.Contains("ex is PlaybackPlanTerminalException terminal && terminal.Retryable", filter);
        Assert.Contains("PlaybackFailureDescription.IsAccountLimitReason(terminal.Reason, includeTranscode: retiringTranscode)", filter);
        Assert.Equal(2, method.Split("session = await _playbackManager.StartSessionAsync(", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("while (", method[firstStart..retry]);
        Assert.DoesNotContain("for (", method[firstStart..retry]);
    }

    [Fact]
    public void AudioSwitchReplansTheV3SessionAndNeverPatchesAudioOnTheServer()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));
        var methodStart = source.IndexOf("private async Task SwitchAudioTrackCoreAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("// ── Subtitles", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        // Every session is protocol v3: audio changes go through a replan.
        // The pre-v3 PATCH /audio path (and its recovery) no longer exists.
        Assert.Contains("manager.ReplanAudioAsync(trackIndex, currentPos, ct)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ChangeAudioTrackAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyAudioChange", method, StringComparison.Ordinal);
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
        var captureStart = source.IndexOf("private bool CaptureUserPausedState()", StringComparison.Ordinal);
        var captureEnd = source.IndexOf("// ── State", captureStart, StringComparison.Ordinal);
        Assert.True(captureStart >= 0 && captureEnd > captureStart);
        var capture = source[captureStart..captureEnd];
        Assert.Contains("lock (_liveSubtitleLock)", capture, StringComparison.Ordinal);
        Assert.Contains("if (_liveSubtitleResumeCts != null && _resumeAfterLiveSubtitleBuffer) return false;", capture, StringComparison.Ordinal);
        Assert.Contains("var paused = mpv?.IsPaused ?? IsPaused;", capture, StringComparison.Ordinal);
        Assert.Contains("mpv.TryGetPropertyFlag(\"pause\", out var nativePaused)) paused = nativePaused;", capture, StringComparison.Ordinal);
        Assert.Contains("return paused && mpv?.IsBufferingForCache != true;", capture, StringComparison.Ordinal);

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

        // v3 plans carry the transport-local start and timeline offset.
        Assert.Contains(
            ": Math.Max(0, session.PlayerStartSeconds),",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "TimelineOffsetSeconds: Math.Max(0, session.TimelineOffsetSeconds),",
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
        // The v3 plan reports the active quality; the client does not pick one.
        Assert.Contains("_activeQualityTier = session.ActiveQuality;", source, StringComparison.Ordinal);
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

        Assert.Contains("private sealed record PlaybackUiSnapshot(PlaybackAuthorityScope Authority, PlaybackManager? Manager,", source, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(snapshot.Authority, _activeAuthority)", source, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(snapshot.Manager, _playbackManager)", source, StringComparison.Ordinal);
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
        Assert.Contains("restorePlayerInput = GetForegroundWindow() == hostHwnd", host, StringComparison.Ordinal);
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
        Assert.Contains("IsTranslatableSubtitleSource(entry.Track)", overlay);
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

        var recoveryStart = service.IndexOf("private async Task RunStreamRecoveryAsync", StringComparison.Ordinal);
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
    public void TerminalRetryPreservesTheOriginalRecoveryTrigger()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var retryStart = source.IndexOf(
            "private async Task RetryPlaybackFromTerminalAsync()",
            StringComparison.Ordinal);
        var retryEnd = source.IndexOf("private double _resumePosition", retryStart, StringComparison.Ordinal);
        Assert.True(retryStart >= 0 && retryEnd > retryStart);
        var retry = source[retryStart..retryEnd];

        Assert.Contains(
            "RecoverInterruptedStreamAsync(terminal.Position, terminal.Trigger, terminal.RestartSession)",
            retry,
            StringComparison.Ordinal);
        Assert.DoesNotContain("user-retry:", retry, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalRetryCarriesRestartIntentWhenRetirementRemovedTheCurrentSession()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task RecoverInterruptedStreamCoreAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private void EnterPlaybackTerminalState", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var recovery = source[start..end];

        Assert.Contains("var session = manager?.CurrentSession ?? restartSession;", recovery);
        Assert.Contains("throw new PlaybackPlanTerminalException(\"session_expired\"", recovery);
        Assert.Contains("retryable: false", recovery);
        Assert.DoesNotContain("Stream recovery skipped", recovery);
        Assert.Equal(2, recovery.Split("manager.CurrentSession != null && !manager.SessionRetirementPending", StringSplitOptions.None).Length - 1);
        Assert.Contains("manager.StartReplacementSessionAsync(", recovery);
        Assert.Contains("audioTrackIndex: audioTrackIndex", recovery);
        Assert.Contains("qualityPreference: manager.QualityPreference", recovery);
        Assert.Contains("EnterPlaybackTerminalState(ex, resumePosition, reason, restorePaused, manager.CurrentSession ?? session)", recovery);

        var terminalEnd = source.IndexOf("private void ShowStartupPlaybackFailure", end, StringComparison.Ordinal);
        var terminal = source[end..terminalEnd];
        Assert.Contains("_playbackManager?.CurrentSession ?? restartSession", terminal);
        Assert.Contains("_mpv?.Pause();", terminal);
        Assert.Contains("StopPlaybackStallWatchdog();", terminal);
        Assert.Contains("RecoverInterruptedStreamAsync(terminal.Position, terminal.Trigger, terminal.RestartSession)", source);
        Assert.Contains("queuedOwner != null || restartSession != null", source);
        Assert.Contains("!ReferenceEquals(retryTerminal, _playbackTerminalState)", source);
        Assert.Contains("description = description with { CanRetry = false };", terminal);

        var failureStart = source.IndexOf("private void HandleMpvFailure", StringComparison.Ordinal);
        var failureEnd = source.IndexOf("private async Task SwitchVersionCoreAsync", failureStart, StringComparison.Ordinal);
        Assert.Contains("_closing || State == PlayerState.Idle || _playbackTerminalState != null", source[failureStart..failureEnd]);
        var endStart = source.IndexOf("private void HandleMpvEndSignal", StringComparison.Ordinal);
        var endGuard = source.IndexOf("var pos = CurrentMediaPosition;", endStart, StringComparison.Ordinal);
        Assert.Contains("_playbackTerminalState != null", source[endStart..endGuard]);
    }

    [Fact]
    public void FailedVersionSwitchCannotContinueOnARetiredOrUnconfirmedSession()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task SwitchVersionCoreAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("public Task SwitchAudioTrackAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var method = source[start..end];
        Assert.Contains("var previousSession = manager.CurrentSession;", method);
        Assert.Contains("var survivingSession = manager.CurrentSession;", method);
        var retired = method.IndexOf("manager.CurrentSession == null || manager.SessionRetirementPending", StringComparison.Ordinal);
        Assert.True(retired >= 0);
        var terminal = method.IndexOf("EnterPlaybackTerminalState(ex, currentPos, \"version-switch-failed\", wasPaused, previousSession)", retired, StringComparison.Ordinal);
        Assert.True(terminal > retired);
        var accepted = method.IndexOf("else if ((replacementAccepted || !ReferenceEquals(manager.CurrentSession, survivingSession)) &&", terminal, StringComparison.Ordinal);
        Assert.True(accepted > terminal);
        var recovery = method.IndexOf("RecoverInterruptedStreamAsync(currentPos, \"version-switch-failed\")", accepted, StringComparison.Ordinal);
        Assert.True(recovery > accepted);
        var notice = method.IndexOf("ShowNotice(title, detail, \"error\")", recovery, StringComparison.Ordinal);
        Assert.True(notice > recovery);
    }

    [Fact]
    public void SeekAccountLimitsShowGuidanceWithoutFailingTheHealthyTransport()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task RestartTransportForSeekAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task ReportSeekProgressAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end].Replace("\r", "");
        var accountCatch = method.IndexOf("catch (Exception ex) when (", StringComparison.Ordinal);
        Assert.True(accountCatch >= 0);
        var genericCatch = method.IndexOf("catch (Exception ex)\n", accountCatch, StringComparison.Ordinal);
        Assert.True(genericCatch > accountCatch);
        var refusal = method[accountCatch..genericCatch];
        Assert.Contains("PlaybackFailureDescription.IsAccountLimitReason(terminal.Reason)", refusal);
        Assert.Contains("PlaybackFailureDescription.IsAccountLimitReason(api.ErrorCode)", refusal);
        Assert.Contains("generation == Volatile.Read(ref _seekRestartGeneration)", refusal);
        Assert.Contains("if (_idleRemuxResume.IsReopening)", refusal);
        Assert.Contains("EnterPlaybackTerminalState(ex, mediaPosition, \"idle-remux-resume\", _idleRemuxResume.DesiredPaused)", refusal);
        var healthy = refusal[refusal.IndexOf("else", StringComparison.Ordinal)..];
        Assert.Contains("SetSwitchingContent(false);", healthy);
        Assert.Contains("IsLoading = false;", healthy);
        Assert.Contains("HideLoadingOverlay(loadingOwner)", healthy);
        Assert.Contains("DescribePlaybackError(ex)", healthy);
        Assert.Contains("ShowNotice(title, detail, \"error\")", healthy);
        Assert.DoesNotContain("EnterPlaybackTerminalState", healthy);
        Assert.DoesNotContain(".Pause()", healthy);
        Assert.DoesNotContain(".Play()", healthy);
        Assert.DoesNotContain("StopSession", refusal);
        Assert.DoesNotContain("Seek failed", refusal);
        Assert.DoesNotContain("RecoverInterruptedStreamAsync", refusal);
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
        Assert.Contains("StartPlanInvalidationRecovery(invalidation);", source, StringComparison.Ordinal);
        Assert.Contains("return Complete(new CommandResult());", source, StringComparison.Ordinal);
        Assert.Contains("private async Task ObservePlanInvalidationRecoveryAsync", source, StringComparison.Ordinal);
        Assert.Contains("await _planInvalidationGate.WaitAsync", source, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "return HandlePlanInvalidationAsync(invalidation);",
            source,
            StringComparison.Ordinal);
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
        var dispatcherIndex = reconcile.IndexOf(
            "var dispatcher = App.MainWindowInstance?.DispatcherQueue;",
            StringComparison.Ordinal);
        var callbackIndex = reconcile.IndexOf("void Reconcile()", StringComparison.Ordinal);
        var oscIndex = reconcile.IndexOf("osc-set-visibility", StringComparison.Ordinal);
        var fullscreenIndex = reconcile.IndexOf("SynchronizeFullscreenState", StringComparison.Ordinal);
        Assert.True(dispatcherIndex >= 0 && callbackIndex > dispatcherIndex);
        Assert.True(oscIndex > callbackIndex);
        Assert.True(fullscreenIndex > callbackIndex);
        Assert.DoesNotContain("var actualFullscreen", reconcile, StringComparison.Ordinal);
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

    [Fact]
    public void SiloCastSubtitleRoutesReuseOneNativeAndUrlSnapshotPerCapture()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.SiloCast.cs"));
        var start = source.IndexOf("public JsonObject CaptureSiloCastState()", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task ApplySiloCastControlAsync(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var capture = source[start..end];
        var loopStart = capture.IndexOf("foreach (var t in advertisedSubtitles)", StringComparison.Ordinal);
        Assert.True(loopStart > 0);
        Assert.Equal(1, capture[..loopStart].Split("CaptureSubtitleSelectionRoutes(subtitleInventory)", StringSplitOptions.None).Length - 1);
        Assert.Contains("HasSubtitleSelectionRoute(t, routes)", capture[..loopStart]);
        Assert.DoesNotContain("CaptureSubtitleSelectionRoutes(", capture[loopStart..]);

        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var snapshotStart = service.IndexOf("private SubtitleRouteSnapshot CaptureSubtitleSelectionRoutes(", StringComparison.Ordinal);
        var snapshotEnd = service.IndexOf("internal sealed record SubtitleMenuSnapshot(", snapshotStart, StringComparison.Ordinal);
        Assert.True(snapshotStart >= 0 && snapshotEnd > snapshotStart);
        var snapshot = service[snapshotStart..snapshotEnd];
        foreach (var read in new[] { "GetEmbeddedSubtitleTracks()", "HasFfmpegTrackIndices()", "HasContainerTrackIds()", "GetSubtitleUrls(inventory)" })
        {
            Assert.Equal(1, snapshot.Split(read, StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain(read, capture);
        }
        Assert.Contains("url.Scheme is \"http\" or \"https\"", snapshot);
        Assert.Contains(".Select(pair => pair.Track.Index).ToHashSet()", snapshot);
        var resolverStart = service.IndexOf("private static int ResolveNativeEmbeddedSidFromSnapshot(", StringComparison.Ordinal);
        var resolverEnd = service.IndexOf("private void SendTitleToOsc()", resolverStart, StringComparison.Ordinal);
        Assert.True(resolverStart >= 0 && resolverEnd > resolverStart);
        var resolver = service[resolverStart..resolverEnd];
        Assert.Contains("NativeSubtitleSelection.Resolve(", resolver);
        Assert.Contains("routes.EmbeddedTracks, routes.TrustworthyStreamIndex, routes.TrustworthyContainerTrackId", resolver);
        Assert.DoesNotContain("_mpv.", resolver);
        Assert.Contains("if (sidecarIndices != null) return sidecarIndices.Contains(track.Index);", service);
    }

    [Fact]
    public void ManagedSubtitleUrlFallbackHasADeadlineAndNativeAddsRetainLateReplyCleanup()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("async Task<SubtitlePrefetchOutcome> ReadUrlFallbackAsync()", StringComparison.Ordinal);
        Assert.True(start >= 0, "Subtitle URL fallback method was not found.");
        var end = source.IndexOf("var read = await SubtitleSidecarReader.ReadAsync(", start, StringComparison.Ordinal);
        Assert.True(end > start, "Subtitle URL fallback method boundary was not found.");
        var fallback = source[start..end];
        Assert.Contains("private static readonly TimeSpan SubtitleUrlFallbackTimeout = TimeSpan.FromMinutes(5);", source);
        Assert.Contains("private TimeProvider _subtitleUrlFallbackTimeProvider = TimeProvider.System;", source);
        Assert.Contains("using var fallbackDeadline = new CancellationTokenSource(SubtitleUrlFallbackTimeout, _subtitleUrlFallbackTimeProvider);", fallback);
        Assert.Contains("using var fallbackBudget = CancellationTokenSource.CreateLinkedTokenSource(bounded.Token, fallbackDeadline.Token);", fallback);
        Assert.DoesNotContain("CancelAfter(TimeSpan.FromSeconds(8))", fallback);
        Assert.Contains("ShowNotice(\"Loading subtitle\"", fallback);
        Assert.Contains("choose another subtitle to cancel this download", fallback);
        // Slow extraction must use managed HTTP: mpv's per-file socket timeout
        // can expire before headers arrive. Stream bounded bytes to disk, then
        // cache only a complete file that still belongs to this read owner.
        var download = fallback.IndexOf("await SubtitleSidecarDownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, fallbackBudget.Token)", StringComparison.Ordinal);
        Assert.True(download >= 0, "Fallback must await headers with its own cancellation budget.");
        var busy = fallback.IndexOf("if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable) return SubtitlePrefetchOutcome.Busy;", download, StringComparison.Ordinal);
        var success = fallback.IndexOf("response.EnsureSuccessStatusCode();", StringComparison.Ordinal);
        var declaredCap = fallback.IndexOf("if (response.Content.Headers.ContentLength > maxBytes) return SubtitlePrefetchOutcome.TooLarge;", StringComparison.Ordinal);
        var create = fallback.IndexOf("new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,", StringComparison.Ordinal);
        Assert.True(busy > download && success > busy && declaredCap > success && create > declaredCap);
        Assert.Contains("SubtitleSidecarFile.DownloadExtension(response.Content.Headers.ContentType?.MediaType, url, track.Codec)", fallback);
        Assert.Contains("var maxBytes = SubtitleSidecarFile.DownloadMaxBytes(extension);", fallback);
        Assert.Contains("await response.Content.ReadAsStreamAsync(fallbackBudget.Token)", fallback);
        Assert.Contains("FileOptions.Asynchronous | FileOptions.SequentialScan", fallback);
        var bodyRead = fallback.IndexOf("await body.ReadAsync(chunk, fallbackBudget.Token)", StringComparison.Ordinal);
        var streamedCap = fallback.IndexOf("if (total + read > maxBytes) return SubtitlePrefetchOutcome.TooLarge;", StringComparison.Ordinal);
        var write = fallback.IndexOf("await file.WriteAsync(chunk.AsMemory(0, read), fallbackBudget.Token);", StringComparison.Ordinal);
        Assert.True(bodyRead > create && streamedCap > bodyRead && write > streamedCap);
        var ownerCheck = fallback.IndexOf("if (!CurrentOwner()) return SubtitlePrefetchOutcome.Retired;", write, StringComparison.Ordinal);
        var deadlineCheck = fallback.IndexOf("fallbackBudget.Token.ThrowIfCancellationRequested();", StringComparison.Ordinal);
        var emptyCheck = fallback.IndexOf("if (total == 0) throw new InvalidDataException(\"Empty subtitle sidecar.\");", StringComparison.Ordinal);
        var syncLock = fallback.IndexOf("lock (_subtitleSyncGate)", StringComparison.Ordinal);
        var fallbackLock = fallback.IndexOf("lock (_subtitleTrackStateLock)", download, StringComparison.Ordinal);
        var verify = fallback.IndexOf("if (generation != _subtitleSidecarGeneration || !CurrentOwner()) return SubtitlePrefetchOutcome.Retired;", StringComparison.Ordinal);
        var cache = fallback.IndexOf("_prefetchedSubtitleFiles[track.Index] = path;", StringComparison.Ordinal);
        var register = fallback.IndexOf("_subtitleSyncFiles.Add(path);", StringComparison.Ordinal);
        Assert.True(ownerCheck > write && deadlineCheck > ownerCheck && emptyCheck > deadlineCheck &&
            syncLock > emptyCheck && fallbackLock > syncLock && verify > fallbackLock &&
            cache > verify && register > cache);
        Assert.True(fallback.IndexOf("path = null;", register, StringComparison.Ordinal) > register);
        Assert.Contains("if (!CurrentOwner()) return SubtitlePrefetchOutcome.Retired;", fallback[download..busy]);
        var readOwnerStart = source.LastIndexOf("bool CurrentOwner()", start, StringComparison.Ordinal);
        Assert.True(readOwnerStart >= 0, "Fallback must retain the captured read owner.");
        var readOwner = source[readOwnerStart..start];
        Assert.Contains("fileLoadGeneration == Volatile.Read(ref _fileLoadGeneration)", readOwner);
        Assert.Contains("ReferenceEquals(session, manager?.CurrentSession)", readOwner);
        Assert.Contains("authority.ApiClient.IsCurrentContext(context)", readOwner);
        Assert.Contains("ReferenceEquals(selection, Volatile.Read(ref _subtitleReadOperation)) && !selection.Token.IsCancellationRequested", readOwner);
        Assert.Contains("return CurrentOwner() ? SubtitlePrefetchOutcome.TimedOut : SubtitlePrefetchOutcome.Retired;", source);
        Assert.Contains("fallbackError is OperationCanceledException ? SubtitlePrefetchOutcome.TimedOut : SubtitlePrefetchOutcome.Error", source);
        Assert.Contains("if (path != null) try { File.Delete(path); } catch { }", source);
        Assert.DoesNotContain("mpv.AddSubtitle", fallback);
        Assert.DoesNotContain("mpv.SetSubtitleTrack", fallback);
        Assert.DoesNotContain("_prefetchedSubtitleSids[track.Index] =", fallback);
        Assert.DoesNotContain("_loadedExternalSubtitleSids[track.Index] =", fallback);

        // Native asynchronous adds still retain their late-reply cleanup even
        // though managed prefetch no longer delegates network reads to them.
        var native = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Player", "MpvPlayer.AsyncSubtitle.cs"));
        var publicStart = native.IndexOf("public async Task<int> AddSubtitleUnselectedAsync(", StringComparison.Ordinal);
        var laneStart = native.IndexOf("private async Task<(int Sid, long FileGeneration)> AddSubtitleInLaneAsync(", publicStart, StringComparison.Ordinal);
        Assert.True(publicStart >= 0 && laneStart > publicStart);
        var caller = native[publicStart..laneStart];
        Assert.Contains("pending.WaitAsync(ct)", caller);
        Assert.Contains("CleanupRetiredSubtitleAddAsync(pending)", caller);
        Assert.DoesNotContain("_asyncSubtitleAddLane.Release()", caller);
        Assert.Contains("finally { _asyncSubtitleAddLane.Release(); }", native[laneStart..]);
        Assert.Contains("await pending.ConfigureAwait(false)", native);
        Assert.Contains("sid > 0 && fileGeneration == _asyncSubtitleFileGeneration", native);
        Assert.Contains("Command(\"sub-remove\", sid.ToString", native);
        Assert.Contains("if (ct.IsCancellationRequested || fileRequest != _asyncSubtitleFileRequest || !added)", native);
    }

    [Fact]
    public void RemoteSidecarsAreDownloadedBeforeTheSynchronousSubAdd()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var prefetchStart = source.IndexOf("private async Task<SubtitlePrefetchOutcome> PrefetchSubtitleSidecarForOwnerAsync(", StringComparison.Ordinal);
        Assert.True(prefetchStart >= 0, "Subtitle prefetch method was not found.");
        var prefetchEnd = source.IndexOf("private int ResolveNativeEmbeddedSid(", prefetchStart, StringComparison.Ordinal);
        Assert.True(prefetchEnd > prefetchStart, "Subtitle prefetch method boundary was not found.");
        var prefetch = source[prefetchStart..prefetchEnd];
        // Retry busy text with the bounded shared reader before a local add.
        // Only non-busy failures/opaque formats may use the separately bounded
        // managed disk-streaming route, never a remote native sub-add.
        Assert.Contains("SubtitleSidecarReader.ReadAsync(SubtitleSidecarDownloadClient, url,", prefetch);
        Assert.Contains("HttpClient SubtitleSidecarDownloadClient = new() { Timeout = Timeout.InfiniteTimeSpan }", source);
        Assert.Contains("bounded.CancelAfter(TimeSpan.FromMinutes(10));", prefetch);
        Assert.DoesNotContain("CreateAuthenticatedRequest", prefetch);
        Assert.Contains("if (read.Outcome == SubtitleSidecarReadOutcome.Busy) return SubtitlePrefetchOutcome.Busy;", prefetch);
        Assert.Contains("return read.CanUseUrlFallback ? await ReadUrlFallbackAsync() : SubtitlePrefetchOutcome.Error;", prefetch);
        Assert.Contains("allowUrlFallback = !read.WasBusy;", prefetch);
        Assert.Contains("if (!allowUrlFallback) return SubtitlePrefetchOutcome.Error;", prefetch);
        Assert.Contains("await SubtitleSidecarDownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, fallbackBudget.Token)", prefetch);
        Assert.Contains("SubtitleSidecarFile.DownloadMaxBytes(extension)", prefetch);
        Assert.DoesNotContain("mpv.AddSubtitle(", prefetch);
        Assert.DoesNotContain("mpv.AddSubtitleUnselectedAsync(", prefetch);
        Assert.DoesNotContain("ReadAsByteArrayAsync", prefetch);
        // The fast text probe enforces its own buffering cap; the longer
        // fallback above streams to disk under its format-specific cap.
        var reader = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer.Core", "Services", "SubtitleSidecarReader.cs"));
        Assert.Contains("response.Content.Headers.ContentLength > SubtitleSidecarFile.MaxBytes", reader);
        var sizeCheck = reader.IndexOf("if (buffer.Length + read > SubtitleSidecarFile.MaxBytes) return Failure();", StringComparison.Ordinal);
        var bufferWrite = reader.IndexOf("buffer.Write(chunk, 0, read);", StringComparison.Ordinal);
        Assert.True(sizeCheck >= 0 && bufferWrite > sizeCheck,
            "Chunked text sidecars must enforce the cap before buffering each chunk.");
        Assert.DoesNotContain("ReadAsByteArrayAsync", reader);
        var write = prefetch.IndexOf("await File.WriteAllBytesAsync(path, read.Bytes!, bounded.Token);", StringComparison.Ordinal);
        Assert.True(write >= 0);
        var cacheLock = prefetch.IndexOf("lock (_subtitleTrackStateLock)", write, StringComparison.Ordinal);
        var cacheOwner = prefetch.IndexOf("if (generation != _subtitleSidecarGeneration || !CurrentOwner()) return SubtitlePrefetchOutcome.Retired;", cacheLock, StringComparison.Ordinal);
        var fileCache = prefetch.IndexOf("_prefetchedSubtitleFiles[track.Index] = path;", cacheOwner, StringComparison.Ordinal);
        Assert.True(cacheLock > write && cacheOwner > cacheLock && fileCache > cacheOwner);
        Assert.Contains("if (path != null) try { File.Delete(path); } catch { }", prefetch);
        // A timing-invalidated SID may remain applied while fresh cues load.
        // Readiness requires a non-stale applied SID, local bytes or a verified
        // unselected SID belonging to this exact native file.
        Assert.Contains("if ((_loadedExternalSubtitleSids.ContainsKey(track.Index) && !_staleExternalSubtitleIndices.Contains(track.Index)) ||\n                _prefetchedSubtitleFiles.ContainsKey(track.Index) ||\n                _prefetchedSubtitleSids.TryGetValue(track.Index, out var ready) &&\n                    ready.FileGeneration == fileLoadGeneration && mpv.GetSubtitleTrackIds(externalOnly: true).Contains(ready.Sid))\n                return SubtitlePrefetchOutcome.Ready;", prefetch.Replace("\r", ""));
        var sidecarStart = source.IndexOf("private bool SelectSubtitleSidecar(", StringComparison.Ordinal);
        Assert.True(sidecarStart >= 0 && sidecarStart < prefetchStart);
        var sidecar = source[sidecarStart..prefetchStart];
        Assert.Contains("if (!_prefetchedSubtitleFiles.TryGetValue(track.Index, out var local)) return false;", sidecar);
        Assert.Contains("if (!File.Exists(source)) return false;", sidecar);
        Assert.Contains("mpv.AddSubtitle(source, label, track.Language, select: false)", sidecar);
        Assert.DoesNotContain("AddSubtitle(pair.Value.FullUrl", sidecar);
        Assert.DoesNotContain("? local : pair.Value.FullUrl", sidecar);
        Assert.DoesNotContain("SubtitleSidecarFile.Extension(null, remote, track.Codec)", source);
        Assert.DoesNotContain("SubtitleSidecarFile.Extension(null, parsed, track.Codec)", prefetch);
        Assert.Contains("if (read.Outcome == SubtitleSidecarReadOutcome.Fallback)\n                return read.CanUseUrlFallback ? await ReadUrlFallbackAsync() : SubtitlePrefetchOutcome.Error;", prefetch.Replace("\r", ""));
        Assert.Contains("generation != _subtitleSidecarGeneration", prefetch);
        Assert.Contains("_subtitleSyncFiles.Add(path);", prefetch);
        Assert.Contains("authority.ApiClient.IsCurrentContext(context)", prefetch);
        // Same-session reloads delete and unregister this file's downloads.
        var load = source[source.IndexOf("private void LoadSubtitles()", StringComparison.Ordinal)..];
        load = load[..load.IndexOf("private static bool IsUnsupportedBitmapSubtitle(", StringComparison.Ordinal)];
        Assert.Contains("CancelSubtitleSelectionRead();", load);
        Assert.Contains("_subtitleSyncFiles.Remove(path);", load);
        Assert.Contains("File.Delete(path);", load);
        var sync = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.SubtitleSync.cs"));
        var stop = sync[sync.IndexOf("private void StopSubtitleSync()", StringComparison.Ordinal)..];
        stop = stop[..stop.IndexOf("private void EnsureSubtitleSync()", StringComparison.Ordinal)];
        Assert.Contains("CancelSubtitleSelectionRead();", stop);
        Assert.Contains("_subtitleSidecarGeneration++;", stop);

        var persist = source[source.IndexOf("private async Task<bool> TrySetSubtitleTrackAndPersistForOwnerAsync(", StringComparison.Ordinal)..];
        persist = persist[..persist.IndexOf("private bool MatchesSubtitlePreference(", StringComparison.Ordinal)];
        Assert.Contains("if (track != null && (!deferLocalSelection || manager?.CurrentSession?.IsSubtitleBurnedIn == true))", persist);
        var readiness = persist.IndexOf("await PrefetchSubtitleSidecarAsync(track);", StringComparison.Ordinal);
        var admit = persist.IndexOf("choice.Accepted = true;", readiness, StringComparison.Ordinal);
        var readOwner = persist.IndexOf("if (!CurrentReadChoice()) return false;", readiness, StringComparison.Ordinal);
        var readLoad = persist.IndexOf("readLoadGeneration != Volatile.Read(ref _loadedMediaInitGeneration)", readiness, StringComparison.Ordinal);
        Assert.True(readiness >= 0 && readOwner > readiness && readLoad > readOwner && admit > readLoad);
        // A subtitle_ready refresh can reindex the retained row while prefetch
        // is in flight; ownership follows that row, not its original ordinal.
        AssertPrefetchedThenCurrentBeforeSelection(persist, "_activeSubtitleServerIndex != track.Index");
        var persistSelect = persist.IndexOf("SelectSubtitleTrackForOwner(track,", StringComparison.Ordinal);
        var persistPrefetch = persist.LastIndexOf("await PrefetchSubtitleSidecarForOwnerAsync(track, CanActivate);", persistSelect, StringComparison.Ordinal);
        Assert.Contains("bool CanActivate() => CurrentChoice() && currentOwner?.HasCurrentSubtitleChoice != false;", persist);
        Assert.Contains("=> await PrefetchSubtitleSidecarForOwnerAsync(track, null);", source);
        // Restoration uses the state from just before the sub-add, not from before the download.
        Assert.InRange(persist.IndexOf("wasPaused = CaptureUserPausedState();", persistPrefetch, StringComparison.Ordinal), persistPrefetch, persistSelect);
        Assert.InRange(persist.IndexOf("position = CurrentMediaPosition;", persistPrefetch, StringComparison.Ordinal), persistPrefetch, persistSelect);
        // A superseding file load hands restoration to the reload; preferences
        // are completed only by the choice that actually executes.
        var loadChanged = persist.IndexOf("CurrentSubtitleSidecarGeneration() != sidecarGeneration", persistPrefetch, StringComparison.Ordinal);
        var handOff = persist.IndexOf("transportReloadScheduled = true;", loadChanged, StringComparison.Ordinal);
        Assert.InRange(handOff, loadChanged, persistSelect);
        Assert.DoesNotContain("return;", persist[loadChanged..persistSelect], StringComparison.Ordinal);
        Assert.True(persist.IndexOf("await CompleteExecutedSubtitleSelectionAsync(choice, track);", persistSelect, StringComparison.Ordinal) > persistSelect);
        var lifecycle = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.SubtitleSelection.cs"));
        Assert.Contains("Interlocked.CompareExchange(ref owner.PreferenceClaimed, 1, 0)", lifecycle);
        Assert.Contains("Volatile.Write(ref owner.PreferenceClaimed, 0)", lifecycle);
        Assert.Contains("ReferenceEquals(owner, Volatile.Read(ref _subtitleSelectionOperation))", lifecycle);
        Assert.Contains("SaveExecutedSubtitlePreferenceAsync(owner.PreferenceOwner, applied)", lifecycle);
        Assert.DoesNotContain("CreateLinkedTokenSource(owner.Token", lifecycle);
        Assert.Contains("await _subtitlePreferenceSaveGate.WaitAsync(owner.Token)", source);
        Assert.Contains("ProtocolVersion >= 3 ? track.NativeTitle : track.Label", source);
        Assert.DoesNotContain("using var subtitleChoice", persist);
        var byIndex = source[source.IndexOf("private async Task<bool> SelectSubtitleByServerIndexAsync(", StringComparison.Ordinal)..];
        var byIndexPrefetch = byIndex.IndexOf("await PrefetchSubtitleSidecarForOwnerAsync(track, CurrentSelection);", StringComparison.Ordinal);
        Assert.True(byIndexPrefetch >= 0, "Load-time selection must download for its current owner before native selection.");
        var byIndexSelect = byIndex.IndexOf("SelectSubtitleTrackForOwner(track, CurrentSelection)", byIndexPrefetch, StringComparison.Ordinal);
        // The stale-owner guard now also reconciles timing-only retirement.
        // It must still return before any native selection, not fall through.
        var byIndexCurrentCheck = byIndex.IndexOf("if (!CurrentSelection())", byIndexPrefetch, StringComparison.Ordinal);
        var byIndexReadFailure = byIndex.IndexOf("if (readOutcome is SubtitlePrefetchOutcome.Busy", byIndexPrefetch, StringComparison.Ordinal);
        Assert.InRange(byIndexCurrentCheck, byIndexPrefetch, byIndexSelect);
        Assert.InRange(byIndexReadFailure, byIndexCurrentCheck, byIndexSelect);
        var retiredSelection = byIndex[byIndexCurrentCheck..byIndexReadFailure].Replace("\r", "");
        Assert.Contains("if (CurrentSelectionOwner() &&", retiredSelection);
        Assert.Contains("RestoreExecutedSubtitleSelection(reapply: false, currentSelection: CurrentSelectionOwner)", retiredSelection);
        Assert.Contains("ShowSubtitleReadFailure(SubtitlePrefetchOutcome.Error);", retiredSelection);
        Assert.EndsWith("return false;\n                }", retiredSelection.TrimEnd());
        Assert.Contains("CurrentSubtitleSidecarGeneration() == sidecarGeneration", byIndex);
        Assert.Contains("Volatile.Read(ref _loadedMediaInitGeneration) == loadedMediaGeneration", byIndex);
    }

    private static void AssertPrefetchedThenCurrentBeforeSelection(string method, string currentCheck)
    {
        var select = method.IndexOf("SelectSubtitleTrackForOwner(track,", StringComparison.Ordinal);
        var prefetch = method.LastIndexOf("await PrefetchSubtitleSidecarForOwnerAsync(track, CanActivate);", select, StringComparison.Ordinal);
        var captured = method.LastIndexOf("var sidecarGeneration = CurrentSubtitleSidecarGeneration();", prefetch, StringComparison.Ordinal);
        var current = method.IndexOf(currentCheck, prefetch, StringComparison.Ordinal);
        var loadCurrent = method.IndexOf("CurrentSubtitleSidecarGeneration() != sidecarGeneration", prefetch, StringComparison.Ordinal);
        // A superseded file load (same session and index) must not select either.
        Assert.True(captured >= 0 && captured < prefetch && prefetch < current && current < select && prefetch < loadCurrent && loadCurrent < select);
    }
}
