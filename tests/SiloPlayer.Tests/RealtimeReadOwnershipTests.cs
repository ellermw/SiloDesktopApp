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
        internal readonly Clock Clock = new();
        private readonly RealtimeMediaStateRefresh _refresh;
        internal Fixture()
        {
            _http = new(Wire); Client = new(_http); Client.SetBaseUrl("https://review-owned.invalid"); Client.SetProfile("profile");
            _auth = new(Client, new AuthApi(Client));
            var catalog = new CatalogApi(Client);
            var cache = new ItemDetailPrefetchCache(catalog, _auth);
            DetailCacheInvalidation.Attach(cache, Messenger);
            Revision = DetailCacheInvalidation.AttachUserStateRevision(Messenger);
            Vm = new(catalog, cache);
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
        internal int Count, DetailReads, EpisodeReads, WatchReads;
        internal double Position = 31;
        internal string? Target = EpisodeA;
        internal bool HoldDetails, HoldEpisodes;
        internal Action? BeforeWatchResponse;
        internal readonly List<Uri> CatalogUris = [];
        private readonly Channel<Read> _held = Channel.CreateUnbounded<Read>();
        private readonly List<Read> _all = [];
        internal Task<Read> NextAsync() => _held.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        internal void ReleaseAll() { lock (_all) foreach (var read in _all) read.Release(); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("review-owned.invalid", request.RequestUri!.Host);
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri.AbsolutePath;
            object body;
            var hold = false;
            if (path.StartsWith("/api/v2/watch/"))
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
                else if (path.EndsWith("/seasons")) body = new { items = new[] { new { content_id = "series:review-S01", season_number = 1, episode_count = 8, user_data = new { watched_count = Count } } } };
                else
                {
                    Assert.StartsWith("/api/v2/catalog/items/", path);
                    DetailReads++; hold = HoldDetails;
                    body = new { content_id = Series, type = "series", episode_count = 8, play_content_id = Target,
                        user_state = new { }, user_data = new { watched_count = Count, played = false } };
                }
            }
            var snapshot = JsonSerializer.Serialize(body);
            if (hold)
            {
                var read = new Read(); lock (_all) _all.Add(read); _held.Writer.TryWrite(read);
                await read.Pending.WaitAsync(ct);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(snapshot, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Read
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Pending => _ready.Task;
        internal void Release() => _ready.TrySetResult();
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
