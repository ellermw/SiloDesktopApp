using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// The shared environment-managed mechanism: the sensitive-status contract (official <c>managed_by_env_vars</c>), the row note,
/// and the draft rule of the official <c>useSettingsForm</c>. Values are synthetic.
/// </summary>
public sealed class AdminEnvManagedSettingsTests
{
    private static async Task<AdminSettingsSensitiveStatus> ReadStatusAsync(string json)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(json));
        return await new AdminServerSettingsApi(kit.Client).GetSensitiveStatusAsync(lease);
    }

    // ── Contract ─────────────────────────────────────────────────────

    [Fact]
    public async Task StatusCarriesTheVariableNamedForEachManagedKey()
    {
        var status = await ReadStatusAsync("""{"configured":["redis.url"],"managed_by_env":["redis.url","redis.db","server.public_url"],"managed_by_env_vars":{"redis.url":"REDIS_URL","redis.db":"REDIS_URL","server.public_url":"SILO_PUBLIC_URL"}}""");
        Assert.Equal(["redis.url", "redis.db", "server.public_url"], status.ManagedByEnv);
        Assert.Equal("REDIS_URL", status.Environment.VariableName("redis.db"));
        Assert.Equal("SILO_PUBLIC_URL", status.Environment.VariableName("server.public_url"));
        Assert.Null(status.Environment.VariableName("branding.server_name"));
        Assert.False(status.Environment.IsManaged("branding.server_name"));
    }

    [Fact]
    public async Task AnOlderServerThatSendsOnlyTheListKeepsWorkingWithoutNames()
    {
        foreach (var json in new[]
        {
            """{"configured":[],"managed_by_env":["server.public_url"]}""",
            """{"configured":[],"managed_by_env":["server.public_url"],"managed_by_env_vars":null}""",
        })
        {
            var status = await ReadStatusAsync(json);
            Assert.Equal(["server.public_url"], status.ManagedByEnv);
            Assert.True(status.Environment.IsManaged("server.public_url"));
            Assert.Null(status.Environment.VariableName("server.public_url"));
            Assert.Empty(status.Environment.Names);
        }
    }

    [Fact]
    public async Task AKeyOnlyTheMapNamesIsStillManagedAndAnUnusableNameLeavesTheKeyManagedButUnnamed()
    {
        var status = await ReadStatusAsync("""{"configured":[],"managed_by_env":[],"managed_by_env_vars":{"a.one":"SILO_ONE","a.number":7,"a.blank":"  ","a.control":"BAD\u0007NAME","a.null":null}}""");
        Assert.Equal(["a.one", "a.number", "a.blank", "a.control", "a.null"], status.ManagedByEnv);
        Assert.Equal("SILO_ONE", status.Environment.VariableName("a.one"));
        foreach (var key in new[] { "a.number", "a.blank", "a.control", "a.null" })
        {
            Assert.True(status.Environment.IsManaged(key));
            Assert.Null(status.Environment.VariableName(key));
        }
        Assert.Equal("SILO_" + new string('X', 20), AdminEnvManagedSettings.Normalize("SILO_" + new string('X', 20)));
        Assert.Equal(new string('X', AdminEnvManagedSettings.MaxNameLength), AdminEnvManagedSettings.Normalize(new string('X', AdminEnvManagedSettings.MaxNameLength)));
    }

    [Theory]
    [InlineData("SILO_PUBLIC_URL")]
    [InlineData("_PRIVATE")]
    [InlineData("REDIS_URL_FILE")]
    [InlineData("a1")]
    public void EveryPlainIdentifierIsAUsableName(string name) => Assert.Equal(name, AdminEnvManagedSettings.Normalize(name));

    [Theory]
    [InlineData("SILO_A\u2028B", "a line separator")]
    [InlineData("SILO_A\u2029B", "a paragraph separator")]
    [InlineData("SILO_\u202EB", "a bidirectional override")]
    [InlineData("\u202ESILO_B", "a leading bidirectional override")]
    [InlineData("SILO_\u2066B\u2069", "a bidirectional isolate")]
    [InlineData("SILO_\u200BB", "a zero-width space")]
    [InlineData("SILO_\u200DB", "a zero-width joiner")]
    [InlineData("SILO_\uFEFFB", "a byte-order mark")]
    [InlineData("SILO_A\u0007B", "a control character")]
    [InlineData("SILO_A\nB", "a line feed")]
    [InlineData("SILO_A\n", "a trailing line feed")]
    [InlineData("SILO A", "a space")]
    [InlineData(" SILO_A", "a leading space")]
    [InlineData("REDIS_URL, REDIS_URL_FILE", "two names")]
    [InlineData("SILO-A", "a hyphen")]
    [InlineData("9SILO", "a leading digit")]
    [InlineData("SILO_\u00C9", "a non-ASCII letter")]
    [InlineData("", "an empty name")]
    [InlineData("  ", "a blank name")]
    public void AnythingButAPlainIdentifierIsNotShownAndTheKeyStaysManagedWithTheUnnamedNote(string name, string why)
    {
        Assert.True(AdminEnvManagedSettings.Normalize(name) == null, why);
        var env = new AdminEnvManagedSettings([], [new("server.public_url", name)]);
        Assert.True(env.IsManaged("server.public_url"));
        Assert.Null(env.VariableName("server.public_url"));
        Assert.Equal(AdminEnvManagedSettings.NoteFor(null), env.Note("server.public_url"));
    }

    [Fact]
    public void ANameLongerThanOneHundredTwentyEightCharactersIsNotShown()
    {
        Assert.Null(AdminEnvManagedSettings.Normalize(new string('X', AdminEnvManagedSettings.MaxNameLength + 1)));
        Assert.Equal(128, AdminEnvManagedSettings.MaxNameLength);
    }

    [Theory]
    [InlineData("""["redis.url"]""")]
    [InlineData("\"REDIS_URL\"")]
    [InlineData("7")]
    [InlineData("true")]
    public async Task AMapThatIsNotAnObjectNamesNoVariableAndTheListStillDecidesWhatIsManaged(string map)
    {
        var status = await ReadStatusAsync("{\"configured\":[],\"managed_by_env\":[\"server.public_url\"],\"managed_by_env_vars\":" + map + "}");
        Assert.Equal(["server.public_url"], status.ManagedByEnv);
        Assert.True(status.Environment.IsManaged("server.public_url"));
        Assert.Null(status.Environment.VariableName("server.public_url"));
        Assert.Empty(status.Environment.Names);
        // The list itself being malformed (or missing) is still a refused status.
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadStatusAsync("""{"configured":[],"managed_by_env_vars":{}}"""));
        await Assert.ThrowsAnyAsync<Exception>(() => ReadStatusAsync("{\"configured\":[],\"managed_by_env\":\"server.public_url\",\"managed_by_env_vars\":" + map + "}"));
    }

    [Fact]
    public async Task AMapThatIsNotAnObjectDoesNotMakeAControllerTreatTheStatusAsFailed()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request, """{"configured":[],"managed_by_env":["server.public_url"],"managed_by_env_vars":["server.public_url"]}"""));
        await controller.OpenAsync();
        Assert.False(controller.State.SensitiveStatusError);
        Assert.Equal(["server.public_url"], controller.State.ManagedByEnv);
        controller.Set("branding.server_name", "Edited");
        Assert.Equal(["branding.server_name"], controller.State.DirtyKeys);
    }

    [Fact]
    public void AStatusBuiltFromTheTwoArgumentFormHasNoNames()
    {
        var status = new AdminSettingsSensitiveStatus(["x"], ["a", "b"]);
        Assert.Equal(["a", "b"], status.Environment.Keys);
        Assert.Empty(status.Environment.Names);
    }

    [Fact]
    public void RestrictKeepsOnlyThePagesKeysAndTheirNames()
    {
        var all = new AdminEnvManagedSettings(["a", "b"], [new("b", "SILO_B"), new("c", "SILO_C")]);
        var page = all.Restrict(["b", "c", "d"]);
        Assert.Equal(["b", "c"], page.Keys);
        Assert.Equal("SILO_B", page.VariableName("b"));
        Assert.False(page.IsManaged("a"));
    }

    // ── Note text ────────────────────────────────────────────────────

    [Fact]
    public void NoteIsTheOfficialMessageWithTheVariableName()
    {
        var env = new AdminEnvManagedSettings(["server.public_url"], [new("server.public_url", "SILO_PUBLIC_URL")]);
        Assert.Equal("Set by SILO_PUBLIC_URL in the server’s environment.", env.Note("server.public_url"));
        Assert.Equal("Set by SILO_PUBLIC_URL in the server’s environment.", AdminEnvManagedSettings.NoteFor("SILO_PUBLIC_URL"));
    }

    [Fact]
    public void ASettingNamingSeveralVariablesShowsThemAsTheServerSentThemAndKeysSharingOneVariableEachSayIt()
    {
        // The server names one variable per key (map[string]string); a value that lists several is not a plain identifier and is not shown.
        var several = new AdminEnvManagedSettings(["redis.url"], [new("redis.url", "REDIS_URL, REDIS_URL_FILE")]);
        Assert.Equal(AdminEnvManagedSettings.NoteFor(null), several.Note("redis.url"));
        var shared = new AdminEnvManagedSettings(["redis.url", "redis.db"], [new("redis.url", "REDIS_URL"), new("redis.db", "REDIS_URL")]);
        Assert.Equal(shared.Note("redis.url"), shared.Note("redis.db"));
        Assert.Equal("Set by REDIS_URL in the server’s environment.", shared.Note("redis.db"));
    }

    [Fact]
    public void ANamelessManagedKeyUsesTheOfficialFieldMessageAndAnEditableKeyHasNoNote()
    {
        var env = new AdminEnvManagedSettings(["server.public_url"], null);
        const string unnamed = "Set by the environment. Change it there and restart the affected service.";
        Assert.Equal(unnamed, env.Note("server.public_url"));
        Assert.Equal(unnamed, AdminEnvManagedSettings.NoteFor(null));
        Assert.Equal(unnamed, AdminEnvManagedSettings.NoteFor(""));
        Assert.Null(env.Note("branding.server_name"));
        Assert.Null(AdminEnvManagedSettings.None.Note("server.public_url"));
    }

    [Fact]
    public void TheNoteTextsAreInTheCatalogWithTheOfficialWording()
    {
        var english = (IReadOnlyDictionary<string, string>)typeof(AdminText).GetField("English", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        Assert.Equal("Set by {env} in the server’s environment.", english["web.admin.settings.fields.managedByEnv"]);
        Assert.Equal("Set by the environment. Change it there and restart the affected service.", english["error.field.managed_by_environment"]);
    }

    // ── Draft rule ───────────────────────────────────────────────────

    private static AdminSettingsForm Form() => new(["server.public_url", "branding.server_name", "redis.url", "redis.db"], ["redis.url"],
        new Dictionary<string, string> { ["server.public_url"] = "https://saved.invalid", ["branding.server_name"] = "Silo", ["redis.db"] = "3" }, "\"v1\"");

    [Fact]
    public void AKeyThatBecomesManagedLosesItsDraftAndTheRestStaysAndSaves()
    {
        var form = Form();
        form.Set("server.public_url", "https://draft.invalid");
        form.Set("branding.server_name", "Edited");
        var dropped = form.ApplyEnvironment(new AdminEnvManagedSettings(["server.public_url"], [new("server.public_url", "SILO_PUBLIC_URL")]));
        Assert.Equal(["server.public_url"], dropped);
        Assert.Equal("https://saved.invalid", form.Get("server.public_url"));
        Assert.Equal(["branding.server_name"], form.DirtyKeys);
        Assert.Equal(["branding.server_name"], form.CaptureSave().Values.Keys);
    }

    [Fact]
    public void AManagedKeyCannotBeStagedAgainAndIsNeverInASaveBody()
    {
        var form = Form();
        form.ApplyEnvironment(new AdminEnvManagedSettings(["server.public_url", "redis.url"], null));
        form.Set("server.public_url", "https://later.invalid");
        form.StageRedisUrl("redis://later.invalid");
        Assert.Empty(form.DirtyKeys);
        Assert.Equal("https://saved.invalid", form.Get("server.public_url"));
        Assert.Empty(form.CaptureSave().Values);
        Assert.True(form.IsEnvironmentManaged("server.public_url"));
        // Lifting the management (a later status without the key) makes the key editable again.
        form.ApplyEnvironment(AdminEnvManagedSettings.None);
        form.Set("server.public_url", "https://later.invalid");
        Assert.Equal(["server.public_url"], form.DirtyKeys);
    }

    [Fact]
    public void ASecretThatBecomesManagedNeverKeepsShowsOrSendsATypedValue()
    {
        var form = Form();
        form.StageRedisUrl("redis://user:typed-secret@host.invalid:6379");
        Assert.Equal("redis://user:typed-secret@host.invalid:6379", form.Get("redis.url"));
        form.ApplyEnvironment(new AdminEnvManagedSettings(["redis.url", "redis.db"], [new("redis.url", "REDIS_URL"), new("redis.db", "REDIS_URL")]));
        Assert.Equal("", form.Get("redis.url"));
        Assert.Empty(form.DirtyKeys);
        form.Set("redis.url", "redis://other.invalid");
        Assert.Equal("", form.Get("redis.url"));
        Assert.DoesNotContain("redis.url", form.CaptureSave().Values.Keys);
    }

    [Fact]
    public void AnEmptiedRedisDatabaseNumberIsTheOneDraftThatSurvivesTheEnvironmentTakingOver()
    {
        var form = Form();
        form.Set("redis.db", "");
        form.ApplyEnvironment(new AdminEnvManagedSettings(["redis.url", "redis.db"], [new("redis.url", "REDIS_URL"), new("redis.db", "REDIS_URL")]));
        Assert.Equal(["redis.db"], form.DirtyKeys);
        Assert.Equal("", form.CaptureSave().Values["redis.db"]);

        var typed = Form();
        typed.Set("redis.db", "5");
        typed.ApplyEnvironment(new AdminEnvManagedSettings(["redis.url", "redis.db"], null));
        Assert.Empty(typed.DirtyKeys);
        Assert.Equal("3", typed.Get("redis.db"));
    }

    // ── A controller over the wire ───────────────────────────────────

    private static HttpResponseMessage Read(HttpRequestMessage request, string status)
    {
        var path = request.RequestUri!.AbsolutePath;
        var response = path.EndsWith("effective") ? AdminMonitorKit.Json("""{"branding.server_name":"Silo","server.public_url":"https://saved.invalid","redis.db":"2"}""")
            : path.EndsWith("sensitive-status") ? AdminMonitorKit.Json(status)
            : AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
        response.Headers.ETag = new("\"one\"");
        return response;
    }

    private const string Editable = """{"configured":[],"managed_by_env":[],"managed_by_env_vars":{}}""";
    private const string UrlManaged = """{"configured":[],"managed_by_env":["server.public_url"],"managed_by_env_vars":{"server.public_url":"SILO_PUBLIC_URL"}}""";

    [Fact]
    public async Task AReloadThatFindsAKeyManagedDropsItsDraftAndTheSaveBodyOmitsIt()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        var status = Editable;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var saved = AdminMonitorKit.Json("""{"values":{"branding.server_name":"Edited"},"restart_required":false}""");
                saved.Headers.ETag = new("\"two\""); return Task.FromResult(saved);
            }
            return Task.FromResult(Read(request, status));
        };
        await controller.OpenAsync();
        Assert.False(controller.State.EnvManaged.IsManaged("server.public_url"));
        controller.Set("server.public_url", "https://draft.invalid");
        controller.Set("branding.server_name", "Edited");
        Assert.Equal(2, controller.State.DirtyKeys.Count);

        status = UrlManaged;
        await controller.ReloadAsync(preserveDraft: true);
        Assert.Equal(["branding.server_name"], controller.State.DirtyKeys);
        Assert.Equal("https://saved.invalid", controller.State.Values["server.public_url"]);
        Assert.Equal("Set by SILO_PUBLIC_URL in the server’s environment.", controller.State.EnvManaged.Note("server.public_url"));
        controller.Set("server.public_url", "https://again.invalid");
        Assert.Equal(["branding.server_name"], controller.State.DirtyKeys);

        await controller.SaveAsync();
        using var body = JsonDocument.Parse(kit.Http.Bodies.Last());
        Assert.Equal(["branding.server_name"], body.RootElement.GetProperty("values").EnumerateObject().Select(entry => entry.Name));
        Assert.Empty(controller.State.DirtyKeys);
    }

    [Fact]
    public async Task ARedisUrlTheEnvironmentTakesOverNeverKeepsTheTypedValueAndAnEmptiedDatabaseNumberIsStillSaved()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        var status = Editable;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var saved = AdminMonitorKit.Json("""{"values":{},"restart_required":false}""");
                saved.Headers.ETag = new("\"two\""); return Task.FromResult(saved);
            }
            return Task.FromResult(Read(request, status));
        };
        await controller.OpenAsync();
        controller.StageRedisUrl("redis://typed-secret.invalid");
        controller.Set("redis.db", "");
        status = """{"configured":[],"managed_by_env":["redis.url","redis.db"],"managed_by_env_vars":{"redis.url":"REDIS_URL","redis.db":"REDIS_URL"}}""";
        await controller.ReloadAsync(preserveDraft: true);
        Assert.Equal("", controller.State.Values["redis.url"]);
        Assert.Equal(["redis.db"], controller.State.DirtyKeys);

        await controller.SaveAsync();
        using var body = JsonDocument.Parse(kit.Http.Bodies.Last());
        var values = body.RootElement.GetProperty("values").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString());
        Assert.Equal(new Dictionary<string, string?> { ["redis.db"] = "" }, values);
    }

    [Fact]
    public async Task AnOlderServerThatSendsOnlyTheListStillLocksTheKeyAndDropsItsDraft()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        var status = """{"configured":[],"managed_by_env":[]}""";
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request, status));
        await controller.OpenAsync();
        controller.Set("server.public_url", "https://draft.invalid");
        status = """{"configured":[],"managed_by_env":["server.public_url"]}""";
        await controller.ReloadAsync(preserveDraft: true);
        Assert.Empty(controller.State.DirtyKeys);
        Assert.Equal(["server.public_url"], controller.State.ManagedByEnv);
        Assert.Null(controller.State.EnvManaged.VariableName("server.public_url"));
        Assert.Equal("Set by the environment. Change it there and restart the affected service.", controller.State.EnvManaged.Note("server.public_url"));
    }

    [Fact]
    public async Task TheEnvironmentTakingTheJellyfinApiSwitchOverAlsoTakesBackTheStagedWebPlayerSwitchOff()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminIntegrationsSettings.AllKeys, AdminIntegrationsSettings.Secrets);
        using var controller = new AdminIntegrationSettingsController(AdminIntegrationsSettings.CompatibilityKeys, api, store, authority, new AdminIntegrationsApi(kit.Client));
        var status = Editable;
        kit.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var response = path.EndsWith("effective") ? AdminMonitorKit.Json("""{"jellyfin_compat.enabled":"true","jellyfin_compat.web_enabled":"true","jellyfin_compat.server_name":"Silo"}""")
                : path.EndsWith("sensitive-status") ? AdminMonitorKit.Json(status) : AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
            response.Headers.ETag = new("\"one\""); return Task.FromResult(response);
        };
        await controller.OpenAsync();
        controller.Set("jellyfin_compat.enabled", "false");
        controller.Set("jellyfin_compat.web_enabled", "false");
        controller.Set("jellyfin_compat.server_name", "Renamed");
        Assert.Equal(3, controller.State.DirtyKeys.Count);

        status = """{"configured":[],"managed_by_env":["jellyfin_compat.enabled"],"managed_by_env_vars":{"jellyfin_compat.enabled":"SILO_JELLYFIN_ENABLED"}}""";
        await controller.ReloadAsync(preserveDraft: true);
        Assert.Equal(["jellyfin_compat.server_name"], controller.State.DirtyKeys);
        Assert.Equal("true", controller.State.Values["jellyfin_compat.web_enabled"]);
        Assert.Equal("Set by SILO_JELLYFIN_ENABLED in the server’s environment.", controller.State.EnvManaged.Note("jellyfin_compat.enabled"));
    }

    // ── A status read that fails after one answered (official useSettingsForm keeps the last list) ──

    private const string Failing = "fail";

    private static HttpResponseMessage ReadOrFail(HttpRequestMessage request, string status) =>
        status == Failing && request.RequestUri!.AbsolutePath.EndsWith("sensitive-status") ? AdminMonitorKit.Json("{}", System.Net.HttpStatusCode.ServiceUnavailable) : Read(request, status);

    [Fact]
    public void ResolveKeepsTheLastListWhenTheReadFailedAndTakesTheAnswerOtherwise()
    {
        var last = new AdminEnvManagedSettings(["server.public_url"], [new("server.public_url", "SILO_PUBLIC_URL")]);
        Assert.Same(last, AdminEnvManagedSettings.Resolve(null, last));
        Assert.Same(AdminEnvManagedSettings.None, AdminEnvManagedSettings.Resolve(null, AdminEnvManagedSettings.None));
        var answered = new AdminSettingsSensitiveStatus([], []);
        Assert.Same(answered.Environment, AdminEnvManagedSettings.Resolve(answered, last));
        Assert.Empty(AdminEnvManagedSettings.Resolve(answered, last).Keys);
    }

    [Fact]
    public async Task AFailedStatusReadAfterAnAnswerKeepsTheManagedKeyLockedAndOutOfTheSave()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        var status = UrlManaged;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var saved = AdminMonitorKit.Json("""{"values":{"branding.server_name":"Edited"},"restart_required":false}""");
                saved.Headers.ETag = new("\"two\""); return Task.FromResult(saved);
            }
            return Task.FromResult(ReadOrFail(request, status));
        };
        await controller.OpenAsync();
        Assert.True(controller.State.EnvManaged.IsManaged("server.public_url"));

        status = Failing;
        await controller.ReloadAsync(preserveDraft: true);
        Assert.True(controller.State.Loaded);
        Assert.True(controller.State.SensitiveStatusError);
        Assert.Equal(["server.public_url"], controller.State.ManagedByEnv);
        Assert.Equal("Set by SILO_PUBLIC_URL in the server’s environment.", controller.State.EnvManaged.Note("server.public_url"));
        controller.Set("server.public_url", "https://draft.invalid");
        controller.Set("branding.server_name", "Edited");
        Assert.Equal(["branding.server_name"], controller.State.DirtyKeys);

        await controller.SaveAsync();
        Assert.Equal(["branding.server_name"], EnvBodies.PutKeys(kit));
    }

    [Fact]
    public async Task AnAnsweredStatusAfterAFailedOneReplacesTheKeptList()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        var status = UrlManaged;
        kit.Http.Respond = (request, _) => Task.FromResult(ReadOrFail(request, status));
        await controller.OpenAsync();
        status = Failing; await controller.ReloadAsync(preserveDraft: true);
        Assert.True(controller.State.EnvManaged.IsManaged("server.public_url"));
        status = Editable; await controller.ReloadAsync(preserveDraft: true);
        Assert.False(controller.State.EnvManaged.IsManaged("server.public_url"));
        Assert.Empty(controller.State.ManagedByEnv);
        controller.Set("server.public_url", "https://later.invalid");
        Assert.Equal(["server.public_url"], controller.State.DirtyKeys);
    }

    [Fact]
    public async Task OpeningThePageAgainStartsFromNothingSoAPreviousServersListNeverCarriesOver()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        var status = UrlManaged;
        kit.Http.Respond = (request, _) => Task.FromResult(ReadOrFail(request, status));
        await controller.OpenAsync();
        Assert.True(controller.State.EnvManaged.IsManaged("server.public_url"));

        // The account or server changes: the page is retired and opened again, and the new server's first status read fails.
        controller.Suspend();
        Assert.Empty(controller.State.EnvManaged.Keys);
        status = Failing;
        await controller.OpenAsync();
        Assert.True(controller.State.Loaded);
        Assert.True(controller.State.SensitiveStatusError);
        Assert.Empty(controller.State.EnvManaged.Keys);
        Assert.Empty(controller.State.ManagedByEnv);
        Assert.Null(controller.State.EnvManaged.Note("server.public_url"));
    }

    // ── Integrations: the staged web-off, and a failed status read ──

    private const string JellyfinEffective = """{"jellyfin_compat.enabled":"false","jellyfin_compat.web_enabled":"true","jellyfin_compat.server_name":"Silo"}""";
    private const string JellyfinApiManaged = """{"configured":[],"managed_by_env":["jellyfin_compat.enabled"],"managed_by_env_vars":{"jellyfin_compat.enabled":"SILO_JELLYFIN_ENABLED"}}""";

    private sealed class IntegrationsHarness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsStore Store;
        internal readonly AdminIntegrationSettingsController Controller;
        internal string Status = Editable;
        internal string Effective = JellyfinEffective;
        internal IntegrationsHarness(IReadOnlyList<string>? keys = null)
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            var api = new AdminServerSettingsApi(Kit.Client);
            Store = new AdminServerSettingsStore(api, Authority, AdminIntegrationsSettings.AllKeys, AdminIntegrationsSettings.Secrets);
            Controller = new AdminIntegrationSettingsController(keys ?? AdminIntegrationsSettings.CompatibilityKeys, api, Store, Authority, new AdminIntegrationsApi(Kit.Client));
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method == HttpMethod.Put)
                {
                    var saved = AdminMonitorKit.Json("""{"values":{"jellyfin_compat.server_name":"Renamed"},"restart_required":false}""");
                    saved.Headers.ETag = new("\"two\""); return Task.FromResult(saved);
                }
                var path = request.RequestUri!.AbsolutePath;
                var response = path.EndsWith("effective") ? AdminMonitorKit.Json(Effective)
                    : path.EndsWith("sensitive-status") ? (Status == Failing ? AdminMonitorKit.Json("{}", System.Net.HttpStatusCode.ServiceUnavailable) : AdminMonitorKit.Json(Status))
                    : AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
                response.Headers.ETag = new("\"one\""); return Task.FromResult(response);
            };
        }
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    [Fact]
    public async Task AFailedStatusReadAfterAnAnswerKeepsTheIntegrationsListAndOnlyAFirstFailureLocksEveryField()
    {
        using var h = new IntegrationsHarness(AdminIntegrationsSettings.AiKeys) { Effective = """{"ai.chat_model":"m1","ai.max_concurrent_jobs":"2"}""", Status = """{"configured":[],"managed_by_env":["ai.chat_model"],"managed_by_env_vars":{"ai.chat_model":"SILO_SYNTHETIC_MODEL"}}""" };
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.StatusKnown);
        Assert.Equal(["ai.chat_model"], h.Controller.State.ManagedByEnvironment);

        h.Status = Failing;
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.True(h.Controller.State.Loaded);
        Assert.True(h.Controller.State.StatusKnown);
        Assert.Equal(["ai.chat_model"], h.Controller.State.ManagedByEnvironment);
        Assert.Equal("Set by SILO_SYNTHETIC_MODEL in the server’s environment.", h.Controller.State.EnvManaged.Note("ai.chat_model"));
        h.Controller.Set("ai.chat_model", "m2");
        h.Controller.Set("ai.max_concurrent_jobs", "4");
        Assert.Equal(["ai.max_concurrent_jobs"], h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(["ai.max_concurrent_jobs"], EnvBodies.PutKeys(h.Kit));

        // Opened again on another server whose first read fails: nothing is known, so every field stays locked and no list carries over.
        h.Controller.Suspend();
        await h.Controller.OpenAsync();
        Assert.False(h.Controller.State.StatusKnown);
        Assert.Empty(h.Controller.State.EnvManaged.Keys);
        Assert.Equal(AdminIntegrationsSettings.AiKeys.OrderBy(key => key), h.Controller.State.ManagedByEnvironment.OrderBy(key => key));
        h.Controller.Set("ai.max_concurrent_jobs", "9");
        Assert.Empty(h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task TheApiSwitchTakesTheWebPlayerOffWithItOnlyWhileTheEnvironmentDoesNotHoldTheSwitch()
    {
        using var h = new IntegrationsHarness { Effective = JellyfinEffective.Replace("\"jellyfin_compat.enabled\":\"false\"", "\"jellyfin_compat.enabled\":\"true\"") };
        await h.Controller.OpenAsync();
        h.Controller.SetCompatibilityApi(false);
        Assert.Equal(new[] { "jellyfin_compat.enabled", "jellyfin_compat.web_enabled" }, h.Controller.State.DirtyKeys.OrderBy(key => key));
        Assert.Equal("false", h.Controller.State.Values["jellyfin_compat.web_enabled"]);
        h.Controller.Discard();

        // The environment holds the switch (Off) while the web player is saved On: a late report from the locked switch stages nothing.
        h.Effective = JellyfinEffective; h.Status = JellyfinApiManaged;
        await h.Controller.ReloadAsync(preserveDraft: false);
        h.Controller.SetCompatibilityApi(false);
        h.Controller.SetCompatibilityApi(true);
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("true", h.Controller.State.Values["jellyfin_compat.web_enabled"]);
    }

    [Fact]
    public async Task AManagedWebPlayerSwitchIsNotStagedByTheApiSwitchEither()
    {
        using var h = new IntegrationsHarness
        {
            Effective = JellyfinEffective.Replace("\"jellyfin_compat.enabled\":\"false\"", "\"jellyfin_compat.enabled\":\"true\""),
            Status = """{"configured":[],"managed_by_env":["jellyfin_compat.web_enabled"],"managed_by_env_vars":{"jellyfin_compat.web_enabled":"SILO_JELLYFIN_WEB"}}""",
        };
        await h.Controller.OpenAsync();
        h.Controller.SetCompatibilityApi(false);
        Assert.Equal(["jellyfin_compat.enabled"], h.Controller.State.DirtyKeys);
    }

    // ── Search: a fallback variable only where exactly one variable can set the key ──

    [Fact]
    public void SearchKeepsAFallbackNameOnlyForTheKeysOneVariableSetsAndNoneForTheApiKeyThatMayComeFromAFile()
    {
        Assert.Equal("SILO_SEARCH_PROVIDER", AdminSearchSettingsRules.EnvironmentVariable(AdminSearchSettingsRules.Provider));
        Assert.Equal("SILO_MEILISEARCH_URL", AdminSearchSettingsRules.EnvironmentVariable(AdminSearchSettingsRules.MeilisearchUrl));
        Assert.Null(AdminSearchSettingsRules.EnvironmentVariable(AdminSearchSettingsRules.MeilisearchApiKey));
        Assert.Null(AdminSearchSettingsRules.EnvironmentVariable(AdminSearchSettingsRules.Index));
        // With no name from the server the API key shows the unnamed note, never a name that may be the wrong one.
        var unnamed = new AdminEnvManagedSettings([AdminSearchSettingsRules.MeilisearchApiKey], null);
        Assert.Equal(AdminEnvManagedSettings.NoteFor(null), AdminEnvManagedSettings.NoteFor(unnamed.VariableName(AdminSearchSettingsRules.MeilisearchApiKey) ?? AdminSearchSettingsRules.EnvironmentVariable(AdminSearchSettingsRules.MeilisearchApiKey)));
        // A server that names the file variable is believed.
        var file = new AdminEnvManagedSettings([], [new(AdminSearchSettingsRules.MeilisearchApiKey, "SILO_MEILISEARCH_API_KEY_FILE")]);
        Assert.Equal("Set by SILO_MEILISEARCH_API_KEY_FILE in the server’s environment.", file.Note(AdminSearchSettingsRules.MeilisearchApiKey));
    }
}

internal static class EnvBodies
{
    /// <summary>The keys of the newest settings write: the <c>values</c> of the last request body that carried content.</summary>
    internal static string[] PutKeys(AdminMonitorKit kit)
    {
        using var body = JsonDocument.Parse(kit.Http.Bodies.Last(text => text.Length > 0));
        return body.RootElement.GetProperty("values").EnumerateObject().Select(entry => entry.Name).ToArray();
    }
}
