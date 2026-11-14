using System.Threading.Channels;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminMonitorLogsStreamTests
{
    [Theory]
    [InlineData("app")]
    [InlineData("audit")]
    public async Task HistoryTimeoutSettlesLoadingAndAllowsRetry(string stream)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout", new TimeoutException()));
        var api = new AdminMonitorApi(kit.Client);
        using var controller = new LogsPageController(api, authority);
        await controller.OpenAsync(stream, new(), false);
        var tickets = kit.Http.Requests.Count(request => request.StartsWith("POST"));
        Assert.False(controller.State.Loading);
        Assert.NotNull(controller.State.Error);
        await controller.BrowseHistoryAsync();
        Assert.True(controller.State.History);
        Assert.False(controller.State.Loading);
        Assert.NotNull(controller.State.Error);
        Assert.True(controller.State.Owner!.IsCurrent);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}"""));
        await controller.RetryHistoryAsync();
        Assert.False(controller.State.Loading);
        Assert.Null(controller.State.Error);
        Assert.Equal(tickets, kit.Http.Requests.Count(request => request.StartsWith("POST")));
    }

    [Fact]
    public async Task HistoryInvalidCursorWaitsForManualFreshPageAndNeverRemintsTheLiveTicket()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? AdminMonitorKit.Json("{\"ticket\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"protocol\":\"silo.admin-logs.v2\",\"expires_in\":30,\"max_connection_seconds\":300}")
            : request.RequestUri!.Query.Contains("cursor=") ? AdminMonitorKit.Json("""{"type":"about:blank","title":"Invalid cursor","status":400,"code":"invalid_cursor"}""", System.Net.HttpStatusCode.BadRequest) : AdminMonitorKit.Json("{\"items\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"older\"}}"));
        var socket = new LogsSyntheticSocket(); var api = new AdminMonitorApi(kit.Client);
        using var controller = new LogsPageController(api, authority, new(api, () => socket));
        var opened = controller.OpenAsync("app", new(), false);
        await socket.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await controller.BrowseHistoryAsync();
        await opened.WaitAsync(TimeSpan.FromSeconds(5));
        await controller.OlderAsync();
        Assert.True(controller.State.HistoryStale);
        Assert.False(controller.State.Loading);
        var requests = kit.Http.Requests.Count;
        await controller.RetryHistoryAsync();
        Assert.Equal(requests + 1, kit.Http.Requests.Count);
        Assert.Equal(1, controller.State.Page);
        Assert.False(controller.State.HistoryStale);
        Assert.DoesNotContain("cursor=", kit.Http.Requests.Last());
        Assert.Single(kit.Http.Requests.Where(request => request.StartsWith("POST")));
        Assert.True(socket.Aborted);
    }

    [Fact]
    public async Task RetiringAQueuedAppendPreventsItsCoalescedPublication()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"ticket\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"protocol\":\"silo.admin-logs.v2\",\"expires_in\":30,\"max_connection_seconds\":300}"));
        var socket = new LogsSyntheticSocket(); var api = new AdminMonitorApi(kit.Client);
        using var controller = new LogsPageController(api, authority, new(api, () => socket));
        var opened = controller.OpenAsync("app", new(), false);
        await socket.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await socket.Frames.Writer.WriteAsync("{\"type\":\"snapshot\",\"stream\":\"app\",\"entries\":[]}");
        await WaitFor(controller, () => controller.State.Connection == "live");
        await socket.Frames.Writer.WriteAsync("{\"type\":\"append\",\"stream\":\"app\",\"entry\":{\"id\":1,\"timestamp\":\"2026-11-14T12:00:00Z\",\"level\":\"info\",\"component\":\"scanner\",\"message\":\"retired\"}}");
        authority.Retire();
        await opened.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        Assert.Empty(controller.State.AppRows);
        Assert.Null(controller.State.Owner);
        Assert.Null(controller.State.Error);
        Assert.True(socket.Aborted);
    }

    [Theory]
    [InlineData("https://silo.test", "wss://silo.test/api/v2/admin/logs/ws")]
    [InlineData("https://silo.test/silo", "wss://silo.test/silo/api/v2/admin/logs/ws")]
    [InlineData("https://silo.test/silo/", "wss://silo.test/silo/api/v2/admin/logs/ws")]
    [InlineData("http://silo.test:8080/silo", "ws://silo.test:8080/silo/api/v2/admin/logs/ws")]
    public async Task LogSocketUsesTicketProtocolsAndWaitsForMatchingSnapshot(string baseUrl, string socketUrl)
    {
        using var kit = AdminMonitorKit.Create();
        kit.Client.SetBaseUrl(baseUrl);
        kit.Auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        // SetTokens starts a new session and clears the kit's authenticated user.
        kit.Auth.SetCurrentUser(new SiloPlayer.Core.Models.Auth.UserInfo { Id = "1", Username = "synthetic", Role = "admin" });
        kit.Auth.SelectProfile("p1", null, new SiloPlayer.Core.Models.Auth.Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"ticket":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","protocol":"silo.admin-logs.v2","expires_in":30,"max_connection_seconds":300}"""));
        var socket = new LogsSyntheticSocket();
        var client = new LogsStreamClient(new AdminMonitorApi(kit.Client), () => socket);
        using var lease = authority.Capture();
        var received = new TaskCompletionSource<LogsSocketFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(lease, "app", new() { Message = "secret test filter" }, frame => received.TrySetResult(frame));
        await socket.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(socketUrl + "?stream=app&q=secret%20test%20filter&limit=100", socket.Address!.AbsoluteUri);
        Assert.Equal(["silo.admin-logs.v2", "silo.ticket.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"], socket.Protocols);
        await socket.Frames.Writer.WriteAsync("""{"type":"snapshot","stream":"audit","entries":[]}""");
        Assert.False(received.Task.IsCompleted);
        await socket.Frames.Writer.WriteAsync("""{"type":"snapshot","stream":"app","entries":[]}""");
        Assert.Equal("snapshot", (await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).Type);
        authority.Retire();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(socket.Aborted);
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task RepeatedDisconnectFailuresRequireManualReconnectAndFilterChangesImmediatelyHideOldRows()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"ticket":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","protocol":"silo.admin-logs.v2","expires_in":30,"max_connection_seconds":300}"""));
        var sockets = new List<LogsSyntheticSocket>();
        var created = new TaskCompletionSource<LogsSyntheticSocket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new AdminMonitorApi(kit.Client);
        using var controller = new LogsPageController(api, authority, new LogsStreamClient(api, () =>
        {
            var socket = new LogsSyntheticSocket { ConnectFailure = sockets.Count == 0 ? null : new System.Net.WebSockets.WebSocketException() };
            sockets.Add(socket); created.TrySetResult(socket); return socket;
        }));
        var opened = controller.OpenAsync("app", new(), debounce: false);
        await (await created.Task.WaitAsync(TimeSpan.FromSeconds(5))).Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sockets[0].Frames.Writer.WriteAsync("""{"type":"snapshot","stream":"app","entries":[{"id":1,"timestamp":"2026-11-14T12:00:00Z","level":"info","component":"scanner","message":"First"}]}""");
        await WaitFor(controller, () => controller.State.Connection == "live");
        Assert.Single(controller.State.AppRows);
        await sockets[0].Frames.Writer.WriteAsync(null);
        await opened.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("disconnected", controller.State.Connection);
        Assert.Equal(4, sockets.Count);
        Assert.Equal(4, kit.Http.Requests.Count);
        Assert.All(sockets, socket => Assert.True(socket.Aborted));
        var changed = controller.SelectAsync("app", new() { Message = "new" });
        Assert.Empty(controller.State.AppRows);
        Assert.Equal("connecting", controller.State.Connection);
        kit.SetRole("user");
        await changed;
        Assert.Empty(controller.State.AppRows);
        Assert.Equal("disconnected", controller.State.Connection);
        Assert.Equal(4, sockets.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanCloseOrTicketLifetimeRenewsUnderTheSameLiveSelection(bool lifetimeExpired)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"ticket":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","protocol":"silo.admin-logs.v2","expires_in":30,"max_connection_seconds":300}"""));
        var sockets = Channel.CreateUnbounded<LogsSyntheticSocket>();
        var time = new DashboardManualTime();
        var api = new AdminMonitorApi(kit.Client);
        using var controller = new LogsPageController(api, authority, new LogsStreamClient(api, () =>
        {
            var socket = new LogsSyntheticSocket(); sockets.Writer.TryWrite(socket); return socket;
        }, lifetimeExpired ? time : TimeProvider.System));
        var opened = controller.OpenAsync("app", new() { Message = "keep filter" }, false);
        var first = await sockets.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await first.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await first.Frames.Writer.WriteAsync("""{"type":"snapshot","stream":"app","entries":[{"id":1,"timestamp":"2026-11-14T12:00:00Z","level":"info","component":"scanner","message":"old"}]}""");
        await WaitFor(controller, () => controller.State.Connection == "live");
        var owner = controller.State.Owner;
        var selection = controller.State.Selection;
        if (lifetimeExpired) time.Advance(TimeSpan.FromSeconds(300));
        else await first.Frames.Writer.WriteAsync(null);
        var second = await sockets.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await second.Connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(first.Aborted);
        Assert.Same(owner, controller.State.Owner);
        Assert.Equal(selection, controller.State.Selection);
        Assert.Equal("live", controller.State.Connection);
        Assert.Equal(2, kit.Http.Requests.Count);
        Assert.Contains("q=keep%20filter", second.Address!.Query);
        await second.Frames.Writer.WriteAsync("""{"type":"snapshot","stream":"app","entries":[{"id":2,"timestamp":"2026-11-14T12:00:01Z","level":"info","component":"scanner","message":"fresh"}]}""");
        await WaitFor(controller, () => controller.State.AppRows.FirstOrDefault()?.Id == "2");
        Assert.Single(controller.State.AppRows);
        kit.SetRole("user");
        await opened.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(second.Aborted);
        Assert.Empty(controller.State.AppRows);
        Assert.Equal(2, kit.Http.Requests.Count);
        Assert.False(sockets.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Unauthorized)]
    [InlineData(System.Net.HttpStatusCode.Forbidden)]
    public async Task TicketAccessFailuresNeverRetryOrRefreshAuthentication(System.Net.HttpStatusCode status)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var refreshes = 0;
        var sockets = 0;
        kit.Client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", status));
        var stream = new LogsStreamClient(new(kit.Client), () => { sockets++; return new LogsSyntheticSocket(); });
        await Assert.ThrowsAsync<ApiException>(() => stream.RunAsync(lease, "app", new(), _ => { }));
        Assert.Single(kit.Http.Requests);
        Assert.Equal(0, sockets);
        Assert.Equal(0, refreshes);
    }

    private static async Task WaitFor(LogsPageController controller, Func<bool> predicate)
    {
        if (predicate()) return;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed() { if (predicate()) done.TrySetResult(); }
        controller.Changed += Changed;
        try { Changed(); await done.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { controller.Changed -= Changed; }
    }
}

internal sealed class LogsSyntheticSocket : ILogsSocket
{
    public Channel<string?> Frames { get; } = Channel.CreateUnbounded<string?>();
    public TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Uri? Address { get; private set; }
    public IReadOnlyList<string> Protocols { get; private set; } = [];
    public bool Aborted { get; private set; }
    public Exception? ConnectFailure { get; init; }
    public Task ConnectAsync(Uri address, IReadOnlyList<string> protocols, CancellationToken ct)
    {
        Address = address; Protocols = protocols; Connected.TrySetResult(); return ConnectFailure == null ? Task.CompletedTask : Task.FromException(ConnectFailure);
    }
    public async Task<string?> ReceiveAsync(CancellationToken ct) => await Frames.Reader.ReadAsync(ct);
    public void Abort() => Aborted = true;
    public void Dispose() { Abort(); Frames.Writer.TryComplete(); }
}
