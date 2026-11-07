using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class RealtimeReadOwnershipTests
{
    [Fact]
    public async Task UnchangedSweepsDoNotNotifyOrReplaceRetainedCollections()
    {
        using var f = new Fixture(); f.Seed();
        var item = f.Vm.Item;
        var season = f.Vm.Seasons[0];
        var episode = f.Vm.Episodes[0];
        var changes = new List<string?>();
        var collectionChanges = 0;
        f.Vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        f.Vm.Seasons.CollectionChanged += (_, _) => collectionChanges++;
        await f.Vm.RefreshMediaStateAsync(f.Change(EpisodeA));
        await f.Vm.RefreshMediaStateAsync(f.Change(EpisodeA));
        Assert.DoesNotContain(nameof(f.Vm.Item), changes);
        Assert.DoesNotContain(nameof(f.Vm.Seasons), changes);
        Assert.DoesNotContain(nameof(f.Vm.Episodes), changes);
        Assert.Equal(0, collectionChanges);
        Assert.Same(item, f.Vm.Item);
        Assert.Same(season, f.Vm.Seasons[0]);
        Assert.Same(episode, f.Vm.Episodes[0]);
    }

    [Theory]
    [InlineData("series", true)]
    [InlineData("series", false)]
    [InlineData("season", false)]
    public async Task NewContextCatchUpRepaintsValuesCommittedByARetiredSweep(string type, bool holdSeasons)
    {
        using var f = new Fixture(); f.Seed();
        if (type == "season")
        {
            f.Vm.Item!.Type = type;
            f.Vm.Item.ContentId = "series:review-S01";
            f.Vm.Item.SeriesId = Series;
            f.Wire.DetailType = type; f.Wire.DetailSeriesId = Series;
        }
        var mounted = f.Vm.Item!;
        mounted.EpisodeCount = 4;
        var season = f.Vm.Seasons[0];
        var episode = f.Vm.Episodes[0];
        var changes = new List<string?>();
        f.Vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        f.Wire.Count = 2; f.Wire.Target = EpisodeB;
        f.Wire.HoldSeasons = holdSeasons; f.Wire.HoldEpisodes = !holdSeasons;
        var active = f.Vm.RefreshMediaStateAsync(f.Change(EpisodeA));
        var held = await f.Wire.NextAsync();
        Assert.Equal(8, mounted.EpisodeCount);
        Assert.Equal(EpisodeB, mounted.PlayContentId);
        Assert.DoesNotContain(nameof(f.Vm.Item), changes);
        if (type == "series" && !holdSeasons)
        {
            Assert.Equal(2, season.UserData!.WatchedCount);
            Assert.DoesNotContain(nameof(f.Vm.Seasons), changes);
        }

        // Reconnect retires reads without changing the mounted item/profile.
        f.Client.InvalidateAccessContext();
        f.Wire.HoldSeasons = false; f.Wire.HoldEpisodes = false;
        var catchUp = f.Vm.RefreshMediaStateAsync(f.Change() with { CatchUp = true });
        await Task.WhenAll(active, catchUp).WaitAsync(TimeSpan.FromSeconds(5));
        await held.Settled.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(mounted, f.Vm.Item);
        Assert.Same(season, Assert.Single(f.Vm.Seasons));
        Assert.Same(episode, Assert.Single(f.Vm.Episodes));
        Assert.Equal(2, mounted.UserData!.WatchedCount);
        Assert.Equal(EpisodeB, mounted.PlayContentId);
        Assert.Contains(nameof(f.Vm.Item), changes);
        Assert.Contains(nameof(f.Vm.Seasons), changes);
        Assert.Contains(nameof(f.Vm.Episodes), changes);
    }

    [Theory]
    [InlineData("favorite", false, "cancel")]
    [InlineData("favorite", true, "cancel")]
    [InlineData("watchlist", false, "cancel")]
    [InlineData("watchlist", true, "cancel")]
    [InlineData("rating", false, "cancel")]
    [InlineData("rating", true, "cancel")]
    [InlineData("favorite", false, "navigation")]
    [InlineData("favorite", true, "navigation")]
    [InlineData("watchlist", false, "navigation")]
    [InlineData("watchlist", true, "navigation")]
    [InlineData("rating", false, "navigation")]
    [InlineData("rating", true, "navigation")]
    [InlineData("favorite", false, "same-id")]
    [InlineData("favorite", true, "same-id")]
    [InlineData("watchlist", false, "same-id")]
    [InlineData("watchlist", true, "same-id")]
    [InlineData("rating", false, "same-id")]
    [InlineData("rating", true, "same-id")]
    [InlineData("favorite", false, "profile")]
    [InlineData("favorite", true, "profile")]
    [InlineData("watchlist", false, "profile")]
    [InlineData("watchlist", true, "profile")]
    [InlineData("rating", false, "profile")]
    [InlineData("rating", true, "profile")]
    [InlineData("favorite", false, "context")]
    [InlineData("favorite", true, "context")]
    [InlineData("watchlist", false, "context")]
    [InlineData("watchlist", true, "context")]
    [InlineData("rating", false, "context")]
    [InlineData("rating", true, "context")]
    public async Task RetiredViewerWriteCannotApplyStaleFeedbackOrRollBackTheCurrentDetail(string mutation, bool fail, string departure)
    {
        using var f = new Fixture(); f.Seed();
        f.Wire.PendingWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Wire.WriteStatus = fail ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Wire.OnWrite = () => started.TrySetResult();
        var context = f.Client.CaptureContext();
        var recipient = new object();
        var messages = new List<MediaSurfaceChanged>();
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(recipient, (_, message) =>
        {
            // Ignore other parallel fixtures, but retain wrong/missing authority
            // on our target and wrong targets published by our backend.
            if (message.ContentId == Series || message.Context?.BaseUrl == context.BaseUrl) messages.Add(message);
        });
        try
        {
            var operation = mutation == "favorite" ? f.Vm.ToggleFavoriteCommand.ExecuteAsync(null)
                : mutation == "watchlist" ? f.Vm.ToggleWatchlistCommand.ExecuteAsync(null)
                : f.Vm.SetRatingCommand.ExecuteAsync(4);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (departure == "cancel") f.Vm.CancelPendingLoads();
            else if (departure == "navigation") await f.Vm.LoadInLibraryAsync(EpisodeB, 9);
            else if (departure == "same-id") f.Vm.Item = new() { ContentId = Series, Type = "series", UserState = new() };
            else if (departure == "profile") f.Client.SetProfile("replacement");
            else f.Client.InvalidateAccessContext();
            // Opposite values expose either a stale success publish or a stale rollback.
            f.Vm.IsFavorite = fail; f.Vm.InWatchlist = fail;
            int? currentRating = fail ? 4 : null;
            f.Vm.Item!.UserRating = f.Vm.UserRating = currentRating;
            var mounted = f.Vm.Item;
            f.Wire.PendingWrite.SetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(mounted, f.Vm.Item);
            Assert.Equal(fail, f.Vm.IsFavorite);
            Assert.Equal(fail, f.Vm.InWatchlist);
            Assert.Equal(currentRating, f.Vm.UserRating);
            Assert.Equal(currentRating, mounted!.UserRating);
            if (!fail && departure is ("cancel" or "navigation" or "same-id" or "context"))
            {
                // Accepted viewer writes still notify app-lifetime recipients,
                // without applying their old feedback to this replacement viewer.
                var message = Assert.Single(messages);
                Assert.Equal(Series, message.ContentId);
                Assert.Equal(f.Client.CaptureContext(), message.Context);
                Assert.Equal(mutation == "rating" ? MediaSurfaceChangeKind.RatingChanged
                    : mutation == "favorite" ? MediaSurfaceChangeKind.FavoriteAdded : MediaSurfaceChangeKind.WatchlistAdded, message.Kind);
                if (mutation == "rating") Assert.Equal(4, message.Rating);
            }
            else Assert.Empty(messages);
        }
        finally { WeakReferenceMessenger.Default.UnregisterAll(recipient); }
    }

    [Theory]
    [InlineData("context", false)]
    [InlineData("profile", false)]
    [InlineData("context", true)]
    public async Task AcceptedWatchedWritePublishesAcrossReconnectButNeverAcrossProfiles(string departure, bool fail)
    {
        using var f = new Fixture(); f.Seed();
        f.Wire.WriteMethod = HttpMethod.Post;
        f.Wire.PendingWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Wire.WriteStatus = fail ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Wire.OnWrite = () => started.TrySetResult();
        var recipient = new object();
        var messages = new List<MediaSurfaceChanged>();
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(recipient, (_, message) =>
        {
            if (message.ContentId == Series) messages.Add(message);
        });
        try
        {
            var write = f.Vm.ToggleWatchedCommand.ExecuteAsync(null);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (departure == "profile") f.Client.SetProfile("other-profile");
            else f.Client.InvalidateAccessContext();
            f.Vm.IsWatched = false;
            f.Wire.PendingWrite.SetResult();
            await write.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(f.Vm.IsWatched);
            Assert.False(f.Vm.Item!.UserData!.Played);
            if (departure == "context" && !fail)
            {
                var message = Assert.Single(messages);
                Assert.Equal(MediaSurfaceChangeKind.WatchedMarked, message.Kind);
                Assert.Equal(f.Client.CaptureContext(), message.Context);
            }
            else Assert.Empty(messages);
        }
        finally { WeakReferenceMessenger.Default.UnregisterAll(recipient); }
    }

    [Theory]
    [InlineData("same-identity")]
    [InlineData("other-profile")]
    [InlineData("returning-profile")]
    [InlineData("legacy")]
    public async Task ProgressCallbacksAdmitOnlyTheirOriginalIdentityOrLegacyMessages(string ownerKind)
    {
        using var f = new Fixture(); f.Seed();
        var owner = f.Client.CaptureIdentityContext();
        if (ownerKind is "other-profile" or "returning-profile") f.Client.SetProfile("other-profile");
        if (ownerKind == "returning-profile") f.Client.SetProfile("profile");
        var allowed = ownerKind is "same-identity" or "legacy";
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beforeRevision = f.Vm.WatchedStateRevision;
        try
        {
            f.Vm.Receive(new PlaybackProgressUpdated(EpisodeA, 900, 1000, false)
            {
                SeriesId = Series, Persisted = allowed ? Task.CompletedTask : saved.Task,
                Identity = ownerKind == "legacy" ? null : owner
            });
            Assert.Equal(allowed ? 900 : 31, f.Vm.Episodes[0].UserData!.PositionSeconds);
            Assert.Equal(beforeRevision + (allowed ? 1 : 0), f.Vm.WatchedStateRevision);
            // A rejected old-profile save must not own the current visit's read barrier.
            await f.Vm.RefreshMediaStateAsync(f.Change()).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(f.Wire.DetailReads > 0);
        }
        finally { saved.TrySetResult(); }
    }

    [Theory]
    [InlineData("rating", "remote")]
    [InlineData("rating", "local")]
    [InlineData("rating", "replacement")]
    [InlineData("lists", "remote")]
    [InlineData("lists", "replacement")]
    public async Task LegacyViewerReadsCannotOverwriteNewerState(string readKind, string successor)
    {
        using var f = new Fixture(); f.Seed();
        f.Wire.Rating = 1;
        f.Wire.HoldViewerReads = true;
        f.Wire.OmitViewerState = readKind == "lists";
        var loading = readKind == "rating" ? f.Vm.LoadRatingCommand.ExecuteAsync(null) : f.Vm.LoadCommand.ExecuteAsync(Series);
        var first = await f.Wire.NextAsync();
        var second = readKind == "lists" ? await f.Wire.NextAsync() : null;
        if (successor == "remote")
        {
            f.Wire.OmitViewerState = false;
            f.Wire.Favorite = f.Wire.Watchlist = true; f.Wire.Rating = 4;
            await f.Vm.RefreshMediaStateAsync(f.Change(Series)).WaitAsync(TimeSpan.FromSeconds(5));
        }
        else if (successor == "local")
        {
            f.Wire.OnWrite = () => { };
            await f.Vm.SetRatingCommand.ExecuteAsync(4);
        }
        else
        {
            f.Vm.Item = new() { ContentId = Series, Type = "series", UserState = new() { IsFavorite = true, InWatchlist = true }, UserRating = 4 };
            f.Vm.UserRating = 4; f.Vm.IsFavorite = f.Vm.InWatchlist = true;
        }
        first.Release(); second?.Release();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        if (readKind == "rating") Assert.Equal(4, f.Vm.UserRating);
        else { Assert.True(f.Vm.IsFavorite); Assert.True(f.Vm.InWatchlist); }
    }

    [Theory]
    [InlineData(true, "context")]
    [InlineData(false, "context")]
    [InlineData(true, "profile")]
    [InlineData(false, "profile")]
    [InlineData(true, "replacement")]
    [InlineData(false, "replacement")]
    public async Task RetiredInitialCompanionMarksOnlyItsReconnectOwnedFailure(bool seasons, string departure)
    {
        using var f = new Fixture(); f.Seed();
        f.Vm.Episodes.Clear(); if (seasons) f.Vm.Seasons.Clear();
        f.Wire.HoldSeasons = seasons; f.Wire.HoldEpisodes = !seasons;
        var loading = seasons ? f.Vm.LoadSeasonsCommand.ExecuteAsync(null) : f.Vm.SelectSeasonCommand.ExecuteAsync(1);
        var read = await f.Wire.NextAsync();
        if (departure == "context") f.Client.InvalidateAccessContext();
        else if (departure == "profile") f.Client.SetProfile("other-profile");
        else f.Vm.Item = new() { ContentId = Series, Type = "series" };
        read.Release();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(departure == "context", seasons ? f.Vm.SeasonsLoadFailed : f.Vm.EpisodesLoadFailed);
        Assert.Empty(f.Vm.Episodes); if (seasons) Assert.Empty(f.Vm.Seasons);
        Assert.False(seasons ? f.Vm.IsSeasonsLoading : f.Vm.IsEpisodesLoading);
        if (departure != "context") return;
        f.Wire.HoldSeasons = f.Wire.HoldEpisodes = false;
        await (seasons ? f.Vm.LoadSeasonsCommand.ExecuteAsync(null) : f.Vm.SelectSeasonCommand.ExecuteAsync(1))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(seasons ? f.Vm.SeasonsLoadFailed : f.Vm.EpisodesLoadFailed);
        Assert.Single(f.Vm.Episodes);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task InitialCompanionRetirementAfterApiAcceptanceKeepsItsOwnedRecoveryMarker(bool seasons, bool changeProfile)
    {
        using var f = new Fixture(); f.Seed();
        f.Vm.Episodes.Clear(); if (seasons) f.Vm.Seasons.Clear();
        f.Wire.HoldSeasons = seasons; f.Wire.HoldEpisodes = !seasons;
        var queue = new ControlledStateContext();
        var previous = SynchronizationContext.Current;
        Task loading;
        SynchronizationContext.SetSynchronizationContext(queue);
        try { loading = seasons ? f.Vm.LoadSeasonsCommand.ExecuteAsync(null) : f.Vm.SelectSeasonCommand.ExecuteAsync(1); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        var read = await f.Wire.NextAsync();
        read.Release();
        await queue.RunNextAsync(installContext: false); // Release the HTTP handler's held response.
        await queue.WaitUntilPostedAsync(); // The API has accepted it; the VM has not resumed.
        if (changeProfile) f.Client.SetProfile("other-profile");
        else f.Client.InvalidateAccessContext();
        await queue.RunUntilCompleteAsync(loading);
        Assert.Equal(!changeProfile, seasons ? f.Vm.SeasonsLoadFailed : f.Vm.EpisodesLoadFailed);
        Assert.Empty(f.Vm.Episodes); if (seasons) Assert.Empty(f.Vm.Seasons);
        Assert.False(seasons ? f.Vm.IsSeasonsLoading : f.Vm.IsEpisodesLoading);
    }

    [Theory]
    [InlineData("favorite", false)]
    [InlineData("favorite", true)]
    [InlineData("watchlist", false)]
    [InlineData("watchlist", true)]
    [InlineData("rating", false)]
    [InlineData("rating", true)]
    public async Task ReconnectCatchUpRepairsFailedOptimisticViewerStateWhenTheSnapshotIsUnchanged(string mutation, bool initiallySet)
    {
        using var f = new Fixture(); f.Seed();
        var item = f.Vm.Item!;
        item.Type = f.Wire.DetailType = "movie";
        f.Vm.IsSeries = false;
        f.Vm.Seasons.Clear(); f.Vm.Episodes.Clear();
        item.UserState = new() { IsFavorite = initiallySet, InWatchlist = initiallySet };
        f.Vm.IsFavorite = f.Wire.Favorite = initiallySet;
        f.Vm.InWatchlist = f.Wire.Watchlist = initiallySet;
        item.UserRating = f.Vm.UserRating = f.Wire.Rating = initiallySet ? 4 : null;
        f.Wire.PendingWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Wire.WriteStatus = HttpStatusCode.Forbidden;
        f.Wire.WriteMethod = initiallySet ? HttpMethod.Delete : HttpMethod.Put;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Wire.OnWrite = () => started.TrySetResult();
        var write = mutation == "favorite" ? f.Vm.ToggleFavoriteCommand.ExecuteAsync(null)
            : mutation == "watchlist" ? f.Vm.ToggleWatchlistCommand.ExecuteAsync(null)
            : f.Vm.SetRatingCommand.ExecuteAsync(4);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Client.InvalidateAccessContext();
        var catchUp = f.Vm.RefreshMediaStateAsync(f.Change(item.ContentId) with { CatchUp = true });
        f.Wire.PendingWrite.SetResult();
        await Task.WhenAll(write, catchUp).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(item, f.Vm.Item);
        Assert.Equal(initiallySet, item.UserState.IsFavorite);
        Assert.Equal(initiallySet, item.UserState.InWatchlist);
        Assert.Equal(initiallySet, f.Vm.IsFavorite);
        Assert.Equal(initiallySet, f.Vm.InWatchlist);
        Assert.Equal(initiallySet ? (int?)4 : null, item.UserRating);
        Assert.Equal(item.UserRating, f.Vm.UserRating);
        Assert.Equal(1, f.Wire.DetailReads);
        Assert.Null(f.Vm.RemoteStateErrorMessage);
    }

    [Theory]
    [InlineData("movie", "watched", "favorite", false)]
    [InlineData("movie", "watched", "favorite", true)]
    [InlineData("movie", "progress", "rating", false)]
    [InlineData("movie", "progress", "rating", true)]
    [InlineData("episode", "watched", "favorite", false)]
    [InlineData("episode", "watched", "favorite", true)]
    [InlineData("episode", "progress", "rating", false)]
    [InlineData("episode", "progress", "rating", true)]
    public async Task SameLeafMixedBatchesAndMergedPlaybackScopeReconcilePlayback(
        string type, string playbackChange, string viewerChange, bool trailing)
    {
        using var f = new Fixture(); f.Seed();
        var item = f.Vm.Item!;
        item.ContentId = EpisodeA; item.Type = f.Wire.DetailType = type;
        item.UserData!.PositionSeconds = 31;
        var mountedPlayback = item.UserData;
        f.Vm.IsSeries = false;
        f.Vm.Seasons.Clear(); f.Vm.Episodes.Clear();
        f.Wire.Played = true; f.Wire.Count = 2; f.Wire.DetailPosition = 900;
        f.Wire.Target = EpisodeB; f.Wire.Favorite = true; f.Wire.Rating = 4;
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(f.Vm.Item) && f.Vm.IsWatched && item.UserData!.PositionSeconds == 900
                && f.Vm.IsFavorite && f.Vm.UserRating == 4)
                applied.TrySetResult();
        };
        if (trailing)
        {
            // A legacy targeted read already owns playback scope. Merging a
            // viewer-only batch must retain that scope in the trailing read.
            f.Wire.Played = false; f.Wire.Count = 0; f.Wire.DetailPosition = 31;
            f.Wire.Target = EpisodeA; f.Wire.Favorite = false; f.Wire.Rating = null;
            f.Wire.HoldDetails = true;
            var active = f.Vm.RefreshMediaStateAsync(f.Change(EpisodeA) with { CatchUp = true });
            var heldRead = await f.Wire.NextAsync();
            f.Wire.Played = true; f.Wire.Count = 2; f.Wire.DetailPosition = 900;
            f.Wire.Target = EpisodeB; f.Wire.Favorite = true; f.Wire.Rating = 4;
            f.Raw(EpisodeA, viewerChange); f.Clock.Advance(200);
            f.Wire.HoldDetails = false; heldRead.Release();
            await active.WaitAsync(TimeSpan.FromSeconds(5));
        }
        else
        {
            f.Raw(EpisodeA, playbackChange); f.Raw(EpisodeA, viewerChange);
            f.Clock.Advance(200);
        }
        await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(item, f.Vm.Item);
        Assert.NotSame(mountedPlayback, item.UserData);
        Assert.True(f.Vm.IsWatched);
        Assert.True(item.UserData!.Played);
        Assert.Equal(900, item.UserData.PositionSeconds);
        Assert.Equal(EpisodeB, item.PlayContentId);
        Assert.True(f.Vm.IsFavorite);
        Assert.Equal(4, f.Vm.UserRating);
        Assert.Equal(trailing ? 2 : 1, f.Wire.DetailReads);
        Assert.Equal(0, f.Wire.EpisodeReads);
        Assert.Null(f.Vm.RemoteStateErrorMessage);
    }

    [Theory]
    [InlineData("favorite", false)]
    [InlineData("favorite", true)]
    [InlineData("watchlist", false)]
    [InlineData("watchlist", true)]
    [InlineData("rating", false)]
    [InlineData("rating", true)]
    public async Task CurrentViewerWritePublishesItsCapturedItemAndContext(string mutation, bool initiallySet)
    {
        using var f = new Fixture(); f.Seed();
        f.Vm.Item!.SeriesId = "parent-series";
        f.Vm.IsFavorite = initiallySet; f.Vm.InWatchlist = initiallySet;
        f.Vm.UserRating = initiallySet ? 4 : null;
        f.Wire.WriteMethod = initiallySet ? HttpMethod.Delete : HttpMethod.Put;
        f.Wire.OnWrite = () => { };
        var context = f.Client.CaptureContext();
        var recipient = new object();
        var messages = new List<MediaSurfaceChanged>();
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(recipient, (_, message) => messages.Add(message));
        try
        {
            await (mutation == "favorite" ? f.Vm.ToggleFavoriteCommand.ExecuteAsync(null)
                : mutation == "watchlist" ? f.Vm.ToggleWatchlistCommand.ExecuteAsync(null)
                : f.Vm.SetRatingCommand.ExecuteAsync(4));
            var message = Assert.Single(messages);
            Assert.Equal(Series, message.ContentId);
            Assert.Equal("parent-series", message.SeriesId);
            Assert.Equal(context, message.Context);
            Assert.Equal(mutation == "rating" ? MediaSurfaceChangeKind.RatingChanged
                : mutation == "favorite" ? initiallySet ? MediaSurfaceChangeKind.FavoriteRemoved : MediaSurfaceChangeKind.FavoriteAdded
                : initiallySet ? MediaSurfaceChangeKind.WatchlistRemoved : MediaSurfaceChangeKind.WatchlistAdded, message.Kind);
            if (mutation == "rating")
            {
                int? expected = initiallySet ? null : 4;
                Assert.Equal(expected, message.Rating);
                Assert.Equal(expected, f.Vm.UserRating);
                Assert.Equal(expected, f.Vm.Item.UserRating);
            }
            else Assert.Equal(!initiallySet, mutation == "favorite" ? f.Vm.IsFavorite : f.Vm.InWatchlist);
        }
        finally { WeakReferenceMessenger.Default.UnregisterAll(recipient); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCurrentRatingWriteRollsBackWithoutPublishing(bool initiallySet)
    {
        using var f = new Fixture(); f.Seed();
        int? prior = initiallySet ? 4 : null;
        f.Vm.UserRating = prior;
        f.Wire.WriteMethod = initiallySet ? HttpMethod.Delete : HttpMethod.Put;
        f.Wire.WriteStatus = HttpStatusCode.Forbidden;
        f.Wire.OnWrite = () => { };
        var recipient = new object();
        var messages = new List<MediaSurfaceChanged>();
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(recipient, (_, message) => messages.Add(message));
        try
        {
            await f.Vm.SetRatingCommand.ExecuteAsync(4);
            Assert.Equal(prior, f.Vm.UserRating);
            Assert.Empty(messages);
        }
        finally { WeakReferenceMessenger.Default.UnregisterAll(recipient); }
    }

    [Theory]
    [InlineData("favorite", false)]
    [InlineData("favorite", true)]
    [InlineData("watchlist", false)]
    [InlineData("watchlist", true)]
    [InlineData("rating", false)]
    [InlineData("rating", true)]
    public async Task DepartedViewerWriteStillUpdatesCollectionsAndClearsPrefetchCache(string mutation, bool initiallySet)
    {
        using var f = new Fixture(); f.Seed();
        var catalog = new CatalogApi(f.Client);
        var favorites = new FavoritesViewModel(catalog);
        var watchlist = new WatchlistViewModel(catalog, new RequestsApi(f.Client));
        DetailCacheInvalidation.Attach(f.Cache);
        var revision = DetailCacheInvalidation.AttachUserStateRevision();
        try
        {
            var items = mutation == "favorite" ? favorites.Items : watchlist.Items;
            if (initiallySet && mutation != "rating") items.Add(new() { ContentId = Series, Type = "series" });
            f.Wire.DetailSeriesId = "parent-series";
            f.Vm.UserRating = f.Wire.Rating = initiallySet ? 4 : null;
            var cachedDetail = await f.Cache.GetAsync(Series, CancellationToken.None);
            f.Vm.Item = cachedDetail;
            f.Vm.IsFavorite = initiallySet; f.Vm.InWatchlist = initiallySet;
            f.Wire.WriteMethod = initiallySet ? HttpMethod.Delete : HttpMethod.Put;
            f.Wire.PendingWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Wire.OnWrite = () => started.TrySetResult();
            // Use another item so the detail VM's own per-item invalidation
            // cannot stand in for the app-lifetime cache subscription.
            var prefetched = await f.Cache.GetAsync(EpisodeB, CancellationToken.None);
            Assert.Same(prefetched, await f.Cache.GetAsync(EpisodeB, CancellationToken.None));
            var reads = f.Wire.DetailReads;
            var stamp = revision.Current;
            var operation = mutation == "favorite" ? f.Vm.ToggleFavoriteCommand.ExecuteAsync(null)
                : mutation == "watchlist" ? f.Vm.ToggleWatchlistCommand.ExecuteAsync(null)
                : f.Vm.SetRatingCommand.ExecuteAsync(4);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            f.Vm.CancelPendingLoads();
            if (mutation == "rating") f.Wire.Rating = initiallySet ? null : 4;
            f.Wire.PendingWrite.SetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            // Additions reload the retained collection; removals drop the item.
            if (mutation != "rating")
            {
                for (var n = 0; n < 50 && (items.Count != (initiallySet ? 0 : 1)
                    || favorites.IsLoading || watchlist.IsLoading); n++) await Task.Delay(10);
                if (initiallySet) Assert.Empty(items);
                else Assert.Equal(Series, Assert.Single(items).ContentId);
            }
            else
            {
                Assert.Empty(favorites.Items);
                Assert.Empty(watchlist.Items);
            }
            Assert.Null(favorites.ErrorMessage);
            Assert.Null(watchlist.ErrorMessage);
            Assert.Equal(initiallySet || mutation == "rating" ? 0 : 1, f.Wire.CollectionReads);
            Assert.True(revision.Current > stamp);
            Assert.False(revision.IsCurrent(stamp, Series, "parent-series"));
            Assert.NotSame(prefetched, await f.Cache.GetAsync(EpisodeB, CancellationToken.None));
            var freshDetail = await f.Cache.GetAsync(Series, CancellationToken.None);
            Assert.NotSame(cachedDetail, freshDetail);
            Assert.Equal(reads + 2, f.Wire.DetailReads);
            if (mutation == "rating")
            {
                Assert.Equal(initiallySet ? (int?)4 : null, cachedDetail.UserRating);
                Assert.Equal(initiallySet ? null : (int?)4, freshDetail.UserRating);
            }
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(favorites);
            WeakReferenceMessenger.Default.UnregisterAll(watchlist);
            WeakReferenceMessenger.Default.UnregisterAll(f.Cache);
            WeakReferenceMessenger.Default.UnregisterAll(revision);
        }
    }

    [Theory]
    [InlineData(MediaSurfaceChangeKind.FavoriteAdded)]
    [InlineData(MediaSurfaceChangeKind.WatchlistAdded)]
    [InlineData(MediaSurfaceChangeKind.RatingChanged)]
    public async Task QueuedCatchUpRetainsItsScopeWhenViewerStateChangesBeforeTheFirstOwnedRead(MediaSurfaceChangeKind kind)
    {
        var queued = new QueuedStateContext();
        var previous = SynchronizationContext.Current;
        Fixture f;
        Task refresh;
        try
        {
            SynchronizationContext.SetSynchronizationContext(queued);
            f = new Fixture(); f.Seed();
            f.Wire.Count = 2;
            f.Wire.Favorite = kind == MediaSurfaceChangeKind.FavoriteAdded;
            f.Wire.Watchlist = kind == MediaSurfaceChangeKind.WatchlistAdded;
            f.Wire.Rating = kind == MediaSurfaceChangeKind.RatingChanged ? 4 : null;
            refresh = f.Vm.RefreshMediaStateAsync(f.Change() with { CatchUp = true });
            f.Vm.Receive(new MediaSurfaceChanged(kind, Series, rating: 4, context: f.Client.CaptureContext()));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        using (f)
        {
            Assert.Equal(0, f.Wire.DetailReads);
            var mounted = f.Vm.Item;
            queued.Drain();
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(mounted, f.Vm.Item);
            Assert.Equal(2, f.Vm.Item!.UserData!.WatchedCount);
            Assert.Equal(2, f.Vm.Seasons[0].UserData!.WatchedCount);
            Assert.Equal(f.Wire.Favorite, f.Vm.IsFavorite);
            Assert.Equal(f.Wire.Watchlist, f.Vm.InWatchlist);
            Assert.Equal(f.Wire.Rating, f.Vm.UserRating);
            Assert.Equal(1, f.Wire.DetailReads);
        }
    }

    [Theory]
    [InlineData("favorite")]
    [InlineData("watchlist")]
    [InlineData("rating")]
    [InlineData("not-interested")]
    public async Task CatchUpRetainsItsScopeWhenViewerStateChangesDuringTheDetailRead(string mutation)
    {
        using var f = new Fixture(); f.Seed();
        f.Wire.HoldDetails = true;
        var refresh = f.Vm.RefreshMediaStateAsync(f.Change() with { CatchUp = true });
        var oldDetail = await f.Wire.NextAsync();
        f.Wire.OnWrite = () =>
        {
            f.Wire.Favorite = mutation == "favorite";
            f.Wire.Watchlist = mutation == "watchlist";
            f.Wire.Rating = mutation == "rating" ? 4 : null;
        };
        if (mutation == "favorite") await f.Vm.ToggleFavoriteCommand.ExecuteAsync(null);
        else if (mutation == "watchlist") await f.Vm.ToggleWatchlistCommand.ExecuteAsync(null);
        else if (mutation == "rating") await f.Vm.SetRatingCommand.ExecuteAsync(4);
        else
        {
            f.Wire.NotInterested = true;
            f.Vm.Receive(new NotInterestedChange(f.Client.CaptureContext(), Series, true));
        }
        f.Wire.HoldDetails = false; oldDetail.Release();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(mutation == "favorite", f.Vm.IsFavorite);
        Assert.Equal(mutation == "watchlist", f.Vm.InWatchlist);
        Assert.Equal(mutation == "rating" ? (int?)4 : null, f.Vm.UserRating);
        Assert.Equal(mutation == "not-interested", f.Vm.Item!.UserState!.NotInterested);
        Assert.Equal(2, f.Wire.DetailReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialCompanionRejectsAnInvalidatedCorrectiveReadAndCanRetry(bool seasons)
    {
        using var f = new Fixture(); f.Seed();
        f.Vm.Episodes.Clear();
        if (seasons) f.Vm.Seasons.Clear();
        f.Wire.HoldSeasons = seasons; f.Wire.HoldEpisodes = !seasons;
        var loading = seasons ? f.Vm.LoadSeasonsCommand.ExecuteAsync(null) : f.Vm.SelectSeasonCommand.ExecuteAsync(1);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var read = await f.Wire.NextAsync();
            f.Wire.Count++;
            f.Wire.Position += 100;
            await f.Vm.RefreshMediaStateAsync(f.Change()).WaitAsync(TimeSpan.FromSeconds(5));
            read.Release();
        }
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(f.Vm.IsSeasonsLoading);
        Assert.False(f.Vm.IsEpisodesLoading);
        Assert.Empty(f.Vm.Episodes);
        if (seasons) Assert.Empty(f.Vm.Seasons);
        Assert.True(seasons ? f.Vm.SeasonsLoadFailed : f.Vm.EpisodesLoadFailed);
        Assert.Equal(3, seasons ? f.Wire.SeasonReads : f.Wire.EpisodeReads);

        f.Wire.HoldSeasons = f.Wire.HoldEpisodes = false;
        await (seasons ? f.Vm.LoadSeasonsCommand.ExecuteAsync(null) : f.Vm.SelectSeasonCommand.ExecuteAsync(1))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(seasons ? f.Vm.SeasonsLoadFailed : f.Vm.EpisodesLoadFailed);
        Assert.Equal(f.Wire.Position, Assert.Single(f.Vm.Episodes).UserData!.PositionSeconds);
        if (seasons) Assert.Equal(f.Wire.Count, Assert.Single(f.Vm.Seasons).UserData!.WatchedCount);
        Assert.Equal(4, seasons ? f.Wire.SeasonReads : f.Wire.EpisodeReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialCompanionQueuesOneOwnedCorrectionAfterTwoInvalidatedReads(bool seasons)
    {
        using var f = new Fixture(); f.Seed();
        f.Vm.Episodes.Clear();
        if (seasons) f.Vm.Seasons.Clear();
        f.Wire.HoldSeasons = seasons; f.Wire.HoldEpisodes = !seasons;
        var loading = seasons ? f.Vm.LoadSeasonsCommand.ExecuteAsync(null) : f.Vm.SelectSeasonCommand.ExecuteAsync(1);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var read = await f.Wire.NextAsync();
            f.Wire.Count++;
            f.Wire.Position += 100;
            await f.Vm.RefreshMediaStateAsync(f.Change()).WaitAsync(TimeSpan.FromSeconds(5));
            read.Release();
        }

        var correctiveRead = f.Wire.NextAsync();
        Assert.Same(correctiveRead, await Task.WhenAny(loading, correctiveRead));
        var correction = await correctiveRead;
        Assert.Empty(f.Vm.Episodes);
        if (seasons) Assert.Empty(f.Vm.Seasons);
        Assert.True(seasons ? f.Vm.IsSeasonsLoading : f.Vm.IsEpisodesLoading);
        correction.Release();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, seasons ? f.Wire.SeasonReads : f.Wire.EpisodeReads);
        Assert.Equal(f.Wire.Position, Assert.Single(f.Vm.Episodes).UserData!.PositionSeconds);
        if (seasons) Assert.Equal(f.Wire.Count, Assert.Single(f.Vm.Seasons).UserData!.WatchedCount);
        Assert.False(seasons ? f.Vm.SeasonsLoadFailed : f.Vm.EpisodesLoadFailed);
        Assert.False(f.Vm.IsSeasonsLoading);
        Assert.False(f.Vm.IsEpisodesLoading);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostPlaybackSnapshotOwnsItsReadDuringRawRealtimeCacheInvalidation(bool scoped)
    {
        using var f = new Fixture();
        if (scoped) await f.Vm.LoadInLibraryAsync(Series, 9);
        f.Seed();
        var mounted = f.Vm.Item;
        var readsBefore = f.Wire.DetailReads;
        f.Wire.Count = 2; f.Wire.Played = true;
        f.Wire.DetailPosition = 999; f.Wire.Target = EpisodeB;
        // Deliver raw events inside every GET, without flushing a separate
        // mounted sweep that could mask a dropped post-playback refresh.
        f.Wire.BeforeDetailResponse = () =>
        {
            f.Raw(EpisodeB); f.Raw(EpisodeB); f.Raw(EpisodeB);
        };

        await f.Vm.RefreshWatchedStateAsync(Task.CompletedTask, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(readsBefore + 1, f.Wire.DetailReads);
        Assert.Equal(3L, f.Revision.Current);
        Assert.Same(mounted, f.Vm.Item);
        Assert.True(f.Vm.IsWatched);
        Assert.Equal(2, f.Vm.Item!.UserData!.WatchedCount);
        Assert.Equal(999, f.Vm.Item.UserData.PositionSeconds);
        Assert.Equal(EpisodeB, f.Vm.Item.PlayContentId);
        Assert.Equal(2, f.Vm.Seasons[0].UserData!.WatchedCount);
        Assert.All(f.Wire.CatalogUris, uri => Assert.Equal(scoped ? "?library_id=9" : "", uri.Query));
    }

    [Theory]
    [InlineData("movie", false, false)]
    [InlineData("movie", false, true)]
    [InlineData("movie", true, false)]
    [InlineData("episode", false, false)]
    [InlineData("episode", false, true)]
    [InlineData("episode", true, false)]
    public async Task OnlyPlaybackOwningSweepsSupersedeAnInFlightPostPlaybackRead(string type, bool viewerOnly, bool mixed)
    {
        using var f = new Fixture(); f.Seed();
        var mounted = f.Vm.Item!;
        mounted.ContentId = EpisodeA; mounted.Type = f.Wire.DetailType = type;
        f.Vm.IsSeries = false;
        f.Vm.Seasons.Clear(); f.Vm.Episodes.Clear();
        f.Wire.Count = 2; f.Wire.Played = true;
        f.Wire.DetailPosition = 999; f.Wire.Target = EpisodeB;
        f.Wire.HoldDetails = true;
        var postPlayback = f.Vm.RefreshWatchedStateAsync(Task.CompletedTask, CancellationToken.None);
        var localRead = await f.Wire.NextAsync();

        // Both targeted playback and mixed leaf batches supersede the older
        // playback owner; only viewer-only batches preserve its authority.
        f.Wire.Count = 3; f.Wire.Played = false;
        f.Wire.DetailPosition = 111; f.Wire.Target = EpisodeA; f.Wire.Favorite = true;
        f.Wire.HoldDetails = false;
        await f.Vm.RefreshMediaStateAsync(f.Change(EpisodeA) with
        {
            CatchUp = !viewerOnly,
            ViewerStateContentIds = new HashSet<string>(viewerOnly || mixed ? [EpisodeA] : []),
            PlaybackStateContentIds = new HashSet<string>(viewerOnly ? [] : [EpisodeA])
        }).WaitAsync(TimeSpan.FromSeconds(5));
        localRead.Release();
        await postPlayback.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(mounted, f.Vm.Item);
        var preservesPostPlayback = viewerOnly;
        Assert.Equal(preservesPostPlayback, f.Vm.IsWatched);
        Assert.Equal(preservesPostPlayback, mounted.UserData!.Played);
        Assert.Equal(preservesPostPlayback ? 2 : 3, mounted.UserData.WatchedCount);
        Assert.Equal(preservesPostPlayback ? 999 : 111, mounted.UserData.PositionSeconds);
        Assert.Equal(preservesPostPlayback ? EpisodeB : EpisodeA, mounted.PlayContentId);
        Assert.True(f.Vm.IsFavorite);
        Assert.True(mounted.UserState!.IsFavorite);
        Assert.Equal(2, f.Wire.DetailReads);
    }

    [Theory]
    [InlineData("series", false)]
    [InlineData("series", true)]
    [InlineData("season", false)]
    [InlineData("season", true)]
    public async Task PostPlaybackRefreshWaitsForAllSavesToSettleEvenWhenOneFailsOrIsCanceled(string type, bool canceledSave)
    {
        using var f = new Fixture(); f.Seed();
        if (type == "season")
        {
            f.Vm.Item!.Type = "season";
            f.Vm.Item.ContentId = "series:review-S01";
            f.Vm.Item.SeriesId = Series;
            f.Wire.DetailType = type; f.Wire.DetailSeriesId = Series;
        }
        f.Vm.Item!.EpisodeCount = 4;
        var mounted = f.Vm.Item;
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = f.Vm.RefreshWatchedStateAsync(Task.WhenAll(failed.Task, closing.Task), CancellationToken.None);
        if (canceledSave) failed.SetCanceled();
        else failed.SetException(new HttpRequestException("Synthetic retired-session save failure."));
        Assert.False(refresh.IsCompleted);
        Assert.Equal(0, f.Wire.DetailReads);
        Assert.Equal(0, f.Wire.SeasonReads);

        f.Wire.Count = 2; f.Wire.Played = true;
        f.Wire.DetailPosition = 999; f.Wire.Target = EpisodeB;
        closing.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(mounted, f.Vm.Item);
        Assert.Equal(1, f.Wire.DetailReads);
        Assert.Equal(8, mounted!.EpisodeCount);
        Assert.Equal(2, mounted.UserData!.WatchedCount);
        Assert.Equal(999, mounted.UserData.PositionSeconds);
        Assert.Equal(EpisodeB, mounted.PlayContentId);
        Assert.True(f.Vm.IsWatched);
        Assert.Equal(type == "series" ? 1 : 0, f.Wire.SeasonReads);
        if (type == "series") Assert.Equal(2, f.Vm.Seasons[0].UserData!.WatchedCount);
        Assert.Equal(31, f.Vm.Episodes[0].UserData!.PositionSeconds);
    }

    [Fact]
    public async Task CallerCancellationAbortsPostPlaybackRefreshWithoutWaitingForTheSave()
    {
        using var f = new Fixture(); f.Seed();
        using var cancellation = new CancellationTokenSource();
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = f.Vm.RefreshWatchedStateAsync(save.Task, cancellation.Token);
        try
        {
            cancellation.Cancel();
            await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(save.Task.IsCompleted);
            Assert.Equal(0, f.Wire.DetailReads);
            Assert.Equal(0, f.Wire.SeasonReads);
        }
        finally { save.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowMountedSnapshotsSettleDuringRawAndCoalescedTrafficWithProductionCacheRegistration(bool scoped)
    {
        using var f = new Fixture();
        if (scoped) await f.Vm.LoadInLibraryAsync(Series, 9);
        f.Seed();
        f.Wire.Count = 1; f.Wire.HoldDetails = true;
        var operation = f.Vm.RefreshMediaStateAsync(f.Change());
        for (var count = 1; count <= 3; count++)
        {
            var request = await f.Wire.NextAsync();
            f.Wire.Count = count + 1;
            f.Raw(EpisodeB); f.Clock.Advance(1000);
            request.Release();
            await f.WaitCountAsync(count);
        }
        var last = await f.Wire.NextAsync();
        f.Wire.HoldDetails = false; last.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, f.Vm.Item!.UserData!.WatchedCount);
        Assert.Equal(4, f.Wire.DetailReads - (scoped ? 1 : 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveEpisodeOrCatchUpScopeSurvivesATrailingUnknownEpisode(bool catchUp)
    {
        using var f = new Fixture(); f.Seed();
        f.Wire.HoldDetails = true;
        var operation = f.Vm.RefreshMediaStateAsync(f.Change(EpisodeA) with { CatchUp = catchUp });
        var active = await f.Wire.NextAsync();
        f.Wire.Count = 2; f.Wire.Position = 900;
        f.Raw(EpisodeB); f.Clock.Advance(1000);
        f.Wire.HoldDetails = false; active.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(900, Assert.Single(f.Vm.Episodes).UserData!.PositionSeconds);
        Assert.Equal(2, f.Vm.Item!.UserData!.WatchedCount);
        Assert.InRange(f.Wire.DetailReads, 1, 2);
        Assert.InRange(f.Wire.EpisodeReads, 1, 2);
    }

    [Fact]
    public async Task RetiredLegacyViewerFallbackCannotConsumeTheNewLoadsInitialInvalidation()
    {
        using var f = new Fixture();
        f.Wire.OmitViewerState = true; f.Wire.HoldPersonalListCapability = true;
        var retired = f.Vm.LoadInLibraryAsync(EpisodeA, 9);
        var fallback = await f.Wire.NextAsync();
        Assert.Equal(EpisodeA, f.Vm.Item!.ContentId);

        f.Wire.OmitViewerState = false; f.Wire.HoldDetails = true;
        var loading = f.Vm.LoadInLibraryAsync(Series, 9);
        var initial = await f.Wire.NextAsync();
        Assert.Null(f.Vm.Item);
        f.Wire.Count = 2; f.Wire.Target = EpisodeB; f.Wire.Favorite = true;
        f.Raw(Series, "watched"); f.Clock.Advance(200);
        Assert.False(retired.IsCompleted);

        // Let the old transport observe cancellation only after the new load
        // has retained its event. The fallback swallows that cancellation.
        fallback.Release();
        await retired.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(f.Vm.Item);
        Assert.True(f.Vm.IsLoading);
        f.Wire.HoldDetails = false; initial.Release();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Series, f.Vm.Item!.ContentId);
        Assert.Equal(2, f.Vm.Item.UserData!.WatchedCount);
        Assert.Equal(EpisodeB, f.Vm.Item.PlayContentId);
        Assert.True(f.Vm.IsFavorite);
        Assert.True(f.Vm.IsSeries);
        Assert.Equal(3, f.Wire.DetailReads);
        Assert.False(f.Vm.IsLoading);
        Assert.Null(f.Vm.ErrorMessage);
        Assert.Null(f.Vm.RemoteStateErrorMessage);
    }

    [Fact]
    public async Task EventWhileInitialScopedDetailIsNullCannotPublishItsOldCountsOrTarget()
    {
        using var f = new Fixture();
        f.Wire.HoldDetails = true;
        var loading = f.Vm.LoadInLibraryAsync(Series, 9);
        var initial = await f.Wire.NextAsync();
        Assert.Null(f.Vm.Item);
        f.Wire.Count = 2; f.Wire.Target = EpisodeB;
        f.Raw(EpisodeB, "watched"); f.Clock.Advance(1000);
        f.Wire.HoldDetails = false; initial.Release();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, f.Vm.Item!.UserData!.WatchedCount);
        Assert.Equal(EpisodeB, f.Vm.Item.PlayContentId);
        Assert.All(f.Wire.CatalogUris, uri => Assert.Equal("?library_id=9", uri.Query));
        Assert.InRange(f.Wire.DetailReads, 2, 3);
    }

    [Theory]
    [InlineData("movie", false)]
    [InlineData("movie", true)]
    [InlineData("episode", false)]
    [InlineData("episode", true)]
    [InlineData("series", false)]
    [InlineData("series", true)]
    [InlineData("season", false)]
    [InlineData("season", true)]
    public async Task FinalProgressDuringInitialLoadWaitsForSettlementAndReReadsLeavesAndContainers(string type, bool scoped)
    {
        using var f = new Fixture();
        var id = type is "movie" or "episode" ? EpisodeA : Series;
        f.Wire.DetailType = type;
        f.Wire.HoldDetails = true;
        var loading = f.Vm.LoadInLibraryAsync(id, scoped ? 9 : null);
        var initial = await f.Wire.NextAsync();
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Direct delivery also covers recipients without the global cache
        // registration: a null Item must still retain the local save barrier.
        f.Vm.Receive(new PlaybackProgressUpdated(EpisodeA, 999, 1000, true)
            { SeriesId = Series, Persisted = save.Task });
        f.Wire.HoldDetails = false;
        initial.Release();
        await initial.Settled.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        Assert.Null(f.Vm.Item);
        Assert.False(loading.IsCompleted);
        f.Wire.Count = 2; f.Wire.DetailPosition = 999; f.Wire.Played = true;
        save.SetResult();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, f.Vm.Item!.UserData!.WatchedCount);
        Assert.Equal(999, f.Vm.Item.UserData.PositionSeconds);
        Assert.True(f.Vm.IsWatched);
        Assert.Null(f.Vm.ErrorMessage);
        Assert.True(f.Wire.DetailReads >= 2);
        Assert.All(f.Wire.CatalogUris, uri => Assert.Equal(scoped ? "?library_id=9" : "", uri.Query));
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
    public async Task FinalSaveAtTheInitialCommitIsRetainedOnEitherSideOfItemAssignment(
        string type, bool scoped, bool beforeAssignment)
    {
        using var f = new Fixture();
        var id = type == "series" ? Series : "series:review-S01";
        f.Wire.DetailType = type;
        f.Wire.DetailSeriesId = type == "season" ? Series : null;
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = false;
        void Deliver(string? property)
        {
            if (delivered || property != nameof(f.Vm.Item)) return;
            delivered = true;
            if (beforeAssignment) Assert.Null(f.Vm.Item);
            else Assert.NotNull(f.Vm.Item);
            // Model the closing thread at the commit boundary without a
            // global cache recipient masking a lost VM-local barrier.
            f.Vm.Receive(new PlaybackProgressUpdated(EpisodeA, 999, 1000, true)
                { SeriesId = Series, Persisted = save.Task });
        }
        if (beforeAssignment) f.Vm.PropertyChanging += (_, e) => Deliver(e.PropertyName);
        else f.Vm.PropertyChanged += (_, e) => Deliver(e.PropertyName);

        await f.Vm.LoadInLibraryAsync(id, scoped ? 9 : null).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(delivered);
        Assert.False(save.Task.IsCompleted);
        Assert.Equal(1, f.Wire.DetailReads);
        Assert.Equal(0, f.Vm.Item!.UserData!.WatchedCount);
        if (type == "series")
            f.Vm.Seasons.Add(new() { ContentId = "series:review-S01", SeasonNumber = 1, EpisodeCount = 8, UserData = new() });
        var mounted = f.Vm.Item!;
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(f.Vm.Item) && mounted.UserData!.WatchedCount == 2)
                applied.TrySetResult();
        };
        f.Wire.Count = 2; f.Wire.Played = true; f.Wire.Target = EpisodeB;
        save.SetResult();
        await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(mounted, f.Vm.Item);
        Assert.True(f.Vm.IsWatched);
        Assert.Equal(2, mounted.UserData!.WatchedCount);
        Assert.Equal(EpisodeB, mounted.PlayContentId);
        if (type == "series") Assert.Equal(2, Assert.Single(f.Vm.Seasons).UserData!.WatchedCount);
        Assert.Null(f.Vm.RemoteStateErrorMessage);
        Assert.All(f.Wire.CatalogUris, uri => Assert.Equal(scoped ? "?library_id=9" : "", uri.Query));
    }

    [Theory]
    [InlineData("series", false)]
    [InlineData("series", true)]
    [InlineData("season", false)]
    [InlineData("season", true)]
    public async Task FinalSaveAfterInitialLoadRefreshesTheMountedAggregateWithoutAnIdleCallback(string type, bool scoped)
    {
        using var f = new Fixture();
        var id = type == "series" ? Series : "series:review-S01";
        f.Wire.DetailType = type;
        f.Wire.DetailSeriesId = type == "season" ? Series : null;
        await f.Vm.LoadInLibraryAsync(id, scoped ? 9 : null);
        if (type == "series")
            f.Vm.Seasons.Add(new() { ContentId = "series:review-S01", SeasonNumber = 1, EpisodeCount = 8, UserData = new() });
        var mounted = f.Vm.Item!;
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(f.Vm.Item) && mounted.UserData!.WatchedCount == 2)
                applied.TrySetResult();
        };
        f.Vm.Receive(new PlaybackProgressUpdated(EpisodeA, 999, 1000, true)
            { SeriesId = Series, Persisted = save.Task });
        // Give the bounded invalidation time to reach its save barrier.
        await Task.Delay(250);
        Assert.Equal(1, f.Wire.DetailReads);
        Assert.Equal(0, mounted.UserData!.WatchedCount);
        f.Wire.Count = 2; f.Wire.Played = true; f.Wire.Target = EpisodeB;
        save.SetResult();
        await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(mounted, f.Vm.Item);
        Assert.True(f.Vm.IsWatched);
        Assert.Equal(2, mounted.UserData.WatchedCount);
        Assert.Equal(EpisodeB, mounted.PlayContentId);
        if (type == "series") Assert.Equal(2, Assert.Single(f.Vm.Seasons).UserData!.WatchedCount);
        Assert.Null(f.Vm.RemoteStateErrorMessage);
        Assert.All(f.Wire.CatalogUris, uri => Assert.Equal(scoped ? "?library_id=9" : "", uri.Query));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalProgressPublishedBeforeTheDetailRecipientExistsHoldsTheInitialScopedRead(bool accessInvalidation)
    {
        using var f = new Fixture();
        f.Messenger.UnregisterAll(f.Vm);
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Messenger.Send(new PlaybackProgressUpdated(EpisodeB, 999, 1000, true)
            { SeriesId = Series, Persisted = save.Task });
        // Only the app-lifetime cache saw the message, as when navigation
        // creates a new detail VM after playback has already closed.
        if (accessInvalidation) f.Client.InvalidateAccessContext();
        var loading = f.Vm.LoadInLibraryAsync(Series, 9);
        Assert.False(loading.IsCompleted);
        Assert.Equal(0, f.Wire.DetailReads);
        f.Wire.Count = 2; f.Wire.Target = EpisodeB;
        save.SetResult();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, f.Vm.Item!.UserData!.WatchedCount);
        Assert.Equal(EpisodeB, f.Vm.Item.PlayContentId);
        Assert.Equal(1, f.Wire.DetailReads);
    }

    [Theory]
    [InlineData("season", false)]
    [InlineData("season", true)]
    [InlineData("episode", false)]
    [InlineData("episode", true)]
    public async Task SettledSeriesSaveDoesNotForceAnotherDetailReadWhenTheResponseRevealsItsSeries(string type, bool scoped)
    {
        using var f = new Fixture();
        f.Messenger.UnregisterAll(f.Vm);
        f.Messenger.Send(new PlaybackProgressUpdated(EpisodeB, 999, 1000, true)
            { SeriesId = Series, Persisted = Task.CompletedTask });
        await f.Cache.WaitForProgressSettlementAsync(CancellationToken.None);
        var id = type == "season" ? "series:review-S01" : EpisodeA;
        f.Wire.DetailType = type; f.Wire.DetailSeriesId = Series;
        // A hover-prefetched snapshot should paint without another transport;
        // scoped pages should likewise need only their one initial read.
        if (!scoped) await f.Cache.PrefetchAsync(id);
        f.Wire.BeforeDetailResponse = () => Assert.Equal(1, f.Wire.DetailReads);

        await f.Vm.LoadInLibraryAsync(id, scoped ? 9 : null).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, f.Wire.DetailReads);
        Assert.Equal(Series, f.Vm.Item!.SeriesId);
        Assert.False(f.Vm.IsLoading);
        Assert.Null(f.Vm.ErrorMessage);
        Assert.Null(f.Vm.RemoteStateErrorMessage);
    }

    [Theory]
    [InlineData("season", false)]
    [InlineData("season", true)]
    [InlineData("episode", false)]
    [InlineData("episode", true)]
    public async Task NewlyDiscoveredSeriesSaveReconcilesContainersButDoesNotBlockSiblingLeaves(string type, bool settlesDuringRead)
    {
        using var f = new Fixture();
        f.Messenger.UnregisterAll(f.Vm);
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Messenger.Send(new PlaybackProgressUpdated(EpisodeB, 999, 1000, true)
            { SeriesId = Series, Persisted = save.Task });
        var id = type == "season" ? "series:review-S01" : EpisodeA;
        f.Wire.DetailType = type; f.Wire.DetailSeriesId = Series; f.Wire.HoldDetails = true;
        var loading = f.Vm.LoadInLibraryAsync(id, 9);
        var initial = await f.Wire.NextAsync();
        try
        {
            f.Wire.Count = 2; f.Wire.Target = EpisodeB;
            if (settlesDuringRead)
            {
                save.SetResult();
                await f.Cache.WaitForProgressSettlementAsync(CancellationToken.None);
            }
            f.Wire.HoldDetails = false; initial.Release();
            if (type == "episode")
            {
                await loading.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(1, f.Wire.DetailReads);
                Assert.Equal(0, f.Vm.Item!.UserData!.WatchedCount);
                Assert.False(f.Vm.IsLoading);
                Assert.Null(f.Vm.RemoteStateErrorMessage);
                Assert.Equal(settlesDuringRead, save.Task.IsCompleted);
                return;
            }
            if (!settlesDuringRead)
            {
                await initial.Settled.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(loading.IsCompleted);
                Assert.Null(f.Vm.Item);
                save.SetResult();
            }
            await loading.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, f.Wire.DetailReads);
            Assert.Equal(2, f.Vm.Item!.UserData!.WatchedCount);
            Assert.Equal(EpisodeB, f.Vm.Item.PlayContentId);
            Assert.Null(f.Vm.RemoteStateErrorMessage);
        }
        finally { save.TrySetResult(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UnrelatedNeverSettlingSaveDoesNotDelayDetailNavigation(bool scoped, bool duringRead)
    {
        using var f = new Fixture();
        f.Wire.DetailType = "movie";
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new PlaybackProgressUpdated("episode:other", 99, 100, false)
            { SeriesId = "series:other", Persisted = save.Task };
        try
        {
            if (!duringRead) f.Messenger.Send(progress);
            f.Wire.HoldDetails = duringRead;
            var loading = f.Vm.LoadInLibraryAsync("movie:unrelated", scoped ? 9 : null);
            if (duringRead)
            {
                var initial = await f.Wire.NextAsync();
                f.Messenger.Send(progress);
                f.Wire.HoldDetails = false;
                initial.Release();
            }
            await loading.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(save.Task.IsCompleted);
            Assert.False(f.Vm.IsLoading);
            Assert.Equal("movie:unrelated", f.Vm.Item!.ContentId);
            Assert.Null(f.Vm.ErrorMessage);
            Assert.Null(f.Vm.RemoteStateErrorMessage);
        }
        finally { save.TrySetResult(); }
    }

    [Theory]
    [InlineData("movie", true)]
    [InlineData("series", true)]
    [InlineData("season", true)]
    [InlineData("episode", false)]
    public async Task SlowRelatedSaveHasABoundedLoadingBarrierAndAnOwnedPostSettlementRefresh(string type, bool beforeLoad)
    {
        using var f = new Fixture();
        var id = type is "movie" or "episode" ? EpisodeA : type == "season" ? "series:review-S01" : Series;
        f.Wire.DetailType = type;
        f.Wire.DetailSeriesId = type is "episode" or "season" ? Series : null;
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new PlaybackProgressUpdated(EpisodeA, 999, 1000, true)
            { SeriesId = Series, Persisted = save.Task };
        try
        {
            if (beforeLoad) f.Messenger.Send(progress);
            f.Wire.HoldDetails = !beforeLoad;
            var loading = f.Vm.LoadInLibraryAsync(id, 9);
            if (!beforeLoad)
            {
                var initial = await f.Wire.NextAsync();
                // Exercise the VM-local barrier without the shared cache recipient.
                f.Vm.Receive(progress);
                f.Wire.HoldDetails = false;
                initial.Release();
            }
            await loading.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.False(save.Task.IsCompleted);
            Assert.False(f.Vm.IsLoading);
            Assert.Equal(0, f.Vm.Item!.UserData!.WatchedCount);
            Assert.Null(f.Vm.ErrorMessage);
            var mounted = f.Vm.Item;
            f.Wire.Count = 2; f.Wire.DetailPosition = 999; f.Wire.Played = true;
            save.SetResult();
            await f.WaitCountAsync(2);
            Assert.Same(mounted, f.Vm.Item);
            Assert.Equal(999, mounted.UserData.PositionSeconds);
            Assert.True(f.Vm.IsWatched);
            Assert.Null(f.Vm.RemoteStateErrorMessage);
            Assert.All(f.Wire.CatalogUris, uri => Assert.Equal("?library_id=9", uri.Query));
        }
        finally { save.TrySetResult(); }
    }

    [Theory]
    [InlineData("movie", false)]
    [InlineData("episode", true)]
    [InlineData("series", false)]
    [InlineData("season", true)]
    public async Task FailedPostFinalSaveInitialReadKeepsTheSnapshotAndInlineRetryAlsoRecoversLeaves(string type, bool sharedSave)
    {
        using var f = new Fixture();
        var id = type is "movie" or "episode" ? EpisodeA : Series;
        f.Wire.DetailType = type;
        f.Wire.HoldDetails = true;
        var loading = f.Vm.LoadInLibraryAsync(id, 9);
        var initial = await f.Wire.NextAsync();
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new PlaybackProgressUpdated(EpisodeA, 999, 1000, true)
            { SeriesId = Series, Persisted = save.Task };
        if (sharedSave) f.Messenger.Send(progress);
        else f.Vm.Receive(progress);
        f.Wire.BeforeDetailResponse = () => throw new HttpRequestException("Synthetic post-save read failure.");
        f.Wire.HoldDetails = false;
        save.SetResult();
        initial.Release();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));

        var mounted = f.Vm.Item;
        Assert.NotNull(mounted);
        Assert.Equal(0, mounted.UserData!.WatchedCount);
        Assert.False(f.Vm.IsLoading);
        Assert.Null(f.Vm.ErrorMessage);
        Assert.Equal(DetailFailureKind.None, f.Vm.FailureKind);
        Assert.NotNull(f.Vm.RemoteStateErrorMessage);
        Assert.Equal(3, f.Wire.DetailReads);

        f.Wire.BeforeDetailResponse = null;
        f.Wire.Count = 2; f.Wire.DetailPosition = 999; f.Wire.Played = true;
        await f.Vm.RetryRemoteStateCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(mounted, f.Vm.Item);
        Assert.Equal(2, mounted.UserData.WatchedCount);
        Assert.Equal(999, mounted.UserData.PositionSeconds);
        Assert.True(f.Vm.IsWatched);
        Assert.Null(f.Vm.RemoteStateErrorMessage);
        Assert.Null(f.Vm.ErrorMessage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CorrectiveInitialReadRetriesAndPreservesTheSuccessfulSnapshotOnExhaustion(int failures)
    {
        using var f = new Fixture();
        f.Wire.HoldDetails = true;
        var loading = f.Vm.LoadInLibraryAsync(Series, 9);
        var initial = await f.Wire.NextAsync();
        f.Raw(EpisodeB);
        f.Wire.Count = 2;
        var attempts = 0;
        f.Wire.BeforeDetailResponse = () =>
        {
            if (++attempts <= failures) throw new HttpRequestException("Synthetic corrective read failure.");
        };
        f.Wire.HoldDetails = false; initial.Release();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(f.Vm.Item);
        Assert.Null(f.Vm.ErrorMessage);
        Assert.Equal(DetailFailureKind.None, f.Vm.FailureKind);
        Assert.False(f.Vm.IsLoading);
        Assert.Equal(3, f.Wire.DetailReads);
        Assert.Equal(failures == 1 ? 2 : 0, f.Vm.Item.UserData!.WatchedCount);
        Assert.Equal(failures == 2, f.Vm.RemoteStateErrorMessage != null);
    }

    [Fact]
    public async Task InitialDetailLoadCompletesWhileItsTrailingRealtimeReadIsStillPending()
    {
        using var f = new Fixture();
        f.Wire.HoldDetails = true;
        var loading = f.Vm.LoadInLibraryAsync(Series, 9);
        var initial = await f.Wire.NextAsync();
        f.Wire.Count = 1;
        f.Raw(EpisodeB); f.Clock.Advance(200);
        initial.Release();

        // A second event during the bounded replacement requires a trailing
        // sweep, but must not keep the already committed page on its skeleton.
        var replacement = await f.Wire.NextAsync();
        f.Wire.Count = 2;
        f.Raw(EpisodeB); f.Clock.Advance(200);
        replacement.Release();
        var trailing = await f.Wire.NextAsync();
        try
        {
            await loading.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(f.Vm.IsLoading);
            Assert.Equal(1, f.Vm.Item!.UserData!.WatchedCount);
            Assert.False(trailing.Pending.IsCompleted);
        }
        finally
        {
            f.Wire.HoldDetails = false;
            trailing.Release();
        }
        await f.WaitCountAsync(2);
        Assert.All(f.Wire.CatalogUris, uri => Assert.Equal("?library_id=9", uri.Query));
    }

    [Theory]
    [InlineData(MediaSurfaceChangeKind.WatchedMarked, true)]
    [InlineData(MediaSurfaceChangeKind.WatchedCleared, false)]
    [InlineData(MediaSurfaceChangeKind.FavoriteAdded, true)]
    [InlineData(MediaSurfaceChangeKind.FavoriteRemoved, false)]
    [InlineData(MediaSurfaceChangeKind.WatchlistAdded, true)]
    [InlineData(MediaSurfaceChangeKind.WatchlistRemoved, false)]
    [InlineData(MediaSurfaceChangeKind.RatingChanged, true)]
    [InlineData(MediaSurfaceChangeKind.RatingChanged, false)]
    public async Task AcceptedLocalFeedbackDuringLibraryDetailLoadCorrectsThePreWriteSnapshot(
        MediaSurfaceChangeKind kind, bool enabled)
    {
        using var f = new Fixture();
        f.Wire.DetailType = "movie";
        f.Wire.Played = f.Wire.Favorite = f.Wire.Watchlist = !enabled;
        f.Wire.Rating = enabled ? null : 4;
        f.Wire.HoldDetails = true;
        var loading = f.Vm.LoadInLibraryAsync(EpisodeA, 9);
        var initial = await f.Wire.NextAsync();
        Assert.Null(f.Vm.Item);
        f.Wire.Played = f.Wire.Favorite = f.Wire.Watchlist = enabled;
        f.Wire.Rating = enabled ? 4 : null;
        f.Vm.Receive(new MediaSurfaceChanged(kind, EpisodeA, rating: f.Wire.Rating,
            context: f.Client.CaptureContext()));
        f.Wire.HoldDetails = false;
        initial.Release();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, f.Wire.DetailReads);
        Assert.Equal(enabled, f.Vm.IsWatched);
        Assert.Equal(enabled, f.Vm.Item!.UserData!.Played);
        Assert.Equal(enabled, f.Vm.IsFavorite);
        Assert.Equal(enabled, f.Vm.Item.UserState!.IsFavorite);
        Assert.Equal(enabled, f.Vm.InWatchlist);
        Assert.Equal(enabled, f.Vm.Item.UserState.InWatchlist);
        Assert.Equal(f.Wire.Rating, f.Vm.UserRating);
        Assert.Equal(f.Wire.Rating, f.Vm.Item.UserRating);
        Assert.Null(f.Vm.RemoteStateErrorMessage);
        Assert.All(f.Wire.CatalogUris, uri => Assert.Equal("?library_id=9", uri.Query));
    }

    [Fact]
    public async Task RemoteChangeDuringInitialSeasonsReadRetriesStaleCounts()
    {
        using var f = new Fixture(); f.Seed();
        f.Vm.Seasons.Clear(); f.Vm.Episodes.Clear();
        f.Wire.HoldSeasons = true;
        var loading = f.Vm.LoadSeasonsCommand.ExecuteAsync(null);
        var initial = await f.Wire.NextAsync();
        var watchedRevision = f.Vm.WatchedStateRevision;
        f.Wire.Count = 2;
        try
        {
            await f.Vm.RefreshMediaStateAsync(f.Change(EpisodeB)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, f.Vm.Item!.UserData!.WatchedCount);
            Assert.Equal(watchedRevision, f.Vm.WatchedStateRevision);
            Assert.False(loading.IsCompleted);
        }
        finally
        {
            f.Wire.HoldSeasons = false; initial.Release();
            await loading.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(2, Assert.Single(f.Vm.Seasons).UserData!.WatchedCount);
        Assert.Equal(2, f.Wire.SeasonReads);
        Assert.False(f.Vm.IsWatched);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoteChangeDuringInitialEpisodeReadRetriesStaleRowState(bool autoSelect)
    {
        using var f = new Fixture(); f.Seed();
        f.Wire.HoldEpisodes = true;
        var loading = autoSelect ? f.Vm.LoadSeasonsCommand.ExecuteAsync(null) : f.Vm.SelectSeasonCommand.ExecuteAsync(1);
        var initial = await f.Wire.NextAsync();
        Assert.Empty(f.Vm.Episodes);
        var watchedRevision = f.Vm.WatchedStateRevision;
        f.Wire.Count = 2; f.Wire.Position = 900;
        try
        {
            await f.Vm.RefreshMediaStateAsync(f.Change(EpisodeA)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, f.Vm.Item!.UserData!.WatchedCount);
            Assert.Equal(2, Assert.Single(f.Vm.Seasons).UserData!.WatchedCount);
            Assert.Equal(watchedRevision, f.Vm.WatchedStateRevision);
            Assert.False(loading.IsCompleted);
        }
        finally
        {
            f.Wire.HoldEpisodes = false; initial.Release();
            await loading.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(900, Assert.Single(f.Vm.Episodes).UserData!.PositionSeconds);
        Assert.Equal(2, f.Wire.EpisodeReads);
        Assert.False(f.Vm.IsWatched);
    }

    [Fact]
    public async Task LocalFinalProgressOwnsTheRowOverAnOlderRemoteEpisodeSnapshot()
    {
        using var f = new Fixture(); f.Seed();
        f.Wire.Position = 40; f.Wire.HoldEpisodes = true;
        var operation = f.Vm.RefreshMediaStateAsync(f.Change(EpisodeA));
        var oldEpisodes = await f.Wire.NextAsync();
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Messenger.Send(new PlaybackProgressUpdated(EpisodeA, 900, 1000, false) { Persisted = save.Task });
        Assert.Equal(900, f.Vm.Episodes[0].UserData!.PositionSeconds);
        f.Wire.Position = 900; save.SetResult();
        f.Wire.HoldEpisodes = false; oldEpisodes.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(900, f.Vm.Episodes[0].UserData!.PositionSeconds);
    }

    [Theory]
    [InlineData("series", true)]
    [InlineData("series", false)]
    [InlineData("season", true)]
    [InlineData("season", false)]
    public async Task FinalSaveOutsideTheLoadedSeasonRetiresTheOldAggregateAndHoldsItsRetry(string type, bool namedSeries)
    {
        using var f = new Fixture(); f.Seed();
        if (type == "season")
        {
            f.Vm.Item!.Type = "season";
            f.Vm.Item.ContentId = "series:review-S02";
            f.Vm.Item.SeriesId = Series;
            // Keep backend identity aligned with the mounted season. An
            // explicit null series_id would intentionally retire its rows.
            f.Wire.DetailType = type;
            f.Wire.DetailSeriesId = Series;
        }
        f.Vm.Item!.PlayContentId = namedSeries ? EpisodeA : EpisodeB;
        f.Wire.Count = 1; f.Wire.HoldDetails = true;
        var operation = f.Vm.RefreshMediaStateAsync(f.Change());
        var oldDetail = await f.Wire.NextAsync();
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revision = f.Vm.WatchedStateRevision;
        f.Messenger.Send(new PlaybackProgressUpdated(EpisodeB, 900, 1000, false)
            { SeriesId = namedSeries ? Series : null, Persisted = save.Task });
        Assert.True(f.Vm.WatchedStateRevision > revision);
        Assert.Equal(31, Assert.Single(f.Vm.Episodes).UserData!.PositionSeconds);
        var retryWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Wire.BeforeDetailResponse = () =>
        {
            Assert.True(save.Task.IsCompleted, "The retry read began before the cross-season final save settled.");
            retryWaiting.SetResult();
        };
        f.Wire.HoldDetails = false;
        oldDetail.Release();

        // Let the retired read settle; the owned sweep must stay open at the save barrier.
        await oldDetail.Settled.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.IsCompleted);
        Assert.Equal(0, f.Vm.Item.UserData!.WatchedCount);
        f.Wire.Count = 2;
        save.SetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(retryWaiting.Task.IsCompleted);
        Assert.Equal(2, f.Wire.DetailReads);
        Assert.Equal(2, f.Vm.Item.UserData.WatchedCount);
        Assert.Equal(31, f.Vm.Episodes[0].UserData!.PositionSeconds);
    }

    [Fact]
    public async Task FailedFinalSaveThenSuccessfulSaveCannotPoisonLaterRemoteRefreshOrRestoreAnOldSnapshot()
    {
        using var f = new Fixture(); f.Seed();
        f.Wire.Count = 1; f.Wire.Position = 40; f.Wire.HoldEpisodes = true;
        var operation = f.Vm.RefreshMediaStateAsync(f.Change(EpisodeA));
        var oldEpisodes = await f.Wire.NextAsync();
        var failedSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Messenger.Send(new PlaybackProgressUpdated(EpisodeA, 900, 1000, false) { Persisted = failedSave.Task });
        failedSave.SetException(new HttpRequestException("Synthetic failed final progress save."));
        var successfulSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Messenger.Send(new PlaybackProgressUpdated(EpisodeA, 950, 1000, false) { Persisted = successfulSave.Task });
        f.Raw(EpisodeB); f.Clock.Advance(1000);
        f.Wire.HoldEpisodes = false; oldEpisodes.Release();
        Assert.Equal(950, f.Vm.Episodes[0].UserData!.PositionSeconds);
        f.Wire.Count = 2; f.Wire.Position = 950; successfulSave.SetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, f.Vm.Item!.UserData!.WatchedCount);
        Assert.Equal(950, f.Vm.Episodes[0].UserData!.PositionSeconds);
        Assert.Equal(2, f.Wire.DetailReads);
        // Recovery lasts beyond the first sweep; a later remote event still reads.
        f.Wire.Count = 3;
        await f.Vm.RefreshMediaStateAsync(f.Change(EpisodeB));
        Assert.Equal(3, f.Vm.Item.UserData.WatchedCount);
    }

    [Fact]
    public async Task SiblingTicksDoNotAbortRealWatchPreparationButOwnAndContainerChangesStillRetireIt()
    {
        using var f = new Fixture();
        var playback = new PlaybackApi(f.Client);
        var freshness = new WatchDetailFreshness<WatchDetailResponse>(f.Revision, TimeSpan.FromSeconds(1), d => d.ContentId, d => d.SeriesId);
        f.Wire.BeforeWatchResponse = () => f.Raw(EpisodeA);
        var sibling = await freshness.GetCurrentAsync(ct => freshness.ReadAsync(EpisodeB,
            token => playback.GetWatchDetailAsync(EpisodeB, token), ct), _ => { }, CancellationToken.None);
        Assert.Equal(EpisodeB, sibling.ContentId);
        Assert.Equal(1, f.Wire.WatchReads);
        f.Wire.BeforeWatchResponse = null;
        var own = await freshness.ReadAsync(EpisodeA, ct => playback.GetWatchDetailAsync(EpisodeA, ct), CancellationToken.None);
        f.Raw(EpisodeA);
        Assert.False(freshness.IsCurrent(own));
        var beforeContainer = await freshness.ReadAsync(EpisodeB, ct => playback.GetWatchDetailAsync(EpisodeB, ct), CancellationToken.None);
        f.Raw("series:review-S01", "watched");
        Assert.False(freshness.IsCurrent(beforeContainer));
    }

    [Fact]
    public async Task CoalescedRemoteReplayDoesNotExhaustOwnItemWatchPreparation()
    {
        using var f = new Fixture();
        var playback = new PlaybackApi(f.Client);
        var freshness = new WatchDetailFreshness<WatchDetailResponse>(f.Revision, TimeSpan.FromSeconds(1), d => d.ContentId, d => d.SeriesId);
        f.Wire.BeforeWatchResponse = () =>
        {
            if (f.Wire.WatchReads <= 2)
            {
                f.Wire.Position = f.Wire.WatchReads * 100;
                f.Raw(EpisodeA);
            }
            else f.Clock.Advance(200); // Replay both events during the last retry.
        };

        var detail = await freshness.GetCurrentAsync(ct => freshness.ReadAsync(EpisodeA,
            token => playback.GetWatchDetailAsync(EpisodeA, token), ct), _ => { }, CancellationToken.None);

        Assert.Equal(3, f.Wire.WatchReads);
        Assert.Equal(200, detail.UserData!.PositionSeconds);
        Assert.True(freshness.IsCurrent(detail));
        Assert.Equal(2L, f.Revision.Current);
    }

    [Fact]
    public async Task NewRemoteProgressDuringEachOfThreeWatchReadsStillRejectsKnownStalePositions()
    {
        using var f = new Fixture();
        var playback = new PlaybackApi(f.Client);
        var freshness = new WatchDetailFreshness<WatchDetailResponse>(f.Revision, TimeSpan.FromSeconds(1), d => d.ContentId, d => d.SeriesId);
        f.Wire.BeforeWatchResponse = () =>
        {
            f.Raw(EpisodeA);
            f.Clock.Advance(200);
        };

        var error = await Assert.ThrowsAsync<UserStateChangedException>(() => freshness.GetCurrentAsync(
            ct => freshness.ReadAsync(EpisodeA, token => playback.GetWatchDetailAsync(EpisodeA, token), ct),
            _ => { }, CancellationToken.None));

        Assert.Equal(EpisodeA, error.ContentId);
        Assert.Equal(3, f.Wire.WatchReads);
        Assert.Equal(3L, f.Revision.Current); // Each event advances once, not again on flush.
    }

    [Theory]
    [InlineData("movie", false)]
    [InlineData("movie", true)]
    [InlineData("episode", false)]
    [InlineData("episode", true)]
    public async Task DirectLeafEventsDuringInitialAndCorrectiveReadsAreNotLost(string type, bool trailingEvent)
    {
        using var f = new Fixture();
        f.Wire.DetailType = type;
        f.Wire.DetailSeriesId = type == "episode" ? Series : null;
        f.Wire.HoldDetails = true;
        var loading = f.Vm.LoadInLibraryAsync(EpisodeA, 9);
        var initial = await f.Wire.NextAsync();
        f.Wire.Count = 1; f.Wire.DetailPosition = 100;
        f.Raw(EpisodeA); f.Clock.Advance(200);
        initial.Release();
        var corrective = await f.Wire.NextAsync();
        if (trailingEvent)
        {
            f.Wire.Count = 2; f.Wire.DetailPosition = 900; f.Wire.Played = true;
            f.Raw(EpisodeA); f.Clock.Advance(200);
        }
        f.Wire.HoldDetails = false; corrective.Release();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        await f.WaitCountAsync(trailingEvent ? 2 : 1);
        Assert.Equal(trailingEvent ? 900 : 100, f.Vm.Item!.UserData!.PositionSeconds);
        Assert.Equal(trailingEvent, f.Vm.IsWatched);
        Assert.Equal(trailingEvent ? 3 : 2, f.Wire.DetailReads);
        Assert.Null(f.Vm.RemoteStateErrorMessage);
        Assert.All(f.Wire.CatalogUris, uri => Assert.Equal("?library_id=9", uri.Query));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NeverSettlingSiblingSaveDoesNotHoldAnEpisodeInTheSameSeries(bool scoped, bool duringRead)
    {
        using var f = new Fixture();
        f.Wire.DetailType = "episode"; f.Wire.DetailSeriesId = Series;
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new PlaybackProgressUpdated(EpisodeB, 99, 100, false)
            { SeriesId = Series, Persisted = save.Task };
        try
        {
            if (!duringRead) f.Messenger.Send(progress);
            f.Wire.HoldDetails = duringRead;
            var loading = f.Vm.LoadInLibraryAsync(EpisodeA, scoped ? 9 : null);
            if (duringRead)
            {
                var initial = await f.Wire.NextAsync();
                f.Messenger.Send(progress);
                f.Wire.HoldDetails = false; initial.Release();
            }
            await loading.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(save.Task.IsCompleted);
            Assert.False(f.Vm.IsLoading);
            Assert.Equal(EpisodeA, f.Vm.Item!.ContentId);
            Assert.Equal(Series, f.Vm.Item.SeriesId);
            Assert.Null(f.Vm.ErrorMessage);
            Assert.Null(f.Vm.RemoteStateErrorMessage);
            Assert.InRange(f.Wire.DetailReads, 1, scoped ? 1 : 2);
            // A later owned leaf retry must use that same leaf-only cache barrier.
            await f.Vm.RetryRemoteStateCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(save.Task.IsCompleted);
            Assert.Null(f.Vm.RemoteStateErrorMessage);
        }
        finally { save.TrySetResult(); }
    }

    [Fact]
    public async Task SaveJoiningAfterInnerSettlementStillBlocksSnapshotRevisionOwnership()
    {
        var queue = new ControlledStateContext();
        var priorContext = SynchronizationContext.Current;
        Fixture f;
        SynchronizationContext.SetSynchronizationContext(queue);
        try { f = new Fixture(); f.Seed(); }
        finally { SynchronizationContext.SetSynchronizationContext(priorContext); }
        using (f)
        {
            var firstSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Vm.Receive(new PlaybackProgressUpdated(EpisodeA, 100, 1000, false) { Persisted = firstSave.Task, SeriesId = Series });
            // Receive queues both the raw invalidation and the local coalescer
            // separately from the sweep. Retire both setup callbacks before
            // controlling the actual settlement await boundary.
            await queue.RunNextAsync(installContext: true);
            await queue.RunNextAsync(installContext: true);
            Task refresh;
            SynchronizationContext.SetSynchronizationContext(queue);
            try
            {
                using (ExecutionContext.SuppressFlow())
                    refresh = f.Vm.RefreshMediaStateAsync(f.Change());
            }
            finally { SynchronizationContext.SetSynchronizationContext(priorContext); }
            try
            {
                // Begin the sweep under its captured context and suspend the
                // inner settlement helper on the first real progress write.
                await queue.RunNextAsync(installContext: true);
                Assert.Equal(0, f.Wire.DetailReads);
                firstSave.SetResult();
                // Let the helper finish, but hold its caller's continuation.
                await queue.RunNextAsync(installContext: false);
                Assert.Equal(0, f.Wire.DetailReads);
                f.Vm.Receive(new PlaybackProgressUpdated(EpisodeA, 900, 1000, false) { Persisted = secondSave.Task, SeriesId = Series });
                await queue.RunNextAsync(installContext: false);
                Assert.Equal(0, f.Wire.DetailReads);
                Assert.False(refresh.IsCompleted);
            }
            finally
            {
                firstSave.TrySetResult(); secondSave.TrySetResult();
                while (!refresh.IsCompleted) await queue.RunNextAsync(installContext: true);
                await refresh.WaitAsync(TimeSpan.FromSeconds(5));
            }
            Assert.True(f.Wire.DetailReads > 0);
        }
    }

    private sealed class ControlledStateContext : SynchronizationContext
    {
        private readonly Channel<(SendOrPostCallback Callback, object? State)> _queue = Channel.CreateUnbounded<(SendOrPostCallback, object?)>();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Writer.TryWrite((callback, state));
        internal async Task WaitUntilPostedAsync() => Assert.True(await _queue.Reader.WaitToReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        internal async Task RunUntilCompleteAsync(Task operation)
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                while (!operation.IsCompleted)
                {
                    var posted = _queue.Reader.WaitToReadAsync(budget.Token).AsTask();
                    if (await Task.WhenAny(operation, posted) == operation) break;
                    Assert.True(await posted);
                    await RunNextAsync(installContext: false);
                }
                await operation.WaitAsync(budget.Token);
            }
            finally { budget.Cancel(); }
        }
        internal async Task RunNextAsync(bool installContext)
        {
            var work = await _queue.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var previous = Current;
            SetSynchronizationContext(installContext ? this : null);
            try
            {
                // The fixture controls context installation explicitly; a
                // captured ExecutionContext must not inline the next await.
                using (ExecutionContext.SuppressFlow()) work.Callback(work.State);
            }
            finally { SetSynchronizationContext(previous); }
        }
    }

    private sealed class QueuedStateContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue((callback, state));
        internal void Drain()
        {
            // Only hold the initial yield; subsequent asynchronous reads use
            // the caller's normal test context rather than an unpumped queue.
            while (_queue.TryDequeue(out var work)) work.Callback(work.State);
        }
    }

    private const string Series = "series:review";
    private const string EpisodeA = "episode:review-a";
    private const string EpisodeB = "episode:review-b";

    private sealed class Fixture : IDisposable
    {
        internal readonly Backend Wire = new();
        private readonly HttpClient _http;
        private readonly AuthService _auth;
        internal readonly SiloApiClient Client;
        internal readonly ItemDetailViewModel Vm;
        internal readonly WeakReferenceMessenger Messenger = new();
        internal readonly UserStateRevision Revision;
        internal readonly ItemDetailPrefetchCache Cache;
        internal readonly Clock Clock = new();
        private readonly RealtimeMediaStateRefresh _refresh;
        internal Fixture()
        {
            _http = new(Wire); Client = new(_http); Client.SetBaseUrl("https://review-owned.invalid"); Client.SetProfile("profile");
            _auth = new(Client, new AuthApi(Client));
            var catalog = new CatalogApi(Client);
            Cache = new ItemDetailPrefetchCache(catalog, _auth);
            DetailCacheInvalidation.Attach(Cache, Messenger);
            Revision = DetailCacheInvalidation.AttachUserStateRevision(Messenger);
            Vm = new(catalog, Cache);
            Messenger.Register<ItemDetailViewModel, MediaStateInvalidated>(Vm, static (vm, message) => vm.Receive(message));
            Messenger.Register<ItemDetailViewModel, PlaybackProgressUpdated>(Vm, static (vm, message) => vm.Receive(message));
            _refresh = new(Client, action => action(), Messenger, Clock);
        }
        internal void Seed()
        {
            Vm.Item = new() { ContentId = Series, Type = "series", EpisodeCount = 8, PlayContentId = EpisodeA, UserState = new(), UserData = new() };
            Vm.IsSeries = true; Vm.SelectedSeasonNumber = 1;
            Vm.Seasons.Add(new() { ContentId = "series:review-S01", SeasonNumber = 1, EpisodeCount = 8, UserData = new() });
            Vm.Episodes.Add(new() { ContentId = EpisodeA, SeasonNumber = 1, UserData = new() { PositionSeconds = 31 } });
        }
        internal MediaStateInvalidated Change(string id = EpisodeB) => new(Client.CaptureContext(), new HashSet<string>([id]), new HashSet<string>([Series]));
        internal void Raw(string id, string change = "progress")
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { profile_id = "profile", content_id = id, series_id = Series, change }));
            _refresh.ReceiveRealtime("user_state", "user_state.changed", json.RootElement);
        }
        internal async Task WaitCountAsync(int expected)
        {
            for (var n = 0; n < 50 && Vm.Item?.UserData?.WatchedCount != expected; n++) await Task.Delay(10);
            Assert.Equal(expected, Vm.Item!.UserData!.WatchedCount);
        }
        public void Dispose()
        {
            _refresh.Dispose(); Vm.CancelPendingLoads(); Wire.ReleaseAll();
            WeakReferenceMessenger.Default.UnregisterAll(Vm); _auth.Dispose(); _http.Dispose();
        }
    }

    private sealed class Backend : HttpMessageHandler
    {
        internal int Count, DetailReads, SeasonReads, EpisodeReads, WatchReads, CollectionReads;
        internal double Position = 31;
        internal double DetailPosition;
        internal string DetailType = "series";
        internal string? DetailSeriesId;
        internal bool Played;
        internal string? Target = EpisodeA;
        internal bool HoldDetails, HoldSeasons, HoldEpisodes, HoldViewerReads, OmitViewerState, HoldPersonalListCapability;
        internal bool Favorite, Watchlist, NotInterested;
        internal int? Rating;
        internal Action? OnWrite;
        internal TaskCompletionSource? PendingWrite;
        internal HttpStatusCode WriteStatus = HttpStatusCode.NoContent;
        internal HttpMethod WriteMethod = HttpMethod.Put;
        internal Action? BeforeWatchResponse;
        internal Action? BeforeDetailResponse;
        internal readonly List<Uri> CatalogUris = [];
        private readonly Channel<Read> _held = Channel.CreateUnbounded<Read>();
        private readonly List<Read> _all = [];
        internal Task<Read> NextAsync() => _held.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        internal void ReleaseAll()
        {
            PendingWrite?.TrySetResult();
            lock (_all) foreach (var read in _all) read.Release();
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("review-owned.invalid", request.RequestUri!.Host);
            if (request.Method != HttpMethod.Get && OnWrite is { } write)
            {
                Assert.Equal(WriteMethod, request.Method);
                Assert.Contains(request.RequestUri.AbsolutePath, new[]
                {
                    $"/api/v2/favorites/{Uri.EscapeDataString(Series)}",
                    $"/api/v2/watchlist/{Uri.EscapeDataString(Series)}",
                    $"/api/v2/ratings/{Uri.EscapeDataString(Series)}",
                    $"/api/v2/watched/{Uri.EscapeDataString(Series)}"
                });
                write();
                if (PendingWrite != null) await PendingWrite.Task.WaitAsync(ct);
                return new(WriteStatus);
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri.AbsolutePath;
            object body;
            var hold = false;
            if (path == "/api/v2/capabilities/personal-lists")
            {
                hold = HoldPersonalListCapability;
                body = new { membership_supported = !HoldPersonalListCapability };
            }
            else if (path.StartsWith("/api/v2/ratings/"))
            {
                hold = HoldViewerReads; body = new { rating = Rating };
            }
            else if (path.EndsWith("/membership"))
            {
                hold = HoldViewerReads; body = new { is_member = path.StartsWith("/api/v2/favorites/") ? Favorite : Watchlist };
            }
            else if (path == "/api/v2/catalog/search/capabilities")
            {
                // Movie details probe optional box sets. Capabilities are global;
                // only the actual detail/season/episode reads carry library scope.
                Assert.Equal("", request.RequestUri.Query);
                body = new { box_sets = false };
            }
            else if (path is "/api/v2/favorites" or "/api/v2/watchlist")
            {
                CollectionReads++;
                body = new { items = new[] { new { content_id = Series, type = "series" } } };
            }
            else if (path.StartsWith("/api/v2/watch/"))
            {
                WatchReads++; body = new { content_id = Uri.UnescapeDataString(path.Split('/').Last()), series_id = Series, type = "episode", user_data = new { position_seconds = Position } };
                BeforeWatchResponse?.Invoke();
            }
            else
            {
                CatalogUris.Add(request.RequestUri);
                if (path.EndsWith("/episodes"))
                {
                    EpisodeReads++; hold = HoldEpisodes;
                    body = new { items = new[] { new { content_id = EpisodeA, season_number = 1, user_data = new { position_seconds = Position, played = false } } } };
                }
                else if (path.EndsWith("/seasons"))
                {
                    SeasonReads++; hold = HoldSeasons;
                    body = new { items = new[] { new { content_id = "series:review-S01", season_number = 1, episode_count = 8, user_data = new { watched_count = Count } } } };
                }
                else
                {
                    Assert.StartsWith("/api/v2/catalog/items/", path);
                    DetailReads++; hold = HoldDetails;
                    BeforeDetailResponse?.Invoke();
                    body = new { content_id = Uri.UnescapeDataString(path.Split('/').Last()), type = DetailType, series_id = DetailSeriesId,
                        episode_count = 8, play_content_id = Target,
                        user_state = OmitViewerState ? null : new { is_favorite = Favorite, in_watchlist = Watchlist, not_interested = NotInterested },
                        user_rating = Rating, user_data = new { watched_count = Count, played = Played, position_seconds = DetailPosition } };
                }
            }
            var snapshot = JsonSerializer.Serialize(body);
            if (hold)
            {
                var read = new Read(); lock (_all) _all.Add(read); _held.Writer.TryWrite(read);
                try
                {
                    // Model a legacy fallback transport that settles after
                    // cancellation rather than completing on the cancel call.
                    if (path == "/api/v2/capabilities/personal-lists") await read.Pending;
                    else await read.Pending.WaitAsync(ct);
                }
                finally { read.MarkSettled(); }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(snapshot, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Read
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Pending => _ready.Task;
        internal Task Settled => _settled.Task;
        internal void Release() => _ready.TrySetResult();
        internal void MarkSettled() => _settled.TrySetResult();
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        private readonly List<Timer> _timers = [];
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period)
        { var timer = new Timer(this, callback, state); timer.Change(due, period); _timers.Add(timer); return timer; }
        internal void Advance(int milliseconds)
        {
            _now += TimeSpan.FromMilliseconds(milliseconds);
            foreach (var timer in _timers.Where(t => t.Due <= _now).ToArray()) timer.Fire();
        }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            internal DateTimeOffset Due = DateTimeOffset.MaxValue;
            public bool Change(TimeSpan due, TimeSpan period) { Due = clock._now + due; return true; }
            internal void Fire() { Due = DateTimeOffset.MaxValue; callback(state); }
            public void Dispose() => Due = DateTimeOffset.MaxValue;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
