using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;
namespace SiloPlayer.Tests;
public sealed class BoxSetOwnershipTests
{
    [Theory]
    [InlineData("refresh", false)] [InlineData("refresh", true)]
    [InlineData("access", false)] [InlineData("access", true)]
    [InlineData("language", false)] [InlineData("language", true)]
    public async Task SupersededPlaybackRevalidationCannotBorrowAnUnfinishedSuccessor(string change, bool denied)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12");
        var retained = vm.Detail;
        client.InvalidateAccessContext(); wire.Hold = true;
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRelease = wire.Release; var calls = 0;
        var play = vm.PlayAsync((_, _, _, _) => { calls++; return Task.CompletedTask; });
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (change == "access") client.InvalidateAccessContext();
        if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var successor = vm.RefreshAsync();
        try
        {
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(retained, vm.Detail); Assert.True(vm.IsLoading);
            oldRelease.TrySetResult();
            await Assert.ThrowsAsync<InvalidOperationException>(() => play.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(0, calls); Assert.True(vm.IsLoading);
            wire.AllPlayed = true;
            if (denied) wire.DetailStatus = HttpStatusCode.Forbidden;
            wire.Release.TrySetResult(); await successor.WaitAsync(TimeSpan.FromSeconds(2));
            if (denied)
            {
                Assert.Null(vm.Detail);
                await Assert.ThrowsAsync<InvalidOperationException>(() => vm.PlayAsync((_, _, _, _) => { calls++; return Task.CompletedTask; }));
                Assert.Equal(0, calls);
            }
            else
            {
                await vm.PlayAsync((id, library, owner, ct) =>
                { calls++; Assert.Equal("first", id); Assert.Equal(12, library); Assert.True(client.IsCurrentContext(owner)); Assert.False(ct.IsCancellationRequested); return Task.CompletedTask; });
                Assert.Equal(1, calls);
            }
        }
        finally { oldRelease.TrySetResult(); wire.Release.TrySetResult(); await successor; vm.Cancel(); }
    }

    [Fact]
    public async Task PlaybackStartedDuringBackgroundRefreshMustValidateItsOwnCompletedRead()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12");
        wire.Hold = true; wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshing = vm.RefreshAsync(); var release = wire.Release; var calls = 0;
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var play = vm.PlayAsync((id, _, owner, _) => { calls++; Assert.Equal("first", id); Assert.True(client.IsCurrentContext(owner)); return Task.CompletedTask; });
        try
        {
            Assert.Equal(0, calls);
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            wire.AllPlayed = true; release.TrySetResult(); wire.Release.TrySetResult();
            await Task.WhenAll(refreshing, play).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, calls);
        }
        finally { release.TrySetResult(); wire.Release.TrySetResult(); await refreshing; vm.Cancel(); }
    }

    [Theory]
    [InlineData(null, "other", "movie", false)] [InlineData(12, "other", "movie", false)]
    [InlineData(12, "film", "episode", false)] [InlineData(12, "film", "box_set", false)]
    [InlineData(12, "film", "", false)] [InlineData(null, "film", "movie", true)] [InlineData(12, "film", "movie", true)]
    public async Task MembershipMustContainTheRequestedRealMovie(int? library, string contentId, string type, bool valid)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var vm = new ItemDetailViewModel(new CatalogApi(Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(vm, library); await vm.RefreshBoxSetAsync(); Assert.NotNull(vm.BoxSet);
        wire.MembershipContentId = contentId; wire.MembershipType = type;
        try
        {
            await vm.RefreshBoxSetAsync();
            Assert.Equal(valid, vm.BoxSet != null);
            var membership = typeof(ItemDetailViewModel).GetField("_boxSetMembership", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(vm);
            Assert.Equal(valid, membership != null);
            Assert.Equal("film", vm.Item!.ContentId); Assert.Null(vm.ErrorMessage);
        }
        finally { vm.CancelPendingLoads(); }
    }

    [Theory]
    [InlineData("access", 503, false)]
    [InlineData("access", 503, true)]
    [InlineData("transport", 503, false)]
    [InlineData("profile", 503, false)]
    [InlineData("aba", 503, false)]
    [InlineData("library", 503, false)]
    [InlineData("item", 503, false)]
    [InlineData("access", 403, false)]
    [InlineData("access", 200, false)]
    public async Task MembershipRefreshRetainsOnlySameIdentityRowUntilAuthoritativeResult(string change, int status, bool capability)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var movie = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync(); var row = movie.BoxSet; Assert.NotNull(row);
        if (change == "access") client.InvalidateAccessContext();
        if (change == "transport") Assert.True(client.TryInvalidateAccessContext(client.CaptureContext(), out _));
        if (change is "profile" or "aba") client.SetProfile("other");
        if (change == "aba") client.SetProfile("alpha");
        if (change == "library") typeof(ItemDetailViewModel).GetProperty(nameof(movie.LibraryId))!.SetValue(movie, 2);
        if (change == "item") movie.Item = new MediaItemDetail { ContentId = "other", Type = "movie" };
        if (capability) wire.CapabilityStatus = (HttpStatusCode)status;
        else { wire.DetailStatus = (HttpStatusCode)status; wire.NoMembership = status == 200; }
        try
        {
            await movie.RefreshBoxSetAsync();
            if (change is "access" or "transport" && status == 503) Assert.Same(row, movie.BoxSet);
            else Assert.Null(movie.BoxSet);
        }
        finally { movie.CancelPendingLoads(); }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task FailedBackgroundReadMustRevalidateBeforeNewPlayback(bool recovers)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12");
        var previous = vm.Detail;
        client.InvalidateAccessContext(); wire.DetailStatus = HttpStatusCode.ServiceUnavailable;
        await vm.RefreshAsync();
        Assert.True(vm.IsCurrent); Assert.Same(previous, vm.Detail); Assert.Equal(DetailFailureKind.Transient, vm.Failure);
        var reads = wire.DetailReads; var calls = 0;
        wire.AllPlayed = true; if (recovers) wire.DetailStatus = HttpStatusCode.OK;
        try
        {
            if (recovers)
                await vm.PlayAsync((id, library, _, _) =>
                { calls++; Assert.Equal("first", id); Assert.Equal(12, library); return Task.CompletedTask; });
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => vm.PlayAsync((_, _, _, _) =>
                { calls++; return Task.CompletedTask; }));
            Assert.True(wire.DetailReads > reads);
            Assert.Equal(recovers ? 1 : 0, calls);
        }
        finally { vm.Cancel(); }
    }

    [Theory]
    [InlineData("access")][InlineData("profile")][InlineData("aba")]
    [InlineData("departure")][InlineData("library")][InlineData("item")]
    public async Task MembershipPostSaveRefreshKeepsStableViewerIdentityAndVisit(string change)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(vm); await vm.RefreshBoxSetAsync(); Assert.Equal(1, vm.BoxSet?.PlayedCount);
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = (Task)typeof(ItemDetailViewModel).GetMethod("RefreshBoxSetAfterPlaybackAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(vm,
                [new SiloPlayer.Messaging.PlaybackProgressUpdated("film", 100, 100, true) { Persisted = saved.Task }])!;
        var reads = wire.DetailReads;
        if (change == "access") client.InvalidateAccessContext();
        if (change is "profile" or "aba") client.SetProfile("other");
        if (change == "aba") client.SetProfile("alpha");
        if (change == "departure") vm.CancelPendingLoads();
        if (change == "library") typeof(ItemDetailViewModel).GetProperty(nameof(vm.LibraryId))!.SetValue(vm, 2);
        if (change == "item") vm.Item = new MediaItemDetail { ContentId = "other", Type = "movie" };
        wire.AllPlayed = true; saved.SetResult();
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(reads + (change == "access" ? 1 : 0), wire.DetailReads);
            if (change == "access") Assert.Equal(2, vm.BoxSet?.PlayedCount);
        }
        finally { saved.TrySetResult(); vm.CancelPendingLoads(); }
    }

    [Fact] public async Task MoviePublishesBeforeOptionalRowAndDepartureRetiresIt()
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire); var client = Client(http);
        var vm = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((id, _) => Task.FromResult(new MediaItemDetail { ContentId = id, Type = "movie", UserState = new() })));
        await vm.LoadInLibraryAsync("film", 12).WaitAsync(TimeSpan.FromSeconds(2));
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("film", vm.Item!.ContentId); Assert.False(vm.IsLoading); Assert.Null(vm.BoxSet);
        vm.CancelPendingLoads(); wire.Release.TrySetResult(); await Task.Delay(25); Assert.Null(vm.BoxSet);
    }
    [Fact] public async Task DepartureCannotBeRevivedByQueuedRefresh()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var set = new BoxSetViewModel(api); await set.LoadAsync("7", "12"); set.Cancel();
        var reads = wire.DetailReads; await set.RefreshAsync(); Assert.Equal(reads, wire.DetailReads); Assert.Null(set.Detail);
    }
    [Fact] public async Task DepartureCannotBeRevivedByQueuedRefreshForMovie()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var movie = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync();
        movie.CancelPendingLoads(); var reads = wire.DetailReads;
        await movie.RefreshBoxSetAsync(); Assert.Equal(reads, wire.DetailReads); Assert.Null(movie.BoxSet);
    }
    [Fact] public async Task PlaybackWriteArrivingDuringHeldMembershipReadMustRefreshAfterPersistence()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var movie = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync();
        wire.Hold = true; wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var visible = movie.BoxSet;
        var refreshing = movie.RefreshBoxSetAsync(); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); Assert.Same(visible, movie.BoxSet);
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        movie.Receive(new SiloPlayer.Messaging.PlaybackProgressUpdated("first", 100, 100, true) { Persisted = persisted.Task });
        wire.Hold = false; wire.Release.TrySetResult(); await refreshing;
        wire.AllPlayed = true; var reads = wire.DetailReads; persisted.TrySetResult();
        for (var i = 0; i < 100 && (wire.DetailReads == reads || movie.BoxSet?.PlayedCount != 2); i++) await Task.Delay(10);
        Assert.True(wire.DetailReads > reads, "A playback message received during a held row refresh still owns a post-write refresh.");
        Assert.Equal(2, movie.BoxSet!.PlayedCount); movie.CancelPendingLoads();
    }
    [Fact] public async Task QueuedRefreshCannotSuppressPersistedPlaybackState()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var movie = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync();
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
        SetMovie(movie); await movie.RefreshBoxSetAsync();
        Assert.Null(movie.BoxSet); Assert.Equal(0, wire.DetailReads); movie.CancelPendingLoads();
    }
    [Fact] public async Task OptionalMembershipFailureKeepsOrdinaryMovieUsable()
    {
        using var wire = new Wire { DetailStatus = HttpStatusCode.ServiceUnavailable }; using var http = new HttpClient(wire);
        var movie = new ItemDetailViewModel(new CatalogApi(Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync();
        Assert.Equal("film", movie.Item!.ContentId); Assert.Null(movie.BoxSet); Assert.Null(movie.ErrorMessage); movie.CancelPendingLoads();
    }
    [Fact] public async Task DisabledLibraryRetiresAlreadyVisibleRow()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(vm);
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
        SetMovie(vm);
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
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12");
        var calls = 0;
        await vm.PlayAsync((id, library, owner, ct) => { calls++; Assert.Equal(allPlayed ? "first" : "second", id); Assert.Equal(12, library); Assert.True(client.IsCurrentContext(owner)); Assert.False(ct.IsCancellationRequested); return Task.CompletedTask; });
        Assert.Equal(1, calls); vm.Cancel();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.PlayAsync((_, _, _, _) => { calls++; return Task.CompletedTask; }));
        Assert.Contains("Reopen", error.Message); Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData("2", true, true)]
    [InlineData("12", true, false)]
    [InlineData("12", false, false)]
    [InlineData("99", true, false)]
    [InlineData("0", true, false)]
    [InlineData("not-a-library", true, false)]
    public async Task DedicatedDetailValidatesTheReturnedLibraryBeforePublishingOrPlaying(string returned, bool enabled, bool allowed)
    {
        using var wire = new Wire { ReturnedLibrary = returned, Enabled = enabled };
        using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "2");
        var calls = 0;
        if (allowed)
        {
            Assert.Equal(returned, vm.Detail?.LibraryId); Assert.Equal(DetailFailureKind.None, vm.Failure);
            await vm.PlayAsync((_, library, _, _) => { calls++; Assert.Equal(2, library); return Task.CompletedTask; });
            Assert.Equal(1, calls);
        }
        else
        {
            Assert.Null(vm.Detail); Assert.Equal(DetailFailureKind.AccessDenied, vm.Failure);
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.PlayAsync((_, _, _, _) => { calls++; return Task.CompletedTask; }));
            Assert.Equal(0, calls);
        }
        Assert.False(vm.IsLoading); vm.Cancel();
    }

    [Theory]
    [InlineData("8", "12")]
    [InlineData("", "12")]
    [InlineData("7", "2")]
    [InlineData("8", "2")]
    public async Task DedicatedDetailRejectsMismatchedResponseOnLoadAndRefresh(string returnedId, string returnedLibrary)
    {
        using var wire = new Wire { ReturnedId = returnedId, ReturnedLibrary = returnedLibrary };
        using var http = new HttpClient(wire);
        var vm = new BoxSetViewModel(new CatalogApi(Client(http)));
        var calls = 0;
        async Task AssertRejected()
        {
            Assert.Null(vm.Detail); Assert.Equal(DetailFailureKind.AccessDenied, vm.Failure); Assert.False(vm.IsLoading);
            await Assert.ThrowsAsync<InvalidOperationException>(() => vm.PlayAsync((_, _, _, _) => { calls++; return Task.CompletedTask; }));
            Assert.Equal(0, calls);
        }
        try
        {
            await vm.LoadAsync("7", "12");
            await AssertRejected();
            wire.ReturnedId = "7"; wire.ReturnedLibrary = "12";
            await vm.RefreshAsync();
            Assert.NotNull(vm.Detail); Assert.Equal(DetailFailureKind.None, vm.Failure);
            wire.ReturnedId = returnedId; wire.ReturnedLibrary = returnedLibrary;
            await vm.RefreshAsync();
            await AssertRejected();
        }
        finally { vm.Cancel(); }
    }

    [Theory] [InlineData("profile")] [InlineData("access")] [InlineData("language")]
    public async Task RetiredActiveReadReleasesItsOwnLoadingWithoutPublishingStaleDetail(string change)
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); var read = vm.LoadAsync("7", "12");
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(vm.IsLoading);
        if (change == "profile") client.SetProfile("other");
        if (change == "access") client.InvalidateAccessContext();
        if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        wire.Release.TrySetResult(); await read.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(vm.IsLoading); Assert.Null(vm.Detail); Assert.Equal(DetailFailureKind.Transient, vm.Failure); vm.Cancel();
    }

    [Fact] public async Task RetiredOlderReadDoesNotReleaseTheNewerRequestLoading()
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); var old = vm.LoadAsync("7", "12");
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var oldRelease = wire.Release;
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = vm.RefreshAsync(); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        oldRelease.TrySetResult(); await old.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(vm.IsLoading); Assert.Null(vm.Detail);
        wire.Release.TrySetResult(); await current.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(vm.IsLoading); Assert.NotNull(vm.Detail); vm.Cancel();
    }

    [Fact] public async Task PrimaryActionRevalidatesAccessAndUsesTheCurrentTargetAndReadOwner()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12");
        client.InvalidateAccessContext(); wire.AllPlayed = true;
        Assert.False(vm.IsCurrent); Assert.True(vm.CanPlay);
        var reads = wire.DetailReads; var calls = 0;
        await vm.PlayAsync((id, library, owner, ct) =>
        {
            calls++; Assert.Equal("first", id); Assert.Equal(12, library);
            Assert.True(client.IsCurrentContext(owner)); Assert.False(ct.IsCancellationRequested);
            return Task.CompletedTask;
        });
        Assert.Equal(1, calls); Assert.True(wire.DetailReads > reads); Assert.True(vm.IsCurrent); vm.Cancel();
    }
    [Fact] public async Task FailedAccessRevalidationReportsARetryablePrimaryActionFailure()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12");
        client.InvalidateAccessContext(); wire.DetailStatus = HttpStatusCode.ServiceUnavailable;
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.PlayAsync((_, _, _, _) => { calls++; return Task.CompletedTask; }));
        Assert.Equal(0, calls); Assert.Equal(DetailFailureKind.Transient, vm.Failure);
        wire.DetailStatus = HttpStatusCode.OK; await vm.RefreshAsync(); Assert.True(vm.CanPlay); vm.Cancel();
    }
    [Fact] public async Task PrimaryActionCannotAdoptADifferentViewer()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12");
        client.SetProfile("other"); var calls = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.PlayAsync((_, _, _, _) => { calls++; return Task.CompletedTask; }));
        Assert.Contains("Reopen", error.Message); Assert.False(vm.CanPlay); Assert.Equal(0, calls); vm.Cancel();
    }
    [Theory] [InlineData("profile")] [InlineData("departure")] [InlineData("language")]
    public async Task PrimaryActionReportsRetirementDuringAccessRevalidation(string change)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12");
        client.InvalidateAccessContext(); wire.Hold = true;
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var play = vm.PlayAsync((_, _, _, _) => { calls++; return Task.CompletedTask; });
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (change == "profile") client.SetProfile("other");
        if (change == "departure") vm.Cancel();
        if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        wire.Release.TrySetResult();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => play);
        Assert.Contains("Reopen", error.Message); Assert.Equal(0, calls); vm.Cancel();
    }
    [Fact] public async Task DedicatedFailureAllowsRetryButUnavailableIsDistinct()
    {
        using var wire = new Wire { DetailStatus = HttpStatusCode.ServiceUnavailable }; using var http = new HttpClient(wire);
        var vm = new BoxSetViewModel(new CatalogApi(Client(http))); await vm.LoadAsync("7", "12"); Assert.Equal(DetailFailureKind.Transient, vm.Failure);
        wire.DetailStatus = HttpStatusCode.NotFound; await vm.RefreshAsync(); Assert.Equal(DetailFailureKind.NotFound, vm.Failure);
        wire.DetailStatus = HttpStatusCode.Forbidden; await vm.RefreshAsync(); Assert.Equal(DetailFailureKind.AccessDenied, vm.Failure); Assert.Null(vm.Detail); Assert.False(vm.IsLoading);
        wire.DetailStatus = HttpStatusCode.OK; await vm.RefreshAsync(); Assert.NotNull(vm.Detail); Assert.Equal(DetailFailureKind.None, vm.Failure); vm.Cancel();
    }
    [Fact] public async Task CapabilityFaultIsRetryableOnDedicatedDestination()
    {
        using var wire = new Wire { CapabilityStatus = HttpStatusCode.ServiceUnavailable }; using var http = new HttpClient(wire);
        var vm = new BoxSetViewModel(new CatalogApi(Client(http)));
        await vm.LoadAsync("7", "12");
        Assert.Equal(DetailFailureKind.Transient, vm.Failure); Assert.False(vm.IsLoading); Assert.Null(vm.Detail);
        wire.CapabilityStatus = HttpStatusCode.OK; await vm.RefreshAsync();
        Assert.NotNull(vm.Detail); Assert.Equal(DetailFailureKind.None, vm.Failure); vm.Cancel();
    }
    [Theory] [InlineData(404)] [InlineData(200)]
    public async Task ExplicitlyUnsupportedCapabilityIsUnavailable(int status)
    {
        using var wire = new Wire { CapabilityStatus = (HttpStatusCode)status, Supported = false }; using var http = new HttpClient(wire);
        var vm = new BoxSetViewModel(new CatalogApi(Client(http))); await vm.LoadAsync("7", "12");
        Assert.Equal(DetailFailureKind.NotFound, vm.Failure); Assert.Equal(0, wire.DetailReads); vm.Cancel();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task TransportTimeoutIsNotCallerCancellation(bool capability)
    {
        using var wire = new Wire { TimeoutDetail = !capability, TimeoutCapability = capability }; using var http = new HttpClient(wire);
        var vm = new BoxSetViewModel(new CatalogApi(Client(http))); await vm.LoadAsync("7", "12");
        Assert.Equal(DetailFailureKind.Transient, vm.Failure); Assert.False(vm.IsLoading); Assert.Null(vm.Detail);
        wire.TimeoutDetail = wire.TimeoutCapability = false; await vm.RefreshAsync(); Assert.NotNull(vm.Detail); vm.Cancel();
    }
    [Fact] public async Task BackgroundFailureKeepsSuccessfulDetailAndRow()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var set = new BoxSetViewModel(api); await set.LoadAsync("7", "12"); var visible = set.Detail;
        var movie = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync(); var row = movie.BoxSet;
        wire.DetailStatus = HttpStatusCode.ServiceUnavailable;
        await set.RefreshAsync(); await movie.RefreshBoxSetAsync();
        Assert.NotNull(visible); Assert.Same(visible, set.Detail); Assert.Equal(DetailFailureKind.Transient, set.Failure);
        Assert.NotNull(row); Assert.Same(row, movie.BoxSet);
        set.Cancel(); movie.CancelPendingLoads();
    }
    [Fact] public async Task SuccessfulEmptyMembershipRetiresVisibleRow()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var movie = new ItemDetailViewModel(new CatalogApi(Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync(); Assert.NotNull(movie.BoxSet);
        wire.NoMembership = true; await movie.RefreshBoxSetAsync(); Assert.Null(movie.BoxSet); movie.CancelPendingLoads();
    }
    [Fact] public async Task AcceptedStartSurvivesRefreshAndLanguageReadButNotDeparture()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken accepted = default; var completed = false;
        var start = vm.PlayAsync(async (_, _, _, ct) => { accepted = ct; await release.Task.WaitAsync(ct); completed = true; });
        await vm.RefreshAsync();
        client.LocalizedReads.NotifySettingChanged("catalog.metadata_language"); await vm.RefreshAsync();
        Assert.False(accepted.IsCancellationRequested);
        release.TrySetResult(); await start; Assert.True(completed);
        vm.Cancel(); Assert.True(accepted.IsCancellationRequested);
    }
    [Theory] [InlineData(200)] [InlineData(503)]
    public async Task AccessRevisionDuringAcceptedStartKeepsItsTokenAndMountedDetail(int status)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12");
        var visible = vm.Detail; Assert.NotNull(visible);
        var detailCleared = false;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.Detail) && vm.Detail is null) detailCleared = true;
        };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken accepted = default; var completed = false;
        var start = vm.PlayAsync(async (_, _, _, ct) => { accepted = ct; await release.Task.WaitAsync(ct); completed = true; });
        client.InvalidateAccessContext();
        wire.Hold = true; wire.DetailStatus = (HttpStatusCode)status;
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshing = vm.RefreshAsync();
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(accepted.IsCancellationRequested); Assert.Same(visible, vm.Detail); Assert.True(vm.IsLoading);
        wire.Release.TrySetResult(); await refreshing;
        Assert.False(accepted.IsCancellationRequested); Assert.False(detailCleared); Assert.NotNull(vm.Detail);
        Assert.True(vm.IsCurrent); Assert.True(vm.CanPlay); Assert.False(vm.IsLoading);
        Assert.Equal(status == 200 ? DetailFailureKind.None : DetailFailureKind.Transient, vm.Failure);
        if (status != 200) Assert.Same(visible, vm.Detail);
        release.TrySetResult(); await start.WaitAsync(TimeSpan.FromSeconds(2)); Assert.True(completed);
        vm.Cancel(); Assert.True(accepted.IsCancellationRequested);
    }
    [Fact] public async Task AuthorityChangeRetiresAcceptedStart()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12");
        CancellationToken accepted = default;
        await vm.PlayAsync((_, _, _, ct) => { accepted = ct; return Task.CompletedTask; });
        client.SetProfile("other"); await vm.RefreshAsync(); Assert.True(accepted.IsCancellationRequested); vm.Cancel();
    }
    [Theory] [InlineData(null, 1)] [InlineData(12, 0)]
    public async Task UnscopedOrDisabledMovieCannotBorrowAnotherLibraryFlag(int? library, int expectedReads)
    {
        using var wire = new Wire { Enabled = false }; using var http = new HttpClient(wire);
        var movie = new ItemDetailViewModel(new CatalogApi(Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie, library); await movie.RefreshBoxSetAsync();
        Assert.Null(movie.BoxSet); Assert.Equal(expectedReads, wire.DetailReads); movie.CancelPendingLoads();
    }
    [Fact] public async Task UnscopedMovieLoadDiscoversMembershipWithoutInventingALibraryScope()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var movie = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((id, ct) => api.GetItemDetailAsync(id, ct)));
        await movie.LoadInLibraryAsync("film", null);
        await movie.RefreshBoxSetAsync();
        Assert.Equal("film", movie.Item!.ContentId); Assert.Null(movie.LibraryId);
        Assert.Equal("12", movie.BoxSet?.LibraryId);
        Assert.Equal("", wire.LastMembershipQuery); Assert.Equal("?include_hidden=true", wire.LastLibraryQuery);
        movie.CancelPendingLoads();
    }
    [Theory] [InlineData("2", true)] [InlineData("12", true)] [InlineData("99", false)] [InlineData("invalid", false)] [InlineData("0", false)]
    public async Task UnscopedResponseMustBelongToAnEligibleViewerLibrary(string returnedLibrary, bool visible)
    {
        using var wire = new Wire { ReturnedLibrary = returnedLibrary }; using var http = new HttpClient(wire);
        var movie = new ItemDetailViewModel(new CatalogApi(Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie, null); await movie.RefreshBoxSetAsync();
        Assert.Equal(visible, movie.BoxSet != null); Assert.Equal(1, wire.DetailReads);
        movie.CancelPendingLoads();
    }
    [Fact] public async Task UnscopedMovieSkipsMembershipWhenNoViewerLibraryIsEligible()
    {
        using var wire = new Wire { Enabled = false, OtherEnabled = false }; using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        Assert.False(await api.BoxSetsEnabledAsync(null));
        var movie = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie, null); await movie.RefreshBoxSetAsync();
        Assert.Null(movie.BoxSet); Assert.Equal(0, wire.DetailReads); movie.CancelPendingLoads();
    }
    [Theory] [InlineData(null, true)] [InlineData(12, true)] [InlineData(99, false)] [InlineData(0, false)]
    public async Task EligibilitySupportsUnscopedDiscoveryButScopedChecksRemainExact(int? library, bool expected)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        Assert.Equal(expected, await new CatalogApi(Client(http)).BoxSetsEnabledAsync(library));
    }
    [Theory] [InlineData("profile")] [InlineData("access")] [InlineData("language")] [InlineData("departure")] [InlineData("library")]
    public async Task UnscopedMembershipCannotAdoptAfterItsOwnerChanges(string change)
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire); var client = Client(http);
        var movie = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie, null); var pending = movie.RefreshBoxSetAsync(); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (change == "profile") client.SetProfile("other");
        if (change == "access") client.InvalidateAccessContext();
        if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        if (change == "departure") movie.CancelPendingLoads();
        if (change == "library") typeof(ItemDetailViewModel).GetProperty(nameof(movie.LibraryId))!.SetValue(movie, 12);
        wire.Release.TrySetResult(); await pending;
        Assert.Null(movie.BoxSet); movie.CancelPendingLoads();
    }
    [Fact] public async Task ScopedMembershipCannotUseAnotherEligibleLibrary()
    {
        using var wire = new Wire { ReturnedLibrary = "2" }; using var http = new HttpClient(wire);
        var movie = new ItemDetailViewModel(new CatalogApi(Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync();
        Assert.Null(movie.BoxSet); Assert.Equal("?library_id=12", wire.LastMembershipQuery); movie.CancelPendingLoads();
    }
    [Fact] public async Task MembershipUsesExactLibraryAndIncludesAuthorizedHiddenLibraries()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var movie = new ItemDetailViewModel(new CatalogApi(Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync();
        Assert.NotNull(movie.BoxSet); Assert.Equal("?include_hidden=true", wire.LastLibraryQuery); Assert.Equal("?library_id=12", wire.LastMembershipQuery);
        movie.CancelPendingLoads();
    }
    [Fact] public async Task UnrelatedWatchedChangeDoesNotRefreshMembership()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var movie = new ItemDetailViewModel(new CatalogApi(Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync(); var reads = wire.DetailReads;
        movie.Receive(new SiloPlayer.Messaging.MediaSurfaceChanged(SiloPlayer.Messaging.MediaSurfaceChangeKind.WatchedMarked, "unrelated"));
        Assert.Equal(reads, wire.DetailReads); movie.CancelPendingLoads();
    }
    [Fact] public async Task SameMovieReloadPreservesRowDuringTranslationPolling()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var movie = new ItemDetailViewModel(new CatalogApi(Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync(); var row = movie.BoxSet;
        wire.Hold = true; wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await movie.ReloadAsync("film"); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Same(row, movie.BoxSet);
        wire.Hold = false; wire.Release.TrySetResult(); movie.CancelPendingLoads();
    }
    private static void SetMovie(ItemDetailViewModel vm, int? library = 12)
    {
        typeof(ItemDetailViewModel).GetProperty(nameof(vm.LibraryId))!.SetValue(vm, library);
        vm.Item = new MediaItemDetail { ContentId = "film", Type = "movie" };
    }
    private static SiloApiClient Client(HttpClient http) { var client = new SiloApiClient(http); client.SetBaseUrl("https://boxsets.invalid"); client.SetProfile("alpha"); return client; }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool? Supported = true;
        internal bool Hold, HoldCapability, AllPlayed, Enabled = true, OtherEnabled = true; internal int DetailReads;
        internal string ReturnedId = "7", ReturnedLibrary = "12";
        internal string MembershipContentId = "film", MembershipType = "movie";
        internal HttpStatusCode DetailStatus = HttpStatusCode.OK, CapabilityStatus = HttpStatusCode.OK;
        internal bool TimeoutDetail, TimeoutCapability, NoMembership;
        internal string? LastLibraryQuery, LastMembershipQuery;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = Supported.HasValue ? "{\"box_sets\":" + Supported.Value.ToString().ToLowerInvariant() + "}" : "{}"; var status = HttpStatusCode.OK;
            if (path.EndsWith("/capabilities"))
            {
                if (HoldCapability) { Started.TrySetResult(); await Release.Task; }
                if (TimeoutCapability) throw new TaskCanceledException("Synthetic capability timeout without caller cancellation.");
                status = CapabilityStatus;
            }
            else if (path.EndsWith("/user/libraries"))
            {
                LastLibraryQuery = request.RequestUri.Query;
                json = "{\"items\":[{\"id\":2,\"box_sets_enabled\":" + OtherEnabled.ToString().ToLowerInvariant() + "},{\"id\":12,\"box_sets_enabled\":" + Enabled.ToString().ToLowerInvariant() + "}]}";
            }
            else if (path.EndsWith("/items/film")) json = "{\"content_id\":\"film\",\"type\":\"movie\",\"user_state\":{}}";
            else
            {
                DetailReads++; Started.TrySetResult(); if (Hold) await Release.Task;
                if (TimeoutDetail) throw new TaskCanceledException("Synthetic transport timeout without caller cancellation.");
                if (path.EndsWith("/box-set")) LastMembershipQuery = request.RequestUri.Query;
                status = DetailStatus;
                var detail = "{\"id\":\"" + ReturnedId + "\",\"library_id\":\"" + ReturnedLibrary + "\",\"movie_count\":2,\"played_count\":" + (AllPlayed ? "2" : "1") + ",\"items\":[{\"type\":\"movie\",\"content_id\":\"first\",\"user_state\":{\"played\":true}},{\"type\":\"movie\",\"content_id\":\"second\",\"user_state\":{\"played\":" + AllPlayed.ToString().ToLowerInvariant() + "}}]}";
                if (path.EndsWith("/box-set"))
                {
                    // A valid item-membership envelope contains the requested film;
                    // dedicated detail order/targets stay first and second.
                    detail = detail.Replace("\"content_id\":\"second\"", "\"content_id\":\"" + MembershipContentId + "\"")
                        .Replace("{\"type\":\"movie\",\"content_id\":\"" + MembershipContentId + "\"", "{\"type\":\"" + MembershipType + "\",\"content_id\":\"" + MembershipContentId + "\"");
                    json = NoMembership ? "{\"box_set\":null}" : "{\"box_set\":" + detail + "}";
                }
                else json = detail;
            }
            return new(status) { Content = new StringContent(json) };
        }
    }
    [Fact] public async Task MembershipPostSaveRefreshSurvivesSameViewerAccessInvalidationDuringRead()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var movie = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync();
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        movie.Receive(new SiloPlayer.Messaging.PlaybackProgressUpdated("film", 100, 100, true) { Persisted = persisted.Task });
        wire.Hold = true; wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshing = movie.RefreshBoxSetAsync();
        try
        {
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var readOwner = client.CaptureContext(); var identity = client.CaptureIdentityContext();
            client.InvalidateAccessContext();
            Assert.False(client.IsCurrentContext(readOwner)); Assert.True(client.IsCurrentIdentityContext(identity));
            wire.Hold = false; wire.Release.TrySetResult(); await refreshing;
            await movie.RefreshBoxSetAsync();
            Assert.Equal(1, movie.BoxSet!.PlayedCount); Assert.Equal("film", movie.BoxSet.PlayTarget?.ContentId);
            var reads = wire.DetailReads; wire.AllPlayed = true; persisted.TrySetResult();
            for (var i = 0; i < 100 && (wire.DetailReads == reads || movie.BoxSet?.PlayedCount != 2); i++) await Task.Delay(10);
            Assert.True(wire.DetailReads > reads, "A retired pre-save read must not retire the visit's post-save refresh.");
            Assert.Equal(2, movie.BoxSet!.PlayedCount); Assert.Equal("first", movie.BoxSet.PlayTarget?.ContentId);
        }
        finally { movie.CancelPendingLoads(); wire.Release.TrySetResult(); persisted.TrySetResult(); await refreshing; }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task FirstActivationRefreshesAfterAnOutstandingSaveWithoutReceivingItsMessage(bool dedicated)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var set = new BoxSetViewModel(api);
        var movie = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((id, _) =>
            Task.FromResult(new MediaItemDetail { ContentId = id, Type = "movie", UserState = new() })));
        try
        {
            if (dedicated) await set.LoadAsync("7", "12", persisted.Task);
            else await movie.LoadInLibraryAsync("film", null, persisted.Task);
            for (var i = 0; i < 100 && (dedicated ? set.Detail : movie.BoxSet) == null; i++) await Task.Delay(10);
            Assert.Equal(1, (dedicated ? set.Detail : movie.BoxSet)!.PlayedCount);
            var reads = wire.DetailReads;
            wire.AllPlayed = true; persisted.TrySetResult();
            for (var i = 0; i < 100 && (wire.DetailReads == reads || (dedicated ? set.Detail : movie.BoxSet)?.PlayedCount != 2); i++) await Task.Delay(10);
            var refreshed = dedicated ? set.Detail : movie.BoxSet;
            Assert.True(wire.DetailReads > reads);
            Assert.Equal(2, refreshed!.PlayedCount); Assert.Equal("first", refreshed.PlayTarget?.ContentId);
        }
        finally { movie.CancelPendingLoads(); set.Cancel(); persisted.TrySetResult(); }
    }
    [Theory]
    [InlineData(false, "departure")] [InlineData(true, "departure")]
    [InlineData(false, "profile")] [InlineData(true, "profile")]
    [InlineData(false, "route")] [InlineData(true, "route")]
    public async Task FirstActivationPostSaveRefreshCannotReviveARetiredVisit(bool dedicated, string change)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http); var api = new CatalogApi(client);
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var set = new BoxSetViewModel(api);
        var movie = new ItemDetailViewModel(api, new ItemDetailPrefetchCache((id, _) =>
            Task.FromResult(new MediaItemDetail { ContentId = id, Type = "movie", UserState = new() })));
        try
        {
            if (dedicated) await set.LoadAsync("7", "12", persisted.Task);
            else await movie.LoadInLibraryAsync("film", null, persisted.Task);
            for (var i = 0; i < 100 && (dedicated ? set.Detail : movie.BoxSet) == null; i++) await Task.Delay(10);
            Assert.NotNull(dedicated ? set.Detail : movie.BoxSet);
            if (change == "departure") { set.Cancel(); movie.CancelPendingLoads(); }
            if (change == "profile") client.SetProfile("other");
            if (change == "route")
            {
                if (dedicated) await set.LoadAsync("8", "12");
                else movie.Item = new MediaItemDetail { ContentId = "other", Type = "movie" };
            }
            var reads = wire.DetailReads; wire.AllPlayed = true; persisted.TrySetResult(); await Task.Delay(50);
            Assert.Equal(reads, wire.DetailReads);
        }
        finally { movie.CancelPendingLoads(); set.Cancel(); persisted.TrySetResult(); }
    }
    [Theory] [InlineData(200)] [InlineData(503)]
    public async Task MembershipAccessRevisionPreservesVisibleRowAndPostSaveMembership(int status)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var movie = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync(); var visible = movie.BoxSet;
        var blanked = false;
        movie.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(movie.BoxSet) && movie.BoxSet == null) blanked = true; };
        wire.Hold = true; wire.DetailStatus = (HttpStatusCode)status;
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.InvalidateAccessContext();
        var refreshing = movie.RefreshBoxSetAsync();
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); Assert.Same(visible, movie.BoxSet); Assert.False(blanked);
            movie.Receive(new SiloPlayer.Messaging.PlaybackProgressUpdated("film", 100, 100, true) { Persisted = persisted.Task });
            wire.Hold = false; wire.Release.TrySetResult(); await refreshing;
            Assert.False(blanked);
            if (status == 503) Assert.Same(visible, movie.BoxSet);
            var reads = wire.DetailReads; wire.DetailStatus = HttpStatusCode.OK; wire.AllPlayed = true; persisted.TrySetResult();
            for (var i = 0; i < 100 && (wire.DetailReads == reads || movie.BoxSet?.PlayedCount != 2); i++) await Task.Delay(10);
            Assert.True(wire.DetailReads > reads); Assert.Equal(2, movie.BoxSet!.PlayedCount);
        }
        finally { movie.CancelPendingLoads(); wire.Release.TrySetResult(); persisted.TrySetResult(); await refreshing; }
    }
    [Theory]
    [InlineData("profile", false)] [InlineData("access", false)] [InlineData("language", false)]
    [InlineData("profile", true)] [InlineData("access", true)] [InlineData("language", true)]
    public async Task AbandonedDedicatedReadEndsLoadingAndAllowsRetry(string change, bool capability)
    {
        using var wire = new Wire { Hold = !capability, HoldCapability = capability };
        using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client));
        var pending = vm.LoadAsync("7", "12");
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (change == "profile") client.SetProfile("other");
        if (change == "access") client.InvalidateAccessContext();
        if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        wire.Hold = wire.HoldCapability = false; wire.Release.TrySetResult(); await pending;
        Assert.Null(vm.Detail); Assert.False(vm.IsLoading); Assert.False(vm.IsCurrent);
        Assert.Equal(DetailFailureKind.Transient, vm.Failure);
        await vm.RefreshAsync();
        Assert.NotNull(vm.Detail); Assert.False(vm.IsLoading); Assert.True(vm.IsCurrent);
        Assert.Equal(DetailFailureKind.None, vm.Failure); vm.Cancel();
    }
    [Theory] [InlineData("profile")] [InlineData("access")] [InlineData("language")]
    public async Task AbandonedBackgroundReadIsTerminalWithoutAdoptingStaleDetail(string change)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "12"); var visible = vm.Detail;
        wire.Hold = true;
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.RefreshAsync(); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (change == "profile") client.SetProfile("other");
        if (change == "access") client.InvalidateAccessContext();
        if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        wire.Release.TrySetResult(); await pending;
        Assert.False(vm.IsLoading); Assert.Equal(DetailFailureKind.Transient, vm.Failure);
        if (change == "profile") Assert.Null(vm.Detail); else Assert.Same(visible, vm.Detail);
        vm.Cancel();
    }
    [Fact] public async Task AbandonedReadCannotFinishItsSuccessorsLoadingState()
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); var first = vm.LoadAsync("7", "12");
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); var releaseFirst = wire.Release;
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.InvalidateAccessContext(); var second = vm.RefreshAsync();
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); releaseFirst.TrySetResult(); await first;
        Assert.True(vm.IsLoading); Assert.Equal(DetailFailureKind.None, vm.Failure); Assert.Null(vm.Detail);
        wire.Release.TrySetResult(); await second;
        Assert.False(vm.IsLoading); Assert.Equal(DetailFailureKind.None, vm.Failure); Assert.NotNull(vm.Detail); vm.Cancel();
    }
    [Fact] public async Task AbandonedReadCannotPublishFailureAfterDeparture()
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire);
        var vm = new BoxSetViewModel(new CatalogApi(Client(http))); var pending = vm.LoadAsync("7", "12");
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); vm.Cancel(); wire.Release.TrySetResult(); await pending;
        Assert.False(vm.IsLoading); Assert.Null(vm.Detail); Assert.Equal(DetailFailureKind.None, vm.Failure);
    }
}
