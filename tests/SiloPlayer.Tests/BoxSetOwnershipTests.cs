using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;
namespace SiloPlayer.Tests;
public sealed class BoxSetOwnershipTests
{
    [Fact] public async Task MoviePublishesBeforeOptionalRowAndDepartureRetiresIt()
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire); var client = Client(http);
        var vm = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((id, _) => Task.FromResult(new MediaItemDetail { ContentId = id, Type = "movie", UserState = new() })));
        await vm.LoadInLibraryAsync("film", null).WaitAsync(TimeSpan.FromSeconds(2));
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("film", vm.Item!.ContentId); Assert.False(vm.IsLoading); Assert.Null(vm.BoxSet);
        vm.CancelPendingLoads(); wire.Release.TrySetResult(); await Task.Delay(25); Assert.Null(vm.BoxSet);
    }
    [Fact] public async Task DepartureCannotBeRevivedByQueuedRefresh()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var set = new BoxSetViewModel(api); await set.LoadAsync("7", "2"); set.Cancel();
        var reads = wire.DetailReads; await set.RefreshAsync(); Assert.Equal(reads, wire.DetailReads); Assert.Null(set.Detail);
    }
    [Fact] public async Task DepartureCannotBeRevivedByQueuedRefreshForMovie()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var movie = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        movie.Item = new MediaItemDetail { ContentId = "film", Type = "movie" }; await movie.RefreshBoxSetAsync();
        movie.CancelPendingLoads(); var reads = wire.DetailReads;
        await movie.RefreshBoxSetAsync(); Assert.Equal(reads, wire.DetailReads); Assert.Null(movie.BoxSet);
    }
    [Fact] public async Task PlaybackWriteArrivingDuringHeldMembershipReadMustRefreshAfterPersistence()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var movie = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        movie.Item = new MediaItemDetail { ContentId = "film", Type = "movie" }; await movie.RefreshBoxSetAsync();
        wire.Hold = true; wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshing = movie.RefreshBoxSetAsync(); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); Assert.Null(movie.BoxSet);
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        movie.Receive(new SiloPlayer.Messaging.PlaybackProgressUpdated("first", 100, 100, true) { Persisted = persisted.Task });
        wire.Hold = false; wire.Release.TrySetResult(); await refreshing;
        wire.AllPlayed = true; var reads = wire.DetailReads; persisted.TrySetResult();
        for (var i = 0; i < 100 && (wire.DetailReads == reads || movie.BoxSet?.PlayedCount != 2); i++) await Task.Delay(10);
        Assert.True(wire.DetailReads > reads, "A playback message received while the public row is cleared still owns a post-write refresh.");
        Assert.Equal(2, movie.BoxSet!.PlayedCount); movie.CancelPendingLoads();
    }
    [Fact] public async Task QueuedRefreshCannotSuppressPersistedPlaybackState()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var movie = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        movie.Item = new MediaItemDetail { ContentId = "film", Type = "movie" }; await movie.RefreshBoxSetAsync();
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        movie.Receive(new SiloPlayer.Messaging.PlaybackProgressUpdated("first", 100, 100, true) { Persisted = persisted.Task });
        await movie.RefreshBoxSetAsync(); var reads = wire.DetailReads; persisted.TrySetResult();
        for (var i = 0; i < 100 && wire.DetailReads == reads; i++) await Task.Delay(10);
        Assert.True(wire.DetailReads > reads, "A read after the final write must survive an earlier same-owner refresh."); movie.CancelPendingLoads();
    }
    [Theory] [InlineData(null, true)] [InlineData(false, true)] [InlineData(true, false)]
    public async Task UnsupportedOrDisabledFeatureNeverReadsMembership(bool? supported, bool enabled)
    {
        using var wire = new Wire { Supported = supported, Enabled = enabled }; using var http = new HttpClient(wire);
        var movie = new ItemDetailViewModel(new CatalogApi(Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        movie.Item = new MediaItemDetail { ContentId = "film", Type = "movie" }; await movie.RefreshBoxSetAsync();
        Assert.Null(movie.BoxSet); Assert.Equal(0, wire.DetailReads); movie.CancelPendingLoads();
    }
    [Fact] public async Task OptionalMembershipFailureKeepsOrdinaryMovieUsable()
    {
        using var wire = new Wire { DetailStatus = HttpStatusCode.ServiceUnavailable }; using var http = new HttpClient(wire);
        var movie = new ItemDetailViewModel(new CatalogApi(Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        movie.Item = new MediaItemDetail { ContentId = "film", Type = "movie" }; await movie.RefreshBoxSetAsync();
        Assert.Equal("film", movie.Item.ContentId); Assert.Null(movie.BoxSet); Assert.Null(movie.ErrorMessage); movie.CancelPendingLoads();
    }
    [Fact] public async Task DisabledLibraryRetiresAlreadyVisibleRow()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        vm.Item = new MediaItemDetail { ContentId = "film", Type = "movie" };
        await vm.RefreshBoxSetAsync(); Assert.Equal("12", vm.BoxSet!.LibraryId);
        var reads = wire.DetailReads; wire.Enabled = false;
        await vm.RefreshBoxSetAsync(); Assert.Null(vm.BoxSet); Assert.Equal(reads, wire.DetailReads);
        vm.CancelPendingLoads();
    }
    [Theory] [InlineData("profile")] [InlineData("language")] [InlineData("departure")] [InlineData("item")]
    public async Task MembershipCompletionHasOneOwner(string change)
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire); var client = Client(http);
        var vm = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        vm.Item = new MediaItemDetail { ContentId = "film", Type = "movie" };
        var task = vm.RefreshBoxSetAsync(); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (change == "profile") client.SetProfile("other");
        if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        if (change == "departure") vm.CancelPendingLoads();
        if (change == "item") vm.Item = new MediaItemDetail { ContentId = "other", Type = "movie" };
        wire.Release.TrySetResult(); await task; Assert.Null(vm.BoxSet); vm.CancelPendingLoads();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task SetActionSelectsNormalMovieWithResponseLibraryAndRetiresOnDeparture(bool allPlayed)
    {
        using var wire = new Wire { AllPlayed = allPlayed }; using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "2");
        var calls = 0;
        await vm.PlayAsync((id, library, owner, ct) => { calls++; Assert.Equal(allPlayed ? "first" : "second", id); Assert.Equal(12, library); Assert.True(client.IsCurrentContext(owner)); Assert.False(ct.IsCancellationRequested); return Task.CompletedTask; });
        Assert.Equal(1, calls); vm.Cancel();
        await vm.PlayAsync((_, _, _, _) => { calls++; return Task.CompletedTask; }); Assert.Equal(1, calls);
    }
    [Fact] public async Task DedicatedFailureAllowsRetryButUnavailableIsDistinct()
    {
        using var wire = new Wire { DetailStatus = HttpStatusCode.ServiceUnavailable }; using var http = new HttpClient(wire);
        var vm = new BoxSetViewModel(new CatalogApi(Client(http))); await vm.LoadAsync("7", "2"); Assert.Equal(DetailFailureKind.Transient, vm.Failure);
        wire.DetailStatus = HttpStatusCode.NotFound; await vm.RefreshAsync(); Assert.Equal(DetailFailureKind.NotFound, vm.Failure);
        wire.DetailStatus = HttpStatusCode.OK; await vm.RefreshAsync(); Assert.NotNull(vm.Detail); Assert.Equal(DetailFailureKind.None, vm.Failure); vm.Cancel();
    }
    private static SiloApiClient Client(HttpClient http) { var client = new SiloApiClient(http); client.SetBaseUrl("https://boxsets.invalid"); client.SetProfile("alpha"); return client; }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool? Supported = true;
        internal bool Hold, AllPlayed, Enabled = true; internal int DetailReads;
        internal HttpStatusCode DetailStatus = HttpStatusCode.OK;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = Supported.HasValue ? "{\"box_sets\":" + Supported.Value.ToString().ToLowerInvariant() + "}" : "{}"; var status = HttpStatusCode.OK;
            if (path.EndsWith("/user/libraries")) json = "{\"items\":[{\"id\":2,\"box_sets_enabled\":" + Enabled.ToString().ToLowerInvariant() + "}]}";
            else if (!path.EndsWith("/capabilities"))
            {
                DetailReads++; Started.TrySetResult(); if (Hold) await Release.Task;
                status = DetailStatus;
                var detail = "{\"id\":\"7\",\"library_id\":\"12\",\"movie_count\":2,\"played_count\":" + (AllPlayed ? "2" : "1") + ",\"items\":[{\"type\":\"movie\",\"content_id\":\"first\",\"user_state\":{\"played\":true}},{\"type\":\"movie\",\"content_id\":\"second\",\"user_state\":{\"played\":" + AllPlayed.ToString().ToLowerInvariant() + "}}]}";
                json = path.EndsWith("/box-set") ? "{\"box_set\":" + detail + "}" : detail;
            }
            return new(status) { Content = new StringContent(json) };
        }
    }
}
