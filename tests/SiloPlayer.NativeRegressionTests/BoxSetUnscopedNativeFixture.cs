using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class BoxSetUnscopedNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var original = SiloPlayer.App.Services;
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://boxset-unscoped.invalid"); client.SetProfile("alpha");
        var catalog = new CatalogApi(client);
        var cache = new ItemDetailPrefetchCache((id, ct) => catalog.GetItemDetailAsync(id, ct),
            contextKey: () => client.CaptureContext().RequestContextGeneration.ToString());
        var frame = new Frame { Width = 960, Height = 700 };
        var navigation = new NavigationService { Frame = frame };
        var services = new Services(original, navigation, catalog, client, cache);
        servicesField.SetValue(null, services);
        try
        {
            parent.Children.Add(frame);
            var card = new PosterCard { MediaItem = new MediaItem { ContentId = "film", Type = "movie", Title = "Unscoped film" } };
            parent.Children.Add(card);
            Check(MediaNavigationContext.LibraryId(card) == null, "ordinary Home/global-search card has no selected library");
            Check((bool)typeof(PosterCard).GetMethod("ActivateCard", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(card, null)!, "ordinary card activation");
            await Wait(() => frame.Content is ItemDetailPage { IsLoaded: true, ViewModel.Item.ContentId: "film" });
            var page = (ItemDetailPage)frame.Content;
            await Wait(() => page.ViewModel.BoxSet != null);
            Check(page.ViewModel.LibraryId == null && wire.MovieQuery == "" && wire.MembershipQuery == "", "unscoped route preserves absent library in both requests");
            var section = (FrameworkElement)page.FindName("BoxSetSection");
            var row = (StackPanel)page.FindName("BoxSetMovies");
            Check(section.Visibility == Visibility.Visible && Descendants(row).OfType<PosterCard>().Count() == 2, "actual unscoped detail renders optional membership row");
            Check(Descendants(row).OfType<PosterCard>().All(x => MediaNavigationContext.LibraryId(x) == 12), "discovered row adopts returned eligible library for member actions");
            await Capture(section, "unscoped-row-wide.png");
            Type? target = null; object? args = null;
            navigation.NavigationRequestHandler = (type, value) => { target = type; args = value; return true; };
            var member = Descendants(row).OfType<PosterCard>().Last();
            typeof(PosterCard).GetMethod("ActivateCard", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(member, null);
            Check(target == typeof(ItemDetailPage) && args is ItemDetailNavigationArgs { ContentId: "other", LibraryId: 12 }, "discovered member has normal scoped detail destination");
            var viewAll = Descendants(section).OfType<Button>().Single(x => (x.Content as string) == "View all");
            ((IInvokeProvider)new ButtonAutomationPeer(viewAll).GetPattern(PatternInterface.Invoke)).Invoke();
            Check(target == typeof(BoxSetPage) && args is BoxSetNavigationArgs { Id: "7", LibraryId: "12" }, "discovered View all keeps returned library");
            page.Width = 540; page.UpdateLayout(); await Task.Delay(80);
            Check(section.ActualWidth <= 540, "compact discovered row fits detail viewport");
            await Capture(section, "unscoped-row-compact.png");
            wire.Enabled = false;
            catalog.InvalidateLibraryCache();
            await Wait(() => page.ViewModel.BoxSet == null);
            Check(section.Visibility == Visibility.Collapsed && row.Children.Count == 0, "disabled response library cannot borrow the other enabled library flag");
            wire.Enabled = true; await page.ViewModel.RefreshBoxSetAsync(); await Wait(() => section.Visibility == Visibility.Visible);
            wire.ReturnedLibrary = "99"; await page.ViewModel.RefreshBoxSetAsync();
            Check(page.ViewModel.BoxSet == null && section.Visibility == Visibility.Collapsed, "unknown returned library cannot appear in unscoped row");
            wire.ReturnedLibrary = "12"; await page.ViewModel.RefreshBoxSetAsync();
            wire.Hold = true; var pending = page.ViewModel.RefreshBoxSetAsync(); await wire.Started.Task;
            client.SetProfile("beta"); wire.Release.TrySetResult(); await pending;
            // Retiring the route must clear the previous visible row as well as the held result.
            navigation.NavigationRequestHandler = null; frame.Navigate(typeof(ItemDetailPage));
            await Task.Delay(80);
            Check(page.ViewModel.BoxSet == null, "departure retires discovered membership after profile change");
            Program.Log("PASS: actual unscoped PosterCard/Frame/movie-detail row, optional HTTP scope, eligible returned-library actions, disabled/unknown library rejection, compact viewport and departure.");
        }
        finally
        {
            frame.Content = null; parent.Children.Clear(); await Task.Delay(80); servicesField.SetValue(null, original);
        }
    }

    private static async Task Wait(Func<bool> ready)
    {
        for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(10);
        Check(ready(), "expected unscoped route state was not reached");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Box-set unscoped native: " + message); }
    private static async Task Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout(); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var path = Path.Combine(Program.ResultDirectory, name); File.WriteAllBytes(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private sealed class Services(IServiceProvider original, NavigationService navigation, CatalogApi catalog, SiloApiClient client, ItemDetailPrefetchCache cache) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(NavigationService) ? navigation : type == typeof(CatalogApi) ? catalog
            : type == typeof(SiloApiClient) ? client : type == typeof(ItemDetailPrefetchCache) ? cache
            : type == typeof(ItemDetailViewModel) ? new ItemDetailViewModel(catalog, cache) : original.GetService(type);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool Enabled = true, Hold;
        internal string ReturnedLibrary = "12";
        internal string? MovieQuery, MembershipQuery;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; object body;
            if (path.EndsWith("/capabilities")) body = new { box_sets = true };
            else if (path.EndsWith("/libraries")) body = new { items = new[] { new { id = 2, box_sets_enabled = true }, new { id = 12, box_sets_enabled = Enabled } } };
            else if (path.EndsWith("/box-set"))
            {
                MembershipQuery = request.RequestUri.Query;
                body = new { box_set = new { id = "7", library_id = ReturnedLibrary, title = "Discovered films", movie_count = 2, items = new[] {
                    new { content_id = "film", type = "movie", title = "Unscoped film" }, new { content_id = "other", type = "movie", title = "Other film" } } } };
                if (Hold) { Started.TrySetResult(); await Release.Task; }
            }
            else if (path.EndsWith("/items/film"))
            { MovieQuery = request.RequestUri.Query; body = new { content_id = "film", type = "movie", title = "Unscoped film", user_state = new { } }; }
            else body = new { items = Array.Empty<object>() };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body)) };
        }
    }
}
