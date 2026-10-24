using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class LibraryFilterClearNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://filter-clear.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture" });
        var catalog = new CatalogApi(client); var settings = new SettingsApi(client);
        var navigation = new NavigationService();
        using var events = new EventChannelClient(client, auth);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "images"));
        servicesField.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth, [typeof(CatalogApi)] = catalog,
            [typeof(SettingsApi)] = settings, [typeof(NavigationService)] = navigation,
            [typeof(SettingsService)] = new SettingsService(Path.Combine(Program.ResultDirectory, "settings")),
            [typeof(UICustomizationService)] = new UICustomizationService(settings),
            [typeof(CardOverlayService)] = new CardOverlayService(settings),
            [typeof(EventChannelClient)] = events, [typeof(ImageService)] = images, [typeof(HttpClient)] = http,
        }, () => new LibraryViewModel(catalog)));
        var root = new Grid(); var window = new Window { Content = root }; Frame? frame = null;
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280, 900)); window.AppWindow.Show(false);
            async Task<LibraryPage> Mount()
            {
                if (frame != null) { frame.Navigate(typeof(Page)); await Task.Delay(80); root.Children.Remove(frame); }
                frame = new Frame(); root.Children.Add(frame); navigation.Frame = frame;
                frame.Navigate(typeof(LibraryPage), new LibraryPage.NavigationArgs(new Library { Id = 11, Name = "Adult Scenes", Type = "movie" }, "Library"));
                var page = (LibraryPage)frame.Content;
                await Until(() => page.IsLoaded && page.ViewModel.Library?.Id == 11 && page.ViewModel.Items.Count > 0 && !page.ViewModel.IsLoading);
                page.UpdateLayout(); return page;
            }
            var failures = new List<string>();
            foreach (var advanced in new[] { true, false })
            foreach (var clearAll in new[] { true, false })
            {
                wire.SavedSearch = advanced
                    ? "?tab=library&sort=title&order=asc&groups[0][match]=all&groups[0][rules][0][field]=year&groups[0][rules][0][op]=gte&groups[0][rules][0][value]=2000&groups[0][rules][1][field]=resolution&groups[0][rules][1][op]=is&groups[0][rules][1][value]=2160p"
                    : "?tab=library&genre=Drama&year_min=2000";
                var page = await Mount();
                await Task.Delay(700); // Let the initial ShowTab state save finish before testing removal.
                var badges = (SiloPlayer.Controls.WrapPanel)page.FindName("FilterBadgesPanel");
                if (badges.Children.Count != 2) throw new InvalidOperationException("Fixture failed to restore its two active filters.");
                var writesBefore = wire.Writes;
                if (clearAll) Invoke((Button)page.FindName("ClearAllFiltersButton"));
                else foreach (var badge in badges.Children.ToArray()) Invoke(Descendants<Button>(badge).Single());
                await Task.Delay(750);
                var label = $"{(advanced ? "grouped" : "guided")}/{(clearAll ? "Clear All" : "chip removal")}";
                if (wire.Writes <= writesBefore) failures.Add(label + ": removing filters did not persist the new library state.");
                // Navigate away and create a fresh native page, like reopening a library.
                var reopened = await Mount();
                var restoredBadges = (SiloPlayer.Controls.WrapPanel)reopened.FindName("FilterBadgesPanel");
                if (restoredBadges.Children.Count != 0) failures.Add(label + $": reopening restored {restoredBadges.Children.Count} cleared filters.");
                if (wire.OtherSearch != "?tab=collections") failures.Add(label + ": another library's remembered state changed.");
                Program.Log($"Filter clear {label}: saved writes={wire.Writes - writesBefore}, reopened filters={restoredBadges.Children.Count}.");
                frame!.Navigate(typeof(Page)); await Task.Delay(700); root.Children.Remove(frame); frame = null;
            }
            if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
            Program.Log("PASS: Clear All and individual badge removal persist cleared guided/grouped filters across native page recreation; unrelated library state is preserved.");
        }
        finally
        {
            frame?.Navigate(typeof(Page)); navigation.Frame = null; root.Children.Clear();
            await Task.Delay(300); window.Close(); servicesField.SetValue(null, previous);
        }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var n = 0; n < 200 && !ready(); n++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Library failed to settle."); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match;
            foreach (var next in Descendants<T>(child)) yield return next;
        }
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<LibraryViewModel> factory) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(LibraryViewModel) ? factory() : values.GetValueOrDefault(type) ?? fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal string SavedSearch = "?tab=library";
        internal string OtherSearch = "?tab=collections";
        internal int Writes;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "filter-clear.invalid") throw new InvalidOperationException("External fixture request blocked.");
            var path = request.RequestUri.AbsolutePath;
            object body = new { items = Array.Empty<object>() };
            if (request.Method == HttpMethod.Put && path.EndsWith("ui.library_page_state"))
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
                var libraries = doc.GetProperty("value").GetProperty("libraries");
                SavedSearch = libraries.GetProperty("11").GetProperty("search").GetString()!;
                OtherSearch = libraries.GetProperty("12").GetProperty("search").GetString()!; Writes++;
            }
            else if (path.EndsWith("/settings/values/effective")) body = new { items = new object[]
            {
                new { key = "ui.remember_library_page_state", value = (object)true },
                new { key = "ui.library_page_state", value = (object)new { version = 1, libraries = new Dictionary<string, object> { ["11"] = new { search = SavedSearch }, ["12"] = new { search = OtherSearch } } } },
                new { key = "ui.card_presentation", value = (object)new { poster_size = "standard", caption = "title_metadata" } },
            } };
            else if (path == "/api/v2/events/ws-ticket") return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"fixture_no_socket\"}") };
            else if (path == "/api/v2/catalog") body = new { items = new[] { new { content_id = "fixture-item", type = "movie", title = "Fixture item" } }, total = 1, window_cursor = "fixture-cursor", page = new { has_more = false } };
            else if (path == "/api/v2/catalog/filters") body = new { genres = new[] { "Drama" }, resolutions = new[] { "2160p" } };
            else if (path == "/api/v2/catalog/search/capabilities") body = new { people_media_scope = true };
            else if (path == "/api/v2/user/libraries") body = new { items = new[] { new { id = 11, name = "Adult Scenes", type = "movie" } } };
            else if (path.StartsWith("/api/v2/settings/") || path == "/api/v2/library/11/layout") { }
            else throw new InvalidOperationException("Unexpected fixture route: " + path);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        }
    }
}
