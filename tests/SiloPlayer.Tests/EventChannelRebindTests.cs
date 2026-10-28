using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
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

/// <summary>
/// Pages subscribe to realtime channels on navigation. Changing the channel set
/// must rebind the live socket with a subscribe frame (which the server applies
/// in place) rather than minting a ticket and reconnecting on every page change.
/// </summary>
public sealed class EventChannelRebindTests
{
    [Fact]
    public async Task ChannelSetChangesRebindTheOpenSocketWithoutReconnecting()
    {
        await using var fixture = await Fixture.StartAsync();
        using var shell = fixture.Events.Subscribe("notifications", "user_state");
        await fixture.UntilSubscribed(["notifications", "user_state"]);

        // Navigate to Home: catalog joins the set.
        var home = fixture.Events.Subscribe("catalog");
        await fixture.UntilSubscribed(["catalog", "notifications", "user_state"]);
        // Navigate away: catalog leaves the set.
        home.Dispose();
        await fixture.UntilSubscribed(["notifications", "user_state"]);
        // And back again.
        using var homeAgain = fixture.Events.Subscribe("catalog");
        await fixture.UntilSubscribed(["catalog", "notifications", "user_state"]);

        Assert.Equal(1, fixture.Sockets);
        Assert.Equal(1, fixture.Tickets);
        Assert.Equal(4, fixture.SubscribeFrames);
    }

    [Fact]
    public async Task ReleasingTheLastChannelStillClosesTheSocket()
    {
        await using var fixture = await Fixture.StartAsync();
        var only = fixture.Events.Subscribe("notifications");
        await fixture.UntilSubscribed(["notifications"]);

        only.Dispose();

        await Until(() => fixture.ClosedSockets == 1);
        Assert.Equal(1, fixture.Sockets);
    }

    [Fact]
    public async Task AChangeQueuedBeforeHelloIsCarriedByTheFirstSubscribe()
    {
        await using var fixture = await Fixture.StartAsync(delayHello: true);
        using var first = fixture.Events.Subscribe("notifications");
        await Until(() => fixture.Sockets == 1);
        using var second = fixture.Events.Subscribe("catalog");
        fixture.ReleaseHello();

        await fixture.UntilSubscribed(["catalog", "notifications"]);
        Assert.Equal(1, fixture.Sockets);
        Assert.Equal(1, fixture.SubscribeFrames);
    }

    private static async Task Until(Func<bool> ready)
    {
        for (var i = 0; i < 300 && !ready(); i++) await Task.Delay(10);
        Assert.True(ready());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly HttpClient _http = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _hello = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private string[] _lastSubscribed = [];
        internal int Tickets, Sockets, ClosedSockets, SubscribeFrames;
        internal SiloApiClient Api { get; private set; } = null!;
        internal AuthService Auth { get; private set; } = null!;
        internal EventChannelClient Events { get; private set; } = null!;
        private Fixture(WebApplication app) => _app = app;

        internal void ReleaseHello() => _hello.TrySetResult();

        internal async Task UntilSubscribed(string[] expected)
        {
            var sorted = expected.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            await Until(() =>
            {
                lock (_gate) return _lastSubscribed.SequenceEqual(sorted);
            });
        }

        internal static async Task<Fixture> StartAsync(bool delayHello = false)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var fixture = new Fixture(builder.Build());
            if (!delayHello) fixture.ReleaseHello();
            fixture._app.UseWebSockets();
            fixture._app.Run(async context =>
            {
                if (context.Request.Path == "/api/v2/events/ws-ticket")
                {
                    Interlocked.Increment(ref fixture.Tickets);
                    await context.Response.WriteAsync("{\"ticket\":\"rebind-fixture\",\"protocol\":\"silo.events.v2\"}");
                    return;
                }
                if (context.Request.Path != "/api/v2/events/ws" || !context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 404;
                    return;
                }
                using var socket = await context.WebSockets.AcceptWebSocketAsync("silo.events.v2");
                Interlocked.Increment(ref fixture.Sockets);
                try
                {
                    await fixture._hello.Task.WaitAsync(fixture._stop.Token);
                    await Send(socket, "{\"type\":\"hello\",\"connection_id\":\"rebind\"}");
                    var buffer = new byte[4096];
                    while (true)
                    {
                        var result = await socket.ReceiveAsync(buffer, fixture._stop.Token);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        using var frame = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        if (frame.RootElement.GetProperty("type").GetString() != "subscribe") continue;
                        var channels = frame.RootElement.GetProperty("channels").EnumerateArray()
                            .Select(channel => channel.GetString()!)
                            .OrderBy(name => name, StringComparer.Ordinal)
                            .ToArray();
                        Interlocked.Increment(ref fixture.SubscribeFrames);
                        await Send(socket, "{\"type\":\"subscribed\",\"rejected\":[]}");
                        lock (fixture._gate) fixture._lastSubscribed = channels;
                    }
                }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
                finally { Interlocked.Increment(ref fixture.ClosedSockets); }
            });
            await fixture._app.StartAsync();
            var address = fixture._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            fixture.Api = new(fixture._http);
            fixture.Api.SetBaseUrl(address);
            fixture.Auth = new(fixture.Api, new(fixture.Api));
            fixture.Auth.SetTokens("rebind-token", "rebind-refresh", 86400);
            fixture.Auth.SetCurrentUser(new() { Id = "account", Role = "user" });
            fixture.Events = new(fixture.Api, fixture.Auth);
            return fixture;
        }

        private static Task Send(WebSocket socket, string json)
            => socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            Events.Dispose();
            Auth.Dispose();
            _stop.Cancel();
            await _app.StopAsync();
            await _app.DisposeAsync();
            _http.Dispose();
            _stop.Dispose();
        }
    }
}
