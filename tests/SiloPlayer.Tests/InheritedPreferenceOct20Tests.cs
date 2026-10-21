using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class InheritedPreferenceOct20Tests
{
    [Theory]
    [InlineData("profile", "profile_device,profile")]
    [InlineData("profile_device", "profile_device")]
    [InlineData("default", "profile_device")]
    public async Task TitleArtResetDeletesOnlyCurrentDeviceAndEffectiveProfileChoice(string source, string expected)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://default-fixture.invalid");
        await new InheritedPreferencePolicy(new SettingsApi(client)).ResetAsync(client.CaptureContext(), "ui.title_art", source);
        Assert.Equal(expected, string.Join(",", wire.Scopes));
    }

    [Fact]
    public async Task ProfileSwitchBetweenClearsCannotDeleteReplacementProfile()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://default-fixture.invalid");
        wire.AfterDelete = () => client.SetProfile("other");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new InheritedPreferencePolicy(new SettingsApi(client))
            .ResetAsync(client.CaptureContext(), "ui.title_art", "profile"));
        Assert.Single(wire.Scopes);
    }

    [Theory]
    [InlineData("default", null, "selected", true, true)]
    [InlineData("profile", "selected", "selected", true, false)]
    [InlineData("profile", "other", "selected", false, false)]
    [InlineData("profile", "other", null, false, false)]
    public void RealtimeDefaultsIgnoreOtherProfileAndScheduleOneSettlingRead(string scope, string? profile, string? current, bool expected, bool settle)
    {
        var data = JsonSerializer.SerializeToElement(new { key = "ui.next_up_mode", scope, profile_id = profile });
        Assert.Equal(expected, InheritedPreferencePolicy.TryReadRealtimeChange("user_settings", "user_settings.changed", data, current, out var change));
        if (expected) { Assert.Equal("ui.next_up_mode", change.Key); Assert.Equal(settle, change.NeedsSettlingRead); }
    }

    [Fact]
    public void UnrelatedKeysDoNotRefreshAppearanceOrHome()
    {
        var data = JsonSerializer.SerializeToElement(new { key = "playback.subtitle_mode", scope = "default" });
        Assert.False(InheritedPreferencePolicy.TryReadRealtimeChange("user_settings", "user_settings.changed", data, "selected", out _));
    }

    [Fact]
    public void DeviceTitleArtMetadataLabelsInheritedAdministratorDefault()
    {
        Assert.Equal("Server default", DeviceSettingDisplay.InheritedSource(new ContractEffectiveSettingEntry
            { Key = "ui.title_art", Source = "default" }, "Viewer"));
        Assert.Equal("App default", DeviceSettingDisplay.InheritedSource(new ContractEffectiveSettingEntry
            { Key = "player.playback_speed", Source = "default" }, "Viewer"));
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal List<string> Scopes = [];
        internal Action? AfterDelete;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            Scopes.Add(query["scope"]!);
            AfterDelete?.Invoke();
            // Missing stored values are a successful reset, not an error.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
