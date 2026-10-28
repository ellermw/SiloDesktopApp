using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class RequestDiscoverCurrentNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var wire = new Handler(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-layout.invalid");
        var api = new RequestsApi(client); var vm = new RequestsViewModel(api);
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(api).AddSingleton(vm)
            .AddSingleton(new CatalogApi(client)).AddSingleton(new PeopleApi(client)).AddSingleton<SearchViewModel>()
            .AddSingleton(new NavigationService()).AddSingleton(new SettingsApi(client))
            .AddSingleton(new UICustomizationService(new SettingsApi(client)))
            .AddSingleton(new ToastService()).BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var prior = field.GetValue(null); field.SetValue(null, services);
        var mounted = new List<UIElement>();
        try
        {
            await vm.LoadCommand.ExecuteAsync(null);
            var page = new RequestsPage { Width = 1000, Height = 720 }; mounted.Add(page); parent.Children.Add(page);
            await Task.Delay(100); Invoke(page, "Render"); page.UpdateLayout();
            var list = (StackPanel)page.FindName("DiscoveryList");
            var headings = list.Children.OfType<StackPanel>().Select(row => Descendants(row).OfType<TextBlock>().FirstOrDefault()?.Text).ToArray();
            if (!headings.SequenceEqual(new[] { "Choose a genre", "Curated picks", "Channels" }))
                throw new InvalidOperationException("Configured Discover row order/labels were lost: " + string.Join(",", headings));
            var explore = Descendants(list).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Explore Curated picks");
            if (explore.Visibility != Visibility.Visible) throw new InvalidOperationException("Trimmed one-page Discover row lost Explore All.");
            if (wire.Paths.Any(path => path.EndsWith("/studios"))) throw new InvalidOperationException("Hidden Studios row was requested.");
            if (!vm.BrandErrors.ContainsKey("network")) throw new InvalidOperationException("Partial brand failure was not retained.");
            var item = new RequestMediaResult { MediaType = "movie", TmdbId = 42, Title = "Fixture title", Request = new() { Requestable = true } };
            var card = (Grid)Invoke(page, "BuildMediaPosterCard", item)!;
            AssertNoRequest(card, "Discover/search");
            var search = new SearchPage(); var searchHost = new ContentControl();
            Invoke(search, "BuildExternalRequestCard", searchHost, item); AssertNoRequest(searchHost, "Search Request to add");
            var detail = new RequestDetailPage { Width = 1000 };
            Set(detail, "_features", new RequestFeatureStatus { WatchlistTitlesSupported = true });
            Invoke(detail, "BuildRecommendations", (object)new[] { item });
            AssertNoRequest((StackPanel)detail.FindName("RecommendationsPanel"), "external recommendations");

            var browse = new RequestBrowsePage { Width = 1000, Height = 600 }; mounted.Add(browse); parent.Children.Add(browse);
            Set(browse, "_navigation", new RequestBrowseNavigation("section", "r_picks"));
            await (Task)Invoke(browse, "LoadAsync")!;
            if (!wire.Paths.Contains("/api/v2/requests/discover/rows/r_picks?page=1")) throw new InvalidOperationException("Custom row browse used a legacy built-in route.");
            var host = new ContentControl(); Invoke(browse, "BuildCard", host, item); AssertNoRequest(host, "brand/custom browse");
            // The shared card still permits the separate Watchlist request workflow.
            var watchlist = ExternalTitleCard.Build(item, 160, request: () => Task.CompletedTask, watchlistCard: true);
            if (!Descendants(watchlist).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "Request Fixture title"))
                throw new InvalidOperationException("Watchlist lost its separate Request action.");
            // A different legacy owner has three known independent brand rows
            // even when its title discovery request fails.
            wire.LegacyDiscoveryFailure = true; client.SetBaseUrl("https://legacy-request-layout.invalid");
            vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null); Invoke(page, "Render"); page.UpdateLayout();
            var legacyHeadings = list.Children.OfType<StackPanel>().Select(row => Descendants(row).OfType<TextBlock>().FirstOrDefault()?.Text).ToArray();
            if (vm.DiscoveryError == null || !legacyHeadings.SequenceEqual(new[] { "Studios", "Networks", "Genres" }) || vm.Genres.Count != 1)
                throw new InvalidOperationException("Legacy title failure hid the successful independent brand rows.");
            Program.Log("PASS: native configured/interleaved/unknown/empty Discover rows, trimmed Explore All, hidden brands, partial failure, opaque row browse and card-action contexts.");
        }
        finally
        {
            foreach (var element in mounted) parent.Children.Remove(element);
            field.SetValue(null, prior);
        }
    }

    private static void AssertNoRequest(DependencyObject root, string context)
    {
        if (Descendants(root).OfType<Button>().Any(button => AutomationProperties.GetName(button).StartsWith("Request ", StringComparison.Ordinal)))
            throw new InvalidOperationException(context + " still exposes the removed direct Request action.");
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        // Unmounted ContentControl trees have not acquired a visual template yet.
        if (root is ContentControl { Content: DependencyObject content } && VisualTreeHelper.GetChildrenCount(root) == 0)
        { yield return content; foreach (var nested in Descendants(content)) yield return nested; }
        if (root is Panel panel && VisualTreeHelper.GetChildrenCount(root) == 0)
            foreach (var child in panel.Children) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];
        internal bool LegacyDiscoveryFailure;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery; Paths.Add(path);
            var json = path switch
            {
                "/api/v2/requests/status" when LegacyDiscoveryFailure => """{"requests_enabled":true}""",
                "/api/v2/requests/status" => """{"requests_enabled":true,"discover_rows_supported":true,"watchlist_titles_supported":true} """,
                "/api/v2/requests/discover" => """
                {"row_order":[{"key":"genres","type":"genres","title":"Choose a genre"},
                  {"key":"r_picks","type":"tmdb_list","title":"Curated picks"},
                  {"key":"r_empty","type":"tmdb_discover","title":"Empty"},
                  {"key":"r_future","type":"future_source","title":"Unknown"},
                  {"key":"networks","type":"networks","title":"Channels"}],
                 "items":[{"key":"r_picks","title":"Curated picks","page":1,"total_pages":1,"total_results":8,
                   "results":[{"media_type":"movie","tmdb_id":42,"title":"Fixture title","request":{"requestable":true}}]}]}
                """,
                "/api/v2/requests/discover/genres" => """{"items":[{"slug":"drama","display_name":"Drama"}]}""",
                "/api/v2/requests/discover/rows/r_picks?page=1" => """{"key":"r_picks","title":"Curated picks","page":1,"total_pages":1,"total_results":1,"results":[{"media_type":"movie","tmdb_id":42,"title":"Fixture title","request":{"requestable":true}}]}""",
                _ => "{\"items\":[]}"
            };
            var status = path.EndsWith("/networks") && !LegacyDiscoveryFailure || path == "/api/v2/requests/discover" && LegacyDiscoveryFailure
                ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
        }
    }
}
