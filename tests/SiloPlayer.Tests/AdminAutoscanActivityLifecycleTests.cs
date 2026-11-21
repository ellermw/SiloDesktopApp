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
        /// <summary>The live total the history endpoints report.</summary>
        internal int Total = 75;
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Reply;

        /// <summary>
        /// A history page as the server serves it: the cursor "o&lt;offset&gt;" is the signed continuation, every page holds
        /// <c>limit</c> rows with unique identities, and only a full page carries the next cursor.
        /// </summary>
        internal static string Rows(Uri uri, string template, int total)
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var limit = int.Parse(query["limit"] ?? "50"); var cursor = query["cursor"];
            var offset = cursor == null ? 0 : int.Parse(cursor[1..]);
            var count = Math.Max(0, Math.Min(limit, total - offset));
            var items = Enumerable.Range(0, count).Select(index => uri.AbsolutePath.EndsWith("/scans")
                ? template.Replace("scan-1", "scan-" + (offset + index))
                : template.Replace("\"id\":\"11\"", "\"id\":\"" + (1000 + offset + index) + "\""));
            var more = offset + count < total;
            return "{\"items\":[" + string.Join(',', items) + "],\"total\":" + total + ",\"page\":{\"has_more\":" + (more ? "true" : "false") + (more ? ",\"next_cursor\":\"o" + (offset + count) + "\"" : "") + "}}";
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Reads.Enqueue(request.RequestUri!.PathAndQuery);
            if (request.Method != HttpMethod.Get) Writes++;
            return Reply?.Invoke(request) ?? Task.FromResult(Json(request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/admin/autoscan/scans" => Rows(request.RequestUri, Scan, Total),
                "/api/v2/admin/autoscan/events" => Rows(request.RequestUri, Poll, Total),
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

    /// <summary>A channel that, like the event transport, guards its subscriptions with a lock it also holds while it raises retirement.</summary>
    internal sealed class LockedChannel : IAdminRealtimeChannel
    {
        internal object Lock { get; } = new();
        internal int Leases;
        /// <summary>Runs just before a caller waits for the transport lock.</summary>
        internal Action? Entering;
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived { add { } remove { } }
        public event Action<string, string, JsonElement>? EventReceived { add { } remove { } }
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public IDisposable Subscribe(params string[] channels) { Entering?.Invoke(); lock (Lock) Leases++; return new Release(this); }
        public bool TryGetLatestSnapshot(string channel, out JsonElement data) { data = default; return false; }
        private sealed class Release(LockedChannel channel) : IDisposable
        {
            private int _released;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0) return;
                channel.Entering?.Invoke(); lock (channel.Lock) channel.Leases--;
            }
        }
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

    private static int ScanReads(Harness harness) => harness.Wire.Reads.Count(path => path.StartsWith("/api/v2/admin/autoscan/scans"));

    [Fact]
    public async Task NumberedPagesWalkTheEarlierContinuationsOnceAndReuseThem()
    {
        using var harness = new Harness(); using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        await controller.OpenAsync(); Assert.Equal(1, ScanReads(harness)); Assert.Equal(3, controller.State.Scans.PageCount);
        await controller.GoToPageAsync(2);
        // The official view offsets by walking the signed continuations: page 1's cursor comes from page 0, page 2's from page 1.
        Assert.Equal(3, ScanReads(harness)); Assert.Equal(2, controller.State.Scans.PageIndex);
        Assert.Equal("scan-50", controller.State.Scans.Items[0].Id); Assert.Equal(25, controller.State.Scans.Items.Count);
        await controller.GoToPageAsync(1); Assert.Equal(4, ScanReads(harness)); Assert.Equal("scan-25", controller.State.Scans.Items[0].Id);
        await controller.GoToPageAsync(0); Assert.Equal(5, ScanReads(harness)); Assert.Equal("scan-0", controller.State.Scans.Items[0].Id);
        await controller.GoToPageAsync(2); Assert.Equal(6, ScanReads(harness)); Assert.Equal(2, controller.State.Scans.PageIndex);
        Assert.Contains("cursor=o50", harness.Wire.Reads.Last(path => path.StartsWith("/api/v2/admin/autoscan/scans")));
        await controller.GoToPageAsync(99); Assert.Equal(2, controller.State.Scans.PageIndex); Assert.Equal(6, ScanReads(harness));
    }

    [Fact]
    public async Task ExplicitReloadWalksFromTheFirstPageRatherThanTrustingCachedContinuations()
    {
        using var harness = new Harness(); using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        await controller.OpenAsync(); await controller.GoToPageAsync(2); var before = ScanReads(harness);
        await controller.RefreshAsync(); Assert.Equal(before + 1, ScanReads(harness));
        await controller.ReloadHistoryAsync(); Assert.Equal(before + 4, ScanReads(harness));
        Assert.Equal(2, controller.State.Scans.PageIndex);
    }

    [Fact]
    public async Task FilterAndViewChangesReturnToTheFirstPageAndShareTheSearch()
    {
        using var harness = new Harness(); using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        await controller.OpenAsync(); await controller.GoToPageAsync(1);
        await controller.FilterAsync("/new path", "failed", 50);
        var request = harness.Wire.Reads.Last(path => path.StartsWith("/api/v2/admin/autoscan/scans"));
        Assert.Contains("limit=50", request); Assert.Contains("q=%2Fnew%20path", request);
        Assert.Contains("status=failed", request); Assert.DoesNotContain("cursor=", request);
        Assert.Equal(0, controller.State.Scans.PageIndex);
        await controller.GoToPageAsync(1);
        await controller.SelectAsync(AdminAutoscanHistory.Polls);
        var poll = harness.Wire.Reads.Last(path => path.StartsWith("/api/v2/admin/autoscan/events"));
        // The search and page size are shared; the failed status belongs to Scans only; a view change returns to its first page.
        Assert.Contains("q=%2Fnew%20path", poll); Assert.Contains("limit=50", poll); Assert.DoesNotContain("status=", poll); Assert.DoesNotContain("cursor=", poll);
        Assert.Equal("/new path", controller.State.Query); Assert.Equal("failed", controller.State.ScanStatus); Assert.Equal("", controller.State.PollStatus);
        Assert.Equal(0, controller.State.Polls.PageIndex);
        await controller.ResetFiltersAsync();
        Assert.Equal("", controller.State.Query); Assert.Equal("", controller.State.ScanStatus); Assert.Equal(50, controller.State.PageSize);
        Assert.False(controller.State.HasHistoryFilters);
    }

    [Fact]
    public async Task ALateReadForAnEarlierPageNeverOverwritesTheNewerSelection()
    {
        using var harness = new Harness(); using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        await controller.OpenAsync();
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Only the read of page 1 is held; every other request is answered by the default wire (a null reply falls through to it).
        harness.Wire.Reply = request => request.RequestUri!.AbsolutePath.EndsWith("/scans") && request.RequestUri.Query.Contains("cursor=o25") ? held.Task : null!;
        var slow = controller.GoToPageAsync(1);
        harness.Wire.Reply = null; await controller.GoToPageAsync(0);
        held.SetResult(Json(Wire.Rows(new Uri("https://fixture.invalid/api/v2/admin/autoscan/scans?limit=25&cursor=o25"), Scan, 75))); await slow;
        Assert.Equal(0, controller.State.Scans.PageIndex); Assert.Equal("scan-0", controller.State.Scans.Items[0].Id); Assert.Null(controller.State.Scans.Failure);
    }

    [Fact]
    public async Task ARepeatedContinuationFailsTheReadInsteadOfLooping()
    {
        using var harness = new Harness();
        harness.Wire.Reply = request => Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith("/scans")
            ? Page(Scan, "same").Replace("\"total\":75", "\"total\":1000")
            : request.RequestUri.AbsolutePath.EndsWith("/status") ? """{"enabled":true,"active_scans":0,"accepted_scans":0,"running_scans":0,"sources":[],"running_polls":[]}"""
            : """{"items":[],"page":{"has_more":false}}"""));
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        await controller.OpenAsync(); Assert.Equal(1, controller.State.Scans.Items.Count);
        await controller.GoToPageAsync(3);
        Assert.NotNull(controller.State.Scans.Failure); Assert.Empty(controller.State.Scans.Items); Assert.False(controller.State.Scans.Loaded);
        Assert.True(ScanReads(harness) <= 4);
    }

    [Fact]
    public async Task AShrinkingResultMovesBackToTheLastPageThatStillHasRows()
    {
        using var harness = new Harness(); using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        await controller.OpenAsync(); await controller.GoToPageAsync(2); Assert.Equal(2, controller.State.Scans.PageIndex);
        harness.Wire.Total = 30; await controller.RefreshAsync();
        Assert.Equal(1, controller.State.Scans.PageIndex); Assert.Equal(5, controller.State.Scans.Items.Count); Assert.Equal(2, controller.State.Scans.PageCount);
        harness.Wire.Total = 0; await controller.RefreshAsync();
        Assert.Equal(0, controller.State.Scans.PageIndex); Assert.Empty(controller.State.Scans.Items); Assert.True(controller.State.Scans.Loaded);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetirementRaisedUnderTheTransportLockNeverDeadlocksOpenOrDispose(bool opened)
    {
        // Not in using blocks: if the lock order regressed, disposing here would wait behind the two stuck threads instead of failing.
        var harness = new Harness(); var channel = new LockedChannel();
        var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        if (opened) { await controller.OpenAsync(); Assert.Equal(1, channel.Leases); }
        var held = new ManualResetEventSlim(); var entering = new ManualResetEventSlim();
        channel.Entering = entering.Set;
        // The transport raises access_changed while holding its own lock; the retirement reaches the controller on that thread,
        // while the page thread is on its way into the same lock to subscribe (open) or to release its lease (dispose).
        var socket = Task.Run(() =>
        {
            lock (channel.Lock) { held.Set(); Assert.True(entering.Wait(TimeSpan.FromSeconds(30))); harness.Authority.Retire(); }
        });
        Assert.True(held.Wait(TimeSpan.FromSeconds(30)));
        var page = opened ? Task.Run(() => controller.Dispose()) : Task.Run(() => controller.OpenAsync());
        await Task.WhenAll(socket, page).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(0, channel.Leases); Assert.False(controller.HasOwner); Assert.False(controller.State.IsOpen);
        controller.Dispose(); harness.Dispose();
    }

    [Fact]
    public async Task HistoryDepthStopsAtOneHundredPagesWithoutWalkingPastThem()
    {
        using var harness = new Harness(); harness.Wire.Total = 10000;
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        await controller.OpenAsync(); Assert.Equal(1, ScanReads(harness));
        // 10,000 rows at 25 per page is 400 pages; the official view refuses an offset of 100 pages or more, so 100 are offered.
        Assert.Equal(100, controller.State.Scans.PageCount); Assert.True(controller.State.Scans.Capped);
        await controller.GoToPageAsync(500);
        Assert.Equal(99, controller.State.Scans.PageIndex); Assert.Equal("scan-2475", controller.State.Scans.Items[0].Id);
        var offsets = harness.Wire.Reads.Where(path => path.StartsWith("/api/v2/admin/autoscan/scans")).Select(path => path.Contains("cursor=o") ? int.Parse(path[(path.IndexOf("cursor=o") + 8)..].Split('&')[0]) : 0).ToArray();
        Assert.Equal(2475, offsets.Max()); Assert.Equal(100, offsets.Distinct().Count());
        await controller.GoToPageAsync(99); Assert.Equal(100, ScanReads(harness));
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "Scan state changed")]
    [InlineData(HttpStatusCode.PreconditionFailed, "Scan state changed")]
    [InlineData(HttpStatusCode.Forbidden, "Scan state changed")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "Scan state changed")]
    public async Task DefiniteRefusalShowsTheServerReasonAndAllowsAnotherRequest(HttpStatusCode status, string title)
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync();
        channel.Snapshot("""[{"id":"active","library_id":7,"mode":"file","trigger":"autoscan","status":"running"}]""");
        harness.Wire.Reply = request => Task.FromResult(request.Method == HttpMethod.Post
            ? Json("{\"title\":\"" + title + "\",\"status\":" + (int)status + "}", status)
            : Json("""{"enabled":true,"active_scans":1,"accepted_scans":0,"running_scans":1,"sources":[],"running_polls":[]}"""));
        await controller.CancelAsync("7");
        Assert.Empty(controller.State.CancelReview);
        Assert.Contains(title, controller.State.Message);
        Assert.Null(controller.State.Cancelling);
        await controller.CancelAsync("7"); Assert.Equal(2, harness.Wire.Writes);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task ServerFailureOrTimeoutAfterSendingIsAnUnknownOutcome(HttpStatusCode status)
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync();
        channel.Snapshot("""[{"id":"active","library_id":7,"mode":"file","trigger":"autoscan","status":"running"}]""");
        harness.Wire.Reply = request => Task.FromResult(request.Method == HttpMethod.Post
            ? Json("{\"title\":\"synthetic\",\"status\":" + (int)status + "}", status)
            : Json("""{"enabled":true,"active_scans":0,"accepted_scans":0,"running_scans":0,"sources":[],"running_polls":[]}"""));
        await controller.CancelAsync("7");
        Assert.True(controller.State.CancelReview.ContainsKey("7"));
        await controller.CancelAsync("7"); Assert.Equal(1, harness.Wire.Writes);
        await controller.ReconcileCancelAsync("7"); Assert.Empty(controller.State.CancelReview); Assert.Equal(1, harness.Wire.Writes);
    }

    [Fact]
    public async Task FailedBackgroundReadsKeepTheRowsAndStatusAndKeepPolling()
    {
        using var harness = new Harness(); var clock = new Clock();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), clock);
        await controller.OpenAsync();
        Assert.Equal(25, controller.State.Scans.Items.Count); Assert.NotNull(controller.State.Status);
        harness.Wire.Reply = _ => Task.FromResult(Json("""{"title":"synthetic unavailable","status":503}""", HttpStatusCode.ServiceUnavailable));
        await controller.RefreshAsync();
        Assert.NotNull(controller.State.Scans.Failure); Assert.Equal(25, controller.State.Scans.Items.Count);
        Assert.NotNull(controller.State.StatusFailure); Assert.NotNull(controller.State.Status);
        Assert.Contains(clock.Pending, timer => !timer.Disposed && timer.Delay == TimeSpan.FromSeconds(15));
        harness.Wire.Reply = null;
        await controller.RefreshAsync();
        Assert.Null(controller.State.Scans.Failure); Assert.Null(controller.State.StatusFailure);
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
