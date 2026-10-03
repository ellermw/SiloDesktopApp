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
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class RequestViewerNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-fixture.invalid"); client.SetProfile("fixture-profile");
        var api = new RequestsApi(client);
        var overlays = new CardOverlayService(new SettingsApi(client));
        using var services = new ServiceCollection().AddSingleton(api).AddSingleton(client)
            .AddSingleton(new PeopleApi(client)).AddSingleton(new SettingsApi(client)).AddSingleton<SearchViewModel>()
            .AddSingleton(new CatalogApi(client)).AddSingleton(new ToastService()).AddSingleton(overlays)
            .AddSingleton(new UICustomizationService(new SettingsApi(client)))
            .AddSingleton(new ItemDetailPrefetchCache((id, _) => Task.FromException<MediaItemDetail>(new ApiException("not_found", "Fixture inaccessible library", 404))))
            .AddSingleton<WatchlistViewModel>().BuildServiceProvider();
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = serviceField.GetValue(null); serviceField.SetValue(null, services);
        try
        {
            await SearchAndRulesAsync(parent);
            var page = new RequestDetailPage { Width = 1000, Height = 700 };
            parent.Children.Add(page);
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
            if (actions.Children.OfType<Button>().Any(b => (string?)b.Content == "Open in library"))
                throw new InvalidOperationException("Native external title offers an inaccessible library copy.");
            var watchlist = actions.Children.OfType<Button>().Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Add to watchlist");
            Invoke(page, "Watchlist_Click", watchlist, new RoutedEventArgs());
            await UntilAsync(() => handler.Paths.Contains("PUT /api/v2/watchlist/titles/series/42") && actions.Children.OfType<Button>().Any(b => (string?)b.Content == "Remove from watchlist"));
            parent.Children.Remove(page);

            var watchlistPage = new WatchlistPage { Width = 900, Height = 700 };
            parent.Children.Add(watchlistPage);
            ((ComboBox)watchlistPage.FindName("WatchlistScope")).SelectedIndex = 1;
            await UntilAsync(() => watchlistPage.ViewModel.ExternalTitles.Count == 1);
            watchlistPage.Measure(new Windows.Foundation.Size(900, 700));
            watchlistPage.Arrange(new Windows.Foundation.Rect(0, 0, 900, 700)); watchlistPage.UpdateLayout();
            await Task.Delay(150);
            if (((ItemsRepeater)watchlistPage.FindName("ExternalRepeater")).Visibility != Visibility.Visible || ((ItemsRepeater)watchlistPage.FindName("PosterRepeater")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Native watchlist tab visibility is wrong.");
            if (((TextBlock)watchlistPage.FindName("ItemCountText")).Text != "1") throw new InvalidOperationException("Native external watchlist count is wrong.");
            parent.Children.Remove(watchlistPage);
            Program.Log("PASS: native request title seasons, download phase, follow/unfollow label, watchlist write, external tab and count.");
        }
        finally { serviceField.SetValue(null, previous); }
    }

    private static void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static async Task UntilAsync(Func<bool> condition)
    { var deadline = DateTime.UtcNow.AddSeconds(5); while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Native request action did not finish."); await Task.Delay(30); } }

    private static async Task SearchAndRulesAsync(StackPanel parent)
    {
        var dialog = new SiloPlayer.Controls.GlobalSearchDialog();
        var box = (TextBox)dialog.FindName("SearchBox"); box.Text = "Fixture";
        ((List<SiloPlayer.Core.Models.Home.MediaItem>)dialog.GetType().GetField("_results", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Add(new() { ContentId = "title", Title = "Fixture title" });
        ((List<RequestMediaResult>)dialog.GetType().GetField("_requestResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Add(new() { MediaType = "series", TmdbId = 42, Title = "Fixture request" });
        Invoke(dialog, "Render");
        var selection = (SearchSelectionState)dialog.GetType().GetField("_selection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        selection.Move(1); selection.Move(1);
        ((List<Person>)dialog.GetType().GetField("_peopleResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Add(new() { Id = "person", Name = "Fixture Person" });
        Invoke(dialog, "Render");
        var rows = ((StackPanel)dialog.FindName("ResultsPanel")).Children.OfType<Button>().ToArray();
        if (!rows.Select(row => (int)row.Tag).SequenceEqual(new[] { 0, 1, 2 }) || selection.Index != 2)
            throw new InvalidOperationException("Native quick search skips optional keyboard rows or loses selected identity.");
        box.Text = "Fixture Person";
        Invoke(dialog, "Render");
        rows = ((StackPanel)dialog.FindName("ResultsPanel")).Children.OfType<Button>().ToArray();
        var firstTitle = ((Grid)((Border)rows[0].Content).Child).Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().First().Text;
        if (firstTitle != "Fixture Person" ||
            !rows.Select(row => (int)row.Tag).SequenceEqual(new[] { 0, 1, 2 }))
            throw new InvalidOperationException("Exact person search does not lead the native result groups.");
        ((List<SiloPlayer.Core.Models.Home.MediaItem>)dialog.GetType().GetField("_results", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Clear();
        ((List<Person>)dialog.GetType().GetField("_peopleResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Clear();
        ((List<RequestMediaResult>)dialog.GetType().GetField("_requestResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Clear();
        Field(dialog, "_optionalSearchFailed", true);
        Invoke(dialog, "Render");
        if (!((TextBlock)dialog.FindName("EmptyText")).Text.Contains("could not be loaded"))
            throw new InvalidOperationException("An unavailable optional search source is reported as no matches.");
        var timer = dialog.GetType().GetField("_debounceTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog) as DispatcherTimer; timer?.Stop();

        var query = new SiloPlayer.Core.Models.Collections.QueryDefinition { Match = "any", Groups =
        [ new() { Match = "all", Rules = [new() { Field = "year", Op = "between", Value = new[] { 2020, 2026 } }] },
          new() { Match = "any", Rules = [new() { Field = "hdr", Op = "is", Value = false }] } ] };
        var editor = new SiloPlayer.Controls.QueryRulesEditor { Width = 480 }; editor.Load(query); parent.Children.Add(editor);
        editor.UpdateLayout(); await Task.Delay(50);
        var range = Descendants(editor).OfType<TextBox>().Single(box => box.Text == "2020, 2026");
        if (range.Text != "2020, 2026" || query.Groups[1].Rules[0].Value is not false) throw new InvalidOperationException("Native rule load changed typed values.");
        range.Text = "bad, range"; await UntilAsync(() => !editor.IsValid);
        range.Text = "2010, 2019";
        await UntilAsync(() => editor.IsValid && query.Groups[0].Rules[0].Value is double[]);
        if (!editor.IsValid || query.Groups[0].Rules[0].Value is not double[] values || values[0] != 2010 || query.Match != "any" || query.Groups[0].Match != "all")
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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(request.Method + " " + path);
            object reply;
            if (path.EndsWith("/requests/status")) reply = new { requests_enabled = true, allowed = true, follow_supported = true, season_requests_supported = true, watchlist_titles_supported = true };
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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(reply)) });
        }
    }
}
