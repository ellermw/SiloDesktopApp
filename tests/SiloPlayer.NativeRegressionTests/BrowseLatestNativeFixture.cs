using System.Collections;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

// Coordinator dispatch: browse-acceptance / SILO_NATIVE_BROWSE_LATEST=1.
// This adds only the remaining478 Hero/provider and Library capability/wire seams.
internal static class BrowseLatestNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://browse-latest.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("first", profile: new() { Id = "first", Name = "First" });
        var catalog = new CatalogApi(client); var settings = new SettingsApi(client); var navigation = new NavigationService();
        using var events = new EventChannelClient(client, auth);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "browse-latest-images"));
        servicesField.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth, [typeof(CatalogApi)] = catalog,
            [typeof(SettingsApi)] = settings, [typeof(NavigationService)] = navigation,
            [typeof(SettingsService)] = new SettingsService(Path.Combine(Program.ResultDirectory, "browse-latest-settings")),
            [typeof(UICustomizationService)] = new UICustomizationService(settings),
            [typeof(CardOverlayService)] = new CardOverlayService(settings), [typeof(EventChannelClient)] = events,
            [typeof(ImageService)] = images, [typeof(HttpClient)] = http,
        }, () => new LibraryViewModel(catalog)));
        var owner = new Grid(); var window = new Window { Content = owner }; Frame? frame = null;
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280, 900)); window.AppWindow.Show(false);
            await Task.Delay(100); var scale = owner.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(1280 * scale), (int)Math.Round(900 * scale)));
            await HeroAsync(owner, window, scale);

            async Task<LibraryPage> NewPageAsync(bool wait = true)
            {
                if (frame != null) { frame.Navigate(typeof(Page)); await Task.Delay(120); owner.Children.Remove(frame); }
                frame = new Frame(); owner.Children.Add(frame); navigation.Frame = frame;
                frame.Navigate(typeof(LibraryPage), new Library { Id = 77, Name = "Latest movies", Type = "movie" });
                var page = (LibraryPage)frame.Content;
                if (wait) await Until(() => page.ViewModel.Items.Count > 0 && !page.ViewModel.IsLoading && Field<string>(page, "_currentTab") == "Library", "Library mount");
                page.UpdateLayout(); await Task.Delay(100); return page;
            }
            void Capability(string mode, string? savedSort = null)
            {
                wire.Mode = mode; wire.SavedSort = savedSort; catalog.InvalidateRatingCapability();
            }
            Capability("missing"); var page = await NewPageAsync();
            Choices(page, critic: false, audience: false);
            Require(wire.CatalogRequests.Any(query => query.GetValueOrDefault("sort") == "title"), "Missing saved sort must use the real explicit title request.");
            Program.Log("PASS: missing capability/default Library sort keeps IMDb/TMDB, hides optional RT, and emits explicit title.");

            Capability("unsupported", "rating_rt_audience"); page = await NewPageAsync();
            Choices(page, critic: false, audience: false);
            Require(page.ViewModel.SelectedSort == "title", "Ordinary Library must normalize a saved unavailable RT sort to title.");
            Capability("critic", "rating_rt_audience"); page = await NewPageAsync();
            Choices(page, critic: true, audience: false);
            var combo = (ComboBox)page.FindName("SortComboBox");
            combo.SelectedItem = ((IEnumerable)combo.ItemsSource).Cast<object>().Single(item => Value(item) == "rating_rt_critic");
            await Until(() => wire.CatalogRequests.Any(query => query.GetValueOrDefault("sort") == "rating_rt_critic" && query.GetValueOrDefault("order") == "desc") && !page.ViewModel.IsLoading, "actual critic selection/catalog request");
            Require(page.ViewModel.SelectedSort == "rating_rt_critic", "Real ComboBox selection did not publish the supported sort.");
            await MediaParityNativeFixture.CaptureAsync(owner, "browse-latest-library-critic-1280.png");
            Program.Log("PASS: actual Library unavailable saved-sort normalization and capability-limited critic choice emits canonical sort/order.");

            Capability("hold", "title"); wire.StartPending(); var pending = await NewPageAsync(wait: false);
            await wire.CapabilityStarted.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Require(!SortValues(pending).Contains("rating_rt_critic") && !SortValues(pending).Contains("rating_rt_audience"), "Pending Library capability exposed optional RT choices.");
            var retiredReads = wire.CatalogRequests.Count;
            auth.SelectProfile("second", profile: new() { Id = "second", Name = "Second" });
            Capability("audience", "title"); page = await NewPageAsync(); Choices(page, critic: false, audience: true);
            wire.ReleaseCapability(); await Task.Delay(180);
            Require(catalog.CachedShownRatingSources.SetEquals(new[] { "rt_audience" }), "Ignored-cancellation old capability reply replaced current authority.");
            Require(wire.CatalogRequests.Skip(retiredReads).All(query => query["fixture_profile"] == "second"), "Old pending Library continuation loaded catalog after profile retirement.");
            Choices(page, critic: false, audience: true);
            Program.Log("PASS: actual pending Library choices remain hidden; new authority mounts without waiting for old capability; stale reply cannot repaint/cache/load.");

            // Both reachable Library routes always emit SelectedSort. Do not invent an implicit-sort toggle.
            Capability("audience", "title"); page = await NewPageAsync();
            Require(wire.CatalogRequests.Last(query => query["fixture_profile"] == "second").GetValueOrDefault("sort") == "title", "Saved explicit title must retain explicit wire identity.");
            var model = page.ViewModel;
            await model.LoadWindowAsync(0, 100, force: true);
            var boundaryStart = wire.CatalogRequests.Count;
            await model.LoadWindowAsync(100, 60);
            Require(wire.CatalogRequests.Skip(boundaryStart).Any(query => query.GetValueOrDefault("cursor") == "next-100" && !query.ContainsKey("seek")), "Mounted production Library adjacent complete window did not consume next-100 without seek.");
            Require(model.GetWindowItem(100)?.ContentId == "latest-100", "Adjacent Library window did not publish its actual item.");
            var jumpStart = wire.CatalogRequests.Count;
            await model.LoadWindowAsync(900, 60);
            Require(wire.CatalogRequests.Skip(jumpStart).Any(query => query.GetValueOrDefault("seek") == "900" && query.GetValueOrDefault("cursor") == "root-window"), "Distant Library jump did not seek directly within the existing window.");
            Program.Log("PASS: reachable Library missing/explicit title always emits sort; mounted production VM consumes adjacent cursor and distant seek independently.");
            Program.Log("PASS: BROWSE_LATEST478_COMPLETED Hero primary ordered metadata/provider pixels and Library capability/sort/authority/continuation wire.");
        }
        finally
        {
            wire.ReleaseCapability(); frame?.Navigate(typeof(Page)); servicesField.SetValue(null, previous);
            await Task.Delay(250); navigation.Frame = null; owner.Children.Clear(); window.Close();
        }
    }

    private static async Task HeroAsync(Grid owner, Window window, double scale)
    {
        foreach (var width in new[] { 460, 1280 })
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
            var hero = new HeroCarousel { VerticalAlignment = VerticalAlignment.Top }; owner.Children.Add(hero);
            await Until(() => hero.IsLoaded && Math.Abs(hero.ActualWidth - width) < .5, "actual Hero client width");
            var item = new MediaItem { ContentId = "latest-hero", Type = "movie", Title = "Latest primary rating", Year = 2026,
                Runtime = 125, RatingImdb = 7.35, RatingTmdb = 9.5, Genres = [" Drama ", "Drama", "Crime", "Action"], ContentRating = " pg-13 " };
            hero.ItemsSource = new[] { item }; hero.UpdateLayout(); await Task.Delay(100);
            var row = (StackPanel)hero.FindName("HeroMetaPillsRow");
            Require(Entries(row).SequenceEqual(new[] { "2026", "2h 5m", "IMDb 7.4", "Drama", "Crime", "PG-13" }), "Hero metadata order/deduplicated two genres/IMDb priority/decimal display differ.");
            var rating = row.Children.OfType<StackPanel>().Single(); var labels = rating.Children.OfType<TextBlock>().ToArray();
            Require(rating.Spacing == 6 && labels.Length == 2 && labels[0].Text == "IMDb" && labels[0].FontSize == 12 && labels[0].FontWeight.Weight == 600
                && Math.Abs(labels[0].Opacity - .75) < 1e-6 && labels[1].FontSize == 14 && labels[1].FontWeight.Weight == 700, "Actual Hero small provider/score style differs.");
            await MediaParityNativeFixture.CaptureAsync(owner, $"browse-latest-hero-imdb-{width}.png");
            item.RatingImdb = double.NaN; item.RatingTmdb = 8.1; hero.ItemsSource = new[] { item }; hero.UpdateLayout(); await Task.Delay(150);
            Require(Entries(row).SequenceEqual(new[] { "2026", "2h 5m", "TMDB 8.1", "Drama", "Crime", "PG-13" }), "Invalid IMDb must fall back to the real TMDB mark in the same metadata position.");
            var mark = Descendants<Image>(row).Single(image => image.Source is SvgImageSource);
            Require(mark.Height == 8 && mark.ActualHeight > 0 && ((SvgImageSource)mark.Source).UriSource.AbsoluteUri.EndsWith("/Assets/Ratings/tmdb-logo.svg", StringComparison.Ordinal), "Hero TMDB did not use the official8px asset.");
            var raster = new RenderTargetBitmap(); await raster.RenderAsync(mark); var pixels = (await raster.GetPixelsAsync()).ToArray();
            Require(pixels.Length > 0 && Enumerable.Range(0, pixels.Length / 4).Any(index => pixels[index * 4 + 3] > 0), "Hero official TMDB asset did not paint actual pixels.");
            await MediaParityNativeFixture.CaptureAsync(owner, $"browse-latest-hero-tmdb-{width}.png");
            item.RatingImdb = 0; item.RatingTmdb = 11; hero.ItemsSource = new[] { item };
            Require(!row.Children.OfType<StackPanel>().Any(), "Hero invalid scores must omit the rating entry rather than show a fallback star.");
            item.RatingImdb = null; item.RatingTmdb = null; hero.ItemsSource = new[] { item };
            Require(!row.Children.OfType<StackPanel>().Any(), "Hero absent scores must omit the rating entry.");
            item.Type = "episode"; item.SeasonNumber = 2; item.EpisodeNumber = 3; item.RatingImdb = 9; item.Runtime = 42;
            hero.ItemsSource = new[] { item };
            Require(Entries(row).SequenceEqual(new[] { "S2 · E3", "42 min", "PG-13" }), "Episode Hero must omit year/provider/genres and retain episode/runtime/content rating order.");
            owner.Children.Remove(hero); await Task.Delay(50);
        }
        Program.Log("PASS: actual460/1280 Hero ordered primary metadata, IMDb7.35→7.4 priority, TMDB fallback/official pixels, absent-invalid omission and episode metadata.");
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(1280 * scale), (int)Math.Round(900 * scale)));
        await Task.Delay(100);
    }

    private static string[] Entries(StackPanel row) => row.Children.Select(child => child is TextBlock text ? text.Text : AutomationProperties.GetName(child)).Where(text => text != "·").ToArray();
    private static string Value(object item) => (string)item.GetType().GetProperty("Value")!.GetValue(item)!;
    private static HashSet<string> SortValues(LibraryPage page) => ((IEnumerable?)((ComboBox)page.FindName("SortComboBox")).ItemsSource)?.Cast<object>().Select(Value).ToHashSet(StringComparer.Ordinal) ?? [];
    private static void Choices(LibraryPage page, bool critic, bool audience)
    {
        var choices = SortValues(page);
        Require(choices.Contains("rating_imdb") && choices.Contains("rating_tmdb") && choices.Contains("rating_rt_critic") == critic && choices.Contains("rating_rt_audience") == audience, "Actual Library choices differ from current rating capability.");
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready, string message) { for (var n = 0; n < 200 && !ready(); n++) await Task.Delay(25); Require(ready(), "Latest browse did not settle: " + message); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var n = 0; n < VisualTreeHelper.GetChildrenCount(parent); n++) { var child = VisualTreeHelper.GetChild(parent, n); if (child is T match) yield return match; foreach (var next in Descendants<T>(child)) yield return next; } }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<LibraryViewModel> factory) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(LibraryViewModel) ? factory() : values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }

    private sealed class Wire : HttpMessageHandler
    {
        internal string Mode = "missing"; internal string? SavedSort;
        internal readonly System.Collections.Concurrent.ConcurrentQueue<Dictionary<string, string>> CatalogRequests = new();
        internal TaskCompletionSource CapabilityStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _capabilityRelease;
        internal void StartPending() { CapabilityStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); _capabilityRelease = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        internal void ReleaseCapability() => _capabilityRelease?.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri?.Host == "browse-latest.invalid", "Browse latest fixture attempted external network.");
            var path = request.RequestUri!.AbsolutePath;
            var profile = request.Headers.TryGetValues("X-Profile-Id", out var headers) ? headers.Single() : "";
            Program.Log($"Browse latest wire {profile} {request.Method} {request.RequestUri.PathAndQuery}");
            if (path == "/api/v2/capabilities/ratings")
            {
                var mode = Mode;
                if (mode == "hold") { CapabilityStarted.TrySetResult(); await _capabilityRelease!.Task; mode = "critic"; }
                return Reply(mode == "missing" ? new { } : (object)new { state = mode == "unsupported" ? "unsupported" : "available", sources = new[] { new { source = mode == "audience" ? "rt_audience" : "rt_critic", name = "Fixture provider" } } });
            }
            if (path == "/api/v2/events/ws-ticket") return Reply(new { error = "fixture", message = "No fixture socket" }, HttpStatusCode.ServiceUnavailable);
            if (path == "/api/v2/catalog")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2))
                    .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);
                query["fixture_profile"] = profile; CatalogRequests.Enqueue(query);
                var offset = query.TryGetValue("seek", out var seek) ? int.Parse(seek) : int.Parse(query["cursor"].Split('-').Last());
                var limit = int.Parse(query["limit"]);
                return Reply(new { items = Enumerable.Range(offset, limit).Select(index => new { content_id = "latest-" + index, title = "Latest item " + index, type = "movie" }), total = 1000, total_exact = true, window_cursor = "root-window", page = new { has_more = offset + limit < 1000, next_cursor = "next-" + (offset + limit) } });
            }
            if (path.EndsWith("/settings/values/effective", StringComparison.Ordinal)) return Reply(new { items = new object[]
            { new { key = "ui.remember_library_page_state", value = (object)true }, new { key = "ui.library_page_state", value = (object)new { version = 1, libraries = new Dictionary<string, object> { ["77"] = new { search = "?tab=library" + (SavedSort == null ? "" : "&sort=" + SavedSort) } } } } } });
            if (path.StartsWith("/api/v2/settings/", StringComparison.Ordinal)) return Reply(new { items = Array.Empty<object>(), key = "fixture", value = new { } });
            if (path == "/api/v2/catalog/filters") return Reply(new { genres = Array.Empty<string>() });
            if (path == "/api/v2/library/77/layout") return Reply(new { sections = Array.Empty<object>() });
            if (path == "/api/v2/user/libraries") return Reply(new { items = new[] { new { id = 77, name = "Latest movies", type = "movie" } } });
            throw new InvalidOperationException("Unexpected latest browse route: " + request.RequestUri.PathAndQuery);
        }
        private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }
}
