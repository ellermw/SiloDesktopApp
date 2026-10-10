using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Controls;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class BrowseCalendarArtworkNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)serviceField.GetValue(null)!;
        using var wire = new Wire(await CoverBytes()); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://calendar-art.invalid");
        var catalog = new CatalogApi(client); var presentation = new UICustomizationService(new SettingsApi(client));
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "calendar-images"));
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "calendar-settings"));
        CalendarViewModel? model = null;
        var frame = new Frame();
        var navigation = new NavigationService { Frame = frame };
        serviceField.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(HttpClient)] = http, [typeof(CatalogApi)] = catalog,
            [typeof(ImageService)] = images, [typeof(UICustomizationService)] = presentation, [typeof(NavigationService)] = navigation,
        }, () => model!));
        try
        {
            var layoutDifferences = new List<string>();
            foreach (var (size, width, expectedWidth) in new[] { ("compact", 460, 120d), ("standard", 900, 160d), ("large", 1280, 220d) })
            {
                frame = new Frame(); navigation.Frame = frame;
                model = new(catalog, settings) { HasLoaded = true };
                var today = DateTime.Today.ToString("yyyy-MM-dd");
                var other = DateTime.Parse(model.WeekStart).AddDays(1).ToString("yyyy-MM-dd");
                if (other == today) other = DateTime.Parse(model.WeekStart).AddDays(2).ToString("yyyy-MM-dd");
                model.Days.Add(new() { Date = today, Items = [new() { ContentId = "watched-art", Type = "movie", Title = "Watched Feature", Watched = true, PosterUrl = "https://calendar-art.invalid/cover.png" }, new() { ContentId = "color-art", Type = "movie", Title = "Upcoming Feature", PosterUrl = "https://calendar-art.invalid/cover.png" }] });
                model.Days.Add(new() { Date = other, Items = [new() { ContentId = "other-day", Type = "movie", Title = "Other scheduled title" }] });
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_CALENDAR_LAYOUT") == "1")
                {
                    model.Days[0].Items[0].Badges = ["series_premiere"];
                    model.Days[0].Items[0].AirTime = "21:00";
                    model.Days[0].Items[1].AirTime = "21:00";
                    for (var i = 0; i < 12; i++) model.Days[0].Items.Add(new() { ContentId = "overflow-" + i, Type = "movie", Title = "Carousel title " + i });
                }
                var owner = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] };
                owner.Children.Add(frame); var window = new Window { Content = owner };
                try
                {
                    window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000)); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900)); window.AppWindow.Show(false); await Task.Delay(100);
                    var scale = owner.XamlRoot.RasterizationScale; window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
                    frame.Navigate(typeof(CalendarPage)); var page = (CalendarPage)frame.Content;
                    if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_CALENDAR_LAYOUT") == "1")
                    {
                        var initialPreset = (Button)page.FindName("FilterFollowingBtn");
                        if (initialPreset.Background is not SolidColorBrush activeBrush || activeBrush.Color != ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color)
                            layoutDifferences.Add($"{width}: the saved Following preset is not highlighted before asynchronous Calendar results arrive.");
                    }
                    await presentation.SaveCardPresentationAsync(new() { PosterSize = size });
                    await Until(() => Field<double>(page, "_eventCardWidth") == expectedWidth);
                    if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_CALENDAR_LAYOUT") == "1")
                    {
                        page.UpdateLayout();
                        var heading = Descendants<TextBlock>(page).Single(text => text.Text == "Calendar");
                        var header = (Grid)page.FindName("HeaderGrid");
                        var actions = (StackPanel)page.FindName("HeaderActions");
                        var preset = (Button)page.FindName("FilterFollowingBtn");
                        var navigator = (Border)page.FindName("WeekNavigatorBorder");
                        if (((Border)page.FindName("WeekNavigatorSpace")).Margin.Bottom != 20)
                            layoutDifferences.Add($"{width}: the first day starts more than the current20px after the navigator.");
                        var selectedEmpty = (Border)page.FindName("SelectedDayEmptyState");
                        var expectedNavigatorHeight = width < 640 ? 120 : 140.5;
                        if (Math.Abs(navigator.ActualHeight - expectedNavigatorHeight) > 1 || (width >= 700 && header.RowSpacing != 0))
                            layoutDifferences.Add($"{width}: unused header row or week navigator line boxes add incorrect vertical space; navigator={navigator.ActualHeight}, gap={header.RowSpacing}.");
                        var firstCard = Descendants<Button>(page).First(button => AutomationProperties.GetName(button).StartsWith("Watched Feature, Movie", StringComparison.Ordinal));
                        var title = Descendants<TextBlock>(firstCard).Single(text => text.Text == "Watched Feature");
                        if (title.LineHeight != 21 || ((StackPanel)VisualTreeHelper.GetParent(title)).Margin.Top != 12)
                            layoutDifferences.Add($"{width}: Calendar caption does not retain rendered WebUI21px title line and12px top spacing.");
                        var movieCaption = Descendants<TextBlock>(firstCard).Single(text => text.FontSize == 11 && text.CharacterSpacing == 140);
                        if (movieCaption.Text != "MOVIE") layoutDifferences.Add($"{width}: Calendar metadata caption must render uppercase.");
                        var firstDayHeading = Descendants<TextBlock>(page).Single(text => text.Text == CalendarViewModel.FormatDayHeading(today));
                        var todayLabel = Descendants<TextBlock>(page).Single(text => text.Text == "TODAY");
                        var todayPoint = todayLabel.TransformToVisual(firstDayHeading).TransformPoint(new());
                        if (todayPoint.X > firstDayHeading.ActualWidth + 24 || todayLabel.Foreground is not SolidColorBrush todayForeground || todayForeground.Color != ((SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"]).Color)
                            layoutDifferences.Add($"{width}: Today badge must sit beside the day heading with the primary foreground.");
                        var cardPanel = Descendants<StackPanel>(page).First(panel => panel.Children.OfType<Button>().Contains(firstCard));
                        var expectedCardHeight = expectedWidth * 1.5 + 72;
                        Program.Log($"Calendar caption geometry{width}: card={firstCard.ActualHeight}, expected={expectedCardHeight}, row={cardPanel.ActualHeight}.");
                        if (Math.Abs(firstCard.ActualHeight - expectedCardHeight) > 2 || cardPanel.ActualHeight + 1 < firstCard.ActualHeight)
                            layoutDifferences.Add($"{width}: all three caption lines must fit the row before the next day; height={firstCard.ActualHeight}, row={cardPanel.ActualHeight}.");
                        if (cardPanel.Spacing != (width >= 1024 ? 20 : 16)) layoutDifferences.Add($"{width}: Calendar cards must use the desktop20px carousel gap.");
                        var rail = Descendants<CarouselRail>(page).FirstOrDefault();
                        if (rail == null) layoutDifferences.Add($"{width}: Calendar is missing carousel edge/keyboard/drag controls.");
                        else
                        {
                            var next = Descendants<Button>(rail).Single(button => AutomationProperties.GetName(button) == "Scroll right");
                            var scroll = Descendants<ScrollViewer>(rail).Single();
                            if (next.Width != 44 || next.Height != 44 || next.VerticalAlignment != VerticalAlignment.Center || scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Hidden)
                                layoutDifferences.Add($"{width}: Calendar edge controls differ from current44px WebUI arrows.");
                            Invoke(next); await Until(() => scroll.HorizontalOffset > 0);
                            var previousArrow = Descendants<Button>(rail).Single(button => AutomationProperties.GetName(button) == "Scroll left");
                            Invoke(previousArrow); await Until(() => scroll.HorizontalOffset < 1);
                        }
                        var premiere = Descendants<TextBlock>(firstCard).Single(text => text.Text == "SERIES PREMIERE");
                        var premiereBorder = (Border)premiere.Parent;
                        if (premiereBorder.Height != 16 || premiereBorder.Padding.Left != 8 || premiere.CharacterSpacing != 50 || ((FrameworkElement)premiereBorder.Parent).Margin.Left != 10)
                            layoutDifferences.Add($"{width}: Calendar artwork badges differ from current10px inset/16px pill/8px padding/0.05em tracking.");
                        await presentation.SaveCardPresentationAsync(new() { PosterSize = size, Caption = "artwork" });
                        await Task.Delay(100); page.UpdateLayout();
                        var artworkCard = Descendants<Button>(page).First(button => AutomationProperties.GetName(button).StartsWith("Watched Feature, Movie", StringComparison.Ordinal));
                        if (Descendants<TextBlock>(artworkCard).Any(text => text.Text == "Watched Feature" && text.Visibility == Visibility.Visible && ((FrameworkElement)VisualTreeHelper.GetParent(text)).Visibility == Visibility.Visible))
                            layoutDifferences.Add($"{width}: artwork-only Calendar still renders captions.");
                        await presentation.SaveCardPresentationAsync(new() { PosterSize = size, Caption = "title" });
                        await Task.Delay(100); page.UpdateLayout();
                        var titleCard = Descendants<Button>(page).First(button => AutomationProperties.GetName(button).StartsWith("Watched Feature, Movie", StringComparison.Ordinal));
                        if (Descendants<TextBlock>(titleCard).Any(text => text.FontSize == 11 && text.Visibility == Visibility.Visible))
                            layoutDifferences.Add($"{width}: title-only Calendar still renders metadata.");
                        if (heading.FontSize != (width < 640 ? 24 : 30) || heading.CharacterSpacing != -25 || header.Margin.Top != (width < 640 ? 32 : width < 1024 ? 40 : 56) || header.Margin.Bottom != 24 || actions.Spacing != 8)
                            layoutDifferences.Add($"{width}: header typography, outer vertical rhythm and action gaps differ from current Calendar.");
                        if (preset.Padding.Left != 16 || navigator.Padding.Left != (width < 640 ? 8 : 12) || selectedEmpty.CornerRadius.TopLeft != 20)
                            layoutDifferences.Add($"{width}: preset/navigator padding and selected-day empty radius differ from current source.");
                        var dayButtons = Descendants<Button>((Grid)page.FindName("WeekStripPanel")).ToArray();
                        Program.Log($"Calendar measured{width}: header={header.ActualHeight}, navigator={navigator.ActualHeight}; days=" + string.Join(" | ", dayButtons.Select(button => $"{button.ActualHeight}:" + string.Join(",", Descendants<TextBlock>(button).Select(text => $"{text.Text}={text.ActualHeight}/line{text.LineHeight}/padding{text.Padding}/margin{text.Margin}")))));
                        var labels = dayButtons.SelectMany(button => Descendants<TextBlock>(button)).ToArray();
                        var dots = dayButtons.SelectMany(button => Descendants<Microsoft.UI.Xaml.Shapes.Ellipse>(button)).ToArray();
                        if (!labels.Any(text => text.FontWeight.Weight == 700 && text.FontSize == (width < 640 ? 14 : 16)) || dots.Any(dot => dot.Width != 4))
                            layoutDifferences.Add($"{width}: week-day number typography/event dots differ from responsive navigator.");
                        model.Days.Clear(); model.IsEmpty = true; await Task.Delay(100);
                        var emptyShell = (StackPanel)page.FindName("EmptyState"); var emptyPanel = emptyShell.Children.OfType<Border>().Single();
                        if (emptyShell.Padding.Left != 0 || emptyShell.Margin.Top != 8 || emptyPanel.MinHeight != 300 || Math.Abs(emptyPanel.CornerRadius.TopLeft - 28.8) > .01 || emptyPanel.Padding.Left != 24 || emptyPanel.Padding.Top != 64 || emptyPanel.BorderThickness.Top != 0)
                            layoutDifferences.Add($"{width}: empty week uses additional nested padding and the wrong panel height/radius/border.");
                        await MediaParityNativeFixture.CaptureAsync(owner, $"calendar-source-layout-{width}.png");
                        continue;
                    }
                    bool HasCard(string title) => Descendants<Button>(page).Any(button => AutomationProperties.GetName(button) == title + ", Movie");
                    Image Poster(string title)
                    {
                        var card = Descendants<Button>(page).Single(button => AutomationProperties.GetName(button) == title + ", Movie");
                        var root = (Grid)card.Content;
                        return root.Children.OfType<Grid>().Single().Children.OfType<Image>().Single();
                    }
                    await Until(() => HasCard("Watched Feature") && HasCard("Upcoming Feature") && Poster("Watched Feature").Source != null && Poster("Upcoming Feature").Source != null);
                    var watched = Poster("Watched Feature"); var color = Poster("Upcoming Feature");
                    page.UpdateLayout(); await Until(() => watched.ActualWidth > 0 && watched.ActualHeight > 0 && color.ActualWidth > 0 && color.ActualHeight > 0); await Task.Delay(100);
                    var watchedPoint = watched.TransformToVisual(owner).TransformPoint(new()); var colorPoint = color.TransformToVisual(owner).TransformPoint(new());
                    Program.Log($"Calendar layout diagnostic: owner={owner.ActualWidth:R}x{owner.ActualHeight:R}, watched={watched.ActualWidth:R}x{watched.ActualHeight:R}@{watchedPoint.X:R},{watchedPoint.Y:R}, color={color.ActualWidth:R}x{color.ActualHeight:R}@{colorPoint.X:R},{colorPoint.Y:R}.");
                    Program.Log($"Calendar opacity diagnostic: watched={watched.Opacity:R}, color={color.Opacity:R}, watched loaded={watched.IsLoaded}, color loaded={color.IsLoaded}.");
                    var watchedPoster = (Grid)VisualTreeHelper.GetParent(watched);
                    var colorPoster = (Grid)VisualTreeHelper.GetParent(color);
                    var watchedCaption = Descendants<TextBlock>(Descendants<Button>(page).Single(button => AutomationProperties.GetName(button) == "Watched Feature, Movie")).Single(text => text.Text == "Watched Feature");
                    var expectedMuted = ((SolidColorBrush)Application.Current.Resources["SecondaryTextBrush"]).Color;
                    var gradient = watchedPoster.Children.OfType<Border>().Single(border => border.Background is LinearGradientBrush);
                    Program.Log($"Calendar subtree diagnostic: watchedPosterOpacity={watchedPoster.Opacity:R}, imageOpacity={watched.Opacity:R}, unwatchedPosterOpacity={colorPoster.Opacity:R}, gradientHeight={gradient.ActualHeight:R}, posterRadius={watchedPoster.CornerRadius.TopLeft:R}, titleColor={((SolidColorBrush)watchedCaption.Foreground).Color}, expectedMuted={expectedMuted}.");
                    if (Math.Abs(watchedPoster.Opacity - .6) > .000001 || watched.Opacity != 1 || colorPoster.Opacity != 1 || Math.Abs(gradient.ActualHeight - 96) > .001 || watchedPoster.CornerRadius.TopLeft != 16 || ((SolidColorBrush)watchedCaption.Foreground).Color != expectedMuted)
                        throw new InvalidOperationException("Actual Calendar watched poster subtree must share0.6 opacity including check/gradient,96px gradient, source16px corners and muted watched title.");
                    if (watched.Opacity != 1 || color.Opacity != 1) throw new InvalidOperationException("Calendar watched opacity must be0.6 while an unwatched poster remains opaque.");
                    var watchedPixel = await Pixel(watched); var colorPixel = await Pixel(color);
                    Program.Log($"Calendar{width}/{size}: card={expectedWidth}, watched BGRA={string.Join(",", watchedPixel)}, color BGRA={string.Join(",", colorPixel)}, opacity={watched.Opacity}.");
                    if (watchedPixel[3] == 0 || watchedPixel.Take(3).Max() < 15 || watchedPixel.Take(3).Max() - watchedPixel.Take(3).Min() > 3 || colorPixel.Take(3).Max() - colorPixel.Take(3).Min() < 40)
                        throw new InvalidOperationException("Actual decoded watched poster must be grayscale; the same local image's unwatched poster must retain color.");
                    var badge = Descendants<Border>(page).Single(border => AutomationProperties.GetName(border) == "Watched");
                    var badgePoint = badge.TransformToVisual(watched).TransformPoint(new());
                    if (badge.ActualWidth != 32 || badge.ActualHeight != 32 || Math.Abs(badgePoint.X - (watched.ActualWidth - 32) / 2) > .5 || Math.Abs(badgePoint.Y - (watched.ActualHeight - 32) / 2) > .5)
                        throw new InvalidOperationException("Watched Calendar badge must render32x32 centered over the actual loaded poster.");
                    var source = watched.Source; var colorSource = color.Source; var reads = wire.ImageReads;
                    Invoke(Descendants<Button>(page).Single(button => AutomationProperties.GetName(button) == CalendarViewModel.FormatDayHeading(other) + ", scheduled releases")); await Task.Delay(100);
                    Invoke(Descendants<Button>(page).Single(button => AutomationProperties.GetName(button) == CalendarViewModel.FormatDayHeading(today) + ", scheduled releases")); await Task.Delay(100);
                    Invoke(Descendants<Button>(page).Single(button => button.Content?.ToString() == "Today")); await Task.Delay(100);
                    if (!ReferenceEquals(Poster("Watched Feature").Source, source) || !ReferenceEquals(Poster("Upcoming Feature").Source, colorSource) || wire.ImageReads != reads || wire.CalendarReads != 0)
                        throw new InvalidOperationException("Actual day-selection/current-week Today must retain loaded poster identity without artwork or Calendar rereads.");
                    await Capture(owner, $"browse-calendar-artwork-{size}-{width}.png");
                }
                finally { if (frame.Content is CalendarPage) frame.Navigate(typeof(Page)); await Task.Delay(80); owner.Children.Remove(frame); window.Close(); }
            }
            if (layoutDifferences.Count > 0) throw new InvalidOperationException(string.Join("\n", layoutDifferences));
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_CALENDAR_LAYOUT") == "1")
            {
                Program.Log("PASS: CALENDAR_SOURCE_LAYOUT_COMPLETED actual responsive header, preset/navigator padding, day typography/dots and empty-week panel.");
                return;
            }
            if (wire.PreferenceWrites != 3) throw new InvalidOperationException("Mounted Calendar preference proof must persist each of the three actual card sizes.");
            Program.Log("PASS: actual Calendar local-color watched grayscale/opacity/32px centered badge, persisted compact/normal/large mounted preferences, and day/Today image-identity retention.");
        }
        finally { frame.Content = null; navigation.Frame = null; serviceField.SetValue(null, previous); }
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var attempt = 0; attempt < 100 && !ready(); attempt++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Actual Calendar artwork/preference state did not settle."); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T typed) yield return typed; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static async Task<byte[]> Pixel(Image image)
    {
        var rendered = new RenderTargetBitmap(); await rendered.RenderAsync(image); var pixels = (await rendered.GetPixelsAsync()).ToArray();
        var x = Math.Max(0, rendered.PixelWidth / 4); var y = Math.Max(0, rendered.PixelHeight / 4); var index = (y * rendered.PixelWidth + x) * 4;
        if (pixels.Length < index + 4) throw new InvalidOperationException($"Actual Calendar image rendered no readable pixels: actual={image.ActualWidth:R}x{image.ActualHeight:R}, bitmap={rendered.PixelWidth}x{rendered.PixelHeight}, bytes={pixels.Length}.");
        return pixels[index..(index + 4)];
    }
    private static async Task<byte[]> CoverBytes()
    {
        var pixels = new byte[256 * 256 * 4]; var colors = new[] { (R: 246, G: 67, B: 40), (R: 32, G: 117, B: 218), (R: 247, G: 205, B: 56), (R: 30, G: 175, B: 91) };
        for (var y = 0; y < 256; y++) for (var x = 0; x < 256; x++) { var color = colors[(y / 128) * 2 + x / 128]; var offset = (y * 256 + x) * 4; pixels[offset] = (byte)color.B; pixels[offset + 1] = (byte)color.G; pixels[offset + 2] = (byte)color.R; pixels[offset + 3] = 255; }
        using var stream = new MemoryStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream()); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 256, 256, 96, 96, pixels); await encoder.FlushAsync(); return stream.ToArray();
    }
    private static async Task Capture(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element); var pixels = await bitmap.GetPixelsAsync(); var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<CalendarViewModel> model) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(CalendarViewModel) ? model() : values.GetValueOrDefault(type) ?? fallback.GetService(type); }
    private sealed class Wire(byte[] cover) : HttpMessageHandler
    {
        internal int ImageReads, CalendarReads, PreferenceWrites;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "calendar-art.invalid") throw new InvalidOperationException("Calendar artwork fixture attempted external networking.");
            if (request.RequestUri.AbsolutePath == "/cover.png") { ImageReads++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent(cover) }; }
            if (request.RequestUri.AbsolutePath == "/api/v2/calendar") { CalendarReads++; return new(HttpStatusCode.OK) { Content = new StringContent("{\"events\":[]}") }; }
            if (request.Method == HttpMethod.Put && request.RequestUri.AbsolutePath.StartsWith("/api/v2/settings/values/", StringComparison.Ordinal))
            {
                PreferenceWrites++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { key = "ui.card_presentation", value = body.RootElement.GetProperty("value"), source = "profile_client" })) };
            }
            throw new InvalidOperationException("Unexpected Calendar artwork fixture route: " + request.RequestUri.AbsolutePath);
        }
    }
}
