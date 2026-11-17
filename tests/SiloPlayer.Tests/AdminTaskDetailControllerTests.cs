using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Behaviour of one task's detail state (official AdminTaskDetail.tsx and AdminTaskDetail.v2.test.tsx). Synthetic data only.</summary>
public sealed class AdminTaskDetailControllerTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        public readonly AdminMonitorAuthority Authority;
        public readonly DashboardManualTime Time = new();
        public readonly AdminTasksFakeChannel Channel = new();
        public readonly AdminTaskDetailController Controller;
        public readonly ConcurrentDictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> Routes = new();
        public readonly ConcurrentQueue<AdminTaskNotice> Notices = new();
        public readonly ConcurrentQueue<(string? IfMatch, string Body)> Writes = new();
        public int ScheduleReads;
        public readonly string Key;

        public Fixture(string key = "fixture", bool manual = false)
        {
            Key = key;
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            var path = "/api/v2/admin/tasks/" + key;
            Routes["GET " + path] = (_, _) => Json(AdminTasksControllerTests.TaskJson(key, manual: manual));
            Routes["GET " + path + "/history"] = (_, _) => Json(AdminTasksApiTests.HistoryJson.Replace("\"fixture\"", "\"" + key + "\""));
            Routes["GET " + path + "/metrics"] = (_, _) => Json(AdminTasksApiTests.MetricsJson);
            Routes["GET " + path + "/triggers"] = (_, _) =>
            {
                var read = Interlocked.Increment(ref ScheduleReads);
                var response = AdminMonitorKit.Json($$"""{"task_key":"{{key}}","triggers":[{"type":"interval","interval_ms":{{(read == 1 ? 1000 : 60000)}}}]}""");
                response.Headers.ETag = EntityTagHeaderValue.Parse($"\"revision-{read}\"");
                return Task.FromResult(response);
            };
            Kit.Http.Respond = async (request, ct) =>
            {
                var route = $"{request.Method} {request.RequestUri!.AbsolutePath}";
                if (request.Method == HttpMethod.Put)
                    Writes.Enqueue((request.Headers.IfMatch.SingleOrDefault()?.ToString(), await request.Content!.ReadAsStringAsync(ct)));
                return Routes.TryGetValue(route, out var handler) ? await handler(request, ct) : AdminMonitorKit.Json("{}", HttpStatusCode.NotFound);
            };
            Controller = new AdminTaskDetailController(new AdminTasksApi(Kit.Client), Authority, key, Channel, Time);
            Controller.Notice += notice => Notices.Enqueue(notice);
        }

        public string Path => "/api/v2/admin/tasks/" + Key;
        public static Task<HttpResponseMessage> Json(string body, HttpStatusCode status = HttpStatusCode.OK) => Task.FromResult(AdminMonitorKit.Json(body, status));
        public static Task<HttpResponseMessage> Problem(HttpStatusCode status, string type) =>
            Json($$"""{"type":"https://siloserver.org/docs/api/v2/problems/{{type}}","title":"Problem","status":{{(int)status}}}""", status);
        public int Count(string request) => Kit.Http.Requests.Count(entry => entry.StartsWith(request, StringComparison.Ordinal));
        public void Dispose() { Controller.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private static async Task Until(Func<bool> condition, string label)
    {
        for (var attempt = 0; attempt < 400 && !condition(); attempt++) await Task.Delay(5);
        Assert.True(condition(), "Timed out: " + label);
    }

    private static async Task<Fixture> OpenedAsync(string key = "fixture", bool manual = false)
    {
        var fixture = new Fixture(key, manual);
        fixture.Controller.SetActive(true);
        Assert.True(fixture.Controller.Open());
        await Until(() => fixture.Controller.State.Task != null && fixture.Controller.State.HistoryLoaded, "initial load");
        return fixture;
    }

    private static async Task<Fixture> EditingAsync()
    {
        var fixture = await OpenedAsync();
        await fixture.Controller.BeginEditAsync(fixture.Controller.State.Owner);
        Assert.True(fixture.Controller.State.Editing);
        return fixture;
    }

    [Fact]
    public async Task FailedTaskRetryReportsLoadingUntilItsReadSettles()
    {
        using var fixture = new Fixture();
        fixture.Routes["GET " + fixture.Path] = (_, _) => Fixture.Problem(HttpStatusCode.ServiceUnavailable, "unavailable");
        Assert.True(fixture.Controller.Open());
        await Until(() => fixture.Controller.State.Failure != null && !fixture.Controller.State.Loading, "initial read failure");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes["GET " + fixture.Path] = async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return AdminMonitorKit.Json(AdminTasksControllerTests.TaskJson("fixture"));
        };
        try
        {
            var retry = fixture.Controller.RetryAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(fixture.Controller.State.Loading);
            Assert.NotNull(fixture.Controller.State.Failure);
            Assert.Null(fixture.Controller.State.Task);
            release.TrySetResult();
            await retry.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(fixture.Controller.State.Loading);
            Assert.Null(fixture.Controller.State.Failure);
            Assert.NotNull(fixture.Controller.State.Task);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task OpenReadsTheTaskAndTheFirstHistoryPage()
    {
        using var fixture = await OpenedAsync();
        var state = fixture.Controller.State;
        Assert.Equal("fixture", state.Task!.Key);
        Assert.Equal(["3", "2"], state.History.Select(item => item.Id));
        Assert.True(state.HistoryHasMore);
        Assert.Null(state.Metrics);
        Assert.Equal(0, fixture.Count("GET " + fixture.Path + "/metrics"));
    }

    [Fact]
    public async Task HiddenTasksOpenThroughDirectDetailRead()
    {
        using var fixture = await OpenedAsync("rebuild_catalog_search_index");
        Assert.Equal("Rebuild Catalog Search Index", AdminTasksText.TaskName(fixture.Controller.State.Task!));
        Assert.Equal(0, fixture.Count("GET /api/v2/admin/tasks?"));
    }

    [Fact]
    public async Task ARefetchsNotFoundReplacesACachedTask()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes["GET " + fixture.Path] = (_, _) => Fixture.Problem(HttpStatusCode.NotFound, "not_found");
        fixture.Channel.Updated("""{"key":"fixture","state":"running"}""");
        await Until(() => fixture.Controller.State.NotFound, "not found");
        Assert.Null(fixture.Controller.State.Task);
        Assert.Null(fixture.Controller.State.Failure);
    }

    [Fact]
    public async Task FirstReadFailuresOfferRetryButTransientRefreshFailuresKeepTheTask()
    {
        using var failing = new Fixture();
        failing.Routes["GET " + failing.Path] = (_, _) => Fixture.Json("{}", HttpStatusCode.ServiceUnavailable);
        failing.Controller.Open();
        await Until(() => failing.Controller.State.Failure != null, "first failure");
        Assert.False(failing.Controller.State.NotFound);
        Assert.Null(failing.Controller.State.Task);
        failing.Routes["GET " + failing.Path] = (_, _) => Fixture.Json(AdminTasksControllerTests.TaskJson("fixture"));
        await failing.Controller.RetryAsync();
        await Until(() => failing.Controller.State.Task != null && failing.Controller.State.Failure == null, "retry");

        using var fixture = await OpenedAsync();
        fixture.Routes["GET " + fixture.Path] = (_, _) => Fixture.Json("{}", HttpStatusCode.ServiceUnavailable);
        fixture.Channel.Updated("""{"key":"fixture","state":"running"}""");
        await Until(() => fixture.Controller.State.Failure != null && !fixture.Controller.State.Refreshing, "refresh failure");
        Assert.NotNull(fixture.Controller.State.Task);
        Assert.NotNull(fixture.Controller.State.Failure);
    }

    [Fact]
    public async Task FailedTerminalRefreshKeepsCancellingTaskAndRetryReadsWithoutReplayingCancel()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes["GET " + fixture.Path] = (_, _) => Fixture.Json(AdminTasksControllerTests.TaskJson("fixture", state: "running"));
        await fixture.Controller.RetryAsync();
        fixture.Routes["POST " + fixture.Path + "/cancel"] = (_, _) =>
        {
            fixture.Routes["GET " + fixture.Path] = (_, _) => Fixture.Json(AdminTasksControllerTests.TaskJson("fixture", state: "cancelling"));
            return Fixture.Json(AdminTasksControllerTests.TaskJson("fixture", state: "cancelling"));
        };
        await fixture.Controller.CancelAsync(fixture.Controller.State.Owner);
        await Until(() => fixture.Controller.State.Task?.State == "cancelling" && !fixture.Controller.State.Refreshing, "cancelling");
        var cached = fixture.Controller.State.Task;
        fixture.Routes["GET " + fixture.Path] = (_, _) => Fixture.Problem(HttpStatusCode.ServiceUnavailable, "unavailable");
        fixture.Channel.Updated("""{"key":"fixture","state":"idle"}""");
        await Until(() => fixture.Controller.State.Failure != null && !fixture.Controller.State.Refreshing, "terminal refresh failure");
        Assert.Same(cached, fixture.Controller.State.Task);
        Assert.False(fixture.Controller.State.NotFound);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes["GET " + fixture.Path] = async (_, ct) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            return AdminMonitorKit.Json(AdminTasksControllerTests.TaskJson("fixture", state: "idle"));
        };
        var reads = fixture.Kit.Http.Requests.Count(entry => entry == "GET " + fixture.Path);
        try
        {
            var retry = fixture.Controller.RetryAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(fixture.Controller.State.Refreshing);
            Assert.False(fixture.Controller.State.Loading);
            Assert.NotNull(fixture.Controller.State.Failure);
            Assert.Same(cached, fixture.Controller.State.Task);
            release.TrySetResult();
            await retry.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("idle", fixture.Controller.State.Task!.State);
            Assert.Null(fixture.Controller.State.Failure);
            Assert.False(fixture.Controller.State.Refreshing);
            Assert.Equal(reads + 1, fixture.Kit.Http.Requests.Count(entry => entry == "GET " + fixture.Path));
            Assert.Equal("POST " + fixture.Path + "/cancel", Assert.Single(fixture.Kit.Http.Requests.Where(entry => entry.StartsWith("POST ", StringComparison.Ordinal))));
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public void UnsafeKeysAreNotFoundWithoutARequest()
    {
        using var fixture = new Fixture("a b");
        fixture.Controller.Open();
        Assert.True(fixture.Controller.State.NotFound);
        Assert.Empty(fixture.Kit.Http.Requests);
    }

    [Fact]
    public async Task EventsForOtherTasksAreIgnoredAndIdleEventsRestartHistory()
    {
        using var fixture = await OpenedAsync();
        var histories = fixture.Kit.Http.Requests.Count(entry => entry.Contains("/history"));
        var reads = fixture.Kit.Http.Requests.Count;
        fixture.Channel.Updated("""{"key":"other","state":"idle"}""");
        await Task.Delay(40);
        Assert.Equal(reads, fixture.Kit.Http.Requests.Count);
        fixture.Channel.Updated("""{"key":"fixture","state":"running"}""");
        await Until(() => fixture.Kit.Http.Requests.Count == reads + 1, "task refetch only");
        fixture.Channel.Updated("""{"key":"fixture","state":"idle"}""");
        await Until(() => fixture.Kit.Http.Requests.Count(entry => entry.Contains("/history")) == histories + 1, "history restarted");
        Assert.Equal("/api/v2/admin/tasks/fixture/history?limit=20", fixture.Kit.Http.Requests.Last(entry => entry.Contains("/history"))[4..]);
    }

    [Fact]
    public async Task HistoryContinuesByCursorAndRejectsARepeatedIdentity()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes["GET " + fixture.Path + "/history"] = (request, _) =>
        {
            Assert.Contains("cursor=cursor-2", request.RequestUri!.Query);
            return Fixture.Json("""{"items":[{"task_key":"fixture","started_at":"2026-01-01T00:00:00Z","completed_at":"2026-01-01T00:00:01Z","status":"completed","duration_ms":1000,"id":"1"}],"page":{"has_more":false}}""");
        };
        await fixture.Controller.LoadOlderAsync();
        Assert.Equal(["3", "2", "1"], fixture.Controller.State.History.Select(item => item.Id));
        Assert.False(fixture.Controller.State.HistoryHasMore);

        using var repeated = await OpenedAsync();
        repeated.Routes["GET " + repeated.Path + "/history"] = (_, _) =>
            Fixture.Json("""{"items":[{"task_key":"fixture","started_at":"2026-01-01T00:00:00Z","completed_at":"2026-01-01T00:00:01Z","status":"completed","duration_ms":1000,"id":"2"}],"page":{"has_more":false}}""");
        await repeated.Controller.LoadOlderAsync();
        Assert.True(repeated.Controller.State.HistoryFailed);
        Assert.Equal(["3", "2"], repeated.Controller.State.History.Select(item => item.Id));
        repeated.Routes["GET " + repeated.Path + "/history"] = (_, _) => Fixture.Json(AdminTasksApiTests.HistoryJson);
        await repeated.Controller.RestartHistoryAsync();
        await Until(() => !repeated.Controller.State.HistoryFailed && repeated.Controller.State.HistoryHasMore, "restart");
    }

    [Fact]
    public async Task RefreshMetadataShowsMetricsAndTicksOnlyWhileActive()
    {
        using var fixture = await OpenedAsync("refresh_metadata");
        await Until(() => fixture.Controller.State.Metrics != null, "metrics");
        var reads = fixture.Count("GET " + fixture.Path + "/metrics");
        fixture.Time.Advance(AdminTasksController.TickInterval);
        await Until(() => fixture.Count("GET " + fixture.Path + "/metrics") == reads + 1, "tick");
        fixture.Controller.SetActive(false);
        fixture.Time.Advance(AdminTasksController.TickInterval * 3);
        await Task.Delay(40);
        Assert.Equal(reads + 1, fixture.Count("GET " + fixture.Path + "/metrics"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InactiveEventsAndSnapshotsCatchUpOnceWithoutReplacingTheDraft(bool snapshot)
    {
        using var fixture = await OpenedAsync("refresh_metadata");
        await Until(() => fixture.Controller.State.Metrics != null, "metrics");
        await fixture.Controller.BeginEditAsync(fixture.Controller.State.Owner);
        fixture.Controller.UpdateDraft(draft => draft.Add());
        var draft = fixture.Controller.State.Draft;
        var owner = fixture.Controller.State.Owner;
        fixture.Controller.SetActive(false);
        var reads = fixture.Kit.Http.Requests.Count;
        var tasks = fixture.Kit.Http.Requests.Count(entry => entry == "GET " + fixture.Path);
        var histories = fixture.Count("GET " + fixture.Path + "/history");
        var metrics = fixture.Count("GET " + fixture.Path + "/metrics");
        var revision = fixture.Controller.State.Revision;
        for (var i = 0; i < 5; i++)
        {
            if (snapshot) fixture.Channel.Snapshot();
            else fixture.Channel.Updated("""{"key":"refresh_metadata","state":"idle"}""");
        }
        await Task.Delay(40);
        Assert.Equal(reads, fixture.Kit.Http.Requests.Count);
        Assert.Equal(revision, fixture.Controller.State.Revision);
        fixture.Controller.SetActive(true);
        await Until(() => fixture.Kit.Http.Requests.Count == reads + 3 && !fixture.Controller.State.HistoryLoading, "one catch-up per resource");
        await Task.Delay(40);
        Assert.Equal(tasks + 1, fixture.Kit.Http.Requests.Count(entry => entry == "GET " + fixture.Path));
        Assert.Equal(histories + 1, fixture.Count("GET " + fixture.Path + "/history"));
        Assert.Equal(metrics + 1, fixture.Count("GET " + fixture.Path + "/metrics"));
        Assert.Same(owner, fixture.Controller.State.Owner);
        Assert.Same(draft, fixture.Controller.State.Draft);
        Assert.True(fixture.Controller.State.Dirty);
        Assert.Equal(1, fixture.ScheduleReads);
    }

    [Fact]
    public async Task DeactivationDefersQueuedTaskHistoryAndMetricsTrailingReads()
    {
        using var fixture = await OpenedAsync("refresh_metadata");
        await Until(() => fixture.Controller.State.Metrics != null, "metrics");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes["GET " + fixture.Path] = async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return AdminMonitorKit.Json(AdminTasksControllerTests.TaskJson(fixture.Key));
        };
        fixture.Routes["GET " + fixture.Path + "/history"] = async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return AdminMonitorKit.Json(AdminTasksApiTests.HistoryJson.Replace("\"fixture\"", "\"refresh_metadata\""));
        };
        fixture.Routes["GET " + fixture.Path + "/metrics"] = async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return AdminMonitorKit.Json(AdminTasksApiTests.MetricsJson);
        };
        var reads = fixture.Kit.Http.Requests.Count;
        fixture.Channel.Snapshot();
        await Until(() => fixture.Kit.Http.Requests.Count == reads + 3, "reads started");
        fixture.Channel.Snapshot();
        fixture.Controller.SetActive(false);
        release.SetResult();
        await Until(() => !fixture.Controller.State.HistoryLoading, "history settled");
        await Task.Delay(40);
        Assert.Equal(reads + 3, fixture.Kit.Http.Requests.Count);
        fixture.Controller.SetActive(true);
        await Until(() => fixture.Kit.Http.Requests.Count == reads + 6 && !fixture.Controller.State.HistoryLoading, "deferred trailing reads");
        await Task.Delay(40);
        Assert.Equal(reads + 6, fixture.Kit.Http.Requests.Count);
    }

    [Fact]
    public async Task InactiveProgressEventsCatchUpOnlyTheMatchingTask()
    {
        using var fixture = await OpenedAsync("refresh_metadata");
        await Until(() => fixture.Controller.State.Metrics != null, "metrics");
        fixture.Controller.SetActive(false);
        var reads = fixture.Kit.Http.Requests.Count;
        fixture.Channel.Updated("""{"key":"other","state":"idle"}""");
        fixture.Controller.SetActive(true);
        Assert.Equal(reads, fixture.Kit.Http.Requests.Count);
        fixture.Controller.SetActive(false);
        for (var i = 0; i < 5; i++) fixture.Channel.Updated("""{"key":"refresh_metadata","state":"running"}""");
        Assert.Equal(reads, fixture.Kit.Http.Requests.Count);
        fixture.Controller.SetActive(true);
        await Until(() => fixture.Kit.Http.Requests.Count == reads + 1, "task-only catch-up");
        Assert.Equal("GET " + fixture.Path, fixture.Kit.Http.Requests.Last());
    }

    [Fact]
    public async Task EditingCapturesTheStrongValidatorAndDraft()
    {
        using var fixture = await EditingAsync();
        var state = fixture.Controller.State;
        Assert.Equal(1000, Assert.Single(state.Draft!.Rows).Trigger.IntervalMs);
        Assert.False(state.Dirty);
        Assert.DoesNotContain("revision", state.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWeakValidatorRefusesEditing()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes["GET " + fixture.Path + "/triggers"] = (_, _) =>
        {
            var response = AdminMonitorKit.Json("""{"task_key":"fixture","triggers":[]}""");
            response.Headers.ETag = EntityTagHeaderValue.Parse("W/\"weak\"");
            return Task.FromResult(response);
        };
        await fixture.Controller.BeginEditAsync(fixture.Controller.State.Owner);
        Assert.False(fixture.Controller.State.Editing);
        Assert.Equal("Schedule revision unavailable. Reload before editing.", fixture.Controller.State.EditorError);
    }

    [Fact]
    public async Task ManualOnlyTasksExposeNoEditor()
    {
        using var fixture = await OpenedAsync(manual: true);
        await fixture.Controller.BeginEditAsync(fixture.Controller.State.Owner);
        Assert.False(fixture.Controller.State.Editing);
        Assert.Equal(0, fixture.Count("GET " + fixture.Path + "/triggers"));
    }

    [Fact]
    public async Task A412KeepsTheGuardAndDraftUntilExplicitRevisionAdoption()
    {
        using var fixture = await EditingAsync();
        var row = fixture.Controller.State.Draft!.Rows[0].Id;
        fixture.Controller.UpdateDraft(draft => draft.SetInterval(row, "3", AdminTaskIntervalUnit.Seconds));
        fixture.Routes["PUT " + fixture.Path + "/triggers"] = (_, _) => Fixture.Problem(HttpStatusCode.PreconditionFailed, "precondition_failed");
        await fixture.Controller.SaveAsync(fixture.Controller.State.Owner);
        var state = fixture.Controller.State;
        Assert.True(state.NeedsReview && state.Editing && state.Dirty);
        Assert.Equal("The schedule changed. Your draft is kept. Review the current schedule before submitting a new edit.", state.EditorError);
        Assert.Equal("3", state.Draft!.Rows[0].IntervalText);
        Assert.Equal(("\"revision-1\"", """{"triggers":[{"type":"interval","interval_ms":3000}]}"""), Assert.Single(fixture.Writes));

        await fixture.Controller.SaveAsync(fixture.Controller.State.Owner);
        Assert.Single(fixture.Writes);
        await fixture.Controller.ReviewAsync(fixture.Controller.State.Owner);
        Assert.Equal(60000, Assert.Single(fixture.Controller.State.Candidate!).IntervalMs);
        fixture.Controller.AdoptRevision(fixture.Controller.State.Owner);
        Assert.False(fixture.Controller.State.NeedsReview);
        Assert.Equal("3", fixture.Controller.State.Draft!.Rows[0].IntervalText);
        Assert.Single(fixture.Writes);

        fixture.Routes["PUT " + fixture.Path + "/triggers"] = (_, _) =>
        {
            var response = AdminMonitorKit.Json("""{"task_key":"fixture","triggers":[{"type":"interval","interval_ms":3000}]}""");
            response.Headers.ETag = EntityTagHeaderValue.Parse("\"revision-3\"");
            return Task.FromResult(response);
        };
        await fixture.Controller.SaveAsync(fixture.Controller.State.Owner);
        Assert.Equal(("\"revision-2\"", """{"triggers":[{"type":"interval","interval_ms":3000}]}"""), fixture.Writes.Last());
        Assert.False(fixture.Controller.State.Editing);
        Assert.Contains(fixture.Notices, notice => notice.Message == "Schedule saved and applied on this server. Other servers load it on restart.");
    }

    [Fact]
    public async Task AnUnknownSaveOutcomeRequiresReviewAndIsNeverReplayed()
    {
        using var fixture = await EditingAsync();
        fixture.Routes["PUT " + fixture.Path + "/triggers"] = (_, _) => throw new HttpRequestException("synthetic transport failure");
        await fixture.Controller.SaveAsync(fixture.Controller.State.Owner);
        Assert.True(fixture.Controller.State.NeedsReview);
        Assert.Equal("The outcome is unknown. Check current task state before trying again.", fixture.Controller.State.EditorError);
        await fixture.Controller.SaveAsync(fixture.Controller.State.Owner);
        Assert.Single(fixture.Writes);
    }

    [Fact]
    public async Task InvalidDraftsCannotBeSaved()
    {
        using var fixture = await EditingAsync();
        var row = fixture.Controller.State.Draft!.Rows[0].Id;
        fixture.Controller.UpdateDraft(draft => draft.SetInterval(row, "", AdminTaskIntervalUnit.Minutes));
        Assert.False(fixture.Controller.State.CanSave);
        await fixture.Controller.SaveAsync(fixture.Controller.State.Owner);
        Assert.Empty(fixture.Writes);
    }

    [Fact]
    public async Task CancelEditDropsTheDraftAndValidator()
    {
        using var fixture = await EditingAsync();
        fixture.Controller.UpdateDraft(draft => draft.Add());
        Assert.True(fixture.Controller.State.Dirty);
        fixture.Controller.CancelEdit();
        Assert.False(fixture.Controller.State.Editing);
        Assert.Null(fixture.Controller.State.Draft);
        Assert.False(fixture.Controller.State.Dirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpposingCommandsAreIgnoredWhileARequestIsHeldDespiteARealtimeStateChange(bool run)
    {
        using var fixture = await OpenedAsync();
        if (!run)
        {
            fixture.Routes["GET " + fixture.Path] = (_, _) => Fixture.Json(AdminTasksControllerTests.TaskJson("fixture", state: "running"));
            await fixture.Controller.RetryAsync();
            Assert.True(fixture.Controller.State.Task!.IsActive);
        }
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = run ? "/run" : "/cancel";
        var opposite = run ? "/cancel" : "/run";
        fixture.Routes["POST " + fixture.Path + operation] = async (_, ct) =>
        {
            await release.Task.WaitAsync(ct);
            return AdminMonitorKit.Json(AdminTasksControllerTests.TaskJson("fixture", state: run ? "running" : "cancelling"));
        };
        var owner = fixture.Controller.State.Owner;
        var command = run ? fixture.Controller.RunAsync(owner) : fixture.Controller.CancelAsync(owner);
        try
        {
            await Until(() => fixture.Count("POST " + fixture.Path + operation) == 1, "held command");
            var state = run ? "running" : "idle";
            fixture.Routes["GET " + fixture.Path] = (_, _) => Fixture.Json(AdminTasksControllerTests.TaskJson("fixture", state: state));
            var revision = fixture.Controller.State.Revision;
            fixture.Channel.Updated($$"""{"key":"fixture","state":"{{state}}"}""");
            await Until(() => fixture.Controller.State.Revision > revision && fixture.Controller.State.Task?.State == state, "refresh during command");
            await (run ? fixture.Controller.CancelAsync(owner) : fixture.Controller.RunAsync(owner));
            Assert.Equal(0, fixture.Count("POST " + fixture.Path + opposite));
            Assert.True(run ? fixture.Controller.State.RunPending : fixture.Controller.State.CancelPending);
            release.TrySetResult();
            await command.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(fixture.Controller.State.RunPending);
            Assert.False(fixture.Controller.State.CancelPending);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task RunAndCancelAreSentOnceAndInvalidateTheTask()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes["POST " + fixture.Path + "/run"] = (_, _) => Fixture.Json(AdminTasksControllerTests.TaskJson("fixture", state: "running"));
        var reads = fixture.Kit.Http.Requests.Count(entry => entry == "GET " + fixture.Path);
        await fixture.Controller.RunAsync(fixture.Controller.State.Owner);
        await Until(() => fixture.Kit.Http.Requests.Count(entry => entry == "GET " + fixture.Path) == reads + 1, "invalidated");
        Assert.Equal("Task started on this server", Assert.Single(fixture.Notices).Message);
        fixture.Routes["POST " + fixture.Path + "/cancel"] = (_, _) => Fixture.Json("{}", HttpStatusCode.BadGateway);
        await fixture.Controller.CancelAsync(fixture.Controller.State.Owner);
        Assert.Equal(AdminTaskNoticeKind.OutcomeUnknown, fixture.Notices.Last().Kind);
        Assert.Equal(1, fixture.Count("POST " + fixture.Path + "/cancel"));
    }

    [Fact]
    public async Task SuspensionClearsTheDraftValidatorCursorAndStopsEverything()
    {
        using var fixture = await EditingAsync();
        fixture.Controller.UpdateDraft(draft => draft.Add());
        fixture.Controller.Suspend();
        var state = fixture.Controller.State;
        Assert.Null(state.Task);
        Assert.Null(state.Draft);
        Assert.Empty(state.History);
        Assert.False(state.Dirty);
        Assert.Equal(0, fixture.Channel.Leases);
        Assert.False(fixture.Controller.IsTicking);
        await fixture.Controller.LoadOlderAsync();
        Assert.Equal(0, fixture.Count("GET " + fixture.Path + "/history?limit=20&cursor"));
    }

    [Fact]
    public async Task AuthorityLossClearsTheDraftAndAsksToReopen()
    {
        using var fixture = await EditingAsync();
        var retired = 0;
        fixture.Controller.AuthorityRetired += () => Interlocked.Increment(ref retired);
        fixture.Kit.Auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "user" });
        await Until(() => Volatile.Read(ref retired) == 1, "retired");
        Assert.Null(fixture.Controller.State.Draft);
        Assert.Null(fixture.Controller.State.Task);
    }
}
