namespace SiloPlayer.Tests;

public sealed class SubtitleReviewRegressionTests
{
    [Fact]
    public void ManualAiActivationReceiptDoesNotRetireAppliedSaveOrOrdinaryGrantLifetime()
    {
        var owner = Read("PlayerService.SubtitleOperationOwner.cs");
        Assert.Contains("_manualSubtitleChoiceVersion = Volatile.Read(ref player._manualSubtitleChoiceVersion);", owner);
        Assert.Contains("internal bool IsCurrent => Stable && ReferenceEquals", owner);
        var ordinary = owner[owner.IndexOf("internal bool CanUseSession", StringComparison.Ordinal)..];
        Assert.DoesNotContain("HasCurrentSubtitleChoice", ordinary);
        var service = Read("PlayerService.cs");
        var admission = Between(service, "private async Task<bool> TrySetSubtitleTrackAndPersistForOwnerAsync(", "private bool MatchesSubtitlePreference(");
        Assert.Contains("if (currentOwner == null) Interlocked.Increment(ref _manualSubtitleChoiceVersion);", admission);
        Assert.Contains("bool CanActivate() => CurrentChoice() && currentOwner?.HasCurrentSubtitleChoice != false;", admission);
        Assert.Contains("PrefetchSubtitleSidecarForOwnerAsync(track, CanActivate)", admission);
        var save = Between(service, "private async Task<bool> SaveExecutedSubtitlePreferenceAsync(", "private void RestorePlaybackStateAfterSubtitleChange(");
        Assert.DoesNotContain("HasCurrentSubtitleChoice", save);
        Assert.Contains("if (!operationOwner.IsCurrent) return false;", save);
        Assert.Contains("await _subtitlePreferenceSaveGate.WaitAsync(owner.Token)", save);
        Assert.Contains("if (!CurrentOwner()) return false;", save);
        Assert.Contains("!IsLoading && _liveSubtitleOperationOwner?.HasCurrentSubtitleChoice != false", service);
    }

    [Fact]
    public void AiContinuityRebasesOnlyWithinTheCapturedFileAndAuthority()
    {
        var owner = Read("PlayerService.SubtitleOperationOwner.cs");
        var ai = Between(owner, "internal bool IsCurrentForAi", "internal bool CanUseSession");
        Assert.Contains("if (!Stable || _mediaFileLifetimeGeneration != _manager.MediaFileLifetimeGeneration) return false;", ai);
        Assert.Contains("current.MediaFileId != _mediaFileId", ai);
        Assert.Contains("Interlocked.CompareExchange(ref _session, current, previous)", ai);
        Assert.True(ai.IndexOf("lock (_manager.SubtitleInventorySyncRoot)", StringComparison.Ordinal) <
            ai.IndexOf("if (!Stable ||", StringComparison.Ordinal));
        Assert.Contains("ReferenceEquals(current, _manager.CurrentSession)", ai);
        Assert.Contains("internal bool IsCurrent => Stable && ReferenceEquals", owner);
        Assert.Contains("_mediaFileLifetimeGeneration == _manager.MediaFileLifetimeGeneration", owner);
        var capture = Between(owner, "internal SubtitleOperationOwner? CaptureSubtitleOperationOwner", "internal sealed class SubtitleOperationOwner");
        var captureGate = capture.IndexOf("lock (manager.SubtitleInventorySyncRoot)", StringComparison.Ordinal);
        var expected = capture.IndexOf("!ReferenceEquals(session, manager.CurrentSession)", StringComparison.Ordinal);
        var receipt = capture.IndexOf("return new(this, _activeAuthority, manager, session);", StringComparison.Ordinal);
        Assert.True(captureGate >= 0 && expected > captureGate && receipt > expected);
        var service = Read("PlayerService.cs");
        var live = Between(service, "private void ApplySubtitleTranslationCues(", "private void SendTranslationBufferingState(");
        Assert.DoesNotContain("_liveSubtitleOperationOwner?.IsCurrent == false", live);
        Assert.Contains("_liveSubtitleOperationOwner?.IsCurrentForAi == false", live);
        Assert.Contains("if (_switchingContent || IsLoading) _restorePausedAfterLoad = false;", live);
        Assert.Contains("else if (_mpv?.IsPaused == true) _mpv.Play();", live);
        var overlay = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs"));
        Assert.Contains("bool currentOwner() => operationOwner.IsCurrentForAi;", overlay);
    }

    [Fact]
    public void RetiredAiBufferingCleansUpWithoutTouchingAReplacementLoad()
    {
        var service = Read("PlayerService.cs");
        var retain = Between(service, "private bool TryRetainLiveSubtitleTranslationLocked(", "private void SendTranslationBufferingState(");
        var jobGuard = retain.IndexOf("if (jobId <= 0 || _liveSubtitleJobId != jobId) return false;", StringComparison.Ordinal);
        var cleanup = retain.IndexOf("ClearLiveSubtitleTranslationLocked(restorePreviousSubtitle: false);", StringComparison.Ordinal);
        Assert.True(jobGuard >= 0 && cleanup > jobGuard);
        Assert.Contains("_liveSubtitleOperationOwner?.IsCurrentForAi == false", retain);
        Assert.Contains("_playbackManager?.CurrentSession?.MediaFileId != _liveSubtitleFileId", retain);
        foreach (var (first, next) in new[]
        {
            ("private void ApplySubtitleTranslationCues(", "private void ApplySubtitleTranslationCompleted("),
            ("private void ApplySubtitleTranslationCompleted(", "private void ApplySubtitleTranslationFailed("),
            ("private void ApplySubtitleTranslationFailed(", "public void FailLiveSubtitleTranslation("),
            ("public void FailLiveSubtitleTranslation(", "private void ResumeAfterLiveSubtitleBuffer("),
            ("private void ResumeAfterLiveSubtitleBuffer(", "private bool TryRetainLiveSubtitleTranslationLocked("),
        })
            Assert.Contains("if (!TryRetainLiveSubtitleTranslationLocked(jobId)", Between(service, first, next));

        var load = Between(service, "private void BeginMpvLoad(", "private void ConfigureAudioOutput(");
        var generation = load.IndexOf("Interlocked.Increment(ref _fileLoadGeneration)", StringComparison.Ordinal);
        var retire = load.IndexOf("TryRetainLiveSubtitleTranslationLocked(_liveSubtitleJobId);", StringComparison.Ordinal);
        var loadFile = load.IndexOf("mpv.LoadFile(", StringComparison.Ordinal);
        Assert.True(generation >= 0 && retire > generation && loadFile > retire);
        var clear = Between(service, "private void ClearLiveSubtitleTranslationLocked(", "private void DeleteLiveSubtitleFileLocked(");
        Assert.Contains("_liveSubtitleResumeCts?.Cancel();", clear);
        Assert.Contains("_liveSubtitleResumeCts?.Dispose();", clear);
        Assert.Contains("_liveSubtitleResumeCts = null;", clear);
        Assert.Contains("SendTranslationBufferingState(false, null);", clear);
        Assert.Contains("_liveSubtitleFileId == _playbackManager?.CurrentSession?.MediaFileId", clear);
        Assert.Contains("_liveSubtitleFileLoadGeneration == Volatile.Read(ref _fileLoadGeneration)", clear);
        Assert.Contains("if (_liveSubtitleSid > 0 && sameLoad)", clear);
        Assert.Contains("var restoreLiveSelection = restorePreviousSubtitle && sameLoad && _liveSubtitleSid > 0 &&", clear);
        Assert.Contains("(int)Math.Round(_mpv.GetPropertyDouble(\"sid\")) == _liveSubtitleSid;", clear);
        Assert.Contains("if (restoreLiveSelection)", clear);
        Assert.Contains("if (_executedSubtitleFileId == _liveSubtitleFileId)", clear);
        Assert.Contains("var track = _executedSubtitleTrack;", clear);
        Assert.Contains("_loadedExternalSubtitleSids.TryGetValue(track.Index", clear);
        Assert.Contains("ResolveNativeEmbeddedSid(track)", clear);
        Assert.Contains("if (_resumeAfterLiveSubtitleBuffer && sameLoad && _mpv.IsPaused)", clear);
        Assert.Contains("_resumeAfterLiveSubtitleBuffer = false;", clear);
    }

    [Fact]
    public void RestorationRevalidatesPickerOwnershipBeforeCommittingUnderTheStateLock()
    {
        var restore = Between(Read("PlayerService.cs"), "private bool RestoreExecutedSubtitleSelection(",
            "private void ShowSubtitleSelectionUnavailable(");
        var snapshotLock = restore.IndexOf("lock (_subtitleTrackStateLock)", StringComparison.Ordinal);
        var captureOwner = restore.IndexOf("var preferenceOwner = _pendingSubtitlePreferenceOwner;", StringComparison.Ordinal);
        var reapply = restore.IndexOf("!SelectSubtitleTrackForOwner(retained, restoreOwner)", StringComparison.Ordinal);
        var commitLock = restore.IndexOf("lock (_subtitleTrackStateLock)", reapply, StringComparison.Ordinal);
        var ownerCheck = restore.IndexOf("if (!restoreOwner()) return false;", commitLock, StringComparison.Ordinal);
        var clearOwner = restore.IndexOf("_pendingSubtitlePreferenceOwner = null;", StringComparison.Ordinal);
        Assert.True(snapshotLock >= 0 && captureOwner > snapshotLock && reapply > captureOwner &&
            commitLock > reapply && ownerCheck > commitLock && clearOwner > ownerCheck);
        Assert.Contains("ReferenceEquals(preferenceOwner, _pendingSubtitlePreferenceOwner)", restore);
        Assert.Contains("ReferenceEquals(previous, _executedSubtitleTrack)", restore);
        Assert.Contains("loadGeneration == Volatile.Read(ref _loadedMediaInitGeneration)", restore);
    }

    [Fact]
    public void UnmappableDeferredPickStillCompletesLoadedFileReconciliationAndAiPublication()
    {
        var initialize = Between(Read("PlayerService.cs"), "private async Task InitializeLoadedMediaAsync(",
            "private bool IsLoadedMediaInitializationCurrent(");
        var manual = Between(initialize, "if (manualChoice is { Accepted: true }",
            "if (resolvedSubtitleIndex is int initialSubtitleIndex)");
        var refusal = Between(manual, "if (manualChoice.RequestedTrack != null && mapped == null)",
            "else");
        Assert.Contains("RestoreExecutedSubtitleSelection(reapply: true);", refusal);
        Assert.Contains("ShowSubtitleSelectionUnavailable();", refusal);
        // Null skips execution, unlike -1, which would apply and potentially save Off.
        Assert.Contains("resolvedSubtitleIndex = null;", refusal);
        Assert.DoesNotContain("return;", refusal);
        Assert.Matches(@"}\s*else\s+resolvedSubtitleIndex = mapped\?\.Index \?\? -1;", manual);

        var remaining = initialize[initialize.IndexOf("if (resolvedSubtitleIndex is int initialSubtitleIndex)", StringComparison.Ordinal)..];
        var reset = remaining.IndexOf("subtitleFailureSession.SubtitleReconciliationFailed = false;", StringComparison.Ordinal);
        var settings = remaining.IndexOf("await settingsTask;", StringComparison.Ordinal);
        var availability = remaining.IndexOf("await SendSubtitleAiAvailabilityToOscAsync(ct);", StringComparison.Ordinal);
        Assert.True(reset >= 0 && settings > reset && availability > settings);
        // After refusing the pick, only retirement may bypass the per-file publication.
        foreach (var line in remaining[..availability].Split('\n').Where(line => line.Contains("return;", StringComparison.Ordinal)))
            Assert.Equal("if (!IsLoadedMediaInitializationCurrent(generation, ownerCts)) return;", line.Trim());
    }

    [Fact]
    public void NonReapplyingRestorationRequiresTheRetainedTracksActualCurrentSid()
    {
        var restore = Between(Read("PlayerService.cs"), "private bool RestoreExecutedSubtitleSelection(",
            "private void ShowSubtitleSelectionUnavailable(");
        var verify = Between(restore, "if (!reapply && retained != null", "if (retained == null) _mpv?.SetSubtitleTrack(0);");
        Assert.Contains("!IsBurnedInSubtitle(session, retained)", verify);
        Assert.Contains("Volatile.Read(ref _fileLoadTimeoutCts) == null", verify);
        Assert.Contains("GetPropertyDouble(\"sid\")", verify);
        Assert.Contains("_loadedExternalSubtitleSids.TryGetValue(retained.Index, out var cached)", verify);
        Assert.Contains("cached == sid && mpv!.GetSubtitleTrackIds(externalOnly: true).Contains(sid)", verify);
        Assert.Contains("CanUseNativeEmbeddedSubtitleTrack() && ResolveNativeEmbeddedSid(retained) == sid", verify);
        Assert.Contains("if (!appliedSidecar && !appliedNative) retained = null;", verify);
        Assert.DoesNotContain("SetSubtitleTrack(", verify);
        Assert.Contains("_activeSubtitleServerIndex = retained?.Index ?? -1;", restore);
        Assert.True(restore.IndexOf("if (!restoreOwner()) return false;", StringComparison.Ordinal) <
            restore.IndexOf("if (!reapply && retained != null", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusedPreAcceptanceReadsReconcileOnlyTheCurrentPickBeforePublishingFailure()
    {
        var manual = Between(Read("PlayerService.cs"), "private async Task<bool> TrySetSubtitleTrackAndPersistForOwnerAsync(",
            "private bool MatchesSubtitlePreference(");
        var beforeAcceptance = manual[..manual.IndexOf("choice.Accepted = true;", StringComparison.Ordinal)];
        var failure = Between(beforeAcceptance, "if (readOutcome is SubtitlePrefetchOutcome.Busy", "var selectionSession = manager?.CurrentSession;");
        Assert.Contains("SubtitlePrefetchOutcome.Error or SubtitlePrefetchOutcome.TooLarge or SubtitlePrefetchOutcome.TimedOut or SubtitlePrefetchOutcome.Retired", failure);
        var restore = failure.IndexOf("if (!RestoreExecutedSubtitleSelection(reapply: false, currentSelection: CurrentReadChoice)) return false;", StringComparison.Ordinal);
        var feedback = failure.IndexOf("ShowSubtitleReadFailure(", StringComparison.Ordinal);
        Assert.True(restore >= 0 && feedback > restore);
    }

    [Fact]
    public void SupersededBitmapAfterSeekDrainRescuesOnlyTheLatestAcceptedDeferredChoice()
    {
        var burn = Between(Read("PlayerService.cs"), "private async Task<SubtitleTransportResult> SetBitmapSubtitleBurnInForOwnerAsync(",
            "private void MarkSubtitleSelectionDeferred(");
        var superseded = Between(burn, "if (!ReferenceEquals(manualChoice, Volatile.Read(ref _subtitleSelectionOperation)))",
            "if (_activeSubtitleServerIndex != (requestedIndex ?? -1)");
        Assert.Contains("lock (_subtitleTrackStateLock)", superseded);
        Assert.Contains("Volatile.Read(ref _subtitleSelectionOperation) is { Accepted: true } latest", superseded);
        Assert.Contains("IsCurrentSubtitleChoice(latest)", superseded);
        Assert.Contains("_pendingSubtitlePreferenceOwner is { NeedsDeferredExecution: true } deferred", superseded);
        Assert.Contains("ReferenceEquals(latest.PreferenceOwner, deferred) && !ReferenceEquals(deferred, preferenceOwner)", superseded);
        Assert.Contains("if (hasDeferredChoice)", superseded);
        Assert.Contains("return RefuseOrRecoverSubtitleSelection(SubtitleTransportResult.Superseded);", superseded);
        Assert.Contains("return SubtitleTransportResult.Superseded;", superseded);
        var refusal = Between(burn, "void RefuseTentativeSubtitleSelection(", "SubtitleTransportResult RefuseOrRecoverSubtitleSelection(");
        Assert.Contains("!loadStarted && HasOtherTransportOwner()", refusal);
        Assert.Contains("SetSwitchingContent(false);", refusal);
        Assert.Contains("IsLoading = false;", refusal);
        Assert.Contains("reconcileDeferredSelection = true;", refusal);
        Assert.Contains("await ReconcileDeferredSubtitleSelectionAsync(manager, session);", burn);
    }

    [Fact]
    public void KnownPgsUsesOneBoundedDiskDownloadInsteadOfTheTextProbe()
    {
        var prefetch = Between(Read("PlayerService.cs"), "private async Task<SubtitlePrefetchOutcome> PrefetchSubtitleSidecarForOwnerAsync(",
            "private int ResolveNativeEmbeddedSid(");
        var bitmap = prefetch.IndexOf("if (SubtitleSidecarFile.DownloadExtension(null, url, track.Codec) == \".sup\")", StringComparison.Ordinal);
        var download = prefetch.IndexOf("return await ReadUrlFallbackAsync();", bitmap, StringComparison.Ordinal);
        var text = prefetch.IndexOf("var read = await SubtitleSidecarReader.ReadAsync(", StringComparison.Ordinal);
        Assert.True(bitmap >= 0 && download > bitmap && text > download);
        Assert.Contains("var maxBytes = SubtitleSidecarFile.DownloadMaxBytes(extension);", prefetch);
        Assert.Contains("if (response.Content.Headers.ContentLength > maxBytes) return SubtitlePrefetchOutcome.TooLarge;", prefetch);
        Assert.Contains("if (total + read > maxBytes) return SubtitlePrefetchOutcome.TooLarge;", prefetch);
        Assert.Contains("fallbackBudget.Token", prefetch);
        Assert.DoesNotContain("mpv.AddSubtitle", prefetch);
    }

    [Fact]
    public void FinalAiSelectionRetiresTheMatchingJobAndItsRestoreState()
    {
        var service = Read("PlayerService.cs");
        var refresh = Between(service, "internal async Task RefreshSubtitlesAfterAiForOwnerAsync(",
            "private void WriteLiveSubtitleFileLocked(");
        var executed = refresh.IndexOf("_executedSubtitleTrack?.Id != preferred.Id", StringComparison.Ordinal);
        var matchingJob = refresh.IndexOf("if (liveJobId > 0 && _liveSubtitleJobId == liveJobId)", executed, StringComparison.Ordinal);
        var retire = refresh.IndexOf("ClearLiveSubtitleTranslationLocked(restorePreviousSubtitle: false);", matchingJob, StringComparison.Ordinal);
        Assert.True(executed >= 0 && matchingJob > executed && retire > matchingJob);
        var clear = Between(service, "private void ClearLiveSubtitleTranslationLocked(", "private void DeleteLiveSubtitleFileLocked(");
        foreach (var reset in new[] { "_liveSubtitleJobId = 0;", "_liveSubtitleOperationOwner = null;", "_liveSubtitleFileId = 0;",
            "_liveSubtitleFileLoadGeneration = 0;", "_preLiveSubtitleSid = 0;", "_liveSubtitleResumeCts = null;" })
            Assert.Contains(reset, clear);
    }

    [Fact]
    public void LiveTranslationClaimsOnlyItsNewExternalTrackAfterASuccessfulAdd()
    {
        var live = Between(Read("PlayerService.cs"), "private void ApplySubtitleTranslationCues(",
            "private void ApplySubtitleTranslationCompleted(");
        var previous = live.IndexOf("var previousIds = mpv.GetSubtitleTrackIds().ToHashSet();", StringComparison.Ordinal);
        var add = live.IndexOf("var added = mpv.AddSubtitle(_liveSubtitlePath, \"AI translation\", null, select: false);", StringComparison.Ordinal);
        var identify = live.IndexOf("mpv.GetSubtitleTrackIds(externalOnly: true, externalFilename: _liveSubtitlePath)", StringComparison.Ordinal);
        var delta = live.IndexOf(".Where(id => !previousIds.Contains(id)).ToArray();", StringComparison.Ordinal);
        var unique = live.IndexOf("var newSid = ownedIds.Length == 1 ? ownedIds[0] : 0;", StringComparison.Ordinal);
        var sameLoad = live.IndexOf("if (ReferenceEquals(mpv, _mpv) && loadGeneration == Volatile.Read(ref _fileLoadGeneration) && newSid > 0)", StringComparison.Ordinal);
        var success = live.IndexOf("if (added)", StringComparison.Ordinal);
        var claim = live.IndexOf("_liveSubtitleSid = newSid;", StringComparison.Ordinal);
        var select = live.IndexOf("mpv.SetSubtitleTrack(newSid);", StringComparison.Ordinal);
        Assert.True(previous >= 0 && add > previous && identify > add && delta > identify && unique > delta &&
            sameLoad > unique && success > sameLoad && claim > success && select > claim);
        Assert.Contains("else mpv.RemoveSubtitle(newSid);", live);
        Assert.DoesNotContain("Math.Max(1,", live);
        Assert.DoesNotContain("_liveSubtitleSid = Math", live);
        Assert.DoesNotContain("select: true", live);
    }

    [Fact]
    public void SidecarAddsNeverFallBackToRemoteUrlsOrHoldTheAddGateWhileDownloading()
    {
        var service = Read("PlayerService.cs");
        var add = Between(service, "private bool SelectSubtitleSidecar(", "private long CurrentSubtitleSidecarGeneration()");
        Assert.Contains("if (!_prefetchedSubtitleFiles.TryGetValue(track.Index, out var local)) return false;", add);
        Assert.Contains("source = local;", add);
        Assert.Contains("mpv.AddSubtitle(source, label, track.Language, select: false)", add);
        Assert.DoesNotContain("FullUrl", add);
        // Busy text reads and native-probed formats now share typed outcomes;
        // the fallback must still download to bounded disk, never remote sub-add.
        var prefetch = Between(service, "private async Task<SubtitlePrefetchOutcome> PrefetchSubtitleSidecarForOwnerAsync(", "private int ResolveNativeEmbeddedSid(");
        Assert.DoesNotContain("lock (_subtitleAddGate)", prefetch);
        Assert.DoesNotContain("mpv.AddSubtitle", prefetch);
        Assert.Contains("SubtitleSidecarFile.DownloadExtension(", prefetch);
        Assert.Contains("var maxBytes = SubtitleSidecarFile.DownloadMaxBytes(extension);", prefetch);
        Assert.Contains("response.Content.Headers.ContentLength > maxBytes", prefetch);
        Assert.Contains("total + read > maxBytes", prefetch);
        Assert.Contains("await file.WriteAsync(chunk.AsMemory(0, read), fallbackBudget.Token)", prefetch);
        Assert.DoesNotContain("new MemoryStream", prefetch);
        Assert.Contains("loadedMediaGeneration == Volatile.Read(ref _loadedMediaInitGeneration)", prefetch);
        Assert.Contains("choiceVersion == Volatile.Read(ref _legacySubtitleChoiceVersion)", prefetch);
        Assert.Contains("playGeneration == Volatile.Read(ref _playRequestGeneration)", prefetch);
        Assert.Contains("authority.ApiClient.IsCurrentIdentityContext(identity)", prefetch);
        Assert.Contains("if (!CurrentOwner()) { bounded.Cancel(); return; }", prefetch);
        Assert.Contains("var watch = WatchOwnerAsync();", prefetch);
        Assert.Contains("await watch;", prefetch);
        Assert.Contains("bounded.CancelAfter(TimeSpan.FromMinutes(10))", prefetch);
        Assert.Contains("SubtitleSidecarDownloadClient.GetAsync", prefetch);
        Assert.Contains("SubtitleSidecarReader.ReadAsync(SubtitleSidecarDownloadClient", prefetch);
        Assert.Contains("if (read.Outcome == SubtitleSidecarReadOutcome.Busy) return SubtitlePrefetchOutcome.Busy;", prefetch);
        Assert.DoesNotContain("SubtitleSyncDownloadClient", prefetch);
        Assert.Contains("SubtitleSidecarDownloadClient = new() { Timeout = Timeout.InfiniteTimeSpan }", service);
        Assert.Contains("currentOwner?.Invoke() != false", prefetch);
        Assert.Contains("generation != _subtitleSidecarGeneration || !CurrentOwner()", prefetch);
    }

    [Fact]
    public void SidecarFailuresDistinguishDownloadLimitsAndNotifyAutomaticSelections()
    {
        var service = Read("PlayerService.cs");
        var prefetch = Between(service, "private async Task<SubtitlePrefetchOutcome> PrefetchSubtitleSidecarForOwnerAsync(", "private int ResolveNativeEmbeddedSid(");
        Assert.Contains("if (response.Content.Headers.ContentLength > maxBytes) return SubtitlePrefetchOutcome.TooLarge;", prefetch);
        Assert.Contains("if (total + read > maxBytes) return SubtitlePrefetchOutcome.TooLarge;", prefetch);
        Assert.Contains("return CurrentOwner() ? SubtitlePrefetchOutcome.TimedOut : SubtitlePrefetchOutcome.Retired;", prefetch);
        Assert.Contains("fallbackError is OperationCanceledException ? SubtitlePrefetchOutcome.TimedOut : SubtitlePrefetchOutcome.Error", prefetch);
        var manual = Between(service, "private async Task<bool> TrySetSubtitleTrackAndPersistForOwnerAsync(", "private bool MatchesSubtitlePreference(");
        Assert.Contains("var readOutcome = await PrefetchSubtitleSidecarForOwnerAsync(track, CanActivate);", manual);
        var loaded = Between(service, "private async Task<bool> SelectSubtitleByServerIndexAsync(", "public int ActiveSubtitleServerIndex");
        Assert.Contains("var readOutcome = await PrefetchSubtitleSidecarForOwnerAsync(track, CurrentSelection);", loaded);
        foreach (var selection in new[] { manual, loaded })
        {
            Assert.Contains("SubtitlePrefetchOutcome.TooLarge or SubtitlePrefetchOutcome.TimedOut", selection);
            Assert.Contains("ShowSubtitleReadFailure(readOutcome == SubtitlePrefetchOutcome.Retired ? SubtitlePrefetchOutcome.Error : readOutcome);", selection);
        }
        var feedback = Between(Read("PlayerService.SubtitleSelection.cs"), "private void ShowSubtitleReadFailure(", "private void ShowSubtitleReadBusy()");
        Assert.Contains("ShowSubtitleReadBusy();", feedback);
        Assert.Contains("SubtitlePrefetchOutcome.TooLarge =>", feedback);
        Assert.Contains("download limit. Choose another subtitle or turn subtitles off.", feedback);
        Assert.Contains("SubtitlePrefetchOutcome.TimedOut =>", feedback);
        Assert.Contains("Subtitle download timed out. The server may still be extracting the track. Try again", feedback);
        Assert.Contains("checking your connection and available disk space.", feedback);
        Assert.Contains("osc-set-active-subtitle", feedback);
        Assert.Contains("ShowNotice(\"Subtitle could not be loaded\", detail, \"warning\");", feedback);
        Assert.DoesNotContain("showUnavailable", feedback);
    }

    [Fact]
    public void OscInventoryPublishesStableCaptionIdentityAlongsideTheCombinedIndex()
    {
        var publish = Between(Read("PlayerService.cs"), "private void SendSubtitleListToOsc()", "private void SendAudioTrackListToOsc()");
        Assert.Contains("[\"track_id\"] = t.TrackId", publish);
        Assert.Contains("[\"sync_key\"] = t.SyncKey", publish);
        Assert.Contains("session.MediaFileId.ToString(", publish);
    }

    [Fact]
    public void OscSubtitleSelectionIsDispatchedBeforeRunningSelectionWork()
    {
        var selection = Between(Read("PlayerService.cs"), "case \"silo-subtitle-select\":", "case \"silo-subtitle-cycle\":");
        Assert.Contains("int.TryParse(args[1], out var subIdx)", selection);
        Assert.Contains("dispatch.TryEnqueue(() => _ = SelectSubtitleByServerIndexAsync(subIdx));", selection);
        Assert.Equal(1, selection.Split("SelectSubtitleByServerIndexAsync(", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void CorrectedCuesAreBoundedBeforeWritingEvenWithoutAContentLength()
    {
        var reload = Reload();
        var header = reload.IndexOf("response.Content.Headers.ContentLength > SubtitleSidecarFile.MaxBytes", StringComparison.Ordinal);
        var streamed = reload.IndexOf("buffer.Length + read > SubtitleSidecarFile.MaxBytes", StringComparison.Ordinal);
        var write = reload.IndexOf("await File.WriteAllBytesAsync", StringComparison.Ordinal);
        Assert.True(header >= 0 && streamed > header && write > streamed);
        Assert.Contains("throw new InvalidDataException(\"Subtitle cues exceed the size limit.\")", reload);
        Assert.DoesNotContain("ReadAsByteArrayAsync", reload);
    }

    [Fact]
    public void CorrectedCueFailureCannotBorrowThePreviousSidOrCommitTiming()
    {
        var reload = Reload();
        Assert.Contains("var previousIds = mpv.GetSubtitleTrackIds().ToHashSet();", reload);
        Assert.Contains("var added = mpv.AddSubtitle", reload);
        Assert.Contains("select: false", reload);
        Assert.Contains("Where(id => !previousIds.Contains(id)).ToArray()", reload);
        Assert.Contains("externalFilename: path", reload);
        var verified = reload.IndexOf("if (!added || newSid <= 0 || !SameFile() || !SyncCurrent(ct))", StringComparison.Ordinal);
        var selected = reload.IndexOf("!= newSid)", StringComparison.Ordinal);
        var rollback = reload.IndexOf("if (!loaded && SameFile())", StringComparison.Ordinal);
        var map = reload.IndexOf("_loadedExternalSubtitleSids[index] = newSid;", StringComparison.Ordinal);
        var timing = reload.IndexOf("entry.LastAppliedTiming = timing;", StringComparison.Ordinal);
        Assert.True(verified >= 0 && selected > verified && rollback > selected && map > rollback && timing > map);
        Assert.Contains("if (newSid > 0) mpv.RemoveSubtitle(newSid);", reload);
        Assert.Contains("if (newSid > 0 && selectedSid == newSid) mpv.SetSubtitleTrack(previousSid);", reload);
        Assert.DoesNotContain("foreach (var addedSid in newIds)", reload);
        Assert.Contains("entry.Error = \"Timing changed, but the new cues couldn't load.", reload);
    }

    [Fact]
    public void LegacyPrePlayRequiresAnEmbeddedRowAndPublishesOnlyDemonstratedExecution()
    {
        var initial = Between(Read("PlayerService.cs"), "private void ResolveInitialSubtitleSelection(",
            "private async Task<PlaybackStartResponse> ReconcileInitialSubtitlePlanAsync(");
        Assert.Contains("ElementAtOrDefault(explicitSelection.Value) is { External: not true } legacyTrack", initial);
        var legacy = Read("PlayerService.LegacySubtitle.cs");
        Assert.Contains("NativeSubtitleSelection.Resolve(", legacy);
        Assert.DoesNotContain("tracks[choice.Ordinal]", legacy);
        var verify = legacy.IndexOf("if ((int)Math.Round(_mpv.GetPropertyDouble(\"sid\")) != sid)", StringComparison.Ordinal);
        var record = legacy.IndexOf("choice.Manager.RecordLocalSubtitleSelection(track.Index);", StringComparison.Ordinal);
        var commit = legacy.IndexOf("CommitExecutedSubtitleSelection(track);", StringComparison.Ordinal);
        var publish = legacy.IndexOf("SendSubtitleListToOsc();", StringComparison.Ordinal);
        Assert.True(verify >= 0 && record > verify && commit > record && publish > commit);
        Assert.Contains("_activeSubtitleServerIndex = track.Index;", legacy);
    }

    private static string Reload() => Between(Read("PlayerService.SubtitleSync.cs"),
        "private async Task ReloadCorrectedSubtitleAsync(", "private void SendSubtitleSyncState()");

    private static string Read(string name) => File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", name));

    private static string Between(string source, string first, string next)
    {
        var start = source.IndexOf(first, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf(next, start + first.Length, StringComparison.Ordinal);
        Assert.True(end > start);
        return source[start..end];
    }
}
