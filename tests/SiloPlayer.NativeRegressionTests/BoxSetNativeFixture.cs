using System.Net;
using CommunityToolkit.Mvvm.Messaging;
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
    internal static async Task RunAsync(StackPanel parent, bool invalidMemberChecks = true)
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
        var back = Descendants(page).OfType<Button>().Single(x => x.Name == "BoxSetBack");
        Check(AutomationProperties.GetName(back) == "Go back" && back.Focus(FocusState.Keyboard), "back control has an accessible name and keyboard focus");
        ((IInvokeProvider)new ButtonAutomationPeer(back).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(30);
        Check(routeType == typeof(HomePage), "back control falls back to Home without history");
        var backRequests = 0;
        navigation.Frame!.BackStack.Add(new Microsoft.UI.Xaml.Navigation.PageStackEntry(typeof(ItemDetailPage), null, null));
        navigation.BackNavigationRequestHandler = () => { backRequests++; return true; };
        ((IInvokeProvider)new ButtonAutomationPeer(back).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(30);
        Check(backRequests == 1, "back control uses shell back navigation when history exists");
        navigation.Frame.BackStack.Clear(); navigation.BackNavigationRequestHandler = null;
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
        Check(members[1].Focus(FocusState.Keyboard), "member receives keyboard focus before background refresh");
        var retirementSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementCompleted = false;
        var replacement = page.ViewModel.PlayAsync(async (_, _, _, ct) =>
        {
            // The normal player publishes the retiring member's final-save task while
            // its replacement start is still awaiting the session/watch read.
            page.Receive(new SiloPlayer.Messaging.PlaybackProgressUpdated("second", 40, 100, false) { Persisted = retirementSave.Task });
            await releaseStart.Task.WaitAsync(ct);
            replacementCompleted = true;
        });
        var beforeRetirement = wire.Reads; retirementSave.TrySetResult();
        for (var i = 0; i < 100 && (wire.Reads == beforeRetirement || page.ViewModel.IsLoading); i++) await Task.Delay(10);
        Check(wire.Reads > beforeRetirement, "retiring member triggers a read during the held replacement start");
        releaseStart.TrySetResult(); await replacement;
        Check(replacementCompleted, "retirement refresh does not cancel the replacement start");
        Check(Descendants(page).OfType<PosterCard>().SequenceEqual(members) && members[1].FocusState == FocusState.Keyboard
            && ReferenceEquals(play, Descendants(page).OfType<Button>().Single(x => x.Name == "BoxSetPlay")), "refresh preserves hero, member controls and keyboard focus");
        var unrelatedReads = wire.Reads;
        page.Receive(new SiloPlayer.Messaging.MediaSurfaceChanged(SiloPlayer.Messaging.MediaSurfaceChangeKind.WatchedMarked, "unrelated"));
        await Task.Delay(50); Check(wire.Reads == unrelatedReads, "unrelated watched messages do not refresh the page");
        await Task.Run(catalog.InvalidateLibraryCache);
        for (var i = 0; i < 100 && (wire.Reads == unrelatedReads || page.ViewModel.IsLoading); i++) await Task.Delay(10);
        Check(wire.Reads > unrelatedReads && page.ViewModel.Detail != null, "off-thread library invalidation refreshes on the UI dispatcher");
        await Capture(page, "boxset-wide.png");
        page.Width = 540; page.Height = 600; page.UpdateLayout(); await Task.Delay(100);
        Check(Descendants(page).OfType<TextBlock>().Where(x => x.Text == wire.Title).All(x => x.ActualWidth <= 540), "compact wrapped hero");
        await Capture(page, "boxset-compact.png");
        wire.Played = 3; await page.ViewModel.RefreshAsync(); await Task.Delay(80);
        Check(((TextBlock)Descendants(page).OfType<Button>().Single(x => x.Name == "BoxSetPlay").Content).Text == "Play First", "all-played action wording");
        sent = ""; await page.ViewModel.PlayAsync((id, library, owner, ct) => { sent = id; return Task.CompletedTask; }); Check(sent == "first", "all-played first movie");
        var lastSuccessfulDetail = page.ViewModel.Detail;
        wire.Status = HttpStatusCode.ServiceUnavailable; await page.ViewModel.RefreshAsync();
        Check(ReferenceEquals(page.ViewModel.Detail, lastSuccessfulDetail) && Descendants(page).OfType<PosterCard>().SequenceEqual(members), "background failure preserves loaded content and cards");
        Check(Descendants(page).OfType<Button>().Any(x => (x.Content as string) == "Try again" && x.Visibility == Visibility.Visible), "transient retry control");
        wire.Status = HttpStatusCode.NotFound; await page.ViewModel.RefreshAsync();
        Check(Descendants(page).OfType<TextBlock>().Any(x => x.Text == "Box set unavailable") && !Descendants(page).OfType<Button>().Any(x => (x.Content as string) == "Try again"), "unavailable distinct from retry");
        Check(Descendants(page).OfType<Button>().Contains(back), "unavailable state retains the back control");
        wire.Status = HttpStatusCode.OK; wire.Empty = true; await page.ViewModel.RefreshAsync();
        Check(!Descendants(page).OfType<Button>().Single(x => x.Name == "BoxSetPlay").IsEnabled, "empty set is not playable");
        parent.Children.Remove(page); await Task.Delay(30); var departedReads = wire.Reads; await page.ViewModel.RefreshAsync(); Check(wire.Reads == departedReads, "departure cannot restart queued refresh");
        sent = ""; await page.ViewModel.PlayAsync((id, _, _, _) => { sent = id; return Task.CompletedTask; }); Check(sent == "", "departure retires primary action");

        wire.CapabilityStatus = HttpStatusCode.ServiceUnavailable;
        var faultPage = new BoxSetPage(new CatalogApi(api), api) { Width = 540, Height = 600 }; parent.Children.Add(faultPage);
        await faultPage.ViewModel.LoadAsync("7", "12");
        for (var i = 0; i < 100 && !faultPage.IsLoaded; i++) await Task.Delay(10);
        faultPage.UpdateLayout();
        Check(faultPage.IsLoaded && faultPage.ViewModel.Failure == DetailFailureKind.Transient, "capability fault reaches a loaded transient state");
        Program.Log($"TRACE: capability fault page loaded={faultPage.IsLoaded}, failure={faultPage.ViewModel.Failure}, body={string.Join(',', faultPage.Body.Children.Select(x => x.GetType().Name))}, visual text={string.Join(',', Descendants(faultPage).OfType<TextBlock>().Select(x => x.Text))}.");
        Check(Descendants(faultPage).OfType<TextBlock>().Any(x => x.Text == "Could not load box set")
            && Descendants(faultPage).OfType<Button>().Any(x => (x.Content as string) == "Try again")
            && Descendants(faultPage).OfType<Button>().Any(x => x.Name == "BoxSetBack"), "capability fault has retry and back rather than terminal unavailable");
        wire.CapabilityStatus = HttpStatusCode.OK; await faultPage.ViewModel.RefreshAsync(); Check(faultPage.ViewModel.Detail != null, "capability fault recovers on retry");
        parent.Children.Remove(faultPage);

        // Actual movie-detail row uses the real controls and returned-library tags.
        var detail = new ItemDetailPage { Width = 960, Height = 700 }; parent.Children.Add(detail); await Task.Delay(80);
        detail.ViewModel.Item = new MediaItemDetail { ContentId = "second", Type = "movie", Title = "Current film" };
        var sections = (StackPanel)detail.FindName("DetailContentPanel");
        var boxSetSection = (UIElement)detail.FindName("BoxSetSection");
        var originalSections = sections.Children.Where(section => !ReferenceEquals(section, boxSetSection)).ToArray();
        var set = new BoxSetDetail { Id = "7", LibraryId = "12", Title = wire.Title, MovieCount = 3, Items = [new() { ContentId = "first", Type = "movie", Title = "First" }, new() { ContentId = "second", Type = "movie", Title = "Second" }, new() { ContentId = "third", Type = "movie", Title = "Third" }] };
        if (invalidMemberChecks)
        {
            set.Items.Add(new MediaItem { Type = "movie", ContentId = "", Title = "Invalid empty movie" });
            set.Items.Add(new MediaItem { Type = "box_set", ContentId = "box_set:invalid", Title = "Aggregate is not a member" });
        }
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
        Check(sections.Children.Where(section => !ReferenceEquals(section, boxSetSection)).SequenceEqual(originalSections),
            "box-set membership preserves every unrelated detail section's original order");
        Check(sections.Children.IndexOf(boxSetSection) == sections.Children.IndexOf((UIElement)detail.FindName("SimilarSection")) - 1,
            "box-set row sits immediately before recommendations without moving media locations or metadata");
        var rowCards = Descendants(row).OfType<PosterCard>().ToArray();
        Check(rowCards[1].Focus(FocusState.Keyboard), "movie row card receives keyboard focus");
        var sectionUnloads = 0;
        foreach (var section in sections.Children.OfType<FrameworkElement>()) section.Unloaded += (_, _) => sectionUnloads++;
        var updatedSet = System.Text.Json.JsonSerializer.Deserialize<BoxSetDetail>(System.Text.Json.JsonSerializer.Serialize(set))!;
        updatedSet.PlayedCount = 2;
        typeof(ItemDetailViewModel).GetProperty("BoxSet")!.SetValue(detail.ViewModel, updatedSet);
        typeof(ItemDetailPage).GetMethod("BuildBoxSetRow", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(detail, null);
        await Task.Delay(50);
        Check(Descendants(row).OfType<PosterCard>().SequenceEqual(rowCards) && rowCards[1].FocusState == FocusState.Keyboard && sectionUnloads == 0,
            "same-membership refresh retains row cards, focus and mounted major sections");
        var viewAll = Descendants((DependencyObject)detail.FindName("BoxSetSection")).OfType<Button>().Single(x => (x.Content as string) == "View all");
        ((IInvokeProvider)new ButtonAutomationPeer(viewAll).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(30);
        Check(routeType == typeof(BoxSetPage) && routeArgs is BoxSetNavigationArgs { Id: "7", LibraryId: "12" }, "actual View all destination");
        await Capture((FrameworkElement)detail.FindName("BoxSetSection"), "movie-boxset-row.png");
        detail.Width = 540; detail.UpdateLayout(); await Task.Delay(100);
        Check(((FrameworkElement)detail.FindName("BoxSetSection")).ActualWidth <= 540 && ((TextBlock)detail.FindName("BoxSetTitle")).ActualWidth + viewAll.ActualWidth <= 540, "compact row heading and View all bounds");
        await Capture((FrameworkElement)detail.FindName("BoxSetSection"), "movie-boxset-row-compact.png");
        var mountedOrder = sections.Children.ToArray();
        typeof(ItemDetailViewModel).GetProperty("BoxSet")!.SetValue(detail.ViewModel, null);
        typeof(ItemDetailPage).GetMethod("BuildBoxSetRow", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(detail, null);
        await Task.Delay(50);
        Check(sections.Children.SequenceEqual(mountedOrder) && ((FrameworkElement)boxSetSection).Visibility == Visibility.Collapsed
            && sections.Children.Where(section => !ReferenceEquals(section, boxSetSection)).SequenceEqual(originalSections),
            "clearing membership leaves the original movie layout intact");
        var card = new PosterCard { MediaItem = new MediaItem { Type = "box_set", ContentId = "box_set:7", BoxSet = set } }; parent.Children.Add(card); await Task.Delay(80);
        Check(card.Content is BoxSetCard, "shared aggregate renderer dispatch");
        routeType = null; routeArgs = null;
        ((IInvokeProvider)new ButtonAutomationPeer((BoxSetCard)card.Content).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(30);
        Check(routeType == typeof(BoxSetPage) && routeArgs is BoxSetNavigationArgs { Id: "7", LibraryId: "12" }, "actual aggregate destination");
        Check(MediaItemMenu.Build(card.MediaItem).Items.Count == 0, "aggregate menu boundary");
        Check(ItemMaintenanceActionPolicy.Resolve(true, "box_set") == default, "aggregate maintenance boundary");
        card.MediaItem = new MediaItem { Type = "box_set", ContentId = "box_set:missing", Title = "Missing summary" };
        Check(card.Visibility == Visibility.Collapsed && card.Content == null && !card.IsTabStop, "missing aggregate summary is suppressed rather than a blank button");
        Check((bool)typeof(PosterCard).GetMethod("ActivateCard", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(card, null)! == false, "missing aggregate destination does not consume activation");
        card.MediaItem = new MediaItem { Type = "box_set", ContentId = "box_set:7", BoxSet = set };
        Check(card.Visibility == Visibility.Visible && card.Content is BoxSetCard, "recycled suppressed aggregate restores a valid card");
        // Exercise Frame activation and the production parameterless destination, beyond route interception.
        var destinationFrame = navigation.Frame!; destinationFrame.Width = 540; destinationFrame.Height = 600;
        parent.Children.Add(destinationFrame); navigation.NavigationRequestHandler = null; wire.Empty = false; wire.Played = 1;
        Program.Log("TRACE: before actual Frame box-set activation.");
        Check(navigation.Navigate<BoxSetPage>(new BoxSetNavigationArgs("7", "12")), "actual Frame box-set navigation");
        for (var i = 0; i < 100 && (destinationFrame.Content is not BoxSetPage arriving || !arriving.IsLoaded || arriving.ViewModel.Detail == null); i++) await Task.Delay(10);
        Check(destinationFrame.Content is BoxSetPage opened && opened.ViewModel.Detail is { Id: "7", LibraryId: "12" }, "activated dedicated destination loads scoped data");
        Program.Log("TRACE: actual Frame box-set activation succeeded.");
        var activated = (BoxSetPage)destinationFrame.Content;
        Check(activated.IsLoaded && activated.ViewModel.IsCurrent, "activated destination is loaded and owns the playback notification");
        wire.Hold = true; wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var heldDetail = activated.ViewModel.Detail;
        var heldCards = Descendants(activated).OfType<PosterCard>().ToArray();
        var heldRefresh = activated.ViewModel.RefreshAsync(); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(ReferenceEquals(activated.ViewModel.Detail, heldDetail) && Descendants(activated).OfType<PosterCard>().SequenceEqual(heldCards), "held read preserves public detail and mounted cards");
        var duringReadPersisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Task.Run(() => WeakReferenceMessenger.Default.Send(new SiloPlayer.Messaging.PlaybackProgressUpdated("first", 100, 100, true) { Persisted = duringReadPersisted.Task }));
        wire.Hold = false; wire.Release.TrySetResult(); await heldRefresh;
        var duringReadCount = wire.Reads; wire.Played = 3; await Task.Run(() => duringReadPersisted.TrySetResult());
        for (var i = 0; i < 100 && (wire.Reads == duringReadCount || activated.ViewModel.Detail?.PlayedCount != 3); i++) await Task.Delay(10);
        Check(wire.Reads > duringReadCount && activated.ViewModel.Detail?.PlayedCount == 3, "message during held read must requery after final playback write");
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
        internal bool Hold;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Reads;
        internal int Played=1; internal bool Empty; internal HttpStatusCode Status=HttpStatusCode.OK, CapabilityStatus=HttpStatusCode.OK;
        internal string Title="The Very Long Adventure Box Set";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath; if(path.Contains("box-sets/")) { Reads++; Started.TrySetResult(); if(Hold) await Release.Task; } object body;
            if(path.EndsWith("capabilities")) body=new { box_sets=true };
            else if(path.EndsWith("libraries")) body=new { items=new[] { new { id=2,box_sets_enabled=true }, new { id=12,box_sets_enabled=true } } };
            else body=new { id="7",library_id="12",title=Title,overview="A synthetic release-ordered collection with missing artwork.",movie_count=Empty?0:3,played_count=Played,first_year=2001,last_year=2011,items=Empty?Array.Empty<object>():new object[] { new { content_id="first",type="movie",title="First",user_state=new { played=true } },new { content_id="second",type="movie",title="Second",user_state=new { played=Played==3 } },new { content_id="third",type="movie",title="Third",user_state=new { played=Played==3 } } } };
            return new HttpResponseMessage(path.Contains("box-sets/")?Status:path.EndsWith("capabilities")?CapabilityStatus:HttpStatusCode.OK) { Content=new StringContent(System.Text.Json.JsonSerializer.Serialize(body)) };
        }
    }
}
