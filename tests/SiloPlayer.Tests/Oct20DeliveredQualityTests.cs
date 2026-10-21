using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class Oct20DeliveredQualityTests
{
    [Theory]
    [InlineData("1080p-medium", "1080p-medium", true, "server_transcode_hls", 1080, 3500, "1080p-medium")]
    [InlineData("1080p", "720p-medium", true, "server_transcode_hls", 688, 2000, "720p-medium")]
    [InlineData("1080p", "720p", true, "original_http", 720, 4000, "720p")]
    [InlineData("1080p-medium", null, true, "server_transcode_hls", 1080, 6000, "")]
    [InlineData("1080p-medium", "unknown-rung", true, "server_transcode_hls", 1080, 6000, "")]
    [InlineData("1080p-medium", "1080p-medium", false, "server_transcode_hls", 1080, 3500, "1080p-medium")]
    [InlineData("1080p-medium", null, false, "server_transcode_hls", 1080, 6000, "1080p-medium")]
    [InlineData("auto", "720p-medium", true, "server_transcode_hls", 688, 2000, "720p-medium")]
    public async Task ServerDeliveredRungIsAuthoritativeAndOlderPlansRetainFallback(string preference,
        string? active, bool feature, string delivery, int height, int bitrate, string expected)
    {
        using var wire = new QualityWire(active, feature, delivery, height, bitrate);
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://delivered-quality.fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        using var manager = new PlaybackManager(new PlaybackApi(client), new CatalogApi(client), auth, client);

        var session = await manager.StartSessionAsync(42, qualityPreference: preference);

        Assert.Equal(expected, session.ActiveQuality);
        Assert.Equal(preference, manager.QualityPreference);
        Assert.Equal(preference == "auto" ? "auto" : expected == "auto" ? "" : expected, manager.SelectedQualityMenuId);
        await manager.ReplanAudioAsync(1, 18);
        Assert.Equal(preference, wire.LastQuality); // Delivery must never overwrite future replan intent.
        Assert.Equal(expected, manager.CurrentSession!.ActiveQuality);
    }

    [Theory]
    [InlineData("1080p-medium", "1080p-medium", 3500, "1080p-low")]
    [InlineData("1080p-medium", "1080p-medium", 2000, "720p-low")]
    [InlineData("1080p-medium", "1080p-medium", 1000, null)]
    [InlineData("1080p-low", "1080p-low", 3500, "720p-medium")]
    [InlineData("auto", "1080p-medium", 3500, "1080p-low")]
    public async Task RepeatedStallOfferStepsBelowBothNominalAndDeliveredBitrate(string preference,
        string active, int deliveredBitrate, string? expected)
    {
        using var wire = new QualityWire(active, true, "server_transcode_hls", 1080, deliveredBitrate);
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://delivered-quality.fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        using var manager = new PlaybackManager(new PlaybackApi(client), new CatalogApi(client), auth, client);
        await manager.StartSessionAsync(42, qualityPreference: preference);

        Assert.Equal(expected, manager.GetLowerQualityOption(40000)?.Label);
    }

    internal sealed class QualityWire(string? active, bool feature, string delivery, int height, int bitrate) : HttpMessageHandler
    {
        public string? LastQuality { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/capabilities"))
                return Json(new { allowed = true, state = "available", protocol_versions = new[] { 3 }, installation_id = "11111111-1111-4111-8111-111111111111" });
            if (path.EndsWith("/start") || path.EndsWith("/replan"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                LastQuality = body.RootElement.GetProperty("quality_preference").GetString();
                return Json(new
                {
                    protocol_version = 3, outcome = "playable", session_id = "quality-session",
                    server_features = feature ? new[] { "plan_active_quality_v1" } : [],
                    playback_plan = new
                    {
                        plan_id = "quality-plan", plan_attempt_key = "quality-key", session_id = "quality-session",
                        delivery, effective_media_file_id = 42, stream = new { url = "/stream" },
                        source = new { height = 2160, bitrate_kbps = 40000, duration_seconds = 1800 },
                        timeline = new { can_seek_anywhere = true }, effective_recipe = new { height, bitrate_kbps = bitrate },
                        active_quality = active,
                        available_qualities = new[]
                        {
                            new { label = "1080p-medium", height = 1080, bitrate_kbps = 6000 },
                            new { label = "1080p-low", height = 1080, bitrate_kbps = 3000 },
                            new { label = "720p-medium", height = 720, bitrate_kbps = 2000 },
                            new { label = "720p-low", height = 720, bitrate_kbps = 1500 },
                            new { label = "720p", height = 720, bitrate_kbps = 4000 },
                        },
                    },
                });
            }
            if (path.Contains("/settings/")) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            return Json(new { outcome = "applied" });
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
    }
}
