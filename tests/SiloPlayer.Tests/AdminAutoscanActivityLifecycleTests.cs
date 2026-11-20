using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed partial class AdminAutoscanActivityTests
{
    internal sealed class Wire : HttpMessageHandler
    {
        internal System.Collections.Concurrent.ConcurrentQueue<string> Reads { get; } = new();
        internal int Writes;
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Reads.Enqueue(request.RequestUri!.PathAndQuery);
            if (request.Method != HttpMethod.Get) Writes++;
            return Reply?.Invoke(request) ?? Task.FromResult(Json(request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/admin/autoscan/scans" => Page(Scan, request.RequestUri.Query.Contains("cursor=") ? null : "scan-next"),
                "/api/v2/admin/autoscan/events" => Page(Poll, request.RequestUri.Query.Contains("cursor=") ? null : "poll-next"),
                "/api/v2/admin/autoscan/status" => """{"enabled":true,"active_scans":1,"accepted_scans":1,"running_scans":0,"sources":[],"running_polls":[]}""",
                _ => """{"items":[],"page":{"has_more":false}}"""
            }));
        }
    }

    internal sealed class Clock : IAdminScheduler
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-11-17T12:00:00Z");
        internal System.Collections.Concurrent.ConcurrentQueue<Handle> Pending { get; } = new();
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var handle = new Handle(delay, callback); Pending.Enqueue(handle); return handle;
        }
        internal sealed class Handle(TimeSpan delay, Action callback) : IDisposable
        {
            internal TimeSpan Delay { get; } = delay;
            internal bool Disposed { get; private set; }
            internal void Fire() { if (!Disposed) { Disposed = true; callback(); } }
            public void Dispose() => Disposed = true;
        }
    }

    internal sealed class Channel : IAdminRealtimeChannel
    {
        internal int Leases;
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public IDisposable Subscribe(params string[] channels) { Assert.Equal(new[] { "scans" }, channels); Leases++; return new Release(() => Leases--); }
        public bool TryGetLatestSnapshot(string channel, out JsonElement data) { data = default; return false; }
        internal void Snapshot(string json) => SnapshotReceived?.Invoke("scans", JsonDocument.Parse(json).RootElement.Clone());
        internal void Event(string name, string json) => EventReceived?.Invoke("scans", name, JsonDocument.Parse(json).RootElement.Clone());
        private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
    }

    internal sealed class Harness : IDisposable
    {
        internal Wire Wire { get; } = new();
        internal SiloApiClient Client { get; }
        internal AuthService Auth { get; }
        internal AdminMonitorAuthority Authority { get; }
        internal AdminAutoscanActivityApi Api { get; }
        private readonly HttpClient _http;
        internal Harness()
        {
            _http = new HttpClient(Wire); Client = new SiloApiClient(_http); Client.SetBaseUrl("https://fixture.invalid");
            Auth = new AuthService(Client, new AuthApi(Client)); Auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
            Auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
            Auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
            Authority = new AdminMonitorAuthority(Client, Auth); Api = new AdminAutoscanActivityApi(Client);
        }
        public void Dispose() { Authority.Dispose(); Auth.Dispose(); _http.Dispose(); }
    }

    [Fact]
    public async Task CursorChainsAreIndependentAndFiltersResetBeforeSending()
    {
        using var harness = new Harness(); var clock = new Clock(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, clock);
        await controller.OpenAsync(); await controller.NextAsync();
        Assert.Equal(1, controller.State.Scans.PageIndex);
        await controller.SelectAsync(AdminAutoscanHistory.Polls); await controller.NextAsync();
        Assert.Equal(1, controller.State.Polls.PageIndex);
        await controller.SelectAsync(AdminAutoscanHistory.Scans);
        Assert.Equal(1, controller.State.Scans.PageIndex);
        await controller.FilterAsync("/new path", "failed", 50);
        var request = harness.Wire.Reads.Last(path => path.StartsWith("/api/v2/admin/autoscan/scans"));
        Assert.Contains("limit=50", request); Assert.Contains("q=%2Fnew%20path", request);
        Assert.Contains("status=failed", request); Assert.DoesNotContain("cursor=", request);
        Assert.Equal(0, controller.State.Scans.PageIndex); Assert.Equal(1, controller.State.Polls.PageIndex);
    }

    [Fact]
    public async Task RetiredAndHiddenReadsCannotPublishOrScheduleWork()
    {
        using var harness = new Harness(); var clock = new Clock(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, clock);
        await controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Wire.Reply = _ => pending.Task;
        var read = controller.RefreshAsync(); controller.Suspend();
        pending.SetResult(Json(Page(Scan))); await read;
        Assert.False(controller.State.IsOpen); Assert.Empty(controller.State.Scans.Items); Assert.Equal(0, channel.Leases);
        Assert.All(clock.Pending, timer => Assert.True(timer.Disposed));
    }

    [Fact]
    public async Task UnknownCancellationNeverReplaysAndNeedsExplicitReconciliation()
    {
        using var harness = new Harness(); var clock = new Clock(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, clock);
        await controller.OpenAsync();
        channel.Snapshot("""[{"id":"active","library_id":7,"mode":"file","path":"/synthetic/movie.mkv","trigger":"autoscan","status":"running"}]""");
        harness.Wire.Reply = request => request.Method == HttpMethod.Post ? throw new HttpRequestException("synthetic uncertain outcome") : Task.FromResult(Json(Page(Scan)));
        await controller.CancelAsync("7"); Assert.Equal(1, harness.Wire.Writes);
        Assert.True(controller.State.CancelReview.ContainsKey("7"));
        await controller.CancelAsync("7"); Assert.Equal(1, harness.Wire.Writes);
        await controller.RefreshAsync(); Assert.True(controller.State.CancelReview.ContainsKey("7"));
        await controller.ReconcileCancelAsync("7"); Assert.Equal(1, harness.Wire.Writes);
    }

    [Fact]
    public async Task HiddenWindowStopsIntervalsAndRetainsItsOwnFilters()
    {
        using var harness = new Harness(); var clock = new Clock();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), clock);
        await controller.OpenAsync(); await controller.FilterAsync("synthetic", "running", 50);
        await controller.SetVisibleAsync(false);
        var before = harness.Wire.Reads.Count;
        foreach (var timer in clock.Pending.ToArray()) timer.Fire();
        await controller.RefreshAsync();
        Assert.Equal(before, harness.Wire.Reads.Count);
        Assert.All(clock.Pending, timer => Assert.True(timer.Disposed));
        await controller.SetVisibleAsync(true);
        Assert.Equal("synthetic", controller.State.Scans.Filter.Query);
        Assert.Equal(50, controller.State.Scans.Filter.Limit);
        Assert.True(harness.Wire.Reads.Count > before);
        Assert.All(clock.Pending.Where(timer => !timer.Disposed), timer => Assert.Equal(TimeSpan.FromSeconds(15), timer.Delay));
    }

    [Fact]
    public async Task RetiredProfileNeverReceivesLateRowsAndDoesNotRecapture()
    {
        using var harness = new Harness(); var clock = new Clock(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, clock);
        await controller.OpenAsync();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Wire.Reply = _ => response.Task;
        var pending = controller.RefreshAsync();
        harness.Auth.SelectProfile("p2", null, new Profile { Id = "p2", Name = "Secondary", IsPrimary = false });
        response.SetResult(Json(Page(Scan))); await pending;
        Assert.False(controller.HasOwner); Assert.False(controller.State.IsOpen); Assert.Empty(controller.State.Scans.Items);
        Assert.Equal(0, channel.Leases);
    }

    [Fact]
    public async Task HistoryDepthStopsAtOneHundredPagesWithoutWalkingAhead()
    {
        using var harness = new Harness(); var clock = new Clock(); var count = 0;
        harness.Wire.Reply = request => Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("/scans")
            ? Page(Scan, "cursor-" + Interlocked.Increment(ref count)).Replace("\"total\":75", "\"total\":10000")
            : request.RequestUri.AbsolutePath.EndsWith("/status") ? """{"enabled":true,"active_scans":0,"accepted_scans":0,"running_scans":0,"sources":[],"running_polls":[]}"""
            : """{"items":[],"page":{"has_more":false}}"""));
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), clock);
        await controller.OpenAsync(); Assert.Equal(1, count);
        for (var page = 1; page < 100; page++) await controller.NextAsync();
        Assert.Equal(99, controller.State.Scans.PageIndex); Assert.True(controller.State.Scans.Capped);
        await controller.NextAsync(); Assert.Equal(100, count);
    }

    [Fact]
    public async Task ConflictRequiresReviewAndExplicitReconciliationDoesNotWrite()
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync();
        channel.Snapshot("""[{"id":"active","library_id":7,"mode":"file","trigger":"autoscan","status":"running"}]""");
        harness.Wire.Reply = request => Task.FromResult(request.Method == HttpMethod.Post
            ? Json("""{"title":"Scan state changed","status":409}""", HttpStatusCode.Conflict)
            : Json("""{"enabled":true,"active_scans":0,"accepted_scans":0,"running_scans":0,"sources":[],"running_polls":[]}"""));
        await controller.CancelAsync("7");
        Assert.Contains("changed", controller.State.CancelReview["7"]);
        await controller.CancelAsync("7"); Assert.Equal(1, harness.Wire.Writes);
        await controller.ReconcileCancelAsync("7"); Assert.Empty(controller.State.CancelReview); Assert.Equal(1, harness.Wire.Writes);
    }

    [Fact]
    public async Task QueueFiltersBeforeBoundingDeduplicatesAndReportsEventOverflow()
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync();
        var manual = "{\"id\":\"manual\",\"library_id\":7,\"mode\":\"file\",\"trigger\":\"manual\",\"status\":\"running\"}";
        var active = "{\"id\":\"active\",\"library_id\":7,\"mode\":\"file\",\"trigger\":\"autoscan\",\"status\":\"running\"}";
        channel.Snapshot("[" + string.Join(',', Enumerable.Repeat(manual, 501).Concat([active, active])) + "]");
        Assert.Equal("active", Assert.Single(controller.State.Queue).Id); Assert.False(controller.State.QueueCapped);
        channel.Snapshot("[" + string.Join(',', Enumerable.Range(0, 500).Select(index => active.Replace("active", "run-" + index))) + "]");
        Assert.Equal(500, controller.State.Queue.Count); Assert.False(controller.State.QueueCapped);
        channel.Event("scan.queued", active);
        Assert.Equal(500, controller.State.Queue.Count); Assert.True(controller.State.QueueCapped);
        channel.Snapshot("[" + active + "]");
        Assert.Single(controller.State.Queue); Assert.False(controller.State.QueueCapped);
    }

    [Fact]
    public async Task TerminalTargetEventAllowsExplicitReviewWhileOtherLibrariesRemainActive()
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync();
        channel.Snapshot("""[{"id":"target","library_id":7,"mode":"file","trigger":"autoscan","status":"running"},{"id":"other","library_id":8,"mode":"file","trigger":"autoscan","status":"running"}]""");
        harness.Wire.Reply = request => request.Method == HttpMethod.Post ? throw new HttpRequestException("synthetic uncertainty")
            : Task.FromResult(Json("""{"enabled":true,"active_scans":1,"accepted_scans":0,"running_scans":1,"sources":[],"running_polls":[]}"""));
        await controller.CancelAsync("7");
        await controller.ReconcileCancelAsync("7"); Assert.True(controller.State.CancelReview.ContainsKey("7"));
        channel.Event("scan.completed", """{"id":"target","library_id":7,"mode":"file","trigger":"autoscan","status":"cancelled"}""");
        Assert.True(controller.State.CancelReview.ContainsKey("7"));
        await controller.ReconcileCancelAsync("7");
        Assert.Empty(controller.State.CancelReview); Assert.Equal("other", Assert.Single(controller.State.Queue).Id);
        Assert.Equal(1, harness.Wire.Writes);
    }

    [Fact]
    public async Task ReconciliationIsSingleFlightAndFailureRetainsTheUnknownWrite()
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync();
        channel.Snapshot("""[{"id":"target","library_id":7,"mode":"file","trigger":"autoscan","status":"running"}]""");
        harness.Wire.Reply = _ => throw new HttpRequestException("synthetic uncertainty");
        await controller.CancelAsync("7");
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        harness.Wire.Reply = _ => { Interlocked.Increment(ref reads); return held.Task; };
        var reconcile = controller.ReconcileCancelAsync("7");
        await controller.ReconcileCancelAsync("7"); Assert.Equal("7", controller.State.Reconciling);
        held.SetException(new HttpRequestException("synthetic read failure")); await reconcile;
        Assert.Equal(1, reads); Assert.Null(controller.State.Reconciling);
        Assert.True(controller.State.CancelReview.ContainsKey("7")); Assert.Contains("could not be reloaded", controller.State.Message);
        Assert.Equal(1, harness.Wire.Writes);
    }

    [Fact]
    public async Task HidingDuringReconciliationDiscardsItsLateAnswerAndKeepsTheReviewGate()
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync();
        channel.Snapshot("""[{"id":"target","library_id":7,"mode":"file","trigger":"autoscan","status":"running"}]""");
        harness.Wire.Reply = _ => throw new HttpRequestException("synthetic uncertainty");
        await controller.CancelAsync("7");
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Wire.Reply = _ => held.Task;
        var reconcile = controller.ReconcileCancelAsync("7"); await controller.SetVisibleAsync(false);
        held.SetResult(Json("""{"enabled":true,"active_scans":0,"accepted_scans":0,"running_scans":0,"sources":[],"running_polls":[]}"""));
        await reconcile;
        Assert.Null(controller.State.Reconciling); Assert.True(controller.State.CancelReview.ContainsKey("7"));
        Assert.Equal(1, harness.Wire.Writes);
    }
}
