using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Svg;
using SiloPlayer.Core.Services;
using Windows.Graphics.Imaging;

namespace SiloPlayer.Services;

/// <summary>Converts the server's branding favicon into a cached Windows ICO.</summary>
public static class ServerWindowIcon
{
    private static readonly int[] Sizes = [16, 32, 48, 64, 256];

    public static async Task<string?> GetIconPathAsync(ImageService images, HttpClient http,
        string serverScope, string? faviconUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(faviconUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        try
        {
            var source = await images.GetImageDiskPathAsync(serverScope, "server-favicon", faviconUrl!, http, ct).ConfigureAwait(false);
            if (source is null) return null;
            // Both disk I/O and native rasterization stay off the UI thread.
            return await Task.Run(async () =>
            {
                if (new FileInfo(source).Length > 1024 * 1024) return null; // Official favicon upload limit.
                var data = await File.ReadAllBytesAsync(source, ct).ConfigureAwait(false);
                var cache = Path.Combine(Path.GetDirectoryName(source)!, "window-icons");
                Directory.CreateDirectory(cache);
                var target = Path.Combine(cache, Convert.ToHexString(SHA256.HashData(data)) + ".ico");
                if (File.Exists(target)) return target;
                var icon = IsIco(data) ? data : await ConvertAsync(data, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                // Immutable content names also isolate overlapping old/new server loads.
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, icon, ct).ConfigureAwait(false);
                    File.Move(temporary, target, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                foreach (var obsolete in new DirectoryInfo(cache).GetFiles("*.ico")
                    .OrderByDescending(file => file.LastWriteTimeUtc).Skip(64))
                    try { obsolete.Delete(); } catch (IOException) { }
                return target;
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
        catch (Exception ex)
        {
            LocalLog.AppendException("branding.txt", ex, "window_favicon_load");
            return null;
        }
    }

    private static bool IsIco(byte[] data) => data.Length >= 22 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0;

    private static async Task<byte[]> ConvertAsync(byte[] data, CancellationToken ct)
    {
        var device = CanvasDevice.GetSharedDevice();
        using var input = new MemoryStream(data);
        using var stream = input.AsRandomAccessStream();
        using var svg = IsSvg(data) ? await CanvasSvgDocument.LoadAsync(device, stream) : null;
        using var bitmap = svg is null ? await DecodeBitmapAsync(device, stream) : null;
        var frames = new List<byte[]>();
        foreach (var size in Sizes)
        {
            ct.ThrowIfCancellationRequested();
            using var render = new CanvasRenderTarget(device, size, size, 96);
            using (var drawing = render.CreateDrawingSession())
            {
                drawing.Clear(Microsoft.UI.Colors.Transparent);
                if (svg is not null) drawing.DrawSvg(svg, new(size, size));
                else
                {
                    var scale = Math.Min(size / bitmap!.Size.Width, size / bitmap.Size.Height);
                    var width = bitmap.Size.Width * scale; var height = bitmap.Size.Height * scale;
                    drawing.DrawImage(bitmap, new Windows.Foundation.Rect((size - width) / 2, (size - height) / 2, width, height));
                }
            }
            using var png = new MemoryStream();
            await render.SaveAsync(png.AsRandomAccessStream(), CanvasBitmapFileFormat.Png);
            frames.Add(png.ToArray());
        }
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)frames.Count);
        var offset = 6 + frames.Count * 16;
        for (var index = 0; index < frames.Count; index++)
        {
            writer.Write((byte)(Sizes[index] == 256 ? 0 : Sizes[index])); writer.Write((byte)(Sizes[index] == 256 ? 0 : Sizes[index]));
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(frames[index].Length); writer.Write(offset); offset += frames[index].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
        return output.ToArray();
    }

    private static async Task<CanvasBitmap> DecodeBitmapAsync(CanvasDevice device, Windows.Storage.Streams.IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        if (decoder.OrientedPixelWidth == 0 || decoder.OrientedPixelHeight == 0) throw new InvalidDataException("Empty favicon.");
        var scale = Math.Min(1d, 256d / Math.Max(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight));
        var width = Math.Max(1u, (uint)(decoder.OrientedPixelWidth * scale));
        var height = Math.Max(1u, (uint)(decoder.OrientedPixelHeight * scale));
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform { ScaledWidth = width, ScaledHeight = height }, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        return CanvasBitmap.CreateFromBytes(device, pixels.DetachPixelData(), (int)width, (int)height,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
    }

    private static bool IsSvg(byte[] data)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(data), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader).Root?.Name.LocalName == "svg";
        }
        catch (XmlException) { return false; }
    }
}
