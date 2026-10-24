using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackConnectionTests
{
    [Theory]
    [InlineData(null,null,false,false,false,false)] [InlineData(42,null,false,false,false,true)]
    [InlineData(42,false,false,false,false,false)] [InlineData(42,true,false,false,false,true)]
    [InlineData(42,false,true,false,false,true)] [InlineData(42,false,false,true,false,true)] [InlineData(42,false,false,false,true,true)]
    public void SourcePinHonorsExplicitViewerIntentAndSharedPlayback(int? fileId,bool? explicitPin,bool audio,bool room,bool receiver,bool expected)
        => Assert.Equal(expected,PlaybackSourceVersionPolicy.IsPinned(fileId,explicitPin,audio,room,receiver));
    [Fact] public void OfficialProbeVectorsReplayUnchanged()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Find("BufferingQualityV1", "official-vectors.json")));
        foreach (var vector in json.RootElement.GetProperty("probe_cases").EnumerateArray())
        {
            var meter = new ConnectionProbeMeter(vector.GetProperty("mode").GetString() == "accurate", vector.TryGetProperty("target_kbps", out var target) ? target.GetInt32() : null);
            foreach (var chunk in vector.GetProperty("chunks").EnumerateArray())
            { if (meter.Add(chunk[0].GetDouble(), chunk[1].GetInt32())) break; }
            var result = meter.Finish(vector.TryGetProperty("end", out var end) ? end.GetDouble() : meter.MaximumMs, vector.TryGetProperty("end", out _));
            var expected = vector.GetProperty("expect");
            if (expected.ValueKind == JsonValueKind.Null || !expected.TryGetProperty("kbps", out var rate)) { Assert.Null(result); continue; }
            Assert.True(result != null, vector.GetProperty("name").GetString());
            Assert.Equal(rate.GetInt32(), result!.Kbps);
            Assert.Equal(expected.GetProperty("bytes").GetInt64(), result.Bytes);
            Assert.Equal(expected.GetProperty("duration_ms").GetInt32(), result.DurationMs);
            Assert.Equal(expected.GetProperty("lower_bound").GetBoolean(), result.LowerBound);
        }
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task WireNegotiatesOptionalFieldsAndProtectsOldServers(bool current)
    {
        using var wire = new Wire { Current = current };
        var client = Client(wire); var api = new PlaybackApi(client);
        var started = await api.StartPlaybackV3Async(new() { FileId = 42,
            ClientFeatures = ["playback_plan_v3", "buffering_quality_v1", "connection_speed_v1", "playback_plan_summary_v1"],
            BandwidthEstimateKbps = 5600, BandwidthEstimateSource = "observed_rebuffer", ObservedThroughputKbps = 3000, LearnedLimit = "clear" });
        var body = wire.Requests.Single(r => r.Path.EndsWith("/start")).Body;
        Assert.Equal(current, body.TryGetProperty("observed_throughput_kbps", out _));
        Assert.Equal(current, body.GetProperty("client_features").EnumerateArray().Any(v => v.GetString() == "buffering_quality_v1"));
        await api.ReportRouteEventAsync(new() { SessionId = started.SessionId!, PlanId = "plan", PlaybackAttemptId = "attempt" }, "plan_summary");
        Assert.Equal(current, wire.Requests.Any(r => r.Path.EndsWith("/route-events")));
    }
    [Fact] public async Task AmbiguousReplanRetriesTheExactBodyAndRequestId()
    {
        using var wire = new Wire { Current = true, InterruptReplan = true };
        var api = new PlaybackApi(Client(wire));
        var started = await api.StartPlaybackV3Async(new() { FileId = 42 });
        await api.ReplanPlaybackV3Async(started.SessionId!, new() { Operation = "quality_change", BandwidthEstimateKbps = 5600,
            BandwidthEstimateSource = "observed_rebuffer", ClientFeatures = ["playback_plan_v3", "buffering_quality_v1"] });
        var sent = wire.Requests.Where(r => r.Path.EndsWith("/replan")).ToArray();
        Assert.Equal(2, sent.Length); Assert.Equal(sent[0].Body.GetRawText(), sent[1].Body.GetRawText());
    }
    [Fact] public async Task ConnectionReadsReportsAndResetRetainCapturedOwner()
    {
        using var wire = new Wire { Current = true }; var client = Client(wire); var api = new PlaybackApi(client);
        var owner = api.CaptureConnectionOwner();
        var state = await api.GetConnectionAsync(owner);
        Assert.Equal(5600, state.LearnedLimit!.Kbps);
        Assert.Equal(TimeSpan.FromHours(24), state.LearnedLimit.ExpiresAt - state.LearnedLimit.ObservedAt);
        await api.ReportConnectionProbeAsync(owner, new() { Kbps = 16000, Bytes = 1000000, DurationMs = 500, Manual = true, Transport = "ethernet" });
        await api.ResetLearnedConnectionLimitAsync(owner);
        Assert.True(wire.Requests.Single(r => r.Path.EndsWith("/results")).Body.GetProperty("manual").GetBoolean());
        client.SetProfile("retired");
        var count = wire.Requests.Count;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.ResetLearnedConnectionLimitAsync(owner));
        Assert.Equal(count, wire.Requests.Count);
    }
    [Fact] public async Task MeteredOrBusyPlaybackCannotStartManualProbe()
    {
        using var wire = new Wire(); var service = PlaybackConnectionService.For(new PlaybackApi(Client(wire)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TestAsync("n", true, "wifi"));
        service.SetMediaActive(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TestAsync("n", false, "ethernet"));
        Assert.Empty(wire.Requests);
    }
    [Fact] public async Task BufferingActionCannotReplanAfterAnotherQualityPlanWasAccepted()
    {
        using var wire = new Wire { Current = true }; var client = Client(wire); var api = new PlaybackApi(client);
        using var auth = new AuthService(client, new AuthApi(client));
        using var manager = new PlaybackManager(api, new CatalogApi(client), auth, client);
        await manager.StartSessionAsync(42, preferences: new PlaybackQualityPreferences(client.CaptureContext(), "auto", null));
        var observedPlan = manager.CurrentSession;
        await manager.ReplanQualityAsync("720p", 0);
        var count = wire.Requests.Count(r => r.Path.EndsWith("/replan"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ReplanBufferingQualityAsync(
            new("auto", 5600, "observed_rebuffer", 3000), 0, CancellationToken.None, observedPlan));
        Assert.Equal(count, wire.Requests.Count(r => r.Path.EndsWith("/replan")));
        Assert.Equal("720p", manager.QualityPreference);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task StartReusesOneCapabilitySnapshotAndOneScopedPreferenceBatch(bool current)
    {
        using var wire=new Wire { Current=current }; var client=Client(wire); var api=new PlaybackApi(client);
        using var auth=new AuthService(client,new AuthApi(client)); using var manager=new PlaybackManager(api,new CatalogApi(client),auth,client);
        var prefs=await manager.ResolveQualityPreferencesAsync();
        await manager.StartSessionAsync(42,preferences:prefs);
        Assert.Single(wire.Requests.Where(r=>r.Path.EndsWith("/capabilities")));
        Assert.Single(wire.Requests.Where(r=>r.Path.EndsWith("/effective")));
        Assert.Equal(current,wire.Queries.Single(q=>q.Path.EndsWith("/effective")).Query.Contains("playback.buffering_quality_mode"));
    }
    [Fact] public async Task MissingOptionalPreferencePreservesFixedQualityAndCapWithoutBlockingStart()
    {
        using var wire=new Wire { Current=true, MissingBufferingPreference=true }; var client=Client(wire); var api=new PlaybackApi(client);
        using var auth=new AuthService(client,new AuthApi(client)); using var manager=new PlaybackManager(api,new CatalogApi(client),auth,client);
        var prefs=await manager.ResolveQualityPreferencesAsync(); await manager.StartSessionAsync(42,preferences:prefs);
        var body=wire.Requests.Single(r=>r.Path.EndsWith("/start")).Body;
        Assert.Equal("720p",body.GetProperty("quality_preference").GetString());
        Assert.Equal(4000,body.GetProperty("bandwidth_cap_kbps").GetInt32());
        Assert.Equal("ignore",body.GetProperty("learned_limit").GetString());
        Assert.Equal("never",manager.BufferingQualityMode);
    }
    [Fact] public void ReadingFreshnessAndLowerBoundTargetControlAutomaticTesting()
    {
        var now = DateTimeOffset.UtcNow;
        var cap = new PlaybackCapabilityV3 { BandwidthProbeTargetKbps = 16000, BandwidthProbeMaxAgeSeconds = 60 };
        var state = new PlaybackConnectionState { Probe = new() { Kbps = 8000, LowerBound = true, MeasuredAt = now } };
        Assert.False(PlaybackConnectionService.ProvesConnection(state, cap, now));
        state.Probe.LowerBound = false;
        Assert.True(PlaybackConnectionService.ProvesConnection(state, cap, now));
        Assert.False(PlaybackConnectionService.ProvesConnection(state, cap, now.AddSeconds(61)));
        state.PlaybackSample = new() { Kbps = 20000, MeasuredAt = now };
        Assert.True(PlaybackConnectionService.ProvesConnection(state, cap, now));
    }
    internal static string Find(params string[] path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        { var file = Path.Combine(new[] { dir.FullName, "tests", "SiloPlayer.Tests", "Fixtures" }.Concat(path).ToArray()); if (File.Exists(file)) return file; }
        throw new FileNotFoundException();
    }
    private static SiloApiClient Client(Wire wire) { var c = new SiloApiClient(new HttpClient(wire)); c.SetBaseUrl("https://connection-fixture.invalid"); c.SetProfile("profile"); return c; }
    private sealed class Wire : HttpMessageHandler
    {
        public bool Current, InterruptReplan, MissingBufferingPreference;
        public List<(string Path, JsonElement Body)> Requests { get; } = [];
        public List<(string Path,string Query)> Queries { get; }=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Queries.Add((path,request.RequestUri.Query));
            Requests.Add((path, JsonDocument.Parse(request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(ct)).RootElement.Clone()));
            if (path.EndsWith("/replan") && InterruptReplan && Requests.Count(r => r.Path.EndsWith("/replan")) == 1) throw new HttpRequestException("Ambiguous response loss");
            var payload = path switch
            {
                "/api/v2/playback/capabilities" => "{\"allowed\":true,\"state\":\"available\",\"installation_id\":\"11111111-1111-4111-8111-111111111111\",\"protocol_versions\":[3],\"features\":" + (Current ? "[\"buffering_quality_v1\",\"connection_speed_v1\",\"playback_plan_summary_v1\"]" : "[]") + "}",
                "/api/v2/playback/start" => File.ReadAllText(Find("PlaybackV2", "playback_start_opaque_ids.json")),
                _ when path.EndsWith("/replan") => File.ReadAllText(Find("PlaybackV2", "playback_replan_reanchored.json")),
                "/api/v2/playback/connection" => """{"connection":"local","learned_limit":{"kbps":5600,"observed_at":"2026-10-24T00:00:00Z","expires_at":"2026-10-25T00:00:00Z"}}""",
                "/api/v2/settings/values/effective" => MissingBufferingPreference
                    ? """{"items":[{"key":"playback.preferred_quality","value":"720p"},{"key":"playback.max_bitrate_kbps","value":4000}]}"""
                    : """{"items":[{"key":"playback.buffering_quality_mode","value":"always","source":"default"}]}""",
                _ => "{}",
            };
            return new(path.EndsWith("/results") || path.EndsWith("/learned-limit") ? HttpStatusCode.NoContent : HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        }
    }
}
