using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using R = SiloPlayer.Core.Services.Admin.AdminDirectConnectionsRules;

namespace SiloPlayer.Tests;

/// <summary>
/// The Direct connections section's state owner (official <c>DirectConnectionsSection.tsx</c>, <c>useSettingsForm</c> and
/// <c>directConnections.ts</c>): when the section exists, what is requested and when, request-order rules, Run check outcomes and
/// the batched save. Synthetic hosts only.
/// </summary>
public sealed class AdminDirectConnectionsControllerTests
{
    private const string StatusRead = "GET /api/v2/admin/direct-connections/status";
    private const string StatusRefresh = "GET /api/v2/admin/direct-connections/status?refresh=true";

    private static string StatusJson(string directState = "on", string checkedAt = "2026-11-22T10:00:00.000Z") => $$"""
        {"enabled":true,"hosts":[{"host":{"id":"api","name":"API","role":"api"},"checked_at":"{{checkedAt}}",
         "features":[{"feature":"direct","kind":"local","state":"{{directState}}"}],"knobs":[]}]}
        """;

    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsApi Settings;
        internal readonly AdminServerSettingsStore Store;
        internal readonly DashboardManualTime Time = new();
        internal readonly AdminDirectConnectionsController Controller;
        internal string Enabled = "true", Port = "true", Stun = "stun.fixture.invalid:3478";
        internal string Sensitive = """{"configured":[],"managed_by_env":[],"managed_by_env_vars":{}}""";
        internal HttpStatusCode SettingsStatus = HttpStatusCode.OK;
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Status, Put;
        internal bool Defer;
        internal readonly List<string> Puts = [];

        internal Harness()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Settings = new AdminServerSettingsApi(Kit.Client);
            Store = new AdminServerSettingsStore(Settings, Authority, R.Keys, R.Secrets);
            Controller = new AdminDirectConnectionsController(Settings, Store, new AdminDirectConnectionsApi(Kit.Client), Authority, Time, () => Defer);
            Kit.Http.Respond = async (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/direct-connections/status")) return Status != null ? await Status(request) : AdminMonitorKit.Json(StatusJson());
                if (request.Method == HttpMethod.Put)
                {
                    var body = await request.Content!.ReadAsStringAsync();
                    lock (Puts) Puts.Add(body + " | If-Match " + request.Headers.IfMatch);
                    return Put != null ? await Put(request) : Saved(body);
                }
                var response = path.EndsWith("effective") ? AdminMonitorKit.Json($$"""
                        {"direct_connections.enabled":"{{Enabled}}","direct_connections.port_mapping":"{{Port}}","direct_connections.stun_servers":"{{Stun}}","branding.server_name":"Other page"}
                        """, SettingsStatus)
                    : path.EndsWith("sensitive-status") ? AdminMonitorKit.Json(Sensitive)
                    : AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
                response.Headers.ETag = new("\"one\"");
                return response;
            };
        }

        internal int Count(string request) => Kit.Http.Requests.Count(r => r == request);
        internal int Writes => Puts.Count;
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    /// <summary>Echoes the submitted values back as the canonical ones, as the server does.</summary>
    private static HttpResponseMessage Saved(string requestBody)
    {
        using var document = JsonDocument.Parse(requestBody);
        var values = document.RootElement.GetProperty("values").GetRawText();
        var response = AdminMonitorKit.Json($$"""{"values":{{values}},"restart_required":false}""");
        response.Headers.ETag = new("\"two\"");
        return response;
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string type, string? detail = null) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(new { type = "https://siloserver.org/docs/api/v2/problems/" + type, title = "Problem", status = (int)status, detail }), System.Text.Encoding.UTF8, "application/problem+json") };

    private static async Task Until(Func<bool> condition, string label)
    {
        for (var i = 0; i < 600 && !condition(); i++) await Task.Delay(5);
        Assert.True(condition(), "Timed out: " + label);
    }

    private static async Task<Harness> OpenedAsync(bool active = true, Action<Harness>? configure = null)
    {
        var h = new Harness();
        configure?.Invoke(h);
        h.Controller.SetActive(active);
        await h.Controller.OpenAsync();
        return h;
    }

    // ── when the section exists ──────────────────────────

    [Fact]
    public async Task ASectionSavedOffAndNothingStagedExistsNowhereAndSendsNoStatusRequest()
    {
        using var h = await OpenedAsync(configure: h => h.Enabled = "false");
        Assert.True(h.Controller.State.Loaded);
        Assert.False(h.Controller.State.SectionVisible);
        Assert.False(h.Controller.State.StatusLoading);
        h.Time.Advance(TimeSpan.FromSeconds(45));
        await Task.Delay(50);
        Assert.Equal(0, h.Count(StatusRead));
        Assert.Equal(0, h.Count(StatusRefresh));
    }

    [Fact]
    public async Task StagingASettingMakesTheSectionAppearAndReadAndDiscardingRemovesItAgain()
    {
        using var h = await OpenedAsync(configure: h => h.Enabled = "false");
        Assert.True(h.Controller.SetPortMapping(false));
        Assert.True(h.Controller.State.SectionVisible);
        await Until(() => h.Controller.State.Status != null, "status read");
        Assert.Equal(1, h.Count(StatusRead));
        h.Controller.Discard();
        Assert.False(h.Controller.State.SectionVisible);
        Assert.Null(h.Controller.State.Status);
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(50);
        Assert.Equal(1, h.Count(StatusRead));
    }

    [Fact]
    public async Task ASavedOnSectionReadsOnceOnOpenWithNoQueryAndNoBody()
    {
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "status read");
        Assert.True(h.Controller.State.SectionVisible);
        Assert.Equal(1, h.Count(StatusRead));
        Assert.Equal(0, h.Count(StatusRefresh));
        Assert.Equal("on", h.Controller.State.Status!.Hosts.Single().Features.Single().State);
        Assert.All(h.Kit.Http.Bodies, body => Assert.Equal("", body));
    }

    [Fact]
    public async Task TurningTheSavedSwitchOffAndSavingRemovesTheSectionAndStopsReading()
    {
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "status read");
        Assert.True(h.Controller.SetEnabled(false));
        Assert.True(h.Controller.State.SectionVisible, "the draft keeps the section reachable");
        await h.Controller.SaveAsync();
        Assert.Equal(["{\"values\":{\"direct_connections.enabled\":\"false\"}} | If-Match \"one\""], h.Puts);
        Assert.False(h.Controller.State.SectionVisible);
        Assert.Null(h.Controller.State.Status);
        var reads = h.Count(StatusRead);
        h.Time.Advance(TimeSpan.FromSeconds(45));
        await Task.Delay(50);
        Assert.Equal(reads, h.Count(StatusRead));
    }

    [Fact]
    public async Task AFailedSettingsReadIsVisibleAndAReloadRecovers()
    {
        using var h = await OpenedAsync(configure: h => h.SettingsStatus = HttpStatusCode.ServiceUnavailable);
        Assert.False(h.Controller.State.Loaded);
        Assert.NotNull(h.Controller.State.LoadFailure);
        Assert.False(h.Controller.State.SectionVisible);
        h.SettingsStatus = HttpStatusCode.OK;
        await h.Controller.ReloadAsync(false);
        Assert.Null(h.Controller.State.LoadFailure);
        Assert.True(h.Controller.State.SectionVisible);
        await Until(() => h.Controller.State.Status != null, "status read");
    }

    // ── polling ──────────────────────────────────────────

    [Fact]
    public async Task ItPollsEveryFifteenSecondsOnlyWhileActive()
    {
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        h.Time.Advance(TimeSpan.FromSeconds(14));
        await Task.Delay(30);
        Assert.Equal(1, h.Count(StatusRead));
        h.Time.Advance(TimeSpan.FromSeconds(1));
        await Until(() => h.Count(StatusRead) == 2, "first poll");
        // The request is out; wait until its answer is applied, so activation sees a finished read.
        for (long seen = -1; seen != h.Controller.State.Revision;) { seen = h.Controller.State.Revision; await Task.Delay(120); }
        h.Controller.SetActive(false);
        Assert.False(h.Controller.IsPolling);
        h.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(30);
        Assert.Equal(2, h.Count(StatusRead));
        // Activation reads again only when the last read is a full interval old.
        h.Controller.SetActive(true);
        await Until(() => h.Count(StatusRead) == 3, "activation read");
        Assert.Equal(0, h.Count(StatusRefresh));
    }

    [Fact]
    public async Task APollIsSkippedWhilePlaybackStartKeepsTheNetwork()
    {
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        h.Defer = true;
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await Task.Delay(30);
        Assert.Equal(1, h.Count(StatusRead));
        h.Defer = false;
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => h.Count(StatusRead) == 2, "next tick");
    }

    [Fact]
    public async Task AFailedPollKeepsTheDataOnScreenAndKeepsPolling()
    {
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        h.Status = _ => Task.FromResult(AdminMonitorKit.Json("""{"title":"down"}""", HttpStatusCode.ServiceUnavailable));
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => h.Controller.State.StatusFailure != null, "failed poll");
        Assert.NotNull(h.Controller.State.Status);
        Assert.False(h.Controller.State.StatusLoading);
        h.Status = null;
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => h.Controller.State.StatusFailure == null, "recovered poll");
        Assert.Equal(3, h.Count(StatusRead));
    }

    [Fact]
    public async Task AFailedFirstReadIsVisibleKeepsPollingAndHasAReachableRetry()
    {
        using var h = await OpenedAsync(configure: h => h.Status = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable)));
        await Until(() => h.Controller.State.StatusFailure != null, "failed read");
        Assert.Null(h.Controller.State.Status);
        Assert.False(h.Controller.State.StatusLoading);
        h.Status = null;
        h.Controller.RetryStatus();
        await Until(() => h.Controller.State.Status != null, "retry");
        Assert.Null(h.Controller.State.StatusFailure);
        Assert.Equal(0, h.Count(StatusRefresh));
    }

    [Fact]
    public async Task APollThatStartedBeforeARunCheckResultWasPublishedIsDropped()
    {
        var oldPoll = new TaskCompletionSource<HttpResponseMessage>();
        var refresh = new TaskCompletionSource<HttpResponseMessage>();
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        var reads = 0;
        h.Status = request =>
        {
            if (request.RequestUri!.Query.Contains("refresh")) return refresh.Task;
            return Interlocked.Increment(ref reads) == 1 ? oldPoll.Task : Task.FromResult(AdminMonitorKit.Json(StatusJson("on")));
        };
        var run = h.Controller.RunCheckAsync();
        // A poll that starts while the check runs asks before the check's result exists.
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => reads == 1, "poll started");
        refresh.SetResult(AdminMonitorKit.Json(StatusJson("capped", "2026-11-22T10:00:20.000Z")));
        await run;
        Assert.Equal("capped", h.Controller.State.Status!.Hosts.Single().Features.Single().State);
        // The poll answers last, with an older check time and a different state: request order says it is older than the cache.
        oldPoll.SetResult(AdminMonitorKit.Json(StatusJson("off", "2026-11-22T09:00:00.000Z")));
        await Task.Delay(80);
        Assert.Equal("capped", h.Controller.State.Status!.Hosts.Single().Features.Single().State);
        Assert.Equal(1, h.Count(StatusRefresh));
    }

    [Fact]
    public async Task APollInFlightWhenRunCheckStartsIsSupersededAndRaisesNothingLate()
    {
        var oldPoll = new TaskCompletionSource<HttpResponseMessage>();
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        var reads = 0;
        h.Status = request => request.RequestUri!.Query.Contains("refresh") ? Task.FromResult(AdminMonitorKit.Json(StatusJson("capped")))
            : Interlocked.Increment(ref reads) == 1 ? oldPoll.Task : Task.FromResult(AdminMonitorKit.Json(StatusJson("on")));
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => reads == 1, "poll started");
        await h.Controller.RunCheckAsync();
        // Its late failure is not an error: it was superseded.
        oldPoll.SetResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError));
        await Task.Delay(80);
        Assert.Null(h.Controller.State.StatusFailure);
        Assert.Equal("capped", h.Controller.State.Status!.Hosts.Single().Features.Single().State);
    }

    [Fact]
    public async Task APollResultWithAnOlderCheckTimeStillReplacesAnEarlierPollResult()
    {
        // Request order decides, not the server's clock: no refresh was published in between.
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        h.Status = _ => Task.FromResult(AdminMonitorKit.Json(StatusJson("degraded", "2026-11-22T08:00:00.000Z")));
        h.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => h.Controller.State.Status!.Hosts.Single().Features.Single().State == "degraded", "later poll");
    }

    // ── Run check ────────────────────────────────────────

    [Fact]
    public async Task RunCheckSendsOneRefreshAndTheResultReplacesTheStatus()
    {
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        h.Status = request => request.RequestUri!.Query.Contains("refresh") ? gate.Task : Task.FromResult(AdminMonitorKit.Json(StatusJson("on")));
        var run = h.Controller.RunCheckAsync();
        Assert.True(h.Controller.State.Checking);
        gate.SetResult(AdminMonitorKit.Json(StatusJson("fallback")));
        await run;
        Assert.False(h.Controller.State.Checking);
        Assert.Equal("fallback", h.Controller.State.Status!.Hosts.Single().Features.Single().State);
        Assert.Equal(AdminDirectConnectionsCheckOutcome.None, h.Controller.State.CheckOutcome);
        Assert.Equal(1, h.Count(StatusRefresh));
    }

    [Fact]
    public async Task ASecondRunCheckCannotStartWhileOneIsRunning()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        h.Status = request => request.RequestUri!.Query.Contains("refresh") ? gate.Task : Task.FromResult(AdminMonitorKit.Json(StatusJson()));
        var first = h.Controller.RunCheckAsync();
        await h.Controller.RunCheckAsync();
        gate.SetResult(AdminMonitorKit.Json(StatusJson("capped")));
        await first;
        Assert.Equal(1, h.Count(StatusRefresh));
    }

    [Fact]
    public async Task ARefusedRunCheckShowsTheServersDetailAndKeepsTheStatus()
    {
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        h.Status = request => Task.FromResult(request.RequestUri!.Query.Contains("refresh")
            ? Problem(HttpStatusCode.Forbidden, "permission_denied", "Only the acting administrator may run this check.") : AdminMonitorKit.Json(StatusJson()));
        await h.Controller.RunCheckAsync();
        var state = h.Controller.State;
        Assert.Equal(AdminDirectConnectionsCheckOutcome.Refused, state.CheckOutcome);
        Assert.Equal("Only the acting administrator may run this check.", state.CheckDetail);
        Assert.Equal(AdminFailureKind.Forbidden, state.CheckFailure);
        Assert.NotNull(state.Status);
        Assert.False(state.Checking);
        Assert.Equal(1, h.Count(StatusRefresh));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task AnUnknownRunCheckOutcomeIsNeverResentAndTheStateIsReadAgain(HttpStatusCode status)
    {
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        h.Status = request => Task.FromResult(request.RequestUri!.Query.Contains("refresh")
            ? AdminMonitorKit.Json("""{"title":"timed out"}""", status) : AdminMonitorKit.Json(StatusJson("fallback")));
        await h.Controller.RunCheckAsync();
        Assert.Equal(AdminDirectConnectionsCheckOutcome.Unknown, h.Controller.State.CheckOutcome);
        await Until(() => h.Count(StatusRead) == 2, "re-read");
        await Until(() => h.Controller.State.Status!.Hosts.Single().Features.Single().State == "fallback", "re-read applied");
        Assert.Equal(1, h.Count(StatusRefresh));
        // A new check clears the old message.
        h.Status = _ => Task.FromResult(AdminMonitorKit.Json(StatusJson("on")));
        await h.Controller.RunCheckAsync();
        Assert.Equal(AdminDirectConnectionsCheckOutcome.None, h.Controller.State.CheckOutcome);
    }

    [Fact]
    public async Task AStaleRunCheckAnswerIsDiscardedWhenThePageDepartsOrTheSectionGoes()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        h.Status = request => request.RequestUri!.Query.Contains("refresh") ? gate.Task : Task.FromResult(AdminMonitorKit.Json(StatusJson()));
        var run = h.Controller.RunCheckAsync();
        h.Controller.Suspend();
        gate.SetResult(AdminMonitorKit.Json(StatusJson("capped")));
        await run;
        Assert.Null(h.Controller.State.Status);
        Assert.False(h.Controller.State.Loaded);
        Assert.Equal(AdminDirectConnectionsCheckOutcome.None, h.Controller.State.CheckOutcome);
    }

    [Fact]
    public async Task ARunCheckAnswerForASectionThatWentAwayIsDiscarded()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        using var h = await OpenedAsync(configure: h => h.Enabled = "false");
        h.Controller.SetPortMapping(false);
        await Until(() => h.Controller.State.Status != null, "first read");
        h.Status = request => request.RequestUri!.Query.Contains("refresh") ? gate.Task : Task.FromResult(AdminMonitorKit.Json(StatusJson()));
        var run = h.Controller.RunCheckAsync();
        h.Controller.Discard();
        gate.SetResult(AdminMonitorKit.Json(StatusJson("capped")));
        await run;
        Assert.False(h.Controller.State.SectionVisible);
        Assert.Null(h.Controller.State.Status);
        Assert.False(h.Controller.State.Checking);
    }

    // ── the batched settings form ────────────────────────

    [Fact]
    public async Task AnUntouchedFormSendsNothingAndTheDraftStartsFromTheRawSavedValues()
    {
        using var h = await OpenedAsync(configure: h => h.Stun = "STUN.Fixture.Invalid:3478, a:b");
        var state = h.Controller.State;
        Assert.Equal("STUN.Fixture.Invalid:3478, a:b", state.Values[R.StunServers]);
        Assert.Empty(state.DirtyKeys);
        Assert.False(state.CanSave);
        // A saved value that fails the client check stays in the field, is not checked and is not sent.
        Assert.True(state.Stun.IsValid);
        await h.Controller.SaveAsync();
        Assert.Equal(0, h.Writes);
    }

    [Fact]
    public async Task OnlyTheEditedKeysAreSentWithTheValidatorAndEditingBackUndoesTheEdit()
    {
        using var h = await OpenedAsync();
        h.Controller.SetPortMapping(false);
        Assert.Equal([R.PortMapping], h.Controller.State.DirtyKeys);
        h.Controller.SetPortMapping(true);
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Controller.SetPortMapping(false);
        h.Controller.SetStunServers("STUN.fixture.invalid:3478 , 192.0.2.1:19302");
        Assert.True(h.Controller.State.CanSave);
        await h.Controller.SaveAsync();
        var sent = JsonDocument.Parse(h.Puts.Single().Split(" | ")[0]).RootElement.GetProperty("values");
        Assert.Equal(["direct_connections.port_mapping", "direct_connections.stun_servers"], sent.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("false", sent.GetProperty("direct_connections.port_mapping").GetString());
        Assert.Equal("STUN.fixture.invalid:3478 , 192.0.2.1:19302", sent.GetProperty("direct_connections.stun_servers").GetString());
        Assert.EndsWith("If-Match \"one\"", h.Puts.Single());
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("false", h.Controller.State.PersistedValues[R.PortMapping]);
    }

    [Fact]
    public async Task AnInvalidStunDraftBlocksSaveWithTheServersMessageAndKeepsTheText()
    {
        using var h = await OpenedAsync();
        h.Controller.SetStunServers("stun.fixture.invalid");
        var state = h.Controller.State;
        Assert.False(state.Stun.IsValid);
        Assert.False(state.CanSave);
        Assert.Equal("stun.fixture.invalid", state.Values[R.StunServers]);
        await h.Controller.SaveAsync();
        Assert.Equal(0, h.Writes);
        h.Controller.SetStunServers("a:1,b:1,c:1,d:1,e:1,f:1,g:1,h:1,i:1");
        Assert.Equal(AdminStunProblem.TooMany, h.Controller.State.Stun.Problem);
        h.Controller.SetStunServers("stun.fixture.invalid:3478");
        Assert.True(h.Controller.State.Stun.IsValid);
        Assert.False(h.Controller.State.CanSave, "equal to the saved value: nothing to save");
    }

    [Fact]
    public async Task AnEmptiedStunListIsAStagedClearNotARemovedKey()
    {
        using var h = await OpenedAsync();
        h.Controller.SetStunServers("");
        Assert.Equal([R.StunServers], h.Controller.State.DirtyKeys);
        Assert.True(h.Controller.State.CanSave);
        await h.Controller.SaveAsync();
        Assert.StartsWith("{\"values\":{\"direct_connections.stun_servers\":\"\"}}", h.Puts.Single());
    }

    [Fact]
    public async Task ARefusedSaveShowsTheServersDetailKeepsTheDraftAndAllowsACorrectedRetry()
    {
        using var h = await OpenedAsync();
        h.Put = _ => Task.FromResult(Problem(HttpStatusCode.BadRequest, "validation_failed", "direct_connections.stun_servers requires host:port entries: address x: missing port in address"));
        h.Controller.SetStunServers("x:1234");
        await h.Controller.SaveAsync();
        var state = h.Controller.State;
        Assert.Equal("direct_connections.stun_servers requires host:port entries: address x: missing port in address", state.SaveDetail);
        Assert.Equal(AdminFailureKind.Validation, state.SaveFailure);
        Assert.False(state.OutcomeUnknown);
        Assert.Equal("x:1234", state.Values[R.StunServers]);
        Assert.Equal([R.StunServers], state.DirtyKeys);
        Assert.True(state.CanSave);
        h.Put = null;
        h.Controller.SetStunServers("stun2.fixture.invalid:3478");
        Assert.Null(h.Controller.State.SaveDetail);
        await h.Controller.SaveAsync();
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal(2, h.Writes);
    }

    [Fact]
    public async Task ATimedOutSaveIsAnUnknownOutcomeThatBlocksWritesUntilTheStateIsReadAgain()
    {
        using var h = await OpenedAsync();
        h.Put = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.GatewayTimeout));
        h.Controller.SetPortMapping(false);
        await h.Controller.SaveAsync();
        Assert.True(h.Controller.State.OutcomeUnknown);
        Assert.False(h.Controller.State.CanSave);
        Assert.Equal([R.PortMapping], h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(1, h.Writes);
        h.Port = "false";
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.False(h.Controller.State.OutcomeUnknown);
        // The draft survives the reload for review; the baseline now shows what the server holds.
        Assert.Equal([R.PortMapping], h.Controller.State.DirtyKeys);
        Assert.Equal("false", h.Controller.State.PersistedValues[R.PortMapping]);
        Assert.Equal(1, h.Writes);
    }

    [Fact]
    public async Task AStaleValidatorIsAConflictThatKeepsTheDraft()
    {
        using var h = await OpenedAsync();
        h.Put = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.PreconditionFailed) { Content = new StringContent("{}") });
        h.Controller.SetPortMapping(false);
        await h.Controller.SaveAsync();
        Assert.True(h.Controller.State.Conflict);
        Assert.False(h.Controller.State.CanSave);
        Assert.Equal([R.PortMapping], h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task AKeyTheEnvironmentSetsIsNeverStagedOrSentAndShowsItsVariable()
    {
        using var h = await OpenedAsync(configure: h => h.Sensitive = """{"configured":[],"managed_by_env":["direct_connections.enabled"],"managed_by_env_vars":{"direct_connections.enabled":"SILO_FIXTURE_DIRECT"}}""");
        Assert.True(h.Controller.State.EnvManaged.IsManaged(R.Enabled));
        Assert.Equal("Set by SILO_FIXTURE_DIRECT in the server’s environment.", h.Controller.State.EnvManaged.Note(R.Enabled));
        Assert.False(h.Controller.SetEnabled(false));
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Controller.SetPortMapping(false);
        await h.Controller.SaveAsync();
        Assert.DoesNotContain("direct_connections.enabled", h.Puts.Single());
    }

    [Fact]
    public async Task AnEditsIsRefusedWhileASaveRunsAndAfterTheOwnerRetires()
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        using var h = await OpenedAsync();
        h.Put = _ => gate.Task;
        h.Controller.SetPortMapping(false);
        var save = h.Controller.SaveAsync();
        await Until(() => h.Controller.State.Saving, "saving");
        Assert.False(h.Controller.SetStunServers("stun2.fixture.invalid:3478"));
        gate.SetResult(Saved("""{"values":{"direct_connections.port_mapping":"false"}}"""));
        await save;
        h.Controller.Suspend();
        Assert.False(h.Controller.SetPortMapping(true));
    }

    [Fact]
    public async Task ReopeningNeverInheritsAnotherSessionsStatusOrDraft()
    {
        using var h = await OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first read");
        h.Controller.SetPortMapping(false);
        h.Controller.Suspend();
        Assert.Null(h.Controller.State.Status);
        Assert.Empty(h.Controller.State.DirtyKeys);
        await h.Controller.OpenAsync();
        Assert.Empty(h.Controller.State.DirtyKeys);
        await Until(() => h.Controller.State.Status != null, "read after reopening");
    }
}
