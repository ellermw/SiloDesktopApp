namespace SiloPlayer.Tests;

public sealed class PlayerServiceSourceTests
{
    [Fact]
    public void PlaybackRequestDoesNotReadItsDisposableTokenSourceAfterPublication()
    {
        // CancelPendingPlayRequest may dispose the published source while
        // this request waits for the gate. Native request-supersession checks
        // cover the resulting cancellation; this guards the lifetime boundary.
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task PlayRequestAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private void PublishFullscreenVisualState(", start, StringComparison.Ordinal);
        var request = source[start..end];
        var publication = request.IndexOf("Interlocked.Exchange(ref _playRequestCts, ownerCts)", StringComparison.Ordinal);
        Assert.True(publication >= 0);
        Assert.InRange(request.IndexOf("ownerCts.Token", StringComparison.Ordinal), 0, publication - 1);
        Assert.DoesNotContain("ownerCts.Token", request[publication..]);
    }

    [Fact]
    public void LocalCaptionCycleRetiresOldActivationWithoutChangingPreferenceProvenance()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private void CycleSubtitleBackward()", StringComparison.Ordinal);
        var end = source.IndexOf("private bool HasSubtitleSelectionRoute(", start, StringComparison.Ordinal);
        var cycle = source[start..end];
        var executed = cycle.IndexOf("if (selected.Sid == 0 ? actualSid > 0", StringComparison.Ordinal);
        var retire = cycle.IndexOf("Interlocked.Exchange(ref _subtitleActivationIntent, new object());", StringComparison.Ordinal);
        var intent = cycle.IndexOf("manager!.TryRecordLocalSubtitleSelection(session, selected.Track,", StringComparison.Ordinal);
        Assert.True(executed >= 0 && intent > executed && retire > intent);
        Assert.Contains("isAutomatic: manager.IsSubtitleSelectionAutomatic", cycle[intent..]);
        Assert.Contains("mpv.SetSubtitleTrack(previousSid > 0", cycle[intent..retire]);
        Assert.DoesNotContain("SaveExecutedSubtitlePreferenceAsync", cycle);
        Assert.DoesNotContain("SaveSubtitlePrefsAsync", cycle);
    }

    [Fact]
    public void SubtitleAddUsesSupportedNativeFlagsAndKeepsLanguageOnlyMetadata()
    {
        // The pinned mpv command accepts select/auto/cached; sid's "no"
        // property value is not a sub-add command flag.
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer.Player", "MpvPlayer.cs"));
        var start = source.IndexOf("public bool AddSubtitle(", StringComparison.Ordinal);
        var end = source.IndexOf("public void ReloadSubtitle(", start, StringComparison.Ordinal);
        var method = source[start..end];
        Assert.Contains("new List<string> { \"sub-add\", url, select ? \"select\" : \"auto\" }", method);
        Assert.DoesNotContain("select ? \"select\" : \"no\"", method);
        Assert.Contains("!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(lang)", method);
        Assert.Contains("args.Add(title ?? \"\");", method);
        Assert.Contains("args.Add(lang);", method);
    }

    [Fact]
    public void FailedSubtitleReadCannotRollbackAnAcceptedFileToOutgoingIntent()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private bool RestoreExecutedSubtitleSelection(", StringComparison.Ordinal);
        var end = source.IndexOf("private void ShowSubtitleSelectionUnavailable(", start, StringComparison.Ordinal);
        var method = source[start..end];
        var restore = method.IndexOf("_playbackManager!.RestoreLocalSubtitleIntent(previousIntent);", StringComparison.Ordinal);
        var condition = method.LastIndexOf("if (session != null && previousIntent != null &&", restore, StringComparison.Ordinal);
        Assert.Contains("previousIntent.MediaFileId == session.MediaFileId", method[condition..restore]);
        Assert.Contains("_pendingSubtitlePreferenceOwner = null;", method);
        Assert.DoesNotContain("RecordLocalSubtitleSelection(null", method);
    }

    [Fact]
    public void RemappedSubtitleReloadAcknowledgesOnlyItsAdmittedLogicalChoice()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task<bool> TrySetSubtitleTrackAndPersistAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task SaveExecutedSubtitlePreferenceAsync(", start, StringComparison.Ordinal);
        var method = source[start..end];
        var ack = method.IndexOf("if (transportReloadScheduled && admittedIntent != null)", StringComparison.Ordinal);
        var oldFileGuard = method.IndexOf("manager?.CurrentSession?.MediaFileId != selectionFileId", StringComparison.Ordinal);
        Assert.InRange(ack, 0, oldFileGuard - 1);
        var receipt = method[ack..oldFileGuard];
        Assert.Contains("ReferenceEquals(selectionActivationIntent, Volatile.Read(ref _subtitleActivationIntent))", receipt);
        Assert.Contains("manager?.CaptureLocalSubtitleIntent() == admittedIntent", receipt);
        Assert.Contains("authority.ApiClient.IsCurrentIdentityContext(selectionIdentity)", receipt);
        Assert.Contains("selectionPlayGeneration == Volatile.Read(ref _playRequestGeneration)", receipt);
        Assert.Contains("selectionRoomGeneration == Volatile.Read(ref _roomPlaybackGeneration)", receipt);
        var accepted = receipt.IndexOf("return true;", StringComparison.Ordinal);
        Assert.Contains("return false;", receipt[accepted..]);
        var commonGuard = method.IndexOf("if (_closing ||", ack, StringComparison.Ordinal);
        Assert.Contains("_playbackTerminalState != null", method[commonGuard..oldFileGuard]);
    }

    [Fact]
    public void BitmapFollowupUsesRecordedIdentityAndRebindsOnlyLivePreferenceAuthority()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task<SubtitleTransportResult> SetBitmapSubtitleBurnInAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task ReconcileDeferredSubtitleSelectionAsync(", start, StringComparison.Ordinal);
        var method = source[start..end];
        var loop = method.IndexOf("while (true)", StringComparison.Ordinal);
        Assert.Contains("manager.TryRecordLocalSubtitleSelection(session, requestedTrack, manager.IsSubtitleSelectionAutomatic)", method[..loop]);
        Assert.Contains("manager.ReplanRecordedSubtitleSelectionAsync(position, ct)", method[loop..]);
        Assert.DoesNotContain("manager.ReplanSubtitleAsync(", method[loop..]);
        Assert.Contains("manager.CaptureLocalSubtitleIntent() != selectionIntent", method[loop..]);
        Assert.Contains("pendingOwner.MediaFileId == selectionIntent.MediaFileId", method[loop..]);
        Assert.Contains("_pendingSubtitlePreferenceOwner = pendingOwner with { MediaFileId = replanned.MediaFileId };", method[loop..]);
        Assert.Contains("_pendingSubtitlePreferenceIndex = resolvedIndex;", method[loop..]);
    }

    [Fact]
    public void ReplacementLoadPublishesOwnedBusyBeforeRetiringOutgoingSubtitleReads()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private void BeginOwnedMpvLoad(", StringComparison.Ordinal);
        var end = source.IndexOf("private void ConfigureAudioOutput(", start, StringComparison.Ordinal);
        var method = source[start..end];
        var claim = method.IndexOf("var loadBusyOwner = BeginMpvLoadBusy();", StringComparison.Ordinal);
        var retire = method.IndexOf("CancelPendingLoadedMediaInitialization();", StringComparison.Ordinal);
        Assert.InRange(claim, 0, retire - 1);
        Assert.InRange(method.IndexOf("acceptBusy?.Invoke(loadBusyOwner);", StringComparison.Ordinal), claim, retire - 1);
        var failure = method[method.IndexOf("catch", StringComparison.Ordinal)..];
        Assert.Contains("ReleasePlaybackBusy(loadBusyOwner", failure);
    }

    [Fact]
    public void SiloCastSubtitleInventoryUsesTheSameSelectionRouteAsItsControls()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.SiloCast.cs"));
        var capture = source[..source.IndexOf("public async Task ApplySiloCastControlAsync", StringComparison.Ordinal)];
        Assert.Contains(".Where(t => HasSubtitleSelectionRoute(t, routes))", capture);
        Assert.Contains("!HasSubtitleSelectionRoute(subtitleTrack)", source);
    }

    [Fact]
    public void SubtitleReadAdmissionReleasesStateBeforeCancellationAndPrefetch()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private Task<SubtitlePrefetchOutcome> PrefetchSubtitleSidecarForSelectionAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task<SubtitlePrefetchOutcome> PrefetchSubtitleSidecarAsync(", start, StringComparison.Ordinal);
        Assert.DoesNotContain("lock (", source[start..end]);
        Assert.Contains("PrefetchSubtitleSidecarAsync(track, current)", source[start..end]);
        var selection = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.SubtitleSelection.cs"));
        start = selection.IndexOf("private SubtitleReadOperation? BeginSubtitleRead(", StringComparison.Ordinal);
        Assert.True(start >= 0, "Read admission must support rejecting a retired owner before exchanging operations.");
        end = selection.IndexOf("private void CancelSubtitleSelectionRead()", start, StringComparison.Ordinal);
        var admission = selection[start..end];
        var gate = admission.IndexOf("lock (_subtitleTrackStateLock)", StringComparison.Ordinal);
        var gateEnd = admission.IndexOf('}', gate);
        Assert.Contains("if (current?.Invoke() == false) return null;", admission[gate..gateEnd]);
        Assert.Contains("Interlocked.Exchange(ref _subtitleReadOperation, next)", admission[gate..gateEnd]);
        Assert.True(admission.IndexOf("previous?.Cancel();", StringComparison.Ordinal) > gateEnd,
            "Cancellation callbacks must execute after releasing the state lock.");
    }

    [Fact]
    public void SubtitleAttachmentAdmissionCannotCancelAReadBeforeValidActivation()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task<bool> TrySetSubtitleTrackAndPersistAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task SaveExecutedSubtitlePreferenceAsync(", start, StringComparison.Ordinal);
        var method = source[start..end];
        var admission = method.IndexOf("if (acceptActivation?.Invoke() == false) return false;", StringComparison.Ordinal);
        Assert.True(admission > 0);
        Assert.DoesNotContain("CancelSubtitleSelectionRead();", method[..admission]);
        Assert.DoesNotContain("_pendingLegacySubtitleSelection = null;", method[..admission]);
        var detached = method.IndexOf("Interlocked.Exchange(ref _subtitleReadOperation, null)", admission, StringComparison.Ordinal);
        Assert.True(detached > admission, "Read retirement must follow accepted activation under the same state lock.");
        Assert.Contains("retiredRead?.Cancel();", method[detached..], StringComparison.Ordinal);
    }

    [Fact]
    public void SubtitleAttachmentMutationLocksRecheckTheirCurrentSelectionOwner()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task<bool> TrySetSubtitleTrackAndPersistAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task SaveExecutedSubtitlePreferenceAsync(", start, StringComparison.Ordinal);
        var method = source[start..end];
        foreach (var mutation in new[] { "_activeSubtitleServerIndex = selectedSubtitleIndex;", "mpv.SetSubtitleTrack(0);", "CommitExecutedSubtitleSelection(track);" })
        {
            var at = method.IndexOf(mutation, StringComparison.Ordinal);
            var gate = method.LastIndexOf("lock (_subtitleTrackStateLock)", at, StringComparison.Ordinal);
            Assert.True(gate >= 0 && at > gate);
            Assert.Contains("if (!CurrentReadChoice())", method[gate..at], StringComparison.Ordinal);
        }
    }

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
        Assert.Contains("var current = _liveSubtitleActivation?.Invoke() != false;", resume);
        Assert.Contains("if (_resumeAfterLiveSubtitleBuffer && current && (!IsWatchTogetherPlayback || !_roomPlaybackPaused))", resume);
        Assert.Contains("if (_switchingContent || IsLoading) _restorePausedAfterLoad = false;", resume);
        Assert.Contains("if (_mpv?.IsPaused == true) _mpv.Play();", resume);
        Assert.Contains("_resumeAfterLiveSubtitleBuffer = false;", resume);
        Assert.Contains("public void ReloadSubtitle(int sid)", mpv);

        var refreshStart = source.IndexOf("internal async Task RefreshSubtitlesAfterAiForOwnerAsync(", StringComparison.Ordinal);
        var refreshEnd = source.IndexOf("public Func<bool> BeginSubtitleActivationIntent()", refreshStart, StringComparison.Ordinal);
        Assert.True(refreshStart >= 0 && refreshEnd > refreshStart);
        var refresh = source[refreshStart..refreshEnd];
        Assert.DoesNotContain("SwitchVersionAsync", refresh);
        Assert.Contains("await TrySetSubtitleTrackAndPersistAsync(", refresh, StringComparison.Ordinal);
        Assert.Contains("preferred.Index + 1, preferred.Language, preferred, claimActivation: false,", refresh, StringComparison.Ordinal);
        Assert.Contains("acceptActivation: () => Current() && mayActivate?.Invoke() != false &&", refresh, StringComparison.Ordinal);
        Assert.Contains("currentOwner: currentOwner).ConfigureAwait(false);", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "RestorePlaybackStateAfterSubtitleChange(wasPaused, position",
            refresh,
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
        var methodStart = source.IndexOf("private async Task ShowSubtitleSearchDialogAsync(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private async Task ShowSubtitleAppearanceDialogAsync(", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("dialog.SubtitleDownloadStarted += () => activation = BeginSubtitleActivationIntent();", method, StringComparison.Ordinal);
        Assert.Contains("await RefreshSubtitlesAfterAiForOwnerAsync(session.MediaFileId, downloadedSubtitleId, null,", method, StringComparison.Ordinal);
        Assert.Contains("mayActivate: activation, isCurrent: snapshot.IsCurrent);", method, StringComparison.Ordinal);
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

        var searchStart = source.IndexOf("private async Task ShowSubtitleSearchDialogAsync(", StringComparison.Ordinal);
        var searchEnd = source.IndexOf("private async Task ShowSubtitleAppearanceDialogAsync(", searchStart, StringComparison.Ordinal);
        Assert.True(searchStart >= 0 && searchEnd > searchStart);
        var search = source[searchStart..searchEnd];
        Assert.Contains("var snapshot = CapturePlaybackUiSnapshot();", search, StringComparison.Ordinal);
        Assert.Contains("RestorePlaybackUiSnapshot(snapshot, restorePlayerInput);", search, StringComparison.Ordinal);
        Assert.Contains("dialog.SubtitleDownloadStarted += () => activation = BeginSubtitleActivationIntent();", search, StringComparison.Ordinal);
        Assert.Contains("await RefreshSubtitlesAfterAiForOwnerAsync(session.MediaFileId, downloadedSubtitleId, null,", search, StringComparison.Ordinal);
        Assert.Contains("mayActivate: activation, isCurrent: snapshot.IsCurrent);", search, StringComparison.Ordinal);
        Assert.DoesNotContain("RestorePlaybackStateAfterSubtitleChange(snapshot.WasPaused, snapshot.Position, allowSeek: true);", search, StringComparison.Ordinal);
        Assert.DoesNotContain("SwitchVersionAsync", search, StringComparison.Ordinal);
        Assert.DoesNotContain("_videoWindow?.Hide()", search, StringComparison.Ordinal);
        Assert.Contains("PlaybackDialogHost.ShowAsync", search, StringComparison.Ordinal);
        Assert.Contains("NotifySubtitleDialogClosed(snapshot, nativeRequest)", search, StringComparison.Ordinal);

        var appearanceStart = source.IndexOf("private async Task ShowSubtitleAppearanceDialogAsync(", StringComparison.Ordinal);
        var appearanceEnd = source.IndexOf("private async Task ShowSubtitleAiDialogAsync(", appearanceStart, StringComparison.Ordinal);
        Assert.True(appearanceStart >= 0 && appearanceEnd > appearanceStart);
        var appearance = source[appearanceStart..appearanceEnd];
        Assert.Contains("var snapshot = CapturePlaybackUiSnapshot();", appearance, StringComparison.Ordinal);
        Assert.Contains("RestorePlaybackUiSnapshot(snapshot, restorePlayerInput);", appearance, StringComparison.Ordinal);
        Assert.DoesNotContain("_videoWindow?.Hide()", appearance, StringComparison.Ordinal);
        Assert.Contains("PlaybackDialogHost.ShowAsync", appearance, StringComparison.Ordinal);
        Assert.Contains("NotifySubtitleDialogClosed(snapshot, nativeRequest)", appearance, StringComparison.Ordinal);

        var aiStart = source.IndexOf("private async Task ShowSubtitleAiDialogAsync(", StringComparison.Ordinal);
        var aiEnd = source.IndexOf("private sealed record PlaybackUiSnapshot", aiStart, StringComparison.Ordinal);
        Assert.True(aiStart >= 0 && aiEnd > aiStart);
        var ai = source[aiStart..aiEnd];
        Assert.Contains("var snapshot = CapturePlaybackUiSnapshot();", ai, StringComparison.Ordinal);
        Assert.Contains("RestorePlaybackUiSnapshot(snapshot, restorePlayerInput);", ai, StringComparison.Ordinal);
        Assert.DoesNotContain("_videoWindow?.Hide()", ai, StringComparison.Ordinal);
        Assert.Contains("PlaybackDialogHost.ShowAsync", ai, StringComparison.Ordinal);
        Assert.Contains("NotifySubtitleDialogClosed(snapshot, nativeRequest)", ai, StringComparison.Ordinal);

        var closeStart = source.IndexOf("private void NotifySubtitleDialogClosed(", StringComparison.Ordinal);
        var closeEnd = source.IndexOf("private void RestorePlaybackUiSnapshot(", closeStart, StringComparison.Ordinal);
        var close = source[closeStart..closeEnd];
        Assert.Contains("osc-subtitle-dialog-closed", close, StringComparison.Ordinal);
        Assert.Contains("request.OwnerToken", close, StringComparison.Ordinal);
        Assert.Contains("request.Sequence.ToString", close, StringComparison.Ordinal);
        Assert.Contains("snapshot.IsCurrent() ? \"true\" : \"false\"", close, StringComparison.Ordinal);

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
    public void RemoteSidecarsAreDownloadedBeforeTheSynchronousSubAdd()
    {
        // libmpv's synchronous sub-add of an HTTP URL blocks its caller (the UI
        // thread for player-overlay selections) for the whole download.
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var prefetchStart = source.IndexOf("private async Task<SubtitlePrefetchOutcome> PrefetchSubtitleSidecarAsync(", StringComparison.Ordinal);
        Assert.True(prefetchStart >= 0, "Subtitle prefetch method was not found.");
        var prefetchEnd = source.IndexOf("private int ResolveNativeEmbeddedSid(", prefetchStart, StringComparison.Ordinal);
        Assert.True(prefetchEnd > prefetchStart, "Subtitle prefetch method boundary was not found.");
        var prefetch = source[prefetchStart..prefetchEnd];
        // Same request mpv makes: no account header for worker/CDN sidecars.
        // Text extraction shares the complete-track ten-minute deadline; the
        // reader's default short budget is reserved for busy retries/sync reads.
        Assert.Matches(@"SubtitleSidecarReader\.ReadAsync\(SubtitleSidecarDownloadClient,\s*url,\s*track\.Codec,\s*bounded\.Token,\s*readTimeout:\s*TimeSpan\.FromMinutes\(10\)\)", prefetch);
        // Complete tracks use their own bounded budget, not the short sync client.
        Assert.Contains("SubtitleSidecarDownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, bounded.Token)", prefetch, StringComparison.Ordinal);
        Assert.Contains("HttpClient SubtitleSidecarDownloadClient = new() { Timeout = Timeout.InfiniteTimeSpan }", source, StringComparison.Ordinal);
        Assert.Contains("bounded.CancelAfter(TimeSpan.FromMinutes(10));", prefetch, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAuthenticatedRequest", prefetch, StringComparison.Ordinal);
        Assert.Contains("if (read.Outcome == SubtitleSidecarReadOutcome.Busy) return SubtitlePrefetchOutcome.Busy;", prefetch, StringComparison.Ordinal);
        Assert.Contains("if (read.Outcome == SubtitleSidecarReadOutcome.Error)", prefetch, StringComparison.Ordinal);
        Assert.Contains("return read.Failure switch", prefetch, StringComparison.Ordinal);
        Assert.Contains("SubtitleSidecarReadFailure.DownloadLimit => SubtitlePrefetchOutcome.DownloadLimit", prefetch, StringComparison.Ordinal);
        Assert.Contains("SubtitleSidecarReadFailure.Timeout => SubtitlePrefetchOutcome.Timeout", prefetch, StringComparison.Ordinal);
        Assert.Contains("_ => SubtitlePrefetchOutcome.Error", prefetch, StringComparison.Ordinal);
        Assert.Contains("authority.ApiClient.IsCurrentContext(context)", prefetch, StringComparison.Ordinal);
        Assert.Contains("generation != _subtitleSidecarGeneration", prefetch, StringComparison.Ordinal);
        Assert.Contains("_subtitleSyncFiles.Add(path);", prefetch, StringComparison.Ordinal);
        // The size cap applies while reading, not after buffering the body.
        Assert.DoesNotContain("ReadAsByteArrayAsync", prefetch, StringComparison.Ordinal);
        var reader = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer.Core", "Services", "SubtitleSidecarReader.cs"));
        var textSizeCheck = reader.IndexOf("if (buffer.Length + read > SubtitleSidecarFile.MaxBytes)", StringComparison.Ordinal);
        var textLimitFailure = reader.IndexOf("return Failure(SubtitleSidecarReadFailure.DownloadLimit);", textSizeCheck, StringComparison.Ordinal);
        var textWrite = reader.IndexOf("buffer.Write(chunk, 0, read);", StringComparison.Ordinal);
        Assert.True(textSizeCheck >= 0 && textLimitFailure > textSizeCheck && textWrite > textLimitFailure,
            "Chunked text sidecars must reject oversized content before buffering each chunk.");
        Assert.Contains("http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, bounded.Token)", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAuthenticatedRequest", reader, StringComparison.Ordinal);
        Assert.Contains("var maxBytes = SubtitleSidecarFile.DownloadMaxBytes(extension);", prefetch, StringComparison.Ordinal);
        Assert.Contains("if (response.Content.Headers.ContentLength > maxBytes) return limitOutcome;", prefetch, StringComparison.Ordinal);
        var sizeCheck = prefetch.IndexOf("if (total + read > maxBytes) return limitOutcome;", StringComparison.Ordinal);
        var write = prefetch.IndexOf("await file.WriteAsync(chunk.AsMemory(0, read), bounded.Token);", StringComparison.Ordinal);
        Assert.True(sizeCheck >= 0 && write > sizeCheck,
            "Chunked sidecars must enforce the format-specific cap before writing each chunk.");
        Assert.Contains("if (path != null) try { File.Delete(path); } catch { }", prefetch, StringComparison.Ordinal);
        // Same-session reloads delete and unregister this file's downloads.
        var load = source[source.IndexOf("private void LoadSubtitles()", StringComparison.Ordinal)..];
        load = load[..load.IndexOf("private static bool IsUnsupportedBitmapSubtitle(", StringComparison.Ordinal)];
        Assert.Contains("_subtitleSyncFiles.Remove(path);", load, StringComparison.Ordinal);
        Assert.Contains("File.Delete(path);", load, StringComparison.Ordinal);
        // Close, dispose and session changes delete every sync file, prefetched sidecars
        // included: forget them and reject downloads still in flight.
        var sync = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "Services", "PlayerService.SubtitleSync.cs"));
        var stop = sync[sync.IndexOf("private void StopSubtitleSync()", StringComparison.Ordinal)..];
        stop = stop[..stop.IndexOf("private void EnsureSubtitleSync()", StringComparison.Ordinal)];
        var invalidate = stop.IndexOf("lock (_subtitleTrackStateLock)", StringComparison.Ordinal);
        var filesClear = stop.IndexOf("_subtitleSyncFiles.Clear();", StringComparison.Ordinal);
        Assert.InRange(invalidate, stop.IndexOf("lock (_subtitleSyncGate)", StringComparison.Ordinal), filesClear);
        var lockEnd = stop.IndexOf('}', invalidate);
        Assert.InRange(lockEnd, invalidate, filesClear);
        var invalidation = stop[invalidate..lockEnd];
        Assert.Contains("_prefetchedSubtitleFiles.Clear();", invalidation, StringComparison.Ordinal);
        Assert.Contains("_preparedSubtitleReload = null;", invalidation, StringComparison.Ordinal);
        Assert.Contains("_subtitleSidecarGeneration++;", invalidation, StringComparison.Ordinal);

        var persist = source[source.IndexOf("private async Task<bool> TrySetSubtitleTrackAndPersistAsync(", StringComparison.Ordinal)..];
        persist = persist[..persist.IndexOf("private bool MatchesSubtitlePreference(", StringComparison.Ordinal)];
        // A subtitle_ready refresh can reindex the retained row while prefetch
        // is in flight; ownership follows that row, not its original ordinal.
        AssertPrefetchedThenCurrentBeforeSelection(persist, "_activeSubtitleServerIndex != track.Index");
        var persistSelect = persist.IndexOf("SelectSubtitleTrackForOwner(track,", StringComparison.Ordinal);
        var persistPrefetch = persist.LastIndexOf("await PrefetchSubtitleSidecarForSelectionAsync(track, CurrentReadChoice);", persistSelect, StringComparison.Ordinal);
        Assert.Contains("currentOwner?.HasCurrentSubtitleChoice != false", persist);
        // Restoration uses the state from just before the sub-add, not from before the download.
        Assert.InRange(persist.IndexOf("wasPaused = CaptureUserPausedState();", persistPrefetch, StringComparison.Ordinal), persistPrefetch, persistSelect);
        Assert.InRange(persist.IndexOf("position = CurrentMediaPosition;", persistPrefetch, StringComparison.Ordinal), persistPrefetch, persistSelect);
        // A superseding file load hands restoration to the reload but still persists the choice.
        var loadChanged = persist.IndexOf("CurrentSubtitleSidecarGeneration() != sidecarGeneration", persistPrefetch, StringComparison.Ordinal);
        var handOff = persist.IndexOf("transportReloadScheduled = true;", loadChanged, StringComparison.Ordinal);
        Assert.InRange(handOff, loadChanged, persistSelect);
        Assert.DoesNotContain("return;", persist[loadChanged..persistSelect], StringComparison.Ordinal);
        Assert.True(persist.IndexOf("await SaveExecutedSubtitlePreferenceAsync(preferenceOwner, track);", persistSelect, StringComparison.Ordinal) > persistSelect);
        var byIndex = source[source.IndexOf("private async Task<bool> SelectSubtitleByServerIndexAsync(", StringComparison.Ordinal)..];
        var byIndexPrefetch = byIndex.IndexOf("await PrefetchSubtitleSidecarForSelectionAsync(track, CurrentSelection);", StringComparison.Ordinal);
        Assert.True(byIndexPrefetch >= 0, "Load-time selection must download for its current owner before native selection.");
        var byIndexSelect = byIndex.IndexOf("SelectSubtitleTrackForOwner(track, CurrentSelection)", byIndexPrefetch, StringComparison.Ordinal);
        Assert.InRange(byIndex.IndexOf("if (!CurrentSelection()) return false;", byIndexPrefetch, StringComparison.Ordinal), byIndexPrefetch, byIndexSelect);
        Assert.Contains("CurrentSubtitleSidecarGeneration() == sidecarGeneration", byIndex);
        Assert.Contains("Volatile.Read(ref _loadedMediaInitGeneration) == loadedMediaGeneration", byIndex);
    }

    private static void AssertPrefetchedThenCurrentBeforeSelection(string method, string currentCheck)
    {
        var select = method.IndexOf("SelectSubtitleTrackForOwner(track,", StringComparison.Ordinal);
        var prefetch = method.LastIndexOf("await PrefetchSubtitleSidecarForSelectionAsync(track, CurrentReadChoice);", select, StringComparison.Ordinal);
        var captured = method.LastIndexOf("var sidecarGeneration = CurrentSubtitleSidecarGeneration();", prefetch, StringComparison.Ordinal);
        var current = method.IndexOf(currentCheck, prefetch, StringComparison.Ordinal);
        var loadCurrent = method.IndexOf("CurrentSubtitleSidecarGeneration() != sidecarGeneration", prefetch, StringComparison.Ordinal);
        // A superseded file load (same session and index) must not select either.
        Assert.True(captured >= 0 && captured < prefetch && prefetch < current && current < select && prefetch < loadCurrent && loadCurrent < select);
    }
}
