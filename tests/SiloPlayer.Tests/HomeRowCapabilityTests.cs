using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

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
        Assert.Equal(expectedQuery.Length == 0, response.Section.HasReadWhole);
        Assert.Equal(expectedQuery.Length > 0, response.Section.CanComplete);
    }

    [Fact]
    public async Task CapabilityHttpTimeoutFallsBackToHealthyUncappedRow()
    {
        using var wire = new Wire { TimeoutCapabilities = true }; using var http = new HttpClient(wire) { Timeout = TimeSpan.FromMilliseconds(100) };
        var response = await new HomeApi(Client(http)).GetInitialSectionItemsAsync("row");
        Assert.Equal("", Assert.Single(wire.Rows).Query); Assert.False(response.Section!.HasMore);
    }

    [Fact]
    public async Task HungOptionalProbeHasItsOwnDeadlineAndReleasesAllQueuedRows()
    {
        using var wire = new Wire { TimeoutCapabilities = true };
        using var http = new HttpClient(wire); // Production's default 100-second timeout.
        var api = new HomeApi(Client(http));
        await api.GetLayoutAsync();
        var rows = Enumerable.Range(1, 8).Select(i => api.GetInitialSectionItemsAsync("row" + i)).ToArray();
        await Task.WhenAll(rows).WaitAsync(TimeSpan.FromSeconds(10));
        await api.GetInitialSectionItemsAsync("queued-row");
        Assert.Equal(1, wire.CapabilityReads);
        Assert.Equal(9, wire.Rows.Count);
        Assert.All(wire.Rows, row => Assert.Equal("", row.Query));
        Assert.All(rows, row => Assert.True(row.Result.Section!.HasReadWhole));

        wire.TimeoutCapabilities = false;
        await api.GetLayoutAsync();
        await api.GetInitialSectionItemsAsync("refreshed");
        Assert.Equal(2, wire.CapabilityReads);
        Assert.Equal("?item_limit=20", wire.Rows.Last().Query);
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
    public async Task DefaultWholeReadRemainsUncappedAndPreservesServerTruncation()
    {
        using var wire = new Wire { Section = """{"id":"row","has_more":true,"items":[]}""" }; using var http = new HttpClient(wire);
        var response = await new HomeApi(Client(http)).GetSectionItemsAsync("row");
        Assert.Equal(0, wire.CapabilityReads); Assert.Equal("", Assert.Single(wire.Rows).Query);
        Assert.True(response.Section!.HasMore);
        Assert.False(response.Section.HasReadWhole); Assert.True(response.Section.CanComplete);
    }

    [Theory]
    [InlineData("{\"row_item_limit_max\":100,\"section_item_limit_max\":100}", 50, "?item_limit=50")]
    [InlineData("{\"row_item_limit_max\":100,\"section_item_limit_max\":100}", 150, "?item_limit=100")]
    [InlineData("{\"row_item_limit_max\":40,\"section_item_limit_max\":100}", 50, "")]
    [InlineData("{\"row_item_limit_max\":40,\"section_item_limit_max\":100}", 30, "?item_limit=30")]
    [InlineData("{\"row_item_limit_max\":100,\"section_item_limit_max\":60}", 80, "?item_limit=60")]
    [InlineData("{\"row_item_limit_max\":100,\"section_item_limit_max\":100}", 0, "?item_limit=100")]
    [InlineData("{\"row_item_limit_max\":100}", 50, "")]
    [InlineData("{\"section_item_limit_max\":100}", 50, "")]
    [InlineData("{\"row_item_limit_max\":0,\"section_item_limit_max\":100}", 50, "")]
    [InlineData("null", 50, "")]
    public async Task HomeWholeReadUsesAdvertisedMaximaClampedToTheConfiguredRow(string capabilities, int itemLimit, string expectedQuery)
    {
        using var wire = new Wire { Capabilities = capabilities }; using var http = new HttpClient(wire);
        var api = new HomeApi(Client(http));
        var response = await api.GetWholeSectionItemsAsync(api.CaptureContext(), api.LanguageGeneration, "row/one", itemLimit);
        Assert.Equal(1, wire.CapabilityReads);
        var request = Assert.Single(wire.Rows);
        Assert.Equal(expectedQuery, request.Query);
        Assert.Contains("row%2Fone", request.AbsoluteUri);
        Assert.True(response.Section!.HasReadWhole);
        Assert.False(response.Section.CanComplete);
    }

    [Fact]
    public async Task TruncatedHomeWholeResponseCanBeRetrievedAgainWithoutLosingInitialPriority()
    {
        static string Section(int count, int limit, bool hasMore) => JsonSerializer.Serialize(new
        {
            id = "row", item_limit = limit, has_more = hasMore,
            items = Enumerable.Range(1, count).Select(i => new { content_id = $"card{i}" })
        });
        using var wire = new Wire { Section = Section(20, 20, true) }; using var http = new HttpClient(wire);
        var api = new HomeApi(Client(http));
        var initial = (await api.GetInitialSectionItemsAsync("row")).Section!;
        wire.Section = Section(25, 50, true);
        var partial = (await api.GetWholeSectionItemsAsync(api.CaptureContext(), api.LanguageGeneration, "row", 50)).Section!;
        Assert.False(partial.HasReadWhole);
        Assert.True(partial.CanComplete);
        var expanded = HomeRowWindow.Complete(initial, partial, initial, 1);
        Assert.Equal(25, expanded.Items.Count);
        Assert.True(expanded.CanComplete);

        wire.Section = Section(35, 50, false);
        var whole = (await api.GetWholeSectionItemsAsync(api.CaptureContext(), api.LanguageGeneration, "row", 50)).Section!;
        var completed = HomeRowWindow.Complete(expanded, whole, expanded, 2);
        Assert.Equal(new[] { "?item_limit=20", "?item_limit=50", "?item_limit=50" }, wire.Rows.Select(row => row.Query));
        Assert.Equal(Enumerable.Range(1, 35).Select(i => $"card{i}"), completed.Items.Select(item => item.ContentId));
        Assert.True(completed.HasReadWhole);
        Assert.False(completed.CanComplete);
        Assert.Equal(20, completed.ShownCount);
        Assert.Equal(1, completed.CompletedOrder);
    }

    [Fact]
    public async Task UnadvertisedInitialReadDoesNotDiscardServerTruncation()
    {
        using var wire = new Wire { Unsupported = true, Section = """{"id":"row","has_more":true,"items":[]}""" };
        using var http = new HttpClient(wire);
        var response = await new HomeApi(Client(http)).GetInitialSectionItemsAsync("row");
        Assert.Equal("", Assert.Single(wire.Rows).Query);
        Assert.True(response.Section!.HasMore);
        Assert.False(response.Section.HasReadWhole);
        Assert.True(response.Section.CanComplete);
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
        public string? Section;
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
            return new(HttpStatusCode.OK) { Content = new StringContent(Section ?? (request.RequestUri.Query == "?item_limit=20"
                ? """{"id":"row","has_more":true,"items":[]}""" : """{"id":"row","has_more":false,"items":[]}""")) };
        }
    }
}
