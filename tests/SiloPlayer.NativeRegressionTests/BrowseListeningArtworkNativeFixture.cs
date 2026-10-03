using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class BrowseListeningArtworkNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        var sharedCover = await CoverBytes();
        Program.Log("Listening cover PNG SHA256=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sharedCover)));
        using var wire = new Wire(sharedCover); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://listening-art.invalid");
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "listening-images"));
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> trace = (_, args) =>
        {
            var stack = args.Exception.StackTrace ?? "";
            if (stack.Contains("NowListeningHero", StringComparison.Ordinal) || stack.Contains("ArtworkEffects", StringComparison.Ordinal))
                Program.Log("TRACE artwork actual load/effect: " + args.Exception);
        };
        AppDomain.CurrentDomain.FirstChanceException += trace;
        field.SetValue(null, new Services(previous, http, images, new CatalogApi(client)));
        try
        {
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_LISTENING_REMOUNT") == "1") { await CanceledCoverRemountAsync(wire); Program.Log("PASS: actual canceled listening-cover remount."); return; }
            foreach (var width in new[] { 460, 1280 })
            {
                var owner = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] };
                // LibraryPage.RecommendedPanel measures this hero through a
                // vertical StackPanel in a ScrollViewer, with natural height.
                var sections = new StackPanel();
                owner.Children.Add(new ScrollViewer { Content = sections, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden });
                var window = new Window { Content = owner };
                try
                {
                    window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900)); window.AppWindow.Show(false); await Task.Delay(100);
                    var scale = owner.XamlRoot.RasterizationScale;
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
                    var hero = new NowListeningHero();
                    sections.Children.Add(hero);
                    hero.Bind(new() { ContentId = "audio-fixture", Type = "audiobook", Title = "An Audiobook With a Longer Title", PositionSeconds = 1200, DurationSeconds = 7200, PosterUrl = "https://listening-art.invalid/cover.png" });
                    var cover = (Image)hero.FindName("CoverImage"); var background = (Image)hero.FindName("BackgroundImage");
                    for (var attempt = 0; attempt < 100 && (cover.Source == null || background.Source == null); attempt++) await Task.Delay(25);
                    if (cover.Source == null || background.Source == null)
                    {
                        Program.Log($"Artwork load state: cover={cover.Source != null}, background={background.Source != null}; cache={string.Join(",", Directory.GetFiles(Path.Combine(Program.ResultDirectory, "listening-images")).Select(path => Path.GetFileName(path) + ":" + new FileInfo(path).Length))}.");
                        throw new InvalidOperationException("Actual Now Listening artwork did not finish loading through ImageService and its effect path.");
                    }
                    hero.UpdateLayout(); await Task.Delay(100);
                    var border = (Border)hero.FindName("CoverBorder"); var title = (TextBlock)hero.FindName("TitleText");
                    var progress = (Border)hero.FindName("ProgressTrack");
                    var point = border.TransformToVisual(hero).TransformPoint(new()); var titlePoint = title.TransformToVisual(hero).TransformPoint(new());
                    var progressPoint = progress.TransformToVisual(hero).TransformPoint(new());
                    var resume = Descendants<Button>(hero).Single(button => Descendants<TextBlock>(button).Any(text => text.Text == "Resume"));
                    var info = Descendants<Button>(hero).Single(button => Descendants<TextBlock>(button).Any(text => text.Text == "More Info"));
                    Program.Log($"Loaded Now Listening{width}: section={hero.ActualWidth}x{hero.ActualHeight}, cover={point}/{border.ActualWidth}x{border.ActualHeight}, title={titlePoint}/{title.ActualWidth}x{title.ActualHeight}/line{title.LineHeight}, progress={progressPoint}/{progress.ActualWidth}, pills={resume.ActualHeight}/{resume.CornerRadius.TopLeft},{info.ActualHeight}/{info.CornerRadius.TopLeft}.");
                    await Capture(owner, $"browse-listening-artwork-{width}.png");
                    if (Math.Abs(hero.ActualHeight - (width == 460 ? 540.609 : 400)) > 2 || Math.Abs(titlePoint.Y - (width == 460 ? 303.516 : 163.203)) > 2 || title.LineHeight != (width == 460 ? 36 : 48) || (width == 1280 && Math.Abs(progress.ActualWidth - 576) > 1))
                        throw new InvalidOperationException("Loaded Now Listening composition must match actual pinned-source section height, title leading/position and576px desktop progress width.");
                    if (Math.Abs(point.X - (width == 460 ? 16 : 48)) > .5 || Math.Abs(point.Y - (width == 460 ? 112 : 128)) > .5 || border.Width != (width == 460 ? 144 : 224))
                        throw new InvalidOperationException("Loaded Now Listening cover must match the actual144/224px source positions.");
                    if (Math.Abs(progressPoint.X - titlePoint.X) > .5 || Math.Abs(resume.CornerRadius.TopLeft - resume.ActualHeight / 2) > .5 || Math.Abs(info.CornerRadius.TopLeft - info.ActualHeight / 2) > .5)
                        throw new InvalidOperationException("Actual progress must start at the source title edge and transport buttons must use circular capsule ends.");
                    if (((TextBlock)hero.FindName("PositionText")).Text != "2 hr" || ((TextBlock)hero.FindName("TimeLeftText")).Text != "1 hr 40 min left")
                        throw new InvalidOperationException("Actual loaded listening time captions must use the pinned audiobook hour/minute formatting.");
                }
                finally { window.Close(); }
            }
            await CanceledCoverRemountAsync(wire);
            Program.Log("PASS: actual artwork-loaded Now Listening460/1280 composition against the pinned correct-CSS reference pair and canceled-cover remount.");
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= trace; field.SetValue(null, previous); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T typed) yield return typed; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static async Task CanceledCoverRemountAsync(Wire wire)
    {
        var owner = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] };
        var sections = new StackPanel(); owner.Children.Add(new ScrollViewer { Content = sections });
        var window = new Window { Content = owner };
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000)); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(460, 900)); window.AppWindow.Show(false); await Task.Delay(100);
            wire.DelayCover = true;
            var hero = new NowListeningHero(); sections.Children.Add(hero);
            hero.Bind(new() { ContentId = "reload-audio-fixture", Type = "audiobook", Title = "Remounted listening cover", PosterUrl = "https://listening-art.invalid/delayed-cover.png" });
            for (var attempt = 0; attempt < 100 && wire.DelayedCoverRequests == 0; attempt++) await Task.Delay(25);
            if (wire.DelayedCoverRequests != 1) throw new InvalidOperationException("Delayed foreground cover did not start through actual ImageService.");
            sections.Children.Remove(hero); await Task.Delay(100);
            if (hero.IsLoaded) throw new InvalidOperationException("Listening lifecycle fixture did not actually unload its hero.");
            wire.CoverGate.SetResult(); await Task.Delay(150);
            sections.Children.Add(hero);
            var cover = (Image)hero.FindName("CoverImage"); var background = (Image)hero.FindName("BackgroundImage");
            for (var attempt = 0; attempt < 100 && (cover.Source == null || background.Source == null); attempt++) await Task.Delay(25);
            Program.Log($"Canceled cover remount: loaded={hero.IsLoaded}, requests={wire.DelayedCoverRequests}, cover={cover.Source != null}, backdrop={background.Source != null}.");
            if (cover.Source == null || background.Source == null) throw new InvalidOperationException("Remounting the same listening hero without another Bind must recover its actually canceled cover/backdrop load.");
            var loadedCover = cover.Source; var loadedRequests = wire.DelayedCoverRequests;
            sections.Children.Remove(hero); await Task.Delay(100); sections.Children.Add(hero); await Task.Delay(150);
            if (!ReferenceEquals(cover.Source, loadedCover) || wire.DelayedCoverRequests != loadedRequests)
                throw new InvalidOperationException("Remounting an already-loaded listening cover must reuse its foreground/cache instead of fetching again.");
        }
        finally { window.Close(); }
    }
    private static async Task<byte[]> CoverBytes()
    {
        var pixels = new byte[256 * 256 * 4];
        var colors = new[] { (R: 246, G: 67, B: 40), (R: 32, G: 117, B: 218), (R: 247, G: 205, B: 56), (R: 30, G: 175, B: 91) };
        for (var y = 0; y < 256; y++) for (var x = 0; x < 256; x++) { var color = colors[(y / 128) * 2 + x / 128]; var offset = (y * 256 + x) * 4; pixels[offset] = (byte)color.B; pixels[offset + 1] = (byte)color.G; pixels[offset + 2] = (byte)color.R; pixels[offset + 3] = 255; }
        using var stream = new MemoryStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 256, 256, 96, 96, pixels); await encoder.FlushAsync(); return stream.ToArray();
    }
    private static async Task Capture(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element); var pixels = await bitmap.GetPixelsAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
    }
    private sealed class Services(IServiceProvider fallback, HttpClient http, ImageService images, CatalogApi catalog) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(HttpClient) ? http : type == typeof(ImageService) ? images : type == typeof(CatalogApi) ? catalog : fallback.GetService(type); }
    private sealed class Wire(byte[] cover) : HttpMessageHandler
    {
        internal bool DelayCover; internal int DelayedCoverRequests; internal TaskCompletionSource CoverGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "listening-art.invalid") throw new InvalidOperationException("Now Listening artwork fixture attempted external networking.");
            if (request.RequestUri.AbsolutePath == "/delayed-cover.png") { DelayedCoverRequests++; if (DelayCover && DelayedCoverRequests == 1) await CoverGate.Task; }
            var reply = request.RequestUri.AbsolutePath is "/cover.png" or "/delayed-cover.png"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(cover) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"content_id\":\"audio-fixture\",\"type\":\"audiobook\",\"title\":\"An Audiobook With a Longer Title\",\"versions\":[],\"audiobook\":{\"total_duration_seconds\":7200,\"authors\":[],\"narrators\":[]},\"user_data\":{\"position_seconds\":1200}}") };
            return reply;
        }
    }
}
