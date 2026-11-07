using System.Net;
using System.Reflection;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class BoxSetInitialProgressTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnknownSiblingProgressRechecksMembershipAfterTheSaveInEitherReadOrder(bool initialFinishesFirst)
    {
        using var scope = new Scope();
        var initialGate = scope.Wire.HoldRead(1);
        var initial = scope.Movie.RefreshBoxSetAsync();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await initialGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        scope.Movie.Receive(new PlaybackProgressUpdated("sibling", 100, 100, true) { Persisted = saved.Task });
        try
        {
            Assert.Null(scope.Movie.BoxSet);
            Assert.Equal(1, scope.Wire.Reads);
            if (initialFinishesFirst)
            {
                initialGate.Release.TrySetResult(); await initial;
                Assert.Equal(0, scope.Movie.BoxSet!.PlayedCount);
                Assert.Equal("sibling", scope.Movie.BoxSet.PlayTarget!.ContentId);
                Assert.Equal(1, scope.Wire.Reads);
            }
            scope.Wire.Played = true;
            saved.TrySetResult();
            await ExpectReadAsync(scope.Wire, 2);
            await UntilAsync(() => scope.Movie.BoxSet?.PlayedCount == 1);
            Assert.Equal("film", scope.Movie.BoxSet!.PlayTarget!.ContentId);
            initialGate.Release.TrySetResult(); await initial;
            Assert.Equal(1, scope.Movie.BoxSet.PlayedCount);
            Assert.Equal(2, scope.Wire.Reads);
        }
        finally { saved.TrySetResult(); initialGate.Release.TrySetResult(); await initial; }
    }

    [Theory]
    [InlineData("unrelated")]
    [InlineData("empty")]
    [InlineData("cancel")]
    [InlineData("profile")]
    [InlineData("aba")]
    [InlineData("item")]
    [InlineData("library")]
    [InlineData("save-failed")]
    public async Task PendingUnknownProgressDoesNotRefreshAnUnrelatedOrRetiredMembership(string change)
    {
        using var scope = new Scope();
        if (change == "unrelated") scope.Wire.Member = "other";
        if (change == "empty") scope.Wire.NoMembership = true;
        var initialGate = scope.Wire.HoldRead(1);
        var initial = scope.Movie.RefreshBoxSetAsync();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await initialGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var message = new PlaybackProgressUpdated("sibling", 100, 100, true) { Persisted = saved.Task };
        scope.Movie.Receive(message);
        var callback = InvokePostSaveAsync(scope.Movie, message);
        initialGate.Release.TrySetResult(); await initial;
        if (change == "cancel") scope.Movie.CancelPendingLoads();
        if (change is "profile" or "aba") scope.Client.SetProfile("other");
        if (change == "aba") scope.Client.SetProfile("alpha");
        if (change == "item") scope.Movie.Item = new MediaItemDetail { ContentId = "replacement", Type = "movie" };
        if (change == "library") SetLibrary(scope.Movie, 2);
        if (change == "save-failed") saved.TrySetException(new IOException("Synthetic rejected progress write."));
        else saved.TrySetResult();
        // Await the real post-save callback captured before retirement. The
        // separate public Receive tests cover unknown-membership admission.
        await callback.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, scope.Wire.Reads);
    }

    [Fact]
    public async Task AccessOnlyRefreshStillAllowsTheRetainedSiblingProgress()
    {
        using var scope = new Scope();
        var gate = scope.Wire.HoldRead(1);
        var initial = scope.Movie.RefreshBoxSetAsync();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        scope.Movie.Receive(new PlaybackProgressUpdated("sibling", 100, 100, true) { Persisted = saved.Task });
        gate.Release.TrySetResult(); await initial;
        scope.Client.InvalidateAccessContext(); scope.Wire.Played = true;
        saved.TrySetResult();
        try
        {
            await ExpectReadAsync(scope.Wire, 2);
            await UntilAsync(() => scope.Movie.BoxSet?.PlayedCount == 1);
        }
        finally { saved.TrySetResult(); gate.Release.TrySetResult(); await initial; }
    }

    [Fact]
    public async Task ACompletedOrCanceledUnknownReadDoesNotRemainAnInvalidationSubscription()
    {
        using var scope = new Scope(); scope.Wire.NoMembership = true;
        await scope.Movie.RefreshBoxSetAsync();
        scope.Movie.Receive(new PlaybackProgressUpdated("sibling", 10, 100, false));
        scope.Movie.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "sibling"));
        Assert.Equal(1, scope.Wire.Reads);
        scope.Movie.CancelPendingLoads();
        scope.Movie.Receive(new PlaybackProgressUpdated("sibling", 10, 100, false));
        Assert.Equal(1, scope.Wire.Reads);
    }

    [Fact]
    public async Task AnOldReadFinallyCannotRetireItsStillUnknownSuccessor()
    {
        using var scope = new Scope();
        var oldGate = scope.Wire.HoldRead(1); var currentGate = scope.Wire.HoldRead(2);
        var old = scope.Movie.RefreshBoxSetAsync(); await oldGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var current = scope.Movie.RefreshBoxSetAsync(); await currentGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        oldGate.Release.TrySetResult(); await old;
        Assert.Null(scope.Movie.BoxSet);
        scope.Wire.Played = true;
        scope.Movie.Receive(new PlaybackProgressUpdated("sibling", 100, 100, true));
        try
        {
            await ExpectReadAsync(scope.Wire, 3);
            await UntilAsync(() => scope.Movie.BoxSet?.PlayedCount == 1);
            currentGate.Release.TrySetResult(); await current;
            Assert.Equal(1, scope.Movie.BoxSet!.PlayedCount);
        }
        finally { oldGate.Release.TrySetResult(); currentGate.Release.TrySetResult(); await Task.WhenAll(old, current); }
    }

    [Fact]
    public async Task UnknownInitialMembershipAlsoRetainsAnAlreadyAcceptedWatchedInvalidation()
    {
        using var scope = new Scope(); var gate = scope.Wire.HoldRead(1);
        var initial = scope.Movie.RefreshBoxSetAsync(); await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        scope.Wire.Played = true;
        scope.Movie.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "sibling"));
        try
        {
            await ExpectReadAsync(scope.Wire, 2);
            await UntilAsync(() => scope.Movie.BoxSet?.PlayedCount == 1);
            gate.Release.TrySetResult(); await initial;
            Assert.Equal(1, scope.Movie.BoxSet!.PlayedCount);
        }
        finally { gate.Release.TrySetResult(); await initial; }
    }

    private static Task InvokePostSaveAsync(ItemDetailViewModel movie, PlaybackProgressUpdated message) =>
        (Task)typeof(ItemDetailViewModel).GetMethod("RefreshBoxSetAfterPlaybackAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(movie, [message])!;

    private static async Task ExpectReadAsync(Wire wire, int count)
    {
        await Task.WhenAny(wire.Started(count).Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Equal(count, wire.Reads);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), "The current membership did not publish its post-save state.");
    }

    private static void SetLibrary(ItemDetailViewModel movie, int library) =>
        typeof(ItemDetailViewModel).GetProperty(nameof(movie.LibraryId))!.SetValue(movie, library);

    private sealed class Scope : IDisposable
    {
        internal readonly Wire Wire = new();
        internal readonly HttpClient Http;
        internal readonly SiloApiClient Client;
        internal readonly ItemDetailViewModel Movie;
        internal Scope()
        {
            Http = new(Wire); Client = new(Http); Client.SetBaseUrl("https://boxset-progress.invalid"); Client.SetProfile("alpha");
            Movie = new(new CatalogApi(Client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
            SetLibrary(Movie, 12); Movie.Item = new MediaItemDetail { ContentId = "film", Type = "movie" };
        }
        public void Dispose() { Movie.CancelPendingLoads(); Http.Dispose(); }
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal sealed class Gate
        {
            internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Gate> gates = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource> starts = new();
        internal int Reads;
        internal bool Played, NoMembership;
        internal string Member = "sibling";
        internal Gate HoldRead(int ordinal) { var gate = new Gate(); Assert.True(gates.TryAdd(ordinal, gate)); return gate; }
        internal TaskCompletionSource Started(int ordinal) => starts.GetOrAdd(ordinal, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/capabilities")) return Json(new { box_sets = true });
            if (path.EndsWith("/libraries")) return Json(new { items = new[] { new { id = 12, box_sets_enabled = true } } });
            if (!path.EndsWith("/box-set")) throw new InvalidOperationException("Unexpected isolated membership request: " + path);
            var ordinal = Interlocked.Increment(ref Reads);
            var response = NoMembership ? Json(new { box_set = (object?)null }) : Json(new { box_set = new {
                id = "7", library_id = "12", title = "Fixture set", movie_count = 2, played_count = Played ? 1 : 0,
                items = new[] {
                    new { content_id = Member, type = "movie", user_state = new { played = Played } },
                    new { content_id = "film", type = "movie", user_state = new { played = false } },
                },
            } });
            Started(ordinal).TrySetResult();
            if (gates.TryGetValue(ordinal, out var hold)) { hold.Started.TrySetResult(); await hold.Release.Task; }
            return response;
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}

