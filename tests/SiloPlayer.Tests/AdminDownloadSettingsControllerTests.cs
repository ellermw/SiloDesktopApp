using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using R = SiloPlayer.Core.Services.Admin.AdminDownloadSettingsRules;

namespace SiloPlayer.Tests;

/// <summary>Manage > Downloads > Settings (official DownloadsSettings.tsx at silo-server abd85d0c) on synthetic data.</summary>
public sealed class AdminDownloadSettingsControllerTests
{
    private const string EffectiveJson = """
        {"download.enabled":"true","download.transcode_enabled":"false","download.max_concurrent_prepares":"2","download.artifact_dir":"",
         "download.artifact_max_bytes":"53687091200","download.artifact_disk_ceiling_percent":"85","download.server_bandwidth_mbps":"0",
         "download.user_bandwidth_mbps":"50","download.max_concurrent_per_user":"3","download.max_per_period":"0","download.period_duration":"24h",
         "download.local_transcode_fallback":"true","download.artifact_cache_hours":"72","playback.transcode_dir":"/mnt/fast/transcode",
         "branding.server_name":"Other page","email.smtp_host":"mail.example.com"}
        """;

    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsApi Api;
        internal readonly AdminServerSettingsStore Store;
        internal readonly AdminDownloadSettingsController Controller;
        internal string Effective = EffectiveJson;
        internal string Sensitive = """{"configured":[],"managed_by_env":[]}""";
        internal string Restart = """{"keys":["download.artifact_dir"],"prefixes":[]}""";
        internal HttpStatusCode EffectiveStatus = HttpStatusCode.OK;
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Write;
        internal Harness()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Api = new AdminServerSettingsApi(Kit.Client);
            Store = new AdminServerSettingsStore(Api, Authority, AdminDownloadSettingsController.Keys.Concat(AdminDownloadSettingsController.ReadOnlyKeys),
                AdminDownloadSettingsController.Secrets);
            Controller = new AdminDownloadSettingsController(Api, Store, Authority);
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method != HttpMethod.Get) return Write?.Invoke(request) ?? Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError));
                var path = request.RequestUri!.AbsolutePath;
                HttpResponseMessage response;
                if (path.EndsWith("effective")) response = EffectiveStatus == HttpStatusCode.OK ? AdminMonitorKit.Json(Effective)
                    : AdminMonitorKit.Json($$"""{"type":"permission_denied","status":{{(int)EffectiveStatus}}}""", EffectiveStatus);
                else if (path.EndsWith("sensitive-status")) response = Sensitive == "fail" ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : AdminMonitorKit.Json(Sensitive);
                else response = Restart == "fail" ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : AdminMonitorKit.Json(Restart);
                response.Headers.ETag = new("\"one\"");
                return Task.FromResult(response);
            };
        }
        internal string[] Writes => Kit.Http.Requests.Where(r => !r.StartsWith("GET")).ToArray();
        internal string LastBody => Kit.Http.Bodies.Last();
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private static HttpResponseMessage Saved(string values, string etag = "\"two\"", bool restart = false)
    {
        var response = AdminMonitorKit.Json($$"""{"values":{{values}},"restart_required":{{(restart ? "true" : "false")}}}""");
        response.Headers.ETag = new(etag);
        return response;
    }

    [Fact]
    public async Task LoadProjectsOnlyTheThirteenDownloadKeysAndReadsTheTranscodeDirectoryAsDisplayOnly()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var state = h.Controller.State;
        Assert.True(state.Loaded);
        Assert.Equal(R.Keys.OrderBy(k => k), state.Values.Keys.OrderBy(k => k));
        Assert.DoesNotContain("branding.server_name", state.Values.Keys);
        Assert.DoesNotContain("email.smtp_host", state.Values.Keys);
        Assert.DoesNotContain(R.TranscodeDir, state.Values.Keys);
        Assert.Equal("/mnt/fast/transcode", state.TranscodeDirectory);
        Assert.Equal("/mnt/fast/silo-download-artifacts", state.DerivedArtifactDirectory);
        Assert.Equal("53687091200", state.Values[R.ArtifactMaxBytes]);
        Assert.True(state.RestartKeys.Has(R.ArtifactDir));
        Assert.False(state.RestartKeys.Has(R.Enabled));
        Assert.Empty(state.DirtyKeys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturningWithinTheSnapshotWindowReadsTheCurrentServer(bool unavailable)
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var reads = h.Kit.Http.Requests.Count(r => r == "GET /api/v2/admin/settings/effective");
        h.Controller.Suspend();
        h.Effective = EffectiveJson.Replace("\"download.artifact_cache_hours\":\"72\"", "\"download.artifact_cache_hours\":\"48\"");
        if (unavailable) h.EffectiveStatus = HttpStatusCode.ServiceUnavailable;

        await h.Controller.OpenAsync();

        Assert.True(h.Kit.Http.Requests.Count(r => r == "GET /api/v2/admin/settings/effective") > reads);
        Assert.Equal(!unavailable, h.Controller.State.Loaded);
        if (unavailable)
        {
            Assert.NotNull(h.Controller.State.Error);
            Assert.Empty(h.Controller.State.Values);
        }
        else Assert.Equal("48", h.Controller.State.Values[R.CacheHours]);
    }

    [Fact]
    public async Task LimitingASavedUnlimitedQuotaReportsTheCleanPeriodDependency()
    {
        using var h = new Harness { Effective = EffectiveJson.Replace("\"download.period_duration\":\"24h\"", "\"download.period_duration\":\"0s\"") };
        await h.Controller.OpenAsync();
        Assert.Empty(h.Controller.State.Errors);

        h.Controller.Set(R.MaxPerPeriod, "5");

        Assert.Equal([R.MaxPerPeriod], h.Controller.State.DirtyKeys);
        Assert.Equal(AdminDownloadFieldProblem.PeriodRequired, h.Controller.State.Errors[R.PeriodDuration].Problem);
        await h.Controller.SaveAsync();
        Assert.Empty(h.Writes);
        h.Controller.Set(R.PeriodDuration, "24h");
        Assert.Empty(h.Controller.State.Errors);
    }

    [Fact]
    public async Task SaveSendsOneStrongIfMatchWriteOfOnlyTheDirtyKeysAndAdoptsCanonicalValues()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        string? ifMatch = null;
        h.Write = request => { ifMatch = request.Headers.IfMatch.Single().ToString(); return Task.FromResult(Saved("""{"download.artifact_max_bytes":"100000000000","download.max_concurrent_per_user":"5"}""", restart: false)); };
        h.Controller.Set(R.ArtifactMaxBytes, R.ToStoredUnits("100", R.BytesPerGigabyte));
        h.Controller.Set(R.MaxConcurrentPerUser, "+5"); // the server canonicalizes this to 5
        Assert.Equal(new[] { R.ArtifactMaxBytes, R.MaxConcurrentPerUser }.Order(), h.Controller.State.DirtyKeys.Order());
        await h.Controller.SaveAsync();
        Assert.Equal("\"one\"", ifMatch);
        Assert.Equal(["PUT /api/v2/admin/settings"], h.Writes);
        using (var body = JsonDocument.Parse(h.LastBody))
        {
            var values = body.RootElement.GetProperty("values").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
            Assert.Equal(new Dictionary<string, string?> { [R.ArtifactMaxBytes] = "100000000000", [R.MaxConcurrentPerUser] = "+5" }, values);
        }
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("5", h.Controller.State.Values[R.MaxConcurrentPerUser]);
        Assert.Equal("100000000000", h.Controller.State.PersistedValues[R.ArtifactMaxBytes]);
    }

    [Fact]
    public async Task ChangingAValueBackToTheSavedOneWithdrawsTheEdit()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.Set(R.CacheHours, "100");
        Assert.Equal([R.CacheHours], h.Controller.State.DirtyKeys);
        h.Controller.Set(R.CacheHours, "72");
        Assert.Empty(h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task InvalidValuesNeverReachTheServerAndExplainThemselvesUntilFixed()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(Saved("{}"));
        h.Controller.Set(R.DiskCeilingPercent, "99");
        h.Controller.Set(R.PeriodDuration, "tomorrow");
        Assert.Equal(AdminDownloadFieldProblem.OutOfRange, h.Controller.State.Errors[R.DiskCeilingPercent].Problem);
        Assert.Equal(AdminDownloadFieldProblem.NotDuration, h.Controller.State.Errors[R.PeriodDuration].Problem);
        await h.Controller.SaveAsync();
        Assert.Empty(h.Writes);
        Assert.Equal(2, h.Controller.State.DirtyKeys.Count);
        h.Controller.Set(R.DiskCeilingPercent, "90"); h.Controller.Set(R.PeriodDuration, "7d");
        Assert.Empty(h.Controller.State.Errors);
        await h.Controller.SaveAsync();
        Assert.Single(h.Writes);
    }

    [Fact]
    public async Task LimitingDownloadsPerPeriodWithNoPositivePeriodIsBlockedOnThePeriod()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.Set(R.PeriodDuration, "0s");
        h.Controller.Set(R.MaxPerPeriod, "5");
        Assert.Equal(AdminDownloadFieldProblem.DurationNotPositive, h.Controller.State.Errors[R.PeriodDuration].Problem);
        h.Controller.Set(R.PeriodDuration, "24h"); // saved value again: the edit withdraws
        Assert.Empty(h.Controller.State.Errors);
        h.Controller.Set(R.PeriodDuration, "168h");
        Assert.Empty(h.Controller.State.Errors);
    }

    [Fact]
    public async Task ZeroUnlimitedAndInheritValuesAreWrittenVerbatim()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(Saved("""{"download.artifact_max_bytes":"0","download.user_bandwidth_mbps":"0","download.max_concurrent_prepares":"0","download.artifact_dir":""}"""));
        h.Controller.Set(R.ArtifactMaxBytes, "0");        // Unlimited
        h.Controller.Set(R.UserBandwidth, "0");           // Unlimited
        h.Controller.Set(R.MaxConcurrentPrepares, "0");   // "use the built-in default of 2", not unlimited
        h.Controller.Set(R.ArtifactDir, "/mnt/downloads");
        h.Controller.Set(R.ArtifactDir, "");              // back to the saved blank: no edit remains for it
        await h.Controller.SaveAsync();
        using var body = JsonDocument.Parse(h.LastBody);
        var values = body.RootElement.GetProperty("values").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        Assert.Equal("0", values[R.ArtifactMaxBytes]);
        Assert.Equal("0", values[R.UserBandwidth]);
        Assert.Equal("0", values[R.MaxConcurrentPrepares]);
        Assert.DoesNotContain(R.ArtifactDir, values.Keys);
    }

    [Fact]
    public async Task ResettingAnOverriddenDirectoryStagesTheBlankDefaultAndWritesIt()
    {
        using var h = new Harness { Effective = EffectiveJson.Replace("\"download.artifact_dir\":\"\"", "\"download.artifact_dir\":\"/mnt/downloads\"") };
        await h.Controller.OpenAsync();
        Assert.Equal("/mnt/downloads", h.Controller.State.Values[R.ArtifactDir]);
        h.Write = _ => Task.FromResult(Saved("""{"download.artifact_dir":""}""", restart: true));
        h.Controller.ResetArtifactDirectory();
        Assert.Equal([R.ArtifactDir], h.Controller.State.DirtyKeys);
        Assert.Equal("", h.Controller.State.Values[R.ArtifactDir]);
        await h.Controller.SaveAsync();
        Assert.Contains("\"download.artifact_dir\":\"\"", h.LastBody);
        Assert.Equal("", h.Controller.State.PersistedValues[R.ArtifactDir]);
        Assert.True(h.Controller.State.RestartRequired);
    }

    [Fact]
    public async Task ADirectoryEditNeverTouchesTheTranscodeDirectoryOrAnotherPagesKeys()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(Saved("""{"download.artifact_dir":"/srv/dl"}""", restart: true));
        h.Controller.Set(R.ArtifactDir, "/srv/dl");
        Assert.Throws<ArgumentException>(() => h.Controller.Set(R.TranscodeDir, "/elsewhere"));
        Assert.Throws<ArgumentException>(() => h.Controller.Set("email.smtp_host", "x"));
        await h.Controller.SaveAsync();
        Assert.DoesNotContain("playback.", h.LastBody);
        Assert.DoesNotContain("email.", h.LastBody);
        Assert.Equal("/mnt/fast/transcode", h.Controller.State.TranscodeDirectory);
    }

    [Fact]
    public async Task EnvironmentManagedKeysCannotBeEditedOrWritten()
    {
        using var h = new Harness { Sensitive = """{"configured":[],"managed_by_env":["download.artifact_dir","redis.url"]}""" };
        await h.Controller.OpenAsync();
        Assert.Equal([R.ArtifactDir], h.Controller.State.ManagedByEnv);
        h.Controller.Set(R.ArtifactDir, "/x");
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Controller.Set(R.CacheHours, "1");
        Assert.Equal([R.CacheHours], h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task UnreadableEnvironmentAndRestartStatusDoNotBlockEditingLikeTheOfficialPage()
    {
        using var h = new Harness { Sensitive = "fail", Restart = "fail" };
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.Loaded);
        Assert.Empty(h.Controller.State.ManagedByEnv);
        h.Controller.Set(R.CacheHours, "1");
        Assert.Equal([R.CacheHours], h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task FailedReadShowsNoEditableDefaultsAndRetryRecovers()
    {
        using var h = new Harness { EffectiveStatus = HttpStatusCode.ServiceUnavailable };
        await h.Controller.OpenAsync();
        var failed = h.Controller.State;
        Assert.False(failed.Loaded);
        Assert.NotNull(failed.Error);
        h.Controller.Set(R.CacheHours, "1");
        Assert.Empty(h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Empty(h.Writes);
        h.EffectiveStatus = HttpStatusCode.OK;
        await h.Controller.ReloadAsync(preserveDraft: false);
        Assert.True(h.Controller.State.Loaded);
        Assert.Null(h.Controller.State.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, AdminFailureKind.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized, AdminFailureKind.Unauthorized)]
    public async Task AccessFailuresAreClassifiedForTheirOwnRecoveryText(HttpStatusCode status, AdminFailureKind kind)
    {
        using var h = new Harness { EffectiveStatus = status };
        await h.Controller.OpenAsync();
        Assert.False(h.Controller.State.Loaded);
        Assert.Equal(kind, h.Controller.State.Failure);
    }

    [Fact]
    public async Task ConflictRetainsDraftBlocksSaveAndExplicitReloadReconciles()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json("""{"type":"precondition_failed","status":412}""", HttpStatusCode.PreconditionFailed));
        h.Controller.Set(R.CacheHours, "100");
        await h.Controller.SaveAsync();
        Assert.True(h.Controller.State.Conflict);
        await h.Controller.SaveAsync();
        Assert.Single(h.Writes);
        Assert.Equal("100", h.Controller.State.Values[R.CacheHours]);
        h.Effective = EffectiveJson.Replace("\"download.artifact_cache_hours\":\"72\"", "\"download.artifact_cache_hours\":\"96\"");
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.False(h.Controller.State.Conflict);
        Assert.Equal([R.CacheHours], h.Controller.State.DirtyKeys);
        Assert.Equal("96", h.Controller.State.PersistedValues[R.CacheHours]);
        Assert.Equal("100", h.Controller.State.Values[R.CacheHours]);
    }

    [Fact]
    public async Task UnknownSaveOutcomeIsNeverReplayedAndNeverInventsACleanValue()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromException<HttpResponseMessage>(new HttpRequestException());
        h.Controller.Set(R.ServerBandwidth, "500");
        await h.Controller.SaveAsync(); await h.Controller.SaveAsync();
        Assert.True(h.Controller.State.OutcomeUnknown);
        Assert.Single(h.Writes);
        Assert.Equal([R.ServerBandwidth], h.Controller.State.DirtyKeys);
        Assert.Equal("0", h.Controller.State.PersistedValues[R.ServerBandwidth]);
        Assert.DoesNotContain(h.Kit.Http.Requests, r => r.Contains("/auth/"));
    }

    [Fact]
    public async Task ServerValidationRefusalKeepsTheDraftAndIsNotRetried()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json("""{"type":"validation_failed","title":"download.artifact_dir must be an absolute path","status":422}""", HttpStatusCode.UnprocessableEntity));
        h.Controller.Set(R.ArtifactDir, "/ok/dir");
        await h.Controller.SaveAsync();
        Assert.Equal(AdminFailureKind.Validation, h.Controller.State.Failure);
        Assert.False(h.Controller.State.Saving);
        Assert.DoesNotContain("absolute", h.Controller.State.Error!);
        Assert.Equal([R.ArtifactDir], h.Controller.State.DirtyKeys);
        Assert.Single(h.Writes);
    }

    [Fact]
    public async Task LateEditsDuringASaveStayDirtyOverTheAcknowledgedCanonicalValue()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        h.Controller.Set(R.CacheHours, "100");
        var save = h.Controller.SaveAsync();
        Assert.True(h.Controller.State.Saving);
        h.Controller.Set(R.CacheHours, "200");
        pending.SetResult(Saved("""{"download.artifact_cache_hours":"100"}"""));
        await save;
        Assert.Equal("200", h.Controller.State.Values[R.CacheHours]);
        Assert.Equal("100", h.Controller.State.PersistedValues[R.CacheHours]);
        Assert.Equal([R.CacheHours], h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task DiscardWithdrawsEveryDraftAndIsIgnoredWhileSaving()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.Set(R.CacheHours, "100"); h.Controller.Set(R.Enabled, "false");
        h.Controller.Discard();
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("72", h.Controller.State.Values[R.CacheHours]);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        h.Controller.Set(R.CacheHours, "100");
        var save = h.Controller.SaveAsync();
        h.Controller.Discard();
        Assert.Equal([R.CacheHours], h.Controller.State.DirtyKeys);
        pending.SetResult(Saved("""{"download.artifact_cache_hours":"100"}"""));
        await save;
    }

    [Fact]
    public async Task SavedEventReportsTheWrittenKeysAndRestartRequirementSurfaces()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        IReadOnlyCollection<string>? saved = null;
        h.Controller.Saved += keys => saved = keys;
        h.Write = _ => Task.FromResult(Saved("""{"download.artifact_dir":"/srv/dl"}""", restart: true));
        h.Controller.Set(R.ArtifactDir, "/srv/dl");
        await h.Controller.SaveAsync();
        Assert.Equal([R.ArtifactDir], saved);
        Assert.True(h.Controller.State.RestartRequired);
    }

    [Fact]
    public async Task RetiredAuthorityClearsDraftsAndIgnoresALateReply()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        h.Controller.Set(R.CacheHours, "100");
        var save = h.Controller.SaveAsync();
        h.Authority.Retire();
        pending.SetResult(Saved("{}"));
        await save;
        Assert.Empty(h.Controller.State.Values);
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("", h.Controller.State.TranscodeDirectory);
        Assert.False(h.Controller.State.Loaded);
    }

    [Fact]
    public async Task SuspendingThePageClearsEverythingAndSavesNothing()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.Set(R.CacheHours, "100");
        h.Controller.Suspend();
        Assert.False(h.Controller.State.Loaded);
        await h.Controller.SaveAsync();
        Assert.Empty(h.Writes);
    }
}
