using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class ServerActivityStoreTests
{
    private sealed class FakeChannel : IAdminRealtimeChannel
    {
        public readonly Dictionary<string, int> Refs = new();
        public readonly Dictionary<string, JsonElement> Latest = new();
        public WebSocketState CurrentState { get; set; } = WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged;

        public IDisposable Subscribe(params string[] channels)
        {
            foreach (var channel in channels) Refs[channel] = Refs.GetValueOrDefault(channel) + 1;
            return new Handle(() => { foreach (var channel in channels) Refs[channel]--; });
        }

        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) => Latest.TryGetValue(channel, out snapshot);
        public void Snapshot(string channel, string json) => SnapshotReceived?.Invoke(channel, JsonDocument.Parse(json).RootElement.Clone());
        public void Event(string channel, string name, string json) => EventReceived?.Invoke(channel, name, JsonDocument.Parse(json).RootElement.Clone());
        public void State(WebSocketState state) { CurrentState = state; StateChanged?.Invoke(state); }
        public int Active => Refs.Values.Sum();

        private sealed class Handle(Action release) : IDisposable
        {
            private Action? _release = release;
            public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }

    private sealed class ManualScheduler : IAdminScheduler
    {
        private readonly List<(DateTimeOffset Due, Action Callback, Cancel Token)> _timers = [];
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.Parse("2026-11-14T12:00:00Z");

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var token = new Cancel();
            lock (_timers) _timers.Add((UtcNow + delay, callback, token));
            return token;
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                (DateTimeOffset Due, Action Callback, Cancel Token) next;
                lock (_timers)
                {
                    var due = _timers.Where(t => t.Due <= UtcNow).OrderBy(t => t.Due).FirstOrDefault();
                    if (due.Callback == null) return;
                    _timers.Remove(due);
                    next = due;
                }
                if (!next.Token.Cancelled) next.Callback();
            }
        }

        public int Pending { get { lock (_timers) return _timers.Count(t => !t.Token.Cancelled); } }

        public sealed class Cancel : IDisposable
        {
            public bool Cancelled;
            public void Dispose() => Cancelled = true;
        }
    }

    private sealed class Harness : IDisposable
    {
        public readonly AdminMonitorKit Kit;
        public readonly AdminMonitorAuthority Authority;
        public readonly FakeChannel Channel = new();
        public readonly ManualScheduler Scheduler = new();
        public readonly ServerActivityStore Store;
        public readonly ConcurrentDictionary<string, int> Hits = new();
        public Func<string, Task<HttpResponseMessage>>? Override;
        public string Sessions = """{"items":[],"page":{"has_more":false}}""";
        public string Preparations = """{"counts":{"running":0,"queued":0,"retrying":0,"paused":0,"failed_recent":0},"items":[]}""";

        public Harness(string role = "admin")
        {
            Kit = AdminMonitorKit.Create(role);
            Kit.Http.Respond = async (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                Hits.AddOrUpdate(path, 1, (_, n) => n + 1);
                if (Override != null) return await Override(path);
                return path switch
                {
                    "/api/v2/admin/sessions" => AdminMonitorKit.Json(Sessions),
                    "/api/v2/admin/tasks" => AdminMonitorKit.Json("""{"items":[{"key":"k","name":"Hidden","description":"","category":"system","state":"running","progress":0}]}"""),
                    "/api/v2/libraries" => AdminMonitorKit.Json("""{"items":[{"id":"1","name":"Movies","type":"movies"}]}"""),
                    "/api/v2/admin/downloads/preparations" => AdminMonitorKit.Json(Preparations),
                    _ => AdminMonitorKit.Json("{}", HttpStatusCode.NotFound),
                };
            };
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Store = new ServerActivityStore(new AdminMonitorApi(Kit.Client), Authority, Channel, Scheduler);
        }

        public int HitCount(string path) => Hits.GetValueOrDefault(path);

        public void Dispose()
        {
            Store.Dispose();
            Authority.Dispose();
            Kit.Dispose();
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task OrdinaryUserMakesNoPrivilegedReadsOrSubscriptions()
    {
        using var h = new Harness("user");
        using var demand = h.Store.AcquireReads();
        await Task.Delay(50);
        Assert.False(h.Store.Current.IsActingAdmin);
        Assert.Equal(0, h.Channel.Active);
        Assert.Empty(h.Hits);
    }

    [Fact]
    public async Task ActingAdminLeasesAdminChannelsOnceAndReadsOnDemand()
    {
        using var h = new Harness();
        Assert.True(h.Store.Current.IsActingAdmin);
        Assert.All(ServerActivityStore.AdminChannels, channel => Assert.Equal(1, h.Channel.Refs[channel]));
        Assert.Empty(h.Hits); // no reads until a consumer exists

        using (h.Store.AcquireReads())
        using (h.Store.AcquireReads())
        {
            await Until(() => h.Store.Current.SessionsLoaded && h.Store.Current.Tasks.Count == 1 && h.Store.Current.LibraryNames.Count == 1
                              && h.Store.Current.Preparations != null);
        }
        Assert.Equal(1, h.HitCount("/api/v2/admin/sessions"));
        Assert.Equal(1, h.HitCount("/api/v2/admin/tasks"));
        Assert.Equal("Movies", h.Store.Current.LibraryNames["1"]);
        Assert.Equal(6, h.Channel.Active); // one lease for many consumers
    }

    [Fact]
    public async Task RoleLossRetiresDataTimersAndChannelLeaseAndRegrantDoesNotRevive()
    {
        using var h = new Harness();
        h.Sessions = """{"items":[{"session_id":"s","user_id":"7","username":"u","profile_id":"p","media_file_id":"1","requested_media_file_id":"1","media_title":"T","media_type":"movie","play_method":"direct","reporting_node":"","started_at":"2026-11-14T10:00:00Z","updated_at":"2026-11-14T10:00:00Z","position_seconds":0,"is_paused":false,"has_playback_control":true}],"page":{"has_more":false}}""";
        using var demand = h.Store.AcquireReads();
        await Until(() => h.Store.Current.Sessions.Count == 1);
        var epoch = h.Store.Current.Epoch;

        h.Kit.SetRole("user");
        Assert.False(h.Store.Current.IsActingAdmin);
        Assert.Empty(h.Store.Current.Sessions);
        Assert.Equal(0, h.Channel.Active);

        h.Kit.SetRole("admin");
        Assert.True(h.Store.Current.IsActingAdmin);
        Assert.True(h.Store.Current.Epoch > epoch);
        Assert.Equal(6, h.Channel.Active);
        await Until(() => h.Store.Current.Sessions.Count == 1);
    }

    [Fact]
    public async Task LateReadFromRetiredAuthorityIsDropped()
    {
        using var h = new Harness();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionCalls = 0;
        h.Override = path => path != "/api/v2/admin/sessions"
            ? Task.FromResult(AdminMonitorKit.Json("""{"items":[]}"""))
            : Interlocked.Increment(ref sessionCalls) == 1
                ? gate.Task
                : Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}"""));
        using var demand = h.Store.AcquireReads();
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 1);

        h.Kit.Client.InvalidateAccessContext(); // access_changed / catch-up
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 2);
        gate.SetResult(AdminMonitorKit.Json("""{"items":[{"session_id":"old","user_id":"7","username":"u","profile_id":"p","media_file_id":"1","requested_media_file_id":"1","media_title":"Old","media_type":"movie","play_method":"direct","reporting_node":"","started_at":"2026-11-14T10:00:00Z","updated_at":"2026-11-14T10:00:00Z","position_seconds":0,"is_paused":false,"has_playback_control":true}],"page":{"has_more":false}}"""));
        await Until(() => h.Store.Current.SessionsLoaded);
        Assert.Empty(h.Store.Current.Sessions);
    }

    [Fact]
    public async Task ConnectionDotAppearsOnlyAfterFourContinuousNonLiveSeconds()
    {
        using var h = new Harness();
        h.Channel.State(WebSocketState.Closed);
        Assert.Equal(AdminRealtimeConnectionState.Disconnected, h.Store.Current.Connection);
        h.Scheduler.Advance(TimeSpan.FromSeconds(3));
        Assert.False(h.Store.Current.ConnectionProblem);
        h.Channel.State(WebSocketState.Connecting); // still non-live: clock continues
        h.Scheduler.Advance(TimeSpan.FromSeconds(1.1));
        Assert.True(h.Store.Current.ConnectionProblem);
        h.Channel.State(WebSocketState.Open);
        Assert.False(h.Store.Current.ConnectionProblem);
        Assert.Equal(AdminRealtimeConnectionState.Live, h.Store.Current.Connection);

        h.Channel.State(WebSocketState.Closed);
        h.Scheduler.Advance(TimeSpan.FromSeconds(2));
        h.Channel.State(WebSocketState.Open);
        h.Scheduler.Advance(TimeSpan.FromSeconds(5));
        Assert.False(h.Store.Current.ConnectionProblem);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ScansHydrateFromChannelAndPatchByEvent()
    {
        using var h = new Harness();
        Assert.False(h.Store.Current.ScansLoaded);
        using var demand = h.Store.AcquireReads();
        h.Channel.Snapshot("scans", """[{"id":"a","library_id":1,"mode":"library","status":"running"}]""");
        Assert.True(h.Store.Current.ScansLoaded);
        Assert.Equal("1", Assert.Single(h.Store.Current.Scans).LibraryId);

        h.Channel.Event("scans", "scan.updated", """{"id":"b","library_id":1,"mode":"file","status":"accepted"}""");
        Assert.Equal(2, h.Store.Current.Scans.Count);
        await Until(() => h.HitCount("/api/v2/libraries") >= 1);
        var librariesBefore = h.HitCount("/api/v2/libraries");
        h.Channel.Event("scans", "scan.completed", """{"id":"a","library_id":1,"mode":"library","status":"completed"}""");
        Assert.Single(h.Store.Current.Scans);
        await Until(() => h.HitCount("/api/v2/libraries") > librariesBefore);
    }

    [Fact]
    public async Task SessionEventsRefreshWithLeadingAndTrailingCoalescing()
    {
        using var h = new Harness();
        using var demand = h.Store.AcquireReads();
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 1);

        h.Channel.Event("sessions", "sessions.replaced", "{}");
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 2);
        h.Channel.Event("sessions", "sessions.replaced", "{}");
        h.Channel.Event("sessions", "sessions.replaced", "{}");
        await Task.Delay(50);
        Assert.Equal(2, h.HitCount("/api/v2/admin/sessions"));
        h.Scheduler.Advance(TimeSpan.FromSeconds(5));
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 3);
        h.Channel.Event("sessions", "session.started", "{}"); // only sessions.replaced re-reads
        await Task.Delay(50);
        Assert.Equal(3, h.HitCount("/api/v2/admin/sessions"));
    }

    [Fact]
    public async Task PreparationProgressPatchesWithoutReadAndMaintenanceRunsEverySixtySeconds()
    {
        using var h = new Harness();
        h.Preparations = """{"counts":{"running":1,"queued":0,"retrying":0,"paused":0,"failed_recent":0},"items":[{"id":"j","state":"running","format":"remux","media_file_id":"1","media_title":"T","media_type":"movie","output":{"container":"mp4","video_codec":"copy","audio_codec":"copy"},"progress_unavailable":false}]}""";
        using var demand = h.Store.AcquireReads();
        await Until(() => h.Store.Current.Preparations?.Items.Count == 1);
        var reads = h.HitCount("/api/v2/admin/downloads/preparations");

        h.Channel.Event("download_preparations", "download_preparation.progress",
            """{"id":"j","progress":{"encoded_seconds":30,"duration_seconds":60,"speed":2,"updated_at":"2026-11-14T12:00:05Z"}}""");
        Assert.Equal(30, h.Store.Current.Preparations!.Items[0].Progress!.EncodedSeconds);
        await Task.Delay(30);
        Assert.Equal(reads, h.HitCount("/api/v2/admin/downloads/preparations"));

        h.Scheduler.Advance(TimeSpan.FromSeconds(60));
        await Until(() => h.HitCount("/api/v2/admin/downloads/preparations") == reads + 1);
    }

    [Fact]
    public async Task FailureExpiryReadsOnceAfterTheTwentyFourHourWindow()
    {
        using var h = new Harness();
        h.Preparations = """{"counts":{"running":0,"queued":0,"retrying":0,"paused":0,"failed_recent":1},"items":[{"id":"f","state":"failed","format":"remux","media_file_id":"1","media_title":"T","media_type":"movie","failed_at":"2026-11-13T12:10:00Z","output":{"container":"mp4","video_codec":"copy","audio_codec":"copy"},"progress_unavailable":false}]}""";
        using var demand = h.Store.AcquireReads();
        await Until(() => h.Store.Current.Preparations?.Counts.FailedRecent == 1);
        var reads = h.HitCount("/api/v2/admin/downloads/preparations");
        h.Scheduler.Advance(TimeSpan.FromMinutes(10));
        await Task.Delay(30);
        Assert.Equal(reads, h.HitCount("/api/v2/admin/downloads/preparations"));
        h.Scheduler.Advance(TimeSpan.FromSeconds(1));
        await Until(() => h.HitCount("/api/v2/admin/downloads/preparations") == reads + 1);
    }

    [Fact]
    public async Task UnavailablePreparationQueueHidesTheSection()
    {
        using var h = new Harness();
        h.Override = path => Task.FromResult(path == "/api/v2/admin/downloads/preparations"
            ? AdminMonitorKit.Json("""{"type":"https://siloserver.org/docs/api/v2/problems/dependency_unavailable","title":"x","status":503,"detail":"x","instance":"urn:silo:request:1"}""", HttpStatusCode.ServiceUnavailable)
            : AdminMonitorKit.Json(path == "/api/v2/admin/sessions" ? """{"items":[],"page":{"has_more":false}}""" : """{"items":[]}"""));
        using var demand = h.Store.AcquireReads();
        await Until(() => h.HitCount("/api/v2/admin/downloads/preparations") == 1 && h.Store.Current.SessionsLoaded);
        await Task.Delay(30);
        Assert.Null(h.Store.Current.Preparations);
    }

    [Fact]
    public void FreshAuthorityDoesNotSeedScansFromRetiredTransportCache()
    {
        using var h = new Harness();
        const string scans = """[{"id":"old","library_id":1,"mode":"library","status":"running"}]""";
        h.Channel.Latest["scans"] = JsonDocument.Parse(scans).RootElement.Clone();
        h.Channel.Snapshot("scans", scans);
        Assert.Single(h.Store.Current.Scans);
        // EventChannelClient invalidates authority before clearing its cache.
        h.Kit.Client.InvalidateAccessContext();
        Assert.Empty(h.Store.Current.Scans);
        Assert.False(h.Store.Current.ScansLoaded);
        h.Channel.Snapshot("scans", "[]");
        Assert.True(h.Store.Current.ScansLoaded);
    }

    [Fact]
    public async Task InitialSessionsFailureSettlesAndExplicitRefreshRecovers()
    {
        using var h = new Harness();
        h.Override = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
        using var demand = h.Store.AcquireReads();
        await Until(() => h.Store.Current.SessionsError != null);
        Assert.True(h.Store.Current.SessionsLoaded);
        h.Override = null;
        await h.Store.RefreshSessionsAsync();
        Assert.True(h.Store.Current.SessionsLoaded);
        Assert.Null(h.Store.Current.SessionsError);
    }
}
