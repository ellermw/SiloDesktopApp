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
    public async Task DelayedPostPlaybackReadCannotOverwriteANewerRealtimeSnapshot(string type)
    {
        using var scope = new Scope(type)
        {
            IncludeTarget = true, FreshTarget = "newer-episode",
            BlockedRead = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var mounted = scope.Vm.Item!;
        var playback = scope.Vm.RefreshWatchedStateAsync(Task.CompletedTask, CancellationToken.None);
        await scope.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await scope.Vm.RefreshMediaStateAsync(scope.Message()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, mounted.UserData!.WatchedCount);
        Assert.Equal("newer-episode", mounted.PlayContentId);
        var newerData = mounted.UserData;
        var newerSeasonData = scope.Vm.Seasons[0].UserData;

        scope.BlockedRead.SetResult(new MediaItemDetail
        {
            ContentId = mounted.ContentId, Type = type, PlayContentId = "finished-episode",
            PlaySeasonNumber = 1, UserData = new() { WatchedCount = 1, UnplayedCount = 3 }
        });
        await playback.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(mounted, scope.Vm.Item);
        Assert.Same(newerData, mounted.UserData);
        Assert.Same(newerSeasonData, scope.Vm.Seasons[0].UserData);
        Assert.Equal(2, mounted.UserData.WatchedCount);
        Assert.Equal("newer-episode", mounted.PlayContentId);
        Assert.Equal(2, mounted.PlaySeasonNumber);
        Assert.Equal(2, scope.DetailReads);
    }

    [Theory]
    [InlineData("series", true, "next-episode")]
    [InlineData("series", true, null)]
    [InlineData("series", false, null)]
    [InlineData("season", true, "next-episode")]
    [InlineData("season", true, null)]
    [InlineData("season", false, null)]
    public async Task LocalFinalSaveRefreshesTheMountedPlayActionAndLoadedSeasonCounts(
        string type, bool includeTarget, string? nextTarget)
    {
        using var scope = new Scope(type) { IncludeTarget = includeTarget, FreshTarget = nextTarget };
        var item = scope.Vm.Item!;
        item.PlayContentId = "finished-episode"; item.PlaySeasonNumber = 1;
        item.UserData!.InProgressCount = 1;
        scope.Vm.SelectedSeasonNumber = 1;
        scope.OverrideDetail = () =>
        {
            var detail = scope.FreshDetail(item.ContentId, type);
            detail.UserData!.InProgressCount = 0;
            return Task.FromResult(detail);
        };
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Vm.Receive(new PlaybackProgressUpdated("finished-episode", 999, 1000, true)
            { SeriesId = "series", Persisted = save.Task });
        var changes = new List<string?>();
        SeriesPrimaryActionResolver.DetailAction? paintedAction = null;
        scope.Vm.PropertyChanged += (_, e) =>
        {
            changes.Add(e.PropertyName);
            if (e.PropertyName == nameof(scope.Vm.Item))
                paintedAction = SeriesPrimaryActionResolver.ResolveDetail(scope.Vm.Item!);
        };

        var refresh = scope.Vm.RefreshWatchedStateAsync(save.Task, CancellationToken.None);
        Assert.False(refresh.IsCompleted);
        Assert.Equal(0, scope.DetailReads);
        save.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(item, scope.Vm.Item);
        Assert.Equal(includeTarget ? nextTarget : "finished-episode", item.PlayContentId);
        Assert.Equal(includeTarget ? 2 : 1, item.PlaySeasonNumber);
        Assert.Equal(1, scope.DetailReads);
        Assert.Contains(nameof(scope.Vm.Item), changes);
        if (type == "series")
        {
            Assert.NotNull(paintedAction);
            Assert.Equal(item.PlayContentId, paintedAction.ContentId);
            Assert.Equal(item.PlayContentId == null ? "Browse Series" : "Play Next", paintedAction.Label);
            Assert.Equal(2, scope.Vm.Seasons[0].UserData!.WatchedCount);
            Assert.Contains(nameof(scope.Vm.Seasons), changes);
        }
        Assert.False(scope.Vm.IsLoading);
    }

    [Theory]
    [InlineData("series", false, null)]
    [InlineData("series", true, null)]
    [InlineData("series", true, "new-target")]
    [InlineData("season", false, null)]
    [InlineData("season", true, null)]
    [InlineData("season", true, "new-target")]
    public async Task PostPlaybackParentRemapRetiresOldRowsAndTargetAndNotifiesThePage(
        string type, bool includeTarget, string? target)
    {
        using var scope = new Scope(type);
        var vm = scope.Vm;
        var item = vm.Item!;
        item.SeriesId = "old-series"; item.SeriesTitle = "Old series";
        item.PlayContentId = "old-target"; item.PlaySeasonNumber = 9;
        var parentRevision = vm.ParentPlaybackRevision;
        vm.EpisodesSeasonContentId = "old-season";
        scope.OverrideDetail = () =>
        {
            var refreshed = new MediaItemDetail
            {
                ContentId = item.ContentId, Type = type, SeriesId = "new-series", SeriesTitle = "New series",
                UserData = new() { WatchedCount = 1, UnplayedCount = 1 }
            };
            if (includeTarget) { refreshed.PlayContentId = target; refreshed.PlaySeasonNumber = 2; }
            return Task.FromResult(refreshed);
        };
        var reloadNotified = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Episodes)) reloadNotified = true;
        };

        await vm.RefreshWatchedStateAsync(Task.CompletedTask, CancellationToken.None);

        Assert.True(reloadNotified);
        Assert.NotEqual(parentRevision, vm.ParentPlaybackRevision);
        Assert.Same(item, vm.Item);
        Assert.Equal("new-series", item.SeriesId);
        Assert.Equal("New series", item.SeriesTitle);
        Assert.Equal(includeTarget, item.HasAuthoritativePlayTarget);
        Assert.Equal(target, item.PlayContentId);
        Assert.Equal(includeTarget ? (int?)2 : null, item.PlaySeasonNumber);
        Assert.Null(vm.EpisodesSeasonContentId);
        Assert.Empty(vm.Episodes);
        Assert.Equal(1, item.UserData!.WatchedCount);
    }

    [Theory]
    [InlineData("series")]
    [InlineData("season")]
    public async Task PostPlaybackLegacyResponsePreservesKnownParentRowsAndTarget(string type)
    {
        using var scope = new Scope(type);
        var vm = scope.Vm;
        var item = vm.Item!;
        item.SeriesId = "known-series"; item.SeriesTitle = "Known series";
        item.PlayContentId = "known-target"; item.PlaySeasonNumber = 1;
        vm.EpisodesSeasonContentId = "known-season";
        var parentRevision = vm.ParentPlaybackRevision;
        var row = Assert.Single(vm.Episodes);

        await vm.RefreshWatchedStateAsync(Task.CompletedTask, CancellationToken.None);

        Assert.Same(item, vm.Item);
        Assert.Equal("known-series", item.SeriesId);
        Assert.Equal("Known series", item.SeriesTitle);
        Assert.True(item.HasAuthoritativePlayTarget);
        Assert.Equal("known-target", item.PlayContentId);
        Assert.Equal(1, item.PlaySeasonNumber);
        Assert.Equal("known-season", vm.EpisodesSeasonContentId);
        Assert.Equal(parentRevision, vm.ParentPlaybackRevision);
        Assert.Same(row, Assert.Single(vm.Episodes));
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

    [Theory]
    [InlineData("series")]
    [InlineData("season")]
    public async Task CatchUpRetainsViewerAndCompanionScopeThroughARestOutage(string type)
    {
        using var scope = new Scope(type)
        {
            IncludeTarget = true, FreshTarget = "recovered-target",
            FreshViewerState = new() { IsFavorite = true, InWatchlist = true }, FreshRating = 4
        };
        scope.Vm.SelectedSeasonNumber = 1;
        var item = scope.Vm.Item;
        var detailAttempts = 0;
        var companionAttempts = 0;
        scope.OverrideResponse = request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/catalog/items/" + type && ++detailAttempts <= 2)
                return Unavailable();
            if (path.EndsWith(type == "series" ? "/seasons" : "/episodes") && ++companionAttempts <= 2)
                return Unavailable();
            return null;
        };

        await scope.Vm.RefreshMediaStateAsync(scope.Message() with { CatchUp = true })
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, detailAttempts);
        Assert.Equal(3, companionAttempts);
        Assert.Same(item, scope.Vm.Item);
        Assert.Equal(2, item!.UserData!.WatchedCount);
        Assert.Equal("recovered-target", item.PlayContentId);
        Assert.True(scope.Vm.IsFavorite);
        Assert.True(scope.Vm.InWatchlist);
        Assert.Equal(4, scope.Vm.UserRating);
        Assert.Equal(0, scope.Vm.Episodes[0].UserData!.PositionSeconds);
        if (type == "series") Assert.Equal(2, scope.Vm.Seasons[0].UserData!.WatchedCount);
        Assert.Null(scope.Vm.ErrorMessage);
        Assert.Equal(DetailFailureKind.None, scope.Vm.FailureKind);
    }

    [Theory]
    [InlineData("series", "seasons", false)]
    [InlineData("series", "episodes", false)]
    [InlineData("series", "episodes", true)]
    [InlineData("season", "episodes", false)]
    public async Task TransientCompanionFailureRetriesWithoutWaitingForAnotherEvent(
        string type, string companion, bool canonicalSeason)
    {
        using var scope = new Scope(type);
        scope.Vm.SelectedSeasonNumber = 1;
        if (canonicalSeason) scope.Vm.EpisodesSeasonContentId = "season";
        var attempts = 0;
        scope.OverrideResponse = request => request.RequestUri!.AbsolutePath.EndsWith("/" + companion)
            && ++attempts == 1 ? Unavailable() : null;

        await scope.Vm.RefreshMediaStateAsync(scope.Message("sibling"))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, attempts);
        Assert.Equal(1, scope.DetailReads);
        Assert.Equal(2, scope.Vm.Item!.UserData!.WatchedCount);
        if (type == "series") Assert.Equal(2, scope.Vm.Seasons[0].UserData!.WatchedCount);
        Assert.Equal(0, scope.Vm.Episodes[0].UserData!.PositionSeconds);
        Assert.Null(scope.Vm.ErrorMessage);
    }

    [Theory]
    [InlineData("series", "detail", false)]
    [InlineData("series", "seasons", false)]
    [InlineData("series", "episodes", false)]
    [InlineData("series", "detail", true)]
    [InlineData("season", "detail", true)]
    [InlineData("season", "episodes", true)]
    public async Task ExhaustedCatchUpExposesOnlyInlineRetryAndAFullRecoveryClearsIt(
        string type, string failedRead, bool explicitRetry)
    {
        using var scope = new Scope(type);
        scope.Vm.SelectedSeasonNumber = 1;
        var attempts = 0;
        var item = scope.Vm.Item;
        scope.OverrideResponse = request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (failedRead == "detail" ? path != "/api/v2/catalog/items/" + type : !path.EndsWith("/" + failedRead))
                return null;
            attempts++;
            return Unavailable();
        };

        await scope.Vm.RefreshMediaStateAsync(scope.Message() with { CatchUp = true })
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(4, attempts);
        Assert.Same(item, scope.Vm.Item);
        Assert.Equal(DetailFailureKind.None, scope.Vm.FailureKind);
        Assert.Null(scope.Vm.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(scope.Vm.RemoteStateErrorMessage));
        if (failedRead != "detail") Assert.Equal(2, item!.UserData!.WatchedCount);
        scope.OverrideResponse = null;
        scope.FreshViewerState = new() { IsFavorite = true, InWatchlist = true };
        scope.FreshRating = 4;
        // Both inline Retry and a narrower later event must recover the full
        // catch-up's viewer state and episode companion scope without remounting.
        if (explicitRetry) await scope.Vm.RetryRemoteStateCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        else await scope.Vm.RefreshMediaStateAsync(scope.Message()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(item, scope.Vm.Item);
        Assert.Null(scope.Vm.RemoteStateErrorMessage);
        Assert.Null(scope.Vm.ErrorMessage);
        Assert.Equal(DetailFailureKind.None, scope.Vm.FailureKind);
        Assert.True(scope.Vm.IsFavorite);
        Assert.True(scope.Vm.InWatchlist);
        Assert.Equal(4, scope.Vm.UserRating);
        Assert.Equal(2, item!.UserData!.WatchedCount);
        if (type == "series") Assert.Equal(2, scope.Vm.Seasons[0].UserData!.WatchedCount);
        Assert.Equal(0, scope.Vm.Episodes[0].UserData!.PositionSeconds);
    }

    [Theory]
    [InlineData("movie", false)]
    [InlineData("movie", true)]
    [InlineData("episode", false)]
    [InlineData("episode", true)]
    public async Task ViewerOnlyLeafSweepCannotClearOrDowngradeAPendingPlaybackRetry(string type, bool viewerFails)
    {
        using var scope = new Scope(type)
        {
            IncludeTarget = true, FreshTarget = "new-target", ContainerPlayed = true,
            FreshViewerState = new() { IsFavorite = true }
        };
        scope.Vm.Seasons.Clear(); scope.Vm.Episodes.Clear();
        var item = scope.Vm.Item!;
        item.PlayContentId = "old-target";
        var stalePlayback = item.UserData;
        scope.OverrideResponse = _ => Unavailable();
        await scope.Vm.RefreshMediaStateAsync(scope.Message(item.ContentId) with { CatchUp = true })
            .WaitAsync(TimeSpan.FromSeconds(10));
        var warning = scope.Vm.RemoteStateErrorMessage;
        Assert.False(string.IsNullOrWhiteSpace(warning));

        if (!viewerFails) scope.OverrideResponse = null;
        await scope.Vm.RefreshMediaStateAsync(scope.Message(item.ContentId) with
        {
            ViewerStateContentIds = new HashSet<string>([item.ContentId]),
            PlaybackStateContentIds = new HashSet<string>()
        }).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(warning, scope.Vm.RemoteStateErrorMessage);
        Assert.Same(stalePlayback, item.UserData);
        Assert.False(scope.Vm.IsWatched);
        Assert.Equal("old-target", item.PlayContentId);
        if (!viewerFails) Assert.True(scope.Vm.IsFavorite);

        scope.OverrideResponse = null;
        await scope.Vm.RetryRemoteStateCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(scope.Vm.RemoteStateErrorMessage);
        Assert.True(scope.Vm.IsWatched);
        Assert.True(item.UserData!.Played);
        Assert.Equal("new-target", item.PlayContentId);
    }

    [Theory]
    [InlineData("navigation")]
    [InlineData("profile")]
    public async Task RetiredCatchUpCannotRetryOrShowAnErrorAfterBackoff(string departure)
    {
        using var scope = new Scope("series");
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        scope.OverrideResponse = _ =>
        {
            attempts++;
            failed.TrySetResult();
            return Unavailable();
        };
        var operation = scope.Vm.RefreshMediaStateAsync(scope.Message() with { CatchUp = true });
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (departure == "navigation") scope.Vm.CancelPendingLoads();
        else scope.Client.SetProfile("replacement");

        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, attempts);
        Assert.Null(scope.Vm.ErrorMessage);
        Assert.Null(scope.Vm.RemoteStateErrorMessage);
        Assert.Equal(DetailFailureKind.None, scope.Vm.FailureKind);
        Assert.Equal(0, scope.Vm.Item!.UserData!.WatchedCount);
    }

    [Theory]
    [InlineData("series", "favorite")]
    [InlineData("series", "watchlist")]
    [InlineData("series", "rating")]
    [InlineData("series", "not_interested")]
    [InlineData("season", "favorite")]
    [InlineData("season", "watchlist")]
    [InlineData("season", "rating")]
    [InlineData("season", "not_interested")]
    [InlineData("movie", "favorite")]
    [InlineData("movie", "watchlist")]
    [InlineData("movie", "rating")]
    [InlineData("movie", "not_interested")]
    [InlineData("episode", "favorite")]
    [InlineData("episode", "watchlist")]
    [InlineData("episode", "rating")]
    [InlineData("episode", "not_interested")]
    public async Task RealtimeViewerStateChangesAndRemovalsReconcileTheMountedItem(string type, string change)
    {
        using var scope = new Scope(type);
        var messenger = new WeakReferenceMessenger();
        messenger.Register<ItemDetailViewModel, MediaStateInvalidated>(scope.Vm, static (vm, message) => vm.Receive(message));
        using var refresh = new RealtimeMediaStateRefresh(scope.Client, callback => callback(), messenger);
        var item = scope.Vm.Item!;
        var playbackState = item.UserData;
        item.PlayContentId = "mounted-target";
        if (type is "movie" or "episode") item.UserState = new() { Played = true };
        foreach (var enabled in new[] { true, false })
        {
            scope.FreshViewerState = new()
            {
                IsFavorite = change == "favorite" && enabled,
                InWatchlist = change == "watchlist" && enabled,
                NotInterested = change == "not_interested" && enabled
            };
            scope.FreshRating = change == "rating" && enabled ? 4 : null;
            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            System.ComponentModel.PropertyChangedEventHandler handler = (_, e) =>
            {
                if (e.PropertyName == nameof(scope.Vm.Item)) applied.TrySetResult();
            };
            scope.Vm.PropertyChanged += handler;
            try
            {
                using var data = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    profile_id = "profile", content_id = item.ContentId, change
                }));
                refresh.ReceiveRealtime("user_state", "user_state.changed", data.RootElement);
                await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Same(item, scope.Vm.Item);
                Assert.Equal(scope.FreshViewerState.IsFavorite, scope.Vm.IsFavorite);
                Assert.Equal(scope.FreshViewerState.InWatchlist, scope.Vm.InWatchlist);
                Assert.Equal(scope.FreshViewerState.NotInterested, item.UserState!.NotInterested);
                Assert.Equal(scope.FreshRating, scope.Vm.UserRating);
                Assert.Equal(scope.FreshRating, item.UserRating);
                if (type is "movie" or "episode")
                {
                    Assert.Same(playbackState, item.UserData);
                    Assert.True(item.UserState!.Played);
                    Assert.Equal("mounted-target", item.PlayContentId);
                    Assert.Equal(0, scope.EpisodeReads);
                }
            }
            finally { scope.Vm.PropertyChanged -= handler; }
        }
        Assert.Equal(2, scope.DetailReads);
    }

    private static HttpResponseMessage Unavailable() => new(HttpStatusCode.ServiceUnavailable)
    {
        Content = new StringContent("""{"error":{"code":"service_unavailable","message":"Synthetic outage"}}""",
            Encoding.UTF8, "application/json")
    };

    [Theory]
    [InlineData("movie")]
    [InlineData("episode")]
    public async Task ViewerOnlyLeafRefreshKeepsPlaybackStateEvenWhenCatchUpIsMergedOrPromoted(string type)
    {
        using var scope = new Scope(type) { IncludeTarget = true, FreshTarget = "remote-target",
            FreshViewerState = new() { IsFavorite = true }, FreshRating = 4 };
        var item = scope.Vm.Item!;
        var data = item.UserData;
        item.PlayContentId = "mounted-target";
        // Establish viewer-only retry ownership through an actual failed read;
        // assigning the public error text alone cannot identify its scope.
        scope.OverrideResponse = _ => Unavailable();
        await scope.Vm.RefreshMediaStateAsync(scope.Message(item.ContentId) with
        {
            ViewerStateContentIds = new HashSet<string>([item.ContentId])
        });
        Assert.NotNull(scope.Vm.RemoteStateErrorMessage);
        scope.OverrideResponse = null;
        await scope.Vm.RefreshMediaStateAsync(scope.Message(item.ContentId) with
        {
            CatchUp = true, ViewerStateContentIds = new HashSet<string>([item.ContentId])
        });
        Assert.True(scope.Vm.IsFavorite);
        Assert.Equal(4, scope.Vm.UserRating);
        Assert.Same(data, item.UserData);
        Assert.Equal("mounted-target", item.PlayContentId);
        Assert.Equal(0, scope.EpisodeReads);
        Assert.Null(scope.Vm.RemoteStateErrorMessage);
    }

    [Theory]
    [InlineData("movie")]
    [InlineData("episode")]
    public async Task ExplicitViewerStateRetryDoesNotAdvanceTheMountedLeafPlaybackState(string type)
    {
        using var scope = new Scope(type) { IncludeTarget = true, FreshTarget = "remote-target",
            FreshViewerState = new() { IsFavorite = true } };
        var item = scope.Vm.Item!;
        var data = item.UserData;
        item.PlayContentId = "mounted-target";
        scope.OverrideResponse = _ => Unavailable();
        await scope.Vm.RefreshMediaStateAsync(scope.Message(item.ContentId) with
        {
            ViewerStateContentIds = new HashSet<string>([item.ContentId])
        });
        Assert.NotNull(scope.Vm.RemoteStateErrorMessage);
        scope.OverrideResponse = null;
        await scope.Vm.RetryRemoteStateCommand.ExecuteAsync(null);
        Assert.Null(scope.Vm.RemoteStateErrorMessage);
        Assert.True(scope.Vm.IsFavorite);
        Assert.Same(data, item.UserData);
        Assert.Equal("mounted-target", item.PlayContentId);
    }

    [Theory]
    [InlineData("movie")]
    [InlineData("episode")]
    public async Task AnotherItemsViewerScopeDoesNotEnableAPartialLeafProgressRefresh(string type)
    {
        using var scope = new Scope(type);
        await scope.Vm.RefreshMediaStateAsync(scope.Message(scope.Vm.Item!.ContentId) with
        {
            ViewerStateContentIds = new HashSet<string>(["other-item"])
        });
        Assert.Equal(0, scope.DetailReads);
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
        Assert.Equal(DetailFailureKind.None, scope.Vm.FailureKind);
        Assert.Null(scope.Vm.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(scope.Vm.RemoteStateErrorMessage));
        scope.OverrideResponse = null;
        await scope.Vm.RefreshMediaStateAsync(scope.Message()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, scope.Vm.Item.UserData.WatchedCount);
        Assert.Null(scope.Vm.RemoteStateErrorMessage);
        Assert.Null(scope.Vm.ErrorMessage);
        Assert.Equal(DetailFailureKind.None, scope.Vm.FailureKind);
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

    [Theory]
    [InlineData("series", "remapped-episode")]
    [InlineData("series", null)]
    [InlineData("season", "remapped-episode")]
    [InlineData("season", null)]
    public async Task SameIdMatchReloadReconcilesCountsTargetAndCompanions(string type, string? target)
    {
        using var scope = new Scope(type);
        var mounted = scope.Vm.Item!;
        mounted.SeriesId = "old-series";
        mounted.SeriesTitle = "Old series";
        mounted.SeasonCount = 1;
        mounted.PlayContentId = "old-target";
        var season = scope.Vm.Seasons[0];
        var episode = scope.Vm.Episodes[0];
        scope.Vm.SelectedSeasonNumber = 1;
        scope.Vm.EpisodesSeasonContentId = "season";
        scope.OverrideDetail = () => Task.FromResult(new MediaItemDetail
        {
            ContentId = mounted.ContentId, Type = type, Title = "Matched title",
            SeriesId = "matched-series", SeriesTitle = "Matched series",
            EpisodeCount = 12, SeasonCount = 3, PlayContentId = target, PlaySeasonNumber = 2,
            UserState = new() { IsFavorite = true, InWatchlist = true }, UserRating = 4,
            UserData = new() { WatchedCount = 2, UnplayedCount = 10 }
        });

        await scope.Vm.ReloadAfterMatchAsync(mounted.ContentId).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(mounted, scope.Vm.Item);
        Assert.Equal("Matched title", mounted.Title);
        Assert.Equal("matched-series", mounted.SeriesId);
        Assert.Equal("Matched series", mounted.SeriesTitle);
        Assert.Equal(12, mounted.EpisodeCount);
        Assert.Equal(3, mounted.SeasonCount);
        Assert.Equal(target, mounted.PlayContentId);
        Assert.Equal(2, mounted.PlaySeasonNumber);
        Assert.True(scope.Vm.IsFavorite);
        Assert.True(scope.Vm.InWatchlist);
        Assert.Equal(4, scope.Vm.UserRating);
        Assert.Same(season, Assert.Single(scope.Vm.Seasons));
        // A remap must retire the old parent's rows. The mounted page owns
        // the canonical membership reload; the VM sweep only patches retained rows.
        Assert.Empty(scope.Vm.Episodes);
        Assert.Null(scope.Vm.EpisodesSeasonContentId);
        Assert.Equal(1, scope.Vm.ParentPlaybackRevision);
        Assert.Equal(1, scope.Vm.SelectedSeasonNumber);
        Assert.Equal(31, episode.UserData!.PositionSeconds);
        if (type == "series") Assert.Equal(2, season.UserData!.WatchedCount);
        Assert.Equal(2, scope.DetailReads);
        Assert.Equal(0, scope.EpisodeReads);
        Assert.Null(scope.Vm.ErrorMessage);
        Assert.Null(scope.Vm.RemoteStateErrorMessage);
        await scope.Vm.RefreshMediaStateAsync(scope.Message("unloaded", "old-series"));
        Assert.Equal(2, scope.DetailReads);
        await scope.Vm.RefreshMediaStateAsync(scope.Message("unloaded", "matched-series"));
        Assert.Equal(3, scope.DetailReads);

        // Model the page's completed canonical reload and verify subsequent
        // state refreshes update only the new row, never the retired object.
        var replacement = new Episode { ContentId = "sibling", SeasonNumber = 1, UserData = new() { PositionSeconds = 90 } };
        scope.Vm.Episodes.Add(replacement);
        scope.Vm.EpisodesSeasonContentId = "season";
        await scope.Vm.RefreshMediaStateAsync(scope.Message(mounted.ContentId));
        Assert.Same(replacement, Assert.Single(scope.Vm.Episodes));
        Assert.Equal(0, replacement.UserData!.PositionSeconds);
        Assert.Equal(31, episode.UserData.PositionSeconds);
        Assert.Equal(1, scope.EpisodeReads);
        Assert.Null(scope.Vm.RemoteStateErrorMessage);
    }

    [Theory]
    [InlineData("series", true)]
    [InlineData("season", true)]
    [InlineData("series", false)]
    [InlineData("season", false)]
    public async Task ParentRemapWithoutAPlayTargetDiscardsThePreviousParentsEpisode(string type, bool afterMatch)
    {
        using var scope = new Scope(type);
        var mounted = scope.Vm.Item!;
        mounted.SeriesId = "old-series";
        mounted.PlayContentId = "old-target";
        mounted.PlaySeasonNumber = 9;
        scope.OverrideDetail = () => Task.FromResult(new MediaItemDetail
        {
            // Legacy response deliberately omits play_content_id.
            ContentId = mounted.ContentId, Type = type, SeriesId = "matched-series",
            UserData = new() { WatchedCount = 2, UnplayedCount = 2 }
        });

        if (afterMatch) await scope.Vm.ReloadAfterMatchAsync(mounted.ContentId).WaitAsync(TimeSpan.FromSeconds(5));
        else await scope.Vm.RefreshMediaStateAsync(scope.Message(mounted.ContentId)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(mounted, scope.Vm.Item);
        Assert.Equal("matched-series", mounted.SeriesId);
        Assert.Null(mounted.PlayContentId);
        Assert.Null(mounted.PlaySeasonNumber);
        Assert.False(mounted.HasAuthoritativePlayTarget);
        Assert.Null(SeriesPrimaryActionResolver.ResolveDetail(mounted).ContentId);
        Assert.Null(scope.Vm.MetadataRefreshErrorMessage);
        Assert.Null(scope.Vm.RemoteStateErrorMessage);

        // A later explicit target still restores server authority normally.
        scope.OverrideDetail = () => Task.FromResult(new MediaItemDetail
        {
            ContentId = mounted.ContentId, Type = type, SeriesId = "matched-series",
            PlayContentId = "matched-episode", PlaySeasonNumber = 2,
            UserData = new() { WatchedCount = 2, UnplayedCount = 2 }
        });
        await scope.Vm.RefreshMediaStateAsync(scope.Message(mounted.ContentId)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(mounted.HasAuthoritativePlayTarget);
        Assert.Equal("matched-episode", mounted.PlayContentId);
        Assert.Equal(2, mounted.PlaySeasonNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedSweepAdoptsParentIdentityAndPreservesItWhenLegacyResponsesOmitIt(bool clearParent)
    {
        using var scope = new Scope("season");
        var mounted = scope.Vm.Item!;
        scope.OverrideDetail = () => Task.FromResult(new MediaItemDetail
        {
            ContentId = mounted.ContentId, Type = "season", SeriesId = "new-series", SeriesTitle = "New series"
        });
        await scope.Vm.RefreshMediaStateAsync(scope.Message(mounted.ContentId));
        Assert.Same(mounted, scope.Vm.Item);
        Assert.Equal("new-series", mounted.SeriesId);
        Assert.Equal("New series", mounted.SeriesTitle);
        var reads = scope.DetailReads;
        await scope.Vm.RefreshMediaStateAsync(scope.Message("unloaded", "series"));
        Assert.Equal(reads, scope.DetailReads);
        await scope.Vm.RefreshMediaStateAsync(scope.Message("unloaded", "new-series"));
        Assert.Equal(reads + 1, scope.DetailReads);

        scope.OverrideResponse = request => request.RequestUri!.AbsolutePath == "/api/v2/catalog/items/season"
            ? new(HttpStatusCode.OK) { Content = new StringContent(clearParent
                ? """{"content_id":"season","type":"season","series_id":null}"""
                : """{"content_id":"season","type":"season"}""", Encoding.UTF8, "application/json") } : null;
        await scope.Vm.RefreshMediaStateAsync(scope.Message(mounted.ContentId));
        Assert.Equal(clearParent ? null : "new-series", mounted.SeriesId);
    }

    [Theory]
    [InlineData("series")]
    [InlineData("season")]
    public async Task FailedMetadataReloadKeepsMountedContentAndMetadataOnlyRetryClearsItsInlineError(string type)
    {
        using var scope = new Scope(type);
        var mounted = scope.Vm.Item!;
        var season = scope.Vm.Seasons[0];
        var episode = scope.Vm.Episodes[0];
        scope.Vm.SelectedSeasonNumber = 1;
        scope.Vm.EpisodesSeasonContentId = "season";
        scope.OverrideResponse = request => request.RequestUri!.AbsolutePath == "/api/v2/catalog/items/" + mounted.ContentId
            ? Unavailable() : null;

        await scope.Vm.ReloadAsync(mounted.ContentId);

        Assert.Same(mounted, scope.Vm.Item);
        Assert.Null(scope.Vm.ErrorMessage);
        Assert.Equal(DetailFailureKind.None, scope.Vm.FailureKind);
        Assert.NotNull(scope.Vm.MetadataRefreshErrorMessage);
        Assert.False(scope.Vm.IsLoading);
        Assert.False(scope.Vm.IsMetadataRefreshing);
        Assert.Same(season, Assert.Single(scope.Vm.Seasons));
        Assert.Same(episode, Assert.Single(scope.Vm.Episodes));
        Assert.Equal(0, scope.EpisodeReads);

        // An unrelated state sweep must not dismiss a failed metadata refresh.
        scope.OverrideResponse = null;
        await scope.Vm.RefreshMediaStateAsync(scope.Message());
        Assert.NotNull(scope.Vm.MetadataRefreshErrorMessage);
        var viewerState = mounted.UserData;
        var episodeReads = scope.EpisodeReads;
        scope.OverrideDetail = () => Task.FromResult(new MediaItemDetail
        {
            ContentId = mounted.ContentId, Type = type, Title = "Recovered metadata",
            PlayContentId = "obsolete-metadata-target", UserData = new() { Played = true }
        });
        scope.Vm.RemoteStateErrorMessage = "An independent state refresh still needs retry.";

        // The page's inline metadata retry calls ReloadAsync, not LoadAsync or a state sweep.
        await scope.Vm.ReloadAsync(mounted.ContentId);

        Assert.Equal("Recovered metadata", mounted.Title);
        Assert.Null(scope.Vm.MetadataRefreshErrorMessage);
        Assert.NotNull(scope.Vm.RemoteStateErrorMessage);
        Assert.Null(scope.Vm.ErrorMessage);
        Assert.Equal(DetailFailureKind.None, scope.Vm.FailureKind);
        Assert.Same(mounted, scope.Vm.Item);
        Assert.Same(viewerState, mounted.UserData);
        Assert.False(mounted.HasAuthoritativePlayTarget);
        Assert.Same(season, Assert.Single(scope.Vm.Seasons));
        Assert.Same(episode, Assert.Single(scope.Vm.Episodes));
        Assert.Equal(1, scope.Vm.SelectedSeasonNumber);
        Assert.Equal("season", scope.Vm.EpisodesSeasonContentId);
        Assert.Equal(episodeReads, scope.EpisodeReads);
        Assert.False(scope.Vm.IsLoading);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("profile")]
    [InlineData("replacement")]
    public async Task InterruptedMetadataReloadRetainsOnlyItsReconnectOwnedRetry(string departure)
    {
        using var scope = new Scope("series");
        var fresh = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.OverrideDetail = () => fresh.Task;
        var mounted = scope.Vm.Item!;
        var reload = scope.Vm.ReloadAsync(mounted.ContentId);
        await scope.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (departure == "context") scope.Client.InvalidateAccessContext();
        else if (departure == "profile") scope.Client.SetProfile("other-profile");
        else scope.Vm.Item = new() { ContentId = mounted.ContentId, Type = "series" };
        fresh.SetResult(scope.FreshDetail(mounted.ContentId, "series"));
        await reload.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(departure == "context", scope.Vm.MetadataRefreshErrorMessage != null);
        Assert.False(scope.Vm.IsMetadataRefreshing);
        Assert.Null(scope.Vm.ErrorMessage);
        if (departure != "context") return;
        scope.OverrideDetail = () => Task.FromResult(new MediaItemDetail { ContentId = mounted.ContentId, Type = "series", Title = "Recovered metadata" });
        await scope.Vm.ReloadAsync(mounted.ContentId);
        Assert.Same(mounted, scope.Vm.Item);
        Assert.Equal("Recovered metadata", mounted.Title);
        Assert.Null(scope.Vm.MetadataRefreshErrorMessage);
    }

    [Fact]
    public async Task PendingMetadataReloadDoesNotHideMountedContentBehindTheInitialLoadSkeleton()
    {
        using var scope = new Scope("series");
        var fresh = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.OverrideDetail = () => fresh.Task;
        var mounted = scope.Vm.Item!;
        var reload = scope.Vm.ReloadAsync(mounted.ContentId);
        await scope.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.True(scope.Vm.IsMetadataRefreshing);
            Assert.False(scope.Vm.IsLoading);
            Assert.Same(mounted, scope.Vm.Item);
            Assert.Single(scope.Vm.Seasons);
            Assert.Single(scope.Vm.Episodes);
        }
        finally { fresh.TrySetResult(scope.FreshDetail(mounted.ContentId, "series")); }
        await reload.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(scope.Vm.IsMetadataRefreshing);
    }

    [Theory]
    [InlineData("series", false, false)]
    [InlineData("series", false, true)]
    [InlineData("series", true, false)]
    [InlineData("series", true, true)]
    [InlineData("season", false, false)]
    [InlineData("season", false, true)]
    [InlineData("season", true, false)]
    [InlineData("season", true, true)]
    public async Task MetadataReloadPreservesOmittedIdentityAndAdoptsSuppliedValues(string type, bool supplied, bool afterMatch)
    {
        using var scope = new Scope(type);
        var mounted = scope.Vm.Item!;
        mounted.SeriesId = "known-parent";
        mounted.SeriesTitle = "Known parent";
        mounted.SeasonNumber = 2;
        mounted.EpisodeNumber = 3;
        mounted.IsSpecials = true;
        var body = new Dictionary<string, object?>
        {
            ["content_id"] = mounted.ContentId, ["type"] = type, ["title"] = "Edited title"
        };
        if (supplied)
        {
            body["series_title"] = "Edited parent";
            body["season_number"] = 0;
            body["episode_number"] = 0;
            body["is_specials"] = false;
        }
        scope.OverrideResponse = request => request.RequestUri!.AbsolutePath == "/api/v2/catalog/items/" + mounted.ContentId
            ? new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") }
            : null;
        var notified = false;
        scope.Vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(scope.Vm.Item)) notified = true; };

        if (afterMatch) await scope.Vm.ReloadAfterMatchAsync(mounted.ContentId);
        else await scope.Vm.ReloadAsync(mounted.ContentId);

        Assert.True(notified);
        Assert.Same(mounted, scope.Vm.Item);
        Assert.Equal("Edited title", mounted.Title);
        Assert.Equal("known-parent", mounted.SeriesId);
        Assert.Equal(supplied ? "Edited parent" : "Known parent", mounted.SeriesTitle);
        Assert.Equal(supplied ? 0 : 2, mounted.SeasonNumber);
        Assert.Equal(supplied ? 0 : 3, mounted.EpisodeNumber);
        Assert.Equal(!supplied, mounted.IsSpecials);
        Assert.Null(scope.Vm.MetadataRefreshErrorMessage);
    }

    [Theory]
    [InlineData("series", false)]
    [InlineData("season", false)]
    [InlineData("series", true)]
    [InlineData("season", true)]
    public async Task MetadataReloadRetainsOnlyRowsFromTheSameParentForTheNextRemoteEvent(string type, bool parentChanged)
    {
        using var scope = new Scope(type);
        var mounted = scope.Vm.Item!;
        var season = scope.Vm.Seasons[0];
        var episode = scope.Vm.Episodes[0];
        var viewerState = mounted.UserData;
        scope.Vm.IsSeries = type == "series";
        scope.Vm.SelectedSeasonNumber = 1;
        scope.Vm.EpisodesSeasonContentId = "season";
        scope.Vm.SimilarItems.Add(new() { ContentId = "similar" });
        scope.OverrideDetail = () => Task.FromResult(new MediaItemDetail
        {
            ContentId = mounted.ContentId, Type = type, Title = "Edited title", Overview = "Translated overview",
            SeriesId = parentChanged ? "edited-parent" : mounted.SeriesId, SeriesTitle = "Edited parent",
            SeasonNumber = 1, PendingTranslationLanguage = null, PlayContentId = null,
            UserData = new() { Played = true, WatchedCount = 4 }
        });

        await scope.Vm.ReloadAsync(mounted.ContentId);

        Assert.Same(mounted, scope.Vm.Item);
        Assert.Equal("Edited title", mounted.Title);
        Assert.Equal(parentChanged ? "edited-parent" : type == "season" ? "series" : null, mounted.SeriesId);
        Assert.Equal("Edited parent", mounted.SeriesTitle);
        Assert.Equal("Translated overview", mounted.Overview);
        Assert.Null(mounted.PendingTranslationLanguage);
        Assert.Same(viewerState, mounted.UserData);
        Assert.False(mounted.HasAuthoritativePlayTarget);
        Assert.Same(season, Assert.Single(scope.Vm.Seasons));
        if (parentChanged)
        {
            Assert.Empty(scope.Vm.Episodes);
            Assert.Null(scope.Vm.EpisodesSeasonContentId);
            // A subsequent navigation-owned list load can establish fresh rows;
            // remote state then updates those, never the old parent's objects.
            var retiredEpisode = episode;
            episode = new() { ContentId = "sibling", SeasonNumber = 1, UserData = new() { PositionSeconds = 31 } };
            scope.Vm.Episodes.Add(episode);
            scope.Vm.EpisodesSeasonContentId = "season";
            Assert.NotSame(retiredEpisode, Assert.Single(scope.Vm.Episodes));
        }
        else
        {
            Assert.Same(episode, Assert.Single(scope.Vm.Episodes));
            Assert.Equal("season", scope.Vm.EpisodesSeasonContentId);
        }
        Assert.Single(scope.Vm.SimilarItems);
        Assert.Equal(1, scope.Vm.SelectedSeasonNumber);
        Assert.False(scope.Vm.IsLoading);

        scope.OverrideDetail = null;
        scope.ContainerPlayed = true;
        var changes = new List<string?>();
        scope.Vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        await scope.Vm.RefreshMediaStateAsync(scope.Message("sibling"));

        Assert.Same(season, Assert.Single(scope.Vm.Seasons));
        Assert.Same(episode, Assert.Single(scope.Vm.Episodes));
        Assert.True(episode.UserData!.Played);
        Assert.Equal(0, episode.UserData.PositionSeconds);
        Assert.Contains(nameof(scope.Vm.Episodes), changes);
        if (type == "series")
        {
            Assert.Equal(2, season.UserData!.WatchedCount);
            Assert.Contains(nameof(scope.Vm.Seasons), changes);
        }
        scope.Vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedCleared, "sibling", "series"));
        Assert.False(episode.UserData.Played);
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
                    ["title"] = detail.Title, ["overview"] = detail.Overview,
                    ["episode_count"] = detail.EpisodeCount, ["season_count"] = detail.SeasonCount,
                    ["season_number"] = detail.SeasonNumber, ["pending_translation_language"] = detail.PendingTranslationLanguage,
                    ["user_state"] = detail.UserState == null ? null : new { is_favorite = detail.UserState.IsFavorite,
                        in_watchlist = detail.UserState.InWatchlist, not_interested = detail.UserState.NotInterested },
                    ["user_rating"] = detail.UserRating,
                    ["user_data"] = detail.UserData == null ? null : new { watched_count = detail.UserData.WatchedCount, unplayed_count = detail.UserData.UnplayedCount,
                        in_progress_count = detail.UserData.InProgressCount, played = detail.UserData.Played }
                };
                if (detail.HasAuthoritativeSeriesId) body["series_id"] = detail.SeriesId;
                if (detail.SeriesTitle != null) body["series_title"] = detail.SeriesTitle;
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
