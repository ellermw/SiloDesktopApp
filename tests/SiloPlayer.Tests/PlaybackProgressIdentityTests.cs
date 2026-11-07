using System.Net;
using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackProgressIdentityTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task WatchBarrierRejectsRetiredIdentityIncludingAba(bool aba)
    {
        using var scope = new Fixture();
        var original = scope.Client.CaptureIdentityContext();
        var save = new TaskCompletionSource();
        var freshness = new WatchDetailFreshness<object>(new(), TimeSpan.FromSeconds(5), _ => "episode:a", _ => null);
        freshness.TrackFinalSave("episode:a", save.Task, () => scope.Client.IsCurrentIdentityContext(original));
        scope.Client.SetProfile("second");
        if (aba) scope.Client.SetProfile("first");
        var expected = new object();
        try
        {
            var value = await freshness.ReadAsync("episode:a", _ => Task.FromResult(expected), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Same(expected, value);
            Assert.False(save.Task.IsCompleted);
        }
        finally { save.TrySetResult(); }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task WatchBarrierRetainsEveryCurrentSaveAcrossAccessRefresh(bool legacy)
    {
        using var scope = new Fixture();
        var original = scope.Client.CaptureIdentityContext();
        var first = new TaskCompletionSource(); var second = new TaskCompletionSource();
        var freshness = new WatchDetailFreshness<object>(new(), TimeSpan.FromSeconds(5), _ => "episode:a", _ => null);
        Func<bool>? current = legacy ? null : () => scope.Client.IsCurrentIdentityContext(original);
        freshness.TrackFinalSave("episode:a", first.Task, current);
        freshness.TrackFinalSave("episode:a", second.Task, current);
        scope.Client.InvalidateAccessContext();
        var called = false;
        var read = freshness.ReadAsync("episode:a", _ => { called = true; return Task.FromResult(new object()); }, CancellationToken.None);
        try
        {
            Assert.False(called); first.SetResult(); Assert.False(called);
            second.SetResult(); await read.WaitAsync(TimeSpan.FromSeconds(1)); Assert.True(called);
        }
        finally { first.TrySetResult(); second.TrySetResult(); }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ForeignAndAbaPlaybackMessagesCannotRetireTheCurrentCacheOrRevision(bool aba)
    {
        using var scope = new Fixture();
        var original = scope.Client.CaptureIdentityContext();
        scope.Client.SetProfile("second");
        if (aba) scope.Client.SetProfile("first");
        var current = await scope.Cache.GetAsync("episode:a", CancellationToken.None);
        var revision = scope.Revision.Current;
        var cacheRevision = scope.Cache.GetProgressSaveRevision("episode:a");
        var saved = new TaskCompletionSource();
        try
        {
            scope.Messenger.Send(new PlaybackProgressUpdated("episode:a", 90, 100, false) { Identity = original, Persisted = saved.Task });
            Assert.Same(current, await scope.Cache.GetAsync("episode:a", CancellationToken.None));
            Assert.Equal(revision, scope.Revision.Current);
            Assert.Equal(cacheRevision, scope.Cache.GetProgressSaveRevision("episode:a"));
            await scope.Cache.WaitForProgressSettlementAsync(CancellationToken.None, "episode:a").WaitAsync(TimeSpan.FromSeconds(1));
            saved.SetResult();
            Assert.Same(current, await scope.Cache.GetAsync("episode:a", CancellationToken.None));
            Assert.Equal(revision, scope.Revision.Current);
        }
        finally { saved.TrySetResult(); }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ACurrentSaveCannotClearItsSuccessorProfileCacheWhenItFinishes(bool aba)
    {
        using var scope = new Fixture();
        var original = scope.Client.CaptureIdentityContext();
        var saved = new TaskCompletionSource();
        try
        {
            scope.Messenger.Send(new PlaybackProgressUpdated("episode:a", 90, 100, false) { Identity = original, Persisted = saved.Task });
            scope.Client.SetProfile("second");
            if (aba) scope.Client.SetProfile("first");
            var current = await scope.Cache.GetAsync("episode:a", CancellationToken.None);
            var revision = scope.Revision.Current;
            await scope.Cache.WaitForProgressSettlementAsync(CancellationToken.None, "episode:a").WaitAsync(TimeSpan.FromSeconds(1));
            saved.SetResult();
            Assert.Same(current, await scope.Cache.GetAsync("episode:a", CancellationToken.None));
            Assert.Equal(revision, scope.Revision.Current);
        }
        finally { saved.TrySetResult(); }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CurrentIdentityAndLegacyMessagesSurviveAccessRefreshAndKeepAllSaveBarriers(bool legacy)
    {
        using var scope = new Fixture();
        var original = scope.Client.CaptureIdentityContext();
        var first = new TaskCompletionSource(); var second = new TaskCompletionSource();
        scope.Client.InvalidateAccessContext();
        Assert.True(scope.Client.IsCurrentIdentityContext(original));
        try
        {
            var current = await scope.Cache.GetAsync("episode:a", CancellationToken.None);
            scope.Messenger.Send(new PlaybackProgressUpdated("episode:a", 80, 100, false) { Identity = legacy ? null : original, Persisted = first.Task });
            scope.Messenger.Send(new PlaybackProgressUpdated("episode:a", 90, 100, false) { Identity = legacy ? null : original, Persisted = second.Task });
            Assert.NotSame(current, await scope.Cache.GetAsync("episode:a", CancellationToken.None));
            Assert.Equal(2, scope.Revision.Current);
            var waiting = scope.Cache.WaitForProgressSettlementAsync(CancellationToken.None, "episode:a");
            Assert.False(waiting.IsCompleted);
            first.SetResult(); Assert.False(waiting.IsCompleted);
            second.SetResult(); await waiting.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(4, scope.Revision.Current);
        }
        finally { first.TrySetResult(); second.TrySetResult(); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http = new(new Wire());
        private readonly AuthService _auth;
        public SiloApiClient Client { get; }
        public ItemDetailPrefetchCache Cache { get; }
        public UserStateRevision Revision { get; }
        public WeakReferenceMessenger Messenger { get; } = new();
        public Fixture()
        {
            Client = new(_http); Client.SetBaseUrl("https://progress-identity.invalid"); Client.SetProfile("first");
            _auth = new(Client, new AuthApi(Client));
            Cache = new(new CatalogApi(Client), _auth);
            var current = (Func<ApiIdentityContext, bool>)Client.IsCurrentIdentityContext;
            // The original public methods remain callable. Reflection lets
            // this exact behavioral regression run before optional guards exist.
            var attach = typeof(DetailCacheInvalidation).GetMethod(nameof(DetailCacheInvalidation.Attach))!;
            attach.Invoke(null, attach.GetParameters().Length == 3 ? [Cache, Messenger, current] : [Cache, Messenger]);
            var revision = typeof(DetailCacheInvalidation).GetMethod(nameof(DetailCacheInvalidation.AttachUserStateRevision))!;
            Revision = (UserStateRevision)revision.Invoke(null,
                revision.GetParameters().Length == 2 ? [Messenger, current] : [Messenger])!;
        }
        public void Dispose() { _auth.Dispose(); _http.Dispose(); }
    }

    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "progress-identity.invalid") throw new InvalidOperationException("Unexpected fixture host.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"content_id":"episode:a","type":"episode","user_state":{"position_seconds":0}}""") });
        }
    }
}
