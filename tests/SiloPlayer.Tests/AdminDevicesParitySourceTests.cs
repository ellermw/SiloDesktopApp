namespace SiloPlayer.Tests;

public sealed class AdminDevicesParitySourceTests
{
    [Fact]
    public void DevicesUsesCurrentListDetailAndMutationContracts()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var model = ReadRepoFile("src", "SiloPlayer.Core", "Models", "Admin", "AdminDevice.cs");

        Assert.Contains("/api/v1/admin/devices", api);
        Assert.Contains("/api/v1/admin/devices/{userId}/{Uri.EscapeDataString(deviceId)}", api);
        Assert.Contains("/profiles/{Uri.EscapeDataString(profileId)}/device-settings/", api);
        Assert.Contains("/profiles/{Uri.EscapeDataString(profileId)}/devices/{Uri.EscapeDataString(deviceId)}/settings", api);
        Assert.Contains("public List<AdminDeviceProfileSummary> Profiles", model);
        Assert.Contains("public List<AdminDeviceSetting> Settings", model);
    }

    [Fact]
    public void DevicesIsRegisteredAndInCurrentUsersNavigationOrder()
    {
        var app = ReadRepoFile("src", "SiloPlayer", "App.xaml.cs");
        var shell = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml.cs");
        var title = ReadRepoFile("src", "SiloPlayer", "Helpers", "DocumentTitle.cs");

        Assert.Contains("AdminDevicesViewModel", app);
        Assert.Contains("typeof(AdminDevicesPage)", shell);
        Assert.Contains("AddNavGroup(\"USERS\", NavUsers, NavAccessGroups, NavDevices, NavPlaybackHistory, NavHistoryImport)", shell);
        Assert.Contains("Admin · Devices", title);
    }

    [Fact]
    public void DeviceDetailCoversEveryCurrentManifestSettingAndResetScopes()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "Admin", "AdminDevicesViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminDevicesPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminDevicesPage.xaml.cs");

        string[] keys =
        [
            "playback.preferred_quality", "playback.audio_language", "playback.auto_skip_intro",
            "playback.auto_skip_credits", "playback.auto_skip_recap", "playback.auto_play_next_preview",
            "playback.auto_play_next", "playback.next_up_prompt_seconds", "subtitle_appearance",
            "ui.remember_library_page_state", "player.hdr_enabled", "player.dv_profile7_hdr10_fallback",
            "player.playback_speed", "player.audio_sync_ms", "player.subtitle_sync_ms",
            "player.video_gravity", "player.orientation_mode",
        ];
        foreach (var key in keys) Assert.Contains(key, viewModel);

        Assert.Contains("Search devices, users, IDs, profiles", page);
        Assert.Contains("Devices with Overrides", page);
        Assert.Contains("SAVED VIEWS", page);
        Assert.Contains("OVERRIDE COUNT", page);
        Assert.Contains("LAST SEEN", page);
        Assert.Contains("MaxWidth=\"1920\"", page);
        Assert.Contains("DeviceGroups", page);
        Assert.Contains("Reset profile", page);
        Assert.Contains("SearchShortcutHint", page);
        Assert.Contains("ClearSearchButton", page);
        Assert.Contains("HorizontalContentAlignment=\"Stretch\"", page);
        Assert.Contains("android tv", viewModel);
        Assert.Contains("fire tv", viewModel);
        Assert.Contains("device.OverrideCount >= median * 2", viewModel);
        Assert.Contains("private IEnumerable<AdminDeviceSummary> ScopedDevices", viewModel);
        Assert.Contains("SaveSettingAsync", code);
        Assert.Contains("ResetSettingAsync", code);
        Assert.Contains("Settings_CollectionChanged", code);
        Assert.Contains("x:Name=\"DeviceWorkspaceGrid\"", page);
        Assert.Contains("x:Name=\"FleetPulseGrid\"", page);
        Assert.Contains("ApplyResponsiveLayout", code);
        Assert.Contains("width >= 1024", code);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? "";
        if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("Could not find repository root.");
        return File.ReadAllText(Path.Combine([dir, .. parts]));
    }
}
