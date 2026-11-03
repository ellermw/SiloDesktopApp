using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class BoxSetSearchNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://boxset-search-native.invalid");
        var catalog = new CatalogApi(client); var people = new PeopleApi(client);
        var requests = new RequestsApi(client); var settings = new SettingsApi(client);
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("boxset-search", profile: new() { Id = "boxset-search", Name = "Fixture" });
        var model = new SearchViewModel(catalog, people, requests, settings);
        var routes = new List<(Type Type, object? Args)>();
        var navigation = new NavigationService { NavigationRequestHandler = (type, args) => { routes.Add((type, args)); return true; } };
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth,
            [typeof(CatalogApi)] = catalog, [typeof(PeopleApi)] = people,
            [typeof(RequestsApi)] = requests, [typeof(SettingsApi)] = settings,
            [typeof(SearchViewModel)] = model, [typeof(NavigationService)] = navigation,
            [typeof(UICustomizationService)] = new UICustomizationService(settings),
        }));
        var frame = new Frame { Width = 960, Height = 900 }; parent.Children.Add(frame); navigation.Frame = frame;
        try
        {
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") is "boxset-search-quick" or "boxset-search-selection")
                await QuickAsync(wire, client, catalog, routes);
            else await FullAsync(frame, model, wire, client, catalog, routes);
            Check(wire.WatchReads == 0, "aggregate search never enters normal playback preparation");
        }
        finally
        {
            model.CancelPendingSearch();
            if (frame.Content is Page current) current.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
            frame.CacheSize = 0; frame.BackStack.Clear(); frame.ForwardStack.Clear(); frame.Content = null; navigation.Frame = null;
            await Task.Delay(150); parent.Children.Remove(frame); field.SetValue(null, previous);
        }
    }

    private static async Task FullAsync(Frame frame, SearchViewModel model, Wire wire, SiloApiClient client, CatalogApi catalog, List<(Type Type, object? Args)> routes)
    {
        frame.Navigate(typeof(SearchPage)); await Task.Delay(120);
        var page = (SearchPage)frame.Content;
        var initialization = typeof(SearchPage).GetField("_initializationTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page) as Task;
        if (initialization != null) await initialization;
        wire.HoldNext(); model.Query = "Film"; await model.SearchCommand.ExecuteAsync(null);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(model.Results.Count == 1 && !model.IsLoading && model.BoxSets.IsLoading, "full ordinary results publish during delayed optional search");
        wire.Unhold(); await Until(() => model.BoxSets.Items.Count == 1); await Task.Delay(120); page.UpdateLayout();
        var section = (FrameworkElement)page.FindName("BoxSetSearchSection");
        Check(section.Visibility == Visibility.Visible, "full search renders optional box-set section");
        var aggregate = Descendants<BoxSetCard>(section).Single();
        Check(aggregate.Focus(FocusState.Keyboard), "full search aggregate accepts keyboard focus");
        Invoke(aggregate); await Task.Delay(40);
        Check(routes[^1] is { Type: var type, Args: BoxSetNavigationArgs { Id: "7", LibraryId: "12" } } && type == typeof(BoxSetPage), "full search uses dedicated returned-library destination");
        Check(model.Results.Count == 1 && model.TotalCount == 60, "aggregate does not alter ordinary search totals");
        Check(Descendants<DefaultArtwork>(aggregate).Any() && !Descendants<Button>(aggregate).Any(), "full missing art has native fallback and no title menu or play button");
        var titleLayout = (UniformGridLayout)page.FindName("ResultsGridLayout");
        Check(Math.Abs(aggregate.ActualWidth - titleLayout.MinItemWidth) < 1, "full aggregate shares ordinary title column width");
        Check(section.TransformToVisual(page).TransformPoint(new()).Y < ((FrameworkElement)page.FindName("ResultsRepeater")).TransformToVisual(page).TransformPoint(new()).Y, "full section precedes title grid");
        await MediaParityNativeFixture.CaptureAsync(section, "boxset-search-full-wide.png");
        frame.Width = 460; page.UpdateLayout(); await Task.Delay(120);
        aggregate = Descendants<BoxSetCard>(section).Single();
        Check(section.ActualWidth <= 460 && aggregate.ActualWidth <= section.ActualWidth && Math.Abs(aggregate.ActualWidth - titleLayout.MinItemWidth) < 1, "compact full section retains matching bounded columns");
        await MediaParityNativeFixture.CaptureAsync(section, "boxset-search-full-compact.png");
        var before = wire.SearchReads; var ordinaryReads = wire.CatalogReads;
        wire.Played = 2;
        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "film", context: client.CaptureContext()));
        await Until(() => wire.SearchReads > before && model.BoxSets.Items.FirstOrDefault()?.PlayedCount == 2);
        Check(wire.CatalogReads == ordinaryReads, "watched refresh updates optional counts independently of ordinary window");
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        before = wire.SearchReads;
        WeakReferenceMessenger.Default.Send(new PlaybackProgressUpdated("film", 100, 100, true) { Persisted = persisted.Task });
        await Task.Delay(60); Check(wire.SearchReads == before, "playback summary refresh waits for final write");
        persisted.TrySetResult(); await Until(() => wire.SearchReads > before);
        await Task.Delay(80); aggregate = Descendants<BoxSetCard>(section).Single();
        var routeCount = routes.Count; model.Query = "New query"; Invoke(aggregate); await Task.Delay(40);
        Check(routes.Count == routeCount && model.BoxSets.Items.Count == 0, "full stale aggregate callback retires immediately on query edit");
        wire.HoldNext(); await model.SearchCommand.ExecuteAsync(null); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        frame.Navigate(typeof(Page)); wire.Unhold(); await Task.Delay(100);
        Check(model.BoxSets.Items.Count == 0 && !model.BoxSets.IsLoading, "actual page navigation retires delayed optional completion");
        frame.Navigate(typeof(SearchPage)); await Task.Delay(80); page = (SearchPage)frame.Content;
        wire.Enabled = false; model.Query = "Disabled"; before = wire.SearchReads; await model.SearchCommand.ExecuteAsync(null); await Until(() => !model.BoxSets.IsLoading);
        Check(wire.SearchReads == before && model.Results.Count == 1 && model.BoxSets.Items.Count == 0, "full disabled viewer library skips optional endpoint and keeps titles");
        wire.Enabled = true; wire.Fail = true; model.Query = "NoTitles"; await model.SearchCommand.ExecuteAsync(null); await Until(() => !model.BoxSets.IsLoading); await Task.Delay(80);
        Check(model.ErrorMessage == null && ((FrameworkElement)page.FindName("NoResultsText")).Visibility == Visibility.Visible, "full optional failure settles empty state without primary error");
        wire.Fail = false; wire.HoldNext(); model.Query = "Profile pending"; await model.SearchCommand.ExecuteAsync(null); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        client.SetProfile("other"); wire.Unhold(); await Until(() => !model.BoxSets.IsLoading);
        Check(model.BoxSets.Items.Count == 0, "full delayed read cannot publish under another profile");
        page.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
        frame.Navigate(typeof(Page)); frame.CacheSize = 0; frame.BackStack.Clear();
    }

    private static async Task QuickAsync(Wire wire, SiloApiClient client, CatalogApi catalog, List<(Type Type, object? Args)> routes)
    {
        wire.RequestsEnabled = true;
        var owner = new Grid(); var window = new Window { Content = owner }; GlobalSearchDialog? active = null;
        window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(960, 900)); window.AppWindow.Show(false); await Task.Delay(100);
        async Task<(GlobalSearchDialog Dialog, Task Closed)> Open(string query, bool waitBoxes = true)
        {
            await Task.Delay(60);
            var dialog = active = new GlobalSearchDialog { XamlRoot = owner.XamlRoot };
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Opened += (_, _) => opened.TrySetResult();
            var closed = dialog.ShowAsync().AsTask();
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Until(() => ((TextBox)dialog.FindName("SearchBox")).IsLoaded);
            ((TextBox)dialog.FindName("SearchBox")).Text = query;
            await Until(() => Rows(dialog).Any(x => AutomationProperties.GetName(x).StartsWith("Open Film,")) || query == "NoTitles" && ((TextBlock)dialog.FindName("EmptyText")).Visibility == Visibility.Visible);
            if (waitBoxes) await Until(() => !State(dialog).IsLoading);
            await Until(() => !Field<bool>(dialog, "_requestsPending"));
            await Task.Delay(60); return (dialog, closed);
        }
        try
        {
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "boxset-search-selection")
            {
                var (dialog, closed) = await Open("Film");
                var aggregateSearch = (TextBox)dialog.FindName("SearchBox"); aggregateSearch.Focus(FocusState.Programmatic);
                var aggregateSelection = Field<SearchSelectionState>(dialog, "_selection");
                SendKey(0x28); await Task.Delay(60); Check(aggregateSelection.Index == 0, "actual keyboard selects aggregate before refresh");
                wire.HoldNext(); WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "film", context: client.CaptureContext()));
                await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var retainedDuring = aggregateSelection.Index == 0;
                wire.Unhold(); await Until(() => !State(dialog).IsLoading); await Task.Delay(60);
                var retainedAfter = aggregateSelection.Index == 0;
                for (var i = 0; i < 6; i++) { SendKey(0x26); await Task.Delay(20); }
                SendKey(0x28); await Task.Delay(40); Check(aggregateSelection.Index == 0, "actual keyboard reselects aggregate for newer-input control");
                wire.HoldNext(); WeakReferenceMessenger.Default.Send(new PlaybackProgressUpdated("film", 100, 100, true));
                await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                for (var i = 0; i < 6; i++) { SendKey(0x26); await Task.Delay(20); }
                SendKey(0x28); await Task.Delay(40); SendKey(0x28); await Task.Delay(40);
                wire.Unhold(); await Until(() => !State(dialog).IsLoading); await Task.Delay(60);
                Check(aggregateSelection.Index == 1, "newer keyboard title selection wins over aggregate selected before final-write refresh");
                dialog.Hide(); await closed;
                Program.Log($"TRACE aggregate refresh retainedDuring={retainedDuring}, retainedAfter={retainedAfter}.");
                Check(retainedDuring && retainedAfter, "same-query watched refresh preserves selected aggregate through atomic publication");
                return;
            }
            wire.HoldNext(); var (delayed, delayedClosed) = await Open("Film", false); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var originalTitle = Rows(delayed).Single(x => AutomationProperties.GetName(x).StartsWith("Open Film,"));
            var search = (TextBox)delayed.FindName("SearchBox"); search.Focus(FocusState.Programmatic);
            SendKey(0x28); await Task.Delay(60); var selection = Field<SearchSelectionState>(delayed, "_selection");
            Check(selection.Index == 0 && ((FrameworkElement)delayed.FindName("LoadingPanel")).Visibility == Visibility.Collapsed, "quick titles stay keyboard usable during delayed box-set read");
            wire.Unhold(); await Until(() => State(delayed).Items.Count == 1); await Task.Delay(80);
            Check(selection.Index == 1 && AutomationProperties.GetName(Rows(delayed)[0]).Contains("Box set"), "late quick box-set group leads while title selection follows identity");
            Check(Descendants<DefaultArtwork>(Rows(delayed)[0]).Any(), "quick missing art uses native fallback");
            await MediaParityNativeFixture.CaptureAsync(delayed, "boxset-search-quick-wide.png");
            Invoke(originalTitle); await delayedClosed;
            Check(routes[^1].Type == typeof(ItemDetailPage) && routes[^1].Args as string == "film", "retained ordinary quick callback resolves shifted index by identity");
            var (aggregateDialog, aggregateClosed) = await Open("Film");
            search = (TextBox)aggregateDialog.FindName("SearchBox"); search.Focus(FocusState.Programmatic); SendKey(0x28); await Task.Delay(60); SendKey(0x0D); await aggregateClosed;
            Check(routes[^1].Type == typeof(BoxSetPage) && routes[^1].Args is BoxSetNavigationArgs { Id: "7", LibraryId: "12" }, "actual quick Down Enter uses aggregate returned-library destination");
            var (titleDialog, titleClosed) = await Open("Film");
            search = (TextBox)titleDialog.FindName("SearchBox"); search.Focus(FocusState.Programmatic); SendKey(0x28); await Task.Delay(40); SendKey(0x28); await Task.Delay(40); SendKey(0x0D); await titleClosed;
            Check(routes[^1].Type == typeof(ItemDetailPage) && routes[^1].Args as string == "film", "actual quick title keyboard route remains ordinary item detail");
            var (personDialog, personClosed) = await Open("Film");
            search = (TextBox)personDialog.FindName("SearchBox"); search.Focus(FocusState.Programmatic); for (var i = 0; i < 3; i++) { SendKey(0x28); await Task.Delay(40); } SendKey(0x0D); await personClosed;
            Check(routes[^1].Type == typeof(PersonDetailPage) && routes[^1].Args as string == "person", "quick person global index includes leading box-set count");
            var (requestDialog, requestClosed) = await Open("Film");
            search = (TextBox)requestDialog.FindName("SearchBox"); search.Focus(FocusState.Programmatic); for (var i = 0; i < 4; i++) { SendKey(0x28); await Task.Delay(40); } SendKey(0x0D); await requestClosed;
            Check(routes[^1].Type == typeof(RequestDetailPage) && routes[^1].Args is RequestDetailNavigation { MediaType: "movie", TmdbId: 42 }, "quick request global index includes box sets titles and people");
            var (exactDialog, exactClosed) = await Open("A Person");
            search = (TextBox)exactDialog.FindName("SearchBox"); search.Focus(FocusState.Programmatic); SendKey(0x28); await Task.Delay(40); SendKey(0x28); await Task.Delay(40); SendKey(0x0D); await exactClosed;
            Check(routes[^1].Type == typeof(PersonDetailPage) && routes[^1].Args as string == "person", "exact people match remains ahead of titles behind box sets");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(460, 800)); await Task.Delay(100);
            var (compact, compactClosed) = await Open("Film");
            var boxRow = Rows(compact)[0];
            Check(((FrameworkElement)compact.FindName("SearchSurface")).ActualWidth <= 428 && Descendants<Border>(boxRow).Any(x => x.Tag as string == "QuickSelectedEnter" && x.Visibility == Visibility.Collapsed), "compact quick row fits and hides keyboard hint");
            await MediaParityNativeFixture.CaptureAsync(compact, "boxset-search-quick-compact.png");
            var count = routes.Count; wire.HoldNext(); ((TextBox)compact.FindName("SearchBox")).Text = "Pending"; Invoke(boxRow); await Task.Delay(40);
            Check(routes.Count == count && !Rows(compact).Any(), "quick query edit removes retired rows and rejects old aggregate callback during debounce");
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2)); compact.Hide(); await compactClosed; wire.Unhold(); await Task.Delay(80);
            Check(State(compact).Items.Count == 0 && routes.Count == count, "quick close retires delayed box-set completion");
            var (disabled, disabledClosed) = await Open("Film"); var reads = wire.SearchReads; wire.Enabled = false; catalog.InvalidateLibraryCache(); await Until(() => !State(disabled).IsLoading);
            Check(wire.SearchReads == reads && State(disabled).Items.Count == 0 && Rows(disabled).Any(x => AutomationProperties.GetName(x).StartsWith("Open Film,")), "quick library disable hides cached aggregates without unsupported read or lost titles");
            disabled.Hide(); await disabledClosed; wire.Enabled = true; wire.Fail = true;
            var (failed, failedClosed) = await Open("Film"); Check(State(failed).Items.Count == 0 && Rows(failed).Any(), "quick optional failure keeps ordinary results"); failed.Hide(); await failedClosed;
            var (empty, emptyClosed) = await Open("NoTitles"); Check(((TextBlock)empty.FindName("EmptyText")).Text == "No matches", "quick optional failure settles no matches"); empty.Hide(); await emptyClosed;
            wire.Fail = false; wire.HoldNext(); var (profile, profileClosed) = await Open("Profile pending", false); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            client.SetProfile("other"); wire.Unhold(); await Until(() => !State(profile).IsLoading);
            Check(State(profile).Items.Count == 0, "quick delayed response cannot publish for another profile"); profile.Hide(); await profileClosed;
        }
        finally { active?.Hide(); wire.Unhold(); await Task.Delay(100); window.Close(); }
    }

    private static Button[] Rows(GlobalSearchDialog dialog) => Descendants<Button>(dialog).Where(x => x.Tag is int).ToArray();
    private static BoxSetSearchState State(GlobalSearchDialog dialog) => Field<BoxSetSearchState>(dialog, "_boxSets");
    private static T Field<T>(object value, string field) => (T)value.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 160 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Box-set search native controls did not settle."); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Program.Log("PASS: " + message); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T typed) yield return typed; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static void SendKey(nuint key) { var target = GetFocus(); GetWindowThreadProcessId(target, out var process); if (target == 0 || process != (uint)Environment.ProcessId) throw new InvalidOperationException("Key target is not an owned fixture HWND."); if (!PostMessage(target, 0x0100, key, 1) || !PostMessage(target, 0x0101, key, unchecked((nint)0xC0000001u))) throw new InvalidOperationException("Could not deliver key to owned HWND."); }
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint target, out uint process);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint target, uint message, nuint wParam, nint lParam);
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values) : IServiceProvider
    { public object? GetService(Type type) => values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool Enabled = true, Fail, Hold, RequestsEnabled; internal int SearchReads, CatalogReads, WatchReads, Played;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void HoldNext() { Hold = true; Started = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        internal void Unhold() { Hold = false; Release.TrySetResult(); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "boxset-search-native.invalid") throw new InvalidOperationException("Fixture attempted external network.");
            var path = request.RequestUri.AbsolutePath; var body = "{\"items\":[],\"page\":{\"has_more\":false}}"; var status = HttpStatusCode.OK;
            var empty = request.RequestUri.Query.Contains("NoTitles");
            if (path == "/api/v2/catalog/search/capabilities") body = "{\"box_sets\":true,\"people_media_scope\":true}";
            if (path == "/api/v2/user/libraries") body = "{\"items\":[{\"id\":12,\"enabled\":true,\"box_sets_enabled\":" + Enabled.ToString().ToLowerInvariant() + "}]}";
            if (path == "/api/v2/catalog") { CatalogReads++; if (!empty) body = "{\"items\":[{\"content_id\":\"film\",\"title\":\"Film\",\"type\":\"movie\"}],\"total\":60,\"page\":{\"has_more\":false}}"; }
            if (path == "/api/v2/catalog/people" && !empty) body = "{\"items\":[{\"id\":\"person\",\"name\":\"A Person\"}]}";
            if (path == "/api/v2/catalog/box-sets")
            {
                SearchReads++; status = Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
                body = "{\"items\":[{\"id\":\"7\",\"library_id\":\"12\",\"title\":\"Films Box Set With A Long Synthetic Title\",\"movie_count\":3,\"played_count\":" + Played + ",\"first_year\":2001,\"last_year\":2005}]}";
                Started.TrySetResult(); if (Hold) await Release.Task;
            }
            if (path.StartsWith("/api/v2/watch/")) { WatchReads++; throw new InvalidOperationException("Unexpected playback request."); }
            if (path == "/api/v2/requests/status") body = "{\"requests_enabled\":" + RequestsEnabled.ToString().ToLowerInvariant() + "}";
            if (path == "/api/v2/requests/search") body = empty ? "{\"results\":[]}" : "{\"results\":[{\"media_type\":\"movie\",\"tmdb_id\":42,\"title\":\"Requested Film\",\"availability\":\"unavailable\",\"request\":{\"requestable\":true}}]}";
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
