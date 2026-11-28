using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SiloPlayer.Helpers;

internal static class EpisodeSpoilerArtwork
{
    public static Task<BitmapImage> LoadAsync(string diskPath, int decodeWidth, bool blur, CancellationToken ct, ArtworkDecodeGate? gate = null)
        => LoadAsync((diskPath, null), decodeWidth, blur, ct, gate);

    public static async Task<BitmapImage> LoadAsync((string? Path, byte[]? Bytes) artwork, int decodeWidth, bool blur, CancellationToken ct, ArtworkDecodeGate? gate = null)
    {
        var bitmap = new BitmapImage { DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical };
        if (!blur && artwork.Path is { } path) { bitmap.UriSource = new Uri(path); return bitmap; }
        var bytes = artwork.Bytes ?? await ReadBytesAsync(artwork.Path!, ct, gate);
        if (blur) bytes = await ArtworkEffects.BlurEpisodeStillAsync(bytes, ct, gate);
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

    internal static async Task SetSourceAsync(BitmapImage bitmap, byte[] bytes, CancellationToken ct = default, ArtworkDecodeGate? gate = null)
    {
        using var work = gate == null ? null : await gate.EnterAsync(ct);
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream(bytes);
        using var source = stream.AsRandomAccessStream();
        await bitmap.SetSourceAsync(source);
        ct.ThrowIfCancellationRequested();
    }
}
