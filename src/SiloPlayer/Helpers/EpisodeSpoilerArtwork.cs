using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SiloPlayer.Helpers;

internal static class EpisodeSpoilerArtwork
{
    public static async Task<BitmapImage> LoadAsync(string diskPath, int decodeWidth, bool blur, CancellationToken ct, ArtworkDecodeGate? gate = null)
    {
        var bitmap = new BitmapImage { DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical };
        if (!blur) { bitmap.UriSource = new Uri(diskPath); return bitmap; }
        var bytes = await ReadBytesAsync(diskPath, ct, gate);
        bytes = await ArtworkEffects.BlurEpisodeStillAsync(bytes, ct, gate);
        await SetSourceAsync(bitmap, bytes, ct, gate);
        ct.ThrowIfCancellationRequested();
        return bitmap;
    }

    private static async Task<byte[]> ReadBytesAsync(string diskPath, CancellationToken ct, ArtworkDecodeGate? gate)
    {
        using var work = gate == null ? null : await gate.EnterAsync(ct);
        ct.ThrowIfCancellationRequested();
        return await File.ReadAllBytesAsync(diskPath, ct);
    }

    private static async Task SetSourceAsync(BitmapImage bitmap, byte[] bytes, CancellationToken ct, ArtworkDecodeGate? gate)
    {
        using var work = gate == null ? null : await gate.EnterAsync(ct);
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(bytes);
        await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
        ct.ThrowIfCancellationRequested();
    }
}
