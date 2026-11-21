using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Synthetic tasks channel for the Scheduled Tasks controllers.</summary>
internal sealed class AdminTasksFakeChannel : IAdminRealtimeChannel
{
    public int Leases;
    public WebSocketState CurrentState { get; set; } = WebSocketState.Open;
    public event Action<string, JsonElement>? SnapshotReceived;
    public event Action<string, string, JsonElement>? EventReceived;
    public event Action<WebSocketState>? StateChanged { add { } remove { } }

    public IDisposable Subscribe(params string[] channels)
    {
        Assert.Equal(["tasks"], channels);
        Interlocked.Increment(ref Leases);
        return new Release(() => Interlocked.Decrement(ref Leases));
    }

    public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) { snapshot = default; return false; }
    public void Snapshot() => SnapshotReceived?.Invoke("tasks", JsonDocument.Parse("[]").RootElement.Clone());
    public void Updated(string json) => EventReceived?.Invoke("tasks", "task.updated", JsonDocument.Parse(json).RootElement.Clone());
    public void Other(string channel, string name) => EventReceived?.Invoke(channel, name, JsonDocument.Parse("{}").RootElement.Clone());

    private sealed class Release(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

/// <summary>Behaviour of the Scheduled Tasks list state (official AdminTasks.tsx and tasks.ts hooks). Synthetic data only.</summary>
public sealed class AdminTasksControllerTests
{
    internal static string TaskJson(string key, string category = "system", string state = "idle", bool manual = false, string extra = "") =>
        $$"""{"key":"{{key}}","name":"{{key}} name","description":"","category":"{{category}}","state":"{{state}}","progress":0,"manual_only":{{(manual ? "true" : "false")}},"triggers":[]{{extra}},"execution_scope":"process"}""";

    // Timers and elapsed time advance together; wall time can be adjusted independently.
    private sealed class TaskManualTime : TimeProvider
    {
        private readonly DashboardManualTime _timers = new();
        private long _timestamp;
        public TimeSpan UtcOffset { get; set; }
        public override DateTimeOffset GetUtcNow() => _timers.GetUtcNow() + UtcOffset;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            _timers.CreateTimer(callback, state, dueTime, period);
        public void Advance(TimeSpan by)
        {
            _timestamp += by.Ticks;
            _timers.Advance(by);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        public readonly AdminMonitorAuthority Authority;
        public readonly TaskManualTime Time = new();
        public readonly AdminTasksFakeChannel Channel = new();
        public readonly AdminTasksController Controller;
        public readonly ConcurrentDictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> Routes = new();
        public readonly ConcurrentQueue<AdminTaskNotice> Notices = new();
        public string List = "{\"items\":[" + TaskJson("scan_libraries", "library") + "," + TaskJson("refresh_metadata", "metadata") + "," +
            TaskJson("cleanup", "system") + "," + TaskJson("repair", "system", manual: true) + "," + TaskJson("odd", "unknown") + "]}";

        public Fixture()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Routes["GET /api/v2/admin/tasks"] = (_, _) => Json(List);
            Routes["GET /api/v2/admin/tasks/refresh_metadata/metrics"] = (_, _) => Json(AdminTasksApiTests.MetricsJson);
            Kit.Http.Respond = async (request, ct) =>
            {
                var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
                return Routes.TryGetValue(key, out var route) ? await route(request, ct) : AdminMonitorKit.Json("{}", HttpStatusCode.NotFound);
            };
            Controller = new AdminTasksController(new AdminTasksApi(Kit.Client), Authority, Channel, Time);
            Controller.Notice += notice => Notices.Enqueue(notice);
        }

        public static Task<HttpResponseMessage> Json(string body, HttpStatusCode status = HttpStatusCode.OK) => Task.FromResult(AdminMonitorKit.Json(body, status));
        public int Count(string request) => Kit.Http.Requests.Count(entry => entry == request);
        public void Dispose() { Controller.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private const string ListRead = "GET /api/v2/admin/tasks";
    private const string MetricsRead = "GET /api/v2/admin/tasks/refresh_metadata/metrics";

    private static async Task Until(Func<bool> condition, string label)
    {
        for (var attempt = 0; attempt < 400 && !condition(); attempt++) await Task.Delay(5);
        Assert.True(condition(), "Timed out: " + label);
    }

    private static async Task<Fixture> OpenedAsync(bool active = true)
    {
        var fixture = new Fixture();
        fixture.Controller.SetActive(active);
        Assert.True(fixture.Controller.Open());
        await Until(() => fixture.Controller.State.Loaded && fixture.Controller.State.Metrics != null, "initial load");
        return fixture;
    }

    [Fact]
    public async Task GroupsFollowTheOfficialOrderWithManualTasksLast()
    {
        using var fixture = await OpenedAsync();
        var groups = fixture.Controller.State.Groups;
        Assert.Equal(["library", "metadata", "system", "on-demand"], groups.Select(group => group.Id));
        Assert.Equal(["Library", "Metadata", "System", "On demand"], groups.Select(group => group.Label));
        Assert.Equal("These never run on a schedule. Use them for repairs and one-off maintenance.", groups[3].Description);
        Assert.Equal(["repair"], groups[3].Tasks.Select(task => task.Key));
        Assert.DoesNotContain(groups.SelectMany(group => group.Tasks), task => task.Key == "odd");
        Assert.Equal(1, fixture.Channel.Leases);
    }

    [Fact]
    public void EmptyGroupsAreOmitted() =>
        Assert.Equal(["system"], AdminTasksController.Group([new("only", "Only", "", "system", "idle", 0, false, null, [], null, null)]).Select(group => group.Id));

    [Fact]
    public async Task MetricsAreReadOnlyWhenTheListHasRefreshMetadata()
    {
        using var fixture = new Fixture { List = "{\"items\":[" + TaskJson("cleanup") + "]}" };
        fixture.Controller.SetActive(true);
        fixture.Controller.Open();
        await Until(() => fixture.Controller.State.Loaded, "load");
        await Task.Delay(30);
        Assert.Equal(0, fixture.Count(MetricsRead));
        Assert.Null(fixture.Controller.State.Metrics);
    }

    [Fact]
    public async Task TaskEventsInvalidateTheListAndCoalesceBehindAnInFlightRead()
    {
        using var fixture = await OpenedAsync();
        var gate = new TaskCompletionSource();
        var reads = 0;
        fixture.Routes[ListRead] = async (_, ct) => { Interlocked.Increment(ref reads); await gate.Task.WaitAsync(ct); return AdminMonitorKit.Json(fixture.List); };
        fixture.Channel.Updated("""{"key":"cleanup","state":"running"}""");
        await Until(() => Volatile.Read(ref reads) == 1, "first event read");
        for (var count = 0; count < 5; count++) fixture.Channel.Updated("""{"key":"cleanup","state":"running"}""");
        fixture.Channel.Other("jobs", "job.updated");
        gate.SetResult();
        await Until(() => Volatile.Read(ref reads) == 2, "one trailing read");
        await Task.Delay(40);
        Assert.Equal(2, Volatile.Read(ref reads));
    }

    [Fact]
    public async Task IdleRefreshMetadataEventsAlsoRefreshMetricsButOtherTasksDoNot()
    {
        using var fixture = await OpenedAsync();
        var before = fixture.Count(MetricsRead);
        fixture.Channel.Updated("""{"key":"cleanup","state":"idle"}""");
        fixture.Channel.Updated("""{"key":"refresh_metadata","state":"running"}""");
        await Task.Delay(40);
        Assert.Equal(before, fixture.Count(MetricsRead));
        fixture.Channel.Updated("""{"key":"refresh_metadata","state":"idle"}""");
        await Until(() => fixture.Count(MetricsRead) == before + 1, "metrics invalidated");
    }

    [Fact]
    public async Task SnapshotsRefetchEverything()
    {
        using var fixture = await OpenedAsync();
        var (lists, metrics) = (fixture.Count(ListRead), fixture.Count(MetricsRead));
        fixture.Channel.Snapshot();
        await Until(() => fixture.Count(ListRead) == lists + 1 && fixture.Count(MetricsRead) == metrics + 1, "snapshot catch-up");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InactiveEventsAndSnapshotsCoalesceUntilReactivation(bool snapshot)
    {
        using var fixture = await OpenedAsync();
        fixture.Controller.SetActive(false);
        var (lists, metrics) = (fixture.Count(ListRead), fixture.Count(MetricsRead));
        var revision = fixture.Controller.State.Revision;
        for (var i = 0; i < 5; i++)
        {
            if (snapshot) fixture.Channel.Snapshot();
            else fixture.Channel.Updated("""{"key":"refresh_metadata","state":"idle"}""");
        }
        await Task.Delay(40);
        Assert.Equal(lists, fixture.Count(ListRead));
        Assert.Equal(metrics, fixture.Count(MetricsRead));
        Assert.Equal(revision, fixture.Controller.State.Revision);
        var owner = fixture.Controller.State.Owner;
        fixture.Controller.SetActive(true);
        await Until(() => fixture.Count(ListRead) == lists + 1 && fixture.Count(MetricsRead) == metrics + 1, "one catch-up per resource");
        await Task.Delay(40);
        Assert.Equal(lists + 1, fixture.Count(ListRead));
        Assert.Equal(metrics + 1, fixture.Count(MetricsRead));
        Assert.Same(owner, fixture.Controller.State.Owner);
    }

    [Fact]
    public async Task DeactivationDefersAlreadyQueuedListAndMetricsTrailingReads()
    {
        using var fixture = await OpenedAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes[ListRead] = async (_, ct) => { await release.Task.WaitAsync(ct); return AdminMonitorKit.Json(fixture.List); };
        fixture.Routes[MetricsRead] = async (_, ct) => { await release.Task.WaitAsync(ct); return AdminMonitorKit.Json(AdminTasksApiTests.MetricsJson); };
        var (lists, metrics) = (fixture.Count(ListRead), fixture.Count(MetricsRead));
        fixture.Channel.Snapshot();
        await Until(() => fixture.Count(ListRead) == lists + 1 && fixture.Count(MetricsRead) == metrics + 1, "reads started");
        fixture.Channel.Snapshot();
        fixture.Controller.SetActive(false);
        release.SetResult();
        await Until(() => !fixture.Controller.State.Refreshing, "list settled");
        await Task.Delay(40);
        Assert.Equal(lists + 1, fixture.Count(ListRead));
        Assert.Equal(metrics + 1, fixture.Count(MetricsRead));
        fixture.Controller.SetActive(true);
        await Until(() => fixture.Count(ListRead) == lists + 2 && fixture.Count(MetricsRead) == metrics + 2, "deferred trailing reads");
        await Task.Delay(40);
        Assert.Equal(lists + 2, fixture.Count(ListRead));
        Assert.Equal(metrics + 2, fixture.Count(MetricsRead));
    }

    [Fact]
    public async Task MetricsAndClockTickOnlyWhileActiveAndTheListIsNeverPolled()
    {
        using var fixture = await OpenedAsync();
        var (lists, metrics) = (fixture.Count(ListRead), fixture.Count(MetricsRead));
        var before = fixture.Controller.State.Now;
        fixture.Time.Advance(AdminTasksController.TickInterval);
        await Until(() => fixture.Count(MetricsRead) == metrics + 1, "metrics tick");
        Assert.Equal(before + AdminTasksController.TickInterval, fixture.Controller.State.Now);
        Assert.Equal(lists, fixture.Count(ListRead));
        fixture.Controller.SetActive(false);
        fixture.Time.Advance(AdminTasksController.TickInterval * 4);
        await Task.Delay(40);
        Assert.Equal(metrics + 1, fixture.Count(MetricsRead));
        Assert.Equal(lists, fixture.Count(ListRead));
        Assert.False(fixture.Controller.IsTicking);
    }

    [Fact]
    public async Task APeriodicTickNeverQueuesBehindAnInFlightMetricsRead()
    {
        using var fixture = await OpenedAsync();
        var gate = new TaskCompletionSource();
        var reads = 0;
        fixture.Routes[MetricsRead] = async (_, ct) => { Interlocked.Increment(ref reads); await gate.Task.WaitAsync(ct); return AdminMonitorKit.Json(AdminTasksApiTests.MetricsJson); };
        fixture.Time.Advance(AdminTasksController.TickInterval);
        await Until(() => Volatile.Read(ref reads) == 1, "tick read");
        fixture.Time.Advance(AdminTasksController.TickInterval);
        fixture.Time.Advance(AdminTasksController.TickInterval);
        gate.SetResult();
        await Task.Delay(40);
        Assert.Equal(1, Volatile.Read(ref reads));
    }

    [Fact]
    public async Task TransientFailuresKeepTheLastGoodListAndAFirstFailureCanBeRetried()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes[ListRead] = (_, _) => Fixture.Json("{}", HttpStatusCode.ServiceUnavailable);
        fixture.Channel.Updated("""{"key":"cleanup","state":"idle"}""");
        await Until(() => fixture.Controller.State.Failure != null, "refresh failure");
        Assert.Equal(5, fixture.Controller.State.Tasks.Count);
        Assert.True(fixture.Controller.State.Loaded);

        using var failing = new Fixture();
        failing.Routes[ListRead] = (_, _) => Fixture.Json("{}", HttpStatusCode.ServiceUnavailable);
        failing.Controller.Open();
        await Until(() => failing.Controller.State.Failure != null, "first failure");
        Assert.False(failing.Controller.State.Loaded);
        failing.Routes[ListRead] = (_, _) => Fixture.Json(failing.List);
        await failing.Controller.RetryAsync();
        await Until(() => failing.Controller.State.Loaded && failing.Controller.State.Failure == null, "retry");
    }

    [Fact]
    public async Task RunShowsStartingForAtLeastOneSecondThenReportsStarted()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes["POST /api/v2/admin/tasks/cleanup/run"] = (_, _) => Fixture.Json(TaskJson("cleanup", state: "running"));
        var run = fixture.Controller.RunAsync("cleanup", fixture.Controller.State.Owner);
        await Until(() => fixture.Notices.Any(notice => notice.Kind == AdminTaskNoticeKind.Started), "started notice");
        Assert.Contains("cleanup", fixture.Controller.State.Starting);
        Assert.Equal("Task started on this server", fixture.Notices.Single().Message);
        fixture.Time.Advance(AdminTasksController.RunFeedbackMinimum);
        await run;
        Assert.DoesNotContain("cleanup", fixture.Controller.State.Starting);
        Assert.Equal(1, fixture.Count("POST /api/v2/admin/tasks/cleanup/run"));
    }

    [Fact]
    public async Task RunFeedbackUsesMonotonicTimeWhenUtcMovesBackwardDuringTheRequest()
    {
        using var fixture = await OpenedAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes["POST /api/v2/admin/tasks/cleanup/run"] = async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return AdminMonitorKit.Json(TaskJson("cleanup", state: "running"));
        };
        var run = fixture.Controller.RunAsync("cleanup", fixture.Controller.State.Owner);
        try
        {
            await Until(() => fixture.Count("POST /api/v2/admin/tasks/cleanup/run") == 1, "held run");
            fixture.Time.Advance(TimeSpan.FromMilliseconds(400));
            fixture.Time.UtcOffset = TimeSpan.FromHours(-1);
            release.TrySetResult();
            await Until(() => fixture.Notices.Any(), "response settled and feedback hold scheduled");
            fixture.Time.Advance(TimeSpan.FromMilliseconds(599));
            Assert.Contains("cleanup", fixture.Controller.State.Starting);
            Assert.False(run.IsCompleted);
            fixture.Time.Advance(TimeSpan.FromMilliseconds(1));
            await run.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.DoesNotContain("cleanup", fixture.Controller.State.Starting);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpposingCommandsAreIgnoredWhileARequestIsHeldDespiteARealtimeStateChange(bool run)
    {
        using var fixture = new Fixture { List = "{\"items\":[" + TaskJson("cleanup", state: run ? "idle" : "running") + "]}" };
        fixture.Controller.SetActive(true);
        Assert.True(fixture.Controller.Open());
        await Until(() => fixture.Controller.State.Loaded, "initial task state");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = run ? "run" : "cancel";
        var opposite = run ? "cancel" : "run";
        fixture.Routes["POST /api/v2/admin/tasks/cleanup/" + operation] = async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return AdminMonitorKit.Json(TaskJson("cleanup", state: run ? "running" : "cancelling"));
        };
        var owner = fixture.Controller.State.Owner;
        var command = run ? fixture.Controller.RunAsync("cleanup", owner) : fixture.Controller.StopAsync("cleanup", owner);
        try
        {
            await Until(() => fixture.Count("POST /api/v2/admin/tasks/cleanup/" + operation) == 1, "held command");
            var state = run ? "running" : "idle";
            fixture.List = "{\"items\":[" + TaskJson("cleanup", state: state) + "]}";
            fixture.Channel.Updated($$"""{"key":"cleanup","state":"{{state}}"}""");
            await Until(() => fixture.Controller.State.Tasks.Count == 1 && fixture.Controller.State.Tasks[0].State == state, "realtime refresh during command");
            await (run ? fixture.Controller.StopAsync("cleanup", owner) : fixture.Controller.RunAsync("cleanup", owner)).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(0, fixture.Count("POST /api/v2/admin/tasks/cleanup/" + opposite));
            Assert.Contains("cleanup", run ? fixture.Controller.State.Starting : fixture.Controller.State.Stopping);
            release.TrySetResult();
            await Until(() => fixture.Notices.Any(), "command settled");
            if (run) fixture.Time.Advance(AdminTasksController.RunFeedbackMinimum);
            await command.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(fixture.Controller.State.Starting);
            Assert.Empty(fixture.Controller.State.Stopping);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task UnknownRunOutcomeReconcilesOnceAndNeverReissues()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes["POST /api/v2/admin/tasks/cleanup/run"] = (_, _) => Fixture.Json("{}", HttpStatusCode.GatewayTimeout);
        var lists = fixture.Count(ListRead);
        var run = fixture.Controller.RunAsync("cleanup", fixture.Controller.State.Owner);
        await Until(() => fixture.Notices.Any(), "unknown notice");
        fixture.Time.Advance(AdminTasksController.RunFeedbackMinimum);
        await run;
        await Until(() => fixture.Count(ListRead) == lists + 1, "reconcile read");
        Assert.Equal(AdminTaskNoticeKind.OutcomeUnknown, fixture.Notices.Single().Kind);
        Assert.Equal("The outcome is unknown. Check current task state before trying again.", fixture.Notices.Single().Message);
        Assert.Equal(1, fixture.Count("POST /api/v2/admin/tasks/cleanup/run"));
    }

    [Fact]
    public async Task StopRequestsCancellationOnceAndConflictsAreExplained()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes["POST /api/v2/admin/tasks/cleanup/cancel"] = (_, _) => Fixture.Json("{\"type\":\"https://siloserver.org/docs/api/v2/problems/conflict\",\"status\":409}", HttpStatusCode.Conflict);
        await fixture.Controller.StopAsync("cleanup", fixture.Controller.State.Owner);
        Assert.Equal("Task is not running on this process", Assert.Single(fixture.Notices).Message);
        Assert.Empty(fixture.Controller.State.Stopping);
        fixture.Routes["POST /api/v2/admin/tasks/cleanup/cancel"] = (_, _) => Fixture.Json(TaskJson("cleanup", state: "cancelling"));
        await fixture.Controller.StopAsync("cleanup", fixture.Controller.State.Owner);
        Assert.Equal("Cancellation requested on this server", fixture.Notices.Last().Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task StopRejectionReconcilesAfterAFailedTerminalRefresh(HttpStatusCode status)
    {
        using var fixture = new Fixture { List = "{\"items\":[" + TaskJson("cleanup", state: "running") + "]}" };
        fixture.Controller.SetActive(true);
        fixture.Controller.Open();
        await Until(() => fixture.Controller.State.Loaded, "running task loaded");
        fixture.Routes[ListRead] = (_, _) => Fixture.Json("{}", HttpStatusCode.ServiceUnavailable);
        fixture.Channel.Updated("""{"key":"cleanup","state":"idle"}""");
        await Until(() => fixture.Controller.State.Failure != null, "terminal refresh failed");
        Assert.True(Assert.Single(fixture.Controller.State.Tasks).IsActive);
        fixture.List = "{\"items\":[" + TaskJson("cleanup") + "]}";
        fixture.Routes[ListRead] = (_, _) => Fixture.Json(fixture.List);
        fixture.Routes["POST /api/v2/admin/tasks/cleanup/cancel"] = (_, _) => Fixture.Json("{}", status);
        var lists = fixture.Count(ListRead);
        await fixture.Controller.StopAsync("cleanup", fixture.Controller.State.Owner);
        await Until(() => fixture.Count(ListRead) == lists + 1 && fixture.Controller.State.Failure == null, "rejection reconciled");
        Assert.False(Assert.Single(fixture.Controller.State.Tasks).IsActive);
        Assert.Equal(AdminTaskNoticeKind.Failed, Assert.Single(fixture.Notices).Kind);
        Assert.Equal(1, fixture.Count("POST /api/v2/admin/tasks/cleanup/cancel"));
    }

    [Fact]
    public async Task FailedConflictReconciliationCanBeRetriedWithoutReplayingStop()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes[ListRead] = (_, _) => Fixture.Json("{}", HttpStatusCode.ServiceUnavailable);
        fixture.Routes["POST /api/v2/admin/tasks/cleanup/cancel"] = (_, _) => Fixture.Json("{}", HttpStatusCode.Conflict);
        var lists = fixture.Count(ListRead);
        await fixture.Controller.StopAsync("cleanup", fixture.Controller.State.Owner);
        await Until(() => fixture.Count(ListRead) == lists + 1 && fixture.Controller.State.Failure != null, "reconciliation failed");
        Assert.True(fixture.Controller.State.Loaded);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes[ListRead] = async (_, ct) => { await release.Task.WaitAsync(ct); return AdminMonitorKit.Json(fixture.List); };
        await fixture.Controller.RetryAsync();
        Assert.True(fixture.Controller.State.Refreshing);
        Assert.NotNull(fixture.Controller.State.Failure);
        release.SetResult();
        await Until(() => !fixture.Controller.State.Refreshing && fixture.Controller.State.Failure == null, "read-only retry");
        Assert.Equal(1, fixture.Count("POST /api/v2/admin/tasks/cleanup/cancel"));
    }

    [Fact]
    public async Task ACommandFromARetiredOwnerIsIgnored()
    {
        using var fixture = await OpenedAsync();
        var stale = fixture.Controller.State.Owner;
        fixture.Controller.Suspend();
        fixture.Controller.Resume();
        fixture.Controller.Open();
        await Until(() => fixture.Controller.State.Loaded, "reopen");
        await fixture.Controller.RunAsync("cleanup", stale);
        Assert.Equal(0, fixture.Count("POST /api/v2/admin/tasks/cleanup/run"));
    }

    [Fact]
    public async Task SuspensionClearsDataReleasesTheChannelStopsTimersAndDropsLateResults()
    {
        using var fixture = await OpenedAsync();
        var gate = new TaskCompletionSource();
        fixture.Routes[ListRead] = async (_, ct) => { await gate.Task.WaitAsync(CancellationToken.None); return AdminMonitorKit.Json(fixture.List); };
        fixture.Channel.Updated("""{"key":"cleanup","state":"idle"}""");
        await Until(() => fixture.Count(ListRead) == 2, "late read started");
        fixture.Controller.Suspend();
        Assert.Empty(fixture.Controller.State.Tasks);
        Assert.Null(fixture.Controller.State.Metrics);
        Assert.Equal(0, fixture.Channel.Leases);
        Assert.False(fixture.Controller.IsTicking);
        gate.SetResult();
        await Task.Delay(40);
        Assert.Empty(fixture.Controller.State.Tasks);
        fixture.Channel.Updated("""{"key":"cleanup","state":"idle"}""");
        await Task.Delay(30);
        Assert.Equal(2, fixture.Count(ListRead));
        Assert.False(fixture.Controller.Open());
    }

    [Fact]
    public async Task AuthorityLossClearsEverythingAndAsksThePageToReopen()
    {
        using var fixture = await OpenedAsync();
        var retired = 0;
        fixture.Controller.AuthorityRetired += () => Interlocked.Increment(ref retired);
        fixture.Kit.Auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "user" });
        await Until(() => Volatile.Read(ref retired) == 1, "retired");
        Assert.Empty(fixture.Controller.State.Tasks);
        Assert.Null(fixture.Controller.State.Owner);
        Assert.Equal(0, fixture.Channel.Leases);
    }
}
