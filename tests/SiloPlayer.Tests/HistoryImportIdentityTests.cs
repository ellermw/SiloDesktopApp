using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.HistoryImport;

namespace SiloPlayer.Tests;

public sealed class HistoryImportIdentityTests
{
    [Theory]
    [InlineData("emby")]
    [InlineData("pin")]
    [InlineData("check")]
    public async Task AccessRefreshRetainsAuthenticationOwner(string route)
    {
        using var wire = new Handler(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid"); client.SetProfile("fixture-one");
        var api = new HistoryImportApi(client); var owner = api.CaptureIdentityContext(); var read = api.CaptureContext();
        client.InvalidateAccessContext();
        Assert.False(api.IsCurrentContext(read)); Assert.True(api.IsCurrentIdentityContext(owner));
        await Send(api, owner, route);
        Assert.Equal(1, wire.Calls);
    }

    [Theory]
    [InlineData("account", "emby")]
    [InlineData("account", "pin")]
    [InlineData("account", "check")]
    [InlineData("profile", "emby")]
    [InlineData("profile", "pin")]
    [InlineData("profile", "check")]
    [InlineData("server", "emby")]
    [InlineData("server", "pin")]
    [InlineData("server", "check")]
    public async Task ObsoleteOwnerCannotSendUnderReplacementIdentity(string change, string route)
    {
        using var wire = new Handler(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid"); client.SetProfile("fixture-one");
        var api = new HistoryImportApi(client); var owner = api.CaptureIdentityContext();
        if (change == "account") client.BeginAuthenticationSession("synthetic-account");
        else if (change == "profile") client.SetProfile("fixture-two");
        else client.SetBaseUrl("https://other-fixture.invalid");
        Assert.False(api.IsCurrentIdentityContext(owner));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Send(api, owner, route));
        Assert.Equal(0, wire.Calls);
    }

    private static Task Send(HistoryImportApi api, ApiIdentityContext owner, string route) => route switch
    {
        "emby" => api.EmbyConnectLoginAsync(owner, new EmbyConnectLoginRequest()),
        "pin" => api.PlexAuthPinAsync(owner),
        _ => api.PlexAuthCheckAsync(owner, new PlexCheckRequest { SessionId = "fixture-session" })
    };

    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
