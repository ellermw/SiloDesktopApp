using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackTrackIntentTests
{
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
        Assert.Equal(1, scope.Wire.Starts[2].GetProperty("subtitle_track_index").GetInt32());
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
        Assert.Equal(0, body.GetProperty("subtitle_track_index").GetInt32());
        Assert.Equal(0, body.GetProperty("audio_track_index").GetInt32());
    }

    [Fact]
    public async Task VersionSwitchPreservesExplicitOffAndNeverSavesAutomaticFallback()
    {
        using var scope = new Fixture(); await scope.Manager.StartSessionAsync(42);
        scope.Manager.RecordLocalSubtitleSelection(null);
        await scope.Manager.StartReplacementSessionAsync(44, 14);
        Assert.False(scope.Wire.Starts[1].TryGetProperty("subtitle_track_index", out _));
        Assert.Equal(-1, scope.Manager.SubtitleSelectionForLoad);
        Assert.DoesNotContain(scope.Wire.Paths, path => path.Contains("-prefs/", StringComparison.Ordinal));
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
                _selected = body.TryGetProperty("subtitle_track_index", out var startSub) ? startSub.GetInt32() : Starts.Count == 1 ? 0 : -1;
            }
            else
            {
                Replans.Add(body);
                if (body.GetProperty("selected_tracks").TryGetProperty("subtitle", out var selected)) _selected = selected.GetProperty("index").GetInt32();
                else if (body.GetProperty("operation").GetString() == "track_change") _selected = -1;
                if (BlockNext) { BlockNext = false; Entered.SetResult(); await Release.Task.WaitAsync(ct); }
            }
            var version = Version(_file, _file == 44, _file == 43);
            var inventory = (version.SubtitleTracks ?? []).Select(t => new { track_id = $"sub:{_file}:{t.Index}", combined_index = t.Index, language = t.Language, codec = t.Codec, label = t.Title, source = "embedded", delivery = "native" })
                .Concat(_file == 42 ? new[] { new { track_id = "sub:42:2", combined_index = (int?)2, language = (string?)"fra", codec = (string?)"pgs", label = (string?)"French", source = "embedded", delivery = "native" } } : []).ToArray();
            var subtitleIdentity = _selected < 0 ? null : new { id = $"sub:{_file}:{_selected}", index = _selected };
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "session",
                playback_plan = new
                {
                    protocol_version = 3, plan_id = $"plan-{Starts.Count}-{Replans.Count}", plan_attempt_key = "key", session_id = "session", effective_media_file_id = _file,
                    delivery = "original_http", stream = new { url = "/stream" }, timeline = new { can_seek_anywhere = true },
                    selected_tracks = new { audio = new { id = $"file:{_file}:audio:0", index = 0 }, subtitle = subtitleIdentity },
                    subtitle = new { mode = _selected < 0 ? "off" : "native", track_id = _selected < 0 ? null : $"sub:{_file}:{_selected}", inventory },
                },
            }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
