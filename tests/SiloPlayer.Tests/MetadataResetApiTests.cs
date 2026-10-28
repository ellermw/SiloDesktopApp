using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class MetadataResetApiTests
{
    [Fact]
    public async Task UnlocksBeforeRefreshingWithoutReplacingOtherMetadata()
    {
        using var wire = new Wire(); var client = Client(wire);
        var receipt = await new MediaMaintenanceApi(client).ResetToProviderAsync("movie/1");
        Assert.Equal("queued", receipt.Status);
        Assert.Equal(new[] { "PATCH", "POST" }, wire.Methods);
        Assert.Equal(new[] { "/api/v2/admin/items/movie%2F1/metadata", "/api/v2/admin/items/movie%2F1/refresh-metadata" }, wire.Paths);
        using var unlock = JsonDocument.Parse(wire.Bodies[0]);
        Assert.Single(unlock.RootElement.EnumerateObject());
        Assert.Empty(unlock.RootElement.GetProperty("locked_fields").EnumerateArray());
        using var refresh = JsonDocument.Parse(wire.Bodies[1]);
        Assert.Equal("quick", refresh.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task FailedUnlockCannotQueueARefresh()
    {
        using var wire = new Wire { RefuseUnlock = true }; var client = Client(wire);
        await Assert.ThrowsAsync<ApiException>(() => new MediaMaintenanceApi(client).ResetToProviderAsync("movie"));
        Assert.Equal(new[] { "PATCH" }, wire.Methods);
    }

    [Fact]
    public async Task ReplacedProfileCannotFinishResetUnderANewIdentity()
    {
        using var wire = new Wire(); var client = Client(wire);
        wire.Unlocked = () => client.SetProfile("replacement", "fixture-new-proof");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MediaMaintenanceApi(client).ResetToProviderAsync("movie"));
        Assert.Equal(new[] { "PATCH" }, wire.Methods);
    }

    [Fact]
    public async Task CancellationBeforeResetSendsNoWrite()
    {
        using var wire = new Wire(); var client = Client(wire);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MediaMaintenanceApi(client).ResetToProviderAsync("movie", stop.Token));
        Assert.Empty(wire.Methods);
    }

    private static SiloApiClient Client(Wire wire)
    {
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://metadata-reset.fixture.invalid");
        client.SetAccessToken("fixture-only"); client.SetProfile("original", "fixture-proof"); return client;
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool RefuseUnlock;
        internal Action? Unlocked;
        internal List<string> Methods = [], Paths = [], Bodies = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Methods.Add(request.Method.Method); Paths.Add(request.RequestUri!.AbsolutePath); Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            var unlock = request.Method == HttpMethod.Patch;
            if (unlock && RefuseUnlock) return new(HttpStatusCode.Forbidden) { Content = new StringContent("""{"type":"https://fixture.invalid/forbidden","detail":"Fixture refused"}""") };
            if (unlock) Unlocked?.Invoke();
            return new(unlock ? HttpStatusCode.OK : HttpStatusCode.Accepted) { Content = new StringContent(unlock ? "{}" : """{"id":"fixture-job","state":"queued"}""") };
        }
    }
}
