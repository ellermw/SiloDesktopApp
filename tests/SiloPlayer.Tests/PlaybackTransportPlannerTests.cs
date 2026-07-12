using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackTransportPlannerTests
{
    [Theory]
    [InlineData("/stream/session-1?st=recipe", "progressive")]
    [InlineData("https://proxy.example/stream/direct/signed-token", "progressive")]
    public void Plan_ClassifiesIntegratedAndDistributedDirectAsProgressive(
        string streamUrl,
        string streamType)
    {
        var plan = PlaybackTransportPlanner.Plan("direct", streamUrl, streamType);

        Assert.Equal(PlaybackSemanticMethod.Direct, plan.SemanticMethod);
        Assert.Equal(PlaybackTransportKind.DirectProgressive, plan.TransportKind);
        Assert.False(plan.IsHls);
        Assert.False(plan.RequiresTranscodeStartPreparation);
    }

    [Fact]
    public void Plan_ClassifiesIntegratedRemuxAsProgressive()
    {
        var plan = PlaybackTransportPlanner.Plan(
            "remux",
            "/stream/session-1?st=recipe",
            "progressive");

        Assert.Equal(PlaybackSemanticMethod.Remux, plan.SemanticMethod);
        Assert.Equal(PlaybackTransportKind.RemuxProgressive, plan.TransportKind);
        Assert.False(plan.IsHls);
        Assert.False(plan.RequiresTranscodeStartPreparation);
    }

    [Fact]
    public void Plan_DistributedRemuxUrlOverridesStaleProgressiveMetadata()
    {
        var plan = PlaybackTransportPlanner.Plan(
            "remux",
            "https://proxy.example/stream/transcode/signed-token/master.m3u8",
            "progressive");

        Assert.Equal(PlaybackSemanticMethod.Remux, plan.SemanticMethod);
        Assert.Equal(PlaybackTransportKind.RemuxHls, plan.TransportKind);
        Assert.True(plan.IsHls);
        Assert.True(plan.RequiresTranscodeStartPreparation);
    }

    [Theory]
    [InlineData("/playback/transcode/session-1/master.m3u8?st=recipe")]
    [InlineData("https://proxy.example/stream/transcode/signed-token/master.m3u8")]
    public void Plan_ClassifiesIntegratedAndDistributedTranscodeAsHls(string streamUrl)
    {
        var plan = PlaybackTransportPlanner.Plan("transcode", streamUrl, "hls");

        Assert.Equal(PlaybackSemanticMethod.Transcode, plan.SemanticMethod);
        Assert.Equal(PlaybackTransportKind.TranscodeHls, plan.TransportKind);
        Assert.True(plan.IsHls);
        Assert.True(plan.RequiresTranscodeStartPreparation);
    }

    [Theory]
    [InlineData("https://example.test/video.M3U8?token=access-token")]
    [InlineData("https://proxy.example/stream/transcode/signed-token/not-a-playlist")]
    public void Plan_RejectsDirectSessionWithHlsTransport(string streamUrl)
    {
        var exception = Assert.Throws<PlaybackTransportContractException>(
            () => PlaybackTransportPlanner.Plan("direct", streamUrl, "progressive"));

        Assert.Contains("direct-play", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_UsesReportedHlsWhenUrlDoesNotIdentifyTransport()
    {
        var plan = PlaybackTransportPlanner.Plan("remux", "/opaque/session-1", "HLS");

        Assert.Equal(PlaybackTransportKind.RemuxHls, plan.TransportKind);
        Assert.True(plan.RequiresTranscodeStartPreparation);
    }

    [Fact]
    public void Plan_AcceptsPlaybackStartResponse()
    {
        var response = new PlaybackStartResponse
        {
            PlayMethod = "remux",
            StreamUrl = "https://proxy.example/stream/transcode/token/master.m3u8",
            PlaybackInfo = new PlaybackInfo { StreamType = "progressive" },
        };

        var plan = PlaybackTransportPlanner.Plan(response);

        Assert.Equal(PlaybackTransportKind.RemuxHls, plan.TransportKind);
    }
}
