using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class HomeUndoAuthorityTests
{
    [Fact]
    public async Task UndoUsesCapturedOwnerAndBodylessEscapedDelete()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://undo.invalid");
        client.SetProfile("primary", "proof");
        await new HomeApi(client).UndoDismissalAsync(client.CaptureContext(), "next_up", "title/a b");
        Assert.Equal(HttpMethod.Delete, wire.Method);
        Assert.Equal("/api/v2/home/dismissals/next_up/title%2Fa%20b", wire.Path);
        Assert.Null(wire.Body);
        Assert.Equal("primary", wire.Profile);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("access")]
    [InlineData("server")]
    public async Task RetiredOwnerCannotDeleteUnderReplacementAuthority(string change)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://undo.invalid"); client.SetProfile("primary", "proof");
        var owner = client.CaptureContext();
        if (change == "profile") client.SetProfile("replacement");
        if (change == "access") client.InvalidateAccessContext();
        if (change == "server") client.SetBaseUrl("https://replacement.invalid");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new HomeApi(client).UndoDismissalAsync(owner, "continue_watching", "film"));
        Assert.Null(wire.Method);
    }

    [Fact]
    public async Task DismissalUsesCapturedOwnerAndRefusesAReplacement()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://undo.invalid"); client.SetProfile("primary", "proof");
        var home = new HomeApi(client);
        var owner = client.CaptureContext();
        await home.DismissItemAsync(owner, "next_up", "title/a b", new Dictionary<string, object?> { ["series_id"] = "show" });
        Assert.Equal(HttpMethod.Put, wire.Method);
        Assert.Equal("/api/v2/home/dismissals/next_up/title%2Fa%20b", wire.Path);
        Assert.Equal("""{"series_id":"show"}""", wire.Body);
        Assert.Equal("primary", wire.Profile);
        wire.Method = null;
        client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            home.DismissItemAsync(owner, "next_up", "title/a b", new Dictionary<string, object?> { ["series_id"] = "show" }));
        Assert.Null(wire.Method);
    }

    [Fact]
    public void ReconnectCatchUpKeepsTheIdentityAndAccessAuthorityOfARetiredContext()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://undo.invalid"); client.SetProfile("primary", "proof");
        var owner = client.CaptureContext();
        var identity = client.CaptureIdentityContext();
        Assert.True(client.TryInvalidateAccessContext(owner, out var reconnected, transportCatchUp: true));
        // The dedicated page and the card menu follow a dismissal's owner across this change.
        Assert.False(client.IsCurrentContext(owner));
        Assert.True(client.IsCurrentIdentityContext(identity));
        Assert.Equal(owner.AccessGeneration, reconnected.AccessGeneration);
        // A permission change keeps the identity but not the access authority.
        Assert.NotEqual(owner.AccessGeneration, client.InvalidateAccessContext().AccessGeneration);
        Assert.True(client.IsCurrentIdentityContext(identity));
        client.SetProfile("replacement");
        Assert.False(client.IsCurrentIdentityContext(identity));
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal HttpMethod? Method;
        internal string? Path, Body, Profile;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Method = request.Method; Path = request.RequestUri!.AbsolutePath;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            Profile = request.Headers.TryGetValues("X-Profile-Id", out var profiles) ? profiles.Single() : null;
            return new(HttpStatusCode.NoContent);
        }
    }
}
