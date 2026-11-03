using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackTrackIntentTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public async Task FailedSubtitleSwitchRetriesTheViewerChoiceBeforeRecoveringTheOldPlan(int? subtitle)
    {
        using var scope = new Fixture();
        await scope.Manager.StartSessionAsync(42);
        scope.Wire.FailNextReplan = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Manager.ReplanSubtitleAsync(subtitle, 19));

        var recovered = await scope.Manager.RecoverPlaybackFailureAsync(19, "playback_interrupted", "subtitle-switch-failed");
        var retry = scope.Wire.Replans[1];
        Assert.Equal("track_change", retry.GetProperty("operation").GetString());
        if (subtitle is int selected)
        {
            Assert.Equal(selected, retry.GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("index").GetInt32());
            Assert.Equal($"sub:42:{selected}", recovered.SelectedSubtitleTrackId);
        }
        else
        {
            Assert.False(retry.GetProperty("selected_tracks").TryGetProperty("subtitle", out _));
            Assert.Null(recovered.SelectedSubtitleTrackId);
        }
        Assert.Equal(subtitle ?? -1, scope.Manager.SubtitleSelectionForLoad);
    }

    [Fact]
    public async Task ManualVersionPrefersExactRegionalAudioBeforeSameLanguageTitleSimilarity()
    {
        using var scope = new Fixture();
        scope.Manager.WatchDetail!.Versions[0].AudioTracks![0].Language = "en-AU";
        scope.Manager.WatchDetail.Versions.Add(new() { FileId = 45, AudioTracks =
            [new() { Language = "en-GB", Title = "Main", Codec = "aac" }, new() { Language = "en-AU", Title = "English", Codec = "aac" }] });
        await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalAudioSelection(0);
        await scope.Manager.StartReplacementSessionAsync(45, 8);
        Assert.Equal(1, scope.Wire.Starts[1].GetProperty("audio_track_index").GetInt32());
    }

    [Fact]
    public async Task LocalAudioPickIsSentByLaterQualityChange()
    {
        using var scope = new Fixture(); await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalAudioSelection(1);
        await scope.Manager.ReplanQualityAsync("720p", 12);
        Assert.Equal(1, scope.Wire.Replans.Single().GetProperty("selected_tracks").GetProperty("audio").GetProperty("index").GetInt32());
    }

    [Fact]
    public async Task SeekCannotAcknowledgeAndLoseUnsentLocalSubtitleIntent()
    {
        using var scope = new Fixture(); await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalSubtitleSelection(1);
        await scope.Manager.ReplanSeekAsync(12);
        Assert.Equal("sub:42:0", scope.Wire.Replans[0].GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("id").GetString());
        Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);
        await scope.Manager.ReplanQualityAsync("720p", 13);
        Assert.Equal("sub:42:1", scope.Wire.Replans[1].GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("id").GetString());
    }

    [Fact]
    public async Task LocallySelectedTextSubtitleReachesQualityReplanAndGrantedSelection()
    {
        using var scope = new Fixture(); await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalSubtitleSelection(1);
        var session = await scope.Manager.ReplanQualityAsync("480p-low", 47);
        Assert.Equal("sub:42:1", scope.Wire.Replans.Single().GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("id").GetString());
        Assert.Equal("sub:42:1", session.SelectedSubtitleTrackId);
        Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);
    }

    [Fact]
    public async Task LocalOffIsAnExplicitTrackChangeBeforeQualityAndStaysOff()
    {
        using var scope = new Fixture(); await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalSubtitleSelection(null);
        await scope.Manager.ReplanQualityAsync("360p", 52);
        Assert.Equal(2, scope.Wire.Replans.Count);
        Assert.Equal("track_change", scope.Wire.Replans[0].GetProperty("operation").GetString());
        Assert.False(scope.Wire.Replans[0].GetProperty("selected_tracks").TryGetProperty("subtitle", out _));
        Assert.Equal("quality_change", scope.Wire.Replans[1].GetProperty("operation").GetString());
        Assert.Equal(-1, scope.Manager.SubtitleSelectionForLoad);
    }

    [Fact]
    public async Task PickDuringReplanWinsOverLateGrantedSelectionAndReachesNextRequest()
    {
        using var scope = new Fixture(); await scope.Manager.StartSessionAsync(42);
        scope.Wire.BlockNext = true;
        scope.Manager.RecordLocalSubtitleSelection(1);
        var pending = scope.Manager.ReplanQualityAsync("720p", 30);
        await scope.Wire.Entered.Task;
        scope.Manager.RecordLocalSubtitleSelection(2);
        scope.Wire.Release.SetResult(); await pending;
        Assert.Equal(2, scope.Manager.SubtitleSelectionForLoad);
        await scope.Manager.ReplanQualityAsync("auto", 32);
        Assert.Equal("sub:42:2", scope.Wire.Replans[1].GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("id").GetString());
    }

    [Fact]
    public async Task MissingVersionSubtitleDoesNotEraseIntentWhenReturningToSource()
    {
        using var scope = new Fixture(); await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalSubtitleSelection(1);
        await scope.Manager.StartReplacementSessionAsync(43, 22);
        Assert.False(scope.Wire.Starts[1].TryGetProperty("subtitle_track_index", out _));
        Assert.Equal(-1, scope.Manager.SubtitleSelectionForLoad);
        await scope.Manager.StartReplacementSessionAsync(42, 23);
        Assert.False(scope.Wire.Starts[2].TryGetProperty("subtitle_track_index", out _));
        Assert.Equal(1, scope.Wire.Replans[^1].GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("index").GetInt32());
        Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);
    }

    [Fact]
    public async Task ReorderedVersionUsesTrackSignatureForAudioAndSubtitle()
    {
        using var scope = new Fixture(); await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalSubtitleSelection(1);
        scope.Manager.RecordLocalAudioSelection(1);
        await scope.Manager.StartReplacementSessionAsync(44, 14);
        var body = scope.Wire.Starts[1];
        Assert.False(body.TryGetProperty("subtitle_track_index", out _));
        Assert.Equal(0, scope.Manager.SubtitleSelectionForLoad);
        Assert.Equal("spa", scope.Manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == scope.Manager.SubtitleSelectionForLoad).Language);
        Assert.Equal(0, body.GetProperty("audio_track_index").GetInt32());
    }

    [Theory]
    [InlineData("en-GB", "eng-gb", "en")]
    [InlineData("pt-BR", "por_br", "pt-PT")]
    [InlineData("zh-Hant", "zho-Hant", "zh-Hans")]
    public async Task VersionSwitchKeepsRegionalSubtitleIdentity(string requested, string matching, string other)
    {
        using var scope = new Fixture();
        await scope.Manager.StartSessionAsync(42);
        var desired = scope.Manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1);
        desired.Language = requested;
        desired.Label = "Same title";
        scope.Manager.RecordLocalSubtitleSelection(1);
        scope.Manager.WatchDetail!.Versions.Single(v => v.FileId == 44).SubtitleTracks =
        [
            new() { Index = 0, Language = other, Codec = "srt", Title = "Same title" },
            new() { Index = 1, Language = matching, Codec = "srt", Title = "Same title" },
        ];
        await scope.Manager.StartReplacementSessionAsync(44, 14);
        Assert.False(scope.Wire.Starts[1].TryGetProperty("subtitle_track_index", out _));
        Assert.Equal(1, scope.Wire.Replans[^1].GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("index").GetInt32());
        Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VersionWithNoAutomaticSubtitleLeavesReplacementAutoGrantIntact(bool localAutomaticOff)
    {
        using var scope = new Fixture();
        scope.Wire.InitialSubtitleSelection = -1;
        await scope.Manager.StartSessionAsync(42);
        Assert.Equal(-1, scope.Manager.SubtitleSelectionForLoad);
        if (localAutomaticOff) scope.Manager.RecordLocalSubtitleSelection(null, isAutomatic: true);

        scope.Wire.InitialSubtitleSelection = 1;
        var replacement = await scope.Manager.StartReplacementSessionAsync(44, 14);
        Assert.False(scope.Wire.Starts[1].TryGetProperty("subtitle_track_index", out _));
        Assert.Empty(scope.Wire.Replans);
        Assert.Equal("sub:44:1", replacement.SelectedSubtitleTrackId);
        Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);
        Assert.DoesNotContain(scope.Wire.Paths, path => path.Contains("-prefs/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("und")]
    [InlineData("eng")]
    public async Task DuplicateLegacyMetadataNeverGuessesTheFirstRowInAnotherVersion(string language)
    {
        using var scope = new Fixture();
        scope.Wire.OmitTrackIds = true;
        foreach (var version in scope.Manager.WatchDetail!.Versions.Where(v => v.FileId is 42 or 44))
            version.SubtitleTracks =
            [
                new() { Index = 0, Language = language, Codec = "srt" },
                new() { Index = 1, Language = language, Codec = "srt" },
            ];
        await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalSubtitleSelection(1);
        await scope.Manager.ReplanQualityAsync("720p", 12);
        Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);
        await scope.Manager.ReplanAudioAsync(0, 13);
        Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);

        await scope.Manager.StartReplacementSessionAsync(44, 14);
        Assert.Equal(-1, scope.Manager.SubtitleSelectionForLoad);
        Assert.False(scope.Wire.Replans[^1].GetProperty("selected_tracks").TryGetProperty("subtitle", out _));
        // Refusal on the ambiguous version must not erase the selected source row.
        await scope.Manager.StartReplacementSessionAsync(42, 15);
        Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);
        Assert.Equal(1, scope.Wire.Replans[^1].GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("index").GetInt32());
    }

    [Fact]
    public async Task VersionSwitchPreservesExplicitOffAndNeverSavesAutomaticFallback()
    {
        using var scope = new Fixture(); await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalSubtitleSelection(null);
        await scope.Manager.StartReplacementSessionAsync(44, 14);
        Assert.False(scope.Wire.Starts[1].TryGetProperty("subtitle_track_index", out _));
        var change = Assert.Single(scope.Wire.Replans);
        Assert.Equal("track_change", change.GetProperty("operation").GetString());
        Assert.False(change.GetProperty("selected_tracks").TryGetProperty("subtitle", out _));
        Assert.Equal("off", scope.Manager.CurrentSession!.SubtitleMode);
        Assert.Equal(-1, scope.Manager.SubtitleSelectionForLoad);
        Assert.DoesNotContain(scope.Wire.Paths, path => path.Contains("-prefs/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task LegacyPlanUsesSelectedIndexAndKeepsOffAcrossQualityReload(int selected)
    {
        using var scope = new Fixture();
        scope.Wire.OmitTrackIds = true;
        scope.Wire.InitialSubtitleSelection = selected;
        var session = await scope.Manager.StartSessionAsync(42);
        Assert.Equal(selected < 0 ? (int?)null : selected, session.SelectedSubtitleTrackIndex);
        Assert.Equal(selected, scope.Manager.SubtitleSelectionForLoad);
        await scope.Manager.ReplanQualityAsync("720p", 12);
        Assert.Equal(selected, scope.Manager.SubtitleSelectionForLoad);
        Assert.Single(scope.Wire.Replans);
        var tracks = scope.Wire.Replans[0].GetProperty("selected_tracks");
        if (selected < 0) Assert.False(tracks.TryGetProperty("subtitle", out _));
        else Assert.Equal(selected, tracks.GetProperty("subtitle").GetProperty("index").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("und")]
    public async Task LegacyUnlabelledManualChoiceSurvivesQualityAudioAndVersionChanges(string language)
    {
        using var scope = new Fixture();
        scope.Wire.OmitTrackIds = true;
        scope.Wire.UnlabelledSubtitleLanguage = language;
        foreach (var version in scope.Manager.WatchDetail!.Versions)
        foreach (var track in version.SubtitleTracks!.Where(t => t.Language == "spa"))
        {
            track.Language = language;
            track.Title = null;
        }
        await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalSubtitleSelection(1);
        Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);
        await scope.Manager.ReplanQualityAsync("720p", 12);
        Assert.Equal(1, scope.Wire.Replans[0].GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("index").GetInt32());
        Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);
        await scope.Manager.ReplanAudioAsync(1, 13);
        Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);
        await scope.Manager.StartReplacementSessionAsync(44, 14);
        Assert.False(scope.Wire.Starts[1].TryGetProperty("subtitle_track_index", out _));
        Assert.Equal(0, scope.Manager.SubtitleSelectionForLoad);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementResolvesCombinedIdentityOnlyAfterInventoryIsAccepted(bool omitTrackIds)
    {
        using var scope = new Fixture();
        scope.Wire.OmitTrackIds = omitTrackIds;
        foreach (var version in scope.Manager.WatchDetail!.Versions.Where(v => v.FileId is 42 or 44))
            version.SubtitleTracks =
            [
                new() { Index = 22, Language = "eng", Codec = "srt", Title = "English" },
                new() { Language = "spa", Codec = "srt", Title = "Spanish", External = true },
            ];
        await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalSubtitleSelection(1); // external=0, embedded=1; stream=22
        foreach (var file in new[] { 42, 44 })
        {
            await scope.Manager.StartReplacementSessionAsync(file, 14);
            Assert.False(scope.Wire.Starts[^1].TryGetProperty("subtitle_track_index", out _));
            var subtitle = scope.Wire.Replans[^1].GetProperty("selected_tracks").GetProperty("subtitle");
            Assert.Equal(1, subtitle.GetProperty("index").GetInt32());
            Assert.Equal(omitTrackIds ? "" : $"sub:{file}:1", subtitle.GetProperty("id").GetString());
            Assert.Equal(1, scope.Manager.SubtitleSelectionForLoad);
            Assert.Equal("eng", scope.Manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1).Language);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Wire Wire = new();
        public readonly PlaybackManager Manager;
        private readonly AuthService _auth;
        public Fixture()
        {
            var client = new SiloApiClient(new HttpClient(Wire)); client.SetBaseUrl("https://intent.invalid");
            _auth = new(client, new AuthApi(client));
            Manager = new(new PlaybackApi(client), new CatalogApi(client), _auth, client);
            Manager.UseWatchDetail(new()
            {
                ContentId = "episode", SeriesId = "series",
                Versions = [Version(42, false, false), Version(43, false, true), Version(44, true, false)],
            });
            Wire.Versions = Manager.WatchDetail!.Versions;
        }
        public void Dispose() { Manager.Dispose(); _auth.Dispose(); }
    }

    private static FileVersion Version(int id, bool reversed, bool missing)
    {
        var audio = new List<AudioTrackInfo> { new() { Language = "en", Title = "Main", Codec = "aac" }, new() { Language = "jpn", Title = "Main", Codec = "flac" } };
        var subs = new List<VersionSubtitleTrack> { new() { Index = 0, Language = "eng", Codec = "srt", Title = "English" }, new() { Index = 1, Language = "spa", Codec = "srt", Title = "Spanish" } };
        if (missing) subs.RemoveAt(1);
        if (reversed) { audio.Reverse(); subs.Reverse(); for (var i = 0; i < subs.Count; i++) subs[i].Index = i; }
        return new() { FileId = id, AudioTracks = audio, SubtitleTracks = subs };
    }

    private sealed class Wire : HttpMessageHandler
    {
        public List<JsonElement> Starts = [], Replans = [];
        public List<string> Paths = [];
        public bool BlockNext;
        public bool FailNextReplan;
        public bool OmitTrackIds;
        public int InitialSubtitleSelection;
        public string? UnlabelledSubtitleLanguage;
        public List<FileVersion>? Versions;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _file = 42, _selected = 0;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; Paths.Add(path);
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (path == "/api/v2/settings/values/effective") return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (!path.EndsWith("/start") && !path.EndsWith("/replan")) return Json("""{"outcome":"applied"}""");
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = document.RootElement.Clone();
            if (path.EndsWith("/start"))
            {
                Starts.Add(body); _file = int.Parse(body.GetProperty("file_id").GetString()!);
                _selected = body.TryGetProperty("subtitle_track_index", out var startSub) ? startSub.GetInt32() : InitialSubtitleSelection;
            }
            else
            {
                Replans.Add(body);
                if (FailNextReplan)
                {
                    FailNextReplan = false;
                    throw new InvalidOperationException("Synthetic subtitle replan failure before adoption.");
                }
                if (body.GetProperty("selected_tracks").TryGetProperty("subtitle", out var selected)) _selected = selected.GetProperty("index").GetInt32();
                else if (body.GetProperty("operation").GetString() == "track_change") _selected = -1;
                if (BlockNext) { BlockNext = false; Entered.SetResult(); await Release.Task.WaitAsync(ct); }
            }
            var version = Versions?.FirstOrDefault(v => v.FileId == _file) ?? Version(_file, _file == 44, _file == 43);
            if (UnlabelledSubtitleLanguage is { } language)
                foreach (var track in version.SubtitleTracks!.Where(t => t.Language == "spa"))
                {
                    track.Language = language;
                    track.Title = null;
                }
            var inventory = (version.SubtitleTracks ?? []).OrderByDescending(t => t.External == true)
                .Select((t, index) => new { track_id = OmitTrackIds ? "" : $"sub:{_file}:{index}", combined_index = (int?)index, language = t.Language, codec = t.Codec, label = t.Title, source = t.External == true ? "external" : "embedded", delivery = "native" })
                .Concat(_file == 42 ? new[] { new { track_id = OmitTrackIds ? "" : "sub:42:2", combined_index = (int?)2, language = (string?)"fra", codec = (string?)"pgs", label = (string?)"French", source = "embedded", delivery = "native" } } : []).ToArray();
            var subtitleIdentity = _selected < 0 ? null : new { id = OmitTrackIds ? "" : $"sub:{_file}:{_selected}", index = _selected };
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "session",
                playback_plan = new
                {
                    protocol_version = 3, plan_id = $"plan-{Starts.Count}-{Replans.Count}", plan_attempt_key = "key", session_id = "session", effective_media_file_id = _file,
                    delivery = "original_http", stream = new { url = "/stream" }, timeline = new { can_seek_anywhere = true },
                    selected_tracks = new { audio = new { id = $"file:{_file}:audio:0", index = 0 }, subtitle = subtitleIdentity },
                    subtitle = new { mode = _selected < 0 ? "off" : "native", track_id = OmitTrackIds ? "" : _selected < 0 ? null : $"sub:{_file}:{_selected}", inventory },
                },
            }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
