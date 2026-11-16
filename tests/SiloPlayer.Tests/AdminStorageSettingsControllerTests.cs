using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Configure › Storage page state (official StorageSettings.tsx at 8f93594c) on synthetic data.</summary>
public sealed class AdminStorageSettingsControllerTests
{
    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsStore Store;
        internal readonly DashboardManualTime Time = new();
        internal readonly AdminStorageSettingsController Controller;
        internal readonly ConcurrentQueue<AdminStorageNotice> Notices = new();
        internal readonly ConcurrentDictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> Routes = new();
        internal string Effective = """{"artwork.storage_backend":"","artwork.local_path":"/data/art","metadata.cache_images":"true","s3.public_url_auth":"","s3.public_access_key":"never-retained","s3.private_bucket":""}""";
        internal string Sensitive = """{"configured":["s3.public_access_key","s3.public_secret_key"],"managed_by_env":[]}""";
        internal string Status = """{"artwork_storage":{"backend":"local","locked":true,"status_known":true,"private_locked":false}}""";
        internal string Capabilities = """{"state":"available"}""";
        internal string Health = """{"current_backend":"local","reachable":true,"recovery_pending":false}""";
        internal string Jobs = """{"items":[]}""";
        internal bool Defer;

        internal Harness()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            var settings = new AdminServerSettingsApi(Kit.Client);
            Store = new AdminServerSettingsStore(settings, Authority, AdminStorageSettingsController.Keys, AdminStorageSettingsController.Secrets);
            Controller = new AdminStorageSettingsController(settings, Store, new AdminStorageApi(Kit.Client), Authority, Time, () => Defer);
            Controller.Notice += Notices.Enqueue;
            Kit.Http.Respond = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (Routes.TryGetValue($"{request.Method} {path}", out var route)) return route(request);
                if (request.Method != HttpMethod.Get) return Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError));
                HttpResponseMessage response = path switch
                {
                    "/api/v2/admin/settings/effective" => AdminMonitorKit.Json(Effective),
                    "/api/v2/admin/settings/sensitive-status" => Sensitive == "fail" ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : AdminMonitorKit.Json(Sensitive),
                    "/api/v2/admin/settings/restart-keys" => AdminMonitorKit.Json("""{"keys":["artwork.storage_backend"],"prefixes":["s3."]}"""),
                    "/api/v2/admin/server/status" => Status == "fail" ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : AdminMonitorKit.Json(Status),
                    "/api/v2/admin/storage-transitions/capabilities" => AdminMonitorKit.Json(Capabilities),
                    "/api/v2/admin/storage-transitions/source-health" => AdminMonitorKit.Json(Health),
                    "/api/v2/admin/jobs" => AdminMonitorKit.Json(Jobs),
                    _ => AdminMonitorKit.Json("{}", HttpStatusCode.NotFound),
                };
                response.Headers.ETag = new("\"one\"");
                return Task.FromResult(response);
            };
        }

        internal async Task OpenAsync(bool active = true)
        {
            Controller.SetActive(active);
            await Controller.OpenAsync();
            await Until(() => Controller.State is { Loaded: true, ArtworkStorage: not null, StatusLoading: false }, "open");
            await Task.Delay(30);
        }
        internal string[] Writes => Kit.Http.Requests.Where(r => !r.StartsWith("GET")).ToArray();
        internal int Count(string request) => Kit.Http.Requests.Count(r => r == request);
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private static HttpResponseMessage Saved(string values = "{}", string etag = "\"two\"")
    {
        var response = AdminMonitorKit.Json($$"""{"values":{{values}},"restart_required":false}""");
        response.Headers.ETag = new(etag);
        return response;
    }

    private static async Task Until(Func<bool> condition, string label)
    {
        bool Met() { try { return condition(); } catch (InvalidOperationException) { return false; } }
        for (var i = 0; i < 400 && !Met(); i++) await Task.Delay(5);
        Assert.True(Met(), "Timed out: " + label);
    }

    private const string RecoveryPending = """{"current_backend":"s3","reachable":true,"recovery_pending":true,"recovery_state":"running","recovery_progress_percent":40}""";
    private const string ActiveJob = """{"items":[{"id":"job-1","kind":"storage_transition","state":"running","storage_transition_result":{"phase":"copying","verified_objects":3}}]}""";
    private const string Accepted = """{"job":{"id":"job-9","kind":"storage_transition","state":"queued"},"preflight":{}}""";

    [Fact]
    public async Task OpenReadsSettingsStatusCapabilitiesJobsAndRecoveryOnceWithoutRetainingCredentials()
    {
        using var h = new Harness();
        await h.OpenAsync();
        await Until(() => h.Count("GET /api/v2/admin/storage-transitions/source-health?probe=false") == 1, "recovery read");
        var state = h.Controller.State;
        Assert.True(state.StatusKnown);
        Assert.True(state.ManagedTransitionsAvailable);
        Assert.Equal("", state.Get("s3.public_access_key"));
        Assert.Equal(["s3.public_access_key", "s3.public_secret_key"], state.Configured.Order());
        Assert.Equal(1, h.Count("GET /api/v2/admin/server/status"));
        Assert.Equal(1, h.Count("GET /api/v2/admin/storage-transitions/capabilities"));
        Assert.Equal(1, h.Count("GET /api/v2/admin/jobs?kind=storage_transition&limit=1"));
        Assert.Equal(1, h.Count("GET /api/v2/admin/storage-transitions/source-health?probe=false"));
        Assert.Equal(0, h.Count("GET /api/v2/admin/storage-transitions/source-health?probe=true"));
        Assert.False(h.Controller.IsRecoveryPolling);
        Assert.False(h.Controller.IsJobsPolling);
    }

    [Fact]
    public async Task UnavailableTransitionsSkipTheRecoveryRead()
    {
        using var h = new Harness { Capabilities = """{"state":"not_configured"}""" };
        await h.OpenAsync();
        Assert.False(h.Controller.State.ManagedTransitionsAvailable);
        Assert.Equal(0, h.Count("GET /api/v2/admin/storage-transitions/source-health?probe=false"));
    }

    [Fact]
    public async Task SensitiveStatusFailureFailsClosedAndBlocksEveryWrite()
    {
        using var h = new Harness { Sensitive = "fail" };
        await h.OpenAsync();
        Assert.True(h.Controller.State.SensitiveStatusError);
        h.Controller.Set("metadata.cache_images", "false");
        h.Controller.SetSecret("s3.public_secret_key", "typed");
        await h.Controller.SaveAsync();
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Empty(h.Writes);
    }

    [Fact]
    public async Task UnknownLockStatusSavesOnlySafeKeysAndHoldsLocationsBack()
    {
        using var h = new Harness { Status = """{"artwork_storage":{"backend":"local","locked":false,"status_known":false}}""" };
        await h.OpenAsync();
        Assert.False(h.Controller.State.StatusKnown);
        h.Controller.Set("artwork.local_path", "/elsewhere");
        h.Controller.Set("s3.public_bucket", "media");
        Assert.DoesNotContain("artwork.local_path", h.Controller.State.DirtyKeys);
        Assert.DoesNotContain("s3.public_bucket", h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Empty(h.Writes);
        Assert.Contains(h.Notices, n => n.Key == "web.admin.settings.storage.lockUnknownSave");
        h.Controller.Set("metadata.cache_images", "false");
        h.Controller.Set("s3.public_region", "eu");
        string? body = null;
        h.Routes["PUT /api/v2/admin/settings"] = async request => { body = await request.Content!.ReadAsStringAsync(); return Saved(); };
        await h.Controller.SaveAsync();
        using var sent = JsonDocument.Parse(body!);
        Assert.Equal(["metadata.cache_images", "s3.public_region"], sent.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task LockedLocationChangeSavesOtherKeysThenOpensTheReviewWithoutWritingTheLocation()
    {
        using var h = new Harness();
        await h.OpenAsync();
        h.Controller.Set("artwork.local_path", "/mnt/art");
        h.Controller.Set("metadata.cache_images", "false");
        string? body = null; string? match = null;
        h.Routes["PUT /api/v2/admin/settings"] = async request => { body = await request.Content!.ReadAsStringAsync(); match = request.Headers.IfMatch.ToString(); return Saved(); };
        await h.Controller.SaveAsync();
        Assert.Equal("\"one\"", match);
        Assert.Equal("""{"values":{"metadata.cache_images":"false"}}""", body);
        var review = Assert.IsType<AdminStorageReview>(h.Controller.State.Review);
        Assert.Equal(new("local", "/mnt/art", "preserve_uploads"), review);
        Assert.Contains("artwork.local_path", h.Controller.State.DirtyKeys);
        Assert.Equal(0, h.Count("GET /api/v2/admin/storage-transitions/source-health?probe=true")); // a local source is not probed
    }

    [Fact]
    public async Task TransitionsUnavailableNeverOpenAReview()
    {
        using var h = new Harness { Capabilities = """{"state":"available","allowed":false}""" };
        await h.OpenAsync();
        h.Controller.Set("artwork.local_path", "/mnt/art");
        await h.Controller.SaveAsync();
        Assert.Null(h.Controller.State.Review);
        Assert.Empty(h.Writes);
        Assert.Contains(h.Notices, n => n.Key == "web.admin.settings.storage.transitionsUnavailable");
    }

    [Fact]
    public async Task LockedS3MovingToLocalOpensItsReviewAtOnceAndProbesTheSource()
    {
        using var h = new Harness
        {
            Status = """{"artwork_storage":{"backend":"s3","locked":true,"status_known":true,"private_locked":true}}""",
            Effective = """{"artwork.storage_backend":"s3","artwork.local_path":"/data/art","s3.public_bucket":"media","s3.public_endpoint":"https://s3.example"}""",
        };
        await h.OpenAsync();
        h.Controller.ChangeBackend("local");
        Assert.Equal("local", h.Controller.State.Review?.Backend);
        Assert.DoesNotContain("artwork.storage_backend", h.Controller.State.DirtyKeys);
        await Until(() => h.Controller.State.SourceHealth != null, "probe");
        Assert.Equal(1, h.Count("GET /api/v2/admin/storage-transitions/source-health?probe=true"));
        h.Controller.CloseReview();
        Assert.Null(h.Controller.State.Review);
        // Moving a locked local store to S3 only stages the backend so Public storage can be completed.
        using var local = new Harness();
        await local.OpenAsync();
        local.Controller.ChangeBackend("s3");
        Assert.Null(local.Controller.State.Review);
        Assert.Contains("artwork.storage_backend", local.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task QueueSendsTheReviewedCommandOnceThenResetsItsKeysAndReloads()
    {
        using var h = new Harness();
        await h.OpenAsync();
        h.Controller.Set("artwork.local_path", "/mnt/art");
        await h.Controller.SaveAsync();
        h.Controller.SetReviewPolicy("migrate_all");
        h.Controller.SetReviewLocalPath("/mnt/reviewed");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? body = null;
        h.Routes["POST /api/v2/admin/storage-transitions"] = async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            await gate.Task;
            return AdminMonitorKit.Json(Accepted, HttpStatusCode.Accepted);
        };
        var first = h.Controller.QueueTransitionAsync();
        await Until(() => h.Controller.State.Queuing, "queuing");
        h.Controller.SetReviewLocalPath("/changed-while-queuing");
        await h.Controller.QueueTransitionAsync(); // duplicate activation
        gate.SetResult();
        await first;
        Assert.Equal(["POST /api/v2/admin/storage-transitions"], h.Writes);
        using var sent = JsonDocument.Parse(body!);
        Assert.Equal("migrate_all", sent.RootElement.GetProperty("policy").GetString());
        Assert.Equal("/mnt/reviewed", sent.RootElement.GetProperty("values").GetProperty("artwork.local_path").GetString());
        Assert.Null(h.Controller.State.Review);
        Assert.DoesNotContain("artwork.local_path", h.Controller.State.DirtyKeys);
        Assert.Contains(h.Notices, n => n is { Success: true, Key: "web.admin.settings.storage.transitionQueued", JobId: "job-9" });
        await Until(() => h.Count("GET /api/v2/admin/settings/effective") >= 2, "settings reload");
    }

    [Fact]
    public async Task AmbiguousQueueResultIsReReadAndNeverResent()
    {
        using var h = new Harness();
        await h.OpenAsync();
        h.Controller.Set("artwork.local_path", "/mnt/art");
        await h.Controller.SaveAsync();
        var jobsBefore = h.Count("GET /api/v2/admin/jobs?kind=storage_transition&limit=1");
        h.Routes["POST /api/v2/admin/storage-transitions"] = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.BadGateway));
        await h.Controller.QueueTransitionAsync();
        Assert.Single(h.Writes);
        Assert.Equal("configure.native.storage.transitionUnknown", h.Controller.State.CommandUnknownKey);
        Assert.Null(h.Controller.State.Review);
        Assert.Contains("artwork.local_path", h.Controller.State.DirtyKeys);
        await Until(() => h.Count("GET /api/v2/admin/jobs?kind=storage_transition&limit=1") > jobsBefore, "jobs re-read");
        await h.Controller.QueueTransitionAsync(); // no open review: nothing is resent
        Assert.Single(h.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task QueueInvalidationRunsAfterAnOlderJobsRead(bool accepted)
    {
        using var h = new Harness();
        await h.OpenAsync();
        h.Controller.Set("artwork.local_path", "/mnt/art");
        await h.Controller.SaveAsync();
        h.Controller.SetActive(false);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        h.Routes["GET /api/v2/admin/jobs"] = async _ =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                started.SetResult();
                await release.Task;
                return AdminMonitorKit.Json("{\"items\":[]}");
            }
            return AdminMonitorKit.Json(ActiveJob);
        };
        h.Routes["POST /api/v2/admin/storage-transitions"] = _ => Task.FromResult(
            AdminMonitorKit.Json(accepted ? Accepted : "{}", accepted ? HttpStatusCode.Accepted : HttpStatusCode.BadGateway));
        try
        {
            h.Controller.SetActive(true);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await h.Controller.QueueTransitionAsync();
            release.SetResult();
            await Until(() => h.Controller.State.LatestTransition?.Id == "job-1", "post-command jobs read");
            Assert.Equal(2, reads);
            Assert.Single(h.Writes);
            Assert.True(h.Controller.IsJobsPolling);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task KnownQueueRejectionKeepsTheReviewOpenForCorrection()
    {
        using var h = new Harness();
        await h.OpenAsync();
        h.Controller.Set("artwork.local_path", "/mnt/art");
        await h.Controller.SaveAsync();
        h.Routes["POST /api/v2/admin/storage-transitions"] = _ => Task.FromResult(AdminMonitorKit.Json("""{"status":422,"code":"validation_failed"}""", HttpStatusCode.UnprocessableEntity));
        await h.Controller.QueueTransitionAsync();
        Assert.NotNull(h.Controller.State.Review);
        Assert.False(h.Controller.State.Queuing);
        Assert.Contains(h.Notices, n => n is { Success: false, Key: "web.admin.settings.storage.transitionQueueFailed" });
    }

    [Fact]
    public async Task CredentialsAreWriteOnlyKeepClearAndEraseAfterAcknowledgement()
    {
        using var h = new Harness { Status = """{"artwork_storage":{"backend":"local","locked":false,"status_known":true}}""" };
        await h.OpenAsync();
        h.Controller.SetSecret("s3.public_secret_key", "typed-secret");
        Assert.Contains("s3.public_secret_key", h.Controller.State.DirtyKeys);
        h.Controller.SetSecret("s3.public_secret_key", "");
        Assert.DoesNotContain("s3.public_secret_key", h.Controller.State.DirtyKeys); // emptying keeps the saved value
        h.Controller.ClearSecret("s3.public_access_key");
        Assert.True(h.Controller.State.IsDirty("s3.public_access_key"));
        h.Controller.ClearSecret("s3.private_secret_key"); // not configured: nothing to clear
        Assert.False(h.Controller.State.IsDirty("s3.private_secret_key"));
        h.Controller.SetSecret("s3.public_secret_key", "replacement");
        h.Routes["PUT /api/v2/admin/settings"] = _ => Task.FromResult(Saved("""{"s3.public_secret_key":"echo-must-not-show"}"""));
        await h.Controller.SaveAsync();
        Assert.Equal("", h.Controller.State.Get("s3.public_secret_key"));
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.DoesNotContain("s3.public_access_key", h.Controller.State.Configured);
        Assert.Contains("s3.public_secret_key", h.Controller.State.Configured);
    }

    [Fact]
    public async Task EnvironmentManagedKeysAreNeitherEditedNorWritten()
    {
        using var h = new Harness { Sensitive = """{"configured":[],"managed_by_env":["s3.public_region"]}""" };
        await h.OpenAsync();
        h.Controller.Set("s3.public_region", "eu");
        Assert.Empty(h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task ConflictRequiresExplicitReloadAndUnknownSaveIsNotReplayed()
    {
        using var h = new Harness();
        await h.OpenAsync();
        h.Controller.Set("metadata.cache_images", "false");
        h.Routes["PUT /api/v2/admin/settings"] = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.PreconditionFailed));
        await h.Controller.SaveAsync(); await h.Controller.SaveAsync();
        Assert.True(h.Controller.State.Conflict);
        Assert.Single(h.Writes);
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.False(h.Controller.State.Conflict);
        Assert.Contains("metadata.cache_images", h.Controller.State.DirtyKeys);
        h.Routes["PUT /api/v2/admin/settings"] = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError));
        await h.Controller.SaveAsync(); await h.Controller.SaveAsync();
        Assert.True(h.Controller.State.OutcomeUnknown);
        Assert.Equal(2, h.Writes.Length);
    }

    [Fact]
    public async Task RecoveryPollsEveryFiveSecondsOnlyWhileActiveAndPending()
    {
        using var h = new Harness { Health = RecoveryPending };
        await h.OpenAsync();
        await Until(() => h.Controller.IsRecoveryPolling, "recovery poll");
        var reads = h.Count("GET /api/v2/admin/storage-transitions/source-health?probe=false");
        h.Time.Advance(TimeSpan.FromSeconds(5));
        await Until(() => h.Count("GET /api/v2/admin/storage-transitions/source-health?probe=false") == reads + 1, "recovery poll");
        h.Controller.SetActive(false);
        Assert.False(h.Controller.IsRecoveryPolling);
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(50);
        Assert.Equal(reads + 1, h.Count("GET /api/v2/admin/storage-transitions/source-health?probe=false"));
        // Reactivation refreshes once (focus refetch) and resumes the poll; a cleared recovery stops it.
        h.Health = """{"current_backend":"s3","reachable":true,"recovery_pending":false}""";
        h.Controller.SetActive(true);
        await Until(() => h.Count("GET /api/v2/admin/storage-transitions/source-health?probe=false") == reads + 2, "focus refresh");
        await Until(() => !h.Controller.IsRecoveryPolling, "recovery poll stopped");
    }

    [Fact]
    public async Task FailedCapabilitiesRefreshRetainsManagedRecoveryAndPolling()
    {
        using var h = new Harness { Health = RecoveryPending };
        await h.OpenAsync();
        await Until(() => h.Controller.IsRecoveryPolling, "initial recovery poll");
        h.Controller.SetActive(false);
        h.Time.Advance(TimeSpan.FromSeconds(61));
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = false;
        h.Routes["GET /api/v2/admin/jobs"] = async _ => { await release.Task; return AdminMonitorKit.Json(h.Jobs); };
        h.Routes["GET /api/v2/admin/storage-transitions/source-health"] = async _ => { await release.Task; return AdminMonitorKit.Json(h.Health); };
        h.Routes["GET /api/v2/admin/server/status"] = async _ => { await release.Task; return AdminMonitorKit.Json(h.Status); };
        h.Routes["GET /api/v2/admin/storage-transitions/capabilities"] = _ =>
        {
            requested = true;
            return Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
        };
        h.Controller.Changed += () =>
        {
            if (requested) refreshed.TrySetResult();
        };
        try
        {
            h.Controller.SetActive(true);
            await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(h.Controller.State.ManagedTransitionsAvailable);
            await Until(() => h.Controller.IsRecoveryPolling, "retained recovery poll");
            Assert.True(h.Controller.State.Recovery?.RecoveryPending);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task HiddenPagesNeverPollAndDeferredTicksYieldToPlayback()
    {
        using var h = new Harness { Health = RecoveryPending, Jobs = ActiveJob };
        await h.OpenAsync(active: false);
        Assert.False(h.Controller.IsRecoveryPolling);
        Assert.False(h.Controller.IsJobsPolling);
        var beforeActivation = h.Count("GET /api/v2/admin/jobs?kind=storage_transition&limit=1");
        h.Controller.SetActive(true);
        await Until(() => h.Controller.IsJobsPolling && h.Controller.IsRecoveryPolling
            && h.Count("GET /api/v2/admin/jobs?kind=storage_transition&limit=1") > beforeActivation, "focus refresh and polls");
        var jobs = h.Count("GET /api/v2/admin/jobs?kind=storage_transition&limit=1");
        h.Defer = true;
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        Assert.Equal(jobs, h.Count("GET /api/v2/admin/jobs?kind=storage_transition&limit=1"));
        h.Defer = false;
        h.Jobs = """{"items":[{"id":"job-1","kind":"storage_transition","state":"succeeded","storage_transition_result":{"phase":"completed"}}]}""";
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => h.Controller.State.LatestTransition?.Status == "completed", "terminal job");
        await Until(() => !h.Controller.IsJobsPolling, "jobs poll stopped");
    }

    [Fact]
    public async Task CancelTargetsTheCapturedJobOnceAndAnAmbiguousAnswerIsReRead()
    {
        using var h = new Harness { Jobs = ActiveJob };
        await h.OpenAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Routes["POST /api/v2/admin/jobs/job-1/cancel"] = async _ => { await gate.Task; return AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable); };
        var first = h.Controller.CancelTransitionAsync();
        await Until(() => h.Controller.State.Cancelling, "cancelling");
        await h.Controller.CancelTransitionAsync();
        gate.SetResult();
        await first;
        Assert.Equal(["POST /api/v2/admin/jobs/job-1/cancel"], h.Writes);
        Assert.Equal("configure.native.storage.cancelUnknown", h.Controller.State.CommandUnknownKey);
        Assert.False(h.Controller.State.Cancelling);
    }

    [Fact]
    public async Task DismissHidesOnlyTheCurrentFailedJob()
    {
        using var h = new Harness { Jobs = """{"items":[{"id":"job-3","kind":"storage_transition","state":"failed","storage_transition_result":{"phase":"failed","failure_category":"copy_failed"}}]}""" };
        await h.OpenAsync();
        Assert.True(h.Controller.State.LatestTransition!.IsFailedOrCancelled);
        h.Controller.DismissTransition("other");
        Assert.Null(h.Controller.State.DismissedTransitionId);
        h.Controller.DismissTransition("job-3");
        Assert.Equal("job-3", h.Controller.State.DismissedTransitionId);
    }

    [Fact]
    public async Task ConnectionCheckSendsOnlyStagedCredentialsAndAnEditHidesItsResult()
    {
        using var h = new Harness { Status = """{"artwork_storage":{"backend":"local","locked":false,"status_known":true}}""" };
        await h.OpenAsync();
        h.Controller.Set("s3.public_bucket", "media");
        h.Controller.SetSecret("s3.public_secret_key", "typed-secret");
        string? body = null;
        h.Routes["POST /api/v2/admin/settings/check/s3_public"] = async request => { body = await request.Content!.ReadAsStringAsync(); return AdminMonitorKit.Json("""{"success":true,"message":"Connected"}"""); };
        await h.Controller.CheckConnectionAsync("public");
        using var sent = JsonDocument.Parse(body!);
        var values = sent.RootElement.GetProperty("values");
        Assert.False(values.TryGetProperty("s3.public_access_key", out _));
        Assert.Equal("typed-secret", values.GetProperty("s3.public_secret_key").GetString());
        Assert.Equal(["s3.public_bucket", "s3.public_secret_key"], sent.RootElement.GetProperty("dirty_keys").EnumerateArray().Select(v => v.GetString()).Order());
        Assert.True(h.Controller.State.PublicCheck?.Success);
        h.Controller.Set("s3.private_region", "x");
        Assert.NotNull(h.Controller.State.PublicCheck);
        h.Controller.Set("s3.public_region", "x");
        Assert.Null(h.Controller.State.PublicCheck);
    }

    [Fact]
    public async Task RetiredAuthorityDropsLateResultsCommandsAndPolls()
    {
        using var h = new Harness { Health = RecoveryPending };
        await h.OpenAsync();
        h.Controller.Set("artwork.local_path", "/mnt/art");
        await h.Controller.SaveAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Routes["POST /api/v2/admin/storage-transitions"] = async _ => { await gate.Task; return AdminMonitorKit.Json(Accepted, HttpStatusCode.Accepted); };
        var queue = h.Controller.QueueTransitionAsync();
        await Until(() => h.Controller.State.Queuing, "queuing");
        h.Authority.Retire();
        gate.SetResult();
        await queue;
        var state = h.Controller.State;
        Assert.Null(state.Owner);
        Assert.Empty(state.Values);
        Assert.Null(state.Review);
        Assert.DoesNotContain(h.Notices, n => n.Success);
        Assert.False(h.Controller.IsRecoveryPolling);
    }

    [Fact]
    public async Task StatusFailureAfterAKnownReadDisablesOnlyLocationEditing()
    {
        using var h = new Harness();
        await h.OpenAsync();
        h.Status = "fail";
        h.Controller.RetryStatus();
        await Until(() => h.Controller.State is { StatusFailed: true, StatusLoading: false }, "status failure");
        Assert.NotNull(h.Controller.State.ArtworkStorage);
        Assert.False(h.Controller.State.StatusKnown);
        Assert.False(h.Controller.State.StatusUnavailable);
        h.Controller.Set("artwork.local_path", "/x");
        h.Controller.Set("metadata.cache_images", "false");
        Assert.Equal(["metadata.cache_images"], h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task MissingArtworkStatusIsAFullPageGap()
    {
        using var h = new Harness { Status = """{"restart_required":false}""" };
        h.Controller.SetActive(true);
        await h.Controller.OpenAsync();
        await Until(() => h.Controller.State is { Loaded: true, StatusRead: true }, "status read");
        Assert.True(h.Controller.State.StatusUnavailable);
        await h.Controller.SaveAsync();
        Assert.Empty(h.Writes);
    }
}
