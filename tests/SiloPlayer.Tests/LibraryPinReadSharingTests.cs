using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

/// <summary>The sidebar and the open library share metadata reads, without sharing caller cancellation.</summary>
public sealed class LibraryPinReadSharingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SidebarAndLibraryJoinTheSameInFlightTitleRead(bool collections)
    {
        using var wire = new Wire();
        var api = new CatalogApi(wire.Client);
        var sidebar = Read(api, collections);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var page = Read(api, collections);
        wire.Release.TrySetResult();
        await Task.WhenAll(sidebar, page);
        Assert.Equal(1, wire.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeavingTheLibraryDoesNotCancelTheSidebarTitleRead(bool collections)
    {
        using var wire = new Wire();
        var api = new CatalogApi(wire.Client);
        using var pageLifetime = new CancellationTokenSource();
        var page = Read(api, collections, pageLifetime.Token);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var sidebar = Read(api, collections);
        pageLifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => page);
        wire.Release.TrySetResult();
        await sidebar.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, wire.Reads);
        Assert.Equal(0, wire.CanceledRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportCatchUpDoesNotDuplicateTheSameOwnersTitleRead(bool collections)
    {
        using var wire = new Wire();
        var api = new CatalogApi(wire.Client);
        var first = Read(api, collections);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(wire.Client.TryInvalidateAccessContext(wire.Client.CaptureContext(), out _, transportCatchUp: true));
        var second = Read(api, collections);
        wire.Release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, wire.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccessChangesNeverJoinThePreviousOwnersTitleRead(bool collections)
    {
        using var wire = new Wire();
        var api = new CatalogApi(wire.Client);
        var first = Read(api, collections);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        wire.Client.InvalidateAccessContext();
        var second = Read(api, collections);
        wire.Release.TrySetResult();
        try { await first; } catch (OperationCanceledException) { }
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, wire.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailedTitleReadDoesNotPreventAnExplicitRetry(bool collections)
    {
        using var wire = new Wire { FailFirst = true };
        var api = new CatalogApi(wire.Client);
        wire.Release.TrySetResult();
        await Assert.ThrowsAsync<ApiException>(() => Read(api, collections));
        await Read(api, collections);
        Assert.Equal(2, wire.Reads);
    }

    private static async Task Read(CatalogApi api, bool collections, CancellationToken ct = default)
    {
        if (collections) await api.GetLibraryCollectionsAsync(7, ct);
        else await api.GetLibraryLayoutAsync(7, ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingMissingAndPersonalTargetsKeepTheSavedFallback(bool collections)
    {
        using var wire = new Wire();
        var api = new CatalogApi(wire.Client);
        var type = collections ? "collection" : "section";
        var id = collections ? "films" : "recent";
        var pending = Read(api, collections);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Saved", api.ResolvePinLabel(7, type, id, "Saved"));
        wire.Release.TrySetResult();
        await pending;
        Assert.Equal(collections ? wire.CollectionTitle : wire.RowTitle, api.ResolvePinLabel(7, type, id, "Saved"));
        Assert.Equal("Saved", api.ResolvePinLabel(7, type, "removed", "Saved"));
        Assert.Equal("Personal", api.ResolvePinLabel(null, "collection", "films", "Personal"));
        Assert.Equal("Other library", api.ResolvePinLabel(8, type, id, "Other library"));
    }

    [Fact]
    public async Task EmptySectionTitleKeepsTheSavedFallback()
    {
        using var wire = new Wire { RowTitle = "" };
        var api = new CatalogApi(wire.Client);
        wire.Release.TrySetResult();
        var layout = await api.GetLibraryLayoutAsync(7);
        Assert.Equal("", Assert.Single(layout.Sections).DisplayTitle);
        Assert.Equal("Saved row", api.ResolvePinLabel(7, "section", "recent", "Saved row"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANewPageReadRefreshesTitlesInsteadOfReusingACompletedResponse(bool collections)
    {
        using var wire = new Wire();
        var api = new CatalogApi(wire.Client);
        wire.Release.TrySetResult();
        await api.EnsureLibraryPinTitlesAsync(7, collections);
        var changes = 0;
        api.PinTitlesChanged += () => changes++;
        wire.RowTitle = wire.CollectionTitle = "Renamed";
        await Read(api, collections);
        Assert.Equal("Renamed", api.ResolvePinLabel(7, collections ? "collection" : "section", collections ? "films" : "recent", "Saved"));
        Assert.Equal(2, wire.Reads);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MediaCacheInvalidationDoesNotRefetchPinnedTitles(bool collections)
    {
        using var wire = new Wire();
        var api = new CatalogApi(wire.Client);
        wire.Release.TrySetResult();
        await api.EnsureLibraryPinTitlesAsync(7, collections);
        api.InvalidateLibraryCache();
        api.InvalidateFilterCache();
        await api.EnsureLibraryPinTitlesAsync(7, collections);
        Assert.Equal(1, wire.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOptionalReadsKeepFallbackAndDoNotLoopButPageReadsCanRetry(bool collections)
    {
        using var wire = new Wire { FailFirst = true };
        var api = new CatalogApi(wire.Client);
        wire.Release.TrySetResult();
        await Assert.ThrowsAsync<ApiException>(() => api.EnsureLibraryPinTitlesAsync(7, collections));
        Assert.Equal("Saved", api.ResolvePinLabel(7, collections ? "collection" : "section", collections ? "films" : "recent", "Saved"));
        await api.EnsureLibraryPinTitlesAsync(7, collections);
        Assert.Equal(1, wire.Reads);
        await Read(api, collections);
        Assert.Equal(2, wire.Reads);
    }

    [Theory]
    [InlineData("access")]
    [InlineData("profile")]
    [InlineData("metadata")]
    public async Task RetiredReadsCannotPublishTitlesOrSatisfyTheNewOwner(string retirement)
    {
        using var wire = new Wire();
        var api = new CatalogApi(wire.Client);
        var changes = 0;
        api.PinTitlesChanged += () => Interlocked.Increment(ref changes);
        var retired = api.GetLibraryCollectionsAsync(7);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (retirement == "access") wire.Client.InvalidateAccessContext();
        else if (retirement == "profile") wire.Client.SetProfile("second");
        else wire.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        wire.CollectionTitle = "New owner";
        var current = api.GetLibraryCollectionsAsync(7);
        wire.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retired);
        await current;
        Assert.Equal(2, wire.Reads);
        Assert.Equal(1, changes);
        Assert.Equal("New owner", api.ResolvePinLabel(7, "collection", "films", "Saved"));
    }

    [Fact]
    public async Task KeyedSectionTitlesFollowInterfaceLanguageWithoutChangingTheSavedLabelOrReadingAgain()
    {
        using var wire = new Wire { TitleKey = "home.section.becauseYouEnjoy" };
        var api = new CatalogApi(wire.Client);
        wire.Release.TrySetResult();
        await api.GetLibraryLayoutAsync(7);
        foreach (var language in new[] { "de", "ar", "ja" })
        {
            using var locale = AppStrings.UseForCurrentFlow(new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US")));
            Assert.Equal(AppStrings.ServerText(wire.TitleKey, new Dictionary<string, string> { ["genres"] = "Drama" }, wire.RowTitle),
                api.ResolvePinLabel(7, "section", "recent", "Saved"));
        }
        Assert.Equal(1, wire.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheLastConsumerLeavingStillCancelsTheUnderlyingPageRead(bool collections)
    {
        using var wire = new Wire();
        var api = new CatalogApi(wire.Client);
        using var lifetime = new CancellationTokenSource();
        var pending = Read(api, collections, lifetime.Token);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await wire.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        wire.Release.TrySetResult();
        await Read(api, collections);
        Assert.Equal(1, wire.CanceledRequests);
        Assert.Equal(2, wire.Reads);
    }

    private sealed class Wire : HttpMessageHandler
    {
        private readonly HttpClient _http;
        internal readonly SiloApiClient Client;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Reads, CanceledRequests;
        internal bool FailFirst;
        internal string RowTitle = "Current row title", CollectionTitle = "Current collection title";
        internal string? TitleKey;

        internal Wire()
        {
            _http = new HttpClient(this, disposeHandler: false);
            Client = new SiloApiClient(_http);
            Client.SetBaseUrl("https://pin-sharing.invalid");
            Client.SetProfile("first");
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var read = Interlocked.Increment(ref Reads);
            var rowTitle = RowTitle;
            var collectionTitle = CollectionTitle;
            Started.TrySetResult();
            try { await Release.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { Interlocked.Increment(ref CanceledRequests); Canceled.TrySetResult(); throw; }
            if (FailFirst && read == 1)
                return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"fixture_unavailable\",\"message\":\"retry\"}") };
            return new(HttpStatusCode.OK) { Content = new StringContent(
                request.RequestUri!.AbsolutePath.EndsWith("/layout", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(new { sections = new[] { new { id = "recent", title = rowTitle, title_key = TitleKey, title_args = new { genres = "Drama" } } } })
                    : JsonSerializer.Serialize(new { library_id = 7, collections = new[] { new { id = "films", title = collectionTitle } } })
            ) };
        }

        protected override void Dispose(bool disposing)
        {
            Release.TrySetResult();
            if (disposing) _http.Dispose();
            base.Dispose(disposing);
        }
    }
}
