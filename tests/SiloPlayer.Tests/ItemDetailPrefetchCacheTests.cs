using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ItemDetailPrefetchCacheTests
{

    private sealed class HeldUiContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback callback, object? state) => Interlocked.Increment(ref Posts);
    }

    [Fact]
    public async Task ColdScopedReadStartsWithoutWaitingForTheUiQueue()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new HeldUiContext();
        var cache = new ItemDetailPrefetchCache((string id, int? library, CancellationToken ct) =>
        {
            started.TrySetResult();
            return Task.FromResult(new MediaItemDetail { ContentId = id });
        });
        Task<MediaItemDetail> read;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            read = cache.GetAsync("cold-library-item", 7, CancellationToken.None);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("cold-library-item", (await read).ContentId);
        Assert.Equal(0, context.Posts);
    }

    [Fact]
    public async Task ProgressSaveCancelsSpeculationOutsideTheCacheLock()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledUnderLock = false;
        object? gate = null;
        var cache = new ItemDetailPrefetchCache(async (string id, CancellationToken ct) =>
        {
            using var registration = ct.Register(() =>
            {
                cancelledUnderLock = Monitor.IsEntered(gate!);
                cancelled.TrySetResult();
            });
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new MediaItemDetail { ContentId = id };
        });
        gate = typeof(ItemDetailPrefetchCache).GetField("_sync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cache);
        cache.Prefetch("speculative");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cache.TrackProgressSave(Task.CompletedTask, "speculative");
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(cancelledUnderLock);
    }

    [Fact]
    public async Task PrefetchAndNavigationShareOneInFlightDetailRequest()
    {
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = new ItemDetailPrefetchCache(async (contentId, _) =>
        {
            Interlocked.Increment(ref calls);
            fetchStarted.SetResult();
            return await response.Task;
        });

        var prefetch = cache.PrefetchAsync("movie-1");
        await fetchStarted.Task;
        var navigation = cache.GetAsync("movie-1", CancellationToken.None);
        response.SetResult(new MediaItemDetail { ContentId = "movie-1", Title = "Movie" });

        await prefetch;
        var detail = await navigation;

        Assert.Equal(1, calls);
        Assert.Equal("Movie", detail.Title);
    }

    [Fact]
    public async Task CancelledNavigationDoesNotCancelSharedPrefetch()
    {
        var response = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ItemDetailPrefetchCache((_, _) => response.Task);
        var prefetch = cache.PrefetchAsync("episode-1");
        using var navigationCancellation = new CancellationTokenSource();
        var navigation = cache.GetAsync("episode-1", navigationCancellation.Token);

        navigationCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigation);
        response.SetResult(new MediaItemDetail { ContentId = "episode-1" });

        await prefetch;
        Assert.Equal("episode-1", (await cache.GetAsync("episode-1", CancellationToken.None)).ContentId);
    }

    [Fact]
    public async Task FailedPrefetchIsRemovedSoNavigationCanRetry()
    {
        var calls = 0;
        var cache = new ItemDetailPrefetchCache((contentId, _) =>
        {
            calls++;
            return calls == 1
                ? Task.FromException<MediaItemDetail>(new InvalidOperationException("offline"))
                : Task.FromResult(new MediaItemDetail { ContentId = contentId });
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.PrefetchAsync("movie-2"));
        var detail = await cache.GetAsync("movie-2", CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal("movie-2", detail.ContentId);
    }

    [Fact]
    public async Task ServerOrProfileContextChangeCannotReusePriorDetail()
    {
        var calls = 0;
        var context = "server-a/profile-1";
        var cache = new ItemDetailPrefetchCache(
            (contentId, _) => Task.FromResult(new MediaItemDetail
            {
                ContentId = contentId,
                Title = $"result-{++calls}",
            }),
            contextKey: () => context);

        var first = await cache.GetAsync("shared-id", CancellationToken.None);
        context = "server-b/profile-1";
        var second = await cache.GetAsync("shared-id", CancellationToken.None);
        context = "server-b/profile-2";
        var third = await cache.GetAsync("shared-id", CancellationToken.None);

        Assert.Equal("result-1", first.Title);
        Assert.Equal("result-2", second.Title);
        Assert.Equal("result-3", third.Title);
        Assert.Equal(3, calls);
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ScopedPrefetchIsConsumedOnlyByTheSameLibraryScope()
    {
        var fetch = new ScriptedFetch();
        var cache = new ItemDetailPrefetchCache(fetch.Fetch);
        Assert.True(cache.SupportsLibraryScope);

        cache.Prefetch("movie", 7);
        await fetch.Started("movie", 7).WaitAsync(TestTimeout);
        var scoped = cache.GetAsync("movie", 7, CancellationToken.None);
        fetch.Complete("movie", 7);
        Assert.Equal("movie@7", (await scoped.WaitAsync(TestTimeout)).Title);

        var unscoped = cache.GetAsync("movie", CancellationToken.None);
        fetch.Complete("movie", null);
        Assert.Equal("movie@", (await unscoped.WaitAsync(TestTimeout)).Title);
        var otherLibrary = cache.GetAsync("movie", 8, CancellationToken.None);
        fetch.Complete("movie", 8);
        Assert.Equal("movie@8", (await otherLibrary.WaitAsync(TestTimeout)).Title);
        // A repeated same-scope read is served from the cache.
        Assert.Equal("movie@7", (await cache.GetAsync("movie", 7, CancellationToken.None).WaitAsync(TestTimeout)).Title);

        Assert.Equal(new (string, int?)[] { ("movie", 7), ("movie", null), ("movie", 8) }, fetch.CallKeys());
        Assert.Equal(1, cache.Counters.SpeculativeAdopted);
    }

    [Fact]
    public async Task UnscopedOnlyCacheIgnoresScopedIntentAndRejectsScopedReads()
    {
        var calls = 0;
        var cache = new ItemDetailPrefetchCache((id, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new MediaItemDetail { ContentId = id });
        });

        Assert.False(cache.SupportsLibraryScope);
        cache.Prefetch("movie", 7);
        await Assert.ThrowsAsync<NotSupportedException>(() => cache.GetAsync("movie", 7, CancellationToken.None));
        await Task.Delay(20);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task InvalidateRemovesEveryLibraryScopeAndRetriesAnAwaitedRead()
    {
        var fetch = new ScriptedFetch();
        var cache = new ItemDetailPrefetchCache(fetch.Fetch);
        fetch.Complete("item", null);
        fetch.Complete("item", 7);
        await cache.GetAsync("item", CancellationToken.None).WaitAsync(TestTimeout);
        await cache.GetAsync("item", 7, CancellationToken.None).WaitAsync(TestTimeout);

        cache.Invalidate("item");
        await cache.GetAsync("item", CancellationToken.None).WaitAsync(TestTimeout);
        await cache.GetAsync("item", 7, CancellationToken.None).WaitAsync(TestTimeout);
        Assert.Equal(2, fetch.CallCount("item", null));
        Assert.Equal(2, fetch.CallCount("item", 7));

        var pending = new ScriptedFetch();
        cache = new ItemDetailPrefetchCache(pending.Fetch);
        var awaited = cache.GetAsync("item", 7, CancellationToken.None);
        await pending.Started("item", 7).WaitAsync(TestTimeout);
        cache.Invalidate("item");
        pending.Complete("item", 7);
        await awaited.WaitAsync(TestTimeout);
        Assert.Equal(2, pending.CallCount("item", 7));
    }

    [Fact]
    public async Task ReplacedSpeculativeIntentIsCancelledAndOnlyOneRequestRuns()
    {
        var fetch = new ScriptedFetch();
        var cache = new ItemDetailPrefetchCache(fetch.Fetch);

        cache.Prefetch("a");
        await fetch.Started("a", null).WaitAsync(TestTimeout);
        cache.Prefetch("b");
        cache.Prefetch("c");
        await fetch.Started("c", null).WaitAsync(TestTimeout);

        Assert.True(fetch.TokenFor("a", null).IsCancellationRequested);
        Assert.Equal(0, fetch.CallCount("b", null));
        Assert.Equal(new (string, int?)[] { ("a", null), ("c", null) }, fetch.CallKeys());
        Assert.Equal(1, fetch.PeakRunning);
        var counters = cache.Counters;
        Assert.Equal(3, counters.SpeculativeStarted);
        Assert.Equal(2, counters.SpeculativeCancelled);
        Assert.Equal(1, counters.PeakSpeculativeRunning);

        // The surviving intent is still shared with navigation.
        var navigation = cache.GetAsync("c", CancellationToken.None);
        fetch.Complete("c", null);
        await navigation.WaitAsync(TestTimeout);
        Assert.Equal(1, fetch.CallCount("c", null));
    }

    [Fact]
    public async Task SpeculativeRequestWaitsForEarlierSpeculativeWorkThatIgnoresCancellation()
    {
        var fetch = new ScriptedFetch { HonorCancellation = false };
        var cache = new ItemDetailPrefetchCache(fetch.Fetch);

        cache.Prefetch("a");
        await fetch.Started("a", null).WaitAsync(TestTimeout);
        cache.Prefetch("b");
        await Task.Delay(50);
        Assert.True(fetch.TokenFor("a", null).IsCancellationRequested);
        Assert.Equal(0, fetch.CallCount("b", null));

        fetch.Complete("a", null);
        await fetch.Started("b", null).WaitAsync(TestTimeout);
        Assert.Equal(1, fetch.PeakRunning);
        fetch.Complete("b", null);
    }

    [Fact]
    public async Task ForegroundReadStartsImmediatelyAndCancelsUnrelatedSpeculation()
    {
        var fetch = new ScriptedFetch { HonorCancellation = false };
        var cache = new ItemDetailPrefetchCache(fetch.Fetch);

        cache.Prefetch("hovered");
        await fetch.Started("hovered", null).WaitAsync(TestTimeout);
        var navigation = cache.GetAsync("clicked", 3, CancellationToken.None);
        await fetch.Started("clicked", 3).WaitAsync(TestTimeout);

        Assert.False(fetch.Response("hovered", null).Task.IsCompleted);
        Assert.True(fetch.TokenFor("hovered", null).IsCancellationRequested);
        Assert.False(fetch.TokenFor("clicked", 3).CanBeCanceled);
        fetch.Complete("clicked", 3);
        Assert.Equal("clicked@3", (await navigation.WaitAsync(TestTimeout)).Title);
        Assert.Equal(1, cache.Counters.ForegroundStarted);
        fetch.Complete("hovered", null);
    }

    [Fact]
    public async Task ForegroundAdoptsQueuedSpeculationWithoutWaitingBehindOlderWork()
    {
        var fetch = new ScriptedFetch { HonorCancellation = false };
        var cache = new ItemDetailPrefetchCache(fetch.Fetch);

        cache.Prefetch("older");
        await fetch.Started("older", null).WaitAsync(TestTimeout);
        cache.Prefetch("target", 5);
        await Task.Delay(30);
        Assert.Equal(0, fetch.CallCount("target", 5));

        var navigation = cache.GetAsync("target", 5, CancellationToken.None);
        await fetch.Started("target", 5).WaitAsync(TestTimeout);
        Assert.False(fetch.Response("older", null).Task.IsCompleted);
        fetch.Complete("target", 5);
        Assert.Equal("target@5", (await navigation.WaitAsync(TestTimeout)).Title);
        Assert.Equal(1, fetch.CallCount("target", 5));
        Assert.False(fetch.TokenFor("target", 5).IsCancellationRequested);
        Assert.Equal(1, cache.Counters.SpeculativeAdopted);
        Assert.Equal(0, cache.Counters.ForegroundStarted);
        fetch.Complete("older", null);
    }

    [Fact]
    public async Task AdoptedSpeculationSurvivesConsumerCancellationNewerIntentAndClear()
    {
        var fetch = new ScriptedFetch();
        var cache = new ItemDetailPrefetchCache(fetch.Fetch);

        cache.Prefetch("a");
        await fetch.Started("a", null).WaitAsync(TestTimeout);
        using var navigationCancellation = new CancellationTokenSource();
        var navigation = cache.GetAsync("a", navigationCancellation.Token);
        navigationCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigation);

        cache.Prefetch("b");
        Assert.False(fetch.TokenFor("a", null).IsCancellationRequested);
        cache.Clear();
        Assert.False(fetch.TokenFor("a", null).IsCancellationRequested);

        fetch.Complete("a", null);
        await Task.Delay(50);
        // The queued intent was retired by Clear() before it could start.
        Assert.Equal(0, fetch.CallCount("b", null));
        Assert.Equal(1, cache.Counters.SpeculativeCancelled);
    }

    [Fact]
    public async Task ContextChangeCancelsUnadoptedSpeculation()
    {
        var fetch = new ScriptedFetch();
        var context = "server/profile-1";
        var cache = new ItemDetailPrefetchCache(fetch.Fetch, contextKey: () => context);

        cache.Prefetch("item", 2);
        await fetch.Started("item", 2).WaitAsync(TestTimeout);
        var first = fetch.TokenFor("item", 2);
        context = "server/profile-2";
        var navigation = cache.GetAsync("item", 2, CancellationToken.None);

        Assert.True(first.IsCancellationRequested);
        fetch.Complete("item", 2);
        await navigation.WaitAsync(TestTimeout);
        Assert.Equal(2, fetch.CallCount("item", 2));
    }

    [Fact]
    public async Task ExpiredSpeculationIsCancelledAndReadAgain()
    {
        var time = new ManualTime();
        var fetch = new ScriptedFetch { HonorCancellation = false };
        var cache = new ItemDetailPrefetchCache(fetch.Fetch, timeProvider: time);

        cache.Prefetch("slow");
        await fetch.Started("slow", null).WaitAsync(TestTimeout);
        var stale = fetch.TokenFor("slow", null);
        time.Now += TimeSpan.FromMinutes(2);
        var navigation = cache.GetAsync("slow", CancellationToken.None);

        Assert.True(stale.IsCancellationRequested);
        fetch.Complete("slow", null);
        await navigation.WaitAsync(TestTimeout);
        Assert.Equal(2, fetch.CallCount("slow", null));
    }

    [Fact]
    public async Task EntriesLiveForTwoMinutes()
    {
        var time = new ManualTime();
        var fetch = new ScriptedFetch();
        var cache = new ItemDetailPrefetchCache(fetch.Fetch, timeProvider: time);
        fetch.Complete("item", 4);

        await cache.GetAsync("item", 4, CancellationToken.None).WaitAsync(TestTimeout);
        time.Now += TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(1);
        await cache.GetAsync("item", 4, CancellationToken.None).WaitAsync(TestTimeout);
        Assert.Equal(1, fetch.CallCount("item", 4));
        time.Now += TimeSpan.FromSeconds(1);
        await cache.GetAsync("item", 4, CancellationToken.None).WaitAsync(TestTimeout);
        Assert.Equal(2, fetch.CallCount("item", 4));
    }

    [Fact]
    public async Task DefaultCapacityOf32EvictsTheOldestEntry()
    {
        var time = new ManualTime();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var cache = new ItemDetailPrefetchCache((id, libraryId, _) =>
        {
            lock (counts) counts[id] = counts.GetValueOrDefault(id) + 1;
            return Task.FromResult(new MediaItemDetail { ContentId = id });
        }, timeProvider: time);

        for (var i = 0; i <= 32; i++)
        {
            await cache.GetAsync($"k{i}", i % 2 == 0 ? null : 1, CancellationToken.None).WaitAsync(TestTimeout);
            time.Now += TimeSpan.FromSeconds(1);
        }
        await cache.GetAsync("k1", 1, CancellationToken.None).WaitAsync(TestTimeout);
        await cache.GetAsync("k32", CancellationToken.None).WaitAsync(TestTimeout);
        Assert.Equal(1, counts["k1"]);
        Assert.Equal(1, counts["k32"]);
        await cache.GetAsync("k0", CancellationToken.None).WaitAsync(TestTimeout);
        Assert.Equal(2, counts["k0"]);
    }

    [Fact]
    public async Task FailedScopedSpeculationIsEvictedSoNavigationRetries()
    {
        var calls = 0;
        var cache = new ItemDetailPrefetchCache((id, libraryId, _) => Interlocked.Increment(ref calls) == 1
            ? Task.FromException<MediaItemDetail>(new InvalidOperationException("offline"))
            : Task.FromResult(new MediaItemDetail { ContentId = id, Title = $"{id}@{libraryId}" }));

        cache.Prefetch("movie", 9);
        for (var n = 0; n < 200 && (Volatile.Read(ref calls) == 0 || cache.Counters.SpeculativeRunning != 0); n++)
            await Task.Delay(10);
        await Task.Delay(20);
        var detail = await cache.GetAsync("movie", 9, CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(2, calls);
        Assert.Equal("movie@9", detail.Title);
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A fetch whose responses, start signals and tokens the test controls.</summary>
    private sealed class ScriptedFetch
    {
        private readonly object _gate = new();
        private readonly Dictionary<(string, int?), TaskCompletionSource<MediaItemDetail>> _responses = new();
        private readonly Dictionary<(string, int?), TaskCompletionSource> _started = new();
        private readonly List<(string Id, int? LibraryId, CancellationToken Token)> _calls = new();
        private int _running;
        private int _peakRunning;

        public bool HonorCancellation { get; init; } = true;
        public int PeakRunning { get { lock (_gate) return _peakRunning; } }

        public TaskCompletionSource<MediaItemDetail> Response(string id, int? libraryId)
        {
            lock (_gate)
            {
                if (!_responses.TryGetValue((id, libraryId), out var response))
                    _responses[(id, libraryId)] = response = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return response;
            }
        }

        public Task Started(string id, int? libraryId) => StartedSource(id, libraryId).Task;

        public void Complete(string id, int? libraryId) => Response(id, libraryId)
            .TrySetResult(new MediaItemDetail { ContentId = id, Title = $"{id}@{libraryId}" });

        public (string, int?)[] CallKeys()
        {
            lock (_gate) return _calls.Select(call => (call.Id, call.LibraryId)).ToArray();
        }

        public int CallCount(string id, int? libraryId)
        {
            lock (_gate) return _calls.Count(call => call.Id == id && call.LibraryId == libraryId);
        }

        public CancellationToken TokenFor(string id, int? libraryId)
        {
            lock (_gate) return _calls.First(call => call.Id == id && call.LibraryId == libraryId).Token;
        }

        public async Task<MediaItemDetail> Fetch(string id, int? libraryId, CancellationToken cancellationToken)
        {
            var response = Response(id, libraryId);
            lock (_gate)
            {
                _calls.Add((id, libraryId, cancellationToken));
                _peakRunning = Math.Max(_peakRunning, ++_running);
            }
            StartedSource(id, libraryId).TrySetResult();
            try
            {
                return HonorCancellation
                    ? await response.Task.WaitAsync(cancellationToken)
                    : await response.Task;
            }
            finally
            {
                lock (_gate) _running--;
            }
        }

        private TaskCompletionSource StartedSource(string id, int? libraryId)
        {
            lock (_gate)
            {
                if (!_started.TryGetValue((id, libraryId), out var started))
                    _started[(id, libraryId)] = started = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return started;
            }
        }
    }
}
