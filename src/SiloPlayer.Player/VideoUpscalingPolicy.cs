namespace SiloPlayer.Player;

public enum VideoUpscalingMode { Off, Automatic, Nvidia, Intel, Fsrcnnx }
public sealed record VideoAdapterInfo(string Name, uint VendorId, bool IsSoftware);

public static class VideoUpscalingPolicy
{
    public static VideoUpscalingMode ResolveMode(string? saved, bool legacyNvidia) => saved?.ToLowerInvariant() switch
    {
        null => legacyNvidia ? VideoUpscalingMode.Nvidia : VideoUpscalingMode.Off,
        "automatic" => VideoUpscalingMode.Automatic,
        "nvidia" => VideoUpscalingMode.Nvidia,
        "intel" => VideoUpscalingMode.Intel,
        "fsrcnnx" => VideoUpscalingMode.Fsrcnnx,
        _ => VideoUpscalingMode.Off,
    };

    public static (VideoUpscalingMode Mode, VideoAdapterInfo? Adapter) SelectAdapter(
        VideoUpscalingMode mode, IEnumerable<VideoAdapterInfo> adapters)
    {
        var hardware = adapters.Where(a => !a.IsSoftware).ToArray();
        var nvidia = hardware.FirstOrDefault(a => IsRtxAdapter(a.VendorId, a.Name, false));
        var amd = hardware.FirstOrDefault(a => a.VendorId == 0x1002);
        var intel = hardware.FirstOrDefault(a => a.VendorId == 0x8086);
        return mode switch
        {
            VideoUpscalingMode.Automatic when nvidia != null => (VideoUpscalingMode.Nvidia, nvidia),
            VideoUpscalingMode.Automatic when amd != null => (VideoUpscalingMode.Fsrcnnx, amd),
            VideoUpscalingMode.Automatic => (VideoUpscalingMode.Intel, intel),
            VideoUpscalingMode.Nvidia => (mode, nvidia),
            VideoUpscalingMode.Intel => (mode, intel),
            VideoUpscalingMode.Fsrcnnx => (mode, nvidia ?? amd ?? hardware.FirstOrDefault(a => a.VendorId == 0x10de) ?? intel),
            _ => (VideoUpscalingMode.Off, null),
        };
    }

    public static double GetScale(VideoUpscalingMode mode, int width, int height, int viewportWidth,
        int viewportHeight, string? transfer, double pixelAspect = 1, int rotation = 0)
    {
        var scale = GetScale(mode != VideoUpscalingMode.Off, width, height, viewportWidth,
            viewportHeight, transfer, pixelAspect, rotation);
        // The bundled network reconstructs luma at exactly 2x. Its upstream
        // hook only runs when both display ratios exceed 1.300; mpv then fits
        // the reconstructed image to the viewport with its normal scaler.
        return mode == VideoUpscalingMode.Fsrcnnx ? (scale > 1.300 ? 2 : 1) : scale;
    }

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
