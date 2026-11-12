using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class MetadataResetApiTests
{
    [Fact]
    public async Task GenericRefreshResetUsesCapabilityGatedAtomicOperation()
    {
        using var wire = new Wire(); var client = Client(wire);
        await new MediaMaintenanceApi(client).RefreshMetadataAsync("movie", "reset");
        Assert.Equal("reset", Assert.Single(wire.Modes));
        Assert.DoesNotContain(wire.Requests, r => r.StartsWith("PATCH"));
    }

    [Fact]
    public async Task MissingCapabilityEndpointUsesExplicitLegacySemantics()
    {
        using var wire = new Wire { CapabilityFailure = HttpStatusCode.NotFound }; var client = Client(wire);
        await new MediaMaintenanceApi(client).ResetToProviderAsync("movie");
        Assert.Equal("quick", Assert.Single(wire.Modes));
        Assert.Contains(wire.Requests, r => r.StartsWith("PATCH"));
    }

    [Fact]
    public async Task SupportedResetIsOneAtomicWriteAndClearsTargetLocksAndProviderEmptyValues()
    {
        using var wire = new Wire(); var client = Client(wire);
        var receipt = await new MediaMaintenanceApi(client).ResetToProviderAsync("movie/1", new[] { 2, 10, 99 });
        Assert.Equal("queued", receipt.Status);
        Assert.Equal(new[] { "GET /api/v2/admin/catalog/capabilities", "POST /api/v2/admin/items/movie%2F1/refresh-metadata" }, wire.Requests);
        Assert.Equal("reset", wire.Modes.Single());
        Assert.Empty(wire.TargetLocks); Assert.Null(wire.ProviderEmptyValue);
        Assert.Equal(new[] { 0, 10 }, wire.DescendantLocks);
    }

    [Fact]
    public async Task UnsupportedServerUsesOnlyLegacyUnlockAndQuickRefresh()
    {
        using var wire = new Wire { SupportsReset = false }; var client = Client(wire);
        await new MediaMaintenanceApi(client).ResetToProviderAsync("child", new[] { 2, 10, 99 });
        Assert.Equal(new[] { "GET /api/v2/admin/catalog/capabilities", "PATCH /api/v2/admin/items/child/metadata", "POST /api/v2/admin/items/child/refresh-metadata" }, wire.Requests);
        Assert.Equal("quick", wire.Modes.Single()); Assert.Equal(new[] { 2, 10, 99 }, wire.TargetLocks);
        Assert.Equal("Stale value", wire.ProviderEmptyValue);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task CapabilityFailureNeverUnlocksOrRefreshes(HttpStatusCode status)
    {
        using var wire = new Wire { CapabilityFailure = status }; var client = Client(wire);
        await Assert.ThrowsAsync<ApiException>(() => new MediaMaintenanceApi(client).ResetToProviderAsync("movie"));
        Assert.Equal(new[] { "GET /api/v2/admin/catalog/capabilities" }, wire.Requests);
        Assert.Equal(new[] { 0, 10 }, wire.TargetLocks);
    }

    [Fact]
    public async Task ProviderFailureLeavesExistingLocksAndValuesUntouched()
    {
        using var wire = new Wire { ProviderFails = true }; var client = Client(wire);
        await Assert.ThrowsAsync<ApiException>(() => new MediaMaintenanceApi(client).ResetToProviderAsync("movie"));
        Assert.DoesNotContain(wire.Requests, r => r.StartsWith("PATCH"));
        Assert.Equal(new[] { 0, 10 }, wire.TargetLocks); Assert.Equal("Stale value", wire.ProviderEmptyValue);
    }

    [Theory]
    [InlineData("capability")]
    [InlineData("unlock")]
    [InlineData("refresh")]
    public async Task RetiredAuthorityCannotContinueOrPublishReceipt(string boundary)
    {
        using var wire = new Wire { SupportsReset = boundary != "unlock" }; var client = Client(wire);
        wire.Completed = stage => { if (stage == boundary) client.SetProfile("replacement", "fixture-new-proof"); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MediaMaintenanceApi(client).ResetToProviderAsync("movie"));
        Assert.Equal(boundary == "capability" ? 1 : boundary == "unlock" ? 2 : 2, wire.Requests.Count);
    }

    [Fact]
    public async Task CancellationAfterCapabilityReadCannotDispatchWrite()
    {
        using var wire = new Wire(); var client = Client(wire); using var stop = new CancellationTokenSource();
        wire.Completed = stage => { if (stage == "capability") stop.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MediaMaintenanceApi(client).ResetToProviderAsync("movie", stop.Token));
        Assert.Single(wire.Requests);
    }

    [Fact]
    public async Task CancellationBeforeResetSendsNothing()
    {
        using var wire = new Wire(); var client = Client(wire); using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MediaMaintenanceApi(client).ResetToProviderAsync("movie", stop.Token));
        Assert.Empty(wire.Requests);
    }

    private static SiloApiClient Client(Wire wire)
    {
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://metadata-reset.fixture.invalid");
        client.SetAccessToken("fixture-only"); client.SetProfile("original", "fixture-proof"); return client;
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool SupportsReset = true, ProviderFails;
        internal HttpStatusCode? CapabilityFailure;
        internal Action<string>? Completed;
        internal List<string> Requests = [], Modes = [];
        internal int[] TargetLocks = [0, 10], DescendantLocks = [0, 10];
        internal string? ProviderEmptyValue = "Stale value";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get)
            {
                Completed?.Invoke("capability");
                return Json(CapabilityFailure ?? HttpStatusCode.OK, CapabilityFailure.HasValue ? """{"detail":"Capability refused"}""" : JsonSerializer.Serialize(new { item_metadata_reset = SupportsReset }));
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (request.Method == HttpMethod.Patch)
            {
                TargetLocks = body.RootElement.GetProperty("locked_fields").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                Completed?.Invoke("unlock"); return Json(HttpStatusCode.OK, "{}");
            }
            var mode = body.RootElement.GetProperty("mode").GetString()!; Modes.Add(mode);
            if (ProviderFails) return Json(HttpStatusCode.BadGateway, """{"detail":"Provider unavailable"}""");
            if (mode == "reset") { TargetLocks = []; ProviderEmptyValue = null; }
            Completed?.Invoke("refresh");
            return Json(HttpStatusCode.Accepted, """{"id":"fixture-job","state":"queued"}""");
        }
        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
    }
}
