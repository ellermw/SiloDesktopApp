using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class NotInterestedTests
{
    [Fact]
    public async Task EmptyForYouStillRefreshesWhenAProfileUnmarksItsLastTitle()
    {
        var marked = false;
        using var http = new HttpClient(new Wire(request => Json(request.RequestUri!.AbsolutePath.EndsWith("/discover")
            ? marked ? """{"items":[]}""" : """{"items":[{"title":"Recommended","type":"recommended_for_you","items":[{"content_id":"only","type":"movie"}]}]}""" : "{}")));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested.invalid");
        var vm = new SiloPlayer.ViewModels.RecommendationsViewModel(new CatalogApi(client), new RecommendationsApi(client));
        try
        {
            await vm.LoadCommand.ExecuteAsync(null); marked = true;
            WeakReferenceMessenger.Default.Send(new NotInterestedChange(client.CaptureContext(), "only", true));
            for (var i = 0; i < 100 && vm.Rows.Count > 0; i++) await Task.Delay(10);
            Assert.Empty(vm.Rows); marked = false;
            WeakReferenceMessenger.Default.Send(new NotInterestedChange(client.CaptureContext(), "only", false));
            for (var i = 0; i < 100 && vm.Rows.Count == 0; i++) await Task.Delay(10);
            Assert.Equal("only", Assert.Single(Assert.Single(vm.Rows).Items).ContentId);
        }
        finally { WeakReferenceMessenger.Default.UnregisterAll(vm); }
    }

    [Fact]
    public async Task NotInterestedInvalidatesCachedForYouAndOpenedSectionWithoutReplacingOtherTitles()
    {
        var marked = false;
        using var http = new HttpClient(new Wire(request =>
        {
            var items = marked ? """[{"content_id":"keep","type":"movie","title":"Keep"}]""" : """[{"content_id":"remove","type":"movie","title":"Remove"},{"content_id":"keep","type":"movie","title":"Keep"}]""";
            return Json(request.RequestUri!.AbsolutePath.EndsWith("/discover")
                ? "{\"items\":[{\"label\":\"Recommended\",\"type\":\"recommended_for_you\",\"items\":" + items + "}]}"
                : request.RequestUri.AbsolutePath.Contains("/section/") ? "{\"items\":" + items + "}" : "{}");
        }));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested.invalid"); client.SetProfile("one");
        var api = new RecommendationsApi(client);
        var forYou = new SiloPlayer.ViewModels.RecommendationsViewModel(new CatalogApi(client), api);
        var section = new SiloPlayer.ViewModels.RecommendationSectionViewModel(api);
        try
        {
            await forYou.LoadCommand.ExecuteAsync(null); await section.LoadAsync(new("genre", "drama", "Drama"));
            var row = Assert.Single(forYou.Rows); var firstKeep = row.Items[1]; var sectionKeep = section.Items[1]; marked = true;
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new NotInterestedChange(client.CaptureContext(), "remove", true));
            for (var i = 0; i < 100 && (row.Items.Count > 1 || section.Items.Count > 1); i++) await Task.Delay(10);
            Assert.Same(row, Assert.Single(forYou.Rows)); Assert.Same(firstKeep, Assert.Single(row.Items)); Assert.Same(sectionKeep, Assert.Single(section.Items));
            marked = false;
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new NotInterestedChange(client.CaptureContext(), "remove", false));
            for (var i = 0; i < 100 && (row.Items.Count < 2 || section.Items.Count < 2); i++) await Task.Delay(10);
            Assert.Equal(2, row.Items.Count); Assert.Same(firstKeep, row.Items[1]); Assert.Equal(2, section.Items.Count); Assert.Same(sectionKeep, section.Items[1]);
        }
        finally { CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(forYou); CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(section); }
    }

    [Fact]
    public async Task CurrentNotInterestedEventRefreshesMountedSimilarRowAndPreservesUnchangedObjects()
    {
        var marked = false; var calls = 0;
        using var http = new HttpClient(new Wire(_ =>
        {
            calls++;
            return Json(marked ? """{"items":[{"content_id":"keep","type":"movie","title":"Keep"}]}""" : """{"items":[{"content_id":"remove","type":"movie","title":"Remove"},{"content_id":"keep","type":"movie","title":"Keep"}]}""");
        }));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested.invalid"); client.SetProfile("one");
        var vm = new SiloPlayer.ViewModels.ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        try
        {
            vm.Item = new() { ContentId = "source", Type = "movie" };
            await vm.LoadSimilarCommand.ExecuteAsync(null);
            var keep = vm.SimilarItems[1]; marked = true;
            vm.Receive(new NotInterestedChange(client.CaptureContext(), "remove", true));
            for (var i = 0; i < 100 && calls < 2; i++) await Task.Delay(10);
            Assert.True(calls >= 2, "A mark elsewhere must refresh the mounted detail's discovery row.");
            Assert.Same(keep, Assert.Single(vm.SimilarItems));
            marked = false; vm.Receive(new NotInterestedChange(client.CaptureContext(), "remove", false));
            for (var i = 0; i < 100 && vm.SimilarItems.Count < 2; i++) await Task.Delay(10);
            Assert.Equal(2, vm.SimilarItems.Count); Assert.Same(keep, vm.SimilarItems[1]);
        }
        finally { CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(vm); }
    }

    [Theory]
    [InlineData("available", true)]
    [InlineData("unsupported", false)]
    [InlineData("", false)]
    public async Task CapabilityControlsWhetherAnOlderServerReceivesMutations(string state, bool available)
    {
        var writes = 0;
        using var http = new HttpClient(new Wire(request =>
        {
            if (request.Method == HttpMethod.Get) return Json("{\"state\":\"" + state + "\"}", state == "" ? HttpStatusCode.NotFound : HttpStatusCode.OK);
            writes++; return new(HttpStatusCode.NoContent);
        }));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested.invalid");
        var api = new CatalogApi(client);
        Assert.Equal(available, await api.SupportsNotInterestedAsync());
        var service = new NotInterestedService(api);
        var item = new MediaItem { ContentId = "movie:one", Type = "movie" };
        if (available) await service.ApplyAsync(service.Capture(item, true), item);
        else await Assert.ThrowsAsync<NotSupportedException>(() => service.ApplyAsync(service.Capture(item, true), item));
        Assert.Equal(available ? 1 : 0, writes);
    }

    [Fact]
    public async Task ActualMarkAndListRoutesPreserveOpaqueCursorAndHandleNoContentAndAbsentMark()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new Wire(request =>
        {
            requests.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            if (request.RequestUri.AbsolutePath.Contains("capabilities")) return Json("""{"state":"available"}""");
            if (request.Method != HttpMethod.Get) return new(HttpStatusCode.NoContent);
            if (request.RequestUri.AbsolutePath.EndsWith("not-interested")) return Json("""{"items":[{"content_id":"movie:one","type":"movie","user_state":{"not_interested":true}}],"page":{"has_more":true,"next_cursor":"next"}}""");
            return Json("{}", HttpStatusCode.NotFound);
        }));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested.invalid");
        var api = new CatalogApi(client); var context = api.CaptureContext();
        var page = await api.GetNotInterestedAsync(context, "opaque +/?=");
        Assert.True(Assert.Single(page.Items).UserState!.NotInterested);
        Assert.Equal("next", page.Page!.NextCursor);
        Assert.Contains("GET /api/v2/not-interested?limit=50&cursor=opaque%20%2B%2F%3F%3D", requests);
        Assert.Null(await api.GetNotInterestedMarkAsync(context, "movie:one"));
        await api.SetNotInterestedAsync(context, "movie:one/part", "movie", true);
        await api.SetNotInterestedAsync(context, "movie:one/part", "movie", false);
        Assert.Contains("PUT /api/v2/not-interested/movie%3Aone%2Fpart", requests);
        Assert.Contains("DELETE /api/v2/not-interested/movie%3Aone%2Fpart", requests);
        await Assert.ThrowsAsync<ArgumentException>(() => api.SetNotInterestedAsync(context, "episode", "episode", true));
        Assert.DoesNotContain(requests, request => request.Contains("episode"));
    }

    [Fact]
    public async Task CapturedWriteAndUndoRefuseReplacementProfileAndRollbackPreservesOtherFlags()
    {
        var fail = false; var writes = 0;
        using var http = new HttpClient(new Wire(request =>
        {
            if (request.Method == HttpMethod.Get) return Json("""{"state":"available"}""");
            writes++; return fail ? Json("{}", HttpStatusCode.InternalServerError) : new(HttpStatusCode.NoContent);
        }));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested.invalid"); client.SetProfile("one");
        var service = new NotInterestedService(new CatalogApi(client));
        var changes = new List<NotInterestedChange>(); service.Changed += changes.Add;
        var item = new MediaItem { ContentId = "one", Type = "movie", UserState = new() { Played = true, IsInProgress = true, IsFavorite = true, InWatchlist = true } };
        var mark = service.Capture(item, true);
        await service.ApplyAsync(mark, item); Assert.True(item.UserState.NotInterested); Assert.Single(changes);
        client.SetProfile("two");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(mark with { Marked = false }, item));
        Assert.Equal(1, writes);
        client.SetProfile("one"); fail = true;
        await Assert.ThrowsAsync<ApiException>(() => service.ApplyAsync(service.Capture(item, false), item));
        Assert.True(item.UserState.NotInterested && item.UserState.Played && item.UserState.IsInProgress && item.UserState.IsFavorite && item.UserState.InWatchlist);
        Assert.Single(changes);
    }

    [Fact]
    public async Task LateWriteCannotPublishOrRollbackIntoReplacementProfile()
    {
        var started = new TaskCompletionSource(); var finish = new TaskCompletionSource();
        using var http = new HttpClient(new AsyncWire(async request =>
        {
            if (request.Method == HttpMethod.Get) return Json("""{"state":"available"}""");
            started.SetResult(); await finish.Task; return new(HttpStatusCode.NoContent);
        }));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested.invalid"); client.SetProfile("one");
        var service = new NotInterestedService(new CatalogApi(client)); var changes = 0; service.Changed += _ => changes++;
        var item = new MediaItem { ContentId = "one", Type = "movie" };
        var pending = service.ApplyAsync(service.Capture(item, true), item); await started.Task;
        client.SetProfile("two"); finish.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData("recommended_for_you", true)]
    [InlineData("because_you_watched", true)]
    [InlineData("similar_users_liked", true)]
    [InlineData("taste_match", true)]
    [InlineData("hidden_gems", true)]
    [InlineData("critically_acclaimed", true)]
    [InlineData("award_winners", true)]
    [InlineData("forgotten_favorites", true)]
    [InlineData("format_showcase", true)]
    [InlineData("editorial_spotlight", true)]
    [InlineData("seasonal_themed", true)]
    [InlineData("mood_collection", true)]
    [InlineData("trending_on_server", true)]
    [InlineData("most_watched", true)]
    [InlineData("trending_discover", true)]
    [InlineData("genre_roulette", true)]
    [InlineData("anniversaries", true)]
    [InlineData("short_watches", true)]
    [InlineData("continue_watching", false)]
    [InlineData("next_up", false)]
    [InlineData("recently_added", false)]
    [InlineData("custom", false)]
    [InlineData("favorites", false)]
    [InlineData("watchlist", false)]
    public void MarkRemovesOnlyDesignatedRowsAndKeepsOtherItemObjects(string type, bool removed)
    {
        var keep = new MediaItem { ContentId = "keep" };
        var section = new HomeSectionWithItems { SectionType = type, Items = [new() { ContentId = "mark", Type = "movie", UserState = new() { IsFavorite = true } }, keep] };
        NotInterestedPolicy.Apply([section], "mark", true);
        Assert.Equal(removed ? 1 : 2, section.Items.Count);
        Assert.Same(keep, section.Items.Last());
        if (!removed) { var saved = section.Items[0].UserState!; Assert.True(saved.NotInterested && saved.IsFavorite); }
    }

    [Fact]
    public void RealEventPayloadPatchesOnlyItsProfileAndPreservesMissingState()
    {
        using var http = new HttpClient(new Wire(_ => Json("{}")));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested.invalid"); client.SetProfile("one");
        var service = new NotInterestedService(new CatalogApi(client));
        var changes = new List<NotInterestedChange>(); service.Changed += changes.Add;
        using var payload = JsonDocument.Parse("""{"profile_id":"one","change":"not_interested","content_id":"movie:one","not_interested":true}""");
        service.ReceiveRealtime("user_state", "user_state.changed", payload.RootElement);
        Assert.True(Assert.Single(changes).Marked);
        client.SetProfile("two");
        service.ReceiveRealtime("user_state", "user_state.changed", payload.RootElement);
        Assert.Single(changes);
    }

    [Fact]
    public async Task ManagementPagingRetainsLoadedTitlesOnErrorAndRetriesSameCursor()
    {
        var moreFails = true; var cursors = new List<string>();
        using var http = new HttpClient(new Wire(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("capabilities")) return Json("""{"state":"available"}""");
            cursors.Add(request.RequestUri.Query);
            if (request.RequestUri.Query.Contains("cursor="))
                return moreFails ? Json("{}", HttpStatusCode.InternalServerError) : Json("""{"items":[{"content_id":"two","type":"series"}],"page":{"has_more":false}}""");
            return Json("""{"items":[{"content_id":"one","type":"movie"}],"page":{"has_more":true,"next_cursor":"next +"}}""");
        }));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested.invalid");
        var state = new NotInterestedListState(new CatalogApi(client));
        await state.LoadAsync(); await state.LoadMoreAsync();
        Assert.Equal("one", Assert.Single(state.Items).ContentId); Assert.NotNull(state.MoreError); Assert.True(state.HasMore);
        moreFails = false; await state.LoadMoreAsync();
        Assert.Equal(new[] { "one", "two" }, state.Items.Select(item => item.ContentId)); Assert.False(state.HasMore);
        Assert.Equal(cursors[1], cursors[2]);
    }

    [Fact]
    public async Task ManagementListDoesNotPublishAnotherProfilesLatePage()
    {
        var started = new TaskCompletionSource(); var finish = new TaskCompletionSource();
        using var http = new HttpClient(new AsyncWire(async request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("capabilities")) return Json("""{"state":"available"}""");
            started.TrySetResult(); await finish.Task;
            return Json("""{"items":[{"content_id":"old","type":"movie"}]}""");
        }));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested.invalid"); client.SetProfile("one");
        var state = new NotInterestedListState(new CatalogApi(client));
        var load = state.LoadAsync(); await started.Task; client.SetProfile("two"); finish.SetResult(); await load;
        Assert.Empty(state.Items); Assert.Null(state.Error); Assert.False(state.IsLoading);
    }

    [Fact]
    public async Task ActualCatalogAndDetailDeserializationRetainsNotInterestedAlongsideSavedState()
    {
        using var http = new HttpClient(new Wire(_ => Json("""{"items":[{"content_id":"movie:one","type":"movie","user_state":{"played":true,"is_favorite":true,"in_watchlist":true,"not_interested":true}}],"content_id":"movie:one","type":"movie","user_state":{"played":true,"is_favorite":true,"in_watchlist":true,"not_interested":true}}""")));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested.invalid");
        var api = new CatalogApi(client);
        var item = Assert.Single((await api.GetCatalogAsync(null)).Items);
        var state = JsonSerializer.SerializeToElement(item.UserState);
        Assert.True(state.TryGetProperty("NotInterested", out var flag), "Actual catalog state must expose the not_interested flag.");
        Assert.True(flag.GetBoolean());
        Assert.True(item.UserState!.Played && item.UserState.IsFavorite && item.UserState.InWatchlist);
        var detail = await api.GetItemDetailAsync("movie:one");
        Assert.True(JsonSerializer.SerializeToElement(detail.UserState).GetProperty("NotInterested").GetBoolean());
    }

    [Fact]
    public void ReconcilerPublishesFlagChangeWithoutReplacingUnaffectedPoster()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        MediaItem Read(bool marked) => JsonSerializer.Deserialize<MediaItem>("{\"content_id\":\"one\",\"type\":\"movie\",\"user_state\":{\"not_interested\":" + (marked ? "true" : "false") + "}}", options)!;
        var unaffected = new MediaItem { ContentId = "two", Type = "movie" };
        var items = new ObservableCollection<MediaItem>([Read(false), unaffected]);
        Assert.True(MediaItemCollectionReconciler.Apply(items, [Read(true), new MediaItem { ContentId = "two", Type = "movie" }]));
        Assert.Same(unaffected, items[1]);
    }

    internal static HttpResponseMessage Json(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(value, System.Text.Encoding.UTF8, "application/json") };
    internal sealed class Wire(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
    internal sealed class AsyncWire(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
