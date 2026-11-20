using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminAutoscanContractTests
{
    [Fact]
    public async Task SourceWalkStopsAtTheDeclaredCapAndReportsPartialData()
    {
        using var kit = AdminMonitorKit.Create(); var page = 0;
        kit.Http.Respond = (_, _) =>
        {
            var number = ++page;
            return Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(new { items = Enumerable.Range(0, 100).Select(i => new { id = $"{number}-{i}", delivery_mode = "poll" }), page = new { has_more = true, next_cursor = "next-" + number } })));
        };
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var result = await new AdminAutoscanApi(kit.Client).ListSourcesAsync(lease);
        Assert.False(result.IsComplete); Assert.Equal(1000, result.Items.Count); Assert.Equal(10, kit.Http.Requests.Count);
    }

    [Fact]
    public void ErrorDetailsRedactKnownSecretsAndEntireUrls()
    {
        var source = new AdminAutoscanSource { DeliveryMode = "poll", SourceConfig = new() { ["api_key"] = "synthetic-sensitive-key" }, LastError = "Failed synthetic-sensitive-key at https://fixture.invalid/path?unknown=signed" };
        var safe = AdminAutoscanPresentation.ErrorDetail(source, new());
        Assert.DoesNotContain("synthetic-sensitive-key", safe); Assert.DoesNotContain("signed", safe); Assert.DoesNotContain("https://", safe);
    }

    [Fact]
    public void EditPreservesHiddenConnectionAndPollingFieldsOnWebhookSources()
    {
        var source = new AdminAutoscanSource { DeliveryMode = "webhook", ConnectionId = "stored", PollIntervalSeconds = 42 };
        using var draft = new AdminAutoscanEditorDraft(source, new() { Connection = "none", DeliveryModes = ["webhook"] });
        var body = draft.Build(source); Assert.Equal("stored", body.ConnectionId); Assert.Equal(42, body.PollIntervalSeconds);
    }

    [Fact]
    public async Task PutSendsOnlyTheSevenReplacementFieldsAndNoReadbackSecrets()
    {
        using var kit = AdminMonitorKit.Create();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"id":"source","delivery_mode":"poll"}"""));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await new AdminAutoscanApi(kit.Client).UpdateSourceAsync(lease, "source", new() { Enabled = true, SourceConfig = new() { ["kept"] = "value" }, PathRewrites = [new() { From = "/a", To = "/b" }] });
        using var body = JsonDocument.Parse(Assert.Single(kit.Http.Bodies));
        Assert.Equal(new[] { "connection_id", "delivery_mode", "enabled", "label", "path_rewrites", "poll_interval_seconds", "source_config" }, body.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(s => s).ToArray());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("connection_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("poll_interval_seconds").ValueKind);
        Assert.Equal("value", body.RootElement.GetProperty("source_config").GetProperty("kept").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    public async Task CreateRequires201AndDoesNotAcceptGenericSuccess(HttpStatusCode code)
    {
        using var kit = AdminMonitorKit.Create(); kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"id":"new","plugin_id":"p","capability_id":"c"}""", code));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminAutoscanApi(kit.Client).CreateSourceAsync(lease, "p", "c", new()));
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData(false, "/webhook")]
    [InlineData(true, "/webhook/rotate")]
    public async Task WebhookLifecycleUsesBodylessSinglePost(bool rotate, string suffix)
    {
        using var kit = AdminMonitorKit.Create(); kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"id":"source","delivery_mode":"webhook","webhook_configured":true}"""));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await new AdminAutoscanApi(kit.Client).WebhookAsync(lease, "source", rotate);
        Assert.Equal("POST /api/v2/admin/autoscan/sources/source" + suffix, Assert.Single(kit.Http.Requests)); Assert.Equal("", Assert.Single(kit.Http.Bodies));
    }

    [Fact]
    public async Task UnauthorizedWriteNeverRefreshesAuthenticationOrReplays()
    {
        using var kit = AdminMonitorKit.Create(); kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.Unauthorized));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await Assert.ThrowsAnyAsync<Exception>(() => new AdminAutoscanApi(kit.Client).DeleteSourceAsync(lease, "one"));
        Assert.Equal("DELETE /api/v2/admin/autoscan/sources/one", Assert.Single(kit.Http.Requests));
    }

    [Theory]
    [InlineData("other_task", "running", "process")]
    [InlineData("autoscan_poll", "completed", "process")]
    [InlineData("autoscan_poll", "running", "cluster")]
    public async Task TriggerRejectsWrongTaskStateOrScope(string key, string state, string scope)
    {
        using var kit = AdminMonitorKit.Create(); kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(new { key, state, execution_scope = scope })));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminAutoscanApi(kit.Client).TriggerAsync(lease)); Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task SettingsReturnsNonfatalFailedRescheduleWithoutReplay()
    {
        using var kit = AdminMonitorKit.Create(); kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"settings":{"enabled":true,"default_poll_interval_seconds":600,"debounce_seconds":60},"reschedule_state":"failed"}"""));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var receipt = await new AdminAutoscanApi(kit.Client).UpdateSettingsAsync(lease, new() { Enabled = true, DefaultPollIntervalSeconds = 600, DebounceSeconds = 60 });
        Assert.Equal("failed", receipt.RescheduleState); Assert.True(receipt.Settings!.Enabled); Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task ReusedConnectionSendsIntegrationIdentityWithoutCredentials()
    {
        using var kit = AdminMonitorKit.Create(); kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"id":"new","name":"Requests server","kind":"radarr"}""", HttpStatusCode.Created));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await new AdminAutoscanApi(kit.Client).CreateConnectionAsync(lease, new() { Name = "Requests server", Kind = "radarr", RequestIntegrationId = "15", BaseUrl = "discard", ApiKeyRef = "synthetic-discard" });
        using var body = JsonDocument.Parse(Assert.Single(kit.Http.Bodies));
        Assert.Equal("15", body.RootElement.GetProperty("request_integration_id").GetString());
        Assert.False(body.RootElement.TryGetProperty("api_key_ref", out _)); Assert.False(body.RootElement.TryGetProperty("base_url", out _));
    }

    [Fact]
    public async Task ReusableServerReadsCanonicalRowsAndDiscardsUnrelatedProviderConfig()
    {
        using var kit = AdminMonitorKit.Create(); kit.Http.Respond = (r, _) => Task.FromResult(AdminMonitorKit.Json(r.RequestUri!.AbsolutePath.EndsWith("/15")
            ? """{"id":"15","name":"Requests server","enabled":true,"plugin_config":{"service_kind":"radarr","unused":{"nested":true}}}"""
            : """{"items":[{"id":"15"}],"page":{"has_more":false}}"""));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var rows = await new AdminAutoscanApi(kit.Client).ReusableIntegrationsAsync(lease);
        Assert.Equal("radarr", Assert.Single(Assert.Single(rows).PluginConfig).Value.GetString());
        Assert.Equal(["GET /api/v2/admin/request-integrations?limit=50", "GET /api/v2/admin/request-integrations/15"], kit.Http.Requests);
    }

    [Fact]
    public void SectionConditionsHideValidationAndRemoveStaleSerializedValues()
    {
        var descriptor = new AdminAutoscanDescriptor { ConfigForm = new() { Fields = [new() { Key = "enabled", Control = "SWITCH", DefaultValue = false }, new() { Key = "conditional", Required = true }], Sections = [new() { FieldKeys = ["conditional"], ShowWhen = [new() { Field = "enabled", Equals = ["true"] }] }] } };
        using var draft = new AdminAutoscanEditorDraft(null, descriptor); draft.Config["conditional"] = "old"; draft.ConfigDirty = true;
        Assert.Null(draft.Validate()); Assert.False(draft.Build(null).SourceConfig.ContainsKey("conditional"));
        draft.Config["enabled"] = true; draft.Config["conditional"] = ""; Assert.NotNull(draft.Validate());
    }

    [Fact]
    public void SecretAndDynamicFieldsAreNeverOfferedAndTheirStoredValuesReturnUnchangedWhenConfigChanges()
    {
        var source = new AdminAutoscanSource { DeliveryMode = "poll", SourceConfig = new() { ["secret"] = "synthetic-secret", ["lookup"] = "synthetic-lookup", ["text"] = "old" } };
        var descriptor = new AdminAutoscanDescriptor { ConfigForm = new() { Fields = [
            new() { Key = "secret", Secret = true }, new() { Key = "password", Control = "PASSWORD" }, new() { Key = "lookup", DynamicOptions = true }, new() { Key = "text" }] } };
        var draft = new AdminAutoscanEditorDraft(source, descriptor);
        // The server drops these fields from source forms; no control, value or validation exists for them here.
        Assert.Equal(["text"], draft.Form.Fields.Select(field => field.Key));
        Assert.DoesNotContain("secret", draft.Config.Keys);
        // An untouched config goes back exactly as stored; a changed one keeps what the form never offered.
        Assert.Equal("synthetic-secret", draft.Build(source).SourceConfig["secret"]);
        draft.ConfigDirty = true; draft.Config["text"] = "new";
        var body = draft.Build(source).SourceConfig;
        Assert.Equal("synthetic-secret", body["secret"]); Assert.Equal("synthetic-lookup", body["lookup"]); Assert.Equal("new", body["text"]);
        draft.Dispose(); Assert.Empty(draft.Config);
    }

    [Fact]
    public void EmptyStoredValueStaysAndUntouchedDefaultlessFieldStaysAbsent()
    {
        var source = new AdminAutoscanSource { DeliveryMode = "poll", SourceConfig = new() { ["held"] = "" } };
        var descriptor = new AdminAutoscanDescriptor { ConfigForm = new() { Fields = [new() { Key = "held" }, new() { Key = "never" }] } };
        using var draft = new AdminAutoscanEditorDraft(source, descriptor); draft.ConfigDirty = true;
        var config = draft.Build(source).SourceConfig;
        Assert.Equal("", config["held"]); Assert.False(config.ContainsKey("never"));
    }

    [Fact]
    public void WebhookProjectionPreservesDeploymentPrefixAndMigratesDeliveryRoute()
    {
        Assert.Equal("https://silo.test/prefix/api/v2/autoscan/webhooks/synthetic", AdminAutoscanPresentation.WebhookUrl("/prefix/api/v1/autoscan/webhooks/synthetic", "https://silo.test"));
        Assert.Null(AdminAutoscanPresentation.WebhookUrl("javascript:fixture", "https://silo.test"));
        Assert.Null(AdminAutoscanPresentation.WebhookUrl("https://user:password@fixture.invalid/", "https://silo.test"));
    }
}
