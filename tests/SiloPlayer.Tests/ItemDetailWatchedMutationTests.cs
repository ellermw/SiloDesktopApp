using System.Net;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class ItemDetailWatchedMutationTests
{
    [Theory]
    [InlineData("series", false)]
    [InlineData("series", true)]
    [InlineData("season", false)]
    [InlineData("season", true)]
    [InlineData("episode", false)]
    public async Task AcceptedToggleUpdatesRetainedDetailAndDescendants(string type, bool initiallyWatched)
    {
        using var scope = new Scope(type, initiallyWatched);
        var changes = new List<string?>();
        scope.Vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        var item = scope.Vm.Item;
        await scope.Vm.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.Same(item, scope.Vm.Item);
        Assert.Equal(!initiallyWatched, item!.UserData!.Played);
        Assert.Equal(!initiallyWatched, scope.Vm.Episodes[0].UserData!.Played);
        if (type == "series")
        {
            Assert.Equal(!initiallyWatched, scope.Vm.Seasons[0].UserData!.Played);
            Assert.Equal(initiallyWatched ? 0 : 2, scope.Vm.Seasons[0].UserData!.WatchedCount);
            Assert.Contains(nameof(scope.Vm.Seasons), changes);
        }
        Assert.Contains(nameof(scope.Vm.Item), changes);
        Assert.Contains(nameof(scope.Vm.Episodes), changes);
    }

    [Fact]
    public async Task FailedWriteRestoresHeaderAndPreservesDescendants()
    {
        using var scope = new Scope("series", false) { Status = HttpStatusCode.Forbidden };
        await scope.Vm.ToggleWatchedCommand.ExecuteAsync(null);
        Assert.False(scope.Vm.IsWatched);
        Assert.False(scope.Vm.Item!.UserData!.Played);
        Assert.False(scope.Vm.Seasons[0].UserData!.Played);
        Assert.False(scope.Vm.Episodes[0].UserData!.Played);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedWriteCannotChangeReplacementPageOrProfile(bool changeProfile)
    {
        using var scope = new Scope("series", false) { Delayed = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var operation = scope.Vm.ToggleWatchedCommand.ExecuteAsync(null);
        scope.Vm.Item = new MediaItemDetail { ContentId = "current", Type = "series", UserData = new() };
        scope.Vm.IsWatched = false;
        if (changeProfile) scope.Client.SetProfile("other");
        scope.Delayed.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await operation;
        Assert.False(scope.Vm.IsWatched);
        Assert.False(scope.Vm.Item.UserData.Played);
        Assert.False(scope.Vm.Episodes[0].UserData!.Played);
    }

    [Fact]
    public void ChildMessageUpdatesLoadedEpisodeAndSeasonCounts()
    {
        using var scope = new Scope("series", false);
        scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "episode", "current"));
        Assert.True(scope.Vm.Episodes[0].UserData!.Played);
        Assert.Equal(1, scope.Vm.Seasons[0].UserData!.WatchedCount);
        Assert.Equal(1, scope.Vm.Seasons[0].UserData!.UnplayedCount);
        Assert.False(scope.Vm.IsWatched);
        scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedCleared, "episode", "current"));
        Assert.False(scope.Vm.Episodes[0].UserData!.Played);
        Assert.Equal(0, scope.Vm.Seasons[0].UserData!.WatchedCount);
    }

    [Fact]
    public void AcceptedSeasonAndSeriesMessagesCascadeOnlyMatchingDescendants()
    {
        using var scope = new Scope("series", false);
        scope.Vm.Seasons.Add(new() { ContentId = "other-season", SeasonNumber = 2, EpisodeCount = 1, UserData = new() });
        scope.Vm.Episodes.Add(new() { ContentId = "other-episode", SeasonNumber = 2, UserData = new() });
        scope.Vm.Item!.EpisodeCount = 3;
        scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "season", "current"));
        Assert.True(scope.Vm.Episodes[0].UserData!.Played);
        Assert.False(scope.Vm.Episodes[1].UserData!.Played);
        Assert.True(scope.Vm.Seasons[0].UserData!.Played);
        Assert.False(scope.Vm.Seasons[1].UserData!.Played);
        Assert.Equal(2, scope.Vm.Item.UserData!.WatchedCount);
        Assert.False(scope.Vm.IsWatched);
        scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "current"));
        Assert.True(scope.Vm.Episodes[1].UserData!.Played);
        Assert.True(scope.Vm.IsWatched);
    }

    [Fact]
    public void OldProfileMessageCannotChangeCurrentDetail()
    {
        using var scope = new Scope("series", false);
        var old = scope.Client.CaptureContext();
        scope.Client.SetProfile("replacement");
        scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "current", context: old));
        Assert.False(scope.Vm.IsWatched);
        Assert.False(scope.Vm.Item!.UserData!.Played);
        Assert.False(scope.Vm.Episodes[0].UserData!.Played);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OlderWriteSuccessOrFailureCannotOverwriteNewerChange(bool fail)
    {
        using var scope = new Scope("series", false) { Delayed = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var operation = scope.Vm.ToggleWatchedCommand.ExecuteAsync(null);
        scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedCleared, "current"));
        scope.Delayed.SetResult(new HttpResponseMessage(fail ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent));
        await operation;
        Assert.False(scope.Vm.IsWatched);
        Assert.False(scope.Vm.Item!.UserData!.Played);
        Assert.False(scope.Vm.Episodes[0].UserData!.Played);
    }

    [Fact]
    public async Task PreMutationEpisodeReadIsRetriedBeforeItCanOverwriteAcceptedCascade()
    {
        using var scope = new Scope("series", false);
        var pendingRead = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        scope.Read = request => ++reads == 1 ? pendingRead.Task : Task.FromResult(Json("""
            {"items":[{"content_id":"episode","season_number":1,"user_data":{"played":true}}]}
            """));
        var load = scope.Vm.SelectSeasonCommand.ExecuteAsync(1);
        await scope.Vm.ToggleWatchedCommand.ExecuteAsync(null);
        pendingRead.SetResult(Json("""
            {"items":[{"content_id":"episode","season_number":1,"user_data":{"played":false}}]}
            """));
        await load;
        Assert.Equal(2, reads);
        Assert.True(Assert.Single(scope.Vm.Episodes).UserData!.Played);
        Assert.True(scope.Vm.IsWatched);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Scope : HttpMessageHandler
    {
        private readonly HttpClient _http;
        internal HttpStatusCode Status = HttpStatusCode.NoContent;
        internal TaskCompletionSource<HttpResponseMessage>? Delayed;
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Read;
        internal SiloApiClient Client { get; }
        internal ItemDetailViewModel Vm { get; }
        internal Scope(string type, bool watched)
        {
            _http = new HttpClient(this, disposeHandler: false);
            Client = new SiloApiClient(_http); Client.SetBaseUrl("https://watched-fixture.invalid");
            Vm = new ItemDetailViewModel(new CatalogApi(Client), new ItemDetailPrefetchCache((_, _) =>
                throw new InvalidOperationException("No full detail reload should be needed.")));
            Vm.Item = new MediaItemDetail { ContentId = "current", Type = type, EpisodeCount = 2,
                UserData = new() { Played = watched, WatchedCount = watched ? 2 : 0, UnplayedCount = watched ? 0 : 2 } };
            Vm.IsWatched = watched;
            Vm.Seasons.Add(new Season { ContentId = "season", SeasonNumber = 1, EpisodeCount = 2,
                UserData = new() { Played = watched, WatchedCount = watched ? 2 : 0, UnplayedCount = watched ? 0 : 2 } });
            Vm.Episodes.Add(new Episode { ContentId = type == "episode" ? "current" : "episode", SeasonNumber = 1,
                UserData = new() { Played = watched, PositionSeconds = 42, IsInProgress = true } });
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && Read != null) return Read(request);
            Assert.Contains("/api/v2/watched/", request.RequestUri!.AbsolutePath);
            Assert.True(request.Method == HttpMethod.Post || request.Method == HttpMethod.Delete);
            return Delayed?.Task ?? Task.FromResult(new HttpResponseMessage(Status));
        }
        protected override void Dispose(bool disposing)
        {
            WeakReferenceMessenger.Default.UnregisterAll(Vm);
            if (disposing) _http.Dispose();
            base.Dispose(disposing);
        }
    }
}
