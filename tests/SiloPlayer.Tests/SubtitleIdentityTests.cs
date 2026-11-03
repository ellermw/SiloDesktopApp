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
    public void LoadedSubtitleSelectionPrecedesOptionalSettingsReads()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "PlayerService.cs"));
        var start = source.IndexOf("private async Task InitializeLoadedMediaAsync(", StringComparison.Ordinal);
        var selection = source.IndexOf("await SelectSubtitleByServerIndexAsync(initialSubtitleIndex, persist: false)", start, StringComparison.Ordinal);
        var settings = source.IndexOf("await SendAutoSkipSettingsToOscAsync(ct)", start, StringComparison.Ordinal);
        Assert.True(selection > start && settings > selection);
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
