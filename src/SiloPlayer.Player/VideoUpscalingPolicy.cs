namespace SiloPlayer.Player;

public static class VideoUpscalingPolicy
{
    public static bool IsProcessingFailure(string prefix, int logLevel, bool filterRequested)
        => filterRequested && prefix.Equals("d3d11vpp", StringComparison.OrdinalIgnoreCase) && logLevel <= 30;

    public static bool IsRtxAdapter(uint vendorId, string? description, bool software)
        => vendorId == 0x10de && !software &&
           description?.Contains("RTX ", StringComparison.OrdinalIgnoreCase) == true;

    public static double GetScale(bool enabled, int width, int height, int viewportWidth,
        int viewportHeight, string? transfer, double pixelAspect = 1, int rotation = 0)
    {
        // The first experiment deliberately preserves HDR/Dolby Vision and
        // unusual pixel geometry on the existing gpu-next path.
        if (!enabled || width <= 0 || height <= 0 || width > 1920 || height > 1080 ||
            viewportWidth <= 0 || viewportHeight <= 0 || rotation != 0 ||
            !double.IsFinite(pixelAspect) || Math.Abs(pixelAspect - 1) > 0.001 ||
            transfer is not ("bt.1886" or "srgb" or "gamma2.2" or "gamma2.8"))
            return 1;

        var scale = Math.Min(2, Math.Min((double)viewportWidth / width, (double)viewportHeight / height));
        // Round down so rounding cannot exceed the display area. Require a
        // meaningful enlargement, avoiding churn at the native-size boundary.
        return scale >= 1.1 ? Math.Floor(scale * 1000) / 1000 : 1;
    }
}
