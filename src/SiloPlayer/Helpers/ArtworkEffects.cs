using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System.Runtime.InteropServices.WindowsRuntime;

namespace SiloPlayer.Helpers;

internal static class ArtworkEffects
{
    /// <summary>RequestPosterCard's brightness(.85) saturate(.8), preserving artwork alpha.</summary>
    public static async Task<byte[]> DimRequestPosterAsync(byte[] bytes)
    {
        using var sourceStream = new MemoryStream(bytes);
        var device = CanvasDevice.GetSharedDevice();
        using var bitmap = await CanvasBitmap.LoadAsync(device, sourceStream.AsRandomAccessStream());
        using var darkened = new ColorMatrixEffect { Source = bitmap,
            ColorMatrix = new Matrix5x4 { M11 = .85f, M22 = .85f, M33 = .85f, M44 = 1 } };
        using var saturated = new SaturationEffect { Source = darkened, Saturation = .8f };
        using var target = new CanvasRenderTarget(device, (float)bitmap.Size.Width, (float)bitmap.Size.Height, 96);
        using (var drawing = target.CreateDrawingSession()) drawing.DrawImage(saturated);
        using var output = new MemoryStream();
        await target.SaveAsync(output.AsRandomAccessStream(), CanvasBitmapFileFormat.Png);
        return output.ToArray();
    }

    /// <summary>CSS object-cover followed by blur64/brightness.45/saturation1.15 in viewport pixels.</summary>
    public static async Task<byte[]> TransformBackdropAsync(byte[] bytes, double width, double height, CancellationToken ct = default)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        using var sourceStream = new MemoryStream(bytes);
        var device = CanvasDevice.GetSharedDevice();
        using var bitmap = await CanvasBitmap.LoadAsync(device, sourceStream.AsRandomAccessStream());
        ct.ThrowIfCancellationRequested();
        var targetWidth = (float)Math.Ceiling(width); var targetHeight = (float)Math.Ceiling(height);
        using var covered = new CanvasRenderTarget(device, targetWidth, targetHeight, 96);
        var ratio = Math.Max(targetWidth / bitmap.Size.Width, targetHeight / bitmap.Size.Height);
        var cropWidth = targetWidth / ratio; var cropHeight = targetHeight / ratio;
        using (var drawing = covered.CreateDrawingSession())
            drawing.DrawImage(bitmap, new Windows.Foundation.Rect(0, 0, targetWidth, targetHeight),
                new Windows.Foundation.Rect((bitmap.Size.Width - cropWidth) / 2, (bitmap.Size.Height - cropHeight) / 2, cropWidth, cropHeight));
        ct.ThrowIfCancellationRequested();
        using var blurred = new GaussianBlurEffect { BlurAmount = 64, BorderMode = EffectBorderMode.Soft, Source = covered };
        using var darkened = new ColorMatrixEffect { Source = blurred, ColorMatrix = new Matrix5x4 { M11 = .45f, M22 = .45f, M33 = .45f, M44 = 1 } };
        using var saturated = new SaturationEffect { Source = darkened, Saturation = 1.15f };
        using var target = new CanvasRenderTarget(device, targetWidth, targetHeight, 96);
        using (var drawing = target.CreateDrawingSession()) drawing.DrawImage(saturated);
        using var output = new MemoryStream();
        await target.SaveAsync(output.AsRandomAccessStream(), CanvasBitmapFileFormat.Png);
        ct.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    public static async Task<byte[]> TransformAsync(byte[] bytes, bool blur, CancellationToken ct = default)
    {
        using var sourceStream = new MemoryStream(bytes);
        var device = CanvasDevice.GetSharedDevice();
        using var bitmap = await CanvasBitmap.LoadAsync(device, sourceStream.AsRandomAccessStream());
        ct.ThrowIfCancellationRequested();
        using var target = new CanvasRenderTarget(device, (float)bitmap.Size.Width, (float)bitmap.Size.Height, 96);
        using (var drawing = target.CreateDrawingSession())
        {
            using var effect = blur
                ? (ICanvasImage)new GaussianBlurEffect { BlurAmount = 48, BorderMode = EffectBorderMode.Hard, Source = new SaturationEffect { Saturation = 1.15f, Source = bitmap } }
                : new GrayscaleEffect { Source = bitmap };
            drawing.DrawImage(effect);
        }
        using var output = new MemoryStream();
        await target.SaveAsync(output.AsRandomAccessStream(), CanvasBitmapFileFormat.Png);
        ct.ThrowIfCancellationRequested();
        return output.ToArray();
    }
}
