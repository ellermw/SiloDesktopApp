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
