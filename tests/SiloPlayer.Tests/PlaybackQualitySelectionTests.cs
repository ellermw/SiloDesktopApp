using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackQualitySelectionTests
{
    [Theory]
    [InlineData("720p-high", 720, 4000)]
    [InlineData("720p-medium", 720, 2000)]
    [InlineData("720p-low", 720, 1500)]
    [InlineData("1080p-high", 1080, 10000)]
    [InlineData("1080p-medium", 1080, 6000)]
    [InlineData("1080p-low", 1080, 3000)]
    [InlineData("2160p-high", 2160, 40000)]
    [InlineData("2160p-medium", 2160, 20000)]
    [InlineData("2160p-low", 2160, 10000)]
    public async Task MenuTierReachesServerWithItsBitrateAndReplacesOriginalStream(string tier, int height, int bitrate)
    {
        var handler = new QualityHandler(tier, height, bitrate);
        using var manager = CreateManager(handler);
        await manager.StartSessionAsync(42, forceStartPosition: true);

        var response = await manager.ReplanQualityAsync(tier, 93.3);

        Assert.Equal(tier, handler.LastQuality);
        Assert.Equal("transcode", response.PlayMethod);
        Assert.EndsWith("/master.m3u8", manager.StreamUrl);
        Assert.Equal(tier, response.ActiveQuality);
        Assert.Equal(bitrate, response.PlaybackInfo?.TargetVideoBitrateKbps);
        Assert.Equal(93.3, response.Position);
        // An audio change must retain the selected bandwidth restriction.
        await manager.ReplanAudioAsync(1, 95);
        Assert.Equal(tier, handler.LastQuality);
        await manager.StopSessionAsync();
    }

    [Fact]
    public async Task InitialSessionPreservesExplicitTier()
    {
        var handler = new QualityHandler("720p-medium", 720, 2000);
        using var manager = CreateManager(handler);
        var response = await manager.StartSessionAsync(42, forceStartPosition: true, qualityPreference: "720p-medium");
        Assert.Equal("720p-medium", handler.LastQuality);
        Assert.Equal("720p-medium", response.ActiveQuality);
        await manager.StopSessionAsync();
    }

    [Theory]
    [InlineData("720p-high", 720, 4000, 1080, 3500, 720, 3500, "720p-high")]
    [InlineData("2160p-medium", 2160, 20000, 1540, 25200, 1540, 20000, "2160p-medium")]
    [InlineData("720p-medium", 720, 2000, 1080, 8500, 1080, 8500, "auto")]
    [InlineData("720p-medium", 720, 2000, 1080, 8500, 720, 2500, "auto")]
    public async Task ActiveTierValidatesSourceLimitedRecipe(string tier, int height, int bitrate,
        int sourceHeight, int sourceBitrate, int outputHeight, int outputBitrate, string expected)
    {
        var handler = new QualityHandler(tier, height, bitrate)
        {
            SourceHeight = sourceHeight, SourceBitrate = sourceBitrate,
            OutputHeight = outputHeight, OutputBitrate = outputBitrate,
        };
        using var manager = CreateManager(handler);
        var response = await manager.StartSessionAsync(42, forceStartPosition: true, qualityPreference: tier);
        Assert.Equal(expected, response.ActiveQuality);
        Assert.Equal(outputBitrate, response.PlaybackInfo?.TargetVideoBitrateKbps);
        await manager.StopSessionAsync();
    }

    [Fact]
    public async Task OriginalResponseDoesNotClaimRequestedTier()
    {
        var handler = new QualityHandler("720p-medium", 720, 2000) { ReturnOriginal = true };
        using var manager = CreateManager(handler);
        var response = await manager.StartSessionAsync(42, forceStartPosition: true, qualityPreference: "720p-medium");
        Assert.Equal("original", response.ActiveQuality);
        Assert.Null(response.PlaybackInfo?.TargetVideoBitrateKbps);
        await manager.StopSessionAsync();
    }

    private static PlaybackManager CreateManager(HttpMessageHandler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        return new PlaybackManager(new PlaybackApi(client), new CatalogApi(client),
            new AuthService(client, new AuthApi(client)), client);
    }

    private sealed class QualityHandler(string selectedTier, int height, int bitrate) : HttpMessageHandler
    {
        public string? LastQuality { get; private set; }
        public int SourceHeight { get; init; } = 2160;
        public int SourceBitrate { get; init; } = 50000;
        public int? OutputHeight { get; init; }
        public int? OutputBitrate { get; init; }
        public bool ReturnOriginal { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/start") || path.EndsWith("/replan"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                LastQuality = body.RootElement.GetProperty("quality_preference").GetString();
                var restricted = LastQuality == selectedTier && !ReturnOriginal;
                var position = body.RootElement.TryGetProperty("position_seconds", out var pos) ? pos.GetDouble() : 0;
                var otherTier = selectedTier.EndsWith("-high") ? $"{height}p-low" : $"{height}p-high";
                var json = JsonSerializer.Serialize(new
                {
                    protocol_version = 3, outcome = "playable", session_id = "quality-test",
                    playback_plan = new
                    {
                        protocol_version = 3, plan_id = "quality-plan", plan_attempt_key = "quality-key",
                        session_id = "quality-test", effective_media_file_id = 42, requested_media_file_id = 42,
                        delivery = restricted ? "server_transcode_hls" : "original_http",
                        stream = new { url = restricted ? "/playback/transcode/quality-test/master.m3u8" : "/stream/quality-test", protocol = restricted ? "hls" : "http_progressive" },
                        timeline = new { source_start_seconds = position, can_seek_anywhere = true },
                        selected_tracks = new { }, subtitle = new { mode = "off", inventory = Array.Empty<object>() },
                        source = new { duration_seconds = 1800 },
                        effective_recipe = new { video_codec = "h264", audio_codec = "aac", height = restricted ? OutputHeight ?? height : SourceHeight, bitrate_kbps = restricted ? OutputBitrate ?? bitrate : SourceBitrate },
                        available_qualities = new[]
                        {
                            new { label = "original", height = SourceHeight, bitrate_kbps = SourceBitrate, preserves_source = true },
                            // Same resolution, different bitrate comes first:
                            // matching height alone must not mislabel the plan.
                            new { label = otherTier, height, bitrate_kbps = bitrate + 1000, preserves_source = false },
                            new { label = selectedTier, height, bitrate_kbps = bitrate, preserves_source = false },
                        },
                    },
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
