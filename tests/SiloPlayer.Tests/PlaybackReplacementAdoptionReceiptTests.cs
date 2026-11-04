using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackReplacementAdoptionReceiptTests
{
    [Theory]
    [InlineData("failure", false)]
    [InlineData("failure", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    [InlineData("success", false)]
    [InlineData("success", true)]
    public async Task OwnedReplacementReportsEachAdoptedResponseBeforeFallibleReconciliation(string outcome, bool sameSessionId)
    {
        await using var fixture = new Fixture(outcome, sameSessionId);
        var original = await fixture.Start(41);
        var receipts = new ConcurrentQueue<PlaybackStartResponse>();
        using var cancellation = new CancellationTokenSource();
        var replacement = fixture.Manager.StartOwnedReplacementSessionAsync(71, 25, adopted =>
        {
            Assert.Same(adopted, fixture.Manager.CurrentSession);
            receipts.Enqueue(adopted);
        }, ct: cancellation.Token);
        await fixture.Wire.ReplacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Manager.RecordLocalSubtitleSelection(null);
        fixture.Wire.ReleaseReplacement.TrySetResult();
        await fixture.Wire.ReplanStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var acceptedStart = Assert.Single(receipts);
        Assert.NotSame(original, acceptedStart);
        Assert.Same(acceptedStart, fixture.Manager.CurrentSession);
        Assert.Equal("start-71", acceptedStart.PlanId);
        Assert.Equal(sameSessionId, acceptedStart.SessionId == original.SessionId);
        Assert.False(replacement.IsCompleted);
        if (outcome == "cancel") cancellation.Cancel();
        fixture.Wire.ReleaseReplan.TrySetResult();

        if (outcome == "failure")
        {
            var retained = await replacement;
            Assert.Same(acceptedStart, retained);
            Assert.True(retained.SubtitleReconciliationFailed);
        }
        else if (outcome == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replacement);
        else
        {
            var acceptedReplan = await replacement;
            Assert.Equal(2, receipts.Count);
            Assert.Same(acceptedReplan, receipts.Last());
            Assert.NotSame(acceptedStart, acceptedReplan);
            Assert.Equal("replan-71", acceptedReplan.PlanId);
            Assert.Same(acceptedReplan, fixture.Manager.CurrentSession);
            Assert.Equal(-1, fixture.Manager.SubtitleSelectionForLoad);
        }
        if (outcome != "success")
        {
            Assert.Single(receipts);
            Assert.Same(acceptedStart, fixture.Manager.CurrentSession);
        }
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("success")]
    public async Task ReconciliationNeverBorrowsAnUnrelatedSuccessorResponse(string outcome)
    {
        await using var fixture = new Fixture(outcome, sameSessionId: false);
        await fixture.Start(41);
        var receipts = new ConcurrentQueue<PlaybackStartResponse>();
        var replacement = fixture.Manager.StartOwnedReplacementSessionAsync(71, 25, receipts.Enqueue);
        await fixture.Wire.ReplacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Manager.RecordLocalSubtitleSelection(null);
        fixture.Wire.ReleaseReplacement.TrySetResult();
        await fixture.Wire.ReplanStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var acceptedStart = Assert.Single(receipts);
        var successor = await fixture.Start(99);
        fixture.Wire.ReleaseReplan.TrySetResult();
        if (outcome == "failure") await Assert.ThrowsAsync<ApiException>(() => replacement);
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replacement);
        Assert.Same(successor, fixture.Manager.CurrentSession);
        Assert.Same(acceptedStart, Assert.Single(receipts));
        Assert.NotSame(successor, acceptedStart);
        Assert.DoesNotContain(fixture.Wire.ReplanPaths, path => path.Contains("session-99", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdoptionCallbackCannotReplanAnUnrelatedSuccessor()
    {
        await using var fixture = new Fixture("success", sameSessionId: false);
        await fixture.Start(41);
        PlaybackStartResponse? acceptedStart = null;
        PlaybackStartResponse? successor = null;
        var replacement = fixture.Manager.StartOwnedReplacementSessionAsync(71, 25, adopted =>
        {
            acceptedStart = adopted;
            successor = fixture.Start(99).GetAwaiter().GetResult();
            fixture.Manager.RecordLocalSubtitleSelection(null);
        });
        await fixture.Wire.ReplacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Wire.ReleaseReplacement.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replacement.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.NotNull(acceptedStart);
        Assert.NotSame(acceptedStart, successor);
        Assert.Same(successor, fixture.Manager.CurrentSession);
        Assert.Equal(99, successor!.MediaFileId);
        Assert.Empty(fixture.Wire.ReplanPaths);
    }

    [Fact]
    public async Task QueuedReconciliationCannotReplanAnUnrelatedSuccessor()
    {
        await using var fixture = new Fixture("success", sameSessionId: false);
        await fixture.Start(41);
        var gate = (SemaphoreSlim)typeof(PlaybackManager).GetField("_replanGuard", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(fixture.Manager)!;
        await gate.WaitAsync();
        var adopted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var replacement = fixture.Manager.StartOwnedReplacementSessionAsync(71, 25, _ => adopted.TrySetResult());
            await fixture.Wire.ReplacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Manager.RecordLocalSubtitleSelection(null);
            fixture.Wire.ReleaseReplacement.TrySetResult();
            await adopted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // Observe the actual queued waiter rather than racing the callback's
            // asynchronous signal against the following semaphore await (.NET 8).
            var waiters = typeof(SemaphoreSlim).GetField("m_asyncHead", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (waiters.GetValue(gate) == null && DateTime.UtcNow < deadline) await Task.Delay(1);
            Assert.NotNull(waiters.GetValue(gate));
            Assert.False(replacement.IsCompleted);
            var successor = await fixture.Start(99);
            fixture.Wire.ReleaseReplan.TrySetResult();
            gate.Release();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => replacement.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Same(successor, fixture.Manager.CurrentSession);
            Assert.Empty(fixture.Wire.ReplanPaths);
        }
        finally { if (gate.CurrentCount == 0) gate.Release(); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Wire Wire { get; }
        private readonly HttpClient http;
        private readonly SiloApiClient api;
        private readonly AuthService auth;
        internal PlaybackManager Manager { get; }
        internal Fixture(string outcome, bool sameSessionId)
        {
            Wire = new(outcome, sameSessionId);
            http = new(Wire);
            api = new(http); api.SetBaseUrl("https://replacement-receipt.fixture.invalid"); api.SetProfile("synthetic");
            auth = new(api, new AuthApi(api));
            Manager = new(new PlaybackApi(api), new CatalogApi(api), auth, api);
        }
        internal Task<PlaybackStartResponse> Start(int file) => Manager.StartSessionAsync(file,
            preferences: new PlaybackQualityPreferences(api.CaptureContext(), "auto", null));
        public async ValueTask DisposeAsync()
        {
            Wire.ReleaseReplacement.TrySetResult(); Wire.ReleaseReplan.TrySetResult();
            try { await Manager.StopSessionAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            finally { Manager.Dispose(); auth.Dispose(); http.Dispose(); }
        }
    }

    private sealed class Wire(string outcome, bool sameSessionId) : HttpMessageHandler
    {
        internal ConcurrentQueue<string> ReplanPaths { get; } = new();
        internal TaskCompletionSource ReplacementStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseReplacement { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReplanStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseReplan { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("replacement-receipt.fixture.invalid", request.RequestUri!.Host);
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/playback/capabilities") return Json("""{"allowed":true,"state":"available","installation_id":"11111111-1111-4111-8111-111111111111","protocol_versions":[3],"features":["fixed_media_file_v1"]}""");
            if (request.Method == HttpMethod.Post && path == "/api/v2/playback/start")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var file = int.Parse(body.RootElement.GetProperty("file_id").GetString()!);
                if (file == 71)
                {
                    ReplacementStarted.TrySetResult();
                    await ReleaseReplacement.Task.WaitAsync(cancellationToken);
                }
                return Json(Decision(file, "start-" + file, grantSubtitle: file == 71));
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/replan", StringComparison.Ordinal))
            {
                ReplanPaths.Enqueue(path);
                ReplanStarted.TrySetResult();
                await ReleaseReplan.Task.WaitAsync(cancellationToken);
                if (outcome == "failure") return new(HttpStatusCode.InternalServerError)
                { Content = new StringContent("{\"error\":\"synthetic_reconciliation_failure\"}") };
                return Json(Decision(71, "replan-71", grantSubtitle: false));
            }
            if (request.Method == HttpMethod.Delete) return Json("""{"outcome":"stopped"}""");
            if (request.Method == HttpMethod.Post) return Json("""{"outcome":"applied"}""");
            return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }
        private string Decision(int file, string plan, bool grantSubtitle)
        {
            var session = sameSessionId && file == 71 ? "session-41" : "session-" + file;
            return JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = session,
                playback_plan = new
                {
                    protocol_version = 3, plan_id = plan, plan_attempt_key = plan, session_id = session,
                    effective_media_file_id = file, delivery = "original_http", stream = new { url = "/stream/" + session },
                    timeline = new { source_start_seconds = 25, can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = grantSubtitle ? (object)new { subtitle = new { id = "automatic", index = 8 } } : new { },
                    subtitle = new { mode = grantSubtitle ? "sidecar" : "off", inventory = grantSubtitle
                        ? new object[] { new { track_id = "automatic", combined_index = 8, language = "en", codec = "srt", source = "external", url = "/automatic.srt" } }
                        : Array.Empty<object>() },
                },
            });
        }
        private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
        { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    }
}
