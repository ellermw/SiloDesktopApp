using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SiloPlayer.Helpers;

internal static class EpisodeSpoilerArtwork
{
    public static async Task<BitmapImage> LoadAsync(string diskPath, int decodeWidth, bool blur, CancellationToken ct)
    {
        var bitmap = new BitmapImage { DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical };
        if (!blur) { bitmap.UriSource = new Uri(diskPath); return bitmap; }
        var bytes = await File.ReadAllBytesAsync(diskPath, ct);
        bytes = await ArtworkEffects.BlurEpisodeStillAsync(bytes, ct);
        using var stream = new MemoryStream(bytes);
        await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
        ct.ThrowIfCancellationRequested();
        return bitmap;
    }
}
