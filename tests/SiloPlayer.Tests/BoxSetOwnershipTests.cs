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
        await vm.LoadInLibraryAsync("film", 12).WaitAsync(TimeSpan.FromSeconds(2));
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
    [Fact] public async Task MembershipPostSaveRefreshSurvivesSameViewerAccessInvalidationDuringRead()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var movie = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        SetMovie(movie); await movie.RefreshBoxSetAsync();
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        movie.Receive(new SiloPlayer.Messaging.PlaybackProgressUpdated("second", 100, 100, true) { Persisted = persisted.Task });
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
            Assert.Equal(1, movie.BoxSet!.PlayedCount); Assert.Equal("second", movie.BoxSet.PlayTarget?.ContentId);
            var reads = wire.DetailReads; wire.AllPlayed = true; persisted.TrySetResult();
            for (var i = 0; i < 100 && (wire.DetailReads == reads || movie.BoxSet?.PlayedCount != 2); i++) await Task.Delay(10);
            Assert.True(wire.DetailReads > reads, "A retired pre-save read must not retire the visit's post-save refresh.");
            Assert.Equal(2, movie.BoxSet!.PlayedCount); Assert.Equal("first", movie.BoxSet.PlayTarget?.ContentId);
        }
        finally { movie.CancelPendingLoads(); wire.Release.TrySetResult(); persisted.TrySetResult(); await refreshing; }
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
    [Theory]
    [InlineData("profile", false)] [InlineData("access", false)] [InlineData("language", false)]
    [InlineData("profile", true)] [InlineData("access", true)] [InlineData("language", true)]
    public async Task AbandonedDedicatedReadEndsLoadingAndAllowsRetry(string change, bool capability)
    {
        using var wire = new Wire { Hold = !capability, HoldCapability = capability };
        using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client));
        var pending = vm.LoadAsync("7", "2");
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
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "2"); var visible = vm.Detail;
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
        var vm = new BoxSetViewModel(new CatalogApi(client)); var first = vm.LoadAsync("7", "2");
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
        var vm = new BoxSetViewModel(new CatalogApi(Client(http))); var pending = vm.LoadAsync("7", "2");
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); vm.Cancel(); wire.Release.TrySetResult(); await pending;
        Assert.False(vm.IsLoading); Assert.Null(vm.Detail); Assert.Equal(DetailFailureKind.None, vm.Failure);
    }
    [Fact] public async Task CapabilityFaultIsRetryableOnDedicatedDestination()
    {
        using var wire = new Wire { CapabilityStatus = HttpStatusCode.ServiceUnavailable }; using var http = new HttpClient(wire);
        var vm = new BoxSetViewModel(new CatalogApi(Client(http)));
        await vm.LoadAsync("7", "2");
        Assert.Equal(DetailFailureKind.Transient, vm.Failure); Assert.False(vm.IsLoading); Assert.Null(vm.Detail);
        wire.CapabilityStatus = HttpStatusCode.OK; await vm.RefreshAsync();
        Assert.NotNull(vm.Detail); Assert.Equal(DetailFailureKind.None, vm.Failure); vm.Cancel();
    }
    [Theory] [InlineData(404)] [InlineData(200)]
    public async Task ExplicitlyUnsupportedCapabilityIsUnavailable(int status)
    {
        using var wire = new Wire { CapabilityStatus = (HttpStatusCode)status, Supported = false }; using var http = new HttpClient(wire);
        var vm = new BoxSetViewModel(new CatalogApi(Client(http))); await vm.LoadAsync("7", "2");
        Assert.Equal(DetailFailureKind.NotFound, vm.Failure); Assert.Equal(0, wire.DetailReads); vm.Cancel();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task TransportTimeoutIsNotCallerCancellation(bool capability)
    {
        using var wire = new Wire { TimeoutDetail = !capability, TimeoutCapability = capability }; using var http = new HttpClient(wire);
        var vm = new BoxSetViewModel(new CatalogApi(Client(http))); await vm.LoadAsync("7", "2");
        Assert.Equal(DetailFailureKind.Transient, vm.Failure); Assert.False(vm.IsLoading); Assert.Null(vm.Detail);
        wire.TimeoutDetail = wire.TimeoutCapability = false; await vm.RefreshAsync(); Assert.NotNull(vm.Detail); vm.Cancel();
    }
    [Fact] public async Task BackgroundFailureKeepsSuccessfulDetailAndRow()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        var set = new BoxSetViewModel(api); await set.LoadAsync("7", "2"); var visible = set.Detail;
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
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "2");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken accepted = default; var completed = false;
        var start = vm.PlayAsync(async (_, _, _, ct) => { accepted = ct; await release.Task.WaitAsync(ct); completed = true; });
        await vm.RefreshAsync();
        client.LocalizedReads.NotifySettingChanged("catalog.metadata_language"); await vm.RefreshAsync();
        Assert.False(accepted.IsCancellationRequested);
        release.TrySetResult(); await start; Assert.True(completed);
        vm.Cancel(); Assert.True(accepted.IsCancellationRequested);
    }
    [Fact] public async Task AuthorityChangeRetiresAcceptedStart()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "2");
        CancellationToken accepted = default;
        await vm.PlayAsync((_, _, _, ct) => { accepted = ct; return Task.CompletedTask; });
        client.SetProfile("other"); await vm.RefreshAsync(); Assert.True(accepted.IsCancellationRequested); vm.Cancel();
    }
    [Theory] [InlineData(200)] [InlineData(503)]
    public async Task SameViewerAccessRefreshPreservesAcceptedStartAndVisibleDetail(int status)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); var client = Client(http);
        var vm = new BoxSetViewModel(new CatalogApi(client)); await vm.LoadAsync("7", "2");
        var visible = vm.Detail; Assert.NotNull(visible);
        var identity = client.CaptureIdentityContext(); var previousRead = client.CaptureContext();
        var blanked = false;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Detail) && vm.Detail == null) blanked = true; };
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken accepted = default; var completed = false;
        var start = vm.PlayAsync(async (_, _, _, ct) => { accepted = ct; await releaseStart.Task.WaitAsync(ct); completed = true; });
        wire.Hold = true; wire.AllPlayed = true; wire.DetailStatus = (HttpStatusCode)status;
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            client.InvalidateAccessContext();
            Assert.True(client.IsCurrentIdentityContext(identity)); Assert.False(client.IsCurrentContext(previousRead));
            var refresh = vm.RefreshAsync(); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(vm.IsLoading); Assert.Same(visible, vm.Detail); Assert.False(accepted.IsCancellationRequested);
            wire.Hold = false; wire.Release.TrySetResult(); await refresh;
            Assert.False(blanked); Assert.False(vm.IsLoading); Assert.True(vm.IsCurrent);
            Assert.False(accepted.IsCancellationRequested);
            if (status == 200) Assert.Equal(2, vm.Detail!.PlayedCount);
            else { Assert.Same(visible, vm.Detail); Assert.Equal(DetailFailureKind.Transient, vm.Failure); }
            var calls = 0;
            await vm.PlayAsync((_, _, owner, _) => { calls++; Assert.True(client.IsCurrentContext(owner)); return Task.CompletedTask; });
            Assert.Equal(1, calls);
            releaseStart.TrySetResult(); await start; Assert.True(completed);
        }
        finally
        {
            wire.Release.TrySetResult(); releaseStart.TrySetResult(); vm.Cancel();
            try { await start; } catch (OperationCanceledException) { }
        }
        Assert.True(accepted.IsCancellationRequested);
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
        internal string ReturnedLibrary = "12";
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
                var detail = "{\"id\":\"7\",\"library_id\":\"" + ReturnedLibrary + "\",\"movie_count\":2,\"played_count\":" + (AllPlayed ? "2" : "1") + ",\"items\":[{\"type\":\"movie\",\"content_id\":\"first\",\"user_state\":{\"played\":true}},{\"type\":\"movie\",\"content_id\":\"second\",\"user_state\":{\"played\":" + AllPlayed.ToString().ToLowerInvariant() + "}}]}";
                json = path.EndsWith("/box-set") ? NoMembership ? "{\"box_set\":null}" : "{\"box_set\":" + detail + "}" : detail;
            }
            return new(status) { Content = new StringContent(json) };
        }
    }
}
