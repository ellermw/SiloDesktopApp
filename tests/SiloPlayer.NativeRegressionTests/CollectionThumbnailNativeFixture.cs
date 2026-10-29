using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Converters;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

// Exercises the live server-card builder, native resize events and real decoders.
internal static class CollectionThumbnailNativeFixture
{
    internal static async Task RunAsync()
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        using var wire = new Wire(await CreatePosterAsync());
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://collection-thumbnails.invalid");
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "collection-thumbnail-cache"));
        var catalog = new CatalogApi(client);
        servicesField.SetValue(null, new Services(previous, new()
        {
            [typeof(HttpClient)] = http, [typeof(ImageService)] = images,
            [typeof(CatalogApi)] = catalog,
            [typeof(CollectionsViewModel)] = new CollectionsViewModel(new CollectionsApi(client), catalog),
        }));
        var owner = new Grid(); var window = new Window { Content = owner };
        try
        {
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1400, 1000)); window.AppWindow.Show(false);
            var page = new CollectionsPage { Width = 1280 };
            // Keep native page layout while suppressing its unrelated catalog load.
            page.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), page,
                typeof(CollectionsPage).GetMethod("Page_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!);
            owner.Children.Add(page);
            var build = typeof(CollectionsPage).GetMethod("BuildServerCollectionCard", BindingFlags.Instance | BindingFlags.NonPublic)!;
            const string url = "https://collection-thumbnails.invalid/poster.png";
            foreach (var pageWidth in new[] { 460, 900, 1280 })
            {
                page.Width = pageWidth;
                owner.UpdateLayout(); await Task.Delay(100); owner.UpdateLayout();
                Check(Math.Abs(page.ActualWidth - pageWidth) < .6, "Collection page did not acquire the fixture viewport width.");
                var card = (Border)build.Invoke(page, new object?[] { null, new ServerCollectionSummary { Id = "fixture", Title = "Fixture", PosterUrl = url } })!;
                var content = (StackPanel)((Button)((Grid)card.Child).Children[0]).Content;
                var poster = (Border)content.Children[0]; var picture = (Image)poster.Child;
                Check(picture.Source is BitmapImage initial && initial.DecodePixelType == DecodePixelType.Logical && initial.DecodePixelWidth == (int)Math.Ceiling(poster.Width),
                    $"Live server builder at page width {pageWidth} retained a fixed physical decode instead of its logical poster width.");
                await LoadedAsync(picture.Source);
                card.HorizontalAlignment = HorizontalAlignment.Left; owner.Children.Add(card);
                ImageSource? prior = null;
                foreach (var cardWidth in new[] { 140, 236, 96 })
                {
                    card.Width = cardWidth; owner.UpdateLayout(); await Task.Delay(100); owner.UpdateLayout();
                    Check(Math.Abs(poster.Width - cardWidth) < .6, "Live card resize did not update its poster width.");
                    Check(picture.Source is BitmapImage bitmap && bitmap.DecodePixelType == DecodePixelType.Logical && bitmap.DecodePixelWidth == cardWidth,
                        $"Live server card resize to {cardWidth} retained the previous decode size.");
                    Check(!ReferenceEquals(prior, picture.Source), "Resize reused a differently sized native bitmap.");
                    await LoadedAsync(picture.Source); prior = picture.Source;
                    Program.Log($"PASS: actual server card page={pageWidth}, resized={cardWidth}, logical native decode/load.");
                }
                owner.Children.Remove(card);
            }
            var converter = new UrlToImageSourceConverter();
            ImageSource Convert(string address, object? width = null) => (ImageSource)converter.Convert(address, typeof(ImageSource), width!, "");
            var physical = Convert(url); var logical = Convert(url, 342d);
            Check(physical is BitmapImage { DecodePixelWidth: 342, DecodePixelType: DecodePixelType.Physical }, "Unspecified converter width changed its existing physical342 default.");
            Check(logical is BitmapImage { DecodePixelWidth: 342, DecodePixelType: DecodePixelType.Logical } && !ReferenceEquals(physical, logical), "Source cache conflated logical and physical decode requests.");
            Check(ReferenceEquals(logical, Convert(url, 342d)), "Identical URL and decode size lost native-source reuse.");
            await LoadedAsync(physical); await LoadedAsync(logical);
            Check(wire.PosterReads == 1, "Changing decode width fetched the same artwork bytes again.");
            var renewed = Convert(url + "?token=renewed", 342d);
            Check(!ReferenceEquals(logical, renewed), "Renewed URL reused an older URL's pending native source."); await LoadedAsync(renewed);
            var otherOrigin = Convert("https://collection-thumbnails-other.invalid/poster.png", 342d);
            Check(!ReferenceEquals(logical, otherOrigin), "Native source cache crossed artwork origins."); await LoadedAsync(otherOrigin);
            Check(wire.PosterReads == 2, "Artwork disk identity crossed origins or lost stable renewed-URL byte reuse.");
            var failed = Convert("https://collection-thumbnails.invalid/retry.png", 140d);
            Check(!await UrlToImageSourceConverter.GetLoadOutcome(failed)!.WaitAsync(TimeSpan.FromSeconds(5)), "Failed bitmap reported successful loading.");
            var retried = Convert("https://collection-thumbnails.invalid/retry.png", 140d);
            Check(!ReferenceEquals(failed, retried), "Failed source stayed in the size-aware native cache."); await LoadedAsync(retried);
            foreach (var variant in new[] { "first", "second" })
            {
                var source = Convert("https://collection-thumbnails.invalid/avatar.svg?seed=" + variant, 140d);
                Check(source is SvgImageSource, "Logical decode request replaced SVG with BitmapImage."); await LoadedAsync(source);
                var image = new Image { Source = source, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                owner.Children.Add(image); owner.UpdateLayout(); await Task.Delay(150);
                var raster = new RenderTargetBitmap(); await raster.RenderAsync(image); var pixels = (await raster.GetPixelsAsync()).ToArray();
                var channel = variant == "first" ? 2 : 0;
                Check(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + channel] > 180 && pixels[i * 4 + 3] > 200) > 500,
                    "SVG native pixels lost their artwork variant identity.");
                owner.Children.Remove(image);
            }
            var broken = Convert("https://collection-thumbnails.invalid/broken.svg", 140d);
            Check(broken is SvgImageSource && !await UrlToImageSourceConverter.GetLoadOutcome(broken)!.WaitAsync(TimeSpan.FromSeconds(5)), "Malformed SVG lost its failure outcome.");
            Program.Log("PASS: converter size/type source identity, stable byte reuse, renewed URLs, origin isolation, bitmap failure recovery and SVG outcomes/variant pixels.");
        }
        finally { owner.Children.Clear(); await Task.Delay(100); window.Close(); await Task.Delay(50); servicesField.SetValue(null, previous); }
    }

    private static async Task LoadedAsync(ImageSource source)
    {
        Check(await UrlToImageSourceConverter.GetLoadOutcome(source)!.WaitAsync(TimeSpan.FromSeconds(5)), "Native artwork decode did not complete successfully.");
        if (source is BitmapImage bitmap) Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "Native bitmap has no decoded pixels.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task<byte[]> CreatePosterAsync()
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var pixels = new byte[1200 * 1800 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 32; pixels[i + 1] = 64; pixels[i + 2] = 220; pixels[i + 3] = 255; }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 1200, 1800, 96, 96, pixels); await encoder.FlushAsync();
        stream.Seek(0); using var copy = new MemoryStream(); await stream.AsStreamForRead().CopyToAsync(copy); return copy.ToArray();
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values) : IServiceProvider
    { public object? GetService(Type type) => values.GetValueOrDefault(type) ?? fallback.GetService(type); }
    private sealed class Wire(byte[] poster) : HttpMessageHandler
    {
        internal int PosterReads; private int _retryReads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Check(uri.Host is "collection-thumbnails.invalid" or "collection-thumbnails-other.invalid", "Unexpected external thumbnail request.");
            if (uri.AbsolutePath == "/retry.png" && Interlocked.Increment(ref _retryReads) == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            byte[] bytes = poster; var type = "image/png";
            if (uri.AbsolutePath.EndsWith(".svg", StringComparison.Ordinal))
            {
                type = "image/svg+xml"; var color = uri.Query.Contains("second", StringComparison.Ordinal) ? "#2244ee" : "#ed3344";
                bytes = Encoding.UTF8.GetBytes(uri.AbsolutePath == "/broken.svg" ? "invalid svg" : $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\"><rect width=\"32\" height=\"32\" fill=\"{color}\"/></svg>");
            }
            if (uri.AbsolutePath == "/poster.png") Interlocked.Increment(ref PosterReads);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; response.Content.Headers.ContentType = new(type);
            return Task.FromResult(response);
        }
    }
}
