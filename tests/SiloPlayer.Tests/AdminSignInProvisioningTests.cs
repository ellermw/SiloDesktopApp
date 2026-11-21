using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Settings → Sign-in against official 02a9d1d3e1 (#4602): a sign-in provider the deployment provisions. Synthetic values only; every request body is read back.
/// Absent ownership fields mean an older server: nothing is managed.
/// </summary>
public sealed class AdminSignInProvisioningTests
{
    private const string Local = "auth.local_password_login";

    /// <summary>One OIDC installation: client secret + issuer + a display name entry, with the deployment owning what a test names.</summary>
    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        private readonly AdminServerSettingsStore _store;
        internal readonly AdminSignInController Controller;
        internal readonly List<string> Writes = [];
        internal readonly Dictionary<string, string> Bodies = [];
        internal string? CapabilityManaged, BindingManaged, ConfigManagedIssuer = null, ConfigManagedSecret = null, ConfigManagedUsername = null;
        internal bool BindingExists = true, BindingEnabled = false, DefaultLogin = false, Network = false, Pending = false, OwnershipObject = true;
        internal int RefusalStatus;
        /// <summary>The refusal answers only the write whose path ends with this (all writes when null).</summary>
        internal string? RefusalOnly;
        internal bool AutoProvision = true, OmitDefaultLogin, FailInstallationsRead, AccountQualifies = true, DuplicateSchema;
        internal int DisplayOrder = 4;
        internal TaskCompletionSource? BindingGate;
        internal string? RefusalBody;
        internal Harness()
        {
            Authority = new(Kit.Client, Kit.Auth); var settings = new AdminServerSettingsApi(Kit.Client);
            _store = new(settings, Authority, [Local], []);
            Controller = new(new AdminSignInApi(Kit.Client), settings, _store, Authority);
            Kit.Http.Respond = async (request, ct) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method != HttpMethod.Get)
                {
                    var key = request.Method + " " + path; Writes.Add(key); Bodies[key] = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
                    if (BindingGate != null && path.EndsWith("/auth-binding")) await BindingGate.Task;
                    if (RefusalStatus != 0 && (RefusalOnly == null || path.EndsWith(RefusalOnly))) return AdminMonitorKit.Json(RefusalBody ?? "{}", (HttpStatusCode)RefusalStatus);
                    if (path.EndsWith("/test")) return Reply("""{"ok":true,"steps":[]}""");
                    if (path == "/api/v2/admin/settings") { using var body = JsonDocument.Parse(Bodies[key]); return Reply("{\"values\":" + body.RootElement.GetProperty("values").GetRawText() + ",\"restart_required\":false}", "\"settings-2\""); }
                    return new(HttpStatusCode.NoContent);
                }
                return path switch
                {
                    "/api/v2/admin/settings/effective" => Reply("""{"auth.local_password_login":"true","auth.email_auto_match":"false","auth.user_api_keys":"false","signup.enabled":"false","password_reset.self_service_enabled":"false","server.public_url":"https://fixture.invalid"}"""),
                    "/api/v2/admin/settings/sensitive-status" => Reply("""{"configured":[],"managed_by_env":[]}"""),
                    "/api/v2/admin/settings/restart-keys" => Reply("""{"keys":[],"prefixes":[]}"""),
                    "/api/v2/auth/external-sign-in/capabilities" => Reply("""{"available":true,"break_glass":true,"connection_test":true,"live_provider_changes":true}"""),
                    "/api/v2/admin/users" => Reply("""{"items":[{"id":"1","username":"fixture","role":"admin","enabled":true,"is_owner":true,"break_glass":true,"password_login":true}],"page":{"has_more":false}}"""),
                    "/api/v2/admin/users/1" => Reply("{\"id\":\"1\",\"username\":\"fixture\",\"role\":\"admin\",\"enabled\":true,\"is_owner\":true,\"break_glass\":" + (AccountQualifies ? "true" : "false") + ",\"password_login\":true}"),
                    "/api/v2/admin/plugins/installations" => FailInstallationsRead ? AdminMonitorKit.Json("{}", HttpStatusCode.BadRequest) : Reply(Inventory()),
                    _ => Reply("{}"),
                };
            };
        }
        private static HttpResponseMessage Reply(string body, string tag = "\"fixture-1\"") { var response = AdminMonitorKit.Json(body); response.Headers.TryAddWithoutValidation("ETag", tag); return response; }
        private string Inventory()
        {
            var capability = new JsonObject { ["id"] = "oidc", ["type"] = "auth_provider.v1", ["sign_in_mode"] = Network ? "network" : "oauth", ["display_name"] = "Fixture OIDC", ["metadata"] = new JsonObject { ["connection_test"] = true, ["connection_test_username_field"] = "oidc.lookup_user" } };
            if (CapabilityManaged != null) capability["env_managed"] = JsonNode.Parse(CapabilityManaged);
            var binding = new JsonObject { ["capability_id"] = "oidc", ["enabled"] = BindingEnabled, ["display_order"] = DisplayOrder, ["auto_provision"] = AutoProvision, ["default_login"] = DefaultLogin, ["callback_url"] = "https://fixture.invalid/api/v2/auth/oauth/3/callback", ["post_logout_redirect_url"] = "", ["callback_mode"] = "installation", ["callback_pending"] = Pending };
            if (BindingManaged != null) binding["env_managed"] = JsonNode.Parse(BindingManaged);
            if (OmitDefaultLogin) binding.Remove("default_login");
            var install = new JsonObject
            {
                ["id"] = "3", ["plugin_id"] = "fixture-oidc", ["enabled"] = true,
                ["capabilities"] = new JsonArray(capability), ["auth_bindings"] = BindingExists ? new JsonArray(binding) : new JsonArray(),
                ["global_config_schema"] = new JsonArray(new JsonObject
                {
                    ["key"] = "oidc", ["title"] = "Provider", ["description"] = "", ["required"] = true,
                    ["json_schema"] = """{"type":"object","properties":{"issuer":{"type":"string"},"client_secret":{"type":"string","writeOnly":true},"lookup_user":{"type":"string"},"note":{"type":"string"}}}""",
                    ["admin_form"] = new JsonObject { ["fields"] = new JsonArray(
                        new JsonObject { ["key"] = "issuer", ["label"] = "Issuer URL", ["control"] = "TEXT", ["required"] = true, ["secret"] = false },
                        new JsonObject { ["key"] = "client_secret", ["label"] = "Client secret", ["control"] = "PASSWORD", ["required"] = false, ["secret"] = true, ["multiline"] = true },
                        new JsonObject { ["key"] = "lookup_user", ["label"] = "Look-up user", ["control"] = "TEXT", ["required"] = false, ["secret"] = false },
                        new JsonObject { ["key"] = "note", ["label"] = "Note", ["control"] = "TEXT", ["required"] = false, ["secret"] = false }), ["sections"] = new JsonArray() },
                }),
                ["global_configs"] = new JsonArray(new JsonObject { ["key"] = "oidc", ["value"] = new JsonObject { ["issuer"] = "https://issuer.invalid", ["lookup_user"] = "saved-user", ["note"] = "n" }, ["configured_secrets"] = new JsonArray("client_secret") }),
            };
            if (ConfigManagedIssuer != null || ConfigManagedSecret != null || ConfigManagedUsername != null || OwnershipObject && BindingManaged != null)
            {
                var fields = new JsonArray(); foreach (var f in new[] { ConfigManagedIssuer, ConfigManagedSecret, ConfigManagedUsername }) if (f != null) fields.Add(f);
                install["env_managed"] = new JsonObject { ["config_fields"] = new JsonObject { ["oidc"] = fields }, ["binding_fields"] = new JsonObject() };
            }
            // A plugin's schema list is passed through unchecked: the same entry key can appear twice.
            if (DuplicateSchema) { var schemas = (JsonArray)install["global_config_schema"]!; var copy = JsonNode.Parse(schemas[0]!.ToJsonString())!; copy["title"] = "Provider again"; schemas.Add(copy); }
            return new JsonObject { ["items"] = new JsonArray(install) }.ToJsonString();
        }
        internal AdminSignInProvider Provider => Controller.Providers.Single();
        internal AdminSignInSchema Schema => Provider.GlobalConfigSchema.First();
        internal AdminSignInField Field(string key) => Schema.AdminForm!.Fields.Single(f => f.Key == key);
        internal Task Open() => Controller.OpenAsync();
        internal JsonDocument Body(string key) => JsonDocument.Parse(Bodies[key]);
        public void Dispose() { Controller.Dispose(); _store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private const string ConfigWrite = "PUT /api/v2/admin/plugins/installations/3/config", BindingWrite = "PUT /api/v2/admin/plugins/installations/3/auth-binding", TestWrite = "POST /api/v2/admin/plugins/installations/3/auth-binding/test";

    // ── The contract ──

    [Fact]
    public async Task ProvisionedFieldsAreReadFromTheCapabilityTheBindingAndTheInstallation()
    {
        using var h = new Harness { CapabilityManaged = """["enabled","callback_mode"]""", BindingManaged = """["enabled","callback_mode"]""", ConfigManagedIssuer = "issuer", Pending = true };
        await h.Open();
        Assert.True(h.Provider.BindingFieldManaged("enabled")); Assert.True(h.Provider.BindingFieldManaged("callback_mode")); Assert.False(h.Provider.BindingFieldManaged("auto_provision"));
        Assert.Equal(["enabled", "callback_mode"], h.Provider.Binding!.EnvManaged);
        Assert.True(h.Provider.Binding.CallbackPending); Assert.Equal("installation", h.Provider.Binding.CallbackMode);
        Assert.Equal(["issuer"], h.Provider.ManagedConfigFields("oidc")); Assert.True(h.Provider.ConfigFieldManaged("oidc", "issuer")); Assert.False(h.Provider.ConfigFieldManaged("oidc", "note"));
    }

    [Fact]
    public async Task ABindingFieldIsManagedBeforeAnyBindingExists()
    {
        // The capability names the field even when no binding row exists yet (official authBindingFieldIsManaged reads the capability, not the binding).
        using var h = new Harness { CapabilityManaged = """["enabled"]""", BindingExists = false };
        await h.Open();
        Assert.Null(h.Provider.Binding); Assert.True(h.Provider.BindingFieldManaged("enabled"));
    }

    [Fact]
    public async Task AnOlderServerThatSendsNoOwnershipManagesNothing()
    {
        using var h = new Harness { OwnershipObject = false };
        await h.Open();
        Assert.Empty(h.Provider.Auth!.EnvManaged); Assert.Empty(h.Provider.Binding!.EnvManaged); Assert.Null(h.Provider.EnvManaged);
        Assert.False(h.Provider.BindingFieldManaged("enabled")); Assert.Empty(h.Provider.ManagedConfigFields("oidc")); Assert.False(h.Provider.Binding.CallbackPending);
    }

    [Theory]
    [InlineData("""{"env_managed":null,"auth_bindings":[{"capability_id":"oidc","env_managed":null,"enabled":false,"display_order":1,"auto_provision":true,"default_login":false}]}""")]
    [InlineData("""{"env_managed":{"config_fields":null,"binding_fields":null}}""")]
    [InlineData("""{"env_managed":{}}""")]
    public void MalformedOwnershipReadsAsNothingManagedAndDoesNotThrow(string extra)
    {
        var json = JsonNode.Parse("""{"id":"3","plugin_id":"x","enabled":true,"capabilities":[{"id":"oidc","type":"auth_provider.v1","env_managed":null}]}""")!.AsObject();
        foreach (var (key, value) in JsonNode.Parse(extra)!.AsObject().ToArray()) { json.Remove(key); json[key] = value?.DeepClone(); }
        var provider = JsonSerializer.Deserialize<AdminSignInProvider>(json.ToJsonString(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        Assert.False(provider.BindingFieldManaged("enabled")); Assert.Empty(provider.ManagedConfigFields("oidc"));
    }

    [Fact]
    public void ProviderLabelFollowsTheOfficialOrder()
    {
        AdminSignInProvider Make(string? saved, string? declared, string? presentation, string? capability, string plugin = "plugin-id")
        {
            var provider = new AdminSignInProvider { Id = "1", PluginId = plugin, Capabilities = [new() { Type = "auth_provider.v1", Id = "a", DisplayName = capability ?? "" }] };
            if (declared != null) provider.Capabilities[0].Metadata["display_name"] = declared;
            if (presentation != null) provider.Presentation = new() { DisplayName = presentation };
            if (saved != null) provider.GlobalConfigs.Add(new() { Key = "display_name", Value = new JsonObject { ["value"] = saved } });
            return provider;
        }
        Assert.Equal("Saved", Make("  Saved ", "Declared", "Presented", "Capability").Name);
        Assert.Equal("Declared", Make(" ", "Declared", "Presented", "Capability").Name);
        Assert.Equal("Capability", Make(null, " ", "Presented", "Capability").Name);
        Assert.Equal("Presented", Make(null, null, "Presented", "").Name);
        Assert.Equal("plugin-id", Make(null, null, null, "").Name);
        Assert.Equal("Presented", Make("Saved", null, "Presented", "Capability").PluginName);
    }

    // ── Nothing the deployment owns is ever written ──

    [Fact]
    public async Task AManagedConfigurationFieldCannotBeEditedStagedOrSent()
    {
        using var h = new Harness { ConfigManagedIssuer = "issuer", ConfigManagedSecret = "client_secret" };
        await h.Open();
        h.Controller.SetField(h.Provider, h.Schema, h.Field("issuer"), "https://changed.invalid");
        h.Controller.SetField(h.Provider, h.Schema, h.Field("client_secret"), "synthetic-typed-secret");
        h.Controller.ToggleClear(h.Provider, h.Schema, h.Field("client_secret"));
        Assert.Equal(0, h.Controller.DirtyCount); Assert.False(h.Controller.FieldDirty(h.Provider, "oidc", "issuer")); Assert.False(h.Controller.Clearing(h.Provider, "oidc", "client_secret"));
        // A different field is still editable, and the write names only what the admin changed and the deployment does not own.
        h.Controller.SetField(h.Provider, h.Schema, h.Field("note"), "changed note");
        await h.Controller.SaveAsync(true);
        using var body = h.Body(ConfigWrite);
        var value = body.RootElement.GetProperty("value");
        Assert.Equal("changed note", value.GetProperty("note").GetString());
        Assert.False(value.TryGetProperty("issuer", out _)); Assert.False(value.TryGetProperty("client_secret", out _));
        Assert.Empty(body.RootElement.GetProperty("clear_secrets").EnumerateArray());
        Assert.DoesNotContain("synthetic-typed-secret", h.Bodies[ConfigWrite]);
    }

    [Fact]
    public async Task AConnectionTestNeverSendsAManagedFieldOrTheLookUpUserTheDeploymentOwns()
    {
        using var h = new Harness { ConfigManagedIssuer = "issuer", ConfigManagedUsername = "lookup_user" };
        await h.Open();
        // An edit is staged first so the body carries an entry: the assertion below then runs over something real.
        h.Controller.SetField(h.Provider, h.Schema, h.Field("note"), "staged for the test");
        await h.Controller.TestAsync(h.Provider, "typed-lookup-user");
        using var body = h.Body(TestWrite);
        var sent = body.RootElement.GetProperty("config").EnumerateArray().Select(entry => entry.GetProperty("value")).ToArray();
        Assert.NotEmpty(sent);
        Assert.Contains(sent, value => value.TryGetProperty("note", out var note) && note.GetString() == "staged for the test");
        Assert.All(sent, value => { Assert.False(value.TryGetProperty("issuer", out _)); Assert.False(value.TryGetProperty("lookup_user", out _)); });
        Assert.DoesNotContain("typed-lookup-user", h.Bodies[TestWrite]);
    }

    [Fact]
    public async Task AnUnmanagedLookUpUserIsStillSentOnlyToTheTest()
    {
        using var h = new Harness();
        await h.Open();
        await h.Controller.TestAsync(h.Provider, "typed-lookup-user");
        Assert.Contains("typed-lookup-user", h.Bodies[TestWrite]);
        Assert.Equal(0, h.Controller.DirtyCount);
    }

    [Fact]
    public async Task AManagedEnabledFlagIsNeverWrittenAndNothingIsSent()
    {
        using var h = new Harness { CapabilityManaged = """["enabled"]""", BindingManaged = """["enabled"]""" };
        await h.Open();
        await h.Controller.SetBindingAsync(h.Provider, true, null, true);
        Assert.DoesNotContain(BindingWrite, h.Writes);
        // Nothing was sent, but the page offered what the environment now owns: it waits for a reload and says why.
        Assert.StartsWith("Set by the environment. Change it there and restart the affected service.", h.Controller.Message);
        Assert.Contains("The environment now manages something this page showed as editable.", h.Controller.Message); Assert.Contains("Reload the page", h.Controller.Message);
        Assert.True(h.Controller.ReviewRequired);
        Assert.Null(h.Controller.LastChange);
    }

    [Fact]
    public async Task AManagedAutoProvisionIsNotStagedAndNotSaved()
    {
        using var h = new Harness { CapabilityManaged = """["auto_provision"]""", BindingManaged = """["auto_provision"]""" };
        await h.Open();
        h.Controller.SetAutoProvision(h.Provider, false);
        Assert.Equal(0, h.Controller.DirtyCount); Assert.True(h.Controller.AutoProvision(h.Provider));
        await h.Controller.SaveAsync(true);
        Assert.Empty(h.Writes);
    }

    [Fact]
    public async Task AnUnmanagedBindingWriteSendsTheSavedRowWithOnlyTheChosenChange()
    {
        using var h = new Harness { DefaultLogin = true };
        await h.Open();
        await h.Controller.SetBindingAsync(h.Provider, true, null, true);
        using var body = h.Body(BindingWrite);
        var root = body.RootElement;
        Assert.True(root.GetProperty("enabled").GetBoolean()); Assert.Equal("oidc", root.GetProperty("capability_id").GetString());
        Assert.Equal(4, root.GetProperty("display_order").GetInt32()); Assert.True(root.GetProperty("auto_provision").GetBoolean()); Assert.True(root.GetProperty("default_login").GetBoolean());
        Assert.False(root.TryGetProperty("callback_mode", out _)); // an omitted mode keeps the stored one
        Assert.NotNull(h.Controller.LastChange);
    }

    [Fact]
    public async Task ANetworkBindingKeepsItsSavedDefaultLoginInsteadOfForcingItOff()
    {
        // Official 02a9d1d3e1 writes the binding's own default_login for a network sign-in (it used to write false).
        using var h = new Harness { Network = true, DefaultLogin = true, BindingEnabled = true };
        await h.Open();
        await h.Controller.SetBindingAsync(h.Provider, true, false, true);
        using var body = h.Body(BindingWrite);
        Assert.True(body.RootElement.GetProperty("default_login").GetBoolean()); Assert.False(body.RootElement.GetProperty("auto_provision").GetBoolean());
    }

    [Fact]
    public async Task ARefusalOfAManagedFieldIsShownAsTheServersSentenceKeepsTheDraftAndSendsOnce()
    {
        using var h = new Harness { RefusalStatus = 422, RefusalBody = """{"type":"https://siloserver.org/docs/api/v2/problems/validation_failed","status":422,"errors":[{"location":"body.auth_binding.enabled","code":"managed_by_environment","detail":"synthetic server text that must not be shown"}]}""" };
        await h.Open();
        h.Controller.SetField(h.Provider, h.Schema, h.Field("note"), "kept draft"); h.Controller.SetField(h.Provider, h.Schema, h.Field("client_secret"), "synthetic-typed-secret");
        await h.Controller.SaveAsync(true); await h.Controller.SaveAsync(true);
        Assert.Equal(1, h.Writes.Count(w => w == ConfigWrite));
        Assert.StartsWith("Set by the environment. Change it there and restart the affected service.", h.Controller.Message);
        Assert.Contains("Reload the page", h.Controller.Message); Assert.Contains("A typed secret was cleared", h.Controller.Message);
        // The visible draft stays; the typed secret goes (the page is locked until a reload, so it could not be used).
        Assert.True(h.Controller.FieldDirty(h.Provider, "oidc", "note")); Assert.True(h.Controller.ReviewRequired);
        Assert.False(h.Controller.Values(h.Provider, h.Schema).ContainsKey("client_secret"));
    }

    [Fact]
    public async Task ADeploymentClaimOnAFieldWithAStagedEditDropsTheEditAtOnce()
    {
        using var h = new Harness();
        await h.Open();
        h.Controller.SetField(h.Provider, h.Schema, h.Field("note"), "staged"); h.Controller.SetAutoProvision(h.Provider, false);
        Assert.Equal(2, h.Controller.DirtyCount);
        h.ConfigManagedIssuer = null; h.CapabilityManaged = """["auto_provision"]"""; h.BindingManaged = """["auto_provision"]"""; h.ConfigManagedUsername = "note";
        // A write that succeeds re-reads the providers; the new ownership prunes what it now owns.
        await h.Controller.SetBindingAsync(h.Provider, false, null, true);
        Assert.False(h.Controller.FieldDirty(h.Provider, "oidc", "note")); Assert.False(h.Controller.ProviderDirty(h.Provider));
    }

    // ── The form ──

    [Fact]
    public async Task ASecretFieldMarkedMultilineIsAMaskedBox()
    {
        using var h = new Harness();
        await h.Open();
        Assert.True(h.Field("client_secret").Multiline); Assert.True(h.Field("client_secret").IsSecret);
        Assert.Equal(AdminSignInEditor.Secret, AdminSignInSchemaRules.EditorOf(h.Schema, h.Field("client_secret")));
        Assert.Equal(AdminSignInEditor.Text, AdminSignInSchemaRules.EditorOf(h.Schema, h.Field("note")));
        Assert.Equal(AdminSignInEditor.Multiline, AdminSignInSchemaRules.EditorOf(h.Schema, new AdminSignInField { Key = "k", Control = "TEXTAREA" }));
        Assert.Equal(AdminSignInEditor.Multiline, AdminSignInSchemaRules.EditorOf(h.Schema, new AdminSignInField { Key = "k", Multiline = true }));
        Assert.Equal(AdminSignInEditor.Secret, AdminSignInSchemaRules.EditorOf(h.Schema, new AdminSignInField { Key = "k", Control = "TEXTAREA", Secret = true }));
    }

    [Fact]
    public async Task AnUntouchedSecretIsNotSentAndATypedOneIsErasedAfterAnAcceptedSave()
    {
        using var h = new Harness();
        await h.Open();
        h.Controller.SetField(h.Provider, h.Schema, h.Field("note"), "another");
        await h.Controller.SaveAsync(true);
        Assert.DoesNotContain("client_secret", h.Bodies[ConfigWrite]);
        h.Controller.SetField(h.Provider, h.Schema, h.Field("client_secret"), "synthetic-typed-secret");
        Assert.True(h.Controller.Values(h.Provider, h.Schema).ContainsKey("client_secret"));
        await h.Controller.SaveAsync(true);
        Assert.Contains("synthetic-typed-secret", h.Bodies[ConfigWrite]);
        Assert.False(h.Controller.FieldDirty(h.Provider, "oidc", "client_secret")); Assert.Equal(0, h.Controller.DirtyCount);
        Assert.False(h.Controller.Values(h.Provider, h.Schema).ContainsKey("client_secret"));
    }

    [Fact]
    public async Task ALayoutPutsOwnFieldsInStepsTheLoginNameApartAndTheLookUpUserInTheTest()
    {
        using var h = new Harness();
        await h.Open();
        var layout = AdminSignInLayout.Of(h.Provider);
        Assert.Single(layout.Steps); Assert.Equal("Provider", layout.Steps[0].Title);
        Assert.DoesNotContain(layout.Steps[0].Fields, f => f.Key == "lookup_user");
        Assert.Equal("lookup_user", layout.TestUsername!.Field.Key); Assert.Null(layout.LoginName); Assert.Empty(layout.Advanced); Assert.Empty(layout.Unsupported);
        Assert.Equal("Client Id", AdminSignInLayout.SchemaTitle(new AdminSignInSchema { Key = "client_id", Title = "  " }));
    }

    [Fact]
    public async Task ADoubledBindingChangeIsSentOnce()
    {
        using var h = new Harness();
        await h.Open();
        h.BindingGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = h.Controller.SetBindingAsync(h.Provider, true, null, true);
        while (!h.Writes.Contains(BindingWrite)) await Task.Delay(10);
        var second = h.Controller.SetBindingAsync(h.Provider, true, null, true); // arrives while the first is still in flight
        await second; h.BindingGate.SetResult(); await first;
        Assert.Equal(1, h.Writes.Count(w => w == BindingWrite));
    }

    [Fact]
    public async Task AFailedConnectionTestKeepsTheFallbackAndNeverShowsServerOrTypedText()
    {
        using var h = new Harness { RefusalStatus = 503, RefusalBody = """{"type":"https://siloserver.org/docs/api/v2/problems/dependency_unavailable","status":503,"detail":"synthetic-typed-secret echoed by a hostile server"}""" };
        await h.Open();
        h.Controller.SetField(h.Provider, h.Schema, h.Field("client_secret"), "synthetic-typed-secret");
        await h.Controller.TestAsync(h.Provider, "typed-user");
        var shown = h.Controller.TestFailures[h.Provider.Id] + "|" + h.Controller.Message;
        Assert.DoesNotContain("synthetic-typed-secret", shown); Assert.DoesNotContain("hostile", shown);
        Assert.False(string.IsNullOrWhiteSpace(h.Controller.TestFailures[h.Provider.Id]));
        Assert.Equal(1, h.Writes.Count(w => w == TestWrite));
        // The next test clears the old failure first, and editing a field clears it too.
        h.RefusalStatus = 0; await h.Controller.TestAsync(h.Provider, null);
        Assert.False(h.Controller.TestFailures.ContainsKey(h.Provider.Id));
    }

    [Fact]
    public async Task ATypedSecretIsErasedWhenTheOutcomeOfTheSaveIsUnknown()
    {
        using var h = new Harness { RefusalStatus = 503 }; // answered after sending: the outcome is unknown
        await h.Open();
        h.Controller.SetField(h.Provider, h.Schema, h.Field("client_secret"), "synthetic-typed-secret"); h.Controller.SetField(h.Provider, h.Schema, h.Field("note"), "kept note");
        await h.Controller.SaveAsync(true);
        Assert.Equal(1, h.Writes.Count(w => w == ConfigWrite)); Assert.True(h.Controller.ReviewRequired);
        Assert.False(h.Controller.Values(h.Provider, h.Schema).ContainsKey("client_secret")); Assert.False(h.Controller.FieldDirty(h.Provider, "oidc", "client_secret"));
        Assert.True(h.Controller.FieldDirty(h.Provider, "oidc", "note")); // only the secret goes; the visible draft stays until the admin reloads or discards
    }

    [Fact]
    public async Task ATypedSecretSurvivesADefiniteRefusal()
    {
        using var h = new Harness { RefusalStatus = 422, RefusalBody = """{"type":"https://siloserver.org/docs/api/v2/problems/validation_failed","status":422}""" };
        await h.Open();
        h.Controller.SetField(h.Provider, h.Schema, h.Field("client_secret"), "synthetic-typed-secret");
        await h.Controller.SaveAsync(true);
        Assert.True(h.Controller.Values(h.Provider, h.Schema).ContainsKey("client_secret")); Assert.True(h.Controller.FieldDirty(h.Provider, "oidc", "client_secret"));
        // The secret is not merely present: the page is editable, so it is still usable. A save after the refusal is lifted sends it.
        Assert.False(h.Controller.ReviewRequired); Assert.False(h.Controller.WritesBlocked);
        h.RefusalStatus = 0;
        await h.Controller.SaveAsync(true);
        Assert.Equal(2, h.Writes.Count(w => w == ConfigWrite)); Assert.Contains("synthetic-typed-secret", h.Bodies[ConfigWrite]);
        Assert.False(h.Controller.Values(h.Provider, h.Schema).ContainsKey("client_secret"));
    }

    [Fact]
    public async Task AStagedClearOfASavedSecretIsNotATypedValueAndSurvivesAnUnknownOutcome()
    {
        using var h = new Harness { RefusalStatus = 503 };
        await h.Open();
        h.Controller.ToggleClear(h.Provider, h.Schema, h.Field("client_secret"));
        await h.Controller.SaveAsync(true);
        Assert.True(h.Controller.Clearing(h.Provider, "oidc", "client_secret"));
    }

    // ── A failed write: does the page stay editable, or must it be read again? Decided per answer, from what actually happened ──
    private const string Secret = "synthetic-typed-secret";
    private static string Problem(string type, int status) => "{\"type\":\"https://siloserver.org/docs/api/v2/problems/" + type + "\",\"status\":" + status + "}";
    private static string ManagedProblem => """{"type":"https://siloserver.org/docs/api/v2/problems/validation_failed","status":422,"errors":[{"location":"body.config.oidc.issuer","code":"managed_by_environment"}]}""";
    private static void Stage(Harness h) { h.Controller.SetField(h.Provider, h.Schema, h.Field("note"), "typed note"); h.Controller.SetField(h.Provider, h.Schema, h.Field("client_secret"), Secret); }
    private static bool HasSecret(Harness h) => h.Controller.Values(h.Provider, h.Schema).ContainsKey("client_secret");

    [Fact]
    public async Task A422ValidationRefusalLeavesThePageEditableKeepsTheDraftAndTheSecretAndACorrectedSaveIsAccepted()
    {
        using var h = new Harness { RefusalStatus = 422, RefusalBody = Problem("validation_failed", 422) };
        await h.Open(); Stage(h);
        await h.Controller.SaveAsync(true);
        Assert.False(h.Controller.ReviewRequired); Assert.False(h.Controller.WritesBlocked);
        Assert.True(h.Controller.FieldDirty(h.Provider, "oidc", "note")); Assert.True(h.Controller.FieldDirty(h.Provider, "oidc", "client_secret")); Assert.True(HasSecret(h));
        Assert.Equal("The server rejected these values. Review the fields before trying again.", h.Controller.Message);
        // The admin corrects a value and saves again: the request goes out with the corrected value and the secret typed before, and only then is the secret erased.
        h.RefusalStatus = 0; h.Controller.SetField(h.Provider, h.Schema, h.Field("note"), "corrected note");
        Assert.True(HasSecret(h));
        await h.Controller.SaveAsync(true);
        Assert.Equal(2, h.Writes.Count(w => w == ConfigWrite)); Assert.Contains("corrected note", h.Bodies[ConfigWrite]); Assert.Contains(Secret, h.Bodies[ConfigWrite]);
        Assert.Equal(0, h.Controller.DirtyCount); Assert.False(HasSecret(h)); Assert.False(h.Controller.ReviewRequired);
    }

    [Fact]
    public async Task A429RefusalLeavesThePageEditableAndKeepsTheSecret()
    {
        using var h = new Harness { RefusalStatus = 429, RefusalBody = Problem("rate_limited", 429) };
        await h.Open(); Stage(h);
        await h.Controller.SaveAsync(true);
        Assert.False(h.Controller.ReviewRequired); Assert.True(HasSecret(h)); Assert.True(h.Controller.FieldDirty(h.Provider, "oidc", "note"));
        Assert.Equal("The server asked you to wait. Try again later.", h.Controller.Message);
    }

    [Fact]
    public async Task AProviderAlreadyEnabledRefusalLeavesThePageEditableAndKeepsWhatIsStaged()
    {
        using var h = new Harness { RefusalStatus = 409, RefusalBody = Problem("provider_already_enabled", 409) };
        await h.Open(); h.Controller.Set("signup.enabled", true);
        await h.Controller.SetBindingAsync(h.Provider, true, null, true);
        Assert.Equal(1, h.Writes.Count(w => w == BindingWrite));
        Assert.False(h.Controller.ReviewRequired); Assert.False(h.Controller.WritesBlocked); Assert.True(h.Controller.Dirty("signup.enabled"));
        Assert.Equal(AdminText.T("web.admin.settings.lib.externalSignIn.problem.providerAlreadyEnabled"), h.Controller.Message); Assert.Null(h.Controller.LastChange);
    }

    [Fact]
    public async Task ABreakGlassRefusalByTheServerWhereNothingWasAppliedLeavesThePageEditable()
    {
        using var h = new Harness { BindingEnabled = true, RefusalOnly = "/api/v2/admin/settings", RefusalStatus = 409, RefusalBody = Problem("break_glass_required", 409) };
        await h.Open(); h.Controller.Set(Local, false); Assert.True(h.Controller.Dirty(Local));
        await h.Controller.SaveAsync(true);
        Assert.Contains("PUT /api/v2/admin/settings", h.Writes); Assert.DoesNotContain(ConfigWrite, h.Writes);
        Assert.False(h.Controller.ReviewRequired); Assert.True(h.Controller.Dirty(Local));
        Assert.Equal("Keep an enabled break-glass administrator with password sign-in before turning off Silo passwords.", h.Controller.Message);
        // The admin turns Silo passwords back on and the page lets them save something else.
        h.Controller.Set(Local, true); Assert.False(h.Controller.Dirty(Local));
    }

    [Fact]
    public async Task ARefusalAfterAnEarlierWriteAlreadyLandedWaitsForAReloadAndSaysWhy()
    {
        // The configuration entry is saved, then the settings write is refused: the page still shows the older values for the entry, so it cannot be trusted.
        using var h = new Harness { BindingEnabled = true, RefusalOnly = "/api/v2/admin/settings", RefusalStatus = 409, RefusalBody = Problem("break_glass_required", 409) };
        await h.Open(); h.Controller.Set(Local, false); Stage(h);
        await h.Controller.SaveAsync(true);
        Assert.Equal(1, h.Writes.Count(w => w == ConfigWrite)); Assert.Contains("PUT /api/v2/admin/settings", h.Writes);
        Assert.True(h.Controller.ReviewRequired);
        Assert.Contains("Part of this change was already saved", h.Controller.Message); Assert.Contains("Reload the page", h.Controller.Message);
        Assert.False(HasSecret(h));
    }

    [Fact]
    public async Task ASettingsRefusalOfItsValuesWhereNothingWasAppliedLeavesThePageEditable()
    {
        using var h = new Harness { RefusalOnly = "/api/v2/admin/settings", RefusalStatus = 422, RefusalBody = Problem("validation_failed", 422) };
        await h.Open(); h.Controller.Set("signup.enabled", true);
        await h.Controller.SaveAsync(true);
        Assert.Contains("PUT /api/v2/admin/settings", h.Writes);
        Assert.False(h.Controller.ReviewRequired); Assert.True(h.Controller.Dirty("signup.enabled"));
    }

    [Fact]
    public async Task AFreshReadThatContradictsTheAccountListRefusesLocallySendsNothingAndKeepsThePageEditable()
    {
        // The list on the page shows a break-glass administrator, the fresh read of that account shows it no longer qualifies: the page's own check refuses, nothing is sent.
        using var h = new Harness { BindingEnabled = true, AccountQualifies = false };
        await h.Open(); h.Controller.Set(Local, false); Stage(h);
        await h.Controller.SaveAsync(true);
        Assert.Empty(h.Writes);
        Assert.False(h.Controller.ReviewRequired); Assert.False(h.Controller.WritesBlocked); Assert.True(HasSecret(h)); Assert.True(h.Controller.FieldDirty(h.Provider, "oidc", "note")); Assert.True(h.Controller.Dirty(Local));
        Assert.StartsWith("Keep an enabled break-glass administrator", h.Controller.Message); Assert.Contains("The account list on this page is out of date", h.Controller.Message);
        // The admin turns Silo passwords back on and saves what they typed: the secret was kept and is sent.
        h.Controller.Set(Local, true); await h.Controller.SaveAsync(true);
        Assert.Equal(1, h.Writes.Count(w => w == ConfigWrite)); Assert.Contains(Secret, h.Bodies[ConfigWrite]);
    }

    [Fact]
    public async Task AProviderListThatNoLongerHasAnEnabledProviderRefusesLocallySendsNothingAndKeepsThePageEditable()
    {
        using var h = new Harness { BindingEnabled = true };
        await h.Open(); h.Controller.Set(Local, false); Stage(h);
        h.BindingEnabled = false; // another session turned the provider off
        await h.Controller.SaveAsync(true);
        Assert.Empty(h.Writes);
        Assert.False(h.Controller.ReviewRequired); Assert.True(HasSecret(h)); Assert.True(h.Controller.Dirty(Local));
        Assert.Contains("The provider list on this page is out of date", h.Controller.Message);
    }

    [Fact]
    public async Task AFailedPreCheckReadWaitsForAReloadSendsNothingAndErasesTheSecret()
    {
        using var h = new Harness { BindingEnabled = true };
        await h.Open(); h.Controller.Set(Local, false); Stage(h);
        h.FailInstallationsRead = true;
        await h.Controller.SaveAsync(true);
        Assert.Empty(h.Writes);
        Assert.True(h.Controller.ReviewRequired); Assert.False(HasSecret(h)); Assert.True(h.Controller.FieldDirty(h.Provider, "oidc", "note"));
        Assert.Contains("The current state could not be read before writing.", h.Controller.Message); Assert.Contains("Reload the page", h.Controller.Message); Assert.Contains("A typed secret was cleared", h.Controller.Message);
    }

    [Fact]
    public async Task AConflictWaitsForAReloadErasesTheSecretAndSaysWhy()
    {
        using var h = new Harness { RefusalStatus = 409, RefusalBody = Problem("conflict", 409) };
        await h.Open(); Stage(h);
        await h.Controller.SaveAsync(true);
        Assert.True(h.Controller.ReviewRequired); Assert.False(HasSecret(h)); Assert.True(h.Controller.FieldDirty(h.Provider, "oidc", "note"));
        Assert.Contains("The resource changed on the server.", h.Controller.Message); Assert.Contains("Reload the page", h.Controller.Message);
    }

    [Fact]
    public async Task ARefusalOfAManagedFieldOnTheConfigurationWriteErasesTheSecret()
    {
        using var h = new Harness { RefusalStatus = 422, RefusalBody = ManagedProblem };
        await h.Open(); Stage(h);
        await h.Controller.SaveAsync(true);
        Assert.True(h.Controller.ReviewRequired); Assert.False(HasSecret(h)); Assert.True(h.Controller.FieldDirty(h.Provider, "oidc", "note"));
    }

    // ── A plugin's form is passed through unchecked: it can repeat or empty a key, or send no fields ──
    private static AdminSignInProvider Plugin(params AdminSignInSchema[] schemas) =>
        new() { Id = "9", PluginId = "p", Enabled = true, Capabilities = [new() { Type = "auth_provider.v1", Id = "cap", SignInMode = "oauth" }], GlobalConfigSchema = [.. schemas] };
    private static AdminSignInField Field(string key, string label, bool secret = false) => new() { Key = key, Label = label, Secret = secret };

    [Fact]
    public void ALayoutKeepsTheFirstOfARepeatedKeyAndToleratesAnEmptyKey()
    {
        var loose = new AdminSignInForm { Fields = [Field("a", "First"), Field("a", "Second"), Field("", "Empty"), Field("b", "B")] };
        var layout = AdminSignInLayout.Of(Plugin(new AdminSignInSchema { Key = "cfg", Title = "T", AdminForm = loose }));
        Assert.Equal(["First", "Empty", "B"], layout.Steps.Single().Fields.Select(f => f.Label));
        var sectioned = new AdminSignInForm { Fields = [Field("a", "First"), Field("a", "Second"), Field("", "Empty"), Field("b", "B")], Sections = [new() { Key = "g", Title = "G", Collapsible = true, FieldKeys = ["a", "a", "", "b"] }] };
        layout = AdminSignInLayout.Of(Plugin(new AdminSignInSchema { Key = "cfg", Title = "T", AdminForm = sectioned }));
        Assert.Empty(layout.Steps); Assert.Equal(["First", "Empty", "B"], layout.Advanced.Single().Fields.Select(f => f.Label));
        // The look-up user named by the plugin is taken from the first occurrence too.
        var provider = Plugin(new AdminSignInSchema { Key = "cfg", Title = "T", AdminForm = loose }); provider.Auth!.Metadata[AdminSignInLayout.TestUsernameMetadata] = "cfg.a";
        layout = AdminSignInLayout.Of(provider);
        Assert.Equal("First", layout.TestUsername!.Field.Label); Assert.Equal(["Empty", "B"], layout.Steps.Single().Fields.Select(f => f.Label));
    }

    [Fact]
    public void ALayoutToleratesAFormWithNoFieldsNoListsAndNoKey()
    {
        var layout = AdminSignInLayout.Of(Plugin(
            new AdminSignInSchema { Key = "empty", AdminForm = new AdminSignInForm { Fields = [], Sections = [] } },
            new AdminSignInSchema { Key = "nulls", AdminForm = new AdminSignInForm { Fields = null!, Sections = null! } },
            new AdminSignInSchema { Key = "nulllists", AdminForm = new AdminSignInForm { Fields = [Field("x", "X")], Sections = [new() { Key = "s", FieldKeys = null!, ShowWhen = null! }] } },
            new AdminSignInSchema { Key = null!, AdminForm = new AdminSignInForm { Fields = [Field("y", "Y")] } }));
        Assert.Equal(["X", "Y"], layout.Steps.SelectMany(s => s.Fields).Select(f => f.Label)); Assert.Empty(layout.Advanced); Assert.Empty(layout.Unsupported);
        Assert.Equal("", AdminSignInLayout.SchemaTitle(new AdminSignInSchema { Key = null! }));
    }

    [Fact]
    public async Task RepeatedSchemaKeysFromAPluginNeverCrashTheControllerAndSendOneWrite()
    {
        using var h = new Harness { DuplicateSchema = true };
        await h.Open();
        Assert.Equal(2, h.Provider.GlobalConfigSchema.Count);
        h.Controller.SetField(h.Provider, h.Schema, h.Field("note"), "typed note");
        await h.Controller.TestAsync(h.Provider, "typed-user");
        await h.Controller.SaveAsync(true);
        Assert.Equal(1, h.Writes.Count(w => w == TestWrite)); Assert.Equal(1, h.Writes.Count(w => w == ConfigWrite)); Assert.False(h.Controller.ReviewRequired);
    }

    // ── The login-button field is never a secret ──
    [Fact]
    public void ASecretMarkedLoginNameFieldIsNotTheLoginButtonField()
    {
        var secret = AdminSignInLayout.Of(Plugin(new AdminSignInSchema { Key = "display_name", Title = "Login name", AdminForm = new AdminSignInForm { Fields = [Field("value", "Name", secret: true)] } }));
        Assert.Null(secret.LoginName); Assert.Equal("value", secret.Steps.Single().Fields.Single().Key);
        var password = AdminSignInLayout.Of(Plugin(new AdminSignInSchema { Key = "display_name", AdminForm = new AdminSignInForm { Fields = [new() { Key = "value", Label = "Name", Control = "PASSWORD" }] } }));
        Assert.Null(password.LoginName);
        var plain = AdminSignInLayout.Of(Plugin(new AdminSignInSchema { Key = "display_name", AdminForm = new AdminSignInForm { Fields = [Field("value", "Name")] } }));
        Assert.Equal("value", plain.LoginName!.Field.Key); Assert.Empty(plain.Steps);
    }

    // ── A binding write changes only the field the admin touched and restates the rest from the FRESH read ──
    [Fact]
    public async Task TheAccountCreationSwitchNeverTurnsANetworkSignInBackOnThatWasTurnedOffElsewhere()
    {
        using var h = new Harness { Network = true, BindingEnabled = true };
        await h.Open(); var shown = h.Provider; Assert.True(shown.Binding!.Enabled);
        h.BindingEnabled = false; // another session turned network sign-in off
        await h.Controller.SetBindingAsync(shown, true /* what the page showed */, false, true);
        Assert.DoesNotContain(BindingWrite, h.Writes);
        Assert.Equal(AdminText.T("signIn.native.changedElsewhere"), h.Controller.Message);
        Assert.False(h.Controller.Providers.Single().Binding!.Enabled); Assert.False(h.Controller.ReviewRequired); Assert.Null(h.Controller.LastChange);
    }

    [Fact]
    public async Task TheEnableSwitchNeverOverwritesAnAccountCreationSettingChangedElsewhere()
    {
        using var h = new Harness { Network = true, BindingEnabled = true };
        await h.Open(); var shown = h.Provider; Assert.True(shown.Binding!.AutoProvision);
        h.AutoProvision = false; // another session turned account creation off
        await h.Controller.SetBindingAsync(shown, false, null, true);
        Assert.DoesNotContain(BindingWrite, h.Writes);
        Assert.Equal(AdminText.T("signIn.native.changedElsewhere"), h.Controller.Message);
        Assert.False(h.Controller.Providers.Single().Binding!.AutoProvision); Assert.True(h.Controller.Providers.Single().Binding!.Enabled);
    }

    [Theory]
    [InlineData("default_login")]
    [InlineData("display_order")]
    public async Task ABindingWriteDoesNotRestateAFieldItDidNotTouchThatChangedElsewhere(string changed)
    {
        using var h = new Harness();
        await h.Open(); var shown = h.Provider;
        if (changed == "default_login") h.DefaultLogin = true; else h.DisplayOrder = 9;
        await h.Controller.SetBindingAsync(shown, true, null, true);
        Assert.DoesNotContain(BindingWrite, h.Writes);
        Assert.Equal(AdminText.T("signIn.native.changedElsewhere"), h.Controller.Message);
        // Reading the fresh state, the admin can act again, and the write then restates what is now saved.
        await h.Controller.SetBindingAsync(h.Provider, true, null, true);
        using var body = h.Body(BindingWrite);
        Assert.Equal(changed == "default_login", body.RootElement.GetProperty("default_login").GetBoolean()); Assert.Equal(changed == "display_order" ? 9 : 4, body.RootElement.GetProperty("display_order").GetInt32());
    }

    [Fact]
    public async Task AnAccountCreationChangeTakesTheEnabledFlagFromTheFreshReadNotFromTheCaller()
    {
        using var h = new Harness { Network = true, BindingEnabled = true };
        await h.Open();
        await h.Controller.SetBindingAsync(h.Provider, false /* not what is saved: ignored */, false, true);
        using var body = h.Body(BindingWrite);
        Assert.True(body.RootElement.GetProperty("enabled").GetBoolean()); Assert.False(body.RootElement.GetProperty("auto_provision").GetBoolean());
        Assert.True(h.Controller.LastChange!.Enabled);
    }

    [Fact]
    public async Task ATurnOffRestatesTheFreshAccountCreationAndDefaultLoginValues()
    {
        using var h = new Harness { BindingEnabled = true, DefaultLogin = true, AutoProvision = false };
        await h.Open();
        await h.Controller.SetBindingAsync(h.Provider, false, null, true);
        using var body = h.Body(BindingWrite);
        Assert.False(body.RootElement.GetProperty("enabled").GetBoolean()); Assert.True(body.RootElement.GetProperty("default_login").GetBoolean()); Assert.False(body.RootElement.GetProperty("auto_provision").GetBoolean());
        Assert.Equal(4, body.RootElement.GetProperty("display_order").GetInt32());
    }

    [Fact]
    public async Task ADefaultLoginAbsentFromTheServersAnswerIsSentAsFalse()
    {
        using var h = new Harness { OmitDefaultLogin = true };
        await h.Open();
        await h.Controller.SetBindingAsync(h.Provider, true, null, true);
        using var body = h.Body(BindingWrite);
        Assert.Equal(JsonValueKind.False, body.RootElement.GetProperty("default_login").ValueKind);
    }

    [Fact]
    public async Task ASavedFalseDefaultLoginOnANetworkBindingIsSentAsFalse()
    {
        using var h = new Harness { Network = true, BindingEnabled = true, DefaultLogin = false };
        await h.Open();
        await h.Controller.SetBindingAsync(h.Provider, true, false, true);
        using var body = h.Body(BindingWrite);
        Assert.Equal(JsonValueKind.False, body.RootElement.GetProperty("default_login").ValueKind);
    }

    [Fact]
    public async Task ADefaultLoginTheDeploymentOwnsOnANetworkBindingIsRestatedAndNothingIsRefusedLocally()
    {
        using var h = new Harness { Network = true, BindingEnabled = true, DefaultLogin = true, CapabilityManaged = """["default_login"]""", BindingManaged = """["default_login"]""" };
        await h.Open();
        await h.Controller.SetBindingAsync(h.Provider, false, null, true);
        Assert.Contains(BindingWrite, h.Writes);
        using var body = h.Body(BindingWrite);
        Assert.False(body.RootElement.GetProperty("enabled").GetBoolean()); Assert.True(body.RootElement.GetProperty("default_login").GetBoolean());
        Assert.False(h.Controller.ReviewRequired); Assert.NotNull(h.Controller.LastChange);
    }

    // ── Third-party text and direction ──
    // Invisible and direction characters are built from their code points so they stay visible in this source.
    private static string Chars(params int[] codes) => string.Concat(codes.Select(char.ConvertFromUtf32));

    public static TheoryData<string, string> ThirdPartyCases() => new()
    {
        { "Plain name", "Plain name" },
        { "  a" + Chars(0) + "b\tc\r\nd" + Chars(0x2028) + "e" + Chars(0x2029) + "f  ", "a b c d e f" },
        { "pay" + Chars(0x202E) + "pal" + Chars(0x202C, 0x200E, 0x200F, 0x2066) + "x" + Chars(0x2069), "paypalx" },
        { "in" + Chars(0x200B) + "vis" + Chars(0x2060, 0xFEFF, 0x00AD, 0x3164, 0x115F) + "ible", "invisible" },
        { Chars(0x200D) + "edge" + Chars(0x200C), "edge" },
        { "a " + Chars(0x200D) + " b", "a b" },
        { Chars(0x0645, 0x06CC, 0x200C, 0x062E, 0x0648, 0x0627, 0x0647, 0x0645), Chars(0x0645, 0x06CC, 0x200C, 0x062E, 0x0648, 0x0627, 0x0647, 0x0645) },
        { Chars(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467), Chars(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467) },
        { "", "" },
    };

    [Theory]
    [MemberData(nameof(ThirdPartyCases))]
    public void ThirdPartyTextIsNeutralized(string input, string expected) => Assert.Equal(expected, AdminSignInText.Plain(input));

    [Fact]
    public void ThirdPartyTextIsBoundedByTextElementsAndNeverSplitsAPair()
    {
        var emoji = string.Concat(Enumerable.Repeat(Chars(0x1F600), 100));
        Assert.Equal(string.Concat(Enumerable.Repeat(Chars(0x1F600), 9)) + Chars(0x2026), AdminSignInText.Plain(emoji, 10));
        Assert.Equal("abc" + Chars(0x2026), AdminSignInText.Plain("abcdefgh", 4)); Assert.Equal("abcd", AdminSignInText.Plain("abcd", 4)); Assert.Equal("", AdminSignInText.Plain("abc", 0));
    }

    [Fact]
    public void SetupValuesReadLeftToRightOnARightToLeftPage()
    {
        var arabic = Chars(0x0645, 0x0631, 0x062D, 0x0628, 0x0627); var hebrew = Chars(0x05E9, 0x05DC, 0x05D5, 0x05DD);
        Assert.Equal(AdminSignInDirection.None, AdminSignInText.DirectionFor(false, "https://x", "", true));
        Assert.Equal(AdminSignInDirection.LeftToRight, AdminSignInText.DirectionFor(true, "", "https://issuer.example", false));
        Assert.Equal(AdminSignInDirection.RightToLeft, AdminSignInText.DirectionFor(true, "", arabic, false));
        Assert.Equal(AdminSignInDirection.LeftToRight, AdminSignInText.DirectionFor(true, "secret-text", "", true));
        Assert.Equal(AdminSignInDirection.Auto, AdminSignInText.DirectionFor(true, arabic, "", false));
        Assert.True(AdminSignInText.StartsRightToLeft("123 " + hebrew)); Assert.False(AdminSignInText.StartsRightToLeft("123 abc " + Chars(0x05E9))); Assert.False(AdminSignInText.StartsRightToLeft("123"));
    }
}
