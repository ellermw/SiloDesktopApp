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
