using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
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
    private static readonly ConditionalWeakTable<BitmapImage, TaskCompletionSource<bool>> Outcomes = new();
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
        var dimRequestPoster = parameter is "request-dim";
        // Reuse disk bytes by stable artwork identity, but never attach a
        // renewed URL to an older URL's pending/failed bitmap operation.
        var sourceKey = dimRequestPoster ? url + "|request-dim" : url;
        if (Interlocked.Increment(ref _lookupCount) % PruneInterval == 0)
            PruneDeadSources();

        if (Sources.TryGetValue(sourceKey, out var existing))
        {
            if (existing.TryGetTarget(out var cached))
                return cached;
            RemoveDeadEntry(sourceKey, existing);
        }

        var bitmap = new BitmapImage { DecodePixelWidth = 342 };
        var outcome = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Outcomes.Add(bitmap, outcome);
        Sources[sourceKey] = new WeakReference<BitmapImage>(bitmap);
        _ = PopulateAsync(bitmap, bitmap.DispatcherQueue, cacheKey, sourceKey, url, dimRequestPoster, outcome);
        return bitmap;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    public static Task<bool>? GetLoadOutcome(Microsoft.UI.Xaml.Media.ImageSource? source)
        => source is BitmapImage bitmap && Outcomes.TryGetValue(bitmap, out var outcome) ? outcome.Task : null;

    private static async Task PopulateAsync(BitmapImage bitmap, DispatcherQueue owner, string cacheKey, string sourceKey, string url, bool dimRequestPoster, TaskCompletionSource<bool> outcome)
    {
        var loaded = false;
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var bytes = await imageService.GetImageAsync(cacheKey, "converted", url, httpClient);
            if (bytes == null || bytes.Length == 0) return;
            if (dimRequestPoster) bytes = await SiloPlayer.Helpers.ArtworkEffects.DimRequestPosterAsync(bytes);

            using var stream = new MemoryStream(bytes);
            await DecodeOnOwnerAsync(bitmap, owner, stream.AsRandomAccessStream());
            loaded = true;
        }
        catch (Exception error)
        {
            // Keep credentials and signed URL query strings out of diagnostics.
            var identity = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cacheKey)))[..16];
            LocalLog.AppendLine("artwork_loading.txt", $"fetch_or_decode_failed | artwork={identity} | error={error.GetType().Name} | hresult=0x{error.HResult:X8} | owner_thread={owner.HasThreadAccess}");
        }
        finally
        {
            if (!loaded && Sources.TryGetValue(sourceKey, out var saved) && saved.TryGetTarget(out var current) && ReferenceEquals(current, bitmap))
                ((ICollection<KeyValuePair<string, WeakReference<BitmapImage>>>)Sources).Remove(new(sourceKey, saved));
            outcome.TrySetResult(loaded);
        }
    }

    private static Task DecodeOnOwnerAsync(BitmapImage bitmap, DispatcherQueue owner, Windows.Storage.Streams.IRandomAccessStream stream)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async void Decode()
        {
            try { await bitmap.SetSourceAsync(stream); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        }
        if (owner.HasThreadAccess) Decode();
        else if (!owner.TryEnqueue(Decode)) completion.TrySetException(new InvalidOperationException("Artwork owner dispatcher is closed."));
        return completion.Task;
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
