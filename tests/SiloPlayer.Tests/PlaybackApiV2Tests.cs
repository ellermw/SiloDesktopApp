using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Tests;

public sealed class PlaybackApiV2Tests
{
    [Fact]
    public async Task InterruptedStartRetriesTheIdenticalAttemptAndBody()
    {
        var handler = new FixtureHandler { InterruptFirstStart = true };
        await CreateApi(handler).StartPlaybackV3Async(new PlaybackStartRequestV3 { FileId = 42 });
        var starts = handler.Requests.Where(r => r.Path.EndsWith("/start")).ToArray();
        Assert.Equal(2, starts.Length);
        Assert.Equal(starts[0].Body.GetRawText(), starts[1].Body.GetRawText());
    }

    [Fact]
    public async Task InstallationChangeRefreshesCapabilitiesAndCreatesNewAttempt()
    {
        var handler = new FixtureHandler { ChangeInstallationOnFirstStart = true };
        await CreateApi(handler).StartPlaybackV3Async(new PlaybackStartRequestV3 { FileId = 42 });
        var starts = handler.Requests.Where(r => r.Path.EndsWith("/start")).ToArray();
        Assert.Equal(2, starts.Length);
        Assert.NotEqual(starts[0].Body.GetProperty("playback_attempt_id").GetString(), starts[1].Body.GetProperty("playback_attempt_id").GetString());
        Assert.Equal(2, handler.Requests.Count(r => r.Path.EndsWith("/capabilities")));
    }

    [Fact]
    public async Task FixedSourceCapabilityPreventsAlternateVersionSubstitution()
    {
        var handler = new FixtureHandler { FixedSource = true };
        await CreateApi(handler).StartPlaybackV3Async(new PlaybackStartRequestV3 { FileId = 42 });
        Assert.False(handler.Requests.Last().Body.GetProperty("allow_alternate_versions").GetBoolean());
    }

    [Fact]
    public async Task StaleCapabilityResponseCannotStartOnADifferentServer()
    {
        var handler = new FixtureHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        handler.CapabilityReturned = () => client.SetBaseUrl("https://replacement.test");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PlaybackApi(client).StartPlaybackV3Async(new PlaybackStartRequestV3 { FileId = 42 }));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SubtitleMutationsUseStringIdsAndUnwrapTheJobEnvelope()
    {
        var handler = new FixtureHandler();
        var api = CreateApi(handler);
        await api.SearchSubtitlesAsync(42, ["eng"]);
        await api.DownloadSubtitleAsync(42, new SubtitleSearchResult { SubtitleId = "sub-1", Provider = "provider", Language = "eng" });
        var job = await api.GetSubtitleAiJobAsync(42);
        Assert.True(job.Id > 0);
        foreach (var request in handler.Requests.Where(r => r.Method == HttpMethod.Post))
            Assert.Equal("42", request.Body.GetProperty("media_file_id").GetString());
        Assert.False(handler.Requests[1].Body.TryGetProperty("format", out _));
    }

    [Fact]
    public async Task StartReadsInstallationAndUsesStringIdsWithTheOfficialDecisionFixture()
    {
        var handler = new FixtureHandler();
        var api = CreateApi(handler);
        var decision = await api.StartPlaybackV3Async(new PlaybackStartRequestV3 { FileId = 42, ProfileId = "profile-1" });
        Assert.Equal(42, decision.PlaybackPlan!.EffectiveMediaFileId);
        Assert.StartsWith("/api/v2/stream/", decision.PlaybackPlan.Stream.Url);
        Assert.Equal("/api/v2/playback/capabilities", handler.Requests[0].Path);
        var start = handler.Requests.Single(r => r.Path.EndsWith("/start"));
        Assert.Equal("42", start.Body.GetProperty("file_id").GetString());
        Assert.Equal(Installation, start.Body.GetProperty("installation_id").GetString());
    }

    [Fact]
    public async Task ProgressAndStopCarryIncreasingSequencesAndStableStopIdentity()
    {
        var handler = new FixtureHandler();
        var api = CreateApi(handler);
        var decision = await api.StartPlaybackV3Async(new PlaybackStartRequestV3 { FileId = 42 });
        var session = decision.SessionId!;
        await api.ReportProgressAsync(session, 120, false);
        await api.ReportProgressAsync(session, 60, true);
        await api.StopPlaybackAsync(session);
        await api.StopPlaybackAsync(session);
        var samples = handler.Requests.Where(r => r.Path.EndsWith("/progress")).ToArray();
        Assert.True(samples[1].Body.GetProperty("sequence").GetInt64() > samples[0].Body.GetProperty("sequence").GetInt64());
        var stops = handler.Requests.Where(r => r.Method == HttpMethod.Delete).ToArray();
        Assert.Equal(stops[0].Body.GetRawText(), stops[1].Body.GetRawText());
        Assert.True(Guid.TryParse(stops[0].Body.GetProperty("stop_id").GetString(), out _));
        Assert.Equal(60, stops[0].Body.GetProperty("position").GetDouble());
        Assert.Equal(Installation, stops[0].Body.GetProperty("installation_id").GetString());
    }

    [Fact]
    public async Task UnconfiguredPlaybackDoesNotSendAStart()
    {
        var handler = new FixtureHandler { Unconfigured = true };
        var api = CreateApi(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.StartPlaybackV3Async(new PlaybackStartRequestV3 { FileId = 42 }));
        Assert.DoesNotContain(handler.Requests, r => r.Path.EndsWith("/start"));
    }

    private const string Installation = "11111111-1111-4111-8111-111111111111";
    [Theory]
    [InlineData("profile")]
    [InlineData("profile-return")]
    [InlineData("server")]
    [InlineData("server-return")]
    [InlineData("login")]
    [InlineData("logout")]
    [InlineData("pin-revoked")]
    public async Task RealIdentityChangesRejectOldPlaybackBeforeSendingProgress(string change)
    {
        var handler = new FixtureHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test"); client.BeginAuthenticationSession("fixture-token"); client.SetProfile("profile", "fixture-pin");
        var api = new PlaybackApi(client);
        var started = await api.StartPlaybackV3Async(new() { FileId = 42 });
        switch (change)
        {
            case "profile": client.SetProfile("other"); break;
            case "profile-return": client.SetProfile("other"); client.SetProfile("profile", "fixture-pin"); break;
            case "server": client.SetBaseUrl("https://other.test"); break;
            case "server-return": client.SetBaseUrl("https://other.test"); client.SetBaseUrl("https://example.test"); break;
            case "login": client.BeginAuthenticationSession("new-fixture-token"); break;
            case "logout": client.ClearAuth(); break;
            case "pin-revoked":
                var read = client.CaptureContext();
                Assert.True(client.TryClearProfile(new(read.AuthenticationGeneration, read.RequestContextGeneration, "profile"))); break;
            default: throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown identity change fixture.");
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.ReportProgressAsync(started.SessionId!, 60, false));
        Assert.DoesNotContain(handler.Requests, request => request.Path.EndsWith("/progress"));
    }

    private static PlaybackApi CreateApi(FixtureHandler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        return new PlaybackApi(client);
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public bool Unconfigured { get; init; }
        public bool InterruptFirstStart { get; init; }
        public bool ChangeInstallationOnFirstStart { get; init; }
        public bool FixedSource { get; init; }
        public Action? CapabilityReturned { get; set; }
        public List<(HttpMethod Method, string Path, JsonElement Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, path, JsonDocument.Parse(body).RootElement.Clone()));
            if (path.EndsWith("/start") && Requests.Count(r => r.Path.EndsWith("/start")) == 1)
            {
                if (InterruptFirstStart) throw new HttpRequestException("Connection interrupted after dispatch");
                if (ChangeInstallationOnFirstStart) return new(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("""{"type":"https://siloserver.org/docs/api/v2/problems/installation_changed","status":409,"detail":"Installation changed"}""", Encoding.UTF8, "application/problem+json")
                };
            }
            var fixture = path switch
            {
                "/api/v2/playback/capabilities" => Unconfigured ? "playback_capability_unconfigured" : "playback_capability_available",
                "/api/v2/playback/start" => "playback_start_opaque_ids",
                "/api/v2/subtitles/search" => "subtitles_search_partial",
                "/api/v2/subtitles/download" => "subtitle_download",
                "/api/v2/subtitles/ai/jobs/42" => "subtitle_ai_job_opaque_id",
                _ when path.EndsWith("/progress") => "playback_progress_applied",
                _ when request.Method == HttpMethod.Delete => "playback_stop_completed",
                _ => null
            };
            if (fixture == null) return new(HttpStatusCode.NotFound);
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "PlaybackV2"))) dir = dir.Parent;
            var json = File.ReadAllText(Path.Combine(dir!.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "PlaybackV2", fixture + ".json"));
            if (path.EndsWith("/capabilities"))
            {
                if (FixedSource) json = json.Replace("\"sequenced_progress_v1\"", "\"sequenced_progress_v1\",\"fixed_media_file_v1\"");
                CapabilityReturned?.Invoke();
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
