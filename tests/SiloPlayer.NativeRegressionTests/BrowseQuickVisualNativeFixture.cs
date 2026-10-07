using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using Windows.Graphics.Imaging;

// Measured against public74158b4a GlobalSearch with the same Outfit/Cobalt
// customization, deterministic rows and460/900px viewport. No user app/state.
internal static class BrowseQuickVisualNativeFixture
{
    internal static async Task RunAsync()
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://quick-visual.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture" });
        servicesField.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth, [typeof(HttpClient)] = http,
            [typeof(CatalogApi)] = new CatalogApi(client), [typeof(SettingsApi)] = new SettingsApi(client),
            [typeof(RequestsApi)] = new RequestsApi(client), [typeof(PeopleApi)] = new PeopleApi(client),
        }));
        var owner = new Grid(); var window = new Window { Content = owner }; GlobalSearchDialog? active = null;
        var failures = new List<string>();
        void Check(bool passes, string message) { if (!passes) { failures.Add(message); Program.Log("FAIL: " + message); } }
        try
        {
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.Show(false);
            foreach (var width in new[] { 900, 460 })
            {
                window.AppWindow.ResizeClient(new(width, 900)); await Task.Delay(120);
                var scale = owner.XamlRoot.RasterizationScale;
                if (Math.Abs(scale - 1) > .001) { window.AppWindow.ResizeClient(new((int)(width * scale), (int)(900 * scale))); await Task.Delay(120); }
                Check(Math.Abs(owner.XamlRoot.Size.Width - width) < .5, "Quick fixture viewport differs at " + width);
                var requestOnly = Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_QUICK_REQUEST_VISUALS") == "1";
                var failuresOnly = Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_QUICK_FAILURES") == "1";
                foreach (var state in failuresOnly ? new[] { "people-error", "requests-error", "catalog-error-people", "catalog-slow" } : requestOnly ? new[] { "requests-library", "requests-only" } : new[] { "initial", "results", "loading", "empty", "error", "requests-library", "requests-only" })
                {
                    wire.State = state;
                    active = new GlobalSearchDialog { XamlRoot = owner.XamlRoot };
                    var closing = active.ShowAsync().AsTask();
                    await Task.Delay(100);
                    await Field<Task>(active, "_scopeLoadTask");
                    if (state != "initial")
                    {
                        ((TextBox)active.FindName("SearchBox")).Text = "Alpha";
                        await Task.Delay(450);
                    }
                    active.UpdateLayout(); await Task.Delay(60);
                    var frame = Descendants<Border>(active).Single(b => b.Name == "BackgroundElement" && b.Child is Grid { Name: "DialogSpace" });
                    var origin = frame.TransformToVisual(null).TransformPoint(new(0, 0));
                    Program.Log($"Quick visual {width}/{state}: frame={origin.X:R},{origin.Y:R} {frame.ActualWidth:R}x{frame.ActualHeight:R}, radius={frame.CornerRadius.TopLeft:R}.");
                    Check(Math.Abs(origin.Y - 180) <= 1 && Math.Abs(frame.ActualWidth - Math.Min(512, width - 32)) <= 1, $"Quick frame placement {width}/{state}");
                    Check(frame.CornerRadius.TopLeft == 12, $"Quick current customized dialog radius {width}/{state}");
                    if (state == "initial")
                    {
                        Check(Math.Abs(frame.ActualHeight - 50) <= 1, $"Quick empty-query frame should be50px, {width}");
                        Check(active.FindName("EscapeHint") is Border chip && chip.CornerRadius.TopLeft == 4 && chip.Visibility == (width >= 640 ? Visibility.Visible : Visibility.Collapsed), $"Quick initial ESC key chip {width}");
                    }
                    else
                    {
                        var footer = (Border)active.FindName("SearchFooter");
                        Check(footer.Visibility == (width >= 640 ? Visibility.Visible : Visibility.Collapsed), $"Quick responsive footer {width}/{state}");
                        var keys = Descendants<Border>(footer).Where(b => b.Child is TextBlock { Text: "↑" or "↓" or "Esc" }).ToArray();
                        Check(keys.Length == 3 && keys.All(b => b.CornerRadius.TopLeft == 4 && b.BorderThickness.Top == 1), $"Quick Navigate/Close key chips {width}/{state}");
                        if (failuresOnly)
                        {
                            var text = (TextBlock)active.FindName("EmptyText");
                            if (state == "people-error") Check(text.Visibility == Visibility.Visible && text.Text == "Could not load results. Press Enter to open the search page.", "Quick failed people read cannot claim No matches or use unrelated-source copy.");
                            if (state == "requests-error") Check(text.Visibility == Visibility.Visible && text.Text == "No matches", "Quick failed optional request read is hidden without discarding successful empty library/people reads.");
                            if (state is "catalog-error-people" or "catalog-slow")
                            {
                                Check(Descendants<Button>(active).Any(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == "Open Portrait Person, Person"), "Quick independent people read must remain visible during failed/slow catalog reads.");
                                if (state == "catalog-error-people") Check(text.Visibility == Visibility.Visible && text.Text.StartsWith("Could not load results"), "Quick failed catalog error remains visible beside healthy people results.");
                                else Check(((FrameworkElement)active.FindName("LoadingPanel")).Visibility == Visibility.Collapsed, "Quick healthy people row replaces initial loading while catalog remains pending.");
                            }
                        }
                        else if (state.StartsWith("requests"))
                        {
                            var panel = (StackPanel)active.FindName("ResultsPanel");
                            var rows = Descendants<Button>(panel).Where(b => b.Tag is int).ToArray();
                            var beta = rows.Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).Contains("Beta Series"));
                            var gamma = rows.Single(b => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b).Contains("Gamma Feature"));
                            Check(!Descendants<TextBlock>(beta).Any(t => t.Text == "REQUEST"), $"Quick requestable suggestion has no REQUEST badge {width}/{state}");
                            Check(Descendants<TextBlock>(beta).Single(t => t.Text == "Beta Series").FontSize == 14, $"Quick suggestion title typography {width}/{state}");
                            Check(Descendants<TextBlock>(gamma).Any(t => t.Text == "Pending"), $"Quick suggestion retains current request state {width}/{state}");
                            Check(gamma.Opacity == 1 && Descendants<Border>(gamma).Any(b => b.Width == 40 && b.Height == 56 && Math.Abs(b.Opacity - .7) < .00001), $"Quick unavailable opacity is confined to artwork {width}/{state}");
                            Check(state != "requests-only" || Descendants<TextBlock>(panel).Any(t => t.Text == "Not in your library"), $"Quick discovery-only header {width}");
                            var selection = Field<SearchSelectionState>(active, "_selection");
                            for (var index = 0; index <= (int)beta.Tag; index++) selection.Move(1);
                            typeof(GlobalSearchDialog).GetMethod("SyncSelectionHighlight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(active, null);
                            Check(Descendants<Border>(beta).Any(b => b.Child is TextBlock { Text: "↵" } && b.Visibility == (width >= 640 ? Visibility.Visible : Visibility.Collapsed)), $"Quick request selected Enter hint {width}/{state}");
                        }
                        else if (state == "results")
                        {
                            Check(active.FindName("SeeAllHint") is FrameworkElement hint && hint.Visibility == (width >= 640 ? Visibility.Visible : Visibility.Collapsed), $"Quick unselected Enter/See all hint {width}");
                            var rows = Descendants<Button>(active).Where(b => b.Tag is int).ToArray();
                            Check(rows.Length == 2, "Quick fixture title/person rows");
                            var poster = Descendants<Border>(rows[0]).Single(b => b.Width == 40 && b.Height == 56);
                            Check(poster.CornerRadius.TopLeft == 10, $"Quick customized poster radius {width}");
                            Field<SearchSelectionState>(active, "_selection").Move(1);
                            typeof(GlobalSearchDialog).GetMethod("SyncSelectionHighlight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(active, null);
                            active.UpdateLayout();
                            Check(Descendants<Border>(rows[0]).Any(b => b.Child is TextBlock { Text: "↵" } && b.Visibility == (width >= 640 ? Visibility.Visible : Visibility.Collapsed)), $"Quick selected-row Enter chip {width}");
                            Check(active.FindName("SeeAllHint") is FrameworkElement selectedHint && selectedHint.Visibility == Visibility.Collapsed, $"Quick selected row owns Enter {width}");
                        }
                        else
                        {
                            var status = state == "loading" ? Descendants<TextBlock>((DependencyObject)active.FindName("LoadingPanel")).Single(t => t.Text == "Searching...") : (TextBlock)active.FindName("EmptyText");
                            Check(status.FontSize == 14 && status.LineHeight == 20, $"Quick status typography {width}/{state}");
                            Check(!Descendants<ProgressRing>(active).Any(), $"Quick text-only loading {width}/{state}");
                            var expected = state == "error" ? (width >= 640 ? 155.333 : 119) : (width >= 640 ? 171.333 : 135);
                            Check(Math.Abs(frame.ActualHeight - expected) <= 2, $"Quick status frame height {width}/{state}, expected{expected:R}");
                        }
                    }
                    await Capture(active, $"quick-{state}-{width}.png");
                    active.Hide(); await closing; active = null;
                }
            }
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
            Program.Log("PASS: actual Quick Search current-source initial/results/selection/loading/empty/error frames, key chips and responsive hints at900/460.");
        }
        finally { active?.Hide(); window.Close(); servicesField.SetValue(null, previous); }
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static async Task Capture(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        if (bitmap.PixelWidth == 0) throw new InvalidOperationException("Quick visual capture was empty.");
        using var stream = File.Create(Path.Combine(Program.ResultDirectory, name)).AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values) : IServiceProvider
    { public object? GetService(Type type) => values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal string State = "results";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "quick-visual.invalid") throw new InvalidOperationException("Quick visual fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath; object body = new { items = Array.Empty<object>() }; var status = HttpStatusCode.OK;
            if (path == "/api/v2/catalog/search/capabilities") body = new { people_media_scope = true };
            else if (path == "/api/v2/requests/status") body = new { requests_enabled = State.StartsWith("requests"), allowed = true };
            else if (path == "/api/v2/requests/search" && State == "requests-error") { status = HttpStatusCode.ServiceUnavailable; body = new { message = "bounded optional failure" }; }
            else if (path == "/api/v2/requests/search") body = new { results = new object[] {
                new { tmdb_id = 701, media_type = "series", title = "Beta Series", year = 2026, availability = "unavailable", request = new { requestable = true, status = "", reason = "" } },
                new { tmdb_id = 702, media_type = "movie", title = "Gamma Feature", year = 2024, availability = "unavailable", request = new { requestable = false, status = "pending", state = "pending", reason = "already_requested" } } } };
            else if (path == "/api/v2/catalog")
            {
                if (State == "loading") await Task.Delay(Timeout.Infinite, ct);
                if (State == "catalog-slow") await Task.Delay(1500, ct);
                if (State is "error" or "catalog-error-people") { status = HttpStatusCode.ServiceUnavailable; body = new { message = "fixture unavailable" }; }
                else body = new { items = State is "empty" or "requests-only" or "people-error" or "requests-error" or "catalog-slow" ? Array.Empty<object>() : new object[] { new { content_id = "alpha-movie", play_content_id = "alpha-movie", type = "movie", title = "Alpha Feature", year = 2025 } }, page = new { has_more = false } };
            }
            else if (path == "/api/v2/catalog/people" && State == "people-error") { status = HttpStatusCode.ServiceUnavailable; body = new { message = "bounded people failure" }; }
            else if (path == "/api/v2/catalog/people") body = new { items = State is "results" or "catalog-error-people" or "catalog-slow" ? new object[] { new { id = "portrait-person", name = "Portrait Person" } } : Array.Empty<object>() };
            else if (path.StartsWith("/api/v2/settings/")) body = new { key = "search.media_scope", value = "video" };
            else throw new InvalidOperationException("Unexpected Quick visual route: " + path);
            return new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        }
    }
}
