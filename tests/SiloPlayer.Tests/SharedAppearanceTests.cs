using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SharedAppearanceTests
{
    [Fact]
    public async Task AmbientGlowUsesItsOwnSupportedPublicToken()
    {
        using var f = new Fixture();
        f.Body = Payload(new() { ["primary"] = "#4f46e5", ["ambient"] = "#78aefc" });
        await f.State.RefreshAsync();
        Assert.Equal("#78AEFC", f.State.Colors["ambient"]);
        Assert.Equal("#4F46E5", f.State.Colors["primary"]);
        Assert.False(f.HadAuthorization);
    }

    [Fact]
    public async Task PublicSharedTokensReplaceProfileThemesWithoutAuthenticationOrWrites()
    {
        using var f = new Fixture();
        f.Body = Payload(new() { ["primary"] = "#4f46e5", ["ring"] = "#abc", ["background"] = "#12345678", ["font-body"] = "evil", ["unknown"] = "#ffffff", ["foreground"] = "url(https://example.invalid)" });
        await f.State.RefreshAsync();
        Assert.Equal("#4F46E5", f.State.Colors["primary"]);
        Assert.Equal("#AABBCC", f.State.Colors["ring"]);
        Assert.Equal("#78123456", f.State.Colors["background"]);
        Assert.Equal(3, f.State.Colors.Count);
        Assert.Equal(new[] { "GET /api/v2/theme/admin-css" }, f.Requests);
        Assert.False(f.HadAuthorization);
    }

    [Fact]
    public async Task RefreshFailureAndServerResetRemoveOldOverrides()
    {
        using var f = new Fixture();
        f.Body = Payload(new() { ["primary"] = "#112233" });
        await f.State.RefreshAsync();
        Assert.Single(f.State.Colors);
        f.Body = Payload(new());
        await f.State.RefreshAsync();
        Assert.Empty(f.State.Colors);
        f.Body = Payload(new() { ["primary"] = "#445566" });
        await f.State.RefreshAsync();
        f.Fail = true;
        await f.State.RefreshAsync();
        Assert.Empty(f.State.Colors);
    }

    [Fact]
    public async Task OlderRefreshCannotOverwriteNewerServerOrProfile()
    {
        using var f = new Fixture();
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        f.Next = _ => gate.Task;
        var old = f.State.RefreshAsync();
        f.Client.SetBaseUrl("https://second.invalid");
        f.Next = null;
        f.Body = Payload(new() { ["primary"] = "#AABBCC" });
        await f.State.RefreshAsync();
        gate.SetResult(Response(Payload(new() { ["primary"] = "#112233" })));
        await old;
        Assert.Equal("#AABBCC", f.State.Colors["primary"]);
        gate = new TaskCompletionSource<HttpResponseMessage>();
        f.Next = _ => gate.Task;
        var profileRead = f.State.RefreshAsync();
        f.Client.SetProfile("other-profile");
        gate.SetResult(Response(Payload(new() { ["primary"] = "#445566" })));
        await profileRead;
        Assert.Empty(f.State.Colors);
    }

    [Fact]
    public async Task ResetInvalidatesInflightReadAndCancellationDoesNotRestoreOverrides()
    {
        using var f = new Fixture();
        f.Body = Payload(new() { ["primary"] = "#112233" });
        await f.State.RefreshAsync();
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        f.Next = _ => gate.Task;
        var pending = f.State.RefreshAsync();
        f.State.Reset();
        gate.SetResult(Response(Payload(new() { ["primary"] = "#445566" })));
        await pending;
        Assert.Empty(f.State.Colors);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.State.RefreshAsync(cancelled.Token));
        Assert.Empty(f.State.Colors);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"primary\":null,\"ring\":42,\"background\":\"#ffffff; color:red\"}")]
    public async Task InvalidTokensAndRawCssNeverBecomeNativeColors(string vars)
    {
        using var f = new Fixture();
        f.Body = JsonSerializer.Serialize(new { vars, raw_css = "body { color: #123456; } @import url(https://example.invalid)" });
        await f.State.RefreshAsync();
        Assert.Empty(f.State.Colors);
    }

    [Fact]
    public async Task SameContextRefreshAppliesOnceWithoutAnIntermediateBaseTheme()
    {
        using var f = new Fixture();
        f.Body = Payload(new() { ["primary"] = "#112233" });
        await f.State.RefreshAsync();
        var snapshots = new List<string>();
        f.State.Changed += () => snapshots.Add(f.State.Colors.GetValueOrDefault("primary", "BASE"));
        f.Body = Payload(new() { ["primary"] = "#445566" });
        await f.State.RefreshAsync();
        Assert.Equal(new[] { "#445566" }, snapshots);
    }
    [Fact]
    public async Task FocusRefreshUsesFreshResultButContextChangesBypassFreshness()
    {
        using var f = new Fixture();
        f.Body = Payload(new() { ["primary"] = "#112233" });
        await f.State.RefreshIfStaleAsync();
        f.Body = Payload(new() { ["primary"] = "#445566" });
        await f.State.RefreshIfStaleAsync();
        Assert.Single(f.Requests);
        Assert.Equal("#112233", f.State.Colors["primary"]);
        f.Client.SetProfile("new-profile");
        await f.State.RefreshIfStaleAsync();
        Assert.Equal(2, f.Requests.Count);
        Assert.Equal("#445566", f.State.Colors["primary"]);
    }
    private static string Payload(Dictionary<string, string> vars) => JsonSerializer.Serialize(new { vars = JsonSerializer.Serialize(vars), raw_css = "" });
    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly HttpClient _http;
        public SiloApiClient Client { get; }
        public SharedAppearanceState State { get; }
        public string Body = Payload(new());
        public bool Fail, HadAuthorization;
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Next;
        public List<string> Requests { get; } = [];
        public Fixture()
        {
            _http = new HttpClient(this);
            Client = new SiloApiClient(_http);
            Client.SetBaseUrl("https://first.invalid");
            State = new SharedAppearanceState(new SettingsApi(Client));
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            HadAuthorization |= request.Headers.Authorization != null;
            return Next?.Invoke(request) ?? Task.FromResult(Fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Response(Body));
        }
    }
}
