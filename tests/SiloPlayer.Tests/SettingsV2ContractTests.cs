using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Tests;

public sealed class SettingsV2ContractTests
{
    [Fact]
    public async Task RetiredDeviceOwnerCannotDispatchRenameOrClearNewProfileOverride()
    {
        using var handler = new RecordingHandler("{}");
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://silo.example.test"); client.SetProfile("first");
        var api = new SettingsApi(client); var retired = api.CaptureContext(); client.SetProfile("second");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.RenameUserDeviceAsync(retired, "tv", "Office", "first"));
        Assert.Empty(handler.Requests);
        var context = api.CaptureContext(); handler.AfterRequest = () => client.SetProfile("third");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.SaveProfileDefaultAsync(context, "ui.next_up_mode", "separate", hasDeviceOverride: true));
        Assert.Single(handler.Requests);
        Assert.StartsWith("PUT ", handler.Requests[0]);
    }

    [Fact]
    public async Task ProfileDefaultWritesBeforeClearingShadowAndResetClearsBothScopes()
    {
        var (api, handler) = Create("{}");
        await api.SaveProfileDefaultAsync(api.CaptureContext(), "ui.next_up_mode", "separate", hasDeviceOverride: true);
        Assert.Equal(new[] { "PUT /api/v2/settings/values/ui.next_up_mode?scope=profile", "DELETE /api/v2/settings/values/ui.next_up_mode?scope=profile_device" }, handler.Requests);
        handler.Requests.Clear();
        await api.ResetProfileDefaultAsync(api.CaptureContext(), "ui.next_up_mode");
        Assert.Equal(new[] { "DELETE /api/v2/settings/values/ui.next_up_mode?scope=profile", "DELETE /api/v2/settings/values/ui.next_up_mode?scope=profile_device" }, handler.Requests);
        handler.Requests.Clear();
        await api.SaveProfileDefaultAsync(api.CaptureContext(), "ui.next_up_mode", "combined", hasDeviceOverride: false);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DeviceNamesPreserveHardwareNameAndCapabilityAbsenceIsSafe()
    {
        var (api, _) = Create("""{"items":[{"device_id":"tv","device_name":"Windows PC","custom_name":"Living Room","profile_id":"p1"}]}""");
        var device = (await api.GetUserDevicesAsync()).Devices.Single();
        Assert.Equal("Living Room", device.DisplayName);
        Assert.Equal("Windows PC", device.DeviceName);
        var (old, _) = Create("{}");
        Assert.False((await old.GetContractCapabilitiesAsync()).SupportsDeviceRename);
        var (current, _) = Create("""{"supports_device_rename":true}""");
        Assert.True((await current.GetContractCapabilitiesAsync()).SupportsDeviceRename);
    }

    [Fact]
    public async Task RenameAndResetTargetDeviceOwnerAndPreserveExplicitNull()
    {
        var (api, handler) = Create("{}");
        await api.RenameUserDeviceAsync(api.CaptureContext(), "tv/id", "Living Room", "owner");
        Assert.Equal("PATCH /api/v2/devices/tv%2Fid?profile_id=owner", handler.Requests.Last());
        Assert.Equal("Living Room", JsonDocument.Parse(handler.Body).RootElement.GetProperty("custom_name").GetString());
        await api.RenameUserDeviceAsync(api.CaptureContext(), "tv/id", null, "owner");
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(handler.Body).RootElement.GetProperty("custom_name").ValueKind);
        var count = handler.Requests.Count;
        await Assert.ThrowsAsync<ArgumentException>(() => api.RenameUserDeviceAsync(api.CaptureContext(), "tv", "   ", "owner"));
        await Assert.ThrowsAsync<ArgumentException>(() => api.RenameUserDeviceAsync(api.CaptureContext(), "tv", new string('x', 121), "owner"));
        Assert.Equal(count, handler.Requests.Count);
    }

    [Fact]
    public async Task OfficialTypedSettingAndCapabilitiesFixturesReadWithoutHashRevisionConfusion()
    {
        var (api, _) = Create(Fixture("get_setting_value_ok"));
        Assert.Equal("cinema-light", (await api.GetSettingAsync("ui_theme")).Value);
        var (effective, _) = Create(Fixture("list_effective_settings_ok"));
        Assert.Equal(2, (await effective.GetContractEffectiveSettingsAsync(["ui.theme", "playback.preferred_quality"])).Settings.Count);
        var (capabilities, _) = Create(Fixture("get_settings_contract_capabilities_ok"));
        Assert.Equal(12, (await capabilities.GetContractCapabilitiesAsync()).Revision);
    }

    [Fact]
    public async Task OfficialCollectionFixturesPreserveDevicePluginAndLibraryPreferenceData()
    {
        var (devices, _) = Create(Fixture("list_devices_ok"));
        Assert.Equal("d-1", (await devices.GetUserDevicesAsync()).Devices[0].DeviceId);
        var (plugins, _) = Create(Fixture("list_plugin_settings_ok"));
        Assert.Equal(3, (await plugins.GetPluginSettingsListAsync()).Installations[0].Id);
        var (plugin, _) = Create(Fixture("get_plugin_settings_ok"));
        Assert.Equal(3, (await plugin.GetPluginSettingsAsync(3)).Installation.Id);
        var (preferences, handler) = Create(Fixture("list_library_playback_preferences_ok"));
        Assert.Equal("en", (await preferences.GetLibraryPlaybackPrefsAsync()).Preferences[0].AudioLanguage);
        await preferences.SetLibraryPlaybackPrefsAsync(1, new Dictionary<string, object?> { ["audio_language"] = null });
        Assert.Equal("PATCH /api/v2/library-playback-prefs/1", handler.Requests.Last());
        Assert.Contains("null", handler.Body);
    }

    [Fact]
    public async Task OfficialSectionFixturesPreserveStructuredRecipeConfiguration()
    {
        var (overrides, _) = Create(Fixture("list_profile_section_overrides_ok"));
        var rows = (await overrides.GetProfileSectionsAsync()).Overrides;
        Assert.Equal("s-continue", rows[0].SectionId);
        using var config = JsonDocument.Parse(rows[1].UserConfig);
        Assert.Equal(3, config.RootElement.GetProperty("library_ids")[0].GetInt32());
        var (settings, _) = Create(Fixture("get_profile_section_settings_ok"));
        Assert.Equal(2, (await settings.GetProfileSectionSettingsAsync()).Sections.Count);
        var (recipes, _) = Create(Fixture("list_section_recipes_ok"));
        Assert.Equal("recently_added", (await recipes.GetRecipeCatalogAsync()).Categories["library_staples"][0].Type);
    }

    [Fact]
    public async Task OfficialThemeDocumentsPreserveCamelCasePortableFileFields()
    {
        var (catalog, _) = Create(Fixture("theme_catalog_document"));
        Assert.Equal("https://themes.example/theme.json", (await catalog.GetThemeCatalogAsync()).Themes[0].DownloadUrl);
        var (refresh, _) = Create(Fixture("theme_catalog_refreshed"));
        Assert.NotEmpty((await refresh.RefreshThemeCatalogAsync()).Themes);
        var (theme, _) = Create(Fixture("theme_download_document"));
        var file = await theme.DownloadThemeAsync("https://themes.example/theme.json");
        Assert.Equal("midnight-cinema", file.BaseTheme);
        Assert.Equal("body { color: red; }", file.CustomCss);
    }

    [Fact]
    public async Task LegacyBooleanWriteUsesCanonicalKeyAndJsonBoolean()
    {
        var (api, handler) = Create("{}");
        await api.PutSettingAsync("ui_high_contrast", "true");
        Assert.Equal("/api/v2/settings/values/ui.high_contrast?scope=profile", handler.Path);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(JsonValueKind.True, body.RootElement.GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task EffectiveReadsExplodeKeysAndRestoreCallerAliases()
    {
        var (api, handler) = Create("""{"items":[{"key":"ui.theme","value":"cinema-light","source":"profile"},{"key":"ui.high_contrast","value":true,"source":"profile_device"}],"revision":12}""");
        var effective = await api.GetEffectiveSettingsAsync(["ui_theme", "ui_high_contrast"]);
        Assert.Equal("/api/v2/settings/values/effective?keys=ui.theme&keys=ui.high_contrast", handler.Path);
        Assert.Equal("cinema-light", effective.Settings.Single(x => x.Key == "ui_theme").EffectiveValue);
        Assert.True(effective.Settings.Single(x => x.Key == "ui_high_contrast").HasDeviceOverride);
    }

    [Fact]
    public async Task OnboardingReadsRevisionThenPutsProgressWithIfMatch()
    {
        var (api, handler) = Create("{}");
        await api.ReportOnboardingProgressAsync("tour-1", "step-1", completed: true);
        Assert.Equal(new[] { "GET /api/v2/onboarding/state", "PUT /api/v2/onboarding/progress" }, handler.Requests);
        Assert.Equal("\"fixture-revision\"", handler.IfMatch);
        Assert.DoesNotContain("null", handler.Body);
    }

    [Fact]
    public async Task OnboardingRefusesMutationWhenProfileChangesDuringRevisionRead()
    {
        var handler = new RecordingHandler("{}");
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.example.test");
        client.SetProfile("first");
        handler.AfterRequest = () => client.SetProfile("second");
        await Assert.ThrowsAsync<OperationCanceledException>(() => new SettingsApi(client).ReportOnboardingProgressAsync("tour-1"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task OnboardingRefusesMutationWithoutRevision()
    {
        var (api, handler) = Create("{}");
        handler.IncludeETag = false;
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ReportOnboardingProgressAsync("tour-1"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DeviceListWalksAllHouseholdPages()
    {
        var (api, handler) = Create("{}");
        var responses = new Queue<string>([
            """{"items":[{"device_id":"first"}],"page":{"has_more":true,"next_cursor":"opaque/+"}}""",
            """{"items":[{"device_id":"second"}],"page":{"has_more":false}}""",
        ]);
        handler.NextResponse = () => responses.Dequeue();
        Assert.Equal(2, (await api.GetUserDevicesAsync(household: true)).Devices.Count);
        Assert.Equal("GET /api/v2/devices?scope=household&limit=100&cursor=opaque%2F%2B", handler.Requests[1]);
    }

    [Fact]
    public async Task SectionsMoveScopeToQueryAndOmitUnsetNonNullableFields()
    {
        var (api, handler) = Create("{}");
        await api.UpdateProfileSectionsAsync(new SaveOverridesRequest { Scope = "library", LibraryId = "3", Overrides = [new SectionOverride { SectionId = "s-one", Hidden = true }] });
        Assert.Equal("/api/v2/profile/sections?scope=library&library_id=3", handler.Path);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.False(body.RootElement.TryGetProperty("scope", out _));
        Assert.False(body.RootElement.GetProperty("overrides")[0].TryGetProperty("title", out _));
        await api.ResetProfileSectionsAsync();
        Assert.Equal("/api/v2/profile/sections?scope=home", handler.Path);
    }

    [Fact]
    public async Task DeviceWritesPreserveJsonDocumentsAndProfileResetUsesPatchNull()
    {
        var (api, handler) = Create("{}");
        await api.PutDeviceSettingAsync("subtitle_appearance", "{\"fontSize\":\"large\"}");
        Assert.Equal("/api/v2/settings/values/playback.subtitle_appearance?scope=profile_device", handler.Path);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("value").ValueKind);
        await api.UpdateProfileAsync("p-one", new Dictionary<string, object?> { ["pin"] = "", ["max_playback_quality"] = "" });
        Assert.Equal("PATCH /api/v2/profiles/p-one", handler.Requests.Last());
        using var profile = JsonDocument.Parse(handler.Body);
        Assert.Equal(JsonValueKind.Null, profile.RootElement.GetProperty("pin").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.RootElement.GetProperty("max_playback_quality").ValueKind);
    }

    private static string Fixture(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = System.IO.Path.Combine(directory.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "ApiV2Settings", name + ".json");
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(name);
    }

    private static (SettingsApi, RecordingHandler) Create(string response)
    {
        var handler = new RecordingHandler(response);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://silo.example.test");
        return (new SettingsApi(client), handler);
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public string Path { get; private set; } = "";
        public string Body { get; private set; } = "";
        public string? IfMatch { get; private set; }
        public List<string> Requests { get; } = [];
        public bool IncludeETag { get; set; } = true;
        public Action? AfterRequest { get; set; }
        public Func<string>? NextResponse { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.PathAndQuery;
            Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            IfMatch = request.Headers.IfMatch.FirstOrDefault()?.ToString();
            Requests.Add(request.Method + " " + Path);
            var result = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(NextResponse?.Invoke() ?? response) };
            if (IncludeETag) result.Headers.ETag = new EntityTagHeaderValue("\"fixture-revision\"");
            AfterRequest?.Invoke();
            return result;
        }
    }
}
