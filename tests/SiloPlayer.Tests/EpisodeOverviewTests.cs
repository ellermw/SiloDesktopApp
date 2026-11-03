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
        Assert.Single(scope.Paths);
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
    [InlineData("language", null)] [InlineData("access", null)]
    [InlineData("language", "")] [InlineData("access", "")]
    [InlineData("language", " \t\r\n ")] [InlineData("access", " \t\r\n ")]
    public async Task RetiredSeriesDisplaySurvivesFailedReadsUntilReplacementResolves(string change, string? own)
    {
        using var scope = new Scope { Own = own };
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        var episode = scope.Vm.Item;
        var source = await scope.Vm.ResolveOverviewAsync();
        Assert.NotNull(source);
        if (change == "access") scope.Client.InvalidateAccessContext();
        else scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        Assert.Null(scope.Vm.OverviewSource);
        Assert.Same(source, scope.Vm.OverviewDisplaySource);
        scope.FailSeries = true;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Null(await scope.Vm.ResolveOverviewAsync());
            Assert.True(scope.Vm.OverviewResolutionFailed);
            Assert.Null(scope.Vm.OverviewSource);
            Assert.Same(source, scope.Vm.OverviewDisplaySource);
            Assert.Equal("Series summary", scope.Vm.OverviewDisplaySource!.Overview);
            var reads = scope.Paths.Count;
            await scope.Vm.RefreshOverviewAsync(source!, default);
            Assert.Equal(reads, scope.Paths.Count);
        }
        scope.FailSeries = false; scope.HoldSeries = true; scope.Translated = true;
        var pending = scope.Vm.ResolveOverviewAsync();
        await scope.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(scope.Vm.OverviewSource);
        Assert.Same(source, scope.Vm.OverviewDisplaySource);
        scope.Release.TrySetResult();
        var replacement = await pending;
        Assert.NotNull(replacement);
        Assert.NotSame(source, replacement);
        Assert.Same(replacement, scope.Vm.OverviewSource);
        Assert.Same(replacement, scope.Vm.OverviewDisplaySource);
        Assert.Equal("Résumé traduit", replacement!.Overview);
        Assert.Null(replacement.PendingTranslationLanguage);
        Assert.False(scope.Vm.OverviewResolutionFailed);
        Assert.Same(episode, scope.Vm.Item);
        Assert.Equal(own, episode!.Overview);
    }

    [Theory]
    [InlineData("item")] [InlineData("library")] [InlineData("profile")]
    [InlineData("authentication")] [InlineData("server")] [InlineData("page")]
    public async Task RetiredSeriesDisplayCannotFollowAnotherOwner(string change)
    {
        using var scope = new Scope();
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        var source = await scope.Vm.ResolveOverviewAsync();
        scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        Assert.NotNull(source);
        Assert.Same(source, scope.Vm.OverviewDisplaySource);
        if (change == "item") scope.Vm.Item = new() { ContentId = "other", Type = "episode" };
        else if (change == "library") await scope.Vm.LoadInLibraryAsync("episode", 99);
        else if (change == "profile") scope.Client.SetProfile("other-profile");
        else if (change == "authentication") { scope.Client.BeginAuthenticationSession(null); scope.Client.SetProfile("synthetic"); }
        else if (change == "server") { scope.Client.SetBaseUrl("https://other-overview-tests.invalid"); scope.Client.SetProfile("synthetic"); }
        else scope.Vm.CancelPendingLoads();
        Assert.Null(scope.Vm.OverviewDisplaySource);
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
    [InlineData("movie", "language")] [InlineData("episode", "language")]
    [InlineData("movie", "profile")] [InlineData("episode", "profile")]
    [InlineData("movie", "access")] [InlineData("episode", "access")]
    public async Task OwnOverviewIsRereadBeforePublishingNewScope(string type, string change)
    {
        using var scope = new Scope { ItemType = type, Own = "Old plot" };
        await scope.Vm.LoadInLibraryAsync(type, 42);
        var item = await scope.Vm.ResolveOverviewAsync();
        if (change == "profile") scope.Client.SetProfile("new-profile");
        else if (change == "access") scope.Client.InvalidateAccessContext();
        else scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        scope.Own = "New-language plot";
        scope.TargetLanguage = "de";
        scope.OwnMachineTranslatedFields = ["overview"];
        scope.HoldItem = true;
        var pending = scope.Vm.ResolveOverviewAsync();
        await scope.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(scope.Vm.OverviewSource);
        Assert.Equal("Old plot", item!.Overview);
        if (change != "profile") Assert.Same(item, scope.Vm.OverviewDisplaySource);
        else Assert.Null(scope.Vm.OverviewDisplaySource);
        scope.Release.TrySetResult();
        Assert.Same(item, await pending);
        Assert.Same(item, scope.Vm.Item);
        Assert.Same(item, scope.Vm.OverviewSource);
        Assert.Equal("New-language plot", item.Overview);
        Assert.Equal("de", item.PendingTranslationLanguage);
        Assert.Equal(new[] { "overview" }, item.MachineTranslatedFields);
        Assert.Same(item, await scope.Vm.ResolveOverviewAsync());
        Assert.Equal(2, scope.Paths.Count(path => path == $"/api/v2/catalog/items/{type}?library_id=42"));
    }

    [Theory]
    [InlineData("movie", "item")] [InlineData("episode", "item")]
    [InlineData("movie", "library")] [InlineData("episode", "library")]
    [InlineData("movie", "profile")] [InlineData("episode", "profile")]
    [InlineData("movie", "language")] [InlineData("episode", "language")]
    [InlineData("movie", "page")] [InlineData("episode", "page")]
    [InlineData("movie", "cancellation")] [InlineData("episode", "cancellation")]
    public async Task DelayedOwnOverviewCannotRevalidateRetiredSource(string type, string change)
    {
        using var scope = new Scope { ItemType = type, Own = "Old plot" };
        await scope.Vm.LoadInLibraryAsync(type, 42);
        var item = await scope.Vm.ResolveOverviewAsync();
        scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        scope.Own = "Retired read"; scope.TargetLanguage = "de"; scope.HoldItem = true;
        using var ct = new CancellationTokenSource();
        var pending = scope.Vm.ResolveOverviewAsync(ct.Token);
        await scope.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (change == "item") scope.Vm.Item = new() { ContentId = "other", Type = "episode" };
        else if (change == "library") { scope.HoldItem = false; await scope.Vm.LoadInLibraryAsync(type, 99); }
        else if (change == "profile") scope.Client.SetProfile("other-profile");
        else if (change == "language") scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        else if (change == "page") scope.Vm.CancelPendingLoads();
        else ct.Cancel();
        scope.Release.TrySetResult();
        Assert.Null(await pending);
        Assert.Equal("Old plot", item!.Overview);
        Assert.Equal("fr", item.PendingTranslationLanguage);
        if (change != "library") Assert.Null(scope.Vm.OverviewSource);
    }

    [Theory]
    [InlineData("movie")] [InlineData("episode")]
    public async Task FailedOwnReadRemainsRetiredUntilSuccessfulRetry(string type)
    {
        using var scope = new Scope { ItemType = type, Own = "Old plot" };
        await scope.Vm.LoadInLibraryAsync(type, 42);
        var item = await scope.Vm.ResolveOverviewAsync();
        scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        scope.Own = "New plot"; scope.TargetLanguage = "de"; scope.FailItem = true;
        Assert.Null(await scope.Vm.ResolveOverviewAsync());
        Assert.True(scope.Vm.OverviewResolutionFailed);
        Assert.Null(scope.Vm.OverviewSource);
        Assert.Equal("Old plot", item!.Overview);
        scope.FailItem = false;
        Assert.Same(item, await scope.Vm.ResolveOverviewAsync());
        Assert.False(scope.Vm.OverviewResolutionFailed);
        Assert.Equal("New plot", item.Overview);
        Assert.Equal("de", item.PendingTranslationLanguage);
    }

    [Theory]
    [InlineData("movie", "language")] [InlineData("episode", "language")]
    [InlineData("movie", "access")] [InlineData("episode", "access")]
    public async Task RepeatedOwnReadFailuresPreserveDisplayButNeverRevalidateTranslation(string type, string change)
    {
        using var scope = new Scope { ItemType = type, Own = "Visible plot" };
        await scope.Vm.LoadInLibraryAsync(type, 42);
        var item = await scope.Vm.ResolveOverviewAsync();
        if (change == "access") scope.Client.InvalidateAccessContext();
        else scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        scope.Own = "Replacement plot"; scope.TargetLanguage = "de"; scope.FailItem = true;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Null(await scope.Vm.ResolveOverviewAsync());
            Assert.True(scope.Vm.OverviewResolutionFailed);
            Assert.Null(scope.Vm.OverviewSource);
            Assert.Same(item, scope.Vm.OverviewDisplaySource);
            Assert.Equal("Visible plot", scope.Vm.OverviewDisplaySource!.Overview);
            Assert.Equal("fr", item!.PendingTranslationLanguage);
            var reads = scope.Paths.Count;
            await scope.Vm.RefreshOverviewAsync(item, default);
            Assert.Equal(reads, scope.Paths.Count);
        }
        scope.FailItem = false;
        Assert.Same(item, await scope.Vm.ResolveOverviewAsync());
        Assert.False(scope.Vm.OverviewResolutionFailed);
        Assert.Same(item, scope.Vm.OverviewDisplaySource);
        Assert.Equal("Replacement plot", item!.Overview);
        Assert.Equal("de", item.PendingTranslationLanguage);
    }

    [Fact]
    public async Task OwnReplacementAndSeriesFallbackPublishAtomically()
    {
        using var scope = new Scope { Own = "Visible episode plot" };
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        var item = await scope.Vm.ResolveOverviewAsync();
        scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        scope.Own = " "; scope.TargetLanguage = "de"; scope.FailSeries = true;
        Assert.Null(await scope.Vm.ResolveOverviewAsync());
        Assert.True(scope.Vm.OverviewResolutionFailed);
        Assert.Null(scope.Vm.OverviewSource);
        Assert.Same(item, scope.Vm.OverviewDisplaySource);
        Assert.Equal("Visible episode plot", item!.Overview);
        Assert.Equal("fr", item.PendingTranslationLanguage);
        scope.FailSeries = false;
        var series = await scope.Vm.ResolveOverviewAsync();
        Assert.Equal("series", series!.ContentId);
        Assert.Same(series, scope.Vm.OverviewDisplaySource);
        Assert.Equal(" ", item.Overview);
        Assert.Equal("de", item.PendingTranslationLanguage);
    }

    [Theory]
    [InlineData("item")] [InlineData("library")] [InlineData("profile")] [InlineData("page")]
    public async Task StaleOwnDisplayCannotFollowAnotherOwner(string change)
    {
        using var scope = new Scope { Own = "Visible plot" };
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        var item = await scope.Vm.ResolveOverviewAsync();
        scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        Assert.Same(item, scope.Vm.OverviewDisplaySource);
        if (change == "item") scope.Vm.Item = new() { ContentId = "other", Type = "episode" };
        else if (change == "library") { scope.Own = ""; await scope.Vm.LoadInLibraryAsync("episode", 99); }
        else if (change == "profile") scope.Client.SetProfile("other-profile");
        else scope.Vm.CancelPendingLoads();
        Assert.Null(scope.Vm.OverviewDisplaySource);
    }

    [Fact]
    public async Task LanguageRoundTripRestoresOwnOverviewAfterSeriesFallback()
    {
        using var scope = new Scope { Own = "English episode plot", TargetLanguage = "en" };
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        var episode = await scope.Vm.ResolveOverviewAsync();
        scope.Own = " "; scope.TargetLanguage = "fr";
        scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        var series = await scope.Vm.ResolveOverviewAsync();
        Assert.Equal("series", series!.ContentId);
        Assert.Equal(" ", episode!.Overview);

        scope.Own = "English episode plot"; scope.TargetLanguage = "en";
        scope.OwnMachineTranslatedFields = ["overview"];
        scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        Assert.Same(series, scope.Vm.OverviewDisplaySource);
        Assert.Same(episode, await scope.Vm.ResolveOverviewAsync());
        Assert.Same(episode, scope.Vm.Item);
        Assert.Equal("English episode plot", episode.Overview);
        Assert.Equal("en", episode.PendingTranslationLanguage);
        Assert.Equal(new[] { "overview" }, episode.MachineTranslatedFields);
        Assert.Equal(3, scope.Paths.Count(path => path == "/api/v2/catalog/items/episode?library_id=42"));
        Assert.Single(scope.Paths, path => path == "/api/v2/catalog/items/series?library_id=42");
    }

    [Theory]
    [InlineData("language")] [InlineData("access")]
    public async Task RetiredBlankEpisodePreservesSeriesUntilOwnReadSucceeds(string change)
    {
        using var scope = new Scope();
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        var episode = scope.Vm.Item;
        var series = await scope.Vm.ResolveOverviewAsync();
        scope.Own = "New episode plot"; scope.TargetLanguage = "de"; scope.FailItem = true;
        if (change == "access") scope.Client.InvalidateAccessContext();
        else scope.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        Assert.Null(await scope.Vm.ResolveOverviewAsync());
        Assert.True(scope.Vm.OverviewResolutionFailed);
        Assert.Null(scope.Vm.OverviewSource);
        Assert.Same(series, scope.Vm.OverviewDisplaySource);
        Assert.Equal("", episode!.Overview);

        scope.FailItem = false; scope.HoldItem = true;
        var pending = scope.Vm.ResolveOverviewAsync();
        await scope.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(scope.Vm.OverviewSource);
        Assert.Same(series, scope.Vm.OverviewDisplaySource);
        scope.Release.TrySetResult();
        Assert.Same(episode, await pending);
        Assert.Same(episode, scope.Vm.OverviewDisplaySource);
        Assert.Equal("New episode plot", episode.Overview);
        Assert.Equal("de", episode.PendingTranslationLanguage);
        Assert.Single(scope.Paths, path => path == "/api/v2/catalog/items/series?library_id=42");
    }

    [Fact]
    public async Task TransientSeriesReadFailureCanBeRetriedInTheSameScope()
    {
        using var scope = new Scope { FailSeries = true };
        await scope.Vm.LoadInLibraryAsync("episode", 42);
        var episode = scope.Vm.Item;
        Assert.Null(await scope.Vm.ResolveOverviewAsync());
        Assert.True(scope.Vm.OverviewResolutionFailed);
        scope.FailSeries = false;
        var source = await scope.Vm.ResolveOverviewAsync();
        Assert.False(scope.Vm.OverviewResolutionFailed);
        Assert.Same(episode, scope.Vm.Item);
        Assert.Equal("Series summary", source!.Overview);
        Assert.Equal("fr", source.PendingTranslationLanguage);
        Assert.Null(scope.Vm.ErrorMessage);
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
        internal string ItemType = "episode", TargetLanguage = "fr";
        internal string[] OwnMachineTranslatedFields = [];
        internal bool HoldSeries, FailSeries, Translated, HoldItem, FailItem;
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
                if (FailSeries) return new(HttpStatusCode.InternalServerError);
                return Json(new { content_id = "series", type = "series", overview = Translated ? "Résumé traduit" : "Series summary",
                    pending_translation_language = Translated ? null : "fr", machine_translated_fields = new[] { "overview" } });
            }
            var value = new { content_id = request.RequestUri.AbsolutePath.Split('/').Last(), type = ItemType,
                overview = Own, series_id = SeriesId, pending_translation_language = TargetLanguage,
                machine_translated_fields = OwnMachineTranslatedFields, user_state = new { } };
            if (HoldItem) { Entered.TrySetResult(); await Release.Task; }
            if (FailItem) return new(HttpStatusCode.InternalServerError);
            return Json(value);
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
        protected override void Dispose(bool disposing)
        {
            Release.TrySetResult(); Vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(Vm);
            if (disposing) _http.Dispose(); base.Dispose(disposing);
        }
    }
}
