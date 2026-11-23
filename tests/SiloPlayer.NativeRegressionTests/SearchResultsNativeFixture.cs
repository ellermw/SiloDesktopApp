using System.Net;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Helpers;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

/// <summary>
/// The official search results page (silo-server 3b2b32168, #4450) on synthetic data: the overview with library and
/// request results, the library-only and request views, nothing found and a failed search, at 1440 and about 800 wide.
/// </summary>
internal static class SearchResultsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://search-results-native.invalid");
        var catalog = new CatalogApi(client); var people = new PeopleApi(client);
        var requests = new RequestsApi(client); var settings = new SettingsApi(client);
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("search-results", profile: new() { Id = "search-results", Name = "Fixture" });
        var model = new SearchViewModel(catalog, people, requests, settings);
        var navigation = new NavigationService { NavigationRequestHandler = (_, _) => true };
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth,
            [typeof(CatalogApi)] = catalog, [typeof(PeopleApi)] = people,
            [typeof(RequestsApi)] = requests, [typeof(SettingsApi)] = settings,
            [typeof(SearchViewModel)] = model, [typeof(NavigationService)] = navigation,
            [typeof(UICustomizationService)] = new UICustomizationService(settings),
        }));
        var host = new Grid(); var window = new Window { Content = host };
        window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1500, 3700)); window.AppWindow.Show(false); await Task.Delay(120);
        _windowHeight = window.AppWindow.ClientSize.Height; Program.Log("Window client height " + _windowHeight);
        var frame = new Frame { Width = 1440, Height = 2300, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        host.Children.Add(frame); navigation.Frame = frame;
        try
        {
            foreach (var width in new[] { 1440, 800 })
                await RunWidthAsync(frame, model, wire, width);
        }
        finally
        {
            model.CancelPendingSearch();
            if (frame.Content is Page current) current.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
            frame.CacheSize = 0; frame.BackStack.Clear(); frame.ForwardStack.Clear(); frame.Content = null; navigation.Frame = null;
            await Task.Delay(150); window.Close(); field.SetValue(null, previous);
        }
    }

    private static async Task RunWidthAsync(Frame frame, SearchViewModel model, Wire wire, int width)
    {
        var tag = width.ToString();
        int H(int wide, int narrow) => width >= 1000 ? wide : narrow;
        frame.Width = width; frame.Height = 2300;
        frame.Navigate(typeof(SearchPage)); await Task.Delay(150);
        var page = (SearchPage)frame.Content;
        var initialization = typeof(SearchPage).GetField("_initializationTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page) as Task;
        if (initialization != null) await initialization;
        page.UpdateLayout();

        // ---- Overview: library rows, the request row in its fixed spot, people, more results ----
        await SearchAsync(page, model, "heat", () => !model.IsLoading && model.Results.Count == 60 && model.OutsideSettled && model.PeopleResults.Count > 0 && model.BoxSets.Items.Count > 0 && !model.BoxSets.IsLoading);
        var columns = (int)((UniformGridLayout)page.FindName("BestGridLayout")).MaximumRowsOrColumns;
        var best = (ItemsRepeater)page.FindName("BestRepeater"); var more = (ItemsRepeater)page.FindName("MoreRepeater");
        Check(Count(best) == columns * 2, $"{tag}: overview shows two rows of best matches ({columns} columns)");
        Check(Count(more) == columns * 3, $"{tag}: overview shows three rows of more results");
        Check(Text(page, "FirstRowsTitle") == "Best matches" && Text(page, "MoreResultsTitle") == "More results", $"{tag}: relevance headings read Best matches and More results");
        Check(Text(page, "ViewLibraryCount") == "60+", $"{tag}: library tab count reads 60+ while the server has more");
        Check(Text(page, "ViewRequestsCount") == "18+", $"{tag}: request tab count counts the titles worth requesting and reads + with more pages");
        Check(Visible(page, "ViewRequestsButton") && Visible(page, "RequestOverviewSection") && !Visible(page, "RequestResultsSection"), $"{tag}: request row is one row between the library's rows");
        var bestY = Y(page, "FirstRowsSection"); var requestY = Y(page, "RequestOverviewSection"); var peopleY = Y(page, "PeopleSection"); var moreY = Y(page, "MoreResultsSection");
        Check(bestY < requestY && requestY < peopleY && peopleY < moreY, $"{tag}: best matches, then Not in your library, then people, then more results");
        Check(Visible(page, "ShowAllResultsButton") && Visible(page, "MoreShowAllLink") && !Visible(page, "FirstShowAllButton") && !Visible(page, "SortedByPanel"), $"{tag}: Show all results appears beside More results and under it");
        Check(Visible(page, "BoxSetSearchSection") && Y(page, "BoxSetSearchSection") < bestY, $"{tag}: box sets lead the overview");
        Check(!Visible(page, "ResultOrderCombo"), $"{tag}: Relevance has no order of its own");
        await Capture(frame, $"search-results-{tag}-overview.png", H(2300, 3400));

        // ---- Another sort: one sorted list, the request row two rows in, people last ----
        var sortCombo = (ComboBox)page.FindName("ResultSortCombo");
        SelectTag(sortCombo, "title"); await Until(() => !model.IsLoading && model.Results.Count == 60 && model.SortField == "title"); await Task.Delay(250); page.UpdateLayout();
        Check(wire.LastCatalogQuery.Contains("sort=title"), $"{tag}: a picked sort is sent");
        Check(Text(page, "FirstRowsTitle") == "In your library" && Visible(page, "SortedByPanel") && Text(page, "SortedByText") == "Sorted by Title", $"{tag}: another sort reads as one list with a Sorted by line");
        Check(!Visible(page, "MoreResultsHeader") && Visible(page, "FirstShowAllButton") && Grid.GetRow((FrameworkElement)page.FindName("PeopleSection")) == 5, $"{tag}: no More results heading, Show all in the header, people last");
        await Capture(frame, $"search-results-{tag}-overview-sorted.png", H(2300, 3400));
        Invoke((Button)page.FindName("BackToRelevanceButton")); await Until(() => !model.IsLoading && model.SortsByRelevance && model.Results.Count == 60); await Task.Delay(250);
        Check(wire.LastCatalogQuery.Contains("sort=-relevance") && Text(page, "FirstRowsTitle") == "Best matches", $"{tag}: Back to relevance sends sort=-relevance and restores Best matches");

        // ---- In your library: every library result ----
        Invoke((Button)page.FindName("ViewLibraryButton")); await Task.Delay(300); page.UpdateLayout();
        Check(Visible(page, "LibraryViewSection") && !Visible(page, "FirstRowsSection") && !Visible(page, "MoreResultsSection") && !Visible(page, "PeopleSection") && !Visible(page, "RequestOverviewSection"), $"{tag}: library view holds only the library list");
        Check(Text(page, "LibraryViewTitle") == "All results in your library", $"{tag}: library view heading");
        await Capture(frame, $"search-results-{tag}-library.png", 1100);

        // ---- Request to add: the paged request grid ----
        Invoke((Button)page.FindName("ViewRequestsButton")); await Until(() => Visible(page, "RequestResultsSection")); await Task.Delay(250); page.UpdateLayout();
        Check(Text(page, "RequestPageText") == "Page 1 of 9" && Visible(page, "RequestPagingPanel") && !((Button)page.FindName("RequestPreviousButton")).IsEnabled, $"{tag}: request pager reads Page 1 of 9 with Previous off");
        Check(!Visible(page, "RequestNoLibraryMatchText"), $"{tag}: the library had hits, so no no-match line");
        await Capture(frame, $"search-results-{tag}-requests.png", H(1500, 2700));
        Invoke((Button)page.FindName("RequestNextButton")); await Until(() => wire.LastRequestPage == 2 && Text(page, "RequestPageText") == "Page 2 of 9"); page.UpdateLayout();
        Check(wire.LastRequestPage == 2, $"{tag}: Next reads the second request page");
        wire.FailRequestPage = 3;
        Invoke((Button)page.FindName("RequestNextButton")); await Until(() => Visible(page, "RequestErrorPanel")); await Task.Delay(200); page.UpdateLayout();
        Check(Text(page, "RequestErrorText") == "Couldn’t load page 3." && Text(page, "RequestPageText") == "Page 3" && Visible(page, "RequestFirstPageButton"), $"{tag}: a failed later page says so, offers Retry and Back to page 1");
        await Capture(frame, $"search-results-{tag}-requests-page-failed.png", 700);
        wire.FailRequestPage = 0;
        Invoke((Button)page.FindName("RequestFirstPageButton")); await Until(() => wire.LastRequestPage == 1 && Text(page, "RequestPageText") == "Page 1 of 9");
        Check(!Visible(page, "RequestErrorPanel"), $"{tag}: Back to page 1 recovers");
        // Leaving the request view starts its pager over.
        Invoke((Button)page.FindName("RequestNextButton")); await Until(() => Text(page, "RequestPageText") == "Page 2 of 9");
        Invoke((Button)page.FindName("ViewOverviewButton")); await Until(() => wire.LastRequestPage == 1 && Visible(page, "RequestOverviewSection")); await Task.Delay(200);
        Check(Visible(page, "FirstRowsSection") && Visible(page, "RequestOverviewSection"), $"{tag}: Overview returns to the first request page");

        // ---- Nothing found ----
        await SearchAsync(page, model, "nothing", () => !model.IsLoading && model.LibraryResultsKnown && model.OutsideSettled && !model.BoxSets.IsLoading);
        await Task.Delay(300); page.UpdateLayout();
        Check(Visible(page, "NoResultsText") && !Visible(page, "RequestOverviewSection") && !Visible(page, "RequestResultsSection") && !Visible(page, "ViewRequestsButton"), $"{tag}: nothing found says so and offers no request view");
        await Capture(frame, $"search-results-{tag}-no-results.png", 600);

        // ---- Library has nothing, discovery does: the request grid is the answer ----
        await SearchAsync(page, model, "only requests", () => !model.IsLoading && model.LibraryResultsKnown && model.OutsideSettled && model.OutsideLibraryResults.Count > 0);
        await Task.Delay(300); page.UpdateLayout();
        Check(Visible(page, "RequestResultsSection") && Visible(page, "RequestNoLibraryMatchText") && !Visible(page, "RequestOverviewSection") && !Visible(page, "NoResultsText"), $"{tag}: with an empty library the request grid takes the spot and says nothing matched");
        await Capture(frame, $"search-results-{tag}-requests-only.png", H(1500, 2700));

        // ---- A failed search ----
        await SearchAsync(page, model, "broken", () => !model.IsLoading && model.ErrorMessage != null && model.OutsideSettled);
        await Task.Delay(300); page.UpdateLayout();
        Check(Visible(page, "SearchErrorCard") && !Visible(page, "FirstRowsSection") && Visible(page, "RequestResultsSection"), $"{tag}: a failed search shows the error card and the request results");
        Check(!Visible(page, "NoResultsText"), $"{tag}: a failed search never says nothing was found");
        Check(Y(page, "SearchErrorCard") < Y(page, "RequestResultsSection"), $"{tag}: the error card leads, the request results follow");
        await Capture(frame, $"search-results-{tag}-failed.png", H(1500, 2700));

        page.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
        frame.Navigate(typeof(Page)); frame.CacheSize = 0; frame.BackStack.Clear();
        await Task.Delay(100);
    }

    private static async Task SearchAsync(SearchPage page, SearchViewModel model, string query, Func<bool> settled)
    {
        ((TextBox)page.FindName("SearchBox")).Text = query;
        await Until(settled); await Task.Delay(300); page.UpdateLayout();
    }

    private static int _windowHeight;
    // The page scrolls as one in the browser, so a capture lays it out in a frame as tall as its content. A window that
    // cannot be that tall is captured in screens, scrolling the results between them.
    private static async Task Capture(Frame frame, string name, int height)
    {
        var shown = Math.Min(height, _windowHeight);
        frame.Height = shown; frame.UpdateLayout(); await Task.Delay(250); frame.UpdateLayout();
        await MediaParityNativeFixture.CaptureAsync(frame, name);
        if (shown >= height || frame.Content is not Page page) return;
        var scroll = (ScrollViewer)page.FindName("ResultsScroll");
        for (var part = 2; part <= 3; part++)
        {
            var before = scroll.VerticalOffset;
            scroll.ChangeView(null, before + scroll.ViewportHeight * 0.9, null, disableAnimation: true); await Task.Delay(300);
            if (scroll.VerticalOffset <= before + 1) break;
            await MediaParityNativeFixture.CaptureAsync(frame, name.Replace(".png", $"-part{part}.png"));
        }
        scroll.ChangeView(null, 0, null, disableAnimation: true); await Task.Delay(150);
    }

    private static int Count(ItemsRepeater repeater) => (repeater.ItemsSource as System.Collections.ICollection)?.Count ?? 0;
    private static bool Visible(Page page, string name) => ((UIElement)page.FindName(name)).Visibility == Visibility.Visible;
    private static string Text(Page page, string name) => ((TextBlock)page.FindName(name)).Text;
    private static double Y(Page page, string name) => ((FrameworkElement)page.FindName(name)).TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).Y;
    private static void SelectTag(ComboBox combo, string tag)
    {
        var item = combo.Items.OfType<ComboBoxItem>().First(x => Equals(x.Tag, tag)); combo.SelectedItem = item;
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 240 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Search results native controls did not settle."); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Program.Log("PASS: " + message); }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values) : IServiceProvider
    { public object? GetService(Type type) => values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }

    private sealed class Wire : HttpMessageHandler
    {
        internal string LastCatalogQuery = ""; internal int LastRequestPage; internal int FailRequestPage;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "search-results-native.invalid") throw new InvalidOperationException("Fixture attempted external network.");
            var path = request.RequestUri.AbsolutePath; var query = Uri.UnescapeDataString(request.RequestUri.Query);
            var status = HttpStatusCode.OK; var body = "{\"items\":[],\"page\":{\"has_more\":false}}";
            var noLibrary = query.Contains("q=nothing") || query.Contains("q=only requests");
            var empty = query.Contains("q=nothing");
            if (path == "/api/v2/catalog")
            {
                LastCatalogQuery = query;
                if (query.Contains("q=broken")) { status = HttpStatusCode.ServiceUnavailable; body = "{\"message\":\"down\"}"; }
                else if (!noLibrary)
                    body = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        items = Enumerable.Range(0, 60).Select(i => new { content_id = "film-" + i, title = Titles[i % Titles.Length] + (i >= Titles.Length ? " " + (i / Titles.Length + 1) : ""), type = i % 4 == 3 ? "series" : "movie", year = 1980 + (i * 7 % 44) }),
                        total = 60, page = new { has_more = true }
                    });
            }
            else if (path == "/api/v2/catalog/search/capabilities") body = "{\"box_sets\":true,\"people_media_scope\":true}";
            else if (path == "/api/v2/user/libraries") body = "{\"items\":[{\"id\":12,\"enabled\":true,\"box_sets_enabled\":true}]}";
            else if (path == "/api/v2/catalog/box-sets" && !noLibrary && !query.Contains("q=broken"))
                body = "{\"items\":[{\"id\":\"7\",\"library_id\":\"12\",\"title\":\"Heat Collection\",\"movie_count\":3,\"played_count\":1,\"first_year\":1995,\"last_year\":2005}]}";
            else if (path == "/api/v2/catalog/people" && !noLibrary)
                body = "{\"items\":[{\"id\":\"p1\",\"name\":\"Al Pacino\"},{\"id\":\"p2\",\"name\":\"Robert De Niro\"},{\"id\":\"p3\",\"name\":\"Val Kilmer\"},{\"id\":\"p4\",\"name\":\"Natalie Portman\"}]}";
            else if (path == "/api/v2/requests/status") body = "{\"requests_enabled\":true,\"watchlist_titles_supported\":true}";
            else if (path == "/api/v2/requests/search")
            {
                var page = int.Parse(System.Text.RegularExpressions.Regex.Match(request.RequestUri.Query, "page=(\\d+)").Groups[1].Value);
                LastRequestPage = page;
                if (FailRequestPage == page) { status = HttpStatusCode.BadGateway; body = "{\"message\":\"upstream\"}"; }
                else if (empty) body = "{\"page\":1,\"total_pages\":0,\"total_results\":0,\"results\":[]}";
                else
                    body = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        page, total_pages = 9, total_results = 170,
                        results = Enumerable.Range(1, 20).Select(i => new
                        {
                            tmdb_id = page * 100 + i, media_type = i % 3 == 0 ? "series" : "movie", title = RequestTitles[(i - 1) % RequestTitles.Length], year = 1990 + i,
                            availability = i % 10 == 0 ? "available" : "unavailable", request = new { requestable = i % 5 != 0 }
                        })
                    });
            }
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
        private static readonly string[] Titles = ["Heat", "Heat Wave", "Burning Heat", "Heat of the Night", "Dead Heat", "Heat Lightning", "White Heat", "Some Like It Hot", "Heat Vision", "Body Heat"];
        private static readonly string[] RequestTitles = ["Heat", "Heat: The Series", "Heated Rivalry", "Heat Index", "Heat Signature", "Heat Wave", "Heatwave", "Dead Heat", "Heat Vision and Jack", "Heat Seekers"];
    }
}
