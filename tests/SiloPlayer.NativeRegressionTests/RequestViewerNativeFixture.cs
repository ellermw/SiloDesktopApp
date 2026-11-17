using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class RequestViewerNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SetTokens("request-fixture-access", "request-fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("fixture-profile", profile: new() { Id = "fixture-profile", Name = "Fixture" });
        var api = new RequestsApi(client);
        var overlays = new CardOverlayService(new SettingsApi(client));
        using var services = new ServiceCollection().AddSingleton(api).AddSingleton(client).AddSingleton(auth)
            .AddSingleton(new NavigationService())
            .AddSingleton(new PeopleApi(client)).AddSingleton(new SettingsApi(client)).AddSingleton<SearchViewModel>()
            .AddSingleton(new CatalogApi(client)).AddSingleton(new ToastService()).AddSingleton(overlays)
            .AddSingleton(new UICustomizationService(new SettingsApi(client)))
            .AddSingleton(new ItemDetailPrefetchCache((id, _) => Task.FromException<MediaItemDetail>(new ApiException("not_found", "Fixture inaccessible library", 404))))
            .AddSingleton<WatchlistViewModel>().BuildServiceProvider();
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = serviceField.GetValue(null); serviceField.SetValue(null, services);
        var standalonePages = new List<FrameworkElement>();
        try
        {
            var reviewCase = Environment.GetEnvironmentVariable("SILO_NATIVE_REQUEST_REVIEW_CASE");
            if (reviewCase == "retained-search") { await RetainedSearchOwnerAsync(parent, handler, auth, api); return; }
            if (reviewCase == "retired-title-load") { await RetiredTitleLoadAsync(parent, handler, auth); return; }
            if (reviewCase == "cancelled-write") { await CancelledWriteFeedbackAsync(parent, handler, client); return; }
            await SearchAndRulesAsync(parent);
            await RetainedSearchOwnerAsync(parent, handler, auth, api);
            await RetiredTitleLoadAsync(parent, handler, auth);
            await CancelledWriteFeedbackAsync(parent, handler, client);
            var page = new RequestDetailPage { Width = 1000, Height = 700 };
            standalonePages.Add(page); parent.Children.Add(page);
            Field(page, "_contentOwner", api.CaptureDiscoveryOwner());
            var item = await api.GetDetailAsync("series", 42);
            Field(page, "_item", item); Field(page, "_features", await api.GetStatusAsync());
            Invoke(page, "Render", item);
            var actions = (SiloPlayer.Controls.WrapPanel)page.FindName("ActionsPanel");
            var follow = actions.Children.OfType<Button>().Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Notify me when available");
            Invoke(page, "Follow_Click", follow, new RoutedEventArgs());
            await UntilAsync(() => handler.Paths.Contains("PUT /api/v2/requests/follows/series/42") && actions.Children.OfType<Button>().Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Stop notifying me"));
            if (((TextBlock)page.FindName("DownloadText")).Text != "Waiting for import") throw new InvalidOperationException("Native title lost download phase.");
            if (((StackPanel)page.FindName("SeasonsPanel")).Children.Count != 2) throw new InvalidOperationException("Native title lost seasons.");
            var today = DateTime.UtcNow.Date;
            var selectionItem = new RequestMediaDetail { Title = "Season choices", Request = new() { Requestable = true }, Seasons =
            [ new() { SeasonNumber = 1, Availability = "available", EpisodeCount = 8, AirDate = today.AddDays(-20).ToString("yyyy-MM-dd") },
              new() { SeasonNumber = 2, Availability = "partial", EpisodeCount = 8, AirDate = today.AddDays(-10).ToString("yyyy-MM-dd") },
              new() { SeasonNumber = 3, Availability = "missing", EpisodeCount = 8, AirDate = today.AddDays(10).ToString("yyyy-MM-dd") } ] };
            var build = typeof(SiloPlayer.Views.Dialogs.RequestSeasonsDialog).GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)!;
            var (dialog, checks) = ((ContentDialog, List<ToggleSwitch>))build.Invoke(null, [page.XamlRoot, selectionItem])!;
            if (checks[0].IsEnabled || checks[1].IsOn != true || checks[2].IsOn != false || !dialog.IsPrimaryButtonEnabled)
                throw new InvalidOperationException("Native season picker defaults or unavailable-season gate is wrong.");
            checks[1].IsOn = false;
            if (dialog.IsPrimaryButtonEnabled) throw new InvalidOperationException("Native season picker allows empty selection.");
            checks[2].IsOn = true;
            if (!dialog.IsPrimaryButtonEnabled) throw new InvalidOperationException("Native season picker cannot select an upcoming season.");
            handler.LibraryContentId = "inaccessible:copy";
            await (Task)page.GetType().GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [new RequestDetailNavigation("series", 42)])!;
            if (actions.Children.OfType<Button>().Any(b => b.Content is string label && label == "Open in library"))
                throw new InvalidOperationException("Native external title offers an inaccessible library copy.");
            var watchlist = actions.Children.OfType<Button>().Single(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "watchlist" &&
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Add to Watchlist");
            Invoke(page, "Watchlist_Click", watchlist, new RoutedEventArgs());
            await UntilAsync(() => handler.Paths.Contains("PUT /api/v2/watchlist/titles/series/42") && actions.Children.OfType<Button>().Any(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "watchlist" &&
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "On Watchlist" &&
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetHelpText(b) == "On"));
            await RequestFixtureLifecycle.DetachAsync(page, () => parent.Children.Remove(page));

            var watchlistPage = new WatchlistPage { Width = 900, Height = 700 };
            standalonePages.Add(watchlistPage); parent.Children.Add(watchlistPage);
            ((ComboBox)watchlistPage.FindName("WatchlistScope")).SelectedIndex = 1;
            await UntilAsync(() => watchlistPage.ViewModel.ExternalTitles.Count == 1);
            watchlistPage.Measure(new Windows.Foundation.Size(900, 700));
            watchlistPage.Arrange(new Windows.Foundation.Rect(0, 0, 900, 700)); watchlistPage.UpdateLayout();
            await Task.Delay(150);
            if (((ItemsRepeater)watchlistPage.FindName("ExternalRepeater")).Visibility != Visibility.Visible || ((ItemsRepeater)watchlistPage.FindName("PosterRepeater")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Native watchlist tab visibility is wrong.");
            if (((TextBlock)watchlistPage.FindName("ItemCountText")).Text != "1 title") throw new InvalidOperationException("Native external watchlist count is wrong.");
            await RequestFixtureLifecycle.DetachAsync(watchlistPage, () => parent.Children.Remove(watchlistPage));
            Program.Log("PASS: native request title seasons, download phase, follow/unfollow label, watchlist write, external tab and count.");
        }
        finally
        {
            foreach (var page in standalonePages.Where(page => parent.Children.Contains(page)))
                await RequestFixtureLifecycle.DetachAsync(page, () => parent.Children.Remove(page));
            serviceField.SetValue(null, previous);
        }
    }


    private static async Task RetainedSearchOwnerAsync(StackPanel parent, Handler handler, AuthService auth, RequestsApi api)
    {
        await RetainedSearchVisitAsync(parent, handler, auth, api, routedQuery: true);
        await RetainedSearchVisitAsync(parent, handler, auth, api, routedQuery: false);
    }

    private static async Task RetainedSearchVisitAsync(StackPanel parent, Handler handler, AuthService auth, RequestsApi api, bool routedQuery)
    {
        var navigation = SiloPlayer.App.Services.GetRequiredService<NavigationService>();
        var frame = new Frame { Width = 900, Height = 700, CacheSize = 8 };
        parent.Children.Add(frame); navigation.Frame = frame;
        try
        {
            // A route-supplied query is replayed by OnNavigatedTo on Back and is a
            // positive control. A normally typed query has no route to replay.
            navigation.NavigateImmediately(typeof(SearchPage), routedQuery ? new SearchNavigation("Retained request", "movie") : null);
            await UntilAsync(() => frame.Content is SearchPage search && search.IsLoaded &&
                (bool)search.GetType().GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(search)!);
            var original = (SearchPage)frame.Content;
            var searchBox = (TextBox)original.FindName("SearchBox");
            if (!routedQuery) searchBox.Text = "Retained request";
            await UntilAsync(() => original.ViewModel.OutsideLibraryResults.Count == 1 && !original.ViewModel.IsOutsideLoading);
            var originalQuery = searchBox.Text;
            var originalOwner = original.ViewModel.OutsideDiscoveryOwner;
            if (originalOwner is not { } owner || !api.IsCurrentDiscoveryOwner(owner))
                throw new InvalidOperationException("Search did not capture an initial external-title owner.");
            navigation.NavigateImmediately(typeof(Page));
            auth.SelectProfile("fixture-profile", profile: new() { Id = "fixture-profile", Name = "Renamed fixture" });
            if (api.IsCurrentDiscoveryOwner(owner))
                throw new InvalidOperationException("Same-profile selection did not retire the old discovery owner in the fixture.");
            var reads = handler.Paths.Count(path => path == "GET /api/v2/requests/search");
            navigation.GoBackImmediately();
            await UntilAsync(() => ReferenceEquals(frame.Content, original) && original.IsLoaded);
            try
            {
                await UntilAsync(() => original.ViewModel.OutsideDiscoveryOwner is { } current && api.IsCurrentDiscoveryOwner(current) &&
                    !original.ViewModel.IsOutsideLoading && handler.Paths.Count(path => path == "GET /api/v2/requests/search") > reads);
            }
            catch (TimeoutException)
            {
                if (routedQuery) throw new InvalidOperationException("Routed-search positive control did not reload its replayed query after Back.");
                throw new InvalidOperationException("Retained search leaves external-title controls owned by a retired same-profile identity after Back.");
            }
            if (searchBox.Text != originalQuery || original.ViewModel.OutsideRequestedPage != 1)
                throw new InvalidOperationException("Refreshing retired external-title authority changed the retained search query or page.");
            Program.Log(routedQuery
                ? "PASS: Routed-search positive control refreshes external-title authority by replaying its query after Back."
                : "PASS: Back to typed retained search refreshes external-title authority after a same-profile edit without changing query/page.");
        }
        finally
        {
            await RequestFixtureLifecycle.DetachAsync(frame, () =>
            {
                if (frame.Content is SearchPage) frame.Navigate(typeof(Page));
                frame.BackStack.Clear(); frame.Content = null; navigation.Frame = null; parent.Children.Remove(frame);
            });
        }
    }

    private static async Task RetiredTitleLoadAsync(StackPanel parent, Handler handler, AuthService auth)
    {
        var navigation = SiloPlayer.App.Services.GetRequiredService<NavigationService>();
        var frame = new Frame { Width = 900, Height = 700 };
        handler.LibraryContentId = null;
        handler.DetailEntered = false;
        handler.DetailRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        parent.Children.Add(frame); navigation.Frame = frame;
        try
        {
            navigation.NavigateImmediately(typeof(RequestDetailPage), new RequestDetailNavigation("series", 42));
            await UntilAsync(() => handler.DetailEntered && frame.Content is RequestDetailPage);
            var page = (RequestDetailPage)frame.Content;
            auth.SelectProfile("fixture-profile", profile: new() { Id = "fixture-profile", Name = "Edited while loading" });
            handler.DetailRelease.TrySetResult(true);
            try
            {
                await UntilAsync(() => ((FrameworkElement)page.FindName("DetailSkeleton")).Visibility == Visibility.Collapsed &&
                    ((Button)page.FindName("RetryTitleButton")).Visibility == Visibility.Visible);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("Retired request-title load leaves a loading skeleton without Retry after an allowed identity refresh.");
            }
            Program.Log("PASS: a retired request-title load leaves a reachable Retry instead of an endless skeleton.");
            if (handler.Paths.Count(path => path.EndsWith("/requests/detail/series/42")) != 1)
                throw new InvalidOperationException("Retired title load fetched again before the explicit Retry.");
            var retry = (Button)page.FindName("RetryTitleButton");
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(retry)
                .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => ((FrameworkElement)page.FindName("LoadingLayer")).Visibility == Visibility.Collapsed);
            Program.Log("PASS: Retry loads the current allowed title after identity retirement.");
        }
        finally
        {
            handler.DetailRelease.TrySetResult(true); handler.DetailRelease = null;
            await RequestFixtureLifecycle.DetachAsync(frame, () =>
            {
                frame.Navigate(typeof(Page)); frame.Content = null; navigation.Frame = null; parent.Children.Remove(frame);
            });
        }
    }

    private static async Task CancelledWriteFeedbackAsync(StackPanel parent, Handler handler, SiloApiClient client)
    {
        var navigation = SiloPlayer.App.Services.GetRequiredService<NavigationService>();
        var toastService = SiloPlayer.App.Services.GetRequiredService<ToastService>();
        var toast = new SiloPlayer.Controls.ToastContainer();
        var frame = new Frame { Width = 900, Height = 700 };
        handler.LibraryContentId = null;
        handler.WriteEntered = false;
        handler.CancelReleasedWrite = true;
        handler.WriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        parent.Children.Add(frame); parent.Children.Add(toast); navigation.Frame = frame;
        toastService.Register(toast, parent.DispatcherQueue);
        RequestDetailPage? mountedPage = null;
        try
        {
            navigation.NavigateImmediately(typeof(RequestDetailPage), new RequestDetailNavigation("series", 42));
            await UntilAsync(() => frame.Content is RequestDetailPage loaded && loaded.IsLoaded &&
                ((FrameworkElement)loaded.FindName("LoadingLayer")).Visibility == Visibility.Collapsed);
            var page = (RequestDetailPage)frame.Content; mountedPage = page;
            var action = Descendants(page).OfType<Button>().Single(button =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "follow");
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(action);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => handler.WriteEntered);
            if (!client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true))
                throw new InvalidOperationException("Cancelled request-write fixture failed to advance transport context.");
            handler.WriteRelease.TrySetResult(true);
            await UntilAsync(() => action.IsEnabled);
            try { await UntilAsync(() => ((StackPanel)toast.FindName("ToastStack")).Children.Count > 0); }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("A transport-cancelled request write silently restores its old action without visible feedback.");
            }
            Program.Log("PASS: an active transport-cancelled request action reports visible feedback.");
            ((StackPanel)toast.FindName("ToastStack")).Children.Clear();
            handler.WriteEntered = false; handler.WriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
            action = Descendants(page).OfType<Button>().Single(button =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "follow");
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(action)
                .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => handler.WriteEntered);
            frame.Navigate(typeof(Page));
            handler.WriteRelease.TrySetResult(true);
            await UntilAsync(() => action.IsEnabled);
            if (((StackPanel)toast.FindName("ToastStack")).Children.Count != 0)
                throw new InvalidOperationException("An unloaded request action published stale cancellation feedback.");
            Program.Log("PASS: the same cancelled write stays quiet after its page lifetime unloads.");
        }
        finally
        {
            handler.WriteRelease.TrySetResult(true); handler.WriteRelease = null; handler.ResetActions();
            await RequestFixtureLifecycle.DetachAsync(frame, () =>
            {
                frame.Navigate(typeof(Page)); frame.Content = null; navigation.Frame = null;
                toastService.Unregister(); parent.Children.Remove(toast); parent.Children.Remove(frame);
            }, mountedPage);
        }
    }

    private static void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static async Task UntilAsync(Func<bool> condition)
    { var deadline = DateTime.UtcNow.AddSeconds(5); while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Native request action did not finish."); await Task.Delay(30); } }

    private static async Task SearchAndRulesAsync(StackPanel parent)
    {
        var dialog = new SiloPlayer.Controls.GlobalSearchDialog();
        await (Task)dialog.GetType().GetField("_scopeLoadTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        var box = (TextBox)dialog.FindName("SearchBox"); box.Text = "Fixture";
        ((List<SiloPlayer.Core.Models.Home.MediaItem>)dialog.GetType().GetField("_results", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Add(new() { ContentId = "title", Title = "Fixture title" });
        ((List<RequestMediaResult>)dialog.GetType().GetField("_requestResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Add(new() { MediaType = "series", TmdbId = 42, Title = "Fixture request" });
        Invoke(dialog, "Render");
        var selection = (SearchSelectionState)dialog.GetType().GetField("_selection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        selection.Move(1); selection.Move(1);
        ((List<Person>)dialog.GetType().GetField("_peopleResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Add(new() { Id = "person", Name = "Fixture Person" });
        Invoke(dialog, "Render");
        var rows = Descendants((StackPanel)dialog.FindName("ResultsPanel")).OfType<Button>().Where(row => row.Tag is int).ToArray();
        if (!rows.Select(row => (int)row.Tag).SequenceEqual(new[] { 0, 1, 2 }) || selection.Index != 2)
            throw new InvalidOperationException("Native quick search skips optional keyboard rows or loses selected identity.");
        box.Text = "Fixture Person";
        Invoke(dialog, "Render");
        rows = Descendants((StackPanel)dialog.FindName("ResultsPanel")).OfType<Button>().Where(row => row.Tag is int).ToArray();
        var firstTitle = ((Grid)((Border)rows[0].Content).Child).Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().First().Text;
        if (firstTitle != "Fixture Person" ||
            !rows.Select(row => (int)row.Tag).SequenceEqual(new[] { 0, 1, 2 }))
            throw new InvalidOperationException("Exact person search does not lead the native result groups.");
        ((List<SiloPlayer.Core.Models.Home.MediaItem>)dialog.GetType().GetField("_results", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Clear();
        ((List<Person>)dialog.GetType().GetField("_peopleResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Clear();
        ((List<RequestMediaResult>)dialog.GetType().GetField("_requestResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Clear();
        Field(dialog, "_peopleSearchFailed", true);
        Invoke(dialog, "Render");
        if (((TextBlock)dialog.FindName("EmptyText")).Visibility != Visibility.Visible ||
            !((TextBlock)dialog.FindName("EmptyText")).Text.Contains("Could not load results", StringComparison.Ordinal))
            throw new InvalidOperationException("An unavailable optional search source is reported as no matches.");
        var timer = dialog.GetType().GetField("_debounceTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog) as DispatcherTimer; timer?.Stop();

        var query = new SiloPlayer.Core.Models.Collections.QueryDefinition { Match = "any", Groups =
        [ new() { Match = "all", Rules = [new() { Field = "year", Op = "between", Value = new[] { 2020, 2026 } }] },
          new() { Match = "any", Rules = [new() { Field = "hdr", Op = "is", Value = false }] } ] };
        var editor = new SiloPlayer.Controls.QueryRulesEditor { Width = 480 }; editor.Load(query); parent.Children.Add(editor);
        editor.UpdateLayout(); await Task.Delay(50);
        var from = Descendants(editor).OfType<TextBox>().Single(box => box.PlaceholderText == "From" && box.Text == "2020");
        var to = Descendants(editor).OfType<TextBox>().Single(box => box.PlaceholderText == "To" && box.Text == "2026");
        if (from.Text != "2020" || to.Text != "2026" ||
            query.Groups[0].Rules[0].Value is not int[] savedRange || !savedRange.SequenceEqual(new[] { 2020, 2026 }) ||
            query.Groups[1].Rules[0].Value is not false) throw new InvalidOperationException("Native rule load changed typed values.");
        from.Text = "bad"; await UntilAsync(() => !editor.IsValid);
        from.Text = "2010"; to.Text = "2019";
        await UntilAsync(() => editor.IsValid && query.Groups[0].Rules[0].Value is double[]);
        if (!editor.IsValid || query.Groups[0].Rules[0].Value is not double[] values || !values.SequenceEqual(new[] { 2010d, 2019d }) || query.Match != "any" || query.Groups[0].Match != "all")
            throw new InvalidOperationException("Native rule edit lost range types or mixed group structure.");
        parent.Children.Remove(editor);
        var page = new SearchPage { Width = 900, Height = 700 }; parent.Children.Add(page);
        page.ViewModel.PeopleResults.Add(new() { Id = "fixture", Name = "Fixture Person" }); Invoke(page, "UpdatePeopleSection");
        if (((StackPanel)page.FindName("PeopleSection")).Visibility != Visibility.Visible) throw new InvalidOperationException("Native full search hides people results.");
        page.ViewModel.OutsidePage = 2; page.ViewModel.OutsideTotalPages = 4; Invoke(page, "UpdateRequestResults");
        if (((TextBlock)page.FindName("RequestPageText")).Text != "Page 2 of 4" || !((Button)page.FindName("RequestNextButton")).IsEnabled)
            throw new InvalidOperationException("Native external results paging has no reachable next page.");
        parent.Children.Remove(page);
        Program.Log("PASS: native quick/full people search, all-group selection, independent external paging and typed AND/OR rule editing.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string? LibraryContentId;
        private bool _following;
        private bool _watchlist;
        public TaskCompletionSource<bool>? DetailRelease;
        public TaskCompletionSource<bool>? WriteRelease;
        public bool DetailEntered;
        public bool WriteEntered;
        public bool CancelReleasedWrite;
        public void ResetActions() { _following = false; _watchlist = false; CancelReleasedWrite = false; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(request.Method + " " + path);
            if (path.EndsWith("/requests/detail/series/42") && DetailRelease is { } detailRelease)
            { DetailEntered = true; await detailRelease.Task.WaitAsync(ct); }
            if (path.Contains("/requests/follows/") && WriteRelease is { } writeRelease)
            {
                WriteEntered = true;
                await writeRelease.Task.WaitAsync(ct);
                if (CancelReleasedWrite) throw new OperationCanceledException("Fixture transport cancelled the active write.", ct);
            }
            object reply;
            if (path.EndsWith("/requests/status")) reply = new { requests_enabled = true, allowed = true, follow_supported = true, season_requests_supported = true, watchlist_titles_supported = true };
            else if (path.EndsWith("/requests/search")) reply = new
            {
                results = new[] { new { media_type = "movie", tmdb_id = 72, title = "Retained request", availability = "missing", request = new { requestable = true } } },
                total_pages = 1, total_results = 1, page = 1
            };
            else if (path.Contains("/requests/follows/")) { _following = request.Method == HttpMethod.Put; reply = new { following = _following }; }
            else if (path.Contains("/watchlist/titles/")) { _watchlist = request.Method == HttpMethod.Put; reply = new { media_type = "series", tmdb_id = 42 }; }
            else if (path.EndsWith("/requests/detail/series/42")) reply = new
            {
                media_type = "series", tmdb_id = 42, title = "Fixture series", availability = "missing", in_watchlist = _watchlist, library_content_id = LibraryContentId,
                seasons = new[] { new { season_number = 1, name = "Season 1", availability = "available", episode_count = 8 }, new { season_number = 2, name = "Season 2", availability = "partial", episode_count = 8 } },
                request = new { status = "downloading", state = "partially_available", reason = "already_requested", requested_by_viewer = false, following = _following, download = new { phase = "import_blocked", percent = 100 } }
            };
            else if (path.EndsWith("/watchlist/titles")) reply = new { items = new[] { new { media_type = "series", tmdb_id = 42, title = "Fixture series", status = "active", request = new { status = "pending", state = "pending" } } } };
            else if (path.EndsWith("/settings/contract/capabilities")) reply = new { manifest_revision = 15 };
            else reply = new { items = Array.Empty<object>() };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(reply)) };
        }
    }
}
