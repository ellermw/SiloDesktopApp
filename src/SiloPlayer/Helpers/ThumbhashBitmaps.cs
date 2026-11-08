using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Services;
using Windows.UI;

namespace SiloPlayer.Helpers;

/// <summary>
/// The blurred pictures of artwork thumbhashes, shared by every card that shows one: at most
/// <see cref="Capacity"/> recent pictures of at most 32×32 pixels (about 1 MB), so a card scrolled
/// back into view shows its blur without decoding it again. Decoding and tinting run on the thread
/// pool; only the small bitmap is made on the UI thread. A hash that does not decode is remembered
/// as none. UI thread only.
/// </summary>
internal static class ThumbhashBitmaps
{
    internal const int Capacity = 256;

    private static readonly Dictionary<(string Hash, bool Dim), LinkedListNode<Entry>> Entries = new();
    private static readonly LinkedList<Entry> Recent = new();
    // One decode per hash: cards asking for a hash already on its way share it.
    private static readonly Dictionary<(string Hash, bool Dim), Task<ImageSource?>> Pending = new();

    private sealed record Entry((string Hash, bool Dim) Key, ImageSource? Picture);

    internal static int Count => Entries.Count;

    /// <summary>The picture when it is already made: a recycled card shows it in the same frame.</summary>
    internal static bool TryGet(string hash, bool dim, out ImageSource? picture)
    {
        if (Entries.TryGetValue((hash, dim), out var node))
        {
            Recent.Remove(node);
            Recent.AddFirst(node);
            picture = node.Value.Picture;
            return true;
        }
        picture = null;
        return false;
    }

    /// <summary>The picture of <paramref name="hash"/>; null for one that does not decode.</summary>
    internal static Task<ImageSource?> GetAsync(string hash, bool dim)
    {
        if (TryGet(hash, dim, out var cached)) return Task.FromResult(cached);
        var key = (hash, dim);
        if (!Pending.TryGetValue(key, out var pending))
        {
            pending = CreateAsync(key);
            // A synchronous failure has already finished (and removed nothing yet).
            if (!pending.IsCompleted) Pending[key] = pending;
        }
        return pending;
    }

    private static async Task<ImageSource?> CreateAsync((string Hash, bool Dim) key)
    {
        try
        {
            Pixels? pixels = null;
            try { pixels = await Task.Run(() => Decode(key.Hash, key.Dim)); }
            catch { /* An invalid hash keeps the caller's own fallback. */ }
            ImageSource? picture = null;
            if (pixels is { } decoded)
            {
                var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);
                using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(decoded.Bgra);
                bitmap.Invalidate();
                picture = bitmap;
            }
            Add(key, picture);
            return picture;
        }
        finally { Pending.Remove(key); }
    }

    private static void Add((string Hash, bool Dim) key, ImageSource? picture)
    {
        if (Entries.TryGetValue(key, out var existing)) Recent.Remove(existing);
        Entries[key] = Recent.AddFirst(new Entry(key, picture));
        while (Entries.Count > Capacity)
        {
            Entries.Remove(Recent.Last!.Value.Key);
            Recent.RemoveLast();
        }
    }

    private readonly record struct Pixels(int Width, int Height, byte[] Bgra);

    // Premultiplied BGRA for WriteableBitmap, tinted as DefaultArtwork tints everything it draws.
    private static Pixels Decode(string hash, bool dim)
    {
        var decoded = ThumbhashDecoder.Decode(hash);
        var pixels = decoded.Rgba;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var color = Tint(dim, pixels[i + 3], pixels[i], pixels[i + 1], pixels[i + 2]);
            pixels[i] = (byte)(color.B * color.A / 255);
            pixels[i + 1] = (byte)(color.G * color.A / 255);
            pixels[i + 2] = (byte)(color.R * color.A / 255);
            pixels[i + 3] = color.A;
        }
        return new Pixels(decoded.Width, decoded.Height, pixels);
    }

    /// <summary>DefaultArtwork's colour treatment: unchanged, or desaturated and darkened when dimmed.</summary>
    internal static Color Tint(bool dim, byte alpha, byte r, byte g, byte b)
    {
        if (!dim) return Color.FromArgb(alpha, r, g, b);
        var gray = .213 * r + .715 * g + .072 * b;
        byte Channel(byte channel) => (byte)Math.Round((.8 * channel + .2 * gray) * .85);
        return Color.FromArgb(alpha, Channel(r), Channel(g), Channel(b));
    }
}
