using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class NotInterestedNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, Window window)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://not-interested-native.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("viewer");
        var catalog = new CatalogApi(client); var settingsApi = new SettingsApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "settings"));
        var theme = new ThemeService(settings, settingsApi);
        var vm = new SettingsViewModel(settingsApi, catalog, new AuthApi(client), new HistoryImportApi(client), new WatchProvidersApi(client), auth, theme, settings, new AccessibilityService(settings, theme));
        var home = new HomeViewModel(new HomeApi(client), auth);
        var toast = new ToastService(); var interest = NotInterestedActions.CreateService(catalog);
        var detail = new ItemDetailViewModel(catalog, new ItemDetailPrefetchCache((id, ct) => catalog.GetItemDetailAsync(id, ct)));
        var recommendationsApi = new RecommendationsApi(client);
        var forYou = new RecommendationsViewModel(catalog, recommendationsApi);
        var expanded = new RecommendationSectionViewModel(recommendationsApi);
        using var events = new EventChannelClient(client, auth);
        using var owned = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(catalog)
            .AddSingleton(settingsApi).AddSingleton(settings).AddSingleton(vm).AddSingleton(theme)
            .AddSingleton(toast).AddSingleton(interest).AddSingleton(detail).AddSingleton(home).AddSingleton(events)
            .AddSingleton(forYou).AddSingleton(expanded)
            .AddSingleton(new MediaMaintenanceApi(client)).AddSingleton(new CardOverlayService(settingsApi))
            .AddSingleton(new UICustomizationService(settingsApi)).BuildServiceProvider();
        field.SetValue(null, new Services(owned, previous));
        var toasts = new ToastContainer(); var owner = new Button { Content = "Native menu owner" };
        var homePage = new HomePage { Width = 1000, Height = 850 };
        SettingsPage? page = null; ItemDetailPage? detailPage = null;
        Grid? discoveryHost = null;
        try
        {
            parent.Children.Add(toasts); parent.Children.Add(owner); toast.Register(toasts, parent.DispatcherQueue);
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_INTEREST_FINAL_CASE") is { Length: > 0 } finalCase)
            { await FinalTimeouts(parent, toasts, owner, wire, catalog, client, finalCase); return; }
            wire.Marked = true;
            parent.Children.Add(homePage);
            await Until(() => home.Sections.Count == 3 && home.Sections.All(section => section.LoadCompleted));
            var recommended = home.Sections.Single(section => section.Id == "rec");
            Require(recommended.Items.All(card => card.ContentId != "movie:one"), "initial empty Home slot filters fresh marked DTOs before mounting");
            wire.Marked = false;
            WeakReferenceMessenger.Default.Send(new NotInterestedChange(client.CaptureContext(), "movie:one", false));
            await Until(() => recommended.Items.Any(card => card.ContentId == "movie:one"));
            var saved = home.Sections.Single(section => section.Id == "saved");
            var progress = home.Sections.Single(section => section.Id == "progress");
            var stable = recommended.Items.Single(item => item.ContentId == "keep");
            var stableCard = Descendants(homePage).OfType<PosterCard>().FirstOrDefault(card => card.MediaItem?.ContentId == "keep");
            Require(stableCard != null, "actual mounted Home poster exists");
            var item = recommended.Items.Single(item => item.ContentId == "movie:one");
            var menu = MediaItemMenu.Build(item, owner: owner);
            menu.ShowAt(owner);
            await Until(() => menu.Items.OfType<MenuFlyoutItem>().Any(action => action.Text == "Not Interested" && action.Visibility == Visibility.Visible));
            var action = menu.Items.OfType<MenuFlyoutItem>().Single(action => action.Text == "Not Interested");
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(action).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => wire.Marked && recommended.Items.All(item => item.ContentId != "movie:one"));
            menu.Hide(); await Task.Delay(500);
            Require(saved.Items.Any(item => item.ContentId == "movie:one") && progress.Items.Any(item => item.ContentId == "movie:one"), "saved and progress rows retain the marked title");
            Require(ReferenceEquals(stable, recommended.Items.Single(item => item.ContentId == "keep")) && Descendants(homePage).Contains(stableCard!), "unaffected real Home poster and item remain mounted after refresh");
            Require(!home.ShowUndoBanner, "Not Interested leaves progress dismissal Undo independent");
            var undo = Descendants(toasts).OfType<Button>().First(button => button.Content as string == "Undo"); Invoke(undo);
            await Until(() => !wire.Marked && recommended.Items.Any(item => item.ContentId == "movie:one"));
            Require(wire.Deletes == 1, "actual toast Undo sends captured DELETE");
            Program.Log("PASS: actual poster menu, recommendation-only removal, independent actionable Undo and unchanged mounted Home poster.");

            await interest.ApplyAsync(interest.Capture(recommended.Items.Single(card => card.ContentId == "movie:one"), true));
            wire.Marked = false; // Another client unmarks while this client misses its event.
            client.InvalidateAccessContext();
            await home.CatchUpAfterReconnectAsync(client.CaptureContext());
            await Task.Delay(80);
            Require(recommended.Items.Any(card => card.ContentId == "movie:one"), "authoritative reconnect must recover a missed remote unmark instead of retaining a local override");
            Program.Log("PASS: authoritative reconnect clears an obsolete local Not Interested mark after a missed remote event.");

            // A real detail page has a separate action builder and DTO, not the poster menu.
            detail.Item = await catalog.GetItemDetailAsync("movie:one");
            detailPage = new ItemDetailPage { Width = 1000, Height = 850 }; parent.Children.Add(detailPage);
            Method(detailPage, "BuildMoreFlyout");
            var more = (MenuFlyout)detailPage.FindName("MoreFlyout");
            await Until(() => more.Items.OfType<MenuFlyoutItem>().Any(entry => entry.Text == "Not Interested" && entry.Visibility == Visibility.Visible));
            more.ShowAt(owner);
            var detailAction = more.Items.OfType<MenuFlyoutItem>().Single(entry => entry.Text == "Not Interested");
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(detailAction).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => wire.Marked && detail.Item.UserState!.NotInterested); more.Hide();
            var staleUndo = Descendants(toasts).OfType<Button>().First(button => button.Content as string == "Undo" && button.IsEnabled);
            var deletesBefore = wire.Deletes; auth.SelectProfile("replacement"); Invoke(staleUndo); await Task.Delay(120);
            Require(wire.Deletes == deletesBefore, "stale-profile toast Undo submits no mutation");
            auth.SelectProfile("viewer");
            Program.Log("PASS: real detail action patches detail state and captured toast Undo refuses a replacement profile.");

            // All three actual discovery surfaces stay mounted while another surface marks/unmarks.
            home.SetActive(false); parent.Children.Remove(homePage); await Task.Delay(100);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1200, 1080));
            await interest.ApplyAsync(interest.Capture(new MediaItem { ContentId = "movie:one", Type = "movie" }, false));
            detail.Item = new() { ContentId = "source", Type = "movie", UserState = new() };
            typeof(ItemDetailPage).GetField("_currentContentId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(detailPage, "source");
            parent.Children.Remove(detailPage); await Task.Delay(100);
            discoveryHost = new Grid { Width = 1000, Height = 900 };
            discoveryHost.Children.Add(detailPage);
            var forYouPage = new RecommendationsPage { Width = 1000, Height = 900 }; discoveryHost.Children.Add(forYouPage);
            var sectionPage = new RecommendationSectionPage { Width = 1000, Height = 900 };
            typeof(RecommendationSectionPage).GetField("_args", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(sectionPage, new RecommendationSectionNavigationArgs("genre", "drama", "Drama"));
            discoveryHost.Children.Add(sectionPage); parent.Children.Add(discoveryHost);
            await (Task)Method(detailPage, "LoadSimilarItemsAsync")!;
            await Until(() => forYou.Rows.Count == 1 && forYou.Rows[0].Items.Count == 2 && expanded.Items.Count == 2);
            await Until(() => Descendants(forYouPage).OfType<PosterCard>().Any(card => card.MediaItem?.ContentId == "keep") && Descendants(sectionPage).OfType<PosterCard>().Any(card => card.MediaItem?.ContentId == "keep"));
            var similarKeep = Descendants(detailPage).OfType<RecommendationPosterCard>().Single(card => (card.Tag as MediaItem)?.ContentId == "keep");
            var forYouKeep = Descendants(forYouPage).OfType<PosterCard>().First(card => card.MediaItem?.ContentId == "keep");
            var sectionKeep = Descendants(sectionPage).OfType<PosterCard>().First(card => card.MediaItem?.ContentId == "keep");
            await interest.ApplyAsync(interest.Capture(new MediaItem { ContentId = "movie:one", Type = "movie" }, true));
            await Until(() => detail.SimilarItems.Count == 1 && forYou.Rows[0].Items.Count == 1 && expanded.Items.Count == 1);
            await Task.Delay(120);
            Require(Descendants(detailPage).Contains(similarKeep) && Descendants(forYouPage).Contains(forYouKeep) && Descendants(sectionPage).Contains(sectionKeep), "all mounted discovery surfaces retain the unaffected recommendation controls");
            await interest.ApplyAsync(interest.Capture(new MediaItem { ContentId = "movie:one", Type = "movie" }, false));
            await Until(() => detail.SimilarItems.Count == 2 && forYou.Rows[0].Items.Count == 2 && expanded.Items.Count == 2);
            Require(Descendants(detailPage).Contains(similarKeep) && Descendants(forYouPage).Contains(forYouKeep) && Descendants(sectionPage).Contains(sectionKeep), "discovery restoration also retains unrelated controls");
            wire.ForceEmptyDiscovery = true;
            await interest.ApplyAsync(interest.Capture(new MediaItem { ContentId = "movie:one", Type = "movie" }, true));
            await Until(() => forYou.Rows.Count == 0);
            wire.ForceEmptyDiscovery = false;
            await interest.ApplyAsync(interest.Capture(new MediaItem { ContentId = "movie:one", Type = "movie" }, false));
            await Until(() => forYou.Rows.Count == 1 && forYou.Rows[0].Items.Count == 2);
            Program.Log("PASS: mounted empty For You restores its titles after unmark without navigating or manually reloading.");
            parent.Children.Remove(discoveryHost); await Task.Delay(150); discoveryHost = null; detailPage = null;
            WeakReferenceMessenger.Default.UnregisterAll(forYou); WeakReferenceMessenger.Default.UnregisterAll(expanded); expanded.CancelLoad();
            await interest.ApplyAsync(interest.Capture(new MediaItem { ContentId = "movie:one", Type = "movie" }, true));
            Program.Log("PASS: mounted More Like This, For You and expanded recommendation grid reconcile mark/unmark and retain unaffected actual card controls.");

            home.SetActive(false); parent.Children.Remove(homePage); await Task.Delay(150);
            page = new SettingsPage { Width = 1000, Height = 1050 }; parent.Children.Add(page);
            page.SetShellSidebarHosted(true);
            Method(page, "ShowSettingsDetail", page.FindName("PersonalizeTab"));
            await Until(() => ((StackPanel)page.FindName("NotInterestedRows")).Children.Count == 1);
            var rows = (StackPanel)page.FindName("NotInterestedRows");
            Require(((Button)page.FindName("NotInterestedMore")).Visibility == Visibility.Visible, "Personalize exposes next page");
            wire.FailMore = true; Invoke((Button)page.FindName("NotInterestedMore"));
            await Until(() => ((TextBlock)page.FindName("NotInterestedMoreError")).Visibility == Visibility.Visible);
            Require(rows.Children.Count == 1, "next page error retains existing rows");
            wire.FailMore = false; Invoke((Button)page.FindName("NotInterestedMore"));
            await Until(() => rows.Children.Count == 2);
            Require(wire.Cursors.TakeLast(2).Distinct().Count() == 1, "retry uses same opaque cursor");
            foreach (var width in new[] { 520, 1920, 3440, 3840 })
            {
                var height = width == 1920 ? 1080 : width == 3440 ? 1440 : width == 3840 ? 2160 : 900;
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height)); page.Width = width;
                var panel = (FrameworkElement)page.FindName("NotInterestedPanel");
                panel.UpdateLayout(); await Task.Delay(100);
                Program.Log($"layout width={width} root={parent.XamlRoot.Size.Width} page={page.ActualWidth} panel={panel.ActualWidth}");
                await CaptureAsync(panel, "not-interested-" + width + ".png");
                var longTitle = Descendants(panel).OfType<TextBlock>().First(text => text.Text.StartsWith("Second Series"));
                Require(longTitle.TextTrimming == TextTrimming.CharacterEllipsis && longTitle.IsTextTrimmed, "long title uses actual character ellipsis alongside its action");
                foreach (var row in rows.Children.OfType<Grid>())
                {
                    var titleText = (TextBlock)Descendants(row).OfType<HyperlinkButton>().Single().Content;
                    var metadata = Descendants(row).OfType<TextBlock>().Single(text => text.FontSize == 12);
                    var titleX = titleText.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point()).X;
                    var metadataX = metadata.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point()).X;
                    Require(Math.Abs(titleX - metadataX) < 1, $"title text and year/type metadata share the same left edge (title={titleX}, metadata={metadataX})");
                }
                foreach (var button in Descendants(panel).OfType<Button>().Where(button => button.Content as string == "Show in Recommendations"))
                {
                    var point = button.TransformToVisual(panel).TransformPoint(new Windows.Foundation.Point());
                    Require(point.X >= 0 && point.X + button.ActualWidth <= panel.ActualWidth + 1, "action remains in narrow/wide panel bounds");
                }
            }
            var restore = Descendants(rows).OfType<Button>().First(button => button.Content as string == "Show in Recommendations");
            Invoke(restore); await Until(() => !wire.Marked && rows.Children.Count == 1);
            Program.Log("PASS: native Personalize list, retained rows on paging failure, same-cursor retry, management unmark, and narrow/1080/ultrawide/4K panel captures.");

            wire.Marked = true;
            using var payload = JsonDocument.Parse("""{"profile_id":"viewer","change":"not_interested","content_id":"movie:one","not_interested":true}""");
            interest.ReceiveRealtime("user_state", "user_state.changed", payload.RootElement);
            await Until(() => rows.Children.Count == 1 && Descendants(rows).OfType<TextBlock>().Any(text => text.Text == "First Movie"));
            using var otherProfile = JsonDocument.Parse("""{"profile_id":"other","change":"not_interested","content_id":"movie:one","not_interested":false}""");
            var before = wire.ListCalls; interest.ReceiveRealtime("user_state", "user_state.changed", otherProfile.RootElement); await Task.Delay(100);
            Require(wire.ListCalls == before, "other-profile event does not refresh Personalize");
            Program.Log("PASS: actual user_state.changed payload refreshes visible management state and filters another profile.");

            client.SetBaseUrl("https://unsupported-native.invalid"); wire.Supported = false;
            await (Task)Method(page, "LoadNotInterestedAsync")!;
            Require(((FrameworkElement)page.FindName("NotInterestedPanel")).Visibility == Visibility.Collapsed, "unsupported server omits panel");
            var unsupported = MediaItemMenu.Build(new MediaItem { ContentId = "movie:one", Type = "movie", UserState = new() }, owner: owner);
            unsupported.ShowAt(owner); await Task.Delay(120);
            Require(!unsupported.Items.OfType<MenuFlyoutItem>().Any(entry => entry.Text == "Not Interested" && entry.Visibility == Visibility.Visible), "unsupported server omits menu action"); unsupported.Hide();
            Program.Log("PASS: unsupported capability quietly omits native panel and menu action.");
        }
        finally
        {
            if (page != null) parent.Children.Remove(page);
            if (discoveryHost != null) parent.Children.Remove(discoveryHost);
            if (detailPage != null) parent.Children.Remove(detailPage);
            parent.Children.Remove(homePage); parent.Children.Remove(owner); parent.Children.Remove(toasts);
            await Task.Delay(200); // Unloaded must drain before replacing App.Services/disposal.
            home.SetActive(false); WeakReferenceMessenger.Default.UnregisterAll(home); WeakReferenceMessenger.Default.UnregisterAll(detail);
            WeakReferenceMessenger.Default.UnregisterAll(forYou); WeakReferenceMessenger.Default.UnregisterAll(expanded);
            toast.Unregister(); field.SetValue(null, previous);
        }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static object? Method(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var attempt = 0; attempt < 200; attempt++) { if (ready()) return; await Task.Delay(20); } throw new InvalidOperationException("Not Interested native state timed out."); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element); var pixels = await bitmap.GetPixelsAsync();
        Require(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "panel capture has pixels");
        var folder = await StorageFolder.GetFolderFromPathAsync(Program.ResultDirectory); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
    }
    private static async Task FinalTimeouts(StackPanel parent, ToastContainer toasts, Button owner, Wire wire, CatalogApi catalog, SiloApiClient client, string mode)
    {
        if (mode.StartsWith("list-"))
        {
            var state = new NotInterestedListState(catalog); wire.Marked = true;
            if (mode is "list-cancel" or "list-owner")
            {
                wire.TimeoutPath = "capabilities/not-interested"; wire.HoldTimeout = true;
                var loading = state.LoadAsync(); await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (mode == "list-cancel") state.Cancel(); else client.SetProfile("replacement");
                wire.Release.TrySetResult(); await loading;
                Require(state.Error == null && state.MoreError == null && state.Items.Count == 0, "canceled or retired held read remains quiet without replacement rows");
                Program.Log($"PASS: final Not Interested {mode}: held HTTP timeout after actual cancellation/owner replacement."); return;
            }
            if (mode == "list-cap") wire.TimeoutPath = "capabilities/not-interested";
            if (mode == "list-initial") wire.TimeoutPath = "/api/v2/not-interested";
            await state.LoadAsync();
            if (mode == "list-more")
            {
                wire.TimeoutPath = "/api/v2/not-interested"; await state.LoadMoreAsync();
                Require(state.Items.Count == 1 && state.MoreError != null && state.HasMore, "continuation timeout retains rows and cursor with error");
                wire.TimeoutPath = null; await state.LoadMoreAsync();
                Require(state.Items.Count == 2 && wire.Cursors.TakeLast(2).Distinct().Count() == 1, "continuation retries exact cursor successfully");
            }
            else
            {
                Require(state.Supported && state.Error != null && !state.IsLoading, "capability/initial timeout is visible error with supported Retry");
                wire.TimeoutPath = null; await state.LoadAsync(); Require(state.Items.Count == 1 && state.Error == null, "explicit list Retry recovers");
            }
        }
        else
        {
            var item = new MediaItem { ContentId = "movie:one", Type = "movie", UserState = new() { IsFavorite = true, InWatchlist = true } };
            var menu = MediaItemMenu.Build(item, owner: owner); menu.ShowAt(owner);
            await Until(() => menu.Items.OfType<MenuFlyoutItem>().Any(action => action.Text == "Not Interested" && action.Visibility == Visibility.Visible));
            var action = menu.Items.OfType<MenuFlyoutItem>().Single(action => action.Text == "Not Interested");
            if (mode == "action") wire.TimeoutPath = "not-interested/movie%3Aone";
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(action).GetPattern(PatternInterface.Invoke)).Invoke();
            if (mode == "action")
            {
                await Task.Delay(180);
                Require(Descendants(toasts).OfType<TextBlock>().Any(text => text.Text.Contains("Failed to update Not interested")) && !item.UserState.NotInterested && item.UserState.IsFavorite && item.UserState.InWatchlist, "real action timeout shows error and flag-preserving rollback");
                wire.TimeoutPath = null; await NotInterestedActions.ToggleAsync(item); Require(wire.Marked, "explicit action retry succeeds");
            }
            else
            {
                await Until(() => wire.Marked);
                await Task.Delay(100); wire.TimeoutPath = "not-interested/movie%3Aone";
                var undo = Descendants(toasts).OfType<Button>().First(button => button.Content as string == "Undo" && button.IsEnabled); Invoke(undo);
                await Task.Delay(180);
                Require(Descendants(toasts).OfType<TextBlock>().Any(text => text.Text.Contains("Failed to undo Not interested")) && item.UserState.NotInterested && item.UserState.IsFavorite, "real toast Undo timeout shows error and rolls back");
                wire.TimeoutPath = null; await NotInterestedActions.ToggleAsync(item); Require(!wire.Marked, "explicit restore recovers failed Undo");
            }
            menu.Hide();
        }
        Program.Log($"PASS: final Not Interested {mode}: uncanceled HTTP timeout and explicit recovery.");
    }
    private sealed class Services(IServiceProvider primary, IServiceProvider fallback) : IServiceProvider { public object? GetService(Type type) => primary.GetService(type) ?? fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool Marked, FailMore, ForceEmptyDiscovery, Supported = true;
        internal string? TimeoutPath;
        internal bool HoldTimeout;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Deletes, ListCalls;
        internal readonly List<string> Cursors = [];
        private static object Card(string id, bool marked = false) => new { content_id = id, type = id == "two" ? "series" : "movie", title = id == "movie:one" ? "First Movie" : id == "two" ? "Second Series with a very long title that must truncate before its management action on every supported screen" : "Unchanged Poster", year = 2026, poster_url = new Uri(Path.Combine(Program.AppDirectory, "Assets", "silo-icon-1024.png")).AbsoluteUri,
            user_state = new { played = false, is_favorite = true, in_watchlist = true, not_interested = marked } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri!.Host is "not-interested-native.invalid" or "unsupported-native.invalid", "isolated fixture origin");
            var path = request.RequestUri.AbsolutePath;
            if (TimeoutPath != null && path != "/api/v2/not-interested" && (path.EndsWith(TimeoutPath) || Uri.UnescapeDataString(path).EndsWith(Uri.UnescapeDataString(TimeoutPath))))
            {
                if (HoldTimeout) { Entered.TrySetResult(); await Release.Task; }
                throw new TaskCanceledException("Synthetic HTTP timeout with uncanceled request token");
            }
            if (path == "/api/v2/capabilities/not-interested") return Json(new { state = Supported ? "available" : "unsupported" });
            if (path == "/api/v2/not-interested/movie:one" || path == "/api/v2/not-interested/movie%3Aone")
            {
                await Task.Delay(40, ct);
                if (request.Method == HttpMethod.Put) Marked = true;
                if (request.Method == HttpMethod.Delete) { Marked = false; Deletes++; }
                return new(HttpStatusCode.NoContent);
            }
            if (path == "/api/v2/not-interested")
            {
                ListCalls++; var more = request.RequestUri.Query.Contains("cursor="); Cursors.Add(request.RequestUri.Query);
                if (TimeoutPath == path) throw new TaskCanceledException("Synthetic HTTP timeout with uncanceled request token");
                if (more && FailMore) return Json(new { }, HttpStatusCode.InternalServerError);
                return Json(new { items = more || !Marked ? new[] { Card("two", true) } : new[] { Card("movie:one", true) }, page = new { has_more = !more && Marked, next_cursor = more || !Marked ? null : "opaque +/=" } });
            }
            if (path.EndsWith("/home/layout")) return Json(new { sections = new[] { new { id = "rec", section_type = "recommended_for_you", title = "Recommended" }, new { id = "saved", section_type = "favorites", title = "Favorites" }, new { id = "progress", section_type = "continue_watching", title = "Continue Watching" } } });
            if (path.Contains("/home/sections/"))
            {
                var id = path.Split('/')[5];
                return Json(new { id, section_type = id == "rec" ? "recommended_for_you" : id == "saved" ? "favorites" : "continue_watching", title = id == "rec" ? "Recommended" : id == "saved" ? "Favorites" : "Continue Watching", items = new[] { Card("movie:one", Marked), Card("keep") } }); // Deliberately stale server membership: local filter must persist.
            }
            if (path.StartsWith("/api/v2/catalog/items/")) return Json(new { content_id = "movie:one", type = "movie", title = "First Movie", user_state = new { not_interested = Marked } });
            if (path == "/api/v2/recommendations/discover")
            {
                if (ForceEmptyDiscovery) return Json(new { items = Array.Empty<object>() });
                return Json(new { items = new[] { new { title = "Recommended", type = "recommended_for_you", kind = "genre", key = "drama", items = Marked ? new[] { Card("keep") } : new[] { Card("movie:one"), Card("keep") } } } });
            }
            if (path.Contains("/api/v2/recommendations/section/") || path.Contains("/api/v2/recommendations/similar/"))
                return Json(new { items = Marked ? new[] { Card("keep") } : new[] { Card("movie:one"), Card("keep") } });
            if (path.EndsWith("/settings/manifest")) return Json(new { revision = 19, settings = Array.Empty<object>() });
            if (path.EndsWith("/settings/effective")) return Json(new { values = new { } });
            return Json(new { items = Array.Empty<object>() });
        }
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
