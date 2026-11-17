using System.Collections.Concurrent;
using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Present = SiloPlayer.Core.Services.Admin.AdminBackupPresentation;

namespace SiloPlayer.Tests;

/// <summary>Configure > Server > Backups page controller on a synthetic server and a manual clock.</summary>
public sealed class AdminBackupsControllerTests
{
    private const string Effective = """
        {"backup.enabled":"true","backup.time":"04:00","backup.keep":"7","backup.include_intro_fingerprints":"false","backup.include_subtitles":"true",
         "backup.destination":"default","backup.s3.endpoint":"","backup.s3.region":"","backup.s3.bucket":"","backup.s3.key_prefix":"","backup.s3.access_key":"",
         "backup.s3.path_style":"false","s3.private_bucket":"","branding.server_name":"Other page"}
        """;

    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsApi SettingsApi;
        internal readonly AdminServerSettingsStore Store;
        internal readonly BackupScheduler Scheduler = new();
        internal readonly AdminBackupsController Controller;
        internal readonly ConcurrentQueue<AdminBackupNotice> Notices = new();
        internal string List = BackupJson.List(BackupJson.Backup());
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>?>? Override;
        internal TaskCompletionSource? ListGate;
        internal int ListReads;

        internal Harness()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            SettingsApi = new AdminServerSettingsApi(Kit.Client);
            Store = new AdminServerSettingsStore(SettingsApi, Authority, AdminBackupsController.Keys, AdminBackupsController.Secrets);
            Controller = new AdminBackupsController(new AdminBackupsApi(Kit.Client), SettingsApi, Store, Authority, Scheduler);
            Controller.Notified += Notices.Enqueue;
            Kit.Http.Respond = async (request, ct) =>
            {
                if (Override?.Invoke(request) is { } custom) return await custom;
                var path = request.RequestUri!.AbsolutePath;
                switch (path)
                {
                    case "/api/v2/admin/backups":
                        Interlocked.Increment(ref ListReads);
                        if (ListGate is { } gate) await gate.Task.WaitAsync(ct);
                        return BackupJson.Json(List);
                    case "/api/v2/admin/backups/capabilities": return BackupJson.Json(BackupJson.Capabilities(upload: false));
                    case "/api/v2/admin/tasks/backup": return BackupJson.Json("""{"key":"backup","next_run_at":"2026-11-17T04:00:00.000Z"}""");
                    case "/api/v2/admin/settings/effective":
                        var effective = AdminMonitorKit.Json(Effective); effective.Headers.ETag = new("\"one\""); return effective;
                    case "/api/v2/admin/settings/sensitive-status": return AdminMonitorKit.Json("""{"configured":["backup.s3.secret_key"],"managed_by_env":[]}""");
                    default: return AdminMonitorKit.Json("{}", HttpStatusCode.NotFound);
                }
            };
        }
        internal string[] Requests => Kit.Http.Requests.ToArray();
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    [Fact]
    public async Task OpensWithListCapabilitiesScheduleAndOnlyBackupSettings()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var state = h.Controller.State;
        Assert.NotNull(state.List);
        Assert.True(state.Capabilities!.Available);
        Assert.Equal(DateTimeOffset.Parse("2026-11-17T04:00:00Z"), state.TaskNextRunAt);
        Assert.True(state.SettingsLoaded);
        Assert.Equal(Present.SettingKeys.OrderBy(key => key), state.Values.Keys.OrderBy(key => key));
        Assert.DoesNotContain("branding.server_name", state.PersistedValues.Keys);
        Assert.DoesNotContain(Present.S3SecretKey, state.PersistedValues.Keys);
        Assert.True(state.SecretConfigured);
    }

    [Fact]
    public async Task CapabilitiesRefreshesNeverOverlap()
    {
        using var harness = new Harness();
        await harness.Controller.OpenAsync();
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        harness.Override = request => request.RequestUri!.AbsolutePath == "/api/v2/admin/backups/capabilities"
            ? Counted() : null;
        Task<HttpResponseMessage> Counted()
        {
            Interlocked.Increment(ref reads);
            return answer.Task;
        }
        var first = harness.Controller.RefreshAsync();
        var second = harness.Controller.RefreshAsync();
        try
        {
            await Eventually.True(() => reads > 0, "capabilities read started");
            Assert.Equal(1, reads);
        }
        finally
        {
            answer.TrySetResult(BackupJson.Json(BackupJson.Capabilities(upload: false)));
            await Task.WhenAll(first, second);
        }
    }

    [Fact]
    public async Task CheckingUploadsResumeWhenThePageBecomesVisible()
    {
        using var harness = new Harness();
        harness.Override = request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/admin/backups/capabilities" => Task.FromResult(BackupJson.Json(BackupJson.Capabilities())),
            "/api/v2/admin/backups/uploads" => Task.FromResult(BackupJson.Json("{\"items\":[" + BackupJson.Upload("checking", received: 3) + "]}")),
            _ => null,
        };
        await harness.Controller.OpenAsync();
        Assert.Contains(AdminBackupsController.CheckingInterval, harness.Scheduler.Pending);
        harness.Controller.SetVisible(false);
        Assert.Empty(harness.Scheduler.Pending);
        harness.Controller.SetVisible(true);
        await Eventually.True(() => harness.Scheduler.Pending.Contains(AdminBackupsController.CheckingInterval), "checking polling resumed");
    }

    [Fact]
    public async Task TheListIsReadEvery30SecondsIdleAnd3SecondsWhileTheLockIsHeld()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        Assert.Contains(AdminBackupsController.IdleInterval, h.Scheduler.Pending);
        h.List = BackupJson.List(BackupJson.Backup(status: "in_progress"), busy: true,
            active: """{"operation":"backup","backup_id":"20261115T040000Z-nightly","started_at":"2026-11-16T12:00:00.000Z","progress":40}""");
        h.Scheduler.Advance(AdminBackupsController.IdleInterval);
        await Eventually.True(() => h.Controller.State.List?.Busy == true, "busy list read");
        await Eventually.True(() => h.Scheduler.Pending.Contains(AdminBackupsController.BusyInterval), "busy cadence");
    }

    [Fact]
    public async Task BackgroundTicksNeverQueueAndForegroundRefreshesCoalesce()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var before = h.ListReads;
        h.ListGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = h.Controller.RefreshAsync();
        await Eventually.True(() => h.ListReads == before + 1, "first read in flight");
        _ = h.Controller.RefreshAsync(); _ = h.Controller.RefreshAsync();
        h.Scheduler.Advance(AdminBackupsController.IdleInterval); // a background tick while a read is in flight
        h.ListGate.SetResult(); h.ListGate = null;
        await first;
        await Eventually.True(() => h.ListReads == before + 2, "one coalesced follow-up");
        await Task.Delay(50);
        Assert.Equal(before + 2, h.ListReads);
    }

    [Fact]
    public async Task BackgroundReadsWaitWhilePlaybackStartsAndWhileHidden()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var before = h.ListReads;
        var deferring = true;
        h.Controller.DeferBackground = () => deferring;
        h.Scheduler.Advance(AdminBackupsController.IdleInterval);
        Assert.Equal(before, h.ListReads);
        Assert.Contains(AdminBackupsController.DeferRetry, h.Scheduler.Pending);
        deferring = false;
        h.Scheduler.Advance(AdminBackupsController.DeferRetry);
        await Eventually.True(() => h.ListReads == before + 1, "deferred read runs");
        h.Controller.SetVisible(false);
        Assert.Empty(h.Scheduler.Pending);
        h.Controller.SetVisible(true);
        await Eventually.True(() => h.ListReads == before + 2, "visible again reads at once");
    }

    [Fact]
    public async Task ATransientFailureKeepsTheLastGoodList()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var good = h.Controller.State.List;
        h.Override = request => request.RequestUri!.AbsolutePath == "/api/v2/admin/backups" && request.Method == HttpMethod.Get
            ? Task.FromResult(BackupJson.Json(BackupJson.Problem("dependency_unavailable", 503), HttpStatusCode.ServiceUnavailable)) : null;
        await h.Controller.RefreshAsync();
        Assert.Same(good, h.Controller.State.List);
        Assert.NotNull(h.Controller.State.ListError);
    }

    [Fact]
    public async Task ARetiredOwnersResponsesAndTimersAreDropped()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.ListGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = h.Controller.RefreshAsync();
        await Eventually.True(() => h.ListGate != null && h.ListReads > 1, "read in flight");
        h.Controller.Suspend();
        h.ListGate.SetResult();
        try { await pending; } catch (OperationCanceledException) { }
        Assert.Null(h.Controller.State.Owner);
        Assert.Null(h.Controller.State.List);
        Assert.Empty(h.Scheduler.Pending);
        Assert.Empty(h.Controller.State.Values);
    }

    [Fact]
    public async Task BackUpNowUnknownOutcomeBlocksTheButtonUntilAnExplicitRefresh()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Override = request => request.Method == HttpMethod.Post ? throw new HttpRequestException("synthetic") : null;
        await h.Controller.BackUpNowAsync();
        Assert.Equal("backup", h.Controller.State.UnknownOutcome);
        await h.Controller.BackUpNowAsync();
        Assert.Single(h.Requests, request => request == "POST /api/v2/admin/backups");
        await h.Controller.RefreshAsync();
        Assert.Null(h.Controller.State.UnknownOutcome);
    }

    [Fact]
    public async Task TheStartedBackupsOutcomeIsReadOnlyFromALaterList()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Override = request => request.Method == HttpMethod.Post
            ? Task.FromResult(BackupJson.Json("""{"id":"20261116T120000Z-manual","kind":"manual","status":"in_progress"}""", HttpStatusCode.Accepted)) : null;
        await h.Controller.BackUpNowAsync();
        h.Override = null;
        h.List = BackupJson.List(BackupJson.Backup(), lastRun: """{"id":"20261116T120000Z-manual","kind":"manual","status":"complete","started_at":"2026-11-16T12:00:00.000Z"}""");
        h.Scheduler.Advance(TimeSpan.FromSeconds(1));
        await h.Controller.RefreshAsync();
        Assert.Contains(h.Notices, notice => notice.Title == AdminText.T("web.admin.backups.toast.finished"));
    }

    [Fact]
    public async Task InvalidKeepBlocksSaveAndA412KeepsTheDraftForReload()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.Set(Present.KeepKey, "61");
        Assert.True(h.Controller.State.Errors.ContainsKey(Present.KeepKey));
        await h.Controller.SaveAsync();
        Assert.DoesNotContain(h.Requests, request => request.StartsWith("PUT", StringComparison.Ordinal));
        h.Controller.Set(Present.KeepKey, "14");
        h.Override = request => request.Method == HttpMethod.Put
            ? Task.FromResult(AdminMonitorKit.Json("""{"type":"precondition_failed","status":412}""", HttpStatusCode.PreconditionFailed)) : null;
        await h.Controller.SaveAsync();
        var state = h.Controller.State;
        Assert.True(state.Conflict);
        Assert.Equal("14", state.Values[Present.KeepKey]);
        Assert.Contains(Present.KeepKey, state.DirtyKeys);
        Assert.Single(h.Requests, request => request.StartsWith("PUT", StringComparison.Ordinal));
        h.Override = null;
        await h.Controller.ReloadSettingsAsync(preserveDraft: true);
        Assert.False(h.Controller.State.Conflict);
        Assert.Equal("14", h.Controller.State.Values[Present.KeepKey]);
    }

    [Theory]
    [InlineData("04:00", true)]
    [InlineData("4:05", true)]
    [InlineData("23:59", true)]
    [InlineData("24:00", false)]
    [InlineData("04:0", false)]
    [InlineData("0400", false)]
    public void TimeValidationFollowsTheServersParser(string value, bool valid) => Assert.Equal(valid, AdminBackupsController.IsTime(value));

    [Fact]
    public async Task S3SaveConfirmsTheKeyFirstAndNeverKeepsTheTypedSecret()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        string? putBody = null;
        h.Override = request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/acknowledge-key", StringComparison.Ordinal))
                return Task.FromResult(BackupJson.Json("""{"key_check":"abcd-ef01-2345-6789"}"""));
            if (request.Method == HttpMethod.Put)
            {
                putBody = request.Content!.ReadAsStringAsync().Result;
                var saved = AdminMonitorKit.Json("""{"values":{"backup.destination":"s3"},"restart_required":false}"""); saved.Headers.ETag = new("\"two\"");
                return Task.FromResult(saved);
            }
            return null;
        };
        var problem = await h.Controller.SaveS3Async(new("https://s3.example.invalid", "", "bucket", "", "AKIA", "typed-secret", false), "abcd-ef01-2345-6789");
        Assert.Null(problem);
        var requests = h.Requests;
        Assert.True(Array.FindIndex(requests, request => request.EndsWith("/acknowledge-key", StringComparison.Ordinal)) < Array.FindIndex(requests, request => request.StartsWith("PUT", StringComparison.Ordinal)));
        Assert.Contains("\"backup.s3.secret_key\":\"typed-secret\"", putBody);
        Assert.Contains("\"backup.destination\":\"s3\"", putBody);
        Assert.DoesNotContain(h.Controller.State.Values.Values, value => value.Contains("typed-secret", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Controller.State.PersistedValues.Values, value => value.Contains("typed-secret", StringComparison.Ordinal));
    }
}
