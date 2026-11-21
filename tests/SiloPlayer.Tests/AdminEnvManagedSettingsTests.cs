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
        Assert.Null(AdminEnvManagedSettings.Normalize(new string('X', AdminEnvManagedSettings.MaxNameLength + 1)));
        Assert.Equal("SILO_" + new string('X', 20), AdminEnvManagedSettings.Normalize(" SILO_" + new string('X', 20) + " "));
    }

    [Theory]
    [InlineData("""["redis.url"]""")]
    [InlineData("\"REDIS_URL\"")]
    [InlineData("7")]
    [InlineData("true")]
    public async Task AMapThatIsNotAnObjectIsRefusedLikeAnyOtherMalformedStatus(string map)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadStatusAsync("{\"configured\":[],\"managed_by_env\":[],\"managed_by_env_vars\":" + map + "}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadStatusAsync("""{"configured":[],"managed_by_env_vars":{}}"""));
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
        // The official client interpolates the one string the server sends; it does not split or join names.
        var several = new AdminEnvManagedSettings(["redis.url"], [new("redis.url", "REDIS_URL, REDIS_URL_FILE")]);
        Assert.Equal("Set by REDIS_URL, REDIS_URL_FILE in the server’s environment.", several.Note("redis.url"));
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
}
