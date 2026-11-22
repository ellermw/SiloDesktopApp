using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// The pages that did not yet use the shared environment mechanism: Sign-in settings (<c>signup.enabled</c>), the MDBList key on Providers, the AI providers a deployment
/// declares (official #4668) and the translated embedding lock conflict (official #4442). The variable names are synthetic.
/// </summary>
public sealed class AdminEnvRemainingPagesTests
{
    private const string Synthetic = "SILO_FIXTURE_VALUE";

    // ── Sign-in settings ─────────────────────────────────────────────

    private sealed class SignInServer : IDisposable
    {
        public readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        public readonly AdminMonitorAuthority Authority;
        public readonly AdminSignInController Controller;
        private readonly AdminServerSettingsStore _store;
        public string Status = """{"configured":[],"managed_by_env":[]}""";
        public bool StatusFails;
        public int PutStatus = 200;
        public readonly List<string> PutBodies = [];
        public SignInServer()
        {
            Authority = new(Kit.Client, Kit.Auth);
            var settings = new AdminServerSettingsApi(Kit.Client);
            _store = new(settings, Authority, AdminSignInController.Keys, []);
            Controller = new(new AdminSignInApi(Kit.Client), settings, _store, Authority);
            Kit.Http.Respond = async (request, ct) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method == HttpMethod.Put && path == "/api/v2/admin/settings")
                {
                    var body = await request.Content!.ReadAsStringAsync(ct); PutBodies.Add(body);
                    if (PutStatus != 200) return AdminMonitorKit.Json("""{"type":"https://siloserver.org/docs/api/v2/problems/validation_failed","status":422}""", (HttpStatusCode)PutStatus);
                    using var parsed = JsonDocument.Parse(body);
                    return Reply("{\"values\":" + parsed.RootElement.GetProperty("values").GetRawText() + ",\"restart_required\":false}", "\"settings-2\"");
                }
                return path switch
                {
                    "/api/v2/admin/settings/effective" => Reply("""{"auth.local_password_login":"true","auth.email_auto_match":"false","auth.user_api_keys":"false","signup.enabled":"false","password_reset.self_service_enabled":"false","server.public_url":"https://fixture.invalid"}"""),
                    "/api/v2/admin/settings/sensitive-status" => StatusFails ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : Reply(Status),
                    "/api/v2/admin/settings/restart-keys" => Reply("""{"keys":[],"prefixes":[]}"""),
                    "/api/v2/auth/external-sign-in/capabilities" => Reply("""{"available":true,"break_glass":true,"connection_test":true,"live_provider_changes":true}"""),
                    "/api/v2/admin/users" => Reply("""{"items":[{"id":"1","username":"fixture","role":"admin","enabled":true,"is_owner":true,"break_glass":true,"password_login":true}],"page":{"has_more":false}}"""),
                    "/api/v2/admin/plugins/installations" => Reply("""{"items":[]}"""),
                    _ => Reply("{}"),
                };
            };
        }
        private static HttpResponseMessage Reply(string body, string tag = "\"fixture-1\"") { var response = AdminMonitorKit.Json(body); response.Headers.TryAddWithoutValidation("ETag", tag); return response; }
        public void Dispose() { Controller.Dispose(); _store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private static string SignUpManaged => $$$"""{"configured":[],"managed_by_env":["signup.enabled"],"managed_by_env_vars":{"signup.enabled":"{{{Synthetic}}}"}}""";

    [Fact]
    public async Task SignInSettingNamesTheVariableAndAManagedSwitchIsNeverSentWhileAnUnmanagedOneBesideItSaves()
    {
        using var server = new SignInServer { Status = SignUpManaged };
        await server.Controller.OpenAsync();
        Assert.True(server.Controller.Loaded);
        Assert.Equal("Set by SILO_FIXTURE_VALUE in the server’s environment.", server.Controller.EnvironmentNote("signup.enabled"));
        Assert.Null(server.Controller.EnvironmentNote("password_reset.self_service_enabled"));
        Assert.Equal(["signup.enabled"], server.Controller.Managed);

        server.Controller.Set("signup.enabled", true);
        Assert.False(server.Controller.Dirty("signup.enabled"));
        server.Controller.Set("password_reset.self_service_enabled", true);
        Assert.True(server.Controller.Dirty("password_reset.self_service_enabled"));
        await server.Controller.SaveAsync(true);

        var body = Assert.Single(server.PutBodies);
        Assert.Contains("password_reset.self_service_enabled", body);
        Assert.DoesNotContain("signup.enabled", body);
        Assert.Equal(0, server.Controller.DirtyCount);
    }

    [Fact]
    public async Task SignInDraftOfASwitchTheEnvironmentTookOverIsDroppedByTheRefusalAndTheRestSaves()
    {
        using var server = new SignInServer();
        await server.Controller.OpenAsync();
        server.Controller.Set("signup.enabled", true);
        server.Controller.Set("password_reset.self_service_enabled", true);
        Assert.Equal(2, server.Controller.DirtyCount);

        server.Status = SignUpManaged; server.PutStatus = 422;
        await server.Controller.SaveAsync(true);
        Assert.Contains("signup.enabled", server.PutBodies[0]);
        Assert.Equal(1, server.Controller.DirtyCount);
        Assert.False(server.Controller.Dirty("signup.enabled"));
        Assert.Equal("false", server.Controller.Value("signup.enabled"));
        Assert.Equal("Set by SILO_FIXTURE_VALUE in the server’s environment.", server.Controller.EnvironmentNote("signup.enabled"));

        server.PutStatus = 200;
        await server.Controller.SaveAsync(true);
        Assert.Equal(2, server.PutBodies.Count);
        Assert.DoesNotContain("signup.enabled", server.PutBodies[1]);
        Assert.Contains("password_reset.self_service_enabled", server.PutBodies[1]);
    }

    [Fact]
    public async Task SignInFailedStatusReReadKeepsTheLastKnownLockAndOpeningAgainStartsFromNothing()
    {
        using var server = new SignInServer { Status = SignUpManaged };
        await server.Controller.OpenAsync();
        server.StatusFails = true;
        server.Controller.Set("password_reset.self_service_enabled", true);
        await server.Controller.SaveAsync(true);
        Assert.Single(server.PutBodies);
        Assert.Equal(["signup.enabled"], server.Controller.Managed);
        server.Controller.Set("signup.enabled", true);
        Assert.False(server.Controller.Dirty("signup.enabled"));
        Assert.NotNull(server.Controller.EnvironmentNote("signup.enabled"));

        // The first read of a new open must answer; a list from before never carries over.
        server.StatusFails = false; server.Status = """{"configured":[],"managed_by_env":[]}""";
        await server.Controller.OpenAsync();
        Assert.Empty(server.Controller.Managed);
        Assert.Null(server.Controller.EnvironmentNote("signup.enabled"));
    }

    // ── Providers: the MDBList key ───────────────────────────────────

    private sealed class ProvidersServer : IDisposable
    {
        public readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        public readonly AdminMonitorAuthority Authority;
        public readonly AdminProvidersController Controller;
        public string Status = """{"configured":["mdblist.api_key"],"managed_by_env":[]}""";
        public bool StatusFails;
        public readonly List<string> Writes = [];
        public ProvidersServer()
        {
            Authority = new(Kit.Client, Kit.Auth);
            var settings = new AdminServerSettingsApi(Kit.Client);
            var store = new AdminServerSettingsStore(settings, Authority, AdminProvidersController.Keys, AdminProvidersController.Secrets);
            Controller = new(new AdminProvidersApi(Kit.Client), settings, store, Authority);
            Kit.Http.Respond = async (request, ct) =>
            {
                if (request.Method != HttpMethod.Get) { Writes.Add(request.Method + " " + request.RequestUri!.AbsolutePath + " " + (request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct))); return AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError); }
                var path = request.RequestUri!.AbsolutePath;
                HttpResponseMessage response =
                    path.EndsWith("/subtitle-providers") ? AdminMonitorKit.Json("""{"providers":[]}""")
                    : path.EndsWith("/markers/providers") ? AdminMonitorKit.Json("""{"providers":[]}""")
                    : path.EndsWith("/plugins/installations") ? AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}""")
                    : path.EndsWith("effective") ? AdminMonitorKit.Json("""{"markers.mode":"both"}""")
                    : path.EndsWith("sensitive-status") ? (StatusFails ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : AdminMonitorKit.Json(Status))
                    : AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
                response.Headers.ETag = new("\"one\"");
                return response;
            };
        }
        public void Dispose() { Controller.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private static string MdblistManaged => $$$"""{"configured":["mdblist.api_key"],"managed_by_env":["mdblist.api_key"],"managed_by_env_vars":{"mdblist.api_key":"{{{Synthetic}}}"}}""";

    [Fact]
    public async Task ProvidersMdblistKeyNamesTheVariableAndTakesNoDraft()
    {
        using var server = new ProvidersServer { Status = MdblistManaged };
        await server.Controller.OpenAsync();
        Assert.Equal(["mdblist.api_key"], server.Controller.State.ManagedByEnv);
        Assert.Equal("Set by SILO_FIXTURE_VALUE in the server’s environment.", server.Controller.State.EnvManaged.Note("mdblist.api_key"));
        server.Controller.SetMdblistApiKey("synthetic-typed-key");
        Assert.Empty(server.Controller.State.DirtyKeys);
        await server.Controller.SaveMdblistAsync();
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task ProvidersMdblistDraftIsDroppedWhenAReloadFindsTheKeyManagedAndAFailedReReadKeepsTheLock()
    {
        using var server = new ProvidersServer();
        await server.Controller.OpenAsync();
        server.Controller.SetMdblistApiKey("synthetic-typed-key");
        Assert.Equal(["mdblist.api_key"], server.Controller.State.DirtyKeys);

        server.Status = MdblistManaged;
        await server.Controller.ReloadAsync(preserveDraft: true);
        Assert.Empty(server.Controller.State.DirtyKeys);
        Assert.True(server.Controller.State.EnvManaged.IsManaged("mdblist.api_key"));

        server.StatusFails = true;
        await server.Controller.ReloadAsync(preserveDraft: true);
        Assert.True(server.Controller.State.SensitiveStatusError);
        Assert.True(server.Controller.State.EnvManaged.IsManaged("mdblist.api_key"));
        Assert.Contains("mdblist.api_key", server.Controller.State.ManagedByEnv);
        server.Controller.SetMdblistApiKey("synthetic-typed-key");
        Assert.Empty(server.Controller.State.DirtyKeys);
        Assert.Empty(server.Writes);
    }

    // ── AI providers a deployment declares (official #4668) ──────────

    private static async Task<AdminAiProviders> ReadProvidersAsync(string providersJson)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"providers\":[" + providersJson + "],\"types\":[{\"type\":\"openai\",\"label\":\"OpenAI\",\"default_base_url\":\"https://api.example.invalid/v1\",\"capabilities\":[\"chat\"],\"key_required\":true}]}"));
        return await new AdminIntegrationsApi(kit.Client).GetProvidersAsync(lease);
    }

    private static string ProviderJson(string id, string extra = "") =>
        $$"""{"id":"{{id}}","type":"openai","name":"Provider {{id}}","base_url":"https://api.example.invalid/v1","has_api_key":true,"capabilities":["chat"],"used_by":["subtitles"]{{extra}}}""";

    [Fact]
    public async Task ADeclaredProviderCarriesItsVariableAndNoteWhileAnAdminOneDoesNot()
    {
        var read = await ReadProvidersAsync(ProviderJson("declared", $$""","managed_by_env_var":"{{Synthetic}}" """) + "," + ProviderJson("admin") + "," + ProviderJson("empty", ""","managed_by_env_var":"" """));
        var declared = read.Providers.Single(provider => provider.Id == "declared");
        Assert.True(declared.DeclaredByEnvironment);
        Assert.Equal(Synthetic, declared.EnvironmentVariable);
        Assert.Equal("Set by SILO_FIXTURE_VALUE in the server’s environment.", declared.EnvironmentNote);
        var admin = read.Providers.Single(provider => provider.Id == "admin");
        Assert.False(admin.DeclaredByEnvironment);
        Assert.Null(admin.EnvironmentNote);
        // Official locks on a present value even when it is empty ("managedByEnv != null"); the note then has no name to give.
        var empty = read.Providers.Single(provider => provider.Id == "empty");
        Assert.True(empty.DeclaredByEnvironment);
        Assert.Null(empty.EnvironmentVariable);
        Assert.Equal("Set by the environment. Change it there and restart the affected service.", empty.EnvironmentNote);
    }

    [Fact]
    public async Task ADeclaredProvidersVariableThatIsNotAPlainNameIsNotShown()
    {
        var read = await ReadProvidersAsync(ProviderJson("odd", ",\"managed_by_env_var\":\"A B" + (char)0x202E + "C\""));
        var odd = Assert.Single(read.Providers);
        Assert.True(odd.DeclaredByEnvironment);
        Assert.Null(odd.EnvironmentVariable);
    }

    // ── The embedding lock conflict (official #4442) ─────────────────

    [Theory]
    [InlineData("web.admin.common.connectionCheck.result.embeddingModelMismatch", """{"locked_model":"model-a","model":"model-b"}""", "Embeddings were created with model \"model-a\", not \"model-b\". Reset embeddings before switching models.")]
    [InlineData("web.admin.common.connectionCheck.result.embeddingDimensionsMismatch", """{"locked":"768","current":"1024"}""", "Embeddings were created with 768-dimension vectors, but the model now returns 1024. Reset embeddings before switching models.")]
    [InlineData("web.admin.common.connectionCheck.result.embeddingStorageMismatch", """{"stored":"1536","supported":"3072"}""", "Embeddings were stored at 1536 dimensions; this server stores 3072. Reset embeddings.")]
    [InlineData("web.admin.common.connectionCheck.result.embeddingLockOutOfRange", """{"locked":"4096","max":"3072"}""", "The embedding lock records 4096-dimension vectors; Silo stores 1 to 3072. Reset embeddings and use a model that returns at most 3072 dimensions.")]
    [InlineData("web.admin.recommendations.embedding.lockConflictNoProvider", """{"locked_model":"model-a"}""", "No embedding provider is chosen. Choose a provider that serves \"model-a\", or reset embeddings to use another model.")]
    public async Task AKeyedLockConflictShowsTheCatalogTextWithItsArguments(string key, string args, string expected)
    {
        var status = await ReadStatusAsync("{\"lock_conflict\":\"English from the server\",\"lock_conflict_key\":\"" + key + "\",\"lock_conflict_args\":" + args + "}");
        Assert.Equal(key, status.LockConflictKey);
        Assert.Equal("English from the server", status.LockConflict);
        Assert.Equal(expected, AdminRecommendationLockConflict.Text(status));
    }

    [Theory]
    [InlineData("""{"lock_conflict":"English from the server"}""")]
    [InlineData("""{"lock_conflict":"English from the server","lock_conflict_key":"web.admin.settings.fields.managedByEnv","lock_conflict_args":{"env":"X"}}""")]
    [InlineData("""{"lock_conflict":"English from the server","lock_conflict_key":"web.admin.common.connectionCheck.result.embeddingModelMismatch","lock_conflict_args":{"model":"model-b"}}""")]
    [InlineData("""{"lock_conflict":"English from the server","lock_conflict_key":"web.admin.common.connectionCheck.result.embeddingModelMismatch"}""")]
    [InlineData("""{"lock_conflict":"English from the server","lock_conflict_key":"web.admin.common.connectionCheck.result.embeddingModelMismatch","lock_conflict_args":{"locked_model":"","model":"b"}}""")]
    public async Task WithoutAKnownKeyAndEveryArgumentTheServersEnglishTextIsShown(string json)
    {
        var status = await ReadStatusAsync(json);
        Assert.Equal("English from the server", AdminRecommendationLockConflict.Text(status));
    }

    [Fact]
    public async Task ARecommendationsStatusWithoutAConflictStaysEmpty()
    {
        var status = await ReadStatusAsync("""{"lock_conflict":""}""");
        Assert.Equal("", AdminRecommendationLockConflict.Text(status));
        Assert.Null(status.LockConflictKey);
        Assert.Null(status.LockConflictArgs);
    }

    private static async Task<AdminRecommendationStatus> ReadStatusAsync(string conflictFields)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var body = System.Text.Json.Nodes.JsonNode.Parse("""{"embeddings":{"running":false,"count":1},"taste_profiles":{"running":false,"count":1},"cowatch":{"running":false,"count":1},"recommendations":{"running":false,"count":1},"stale_sweep":{"pending":0}}""")!.AsObject();
        foreach (var (name, value) in System.Text.Json.Nodes.JsonNode.Parse(conflictFields)!.AsObject().ToArray()) body[name] = value?.DeepClone();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body.ToJsonString()));
        return await new AdminIntegrationsApi(kit.Client).GetRecommendationsStatusAsync(lease);
    }
}
