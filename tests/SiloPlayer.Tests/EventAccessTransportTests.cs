using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class EventAccessTransportTests
{
    [Fact]
    public async Task RepeatedShortNormalClosesIncreaseTheReconnectDelay()
    {
        await using var fixture = await Fixture.StartAsync();
        using var subscription = fixture.Events.Subscribe("notifications");
        var first = await fixture.NextAsync();
        await first.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "synthetic short connection", CancellationToken.None);
        var second = await fixture.NextAsync();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        await second.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "synthetic short connection", CancellationToken.None);
        await fixture.NextAsync();
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(1.7),
            "The second short normal close reset the reconnect delay instead of retaining the two-second backoff.");
    }

    [Fact]
    public async Task FailedTicketDoesNotStartTheConnectedInterval()
    {
        using var http = new HttpClient(new FailedTicketHandler());
        var api = new SiloApiClient(http);
        api.SetBaseUrl("https://connection-boundary.fixture.invalid");
        using var auth = new AuthService(api, new(api));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SelectProfile("synthetic-profile", "synthetic-proof");
        using var events = new EventChannelClient(api, auth);
        PrimeChannel(events);
        var opened = false;
        await Assert.ThrowsAsync<ApiException>(() => ConnectOnce(events, CancellationToken.None, () => opened = true));
        Assert.False(opened);
    }

    [Fact]
    public async Task ConnectedIntervalBeginsWithAnActuallyOpenSocket()
    {
        await using var fixture = await Fixture.StartAsync();
        PrimeChannel(fixture.Events);
        // A real loopback socket: leave room under full-suite load. The passing
        // path still ends as soon as the socket opens and stop is cancelled.
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var opened = new TaskCompletionSource<WebSocketState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationBoundary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Events.StateChanged += state =>
        {
            // This event runs after the connection cancellation check and before
            // the first ReceiveAsync. Pin the formerly intermittent cleanup race.
            if (state != WebSocketState.Open) return;
            stop.Cancel();
            cancellationBoundary.TrySetResult();
        };
        var run = ConnectOnce(fixture.Events, stop.Token, () => opened.TrySetResult(fixture.Events.CurrentState));
        try
        {
            Assert.Equal(WebSocketState.Open, await opened.Task.WaitAsync(TimeSpan.FromSeconds(20)));
            await cancellationBoundary.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            stop.Cancel();
            // This private-method fixture bypasses the normal run-loop owner.
            // Cancellation between receive iterations leaves the socket open;
            // disposing the client supplies the normal Abort before awaiting it,
            // rather than waiting for this silent fixture peer's close handshake.
            fixture.Events.Dispose();
            try { await run.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (OperationCanceledException) { }
        }
    }

    private static void PrimeChannel(EventChannelClient events)
        => ((Dictionary<string, int>)typeof(EventChannelClient).GetField("_channelRefs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(events)!)["notifications"] = 1;

    private static Task ConnectOnce(EventChannelClient events, CancellationToken ct, Action opened)
    {
        var method = typeof(EventChannelClient).GetMethod("ConnectAndRunOnce", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(3, method.GetParameters().Length);
        return (Task)method.Invoke(events, [ct, false, opened])!;
    }

    private sealed class FailedTicketHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent("Synthetic ticket unavailable") });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccessCloseReticketsImmediatelyAndRefreshesExactlyOnce(bool sendFrame)
    {
        await using var fixture = await Fixture.StartAsync();
        using var subscription = fixture.Events.Subscribe("notifications");
        var first = await fixture.NextAsync();
        await Send(first, "{\"type\":\"snapshot\",\"channel\":\"notifications\",\"data\":{\"unread\":7}}");
        await Until(() => fixture.Events.TryGetLatestSnapshot("notifications", out _));
        var prior = fixture.Api.CaptureContext();
        var invalidations = 0;
        fixture.Events.AccessChanged += _ => Interlocked.Increment(ref invalidations);
        if (sendFrame) await Send(first, "{\"type\":\"access_changed\"}");
        await first.CloseOutputAsync((WebSocketCloseStatus)4001, "fixture access changed", CancellationToken.None);
        await fixture.NextAsync();
        await Until(() => fixture.Auth.CurrentUser?.Role == "user");
        Assert.Equal(2, fixture.Tickets);
        Assert.Equal(1, fixture.AccountReads);
        Assert.Equal(1, invalidations);
        Assert.False(fixture.Api.IsCurrentContext(prior));
        Assert.False(fixture.Events.TryGetLatestSnapshot("notifications", out _));
        Assert.Equal("profile", fixture.Auth.SelectedProfileId);
    }

    [Fact]
    public async Task TransportLossCatchesUpButAnIntentionalSubscriptionRebindDoesNotReloadTheApp()
    {
        await using var fixture = await Fixture.StartAsync();
        using var subscription = fixture.Events.Subscribe("notifications");
        var first = await fixture.NextAsync();
        var invalidations = 0;
        var catchUps = 0;
        typeof(EventChannelClient).GetEvent("ReadCatchUpRequested")?.AddEventHandler(fixture.Events,
            (Action<ApiRequestContext>)(_ => Interlocked.Increment(ref catchUps)));
        fixture.Events.AccessChanged += _ => Interlocked.Increment(ref invalidations);
        await first.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "fixture transport restart", CancellationToken.None);
        await fixture.NextAsync();
        await Until(() => fixture.AccountReads == 1);
        Assert.Equal(0, invalidations);
        Assert.Equal(1, catchUps);
        // An intentional channel change rebinds the open socket in place: no new
        // socket, no catch-up and no app reload.
        using var extra = fixture.Events.Subscribe("history_import");
        await Task.Delay(100);
        Assert.Equal(2, fixture.Tickets);
        Assert.Equal(0, invalidations);
        Assert.Equal(1, catchUps);
        Assert.Equal(1, fixture.AccountReads);
    }

    private static Task Send(WebSocket socket, string json) => socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, CancellationToken.None);

    [Theory]
    [InlineData(1013)]
    [InlineData(4001)]
    public async Task EventSocketReconnectKeepsActivePlaybackProgressAlive(int closeCode)
    {
        await using var fixture = await Fixture.StartAsync();
        using var subscription = fixture.Events.Subscribe("notifications");
        var first = await fixture.NextAsync();
        using var manager = new PlaybackManager(new(fixture.Api), new(fixture.Api), fixture.Auth, fixture.Api);
        var reconnect = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ProgressReportingFailed += message => reconnect.TrySetResult(message);
        await manager.StartSessionAsync(42, forceStartPosition: true);
        var session = manager.SessionId;
        manager.UpdatePosition(123, false);
        var oldReads = fixture.Api.CaptureContext();
        await first.CloseOutputAsync((WebSocketCloseStatus)closeCode, "fixture reconnect", CancellationToken.None);
        await fixture.NextAsync();
        await Until(() => fixture.AccountReads == 1);
        Assert.False(fixture.Api.IsCurrentContext(oldReads)); // Browse reads still retire.
        // Accelerate the actual production keepalive callback, preserving its three-failure policy.
        var timer = (Timer)typeof(PlaybackManager).GetField("_progressTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        var before = fixture.ProgressReads;
        timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(20));
        await Until(() => reconnect.Task.IsCompleted || fixture.ProgressReads >= before + 3);
        Assert.False(reconnect.Task.IsCompleted, reconnect.Task.IsCompleted ? await reconnect.Task : null);
        Assert.Equal(session, manager.SessionId);
        Assert.Equal(1, fixture.PlaybackStarts);
        await manager.StopSessionAsync(123, false);
        Assert.Equal(1, fixture.PlaybackStops);
    }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(10); Assert.True(ready()); }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _http = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Channel<WebSocket> _sockets = Channel.CreateUnbounded<WebSocket>();
        internal int Tickets, AccountReads, ProgressReads, PlaybackStarts, PlaybackStops;
        internal SiloApiClient Api { get; private set; } = null!;
        internal AuthService Auth { get; private set; } = null!;
        internal EventChannelClient Events { get; private set; } = null!;
        private Fixture(WebApplication app) => _app = app;
        internal static async Task<Fixture> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var fixture = new Fixture(builder.Build());
            fixture._app.UseWebSockets();
            fixture._app.Run(async context =>
            {
                string? playbackFixture = context.Request.Path.Value switch
                {
                    "/api/v2/playback/capabilities" => "playback_capability_available",
                    "/api/v2/playback/start" => "playback_start_opaque_ids",
                    var path when path?.EndsWith("/progress") == true => "playback_progress_applied",
                    var path when path?.StartsWith("/api/v2/playback/") == true && context.Request.Method == "DELETE" => "playback_stop_completed",
                    _ => null
                };
                if (playbackFixture != null)
                {
                    if (playbackFixture == "playback_start_opaque_ids") Interlocked.Increment(ref fixture.PlaybackStarts);
                    if (playbackFixture == "playback_progress_applied") Interlocked.Increment(ref fixture.ProgressReads);
                    if (playbackFixture == "playback_stop_completed") Interlocked.Increment(ref fixture.PlaybackStops);
                    var dir = new DirectoryInfo(AppContext.BaseDirectory);
                    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SiloPlayer.sln"))) dir = dir.Parent;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "PlaybackV2", playbackFixture + ".json")));
                    return;
                }
                if (context.Request.Path == "/api/v2/events/ws-ticket")
                { Interlocked.Increment(ref fixture.Tickets); await context.Response.WriteAsync("{\"ticket\":\"isolated-fixture\",\"protocol\":\"silo.events.v2\"}"); return; }
                if (context.Request.Path == "/api/v2/account/me")
                { Interlocked.Increment(ref fixture.AccountReads); await context.Response.WriteAsync("{\"id\":\"account\",\"username\":\"Fixture\",\"role\":\"user\"}"); return; }
                if (context.Request.Path != "/api/v2/events/ws" || !context.WebSockets.IsWebSocketRequest)
                { context.Response.StatusCode = 404; return; }
                using var socket = await context.WebSockets.AcceptWebSocketAsync("silo.events.v2");
                try
                {
                    await Send(socket, "{\"type\":\"hello\",\"connection_id\":\"fixture\"}");
                    var buffer = new byte[4096];
                    await socket.ReceiveAsync(buffer, fixture._stop.Token);
                    await Send(socket, "{\"type\":\"subscribed\",\"rejected\":[]}");
                    await fixture._sockets.Writer.WriteAsync(socket);
                    await Task.Delay(Timeout.Infinite, fixture._stop.Token);
                }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
            });
            await fixture._app.StartAsync();
            var address = fixture._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            fixture.Api = new(fixture._http); fixture.Api.SetBaseUrl(address);
            fixture.Auth = new(fixture.Api, new(fixture.Api));
            fixture.Auth.SetTokens("isolated-token", "isolated-refresh", 86400);
            fixture.Auth.SetCurrentUser(new() { Id = "account", Role = "admin" });
            fixture.Auth.SelectProfile("profile", "isolated-pin");
            fixture.Events = new(fixture.Api, fixture.Auth);
            return fixture;
        }
        internal Task<WebSocket> NextAsync() => _sockets.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public async ValueTask DisposeAsync()
        {
            Events.Dispose(); Auth.Dispose(); _stop.Cancel();
            await _app.StopAsync(); await _app.DisposeAsync(); _http.Dispose(); _stop.Dispose();
        }
    }
}
