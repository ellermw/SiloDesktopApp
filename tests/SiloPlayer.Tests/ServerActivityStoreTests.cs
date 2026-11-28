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

        // The store subscribes, releases and reads the latest snapshot from whichever thread its authority or reads run on.
        private readonly object _gate = new();

        public IDisposable Subscribe(params string[] channels)
        {
            lock (_gate) foreach (var channel in channels) Refs[channel] = Refs.GetValueOrDefault(channel) + 1;
            return new Handle(() => { lock (_gate) foreach (var channel in channels) Refs[channel]--; });
        }

        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot)
        {
            lock (_gate) return Latest.TryGetValue(channel, out snapshot);
        }
        public void Snapshot(string channel, string json) => SnapshotReceived?.Invoke(channel, JsonDocument.Parse(json).RootElement.Clone());
        public void Event(string channel, string name, string json) => EventReceived?.Invoke(channel, name, JsonDocument.Parse(json).RootElement.Clone());
        public void State(WebSocketState state) { CurrentState = state; StateChanged?.Invoke(state); }
        public int Active { get { lock (_gate) return Refs.Values.Sum(); } }

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

        public bool FireNextBeforeReturning;

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var token = new Cancel();
            if (FireNextBeforeReturning)
            {
                FireNextBeforeReturning = false;
                UtcNow += delay;
                callback();
            }
            else lock (_timers) _timers.Add((UtcNow + delay, callback, token));
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

    /// <summary>
    /// The dashboard data store is confined to one thread in the app (it captures the UI context when it is built, posts feed
    /// and timer callbacks to it and resumes its reads on it). Tests that drive it from the real activity store have to give it
    /// that thread: the store publishes from thread-pool threads, and without a context the dashboard would run those callbacks
    /// inline and concurrently.
    /// </summary>
    private sealed class SerialContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public SerialContext()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
            }) { IsBackground = true, Name = "dashboard-test-ui" };
            _thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            try { _queue.Add((d, state)); }
            catch (InvalidOperationException) { /* completed: the owner is disposing and nothing is left to run */ }
        }

        /// <summary>Runs <paramref name="work"/> on the context's thread and returns what it returned.</summary>
        public T Call<T>(Func<T> work)
        {
            if (Thread.CurrentThread == _thread) return work();
            using var done = new ManualResetEventSlim();
            T? result = default;
            Exception? error = null;
            Post(_ =>
            {
                try { result = work(); }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            }, null);
            done.Wait();
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            return result!;
        }

        /// <summary>Starts <paramref name="work"/> on the context's thread; its continuations resume there.</summary>
        public Task CallAsync(Func<Task> work)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try { await work(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
            }, null);
            return completion.Task;
        }

        public void Dispose() => _queue.CompleteAdding();
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
    public async Task ConfirmedPausePublishesImmediatelyAndSurvivesAnOlderInFlightList()
    {
        using var h = new Harness();
        h.Sessions = """{"items":[{"session_id":"s","user_id":"7","media_file_id":"1","requested_media_file_id":"1","is_paused":false,"lifecycle_seq":1}],"page":{"has_more":false}}""";
        await h.Store.RefreshSessionsAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Override = _ => pending.Task;
        var read = h.Store.RefreshSessionsAsync();
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 2);
        h.Channel.Event("sessions", "session.paused", """{"session_id":"s","state":"paused","seq":3}""");
        Assert.True(Assert.Single(h.Store.Current.Sessions).IsPaused);
        h.Channel.Event("sessions", "session.resumed", """{"session_id":"s","state":"playing","seq":2}""");
        Assert.True(Assert.Single(h.Store.Current.Sessions).IsPaused);
        pending.SetResult(AdminMonitorKit.Json(h.Sessions));
        await read;
        var row = Assert.Single(h.Store.Current.Sessions);
        Assert.True(row.IsPaused);
        Assert.Equal(3L, row.LifecycleSeq);
        h.Channel.Event("sessions", "session.resumed", """{"session_id":"s","state":"playing","seq":4}""");
        Assert.False(Assert.Single(h.Store.Current.Sessions).IsPaused);
    }

    [Fact]
    public async Task ConfirmedPauseRetainsItsLifecycleSequenceAfterAnOlderInFlightList()
    {
        using var h = new Harness();
        h.Sessions = """{"items":[{"session_id":"s","user_id":"7","media_file_id":"1","requested_media_file_id":"1","is_paused":false,"lifecycle_seq":1}],"page":{"has_more":false}}""";
        await h.Store.RefreshSessionsAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Override = _ => pending.Task;
        var read = h.Store.RefreshSessionsAsync();
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 2);
        h.Channel.Event("sessions", "session.paused", """{"session_id":"s","state":"paused","seq":3}""");
        Assert.True(Assert.Single(h.Store.Current.Sessions).IsPaused);
        h.Channel.Event("sessions", "session.resumed", """{"session_id":"s","state":"playing","seq":2}""");
        Assert.True(Assert.Single(h.Store.Current.Sessions).IsPaused);
        pending.SetResult(AdminMonitorKit.Json(h.Sessions));
        await read;
        var row = Assert.Single(h.Store.Current.Sessions);
        Assert.True(row.IsPaused);
        Assert.Equal(3L, row.LifecycleSeq);
        h.Channel.Event("sessions", "session.resumed", """{"session_id":"s","state":"playing","seq":4}""");
        Assert.False(Assert.Single(h.Store.Current.Sessions).IsPaused);
    }

    [Fact]
    public async Task ConfirmedPauseIgnoresAnOlderResumeAndAcceptsANewerResume()
    {
        using var h = new Harness();
        h.Sessions = """{"items":[{"session_id":"s","user_id":"7","media_file_id":"1","requested_media_file_id":"1","is_paused":false,"lifecycle_seq":1}],"page":{"has_more":false}}""";
        await h.Store.RefreshSessionsAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Override = _ => pending.Task;
        var read = h.Store.RefreshSessionsAsync();
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 2);
        h.Channel.Event("sessions", "session.paused", """{"session_id":"s","state":"paused","seq":3}""");
        Assert.True(Assert.Single(h.Store.Current.Sessions).IsPaused);
        h.Channel.Event("sessions", "session.resumed", """{"session_id":"s","state":"playing","seq":2}""");
        Assert.True(Assert.Single(h.Store.Current.Sessions).IsPaused);
        pending.SetResult(AdminMonitorKit.Json(h.Sessions));
        await read;
        var row = Assert.Single(h.Store.Current.Sessions);
        Assert.True(row.IsPaused);
        Assert.Equal(3L, row.LifecycleSeq);
        h.Channel.Event("sessions", "session.resumed", """{"session_id":"s","state":"playing","seq":4}""");
        Assert.False(Assert.Single(h.Store.Current.Sessions).IsPaused);
    }

    [Fact]
    public async Task ConfirmedPauseSurvivesAnInFlightListWithAnIntermediateLifecycleSequence()
    {
        using var h = new Harness();
        h.Sessions = """{"items":[{"session_id":"s","user_id":"7","media_file_id":"1","requested_media_file_id":"1","is_paused":false,"lifecycle_seq":1}],"page":{"has_more":false}}""";
        await h.Store.RefreshSessionsAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Override = _ => pending.Task;
        var read = h.Store.RefreshSessionsAsync();
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 2);
        h.Channel.Event("sessions", "session.paused", """{"session_id":"s","state":"paused","seq":3}""");
        Assert.True(Assert.Single(h.Store.Current.Sessions).IsPaused);
        h.Channel.Event("sessions", "session.resumed", """{"session_id":"s","state":"playing","seq":2}""");
        Assert.True(Assert.Single(h.Store.Current.Sessions).IsPaused);
        pending.SetResult(AdminMonitorKit.Json("""{"items":[{"session_id":"s","user_id":"7","media_file_id":"1","requested_media_file_id":"1","is_paused":false,"lifecycle_seq":2}],"page":{"has_more":false}}"""));
        await read;
        var row = Assert.Single(h.Store.Current.Sessions);
        Assert.True(row.IsPaused);
        Assert.Equal(3L, row.LifecycleSeq);
        h.Channel.Event("sessions", "session.resumed", """{"session_id":"s","state":"playing","seq":4}""");
        Assert.False(Assert.Single(h.Store.Current.Sessions).IsPaused);
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
        var gated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Override = path =>
        {
            if (path != "/api/v2/admin/sessions") return Task.FromResult(AdminMonitorKit.Json("""{"items":[]}"""));
            if (Interlocked.Increment(ref sessionCalls) != 1) return Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}"""));
            gated.SetResult(); // the read made under the authority that is about to retire now holds the gate
            return gate.Task;
        };
        using var demand = h.Store.AcquireReads();
        // Not "the first request was counted": the harness counts a hit before the handler decides which call gets the gate, and on a
        // starved machine the second read could win that decision. It would then be the current authority's read, and applying it is right.
        await gated.Task.WaitAsync(TimeSpan.FromSeconds(10));

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
    public void ReadingDashboardFeedBeforeItsStoreCallbackDoesNotSwallowScanChanges()
    {
        using var h = new Harness();
        ServerActivityDashboardFeed? adapter = null;
        DashboardActivityFeedSnapshot? readAhead = null;
        // This subscriber runs first, in the gap between snapshot capture and the adapter's callback.
        h.Store.Changed += _ => readAhead = adapter!.Current;
        using var feed = new ServerActivityDashboardFeed(h.Store);
        adapter = feed;
        var notified = new List<DashboardActivityFeedSnapshot>();
        feed.Changed += () => notified.Add(feed.Current);

        const string scans = """[{"id":"a","library_id":1,"mode":"library","status":"running"}]""";
        h.Channel.Snapshot("scans", scans);
        var active = Assert.Single(notified);
        Assert.Same(readAhead, active);
        Assert.Equal("a", Assert.Single(active.Scans).Id);

        h.Channel.Snapshot("scans", scans);
        h.Channel.State(WebSocketState.Closed);
        Assert.Single(notified);

        h.Channel.Event("scans", "scan.completed", """{"id":"a","library_id":1,"mode":"library","status":"completed"}""");
        Assert.Equal(2, notified.Count);
        Assert.Same(readAhead, notified[1]);
        Assert.Empty(notified[1].Scans);
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
    public async Task TrailingCallbackBeforeScheduleReturnsDoesNotBlockLaterBursts()
    {
        using var h = new Harness();
        using var demand = h.Store.AcquireReads();
        await Until(() => h.Store.Current.SessionsLoaded && h.Store.Current.Preparations != null);

        h.Channel.Event("sessions", "sessions.replaced", "{}");
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 2);
        h.Scheduler.Advance(TimeSpan.FromMilliseconds(4999));
        h.Scheduler.FireNextBeforeReturning = true;
        h.Channel.Event("sessions", "sessions.replaced", "{}");
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 3);

        // The just-fired handle must not remain installed as the trailing timer.
        h.Channel.Event("sessions", "sessions.replaced", "{}");
        Assert.Equal(1, h.Scheduler.Pending);
        h.Scheduler.Advance(ServerActivityStore.EventCoalescing);
        await Until(() => h.HitCount("/api/v2/admin/sessions") == 4);
        Assert.Equal(0, h.Scheduler.Pending);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DashboardSessionsKeepSharedReadErrorsAndAnyEarlierData(bool previouslyLoaded)
    {
        using var h = new Harness();
        if (previouslyLoaded)
            h.Sessions = """{"items":[{"session_id":"s","user_id":"7","media_file_id":"1","requested_media_file_id":"1"}],"page":{"has_more":false}}""";
        else
            h.Override = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
        using var feed = new ServerActivityDashboardFeed(h.Store);
        using var lease = h.Authority.Capture();
        using var ui = new SerialContext();
        // Built, started, read and disposed on the one thread the store is confined to (see SerialContext).
        var dashboard = ui.Call(() => new DashboardDataStore(new AdminMonitorApi(h.Kit.Client), lease, feed, new DashboardManualTime()));
        try
        {
            ui.Call(() => { dashboard.Start(); return 0; });
            await h.Store.RefreshAllAsync();
            await Until(() => h.Store.Current.SessionsLoaded && ui.Call(() =>
                previouslyLoaded ? dashboard.Get(DashboardDataStore.Sessions).Data != null : dashboard.Get(DashboardDataStore.Sessions).Error != null));
            var (data, updatedAt, loadedSessions) = ui.Call(() =>
            {
                var read = dashboard.Get(DashboardDataStore.Sessions);
                return (read.Data, read.UpdatedAt, read.As<IReadOnlyList<SiloPlayer.Core.Models.Admin.DashboardSession>>());
            });
            if (previouslyLoaded)
                Assert.Single(loadedSessions!);

            h.Override = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
            await ui.CallAsync(() => dashboard.FetchAsync(DashboardDataStore.Sessions, force: true));

            var state = ui.Call(() => dashboard.Get(DashboardDataStore.Sessions));
            Assert.NotNull(h.Store.Current.SessionsError);
            var failed = ui.Call(() => (state.Error, state.Data, state.UpdatedAt));
            Assert.NotNull(failed.Error);
            Assert.Same(data, failed.Data);
            Assert.Equal(updatedAt, failed.UpdatedAt);
            if (!previouslyLoaded)
            {
                Assert.Null(failed.Data);
                Assert.Null(failed.UpdatedAt);
            }

            h.Override = null;
            await ui.CallAsync(() => dashboard.FetchAsync(DashboardDataStore.Sessions, force: true));
            var recovered = ui.Call(() => (state.Error, state.Data, state.UpdatedAt));
            Assert.Null(recovered.Error);
            Assert.NotNull(recovered.Data);
            Assert.NotNull(recovered.UpdatedAt);
        }
        finally
        {
            ui.Call(() => { dashboard.Dispose(); return 0; });
        }
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
