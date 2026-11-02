using System.Net;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class RealtimeDetailStateTests
{
    [Theory]
    [InlineData("series", "watched")]
    [InlineData("season", "watched")]
    [InlineData("series", "progress")]
    [InlineData("season", "progress")]
    public async Task CapturedServerEnvelopeRefreshesOnlyTheNamedAggregate(string type, string change)
    {
        using var scope = new Scope(type);
        var messenger = new WeakReferenceMessenger();
        messenger.Register<ItemDetailViewModel, MediaStateInvalidated>(scope.Vm, static (vm, message) => vm.Receive(message));
        using var refresh = new RealtimeMediaStateRefresh(scope.Client, callback => callback(), messenger);
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(scope.Vm.Item)) applied.TrySetResult(); };
        using (var data = JsonDocument.Parse(JsonSerializer.Serialize(new { profile_id = "profile", content_id = "unloaded", series_id = "series", change, played = true, position_seconds = 900 })))
            refresh.ReceiveRealtime("user_state", "user_state.changed", data.RootElement);
        await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, scope.Vm.Item!.UserData!.WatchedCount);
        Assert.False(scope.Vm.Item.UserData.Played);
        Assert.False(scope.Vm.Episodes[0].UserData!.Played);
        Assert.Equal(31, scope.Vm.Episodes[0].UserData!.PositionSeconds);
        Assert.Equal(1, scope.DetailReads);
    }

    [Theory]
    [InlineData("series")]
    [InlineData("season")]
    public async Task TransientOwnedDetailFailureRetriesTheFullCatchUpScope(string type)
    {
        using var scope = new Scope(type)
        {
            IncludeTarget = true, FreshTarget = "retried-target",
            FreshViewerState = new() { IsFavorite = true }
        };
        scope.Vm.SelectedSeasonNumber = 1;
        var mounted = scope.Vm.Item;
        var attempts = 0;
        scope.OverrideResponse = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/api/v2/catalog/items/" + type || ++attempts != 1) return null;
            return new(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("""{"error":{"code":"service_unavailable","message":"Transient detail failure"}}""",
                    Encoding.UTF8, "application/json")
            };
        };

        await scope.Vm.RefreshMediaStateAsync(scope.Message("sibling") with { CatchUp = true })
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, attempts);
        Assert.Same(mounted, scope.Vm.Item);
        Assert.Equal(2, mounted!.UserData!.WatchedCount);
        Assert.Equal("retried-target", mounted.PlayContentId);
        Assert.True(scope.Vm.IsFavorite);
        Assert.Equal(1, scope.EpisodeReads);
        Assert.Equal(0, scope.Vm.Episodes[0].UserData!.PositionSeconds);
        if (type == "series") Assert.Equal(2, scope.Vm.Seasons[0].UserData!.WatchedCount);
    }

    [Fact]
    public async Task PersistentOwnedDetailFailureHasABoundedRetryAndAllowsLaterRefreshes()
    {
        using var scope = new Scope("series");
        var attempts = 0;
        scope.OverrideResponse = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/api/v2/catalog/items/series") return null;
            attempts++;
            return new(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("""{"error":{"code":"service_unavailable","message":"Persistent detail failure"}}""",
                    Encoding.UTF8, "application/json")
            };
        };

        await scope.Vm.RefreshMediaStateAsync(scope.Message()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, attempts);
        Assert.Equal(0, scope.Vm.Item!.UserData!.WatchedCount);
        scope.OverrideResponse = null;
        await scope.Vm.RefreshMediaStateAsync(scope.Message()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, scope.Vm.Item.UserData.WatchedCount);
    }

    [Fact]
    public async Task UnrelatedSeriesDoesNotReloadTheOpenDetail()
    {
        using var scope = new Scope("series");
        await scope.Vm.RefreshMediaStateAsync(scope.Message("other-episode", "other-series"));
        Assert.Equal(0, scope.DetailReads);
        Assert.Equal(0, scope.Vm.Item!.UserData!.WatchedCount);
    }

    [Theory]
    [InlineData("movie", "content")]
    [InlineData("episode", "content")]
    [InlineData("episode", "series")]
    [InlineData("episode", "loaded-sibling")]
    [InlineData("movie", "catch-up")]
    [InlineData("episode", "catch-up")]
    public async Task PlayableLeavesDoNotReceivePartialRetainedStateRefreshes(string type, string scope)
    {
        using var fixture = new Scope(type) { IncludeTarget = true, FreshTarget = "new-target" };
        var vm = fixture.Vm;
        var mounted = vm.Item!;
        mounted.SeriesId = type == "episode" ? "series" : null;
        mounted.PlayContentId = mounted.ContentId;
        var userData = mounted.UserData!;
        userData.PositionSeconds = 31;
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        var message = scope switch
        {
            "content" => fixture.Message(mounted.ContentId),
            "series" => fixture.Message("series"),
            "loaded-sibling" => fixture.Message("sibling"),
            _ => fixture.Message() with { CatchUp = true }
        };

        await vm.RefreshMediaStateAsync(message with { RefreshDetails = false });
        await vm.RefreshMediaStateAsync(message);

        Assert.Equal(0, fixture.DetailReads);
        Assert.Equal(0, fixture.EpisodeReads);
        Assert.Same(mounted, vm.Item);
        Assert.Same(userData, mounted.UserData);
        Assert.Equal(31, mounted.UserData!.PositionSeconds);
        Assert.Equal(mounted.ContentId, mounted.PlayContentId);
        Assert.Empty(notifications);
    }

    [Theory]
    [InlineData(true, "new-resume-target", "new-resume-target")]
    [InlineData(true, null, null)]
    [InlineData(false, null, "old-target")]
    public async Task RemoteProgressRefreshesTheAuthoritativeTargetAndPreservesAnOmittedLegacyTarget(bool supplied, string? target, string? expected)
    {
        using var scope = new Scope("series") { IncludeTarget = supplied, FreshTarget = target };
        scope.Vm.Item!.PlayContentId = "old-target";
        await scope.Vm.RefreshMediaStateAsync(scope.Message());
        Assert.Equal(expected, scope.Vm.Item.PlayContentId);
        Assert.Equal(31, scope.Vm.Episodes[0].UserData!.PositionSeconds);
        Assert.False(scope.Vm.Episodes[0].UserData!.Played);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("navigation")]
    [InlineData("local-watched")]
    public async Task RetiredReadCannotOverwriteCurrentState(string departure)
    {
        using var scope = new Scope("series") { BlockedRead = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var operation = scope.Vm.RefreshMediaStateAsync(scope.Message());
        await scope.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var retiredSnapshot = scope.FreshDetail("series", "series");
        if (departure == "profile") scope.Client.SetProfile("replacement");
        else if (departure == "navigation") scope.Vm.CancelPendingLoads();
        else
        {
            scope.ContainerPlayed = true; // The accepted local mutation is now on the server.
            scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "series"));
        }
        scope.BlockedRead.SetResult(retiredSnapshot);
        await operation;
        Assert.Equal(departure == "local-watched" ? 4 : 0, scope.Vm.Item!.UserData!.WatchedCount);
        Assert.Equal(departure == "local-watched" ? 2 : 1, scope.DetailReads);
    }

    [Fact]
    public async Task LocalEpisodeMarkRetriesAnActiveSweepAndIncludesTheRemoteEpisode()
    {
        using var scope = new Scope("series") { BlockedRead = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var operation = scope.Vm.RefreshMediaStateAsync(scope.Message());
        await scope.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "sibling", "series"));
        Assert.Equal(1, scope.Vm.Item!.UserData!.WatchedCount);
        var oldSnapshot = scope.FreshDetail("series", "series");
        oldSnapshot.UserData!.WatchedCount = 0;
        scope.BlockedRead.SetResult(oldSnapshot);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, scope.DetailReads);
        Assert.Equal(2, scope.Vm.Item.UserData.WatchedCount);
        Assert.Equal(2, scope.Vm.Seasons[0].UserData!.WatchedCount);
        Assert.True(scope.Vm.Episodes[0].UserData!.Played);
    }

    [Fact]
    public async Task NavigationCancelsTheRetiredOwnedReadWithoutWaitingForTheBackend()
    {
        using var scope = new Scope("series") { BlockedRead = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var operation = scope.Vm.RefreshMediaStateAsync(scope.Message());
        await scope.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scope.Vm.CancelPendingLoads();
        await Task.WhenAny(operation, Task.Delay(300));
        Assert.True(operation.IsCompleted, "Retired detail still waits for the backend response.");
        Assert.False(scope.BlockedRead.Task.IsCompleted);
    }

    [Fact]
    public async Task ImmediateInvalidationsDoNotRestartInitialEpisodeReadsBeforeTheCoalescedRefresh()
    {
        using var wire = new InitialEpisodeWire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://initial-realtime.invalid"); client.SetProfile("profile");
        var vm = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) =>
            throw new InvalidOperationException("Immediate invalidation must not fetch detail.")));
        vm.Item = new() { ContentId = "series", Type = "series", UserData = new() };
        try
        {
            var initialRead = vm.SelectSeasonCommand.ExecuteAsync(1);
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var n = 0; n < 10; n++) vm.Receive(new MediaStateInvalidated(client.CaptureContext(),
                new HashSet<string>(["sibling"]), new HashSet<string>(["series"]), RefreshDetails: false));
            wire.Release.TrySetResult();
            await initialRead;
            Assert.Equal(1, wire.Reads);
            Assert.Equal("sibling", Assert.Single(vm.Episodes).ContentId);
        }
        finally { vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(vm); }
    }

    [Fact]
    public async Task EventsDuringAReadAreSerializedIntoOneTrailingAuthoritativeRead()
    {
        using var scope = new Scope("series") { BlockedRead = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var operation = scope.Vm.RefreshMediaStateAsync(scope.Message());
        await scope.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = scope.Vm.RefreshMediaStateAsync(scope.Message("another"));
        var third = scope.Vm.RefreshMediaStateAsync(scope.Message("last"));
        Assert.Equal(1, scope.DetailReads);
        scope.BlockedRead.SetResult(scope.FreshDetail("series", "series"));
        await Task.WhenAll(operation, second, third);
        Assert.Equal(2, scope.DetailReads);
        Assert.Equal(2, scope.Vm.Item!.UserData!.WatchedCount);
    }

    [Fact]
    public async Task ContinuousRawTicksCannotStarveTheBoundedMountedRefresh()
    {
        using var scope = new Scope("series");
        // Each bounded sweep is slow enough for a later raw progress tick to
        // arrive. The view still needs an authoritative snapshot before the
        // next sweep, even when progress never goes quiet.
        for (var sweep = 0; sweep < 3; sweep++)
        {
            var read = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
            scope.OverrideDetail = () => read.Task;
            var count = scope.DetailReads;
            var operation = scope.Vm.RefreshMediaStateAsync(scope.Message());
            for (var i = 0; i < 100 && scope.DetailReads == count; i++) await Task.Delay(10);
            Assert.Equal(count + 1, scope.DetailReads);
            scope.Vm.Receive(scope.Message("later-progress") with { RefreshDetails = false });
            var snapshot = scope.FreshDetail("series", "series");
            snapshot.UserData!.WatchedCount = sweep + 1;
            read.SetResult(snapshot);
            await operation.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(sweep + 1, scope.Vm.Item!.UserData!.WatchedCount);
        }
    }

    [Fact]
    public async Task ReconnectCatchUpRefreshesTheOpenDetailWithoutReplacingIt()
    {
        using var scope = new Scope("series");
        var item = scope.Vm.Item;
        await scope.Vm.RefreshMediaStateAsync(scope.Message() with { ContentIds = new HashSet<string>(), SeriesIds = new HashSet<string>(), CatchUp = true });
        Assert.Same(item, scope.Vm.Item);
        Assert.Equal(2, item!.UserData!.WatchedCount);
        Assert.Equal(2, scope.Vm.Seasons[0].UserData!.WatchedCount);
    }

    [Theory]
    [InlineData("series", false)]
    [InlineData("series", true)]
    [InlineData("season", false)]
    [InlineData("season", true)]
    public async Task AuthoritativeTotalsStayConsistentWithRemoteCountsAndLaterLocalWatchedFeedback(string type, bool catchUp)
    {
        using var scope = new Scope(type);
        var item = scope.Vm.Item!;
        var total = type == "series" ? 20 : 12;
        item.EpisodeCount = total - 2;
        item.SeasonCount = 2;
        scope.Vm.Seasons[0].EpisodeCount = 10;
        var season = scope.Vm.Seasons[0];
        scope.OverrideResponse = request =>
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("/seasons")
                ? """{"items":[{"content_id":"season","season_number":1,"episode_count":12,"user_data":{"watched_count":4,"unplayed_count":8}}]}"""
                : request.RequestUri.AbsolutePath.EndsWith("/episodes")
                    ? """{"items":[{"content_id":"sibling","season_number":1,"user_data":{"played":false}}]}"""
                    : JsonSerializer.Serialize(new { content_id = item.ContentId, type, episode_count = total, season_count = 3,
                        user_data = new { watched_count = 4, unplayed_count = total - 4 } });
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        };

        await scope.Vm.RefreshMediaStateAsync(scope.Message() with { CatchUp = catchUp });

        Assert.Same(item, scope.Vm.Item);
        Assert.Equal(total, item.EpisodeCount);
        Assert.Equal(3, item.SeasonCount);
        Assert.Equal(total - 4, item.UserData!.UnplayedCount);
        if (type == "series")
        {
            Assert.Same(season, Assert.Single(scope.Vm.Seasons));
            Assert.Equal(12, season.EpisodeCount);
            Assert.Equal(8, season.UserData!.UnplayedCount);
        }
        Assert.False(scope.Vm.Episodes[0].UserData!.Played);

        scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "sibling", "series"));

        Assert.Equal(5, item.UserData.WatchedCount);
        Assert.Equal(total - 5, item.UserData.UnplayedCount);
        if (type == "series")
        {
            Assert.Equal(5, season.UserData!.WatchedCount);
            Assert.Equal(7, season.UserData.UnplayedCount);
        }
        scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, item.ContentId));
        Assert.Equal(total, item.UserData.WatchedCount);
        Assert.Equal(0, item.UserData.UnplayedCount);
    }

    [Fact]
    public async Task SeasonsCompanionReconcilesMembershipAndCountsEvenAfterTheLoadedSetBecomesEmpty()
    {
        using var scope = new Scope("series");
        var retained = scope.Vm.Seasons[0];
        scope.Vm.Seasons.Add(new() { ContentId = "removed", SeasonNumber = 2, EpisodeCount = 8 });
        var body = """
            {"items":[{"content_id":"added","season_number":0,"episode_count":2},
                      {"content_id":"season","season_number":1,"episode_count":12,"user_data":{"watched_count":4,"unplayed_count":8}}]}
            """;
        scope.OverrideResponse = request => request.RequestUri!.AbsolutePath.EndsWith("/seasons")
            ? new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") } : null;

        await scope.Vm.RefreshMediaStateAsync(scope.Message());

        Assert.Equal(new[] { "added", "season" }, scope.Vm.Seasons.Select(s => s.ContentId));
        Assert.Equal(2, scope.Vm.Seasons[0].EpisodeCount);
        Assert.Same(retained, scope.Vm.Seasons[1]);
        Assert.Equal(12, retained.EpisodeCount);
        Assert.Equal(4, retained.UserData!.WatchedCount);

        body = """{"items":[]}""";
        await scope.Vm.RefreshMediaStateAsync(scope.Message());
        Assert.Empty(scope.Vm.Seasons);

        body = """{"items":[{"content_id":"returned","season_number":3,"episode_count":5}]}""";
        await scope.Vm.RefreshMediaStateAsync(scope.Message());
        Assert.Equal("returned", Assert.Single(scope.Vm.Seasons).ContentId);
        Assert.Equal(5, scope.Vm.Seasons[0].EpisodeCount);
    }

    [Fact]
    public async Task RefreshPreservesTheLibraryScopeForBothDetailAndSeasonReads()
    {
        using var wire = new ScopedWire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://scoped-realtime.invalid"); client.SetProfile("profile");
        var vm = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) =>
            throw new InvalidOperationException("A library-scoped detail must not use default-scope prefetch.")));
        try
        {
            await vm.LoadInLibraryAsync("scoped-series", 9);
            vm.Seasons.Add(new() { ContentId = "season", SeasonNumber = 1, EpisodeCount = 4, UserData = new() });
            await vm.RefreshMediaStateAsync(new(client.CaptureContext(), new HashSet<string>(["unknown"]), new HashSet<string>(["scoped-series"])));
            Assert.Equal(2, vm.Item!.UserData!.WatchedCount);
            Assert.Equal(2, vm.Seasons[0].UserData!.WatchedCount);
            Assert.Equal(3, wire.Requests.Count);
            Assert.All(wire.Requests, uri => Assert.Equal("?library_id=9", uri.Query));
        }
        finally { vm.CancelPendingLoads(); WeakReferenceMessenger.Default.UnregisterAll(vm); }
    }

    [Theory]
    [InlineData("series")]
    [InlineData("season")]
    public async Task UnknownEpisodeProgressRefreshesNamedAggregateWithoutApplyingASiblingScalar(string type)
    {
        using var scope = new Scope(type);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Vm.PropertyChanged += (_, e) =>
        {
            // Item is deliberately notified before the independent season read.
            // Wait for both surfaces, not a fixed delay or the earlier notification.
            if (e.PropertyName is nameof(scope.Vm.Item) or nameof(scope.Vm.Seasons)
                && scope.Vm.Item!.UserData!.WatchedCount == 2
                && (type != "series" || scope.Vm.Seasons[0].UserData!.WatchedCount == 2)) changed.TrySetResult();
        };
        scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.PlaybackProgress, "never-loaded", "series",
            context: scope.Client.CaptureContext()));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, scope.Vm.Item!.UserData!.WatchedCount);
        Assert.False(scope.Vm.Episodes[0].UserData!.Played);
        Assert.Equal(31, scope.Vm.Episodes[0].UserData!.PositionSeconds);
        Assert.Equal(1, scope.DetailReads);
        if (type == "series") Assert.Equal(2, scope.Vm.Seasons[0].UserData!.WatchedCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SeriesContainerChangeRefreshesMountedSeasonEpisodeRows(bool played)
    {
        using var scope = new Scope("season") { ContainerPlayed = played };
        var item = scope.Vm.Item!;
        var episode = scope.Vm.Episodes[0];
        item.UserData!.Played = !played;
        episode.UserData!.Played = !played;

        await scope.Vm.RefreshMediaStateAsync(scope.Message("series"));

        Assert.Same(item, scope.Vm.Item);
        Assert.Same(episode, scope.Vm.Episodes[0]);
        Assert.Equal(played, scope.Vm.IsWatched);
        Assert.Equal(played, episode.UserData!.Played);
        Assert.Equal(0, episode.UserData.PositionSeconds);
        Assert.Equal(1, scope.EpisodeReads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3)]
    public async Task ReconnectRefreshesRetainedViewerStateRatingAndSimilarItems(int? rating)
    {
        using var scope = new Scope("series") { FreshRating = rating };
        var item = scope.Vm.Item!;
        scope.Vm.UserRating = 5;
        await scope.Vm.LoadSimilarCommand.ExecuteAsync(null);
        Assert.Single(scope.Vm.SimilarItems);
        scope.FreshViewerState = new() { IsFavorite = true, InWatchlist = true, NotInterested = true };
        scope.HideSimilar = true;

        await scope.Vm.RefreshMediaStateAsync(scope.Message() with { CatchUp = true });

        Assert.Same(item, scope.Vm.Item);
        Assert.True(scope.Vm.IsFavorite);
        Assert.True(scope.Vm.InWatchlist);
        Assert.True(item.UserState!.NotInterested);
        Assert.Equal(rating, scope.Vm.UserRating);
        Assert.Equal(rating, item.UserRating);
        Assert.Empty(scope.Vm.SimilarItems);
    }

    [Theory]
    [InlineData("series", null)]
    [InlineData("season", null)]
    [InlineData("series", 5)]
    [InlineData("season", 5)]
    public async Task ReconnectWithoutAnInlinedRatingPreservesTheMountedRating(string type, int? itemRating)
    {
        using var scope = new Scope(type);
        var item = scope.Vm.Item!;
        item.UserRating = itemRating;
        scope.Vm.UserRating = 5; // May have been loaded from the legacy rating endpoint.
        scope.OverrideResponse = request => request.RequestUri!.AbsolutePath == "/api/v2/catalog/items/" + item.ContentId
            ? new(HttpStatusCode.OK) { Content = new StringContent("{\"content_id\":\"" + item.ContentId + "\",\"user_state\":{}}",
                Encoding.UTF8, "application/json") } : null;

        await scope.Vm.RefreshMediaStateAsync(scope.Message() with { CatchUp = true });

        Assert.Same(item, scope.Vm.Item);
        Assert.Equal(itemRating, item.UserRating);
        Assert.Equal(5, scope.Vm.UserRating);
    }

    [Fact]
    public async Task LocalProgressBurstUsesABoundedAuthoritativeRefresh()
    {
        using var scope = new Scope("series");
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(scope.Vm.Item)) applied.TrySetResult();
        };
        for (var n = 0; n < 30; n++)
            scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.PlaybackProgress,
                "never-loaded-" + n, "series", context: scope.Client.CaptureContext()));

        Assert.Equal(0, scope.DetailReads);
        await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, scope.DetailReads);
        Assert.Equal(2, scope.Vm.Item!.UserData!.WatchedCount);
        Assert.Equal(31, scope.Vm.Episodes[0].UserData!.PositionSeconds);
    }

    [Fact]
    public async Task BackgroundSendDuringOwnedReadIsIncludedInTheAwaitedTrailingRefresh()
    {
        using var scope = new Scope("series") { BlockedRead = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        scope.Vm.SelectedSeasonNumber = 1;
        var operation = scope.Vm.RefreshMediaStateAsync(scope.Message());
        await scope.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var message = scope.Message("sibling");
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trailing = Task.Run(() =>
        {
            var refresh = scope.Vm.RefreshMediaStateAsync(message);
            queued.TrySetResult();
            return refresh;
        });
        await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scope.BlockedRead.SetResult(scope.FreshDetail("series", "series"));
        await Task.WhenAll(operation, trailing).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, scope.DetailReads);
        Assert.Equal(1, scope.EpisodeReads);
        Assert.Equal(0, scope.Vm.Episodes[0].UserData!.PositionSeconds);
    }

    [Theory]
    [InlineData("series", false)]
    [InlineData("series", true)]
    [InlineData("season", false)]
    [InlineData("season", true)]
    public async Task MissingRemoteUserDataPreservesMountedDetailAndCompanionState(string type, bool explicitNull)
    {
        using var scope = new Scope(type);
        var item = scope.Vm.Item!;
        item.UserData!.Played = true;
        item.UserData.PositionSeconds = 75;
        scope.Vm.IsWatched = true;
        scope.Vm.SelectedSeasonNumber = 1;
        var detailData = item.UserData;
        var seasonData = scope.Vm.Seasons[0].UserData;
        var episodeData = scope.Vm.Episodes[0].UserData;
        var reads = new List<string>();
        scope.OverrideResponse = request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            reads.Add(path);
            var data = explicitNull ? ",\"user_data\":null" : "";
            var body = path.EndsWith("/episodes")
                ? "{\"items\":[{\"content_id\":\"sibling\"" + data + "}]}"
                : path.EndsWith("/seasons")
                    ? "{\"items\":[{\"content_id\":\"season\"" + data + "}]}"
                    : "{\"content_id\":\"" + item.ContentId + "\"" + data + "}";
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        };

        await scope.Vm.RefreshMediaStateAsync(scope.Message() with { CatchUp = true });

        Assert.Equal(type == "series" ? 3 : 2, reads.Count);
        Assert.True(scope.Vm.IsWatched);
        Assert.Same(detailData, item.UserData);
        Assert.Equal(4, item.EpisodeCount);
        Assert.Equal(75, item.UserData!.PositionSeconds);
        Assert.Same(seasonData, scope.Vm.Seasons[0].UserData);
        Assert.Equal(4, seasonData!.UnplayedCount);
        Assert.Same(episodeData, scope.Vm.Episodes[0].UserData);
        Assert.Equal(31, episodeData!.PositionSeconds);
    }

    [Theory]
    [InlineData("series", "seasons", false)]
    [InlineData("series", "episodes", false)]
    [InlineData("series", "episodes", true)]
    [InlineData("season", "episodes", false)]
    public async Task CompanionFailureKeepsAuthoritativeDetailAndOtherSuccessfulCompanions(
        string type, string failedCompanion, bool canonicalSeason)
    {
        using var scope = new Scope(type) { IncludeTarget = true, FreshTarget = "new-target", ContainerPlayed = true };
        var item = scope.Vm.Item!;
        item.PlayContentId = "old-target";
        scope.Vm.SelectedSeasonNumber = 1;
        if (canonicalSeason) scope.Vm.EpisodesSeasonContentId = "season";
        var seasonData = scope.Vm.Seasons[0].UserData;
        var episodeData = scope.Vm.Episodes[0].UserData;
        var detailNotified = false;
        var failed = false;
        scope.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(scope.Vm.Item)) detailNotified = true;
        };
        scope.OverrideResponse = request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/" + failedCompanion)) return null;
            failed = true;
            Assert.True(detailNotified);
            Assert.True(scope.Vm.IsWatched);
            Assert.Equal(4, item.UserData!.WatchedCount);
            Assert.Equal("new-target", item.PlayContentId);
            return new(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("""{"error":{"code":"service_unavailable","message":"Synthetic companion failure"}}""",
                    Encoding.UTF8, "application/json")
            };
        };

        await scope.Vm.RefreshMediaStateAsync(scope.Message() with { CatchUp = true });

        Assert.True(failed);
        Assert.True(detailNotified);
        Assert.Same(item, scope.Vm.Item);
        Assert.True(scope.Vm.IsWatched);
        Assert.True(item.UserData!.Played);
        Assert.Equal(4, item.UserData.WatchedCount);
        Assert.Equal("new-target", item.PlayContentId);
        Assert.Equal(2, item.PlaySeasonNumber);
        if (failedCompanion == "seasons")
        {
            Assert.Same(seasonData, scope.Vm.Seasons[0].UserData);
            Assert.Equal(4, seasonData!.UnplayedCount);
            Assert.Equal(1, scope.EpisodeReads);
            Assert.True(scope.Vm.Episodes[0].UserData!.Played);
            Assert.Equal(0, scope.Vm.Episodes[0].UserData!.PositionSeconds);
        }
        else
        {
            Assert.Same(episodeData, scope.Vm.Episodes[0].UserData);
            Assert.False(episodeData!.Played);
            Assert.Equal(31, episodeData.PositionSeconds);
            if (type == "series") Assert.Equal(2, scope.Vm.Seasons[0].UserData!.WatchedCount);
        }
    }

    [Fact]
    public async Task SingleSeasonSpecialsRefreshUsesTheCanonicalSourceOfTheVisibleRows()
    {
        using var scope = new Scope("series");
        scope.Vm.SelectedSeasonNumber = 0;
        scope.Vm.EpisodesSeasonContentId = "special-season";
        scope.Vm.Seasons[0].ContentId = "special-season";
        scope.Vm.Seasons[0].SeasonNumber = 0;
        var episode = scope.Vm.Episodes[0];
        episode.SeasonNumber = 0;
        string? episodePath = null;
        scope.OverrideResponse = request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/episodes")) return null;
            episodePath = request.RequestUri.AbsolutePath;
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"items":[{"content_id":"sibling","season_number":0,"user_data":{"played":true,"position_seconds":90}}]}
                """, Encoding.UTF8, "application/json") };
        };

        await scope.Vm.RefreshMediaStateAsync(scope.Message("sibling"));

        Assert.Equal("/api/v2/catalog/items/special-season/episodes", episodePath);
        Assert.Same(episode, Assert.Single(scope.Vm.Episodes));
        Assert.True(episode.UserData!.Played);
        Assert.Equal(90, episode.UserData.PositionSeconds);
    }

    private sealed class Scope : HttpMessageHandler
    {
        private readonly HttpClient _http;
        private readonly AuthService _auth;
        private readonly ItemDetailPrefetchCache _cache;
        private readonly string _type;
        internal SiloApiClient Client { get; }
        internal ItemDetailViewModel Vm { get; }
        internal int DetailReads;
        internal int EpisodeReads;
        internal bool? ContainerPlayed;
        internal MediaItemUserState? FreshViewerState;
        internal int? FreshRating;
        internal bool HideSimilar;
        internal Func<Task<MediaItemDetail>>? OverrideDetail;
        internal Func<HttpRequestMessage, HttpResponseMessage?>? OverrideResponse;
        internal TaskCompletionSource<MediaItemDetail>? BlockedRead;
        internal readonly TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool IncludeTarget;
        internal string? FreshTarget;
        internal MediaStateInvalidated Message(string id = "unknown", string series = "series") =>
            new(Client.CaptureContext(), new HashSet<string>([id]), new HashSet<string>([series]));
        internal MediaItemDetail FreshDetail(string id, string type)
        {
            var detail = new MediaItemDetail { ContentId = id, Type = type, UserState = FreshViewerState, UserRating = FreshRating,
                UserData = new() { Played = ContainerPlayed ?? false,
                    WatchedCount = ContainerPlayed.HasValue ? (ContainerPlayed.Value ? 4 : 0) : 2,
                    UnplayedCount = ContainerPlayed.HasValue ? (ContainerPlayed.Value ? 0 : 4) : 2, InProgressCount = 1 } };
            if (IncludeTarget) { detail.PlayContentId = FreshTarget; detail.PlaySeasonNumber = 2; }
            return detail;
        }
        internal Scope(string type)
        {
            _http = new HttpClient(this, false);
            Client = new SiloApiClient(_http); Client.SetBaseUrl("https://realtime-detail.invalid"); Client.SetProfile("profile");
            _type = type;
            _auth = new(Client, new AuthApi(Client));
            var catalog = new CatalogApi(Client);
            _cache = new(catalog, _auth);
            DetailCacheInvalidation.Attach(_cache);
            Vm = new ItemDetailViewModel(catalog, _cache);
            Vm.Item = new() { ContentId = type == "series" ? "series" : "season", SeriesId = type == "season" ? "series" : null,
                Type = type, EpisodeCount = 4, UserData = new() { UnplayedCount = 4 } };
            Vm.Seasons.Add(new() { ContentId = "season", SeasonNumber = 1, EpisodeCount = 4, UserData = new() { UnplayedCount = 4 } });
            Vm.Episodes.Add(new() { ContentId = "sibling", SeasonNumber = 1, UserData = new() { PositionSeconds = 31 } });
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (OverrideResponse?.Invoke(request) is { } response) return response;
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/api/v2/recommendations/similar/"))
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                {
                    items = new[] { new { content_id = "similar", user_state = new { not_interested = HideSimilar } } }
                }), Encoding.UTF8, "application/json") };
            if (path.EndsWith("/episodes"))
            {
                EpisodeReads++;
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                {
                    items = new[] { new { content_id = "sibling", season_number = 1,
                        user_data = new { played = ContainerPlayed ?? false, position_seconds = 0 } } }
                }), Encoding.UTF8, "application/json") };
            }
            if (path.StartsWith("/api/v2/catalog/items/"))
            {
                DetailReads++;
                ReadStarted.TrySetResult();
                var detail = OverrideDetail is { } read ? await read().WaitAsync(cancellationToken)
                    : DetailReads == 1 && BlockedRead != null ? await BlockedRead.Task.WaitAsync(cancellationToken)
                    : FreshDetail(request.RequestUri.AbsolutePath.Split('/').Last(), _type);
                var body = new Dictionary<string, object?>
                {
                    ["content_id"] = detail.ContentId, ["type"] = detail.Type,
                    ["user_state"] = detail.UserState == null ? null : new { is_favorite = detail.UserState.IsFavorite,
                        in_watchlist = detail.UserState.InWatchlist, not_interested = detail.UserState.NotInterested },
                    ["user_rating"] = detail.UserRating,
                    ["user_data"] = new { watched_count = detail.UserData?.WatchedCount, unplayed_count = detail.UserData?.UnplayedCount,
                        in_progress_count = detail.UserData?.InProgressCount, played = detail.UserData?.Played }
                };
                if (detail.HasAuthoritativePlayTarget) { body["play_content_id"] = detail.PlayContentId; body["play_season_number"] = detail.PlaySeasonNumber; }
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            }
            Assert.EndsWith("/series/series/seasons", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"items":[{"content_id":"season","season_number":1,"episode_count":4,"user_data":{"watched_count":2,"unplayed_count":2,"in_progress_count":1}}]}
                """, Encoding.UTF8, "application/json") };
        }
        protected override void Dispose(bool disposing)
        {
            Vm.CancelPendingLoads();
            WeakReferenceMessenger.Default.UnregisterAll(Vm);
            WeakReferenceMessenger.Default.UnregisterAll(_cache);
            if (disposing) { _auth.Dispose(); _http.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private sealed class ScopedWire : HttpMessageHandler
    {
        internal readonly List<Uri> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            var body = request.RequestUri!.AbsolutePath.EndsWith("/seasons")
                ? """{"items":[{"content_id":"season","season_number":1,"episode_count":4,"user_data":{"watched_count":2}}]}"""
                : """{"content_id":"scoped-series","type":"series","episode_count":4,"user_state":{},"user_data":{"watched_count":2}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class InitialEpisodeWire : HttpMessageHandler
    {
        internal int Reads;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.EndsWith("/seasons/1/episodes", request.RequestUri!.AbsolutePath);
            Reads++; Started.TrySetResult(); await Release.Task.WaitAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"items":[{"content_id":"sibling","season_number":1,"user_data":{"played":false}}]}""", Encoding.UTF8, "application/json") };
        }
    }
}
