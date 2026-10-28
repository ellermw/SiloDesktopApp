using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Converters;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Views;

internal static class ProfileAvatarNativeFixture
{
    internal static async Task RunAsync()
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://avatars.invalid");
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "avatar-images"));
        var replacements = new Dictionary<Type, object>
        {
            [typeof(AuthApi)] = new AuthApi(client), [typeof(SettingsApi)] = new SettingsApi(client),
            [typeof(HttpClient)] = http, [typeof(ImageService)] = images,
        };
        field.SetValue(null, new Services(previous, replacements));
        var owner = new StackPanel(); var window = new Window { Content = owner };
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1000, 900)); window.AppWindow.Show(false);
            var page = new SettingsPage { Width = 900, Height = 650 }; owner.Children.Add(page);
            await Task.Delay(100);
            var method = typeof(SettingsPage).GetMethod("LoadProfilesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(page, null)!;
            var panel = (StackPanel)page.FindName("ProfileCardsPanel");
            var error = (TextBlock)page.FindName("NoProfilesText");
            Program.Log($"Actual Settings profiles: cards={panel.Children.Count}, message={error.Text}.");
            if (panel.Children.Count != 4 || error.Visibility != Visibility.Collapsed || error.Text.StartsWith("Failed to load", StringComparison.Ordinal))
                throw new InvalidOperationException("Relative or malformed avatar aborted actual Settings profile cards.");
            var converter = new UrlToImageSourceConverter();
            foreach (var url in new[] { "https://avatars.invalid/profile-avatars/preset.svg", "https://api.dicebear.com/9.x/thumbs/svg?seed=fixture", "https://api.dicebear.com/9.x/thumbs/svg?seed=second" })
            {
                var realAssets = Environment.GetEnvironmentVariable("SILO_NATIVE_AVATAR_REAL_ASSETS") != null;
                if (realAssets && url.EndsWith("seed=second", StringComparison.Ordinal)) continue;
                var source = converter.Convert(url, typeof(ImageSource), null!, "");
                if (source is not SvgImageSource) throw new InvalidOperationException("SVG avatar was passed to the bitmap decoder.");
                var outcome = UrlToImageSourceConverter.GetLoadOutcome((ImageSource)source);
                if (outcome == null || !await outcome.WaitAsync(TimeSpan.FromSeconds(3)))
                    throw new InvalidOperationException("SVG artwork did not publish a successful load outcome.");
                var image = new Image { Source = (ImageSource)source, Width = 72, Height = 72, Stretch = Stretch.Fill };
                owner.Children.Add(image); await Task.Delay(250); image.UpdateLayout();
                var raster = new RenderTargetBitmap(); await raster.RenderAsync(image);
                var pixels = (await raster.GetPixelsAsync()).ToArray(); var matching = 0;
                var blue = url.EndsWith("seed=second", StringComparison.Ordinal);
                for (var i = 0; i + 3 < pixels.Length; i += 4)
                    if (pixels[i + 3] > 200 && (realAssets || (blue
                        ? pixels[i] > 180 && pixels[i + 2] < 100 && pixels[i + 1] < 150
                        : pixels[i + 2] > 180 && pixels[i + 1] < 100 && pixels[i] < 130))) matching++;
                if (matching < 100) throw new InvalidOperationException("Native SVG avatar did not render its own variant's expected pixels.");
                Program.Log($"PASS: native preset/generated SVG avatar renders {matching} expected pixels; realAsset={realAssets}, variant={(blue ? "second" : "first")}.");
                owner.Children.Remove(image);
            }
            var photo = converter.Convert("https://avatars.invalid/photo.png", typeof(ImageSource), null!, "");
            if (photo is not BitmapImage || await UrlToImageSourceConverter.GetLoadOutcome((ImageSource)photo)! != true)
                throw new InvalidOperationException("Uploaded bitmap avatar decoding regressed.");
            var broken = (ImageSource)converter.Convert("https://avatars.invalid/broken.svg", typeof(ImageSource), null!, "");
            if (await UrlToImageSourceConverter.GetLoadOutcome(broken)!.WaitAsync(TimeSpan.FromSeconds(3)))
                throw new InvalidOperationException("Malformed SVG did not publish a failed load outcome for fallback artwork.");
            Program.Log("PASS: actual Settings retains all profiles, malformed avatars do not abort the list, SVG avatars render, and uploaded PNG still decodes.");
        }
        finally { owner.Children.Clear(); await Task.Delay(100); window.Close(); await Task.Delay(50); field.SetValue(null, previous); }
    }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values) : IServiceProvider
    { public object? GetService(Type type) => values.GetValueOrDefault(type) ?? fallback.GetService(type); }

    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host is not ("avatars.invalid" or "api.dicebear.com")) throw new InvalidOperationException("External avatar fixture request refused.");
            byte[] bytes; string contentType;
            if (uri.AbsolutePath.EndsWith(".svg", StringComparison.Ordinal) || uri.Host == "api.dicebear.com")
            {
                var color = uri.Query.Contains("seed=second", StringComparison.Ordinal) ? "#2244ee" : "#ed3344";
                bytes = Encoding.UTF8.GetBytes(uri.AbsolutePath == "/broken.svg" ? "not an SVG" : "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"><rect width=\"64\" height=\"64\" fill=\"" + color + "\"/></svg>"); contentType = "image/svg+xml";
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_AVATAR_REAL_ASSETS") is { } assets && uri.AbsolutePath != "/broken.svg")
                    bytes = File.ReadAllBytes(Path.Combine(assets, uri.Host == "api.dicebear.com" ? "dicebear-fixture.svg" : "avatar-3.svg"));
            }
            else if (uri.AbsolutePath.EndsWith("photo.png", StringComparison.Ordinal))
            { bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII="); contentType = "image/png"; }
            else
            {
                var body = uri.AbsolutePath.EndsWith("/profiles", StringComparison.Ordinal)
                    ? "{\"items\":[{\"id\":\"primary-fixture\",\"name\":\"Preset\",\"avatar_url\":\"/profile-avatars/preset.svg\"},{\"id\":\"generated\",\"name\":\"Generated\",\"avatar_url\":\"https://api.dicebear.com/9.x/thumbs/svg?seed=fixture\"},{\"id\":\"bad\",\"name\":\"Malformed\",\"avatar_url\":\"http://[\"},{\"id\":\"photo\",\"name\":\"Photo\",\"avatar_url\":\"/photo.png\"}]}"
                    : "{\"items\":[]}";
                bytes = Encoding.UTF8.GetBytes(body); contentType = "application/json";
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new(contentType); return Task.FromResult(response);
        }
    }
}
