using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class EventAccessChangeBehaviorTests
{
    [Fact]
    public async Task AccessChangeRefreshesAccountOnceAndInvalidatesOldReadsWithoutLosingThePinGrant()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://access-fixture.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        auth.SetTokens("fixture-token", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "account", Role = "admin" });
        auth.SelectProfile("profile", "fixture-pin");
        using var events = new EventChannelClient(api, auth); using var socket = new ClientWebSocket();
        typeof(EventChannelClient).GetField("_ws", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(events, socket);
        typeof(EventChannelClient).GetField("_activeSocketContext", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(events, api.CaptureContext());
        var old = api.CaptureContext();
        await Frame(events, socket, "{\"type\":\"access_changed\"}");
        await Until(() => wire.Reads == 1 && auth.CurrentUser?.Role == "user");
        Assert.False(api.IsCurrentContext(old));
        Assert.Equal("profile", auth.SelectedProfileId);
        using var request = api.CreateAuthenticatedRequest(HttpMethod.Get, "/api/v2/catalog");
        Assert.Equal("fixture-pin", request.Headers.GetValues("X-Profile-Token").Single());
        await Frame(events, socket, "{\"type\":\"access_changed\"}");
        Assert.Equal(1, wire.Reads);
        typeof(EventChannelClient).GetMethod("HandleServerClose", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(events, [socket, 4001]);
        Assert.Equal(1, wire.Reads);
    }

    [Fact]
    public async Task DelayedAccountReadCannotReplaceANewLogin()
    {
        using var wire = new Wire { Delayed = true }; using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://access-fixture.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        auth.SetTokens("fixture-token", "fixture-refresh", 86400); auth.SetCurrentUser(new() { Id = "account", Role = "admin" });
        using var events = new EventChannelClient(api, auth); using var socket = new ClientWebSocket();
        typeof(EventChannelClient).GetField("_ws", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(events, socket);
        typeof(EventChannelClient).GetField("_activeSocketContext", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(events, api.CaptureContext());
        await Frame(events, socket, "{\"type\":\"access_changed\"}");
        await Until(() => wire.Reads == 1);
        auth.SetTokens("other-fixture", "other-refresh", 86400); auth.SetCurrentUser(new() { Id = "other", Role = "admin" });
        wire.Release.TrySetResult(); await Until(() => wire.Completed);
        await Task.Delay(25);
        Assert.Equal("other", auth.CurrentUser?.Id); Assert.Equal("admin", auth.CurrentUser?.Role);
    }

    private static Task Frame(EventChannelClient client, ClientWebSocket socket, string json)
        => (Task)typeof(EventChannelClient).GetMethod("DispatchFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(client, [socket, json, Array.Empty<string>(), CancellationToken.None])!;
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 100; i++) { if (ready()) return; await Task.Delay(5); } Assert.True(ready(), "The actual access_changed frame did not produce the required authoritative account read."); }
    private sealed class Wire : HttpMessageHandler
    {
        internal int Reads; internal bool Delayed, Completed;
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("https://access-fixture.invalid/api/v2/account/me", request.RequestUri!.AbsoluteUri);
            Interlocked.Increment(ref Reads);
            if (Delayed) await Release.Task;
            Completed = true;
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"account\",\"username\":\"Fixture\",\"role\":\"user\"}") };
        }
    }
}
