using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Selective restore (official AdminBackupRestore.tsx at 92ee051) on a synthetic server and a manual clock.</summary>
public sealed class AdminBackupRestoreControllerTests
{
    private static readonly AdminBackupRestoreSourceRef Source = AdminBackupRestoreSourceRef.Backup(BackupJson.BackupId);
    private static readonly string RestorePath = "/api/v2/admin/backups/" + BackupJson.BackupId + "/restore";

    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminBackupsApi Api;
        internal readonly BackupScheduler Scheduler = new();
        internal readonly AdminBackupRestoreController Controller;
        internal bool Authenticated = true;
        internal string Jobs = "";
        internal string JobBody = BackupJson.Job();
        internal Func<HttpRequestMessage, HttpResponseMessage>? Start;
        internal Harness()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Api = new AdminBackupsApi(Kit.Client);
            Controller = new AdminBackupRestoreController(Api, Authority, Scheduler);
            Kit.Http.Respond = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                HttpResponseMessage response = path switch
                {
                    "/api/v2/admin/backups/capabilities" => BackupJson.Json(BackupJson.Capabilities()),
                    "/api/v2/admin/backups" => BackupJson.Json(BackupJson.List(BackupJson.Backup())),
                    "/api/v2/admin/jobs" => BackupJson.Json("{\"items\":[" + Jobs + "]}"),
                    "/api/v2/libraries" => BackupJson.Json("""{"items":[{"id":"4","name":"Movies"}]}"""),
                    "/api/v2/admin/jobs/job-1" => BackupJson.Json(JobBody),
                    "/api/v2/admin/jobs/job-1/cancel" => BackupJson.Json(BackupJson.Job(state: "canceling"), HttpStatusCode.Accepted),
                    _ when path == RestorePath => Start?.Invoke(request) ?? BackupJson.Json(BackupJson.Job(), HttpStatusCode.Accepted, "/api/v2/admin/jobs/job-1"),
                    _ when path.EndsWith("/restore-set", StringComparison.Ordinal) =>
                        Authenticated || request.RequestUri.Query.Contains("accept_unauthenticated=true")
                            ? BackupJson.Json(BackupJson.RestoreSet(BackupJson.Section("settings", BackupJson.Item("general")), Authenticated))
                            : BackupJson.Json(BackupJson.Problem("backup_unauthenticated", 409), HttpStatusCode.Conflict),
                    _ => BackupJson.Json("{}", HttpStatusCode.NotFound),
                };
                return Task.FromResult(response);
            };
        }
        internal string[] Requests => Kit.Http.Requests.ToArray();
        internal int Starts => Requests.Count(request => request == "POST " + RestorePath);
        internal async Task ChooseAndReview()
        {
            await Controller.OpenAsync(Source, null);
            Controller.Toggle(["settings:general"], true);
            Controller.Review();
            Assert.True(Controller.State.Reviewing);
        }
        public void Dispose() { Controller.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    [Fact]
    public async Task DepartingDuringRestoreKeepsTheUnknownOutcomeAcrossReplacement()
    {
        using var harness = new Harness();
        var original = harness.Kit.Http.Respond;
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Kit.Http.Respond = (request, token) => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == RestorePath
            ? answer.Task : original(request, token);
        await harness.ChooseAndReview();
        var pending = harness.Controller.StartAsync();
        await Eventually.True(() => harness.Starts == 1, "restore on wire");
        harness.Controller.Dispose();
        using var replacement = new AdminBackupRestoreController(harness.Api, harness.Authority, harness.Scheduler);
        try
        {
            await replacement.OpenAsync(Source, null);
            Assert.True(replacement.State.OutcomeUnknown);
            await replacement.ReloadPreviewAsync();
            Assert.True(replacement.State.OutcomeUnknown);
            replacement.Toggle(["settings:general"], true);
            replacement.Review();
            await replacement.StartAsync();
            Assert.Equal(1, harness.Starts);
            answer.TrySetResult(BackupJson.Json(BackupJson.Job(), HttpStatusCode.Accepted, "/api/v2/admin/jobs/job-1"));
            await pending;
            Assert.True(replacement.State.OutcomeUnknown);
            await replacement.ReloadPreviewAsync();
            Assert.False(replacement.State.OutcomeUnknown);
        }
        finally
        {
            answer.TrySetResult(BackupJson.Json(BackupJson.Job(), HttpStatusCode.Accepted, "/api/v2/admin/jobs/job-1"));
            await pending;
        }
    }

    [Fact]
    public async Task AConflictDoesNotOfferToOpenAnotherSourcesRestore()
    {
        using var harness = new Harness
        {
            Start = _ => BackupJson.Json(BackupJson.Problem("conflict", 409), HttpStatusCode.Conflict, "/api/v2/admin/jobs/job-1"),
            JobBody = BackupJson.Job(source: "{\"upload_id\":\"" + BackupJson.UploadId + "\"}"),
        };
        await harness.ChooseAndReview();
        await harness.Controller.StartAsync();
        Assert.Null(harness.Controller.State.Refusal!.RunningJobId);
        Assert.Null(harness.Controller.State.JobId);
        Assert.NotNull(harness.Controller.State.Preview);
    }

    [Fact]
    public async Task AnUnverifiedBackupOpensOnlyAfterConsentAndSendsItOnce()
    {
        using var h = new Harness { Authenticated = false };
        await h.Controller.OpenAsync(Source, null);
        Assert.True(h.Controller.State.Unauthenticated);
        Assert.Null(h.Controller.State.Preview);
        await h.Controller.OpenUnverifiedAsync();
        Assert.NotNull(h.Controller.State.Preview);
        Assert.Single(h.Requests, request => request.EndsWith("restore-set?accept_unauthenticated=true", StringComparison.Ordinal));
        h.Controller.Toggle(["settings:general"], true);
        h.Controller.Review();
        h.Controller.SetAcceptUnauthenticated(false);
        Assert.False(AdminBackupRestoreController.CanRestore(h.Controller.State, h.Controller.Selection));
        h.Controller.SetAcceptUnauthenticated(true);
        await h.Controller.StartAsync();
        Assert.Contains("\"accept_unauthenticated\":true", h.Kit.Http.Bodies.Last());
        Assert.Equal(1, h.Starts);
    }

    [Fact]
    public async Task TheReviewedDigestAndSelectionAreSentOnceAndTheJobIsFollowed()
    {
        using var h = new Harness();
        string? job = null;
        h.Controller.JobChanged += id => job = id;
        await h.ChooseAndReview();
        await h.Controller.StartAsync();
        Assert.Equal(1, h.Starts);
        var body = h.Kit.Http.Bodies.ElementAt(Array.IndexOf(h.Requests, "POST " + RestorePath));
        Assert.Contains("\"digest\":\"" + BackupJson.Digest + "\"", body);
        Assert.Contains("\"settings\":[\"general\"]", body);
        Assert.DoesNotContain("accept_unauthenticated", body);
        Assert.Equal("job-1", job);
        Assert.Equal(2, h.Controller.State.Step);
        await h.Controller.StartAsync(); // no second POST once a job exists
        Assert.Equal(1, h.Starts);
    }

    [Fact]
    public async Task AChangedRestoreSetAsksForAFreshPreviewAndReview()
    {
        using var h = new Harness { Start = _ => BackupJson.Json(BackupJson.Problem("restore_set_changed", 409), HttpStatusCode.Conflict) };
        await h.ChooseAndReview();
        await h.Controller.StartAsync();
        var refusal = h.Controller.State.Refusal!;
        Assert.True(refusal.PreviewChanged);
        Assert.Null(refusal.RunningJobId);
        await h.Controller.ReloadPreviewAsync();
        Assert.False(h.Controller.State.Reviewing);
        Assert.Null(h.Controller.State.Refusal);
        Assert.Equal(2, h.Requests.Count(request => request.EndsWith("/restore-set", StringComparison.Ordinal)));
        Assert.Equal(1, h.Starts);
    }

    [Fact]
    public async Task ReloadingAnUncertainRestoreCannotReviewTheOldPreviewDuringItsRead()
    {
        using var harness = new Harness { Start = _ => throw new HttpRequestException("synthetic") };
        await harness.ChooseAndReview();
        await harness.Controller.StartAsync();
        Assert.True(harness.Controller.State.OutcomeUnknown);
        var original = harness.Kit.Http.Respond!;
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Kit.Http.Respond = (request, token) => request.RequestUri!.AbsolutePath.EndsWith("/restore-set", StringComparison.Ordinal)
            ? answer.Task : original(request, token);
        var refresh = harness.Controller.ReloadPreviewAsync();
        try
        {
            Assert.True(harness.Controller.State.PreviewLoading);
            harness.Controller.Review();
            Assert.False(harness.Controller.State.Reviewing);
            Assert.False(AdminBackupRestoreController.CanRestore(harness.Controller.State, harness.Controller.Selection));
        }
        finally
        {
            answer.TrySetResult(BackupJson.Json(BackupJson.RestoreSet(BackupJson.Section("settings", BackupJson.Item("general")))));
            await refresh;
        }
    }

    [Fact]
    public async Task AConflictOffersTheRunningJobFromItsLocation()
    {
        using var h = new Harness { Start = _ => BackupJson.Json(BackupJson.Problem("conflict", 409), HttpStatusCode.Conflict, "/api/v2/admin/jobs/job-7") };
        var original = h.Kit.Http.Respond!;
        h.Kit.Http.Respond = (request, token) => request.RequestUri!.AbsolutePath == "/api/v2/admin/jobs/job-7"
            ? Task.FromResult(BackupJson.Json(BackupJson.Job(id: "job-7"))) : original(request, token);
        await h.ChooseAndReview();
        await h.Controller.StartAsync();
        Assert.Equal("job-7", h.Controller.State.Refusal!.RunningJobId);
        Assert.False(h.Controller.State.Refusal.PreviewChanged);
    }

    [Fact]
    public async Task AnUnknownOutcomeThatNamedItsJobReconcilesThatJobWithoutAnotherPost()
    {
        using var h = new Harness { Start = _ => BackupJson.Json("{\"broken\":", HttpStatusCode.Accepted, "/api/v2/admin/jobs/job-1") };
        await h.ChooseAndReview();
        await h.Controller.StartAsync();
        Assert.Equal("job-1", h.Controller.State.JobId);
        h.Scheduler.Advance(TimeSpan.Zero);
        await Eventually.True(() => h.Controller.State.Job?.Id == "job-1", "named job read");
        Assert.Equal(1, h.Starts);
    }

    [Fact]
    public async Task AnUnknownOutcomeWithoutAJobStaysBlockedAndNeverAdoptsAnotherSourcesJob()
    {
        using var h = new Harness { Start = _ => throw new HttpRequestException("synthetic") };
        h.Jobs = BackupJson.Job(id: "job-other", source: "{\"backup_id\":\"" + BackupJson.OtherBackupId + "\"}");
        await h.ChooseAndReview();
        await h.Controller.StartAsync();
        var state = h.Controller.State;
        Assert.True(state.OutcomeUnknown);
        Assert.False(state.Reviewing); // Chooser exposes uncertainty, Check status and Reload preview directly.
        Assert.Equal(0, state.Step);
        Assert.Null(state.JobId);
        Assert.False(AdminBackupRestoreController.CanRestore(state, h.Controller.Selection));
        await h.Controller.StartAsync();
        Assert.Equal(1, h.Starts);
        h.Jobs = BackupJson.Job(id: "job-1");
        await h.Controller.ReconcileUnknownAsync();
        Assert.Equal("job-1", h.Controller.State.JobId);
        Assert.Equal(1, h.Starts);
    }

    [Fact]
    public async Task TheJobIsReadEveryTwoSecondsUntilTerminalAndStopOnlyTargetsTheDisplayedJob()
    {
        using var h = new Harness();
        var finished = 0;
        h.Controller.Finished += () => finished++;
        await h.Controller.OpenAsync(Source, "job-1");
        Assert.Equal("job-1", h.Controller.State.Job!.Id);
        Assert.Contains(AdminBackupRestoreController.JobInterval, h.Scheduler.Pending);
        await h.Controller.CancelAsync("job-2");
        Assert.DoesNotContain(h.Requests, request => request.EndsWith("/cancel", StringComparison.Ordinal));
        await h.Controller.CancelAsync("job-1");
        Assert.Single(h.Requests, request => request == "POST /api/v2/admin/jobs/job-1/cancel");
        h.JobBody = BackupJson.Job(state: "completed", terminal: true, cancelable: false, report: BackupJson.Report(extra: ",\"before_restore_backup_id\":\"" + BackupJson.OtherBackupId + "\""));
        // The read after Stop may still be in flight; advance the 2 s cadence until the terminal job is read.
        for (var tick = 0; tick < 100 && h.Controller.State.Job?.Terminal != true; tick++)
        {
            h.Scheduler.Advance(AdminBackupRestoreController.JobInterval);
            await Task.Delay(10);
        }
        Assert.True(h.Controller.State.Job?.Terminal == true, "terminal job");
        await Eventually.True(() => finished == 1, "finished raised once");
        var reads = h.Requests.Count(request => request == "GET /api/v2/admin/jobs/job-1");
        h.Scheduler.Advance(AdminBackupRestoreController.JobInterval);
        await Task.Delay(30);
        Assert.Equal(reads, h.Requests.Count(request => request == "GET /api/v2/admin/jobs/job-1"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TerminalAcknowledgementsRunCompletionSideEffectsExactlyOnce(bool fromCancel)
    {
        using var h = new Harness();
        var finished = 0;
        h.Controller.Finished += () => finished++;
        var terminal = BackupJson.Job(state: "completed", terminal: true, cancelable: false, report: BackupJson.Report());
        var original = h.Kit.Http.Respond;
        h.Kit.Http.Respond = (request, token) => request.RequestUri!.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal)
            ? Task.FromResult(BackupJson.Json(terminal, HttpStatusCode.Accepted)) : original(request, token);
        if (fromCancel) await h.Controller.OpenAsync(Source, "job-1");
        else await h.ChooseAndReview();
        var before = h.Requests.Count(request => request == "GET /api/v2/admin/backups");
        if (fromCancel) await h.Controller.CancelAsync("job-1");
        else
        {
            h.Start = _ => BackupJson.Json(terminal, HttpStatusCode.Accepted, "/api/v2/admin/jobs/job-1");
            await h.Controller.StartAsync();
        }
        await Eventually.True(() => finished == 1, "terminal acknowledgement raised Finished");
        Assert.True(h.Controller.State.Job!.Terminal);
        Assert.Equal(before + 1, h.Requests.Count(request => request == "GET /api/v2/admin/backups"));
        h.JobBody = terminal;
        await h.Controller.RetryJobAsync();
        Assert.Equal(1, finished);
        Assert.Equal(before + 1, h.Requests.Count(request => request == "GET /api/v2/admin/backups"));
    }

    [Theory]
    [InlineData(404)]
    [InlineData(503)]
    [InlineData(200)]
    public async Task FailedJobPollRetainsProgressAndRetryClearsTheError(int status)
    {
        using var h = new Harness();
        await h.Controller.OpenAsync(Source, "job-1");
        var retained = h.Controller.State.Job;
        var original = h.Kit.Http.Respond;
        h.Kit.Http.Respond = (request, token) => request.RequestUri!.AbsolutePath == "/api/v2/admin/jobs/job-1"
            ? Task.FromResult(BackupJson.Json("{}", (HttpStatusCode)status)) : original(request, token);
        await h.Controller.RetryJobAsync();
        Assert.Same(retained, h.Controller.State.Job);
        Assert.NotNull(h.Controller.State.JobError);
        h.Kit.Http.Respond = original;
        await h.Controller.RetryJobAsync();
        Assert.NotNull(h.Controller.State.Job);
        Assert.Null(h.Controller.State.JobError);
    }

    [Fact]
    public async Task RetiredJobCompletionCannotReleaseTheReplacementFlight()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync(Source, "job-1");
        var old = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var original = h.Kit.Http.Respond;
        h.Kit.Http.Respond = (request, token) => request.RequestUri!.AbsolutePath == "/api/v2/admin/jobs/job-1"
            ? Interlocked.Increment(ref reads) == 1 ? old.Task : replacement.Task : original(request, token);
        var retired = h.Controller.RetryJobAsync();
        var reopened = h.Controller.OpenAsync(Source, "job-1");
        try
        {
            await Eventually.True(() => reads == 2, "replacement job read started");
            old.SetResult(BackupJson.Json(h.JobBody));
            await retired;
            await h.Controller.RetryJobAsync();
            Assert.Equal(2, reads);
        }
        finally
        {
            old.TrySetResult(BackupJson.Json(h.JobBody));
            replacement.TrySetResult(BackupJson.Json(h.JobBody));
            await Task.WhenAll(retired, reopened);
        }
    }

    [Fact]
    public async Task ADelayedPollAcrossTerminalCancellationCannotStopTheNextRestore()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync(Source, "job-1");
        var old = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementReads = 0;
        var original = h.Kit.Http.Respond;
        h.Kit.Http.Respond = (request, token) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/admin/jobs/job-1" => old.Task,
            "/api/v2/admin/jobs/job-1/cancel" => Task.FromResult(BackupJson.Json(BackupJson.Job(state: "canceled", terminal: true, cancelable: false))),
            "/api/v2/admin/jobs/job-2" => ReadReplacement(),
            _ => original(request, token),
        };
        Task<HttpResponseMessage> ReadReplacement()
        {
            return Interlocked.Increment(ref replacementReads) == 1
                ? replacement.Task : Task.FromResult(BackupJson.Json(BackupJson.Job(id: "job-2")));
        }
        h.Start = _ => BackupJson.Json(BackupJson.Job(id: "job-2"), HttpStatusCode.Accepted, "/api/v2/admin/jobs/job-2");
        var retired = h.Controller.RetryJobAsync();
        try
        {
            await h.Controller.CancelAsync("job-1");
            await h.Controller.ChooseAgainAsync();
            h.Controller.Toggle(["settings:general"], true);
            h.Controller.Review();
            await h.Controller.StartAsync();
            h.Scheduler.Advance(AdminBackupRestoreController.JobInterval);
            await Eventually.True(() => replacementReads == 1, "replacement polls before the old read settles");
            old.SetResult(BackupJson.Json(BackupJson.Job()));
            await retired;
            await h.Controller.RetryJobAsync();
            Assert.Equal(1, replacementReads); // The old completion must not release the replacement flight.
            Assert.Equal("job-2", h.Controller.State.JobId);
            replacement.SetResult(BackupJson.Json(BackupJson.Job(id: "job-2")));
            await Eventually.True(() => replacementReads == 2 && h.Scheduler.Pending.Contains(AdminBackupRestoreController.JobInterval), "queued foreground read and polling survive");
            h.Scheduler.Advance(AdminBackupRestoreController.JobInterval);
            await Eventually.True(() => replacementReads == 3, "replacement polling continues");
        }
        finally
        {
            old.TrySetResult(BackupJson.Json(BackupJson.Job()));
            replacement.TrySetResult(BackupJson.Json(BackupJson.Job(id: "job-2")));
            await retired;
        }
    }

    [Fact]
    public async Task AJobForAnotherSourceIsNeverShown()
    {
        using var h = new Harness { JobBody = BackupJson.Job(source: "{\"upload_id\":\"" + BackupJson.UploadId + "\"}") };
        await h.Controller.OpenAsync(Source, "job-1");
        Assert.Null(h.Controller.State.Job);
        Assert.NotNull(h.Controller.State.JobError);
    }

    [Fact]
    public async Task ARunningRestoreOfThisSourceOpensInsteadOfTheChooser()
    {
        using var h = new Harness { Jobs = BackupJson.Job() };
        string? job = null;
        h.Controller.JobChanged += id => job = id;
        await h.Controller.OpenAsync(Source, null);
        await Eventually.True(() => h.Controller.State.JobId == "job-1", "running job adopted");
        Assert.Equal("job-1", job);
    }

    [Fact]
    public async Task RetirementDropsLateCallbacks()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync(Source, "job-1");
        h.Controller.Suspend();
        h.Scheduler.Advance(AdminBackupRestoreController.JobInterval);
        await Task.Delay(30);
        Assert.Null(h.Controller.State.Owner);
        Assert.Null(h.Controller.State.Job);
    }
}
