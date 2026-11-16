using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using F = SiloPlayer.Core.Models.Admin.AdminRateLimitFields;

namespace SiloPlayer.Tests;

/// <summary>
/// Security settings (official SecurityAccessSettings.tsx + hooks/queries/admin/rateLimits.ts and the
/// apiv2 rate-limit handlers at c11b0a96), on synthetic data only.
/// </summary>
public sealed class AdminSecuritySettingsTests
{
    [Fact]
    public async Task ReopeningRefreshesCachedLocalNetworkAndValidator()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        Assert.Equal("false", h.Controller.State.Values[LocalNetwork]);
        h.Controller.Suspend();
        h.Effective = """{"media_servers.allow_private_destinations":"true"}""";
        h.SettingsETag = "\"fresh\"";
        await h.Controller.OpenAsync();
        Assert.Equal("true", h.Controller.State.Values[LocalNetwork]);
        string? validator = null;
        h.Put = request => { validator = request.Headers.IfMatch.ToString(); return Task.FromResult(Saved("""{"media_servers.allow_private_destinations":"false"}""")); };
        h.Controller.SetLocalNetwork(false);
        await h.Controller.SaveAsync();
        Assert.Equal("\"fresh\"", validator);
    }

    [Fact]
    public async Task SettingsReadFailureDisablesRateEditingUntilSettingsReload()
    {
        using var h = new Harness { SettingsStatus = HttpStatusCode.ServiceUnavailable };
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.RateLoaded);
        Assert.False(h.Controller.State.RateEditable);
        Assert.False(h.Controller.SetRate(F.Enabled, false));
        Assert.Equal(0, h.Controller.State.DirtyCount);
        h.SettingsStatus = HttpStatusCode.OK;
        await h.Controller.ReloadAsync(false);
        Assert.True(h.Controller.State.RateEditable);
        Assert.True(h.Controller.SetRate(F.Enabled, false));
        Assert.True(h.Controller.State.CanSave);
    }

    private const string LocalNetwork = AdminSecuritySettingsController.LocalNetwork;

    private static string Config(double global = 1000, string backend = "memory", double ipBurst = 120, double standardBurst = 20,
        bool enabled = true, string quota = """{"mode":"log","requests_per_hour":10000,"burst":30000}""") => """
        {"enabled":@ENABLED@,"backend":"@BACKEND@","global_requests_per_second":@GLOBAL@,
         "tiers":{"standard":{"requests_per_second":20.5,"requests_per_minute":1200,"burst":@SBURST@},"elevated":{"requests_per_second":100,"requests_per_minute":6000,"burst":100}},
         "ip_requests_per_second":120,"ip_requests_per_minute":6000,"ip_burst":@IPBURST@,
         "auth_endpoints":{"login":{"requests_per_minute":20,"burst":10},"signup":{"requests_per_minute":10,"burst":6},"setup":{"requests_per_minute":10,"burst":6},
           "password_change":{"requests_per_minute":10,"burst":5},"device_start":{"requests_per_minute":20,"burst":10},"device_lookup":{"requests_per_minute":60,"burst":20},
           "device_poll":{"requests_per_minute":120,"burst":30},"autoscan_webhook":{"requests_per_minute":60,"burst":30},
           "invitation":{"requests_per_minute":20,"burst":10},"password_reset":{"requests_per_minute":20,"burst":10},"password_reset_request":{"requests_per_minute":5,"burst":3},
           "apple_push_retire":{"requests_per_minute":20,"burst":10},"android_push_retire":{"requests_per_minute":20,"burst":10}},
         "session_quota":@QUOTA@}
        """.Replace("@ENABLED@", enabled ? "true" : "false").Replace("@BACKEND@", backend).Replace("@QUOTA@", quota)
        .Replace("@GLOBAL@", N(global)).Replace("@SBURST@", N(standardBurst)).Replace("@IPBURST@", N(ipBurst));
    private static string N(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsApi Api;
        internal readonly AdminServerSettingsStore Store;
        internal readonly AdminRateLimitApi RateApi;
        internal readonly AdminSecuritySettingsController Controller;
        internal string Effective = """{"media_servers.allow_private_destinations":"false","ratelimit.enabled":"true","branding.server_name":"Other page"}""";
        internal string RateConfig = Config();
        internal HttpStatusCode SettingsStatus = HttpStatusCode.OK;
        internal string SettingsETag = "\"one\"";
        internal string? RateETag = "\"rate-1\"";
        internal string RateStatus = """{"active":true,"active_backend":"memory","redis_available":true}""";
        internal HttpStatusCode ConfigStatus = HttpStatusCode.OK, StatusStatus = HttpStatusCode.OK;
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Put, Patch;
        internal readonly List<string> Order = [];
        internal readonly List<HttpRequestMessage> Patches = [];
        internal readonly List<string> PatchBodies = [];
        internal Harness()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Api = new AdminServerSettingsApi(Kit.Client);
            Store = new AdminServerSettingsStore(Api, Authority, AdminSecuritySettingsController.Keys, AdminSecuritySettingsController.Secrets);
            RateApi = new AdminRateLimitApi(Kit.Client);
            Controller = new AdminSecuritySettingsController(Api, Store, RateApi, Authority);
            Kit.Http.Respond = async (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                lock (Order) Order.Add($"{request.Method} {path}");
                if (request.Method == HttpMethod.Put) return Put != null ? await Put(request) : Saved("""{"media_servers.allow_private_destinations":"true"}""");
                if (request.Method == HttpMethod.Patch)
                {
                    var patchBody = await request.Content!.ReadAsStringAsync();
                    lock (Order) { Patches.Add(request); PatchBodies.Add(patchBody); }
                    return Patch != null ? await Patch(request) : AdminMonitorKit.Json("""{"status":"ok","restart_required":false}""");
                }
                if (path.EndsWith("/rate-limits/config"))
                {
                    var response = AdminMonitorKit.Json(ConfigStatus == HttpStatusCode.OK ? RateConfig : """{"title":"down"}""", ConfigStatus);
                    if (RateETag != null) response.Headers.TryAddWithoutValidation("ETag", RateETag);
                    return response;
                }
                if (path.EndsWith("/rate-limits/status")) return AdminMonitorKit.Json(StatusStatus == HttpStatusCode.OK ? RateStatus : """{"title":"down"}""", StatusStatus);
                var settings = path.EndsWith("effective") ? AdminMonitorKit.Json(Effective, SettingsStatus)
                    : path.EndsWith("sensitive-status") ? AdminMonitorKit.Json("""{"configured":[],"managed_by_env":[]}""")
                    : AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
                settings.Headers.ETag = new(SettingsETag);
                return settings;
            };
        }
        internal string[] Writes { get { lock (Order) return Order.Where(r => !r.StartsWith("GET")).ToArray(); } }
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private static HttpResponseMessage Saved(string values, string etag = "\"two\"")
    {
        var response = AdminMonitorKit.Json($$"""{"values":{{values}},"restart_required":false}""");
        response.Headers.ETag = new(etag);
        return response;
    }
    private static HttpResponseMessage Problem(HttpStatusCode status, string type, string? detail = null) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(new { type = "https://siloserver.org/docs/api/v2/problems/" + type, title = "Problem", status = (int)status, detail }), System.Text.Encoding.UTF8, "application/problem+json") };

    // ── Ownership and wire ───────────────────────────────

    [Fact]
    public async Task TheFormOwnsOnlyTheLocalNetworkKeyAndRateKeysNeverReachTheBatchedPut()
    {
        using var h = new Harness();
        Assert.Equal([LocalNetwork], AdminSecuritySettingsController.Keys);
        Assert.Empty(AdminSecuritySettingsController.Secrets);
        using var lease = h.Authority.Capture();
        await Assert.ThrowsAsync<ArgumentException>(() => h.Api.PutAsync(lease, new Dictionary<string, string> { ["ratelimit.enabled"] = "false" },
            "\"one\"", AdminSecuritySettingsController.Keys, AdminSecuritySettingsController.Secrets));
        Assert.Empty(h.Kit.Http.Requests);
    }

    [Fact]
    public async Task OpenMergesConfigAndStatusAndKeepsOnlyTheEightVisibleEndpoints()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var state = h.Controller.State;
        Assert.True(state.Loaded && state.RateLoaded && !state.RateLoading);
        Assert.Contains("GET /api/v2/admin/rate-limits/config", h.Order);
        Assert.Contains("GET /api/v2/admin/rate-limits/status", h.Order);
        Assert.Equal(31, state.RateValues!.Paths.Count());
        Assert.DoesNotContain(state.RateValues.Paths, path => path.Contains("invitation") || path.Contains("push_retire") || path.Contains("password_reset"));
        Assert.Equal(20.5, state.RateValues.Number(F.Tier("standard", "requests_per_second")));
        Assert.Equal("20.5", AdminRateLimitNumber.Format(state.RateValues.Number(F.Tier("standard", "requests_per_second"))));
        Assert.Equal("1000", AdminRateLimitNumber.Format(state.RateValues.Number(F.Global)));
        Assert.True(state.RateSnapshot is { Active: true, ActiveBackend: "memory", RedisAvailable: true, ETag: "\"rate-1\"" });
        Assert.Equal(["false"], state.Values.Values);
        Assert.Equal(0, state.DirtyCount);
    }

    [Theory]
    [InlineData("W/\"rate-1\"")]
    [InlineData("*")]
    [InlineData(null)]
    public async Task AWeakWildcardOrMissingValidatorFailsTheRateGroupWithoutEditableDefaults(string? etag)
    {
        using var h = new Harness { RateETag = etag };
        await h.Controller.OpenAsync();
        var state = h.Controller.State;
        Assert.Null(state.RateValues);
        Assert.False(state.RateEditable);
        Assert.Equal(AdminRateLimitMessage.ReloadBeforeEditing, state.RateMessage);
        Assert.False(h.Controller.SetRate(F.Enabled, false));
        // The independent Local network group stays usable and saves alone.
        h.Controller.SetLocalNetwork(true);
        Assert.Equal(1, h.Controller.State.DirtyCount);
        Assert.True(h.Controller.State.CanSave);
        await h.Controller.SaveAsync();
        Assert.Equal(["PUT /api/v2/admin/settings"], h.Writes);
        Assert.Equal(0, h.Controller.State.DirtyCount);
    }

    [Fact]
    public async Task AStatusFailureFailsTheWholeReadUntilAnExplicitRetrySucceeds()
    {
        using var h = new Harness { StatusStatus = HttpStatusCode.ServiceUnavailable };
        await h.Controller.OpenAsync();
        Assert.Null(h.Controller.State.RateValues);
        Assert.Equal(AdminRateLimitMessage.LoadFailed, h.Controller.State.RateMessage);
        Assert.Equal(AdminFailureKind.Server, h.Controller.State.RateFailure);
        h.StatusStatus = HttpStatusCode.OK;
        await h.Controller.ReloadRateAsync();
        Assert.True(h.Controller.State.RateLoaded && h.Controller.State.RateEditable);
        Assert.Equal(AdminRateLimitMessage.None, h.Controller.State.RateMessage);
    }

    [Fact]
    public void AbsentMembersHydrateTheOfficialDefaultsAndMalformedMembersFail()
    {
        using var sparse = JsonDocument.Parse("""{"enabled":false,"backend":"","global_requests_per_second":50}""");
        var values = AdminRateLimitValues.FromConfig(sparse.RootElement);
        Assert.False(values.Bool(F.Enabled));
        Assert.Equal("memory", values.Text(F.Backend));
        Assert.Equal(50, values.Number(F.Global));
        Assert.Equal(20, values.Number(F.Endpoint("login", "requests_per_minute")));
        Assert.Equal(5, values.Number(F.Endpoint("password_change", "burst")));
        Assert.Equal(1200, values.Number(F.Tier("standard", "requests_per_minute")));
        Assert.Equal(("log", 10000d, 30000d), (values.Text(F.QuotaMode), values.Number(F.QuotaPerHour), values.Number(F.QuotaBurst)));
        foreach (var json in new[] { """{"backend":"memory","global_requests_per_second":5}""", """{"enabled":true,"global_requests_per_second":"5"}""",
                     """{"enabled":true,"global_requests_per_second":5,"ip_burst":1.5}""", """{"enabled":true,"global_requests_per_second":5,"session_quota":{"mode":"sometimes","requests_per_hour":1,"burst":1}}""",
                     """{"enabled":true,"global_requests_per_second":5,"backend":"disk"}""" })
        {
            using var bad = JsonDocument.Parse(json);
            Assert.Throws<InvalidDataException>(() => AdminRateLimitValues.FromConfig(bad.RootElement));
        }
    }

    [Theory]
    [InlineData("", false, 0)]
    [InlineData("0", false, 0)]
    [InlineData("-5", false, 0)]
    [InlineData("１２", false, 0)]
    [InlineData("12abc", false, 0)]
    [InlineData("1.9", false, 0)]
    [InlineData("1e3", false, 0)]
    [InlineData("99999999999999", false, 0)]
    [InlineData("2147483648", false, 0)]
    [InlineData("2147483647", true, 2147483647)]
    [InlineData(" 12 ", true, 12)]
    [InlineData("+7", true, 7)]
    [InlineData("05", true, 5)]
    public void NumberEntryAcceptsOnlyPlainAsciiIntegersWithinTheServerBound(string raw, bool valid, double expected)
    {
        Assert.Equal(valid, AdminRateLimitNumber.TryParse(raw, F.MaxRate, out var value));
        Assert.Equal(expected, value);
        Assert.False(AdminRateLimitNumber.TryParse("35791395", F.MaxGlobalRate, out _));
        Assert.True(AdminRateLimitNumber.TryParse("35791394", F.MaxGlobalRate, out _));
    }

    [Fact]
    public async Task TheWholeRateDraftIsOneChangeAndReturningToTheBaselineIsClean()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.TrySetRateNumber(F.Global, "1001"));
        Assert.True(h.Controller.SetRate(F.Enabled, false));
        Assert.True(h.Controller.TrySetRateNumber(F.Endpoint("login", "burst"), "11"));
        Assert.Equal(1, h.Controller.State.DirtyCount);
        h.Controller.SetLocalNetwork(true);
        Assert.Equal(2, h.Controller.State.DirtyCount);
        Assert.False(h.Controller.TrySetRateNumber(F.Global, "12abc"));
        Assert.Equal(1001, h.Controller.State.RateValues!.Number(F.Global));
        h.Controller.TrySetRateNumber(F.Global, "1000"); h.Controller.SetRate(F.Enabled, true); h.Controller.TrySetRateNumber(F.Endpoint("login", "burst"), "10");
        h.Controller.SetLocalNetwork(false);
        Assert.Equal(0, h.Controller.State.DirtyCount);
        Assert.False(h.Controller.State.CanSave);
    }

    [Fact]
    public async Task QuotaModeOffLocksTheSizeBoxesAndKeepsTheirValues()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.SetRate(F.QuotaMode, "off"));
        Assert.True(h.Controller.QuotaSizeLocked(F.QuotaPerHour) && h.Controller.QuotaSizeLocked(F.QuotaBurst));
        Assert.False(h.Controller.TrySetRateNumber(F.QuotaPerHour, "20"));
        Assert.Equal(10000, h.Controller.State.RateValues!.Number(F.QuotaPerHour));
        Assert.False(h.Controller.SetRate(F.QuotaMode, "sometimes"));
        Assert.True(h.Controller.SetRate(F.QuotaMode, "enforce"));
        Assert.True(h.Controller.TrySetRateNumber(F.QuotaPerHour, "20"));
    }

    [Fact]
    public async Task RedisIsRefusedWhenUnavailableAndRuntimeHintsComeFromTheServerSnapshot()
    {
        using var h = new Harness { RateConfig = Config(backend: "redis"), RateStatus = """{"active":true,"active_backend":"memory","redis_available":false}""" };
        await h.Controller.OpenAsync();
        var snapshot = h.Controller.State.RateSnapshot!;
        Assert.False(snapshot.RedisSelectable);
        Assert.Equal("memory", snapshot.RunningBackendDrift);
        Assert.True(h.Controller.SetRate(F.Backend, "memory"));
        Assert.False(h.Controller.SetRate(F.Backend, "redis"));
        Assert.Equal("memory", h.Controller.State.RateSnapshot!.RunningBackendDrift); // drift follows the saved backend, not the draft
        Assert.Contains("using in-memory counters, not the saved Redis backend", AdminText.T("web.admin.settings.security.backend.drift", new { running = "memory", saved = "redis" }));
        Assert.Contains("using Redis counters, not the saved in-memory backend", AdminText.T("web.admin.settings.security.backend.drift", new { running = "redis", saved = "memory" }));
        using var inactive = new Harness { RateStatus = """{"active":false,"redis_available":true}""" };
        await inactive.Controller.OpenAsync();
        Assert.True(inactive.Controller.State.RateSnapshot!.LimiterInactive);
        Assert.Null(inactive.Controller.State.RateSnapshot!.RunningBackendDrift);
        using var older = new Harness { RateStatus = """{"active":true,"active_backend":"memory"}""" };
        await older.Controller.OpenAsync();
        Assert.True(older.Controller.State.RateSnapshot!.RedisSelectable);
    }

    // ── One Save, two ordered writers ────────────────────

    [Fact]
    public async Task SaveRunsThePutThenOneCapturedPatchWithExactMembersAndNoNulls()
    {
        using var h = new Harness();
        bool? restart = false; IReadOnlyCollection<string>? saved = null;
        h.Controller.RateLimitsSaved += value => restart = value;
        h.Controller.Saved += keys => saved = keys;
        h.Patch = _ => Task.FromResult(AdminMonitorKit.Json("""{"status":"ok","restart_required":true}"""));
        await h.Controller.OpenAsync();
        h.Controller.SetLocalNetwork(true);
        h.Controller.SetRate(F.Enabled, false);
        Assert.Equal(2, h.Controller.State.DirtyCount);
        h.RateETag = "\"rate-2\"";
        await h.Controller.SaveAsync();
        Assert.Equal(["PUT /api/v2/admin/settings", "PATCH /api/v2/admin/rate-limits/config"], h.Writes);
        var patch = Assert.Single(h.Patches);
        Assert.Equal("\"rate-1\"", patch.Headers.IfMatch.Single().ToString());
        Assert.Equal("p1", patch.Headers.GetValues("X-Profile-Id").Single());
        var json = h.PatchBodies.Single();
        Assert.DoesNotContain("null", json);
        Assert.DoesNotContain(".0", json);
        using (var body = JsonDocument.Parse(json))
        {
            var root = body.RootElement;
            Assert.Equal(["enabled", "backend", "global_requests_per_second", "tiers", "ip_requests_per_second", "ip_requests_per_minute", "ip_burst", "auth_endpoints", "session_quota"],
                root.EnumerateObject().Select(p => p.Name));
            Assert.False(root.GetProperty("enabled").GetBoolean());
            Assert.Equal(F.Endpoints, root.GetProperty("auth_endpoints").EnumerateObject().Select(p => p.Name));
            Assert.Equal(["standard", "elevated"], root.GetProperty("tiers").EnumerateObject().Select(p => p.Name));
            Assert.Equal(20.5, root.GetProperty("tiers").GetProperty("standard").GetProperty("requests_per_second").GetDouble());
            Assert.Equal("20", root.GetProperty("tiers").GetProperty("standard").GetProperty("burst").GetRawText());
            Assert.Equal(["mode", "requests_per_hour", "burst"], root.GetProperty("session_quota").EnumerateObject().Select(p => p.Name));
        }
        // The receipt has no validator: the config is read again and becomes the clean baseline.
        Assert.Equal("GET /api/v2/admin/rate-limits/config", h.Order.Last(r => r.Contains("rate-limits/config")));
        var state = h.Controller.State;
        Assert.Equal(0, state.DirtyCount);
        Assert.True(restart);
        Assert.Equal([LocalNetwork], saved);
        Assert.Equal("\"rate-2\"", state.RateSnapshot!.ETag);
        Assert.True(state.RateEditable && !state.Saving);
        Assert.Equal(AdminRateLimitBlock.None, state.RateBlock);
    }

    [Fact]
    public async Task APutFailureSkipsThePatchAndKeepsBothDrafts()
    {
        using var h = new Harness { Put = _ => Task.FromResult(Problem(HttpStatusCode.BadRequest, "validation_failed", "invalid duration")) };
        await h.Controller.OpenAsync();
        h.Controller.SetLocalNetwork(true);
        h.Controller.SetRate(F.Enabled, false);
        await h.Controller.SaveAsync();
        Assert.Equal(["PUT /api/v2/admin/settings"], h.Writes);
        Assert.Equal(2, h.Controller.State.DirtyCount);
        Assert.Equal("Failed to update settings.", h.Controller.State.Error);
        Assert.True(h.Controller.State.RateEditable);
    }

    [Fact]
    public async Task TheRateIntentIsCapturedBeforeThePutAndEditorsStayFrozenThroughBothWrites()
    {
        using var h = new Harness();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Put = async _ => { await gate.Task; return Saved("""{"media_servers.allow_private_destinations":"true"}"""); };
        await h.Controller.OpenAsync();
        h.Controller.SetLocalNetwork(true);
        h.Controller.TrySetRateNumber(F.Global, "1500");
        var save = h.Controller.SaveAsync();
        await WaitUntil(() => h.Writes.Length == 1);
        var during = h.Controller.State;
        Assert.True(during.Saving && during.SavePhase == AdminSecuritySavePhase.Settings && !during.RateEditable && !during.CanSave);
        Assert.False(h.Controller.TrySetRateNumber(F.Global, "1700"));
        Assert.False(h.Controller.SetRate(F.Enabled, false));
        h.RateETag = "\"rate-9\"";
        await h.Controller.ReloadRateAsync(); // ignored while saving
        h.Controller.Discard(); // ignored while saving
        // The shared form keeps accepting the local switch; the newer edit stays dirty.
        h.Controller.SetLocalNetwork(false);
        gate.SetResult();
        await save;
        Assert.Equal("\"rate-1\"", h.Patches.Single().Headers.IfMatch.Single().ToString());
        using (var body = JsonDocument.Parse(h.PatchBodies.Single())) Assert.Equal(1500, body.RootElement.GetProperty("global_requests_per_second").GetDouble());
        Assert.Equal([LocalNetwork], h.Controller.State.DirtyKeys);
        Assert.Equal("true", h.Controller.State.PersistedValues[LocalNetwork]);
        Assert.False(h.Controller.State.RateDirty);
    }

    [Fact]
    public async Task PartialSuccessSettlesTheSettingsAndKeepsTheRateDraftWithTheBoundedRedactedDetail()
    {
        using var h = new Harness();
        IReadOnlyCollection<string>? saved = null;
        h.Controller.Saved += keys => saved = keys;
        var detail = "session_quota.burst must refill within a week: see https://internal.example/x?sig=secret " + new string('x', 600);
        h.Patch = _ => Task.FromResult(Problem(HttpStatusCode.BadRequest, "validation_failed", detail));
        await h.Controller.OpenAsync();
        h.Controller.SetLocalNetwork(true);
        h.Controller.TrySetRateNumber(F.QuotaBurst, "9999999");
        await h.Controller.SaveAsync();
        var state = h.Controller.State;
        Assert.Empty(state.DirtyKeys);
        Assert.Equal([LocalNetwork], saved);
        Assert.True(state.RateDirty);
        Assert.Equal(1, state.DirtyCount);
        Assert.Equal(AdminRateLimitMessage.SaveFailed, state.RateMessage);
        Assert.Equal(AdminFailureKind.Validation, state.RateFailure);
        Assert.StartsWith("session_quota.burst must refill within a week", state.RateDetail);
        Assert.DoesNotContain("sig=secret", state.RateDetail);
        Assert.True(state.RateDetail!.Length <= 301);
        Assert.True(state.CanSave && state.RateEditable);
        Assert.Single(h.Patches);
    }

    [Fact]
    public async Task AFailedReReadAfterAnAcknowledgedPatchBlocksRateEditsUntilReload()
    {
        using var h = new Harness();
        var saves = 0;
        h.Controller.RateLimitsSaved += _ => saves++;
        h.Patch = _ => { h.ConfigStatus = HttpStatusCode.ServiceUnavailable; return Task.FromResult(AdminMonitorKit.Json("""{"status":"ok","restart_required":false}""")); };
        await h.Controller.OpenAsync();
        h.Controller.TrySetRateNumber(F.Global, "1500");
        await h.Controller.SaveAsync();
        var state = h.Controller.State;
        Assert.Equal(1, saves);
        Assert.Equal(AdminRateLimitBlock.ReloadRequired, state.RateBlock);
        Assert.Equal(AdminRateLimitMessage.ReloadBeforeEditing, state.RateMessage);
        Assert.Equal(0, state.DirtyCount);
        Assert.False(state.RateEditable);
        Assert.False(h.Controller.TrySetRateNumber(F.Global, "1600"));
        h.ConfigStatus = HttpStatusCode.OK; h.RateConfig = Config(global: 1500); h.RateETag = "\"rate-2\"";
        await h.Controller.ReloadRateAsync();
        Assert.Equal(AdminRateLimitBlock.None, h.Controller.State.RateBlock);
        Assert.True(h.Controller.State.RateEditable);
        Assert.Equal(1500, h.Controller.State.RateBaseline!.Number(F.Global));
        Assert.Single(h.Patches);
    }

    [Fact]
    public async Task APreconditionFailureNeedsAnExplicitReloadThatRebasesOnlyEditedFields()
    {
        using var h = new Harness();
        var calls = 0;
        h.Patch = _ => Task.FromResult(calls++ == 0 ? Problem(HttpStatusCode.PreconditionFailed, "precondition_failed") : AdminMonitorKit.Json("""{"status":"ok","restart_required":false}"""));
        await h.Controller.OpenAsync();
        h.Controller.TrySetRateNumber(F.Global, "1500");
        h.Controller.TrySetRateNumber(F.Tier("standard", "burst"), "25");
        await h.Controller.SaveAsync();
        var state = h.Controller.State;
        Assert.Equal(AdminRateLimitBlock.Conflict, state.RateBlock);
        Assert.Equal(AdminRateLimitMessage.ChangedElsewhere, state.RateMessage);
        Assert.False(state.CanSave);
        Assert.True(state.RateDirty);
        await h.Controller.SaveAsync();
        Assert.Single(h.Patches); // never retried automatically
        h.RateConfig = Config(global: 1200, ipBurst: 200); h.RateETag = "\"rate-2\"";
        await h.Controller.ReloadRateAsync();
        state = h.Controller.State;
        Assert.Equal(AdminRateLimitBlock.None, state.RateBlock);
        Assert.Equal(AdminRateLimitMessage.Merged, state.RateMessage);
        Assert.Equal(1500, state.RateValues!.Number(F.Global));
        Assert.Equal(25, state.RateValues.Number(F.Tier("standard", "burst")));
        Assert.Equal(200, state.RateValues.Number(F.IpBurst));
        Assert.Equal(["global_requests_per_second", "tiers.standard.burst"], state.RateDirtyPaths);
        Assert.True(state.CanSave);
        await h.Controller.SaveAsync();
        Assert.Equal(2, h.Patches.Count);
        Assert.Equal("\"rate-2\"", h.Patches[1].Headers.IfMatch.Single().ToString());
        using var body = JsonDocument.Parse(h.PatchBodies[1]);
        Assert.Equal(200, body.RootElement.GetProperty("ip_burst").GetDouble());
    }

    public enum Reconciled { Applied, NotApplied, Changed }

    [Theory]
    [InlineData(Reconciled.Applied)]
    [InlineData(Reconciled.NotApplied)]
    [InlineData(Reconciled.Changed)]
    public async Task AnUnknownOutcomeIsNeverReplayedAndAReloadReconcilesIt(Reconciled result)
    {
        using var h = new Harness { Patch = _ => Task.FromResult(Problem(HttpStatusCode.GatewayTimeout, "dependency_unavailable")) };
        bool? announced = false; var saves = 0;
        h.Controller.RateLimitsSaved += value => { announced = value; saves++; };
        await h.Controller.OpenAsync();
        h.Controller.TrySetRateNumber(F.Global, "1500");
        await h.Controller.SaveAsync();
        var state = h.Controller.State;
        Assert.Equal(AdminRateLimitBlock.OutcomeUnknown, state.RateBlock);
        Assert.Equal(AdminRateLimitMessage.OutcomeUnknown, state.RateMessage);
        Assert.False(state.RateEditable || state.CanSave);
        await h.Controller.SaveAsync();
        Assert.Single(h.Patches);
        (h.RateConfig, h.RateETag) = result switch
        {
            Reconciled.Applied => (Config(global: 1500), "\"rate-2\""),
            Reconciled.NotApplied => (Config(), "\"rate-1b\""),
            _ => (Config(global: 1200, ipBurst: 200), "\"rate-3\""),
        };
        await h.Controller.ReloadRateAsync();
        state = h.Controller.State;
        Assert.Equal(AdminRateLimitBlock.None, state.RateBlock);
        Assert.True(state.RateEditable);
        switch (result)
        {
            case Reconciled.Applied:
                Assert.False(state.RateDirty);
                Assert.Equal(1, saves); Assert.Null(announced);
                break;
            case Reconciled.NotApplied:
                Assert.Equal(["global_requests_per_second"], state.RateDirtyPaths);
                Assert.Equal(AdminRateLimitMessage.None, state.RateMessage);
                Assert.True(state.CanSave); Assert.Equal(0, saves);
                break;
            default:
                Assert.Equal(1500, state.RateValues!.Number(F.Global));
                Assert.Equal(200, state.RateValues.Number(F.IpBurst));
                Assert.Equal(AdminRateLimitMessage.Merged, state.RateMessage);
                Assert.Equal(0, saves);
                break;
        }
        Assert.Single(h.Patches);
    }

    [Theory]
    [InlineData("""{"status":"ok"}""")]
    [InlineData("""{"status":"ok","restart_required":"yes"}""")]
    [InlineData("""{"status":5,"restart_required":false}""")]
    [InlineData("transport")]
    public async Task MalformedAcknowledgementsAndTransportFailuresAreUnknownAndSentOnce(string reply)
    {
        using var h = new Harness
        {
            Patch = _ => reply == "transport" ? throw new HttpRequestException("synthetic reset") : Task.FromResult(AdminMonitorKit.Json(reply)),
        };
        await h.Controller.OpenAsync();
        h.Controller.SetRate(F.Enabled, false);
        await h.Controller.SaveAsync();
        Assert.Equal(AdminRateLimitBlock.OutcomeUnknown, h.Controller.State.RateBlock);
        Assert.Single(h.Patches);
    }

    [Fact]
    public async Task UnauthorizedPatchIsSentOnceWithoutRefreshOrReplay()
    {
        using var h = new Harness { Patch = _ => Task.FromResult(Problem(HttpStatusCode.Unauthorized, "authentication_required")) };
        await h.Controller.OpenAsync();
        h.Controller.SetRate(F.Enabled, false);
        await h.Controller.SaveAsync();
        Assert.Equal(["PATCH /api/v2/admin/rate-limits/config"], h.Writes);
        Assert.Equal(AdminFailureKind.Unauthorized, h.Controller.State.RateFailure);
        Assert.Equal(AdminRateLimitBlock.None, h.Controller.State.RateBlock);
        Assert.True(h.Controller.State.RateDirty);
    }

    // ── Lifetime ─────────────────────────────────────────

    [Fact]
    public async Task AnAuthorityChangeDropsEvenAnIdenticalDraft()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.SetRate(F.Enabled, false);
        h.Controller.SetLocalNetwork(true);
        Assert.Equal(2, h.Controller.State.DirtyCount);
        h.Kit.Auth.SelectProfile("p2", null, new Core.Models.Auth.Profile { Id = "p2", Name = "Other", IsPrimary = true });
        await WaitUntil(() => h.Controller.State.Owner == null);
        Assert.Null(h.Controller.State.RateValues);
        Assert.Equal(0, h.Controller.State.DirtyCount);
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.RateLoaded);
        Assert.Equal(0, h.Controller.State.DirtyCount);
    }

    [Fact]
    public async Task SuspendCancelsOwnedWorkAndIgnoresLateResults()
    {
        using var h = new Harness();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saves = 0;
        h.Controller.RateLimitsSaved += _ => saves++;
        h.Patch = async request => { await gate.Task; return AdminMonitorKit.Json("""{"status":"ok","restart_required":false}"""); };
        await h.Controller.OpenAsync();
        h.Controller.SetRate(F.Enabled, false);
        var save = h.Controller.SaveAsync();
        await WaitUntil(() => h.Patches.Count == 1);
        h.Controller.Suspend();
        gate.SetResult();
        await save;
        Assert.Equal(0, saves);
        Assert.Null(h.Controller.State.Owner);
        Assert.Null(h.Controller.State.RateValues);
        Assert.False(h.Controller.State.Saving);
        var gets = h.Order.Count(r => r.StartsWith("GET"));
        await Task.Delay(50);
        Assert.Equal(gets, h.Order.Count(r => r.StartsWith("GET")));
    }

    [Fact]
    public async Task AReadThatFinishesAfterRetirementIsIgnored()
    {
        using var h = new Harness();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var respond = h.Kit.Http.Respond;
        h.Kit.Http.Respond = async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/rate-limits/status")) await gate.Task.WaitAsync(ct);
            return await respond(request, ct);
        };
        var open = h.Controller.OpenAsync();
        await WaitUntil(() => h.Kit.Http.Requests.Contains("GET /api/v2/admin/rate-limits/status"));
        h.Controller.Suspend();
        gate.SetResult();
        await open;
        Assert.Null(h.Controller.State.RateValues);
        Assert.False(h.Controller.State.RateLoading);
    }

    // ── Navigation and text ──────────────────────────────

    [Theory]
    [InlineData("/admin/settings/security")]
    [InlineData("/admin/settings?tab=rate-limiting")]
    [InlineData("/admin/settings/rate-limiting")]
    public void SecurityResolvesUnderSignInAndSecurityButNeedsRegistration(string href)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.Equal(AdminRoute.SecuritySettings, resolution.Request!.Route);
        Assert.Equal("/admin/settings/security", resolution.CanonicalHref);
        var topic = AdminRouteCatalog.Sections.Single(section => section.Id == "configure").Entries.Cast<AdminNavTopic>()
            .Single(entry => entry.Leaves.Any(leaf => leaf.Route == AdminRoute.SecuritySettings));
        Assert.Equal("access", topic.Id);
        Assert.False(new AdminPageRegistry([]).CanOpen(resolution.Request));
        Assert.True(new AdminPageRegistry([new(AdminRoute.SecuritySettings, typeof(object))]).CanOpen(resolution.Request));
    }

    [Theory]
    [InlineData("where counters")]
    [InlineData("rate limit")]
    [InlineData("private destinations")]
    public void SecurityIsSearchableOnlyWhenRegistered(string query)
    {
        Assert.Empty(AdminNavigation.SearchGroups(query, new AdminPageRegistry([]).IsRegistered));
        var registry = new AdminPageRegistry([new(AdminRoute.SecuritySettings, typeof(object))]);
        Assert.Contains(AdminNavigation.SearchGroups(query, registry.IsRegistered).SelectMany(group => group.Pages), page => page.Route == AdminRoute.SecuritySettings);
    }

    [Fact]
    public void EveryOfficialSecurityTextKeyIsInTheCatalog()
    {
        Assert.Empty(AdminRouteCatalog.MissingTextKeys(AdminRoute.SecuritySettings));
        string[] keys =
        [
            "web.admin.common.rateLimits.changedElsewhere", "web.admin.common.rateLimits.reloadBeforeEditing", "web.admin.common.rateLimits.saveFailed",
            "web.admin.common.rateLimits.saved", "web.admin.common.rateLimits.savedRestartRequired",
            "web.admin.settings.security.backend.description", "web.admin.settings.security.backend.descriptionNoRedis", "web.admin.settings.security.backend.drift",
            "web.admin.settings.security.backend.label", "web.admin.settings.security.backend.memory", "web.admin.settings.security.backend.redis",
            "web.admin.settings.security.endpoint.autoscanWebhook", "web.admin.settings.security.endpoint.deviceLookup", "web.admin.settings.security.endpoint.devicePoll",
            "web.admin.settings.security.endpoint.deviceStart", "web.admin.settings.security.endpoint.login", "web.admin.settings.security.endpoint.passwordChange",
            "web.admin.settings.security.endpoint.setup", "web.admin.settings.security.endpoint.signup", "web.admin.settings.security.endpoints.heading",
            "web.admin.settings.security.global.description", "web.admin.settings.security.global.label", "web.admin.settings.security.global.unit",
            "web.admin.settings.security.loading", "web.admin.settings.security.localNetwork.description", "web.admin.settings.security.localNetwork.group",
            "web.admin.settings.security.localNetwork.label", "web.admin.settings.security.localNetwork.warning", "web.admin.settings.security.perAddress.description",
            "web.admin.settings.security.perAddress.label", "web.admin.settings.security.quotaMode.enforce", "web.admin.settings.security.quotaMode.log",
            "web.admin.settings.security.quotaMode.off", "web.admin.settings.security.rate.burst", "web.admin.settings.security.rate.perHour",
            "web.admin.settings.security.rate.perMinute", "web.admin.settings.security.rate.perSecond", "web.admin.settings.security.rateLimit.enable",
            "web.admin.settings.security.rateLimit.group", "web.admin.settings.security.rateLimit.notRunning", "web.admin.settings.security.session.heading",
            "web.admin.settings.security.session.quotaDescription", "web.admin.settings.security.session.quotaLabel", "web.admin.settings.security.session.sizeDescription",
            "web.admin.settings.security.session.sizeLabel", "web.admin.settings.security.tier.elevated", "web.admin.settings.security.tier.standard",
            "web.admin.settings.security.tiers.heading", "web.admin.settings.security.title",
        ];
        Assert.Equal(49, keys.Length);
        Assert.All(keys, key => Assert.True(AdminText.Has(key), key));
        Assert.Equal("Rate limit settings saved — restart the server to apply them", AdminText.T("web.admin.common.rateLimits.savedRestartRequired"));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}
