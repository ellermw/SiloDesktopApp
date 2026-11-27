using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.HistoryImport;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Official #5382: the third Emby mode, Server URL (direct sign-in with emby_base_url).</summary>
public sealed class HistoryImportEmbyServerUrlTests
{
    private sealed class Handler(string capabilityBody, HttpStatusCode capabilityStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? RunBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/history-imports/capability") return new(capabilityStatus) { Content = new StringContent(capabilityBody, Encoding.UTF8, "application/json") };
            if (path == "/api/v2/history-imports/runs" && request.Method == HttpMethod.Post)
            {
                RunBody = await request.Content!.ReadAsStringAsync(ct);
                return new(HttpStatusCode.Accepted) { Content = new StringContent("""{"id":"run-1","profile_id":"p1","source_type":"emby","status":"queued"}""", Encoding.UTF8, "application/json") };
            }
            return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }
    }

    private static HistoryImportApi Api(Handler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.example.test");
        client.SetAccessToken("fixture-only");
        return new HistoryImportApi(client);
    }

    [Fact]
    public async Task TheCapabilityReadsEmbyCustomServer()
    {
        var capability = await Api(new Handler("""{"state":"available","allowed":true,"revision":3,"plex_connection_fallback":true,"max_plex_connections":8,"emby_custom_server":true}""")).GetCapabilityAsync();
        Assert.True(HistoryImportRules.SupportsEmbyDirect(capability));
        Assert.True(capability.PlexConnectionFallback);
        Assert.Equal(8, capability.MaxPlexConnections);
    }

    [Fact]
    public async Task AnOlderServerWithoutTheMemberDoesNotOfferServerUrl()
    {
        var capability = await Api(new Handler("""{"state":"available","allowed":true,"revision":2,"plex_connection_fallback":true,"max_plex_connections":8}""")).GetCapabilityAsync();
        Assert.False(capability.EmbyCustomServer);
        Assert.False(HistoryImportRules.SupportsEmbyDirect(capability));
    }

    [Theory]
    [InlineData("unavailable", true)]
    [InlineData("", true)]
    public void ACapabilityThatIsNotAvailableDoesNotOfferServerUrl(string state, bool custom) =>
        Assert.False(HistoryImportRules.SupportsEmbyDirect(new HistoryImportCapability { State = state, EmbyCustomServer = custom }));

    [Fact]
    public void NoCapabilityDoesNotOfferServerUrl()
    {
        Assert.False(HistoryImportRules.SupportsEmbyDirect(null));
        Assert.Equal(["connect", "saved"], HistoryImportRules.EmbyModes(false));
        Assert.Equal(["connect", "direct", "saved"], HistoryImportRules.EmbyModes(true));
    }

    [Fact]
    public void ServerUrlFallsBackToConnectWhenSupportDisappears()
    {
        Assert.Equal("direct", HistoryImportRules.EffectiveEmbyMode("direct", true));
        Assert.Equal("connect", HistoryImportRules.EffectiveEmbyMode("direct", false));
        Assert.Equal("saved", HistoryImportRules.EffectiveEmbyMode("saved", false));
        Assert.Equal("connect", HistoryImportRules.EffectiveEmbyMode("connect", false));
    }

    [Theory]
    [InlineData("https://emby.example.com", "alice", true)]
    [InlineData("  https://emby.example.com  ", "  alice ", true)]
    [InlineData("", "alice", false)]
    [InlineData("https://emby.example.com", "", false)]
    [InlineData("   ", "alice", false)]
    [InlineData("https://emby.example.com", "   ", false)]
    public void StartNeedsAServerUrlAndAUsernameButNotAPassword(string url, string username, bool expected) =>
        Assert.Equal(expected, HistoryImportRules.CanStartEmbyDirect(url, username));

    [Theory]
    [InlineData("https://emby.example.com", true)]
    [InlineData("http://192.168.1.5:8096", true)]
    [InlineData("  https://emby.example.com/emby  ", true)]
    [InlineData("HTTPS://emby.example.com", true)]
    [InlineData("emby.example.com", false)]
    [InlineData("ftp://emby.example.com", false)]
    [InlineData("https://", false)]
    [InlineData("https://user:pass@emby.example.com", false)]
    [InlineData("https://user@emby.example.com", false)]
    [InlineData("https://emby.example.com/?x=1", false)]
    [InlineData("https://emby.example.com/#frag", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ServerAddressesAreCheckedLikeTheWebUi(string? raw, bool valid) =>
        Assert.Equal(valid, HistoryImportRules.IsValidHttpServerUrl(raw));

    [Fact]
    public void AStartErrorBelongsToTheSourceAndItsMode()
    {
        Assert.Equal("emby:direct", HistoryImportRules.FormScope("emby", "direct", "oauth"));
        Assert.Equal("emby:connect", HistoryImportRules.FormScope("emby", "connect", "saved"));
        Assert.Equal("plex:saved", HistoryImportRules.FormScope("plex", "direct", "saved"));
        Assert.Equal("jellyfin", HistoryImportRules.FormScope("jellyfin", "direct", "saved"));
    }

    [Fact]
    public async Task ADirectRunSendsTheServerUrlAndCredentialsAndNothingForConnectOrSavedSources()
    {
        var handler = new Handler("{}");
        await Api(handler).CreateImportRunAsync(new CreateHistoryImportRunRequest
        {
            ProfileId = "p1", Source = "emby", EmbyBaseUrl = "https://emby.example.com", Username = "alice", Password = "",
        });
        using var body = JsonDocument.Parse(handler.RunBody!);
        var root = body.RootElement;
        Assert.Equal("emby", root.GetProperty("source").GetString());
        Assert.Equal("https://emby.example.com", root.GetProperty("emby_base_url").GetString());
        Assert.Equal("alice", root.GetProperty("username").GetString());
        Assert.Equal("", root.GetProperty("password").GetString());
        Assert.False(root.TryGetProperty("connect_session_id", out _));
        Assert.False(root.TryGetProperty("source_id", out _));
        Assert.False(root.TryGetProperty("server_id", out _));
    }

    [Fact]
    public async Task AConnectRunNeverSendsAnEmbyBaseUrl()
    {
        var handler = new Handler("{}");
        await Api(handler).CreateImportRunAsync(new CreateHistoryImportRunRequest { ProfileId = "p1", Source = "emby", ConnectSessionId = "s1", ServerId = "srv" });
        using var body = JsonDocument.Parse(handler.RunBody!);
        Assert.False(body.RootElement.TryGetProperty("emby_base_url", out _));
        Assert.Equal("s1", body.RootElement.GetProperty("connect_session_id").GetString());
    }
}
