using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
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

internal static class BoxSetNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var originalServices = SiloPlayer.App.Services;
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        Type? routeType = null; object? routeArgs = null;
        var navigation = new NavigationService { Frame = new Frame(), NavigationRequestHandler = (type, args) => { routeType = type; routeArgs = args; return true; } };
        var fixtureServices = new NavigationServices(originalServices, navigation);
        servicesField.SetValue(null, fixtureServices);
        try
        {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://boxsets-native.invalid"); api.SetProfile("fixture");
        var catalog = new CatalogApi(api); fixtureServices.Catalog = catalog; fixtureServices.Client = api;
        var page = new BoxSetPage(catalog, api) { Width = 960, Height = 700 };
        parent.Children.Add(page);
        await page.ViewModel.LoadAsync("7", "2"); await Task.Delay(120); page.UpdateLayout();
        Check(page.ViewModel.Detail?.Items.Select(x => x.ContentId).SequenceEqual(new[] { "first", "second", "third" }) == true, "server release order");
        var play = Descendants(page).OfType<Button>().Single(x => x.Name == "BoxSetPlay");
        Check(((TextBlock)play.Content).Text == "Continue · Second" && play.IsEnabled, "partial-watched primary action");
        var sent = ""; await page.ViewModel.PlayAsync((id, library, owner, ct) => { sent = id + ":" + library; return Task.CompletedTask; });
        Check(sent == "second:12", "normal member playback with returned library");
        var aggregate = new BoxSetCard(); aggregate.SetSummary(page.ViewModel.Detail!); parent.Children.Add(aggregate); await Task.Delay(50);
        Check(AutomationProperties.GetName(aggregate).Contains("3 films") && Descendants(aggregate).OfType<ProgressBar>().Single().Value == 1, "aggregate accessibility/progress");
        Check(Descendants(aggregate).OfType<Button>().Count() == 0, "aggregate has no nested title menus");
        Check(aggregate.Focus(FocusState.Keyboard), "aggregate keyboard focus");
        var invoked = false; aggregate.Invoked += _ => invoked = true;
        ((IInvokeProvider)new ButtonAutomationPeer(aggregate).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(50);
        Check(invoked, "aggregate keyboard/automation invocation"); await Capture(aggregate, "boxset-card.png"); parent.Children.Remove(aggregate);
        var members = Descendants(page).OfType<PosterCard>().ToArray();
        Check(members.Length == 3 && members.All(x => MediaNavigationContext.LibraryId(x) == 12), "set member navigation scope");
        typeof(PosterCard).GetMethod("ActivateCard", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(members[1], null);
        Check(routeType == typeof(ItemDetailPage) && routeArgs is ItemDetailNavigationArgs { ContentId: "second", LibraryId: 12 }, "actual member navigation destination");
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Receive(new SiloPlayer.Messaging.PlaybackProgressUpdated("first", 100, 100, true) { Persisted = persisted.Task });
        await page.ViewModel.RefreshAsync(); var beforePersist = wire.Reads; persisted.TrySetResult();
        for (var i = 0; i < 100 && wire.Reads == beforePersist; i++) await Task.Delay(10);
        Check(wire.Reads > beforePersist, "final playback write survives same-visit refresh");
        await Capture(page, "boxset-wide.png");
        page.Width = 540; page.Height = 600; page.UpdateLayout(); await Task.Delay(100);
        Check(Descendants(page).OfType<TextBlock>().Where(x => x.Text == wire.Title).All(x => x.ActualWidth <= 540), "compact wrapped hero");
        await Capture(page, "boxset-compact.png");
        wire.Played = 3; await page.ViewModel.RefreshAsync(); await Task.Delay(80);
        Check(((TextBlock)Descendants(page).OfType<Button>().Single(x => x.Name == "BoxSetPlay").Content).Text == "Play First", "all-played action wording");
        sent = ""; await page.ViewModel.PlayAsync((id, library, owner, ct) => { sent = id; return Task.CompletedTask; }); Check(sent == "first", "all-played first movie");
        wire.Status = HttpStatusCode.ServiceUnavailable; await page.ViewModel.RefreshAsync();
        Check(Descendants(page).OfType<Button>().Any(x => (x.Content as string) == "Try again"), "transient retry control");
        wire.Status = HttpStatusCode.NotFound; await page.ViewModel.RefreshAsync();
        Check(Descendants(page).OfType<TextBlock>().Any(x => x.Text == "Box set unavailable") && !Descendants(page).OfType<Button>().Any(x => (x.Content as string) == "Try again"), "unavailable distinct from retry");
        wire.Status = HttpStatusCode.OK; wire.Empty = true; await page.ViewModel.RefreshAsync();
        Check(!Descendants(page).OfType<Button>().Single(x => x.Name == "BoxSetPlay").IsEnabled, "empty set is not playable");
        parent.Children.Remove(page); await Task.Delay(30); var departedReads = wire.Reads; await page.ViewModel.RefreshAsync(); Check(wire.Reads == departedReads, "departure cannot restart queued refresh");
        sent = ""; await page.ViewModel.PlayAsync((id, _, _, _) => { sent = id; return Task.CompletedTask; }); Check(sent == "", "departure retires primary action");

        // Actual movie-detail row uses the real controls and returned-library tags.
        var detail = new ItemDetailPage { Width = 960, Height = 700 }; parent.Children.Add(detail); await Task.Delay(80);
        detail.ViewModel.Item = new MediaItemDetail { ContentId = "second", Type = "movie", Title = "Current film" };
        var set = new BoxSetDetail { Id = "7", LibraryId = "12", Title = wire.Title, MovieCount = 3, Items = [new() { ContentId = "first", Type = "movie", Title = "First" }, new() { ContentId = "second", Type = "movie", Title = "Second" }, new() { ContentId = "third", Type = "movie", Title = "Third" }] };
        for (var i = 3; i < 9; i++) set.Items.Add(new MediaItem { ContentId = "later-" + i, Type = "movie", Title = "Later " + i });
        typeof(ItemDetailViewModel).GetProperty("BoxSet")!.SetValue(detail.ViewModel, set);
        typeof(ItemDetailPage).GetMethod("BuildBoxSetRow", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(detail, null);
        detail.UpdateLayout(); await Task.Delay(120);
        var row = (StackPanel)detail.FindName("BoxSetMovies"); var scroller = (ScrollViewer)detail.FindName("BoxSetScroller");
        Check(row.Children.Count == 9 && Descendants(row).OfType<PosterCard>().Take(3).Select(x => x.MediaItem!.ContentId).SequenceEqual(new[] { "first", "second", "third" }), "movie row release order");
        Check(scroller.HorizontalOffset > 0, "current-film initial carousel position");
        var marker = Descendants(row).OfType<TextBlock>().Single(x => x.Text == "This film"); var marked = (Grid)((Border)marker.Parent).Parent;
        Check(marked.Children.OfType<PosterCard>().Single().MediaItem!.ContentId == "second" && marker.ActualWidth <= marked.ActualWidth, "current-film marker within correct card");
        Check(Descendants(row).OfType<PosterCard>().All(x => MediaNavigationContext.LibraryId(x) == 12), "movie row uses returned set library");
        var sections = (StackPanel)detail.FindName("DetailContentPanel");
        var order = new[] { "CastSection", "ExtrasSection", "TrailersSection", "BoxSetSection", "SimilarSection", "MediaLocationsSection" }.Select(name => sections.Children.IndexOf((UIElement)detail.FindName(name))).ToArray();
        Check(order.SequenceEqual(order.OrderBy(x => x)), "official movie section order");
        var viewAll = Descendants((DependencyObject)detail.FindName("BoxSetSection")).OfType<Button>().Single(x => (x.Content as string) == "View all");
        ((IInvokeProvider)new ButtonAutomationPeer(viewAll).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(30);
        Check(routeType == typeof(BoxSetPage) && routeArgs is BoxSetNavigationArgs { Id: "7", LibraryId: "12" }, "actual View all destination");
        await Capture((FrameworkElement)detail.FindName("BoxSetSection"), "movie-boxset-row.png");
        detail.Width = 540; detail.UpdateLayout(); await Task.Delay(100);
        Check(((FrameworkElement)detail.FindName("BoxSetSection")).ActualWidth <= 540 && ((TextBlock)detail.FindName("BoxSetTitle")).ActualWidth + viewAll.ActualWidth <= 540, "compact row heading and View all bounds");
        await Capture((FrameworkElement)detail.FindName("BoxSetSection"), "movie-boxset-row-compact.png");
        var card = new PosterCard { MediaItem = new MediaItem { Type = "box_set", ContentId = "box_set:7", BoxSet = set } }; parent.Children.Add(card); await Task.Delay(80);
        Check(card.Content is BoxSetCard, "shared aggregate renderer dispatch");
        routeType = null; routeArgs = null;
        ((IInvokeProvider)new ButtonAutomationPeer((BoxSetCard)card.Content).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(30);
        Check(routeType == typeof(BoxSetPage) && routeArgs is BoxSetNavigationArgs { Id: "7", LibraryId: "12" }, "actual aggregate destination");
        Check(MediaItemMenu.Build(card.MediaItem).Items.Count == 0, "aggregate menu boundary");
        Check(ItemMaintenanceActionPolicy.Resolve(true, "box_set") == default, "aggregate maintenance boundary");
        // Exercise Frame activation and the production parameterless destination, beyond route interception.
        var destinationFrame = navigation.Frame!; destinationFrame.Width = 540; destinationFrame.Height = 600;
        parent.Children.Add(destinationFrame); navigation.NavigationRequestHandler = null;
        Check(navigation.Navigate<BoxSetPage>(new BoxSetNavigationArgs("7", "12")), "actual Frame box-set navigation");
        for (var i = 0; i < 100 && (destinationFrame.Content as BoxSetPage)?.ViewModel.Detail == null; i++) await Task.Delay(10);
        Check(destinationFrame.Content is BoxSetPage opened && opened.ViewModel.Detail is { Id: "7", LibraryId: "12" }, "activated dedicated destination loads scoped data");
        destinationFrame.Content = null; parent.Children.Remove(destinationFrame);
        card.MediaItem = new MediaItem { Type = "movie", ContentId = "ordinary", Title = "Ordinary film" }; await Task.Delay(80);
        Check(card.Content is not BoxSetCard && card.IsTabStop, "recycled aggregate restores ordinary card/focus");
        card.MediaItem = null; Check(card.Content is not BoxSetCard, "recycled empty card clears aggregate");
        parent.Children.Remove(card); detail.ViewModel.CancelPendingLoads(); parent.Children.Remove(detail);
        Program.Log("PASS: box-set hero/primary action, response library, progress/fallback, keyboard/automation, compact/wide captures, failure/retry/unavailable/empty/departure, actual movie row/order/marker/member/View all destinations and recycled card.");
        }
        finally { parent.Children.Clear(); await Task.Delay(100); servicesField.SetValue(null, originalServices); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i); yield return child; foreach(var descendant in Descendants(child)) yield return descendant; }
    }
    private static void Check(bool value,string message) { if(!value) throw new InvalidOperationException("Box-set native: " + message); }
    private static async Task Capture(FrameworkElement element,string name)
    {
        element.UpdateLayout(); var bitmap=new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        var pixels=(await bitmap.GetPixelsAsync()).ToArray();
        File.WriteAllBytes(Path.Combine(Program.ResultDirectory,name), []);
        var file=await StorageFile.GetFileFromPathAsync(Path.Combine(Program.ResultDirectory,name));
        using var stream=await file.OpenAsync(FileAccessMode.ReadWrite); var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels); await encoder.FlushAsync();
    }
    private sealed class NavigationServices(IServiceProvider original, NavigationService navigation) : IServiceProvider
    {
        internal CatalogApi? Catalog; internal SiloApiClient? Client;
        public object? GetService(Type type) => type == typeof(NavigationService) ? navigation : type == typeof(CatalogApi) && Catalog != null ? Catalog : type == typeof(SiloApiClient) && Client != null ? Client : original.GetService(type);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal int Reads;
        internal int Played=1; internal bool Empty; internal HttpStatusCode Status=HttpStatusCode.OK;
        internal string Title="The Very Long Adventure Box Set";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath; if(path.Contains("box-sets/")) Reads++; object body;
            if(path.EndsWith("capabilities")) body=new { box_sets=true };
            else if(path.EndsWith("libraries")) body=new { items=new[] { new { id=2,box_sets_enabled=true }, new { id=12,box_sets_enabled=true } } };
            else body=new { id="7",library_id="12",title=Title,overview="A synthetic release-ordered collection with missing artwork.",movie_count=Empty?0:3,played_count=Played,first_year=2001,last_year=2011,items=Empty?Array.Empty<object>():new object[] { new { content_id="first",type="movie",title="First",user_state=new { played=true } },new { content_id="second",type="movie",title="Second",user_state=new { played=Played==3 } },new { content_id="third",type="movie",title="Third",user_state=new { played=Played==3 } } } };
            return Task.FromResult(new HttpResponseMessage(path.Contains("box-sets/")?Status:HttpStatusCode.OK) { Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(body)) });
        }
    }
}
