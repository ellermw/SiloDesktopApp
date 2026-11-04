using System.Text.Json;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SubtitleIdentityTests
{
    [Fact]
    public void RejectedSubtitleReplanRestoresTheSurvivingStreamBeforeConsideringRecovery()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task<SubtitleTransportResult> SetBitmapSubtitleBurnInAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task ReconcileDeferredSubtitleSelectionAsync(", start, StringComparison.Ordinal);
        var method = source[start..end];
        var failure = method.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var retained = method.IndexOf("if (!transportPreparationAttempted)", failure, StringComparison.Ordinal);
        var recovery = method.IndexOf("_ = RunStreamRecoveryAsync(", failure, StringComparison.Ordinal);
        Assert.True(retained > failure && recovery > retained);
        var refusal = method[retained..recovery];
        Assert.Contains("RefuseTentativeSubtitleSelection();", refusal);
        Assert.Contains("return SubtitleTransportResult.Failed;", refusal);
        Assert.DoesNotContain("BeginMpvLoad(", refusal);
        Assert.DoesNotContain("EnterPlaybackTerminalState(", refusal);
        var rollbackStart = method.IndexOf("void RefuseTentativeSubtitleSelection()", StringComparison.Ordinal);
        var rollbackEnd = method.IndexOf("        try\n", rollbackStart, StringComparison.Ordinal);
        Assert.True(rollbackStart >= 0 && rollbackEnd > rollbackStart);
        var rollback = method[rollbackStart..rollbackEnd];
        Assert.Contains("RestoreExecutedSubtitleSelection(reapply: false);", rollback);
        Assert.Contains("manager.CaptureLocalSubtitleIntent() == selectionIntent", rollback);
        Assert.Contains("_prematureEofRecoveryActive = false;", rollback);
        Assert.Contains("_switchingContent = false;", rollback);
        Assert.Contains("IsLoading = false;", rollback);
        Assert.Contains("HideLoadingOverlay(loadingOwner)", rollback);
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
        Assert.Contains("RefuseTentativeSubtitleSelection();", method[superseded..adopted]);
        Assert.Contains("return SubtitleTransportResult.Superseded;", method[superseded..adopted]);
        Assert.Contains("manager.ReplanCurrentSubtitleAsync(position, ct)", method);
        Assert.DoesNotContain("manager.ReplanSubtitleAsync(requestedIndex", method);
        Assert.Contains("track != null && !session.SubtitleUrls.Contains(track)", method);
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
            ("private async Task<bool> TrySetSubtitleTrackAndPersistAsync(", "private async Task SaveExecutedSubtitlePreferenceAsync("),
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

    [Theory]
    [InlineData("embedded", "ass", "", 8)]
    [InlineData("external", "ass", "", -1)]
    [InlineData("embedded", "subrip", "", -1)]
    [InlineData("embedded", "ass", "new-identity", 8)]
    public void LegacyRebindingRequiresTheSameSourceCodecButCanAcquireIdentity(string source, string codec, string trackId, int expected)
    {
        WithManager(manager =>
        {
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls =
                    [new() { Index = 2, Language = "eng", Codec = "ass", Source = "embedded" }] });
            manager.RecordLocalSubtitleSelection(2);
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager,
                new PlaybackStartResponse { MediaFileId = 43, SubtitleUrls =
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
    public void CrossFileIdentityUsesUniqueFullMetadataAndLabelTieBreak(bool ambiguous)
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
    public void LoadedSubtitleSelectionPrecedesOptionalSettingsReads()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task InitializeLoadedMediaAsync(", StringComparison.Ordinal);
        var selection = source.IndexOf("await SelectSubtitleByServerIndexAsync(initialSubtitleIndex, persist: false)", start, StringComparison.Ordinal);
        var settings = source.IndexOf("await SendAutoSkipSettingsToOscAsync(ct)", start, StringComparison.Ordinal);
        Assert.True(selection > start && settings > selection);
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
        var guardedSelection = worker.IndexOf("if (applyInitialSubtitleSelection)", refinement + 1, StringComparison.Ordinal);
        var settings = worker.IndexOf("await SendAutoSkipSettingsToOscAsync(ct);", guardedSelection, StringComparison.Ordinal);
        var ai = worker.IndexOf("await SendSubtitleAiAvailabilityToOscAsync(ct);", settings, StringComparison.Ordinal);
        Assert.True(refinement >= 0 && guardedSelection > refinement && settings > guardedSelection && ai > settings);
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
        Assert.Contains("_executedSubtitleFileId == session.MediaFileId ? _executedSubtitleTrack?.Index ?? -1 : -1", publish);
        Assert.DoesNotContain("(_pendingInitialServerSubtitleIndex ?? -1)", publish);
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
