using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

public sealed class RealtimeMediaStateRefreshTests
{
    [Fact]
    public void BurstRetainsEveryNamedSeriesIncludingEarlierNamesOmittedByLaterEvents()
    {
        using var fixture = new Fixture();
        fixture.Send("episode-a", "series-a", "watched"); fixture.Drain();
        fixture.Clock.Advance(100);
        fixture.Send("episode-a", null); fixture.Send("episode-a", "series-b"); fixture.Send("episode-b", "series-c"); fixture.Drain();
        fixture.Clock.Advance(199); fixture.Drain();
        Assert.Empty(fixture.Messages.Where(m => m.RefreshDetails));
        fixture.Clock.Advance(1); fixture.Drain();
        var batch = Assert.Single(fixture.Messages.Where(m => m.RefreshDetails));
        Assert.Equal(["episode-a", "episode-b"], batch.ContentIds.Order().ToArray());
        Assert.Equal(["series-a", "series-b", "series-c"], batch.SeriesIds.Order().ToArray());
    }

    [Fact]
    public void ContinuousProgressCannotPostponeTheRefreshPastOneSecond()
    {
        using var fixture = new Fixture();
        for (var n = 0; n < 10; n++)
        {
            fixture.Send("episode", "series"); fixture.Drain();
            fixture.Clock.Advance(100); fixture.Drain();
        }
        Assert.Single(fixture.Messages.Where(m => m.RefreshDetails));
    }

    [Fact]
    public void LocalProgressUsesTheSameDeadlineAndRetainsItsUnionedScope()
    {
        using var fixture = new Fixture();
        var messages = new List<MediaStateInvalidated>();
        using var local = new RealtimeMediaStateRefresh(new CatalogApi(fixture.Client),
            action => action(), messages.Add, fixture.Clock);
        for (var n = 0; n < 10; n++)
        {
            local.Invalidate(new(fixture.Client.CaptureContext(), new HashSet<string>(["episode-" + n]),
                new HashSet<string>(["series-" + n])));
            fixture.Clock.Advance(100);
        }

        var batch = Assert.Single(messages.Where(m => m.RefreshDetails));
        Assert.Equal(10, batch.ContentIds.Count);
        Assert.Equal(10, batch.SeriesIds.Count);
        Assert.Empty(fixture.Messages); // local batching must not rebroadcast global invalidations
    }

    [Fact]
    public void InputQueuedAheadOfTheDueTimerCannotRetireTheDeadlineFlush()
    {
        using var fixture = new Fixture();
        fixture.Send("first", "series"); fixture.Drain();
        fixture.Clock.Advance(100);
        fixture.Send("input-ahead-of-timer", "other-series");
        fixture.Clock.Advance(900); // dispatcher was busy; receipt precedes the due timer callback
        fixture.Drain();
        var batch = Assert.Single(fixture.Messages.Where(m => m.RefreshDetails));
        Assert.Equal(["first", "input-ahead-of-timer"], batch.ContentIds.Order().ToArray());
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("subscription")]
    [InlineData("window")]
    public void RetiredOwnershipCannotPublishDeferredRefreshes(string departure)
    {
        using var fixture = new Fixture();
        fixture.Send("first", "series"); fixture.Drain();
        fixture.Clock.Advance(200); // timer callback is now queued
        fixture.Send("queued", "series"); // invalidated now; visible refresh is still queued
        Assert.Equal(2, fixture.Messages.Count);
        Assert.All(fixture.Messages, message => Assert.False(message.RefreshDetails));
        if (departure == "profile") fixture.Client.SetProfile("replacement");
        else if (departure == "subscription") fixture.Refresh.Reset();
        else fixture.Refresh.Dispose();
        fixture.Drain();
        fixture.Clock.Advance(200); fixture.Drain();
        Assert.Equal(2, fixture.Messages.Count); // no deferred publication after ownership retires
        Assert.All(fixture.Messages, message => Assert.False(message.RefreshDetails));
    }

    [Fact]
    public async Task AcceptedEventImmediatelyRetiresPrefetchedDetailsAndWatchRevisions()
    {
        using var fixture = new Fixture();
        var reads = 0;
        var cache = new ItemDetailPrefetchCache((id, _) => { reads++; return Task.FromResult(new MediaItemDetail { ContentId = id }); });
        DetailCacheInvalidation.Attach(cache, fixture.Messenger);
        var revision = DetailCacheInvalidation.AttachUserStateRevision(fixture.Messenger);
        await cache.GetAsync("series", CancellationToken.None);
        var stamp = revision.Current;
        fixture.Send("never-cached", "series");
        // Neither cache nor /watch freshness may wait for the UI dispatcher.
        Assert.False(Assert.Single(fixture.Messages).RefreshDetails);
        Assert.False(revision.IsCurrent(stamp, "never-cached", "series"));
        Assert.True(revision.IsCurrent(stamp, "unrelated", "other-series"));
        await cache.GetAsync("series", CancellationToken.None);
        Assert.Equal(2, reads);
        var receivedRevision = revision.Current;
        fixture.Drain(); fixture.Clock.Advance(200); fixture.Drain();
        Assert.Single(fixture.Messages.Where(message => message.RefreshDetails));
        Assert.Equal(receivedRevision, revision.Current);
    }

    [Fact]
    public void CatchUpCoalescesWithEventsAndPreservesCapturedOwnership()
    {
        using var fixture = new Fixture();
        var revision = DetailCacheInvalidation.AttachUserStateRevision(fixture.Messenger);
        var stamp = revision.Current;
        var context = fixture.Client.CaptureContext();
        fixture.Refresh.CatchUp(context); fixture.Send("episode", "series"); fixture.Drain();
        Assert.False(revision.IsCurrent(stamp, "unrelated", "other-series"));
        var receivedRevision = revision.Current;
        fixture.Clock.Advance(200); fixture.Drain();
        var batch = Assert.Single(fixture.Messages.Where(m => m.RefreshDetails));
        Assert.True(batch.CatchUp); Assert.Equal(context, batch.Context);
        Assert.Equal(receivedRevision, revision.Current);
        Assert.Contains("series", batch.SeriesIds);
        fixture.Client.SetProfile("replacement");
        fixture.Refresh.CatchUp(context); fixture.Drain(); fixture.Clock.Advance(200); fixture.Drain();
        Assert.Single(fixture.Messages.Where(m => m.RefreshDetails));
    }

    [Theory]
    [InlineData("favorite")]
    [InlineData("watchlist")]
    [InlineData("rating")]
    [InlineData("not_interested")]
    public void ViewerStateEventsRefreshOnlyTheirCapturedContentScope(string change)
    {
        using var fixture = new Fixture();
        fixture.Send("series", null, change); fixture.Drain();
        var immediate = Assert.Single(fixture.Messages);
        Assert.False(immediate.RefreshDetails);
        Assert.False(immediate.CatchUp);
        Assert.Equal(new[] { "series" }, immediate.ContentIds);
        Assert.Equal(new[] { "series" }, immediate.ViewerStateContentIds);
        fixture.Clock.Advance(200); fixture.Drain();
        var batch = Assert.Single(fixture.Messages.Where(m => m.RefreshDetails));
        Assert.Equal(new[] { "series" }, batch.ContentIds);
        Assert.Equal(new[] { "series" }, batch.ViewerStateContentIds);
        Assert.Empty(batch.SeriesIds);
        Assert.False(batch.CatchUp);
        Assert.Equal(fixture.Client.CaptureContext(), batch.Context);
    }

    [Theory]
    [InlineData("favorite")]
    [InlineData("watchlist")]
    [InlineData("rating")]
    [InlineData("not_interested")]
    public async Task ViewerStateEventsInvalidateDetailsWithoutConsumingWatchRetries(string change)
    {
        using var fixture = new Fixture();
        var revision = DetailCacheInvalidation.AttachUserStateRevision(fixture.Messenger);
        var freshness = new WatchDetailFreshness<WatchDetailResponse>(revision, TimeSpan.Zero,
            detail => detail.ContentId, detail => detail.SeriesId);
        var detailReads = 0;
        var cache = new ItemDetailPrefetchCache((id, _) =>
        {
            detailReads++;
            return Task.FromResult(new MediaItemDetail { ContentId = id });
        });
        DetailCacheInvalidation.Attach(cache, fixture.Messenger);
        await cache.GetAsync("episode", CancellationToken.None);
        var stamp = revision.Current;
        var watchReads = 0;
        var evictions = 0;
        var watch = await freshness.GetCurrentAsync(ct => freshness.ReadAsync("episode", _ =>
        {
            watchReads++;
            fixture.Send("episode", "series:show", change);
            return Task.FromResult(new WatchDetailResponse { ContentId = "episode", SeriesId = "series:show" });
        }, ct), _ => evictions++, CancellationToken.None);

        Assert.Equal(1, watchReads);
        Assert.Equal(0, evictions);
        Assert.True(freshness.IsCurrent(watch));
        Assert.Equal(stamp, revision.Current);
        await cache.GetAsync("episode", CancellationToken.None);
        Assert.Equal(2, detailReads);
        fixture.Drain(); fixture.Clock.Advance(200); fixture.Drain();
        Assert.Single(fixture.Messages.Where(message => message.RefreshDetails));
        Assert.Equal(stamp, revision.Current);
        Assert.True(freshness.IsCurrent(watch));
    }

    [Fact]
    public void MixedBatchesRetainOnlyTheViewerStateContentScopeAndResetItAfterFlush()
    {
        using var fixture = new Fixture();
        var revision = DetailCacheInvalidation.AttachUserStateRevision(fixture.Messenger);
        var stamp = revision.Current;
        fixture.Send("favorite-movie", null, "favorite");
        fixture.Send("playing-episode", "series", "progress");
        var receivedRevision = revision.Current;
        fixture.Drain(); fixture.Clock.Advance(200); fixture.Drain();
        Assert.Equal(receivedRevision, revision.Current);
        Assert.True(revision.IsCurrent(stamp, "favorite-movie", null));
        Assert.False(revision.IsCurrent(stamp, "playing-episode", "series"));
        Assert.True(revision.IsCurrent(stamp, "sibling-episode", "series"));
        var batch = Assert.Single(fixture.Messages.Where(m => m.RefreshDetails));
        Assert.Equal(new[] { "favorite-movie", "playing-episode" }, batch.ContentIds.Order().ToArray());
        Assert.Equal(new[] { "favorite-movie" }, batch.ViewerStateContentIds);
        fixture.Messages.Clear();
        fixture.Send("playing-episode", "series", "progress");
        fixture.Drain(); fixture.Clock.Advance(200); fixture.Drain();
        Assert.Empty(Assert.Single(fixture.Messages.Where(m => m.RefreshDetails)).ViewerStateContentIds!);
    }

    [Theory]
    [InlineData("watched", "favorite", false)]
    [InlineData("watched", "favorite", true)]
    [InlineData("progress", "rating", false)]
    [InlineData("progress", "rating", true)]
    public void SameItemMixedBatchRetainsBothKindsAndClearsPlaybackScopeAfterFlush(
        string playbackChange, string viewerChange, bool viewerFirst)
    {
        using var fixture = new Fixture();
        fixture.Send("movie", null, viewerFirst ? viewerChange : playbackChange);
        fixture.Send("movie", null, viewerFirst ? playbackChange : viewerChange);
        fixture.Drain(); fixture.Clock.Advance(200); fixture.Drain();
        var batch = Assert.Single(fixture.Messages.Where(m => m.RefreshDetails));
        Assert.Equal(new[] { "movie" }, batch.ContentIds);
        Assert.Equal(new[] { "movie" }, batch.ViewerStateContentIds);
        Assert.Equal(new[] { "movie" }, batch.PlaybackStateContentIds);

        fixture.Messages.Clear();
        fixture.Send("movie", null, viewerChange);
        fixture.Drain(); fixture.Clock.Advance(200); fixture.Drain();
        batch = Assert.Single(fixture.Messages.Where(m => m.RefreshDetails));
        Assert.Equal(new[] { "movie" }, batch.ViewerStateContentIds);
        Assert.Empty(batch.PlaybackStateContentIds!);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void NonStringProfileIdsStillInvalidateAndRefreshTheCapturedProfile(string profileJson)
    {
        using var fixture = new Fixture();
        using var json = JsonDocument.Parse(
            $$"""{"profile_id": {{profileJson}}, "content_id": "episode", "series_id": "series", "change": "watched"}""");
        fixture.Refresh.ReceiveRealtime("user_state", "user_state.changed", json.RootElement);

        var immediate = Assert.Single(fixture.Messages);
        Assert.False(immediate.RefreshDetails);
        Assert.Equal(fixture.Client.CaptureContext(), immediate.Context);
        Assert.Equal(new[] { "episode" }, immediate.ContentIds);
        Assert.Equal(new[] { "series" }, immediate.SeriesIds);

        fixture.Drain(); fixture.Clock.Advance(200); fixture.Drain();
        var batch = Assert.Single(fixture.Messages.Where(m => m.RefreshDetails));
        Assert.Equal(immediate.Context, batch.Context);
        Assert.Equal(new[] { "episode" }, batch.ContentIds);
        Assert.Equal(new[] { "series" }, batch.SeriesIds);
    }

    [Theory]
    [InlineData("other", "progress", "episode", "user_state", "user_state.changed")]
    [InlineData("profile", "unknown", "episode", "user_state", "user_state.changed")]
    [InlineData("other", "favorite", "series", "user_state", "user_state.changed")]
    [InlineData("profile", "watched", " ", "user_state", "user_state.changed")]
    [InlineData("profile", "progress", "episode", "notifications", "user_state.changed")]
    [InlineData("profile", "progress", "episode", "user_state", "wrong")]
    public void UnrelatedOrInvalidEventsHaveNoSideEffects(string profile, string kind, string id, string channel, string name)
    {
        using var fixture = new Fixture();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { profile_id = profile, change = kind, content_id = id }));
        fixture.Refresh.ReceiveRealtime(channel, name, json.RootElement);
        fixture.Drain(); fixture.Clock.Advance(200); fixture.Drain();
        Assert.Empty(fixture.Messages);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http = new(new RejectHttp());
        private readonly Queue<Action> _queue = new();
        internal readonly WeakReferenceMessenger Messenger = new();
        internal readonly List<MediaStateInvalidated> Messages = [];
        internal readonly ManualClock Clock = new();
        internal readonly SiloApiClient Client;
        internal readonly RealtimeMediaStateRefresh Refresh;
        internal Fixture()
        {
            Client = new SiloApiClient(_http); Client.SetBaseUrl("https://realtime.invalid"); Client.SetProfile("profile");
            Messenger.Register<List<MediaStateInvalidated>, MediaStateInvalidated>(Messages, static (owner, message) => owner.Add(message));
            Refresh = new(Client, _queue.Enqueue, Messenger, Clock);
        }
        internal void Send(string id, string? series, string kind = "progress")
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { profile_id = "profile", content_id = id, series_id = series, change = kind, played = true, position_seconds = 900 }));
            Refresh.ReceiveRealtime("user_state", "user_state.changed", json.RootElement);
        }
        internal void Drain() { while (_queue.TryDequeue(out var action)) action(); }
        public void Dispose() { Refresh.Dispose(); _http.Dispose(); }
    }

    private sealed class RejectHttp : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Realtime batching must not make HTTP requests.");
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        private readonly List<Timer> _timers = [];
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state); timer.Change(dueTime, period); _timers.Add(timer); return timer;
        }
        internal void Advance(int milliseconds)
        {
            _now += TimeSpan.FromMilliseconds(milliseconds);
            foreach (var timer in _timers.Where(t => t.Due <= _now).ToArray()) timer.Fire();
        }
        private sealed class Timer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            internal DateTimeOffset Due = DateTimeOffset.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period) { Due = clock._now + dueTime; return true; }
            internal void Fire() { Due = DateTimeOffset.MaxValue; callback(state); }
            public void Dispose() => Due = DateTimeOffset.MaxValue;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
