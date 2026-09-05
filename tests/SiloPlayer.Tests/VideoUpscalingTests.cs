using SiloPlayer.Player;

namespace SiloPlayer.Tests;

public class VideoUpscalingTests
{
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
