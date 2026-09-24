using SiloPlayer.Player;

namespace SiloPlayer.Tests;

public class VideoUpscalingTests
{
    [Theory]
    [InlineData(null, true, VideoUpscalingMode.Nvidia)]
    [InlineData(null, false, VideoUpscalingMode.Off)]
    [InlineData("off", true, VideoUpscalingMode.Off)]
    [InlineData("automatic", false, VideoUpscalingMode.Automatic)]
    [InlineData("intel", false, VideoUpscalingMode.Intel)]
    [InlineData("fsrcnnx", false, VideoUpscalingMode.Fsrcnnx)]
    [InlineData("unknown", true, VideoUpscalingMode.Off)]
    public void MigratesLegacyPreferenceWithoutEnablingUnknownModes(string? saved, bool legacy, VideoUpscalingMode expected)
        => Assert.Equal(expected, VideoUpscalingPolicy.ResolveMode(saved, legacy));

    [Fact]
    public void AutomaticChoosesDiscreteAmdBeforeIntegratedIntelAndNeverSoftware()
    {
        VideoAdapterInfo[] adapters = [new("Intel UHD", 0x8086, false), new("AMD Radeon", 0x1002, false),
            new("NVIDIA RTX software", 0x10de, true)];
        var selection = VideoUpscalingPolicy.SelectAdapter(VideoUpscalingMode.Automatic, adapters);
        Assert.Equal(VideoUpscalingMode.Fsrcnnx, selection.Mode);
        Assert.Equal("AMD Radeon", selection.Adapter?.Name);
        Assert.Equal("Intel UHD", VideoUpscalingPolicy.SelectAdapter(VideoUpscalingMode.Intel, adapters).Adapter?.Name);
        Assert.Null(VideoUpscalingPolicy.SelectAdapter(VideoUpscalingMode.Nvidia, adapters).Adapter);
        Assert.Null(VideoUpscalingPolicy.SelectAdapter(VideoUpscalingMode.Off, adapters).Adapter);
    }

    [Fact]
    public void PortableNeuralShaderCanBeTestedOnRtxWithoutPretendingItIsAmdHardware()
    {
        VideoAdapterInfo[] adapters = [new("NVIDIA GeForce RTX 5080", 0x10de, false)];
        var selection = VideoUpscalingPolicy.SelectAdapter(VideoUpscalingMode.Fsrcnnx, adapters);
        Assert.Equal(VideoUpscalingMode.Fsrcnnx, selection.Mode);
        Assert.Equal(adapters[0], selection.Adapter);
        Assert.Equal(VideoUpscalingMode.Nvidia, VideoUpscalingPolicy.SelectAdapter(VideoUpscalingMode.Automatic, adapters).Mode);
        Assert.Null(VideoUpscalingPolicy.SelectAdapter(VideoUpscalingMode.Intel, adapters).Adapter);
        VideoAdapterInfo[] olderNvidia = [new("NVIDIA GeForce GTX 1080", 0x10de, false)];
        Assert.Equal(olderNvidia[0], VideoUpscalingPolicy.SelectAdapter(VideoUpscalingMode.Fsrcnnx, olderNvidia).Adapter);
    }

    [Theory]
    [InlineData(VideoUpscalingMode.Fsrcnnx, 1920, 1080, 3840, 2160, "bt.1886", 2)]
    [InlineData(VideoUpscalingMode.Fsrcnnx, 1920, 1080, 2560, 1440, "bt.1886", 2)]
    [InlineData(VideoUpscalingMode.Fsrcnnx, 1920, 1080, 2304, 1296, "bt.1886", 1)]
    [InlineData(VideoUpscalingMode.Fsrcnnx, 1920, 1080, 3840, 2160, "pq", 1)]
    [InlineData(VideoUpscalingMode.Fsrcnnx, 3840, 2160, 7680, 4320, "bt.1886", 1)]
    [InlineData(VideoUpscalingMode.Intel, 1920, 1080, 2560, 1440, "bt.1886", 1.333)]
    public void NeuralShaderUsesFixedTwoTimesReconstructionWithinItsEligibilityRules(VideoUpscalingMode mode,
        int w, int h, int dw, int dh, string gamma, double expected)
        => Assert.Equal(expected, VideoUpscalingPolicy.GetScale(mode, w, h, dw, dh, gamma), 3);

    [Theory]
    [InlineData("d3d11vpp", 30, true, true)] // Input-view creation failure is a warning.
    [InlineData("d3d11vpp", 20, true, true)]
    [InlineData("d3d11vpp", 40, true, false)]
    [InlineData("d3d11vpp", 30, false, false)]
    [InlineData("vo/gpu-next", 30, true, false)]
    public void FallsBackOnVideoProcessorWarningsOnlyWhenRequested(string prefix, int level,
        bool requested, bool expected)
        => Assert.Equal(expected, VideoUpscalingPolicy.IsProcessingFailure(prefix, level, requested));

    [Theory]
    [InlineData(0x10de, "NVIDIA GeForce RTX 5080", false, true)]
    [InlineData(0x10de, "NVIDIA RTX A2000 Laptop GPU", false, true)]
    [InlineData(0x10de, "NVIDIA Quadro RTX 4000", false, true)]
    [InlineData(0x10de, "NVIDIA GeForce GTX 1080", false, false)]
    [InlineData(0x1002, "AMD Radeon RX 7900", false, false)]
    [InlineData(0x10de, "NVIDIA GeForce RTX 5080", true, false)]
    [InlineData(0x1002, "NVIDIA GeForce RTX 5080", false, false)]
    public void RequiresNvidiaRtxHardware(uint vendor, string name, bool software, bool expected)
        => Assert.Equal(expected, VideoUpscalingPolicy.IsRtxAdapter(vendor, name, software));

    [Theory]
    [InlineData(true, 1920, 1080, 3840, 2160, "bt.1886", 2)]
    [InlineData(true, 1920, 800, 3840, 2160, "bt.1886", 2)]
    [InlineData(true, 1920, 1080, 2560, 1440, "bt.1886", 1.333)]
    [InlineData(true, 1280, 720, 3840, 2160, "bt.1886", 2)]
    [InlineData(false, 1920, 1080, 3840, 2160, "bt.1886", 1)]
    [InlineData(true, 3840, 1600, 3840, 2160, "bt.1886", 1)]
    [InlineData(true, 1920, 1080, 1920, 1080, "bt.1886", 1)]
    [InlineData(true, 1920, 1080, 3840, 1000, "bt.1886", 1)]
    [InlineData(true, 0, 0, 3840, 2160, "bt.1886", 1)]
    [InlineData(true, 1920, 1080, 0, 0, "bt.1886", 1)]
    [InlineData(true, 1920, 1080, 3840, 2160, "pq", 1)]
    [InlineData(true, 1920, 1080, 3840, 2160, "hlg", 1)]
    [InlineData(true, 1920, 1080, 3840, 2160, null, 1)]
    public void ScalesOnlyEligibleSdrToFitViewport(bool enabled, int width, int height,
        int viewportWidth, int viewportHeight, string? transfer, double expected)
        => Assert.Equal(expected, VideoUpscalingPolicy.GetScale(enabled, width, height,
            viewportWidth, viewportHeight, transfer), 3);

    [Fact]
    public void LeavesAnamorphicAndRotatedVideoOnOriginalRenderer()
    {
        Assert.Equal(1, VideoUpscalingPolicy.GetScale(true, 1440, 1080, 3840, 2160, "bt.1886", 1.333));
        Assert.Equal(1, VideoUpscalingPolicy.GetScale(true, 1920, 1080, 3840, 2160, "bt.1886", 1, 90));
    }
}
