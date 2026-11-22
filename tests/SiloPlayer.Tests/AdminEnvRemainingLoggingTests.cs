using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Settings > Logging and a setting the server's environment fixes (official <c>LoggingSettings.tsx</c>: <c>managedByEnv={form.envVar("server.log_level")}</c>, and the draft rule of
/// <c>useSettingsForm</c>). The variable name is synthetic.
/// </summary>
public sealed class AdminEnvRemainingLoggingTests
{
    private const string Managed = """{"configured":[],"managed_by_env":["server.log_level"],"managed_by_env_vars":{"server.log_level":"SILO_FIXTURE_VALUE"}}""";
    private const string NotManaged = """{"configured":[],"managed_by_env":[],"managed_by_env_vars":{}}""";

    private sealed class Server : IDisposable
    {
        public readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        public readonly AdminMonitorAuthority Authority;
        public string Status = NotManaged;
        public bool StatusFails;
        public HttpStatusCode PutStatus = HttpStatusCode.OK;
        public readonly List<string> PutBodies = [];
        public int StatusReads;
        public Server()
        {
            Authority = new(Kit.Client, Kit.Auth);
            Kit.Http.Respond = async (request, ct) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method == HttpMethod.Put)
                {
                    PutBodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct));
                    if (PutStatus != HttpStatusCode.OK) return AdminMonitorKit.Json("""{"type":"https://siloserver.org/docs/api/v2/problems/validation_failed","status":422}""", PutStatus);
                    var saved = AdminMonitorKit.Json("""{"values":{"opslog.retention_days":"9"},"restart_required":false}""");
                    saved.Headers.ETag = new("\"two\"");
                    return saved;
                }
                if (path.EndsWith("/sensitive-status"))
                {
                    StatusReads++;
                    return StatusFails ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : AdminMonitorKit.Json(Status);
                }
                return AdminMonitorDiagnosticsControllerTests.Reply(path);
            };
        }
        public LoggingController Controller() => new(new(Kit.Client), Authority);
        public void Dispose() { Authority.Dispose(); Kit.Dispose(); }
    }

    [Fact]
    public async Task AManagedKeyIsNamedStaysReadOnlyAndIsNeverSentWhileAnUnmanagedEditBesideItSaves()
    {
        using var server = new Server { Status = Managed };
        using var controller = server.Controller();
        await controller.OpenAsync();

        Assert.True(controller.State.Loaded);
        Assert.True(controller.State.StatusKnown);
        Assert.True(controller.State.EnvManaged.IsManaged("server.log_level"));
        Assert.Equal("Set by SILO_FIXTURE_VALUE in the server’s environment.", controller.State.EnvManaged.Note("server.log_level"));
        Assert.Null(controller.State.EnvManaged.Note("opslog.retention_days"));

        controller.Set("server.log_level", "debug");
        Assert.Empty(controller.State.DirtyKeys);
        Assert.Equal("info", controller.State.Values["server.log_level"]);

        controller.Set("opslog.retention_days", "9");
        Assert.Equal(["opslog.retention_days"], controller.State.DirtyKeys);
        using var action = controller.CaptureAction();
        await controller.SaveAsync(action);

        Assert.Null(controller.State.Error);
        Assert.Empty(controller.State.DirtyKeys);
        var body = Assert.Single(server.PutBodies);
        Assert.Contains("opslog.retention_days", body);
        Assert.DoesNotContain("server.log_level", body);
    }

    [Fact]
    public async Task ADraftOfAKeyThatTheEnvironmentTookOverIsDroppedByTheRefusalAndTheRestStillSaves()
    {
        using var server = new Server();
        using var controller = server.Controller();
        await controller.OpenAsync();
        Assert.False(controller.State.EnvManaged.IsManaged("server.log_level"));
        controller.Set("server.log_level", "debug");
        controller.Set("opslog.retention_days", "9");
        Assert.Equal(2, controller.State.DirtyKeys.Count);

        // The server restarted with the variable set: it refuses the whole write, and the page reads the list again.
        server.Status = Managed;
        server.PutStatus = (HttpStatusCode)422;
        using var action = controller.CaptureAction();
        await controller.SaveAsync(action);
        Assert.Contains("server.log_level", server.PutBodies[0]);
        Assert.True(controller.State.EnvManaged.IsManaged("server.log_level"));
        Assert.Equal(["opslog.retention_days"], controller.State.DirtyKeys);
        Assert.Equal("info", controller.State.Values["server.log_level"]);
        Assert.False(controller.State.Saving);

        server.PutStatus = HttpStatusCode.OK;
        await controller.SaveAsync(action);
        Assert.Equal(2, server.PutBodies.Count);
        Assert.DoesNotContain("server.log_level", server.PutBodies[1]);
        Assert.Contains("opslog.retention_days", server.PutBodies[1]);
        Assert.Empty(controller.State.DirtyKeys);
    }

    [Fact]
    public async Task AFailedStatusReReadKeepsTheLastKnownLock()
    {
        using var server = new Server { Status = Managed };
        using var controller = server.Controller();
        await controller.OpenAsync();
        Assert.True(controller.State.EnvManaged.IsManaged("server.log_level"));

        server.StatusFails = true;
        var reads = server.StatusReads;
        controller.Set("opslog.retention_days", "9");
        using var action = controller.CaptureAction();
        await controller.SaveAsync(action);
        Assert.True(server.StatusReads > reads, "the save is followed by a status read");

        Assert.True(controller.State.EnvManaged.IsManaged("server.log_level"));
        Assert.Equal("Set by SILO_FIXTURE_VALUE in the server’s environment.", controller.State.EnvManaged.Note("server.log_level"));
        controller.Set("server.log_level", "debug");
        Assert.Empty(controller.State.DirtyKeys);
    }

    [Fact]
    public async Task AReloadThatFindsAKeyManagedDropsItsDraftAndAFailedReloadKeepsTheList()
    {
        using var server = new Server();
        using var controller = server.Controller();
        await controller.OpenAsync();
        controller.Set("server.log_level", "debug");
        controller.Set("opslog.retention_days", "9");

        server.Status = Managed;
        using var action = controller.CaptureAction();
        var latest = await controller.PrepareReconciliationAsync(action);
        Assert.NotNull(latest);
        controller.AcceptReconciliation(action, latest!, preserveDraft: true);
        Assert.Equal(["opslog.retention_days"], controller.State.DirtyKeys);
        Assert.True(controller.State.EnvManaged.IsManaged("server.log_level"));

        // A later reload whose status read fails does not release the key.
        server.StatusFails = true;
        latest = await controller.PrepareReconciliationAsync(action);
        Assert.NotNull(latest);
        controller.AcceptReconciliation(action, latest!, preserveDraft: true);
        Assert.True(controller.State.EnvManaged.IsManaged("server.log_level"));
        controller.Set("server.log_level", "warn");
        Assert.Equal(["opslog.retention_days"], controller.State.DirtyKeys);
    }

    [Fact]
    public async Task AStatusThatNeverAnsweredLeavesTheKeyEditableAsTheOfficialPageDoesAndOpeningAgainStartsFromNothing()
    {
        using var server = new Server { StatusFails = true };
        using var controller = server.Controller();
        await controller.OpenAsync();
        Assert.True(controller.State.Loaded);
        Assert.False(controller.State.StatusKnown);
        controller.Set("server.log_level", "debug");
        Assert.Equal(["server.log_level"], controller.State.DirtyKeys);

        // Opening the page again starts from nothing: a list from before never carries over.
        server.StatusFails = false; server.Status = Managed;
        await controller.OpenAsync();
        Assert.True(controller.State.EnvManaged.IsManaged("server.log_level"));
        server.StatusFails = true;
        await controller.OpenAsync();
        Assert.False(controller.State.StatusKnown);
        Assert.False(controller.State.EnvManaged.IsManaged("server.log_level"));
    }

    [Fact]
    public async Task AnOlderStatusAnswerIsIgnoredWhenANewerOneHasArrived()
    {
        using var server = new Server();
        using var controller = server.Controller();
        await controller.OpenAsync();
        // Two overlapping reads: the one started last wins, whatever order they finish in.
        var slow = new TaskCompletionSource<HttpResponseMessage>();
        var first = true;
        server.Kit.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.EndsWith("/sensitive-status")) return Task.FromResult(AdminMonitorDiagnosticsControllerTests.Reply(path));
            if (first) { first = false; return slow.Task; }
            return Task.FromResult(AdminMonitorKit.Json(Managed));
        };
        using var action = controller.CaptureAction();
        var older = controller.PrepareReconciliationAsync(action);
        await Task.Delay(50);
        var newer = controller.PrepareReconciliationAsync(action);
        await newer;
        Assert.True(controller.State.EnvManaged.IsManaged("server.log_level"));
        slow.SetResult(AdminMonitorKit.Json(NotManaged));
        await older;
        Assert.True(controller.State.EnvManaged.IsManaged("server.log_level"));
    }

    // ── The draft itself ──

    private static LoggingSettingsSnapshot Snapshot() => new(new Dictionary<string, string> { ["server.log_level"] = "info", ["opslog.retention_days"] = "7" }, "\"one\"");

    [Fact]
    public void TheDraftDropsAManagedKeysEditRefusesToStageItAgainAndNeverPutsItInASave()
    {
        var draft = new LoggingDraft();
        draft.Load(Snapshot());
        draft.Set("server.log_level", "debug");
        draft.Set("opslog.retention_days", "9");
        Assert.Equal(2, draft.DirtyKeys.Count);

        var dropped = draft.ApplyEnvironment(new AdminEnvManagedSettings(["server.log_level"], [new("server.log_level", "SILO_FIXTURE_VALUE")]));
        Assert.Equal(["server.log_level"], dropped);
        Assert.Equal("info", draft.Get("server.log_level"));
        Assert.Equal(["opslog.retention_days"], draft.DirtyKeys);
        draft.Set("server.log_level", "error");
        Assert.Equal("info", draft.Get("server.log_level"));
        Assert.Equal(["opslog.retention_days"], draft.DirtyKeys);

        var intent = draft.BeginSave();
        Assert.Equal(["opslog.retention_days"], intent.Values.Keys);
        Assert.Equal(["opslog.retention_days"], intent.Versions.Keys);

        // Released again, the key can be edited.
        draft.ApplyEnvironment(AdminEnvManagedSettings.None);
        draft.Set("server.log_level", "error");
        Assert.Contains("server.log_level", draft.DirtyKeys);
    }

    [Fact]
    public void AReloadThatKeepsDraftsStillDropsAManagedKeysDraft()
    {
        var draft = new LoggingDraft();
        draft.Load(Snapshot());
        draft.Set("server.log_level", "debug");
        draft.ApplyEnvironment(new AdminEnvManagedSettings(["server.log_level"], null));
        // The environment already holds the key when the settings arrive again: nothing is staged for it.
        draft.Load(Snapshot(), preserveDraft: true);
        Assert.Empty(draft.DirtyKeys);
        Assert.Equal("info", draft.Get("server.log_level"));
    }

    [Fact]
    public async Task TheStatusAnswerNeedsItsRequiredListsAndIsRestrictedToThePagesKeys()
    {
        using var server = new Server { Status = """{"configured":[],"managed_by_env":["server.log_level","server.public_url"],"managed_by_env_vars":{"server.log_level":"SILO_FIXTURE_VALUE","server.public_url":"SILO_OTHER"}}""" };
        using var lease = server.Authority.Capture();
        var environment = await new AdminMonitorApi(server.Kit.Client).LoggingGetEnvironmentAsync(lease);
        Assert.Equal(["server.log_level"], environment.Keys);
        Assert.False(environment.IsManaged("server.public_url"));

        server.Status = """{"items":[]}""";
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminMonitorApi(server.Kit.Client).LoggingGetEnvironmentAsync(lease));
    }
}
