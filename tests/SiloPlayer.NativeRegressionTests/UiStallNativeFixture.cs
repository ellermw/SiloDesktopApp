using System.Collections.Specialized;
using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Admin;

/// <summary>
/// Checks ui_lag.txt for startup, post-read rendering and slow Frame transitions against a synthetic server.
/// Dispatch: SILO_NATIVE_TEST_ONLY=ui-stall-log.
/// </summary>
internal static class UiStallNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://ui-stall.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        using var events = new EventChannelClient(client, auth);
        var catalog = new CatalogApi(client);
        var homeApi = new HomeApi(client);
        var settingsApi = new SettingsApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "ui-stall-settings"));
        using var authority = new AdminMonitorAuthority(client, auth);
        var adminApi = new AdminMonitorApi(client);
        using var store = new ServerActivityStore(adminApi, authority, new EventChannelAdminRealtime(events));
        var frame = new Frame { Width = 1000, Height = 700 }; parent.Children.Add(frame);
        var navigation = new NavigationService { Frame = frame };
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = () => client, [typeof(CatalogApi)] = () => catalog, [typeof(HttpClient)] = () => http,
            [typeof(FavoritesViewModel)] = () => new FavoritesViewModel(catalog),
            [typeof(HomeApi)] = () => homeApi,
            [typeof(HomeViewModel)] = () => new HomeViewModel(homeApi, auth),
            [typeof(CollectionsApi)] = () => new CollectionsApi(client),
            [typeof(UICustomizationService)] = () => new UICustomizationService(settingsApi),
            [typeof(TasteSeedViewModel)] = () => new TasteSeedViewModel(new RecommendationsApi(client)),
            [typeof(AuthService)] = () => auth, [typeof(EventChannelClient)] = () => events,
            [typeof(NavigationService)] = () => navigation,
            [typeof(SettingsService)] = () => settings, [typeof(SettingsApi)] = () => settingsApi,
            [typeof(AdminMonitorAuthority)] = () => authority, [typeof(AdminMonitorApi)] = () => adminApi,
            [typeof(ServerActivityStore)] = () => store,
        }));
        var monitor = new UiStallMonitor(UiStallLog.Shared, () => frame, () => "idle");
        var path = Path.Combine(Program.ResultDirectory, "ui_lag.txt");
        try
        {
            var before = Lines(path).Count;
            navigation.Navigate(typeof(FavoritesPage));
            await Until(() => frame.Content is FavoritesPage { IsLoaded: true }, "the Favorites page did not load");
            await wire.FavoritesStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(300);
            var favorites = new UiStallLog.Location(nameof(FavoritesPage));
            UiStallLog.Shared.RecordOperation("before-start", 20_000, favorites);
            monitor.Start();
            // The first tick must not report the much longer operation from before monitoring.
            using (UiStallLog.Shared.Measure("synthetic-block", favorites)) Thread.Sleep(950);
            await Task.Delay(700);
            string? line = null;
            await Until(() => (line = Lines(path).Skip(before).LastOrDefault(text => text.Contains("longest_op=synthetic-block"))) != null,
                "no ui_lag.txt line named the synthetic block");
            var stall = line![line.IndexOf("delay_ms=", StringComparison.Ordinal)..];
            Check(stall.Contains("| page=FavoritesPage |") && stall.Contains("| admin=- |") && stall.Contains("| last=navigate:FavoritesPage ")
                && stall.Contains("| playback=idle |") && stall.Contains("| op_owner=FavoritesPage |") && stall.Contains("breadcrumb="),
                "the stall line must name the sampled page, the operation owner, the navigation and playback: " + stall);
            var delay = long.Parse(stall["delay_ms=".Length..stall.IndexOf(' ')]);
            Check(delay >= 900, $"the stall line must carry the raw tick gap: {delay}ms");
            Program.Log("PASS: a synthetic interface block on the Favorites page wrote: " + stall);

            // The HTTP response is still held, well after Frame.Navigate returned and idle ticks ran.
            // The first reset applies the server result; the second is from ApplySort in the Loaded continuation.
            var favoritesPage = (FavoritesPage)frame.Content;
            var items = favoritesPage.ViewModel.Items;
            Check(items.Count == 0, "Favorites rendered before the read was released");
            var resets = 0;
            void SlowSort(object? sender, NotifyCollectionChangedEventArgs e)
            {
                if (e.Action == NotifyCollectionChangedAction.Reset && ++resets == 2) Thread.Sleep(950);
            }
            items.CollectionChanged += SlowSort;
            var beforeRender = Lines(path).Count;
            try
            {
                wire.ReleaseFavorites.TrySetResult();
                await Until(() => Lines(path).Skip(beforeRender).Any(text =>
                    text.Contains("| page=FavoritesPage |") && text.Contains("| op_owner=FavoritesPage |") &&
                    DurationAtLeast(text, @"longest_op=page-load-render (\d+)ms", 900)),
                    "no ui_lag.txt line timed the post-read Favorites Loaded continuation");
            }
            finally { items.CollectionChanged -= SlowSort; }

            var favoritesReload = wire.FavoritesReload = new HeldResponse();
            var reload = favoritesPage.ViewModel.LoadCommand.ExecuteAsync(null);
            await VerifyCollectionStallAsync(favoritesReload, items, NotifyCollectionChangedAction.Reset, nameof(FavoritesPage), nameof(FavoritesPage));
            await reload;

            var favoritesScroll = (ScrollViewer)favoritesPage.FindName("ContentScrollViewer");
            var nextPage = wire.FavoritesNextPage;
            var favoritesSort = (ComboBox)favoritesPage.FindName("SortComboBox");
            favoritesSort.SelectedIndex = 2; // Date Added uses server order.
            await StartFavoritesPaginationAsync(favoritesScroll, nextPage);
            // The next response only appends; this Reset is the subsequent page-side sort.
            await VerifyCollectionStallAsync(nextPage, items, NotifyCollectionChangedAction.Reset,
                nameof(FavoritesPage), nameof(FavoritesPage), "favorites-pagination-render");
            Check(items.Count == 51 && items.Last().ContentId == "fixture-next-item", "Favorites did not keep the appended page in server order");
            await VerifyFavoritesPaginationWithRemovalAsync(favoritesPage, favoritesScroll, favoritesSort);
            wire.FavoritesReload = null;

            await VerifyNavigationAsync(() => Check(navigation.Navigate<Page>("fixture-route-argument"), "slow navigation was refused"),
                NavigationMode.New, "navigation", "navigate", nameof(FavoritesPage), nameof(Page));
            navigation.GoBackImmediately();
            await VerifyNavigationAsync(navigation.GoForwardImmediately,
                NavigationMode.Forward, "navigation-forward", "forward", nameof(FavoritesPage), nameof(Page));
            var pageBeforeRefresh = frame.Content;
            await VerifyNavigationAsync(() => Check(navigation.RefreshCurrentEntry(), "refresh was refused"),
                NavigationMode.New, "navigation-refresh", "refresh", nameof(Page), nameof(Page));
            Check(!ReferenceEquals(pageBeforeRefresh, frame.Content), "refresh did not remount the route");

            Check(navigation.Navigate<CatalogPage>(new CatalogNavigation("favorites")), "the sidebar Favorites route was refused");
            await Until(() => frame.Content is CatalogPage { IsLoaded: true }, "the sidebar Favorites route did not mount");
            var catalogPage = (CatalogPage)frame.Content;
            var catalogRepeater = (ItemsRepeater)catalogPage.FindName("ItemsRepeater");
            await VerifyCollectionStallAsync(wire.CatalogFavorites, (INotifyCollectionChanged)catalogRepeater.ItemsSource,
                NotifyCollectionChangedAction.Add, nameof(CatalogPage), "CatalogPage/Favorites");
            await VerifyCatalogFiltersAsync(catalogPage, catalogRepeater);

            navigation.Navigate<HomePage>();
            await Until(() => frame.Content is HomePage { IsLoaded: true }, "Home did not mount");
            var home = (HomePage)frame.Content;
            await VerifyCollectionStallAsync(wire.HomeLayout, home.ViewModel.Sections, NotifyCollectionChangedAction.Add, nameof(HomePage), nameof(HomePage));
            await VerifyCollectionStallAsync(wire.HomeSection, home.ViewModel.Sections, NotifyCollectionChangedAction.Replace, nameof(HomePage), nameof(HomePage));
            await VerifyHomeRowRefreshAsync(home);
            await VerifyHomeUndoAsync(home);

            var personalize = typeof(HomePage).GetMethod("PersonalizeHome_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await VerifyNavigationAsync(() => personalize.Invoke(home, [home, new RoutedEventArgs()]),
                NavigationMode.New, "navigation", "navigate", nameof(HomePage), nameof(TasteSeedPage));

            var profileId = "stall-picker-" + Guid.NewGuid();
            auth.SelectProfile(profileId);
            Check(navigation.Navigate<TasteSeedPage>(true), "the Settings-origin picker was refused");
            var picker = (TasteSeedPage)frame.Content;
            await Until(() => picker.ViewModel.Items.Count == 3, "the picker did not receive its three choices");
            foreach (var item in picker.ViewModel.Items) picker.ViewModel.Toggle(item);
            var submit = typeof(TasteSeedPage).GetMethod("SubmitAndReturnAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var pending = (Task)submit.Invoke(picker, null)!;
            await wire.TasteSubmit.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(navigation.Navigate<Page>(), "could not leave the picker with a save in flight");
            var successor = frame.Content;
            wire.TasteSubmit.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(3));
            Check(!string.IsNullOrEmpty(picker.ViewModel.StatusMessage), "the held save did not succeed");
            Check(ReferenceEquals(successor, frame.Content) && !settings.Load().TasteSeedDismissedProfileIds.Contains(profileId),
                "the retired picker redirected the frame or dismissed onboarding");

            auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
            auth.SelectProfile("admin-profile", null, new Profile { Id = "admin-profile", Name = "Primary", IsPrimary = true });
            const string admin = "AdminShellPage/Activity";
            Check(navigation.Navigate<AdminShellPage>(new AdminRouteRequest(AdminRoute.Activity)), "the Admin shell was refused");
            await Until(AdminMounted, "the initial Admin Activity route did not mount");
            await VerifyAdminNavigationAsync(() => Check(navigation.RefreshCurrentEntry(), "Admin refresh was refused"),
                NavigationMode.New, "navigation-refresh", "refresh", admin);
            Check(navigation.Navigate<Page>("after-admin"), "could not leave Admin");
            await VerifyAdminNavigationAsync(navigation.GoBackImmediately, NavigationMode.Back, "navigation-back", "back", nameof(Page));
            navigation.GoBackImmediately();
            await VerifyAdminNavigationAsync(navigation.GoForwardImmediately, NavigationMode.Forward, "navigation-forward", "forward", nameof(Page));
        }
        finally
        {
            wire.ReleaseFavorites.TrySetResult();
            wire.FavoritesReload?.Release.TrySetResult();
            wire.FavoritesNextPage.Release.TrySetResult();
            wire.CatalogFavorites.Release.TrySetResult();
            wire.CatalogFavoriteFilters.Release.TrySetResult();
            wire.HomeLayout.Release.TrySetResult();
            wire.HomeSection.Release.TrySetResult();
            wire.HomeRowRefresh?.Release.TrySetResult();
            wire.HomeRowReorder?.Release.TrySetResult();
            wire.HomeUndo.Release.TrySetResult();
            wire.TasteSubmit.Release.TrySetResult();
            monitor.Stop();
            frame.Navigate(typeof(Page));
            parent.Children.Remove(frame); await Task.Delay(100);
            field.SetValue(null, previous);
        }

        bool AdminMounted() => frame.Content is AdminShellPage { CurrentRoute: AdminRoute.Activity } shell &&
            ((Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!).Content
                is AdminActivityPage { IsLoaded: true };

        async Task VerifyAdminNavigationAsync(Action navigate, NavigationMode frameMode, string operation, string action, string owner)
        {
            const string target = "AdminShellPage/Activity";
            await VerifyNavigationAsync(navigate, frameMode, operation, action, owner, target);
            await Until(AdminMounted, "the returning Admin Activity route did not mount");
            var final = UiStallLog.Shared.Stall(950, 700, 0, new(nameof(AdminShellPage), nameof(AdminRoute.Activity)), "idle");
            Check(DurationAtLeast(final, $@"\| last={action}:{target} (\d+)ms ago \|", 900),
                "the mounted inner route replaced the outer action: " + final);
        }

        async Task VerifyCollectionStallAsync(HeldResponse held, INotifyCollectionChanged items, NotifyCollectionChangedAction action,
            string page, string owner, string operation = "page-load-apply")
        {
            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(300); // Quiet ticks while the read is held must not count toward the application.
            var beforeApply = Lines(path).Count;
            var blocked = false;
            void SlowApply(object? sender, NotifyCollectionChangedEventArgs e)
            {
                if (blocked || e.Action != action) return;
                blocked = true;
                Thread.Sleep(950);
            }
            items.CollectionChanged += SlowApply;
            try
            {
                held.Release.TrySetResult();
                await Until(() => blocked && Lines(path).Skip(beforeApply).Any(text =>
                    text.Contains($"| page={page} |") && text.Contains($"| op_owner={owner} |") &&
                    DurationAtLeast(text, $@"longest_op={operation} (\d+)ms", 900)),
                    $"no ui_lag.txt line timed {operation} on {owner} ({action})");
            }
            finally { items.CollectionChanged -= SlowApply; }
        }

        async Task StartFavoritesPaginationAsync(ScrollViewer scroll, HeldResponse held)
        {
            await Until(() => scroll.ScrollableHeight > 640 || held.Started.Task.IsCompleted,
                "Favorites did not lay out a scrollable first page");
            if (!held.Started.Task.IsCompleted)
                scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }

        async Task VerifyFavoritesPaginationWithRemovalAsync(FavoritesPage page, ScrollViewer scroll, ComboBox sort)
        {
            var held = wire.FavoritesNextPage = new HeldResponse();
            scroll.ChangeView(null, 0, null, disableAnimation: true);
            await Until(() => scroll.VerticalOffset < 1, "Favorites did not return to the top before reloading");
            var vm = page.ViewModel;
            await vm.LoadCommand.ExecuteAsync(null);
            sort.SelectedIndex = 0; // Title sorting puts the appended item first.
            await StartFavoritesPaginationAsync(scroll, held);

            var countText = (TextBlock)page.FindName("ItemCountText");
            string CountLabel(int count) => AppStrings.Get("web.catalog.page.titleCount",
                new Dictionary<string, object?> { ["count"] = count, ["shown"] = count.ToString("N0", AppStrings.FormatCulture) });
            Check(vm.Items.Count == 50, "Favorites should be holding the next page with 50 items displayed");
            vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.FavoriteRemoved, "favorite-1"));
            var atFortyNine = CountLabel(49);
            await Until(() => countText.Text == atFortyNine, "Favorites did not redraw its count during the held read");

            var beforeCount = Lines(path).Count;
            var blocked = false;
            var atFifty = CountLabel(50);
            var token = countText.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
            {
                if (blocked || countText.Text != atFifty) return;
                blocked = true;
                Thread.Sleep(950);
            });
            try
            {
                held.Release.TrySetResult();
                await Until(() => blocked && Lines(path).Skip(beforeCount).Any(text =>
                    text.Contains("| page=FavoritesPage |") && text.Contains("| op_owner=FavoritesPage |") &&
                    DurationAtLeast(text, @"longest_op=favorites-count-render (\d+)ms", 900)),
                    "no ui_lag.txt line timed the Favorites count redraw after pagination");
                Check(vm.Items.Count == 50 && vm.Items[0].ContentId == "fixture-next-item" &&
                    vm.Items.All(item => item.ContentId != "favorite-1"),
                    "Favorites did not sort the appended item when a removal kept the count unchanged");
            }
            finally { countText.UnregisterPropertyChangedCallback(TextBlock.TextProperty, token); }
        }

        async Task VerifyCatalogFiltersAsync(CatalogPage page, ItemsRepeater repeater)
        {
            var held = wire.CatalogFavoriteFilters;
            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var library = (ComboBox)page.FindName("LibraryCombo");
            Check(repeater.Visibility == Visibility.Visible && library.Items.Count == 0,
                "Sidebar Favorites did not render items first while metadata was held");
            await Task.Delay(300); // Measure the filter continuation in a later timer interval.
            var beforeFilters = Lines(path).Count;
            var token = library.RegisterPropertyChangedCallback(ComboBox.SelectedIndexProperty, (_, _) =>
            {
                if (library.SelectedIndex == 0) Thread.Sleep(950);
            });
            try
            {
                held.Release.TrySetResult();
                await Until(() => Lines(path).Skip(beforeFilters).Any(text =>
                    text.Contains("| page=CatalogPage |") && text.Contains("| op_owner=CatalogPage/Favorites |") &&
                    DurationAtLeast(text, @"longest_op=favorites-filter-render (\d+)ms", 900)),
                    "no ui_lag.txt line timed the late sidebar Favorites filter setup");
                Check(library.Items.Count == 2, "Sidebar Favorites did not apply the library metadata");
            }
            finally { library.UnregisterPropertyChangedCallback(ComboBox.SelectedIndexProperty, token); }
        }

        async Task VerifyHomeRowRefreshAsync(HomePage page)
        {
            var panel = (Panel)page.FindName("SectionsPanel");
            await Until(() => panel.Children.OfType<SectionRow>().SingleOrDefault() is { IsLoaded: true, Section: { LoadCompleted: true } },
                "the populated Continue Watching row did not mount");
            var row = panel.Children.OfType<SectionRow>().Single();
            var repeater = (ItemsRepeater)row.FindName("CardsRepeater");
            var mountedItems = row.Section!.Items;
            Check(mountedItems.Count == 2 && mountedItems.All(item => item.Type == "movie") &&
                ReferenceEquals(repeater.ItemTemplate, row.Resources["ContinuePosterCardTemplate"]),
                "Continue Watching did not start with the all-movie cover template");

            var held = wire.HomeRowRefresh = new HeldResponse();
            page.ViewModel.InvalidateCache();
            await page.ViewModel.LoadCommand.ExecuteAsync(null);
            await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(300);
            var beforeRowRefresh = Lines(path).Count;
            var sectionsChanged = false;
            void SectionReplaced(object? sender, NotifyCollectionChangedEventArgs e) => sectionsChanged = true;
            var landscape = row.Resources["LandscapeCardTemplate"];
            var token = repeater.RegisterPropertyChangedCallback(ItemsRepeater.ItemTemplateProperty, (_, _) =>
            {
                if (ReferenceEquals(repeater.ItemTemplate, landscape)) Thread.Sleep(950);
            });
            page.ViewModel.Sections.CollectionChanged += SectionReplaced;
            try
            {
                held.Release.TrySetResult();
                await Until(() => Lines(path).Skip(beforeRowRefresh).Any(text =>
                    text.Contains("| page=HomePage |") && text.Contains("| op_owner=HomePage |") &&
                    DurationAtLeast(text, @"longest_op=home-row-render (\d+)ms", 900)),
                    "no ui_lag.txt line timed the deferred Continue Watching template change");
                Check(!sectionsChanged && ReferenceEquals(row.Section?.Items, mountedItems) && mountedItems.Count == 2 &&
                    mountedItems.Any(item => item.Type == "episode"), "the Home refresh was not an in-place items-only change");
            }
            finally
            {
                page.ViewModel.Sections.CollectionChanged -= SectionReplaced;
                repeater.UnregisterPropertyChangedCallback(ItemsRepeater.ItemTemplateProperty, token);
            }

            var section = row.Section;
            var cardsSource = (INotifyCollectionChanged)repeater.ItemsSource;
            var original = mountedItems.ToArray();
            var reorder = wire.HomeRowReorder = new HeldResponse();
            page.ViewModel.InvalidateCache();
            await page.ViewModel.LoadCommand.ExecuteAsync(null);
            await VerifyCollectionStallAsync(reorder, cardsSource, NotifyCollectionChangedAction.Reset,
                nameof(HomePage), nameof(HomePage), "home-row-render");
            Check(ReferenceEquals(row.Section, section) && ReferenceEquals(repeater.ItemsSource, cardsSource) &&
                ReferenceEquals(repeater.ItemTemplate, landscape) && mountedItems.SequenceEqual(original.Reverse()),
                "the reorder did not retain the mounted row, template and cards");
        }

        async Task VerifyHomeUndoAsync(HomePage page)
        {
            var row = ((Panel)page.FindName("SectionsPanel")).Children.OfType<SectionRow>().Single();
            var items = row.Section!.Items;
            var original = items.ToArray();
            var repeater = (ItemsRepeater)row.FindName("CardsRepeater");
            var cardsSource = (INotifyCollectionChanged)repeater.ItemsSource;
            var series = new MediaItem { ContentId = "fixture-series", Type = "series" };
            var owner = homeApi.CaptureContext();
            wire.HomeSeriesDismissed = true;
            page.ViewModel.Receive(new HomeDismissalApplied("continue_watching", series, owner));
            page.ViewModel.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.HomeDismissed, series.ContentId,
                seriesId: series.ContentId, context: owner));
            Check(items.Count == 1 && page.ViewModel.ShowUndoBanner, "the mounted Home row did not reflect the series dismissal");

            var undo = typeof(HomePage).GetMethod("UndoButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            undo.Invoke(page, [page, new RoutedEventArgs()]);
            await VerifyCollectionStallAsync(wire.HomeUndo, cardsSource, NotifyCollectionChangedAction.Add,
                nameof(HomePage), nameof(HomePage), "home-undo-restore");
            Check(ReferenceEquals(repeater.ItemsSource, cardsSource) && items.SequenceEqual(original),
                "Undo did not restore the series card in the mounted Home row");
        }

        async Task VerifyNavigationAsync(Action navigate, NavigationMode frameMode, string operation, string action, string owner, string target)
        {
            var beforeNavigation = Lines(path).Count;
            var blocked = false;
            NavigatingCancelEventHandler blockNavigation = (_, e) =>
            {
                Check(e.NavigationMode == frameMode, $"{operation} unexpectedly used Frame mode {e.NavigationMode}");
                blocked = true;
                Thread.Sleep(950);
            };
            frame.Navigating += blockNavigation;
            try { navigate(); }
            finally { frame.Navigating -= blockNavigation; }
            Check(blocked, operation + " did not enter the Frame");
            string? transition = null;
            await Until(() => (transition = Lines(path).Skip(beforeNavigation).LastOrDefault(text =>
                text.Contains($"| op_owner={owner} | op_target={target} |"))) != null,
                "no ui_lag.txt line named the slow " + operation);
            var displayed = target.Split('/');
            Check(transition!.Contains($"| page={displayed[0]} | admin={(displayed.Length == 2 ? displayed[1] : "-")} |") &&
                DurationAtLeast(transition, $@"\| last={action}:{target} (\d+)ms ago \|", 900) &&
                DurationAtLeast(transition, $@"longest_op={operation} (\d+)ms", 900) &&
                !transition.Contains("fixture-route-argument"),
                "the transition must name both pages, the duration and the start age without route arguments: " + transition);
            Program.Log("PASS: a slow Frame navigation wrote: " + transition);
        }
    }

    /// <summary>Opt-in physical XButton1 check; does not inject global input.</summary>
    internal static async Task VerifyPhysicalPointerBackAsync()
    {
        await using var scope = EpisodeQuickPlayNativeFixture.CreateOwnedDispatcherShell();
        var window = SiloPlayer.App.MainWindowInstance!;
        var navigation = (NavigationService)SiloPlayer.App.Services.GetService(typeof(NavigationService))!;
        var frame = navigation.Frame!;
        var nav = (NavigationView)((FrameworkElement)window.Content).FindName("NavView");
        navigation.BackNavigationRequestHandler = null;
        window.AppWindow.ResizeClient(new(1100, 800));
        window.Activate();
        Check(navigation.NavigateImmediately(typeof(Page), "first") && navigation.NavigateImmediately(typeof(Page), "second"),
            "could not create pointer Back history");
        window.Title = "Silo stall fixture — use XButton1 in this window";
        ((Page)frame.Content).Content = new TextBlock
        {
            Text = "Press the mouse Back (XButton1) button here once.",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        await Until(() => frame.Content is Page { IsLoaded: true }, "pointer fixture did not mount");
        nav.IsPaneOpen = false;

        var monitor = new UiStallMonitor(UiStallLog.Shared, () => frame, () => "idle");
        var path = Path.Combine(Program.ResultDirectory, "ui_lag.txt");
        var before = Lines(path).Count;
        var back = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void BlockBack(object sender, NavigatingCancelEventArgs e) { if (e.NavigationMode == NavigationMode.Back) Thread.Sleep(950); }
        void CommitBack(object sender, NavigationEventArgs e) { if (e.NavigationMode == NavigationMode.Back) back.TrySetResult(); }
        frame.Navigating += BlockBack;
        frame.Navigated += CommitBack;
        monitor.Start();
        try
        {
            Program.Log("PHYSICAL_STALL_BACK_READY: press XButton1 in the isolated Silo stall fixture window.");
            await back.Task.WaitAsync(TimeSpan.FromMinutes(2));
            await Until(() => Lines(path).Skip(before).Any(text =>
                DurationAtLeast(text, @"\| last=back:Page (\d+)ms ago \|", 900) &&
                DurationAtLeast(text, @"longest_op=navigation-back (\d+)ms", 900)),
                "the slow pointer Back was missing or overwritten by a click");
            Program.Log("PASS: a physical XButton1 press retained the slow Back action and its start age.");
        }
        finally
        {
            monitor.Stop();
            frame.Navigating -= BlockBack;
            frame.Navigated -= CommitBack;
        }
    }

    private static bool DurationAtLeast(string line, string pattern, long minimum)
    {
        var match = System.Text.RegularExpressions.Regex.Match(line, pattern);
        return match.Success && long.Parse(match.Groups[1].Value) >= minimum;
    }

    internal static List<string> Lines(string path)
    {
        if (!File.Exists(path)) return [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } text) lines.Add(text);
        return lines;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("UI stall log: " + message); }
    private static async Task Until(Func<bool> ready, string message)
    {
        for (var attempt = 0; attempt < 200; attempt++) { if (ready()) return; await Task.Delay(25); }
        if (!ready()) throw new TimeoutException("UI stall log: " + message);
    }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, Func<object>> factories) : IServiceProvider
    { public object? GetService(Type type) => factories.TryGetValue(type, out var factory) ? factory() : fallback.GetService(type); }

    private sealed class HeldResponse
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task WaitAsync(CancellationToken ct)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        private const string Movie = """{"content_id":"fixture-item","title":"Fixture movie","type":"movie"}""";
        private const string HomeRowMetadata = """
            "id":"fixture-row","section_type":"continue_watching","title":"Fixture row","item_limit":10
            """;
        internal readonly TaskCompletionSource FavoritesStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseFavorites = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HeldResponse? FavoritesReload;
        internal HeldResponse FavoritesNextPage = new();
        internal readonly HeldResponse CatalogFavorites = new();
        internal readonly HeldResponse CatalogFavoriteFilters = new();
        internal readonly HeldResponse HomeLayout = new();
        internal readonly HeldResponse HomeSection = new();
        internal HeldResponse? HomeRowRefresh;
        internal HeldResponse? HomeRowReorder;
        internal readonly HeldResponse HomeUndo = new();
        internal volatile bool HomeSeriesDismissed;
        internal readonly HeldResponse TasteSubmit = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "ui-stall.invalid") throw new InvalidOperationException("UI stall fixture attempted external network.");
            switch (request.RequestUri.AbsolutePath)
            {
                case "/api/v2/favorites" when request.RequestUri.Query.Contains("cursor=fixture-next", StringComparison.Ordinal):
                    await FavoritesNextPage.WaitAsync(ct);
                    return Json("""{"items":[{"content_id":"fixture-next-item","title":"A new favorite","type":"movie"}],"page":{"has_more":false}}""");
                case "/api/v2/favorites":
                    FavoritesStarted.TrySetResult();
                    await ReleaseFavorites.Task.WaitAsync(ct);
                    if (FavoritesReload is { } reload)
                    {
                        await reload.WaitAsync(ct);
                        var page = string.Join(",", Enumerable.Range(1, 49).Select(index =>
                            $"{{\"content_id\":\"favorite-{index}\",\"title\":\"Favorite {index}\",\"type\":\"movie\"}}"));
                        return Json("{\"items\":[" + Movie + "," + page + "],\"page\":{\"has_more\":true,\"next_cursor\":\"fixture-next\"}}");
                    }
                    return Json("{\"items\":[" + Movie + "],\"page\":{\"has_more\":false}}");
                case "/api/v2/catalog" when request.RequestUri.Query.Contains("source=favorites", StringComparison.Ordinal):
                    await CatalogFavorites.WaitAsync(ct);
                    return Json("{\"items\":[" + Movie + "],\"total\":1,\"total_exact\":true,\"page\":{\"has_more\":false}}");
                case "/api/v2/user/libraries":
                    return Json("""{"items":[{"id":1,"name":"Fixture library"}]}""");
                case "/api/v2/catalog/filters" when request.RequestUri.Query.Contains("source=favorites", StringComparison.Ordinal):
                    await CatalogFavoriteFilters.WaitAsync(ct);
                    return Json("""{"genres":["Drama"]}""");
                case "/api/v2/home/layout":
                    await HomeLayout.WaitAsync(ct);
                    return Json("{\"sections\":[{" + HomeRowMetadata + "}]}");
                case "/api/v2/home/sections/fixture-row/items":
                    await HomeSection.WaitAsync(ct);
                    const string first = """{"content_id":"fixture-item","title":"Fixture movie","type":"movie","item_source":"continue_watching"}""";
                    var second = """{"content_id":"fixture-second","title":"Second movie","type":"movie","item_source":"continue_watching"}""";
                    if (HomeRowRefresh is { } refresh)
                    {
                        await refresh.WaitAsync(ct);
                        second = """{"content_id":"fixture-episode","title":"Fixture episode","type":"episode","series_id":"fixture-series","item_source":"continue_watching"}""";
                    }
                    var homeItems = first + "," + second;
                    if (HomeRowReorder is { } reorder)
                    {
                        await reorder.WaitAsync(ct);
                        homeItems = second + "," + first;
                    }
                    if (HomeSeriesDismissed) homeItems = first;
                    return Json("{" + HomeRowMetadata + ",\"items\":[" + homeItems + "]}");
                case "/api/v2/home/dismissals/continue_watching/fixture-series" when request.Method == HttpMethod.Delete:
                    await HomeUndo.WaitAsync(ct);
                    HomeSeriesDismissed = false;
                    return new(HttpStatusCode.NoContent);
                case "/api/v2/recommendations/taste-seed/items":
                    return Json("""{"items":[{"content_id":"a","title":"A","type":"movie"},{"content_id":"b","title":"B","type":"movie"},{"content_id":"c","title":"C","type":"movie"}],"page":{"has_more":false}}""");
                case "/api/v2/recommendations/taste-seed" when request.Method == HttpMethod.Post:
                    await TasteSubmit.WaitAsync(ct);
                    return Json("""{"added":3}""");
                case "/api/v2/events/ticket":
                    return new(HttpStatusCode.ServiceUnavailable); // Never upgrade a synthetic connection.
                default:
                    return Json("""{"items":[],"total":0,"total_exact":true,"page":{"has_more":false}}""");
            }
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
