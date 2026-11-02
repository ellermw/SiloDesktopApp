using System.Net;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class EpisodeOverviewTests
{
    [Fact]
    public async Task OwnOverviewWinsWithoutReadingSeries()
    {
        using var scope = new Scope { Own = "Episode plot" };
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        var source = await scope.Vm.ResolveOverviewAsync();
        Assert.Same(scope.Vm.Item, source);
        Assert.Equal("Episode plot", source!.Overview);
        Assert.DoesNotContain(scope.Paths, path => path.Contains("/series"));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" \t\r\n ")]
    public async Task BlankOverviewUsesSeriesWithoutChangingEpisodeIdentity(string? own)
    {
        using var scope = new Scope { Own = own };
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        var episode = scope.Vm.Item;
        var source = await scope.Vm.ResolveOverviewAsync();
        Assert.Same(episode, scope.Vm.Item);
        Assert.Equal("series", source!.ContentId);
        Assert.Equal("Series summary", source.Overview);
        Assert.Equal("fr", source.PendingTranslationLanguage);
        Assert.Equal(new[] { "overview" }, source.MachineTranslatedFields);
        Assert.Contains("/api/v2/catalog/items/series?library_id=42", scope.Paths);
        Assert.Equal(own, episode!.Overview);
    }

    [Theory]
    [InlineData("profile")] [InlineData("language")]
    public async Task OwnTranslationSourceRetiresWithItsMetadataScope(string change)
    {
        using var scope = new Scope { Own = "Episode plot" };
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        Assert.NotNull(await scope.Vm.ResolveOverviewAsync());
        if (change == "profile") scope.Client.SetProfile("new-profile");
        else scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        Assert.Null(scope.Vm.OverviewSource);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingOrFailedSeriesNeverFailsEpisode(bool missing)
    {
        using var scope = new Scope { SeriesId = missing ? null : "series", FailSeries = !missing };
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        Assert.Null(await scope.Vm.ResolveOverviewAsync());
        Assert.Null(scope.Vm.OverviewSource);
        Assert.Equal("episode", scope.Vm.Item!.ContentId);
        Assert.Null(scope.Vm.ErrorMessage);
        Assert.False(scope.Vm.IsLoading);
    }

    [Theory]
    [InlineData("item")] [InlineData("library")] [InlineData("profile")]
    [InlineData("language")] [InlineData("page")] [InlineData("cancellation")]
    public async Task DelayedFallbackCannotPublishAfterOwnerChanges(string change)
    {
        using var scope = new Scope { HoldSeries = true };
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        using var ct = new CancellationTokenSource();
        var pending = scope.Vm.ResolveOverviewAsync(ct.Token);
        await scope.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        switch (change)
        {
            case "item": scope.Vm.Item = new() { ContentId = "different", Type = "episode" }; break;
            case "library": await scope.Vm.LoadInLibraryAsync("episode", 99); break;
            case "profile": scope.Client.SetProfile("other-profile"); break;
            case "language": scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language"); break;
            case "page": scope.Vm.CancelPendingLoads(); break;
            case "cancellation": ct.Cancel(); break;
        }
        scope.Release.TrySetResult();
        Assert.Null(await pending);
        Assert.Null(scope.Vm.OverviewSource);
    }

    [Fact]
    public async Task TranslationRefreshReadsSeriesAndUpdatesOnlyItsDescription()
    {
        using var scope = new Scope();
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        var source = await scope.Vm.ResolveOverviewAsync();
        scope.Translated = true;
        await scope.Vm.RefreshOverviewAsync(source!, default);
        Assert.Same(source, scope.Vm.OverviewSource);
        Assert.Equal("Résumé traduit", source!.Overview);
        Assert.Null(source.PendingTranslationLanguage);
        Assert.Equal("episode", scope.Vm.Item!.ContentId);
        Assert.Equal("", scope.Vm.Item.Overview);
        Assert.Equal(2, scope.Paths.Count(path => path == "/api/v2/catalog/items/series?library_id=42"));
    }

    [Theory]
    [InlineData("profile")] [InlineData("language")] [InlineData("item")] [InlineData("page")]
    public async Task DelayedTranslatedDescriptionCannotMutateRetiredSource(string change)
    {
        using var scope = new Scope();
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        var source = await scope.Vm.ResolveOverviewAsync();
        scope.HoldSeries = true; scope.Translated = true;
        var pending = scope.Vm.RefreshOverviewAsync(source!, default);
        await scope.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (change == "profile") scope.Client.SetProfile("other");
        else if (change == "language") scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        else if (change == "item") scope.Vm.Item = new() { ContentId = "new", Type = "episode" };
        else scope.Vm.CancelPendingLoads();
        scope.Release.TrySetResult();
        try { await pending; } catch (OperationCanceledException) { }
        Assert.Equal("Series summary", source!.Overview);
    }

    private sealed class Scope : HttpMessageHandler
    {
        private readonly HttpClient _http;
        internal string? Own = "", SeriesId = "series";
        internal bool HoldSeries, FailSeries, Translated;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<string> Paths = [];
        internal SiloApiClient Client { get; }
        internal ItemDetailViewModel Vm { get; }
        internal Scope()
        {
            _http = new HttpClient(this, false);
            Client = new SiloApiClient(_http); Client.SetBaseUrl("https://overview-tests.invalid"); Client.SetProfile("synthetic");
            Vm = new ItemDetailViewModel(new CatalogApi(Client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException("Unexpected unscoped read")));
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery; Paths.Add(path);
            if (request.RequestUri.AbsolutePath.EndsWith("/series"))
            {
                if (HoldSeries) { Entered.TrySetResult(); await Release.Task; }
                if (FailSeries) return new(HttpStatusCode.NotFound);
                return Json(new { content_id = "series", type = "series", overview = Translated ? "Résumé traduit" : "Series summary",
                    pending_translation_language = Translated ? null : "fr", machine_translated_fields = new[] { "overview" } });
            }
            return Json(new { content_id = "episode", type = "episode", overview = Own, series_id = SeriesId, user_state = new { } });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
        protected override void Dispose(bool disposing)
        {
            Release.TrySetResult(); Vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(Vm);
            if (disposing) _http.Dispose(); base.Dispose(disposing);
        }
    }
}
