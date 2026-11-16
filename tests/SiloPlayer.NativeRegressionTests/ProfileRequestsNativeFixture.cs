using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

internal static class ProfileRequestsNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(StackPanel parent)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://profile-requests-native.invalid");
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SelectProfile("child", profile: new Profile { Id = "child", RequestsAllowed = false });
        var outer = App.Services; var api = new RequestsApi(client, auth); var vm = new RequestsViewModel(api);
        var navigation = new NavigationService();
        using var services = new ServiceCollection()
            .AddSingleton(new HomeViewModel(new HomeApi(client), auth))
            .AddSingleton(new EventChannelClient(client, auth))
            .AddSingleton(new CatalogApi(client)).AddSingleton(new HomeApi(client)).AddSingleton(new CollectionsApi(client))
            .AddSingleton(new SettingsApi(client)).AddSingleton<WatchlistViewModel>()
            .AddSingleton(new PlaybackApi(client)).AddSingleton<PlayerService>()
            .AddSingleton(new ItemDetailPrefetchCache((id, _) => Task.FromException<MediaItemDetail>(new InvalidOperationException("Unexpected fixture library read"))))
            .AddSingleton(client).AddSingleton(auth).AddSingleton(api).AddSingleton(vm).AddSingleton(navigation)
            .AddSingleton(new UICustomizationService(new SettingsApi(client))).AddSingleton(new CardOverlayService(new SettingsApi(client)))
            .AddSingleton(new ToastService()).AddSingleton(outer.GetRequiredService<ImageService>())
            .AddSingleton(new SettingsService(Path.Combine(Program.ResultDirectory, "profile-request-settings"))).BuildServiceProvider();
        var serviceField = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        serviceField.SetValue(null, services);
        var frame = new Frame { Width = 1000, Height = 800 }; parent.Children.Add(frame); navigation.Frame = frame;
        try
        {
            var reviewCase = Environment.GetEnvironmentVariable("SILO_NATIVE_REQUEST_REVIEW_CASE");
            if (reviewCase == "disabled-title") { await DisabledTitleRouteAsync(frame, navigation, wire, auth, api); return; }
            if (reviewCase == "denied-follow") { await DeniedFollowToggleAsync(frame, navigation, wire, auth); return; }
            if (reviewCase == "retained-bookmark") { await RetainedBookmarkAsync(frame, navigation, wire, client, auth, api, vm); return; }
            navigation.NavigateImmediately(typeof(RequestsPage));
            var page = (RequestsPage)frame.Content;
            await Until(() => page.IsLoaded && vm.MyRequests.Count == 2 && !vm.IsLoadingMine);
            foreach (var name in new[] { "RequestTabs", "SearchBarGrid", "DiscoverySection" })
                Check(((FrameworkElement)page.FindName(name)).Visibility == Visibility.Collapsed, "Blocked hub hides " + name);
            Check(((TextBlock)page.FindName("HubIntroText")).Text.Contains("turned off for this profile"), "Blocked hub explains profile restriction");
            var rows = (StackPanel)page.FindName("MyRequestsList");
            Check(!Descendants(rows).OfType<HyperlinkButton>().Any(), "Own history titles have no discovery links");
            Check(Descendants(rows).OfType<TextBlock>().Any(t => t.Text == "Before the block"), "Own request title and state remain visible");
            Check(!wire.Paths.Any(p => p.Contains("discover") || p.Contains("search")), "Blocked visit never loads discovery or search");
            await MediaParityNativeFixture.CaptureAsync(page, "profile-requests-blocked-wide.png");
            frame.Width = 600; await Task.Delay(80);
            await MediaParityNativeFixture.CaptureAsync(page, "profile-requests-blocked-compact.png");
            Click(Descendants(rows).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Cancel request for Before the block"));
            var dialog = await Dialog(page);
            Click(Descendants(dialog).OfType<Button>().Single(b => b.Name == "PrimaryButton"));
            await Until(() => wire.Cancelled && !vm.IsLoadingMine);
            await Until(() => Descendants(rows).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Remove request for Failed before block"));
            Click(Descendants(rows).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Remove request for Failed before block"));
            dialog = await Dialog(page);
            Click(Descendants(dialog).OfType<Button>().Single(b => b.Name == "PrimaryButton"));
            await Until(() => wire.Removed && vm.MyRequests.Count == 1 && !vm.IsLoadingMine);
            Check(!wire.Paths.Any(p => p.Contains("discover") || p.Contains("search")), "Cancel/remove reconciliation never loads discovery");

            // Exercise empty state on the mounted visit before navigating away.
            await Task.Delay(100);
            wire.Empty = true; vm.InvalidateCache(); await vm.RefreshAsync();
            await Until(() => ((FrameworkElement)page.FindName("MineEmptyPanel")).Visibility == Visibility.Visible);
            Check(((FrameworkElement)page.FindName("MineBrowseButton")).Visibility == Visibility.Collapsed &&
                ((FrameworkElement)page.FindName("MineEmptyHint")).Visibility == Visibility.Collapsed, "Empty blocked history offers no discovery action or hint");

            // Real Frame routes return Home before discovery transport.
            navigation.NavigateImmediately(typeof(RequestDetailPage), new RequestDetailNavigation("movie", 42));
            await Until(() => frame.Content is HomePage);
            navigation.NavigateImmediately(typeof(RequestBrowsePage), new RequestBrowseNavigation("genre", "drama"));
            await Until(() => frame.Content is HomePage);
            Check(!wire.Paths.Any(p => p.Contains("/detail/") || p.Contains("discover")), "Blocked direct title and browse routes return Home without reads");

            foreach (var supported in new[] { true, false })
                await EditorAsync(parent, wire, new AuthApi(client), supported);
            Check(!wire.Paths.Any(p => p.Contains("discover") || p.Contains("search")), "Editor checks do not add discovery loads");
            await DeniedFollowToggleAsync(frame, navigation, wire, auth);
            await ReviewRetainedControlsAsync(frame, navigation, wire, auth, client, api, vm);
            Program.Log("PASS: PROFILE_REQUESTS_COMPLETED native history, cancel/remove, direct-route guards, wide/compact and editor capability/kids/save");
        }
        finally
        {
            await RequestFixtureLifecycle.DetachAsync(frame, () =>
            {
                frame.BackStack.Clear(); frame.ForwardStack.Clear();
                frame.Content = null; parent.Children.Remove(frame);
            });
            serviceField.SetValue(null, outer);
        }
    }

    private static async Task DisabledTitleRouteAsync(Frame frame, NavigationService navigation, Wire wire, AuthService auth, RequestsApi api)
    {
        auth.SelectProfile("child", profile: new Profile { Id = "child", RequestsAllowed = true });
        wire.RequestsEnabled = true;
        navigation.NavigateImmediately(typeof(RequestDetailPage), new RequestDetailNavigation("movie", 42));
        await Until(() => frame.Content is RequestDetailPage detail && detail.IsLoaded &&
            ((FrameworkElement)detail.FindName("LoadingLayer")).Visibility == Visibility.Collapsed);
        wire.RequestsEnabled = false;
        await api.GetStatusAsync();
        try { await Until(() => frame.Content is HomePage); }
        catch (TimeoutException)
        { throw new InvalidOperationException("Reloading disabled Requests status leaves the current title route open instead of returning Home."); }
        Program.Log("PASS: disabling Requests on status reload returns the open title route Home.");
    }

    private static async Task DeniedFollowToggleAsync(Frame frame, NavigationService navigation, Wire wire, AuthService auth)
    {
        var host = (Panel)frame.Parent;
        var toast = new SiloPlayer.Controls.ToastContainer();
        var toastService = App.Services.GetRequiredService<ToastService>();
        host.Children.Add(toast); toastService.Register(toast, frame.DispatcherQueue);
        try
        {
            auth.SelectProfile("child", profile: new Profile { Id = "child", RequestsAllowed = true });
            wire.RequestsEnabled = true; wire.Following = true;
            navigation.NavigateImmediately(typeof(RequestDetailPage), new RequestDetailNavigation("movie", 42));
            await Until(() => frame.Content is RequestDetailPage detail && detail.IsLoaded &&
                ((FrameworkElement)detail.FindName("LoadingLayer")).Visibility == Visibility.Collapsed);
            var page = (RequestDetailPage)frame.Content;
            var follow = Descendants(page).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "follow");
            Click(follow);
            await Until(() => !wire.Following && follow.IsEnabled);
            try { await Until(() => Descendants(page).OfType<Button>().Any(button =>
                AutomationProperties.GetAutomationId(button) == "follow" && AutomationProperties.GetName(button) == "Notify me when available")); }
            catch (TimeoutException)
            { throw new InvalidOperationException("Account-denied viewers lose the follow toggle after a successful Unfollow despite the current WebUI toggle contract."); }
            Program.Log("PASS: account denial keeps the follow toggle visible after a successful Unfollow.");
            wire.RejectFollow = true;
            var writes = wire.Paths.Count(path => path == "PUT /api/v2/requests/follows/movie/42");
            var reads = wire.Paths.Count(path => path.Contains("/requests/detail/"));
            follow = Descendants(page).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "follow");
            Click(follow);
            await Until(() => wire.Paths.Count(path => path == "PUT /api/v2/requests/follows/movie/42") == writes + 1 && follow.IsEnabled &&
                ((StackPanel)toast.FindName("ToastStack")).Children.Count > 0);
            Check(!wire.Following && wire.Paths.Count(path => path.Contains("/requests/detail/")) == reads &&
                Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "follow" &&
                    AutomationProperties.GetName(button) == "Notify me when available"),
                "Follow sends the normal guarded request, shows the server refusal and preserves the unfollowed toggle without a speculative detail read");
        }
        finally { wire.RejectFollow = false; toastService.Unregister(); host.Children.Remove(toast); }
    }

    private static async Task ReviewRetainedControlsAsync(Frame frame, NavigationService navigation, Wire wire,
        AuthService auth, SiloApiClient client, RequestsApi api, RequestsViewModel vm)
    {
        wire.Empty = true; wire.Following = true;
        auth.RefreshSelectedProfile(new Profile { Id = "child", RequestsAllowed = true });
        vm.InvalidateCache(); navigation.NavigateImmediately(typeof(RequestsPage));
        await Until(() => frame.Content is RequestsPage loadedHub && loadedHub.IsLoaded);
        Click((Button)((RequestsPage)frame.Content).FindName("DiscoverTabButton"));
        await Until(() => frame.Content is RequestsPage mountedHub && mountedHub.IsLoaded && vm.DiscoverySections.Count > 0 && !vm.IsLoadingDiscovery);
        var hub = (RequestsPage)frame.Content;
        var explore = Descendants(hub).OfType<Button>().Single(button => AutomationProperties.GetName(button).Contains("Review discoveries"));
        Check(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true), "Fixture reconnect catch-up advances transport context");
        Click(explore);
        await Until(() => frame.Content is RequestBrowsePage mountedBrowse && mountedBrowse.IsLoaded && Descendants(mountedBrowse).OfType<Button>().Any(button => button.Tag is RequestDetailNavigation));
        Check(frame.Content is RequestBrowsePage, "Retained Explore control navigates after reconnect catch-up");
        var browse = (RequestBrowsePage)frame.Content;
        var title = Descendants(browse).OfType<Button>().Single(button => button.Tag is RequestDetailNavigation);
        Check(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true), "Second reconnect retains title control authority");
        Click(title);
        await Until(() => frame.Content is RequestDetailPage mountedDetail && mountedDetail.IsLoaded && Descendants(mountedDetail).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "follow"));
        var detail = (RequestDetailPage)frame.Content;
        var follow = Descendants(detail).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "follow");
        Check(AutomationProperties.GetName(follow) == "Stop notifying me", "Account denial retains existing-follow toggle through normal browse/title controls");
        wire.RequestsEnabled = false; await api.GetStatusAsync();
        var reads = wire.Paths.Count(path => path.Contains("/requests/detail/"));
        await Until(() => frame.Content is HomePage);
        Check(wire.Paths.Count(path => path.Contains("/requests/detail/")) == reads, "Disabled Requests title returns Home without speculative detail reload");
        await MediaParityNativeFixture.CaptureAsync((FrameworkElement)frame.Content, "profile-requests-disabled-title-home.png");

        wire.RequestsEnabled = true; await api.GetStatusAsync();
        vm.InvalidateCache(); navigation.NavigateImmediately(typeof(RequestsPage));
        await Until(() => frame.Content is RequestsPage loadedHub && loadedHub.IsLoaded);
        Click((Button)((RequestsPage)frame.Content).FindName("DiscoverTabButton"));
        await Until(() => frame.Content is RequestsPage current && current.IsLoaded && vm.DiscoverySections.Count > 0 && !vm.IsLoadingDiscovery && Descendants(current).OfType<Button>().Any(button => button.Tag is RequestDetailNavigation));
        hub = (RequestsPage)frame.Content;
        var staleTitle = Descendants(hub).OfType<Button>().Single(button => button.Tag is RequestDetailNavigation);
        client.InvalidateAccessContext(); reads = wire.Paths.Count(path => path.Contains("/requests/detail/"));
        Click(staleTitle); await Task.Delay(100);
        Check(ReferenceEquals(frame.Content, hub) && wire.Paths.Count(path => path.Contains("/requests/detail/")) == reads,
            "Retained title control cannot navigate or read after true access loss");
        auth.RefreshSelectedProfile(new Profile { Id = "child", RequestsAllowed = false });
        await Until(() => ((FrameworkElement)hub.FindName("DiscoverySection")).Visibility == Visibility.Collapsed && !vm.IsLoadingMine);
        auth.SelectProfile("child", profile: new Profile { Id = "child", RequestsAllowed = true });
        Click((Button)hub.FindName("DiscoverTabButton"));
        await Until(() => vm.DiscoverySections.Count > 0 && !vm.IsLoadingDiscovery && Descendants(hub).OfType<Button>().Any(button => button.Tag is RequestDetailNavigation));
        await Until(() => ((FrameworkElement)hub.FindName("DiscoverySection")).Visibility == Visibility.Visible);
        Click(Descendants(hub).OfType<Button>().Single(button => button.Tag is RequestDetailNavigation));
        await Until(() => frame.Content is RequestDetailPage loaded && loaded.IsLoaded);
        auth.RefreshSelectedProfile(new Profile { Id = "child", RequestsAllowed = false });
        await Until(() => frame.Content is HomePage);
        Check(frame.Content is HomePage, "Normal title navigation restored after unblock and mounted profile block redirects Home");

        navigation.NavigateImmediately(typeof(CatalogPage), new CatalogNavigation("watchlist"));
        await Until(() => frame.Content is CatalogPage catalogPage && catalogPage.IsLoaded);
        var catalog = (CatalogPage)frame.Content;
        Check(((FrameworkElement)catalog.FindName("WatchlistTabs")).Visibility == Visibility.Collapsed,
            "Blocked mounted watchlist hides external tabs");
        auth.SelectProfile("child", profile: new Profile { Id = "child", RequestsAllowed = true });
        await Until(() => ((FrameworkElement)catalog.FindName("WatchlistTabs")).Visibility == Visibility.Visible);
        Click((Button)catalog.FindName("ExternalWatchlistTab"));
        await Until(() => ((FrameworkElement)catalog.FindName("ExternalWatchlistScroller")).Visibility == Visibility.Visible &&
            Descendants(catalog).OfType<Button>().Any(button => button.Tag is RequestDetailNavigation));
        Check(((FrameworkElement)catalog.FindName("WatchlistTabs")).Visibility == Visibility.Visible,
            "Mounted watchlist restores external tabs and reachable title controls after unblock");
        title = Descendants(catalog).OfType<Button>().Single(button => button.Tag is RequestDetailNavigation);
        Check(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true), "Watchlist reconnect retires only transport context");
        Click(title);
        await Until(() => frame.Content is RequestDetailPage restored && restored.IsLoaded);
        Check(frame.Content is RequestDetailPage, "Restored external watchlist title remains navigable after reconnect");
    }

    private static async Task EditorAsync(StackPanel parent, Wire wire, AuthApi api, bool supported)
    {
        var editor = typeof(ProfileEditorDialog).GetConstructors(Private).Single().Invoke([parent.XamlRoot,
            new Profile { Id = "child", Name = "Child" }, Array.Empty<Library>(), false, api, false, false, false]);
        typeof(ProfileEditorDialog).GetMethod("ConfigureRequestsPermission", Private)!.Invoke(editor, [supported]);
        T Field<T>(string name) => (T)typeof(ProfileEditorDialog).GetField(name, Private)!.GetValue(editor)!;
        var permission = Field<ToggleSwitch>("_requestsAllowed"); var kids = Field<ToggleSwitch>("_kidsToggle");
        Check(permission.IsOn, "Editor defaults to allowed");
        Check(Field<Border>("_requestsAllowedRow").Visibility == (supported ? Visibility.Visible : Visibility.Collapsed), "Editor follows server capability");
        kids.IsOn = true; Check(!permission.IsOn, "Kids preset blocks untouched Requests");
        permission.IsOn = true; kids.IsOn = false; kids.IsOn = true;
        Check(!permission.IsOn, "Clearing kids resets touched preset before next application");
        permission.IsOn = true; kids.IsOn = false;
        permission.IsOn = false; permission.IsOn = true; kids.IsOn = true;
        Check(permission.IsOn, "Kids preset preserves explicitly touched permission");
        permission.IsOn = false; Field<ToggleSwitch>("_restrictLibrariesToggle").IsOn = false;
        foreach (var image in Descendants(Field<Grid>("_avatarPresetGrid")).OfType<Image>()) image.Source = null;
        var dialog = Field<ContentDialog>("_dialog"); wire.LastProfileBody = null; var showing = dialog.ShowAsync();
        try
        {
            await Until(() => dialog.IsLoaded && Descendants(dialog).OfType<Button>().Any(b => b.Name == "PrimaryButton"));
            var scroll = (ScrollViewer)dialog.Content;
            // Let the dialog finish assigning initial focus before scrolling;
            // otherwise its delayed Name focus brings the form back to the top.
            await Task.Delay(100);
            (supported ? permission : kids).Focus(FocusState.Programmatic);
            await Until(() => scroll.ScrollableHeight > 0 && kids.ActualHeight > 0);
            scroll.UpdateLayout(); scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
            await Task.Delay(150);
            await MediaParityNativeFixture.CaptureAsync(dialog, "profile-requests-editor-" + supported + ".png");
            if (supported)
            {
                var position = permission.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point());
                Program.Log($"TRACE permission viewport: y={position.Y}, height={permission.ActualHeight}, viewport={scroll.ActualHeight}, offset={scroll.VerticalOffset}, end={scroll.ScrollableHeight}");
                Check(position.Y >= 0 && position.Y + permission.ActualHeight <= scroll.ActualHeight,
                    "Allow requests is reachable inside the editor viewport");
            }
            Click(Descendants(dialog).OfType<Button>().Single(b => b.Name == "PrimaryButton"));
            await Until(() => wire.LastProfileBody != null); await showing;
            var body = wire.LastProfileBody!.Value;
            Check(body.TryGetProperty("requests_allowed", out var allowed) == supported, "Editor save omits unsupported permission");
            if (supported) Check(!allowed.GetBoolean(), "Editor saves explicit false permission");
        }
        finally { dialog.Hide(); }
    }

    private static async Task RetainedBookmarkAsync(Frame frame, NavigationService navigation, Wire wire,
        SiloApiClient client, AuthService auth, RequestsApi api, RequestsViewModel vm)
    {
        auth.SelectProfile("child", profile: new Profile { Id = "child", RequestsAllowed = true });
        await api.GetStatusAsync();
        navigation.NavigateImmediately(typeof(RequestsPage));
        await Until(() => frame.Content is RequestsPage page && page.IsLoaded &&
            vm.DiscoverySections.Count > 0 && !vm.IsLoadingDiscovery);
        var hub = (RequestsPage)frame.Content;
        Click((Button)hub.FindName("DiscoverTabButton"));
        await Until(() => ((FrameworkElement)hub.FindName("DiscoverySection")).Visibility == Visibility.Visible);
        Button Bookmark() => Descendants(hub).OfType<Button>().Single(button =>
            AutomationProperties.GetName(button).Contains("watchlist", StringComparison.OrdinalIgnoreCase));
        int Writes() => wire.Paths.Count(path => path.StartsWith("PUT /api/v2/watchlist/titles/", StringComparison.Ordinal) ||
            path.StartsWith("DELETE /api/v2/watchlist/titles/", StringComparison.Ordinal));
        var bookmark = Bookmark();
        var writes = Writes();
        Click(bookmark);
        await Until(() => bookmark.IsEnabled && Writes() == writes + 1);
        Check(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true),
            "Bookmark positive control performs an actual transport catch-up");
        Click(bookmark);
        await Until(() => bookmark.IsEnabled && Writes() == writes + 2);
        Check(ReferenceEquals(frame.Content, hub), "Current and reconnect bookmark actions complete on the retained hub");
        var owner = vm.DiscoveryOwner;
        client.InvalidateAccessContext();
        if (owner is not { } retired || api.IsCurrentDiscoveryOwner(retired))
            throw new InvalidOperationException("INFRA: bookmark setup did not retire its discovery owner.");
        writes = Writes();
        Click(bookmark);
        await Task.Delay(120);
        Check(Writes() == writes, "Retired outside-title bookmark does not dispatch a watchlist write after real access loss");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Program.Log("PASS: " + message); }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 300; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Profile Requests native state did not settle."); }
    private static async Task<ContentDialog> Dialog(Page page)
    {
        ContentDialog? found = null;
        await Until(() => (found = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>().FirstOrDefault()) is { IsLoaded: true });
        return found!;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private sealed class Wire : HttpMessageHandler
    {
        internal List<string> Paths = []; internal bool Cancelled, Removed, Empty;
        internal bool RequestsEnabled = true, Following = true, RejectFollow; internal JsonElement? LastProfileBody;
        private static object Discovery() => new { key = "latest", title = "Review discoveries", total_pages = 2, total_results = 2,
            results = new[] { new { media_type = "movie", tmdb_id = 42, title = "Retained review title", availability = "missing", request = new { requestable = false } } } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; Paths.Add(request.Method + " " + path);
            if (path.EndsWith("/cancel")) Cancelled = true;
            if (path.Contains("/requests/follows/") && request.Method == HttpMethod.Put && RejectFollow)
                return new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"code\":\"forbidden\",\"message\":\"Requests are blocked.\"}") };
            if (path.Contains("/requests/follows/")) Following = request.Method == HttpMethod.Put;
            if (request.Method == HttpMethod.Delete) Removed = true;
            if (request.Method == HttpMethod.Patch && path.Contains("/profiles/"))
                LastProfileBody = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
            var json = path.EndsWith("/status") ? JsonSerializer.Serialize(new { requests_enabled = RequestsEnabled, allowed = false, follow_supported = true, watchlist_titles_supported = true, remove_failed_supported = true })
                : path == "/api/v2/watchlist/titles" ? JsonSerializer.Serialize(new { items = new[] { new { media_type = "movie", tmdb_id = 42, title = "Retained review title", status = "active", request = new { status = "pending" } } } })
                : path == "/api/v2/requests/discover" ? JsonSerializer.Serialize(new { items = new[] { Discovery() } })
                : path == "/api/v2/requests/discover/latest" ? JsonSerializer.Serialize(Discovery())
                : path == "/api/v2/requests/detail/movie/42" ? JsonSerializer.Serialize(new { media_type = "movie", tmdb_id = 42, title = "Retained review title", availability = "missing", request = new { status = "pending", reason = "already_requested", requested_by_viewer = false, following = Following } })
                : path.EndsWith("/mine") && Empty ? "{\"items\":[]}"
                : path.EndsWith("/mine") ? "{\"items\":[{\"id\":\"own\",\"title\":\"Before the block\",\"status\":\"pending\",\"outcome\":\"" + (Cancelled ? "cancelled" : "active") + "\"}" +
                    (Removed ? "" : ",{\"id\":\"failed\",\"title\":\"Failed before block\",\"status\":\"approved\",\"outcome\":\"failed\"}") + "]}"
                : path.Contains("/profiles/") ? "{\"id\":\"child\",\"name\":\"Child\",\"requests_allowed\":false}" : "{}";
            return new(request.Method == HttpMethod.Delete ? HttpStatusCode.NoContent : HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
