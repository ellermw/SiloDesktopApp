using System.Text.Json;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SubtitleIdentityTests
{
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

    [Fact]
    public void LatestChoiceRebindsOnlyAvailableInventoryRows()
    {
        WithManager(manager =>
        {
            var original = new SubtitleTrackInfo { Index = 2, TrackId = "old", Language = "eng", Codec = "ass" };
            var session = new PlaybackStartResponse { MediaFileId = 42, SubtitleUrls = [original] };
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
            manager.RecordLocalSubtitleSelection(2);
            session.SubtitleUrls = [new() { Index = 2, TrackId = "old", Language = "eng", Codec = "ass", Unplayable = true }, new() { Index = 8, Language = "eng", Codec = "ass" }];
            Assert.Equal(8, manager.SubtitleSelectionForLoad);
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
            [new(7, 1, "2", "subrip", language.Length == 0 ? null : "eng", null)], false));
    }

    [Fact]
    public void LoadedSubtitleSelectionAndOptionalSettingsReadsAreObservedTogether()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task InitializeLoadedMediaAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("private bool IsLoadedMediaInitializationCurrent(", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var method = source[start..end];
        var worker = method.IndexOf("async Task SelectInitialSubtitleAsync()", StringComparison.Ordinal);
        var selection = method.IndexOf("await SelectSubtitleByServerIndexAsync(initialSubtitleIndex, persist: false)", StringComparison.Ordinal);
        var join = method.IndexOf("await Task.WhenAll(SendAutoSkipSettingsToOscAsync(ct), SelectInitialSubtitleAsync());", StringComparison.Ordinal);
        Assert.True(worker >= 0 && selection > worker && join > selection);
        Assert.DoesNotContain("SendAutoSkipSettingsToOscAsync", method[worker..join], StringComparison.Ordinal);
        Assert.DoesNotContain("await SendAutoSkipSettingsToOscAsync(ct)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("await SelectInitialSubtitleAsync()", method, StringComparison.Ordinal);
        Assert.True(method.IndexOf("await SendSubtitleAiAvailabilityToOscAsync(ct)", StringComparison.Ordinal) > join);
        Assert.True(method.IndexOf("ownerCts.Dispose();", StringComparison.Ordinal) > join);
    }

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
    public void RefusalRestorationRetiresPendingChoiceAndGuardsNativeFailures()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private void RestoreExecutedSubtitleSelection(bool reapply)", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("private void ShowSubtitleSelectionUnavailable()", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var method = source[start..end];
        var guard = method.IndexOf("        try", StringComparison.Ordinal);
        Assert.True(guard >= 0);
        Assert.Contains("_pendingInitialServerSubtitleIndex = null;", method[..guard], StringComparison.Ordinal);
        Assert.Contains("refused.Accepted = false;", method[..guard], StringComparison.Ordinal);
        Assert.Contains("RecordExecutedSubtitleSelection(retained, session.MediaFileId)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordLocalSubtitleSelection(", method, StringComparison.Ordinal);
        Assert.Contains("_executedSubtitleFileId = session?.MediaFileId;", method, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", method, StringComparison.Ordinal);
        Assert.Contains("Subtitle selection restoration failed:", method, StringComparison.Ordinal);

        var feedbackEnd = source.IndexOf("private bool SelectSubtitleTrack(", end, StringComparison.Ordinal);
        Assert.True(feedbackEnd > end);
        var feedback = source[end..feedbackEnd];
        Assert.Contains("catch (Exception ex)", feedback, StringComparison.Ordinal);
        Assert.Contains("Subtitle refusal feedback failed:", feedback, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyFlyoutKeepsUrllessUnavailableRowsAndAttachesTheirOnlyBadge()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Controls", "PlayerOverlay.xaml.cs"));
        var start = source.IndexOf("private void PopulateSubtitleFlyout()", StringComparison.Ordinal);
        var end = source.IndexOf("// Match the compact WebUI delay control", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];
        Assert.Contains("Manager?.CurrentSession?.SubtitleUrls", method, StringComparison.Ordinal);
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
        Assert.Contains("var subtitleTracks = _playerService.Manager?.CurrentSession?.SubtitleUrls ?? [];", method, StringComparison.Ordinal);
        Assert.Contains("new SubtitleSearchDialog(_playerService.ActiveAuthority, session.MediaFileId, subtitleTracks.FirstOrDefault(t => t.Index == activeSubtitleIndex)?.Language)", method, StringComparison.Ordinal);
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
