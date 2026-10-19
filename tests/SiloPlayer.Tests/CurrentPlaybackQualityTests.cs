using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class CurrentPlaybackQualityTests
{
    [Fact]
    public async Task CanonicalQualityAndCapReachStartAndRemainThroughManualQualityChange()
    {
        using var scope = new Fixture();
        await scope.Manager.StartSessionAsync(42);
        Assert.Equal("1080p", scope.Wire.Start!.Value.GetProperty("quality_preference").GetString());
        Assert.Equal("1080p", scope.Manager.QualityPreference);
        Assert.Equal("auto", scope.Manager.CurrentSession!.ActiveQuality); // Direct source delivery does not claim a transcoded tier.
        Assert.Equal(3500, scope.Wire.Start.Value.GetProperty("bandwidth_cap_kbps").GetInt32());
        await scope.Manager.ReplanQualityAsync("auto", 17);
        Assert.Equal("auto", scope.Wire.Replan!.Value.GetProperty("quality_preference").GetString());
        Assert.Equal(3500, scope.Wire.Replan.Value.GetProperty("bandwidth_cap_kbps").GetInt32());
    }

    [Theory]
    [InlineData("original")]
    [InlineData("source")]
    [InlineData("max")]
    public async Task RetiredSourceAliasesSendAuto(string alias)
    {
        using var scope = new Fixture();
        await scope.Manager.StartSessionAsync(42, qualityPreference: alias);
        Assert.Equal("auto", scope.Wire.Start!.Value.GetProperty("quality_preference").GetString());
    }

    [Fact]
    public async Task MissingOlderSettingsEndpointUsesLegacyPreferenceWithoutInventingCap()
    {
        using var scope = new Fixture(); scope.Wire.MissingSettings = true;
        await scope.Manager.StartSessionAsync(42);
        Assert.Equal("720p", scope.Wire.Start!.Value.GetProperty("quality_preference").GetString());
        Assert.True(!scope.Wire.Start.Value.TryGetProperty("bandwidth_cap_kbps", out var cap) || cap.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task RetiredProfileCannotStartPlaybackAfterSettingsReply()
    {
        using var scope = new Fixture();
        scope.Wire.BeforeSettingsReply = () => scope.Client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Manager.StartSessionAsync(42));
        Assert.Null(scope.Wire.Start);
    }

    [Fact]
    public async Task SourceMetadataIdentifiesCroppedTierWithoutOriginalMenuRung()
    {
        using var scope = new Fixture(); scope.Wire.Cropped = true;
        var result = await scope.Manager.StartSessionAsync(42, qualityPreference: "2160p-medium");
        Assert.Equal("2160p-medium", result.ActiveQuality);
    }

    [Fact]
    public void Revision20DeviceEditorsIncludeCellularPairAndExcludeOriginal()
    {
        var definitions = DeviceSettingDisplay.ForRevision(20);
        Assert.Contains(definitions, d => d.Key == "playback.cellular_preferred_quality");
        Assert.Contains(definitions, d => d.Key == "playback.cellular_max_bitrate_kbps");
        Assert.DoesNotContain(definitions.Single(d => d.Key == "playback.preferred_quality").Options, option => option.Value == "original");
        Assert.DoesNotContain(DeviceSettingDisplay.ForRevision(19), d => d.Key.StartsWith("playback.cellular_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolvedPreferencesAreReusedForVersionChoiceAndStart()
    {
        using var scope = new Fixture();
        var preferences = await scope.Manager.ResolveQualityPreferencesAsync();
        var best = scope.Manager.SelectBestVersion([new() { FileId = 1, Resolution = "720p" }, new() { FileId = 2, Resolution = "1080p" }], preferences.Quality);
        Assert.Equal(2, best!.FileId);
        await scope.Manager.StartSessionAsync(best.FileId, preferences: preferences);
        Assert.Equal(1, scope.Wire.SettingsReads);
        await scope.Manager.ReplanAudioAsync(1, 20);
        Assert.Equal(3500, scope.Wire.Replan!.Value.GetProperty("bandwidth_cap_kbps").GetInt32());
    }

    [Fact]
    public void SharedQualityOptionsKeepServerNamesAndSkipMenusWithoutFixedTiers()
    {
        Assert.Empty(PlaybackQualityPreferences.MenuOptions([]));
        Assert.Empty(PlaybackQualityPreferences.MenuOptions([new() { Label = "original", PreservesSource = true }]));
        var options = PlaybackQualityPreferences.MenuOptions([new() { Label = "original", PreservesSource = true }, new() { Label = "1080p-medium", DisplayName = "Full HD" }]);
        Assert.Equal(new[] { "auto", "1080p-medium" }, options.Select(option => option.Label));
        Assert.Equal("Full HD", options[1].DisplayName);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Wire Wire = new();
        public readonly SiloApiClient Client;
        public readonly PlaybackManager Manager;
        private readonly AuthService _auth;
        public Fixture()
        {
            Client = new(new HttpClient(Wire)); Client.SetBaseUrl("https://quality-fixture.invalid");
            _auth = new(Client, new AuthApi(Client));
            _auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
            _auth.SelectProfile("fixture", profile: new() { Id = "fixture", QualityPreference = "720p" });
            Manager = new(new PlaybackApi(Client), new CatalogApi(Client), _auth, Client);
        }
        public void Dispose() { Manager.Dispose(); _auth.Dispose(); }
    }

    private sealed class Wire : HttpMessageHandler
    {
        public JsonElement? Start, Replan;
        public bool MissingSettings, Cropped;
        public int SettingsReads;
        public Action? BeforeSettingsReply;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/settings/values/effective")
            {
                SettingsReads++;
                BeforeSettingsReply?.Invoke();
                if (MissingSettings) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
                return Json("""{"items":[{"key":"playback.preferred_quality","value":"1080p"},{"key":"playback.max_bitrate_kbps","value":3500}]}""");
            }
            if (path == "/api/v2/playback/capabilities")
                return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (path == "/api/v2/playback/start" || path.EndsWith("/replan", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                if (path.EndsWith("/start", StringComparison.Ordinal)) Start = body.RootElement.Clone(); else Replan = body.RootElement.Clone();
                var decision = """{"protocol_version":3,"outcome":"playable","session_id":"session","playback_plan":{"plan_id":"plan","plan_attempt_key":"plan-key","session_id":"session","delivery":"original_http","stream":{"url":"/stream"},"source":{"media_file_id":42,"height":1540,"bitrate_kbps":20000},"timeline":{"can_seek_anywhere":true},"available_qualities":[{"label":"2160p-medium","height":2160,"bitrate_kbps":25000}],"effective_recipe":{}}}""";
                if (Cropped) decision = decision.Replace("original_http", "server_transcode_hls").Replace("\"effective_recipe\":{}", "\"effective_recipe\":{\"height\":1540,\"bitrate_kbps\":3500}");
                return Json(decision);
            }
            return Json("""{"outcome":"applied"}""");
        }
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }
}
