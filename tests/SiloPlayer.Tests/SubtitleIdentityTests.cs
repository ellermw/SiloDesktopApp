using System.Text.Json;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SubtitleIdentityTests
{
    [Theory]
    [InlineData(42, true)]
    [InlineData(43, true)]
    [InlineData(42, false)]
    [InlineData(43, false)]
    public void ExecutedChoiceRestorationSurvivesInventoryReplacementWithoutReusingAnIndex(int fileId, bool available)
    {
        WithManager(manager =>
        {
            var applied = new SubtitleTrackInfo { Index = 2, TrackId = "applied", Language = "eng" };
            var original = new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls = [applied, new() { Index = 3, TrackId = "pending", Language = "spa" }] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, original);
            manager.RecordLocalSubtitleSelection(3);
            var replacement = new PlaybackStartResponse { MediaFileId = fileId, SubtitleUrls = [new() { Index = available ? 2 : 9, TrackId = "different", Language = "jpn" }] };
            if (available) replacement.SubtitleUrls.Add(new() { Index = 8, TrackId = "applied", Language = "eng" });
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, replacement);

            manager.RecordExecutedSubtitleSelection(applied, original.MediaFileId);

            Assert.Equal(available ? 8 : -1, manager.SubtitleSelectionForLoad);
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, original);
            Assert.Equal(2, manager.SubtitleSelectionForLoad);
        });
    }

    [Fact]
    public void ExecutedOffRestorationOverridesAnUnappliedChoice()
    {
        WithManager(manager =>
        {
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                new PlaybackStartResponse { MediaFileId = 43, SelectedSubtitleTrackId = "pending", SubtitleUrls = [new() { Index = 3, TrackId = "pending" }] });
            manager.RecordLocalSubtitleSelection(3);
            manager.RecordExecutedSubtitleSelection(null, 42);
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
        });
    }

    [Fact]
    public void RefusalRestorationRetiresOnlyItsPendingChoiceAndPreservesPriorIntent()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private bool RestoreExecutedSubtitleSelection(bool reapply,", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("private void ShowSubtitleSelectionUnavailable(", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var method = source[start..end];
        var ownerCheck = method.IndexOf("if (!restoreOwner()) return false;", StringComparison.Ordinal);
        var retirement = method.IndexOf("refused.Accepted = false;", StringComparison.Ordinal);
        var clearPending = method.IndexOf("_pendingInitialServerSubtitleIndex = null;", StringComparison.Ordinal);
        Assert.True(ownerCheck >= 0 && retirement > ownerCheck && clearPending > retirement);
        Assert.Contains("currentSelection?.Invoke() != false", method);
        Assert.Contains("ReferenceEquals(preferenceOwner, _pendingSubtitlePreferenceOwner)", method);
        Assert.Contains("previousIntent = preferenceOwner?.PreviousIntent", method);
        Assert.Contains("_playbackManager!.RestoreLocalSubtitleIntent(previousIntent);", method);
        Assert.DoesNotContain("RecordLocalSubtitleSelection(", method);
        Assert.Contains("CommitExecutedSubtitleSelection(retained);", method);
        Assert.Contains("SelectSubtitleTrackForOwner(retained, restoreOwner)", method);
    }

    [Fact]
    public void LegacyFlyoutKeepsUrllessUnavailableRowsAndAttachesTheirOnlyBadge()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs"));
        var start = source.IndexOf("private void PopulateSubtitleFlyout()", StringComparison.Ordinal);
        var end = source.IndexOf("// Match the compact WebUI delay control", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];
        Assert.Contains("_playerService.CaptureSubtitleMenuSnapshot()", method, StringComparison.Ordinal);
        Assert.Contains("menu?.Entries", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSubtitleUrls()", method, StringComparison.Ordinal);
        var badge = method.IndexOf("if (track.Unplayable) badges.Children.Add(CreatePlayerMenuBadge(\"Unavailable\"))", StringComparison.Ordinal);
        var attach = method.IndexOf("if (badges.Children.Count > 0) descriptor.Children.Add(badges)", StringComparison.Ordinal);
        Assert.True(badge >= 0 && attach > badge);
        Assert.Contains("item.IsEnabled = false;", method, StringComparison.Ordinal);
        Assert.Contains("SetHelpText(item, \"Subtitle unavailable\")", method, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyFlyoutSearchUsesRetainedInventoryLanguage()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs"));
        var start = source.IndexOf("private void PopulateSubtitleFlyout()", StringComparison.Ordinal);
        var end = source.IndexOf("private void SubtitleFlyout_Opening(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];
        Assert.Contains("var menu = _playerService.CaptureSubtitleMenuSnapshot();", method, StringComparison.Ordinal);
        Assert.Contains("new SubtitleSearchDialog(_playerService.ActiveAuthority, session.MediaFileId,", method, StringComparison.Ordinal);
        Assert.Contains("menu?.Entries.FirstOrDefault(entry => entry.Track.Index == activeSubtitleIndex).Track?.Language", method, StringComparison.Ordinal);
        Assert.DoesNotContain("subtitleUrls", method, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectedSubtitleReplanResumesOnlyTheUnchangedUnpreparedTransport()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs")).Replace("\r\n", "\n");
        var start = source.IndexOf("private async Task<SubtitleTransportResult> SetBitmapSubtitleBurnInAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task ReconcileDeferredSubtitleSelectionAsync(", start, StringComparison.Ordinal);
        var method = source[start..end];
        var failure = method.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.Contains("return RefuseOrRecoverSubtitleSelection(SubtitleTransportResult.Failed);", method[failure..]);
        var rejectionStart = method.IndexOf("SubtitleTransportResult RefuseOrRecoverSubtitleSelection(", StringComparison.Ordinal);
        var rejectionEnd = method.IndexOf("        try\n", rejectionStart, StringComparison.Ordinal);
        Assert.True(rejectionStart >= 0 && rejectionEnd > rejectionStart);
        var refusal = method[rejectionStart..rejectionEnd];
        Assert.Contains("var recoverTransport = transportPreparationAttempted || subtitlePlanAdopted;", refusal);
        Assert.DoesNotContain("!ReferenceEquals(manager.CurrentSession, session)", refusal);
        var adoptedPlan = method.IndexOf("replanned = replacement;", StringComparison.Ordinal);
        var ownedPlan = method.IndexOf("subtitlePlanAdopted = true;", adoptedPlan, StringComparison.Ordinal);
        Assert.True(adoptedPlan >= 0 && ownedPlan > adoptedPlan);
        Assert.Contains("if (!CurrentOwner() || manager.CurrentSession != replanned) return SubtitleTransportResult.Superseded;", method[adoptedPlan..ownedPlan]);
        var rollbackCall = refusal.IndexOf("RefuseTentativeSubtitleSelection(preserveTransportForRecovery: recoverTransport);", StringComparison.Ordinal);
        var retained = refusal.IndexOf("if (!recoverTransport) return refusal;", StringComparison.Ordinal);
        var recovery = refusal.IndexOf("_ = RunStreamRecoveryAsync(", StringComparison.Ordinal);
        Assert.True(rollbackCall >= 0 && retained > rollbackCall && recovery > retained);
        Assert.Contains("if (!loadStarted)", refusal[..rollbackCall]);
        Assert.Contains("SetSwitchingContent(true);", refusal[retained..recovery]);
        Assert.Contains("IsLoading = true;", refusal[retained..recovery]);
        Assert.Contains("return SubtitleTransportResult.ReloadScheduled;", refusal[recovery..]);
        Assert.DoesNotContain("BeginMpvLoad(", refusal);
        Assert.DoesNotContain("EnterPlaybackTerminalState(", refusal);
        var rollbackStart = method.IndexOf("void RefuseTentativeSubtitleSelection(bool preserveTransportForRecovery)", StringComparison.Ordinal);
        var rollbackEnd = rejectionStart;
        Assert.True(rollbackStart >= 0 && rollbackEnd > rollbackStart);
        var rollback = method[rollbackStart..rollbackEnd];
        Assert.Contains("RestoreExecutedSubtitleSelection(reapply: false);", rollback);
        Assert.Contains("manager.CaptureLocalSubtitleIntent() == selectionIntent", rollback);
        Assert.Contains("_prematureEofRecoveryActive = false;", rollback);
        Assert.Contains("SetSwitchingContent(false);", rollback);
        Assert.Contains("IsLoading = false;", rollback);
        Assert.Contains("HideLoadingOverlay(loadingOwner)", rollback);
        var ownerStart = method.IndexOf("bool HasOtherTransportOwner() =>", StringComparison.Ordinal);
        Assert.True(ownerStart >= 0 && ownerStart < rollbackStart);
        var ownerCheck = method[ownerStart..rollbackStart];
        Assert.Contains("Volatile.Read(ref _fileLoadTimeoutCts) != null", ownerCheck);
        Assert.Contains("_prematureEofRecoveryActive || _qualitySwitchActive || _subtitleVersionSwitchActive", ownerCheck);
        Assert.Contains("Volatile.Read(ref _audioSwitchOperations) > 0", ownerCheck);
        Assert.DoesNotContain("_switchingContent", ownerCheck);
        Assert.DoesNotContain("IsLoading", ownerCheck);
        Assert.DoesNotContain("inheritedTransportOperation", method);
        Assert.Contains("var preserveTransportState = preserveTransportForRecovery || !loadStarted && HasOtherTransportOwner();", rollback);
        var drain = method.IndexOf("await CancelAndDrainTransportRestartsAsync(ct);", StringComparison.Ordinal);
        var captureIntent = method.IndexOf("var selectionIntent = manager.CaptureLocalSubtitleIntent();", StringComparison.Ordinal);
        var capturePreference = method.IndexOf("var preferenceOwner = _pendingSubtitlePreferenceOwner;", StringComparison.Ordinal);
        Assert.True(captureIntent >= 0 && captureIntent < drain);
        Assert.True(capturePreference >= 0 && capturePreference < drain);
        var supersededBeforeMutation = method.IndexOf("_activeSubtitleServerIndex != (requestedIndex ?? -1)", StringComparison.Ordinal);
        var mutation = method.IndexOf("transportMutationAttempted = true;", StringComparison.Ordinal);
        Assert.True(supersededBeforeMutation >= 0 && mutation > supersededBeforeMutation);
        Assert.Contains("return RefuseOrRecoverSubtitleSelection(SubtitleTransportResult.Superseded);", method[supersededBeforeMutation..mutation]);
        Assert.Contains("manager.TryRecordLocalSubtitleSelection(session, requestedTrack, manager.IsSubtitleSelectionAutomatic)", method[..mutation]);
        Assert.DoesNotContain("var inheritedPendingLoad =", method);
        Assert.DoesNotContain("_restorePausedAfterLoad.HasValue", rollback);
        var ownLoadingState = rollback.IndexOf("if (!preserveTransportState)", StringComparison.Ordinal);
        var clearLoading = rollback.IndexOf("SetSwitchingContent(false);", StringComparison.Ordinal);
        Assert.True(ownLoadingState >= 0 && clearLoading > ownLoadingState);
        var inheritedLoad = rollback.IndexOf("if (preserveTransportState) return;", StringComparison.Ordinal);
        Assert.True(inheritedLoad > clearLoading);
        foreach (var ownLoadMutation in new[] { "HideLoadingOverlay", "_restorePausedAfterLoad = null;", "RestorePlaybackStateAfterSubtitleChange" })
            Assert.True(rollback.IndexOf(ownLoadMutation, StringComparison.Ordinal) > inheritedLoad);
        Assert.Contains("bool PauseIntent() => IsWatchTogetherPlayback ? _roomPlaybackPaused : _subtitleReplanPauseIntent ?? wasPaused;", method);
        Assert.Contains("_subtitleReplanPauseIntent = PauseIntent();", method);
        Assert.Contains("if (_subtitleReplanPauseIntent.HasValue) _subtitleReplanPauseIntent = paused;", source);
        Assert.Contains("var restorePaused = PauseIntent();", rollback);
        Assert.Contains("_restorePausedAfterLoad = null;", rollback);
        Assert.Contains("RestorePlaybackStateAfterSubtitleChange(restorePaused, CurrentMediaPosition, allowSeek: false);", rollback);
        Assert.DoesNotContain("RestorePlaybackStateAfterSubtitleChange(wasPaused,", rollback);
        Assert.Contains("ShowSubtitleSelectionUnavailable();", rollback);
        Assert.DoesNotContain("BeginMpvLoad(", rollback);
        Assert.DoesNotContain("EnterPlaybackTerminalState(", rollback);
        var superseded = method.IndexOf("if (replacement == null)", StringComparison.Ordinal);
        var adopted = method.IndexOf("replanned = replacement;", superseded, StringComparison.Ordinal);
        Assert.True(superseded > rollbackEnd && adopted > superseded);
        Assert.Contains("return RefuseOrRecoverSubtitleSelection(SubtitleTransportResult.Superseded);", method[superseded..adopted]);
        Assert.Contains("manager.ReplanCurrentSubtitleAsync(position, ct)", method);
        Assert.DoesNotContain("manager.ReplanRecordedSubtitleSelectionAsync(", method);
        Assert.DoesNotContain("manager.ReplanSubtitleAsync(requestedIndex", method);
    }

    [Fact]
    public void BitmapExecutionUsesNonblankIdentityOrTheGrantedIndexInEverySelectionPath()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var helperStart = source.IndexOf("private static bool IsBurnedInSubtitle(", StringComparison.Ordinal);
        var helperEnd = source.IndexOf("private static bool IsUnsupportedBitmapSubtitle(", helperStart, StringComparison.Ordinal);
        var helper = source[helperStart..helperEnd];
        Assert.Contains("!string.IsNullOrWhiteSpace(session.SelectedSubtitleTrackId)", helper);
        Assert.Contains("!string.IsNullOrWhiteSpace(track.TrackId)", helper);
        Assert.Contains("session.SelectedSubtitleTrackIndex is int index && track.Index == index", helper);
        foreach (var (startSignature, endSignature) in new[]
        {
            ("private async Task<bool> TrySetSubtitleTrackAndPersistAsync(", "private async Task<bool> SaveExecutedSubtitlePreferenceAsync("),
            ("private async Task<bool> SelectSubtitleByServerIndexAsync(", "public int ActiveSubtitleServerIndex"),
            ("private bool SelectSubtitleTrackForOwner(", "private bool SelectSubtitleSidecar("),
        })
        {
            var start = source.IndexOf(startSignature, StringComparison.Ordinal);
            var end = source.IndexOf(endSignature, start, StringComparison.Ordinal);
            var method = source[start..end];
            Assert.Contains("IsBurnedInSubtitle(", method);
            Assert.DoesNotContain("string.Equals(burned.SelectedSubtitleTrackId, track.TrackId", method);
        }
    }

    [Fact]
    public void ManagerRefusesUnavailableWithoutReplacingLatestChoice()
    {
        WithManager(manager =>
        {
            var session = new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls = [new() { Index = 2, TrackId = "available", Language = "eng" }, new() { Index = 3, TrackId = "unavailable", Language = "eng", Unplayable = true }] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
            manager.RecordLocalSubtitleSelection(2);
            Assert.Throws<InvalidOperationException>(() => manager.RecordLocalSubtitleSelection(3));
            Assert.Equal(2, manager.SubtitleSelectionForLoad);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LatestChoiceNeverReplacesAnUnavailableOrMissingIdentity(bool missing, bool reusedIndex)
    {
        WithManager(manager =>
        {
            var original = new SubtitleTrackInfo { Index = 2, TrackId = "old", Language = "eng", Codec = "ass", Source = "embedded" };
            var session = new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls = [original] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
            manager.RecordLocalSubtitleSelection(2);
            var intent = manager.CaptureLocalSubtitleIntent();
            session.SubtitleUrls = [new() { Index = reusedIndex ? 2 : 8, TrackId = "other", Language = "eng", Codec = "ass", Source = "embedded" }];
            if (!missing) session.SubtitleUrls.Add(new() { Index = 2, TrackId = "old", Language = "eng", Codec = "ass", Source = "embedded", Unplayable = true });
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
            Assert.Equal(intent, manager.CaptureLocalSubtitleIntent());
        });
    }

    [Fact]
    public void LatestChoiceRequiresAUniquePlayableIdentityButAllowsItsIndexToChange()
    {
        WithManager(manager =>
        {
            var session = new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls =
                [new() { Index = 2, TrackId = "chosen", Language = "eng", Codec = "ass" }] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
            manager.RecordLocalSubtitleSelection(2);
            session.SubtitleUrls = [new() { Index = 8, TrackId = "chosen", Language = "eng", Codec = "ass" }];
            Assert.Equal(8, manager.SubtitleSelectionForLoad);
            session.SubtitleUrls.Add(new() { Index = 9, TrackId = "chosen", Unplayable = true });
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
        });
    }

    [Fact]
    public void ExecutedPreferenceRequiresTheCurrentIntentAndReboundIdentityNotItsOldIndex()
    {
        WithManager(manager =>
        {
            var requested = new SubtitleTrackInfo { Index = 2, TrackId = "chosen", Language = "eng", Codec = "srt" };
            var session = new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls = [requested] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
            manager.RecordLocalSubtitleSelection(2);
            var intent = manager.CaptureLocalSubtitleIntent();
            var unrelated = new SubtitleTrackInfo { Index = 2, TrackId = "other", Language = "spa", Codec = "srt" };
            var rebound = new SubtitleTrackInfo { Index = 12, TrackId = "chosen", Language = "eng", Codec = "srt" };
            session.SubtitleUrls = [unrelated, rebound];
            Assert.True(manager.IsLocalSubtitleIntentSatisfied(intent, rebound));
            Assert.False(manager.IsLocalSubtitleIntentSatisfied(intent, unrelated));
            Assert.False(manager.IsLocalSubtitleIntentSatisfied(intent, null));
            rebound.Unplayable = true;
            Assert.False(manager.IsLocalSubtitleIntentSatisfied(intent, rebound));
            manager.RecordLocalSubtitleSelection(null);
            Assert.False(manager.IsLocalSubtitleIntentSatisfied(intent, rebound));
            Assert.False(manager.IsLocalSubtitleIntentSatisfied(intent, null));
            Assert.True(manager.IsLocalSubtitleIntentSatisfied(manager.CaptureLocalSubtitleIntent(), null));
        });
    }

    [Theory]
    [InlineData("downloaded", "embedded")]
    [InlineData("embedded", "downloaded")]
    public void CrossVersionLanguageFallbackNeverSubstitutesAStoredDownload(string source, string replacementSource)
    {
        WithManager(manager =>
        {
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls =
                    [new() { Index = 2, Source = source, Language = "en", Codec = "srt" }] });
            manager.RecordLocalSubtitleSelection(2);
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                new PlaybackStartResponse { MediaFileId = 43, SubtitleUrls =
                    [new() { Index = 8, Source = replacementSource, Language = "eng", Codec = "pgs" }] });
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
            Assert.True(manager.IsRetainedSubtitleUnavailable);
        });
    }

    [Theory]
    [InlineData("embedded", "ass", "", true, 8)]
    [InlineData("external", "ass", "", true, -1)]
    [InlineData("embedded", "subrip", "", true, -1)]
    [InlineData("embedded", "ass", "new-identity", true, 8)]
    [InlineData("embedded", "ass", "", false, 8)]
    [InlineData("external", "ass", "", false, 8)]
    [InlineData("embedded", "subrip", "", false, 8)]
    [InlineData("embedded", "ass", "new-identity", false, 8)]
    public void LegacyRebindingRequiresSourceCodecOnTheSameFileButRetainsLanguageAcrossVersions(
        string source, string codec, string trackId, bool sameFile, int expected)
    {
        WithManager(manager =>
        {
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls =
                    [new() { Index = 2, Language = "eng", Codec = "ass", Source = "embedded" }] });
            manager.RecordLocalSubtitleSelection(2);
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                new PlaybackStartResponse { MediaFileId = sameFile ? 42 : 43, SubtitleUrls =
                    [new() { Index = 8, TrackId = trackId, Language = "en", Codec = codec, Source = source }] });
            Assert.Equal(expected, manager.SubtitleSelectionForLoad);
        });
    }

    [Theory]
    [InlineData("en-GB", "eng-gb", "en")]
    [InlineData("POR_br", "pt-BR", "pt-PT")]
    [InlineData("zho-Hant", "zh-hant", "zh-Hans")]
    public void LatestChoiceRebindsOnlyTheSameRegionalLanguage(string requested, string matching, string other)
    {
        WithManager(manager =>
        {
            var original = new SubtitleTrackInfo { Index = 2, Language = requested, Codec = "srt", Source = "embedded", Label = "Same title" };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls = [original] });
            manager.RecordLocalSubtitleSelection(2);
            var replacement = new PlaybackStartResponse { MediaFileId = 43, SubtitleUrls =
            [
                new() { Index = 2, Language = other, Codec = "srt", Source = "embedded", Label = "Same title" },
                new() { Index = 8, Language = matching, Codec = "srt", Source = "embedded", Label = "Same title" },
            ] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, replacement);
            Assert.Equal(8, manager.SubtitleSelectionForLoad);
            replacement.SubtitleUrls.RemoveAt(1);
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
        });
    }

    [Fact]
    public void MissingSameFileNativeIdentityCannotRebindToAnIdentitylessLookalike()
    {
        WithManager(manager =>
        {
            var session = new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls =
                [new() { Index = 2, TrackId = "old", Source = "embedded", Language = "eng", Codec = "ass" }] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
            manager.RecordLocalSubtitleSelection(2);
            session.SubtitleUrls = [new() { Index = 2, Source = "embedded", Language = "eng", Codec = "ass" }];
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
            Assert.True(manager.IsRetainedSubtitleUnavailable);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameFileDownloadedIdentityCannotBeReplacedByMetadataOrAReusedIndex(bool reverse)
    {
        WithManager(manager =>
        {
            var desired = new SubtitleTrackInfo { Index = 2, TrackId = reverse ? "original" : null,
                Source = "downloaded", Language = "en-GB", Codec = "srt" };
            var session = new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls = [desired] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
            manager.RecordLocalSubtitleSelection(2);
            session.SubtitleUrls =
            [
                new() { Index = 2, TrackId = reverse ? null : "first", Source = "downloaded", Language = "eng-gb", Codec = "subrip" },
                new() { Index = 8, TrackId = reverse ? null : "second", Source = "downloaded", Language = "eng-gb", Codec = "subrip" },
            ];
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
            desired.Label = "Selected release";
            session.SubtitleUrls[0].Label = "Other release";
            session.SubtitleUrls[1].Label = desired.Label;
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
            Assert.True(manager.IsRetainedSubtitleUnavailable);
            Assert.Same(desired, manager.CaptureLocalSubtitleIntent().Track);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SameFileDownloadRequiresItsStoredKeyAcrossThePlanIdentityBoundary(bool hasTrackId, bool useSyncKey)
    {
        WithManager(manager =>
        {
            var desired = new SubtitleTrackInfo { Index = 2, TrackId = hasTrackId ? "download:100" : null,
                Id = useSyncKey ? null : 100, SyncKey = useSyncKey ? "stored-100" : null,
                Source = "downloaded", Language = "en", Codec = "srt", Label = "Selected release" };
            var session = new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls = [desired] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
            manager.RecordLocalSubtitleSelection(2);
            var replacement = new SubtitleTrackInfo { Index = 2, Id = 200, SyncKey = "stored-200",
                Source = "downloaded", Language = "en", Codec = "srt", Label = desired.Label };
            session.SubtitleUrls = [replacement];
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
            Assert.True(manager.IsRetainedSubtitleUnavailable);
            Assert.Same(desired, manager.CaptureLocalSubtitleIntent().Track);

            var retained = new SubtitleTrackInfo { Index = 8, Id = 100, SyncKey = "stored-100",
                Source = "downloaded", Language = "en", Codec = "subrip" };
            session.SubtitleUrls.Add(retained);
            Assert.Equal(8, manager.SubtitleSelectionForLoad);
            Assert.False(manager.IsRetainedSubtitleUnavailable);
            retained.Unplayable = true;
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
            Assert.True(manager.IsRetainedSubtitleUnavailable);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossFileIdentityPrefersStrictMetadataThenUsesAUniqueLanguageFallback(bool ambiguous)
    {
        WithManager(manager =>
        {
            var desired = new SubtitleTrackInfo { Index = 2, TrackId = "source", Source = "embedded",
                Language = "en-GB", Codec = "ass", Forced = true, HearingImpaired = true, Label = "Selected" };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls = [desired] });
            manager.RecordLocalSubtitleSelection(2);
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                new PlaybackStartResponse { MediaFileId = 43, SubtitleUrls =
                [
                    new() { Index = 2, TrackId = "wrong-region", Source = "embedded", Language = "en", Codec = "ass", Forced = true, HearingImpaired = true, Label = "Selected" },
                    new() { Index = 3, TrackId = "wrong-source", Source = "external", Language = "en-GB", Codec = "ass", Forced = true, HearingImpaired = true, Label = "Selected" },
                    new() { Index = 4, TrackId = "wrong-codec", Source = "embedded", Language = "en-GB", Codec = "srt", Forced = true, HearingImpaired = true, Label = "Selected" },
                    new() { Index = 5, TrackId = "not-forced", Source = "embedded", Language = "en-GB", Codec = "ass", HearingImpaired = true, Label = "Selected" },
                    new() { Index = 6, TrackId = "not-hi", Source = "embedded", Language = "en-GB", Codec = "ass", Forced = true, Label = "Selected" },
                    new() { Index = 7, TrackId = "other-label", Source = "embedded", Language = "en-GB", Codec = "ass", Forced = true, HearingImpaired = true, Label = ambiguous ? "Selected" : "Other" },
                    new() { Index = 8, TrackId = "equivalent", Source = "embedded", Language = "eng-gb", Codec = "ass", Forced = true, HearingImpaired = true, Label = "Selected" },
                ] });
            Assert.Equal(ambiguous ? -1 : 8, manager.SubtitleSelectionForLoad);
            Assert.Equal(ambiguous, manager.IsRetainedSubtitleUnavailable);
            Assert.Same(desired, manager.CaptureLocalSubtitleIntent().Track);

            // Only after the strict tier disappears may codec/source differ.
            var inventory = manager.CurrentSession!.SubtitleUrls;
            inventory.RemoveAll(t => t.Index is 7 or 8);
            Assert.Equal(4, manager.SubtitleSelectionForLoad); // Same source ranks above same codec.
            Assert.False(manager.IsRetainedSubtitleUnavailable);
            inventory.Add(new() { Index = 9, TrackId = "tied-fallback", Source = "embedded",
                Language = "en-GB", Codec = "pgs", Forced = true, HearingImpaired = true, Label = "Selected" });
            Assert.Equal(-1, manager.SubtitleSelectionForLoad); // Equal scores must not guess.
            Assert.True(manager.IsRetainedSubtitleUnavailable);
            inventory.RemoveAll(t => t.Index is 4 or 9);
            Assert.Equal(3, manager.SubtitleSelectionForLoad); // External is now the only full-language/flags match.
            inventory.RemoveAll(t => t.Index == 3);
            Assert.Equal(-1, manager.SubtitleSelectionForLoad); // Region, Forced and HI remain required.
        });
    }

    [Fact]
    public void GrantedUnavailableTrackIsNotAutomaticallyLoaded()
    {
        WithManager(manager =>
        {
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                new PlaybackStartResponse { SelectedSubtitleTrackId = "bad", SubtitleUrls = [new() { Index = 3, TrackId = "bad", Unplayable = true }] });
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
        });
    }

    private static void WithManager(Action<PlaybackManager> check)
    {
        using var http = new HttpClient();
        var api = new SiloPlayer.Core.Api.SiloApiClient(http);
        using var auth = new AuthService(api, new(api));
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        check(manager);
    }

    [Fact]
    public void WireInventoryRetainsUnavailableAndAbsoluteIdentity()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var track = JsonSerializer.Deserialize<PlaybackSubtitleInventoryV3>("""{"combined_index":7,"stream_index":12,"container_track_id":"41","native_selectable":true,"unplayable":true}""", options)!;
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(track, options));
        Assert.True(actual.RootElement.TryGetProperty("stream_index", out var index));
        Assert.Equal(12, index.GetInt32());
        Assert.Equal("41", actual.RootElement.GetProperty("container_track_id").GetString());
        Assert.True(actual.RootElement.GetProperty("unplayable").GetBoolean());
        Assert.True(actual.RootElement.GetProperty("native_selectable").GetBoolean());
    }

    [Fact]
    public void AutomaticChoiceSkipsUnavailableWithoutRenumbering()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var tracks = JsonSerializer.Deserialize<List<SubtitleTrackInfo>>("""[{"index":3,"language":"en","source":"external","unplayable":true},{"index":8,"language":"en","source":"embedded"}]""", options)!;
        Assert.Equal(8, SubtitleAutoSelect.Resolve(new("always", SubtitleAutoSelect.BuildCandidates(tracks), "en", "ja", "en", false)));
    }

    [Theory]
    [InlineData("eng")]
    [InlineData("en")]
    [InlineData("")]
    [InlineData("und")]
    public void UnlabelledLegacyPlanResolvesUntitledNativeTrack(string language)
    {
        var decision = new PlaybackDecisionResponseV3
        {
            Outcome = "playable",
            PlaybackPlan = new()
            {
                Delivery = "original_http",
                Subtitle = new() { Inventory = [new() { CombinedIndex = 0, Source = "embedded", Codec = "subrip", Language = language }] }
            }
        };
        var method = typeof(PlaybackManager).GetMethod("AdoptProtocolV3Decision", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var response = (PlaybackStartResponse)method.Invoke(null, [decision, "attempt", "original", null])!;
        var track = Assert.Single(response.SubtitleUrls);
        Assert.Null(track.NativeTitle);
        Assert.Equal(7, SiloPlayer.Player.NativeSubtitleSelection.Resolve(
            new(0, track.StreamIndex, track.ContainerTrackId, track.Codec, track.Language, track.NativeTitle),
            [new(7, 1, "2", "subrip", language.Length == 0 || language == "und" ? null : "eng", null)], false));
    }

    [Fact]
    public void NextItemMenuIsInvalidatedBeforeTransportDrainAndEnabledOnlyByPublication()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task PlayCoreAsync(", StringComparison.Ordinal);
        var drain = source.IndexOf("await CancelAndDrainTransportRestartsAsync(requestToken)", start, StringComparison.Ordinal);
        var invalidate = source.IndexOf("_subtitleMenuPending = true;", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && invalidate > start && drain > invalidate);
        var guard = source.IndexOf("private bool IsSubtitleMenuCurrent()", StringComparison.Ordinal);
        var publish = source.IndexOf("private void SendSubtitleListToOsc()", guard, StringComparison.Ordinal);
        Assert.Contains("!_subtitleMenuPending", source[guard..publish]);
        var send = source.IndexOf("_mpv.SendScriptMessage(\"osc-set-subtitles\"", publish, StringComparison.Ordinal);
        var enable = source.IndexOf("_subtitleMenuPending = false;", publish, StringComparison.Ordinal);
        Assert.True(send > publish && enable > send);
    }

    [Fact]
    public void LoadedSubtitleSelectionAndOptionalSettingsDoNotWaitOnEachOther()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task InitializeLoadedMediaAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private bool IsLoadedMediaInitializationCurrent(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var worker = source[start..end];
        var settings = worker.IndexOf("var settingsTask = SendAutoSkipSettingsToOscAsync(ct);", StringComparison.Ordinal);
        var selection = worker.IndexOf("await SelectSubtitleByServerIndexAsync(initialSubtitleIndex, persist: false)", StringComparison.Ordinal);
        var observeSettings = worker.IndexOf("await settingsTask;", StringComparison.Ordinal);
        Assert.True(settings >= 0 && selection > settings && observeSettings > selection,
            "Start settings without awaiting them so neither a settings read nor a sidecar download blocks the other.");
        Assert.DoesNotContain("await SendAutoSkipSettingsToOscAsync(ct)", worker);
        Assert.Contains("finally", worker[selection..observeSettings]);
        Assert.Contains("if (!IsLoadedMediaInitializationCurrent(generation, ownerCts)) return;", worker[..settings]);
    }

    [Fact]
    public void SupersededAutomaticRefinementSkipsOnlySubtitleWorkNotSettingsPublication()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task InitializeLoadedMediaAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private bool IsLoadedMediaInitializationCurrent(", start, StringComparison.Ordinal);
        var worker = source[start..end];
        Assert.DoesNotContain("!ReferenceEquals(_pendingAutomaticSubtitleSelection, automatic)) return;", worker);
        var refinement = worker.IndexOf("applyInitialSubtitleSelection = ReferenceEquals(_pendingAutomaticSubtitleSelection, automatic);", StringComparison.Ordinal);
        Assert.True(refinement >= 0, "Automatic refinement must check its owner under the subtitle state lock.");
        var guardedSelection = worker.IndexOf("if (applyInitialSubtitleSelection)", refinement + 1, StringComparison.Ordinal);
        var settingsStart = worker.IndexOf("var settingsTask = SendAutoSkipSettingsToOscAsync(ct);", StringComparison.Ordinal);
        var settings = worker.IndexOf("await settingsTask;", StringComparison.Ordinal);
        var ai = worker.IndexOf("await SendSubtitleAiAvailabilityToOscAsync(ct);", StringComparison.Ordinal);
        Assert.True(settingsStart >= 0 && settingsStart < refinement && guardedSelection > refinement && settings > guardedSelection && ai > settings);
        Assert.Contains("finally", worker[guardedSelection..settings]);
        Assert.Contains("if (!IsLoadedMediaInitializationCurrent(generation, ownerCts)) return;", worker[..refinement]);
        Assert.Contains("lock (_subtitleTrackStateLock)", worker[..refinement]);
    }

    [Fact]
    public void InventoryRepublishUsesExecutedIdentityOnlyForTheCurrentFile()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private void SendSubtitleListToOsc()", StringComparison.Ordinal);
        var end = source.IndexOf("private void SendAudioTrackListToOsc()", start, StringComparison.Ordinal);
        var publish = source[start..end];
        Assert.Contains("_executedSubtitleFileId == session.MediaFileId && _executedSubtitleTrack is { } executed &&", publish);
        Assert.Contains("tracks.Contains(executed) ? executed.Index : -1", publish); // Unbound rows cannot highlight a reused index.
        Assert.DoesNotContain("_pendingInitialServerSubtitleIndex", publish); // Pending reads must never outrank executed identity.
    }

    [Fact]
    public void KnownUnavailableIdentityDoesNotBecomeAnotherAvailableTrack()
    {
        WithManager(manager =>
        {
            var original = new SubtitleTrackInfo { Index = 2, TrackId = "old", Language = "eng", Codec = "ass" };
            var session = new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls = [original] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
            manager.RecordLocalSubtitleSelection(2);
            var intent = manager.CaptureLocalSubtitleIntent();
            session.SubtitleUrls = [new() { Index = 2, TrackId = "old", Language = "eng", Codec = "ass", Unplayable = true }, new() { Index = 8, Language = "eng", Codec = "ass" }];
            Assert.Equal(-1, manager.SubtitleSelectionForLoad);
            Assert.Equal(intent, manager.CaptureLocalSubtitleIntent());
            session.SubtitleUrls = [new() { Index = 8, TrackId = "old", Language = "eng", Codec = "ass" }];
            Assert.Equal(8, manager.SubtitleSelectionForLoad);
            Assert.Equal(intent, manager.CaptureLocalSubtitleIntent());
        });
    }

    [Fact]
    public void LegacyFlyoutSearchUsesRetainedInventoryLanguage()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs"));
        var start = source.IndexOf("private void PopulateSubtitleFlyout()", StringComparison.Ordinal);
        var end = source.IndexOf("private void SubtitleFlyout_Opening(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];
        Assert.Contains("var menu = _playerService.CaptureSubtitleMenuSnapshot();", method, StringComparison.Ordinal);
        Assert.Contains("var searchAuthority = _playerService.ActiveAuthority;", method, StringComparison.Ordinal);
        Assert.Contains("new SubtitleSearchDialog(searchAuthority, session.MediaFileId,", method, StringComparison.Ordinal);
        Assert.Contains("menu?.Entries.FirstOrDefault(entry => entry.Track.Index == activeSubtitleIndex).Track?.Language", method, StringComparison.Ordinal);
        Assert.DoesNotContain("subtitleUrls", method, StringComparison.Ordinal);
    }

    [Fact]
    public void AdaptedPlanKeepsIdentityAndUnavailableOrdinals()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var decision = JsonSerializer.Deserialize<PlaybackDecisionResponseV3>("""
            {"outcome":"playable","playback_plan":{"delivery":"original_http","subtitle":{"inventory":[
             {"combined_index":8,"stream_index":12,"container_track_id":"41","native_selectable":true,"unplayable":true}]}}}
            """, options)!;
        var method = typeof(PlaybackManager).GetMethod("AdoptProtocolV3Decision", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var response = (PlaybackStartResponse)method.Invoke(null, [decision, "attempt", "original", null])!;
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(Assert.Single(response.SubtitleUrls), options));
        Assert.True(actual.RootElement.TryGetProperty("stream_index", out var index));
        Assert.Equal(12, index.GetInt32());
        Assert.Equal(8, actual.RootElement.GetProperty("index").GetInt32());
        Assert.Equal("41", actual.RootElement.GetProperty("container_track_id").GetString());
        Assert.True(actual.RootElement.GetProperty("unplayable").GetBoolean());
        Assert.True(actual.RootElement.GetProperty("native_selectable").GetBoolean());
    }
}
