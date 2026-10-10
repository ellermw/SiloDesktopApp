using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class SubtitleSyncBehaviorTests
{
    [Fact]
    public void PollingHasDeadlineAndRealtimeFreshnessBoundary()
    {
        var start = DateTimeOffset.Parse("2026-10-09T12:00:00Z");
        var job = new SubtitleSyncJob { Status = "running" };
        Assert.True(SubtitleSyncPolicy.ShouldPoll(job, start, null, start.AddMinutes(5).AddMilliseconds(-1)));
        Assert.False(SubtitleSyncPolicy.ShouldPoll(job, start, null, start.AddMinutes(5)));
        Assert.False(SubtitleSyncPolicy.ShouldPoll(job, start, start.AddSeconds(6), start.AddSeconds(9.999)));
        Assert.True(SubtitleSyncPolicy.ShouldPoll(job, start, start.AddSeconds(6), start.AddSeconds(10)));
        Assert.False(SubtitleSyncPolicy.ShouldPoll(new() { Status = "synced" }, start, null, start.AddSeconds(3)));
        Assert.True(SubtitleSyncPolicy.ShouldPoll(job, start.AddMinutes(5), null, start.AddMinutes(5).AddSeconds(3)));
    }

    [Fact]
    public void LateReadsCannotRegressNewerJobPhaseOrTerminalResult()
    {
        var now = DateTimeOffset.UtcNow;
        var current = new SubtitleSyncJob { Id = "4", Status = "running", Phase = "matching", Progress = .8, CreatedAt = now };
        Assert.False(SubtitleSyncPolicy.AcceptJob(current, new() { Id = "3", Status = "synced", CreatedAt = now.AddSeconds(-1) }));
        Assert.False(SubtitleSyncPolicy.AcceptJob(current, new() { Id = "4", Status = "running", Phase = "analyzing", Progress = .9, CreatedAt = now }));
        Assert.False(SubtitleSyncPolicy.AcceptJob(current, new() { Id = "4", Status = "pending", CreatedAt = now }));
        Assert.False(SubtitleSyncPolicy.AcceptJob(current, new() { Id = "4", Status = "running", Phase = "matching", Progress = .7, CreatedAt = now }));
        var terminal = new SubtitleSyncJob { Id = "4", Status = "synced", CreatedAt = now };
        Assert.True(SubtitleSyncPolicy.AcceptJob(current, terminal));
        Assert.False(SubtitleSyncPolicy.AcceptJob(terminal, current));
        Assert.True(SubtitleSyncPolicy.AcceptJob(terminal, new() { Id = "5", Status = "pending", CreatedAt = now.AddSeconds(1) }));
    }

    [Fact]
    public void InventoryKeysAreOpaqueAndLiveEmbeddedOrExternalPathsCannotInventKeys()
    {
        var external = "external-" + new string('a', 64);
        Assert.Equal(external, SubtitleSyncPolicy.Key(new() { Source = "external", SyncKey = external }));
        Assert.Null(SubtitleSyncPolicy.Key(new() { Source = "external", Url = "/subtitles/name.srt?downloaded_subtitle_id=9" }));
        Assert.Null(SubtitleSyncPolicy.Key(new() { Source = "embedded", SyncKey = "stored-9" }));
        Assert.Null(SubtitleSyncPolicy.Key(new() { Source = "live", SyncKey = "stored-9" }));
        Assert.Equal("stored-9", SubtitleSyncPolicy.Key(new() { Source = "downloaded", Url = "/subtitles?downloaded_subtitle_id=9&position=10" }));
        Assert.False(SubtitleSyncPolicy.IsKey("external-../../source"));
        Assert.False(SubtitleSyncPolicy.IsKey("stored-0"));
    }

    [Fact]
    public void TimingDescriptionIncludesSharedOffsetAndFrameScale()
    {
        Assert.Equal("Original timing", SubtitleSyncPolicy.Describe(new()));
        Assert.Equal("+2.3 s · 25→23.976 fps", SubtitleSyncPolicy.Describe(new() { OffsetMs = 2300, Scale = 25d / 23.976 }));
        Assert.Equal("−0.1 s", SubtitleSyncPolicy.Describe(new() { OffsetMs = -100 }));
        Assert.Equal("This video has no audio Silo can read.", SubtitleSyncPolicy.Failure("no_audio"));
        Assert.Equal("Doesn't match this video", SubtitleSyncPolicy.Status(new() { Sync = new() { Status = "no_match" } }));
    }

    [Fact]
    public void Old404OrResetAnswerCannotOverwriteNewerPushObservation()
    {
        const long sentBeforePush = 3;
        const long afterPush = 4;
        Assert.False(SubtitleSyncPolicy.CanApplyObservation(sentBeforePush, afterPush));
        Assert.True(SubtitleSyncPolicy.CanApplyObservation(afterPush, afterPush));
    }

    [Fact]
    public void TerminalPushBefore202RetainsViewerFeedbackForAlreadyLoadedCorrection()
    {
        var timing = new SubtitleTiming { OffsetMs = 1800 };
        var pushed = new SubtitleSyncState { Timing = timing, Sync = new() { Id = "9", Status = "synced", Result = timing } };
        var late202 = new SubtitleSyncJob { Id = "9", Status = "pending" };
        Assert.False(SubtitleSyncPolicy.AcceptJob(pushed.Sync, late202));
        Assert.False(SubtitleSyncPolicy.CanApplyObservation(1, 2));
        Assert.True(SubtitleSyncPolicy.ShouldAnnounceAppliedTiming(pushed, "9", "9", timing));
        Assert.False(SubtitleSyncPolicy.ShouldAnnounceAppliedTiming(pushed, "another-job", "9", timing));
        Assert.False(SubtitleSyncPolicy.ShouldAnnounceAppliedTiming(pushed, "9", "9", new() { OffsetMs = 1700 }));
        pushed.Timing = new(); // Reset retains the old job but changes timing.
        Assert.False(SubtitleSyncPolicy.ShouldAnnounceAppliedTiming(pushed, "9", "9", pushed.Timing));
        Assert.Equal(TimeSpan.FromSeconds(8), SubtitleSyncPolicy.ApplyingLifetime);
    }

    [Fact]
    public async Task ResetUsesFreshStrongValidatorAndSharedIdentityTiming()
    {
        var wire = new SyncWire(); var client = Client(wire); var api = new SubtitleSyncApi(client); var authority = client.CaptureContext();
        var read = await api.GetAsync(authority, 7, "stored-9");
        var reset = await api.ResetAsync(authority, 7, "stored-9", read.ETag!);
        Assert.Equal("\"revision-2\"", wire.Validator);
        Assert.Equal(0, wire.Body!.Value.GetProperty("offset_ms").GetInt32());
        Assert.Equal(1, wire.Body.Value.GetProperty("scale").GetDouble());
        Assert.True(reset.Body.Subtitle.Timing.IsIdentity);
        Assert.Equal("/api/v2/subtitles/7/sync/stored-9/timing", wire.Paths.Last());
    }

    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(412)] [InlineData(422)]
    public async Task MutationsDoNotRefreshOrReplayRejectedWrites(int status)
    {
        var wire = new SyncWire { Reject = status }; var client = Client(wire); var refreshes = 0;
        client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        var api = new SubtitleSyncApi(client); var context = client.CaptureContext();
        var ex = await Assert.ThrowsAsync<ApiException>(() => api.StartAsync(context, 7, "stored-9"));
        Assert.Equal(status, ex.StatusCode);
        await Assert.ThrowsAsync<ApiException>(() => api.ResetAsync(context, 7, "stored-9", "\"revision-2\""));
        Assert.Equal(2, wire.Writes); Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task OldProfileAuthorityAndWeakValidatorCannotSendMutation()
    {
        var wire = new SyncWire(); var client = Client(wire); var api = new SubtitleSyncApi(client); var old = client.CaptureContext();
        client.SetProfile("other");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.StartAsync(old, 7, "stored-9"));
        await Assert.ThrowsAsync<ArgumentException>(() => api.ResetAsync(client.CaptureContext(), 7, "stored-9", "W/\"old\""));
        Assert.Equal(0, wire.Writes);
    }

    [Fact]
    public async Task ReadFinishingInReplacementProfileCannotExposeOldState()
    {
        var wire = new SyncWire { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var client = Client(wire); var api = new SubtitleSyncApi(client);
        var read = api.GetAsync(client.CaptureContext(), 7, "stored-9");
        await wire.Started.Task; client.SetProfile("replacement"); wire.Gate.TrySetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(0, wire.Writes);
    }

    [Fact]
    public async Task CancelledReadDoesNotBecomeMutationOrOutliveOwner()
    {
        var wire = new SyncWire { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var client = Client(wire); var api = new SubtitleSyncApi(client); using var owner = new CancellationTokenSource();
        var read = api.GetAsync(client.CaptureContext(), 7, "stored-9", owner.Token);
        await wire.Started.Task; owner.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(0, wire.Writes);
    }

    private static SiloApiClient Client(SyncWire wire)
    { var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://subtitle-sync-fixture.invalid"); client.SetAccessToken("fixture-only"); client.SetProfile("own"); return client; }
    private sealed class SyncWire : HttpMessageHandler
    {
        public TaskCompletionSource<bool>? Gate;
        public TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Paths = []; public int Writes, Reject;
        public string? Validator; public JsonElement? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Started.TrySetResult(true);
            if (Gate != null) await Gate.Task.WaitAsync(ct);
            if (request.Method != HttpMethod.Get)
            {
                Writes++;
                Validator = request.Headers.IfMatch.FirstOrDefault()?.ToString();
                if (request.Content != null) Body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct)).RootElement.Clone();
                if (Reject > 0) return new((HttpStatusCode)Reject) { Content = new StringContent("""{"error":"rejected","message":"Fixture rejection"}""", System.Text.Encoding.UTF8, "application/json") };
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                """{"subtitle":{"key":"stored-9","media_file_id":"7","source":"downloaded","timing":{"offset_ms":0,"scale":1},"sync":{"id":"5","status":"synced","trigger":"manual","created_at":"2026-10-09T12:00:00Z"}}}""",
                System.Text.Encoding.UTF8, "application/json") };
            response.Headers.ETag = new("\"revision-2\""); return response;
        }
    }
}
