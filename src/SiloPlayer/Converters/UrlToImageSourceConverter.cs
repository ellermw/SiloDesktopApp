using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Converters;

/// <summary>
/// Converts an absolute artwork URL into the ImageSource type expected by
/// WinUI. Compiled x:Bind cannot safely coerce a nullable string directly to
/// Image.Source and throws while a recycled template is being realized.
/// </summary>
public sealed class UrlToImageSourceConverter : IValueConverter
{
    private const int PruneInterval = 64;
    private static readonly ConcurrentDictionary<string, WeakReference<BitmapImage>> Sources = new();
    private static int _lookupCount;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string url ||
            string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null!;
        }

        var cacheKey = StableIdentity(uri);
        if (Interlocked.Increment(ref _lookupCount) % PruneInterval == 0)
            PruneDeadSources();

        if (Sources.TryGetValue(cacheKey, out var existing))
        {
            if (existing.TryGetTarget(out var cached))
                return cached;
            RemoveDeadEntry(cacheKey, existing);
        }

        var bitmap = new BitmapImage { DecodePixelWidth = 342 };
        Sources[cacheKey] = new WeakReference<BitmapImage>(bitmap);
        _ = PopulateAsync(bitmap, cacheKey, url);
        return bitmap;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static async Task PopulateAsync(BitmapImage bitmap, string cacheKey, string url)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var bytes = await imageService.GetImageAsync(cacheKey, "converted", url, httpClient);
            if (bytes == null || bytes.Length == 0) return;

            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
        }
        catch
        {
            Sources.TryRemove(cacheKey, out _);
        }
    }

    private static string StableIdentity(Uri uri)
        => uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);

    private static void PruneDeadSources()
    {
        var entries = (ICollection<KeyValuePair<string, WeakReference<BitmapImage>>>)Sources;
        foreach (var entry in Sources)
        {
            if (!entry.Value.TryGetTarget(out _))
                entries.Remove(entry);
        }
    }

    private static void RemoveDeadEntry(string cacheKey, WeakReference<BitmapImage> entry)
    {
        var entries = (ICollection<KeyValuePair<string, WeakReference<BitmapImage>>>)Sources;
        entries.Remove(new KeyValuePair<string, WeakReference<BitmapImage>>(cacheKey, entry));
    }
}
