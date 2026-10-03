using SiloPlayer.Core.Services;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Plugins;

namespace SiloPlayer.Tests;

public sealed class AccountParityBehaviorTests
{
    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-GB")]
    [InlineData("zh-Hant")]
    public void CurrentManifestRegionalLanguagesAreSelectable(string tag)
        => Assert.Contains(MediaLanguageCatalog.All, language => language.Code == tag);

    [Fact]
    public void DeviceDefinitionDiscoveryUsesCurrentRevisionAndPlatformWithoutStrandingOverrides()
    {
        var old = DeviceSettingDisplay.ForRevision(6);
        Assert.Contains(old, definition => definition.Key == "playback.auto_skip_intro");
        Assert.DoesNotContain(old, definition => definition.Key == "playback.intro_skip_mode");
        var current = DeviceSettingDisplay.ForRevision(15);
        Assert.Contains(current, definition => definition.Key == "playback.intro_skip_mode");
        Assert.DoesNotContain(current, definition => definition.Key == "playback.auto_skip_intro");
        Assert.Contains(current, definition => definition.Key == "ui.theme_music_enabled");
        Assert.Equal(1, current.Single(definition => definition.Key == "playback.next_up_prompt_seconds").Step);
        Assert.False(DeviceSettingDisplay.CanWrite(new ContractEffectiveSettingEntry { ConstraintKind = "locked" }));
    }

    [Fact]
    public void DormancyKeepsCurrentAndCustomizedDevicesVisible()
    {
        var now = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var old = new UserDevice { LastSeenAt = "2026-01-01T00:00:00Z" };
        Assert.True(DeviceSettingDisplay.IsDormant(old, now));
        old.IsCurrentDevice = true; Assert.False(DeviceSettingDisplay.IsDormant(old, now));
        old.IsCurrentDevice = false; old.ChangedCount = 1; Assert.False(DeviceSettingDisplay.IsDormant(old, now));
        old.ChangedCount = 0; old.LastSeenAt = "invalid"; Assert.True(DeviceSettingDisplay.IsDormant(old, now));
    }

    [Fact]
    public async Task TourSaveFailureRetainsTransitionAndAllowsRetryButBlocksConcurrentSaves()
    {
        var barrier = new OnboardingProgressBarrier();
        var pending = new TaskCompletionSource();
        var attempt = barrier.SaveAsync(() => pending.Task);
        Assert.False(await barrier.SaveAsync(() => Task.CompletedTask));
        pending.SetException(new InvalidOperationException("Offline"));
        Assert.False(await attempt); Assert.Equal("Offline", barrier.Error);
        Assert.False(barrier.IsSaving);
        Assert.True(await barrier.SaveAsync(() => Task.CompletedTask)); Assert.Null(barrier.Error);
    }

    [Fact]
    public void ProviderSchemaValidationAndPayloadRespectConditionsDefaultsAndTypes()
    {
        var schema = new PluginConfigSchema { Key = "connection", Required = true, AdminForm = new PluginAdminForm { Fields =
        [
            new() { Key = "mode", Label = "Mode", Control = "SELECT", DefaultValue = "simple" },
            new() { Key = "endpoint", Label = "Endpoint", Required = true, ShowWhen = [new() { Field = "mode", Equals = ["advanced"] }] },
            new() { Key = "limit", Label = "Limit", Control = "NUMBER", DefaultValue = 4, Validation = new() { Min = 1, Max = 10 } },
        ] } };
        var drafts = new Dictionary<string, Dictionary<string, object?>> { ["connection"] = new() { ["mode"] = "simple", ["limit"] = "6" } };
        var payload = ProviderConnectionConfig.Build([schema], drafts);
        Assert.Equal(6d, payload["connection"]["limit"]); Assert.False(payload["connection"].ContainsKey("endpoint"));
        drafts["connection"]["mode"] = "advanced";
        Assert.Throws<InvalidOperationException>(() => ProviderConnectionConfig.Build([schema], drafts));
        drafts["connection"]["endpoint"] = "https://example.test"; drafts["connection"]["limit"] = "100";
        Assert.Throws<InvalidOperationException>(() => ProviderConnectionConfig.Build([schema], drafts));
        drafts["connection"]["limit"] = "3"; Assert.Equal("https://example.test", ProviderConnectionConfig.Build([schema], drafts)["connection"]["endpoint"]);
        Assert.Empty(ProviderConnectionConfig.Build([new() { Key = "optional", AdminForm = schema.AdminForm }], new Dictionary<string, Dictionary<string, object?>>()));
    }
    [Fact]
    public void ProviderDefaultsDriveConditionsAndJsonSchemaRetainsIntegerAndBooleanTypes()
    {
        var schema = new PluginConfigSchema { Key = "typed", Required = true, JsonSchema = """{"type":"object","properties":{"count":{"type":"integer","minimum":1,"maximum":5},"enabled":{"type":"boolean"}}}""" };
        var payload = ProviderConnectionConfig.Build([schema], new Dictionary<string, Dictionary<string, object?>> { ["typed"] = new() { ["count"] = "3", ["enabled"] = "false" } });
        Assert.IsType<long>(payload["typed"]["count"]); Assert.Equal(3L, payload["typed"]["count"]);
        Assert.Equal(false, payload["typed"]["enabled"]);
        var conditional = new PluginAdminForm { Fields = [new() { Key = "mode", DefaultValue = "advanced" }, new() { Key = "endpoint", Label = "Endpoint", Required = true, ShowWhen = [new() { Field = "mode", Equals = ["advanced"] }] }] };
        Assert.Contains("endpoint", ProviderConnectionConfig.Validate(conditional, new Dictionary<string, object?>()).Keys);
    }

    [Fact]
    public async Task RefreshingSelectedProfileMetadataPreservesPinGrantAndDoesNotSwitchProfiles()
    {
        string? observed = null;
        using var http = new HttpClient(new ProfileHeaderHandler(request => observed = request.Headers.TryGetValues("X-Profile-Token", out var values) ? values.Single() : null));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://profile-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SelectProfile("primary", "fixture-pin-grant", new() { Id = "primary", Name = "Before" });
        auth.RefreshSelectedProfile(new() { Id = "other", Name = "Other" });
        Assert.Equal("Before", auth.SelectedProfile?.Name);
        auth.RefreshSelectedProfile(new() { Id = "primary", Name = "Updated" });
        Assert.Equal("Updated", auth.SelectedProfile?.Name);
        await client.GetAsync<System.Text.Json.JsonElement>("/fixture");
        Assert.Equal("fixture-pin-grant", observed);
    }

    [Fact]
    public async Task ProviderConnectionSendsTypedConfigurationAndRetainsDraftAfterServerRejection()
    {
        using var wire = new ProviderWire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://provider-fixture.invalid");
        var api = new WatchProvidersApi(client);
        var config = new Dictionary<string, Dictionary<string, object?>> { ["account"] = new() { ["limit"] = 3L, ["enabled"] = false } };
        await Assert.ThrowsAsync<ApiException>(() => api.ConnectApiKeyAsync("fixture", "  fixture-key  ", config));
        Assert.Equal(3L, config["account"]["limit"]); Assert.Equal(false, config["account"]["enabled"]);
        wire.Reject = false;
        var connected = await api.ConnectApiKeyAsync("fixture", "  fixture-key  ", config);
        Assert.True(connected.Connected);
        using var body = System.Text.Json.JsonDocument.Parse(wire.Body!);
        Assert.Equal("fixture-key", body.RootElement.GetProperty("api_key").GetString());
        var sent = body.RootElement.GetProperty("connection_config").GetProperty("account");
        Assert.Equal(3, sent.GetProperty("limit").GetInt32()); Assert.False(sent.GetProperty("enabled").GetBoolean());
    }

    private sealed class ProviderWire : HttpMessageHandler
    {
        public bool Reject = true;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                Body = await request.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(Reject ? System.Net.HttpStatusCode.UnprocessableEntity : System.Net.HttpStatusCode.OK)
                { Content = new StringContent(Reject ? "{\"message\":\"Check account configuration\"}" : "{\"connected\":true}") };
            }
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/settings") ? "{}" : "{\"connected\":true}") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"fixture-revision\"");
            return response;
        }
    }

    private sealed class ProfileHeaderHandler(Action<HttpRequestMessage> observe) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            observe(request);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

}
