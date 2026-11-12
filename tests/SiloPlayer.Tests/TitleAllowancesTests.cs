using System.Net;
using System.Reflection;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class TitleAllowancesTests
{
    [Fact]
    public async Task TitleAllowanceReadUsesEscapedItemPathAndActingPrimaryProof()
    {
        using var wire = new Wire();
        var client = new SiloApiClient(new HttpClient(wire));
        client.SetBaseUrl("https://allowances.fixture.invalid");
        client.SetProfile("primary", "synthetic-proof");
        var type = typeof(SiloApiClient).Assembly.GetType("SiloPlayer.Core.Api.TitleAllowancesApi");
        Assert.NotNull(type);
        var api = Activator.CreateInstance(type!, client)!;
        var task = (Task)type!.GetMethod("GetTitleProfilesAsync")!.Invoke(api, [client.CaptureContext(), "movie:1/2", CancellationToken.None])!;
        await task;
        Assert.Equal("/api/v2/catalog/items/movie%3A1%2F2/title-allowances", wire.Path);
        Assert.Equal("primary", wire.Profile);
        Assert.Equal("synthetic-proof", wire.Proof);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal string Path = "", Profile = "", Proof = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.PathAndQuery;
            Profile = request.Headers.GetValues("X-Profile-Id").Single();
            Proof = request.Headers.GetValues("X-Profile-Token").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") });
        }
    }
}
