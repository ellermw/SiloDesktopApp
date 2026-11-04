namespace SiloPlayer.Tests;

public sealed class SubtitleReviewRegressionTests
{
    [Fact]
    public void AiContinuityRebasesOnlyWithinTheCapturedFileAndAuthority()
    {
        var owner = Read("PlayerService.SubtitleOperationOwner.cs");
        var ai = Between(owner, "internal bool IsCurrentForAi", "internal bool CanUseSession");
        Assert.Contains("if (!Stable) return false;", ai);
        Assert.Contains("current.MediaFileId != _mediaFileId", ai);
        Assert.Contains("Interlocked.CompareExchange(ref _session, current, previous)", ai);
        Assert.Contains("ReferenceEquals(current, _manager.CurrentSession)", ai);
        Assert.Contains("internal bool IsCurrent => Stable && ReferenceEquals", owner);
        var service = Read("PlayerService.cs");
        var live = Between(service, "private void ApplySubtitleTranslationCues(", "private void SendTranslationBufferingState(");
        Assert.DoesNotContain("_liveSubtitleOperationOwner?.IsCurrent == false", live);
        Assert.Contains("_liveSubtitleOperationOwner?.IsCurrentForAi == false", live);
        Assert.Contains("if (_switchingContent || IsLoading) _restorePausedAfterLoad = false;", live);
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
        Assert.Contains("var previousIds = _mpv.GetSubtitleTrackIds().ToHashSet();", reload);
        Assert.Contains("var added = _mpv.AddSubtitle", reload);
        Assert.Contains("select: false", reload);
        Assert.Contains("Where(id => !previousIds.Contains(id)).ToArray()", reload);
        var verified = reload.IndexOf("if (!added || newIds.Length != 1)", StringComparison.Ordinal);
        var selected = reload.IndexOf("!= newIds[0])", StringComparison.Ordinal);
        var rollback = reload.IndexOf("if (!loaded)", StringComparison.Ordinal);
        var map = reload.IndexOf("_loadedExternalSubtitleSids[index] = newSid;", StringComparison.Ordinal);
        var timing = reload.IndexOf("entry.LastAppliedTiming = timing;", StringComparison.Ordinal);
        Assert.True(verified >= 0 && selected > verified && rollback > selected && map > rollback && timing > map);
        Assert.Contains("foreach (var addedSid in newIds) _mpv.RemoveSubtitle(addedSid);", reload);
        Assert.Contains("_mpv.SetSubtitleTrack(previousSid);", reload);
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
