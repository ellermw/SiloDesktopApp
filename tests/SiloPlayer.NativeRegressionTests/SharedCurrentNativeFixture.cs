using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Services;

internal static class SharedCurrentNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        await VerifyToolbarLabelsAsync(parent);
        await VerifyCurrentDefaultArtworkAsync(parent);
        using var missingArtwork = new MissingArtworkWire();
        using var http = new HttpClient(missingArtwork);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://shared-current.invalid");
        var customization = new UICustomizationService(new SettingsApi(client));
        customization.CardPresentation.Caption = "title";
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = serviceField.GetValue(null);
        using var services = new ServiceCollection().AddSingleton(customization).AddSingleton(new NavigationService())
            .AddSingleton(http).AddSingleton(_ => new ImageService(Path.Combine(Path.GetTempPath(), "silo-artwork-fixture-" + Guid.NewGuid().ToString("N"))))
            .AddSingleton(new ToastService()).BuildServiceProvider();
        var surface = new StackPanel { Width = 720, Spacing = 16 };
        serviceField.SetValue(null, services);
        try
        {
            await VerifyConvertedFailuresAsync(parent, missingArtwork);
            var item = new RequestMediaResult { Title = "Current request title", MediaType = "movie", Year = 2026,
                Request = new RequestState { Requestable = true } };
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var card = ExternalTitleCard.Build(item, 180, () => pending.Task,
                () => { item.InWatchlist = true; return Task.CompletedTask; });
            surface.Children.Add(card); parent.Children.Add(surface);
            await Task.Delay(80); surface.UpdateLayout();
            var title = Descendants<TextBlock>(card).Single(t => t.Text == item.Title && t.TextTrimming == TextTrimming.CharacterEllipsis);
            var meta = Descendants<TextBlock>(card).SingleOrDefault(t => t.Text == "MOVIE · 2026");
            if (title.FontWeight != FontWeights.SemiBold || title.FontSize != 14 || meta is null || meta.FontSize != 11 || meta.LineHeight != 16.5)
                throw new InvalidOperationException("Request caption must retain14px semibold title and11px metadata in title-only mode.");
            var artwork = card.Children.OfType<Border>().First();
            if (artwork.CornerRadius != new CornerRadius(16) || ((SolidColorBrush)artwork.BorderBrush).Color.A != 166)
                throw new InvalidOperationException("Request artwork does not use current16px radius and65% border color.");
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual((Grid)artwork.Child);
            if (visual.Clip is not Microsoft.UI.Composition.CompositionGeometricClip)
                throw new InvalidOperationException("Request artwork can escape the rounded overflow clip.");
            var request = Descendants<Button>(card).Single(b => AutomationProperties.GetName(b) == "Request Current request title");
            var bookmark = Descendants<Button>(card).Single(b => AutomationProperties.GetName(b).StartsWith("Add Current request title"));
            if (request.Opacity != 0 || bookmark.Opacity != 0 || request.IsHitTestVisible || bookmark.IsHitTestVisible)
                throw new InvalidOperationException("Unrevealed request/watchlist controls still intercept pointer input.");
            if (!request.Focus(FocusState.Keyboard)) throw new InvalidOperationException("Hidden request action lost keyboard accessibility.");
            await Task.Delay(30);
            if (request.Opacity != 1 || !request.IsHitTestVisible) throw new InvalidOperationException("Keyboard focus did not reveal request action.");
            Invoke(request); await Task.Delay(30);
            if (request.IsEnabled || request.Opacity != 1 || AutomationProperties.GetName(request) != "Sending request for Current request title")
                throw new InvalidOperationException("Pending request is not persistently visible/disabled with current accessible status.");
            pending.SetResult(); await Task.Delay(30);
            if (!request.IsEnabled || AutomationProperties.GetName(request) != "Request Current request title")
                throw new InvalidOperationException("Request completion did not restore action authority and accessible name.");
            bookmark.Focus(FocusState.Keyboard); Invoke(bookmark); await Task.Delay(30);
            if ((string?)ToolTipService.GetToolTip(bookmark) != "On Watchlist" || !AutomationProperties.GetName(bookmark).StartsWith("Remove "))
                throw new InvalidOperationException("Bookmark completion did not update tooltip and remove action.");
            var expected = card.XamlRoot.Size.Width >= 640 ? 32d : 24d;
            var expectedIcon = expected == 32 ? 16d : 12d;
            if (bookmark.ActualWidth != expected || ((Viewbox)bookmark.Content).Width != expectedIcon || bookmark.CornerRadius != new CornerRadius(10))
                throw new InvalidOperationException("Request watchlist trigger does not match viewport-dependent poster action geometry.");
            customization.CardPresentation.Caption = "artwork";
            var artworkOnly = ExternalTitleCard.Build(item, 180);
            surface.Children.Add(artworkOnly); surface.UpdateLayout();
            if (Descendants<TextBlock>(artworkOnly).Any(t => t.Text == "MOVIE · 2026" && t.Visibility == Visibility.Visible))
                throw new InvalidOperationException("Artwork-only request card still renders metadata.");
            if (Math.Abs(artworkOnly.ActualHeight - 270) > .5)
                throw new InvalidOperationException("Artwork-only request card reserves an empty caption margin.");
            var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var renders = new HashSet<string>();
            icons.Children.Add(WebUiIcon.Create("plus", 24));
            foreach (var name in new[] { "users", "list-ordered", "wand-sparkles", "grip-vertical" })
                icons.Children.Add(WebUiIcon.Create(name, 24));
            surface.Children.Add(icons); await Task.Delay(50); surface.UpdateLayout();
            foreach (var icon in icons.Children)
            {
                var render = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await render.RenderAsync(icon);
                var pixels = (await render.GetPixelsAsync()).ToArray();
                if (pixels.Length == 0 || !renders.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels))))
                    throw new InvalidOperationException("Shared collection icon fell back to plus or duplicated another icon.");
            }
            await MediaParityNativeFixture.CaptureAsync(surface, "shared-current-request-caption-actions-icons.png");
            await AssertDimArtworkAsync();
            Program.Log("PASS: current request caption policy, keyboard reveal, pending authority, bookmark geometry/tooltip and distinct shared icon strokes.");
        }
        finally { parent.Children.Remove(surface); serviceField.SetValue(null, original); }
    }
    private static async Task AssertDimArtworkAsync()
    {
        using var source = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), 1, 1, 96);
        using (var drawing = source.CreateDrawingSession()) drawing.Clear(Windows.UI.Color.FromArgb(255, 255, 0, 0));
        using var png = new MemoryStream();
        await source.SaveAsync(png.AsRandomAccessStream(), CanvasBitmapFileFormat.Png);
        var type = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Helpers.ArtworkEffects")!;
        var output = await (Task<byte[]>)type.GetMethod("DimRequestPosterAsync")!.Invoke(null, new object[] { png.ToArray() })!;
        using var stream = new MemoryStream(output);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
        var pixels = (await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Straight, new Windows.Graphics.Imaging.BitmapTransform(),
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage)).DetachPixelData();
        // Win2D PNG decoder is BGRA here. The opaque red input becomes brightness.85,
        // saturation.8 red~183/green~9/blue~9; opacity remains255.
        if (pixels.Length != 4 || Math.Abs(pixels[2] - 183) > 3 || Math.Abs(pixels[1] - 9) > 3 || Math.Abs(pixels[0] - 9) > 3 || pixels[3] != 255)
            throw new InvalidOperationException("Request dim artwork still changes alpha or omits current brightness/saturation.");
        Program.Log("PASS: actual request artwork transformed pixels preserve alpha and current brightness/saturation.");
    }
    private static async Task VerifyToolbarLabelsAsync(Panel parent)
    {
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        parent.Children.Add(toolbar);
        try
        {
            foreach (var label in new[] { "Movies & Series", "Date Added", "Descending", "Latest Episode Air Date" })
            {
                var choice = new ComboBox();
                choice.Items.Add(new ComboBoxItem { Content = label });
                choice.SelectedIndex = 0;
                typeof(PosterCard).Assembly.GetType("SiloPlayer.Controls.CatalogToolbarChoices")!
                    .GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { new[] { choice } });
                toolbar.Children.Add(choice);
                await Task.Delay(50); toolbar.UpdateLayout();
                var presenter = Descendants<ContentPresenter>(choice).Single(p => p.Name == "ContentPresenter");
                var text = new TextBlock { Text = label, FontFamily = choice.FontFamily, FontSize = choice.FontSize, FontWeight = choice.FontWeight };
                text.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                Program.Log($"TRACE: toolbar {label}: trigger={choice.ActualWidth}, available={presenter.ActualWidth}, text={text.DesiredSize.Width}, margin={presenter.Margin}, padding={presenter.Padding}.");
                if (presenter.ActualWidth + .5 < text.DesiredSize.Width)
                    throw new InvalidOperationException($"Toolbar selected label is clipped: {label}.");
            }
        }
        finally { parent.Children.Remove(toolbar); }
    }

    private static async Task VerifyConvertedFailuresAsync(Panel parent, MissingArtworkWire wire)
    {
        var converter = new SiloPlayer.Converters.UrlToImageSourceConverter();
        wire.HoldOld = true;
        var pending = (Microsoft.UI.Xaml.Media.Imaging.BitmapImage)converter.Convert("https://image.tmdb.org/t/p/w342/silo-artwork-fixture/pending.png?revision=old", typeof(ImageSource), null!, "");
        await wire.OldStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            var fresh = (Microsoft.UI.Xaml.Media.Imaging.BitmapImage)converter.Convert("https://image.tmdb.org/t/p/w342/silo-artwork-fixture/pending.png?revision=fresh", typeof(ImageSource), null!, "");
            if (ReferenceEquals(pending, fresh) || await SiloPlayer.Converters.UrlToImageSourceConverter.GetLoadOutcome(fresh)!.WaitAsync(TimeSpan.FromSeconds(3)))
                throw new InvalidOperationException("Renewed artwork URL is attached to the previous URL's pending operation.");
        }
        finally { wire.ReleaseOld.TrySetResult(true); }
        await SiloPlayer.Converters.UrlToImageSourceConverter.GetLoadOutcome(pending)!.WaitAsync(TimeSpan.FromSeconds(3));
        wire.HoldOld = false; wire.ArtworkRequests = 0;
        foreach (var revision in new[] { "old", "fresh" })
        {
            var card = ExternalTitleCard.Build(new RequestMediaResult { Title = "Missing artwork fixture", MediaType = "series", PosterPath = "/silo-artwork-fixture/poster.png?revision=" + revision }, 147);
            var image = ((Grid)card.Children.OfType<Border>().First().Child).Children.OfType<Image>().Single();
            var outcome = SiloPlayer.Converters.UrlToImageSourceConverter.GetLoadOutcome(image.Source);
            parent.Children.Add(card);
            try
            {
                if (outcome == null || await outcome.WaitAsync(TimeSpan.FromSeconds(3)))
                    throw new InvalidOperationException("Converted404 artwork did not report failure without relying on WinUI ImageFailed.");
                await Task.Delay(60);
                if (image.Source != null || Descendants<DefaultArtwork>(card).Single().Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Fetch failure left an empty converted bitmap instead of the media-type fallback.");
            }
            finally { parent.Children.Remove(card); }
        }
        if (wire.ArtworkRequests != 2) throw new InvalidOperationException("A renewed poster URL reused a failed bitmap or stale negative-cache entry.");
        Program.Log("PASS: converted artwork404 activates the shared fallback and a renewed URL retries independently.");
    }

    private sealed class MissingArtworkWire : HttpMessageHandler
    {
        public int ArtworkRequests;
        public bool HoldOld;
        public TaskCompletionSource<bool> OldStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseOld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.Contains("/silo-artwork-fixture/", StringComparison.Ordinal)) Interlocked.Increment(ref ArtworkRequests);
            if (HoldOld && request.RequestUri.Query == "?revision=old")
            { OldStarted.TrySetResult(true); await ReleaseOld.Task.WaitAsync(ct); }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }
    }

    private static async Task VerifyCurrentDefaultArtworkAsync(Panel parent)
    {
        var fallback = new DefaultArtwork { Width = 147, Height = 221, MediaType = "series" };
        parent.Children.Add(fallback);
        try
        {
            await Task.Delay(40); fallback.UpdateLayout();
            if (fallback.IsHitTestVisible || AutomationProperties.GetAccessibilityView(fallback) != AccessibilityView.Raw)
                throw new InvalidOperationException("Decorative artwork captures pointer input or introduces an accessibility node.");
            var mark = Descendants<Viewbox>(fallback).Single();
            Program.Log($"TRACE: fallback={fallback.ActualWidth}x{fallback.ActualHeight}, mark={mark.ActualWidth}, opacity={mark.Opacity}, scale={fallback.XamlRoot.RasterizationScale}.");
            // Actual layout rounds to physical pixels; the requested size stays fractional.
            if (Math.Abs(mark.Width - 35.28) > .001 || Math.Abs(mark.ActualWidth - 35.28) > .51 / fallback.XamlRoot.RasterizationScale || Math.Abs(mark.Opacity - .13) > .001)
                throw new InvalidOperationException("Default poster mark does not use clamped24%-width geometry and13% opacity.");
            var gradients = Descendants<Border>(fallback).Select(border => border.Background).OfType<RadialGradientBrush>().ToArray();
            if (gradients.Length != 3 || gradients.Any(brush => brush.MappingMode != BrushMappingMode.Absolute || brush.RadiusX != brush.RadiusY)
                || !gradients.Any(brush => Math.Abs(brush.RadiusX - 110.5) < .05 && Math.Abs(brush.Center.X - 102.9) < .05))
                throw new InvalidOperationException("Default artwork glows are elliptical or do not scale from the longer side.");
            fallback.Width = 315; fallback.Height = 177; fallback.UpdateLayout(); await Task.Delay(30);
            if (mark.ActualWidth != 40 || !gradients.Any(brush => Math.Abs(brush.RadiusX - 157.5) < .05))
                throw new InvalidOperationException("Default still artwork did not resize without clipping the mark.");
            fallback.Reset("audiobook", "not base64"); await fallback.ShowThumbhashAsync();
            if (Descendants<Image>(fallback).Single().Source != null || Descendants<Viewbox>(fallback).Single().Visibility != Visibility.Visible)
                throw new InvalidOperationException("Invalid thumbhash removed the default artwork fallback.");
            fallback.Dim = true;
            var background = ((SolidColorBrush)fallback.Background).Color;
            if (background.R >= 22 || background.G >= 23 || background.B >= 28 || background.A != 255)
                throw new InvalidOperationException("Unavailable-title fallback changes alpha instead of dimming its colors.");
            Program.Log("PASS: actual default artwork poster/still geometry, decorative authority, invalid-thumbhash retention and dimmed colors.");
        }
        finally { parent.Children.Remove(fallback); }
    }

    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
