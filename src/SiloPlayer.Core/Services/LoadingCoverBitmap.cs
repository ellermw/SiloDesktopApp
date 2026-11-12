namespace SiloPlayer.Core.Services;

/// <summary>
/// The title backdrop behind the native loading spinner (upstream
/// PlayerLoadingCover.tsx): the picture covers the frame, dimmed to 22% over
/// black. libmpv draws raw bitmaps above its ASS layer, so the composed bitmap
/// leaves a feathered transparent disc at the centre where the OSC paints its
/// spinner on a black disc of the same outer radius.
/// </summary>
public static class LoadingCoverBitmap
{
    /// <summary>Upstream <c>.hud-loading-backdrop { opacity: 0.22 }</c> over black.</summary>
    public const double Brightness = 0.22;
    /// <summary>Fully clear radius around the spinner, in OSC layout pixels.</summary>
    public const double ClearRadius = 26;
    /// <summary>Radius where the picture is fully opaque again; the OSC disc matches it.</summary>
    public const double FeatherRadius = 44;
    /// <summary>The largest bitmap composed; libmpv scales it to the frame.</summary>
    public const int MaximumWidth = 1920;
    public const int MaximumHeight = 1080;

    /// <summary>Bitmap dimensions for a frame, keeping its aspect within the maximum.</summary>
    public static (int Width, int Height) TargetSize(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0) return (0, 0);
        var scale = Math.Min(1d, Math.Min(MaximumWidth / (double)frameWidth, MaximumHeight / (double)frameHeight));
        return (Math.Max(1, (int)Math.Round(frameWidth * scale)), Math.Max(1, (int)Math.Round(frameHeight * scale)));
    }

    /// <summary>
    /// Composes a premultiplied BGRA bitmap of <paramref name="width"/> x <paramref name="height"/>
    /// from a straight or premultiplied opaque BGRA source, cover-fitted and centred.
    /// <paramref name="layoutScale"/> is the OSC ui_scale times the bitmap/frame ratio.
    /// </summary>
    public static byte[] Compose(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, int sourceStride,
        int width, int height, double layoutScale)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || width <= 0 || height <= 0 ||
            sourceStride < sourceWidth * 4 || source.Length < sourceStride * (sourceHeight - 1) + sourceWidth * 4)
            throw new ArgumentException("Invalid loading cover bitmap dimensions.");
        var result = new byte[checked(width * height * 4)];
        // Cover: scale the source so it fills the target, then centre the crop.
        var scale = Math.Max(width / (double)sourceWidth, height / (double)sourceHeight);
        var offsetX = (sourceWidth * scale - width) / 2;
        var offsetY = (sourceHeight * scale - height) / 2;
        var clear = ClearRadius * Math.Max(0.1, layoutScale);
        var feather = FeatherRadius * Math.Max(0.1, layoutScale);
        var cx = width / 2d;
        var cy = height / 2d;
        for (var y = 0; y < height; y++)
        {
            var sy = Math.Clamp((y + 0.5 + offsetY) / scale - 0.5, 0, sourceHeight - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, sourceHeight - 1);
            var fy = sy - y0;
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Clamp((x + 0.5 + offsetX) / scale - 0.5, 0, sourceWidth - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, sourceWidth - 1);
                var fx = sx - x0;
                var distance = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                var coverage = Coverage(distance, clear, feather);
                var target = (y * width + x) * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var top = source[y0 * sourceStride + x0 * 4 + channel] * (1 - fx) + source[y0 * sourceStride + x1 * 4 + channel] * fx;
                    var bottom = source[y1 * sourceStride + x0 * 4 + channel] * (1 - fx) + source[y1 * sourceStride + x1 * 4 + channel] * fx;
                    var value = (top * (1 - fy) + bottom * fy) * Brightness * coverage;
                    result[target + channel] = (byte)Math.Clamp(Math.Round(value), 0, 255);
                }
                result[target + 3] = (byte)Math.Clamp(Math.Round(255 * coverage), 0, 255);
            }
        }
        return result;
    }

    /// <summary>0 inside the spinner disc, 1 beyond the feather, smooth between.</summary>
    public static double Coverage(double distance, double clear, double feather)
    {
        if (distance <= clear) return 0;
        if (distance >= feather) return 1;
        var t = (distance - clear) / (feather - clear);
        return t * t * (3 - 2 * t);
    }
}
