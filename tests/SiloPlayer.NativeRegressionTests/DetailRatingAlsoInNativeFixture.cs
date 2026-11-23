using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

/// <summary>
/// Rating from 1 to 10 with half stars (silo-server #4398) and the "Also in" row (#4528) on the movie detail page,
/// with synthetic data, at 1440 and 800 wide. Selector: detail-rating-also-in.
/// </summary>
internal static class DetailRatingAlsoInNativeFixture
{
    private const string ThreeCollections = """
        {"items":[
          {"id":"c1","source":"library","title":"Marvel Cinematic Universe","poster_url":"https://also-in-fixture.invalid/poster-1","poster_is_collage":false,"item_count":23,"library_id":"4"},
          {"id":"c2","source":"user","title":"Weekend picks","poster_url":"","poster_is_collage":false,"item_count":7,"creator_profile_id":"me"},
          {"id":"c3","source":"user","title":"Family night","poster_url":"https://also-in-fixture.invalid/poster-3","poster_is_collage":true,"item_count":1204,"creator_profile_id":"owner-b"}],
         "preview_items":[]}
        """;
    private const string OneWithPreview = """
        {"items":[{"id":"c1","source":"library","title":"Marvel Cinematic Universe","poster_url":"https://also-in-fixture.invalid/poster-1","poster_is_collage":false,"item_count":23,"library_id":"4"}],
         "preview_items":[
           {"content_id":"movie:thor","type":"movie","title":"Thor","year":2011,"poster_url":"https://also-in-fixture.invalid/poster-2"},
           {"content_id":"movie:avengers","type":"movie","title":"The Avengers","year":2012,"poster_url":"https://also-in-fixture.invalid/poster-2"},
           {"content_id":"movie:ironman","type":"movie","title":"Iron Man","year":2008,"poster_url":"https://also-in-fixture.invalid/poster-2"}]}
        """;
    private const string NoCollections = "{\"items\":[],\"preview_items\":[]}";

    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)field.GetValue(null)!;
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://also-in-fixture.invalid"); client.SetProfile("me");
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "also-in-images"));
        var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("me");
        field.SetValue(null, new LocalServices(original, client, http, images, auth));
        var differences = new List<string>();
        try
        {
            var owner = new Grid(); var window = new Window { Content = owner };
            try
            {
                window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); window.AppWindow.Show(false); await Task.Delay(100);
                var scale = owner.XamlRoot.RasterizationScale;
                foreach (var width in new[] { 1440d, 800d })
                {
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(900 * scale))); await Task.Delay(100);
                    await RatingControlAsync(owner, width, differences);
                    await PageAsync(owner, handler, width, differences);
                }
            }
            finally { window.Close(); }
        }
        finally { field.SetValue(null, original); }
        if (differences.Count > 0) throw new InvalidOperationException(string.Join("\n", differences));
        Program.Log("PASS: DETAIL_RATING_ALSO_IN_COMPLETED half-star rating control states and the Also in row at 1440 and 800 wide.");
    }

    // ----- The rating control ---------------------------------------------------------------------------------------------

    private static async Task RatingControlAsync(Grid owner, double width, List<string> differences)
    {
        var picked = new List<int?>();
        HalfStarRating Pill(int? value, Action<HalfStarRating>? configure = null)
        {
            // The same capsule the detail action bar draws around the control.
            var pill = new HalfStarRating { HorizontalAlignment = HorizontalAlignment.Left, Value = value };
            pill.ScoreChosen += picked.Add;
            configure?.Invoke(pill);
            return pill;
        }
        var rows = new (string Label, HalfStarRating Pill)[]
        {
            ("Unrated", Pill(null)),
            ("3.5 stars (7)", Pill(7)),
            ("4 stars (8)", Pill(8)),
            ("5 stars (10)", Pill(10)),
            ("Half a star (1)", Pill(1)),
            ("Hover preview over star 3, unrated", Pill(null, pill => pill.PreviewStar(3))),
            ("Hover over the set star 4 previews removal", Pill(8, pill => pill.PreviewStar(4))),
        };
        var gallery = new StackPanel { Width = width, Padding = new Thickness(24), Spacing = 10, Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] };
        foreach (var (label, pill) in rows)
        {
            gallery.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
            gallery.Children.Add(pill);
        }
        owner.Children.Add(gallery); gallery.UpdateLayout(); await Task.Delay(150); gallery.UpdateLayout();

        // The keyboard row: the half that takes focus shows the system focus visual.
        var keyboard = Pill(7);
        gallery.Children.Add(new TextBlock { Text = "Keyboard focus on 3.5 stars", FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        gallery.Children.Add(keyboard); gallery.UpdateLayout();
        keyboard.Halves[6].Focus(FocusState.Keyboard); await Task.Delay(120); gallery.UpdateLayout();

        foreach (var (label, pill) in rows.Append(("keyboard", keyboard)))
        {
            var paths = (Microsoft.UI.Xaml.Shapes.Path[])pill.GetType().GetField("_paths", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pill)!;
            int? hover = (int?)pill.GetType().GetField("_hoverStar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pill);
            var shown = hover is { } star ? star * 2 : pill.Value;
            for (var score = 1; score <= 10; score++)
            {
                var filled = paths[score - 1].Fill != null;
                if (filled != RatingScale.IsFilled(score, shown)) differences.Add($"{width}: '{label}' half {score} is {(filled ? "filled" : "empty")}, expected shown {shown}.");
            }
            // The halves tile into five stars of equal pitch, left half then right half, with no gap or overlap inside a star.
            var x = pill.Halves.Select(half => half.TransformToVisual(pill).TransformPoint(new Windows.Foundation.Point(0, 0)).X).ToArray();
            for (var score = 0; score < 10; score++)
            {
                var half = pill.Halves[score];
                if (Math.Abs(half.ActualWidth - 12) > .1 || Math.Abs(half.ActualHeight - 22) > .1) differences.Add($"{width}: '{label}' half {score + 1} is {half.ActualWidth}x{half.ActualHeight}, expected 12x22.");
                if (score > 0 && Math.Abs(x[score] - (x[score - 1] + pill.Halves[score - 1].ActualWidth)) > .1) differences.Add($"{width}: '{label}' halves {score} and {score + 1} do not touch.");
                if (score % 2 == 0 && score > 0 && Math.Abs((x[score] - x[score - 2]) - 24) > .1) differences.Add($"{width}: '{label}' star {score / 2 + 1} is not one star pitch (24) after the previous.");
            }
            var tabs = pill.Halves.Where(half => half.IsTabStop).ToArray();
            if (tabs.Length != 1 || !ReferenceEquals(tabs[0], pill.Halves[RatingScale.TabbableScore(pill.Value) - 1])) differences.Add($"{width}: '{label}' must have exactly the rated half (or the first) on the tab stop.");
            for (var score = 1; score <= 10; score++)
            {
                var half = pill.Halves[score - 1];
                if (AutomationProperties.GetName(half) != RatingScale.Label(score)) differences.Add($"{width}: '{label}' half {score} is named '{AutomationProperties.GetName(half)}', expected '{RatingScale.Label(score)}'.");
                var status = AutomationProperties.GetItemStatus(half);
                if (status != (pill.Value == score ? "Selected" : "Not selected")) differences.Add($"{width}: '{label}' half {score} reports '{status}'.");
                var removes = ToolTipService.GetToolTip(half) != null;
                if (removes != RatingScale.Clears(score, pill.Value)) differences.Add($"{width}: '{label}' half {score} tooltip presence {removes} does not follow the star that clears.");
            }
        }
        var removal = rows[6].Pill;
        if (removal.Halves.Any(half => Math.Abs(half.Opacity - 0.45) > .001)) differences.Add($"{width}: hovering the set star must dim the stars to preview removal.");
        if (rows[5].Pill.Halves.Any(half => half.Opacity != 1)) differences.Add($"{width}: a hover preview over an unset star must not dim.");

        // Assistive activation sets the half's own score, or clears when that half is the rating.
        picked.Clear();
        Invoke(rows[0].Pill.Halves[6]); Invoke(rows[1].Pill.Halves[6]); Invoke(rows[2].Pill.Halves[6]);
        if (!picked.SequenceEqual(new int?[] { 7, null, 7 })) differences.Add($"{width}: activating halves picked [{string.Join(",", picked.Select(score => score?.ToString() ?? "clear"))}], expected [7,clear,7].");

        await MediaParityNativeFixture.CaptureAsync(gallery, $"rating-control-{width:0}.png");

        // An arrow key reports the stepped half and puts the Tab stop on it before the owner applies the pick, so keyboard focus
        // can follow; left on the old half, Space would set that half's score again. Focus is compared only when this window gave it.
        var focusable = ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(keyboard.XamlRoot), keyboard.Halves[6]);
        picked.Clear();
        Call(keyboard, "StepFromKeyboard", 1);
        var stops = keyboard.Halves.Where(half => half.IsTabStop).ToArray();
        if (!picked.SequenceEqual(new int?[] { 8 })) differences.Add($"{width}: Right from 3.5 stars picked [{string.Join(",", picked.Select(score => score?.ToString() ?? "clear"))}], expected [8].");
        if (stops.Length != 1 || !ReferenceEquals(stops[0], keyboard.Halves[7])) differences.Add($"{width}: Right from 3.5 stars must move the Tab stop to the 4 star half before the owner applies the pick.");
        if (focusable && !ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(keyboard.XamlRoot), keyboard.Halves[7])) differences.Add($"{width}: Right from 3.5 stars must move keyboard focus to the 4 star half.");
        // The owner applies the pick; Space on the stepped half then clears the rating, as activating the rating's own half does.
        keyboard.Value = 8; picked.Clear();
        Invoke(keyboard.Halves[7]);
        if (!picked.SequenceEqual(new int?[] { null })) differences.Add($"{width}: activating the stepped half picked [{string.Join(",", picked.Select(score => score?.ToString() ?? "clear"))}], expected [clear].");
        owner.Children.Remove(gallery);
    }

    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    // ----- The movie detail page -----------------------------------------------------------------------------------------

    private static async Task PageAsync(Grid owner, Handler handler, double width, List<string> differences)
    {
        var page = new ItemDetailPage { Width = width, Height = 900 };
        owner.Children.Add(page); await Task.Delay(100);
        try
        {
            // The page is not navigated here (OnNavigatedTo owns the view model subscription), so BuildAlsoInRow is called directly.
            typeof(ItemDetailViewModel).GetProperty(nameof(page.ViewModel.LibraryId))!.SetValue(page.ViewModel, 4);
            page.ViewModel.Item = new MediaItemDetail
            {
                ContentId = "film", Type = "movie", Title = "Thor: Ragnarok", Year = 2017, Runtime = 130,
                Overview = "Imprisoned on the planet Sakaar, Thor must race against time to return to Asgard and stop Ragnarok.",
                UserState = new(), UserRating = 7,
            };
            page.ViewModel.UserRating = 7;
            Call(page, "UpdateUI"); Call(page, "ArrangeCurrentWebUiContentOrder", "movie");
            Call(page, "UpdateResponsiveLayout", width); page.UpdateLayout(); await Task.Delay(200);

            // Rating on the page: the capsule shows 3.5 stars.
            var rating = (HalfStarRating)page.FindName("StarRatingContainer");
            if (rating.Value != 7 || rating.Visibility != Visibility.Visible) differences.Add($"{width}: the detail rating capsule must show the stored 7 (3.5 stars); it shows {rating.Value}.");
            await MediaParityNativeFixture.CaptureAsync(owner, $"detail-rating-{width:0}.png");
            page.ViewModel.UserRating = 10; Call(page, "UpdateStarRating"); await Task.Delay(60);
            if (rating.Value != 10) differences.Add($"{width}: the capsule must follow a 5 star rating.");
            await MediaParityNativeFixture.CaptureAsync(owner, $"detail-rating-five-{width:0}.png");
            page.ViewModel.UserRating = 7; Call(page, "UpdateStarRating");

            // "Also in" with three collections, then none, then one collection with its titles previewed.
            handler.Collections = ThreeCollections;
            await page.ViewModel.RefreshItemCollectionsAsync(); Call(page, "BuildAlsoInRow");
            await UntilAsync(() => ((FrameworkElement)page.FindName("AlsoInSection")).Visibility == Visibility.Visible && ((StackPanel)page.FindName("AlsoInPanel")).Children.Count == 3);
            page.UpdateLayout(); await Task.Delay(300);
            var section = (FrameworkElement)page.FindName("AlsoInSection");
            var panel = (StackPanel)page.FindName("AlsoInPanel");
            var tiles = panel.Children.OfType<CollectionPosterTile>().ToArray();
            if (tiles.Length != 3 || !tiles.Select(tile => tile.Collection.Title).SequenceEqual(["Marvel Cinematic Universe", "Weekend picks", "Family night"])) differences.Add($"{width}: Also in must show a card for each of the three collections in server order.");
            if (((TextBlock)page.FindName("AlsoInTitle")).Text != "Also in") differences.Add($"{width}: the row title must be 'Also in', not '{((TextBlock)page.FindName("AlsoInTitle")).Text}'.");
            if (((FrameworkElement)page.FindName("AlsoInExploreAll")).Visibility != Visibility.Collapsed) differences.Add($"{width}: the card row has no Explore all.");
            var bylines = Texts(tiles[2]).Concat(Texts(tiles[1])).ToArray();
            if (!bylines.Contains("by Sam")) differences.Add($"{width}: another profile's shared collection must carry its owner's name ('by Sam'); found [{string.Join("|", bylines)}].");
            if (!Texts(tiles[1]).Contains("User collection")) differences.Add($"{width}: the viewer's own personal collection reads 'User collection'.");
            if (!Texts(tiles[2]).Any(text => text.Where(char.IsDigit).SequenceEqual("1204"))) differences.Add($"{width}: the title count of a large collection is grouped; found [{string.Join("|", Texts(tiles[2]))}].");
            var content = (StackPanel)page.FindName("DetailContentPanel");
            var similar = (FrameworkElement)page.FindName("SimilarSection");
            if (content.Children.IndexOf(section) < 0 || content.Children.IndexOf(section) >= content.Children.IndexOf(similar)) differences.Add($"{width}: Also in must come before More Like This.");
            var tops = tiles.Select(tile => tile.TransformToVisual(panel).TransformPoint(new Windows.Foundation.Point(0, 0)).Y).Distinct().Count();
            if (tops != 1) differences.Add($"{width}: the cards must sit in one row.");
            await ScrollToAsync(page, section);
            await MediaParityNativeFixture.CaptureAsync(owner, $"movie-also-in-three-{width:0}.png");

            handler.Collections = OneWithPreview;
            await page.ViewModel.RefreshItemCollectionsAsync(); Call(page, "BuildAlsoInRow");
            await UntilAsync(() => ((TextBlock)page.FindName("AlsoInTitle")).Text == "Marvel Cinematic Universe" && panel.Children.Count == 3);
            page.UpdateLayout(); await Task.Delay(300);
            if (((FrameworkElement)page.FindName("AlsoInExploreAll")).Visibility != Visibility.Visible) differences.Add($"{width}: a lone collection with titles to preview offers Explore all.");
            await ScrollToAsync(page, section);
            await MediaParityNativeFixture.CaptureAsync(owner, $"movie-also-in-preview-{width:0}.png");

            handler.Collections = NoCollections;
            await page.ViewModel.RefreshItemCollectionsAsync(); Call(page, "BuildAlsoInRow");
            await UntilAsync(() => section.Visibility == Visibility.Collapsed);
            page.UpdateLayout(); await Task.Delay(200);
            if (panel.Children.Count != 0 || section.Visibility != Visibility.Collapsed) differences.Add($"{width}: with no collections the row must leave no trace.");
            await ScrollToAsync(page, similar, toEnd: true);
            await MediaParityNativeFixture.CaptureAsync(owner, $"movie-also-in-none-{width:0}.png");

            handler.Collections = null; // a failed read
            await page.ViewModel.RefreshItemCollectionsAsync(); Call(page, "BuildAlsoInRow"); await Task.Delay(100);
            if (section.Visibility != Visibility.Collapsed) differences.Add($"{width}: a failed read must show no row.");
        }
        finally { page.ViewModel.CancelPendingLoads(); owner.Children.Remove(page); }
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Texts(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static async Task ScrollToAsync(ItemDetailPage page, FrameworkElement target, bool toEnd = false)
    {
        DependencyObject? node = (DependencyObject)page.FindName("DetailContentPanel");
        while (node != null && node is not ScrollViewer) node = VisualTreeHelper.GetParent(node);
        if (node is not ScrollViewer scroller) throw new InvalidOperationException("The detail page has no scroller.");
        page.UpdateLayout();
        var offset = toEnd ? scroller.ScrollableHeight : target.TransformToVisual((UIElement)scroller.Content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y - 24;
        scroller.ChangeView(null, Math.Max(0, offset), null, true);
        await Task.Delay(250); page.UpdateLayout();
    }

    private static object? Call(object value, string name, params object[] args)
        => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);

    private static async Task UntilAsync(Func<bool> ready)
    {
        for (var i = 0; i < 160; i++) { if (ready()) return; await Task.Delay(25); }
        throw new TimeoutException("Detail rating / Also in native state timed out.");
    }

    private sealed class LocalServices(IServiceProvider original, SiloApiClient client, HttpClient http, ImageService images, AuthService auth) : IServiceProvider
    {
        public object? GetService(Type type)
            => type == typeof(ItemDetailViewModel) ? new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((id, ct) => new CatalogApi(client).GetItemDetailAsync(id, ct)))
            : type == typeof(SiloApiClient) ? client : type == typeof(SettingsApi) ? new SettingsApi(client) : type == typeof(CatalogApi) ? new CatalogApi(client)
            : type == typeof(AuthApi) ? new AuthApi(client) : type == typeof(AuthService) ? auth
            : type == typeof(HttpClient) ? http : type == typeof(ImageService) ? images : original.GetService(type);
    }

    private sealed class Handler : HttpMessageHandler
    {
        /// <summary>The collections reply; null answers a server error.</summary>
        internal string? Collections = NoCollections;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/poster-"))
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "audiobook-cover.png"), ct)) };
            if (path == "/api/v2/catalog/items/film/collections")
                return Collections == null
                    ? new(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"type\":\"about:blank\",\"title\":\"fixture\",\"status\":500}", Encoding.UTF8, "application/problem+json") }
                    : Json(Collections);
            if (path == "/api/v2/profiles")
                return Json("{\"items\":[{\"id\":\"me\",\"name\":\"Alex\"},{\"id\":\"owner-b\",\"name\":\"Sam\"}]}");
            throw new InvalidOperationException("Unexpected detail rating / Also in fixture request: " + path);
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
