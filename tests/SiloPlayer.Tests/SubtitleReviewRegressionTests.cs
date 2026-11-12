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
        var ownerAdmission = Between(service, "private Task<bool> TrySetSubtitleTrackAndPersistForOwnerAsync(", "private async Task<bool> TrySetSubtitleTrackAndPersistAsync(");
        Assert.Contains("currentOwner.IsCurrent && currentOwner.HasCurrentSubtitleChoice", ownerAdmission);
        Assert.Contains("currentOwner: currentOwner", ownerAdmission);
        var admission = Between(service, "private async Task<bool> TrySetSubtitleTrackAndPersistAsync(", "private bool MatchesSubtitlePreference(");
        Assert.Contains("if (currentOwner == null) Interlocked.Increment(ref _manualSubtitleChoiceVersion);", admission);
        var activation = Between(admission, "bool CurrentReadChoice() =>", "void RetireReadChoice()");
        Assert.Contains("currentOwner?.IsCurrent != false && currentOwner?.HasCurrentSubtitleChoice != false", activation);
        Assert.Contains("(acceptActivation?.Invoke() ?? true)", activation);
        Assert.Contains("PrefetchSubtitleSidecarForSelectionAsync(track, CurrentReadChoice)", admission);
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
        Assert.Contains("if (_resumeAfterLiveSubtitleBuffer && activationCurrent && sameLoad &&", clear);
        Assert.Contains("(!IsWatchTogetherPlayback || !_roomPlaybackPaused) && _mpv.IsPaused)", clear);
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
        var manual = Between(Read("PlayerService.cs"), "private async Task<bool> TrySetSubtitleTrackAndPersistAsync(",
            "private bool MatchesSubtitlePreference(");
        var beforeAcceptance = manual[..manual.IndexOf("choice.Accepted = true;", StringComparison.Ordinal)];
        var failure = Between(beforeAcceptance, "if (IsSubtitleReadFailure(readOutcome) || readOutcome == SubtitlePrefetchOutcome.Retired)", "var selectionSession = manager?.CurrentSession;");
        Assert.Contains("readOutcome == SubtitlePrefetchOutcome.Retired ? SubtitlePrefetchOutcome.Error : readOutcome", failure);
        var restore = failure.IndexOf("if (!RestoreExecutedSubtitleSelection(reapply: false, currentSelection: CurrentPreflightChoice)) return false;", StringComparison.Ordinal);
        var feedback = failure.IndexOf("ShowSubtitleReadFailure(", StringComparison.Ordinal);
        Assert.True(restore >= 0 && feedback > restore);
    }

    [Fact]
    public void SupersededBitmapAfterSeekDrainRescuesOnlyTheLatestAcceptedDeferredChoice()
    {
        var burn = Between(Read("PlayerService.cs"), "private async Task<SubtitleTransportResult> SetBitmapSubtitleBurnInAsync(",
            "private void MarkSubtitleSelectionDeferred(");
        var superseded = Between(burn, "if (!ReferenceEquals(manualChoice, Volatile.Read(ref _subtitleSelectionOperation)))",
            "lock (_playbackBusyLock)");
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
    public void KnownPgsUsesOneOwnedDiskDownloadInsteadOfTheTextProbe()
    {
        var prefetch = Between(Read("PlayerService.cs"), "private async Task<SubtitlePrefetchOutcome> PrefetchSubtitleSidecarForOwnerAsync(",
            "private int ResolveNativeEmbeddedSid(");
        var bitmap = prefetch.IndexOf("if (SubtitleSidecarFile.DownloadExtension(null, url, track.Codec) == \".sup\")", StringComparison.Ordinal);
        var download = prefetch.IndexOf("return await ReadUrlFallbackAsync();", bitmap, StringComparison.Ordinal);
        var text = prefetch.IndexOf("var read = await SubtitleSidecarReader.ReadAsync(", StringComparison.Ordinal);
        Assert.True(bitmap >= 0 && download > bitmap && text > download);
        var fallback = Between(prefetch, "async Task<SubtitlePrefetchOutcome> ReadUrlFallbackAsync()", "        try\n        {");
        Assert.Contains("await SubtitleSidecarDownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, fallbackBudget.Token)", fallback);
        Assert.Contains("SubtitleSidecarFile.DownloadExtension(response.Content.Headers.ContentType?.MediaType, url, track.Codec)", fallback);
        Assert.Contains("SubtitleSidecarFile.DownloadMaxBytes(extension)", fallback);
        Assert.Contains("new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,", fallback);
        Assert.Contains("FileOptions.Asynchronous | FileOptions.SequentialScan", fallback);
        Assert.Contains("new CancellationTokenSource(SubtitleUrlFallbackTimeout, _subtitleUrlFallbackTimeProvider)", fallback);
        Assert.Contains("CreateLinkedTokenSource(bounded.Token, fallbackDeadline.Token)", fallback);
        Assert.DoesNotContain("mpv.SetSubtitleTrack", fallback);
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
        // Busy text reads stay bounded. Non-busy fallback streams to disk
        // under its own deadline, never mpv's shorter socket timeout.
        var prefetch = Between(service, "private async Task<SubtitlePrefetchOutcome> PrefetchSubtitleSidecarForOwnerAsync(", "private int ResolveNativeEmbeddedSid(");
        Assert.DoesNotContain("lock (_subtitleAddGate)", prefetch);
        Assert.DoesNotContain("mpv.AddSubtitle(", prefetch);
        Assert.Contains("SubtitleSidecarFile.DownloadExtension(", prefetch);
        var fallback = Between(prefetch, "async Task<SubtitlePrefetchOutcome> ReadUrlFallbackAsync()", "        try\n        {");
        var download = fallback.IndexOf("await SubtitleSidecarDownloadClient.GetAsync(", StringComparison.Ordinal);
        var headerLimit = fallback.IndexOf("response.Content.Headers.ContentLength > maxBytes", StringComparison.Ordinal);
        var streamLimit = fallback.IndexOf("total + read > maxBytes", StringComparison.Ordinal);
        var write = fallback.IndexOf("await file.WriteAsync(chunk.AsMemory(0, read), fallbackBudget.Token)", StringComparison.Ordinal);
        var verify = fallback.IndexOf("generation != _subtitleSidecarGeneration || !CurrentOwner()", write, StringComparison.Ordinal);
        var cache = fallback.IndexOf("_prefetchedSubtitleFiles[track.Index] = path;", StringComparison.Ordinal);
        Assert.True(download >= 0 && headerLimit > download && streamLimit > headerLimit && write > streamLimit && verify > write && cache > verify);
        Assert.Contains("if (!CurrentOwner()) return SubtitlePrefetchOutcome.Retired;", fallback);
        Assert.Contains("fallbackBudget.Token.ThrowIfCancellationRequested();", fallback);
        Assert.Contains("if (total == 0) throw new InvalidDataException", fallback);
        Assert.Contains("response.StatusCode is System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.TooManyRequests", fallback);
        Assert.Contains("return SubtitlePrefetchOutcome.Busy;", fallback);
        Assert.Contains("lock (_subtitleSyncGate)", fallback);
        Assert.Contains("lock (_subtitleTrackStateLock)", fallback);
        Assert.Contains("_subtitleSyncFiles.Add(path);", fallback);
        Assert.Contains("if (path != null) try { File.Delete(path); } catch { }", prefetch);
        Assert.DoesNotContain("_loadedExternalSubtitleSids[track.Index] =", fallback);
        Assert.DoesNotContain("mpv.SetSubtitleTrack", fallback);
        Assert.DoesNotContain("mpv.AddSubtitle", prefetch);
        Assert.Contains("SubtitleSidecarFile.DownloadMaxBytes(extension)", fallback);
        Assert.DoesNotContain("new MemoryStream", prefetch);
        Assert.Contains("loadedMediaGeneration == Volatile.Read(ref _loadedMediaInitGeneration)", prefetch);
        Assert.Contains("choiceVersion == Volatile.Read(ref _legacySubtitleChoiceVersion)", prefetch);
        Assert.Contains("playGeneration == Volatile.Read(ref _playRequestGeneration)", prefetch);
        Assert.Contains("authority.ApiClient.IsCurrentIdentityContext(identity)", prefetch);
        Assert.Contains("var watch = WatchOwnerAsync();", prefetch);
        Assert.Contains("if (!CurrentOwner()) { bounded.Cancel(); return; }", prefetch);
        Assert.Contains("ReferenceEquals(selection, Volatile.Read(ref _subtitleReadOperation)) && !selection.Token.IsCancellationRequested", prefetch);
        Assert.Contains("await watch;", prefetch);
        Assert.Contains("bounded.CancelAfter(TimeSpan.FromMinutes(10))", prefetch);
        Assert.Equal(1, prefetch.Split("SubtitleSidecarDownloadClient.GetAsync", StringSplitOptions.None).Length - 1);
        Assert.Contains("allowUrlFallback = !read.WasBusy;", prefetch);
        Assert.Equal(2, prefetch.Split("read.CanUseUrlFallback ? await ReadUrlFallbackAsync()", StringSplitOptions.None).Length - 1);
        Assert.Contains("SubtitleSidecarReader.ReadAsync(SubtitleSidecarDownloadClient", prefetch);
        Assert.Contains("if (read.Outcome == SubtitleSidecarReadOutcome.Busy) return SubtitlePrefetchOutcome.Busy;", prefetch);
        Assert.DoesNotContain("SubtitleSyncDownloadClient", prefetch);
        Assert.Contains("SubtitleSidecarDownloadClient = new() { Timeout = Timeout.InfiniteTimeSpan }", service);
        Assert.Contains("if (generation != _subtitleSidecarGeneration || !CurrentOwner()) return SubtitlePrefetchOutcome.Retired;", prefetch);
        Assert.Contains("currentOwner?.Invoke() != false", prefetch);
        var textReader = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer.Core", "Services", "SubtitleSidecarReader.cs"));
        Assert.Contains("response.Content.Headers.ContentLength > SubtitleSidecarFile.MaxBytes", textReader);
        Assert.Contains("buffer.Length + read > SubtitleSidecarFile.MaxBytes", textReader);
        Assert.Contains("SubtitleSidecarReader.ReadAsync(SubtitleSidecarDownloadClient", prefetch);
    }

    [Fact]
    public void NativePrefetchPreservesTheAppliedMappingUntilVerifiedSelectionAndRetiresWithItsFile()
    {
        var service = Read("PlayerService.cs");
        var add = Between(service, "private bool SelectSubtitleSidecar(", "private long CurrentSubtitleSidecarGeneration()");
        var ready = Between(add, "if (_prefetchedSubtitleSids.Remove(track.Index, out var ready))",
            "// A failed, oversized or unsupported download");
        var owner = add.IndexOf("if (!currentSelection()) return false;", StringComparison.Ordinal);
        var consume = add.IndexOf("_prefetchedSubtitleSids.Remove", StringComparison.Ordinal);
        Assert.True(owner >= 0 && consume > owner);
        var epoch = ready.IndexOf("ready.FileGeneration != Volatile.Read(ref _fileLoadGeneration)", StringComparison.Ordinal);
        var select = ready.IndexOf("mpv.SetSubtitleTrack(ready.Sid);", StringComparison.Ordinal);
        var verify = ready.IndexOf("if ((int)Math.Round(mpv.GetPropertyDouble(\"sid\")) != ready.Sid)", StringComparison.Ordinal);
        var commit = ready.IndexOf("_loadedExternalSubtitleSids[track.Index] = ready.Sid;", StringComparison.Ordinal);
        var retire = ready.IndexOf("mpv.RemoveSubtitle(retainedSid);", StringComparison.Ordinal);
        Assert.True(epoch >= 0 && select > epoch && verify > select && commit > verify && retire > commit);
        Assert.Contains("GetSubtitleTrackIds(externalOnly: true).Contains(ready.Sid)", ready);
        Assert.Contains("_staleExternalSubtitleIndices.Remove(track.Index);", ready);
        Assert.Contains("mpv.RemoveSubtitle(ready.Sid);", ready);
        Assert.Contains("_prefetchedSubtitleSids.Clear();", Between(service, "private void LoadSubtitles()", "private static bool IsBurnedInSubtitle("));

        var sync = Read("PlayerService.SubtitleSync.cs");
        var stop = Between(sync, "private void StopSubtitleSync()", "private void EnsureSubtitleSync()");
        Assert.Contains("ready.FileGeneration == Volatile.Read(ref _fileLoadGeneration)", stop);
        Assert.Contains("_mpv?.RemoveSubtitle(ready.Sid);", stop);
        Assert.Contains("_prefetchedSubtitleSids.Clear();", stop);
        var invalidate = Between(sync, "private void InvalidateSubtitleTimingCache(", "private bool IsActiveSyncKey(");
        Assert.Contains("_prefetchedSubtitleSids.Remove(index, out var ready)", invalidate);
        Assert.Contains("ready.FileGeneration == Volatile.Read(ref _fileLoadGeneration)", invalidate);
        Assert.Contains("_mpv?.RemoveSubtitle(ready.Sid);", invalidate);
    }

    [Fact]
    public void SidecarFailuresDistinguishDownloadLimitsAndNotifyAutomaticSelections()
    {
        var service = Read("PlayerService.cs");
        var prefetch = Between(service, "private async Task<SubtitlePrefetchOutcome> PrefetchSubtitleSidecarForOwnerAsync(", "private int ResolveNativeEmbeddedSid(");
        var reader = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer.Core", "Services", "SubtitleSidecarReader.cs"));
        Assert.Contains("response.Content.Headers.ContentLength > SubtitleSidecarFile.MaxBytes", reader);
        Assert.Contains("buffer.Length + read > SubtitleSidecarFile.MaxBytes", reader);
        Assert.Contains("CanUseUrlFallback", reader);
        Assert.Contains("if (read.Failure == SubtitleSidecarReadFailure.DownloadLimit) return SubtitlePrefetchOutcome.DownloadLimit;", prefetch);
        Assert.Contains("SubtitlePrefetchOutcome.BitmapDownloadLimit : SubtitlePrefetchOutcome.DownloadLimit", prefetch);
        Assert.Contains("if (response.Content.Headers.ContentLength > maxBytes) return limitOutcome;", prefetch);
        Assert.Contains("if (total + read > maxBytes) return limitOutcome;", prefetch);
        Assert.Contains("read.Failure == SubtitleSidecarReadFailure.Timeout ? SubtitlePrefetchOutcome.TimedOut : SubtitlePrefetchOutcome.Error", prefetch);
        Assert.Contains("return CurrentOwner() ? SubtitlePrefetchOutcome.TimedOut : SubtitlePrefetchOutcome.Retired;", prefetch);
        Assert.Contains("fallbackError is OperationCanceledException ? SubtitlePrefetchOutcome.TimedOut : SubtitlePrefetchOutcome.Error", prefetch);
        var manual = Between(service, "private async Task<bool> TrySetSubtitleTrackAndPersistAsync(", "private bool MatchesSubtitlePreference(");
        Assert.Contains("var readOutcome = await PrefetchSubtitleSidecarForSelectionAsync(track, CurrentReadChoice);", manual);
        Assert.Contains("RetireReadChoice(); ShowSubtitleReadFailure(readOutcome);", manual);
        var loaded = Between(service, "private async Task<bool> SelectSubtitleByServerIndexAsync(", "public int ActiveSubtitleServerIndex");
        Assert.Contains("var readOutcome = await PrefetchSubtitleSidecarForSelectionAsync(track, CurrentSelection);", loaded);
        foreach (var selection in new[] { manual, loaded })
        {
            Assert.Contains("if (IsSubtitleReadFailure(readOutcome) || readOutcome == SubtitlePrefetchOutcome.Retired)", selection);
            Assert.Contains("ShowSubtitleReadFailure(readOutcome == SubtitlePrefetchOutcome.Retired ? SubtitlePrefetchOutcome.Error : readOutcome);", selection);
        }
        Assert.Contains("RestoreExecutedSubtitleSelection(reapply: true, failedTrack: track, currentSelection: CurrentSelection)", loaded);
        var lifecycle = Read("PlayerService.SubtitleSelection.cs");
        foreach (var outcome in new[] { "Busy", "Error", "DownloadLimit", "BitmapDownloadLimit", "Timeout", "TooLarge", "TimedOut" })
            Assert.Contains("SubtitlePrefetchOutcome." + outcome, Between(lifecycle, "private static bool IsSubtitleReadFailure(", "private void ShowSubtitleReadFailure("));
        var feedback = Between(lifecycle, "private void ShowSubtitleReadFailure(", "private void ShowSubtitleReadBusy()");
        Assert.Contains("ShowSubtitleReadBusy();", feedback);
        Assert.Contains("SubtitlePrefetchOutcome.TooLarge =>", feedback);
        Assert.Contains("16 MiB download limit. Choose another subtitle or turn subtitles off.", feedback);
        Assert.Contains("256 MiB download limit. Choose another subtitle or turn subtitles off.", feedback);
        Assert.Contains("SubtitlePrefetchOutcome.Timeout or SubtitlePrefetchOutcome.TimedOut =>", feedback);
        Assert.Contains("Subtitle download timed out. The server may still be extracting the track. Try again", feedback);
        Assert.Contains("checking your connection and available disk space.", feedback);
        Assert.Contains("osc-set-active-subtitle\", _activeSubtitleServerIndex.ToString", feedback);
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

    [Fact]
    public void PublishedSubtitleInventoryIsRetriedWithBackoffAndNeverReportedAsAPlaybackFailure()
    {
        // PlayerService requires WinUI/libmpv. Guard the failure route here: a
        // published plan that mpv never applied is retried after a delay, and
        // a spent budget leaves the transport mpv is playing alone.
        var ready = Read("PlayerService.SubtitleReady.cs");
        Assert.Contains("private const int SubtitleInventoryApplicationRetries = 2;", ready);
        var recover = Between(ready, "void RecoverUnappliedSubtitleInventory()", "// Serialize even in-place merges against inventory publication.");
        Assert.Contains("!entered || token.IsCancellationRequested || _pendingSubtitleInventoryApplication is not { } unapplied", recover);
        var budget = recover.IndexOf("if (++unapplied.Failures > SubtitleInventoryApplicationRetries) return;", StringComparison.Ordinal);
        var delay = recover.IndexOf("Task.Delay(SubtitleInventoryApplicationRetryDelay * unapplied.Failures, token)", StringComparison.Ordinal);
        var defer = recover.IndexOf("DeferSubtitleReadyRefresh(announcement, owner, eventSessionId, fileId);", StringComparison.Ordinal);
        Assert.True(budget >= 0 && delay > budget && defer > delay);
        // The retry must never replay immediately from the failing attempt.
        Assert.DoesNotContain("DeferSubtitleReadyRefresh(payload, owner, eventSessionId, fileId);", recover);
        // An announcement cannot tear down, report or terminate healthy playback.
        foreach (var escalation in new[] { "RecoverInterruptedStreamAsync", "RunStreamRecoveryAsync", "RecoverPlaybackFailureAsync", "EnterPlaybackTerminalState" })
            Assert.DoesNotContain(escalation, ready);
        var failure = ready[ready.IndexOf("catch (Exception ex)", StringComparison.Ordinal)..];
        Assert.Contains("RecoverUnappliedSubtitleInventory();", failure);
    }

    [Fact]
    public void UnpublishedSubtitleInventoryRefreshIsRetriedWithBackoffInsteadOfDroppingItsAnnouncement()
    {
        // PlayerService requires WinUI/libmpv. Guard the failure route here: a
        // replan that fails or times out before publication re-defers its
        // announcement after a delay, within the same bounded budget.
        var ready = Read("PlayerService.SubtitleReady.cs");
        var retry = Between(ready, "void RetryUnpublishedSubtitleInventory()", "void RecoverUnappliedSubtitleInventory()");
        Assert.Contains("!entered || refreshed || token.IsCancellationRequested || _pendingSubtitleInventoryApplication != null", retry);
        var budget = retry.IndexOf("if (++unpublished.Failures > SubtitleInventoryApplicationRetries) return;", StringComparison.Ordinal);
        var delay = retry.IndexOf("Task.Delay(SubtitleInventoryApplicationRetryDelay * unpublished.Failures, token)", StringComparison.Ordinal);
        var defer = retry.IndexOf("DeferSubtitleReadyRefresh(announcement, owner, eventSessionId, fileId);", StringComparison.Ordinal);
        Assert.True(budget >= 0 && delay > budget && defer > delay);
        // The retry must never replay immediately from the failing attempt.
        Assert.DoesNotContain("DeferSubtitleReadyRefresh(payload, owner, eventSessionId, fileId);", retry);
        // Published inventory renews the budget and retires the pending retries.
        var publish = ready.IndexOf("_pendingSubtitleInventoryApplication ??= new(owner, appliedSession, nativeFile, file);", StringComparison.Ordinal);
        Assert.True(publish >= 0 && ready.IndexOf("_unpublishedSubtitleInventoryRefresh = null;", publish, StringComparison.Ordinal) > publish);
        // A request timeout is a cancellation that superseded nothing.
        Assert.Contains("catch (OperationCanceledException) { if (!RetryIfSuperseded()) RetryUnpublishedSubtitleInventory(); }", ready);
        var failure = ready[ready.IndexOf("catch (Exception ex)", StringComparison.Ordinal)..];
        Assert.Contains("RetryUnpublishedSubtitleInventory();", failure);
    }

    [Fact]
    public void LoadedSidecarCachesRemainASelectionRouteForARowWhoseUrlARefreshBlanked()
    {
        // PlayerService requires WinUI/libmpv. Guard the route here: a row kept
        // playable for its loaded SID/bytes stays selectable in the published
        // menus and the picker, and service caches are read before the
        // manager's inventory gate.
        var service = Read("PlayerService.cs");
        var cached = Between(service, "private HashSet<int> CachedSubtitleSidecarIndices()", "private static bool HasSubtitleSelectionRoute(");
        Assert.Contains("_loadedExternalSubtitleSids.Keys.Where(index => !_staleExternalSubtitleIndices.Contains(index))", cached);
        Assert.Contains(".Concat(_prefetchedSubtitleFiles.Keys)", cached);
        Assert.Contains("pair.Value.FileGeneration == fileLoadGeneration", cached);
        var menu = Between(service, "internal SubtitleMenuSnapshot? CaptureSubtitleMenuSnapshot()", "private bool HasSubtitleSelectionRoute(SubtitleTrackInfo track)");
        var state = menu.IndexOf("lock (_subtitleTrackStateLock)", StringComparison.Ordinal);
        var inventory = menu.IndexOf("lock (manager.SubtitleInventorySyncRoot)", StringComparison.Ordinal);
        var union = menu.IndexOf("routes.SidecarIndices.UnionWith(CachedSubtitleSidecarIndices());", StringComparison.Ordinal);
        Assert.True(state >= 0 && inventory > state && union > inventory);
        var route = Between(service, "private bool HasSubtitleSelectionRoute(SubtitleTrackInfo track)", "private HashSet<int> CachedSubtitleSidecarIndices()");
        state = route.IndexOf("lock (_subtitleTrackStateLock)", StringComparison.Ordinal);
        var loaded = route.IndexOf("if (!track.Unplayable && CachedSubtitleSidecarIndices().Contains(track.Index)) return true;", StringComparison.Ordinal);
        inventory = route.IndexOf("lock (manager.SubtitleInventorySyncRoot)", StringComparison.Ordinal);
        Assert.True(state >= 0 && loaded > state && inventory > loaded);
        Assert.Contains("lock (_subtitleTrackStateLock) sidecarIndices.UnionWith(CachedSubtitleSidecarIndices());",
            Read("PlayerService.NativeSubtitles.cs"));
    }

    [Fact]
    public void BothStoredListRefreshesRetainDownloadsWithoutStoredIdentityByOneRule()
    {
        // The public and owner-scoped refreshes read the same stored list. A
        // download without stored keys must survive both the same way.
        var refresh = Read("PlayerService.SubtitleRefresh.cs");
        const string rule = "private void RetainDownloadsWithoutStoredIdentity(";
        const string retain = "RetainDownloadsWithoutStoredIdentity(mediaFileId, previousDownloads, reserved, downloaded, baseIndex);";
        const string rebind = "SubtitleTrackInfo? Rebind(SubtitleTrackInfo? track)";
        var publicRefresh = Between(refresh, "public async Task RefreshSubtitlesAfterAiAsync(", rule);
        var ownedRefresh = Between(Read("PlayerService.cs"), "internal async Task RefreshSubtitlesAfterAiForOwnerAsync(",
            "public Func<bool> BeginSubtitleActivationIntent()");
        foreach (var entry in new[] { publicRefresh, ownedRefresh })
        {
            // Retention precedes the rebind of the active selection and its caches.
            var kept = entry.IndexOf(retain, StringComparison.Ordinal);
            Assert.True(kept >= 0 && kept < entry.IndexOf(rebind, StringComparison.Ordinal));
            Assert.Contains("downloaded.Contains(track)) return track.Unplayable ? null : track;", entry);
        }
        var shared = refresh[refresh.IndexOf(rule, StringComparison.Ordinal)..];
        Assert.Contains("IsDownloadWithoutStoredIdentity(track) && !downloaded.Contains(track)", shared);
        // A re-indexed row never keeps the URL that addressed its old ordinal.
        var loadedBytes = shared.IndexOf("_loadedExternalSubtitleSids.GetValueOrDefault(legacy.Index) > 0", StringComparison.Ordinal);
        var url = shared.IndexOf("legacy.Url = \"\";", StringComparison.Ordinal);
        var index = shared.IndexOf("legacy.Index = free;", StringComparison.Ordinal);
        Assert.True(loadedBytes >= 0 && url > loadedBytes && index > url);
    }

    [Fact]
    public void InPlaceInventoryAdoptionKeepsTheAppliedSidStaleAndNeverRemovesTheSidOnScreen()
    {
        var ready = Read("PlayerService.SubtitleReady.cs");
        var adopt = Between(ready, "private void AdoptSubtitleInventoryInPlace(", "private async Task ApplySubtitleReadyAsync(");
        var retained = adopt.IndexOf("_loadedExternalSubtitleSids[retainedIndex] = activeSid;", StringComparison.Ordinal);
        Assert.True(retained >= 0 && adopt.IndexOf("_staleExternalSubtitleIndices.Add(retainedIndex);", StringComparison.Ordinal) > retained);
        Assert.Contains("(int)Math.Round(_mpv.GetPropertyDouble(\"sid\")) != sid) _mpv.RemoveSubtitle(sid);", adopt);
    }

    [Fact]
    public void InPlaceAnnouncementMergeRetiresTheCachesOfARowWhoseUrlOrDeliveryItRewrites()
    {
        // PlayerService requires WinUI/libmpv. Guard the merge route here: the
        // row's delivery facts are captured before the merge mutates it, and a
        // rewrite retires the bytes read under them before the menu republishes.
        var ready = Read("PlayerService.SubtitleReady.cs");
        var apply = Between(ready, "private async Task ApplySubtitleReadyAsync(", "private void InvalidateRewrittenSubtitleCache(");
        var capture = apply.IndexOf("var (readUrl, readDelivery) = (rewritten?.Url, rewritten?.Delivery);", StringComparison.Ordinal);
        var merge = apply.IndexOf("if (manager.TryApplySubtitleTrack(session, track))", StringComparison.Ordinal);
        var changed = apply.IndexOf("(readUrl ?? \"\") != rewritten.Url || readDelivery != rewritten.Delivery", StringComparison.Ordinal);
        var retire = apply.IndexOf("InvalidateRewrittenSubtitleCache(rewritten.Index);", StringComparison.Ordinal);
        Assert.True(capture >= 0 && merge > capture && changed > merge && retire > changed);
        Assert.True(apply.IndexOf("SendSubtitleListToOsc();", retire, StringComparison.Ordinal) > retire);

        var cache = ready[ready.IndexOf("private void InvalidateRewrittenSubtitleCache(", StringComparison.Ordinal)..];
        Assert.Contains("_prefetchedSubtitleFiles.Remove(index, out var path)", cache);
        Assert.Contains("try { File.Delete(path); } catch { }", cache);
        Assert.Contains("_prefetchedSubtitleSids.Remove(index, out var ready)", cache);
        // The applied SID stays mapped and visible, but stale. Any other is
        // retired, unless it is the SID mpv still shows.
        var active = cache.IndexOf("if (index == _activeSubtitleServerIndex)", StringComparison.Ordinal);
        var stale = cache.IndexOf("if (_loadedExternalSubtitleSids.ContainsKey(index)) _staleExternalSubtitleIndices.Add(index);", StringComparison.Ordinal);
        var inactive = cache.IndexOf("else if (_loadedExternalSubtitleSids.Remove(index, out var sid))", StringComparison.Ordinal);
        Assert.True(active >= 0 && stale > active && inactive > stale);
        Assert.Contains("(int)Math.Round(_mpv.GetPropertyDouble(\"sid\")) != sid) _mpv.RemoveSubtitle(sid);", cache[inactive..]);
    }

    [Fact]
    public void OwnerScopedAiRefreshSkipsAnUnplayableCompletionRowAndClearsARefusedCompletion()
    {
        // The owner-scoped refresh marks displaced/uncertain rows unplayable,
        // as the public refresh does. Neither may activate such a row, nor
        // leave its completion installed after a refused attempt.
        var refresh = Between(Read("PlayerService.cs"), "internal async Task RefreshSubtitlesAfterAiForOwnerAsync(",
            "public Func<bool> BeginSubtitleActivationIntent()");
        Assert.Contains("downloaded.FirstOrDefault(track => !track.Unplayable && track.Id == preferredSubtitleId)", refresh);
        Assert.Contains("downloaded.SingleOrDefault(track => !track.Unplayable && track.Id == preferredSubtitleId)",
            Read("PlayerService.SubtitleRefresh.cs"));
        var install = refresh.IndexOf("completion = _storedSubtitleCompletion;", StringComparison.Ordinal);
        var attempt = refresh.IndexOf("var selected = await TrySetSubtitleTrackAndPersistAsync(", StringComparison.Ordinal);
        var refused = refresh.IndexOf("if (!selected && completion != null && ReferenceEquals(_storedSubtitleCompletion, completion))", StringComparison.Ordinal);
        var leave = refresh.IndexOf("if (!selected || !Current()) return;", StringComparison.Ordinal);
        Assert.True(install >= 0 && attempt > install && refused > attempt && leave > refused);
        Assert.Contains("_storedSubtitleCompletion = null;", refresh[refused..leave]);
    }

    [Fact]
    public void BothStoredListRefreshesCarryARequestedStoredIdOntoThePlanOnlyRowItLandsOn()
    {
        // PlayerService requires WinUI/libmpv. Guard the route here: a plan-only
        // download holds no stored id, so a pick by stored id that the list
        // places on that row must still find it, through both entry points.
        var publicRefresh = Between(Read("PlayerService.SubtitleRefresh.cs"), "public async Task RefreshSubtitlesAfterAiAsync(",
            "private static void RetainDownloadsAnnouncedDuringRead(");
        var ownedRefresh = Between(Read("PlayerService.cs"), "internal async Task RefreshSubtitlesAfterAiForOwnerAsync(",
            "public Func<bool> BeginSubtitleActivationIntent()");
        foreach (var refresh in new[] { publicRefresh, ownedRefresh })
        {
            var landed = refresh.IndexOf("if (previous == null && StoredEntryMayBeCanonicalDownload(unknownCanonical[0], entry.Language))", StringComparison.Ordinal);
            Assert.True(landed >= 0);
            var requested = refresh.IndexOf("if (unique && matches.Length == 0 && entry.Id == preferredSubtitleId &&", landed, StringComparison.Ordinal);
            var kept = refresh.IndexOf("return unknownCanonical[0];", landed, StringComparison.Ordinal);
            // Only an unambiguous pick on a trusted layout names the row; any
            // other refresh still leaves it without a stored identity.
            Assert.True(requested > landed && kept > requested);
            Assert.Contains("canonicalOrdinalsAgree && !unknownCanonical[0].Unplayable)", refresh[requested..kept]);
            var carried = refresh.IndexOf("if (pickedCanonical != null && displacedCanonical.Count == 0) pickedCanonical.Id = preferredSubtitleId;", kept, StringComparison.Ordinal);
            var pick = refresh.IndexOf("track => !track.Unplayable && track.Id == preferredSubtitleId)", StringComparison.Ordinal);
            Assert.True(carried > kept && pick > carried);
        }
    }

    [Fact]
    public void BothStoredListRefreshesReplanOnceWhenAnUncertainLayoutWithholdsTheRequestedSubtitle()
    {
        // PlayerService requires WinUI/libmpv. Guard the route here: a pick whose
        // entry the list's layout left without a route asks for one authoritative
        // inventory, is started over on it, and otherwise tells the viewer. That
        // covers another language on a plan-only row's ordinal, through both
        // entry points.
        var publicRefresh = Between(Read("PlayerService.SubtitleRefresh.cs"), "public async Task RefreshSubtitlesAfterAiAsync(",
            "private static void RetainDownloadsAnnouncedDuringRead(");
        var ownedRefresh = Between(Read("PlayerService.cs"), "internal async Task RefreshSubtitlesAfterAiForOwnerAsync(",
            "public Func<bool> BeginSubtitleActivationIntent()");
        foreach (var refresh in new[] { publicRefresh, ownedRefresh })
        {
            // The inventory a pick is started over on is its final answer.
            var answered = refresh.IndexOf("if (preferredSubtitleId is long stored) MarkStoredSubtitlePickReplanned(applied, stored);", StringComparison.Ordinal);
            var restarted = refresh.IndexOf("DeferUntilSubtitleReadyIdle(() =>", StringComparison.Ordinal);
            Assert.True(answered >= 0 && restarted > answered);
            var uncertain = refresh.IndexOf("uncertainPick = (displacedCanonical.Count > 0 || !canonicalOrdinalsAgree) &&", StringComparison.Ordinal);
            Assert.True(uncertain > restarted);
            Assert.Contains("response.Subtitles.Any(entry => entry.Id == preferredSubtitleId);", refresh[uncertain..]);
            var pick = refresh.IndexOf("track => !track.Unplayable && track.Id == preferredSubtitleId)", uncertain, StringComparison.Ordinal);
            var fallback = refresh.IndexOf("else if (preferred == null && uncertainPick && preferredSubtitleId is long requested && Current())", StringComparison.Ordinal);
            var replan = refresh.IndexOf("await ReplanForStoredSubtitlePickAsync(manager, session, requested).ConfigureAwait(false)", StringComparison.Ordinal);
            Assert.True(pick > uncertain && fallback > pick && replan > fallback);
            // Nothing applied, or the pick unmatched there too: never a silent drop.
            Assert.True(refresh.IndexOf("ShowSubtitleSelectionUnavailable();", replan, StringComparison.Ordinal) > replan);
        }
        // An owner holds no registration to start it over: it follows the plan itself.
        var followed = ownedRefresh.IndexOf("MarkStoredSubtitlePickReplanned(published, requested);", StringComparison.Ordinal);
        Assert.True(followed >= 0 && ownedRefresh.IndexOf(
            "await RefreshSubtitlesAfterAiForOwnerAsync(mediaFileId, preferredSubtitleId, currentOwner, mayActivate, isCurrent)",
            followed, StringComparison.Ordinal) > followed);

        var ready = Read("PlayerService.SubtitleReady.cs");
        var request = Between(ready, "private async Task<PlaybackStartResponse?> ReplanForStoredSubtitlePickAsync(",
            "private static bool SubtitleInventoryRequiresTransportReload(");
        // One replan per pick and inventory, and only where v3 can supply one.
        var bound = request.IndexOf("if (session.ProtocolVersion < 3 || _replannedStoredSubtitlePicks is { } picks &&", StringComparison.Ordinal);
        var replanned = request.IndexOf("await ApplySubtitleReadyAsync(", StringComparison.Ordinal);
        Assert.True(bound >= 0 && replanned > bound);
        Assert.Contains("ReferenceEquals(picks.Session, session) && picks.SubtitleIds.Contains(subtitleId)) return null;", request);
        // Only an inventory applied in place of the read response counts.
        Assert.Contains("_pendingSubtitleInventoryApplication == null ? applied : null;", request);
    }

    [Fact]
    public void BothStoredListRefreshesTreatEveryWithheldRouteForTheRequestedSubtitleAsUncertain()
    {
        // PlayerService requires WinUI/libmpv. Guard the route here: the requested
        // entry's own row can withhold its route too (an ordinal a canonical row
        // absent from the list still holds, a repeated id, an unplayable row), and
        // a plan-only row is bound only to an entry with its forced and
        // hearing-impaired facts. Each takes the same replan or notice, through
        // both entry points.
        var publicRefresh = Between(Read("PlayerService.SubtitleRefresh.cs"), "public async Task RefreshSubtitlesAfterAiAsync(",
            "private static void RetainDownloadsAnnouncedDuringRead(");
        var ownedRefresh = Between(Read("PlayerService.cs"), "internal async Task RefreshSubtitlesAfterAiForOwnerAsync(",
            "public Func<bool> BeginSubtitleActivationIntent()");
        foreach (var refresh in new[] { publicRefresh, ownedRefresh })
        {
            var requested = refresh.IndexOf("if (unique && matches.Length == 0 && entry.Id == preferredSubtitleId &&", StringComparison.Ordinal);
            Assert.True(requested >= 0);
            var bound = refresh.IndexOf("pickedCanonical = unknownCanonical[0];", requested, StringComparison.Ordinal);
            Assert.True(bound > requested);
            Assert.Contains("entry.Forced == unknownCanonical[0].Forced && entry.HearingImpaired == unknownCanonical[0].HearingImpaired &&",
                refresh[requested..bound]);
            // Judged on the rows the refresh publishes, after retention.
            var retained = refresh.IndexOf("RetainDownloadsWithoutStoredIdentity(mediaFileId, previousDownloads, reserved, downloaded, baseIndex);", StringComparison.Ordinal);
            var withheld = refresh.IndexOf("uncertainPick |= response.Subtitles.Any(entry => entry.Id == preferredSubtitleId) &&", StringComparison.Ordinal);
            var fallback = refresh.IndexOf("else if (preferred == null && uncertainPick && preferredSubtitleId is long requested && Current())", StringComparison.Ordinal);
            Assert.True(retained >= 0 && withheld > retained && fallback > withheld);
            Assert.Contains("!downloaded.Any(track => !track.Unplayable && track.Id == preferredSubtitleId);", refresh[withheld..fallback]);
        }
    }

    [Fact]
    public void StoredPickFallbackWaitsForItsDeferredAnnouncementAndNeverAdoptsAnUnrelatedReplan()
    {
        // PlayerService requires WinUI/libmpv. Guard the route here: a fallback
        // announcement that deferred itself behind a seek restart, or scheduled a
        // retry, applied nothing yet but still owes the pick its inventory. The
        // refresh stays registered for it without a notice, and a response a seek
        // published meanwhile is never taken for that inventory.
        var ready = Read("PlayerService.SubtitleReady.cs");
        var defer = Between(ready, "private void DeferSubtitleReadyRefresh(", "private DeferredSubtitleReadyAnnouncement BeginSubtitleReadyDeferral(");
        var owed = defer.IndexOf("var deferral = BeginSubtitleReadyDeferral(sessionId, fileId);", StringComparison.Ordinal);
        Assert.True(owed >= 0 && defer.IndexOf("DeferUntilSubtitleReadyIdle(() =>", StringComparison.Ordinal) > owed);
        // The deferral ends only once its replay has applied, deferred again or been dropped.
        Assert.Contains(".ContinueWith(_ => EndSubtitleReadyDeferral(deferral), TaskScheduler.Default);", defer);
        Assert.Contains("else EndSubtitleReadyDeferral(deferral);", defer);
        foreach (var retry in new[]
        {
            Between(ready, "void RetryUnpublishedSubtitleInventory()", "void RecoverUnappliedSubtitleInventory()"),
            Between(ready, "void RecoverUnappliedSubtitleInventory()", "// Serialize even in-place merges against inventory publication."),
        })
        {
            var scheduled = retry.IndexOf("var deferral = BeginSubtitleReadyDeferral(eventSessionId, fileId);", StringComparison.Ordinal);
            var delay = retry.IndexOf("Task.Delay(SubtitleInventoryApplicationRetryDelay", StringComparison.Ordinal);
            Assert.True(scheduled >= 0 && delay > scheduled &&
                retry.IndexOf("EndSubtitleReadyDeferral(deferral);", delay, StringComparison.Ordinal) > delay);
        }
        // An applied inventory answers every announcement deferred for it, under
        // the lock and before the registered refreshes are read.
        var restart = Between(ready, "private void RestartSupersededSubtitleListRefreshes(", "foreach (var superseded in reading) superseded(applied);");
        var applied = restart.IndexOf("_appliedSubtitleInventory = applied;", StringComparison.Ordinal);
        var settled = restart.IndexOf("_deferredSubtitleReadyAnnouncements.RemoveWhere(deferral =>", StringComparison.Ordinal);
        var read = restart.IndexOf("reading = _storedSubtitleListRefreshes.ToArray();", StringComparison.Ordinal);
        Assert.True(applied >= 0 && settled > applied && read > settled);
        // Only an inventory an announcement applied during the request answers the pick.
        var request = Between(ready, "private async Task<PlaybackStartResponse?> ReplanForStoredSubtitlePickAsync(",
            "private static bool SubtitleInventoryRequiresTransportReload(");
        var before = request.IndexOf("var answered = _appliedSubtitleInventory;", StringComparison.Ordinal);
        var asked = request.IndexOf("await ApplySubtitleReadyAsync(", StringComparison.Ordinal);
        Assert.True(before >= 0 && asked > before &&
            request.IndexOf("return !ReferenceEquals(_appliedSubtitleInventory, answered) &&", asked, StringComparison.Ordinal) > asked);
        // An owner's refresh is registered under the lock that settles the deferral.
        var waits = Between(ready, "private bool AwaitsDeferredSubtitleReadyAnnouncement(", "private void DeferUntilSubtitleReadyIdle(");
        var locked = waits.IndexOf("lock (_subtitleReadyRefreshLock)", StringComparison.Ordinal);
        Assert.True(locked >= 0 &&
            waits.IndexOf("if (unregistered != null) _storedSubtitleListRefreshes.Add(unregistered);", locked, StringComparison.Ordinal) > locked);

        var publicRefresh = Between(Read("PlayerService.SubtitleRefresh.cs"), "public async Task RefreshSubtitlesAfterAiAsync(",
            "private static void RetainDownloadsAnnouncedDuringRead(");
        var ownedRefresh = Between(Read("PlayerService.cs"), "internal async Task RefreshSubtitlesAfterAiForOwnerAsync(",
            "public Func<bool> BeginSubtitleActivationIntent()");
        foreach (var refresh in new[] { publicRefresh, ownedRefresh })
        {
            var replan = refresh.IndexOf("await ReplanForStoredSubtitlePickAsync(manager, session, requested).ConfigureAwait(false)", StringComparison.Ordinal);
            var waiting = refresh.IndexOf("deferred = AwaitsDeferredSubtitleReadyAnnouncement(session", replan, StringComparison.Ordinal);
            var notice = refresh.IndexOf("if (!deferred && Current()) ShowSubtitleSelectionUnavailable();", replan, StringComparison.Ordinal);
            Assert.True(replan >= 0 && waiting > replan && notice > waiting);
            // The refresh that ends on a deferral keeps its registration.
            Assert.True(refresh.IndexOf("var awaiting = !handedOver && (deferred || AwaitsSubtitleInventoryApplication(manager, session));",
                notice, StringComparison.Ordinal) > notice);
        }
        Assert.Contains("deferred = AwaitsDeferredSubtitleReadyAnnouncement(session, currentOwner != null ? superseded : null);", ownedRefresh);
        // Started over, an owner's refresh keeps its owner.
        Assert.Contains("_ = RefreshSubtitlesAfterAiForOwnerAsync(mediaFileId, preferredSubtitleId, currentOwner, mayActivate, isCurrent);", ownedRefresh);
    }

    [Fact]
    public void SubtitleInventoryTransportReloadChecksOwnershipUnderTheRestartGateBeforeStoppingTheOutgoingProxies()
    {
        // PlayerService requires WinUI/libmpv. Guard the route here: preparing a
        // transport stops the proxies the playing file reads from, so the last
        // ownership check precedes it under the gate a seek restart holds, and
        // nothing between preparation and the load can leave without loading.
        var apply = Between(Read("PlayerService.SubtitleReady.cs"), "private async Task ApplySubtitleReadyAsync(",
            "private void InvalidateRewrittenSubtitleCache(");
        var gate = apply.IndexOf("await _transportRestartGate.WaitAsync(token).ConfigureAwait(false);", StringComparison.Ordinal);
        Assert.True(gate >= 0);
        var owned = apply.IndexOf("if (!Current() || !SameTransport())", gate, StringComparison.Ordinal);
        var prepare = apply.IndexOf("await PreparePlaybackTransportAsync(replacement, version, streamUrl, position, token)", StringComparison.Ordinal);
        var load = apply.IndexOf("BeginMpvLoad(prepared, CaptureUserPausedState());", StringComparison.Ordinal);
        var release = apply.IndexOf("finally { _transportRestartGate.Release(); }", StringComparison.Ordinal);
        Assert.True(owned > gate && prepare > owned && load > prepare && release > load);
        Assert.DoesNotContain("return;", apply[prepare..load]);
        // Reconciling a deferred selection can re-enter the restart gate.
        Assert.True(apply.IndexOf("await ReconcileDeferredSubtitleSelectionAsync(manager, replacement).ConfigureAwait(false);",
            release, StringComparison.Ordinal) > release);
    }

    [Fact]
    public void ManualSubtitleReadRetiredByTheLoadedFileInitializerIsRetainedInsteadOfRefused()
    {
        // PlayerService requires WinUI/libmpv. Guard the route here: a waiting pick
        // can resume while FILE_LOADED's initializer runs. LoadSubtitles cancels its
        // read before advancing the sidecar generation, and the initializer's read
        // of the grant it reapplies replaces that read without advancing it. Both
        // the pre-acceptance and the draft read then wait for the initializer and
        // read again, instead of refusing the viewer's latest choice.
        var admission = Between(Read("PlayerService.cs"), "private async Task<bool> TrySetSubtitleTrackAndPersistAsync(",
            "private bool MatchesSubtitlePreference(");
        var preflight = admission.IndexOf("if (readLoadGeneration != Volatile.Read(ref _loadedMediaInitGeneration) ||", StringComparison.Ordinal);
        var draft = admission.IndexOf("if (readGeneration != Volatile.Read(ref _loadedMediaInitGeneration) ||", StringComparison.Ordinal);
        Assert.True(preflight >= 0 && draft > preflight);
        foreach (var read in new[] { preflight, draft })
        {
            var retained = admission.IndexOf("readInitialization != null && readOutcome == SubtitlePrefetchOutcome.Retired ||", read, StringComparison.Ordinal);
            var wait = admission.IndexOf("Volatile.Read(ref _loadedMediaInitCts) != null))", read, StringComparison.Ordinal);
            var refused = admission.IndexOf("readOutcome == SubtitlePrefetchOutcome.Retired)", read, StringComparison.Ordinal);
            Assert.True(retained > read && wait > retained && refused > wait);
        }
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
