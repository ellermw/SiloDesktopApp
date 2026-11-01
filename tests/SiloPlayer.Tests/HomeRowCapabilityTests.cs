using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public class HomeRowCapabilityTests
{
    [Theory]
    [InlineData("{}", "")]
    [InlineData("null", "")]
    [InlineData("{\"section_item_limit_max\":100}", "")]
    [InlineData("{\"row_item_limit_max\":19}", "")]
    [InlineData("{\"row_item_limit_max\":20}", "?item_limit=20")]
    [InlineData("{\"row_item_limit_max\":100,\"section_item_limit_max\":100}", "?item_limit=20")]
    public async Task FirstReadNegotiatesTheExactRowCapability(string capabilities, string expectedQuery)
    {
        using var wire = new Wire { Capabilities = capabilities }; using var http = new HttpClient(wire);
        var client = Client(http); var api = new HomeApi(client);
        var response = await api.GetInitialSectionItemsAsync("row/one");
        Assert.Equal(expectedQuery, Assert.Single(wire.Rows).Query);
        Assert.Contains("row%2Fone", wire.Rows[0].AbsoluteUri);
        Assert.Equal(expectedQuery.Length > 0, response.Section!.HasMore);
    }

    [Fact]
    public async Task CapabilityHttpTimeoutFallsBackToHealthyUncappedRow()
    {
        using var wire = new Wire { TimeoutCapabilities = true }; using var http = new HttpClient(wire) { Timeout = TimeSpan.FromMilliseconds(100) };
        var response = await new HomeApi(Client(http)).GetInitialSectionItemsAsync("row");
        Assert.Equal("", Assert.Single(wire.Rows).Query); Assert.False(response.Section!.HasMore);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullSectionKeepsTheNullableWrapperForMountedRowFallback(bool initial)
    {
        using var wire = new Wire { Section = "null" }; using var http = new HttpClient(wire);
        var api = new HomeApi(Client(http));
        var result = initial ? await api.GetInitialSectionItemsAsync("row") : await api.GetSectionItemsAsync("row");
        Assert.NotNull(result); Assert.Null(result.Section);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("malformed")]
    [InlineData("null")]
    public async Task OptionalFailureIsSharedAcrossRowsAndRetriedOnLaterLayout(string failure)
    {
        using var wire = new Wire { TimeoutCapabilities = failure == "timeout", Capabilities = failure == "malformed" ? "{" : "null" };
        using var http = new HttpClient(wire) { Timeout = TimeSpan.FromMilliseconds(100) };
        var api = new HomeApi(Client(http));
        await api.GetLayoutAsync();
        await Task.WhenAll(Enumerable.Range(1, 8).Select(i => api.GetInitialSectionItemsAsync("row" + i)));
        // Include rows queued behind Home's five-request cap, after discovery finished.
        await api.GetInitialSectionItemsAsync("queued-row");
        Assert.Equal(1, wire.CapabilityReads);
        Assert.Equal(9, wire.Rows.Count); Assert.All(wire.Rows, row => Assert.Equal("", row.Query));
        wire.TimeoutCapabilities = false; wire.Capabilities = "{\"row_item_limit_max\":100}";
        await api.GetLayoutAsync();
        await api.GetInitialSectionItemsAsync("refreshed");
        Assert.Equal(2, wire.CapabilityReads); Assert.Equal("?item_limit=20", wire.Rows.Last().Query);
    }

    [Fact]
    public async Task CallerCancellationDoesNotBecomeAnUncappedFallback()
    {
        using var wire = new Wire { TimeoutCapabilities = true }; using var http = new HttpClient(wire) { Timeout = TimeSpan.FromSeconds(2) };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new HomeApi(Client(http)).GetInitialSectionItemsAsync("row", cancellation.Token));
        Assert.Empty(wire.Rows);
    }

    [Fact]
    public async Task OldServerCapabilityFailureFallsBackToWholeRead()
    {
        using var wire = new Wire { Unsupported = true }; using var http = new HttpClient(wire);
        var response = await new HomeApi(Client(http)).GetInitialSectionItemsAsync("row");
        Assert.Equal("", Assert.Single(wire.Rows).Query); Assert.False(response.Section!.HasMore);
    }

    [Fact]
    public async Task WholeReadBypassesCapAndCapabilityDiscoveryForHeroAndCompletion()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        await new HomeApi(Client(http)).GetSectionItemsAsync("row");
        Assert.Equal(0, wire.CapabilityReads); Assert.Equal("", Assert.Single(wire.Rows).Query);
    }

    [Fact]
    public async Task CapabilityCacheIsRetiredWithProfileAuthority()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http); var api = new HomeApi(client);
        await api.GetInitialSectionItemsAsync("one"); await api.GetInitialSectionItemsAsync("two");
        Assert.Equal(1, wire.CapabilityReads);
        client.SetProfile("beta"); wire.Capabilities = "{}";
        await api.GetInitialSectionItemsAsync("three");
        Assert.Equal(2, wire.CapabilityReads); Assert.Equal("", wire.Rows.Last().Query);
    }

    [Fact]
    public async Task RetiredCapabilityReadCannotStartARequestForTheNewProfile()
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire); var client = Client(http); var api = new HomeApi(client);
        var pending = api.GetInitialSectionItemsAsync("row"); await wire.Started.Task;
        client.SetProfile("beta"); wire.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.Empty(wire.Rows);
    }

    [Fact]
    public async Task RetiredLanguageCannotStartTheSubsequentRowRead()
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire); var client = Client(http); var api = new HomeApi(client);
        var pending = api.GetInitialSectionItemsAsync("row"); await wire.Started.Task;
        client.LocalizedReads.NotifySettingChanged("catalog.metadata_language"); wire.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.Empty(wire.Rows);
    }

    private static SiloApiClient Client(HttpClient http) { var client = new SiloApiClient(http); client.SetBaseUrl("https://home-window.test"); client.SetProfile("alpha"); return client; }
    private sealed class Wire : HttpMessageHandler
    {
        public string Capabilities = "{\"row_item_limit_max\":100,\"section_item_limit_max\":100}";
        public string Section = """{"id":"row","has_more":true,"items":[]}""";
        public bool Unsupported, Hold, TimeoutCapabilities; public int CapabilityReads; public List<Uri> Rows = [];
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/layout"))
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"sections":[]}""") };
            if (request.RequestUri!.AbsolutePath.EndsWith("/capabilities"))
            {
                CapabilityReads++; Started.TrySetResult(); if (Hold) await Release.Task;
                if (TimeoutCapabilities) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new(Unsupported ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new StringContent(Unsupported ? "{}" : Capabilities) };
            }
            lock (Rows) Rows.Add(request.RequestUri);
            return new(HttpStatusCode.OK) { Content = new StringContent(Section) };
        }
    }
}
