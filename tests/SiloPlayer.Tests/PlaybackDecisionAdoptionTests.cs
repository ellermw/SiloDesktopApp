using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackDecisionAdoptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCanceledStartWaitsForIndependentCleanupBeforePropagating(bool hasCurrentSession)
    {
        await using var fixture = new Fixture();
        if (hasCurrentSession) await fixture.StartCurrent();
        var current = fixture.Manager.CurrentSession;
        var stream = fixture.Manager.StreamUrl;
        var quality = fixture.Manager.QualityPreference;
        fixture.Wire.NextSession = "unused";
        var request = new PlaybackStartRequestV3 { FileId = 71, QualityPreference = "720p" };
        // Use the real API so its accepted server/installation/session authority
        // is registered before cancellation wins at the manager boundary.
        var decision = await fixture.Playback.StartPlaybackV3Async(request);
        fixture.Wire.HoldUnusedStop = true;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var adoption = Accept(fixture.Manager, request, decision, canceled.Token);
        try
        {
            var first = await Task.WhenAny(fixture.Wire.UnusedStopStarted.Task, adoption)
                .WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Same(fixture.Wire.UnusedStopStarted.Task, first);
            Assert.False(adoption.IsCompleted);
            var stop = Assert.Single(fixture.Wire.Stops);
            Assert.Equal("unused", stop.SessionId);
            Assert.False(stop.WasCanceled);
            Assert.True(stop.CanBeCanceled);
            Assert.Same(current, fixture.Manager.CurrentSession);
            Assert.Equal(stream, fixture.Manager.StreamUrl);
            Assert.Equal(quality, fixture.Manager.QualityPreference);
        }
        finally { fixture.Wire.ReleaseUnusedStop.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adoption);
        Assert.Same(current, fixture.Manager.CurrentSession);
        Assert.Equal(stream, fixture.Manager.StreamUrl);
        Assert.Equal(quality, fixture.Manager.QualityPreference);
        Assert.DoesNotContain(fixture.Wire.Stops, stop => stop.SessionId == "active");
    }

    [Fact]
    public async Task LateCanceledDecisionForAlreadyActiveSessionNeverStopsThatSession()
    {
        await using var fixture = new Fixture();
        await fixture.StartCurrent();
        var current = fixture.Manager.CurrentSession;
        var stream = fixture.Manager.StreamUrl;
        fixture.Wire.NextSession = "active";
        var request = new PlaybackStartRequestV3 { FileId = 71, QualityPreference = "720p" };
        var decision = await fixture.Playback.StartPlaybackV3Async(request);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Accept(fixture.Manager, request, decision, canceled.Token));
        Assert.Empty(fixture.Wire.Stops);
        Assert.Same(current, fixture.Manager.CurrentSession);
        Assert.Equal(stream, fixture.Manager.StreamUrl);
        Assert.Equal("1080p", fixture.Manager.QualityPreference);
        Assert.Equal("1080p", fixture.Manager.SelectedQualityMenuId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://delivery.fixture.invalid/api/v1/retired-video")]
    public async Task InvalidStartDeliveryPreservesAdoptedStateAndRetiresOnlyUnusedDecision(string url)
    {
        await using var fixture = new Fixture();
        await fixture.StartCurrent();
        var current = fixture.Manager.CurrentSession;
        var stream = fixture.Manager.StreamUrl;
        fixture.Wire.NextSession = "unused";
        fixture.Wire.NextUrl = url;
        var error = await Record.ExceptionAsync(() => fixture.Manager.StartSessionAsync(71,
            preferences: fixture.Preferences("720p")));
        Assert.NotNull(error);
        Assert.IsNotType<OperationCanceledException>(error);
        AssertCurrentUnchanged(fixture, current, stream);
        var stop = Assert.Single(fixture.Wire.Stops);
        Assert.Equal("unused", stop.SessionId);
        Assert.False(stop.WasCanceled);
        Assert.True(stop.CanBeCanceled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://delivery.fixture.invalid/api/v1/retired-video")]
    public async Task InvalidReplanDeliveryPreservesAdoptedStateWithoutStoppingActiveSession(string url)
    {
        await using var fixture = new Fixture();
        await fixture.StartCurrent();
        var current = fixture.Manager.CurrentSession;
        var stream = fixture.Manager.StreamUrl;
        fixture.Wire.NextSession = "active";
        fixture.Wire.NextUrl = url;
        var error = await Record.ExceptionAsync(() => fixture.Manager.ReplanQualityAsync("720p", 45));
        Assert.NotNull(error);
        AssertCurrentUnchanged(fixture, current, stream);
        Assert.Empty(fixture.Wire.Stops);
        // Subsequent real API work must still reference the adopted plan and
        // session, rather than the rejected decision's plan identity.
        fixture.Wire.NextUrl = "/stream/active-retry";
        await fixture.Manager.ReplanQualityAsync("720p", 46);
        Assert.Equal("plan-active-1080p", fixture.Wire.LastReplanFailedPlanId);
        Assert.Equal("/api/v2/playback/active/replan", fixture.Wire.LastReplanPath);
    }

    private static void AssertCurrentUnchanged(Fixture fixture, PlaybackStartResponse? session, string? stream)
    {
        Assert.Same(session, fixture.Manager.CurrentSession);
        Assert.Equal("active", fixture.Manager.SessionId);
        Assert.Equal(stream, fixture.Manager.StreamUrl);
        Assert.Equal("1080p", fixture.Manager.QualityPreference);
        Assert.Equal("1080p", fixture.Manager.SelectedQualityMenuId);
        Assert.Equal("1080p", fixture.Manager.CurrentSession!.ActiveQuality);
        Assert.False(fixture.Manager.SessionRetirementPending);
    }

    private static Task<PlaybackStartResponse> Accept(PlaybackManager manager, PlaybackStartRequestV3 request,
        PlaybackDecisionResponseV3 decision, CancellationToken cancellationToken)
    {
        var method = typeof(PlaybackManager).GetMethod("AcceptStartedDecisionAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task<PlaybackStartResponse>)method.Invoke(manager, [request, decision, 0L, 0L, cancellationToken])!;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Wire Wire { get; } = new();
        private readonly HttpClient _http;
        private readonly AuthService _auth;
        internal SiloApiClient Api { get; }
        internal PlaybackApi Playback { get; }
        internal PlaybackManager Manager { get; }

        internal Fixture()
        {
            _http = new HttpClient(Wire);
            Api = new SiloApiClient(_http); Api.SetBaseUrl("https://adoption.fixture.invalid"); Api.SetProfile("synthetic");
            Playback = new PlaybackApi(Api);
            _auth = new AuthService(Api, new AuthApi(Api));
            Manager = new PlaybackManager(Playback, new CatalogApi(Api), _auth, Api);
        }

        internal PlaybackQualityPreferences Preferences(string quality) => new(Api.CaptureContext(), quality, 6000);
        internal Task<PlaybackStartResponse> StartCurrent() => Manager.StartSessionAsync(41, preferences: Preferences("1080p"));

        public async ValueTask DisposeAsync()
        {
            Wire.ReleaseUnusedStop.TrySetResult();
            try { await Manager.StopSessionAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            finally { Manager.Dispose(); _auth.Dispose(); _http.Dispose(); }
        }
    }

    private sealed record Stop(string SessionId, bool WasCanceled, bool CanBeCanceled);

    private sealed class Wire : HttpMessageHandler
    {
        internal string NextSession { get; set; } = "active";
        internal string? NextUrl { get; set; }
        internal bool HoldUnusedStop { get; set; }
        internal ConcurrentQueue<Stop> Stops { get; } = new();
        internal TaskCompletionSource UnusedStopStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseUnusedStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string? LastReplanFailedPlanId { get; private set; }
        internal string? LastReplanPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/playback/capabilities")
                return Json("""{"allowed":true,"state":"available","installation_id":"11111111-1111-4111-8111-111111111111","protocol_versions":[3],"features":["fixed_media_file_v1"]}""");
            if (request.Method == HttpMethod.Post && (path == "/api/v2/playback/start" || path.EndsWith("/replan", StringComparison.Ordinal)))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var replan = path.EndsWith("/replan", StringComparison.Ordinal);
                if (replan)
                {
                    LastReplanPath = path;
                    LastReplanFailedPlanId = body.RootElement.GetProperty("failed_plan_id").GetString();
                }
                var quality = body.RootElement.GetProperty("quality_preference").GetString() ?? "1080p";
                return Json(Decision(NextSession, NextUrl ?? "/stream/" + NextSession, quality));
            }
            if (request.Method == HttpMethod.Delete && path.StartsWith("/api/v2/playback/", StringComparison.Ordinal))
            {
                var id = path.Split('/')[4];
                Stops.Enqueue(new(id, cancellationToken.IsCancellationRequested, cancellationToken.CanBeCanceled));
                if (id == "unused")
                {
                    UnusedStopStarted.TrySetResult();
                    if (HoldUnusedStop) await ReleaseUnusedStop.Task.WaitAsync(cancellationToken);
                }
                return Json("""{"outcome":"stopped"}""");
            }
            if (request.Method == HttpMethod.Post)
                return Json("""{"outcome":"applied"}""");
            return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }

        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };

        private static string Decision(string id, string url, string quality) => $$"""
            {"protocol_version":3,"server_features":["plan_active_quality_v1"],"outcome":"playable","session_id":"{{id}}",
             "playback_plan":{"protocol_version":3,"plan_id":"plan-{{id}}-{{quality}}","plan_attempt_key":"attempt-{{id}}-{{quality}}",
              "session_id":"{{id}}","delivery":"original_http","stream":{"url":{{JsonSerializer.Serialize(url)}},"protocol":"http_progressive"},
              "timeline":{"source_start_seconds":0,"stream_origin_seconds":0,"player_start_seconds":0,"timeline_offset_seconds":0,"can_seek_anywhere":true},
              "selected_tracks":{},"effective_recipe":{},"subtitle":{"mode":"off","inventory":[]},
              "active_quality":"{{quality}}","available_qualities":[{"label":"1080p","height":1080,"bitrate_kbps":6000},{"label":"720p","height":720,"bitrate_kbps":3000}],
              "requested_media_file_id":41,"effective_media_file_id":41,"source":{"media_file_id":41,"duration_seconds":60}
             }
            }
            """;
    }
}
