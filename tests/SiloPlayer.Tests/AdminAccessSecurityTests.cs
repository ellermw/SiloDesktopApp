using System.Net;
using System.Reflection;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Behavioral wire regressions. Reflection keeps the test-only checkpoint runnable on Beta before implementation.</summary>
public sealed class AdminAccessSecurityTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };
    private const string Key = """{"id":"7","user_id":"1","label":"Fixture","key_prefix":"synthetic","rate_tier":"standard","scopes":[],"created_at":"2026-11-17T00:00:00Z"}""";
    private const string Document = """{"id":"9","domain":"permission","name":"Fixture","enabled":false,"active_version_id":null,"created_at":"2026-11-17T00:00:00Z","updated_at":"2026-11-17T00:00:00Z"}""";

    private static Type Required(string name) => Assert.IsAssignableFrom<Type>(typeof(SiloApiClient).Assembly.GetType(name));
    private static object Api(string name, SiloApiClient client) => Activator.CreateInstance(Required("SiloPlayer.Core.Api." + name), client)!;
    private static object Model(string name, string json) => JsonSerializer.Deserialize(json, Required("SiloPlayer.Core.Models.Admin." + name), Json)!;
    internal static async Task<object?> Call(object target, string name, params object?[] supplied)
    {
        var method = Assert.Single(target.GetType().GetMethods().Where(m => m.Name == name));
        var args = method.GetParameters().Select((p, i) => i < supplied.Length ? supplied[i] : p.DefaultValue).ToArray();
        Task task;
        try { task = (Task)method.Invoke(target, args)!; }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }
    private static object Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;
    private static HttpResponseMessage Reply(string body, string? tag = "\"fixture-1\"", HttpStatusCode code = HttpStatusCode.OK)
    {
        var response = AdminMonitorKit.Json(body, code);
        if (tag != null) response.Headers.TryAddWithoutValidation("ETag", tag);
        return response;
    }

    [Theory]
    [InlineData("/admin/api-keys", SiloPlayer.Core.Services.Admin.AdminRoute.ApiKeys)]
    [InlineData("/admin/policy", SiloPlayer.Core.Services.Admin.AdminRoute.Policy)]
    [InlineData("/admin/policy?tab=decisions", SiloPlayer.Core.Services.Admin.AdminRoute.Policy)]
    [InlineData("/admin/settings/sign-in", SiloPlayer.Core.Services.Admin.AdminRoute.SignInSettings)]
    public void AccessPagesAreReachableThroughTheRouteCatalog(string href, SiloPlayer.Core.Services.Admin.AdminRoute route)
    {
        Assert.True(SiloPlayer.Core.Services.Admin.AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.Equal(SiloPlayer.Core.Services.Admin.AdminRouteResolutionKind.Route, resolution.Kind);
        Assert.Equal(route, resolution.Request!.Route);
        Assert.False(resolution.IsCatchAll);
        var registry = new SiloPlayer.Core.Services.Admin.AdminPageRegistry([new(route, typeof(object))]);
        Assert.True(registry.CanOpen(resolution.Request));
        Assert.Empty(SiloPlayer.Core.Services.Admin.AdminRouteCatalog.MissingTextKeys(route));
    }

    [Theory]
    [InlineData("AdminApiKeysApi")]
    [InlineData("AdminSignInApi")]
    [InlineData("AdminPolicyApi")]
    public void SecuritySubfamiliesHaveActualApis(string name) => Required("SiloPlayer.Core.Api." + name);

    [Theory]
    [InlineData(null)]
    [InlineData("W/\"weak\"")]
    [InlineData("*")]
    public async Task KeyCanonicalReadRejectsMissingWeakOrWildcardValidator(string? tag)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Reply(Key, tag));
        await Assert.ThrowsAnyAsync<Exception>(() => Call(Api("AdminApiKeysApi", kit.Client), "GetAsync", owner, "7"));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task KeyTierUsesReviewedCanonicalTagAndExactTierBody()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        string? guard = null, body = null;
        kit.Http.Respond = async (request, ct) =>
        {
            if (request.Method == HttpMethod.Put) { guard = request.Headers.GetValues("If-Match").Single(); body = await request.Content!.ReadAsStringAsync(ct); }
            return Reply(request.Method == HttpMethod.Put ? Key.Replace("standard", "elevated") : Key);
        };
        var api = Api("AdminApiKeysApi", kit.Client);
        var editor = await Call(api, "GetAsync", owner, "7");
        await Call(api, "UpdateTierAsync", owner, editor, "elevated");
        Assert.Equal("\"fixture-1\"", guard);
        Assert.Equal("{\"rate_tier\":\"elevated\"}", body);
        Assert.Equal(["GET /api/v2/admin/api-keys/7", "PUT /api/v2/admin/api-keys/7/tier"], kit.Http.Requests);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(412)]
    [InlineData(500)]
    public async Task CredentialCreationNeverRefreshesOrResends(int code)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"type":"https://siloserver.org/docs/api/v2/problems/permission_denied","status":403,"title":"Refused"}""", code: (HttpStatusCode)code));
        await Assert.ThrowsAnyAsync<Exception>(() => Call(Api("AdminApiKeysApi", kit.Client), "CreateAsync", owner, "Fixture", "1", Array.Empty<string>()));
        Assert.Single(kit.Http.Requests);
        Assert.Equal("POST /api/v2/admin/api-keys", kit.Http.Requests.Single());
    }

    [Fact]
    public async Task CreateCarriesOnlyLabelOwnerAndScopesAndDisclosesOnlyTheCreatedResponse()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Reply(Key.Replace("\"key_prefix\":\"synthetic\"", "\"key\":\"sa_SYNTHETIC_ONLY\""), code: HttpStatusCode.Created));
        var result = await Call(Api("AdminApiKeysApi", kit.Client), "CreateAsync", owner, "Fixture", "1", new[] { "catalog:read" });
        using var body = JsonDocument.Parse(kit.Http.Bodies.Single());
        Assert.Equal(["label", "user_id", "scopes"], body.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("catalog:read", body.RootElement.GetProperty("scopes")[0].GetString());
        Assert.DoesNotContain("sa_SYNTHETIC_ONLY", result!.ToString());
    }

    [Theory]
    [InlineData("{\"items\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"same\"}}")]
    [InlineData("{\"items\":[],\"page\":{\"has_more\":false,\"next_cursor\":\"stray\"}}")]
    [InlineData("{\"items\":[]}")]
    public async Task KeyPagesRejectInvalidContinuation(string reply)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Reply(reply));
        await Assert.ThrowsAnyAsync<Exception>(() => Call(Api("AdminApiKeysApi", kit.Client), "ListAsync", owner, "same"));
        Assert.Single(kit.Http.Requests);
        Assert.Contains("limit=50", kit.Http.Requests.Single());
    }

    [Fact]
    public async Task RetiredAuthorityCannotSendOrPublishAKeyRead()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = async (_, _) => { await gate.Task; return Reply(Key); };
        var api = Api("AdminApiKeysApi", kit.Client);
        var pending = Call(api, "GetAsync", owner, "7");
        authority.Retire(); gate.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Call(api, "GetAsync", owner, "7"));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task PolicyActivationUsesReviewedTagAndNeverReplaysConflict()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        string? guard = null;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(Reply(Document));
            guard = request.Headers.GetValues("If-Match").Single();
            return Task.FromResult(Reply("{}", code: HttpStatusCode.PreconditionFailed));
        };
        var api = Api("AdminPolicyApi", kit.Client); var snapshot = await Call(api, "GetAsync", owner, "9");
        await Assert.ThrowsAnyAsync<Exception>(() => Call(api, "ActivateAsync", owner, snapshot, "11"));
        Assert.Equal("\"fixture-1\"", guard);
        Assert.Equal(2, kit.Http.Requests.Count);
        Assert.Equal("{\"version_id\":\"11\"}", kit.Http.Bodies.Last());
    }

    [Fact]
    public async Task InvalidPolicyDraftRemainsAStoredDraftWithoutActivation()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"id":"11","document_id":"9","version_number":1,"source_sha256":"synthetic","compiled_ok":false,"compile_error":"synthetic syntax error","source":"bad rego"}""", code: HttpStatusCode.Created));
        var result = await Call(Api("AdminPolicyApi", kit.Client), "SaveVersionAsync", owner, "9", "bad rego", "fixture");
        Assert.False((bool)Property(result!, "CompiledOk"));
        Assert.Equal(["POST /api/v2/admin/policy/documents/9/versions"], kit.Http.Requests);
    }

    [Fact]
    public async Task PolicySimulationPreservesJsonNullAndUsesExactDomain()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"decision":null,"eval_time_ns":10,"generation":2}"""));
        await Call(Api("AdminPolicyApi", kit.Client), "SimulateAsync", owner, "permission", "", "null");
        using var body = JsonDocument.Parse(kit.Http.Bodies.Single());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("input").ValueKind);
        Assert.Equal("permission", body.RootElement.GetProperty("domain").GetString());
        Assert.False(body.RootElement.TryGetProperty("source", out _));
    }

    [Fact]
    public async Task ConnectionTestIsStagedAndNeverWritesConfiguration()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"ok":false,"steps":[{"id":"groups","label":"Allowed groups","ok":false,"message":"synthetic refusal"}]}"""));
        var staged = Model("AdminSignInConfigWrite", """{"key":"ldap","value":{"allowed_groups":["fixture-readers"],"admin_groups":["fixture-admins"]},"clear_secrets":["bind_password"]}""");
        var array = Array.CreateInstance(staged.GetType(), 1); array.SetValue(staged, 0);
        var result = await Call(Api("AdminSignInApi", kit.Client), "TestAsync", owner, "3", "ldap", array);
        Assert.False((bool)Property(result!, "Ok"));
        Assert.Equal(["POST /api/v2/admin/plugins/installations/3/auth-binding/test"], kit.Http.Requests);
        using var body = JsonDocument.Parse(kit.Http.Bodies.Single());
        Assert.Equal("fixture-admins", body.RootElement.GetProperty("config")[0].GetProperty("value").GetProperty("admin_groups")[0].GetString());
        Assert.Equal("bind_password", body.RootElement.GetProperty("config")[0].GetProperty("clear_secrets")[0].GetString());
    }

    [Theory]
    [InlineData("AdminApiKeysController")]
    [InlineData("AdminPolicyController")]
    [InlineData("AdminSignInController")]
    public void ControllersOwnPageLifetimeAndDispose(string name)
    {
        var type = Required("SiloPlayer.Core.Services.Admin." + name);
        Assert.True(typeof(IDisposable).IsAssignableFrom(type));
        Assert.NotNull(type.GetMethod("Suspend"));
    }

    private sealed class ControllerHarness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly object Controller;
        internal readonly List<string> Writes = [];
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Write;
        internal bool OtherIsOwner, FailList;
        internal string ExtraKey = "";
        internal ControllerHarness()
        {
            Authority = new(Kit.Client, Kit.Auth);
            Controller = Activator.CreateInstance(Required("SiloPlayer.Core.Services.Admin.AdminApiKeysController"), Api("AdminApiKeysApi", Kit.Client), Authority)!;
            Kit.Http.Respond = async (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method != HttpMethod.Get)
                {
                    Writes.Add(request.Method + " " + path);
                    return Write != null ? await Write(request) : Reply(Key.Replace("\"key_prefix\":\"synthetic\"", "\"key\":\"sa_SYNTHETIC_ONLY\""), code: HttpStatusCode.Created);
                }
                return path switch
                {
                    "/api/v2/admin/api-keys/capabilities" => Reply("""{"available":true,"guarded_configuration":true,"scopes":[{"name":"catalog:read","description":"Fixture","audience":"user"}],"rate_tiers":["standard","elevated"]}"""),
                    "/api/v2/admin/api-keys" when FailList => throw new HttpRequestException("synthetic list failure"),
                    "/api/v2/admin/api-keys" => Reply("{\"items\":[" + Key + ExtraKey + "],\"page\":{\"has_more\":false}}"),
                    "/api/v2/admin/api-keys/7" => Reply(Key),
                    "/api/v2/admin/users" => Reply("{\"items\":[" + Account("1", false) + "," + Account("2", OtherIsOwner) + "],\"page\":{\"has_more\":false}}"),
                    "/api/v2/admin/users/1" => Reply(Account("1", false)),
                    "/api/v2/admin/users/2" => Reply(Account("2", OtherIsOwner)),
                    _ => Reply("{}"),
                };
            };
        }
        private static string Account(string id, bool owner) => JsonSerializer.Serialize(new { id, username = "fixture", role = id == "1" || owner ? "admin" : "user", enabled = true, is_owner = owner });
        internal Task Open() => Call(Controller, "OpenAsync");
        internal Task<object?> Create(string user = "1", bool scoped = false, string[]? scopes = null, bool confirmed = true) =>
            Call(Controller, "CreateAsync", "Fixture", user, scopes ?? Array.Empty<string>(), scoped, confirmed);
        public void Dispose() { ((IDisposable)Controller).Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    [Fact]
    public async Task CreateConfirmationAndExplicitScopedSelectionCannotBecomeFullAccess()
    {
        using var h = new ControllerHarness(); await h.Open();
        Assert.Null(await h.Create(confirmed: false)); Assert.Empty(h.Writes);
        Assert.Null(await h.Create(scoped: true)); Assert.Empty(h.Writes);
    }

    [Fact]
    public async Task CurrentAccountOwnershipIsRecheckedImmediatelyBeforeCreating()
    {
        using var h = new ControllerHarness(); await h.Open(); h.OtherIsOwner = true;
        Assert.Null(await h.Create(user: "2")); Assert.Empty(h.Writes);
        Assert.True((bool)Property(h.Controller, "ReviewRequired"));
    }

    [Fact]
    public async Task ConcurrentCreateClicksProduceOnlyOneCredentialAndSecretNeverEntersControllerState()
    {
        using var h = new ControllerHarness(); await h.Open();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = async _ => { await gate.Task; return Reply(Key.Replace("\"key_prefix\":\"synthetic\"", "\"key\":\"sa_SYNTHETIC_ONLY\""), code: HttpStatusCode.Created); };
        var first = h.Create(); await WaitUntil(() => h.Writes.Count == 1);
        Assert.Null(await h.Create()); gate.SetResult(); Assert.Equal("sa_SYNTHETIC_ONLY", await first);
        Assert.Single(h.Writes);
        Assert.Null(h.Controller.GetType().GetProperty("Secret"));
        Assert.DoesNotContain("sa_SYNTHETIC_ONLY", Property(h.Controller, "Rows").ToString());
    }

    [Fact]
    public async Task UnknownCreateBlocksResendUntilExplicitReload()
    {
        using var h = new ControllerHarness(); await h.Open();
        h.Write = _ => throw new HttpRequestException("synthetic credential transport failure");
        Assert.Null(await h.Create()); Assert.Null(await h.Create()); Assert.Single(h.Writes);
        Assert.True((bool)Property(h.Controller, "ReviewRequired"));
        await h.Open(); Assert.False((bool)Property(h.Controller, "ReviewRequired"));
        Assert.Single(h.Writes);
    }

    [Fact]
    public async Task CreatedKeyListIsReconciledWithTheServerCanonicalFirstPage()
    {
        using var h = new ControllerHarness(); await h.Open();
        Assert.Single((System.Collections.IEnumerable)Property(h.Controller, "Rows"));
        h.ExtraKey = "," + Key.Replace("\"id\":\"7\"", "\"id\":\"8\"").Replace("Fixture", "Created elsewhere");
        Assert.Equal("sa_SYNTHETIC_ONLY", await h.Create());
        var ids = ((System.Collections.IEnumerable)Property(h.Controller, "Rows")).Cast<object>().Select(row => (string)Property(row, "Id")).ToArray();
        Assert.Equal(["7", "8"], ids);
        Assert.Equal(2, h.Kit.Http.Requests.Count(r => r.StartsWith("GET /api/v2/admin/api-keys?", StringComparison.Ordinal) || r == "GET /api/v2/admin/api-keys"));
        Assert.Single(h.Writes);
        Assert.False((bool)Property(h.Controller, "ReviewRequired"));
    }

    [Fact]
    public async Task FailedListRefreshAfterCreateKeepsTheOneTimeSecretAndBlocksFurtherWrites()
    {
        using var h = new ControllerHarness(); await h.Open(); h.FailList = true;
        Assert.Equal("sa_SYNTHETIC_ONLY", await h.Create());
        Assert.True((bool)Property(h.Controller, "ReviewRequired"));
        Assert.Null(await h.Create()); Assert.Single(h.Writes);
        Assert.DoesNotContain("sa_SYNTHETIC_ONLY", (string)Property(h.Controller, "Message"));
        h.FailList = false; await h.Open();
        Assert.False((bool)Property(h.Controller, "ReviewRequired"));
    }

    [Fact]
    public async Task SuspendDropsLateCreationSecretAndClearsAllPageState()
    {
        using var h = new ControllerHarness(); await h.Open();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = async _ => { await gate.Task; return Reply(Key.Replace("\"key_prefix\":\"synthetic\"", "\"key\":\"sa_SYNTHETIC_ONLY\""), code: HttpStatusCode.Created); };
        var first = h.Create(); await WaitUntil(() => h.Writes.Count == 1);
        h.Controller.GetType().GetMethod("Suspend")!.Invoke(h.Controller, null); gate.SetResult();
        Assert.Null(await first); Assert.False((bool)Property(h.Controller, "Current"));
        Assert.Empty((System.Collections.IEnumerable)Property(h.Controller, "Rows"));
    }

    [Fact]
    public void SchemaPreservesNumericStringsClearsOnlyExplicitSecretsAndExcludesHiddenFields()
    {
        var schema = Model("AdminSignInSchema", """{"key":"oidc","json_schema":"{\"properties\":{\"client_id\":{\"type\":\"string\"},\"enabled\":{\"type\":\"boolean\"}}}","admin_form":{"fields":[{"key":"client_id","control":"TEXT"},{"key":"client_secret","control":"PASSWORD","secret":true},{"key":"enabled","control":"SWITCH"},{"key":"hidden","control":"TEXT","show_when":[{"field":"enabled","equals":["true"]}]}]}}""");
        var provider = Model("AdminSignInProvider", """{"id":"3","capabilities":[],"global_configs":[]}""");
        var rules = Required("SiloPlayer.Core.Api.AdminSignInSchemaRules");
        var form = Property(schema, "AdminForm"); var field = ((System.Collections.IEnumerable)Property(form, "Fields")).Cast<object>().First();
        var converted = rules.GetMethod("Convert")!.Invoke(null, [schema, field, "00123"]);
        Assert.Equal("\"00123\"", ((System.Text.Json.Nodes.JsonNode)converted!).ToJsonString());
        var values = System.Text.Json.Nodes.JsonNode.Parse("""{"client_id":"00123","client_secret":"","enabled":false,"hidden":"must not save"}""")!;
        var result = rules.GetMethod("Build")!.Invoke(null, [provider, schema, values, new[] { "client_secret" }])!;
        var body = JsonSerializer.SerializeToElement(result, Json);
        Assert.Equal("00123", body.GetProperty("value").GetProperty("client_id").GetString());
        Assert.False(body.GetProperty("value").TryGetProperty("client_secret", out _));
        Assert.False(body.GetProperty("value").TryGetProperty("hidden", out _));
        Assert.Equal("client_secret", body.GetProperty("clear_secrets")[0].GetString());
    }

    [Theory]
    [InlineData("{}", "45")]
    [InlineData("{\"timeout\":null}", "null")]
    public void OptionalNumericDefaultsDoNotReplaceExplicitClears(string draft, string expected)
    {
        var schema = Model("AdminSignInSchema", """{"key":"oidc","json_schema":"{\"properties\":{\"timeout\":{\"type\":\"number\"}}}","admin_form":{"fields":[{"key":"timeout","control":"NUMBER","default_value":45}]}}""");
        var provider = Model("AdminSignInProvider", """{"id":"3","capabilities":[],"global_configs":[]}""");
        var values = System.Text.Json.Nodes.JsonNode.Parse(draft)!;
        var result = Required("SiloPlayer.Core.Api.AdminSignInSchemaRules").GetMethod("Build")!.Invoke(null, [provider, schema, values, Array.Empty<string>()])!;
        var body = JsonSerializer.SerializeToElement(result, Json);
        Assert.Equal(expected, body.GetProperty("value").GetProperty("timeout").GetRawText());
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        for (var i = 0; i < 200 && !predicate(); i++) await Task.Delay(10);
        Assert.True(predicate());
    }

    [Fact]
    public void DiagnosticPolicySamplesRedactNestedCredentialsAndSignedUrls()
    {
        using var json = JsonDocument.Parse("""{"headers":{"authorization":"Bearer synthetic-token"},"rows":[{"api_key":"sa_SYNTHETIC_ONLY"},"https://fixture:synthetic-password@example.test/media?token=synthetic-signed#secret"],"allowed":false}""");
        var safe = (string)Required("SiloPlayer.Core.Api.AdminAccessRedaction").GetMethod("Json")!.Invoke(null, [json.RootElement])!;
        Assert.DoesNotContain("synthetic-token", safe); Assert.DoesNotContain("sa_SYNTHETIC_ONLY", safe);
        Assert.DoesNotContain("synthetic-password", safe); Assert.DoesNotContain("synthetic-signed", safe);
        Assert.Contains("\"allowed\": false", safe);
    }

    [Fact]
    public async Task DocumentWithActiveVersionCannotBeDeletedEvenWhenDisabled()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Reply(Document.Replace("\"active_version_id\":null", "\"active_version_id\":\"11\"")));
        var api = Api("AdminPolicyApi", kit.Client); var reviewed = await Call(api, "GetAsync", owner, "9");
        await Assert.ThrowsAnyAsync<Exception>(() => Call(api, "DeleteAsync", owner, reviewed));
        Assert.Equal(["GET /api/v2/admin/policy/documents/9"], kit.Http.Requests);
    }

    private sealed class PolicyHarness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly object Controller;
        internal bool Available = true, FailSave;
        internal readonly List<string> Writes = [];
        internal PolicyHarness()
        {
            Authority = new(Kit.Client, Kit.Auth);
            Controller = Activator.CreateInstance(Required("SiloPlayer.Core.Services.Admin.AdminPolicyController"), Api("AdminPolicyApi", Kit.Client), Authority)!;
            Kit.Http.Respond = async (request, ct) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method != HttpMethod.Get) Writes.Add(request.Method + " " + path);
                if (path == "/api/v2/policy/capability") return Reply(JsonSerializer.Serialize(new { enabled = Available, editor_available = Available, decision_types = new[] { "permission" }, generation = 1 }));
                if (path == "/api/v2/admin/policy/documents") return Reply("{\"items\":[" + Document + "],\"page\":{\"has_more\":false}}");
                if (path == "/api/v2/admin/policy/documents/9") return Reply(Document);
                if (path.EndsWith("/validate")) return Reply("""{"compiled_ok":true,"errors":[]}""");
                if (path.EndsWith("/versions") && request.Method == HttpMethod.Post)
                {
                    if (FailSave) throw new HttpRequestException("synthetic reset");
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    return Reply(JsonSerializer.Serialize(new { id = "11", document_id = "9", version_number = 1, source_sha256 = "synthetic", compiled_ok = true, source = body.RootElement.GetProperty("source").GetString() }), code: HttpStatusCode.Created);
                }
                if (path.EndsWith("/versions")) return Reply("""{"items":[],"page":{"has_more":false}}""");
                return Reply("{}");
            };
        }
        internal Task Open() => Call(Controller, "OpenAsync", "documents", "9");
        internal void Draft(string source) => Controller.GetType().GetMethod("SetDraft")!.Invoke(Controller, [source]);
        public void Dispose() { ((IDisposable)Controller).Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    [Fact]
    public async Task CreatedPolicyRemainsInTheCollectionAfterReturningFromItsEditor()
    {
        using var h = new PolicyHarness(); await h.Open();
        var created = Document.Replace("\"id\":\"9\"", "\"id\":\"10\"");
        h.Kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/policy/capability" => Reply("""{"enabled":true,"editor_available":true,"decision_types":["permission"],"generation":1}"""),
            "/api/v2/admin/policy/documents" when request.Method == HttpMethod.Post => Reply(created, code: HttpStatusCode.Created),
            "/api/v2/admin/policy/documents" => Reply("{\"items\":[" + Document + "," + created + "],\"page\":{\"has_more\":false}}"),
            "/api/v2/admin/policy/documents/10" => Reply(created),
            _ => Reply("""{"items":[],"page":{"has_more":false}}"""),
        });
        await Call(h.Controller, "CreateAsync", "permission", "Fixture", true);
        Assert.Equal("10", Property(Property(Property(h.Controller, "Snapshot"), "Value"), "Id"));
        await Call(h.Controller, "SelectAsync", (object?)null);
        Assert.Contains(((System.Collections.IEnumerable)Property(h.Controller, "Documents")).Cast<object>(), document => (string)Property(document, "Id") == "10");
        Assert.Single(h.Kit.Http.Requests.Where(request => request == "POST /api/v2/admin/policy/documents"));
    }

    [Fact]
    public async Task UnavailablePolicyDoesNotProbeEditorStorageOrDecisionLog()
    {
        using var h = new PolicyHarness { Available = false }; await h.Open();
        Assert.Equal(["GET /api/v2/policy/capability"], h.Kit.Http.Requests);
        Assert.False((bool)Property(h.Controller, "Available"));
    }

    [Fact]
    public async Task EditingSourceInvalidatesValidationAndCannotSaveAnUnvalidatedDraft()
    {
        using var h = new PolicyHarness(); await h.Open();
        await Call(h.Controller, "ValidateAsync"); Assert.True((bool)Property(h.Controller, "Validated"));
        h.Draft("package silo_custom.permission\nimport rego.v1\n# changed");
        await Call(h.Controller, "SaveAsync", true);
        Assert.False((bool)Property(h.Controller, "Validated"));
        Assert.DoesNotContain(h.Writes, w => w.EndsWith("/versions"));
    }

    [Fact]
    public async Task VersionSaveRequiresExplicitReviewAndAdoptionPreservesDraft()
    {
        using var h = new PolicyHarness(); await h.Open();
        var draft = (string)Property(h.Controller, "Draft"); await Call(h.Controller, "ValidateAsync");
        await Call(h.Controller, "SaveAsync", true);
        Assert.True((bool)Property(h.Controller, "ReviewRequired"));
        Assert.DoesNotContain(h.Writes, w => w.Contains("active-version"));
        await Call(h.Controller, "ReviewAsync");
        Assert.True((bool)Property(h.Controller, "ReviewRequired"));
        h.Controller.GetType().GetMethod("AdoptReview")!.Invoke(h.Controller, null);
        Assert.False((bool)Property(h.Controller, "ReviewRequired"));
        Assert.Equal(draft, Property(h.Controller, "Draft"));
        Assert.Equal("11", Property(Property(h.Controller, "SavedVersion"), "Id"));
    }

    [Fact]
    public async Task EditorLabelsAndTitleFollowTheAdoptedSnapshot()
    {
        using var h = new PolicyHarness(); await h.Open();
        var before = (SiloPlayer.Core.Models.Admin.AdminPolicySnapshot)Property(h.Controller, "Snapshot");
        Assert.Equal("Fixture", SiloPlayer.Core.Services.Admin.AdminPolicyPresentation.Title(before));
        Assert.Equal("Enable override", SiloPlayer.Core.Services.Admin.AdminPolicyPresentation.ToggleLabel(before));
        var newer = Document.Replace("\"name\":\"Fixture\"", "\"name\":\"Renamed elsewhere\"").Replace("\"enabled\":false", "\"enabled\":true").Replace("\"active_version_id\":null", "\"active_version_id\":\"11\"");
        var original = h.Kit.Http.Respond!;
        h.Kit.Http.Respond = (request, ct) => request.RequestUri!.AbsolutePath == "/api/v2/admin/policy/documents/9" ? Task.FromResult(Reply(newer, "\"fixture-2\"")) : original(request, ct);
        await Call(h.Controller, "ReviewAsync");
        h.Controller.GetType().GetMethod("AdoptReview")!.Invoke(h.Controller, null);
        var after = (SiloPlayer.Core.Models.Admin.AdminPolicySnapshot)Property(h.Controller, "Snapshot");
        Assert.NotSame(before, after);
        Assert.Equal("Renamed elsewhere", SiloPlayer.Core.Services.Admin.AdminPolicyPresentation.Title(after));
        Assert.Equal("Disable override", SiloPlayer.Core.Services.Admin.AdminPolicyPresentation.ToggleLabel(after));
        Assert.Equal("Active", SiloPlayer.Core.Services.Admin.AdminPolicyPresentation.Status(after.Value));
        Assert.Equal("enabled", SiloPlayer.Core.Services.Admin.AdminPolicyPresentation.EnabledState(after));
    }

    [Fact]
    public async Task UnknownVersionSaveCannotBeResentAndCapabilityIsRecheckedBeforeTheWrite()
    {
        using var h = new PolicyHarness(); await h.Open(); await Call(h.Controller, "ValidateAsync"); h.FailSave = true;
        await Call(h.Controller, "SaveAsync", true); await Call(h.Controller, "SaveAsync", true);
        Assert.Single(h.Writes.Where(w => w.EndsWith("/versions")));
        Assert.True((bool)Property(h.Controller, "ReviewRequired"));
        Assert.Equal(2, h.Kit.Http.Requests.Count(r => r.EndsWith("/policy/capability")));
    }

    [Theory]
    [InlineData("SaveConfigAsync", "AdminSignInConfigWrite", "{\"key\":\"oidc\",\"value\":{\"client_id\":\"00123\"},\"clear_secrets\":[]}", "/config")]
    [InlineData("SaveBindingAsync", "AdminSignInBinding", "{\"capability_id\":\"oidc\",\"enabled\":true,\"display_order\":1,\"auto_provision\":true,\"default_login\":false}", "/auth-binding")]
    public async Task PluginSignInWritesRequireOfficial204WithoutTryingToDecodeJson(string method, string type, string body, string suffix)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await Call(Api("AdminSignInApi", kit.Client), method, owner, "3", Model(type, body));
        Assert.Equal(["PUT /api/v2/admin/plugins/installations/3" + suffix], kit.Http.Requests);
        Assert.Equal(JsonDocument.Parse(body).RootElement.ToString(), JsonDocument.Parse(kit.Http.Bodies.Single()).RootElement.ToString());
    }

    private sealed class SignInHarness : IDisposable
    {
        internal const string Local = "auth.local_password_login";
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        private readonly SiloPlayer.Core.Services.Admin.AdminServerSettingsStore _store;
        internal readonly object Controller;
        internal bool HasBreakGlass = true, ProviderOn = true, FailConfig, ConfiguredSecret = true;
        internal readonly List<string> Writes = [];
        internal object Provider => ((System.Collections.IEnumerable)Property(Controller, "Providers")).Cast<object>().Single();
        internal object Schema => ((System.Collections.IEnumerable)Property(Provider, "GlobalConfigSchema")).Cast<object>().Single();
        internal object Field(string key) => ((System.Collections.IEnumerable)Property(Property(Schema, "AdminForm"), "Fields")).Cast<object>().Single(f => (string)Property(f, "Key") == key);
        internal SignInHarness()
        {
            Authority = new(Kit.Client, Kit.Auth); var settings = new AdminServerSettingsApi(Kit.Client);
            _store = new(settings, Authority, [Local], []);
            Controller = Activator.CreateInstance(Required("SiloPlayer.Core.Services.Admin.AdminSignInController"), Api("AdminSignInApi", Kit.Client), settings, _store, Authority)!;
            Kit.Http.Respond = async (request, ct) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method != HttpMethod.Get)
                {
                    Writes.Add(request.Method + " " + path);
                    if (path.EndsWith("/config")) return FailConfig ? Reply("{}", code: HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.NoContent);
                    if (path.EndsWith("/test")) return Reply("""{"ok":true,"steps":[{"id":"connection","label":"Connection","ok":true,"message":"synthetic-secret must never appear"}]}""");
                    if (path == "/api/v2/admin/settings")
                    {
                        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                        return Reply("{\"values\":" + body.RootElement.GetProperty("values").GetRawText() + ",\"restart_required\":false}", "\"settings-2\"");
                    }
                    return new(HttpStatusCode.NoContent);
                }
                return path switch
                {
                    "/api/v2/admin/settings/effective" => Reply("""{"auth.local_password_login":"true","auth.email_auto_match":"false","auth.user_api_keys":"false","signup.enabled":"false","password_reset.self_service_enabled":"false","server.public_url":"https://fixture.invalid"}"""),
                    "/api/v2/admin/settings/sensitive-status" => Reply("""{"configured":[],"managed_by_env":[]}"""),
                    "/api/v2/admin/settings/restart-keys" => Reply("""{"keys":[],"prefixes":[]}"""),
                    "/api/v2/auth/external-sign-in/capabilities" => Reply("""{"available":true,"break_glass":true,"connection_test":true,"live_provider_changes":true}"""),
                    "/api/v2/admin/users" => Reply("{\"items\":[" + Account() + "],\"page\":{\"has_more\":false}}"),
                    "/api/v2/admin/users/1" => Reply(Account()),
                    "/api/v2/admin/plugins/installations" => Reply(Inventory()),
                    _ => Reply("{}"),
                };
            };
        }
        private string Account() => JsonSerializer.Serialize(new { id = "1", username = "fixture", role = "admin", enabled = true, is_owner = true, break_glass = HasBreakGlass, password_login = true });
        private string Inventory() => JsonSerializer.Serialize(new { items = new[] { new
        {
            id = "3", plugin_id = "fixture-oidc", enabled = true,
            capabilities = new[] { new { id = "oidc", type = "auth_provider.v1", sign_in_mode = "oauth", display_name = "Fixture", metadata = new { connection_test = true } } },
            auth_bindings = new[] { new { capability_id = "oidc", enabled = ProviderOn, auto_provision = true, display_order = 1, default_login = false } },
            global_config_schema = new[] { new { key = "oidc", title = "Provider", json_schema = "{\"properties\":{\"client_secret\":{\"type\":\"string\"},\"allowed_groups\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}}", admin_form = new { fields = new object[] { new { key = "client_secret", label = "Client secret", control = "PASSWORD", secret = true }, new { key = "allowed_groups", label = "Allowed groups", control = "TEXTAREA" } }, sections = Array.Empty<object>() } } },
            global_configs = new[] { new { key = "oidc", value = new { allowed_groups = new[] { "readers" } }, configured_secrets = ConfiguredSecret ? new[] { "client_secret" } : Array.Empty<string>() } },
        } } });
        internal Task Open() => Call(Controller, "OpenAsync");
        internal void SetLocal(bool value) => Controller.GetType().GetMethod("Set")!.Invoke(Controller, [Local, value]);
        internal void EditSecret() => Controller.GetType().GetMethod("SetField")!.Invoke(Controller, [Provider, Schema, Field("client_secret"), "synthetic-secret"]);
        public void Dispose() { ((IDisposable)Controller).Dispose(); _store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task LocalPasswordDisableRequiresKnownBreakGlassAndAnEnabledPrimaryProvider(bool breakGlass, bool providerOn)
    {
        using var h = new SignInHarness { HasBreakGlass = breakGlass, ProviderOn = providerOn }; await h.Open(); h.SetLocal(false);
        Assert.Equal("true", h.Controller.GetType().GetMethod("Value")!.Invoke(h.Controller, [SignInHarness.Local]));
        await Call(h.Controller, "SaveAsync", true); Assert.Empty(h.Writes);
    }

    [Fact]
    public async Task RemovingBreakGlassAfterConfirmationPreventsSettingsWrite()
    {
        using var h = new SignInHarness(); await h.Open(); h.SetLocal(false); h.HasBreakGlass = false;
        await Call(h.Controller, "SaveAsync", true); Assert.Empty(h.Writes); Assert.True((bool)Property(h.Controller, "ReviewRequired"));
    }

    [Fact]
    public async Task ProviderConfigurationSavesBeforeDisablingLocalPasswordsAndFailureStopsTheSequence()
    {
        using var h = new SignInHarness(); await h.Open(); h.EditSecret(); h.SetLocal(false); h.FailConfig = true;
        await Call(h.Controller, "SaveAsync", true); await Call(h.Controller, "SaveAsync", true);
        Assert.Equal(["PUT /api/v2/admin/plugins/installations/3/config"], h.Writes);
        Assert.True((bool)Property(h.Controller, "ReviewRequired"));
    }

    [Fact]
    public async Task ConfirmedCombinedSaveAcknowledgesProviderThenWritesOnlyDirtyLocalSettings()
    {
        using var h = new SignInHarness(); await h.Open(); h.EditSecret(); h.SetLocal(false);
        await Call(h.Controller, "SaveAsync", true);
        Assert.Equal(["PUT /api/v2/admin/plugins/installations/3/config", "PUT /api/v2/admin/settings"], h.Writes);
        Assert.Equal(0, Property(h.Controller, "DirtyCount"));
        Assert.Equal("false", h.Controller.GetType().GetMethod("SavedValue")!.Invoke(h.Controller, [SignInHarness.Local]));
    }

    [Fact]
    public async Task TestUsesStagedSecretRedactsItsEchoAndDoesNotPersistIt()
    {
        using var h = new SignInHarness(); await h.Open(); h.EditSecret();
        await Call(h.Controller, "TestAsync", h.Provider, null);
        Assert.Equal(["POST /api/v2/admin/plugins/installations/3/auth-binding/test"], h.Writes);
        var results = JsonSerializer.Serialize(Property(h.Controller, "TestResults"), Json);
        Assert.DoesNotContain("synthetic-secret", results);
        h.Controller.GetType().GetMethod("Suspend")!.Invoke(h.Controller, null);
        Assert.Equal(0, Property(h.Controller, "DirtyCount"));
        Assert.Empty((System.Collections.IEnumerable)Property(h.Controller, "Providers"));
    }

    [Fact]
    public async Task TransportExceptionCannotLeakSubmittedCredentialIntoDiagnostics()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SiloAdminAccessFixture", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var operation = LocalLog.BeginOperation("synthetic-admin-access", directory);
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => throw new HttpRequestException("synthetic-secret-echo");
        await Assert.ThrowsAnyAsync<Exception>(() => Call(Api("AdminApiKeysApi", kit.Client), "CreateAsync", owner, "Fixture", "1", Array.Empty<string>()));
        var logs = string.Join("\n", Directory.GetFiles(directory).Select(File.ReadAllText));
        Assert.DoesNotContain("synthetic-secret-echo", logs);
        Assert.Contains("admin_access_transport_failed", logs);
    }

    [Fact]
    public async Task SchemaWriteOnlyOverridesExplicitFormAndReturnedSecretNeverBecomesABaseline()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Reply("""{"items":[{"id":"3","enabled":true,"capabilities":[{"id":"oidc","type":"auth_provider.v1"}],"global_config_schema":[{"key":"oidc","json_schema":"{\"type\":\"object\",\"properties\":{\"client_secret\":{\"type\":\"string\",\"writeOnly\":true}}}","admin_form":{"fields":[{"key":"client_secret","control":"TEXT","secret":false}]}}],"global_configs":[{"key":"oidc","value":{"client_secret":"SYNTHETIC_SERVER_SECRET"},"configured_secrets":["client_secret"]}]}]}"""));
        var result = await Call(Api("AdminSignInApi", kit.Client), "GetInstallationsAsync", owner);
        var provider = ((System.Collections.IEnumerable)result!).Cast<object>().Single();
        Assert.DoesNotContain("SYNTHETIC_SERVER_SECRET", JsonSerializer.Serialize(provider, Json));
        var schema = ((System.Collections.IEnumerable)Property(provider, "GlobalConfigSchema")).Cast<object>().Single();
        var field = ((System.Collections.IEnumerable)Property(Property(schema, "AdminForm"), "Fields")).Cast<object>().Single();
        Assert.True((bool)Property(field, "IsSecret"));
    }

    [Theory]
    [InlineData(false, false, "could not reload")]
    [InlineData(true, true, "notification failed")]
    [InlineData(true, false, "Other servers apply changes independently")]
    public async Task PersistedPolicyReceiptSeparatesLocalApplicationAndPublication(bool applied, bool publicationFailed, string expected)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Get ? Reply(Document) : Reply(JsonSerializer.Serialize(new
        {
            persisted = true,
            document = JsonDocument.Parse(Document).RootElement,
            persisted_generation = 2,
            application = new { local_applied = applied, loaded_generation = applied ? 2 : 1, publication_failed = publicationFailed },
        })));
        var api = Api("AdminPolicyApi", kit.Client); var reviewed = await Call(api, "GetAsync", owner, "9");
        var result = await Call(api, "EnableAsync", owner, reviewed, false);
        Assert.Contains(expected, (string)Property(result!, "ApplicationMessage"));
        Assert.Equal(2, kit.Http.Requests.Count);
    }
}
