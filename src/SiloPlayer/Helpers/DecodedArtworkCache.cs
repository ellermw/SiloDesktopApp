using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Helpers;

/// <summary>Retains a small working set of visible artwork across native unload/reload.</summary>
internal static class DecodedArtworkCache
{
    private const int MaximumEntries = 32;
    private const long MaximumBytes = 24L * 1024 * 1024;
    [ThreadStatic] private static Dictionary<Key, LinkedListNode<Entry>>? _entries;
    [ThreadStatic] private static LinkedList<Entry>? _recent;
    [ThreadStatic] private static long _bytes;

    internal sealed record Key(ImageService Images, SiloApiClient Client, ApiIdentityContext Owner,
        string Url, int DecodeWidth, bool Hidden);
    private sealed record Entry(Key Key, BitmapImage Bitmap, long Bytes);

    internal static Key? Capture(ImageService images, SiloApiClient? client, string url, int width, bool hidden)
        => client == null ? null : new(images, client, client.CaptureIdentityContext(), url, width, hidden);

    internal static bool IsCurrent(Key? key) => key == null || key.Client.IsCurrentIdentityContext(key.Owner);

    internal static bool TryGet(Key? key, out BitmapImage? bitmap)
    {
        bitmap = null;
        if (key == null || !IsCurrent(key) || _entries == null || !_entries.TryGetValue(key, out var node)) return false;
        _recent!.Remove(node); _recent.AddLast(node);
        bitmap = node.Value.Bitmap;
        return true;
    }

    internal static void Remember(Key? key, BitmapImage bitmap, FrameworkElement card)
    {
        if (key == null || !IsCurrent(key) || !card.IsLoaded || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return;
        // Library virtualization still releases offscreen cards. Only actual
        // viewport artwork competes for this bounded native working set.
        if (card.XamlRoot?.Content is not FrameworkElement root) return;
        var bounds = card.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, card.ActualWidth, card.ActualHeight));
        if (bounds.Bottom <= 0 || bounds.Top >= root.ActualHeight || bounds.Right <= 0 || bounds.Left >= root.ActualWidth) return;
        var bytes = (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;
        if (bytes > MaximumBytes) return;
        _entries ??= new(); _recent ??= new();
        if (_entries.Remove(key, out var previous)) { _recent.Remove(previous); _bytes -= previous.Value.Bytes; }
        var node = _recent.AddLast(new Entry(key, bitmap, bytes)); _entries.Add(key, node); _bytes += bytes;
        while (_entries.Count > MaximumEntries || _bytes > MaximumBytes)
        {
            var oldest = _recent.First!; _recent.RemoveFirst(); _entries.Remove(oldest.Value.Key); _bytes -= oldest.Value.Bytes;
        }
    }
}
