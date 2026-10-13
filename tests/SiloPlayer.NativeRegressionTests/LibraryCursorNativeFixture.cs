using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class LibraryCursorNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, Window window)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://library-cursor.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); auth.SelectProfile("fixture");
        var catalog = new CatalogApi(client); var settings = new SettingsApi(client);
        using var events = new EventChannelClient(client, auth);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "images"));
        var navigation = new NavigationService();
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth, [typeof(CatalogApi)] = catalog,
            [typeof(SettingsApi)] = settings, [typeof(NavigationService)] = navigation,
            [typeof(SettingsService)] = new SettingsService(Path.Combine(Program.ResultDirectory, "settings")),
            [typeof(UICustomizationService)] = new UICustomizationService(settings),
            [typeof(CardOverlayService)] = new CardOverlayService(settings), [typeof(EventChannelClient)] = events,
            [typeof(ImageService)] = images, [typeof(HttpClient)] = http,
        }, () => new LibraryViewModel(catalog)));
        Frame? frame = null;
        try
        {
            foreach (var (type, width, height) in new[] { ("movie", 1920, 1080), ("series", 3440, 1440), ("audiobook", 3840, 2160) })
            {
                window.AppWindow.ResizeClient(new(width, height));
                frame = new Frame { Width = width, Height = height }; parent.Children.Add(frame); navigation.Frame = frame;
                frame.Navigate(typeof(LibraryPage), new Library { Id = wire.LibraryId++, Name = "Fixture library", Type = type });
                var page = (LibraryPage)frame.Content;
                await Until(() => page.IsLoaded && page.ViewModel.Library is not null);
                ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("LibraryTab")).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => page.ViewModel.GetWindowItem(0) is not null);
                var scroll = (ScrollViewer)page.FindName("LibraryScroller");
                foreach (var offset in new[] { 600d, 1600d, 4000d })
                {
                    await Until(() => scroll.ScrollableHeight > offset);
                    scroll.ChangeView(null, offset, null, true);
                    await Task.Delay(250);
                    await Until(() => !page.ViewModel.IsLoading);
                    if (wire.InvalidCursors != 0 || page.ViewModel.ErrorMessage is not null)
                        throw new InvalidOperationException("Actual Library tab scrolling failed: " + page.ViewModel.ErrorMessage);
                }
                Program.Log($"PASS: actual {type} Library tab scroll at {width}x{height} retains a valid cursor scope.");
                frame.Navigate(typeof(Page)); parent.Children.Remove(frame); await Task.Delay(100); frame = null;
            }
        }
        finally
        {
            frame?.Navigate(typeof(Page)); if (frame is not null) parent.Children.Remove(frame);
            await Task.Delay(150); navigation.Frame = null; field.SetValue(null, previous);
        }
    }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 200; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Library cursor native state did not settle."); }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<LibraryViewModel> model) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(LibraryViewModel) ? model() : values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal int InvalidCursors, LibraryId = 11;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/catalog")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
                    .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
                var limit = int.Parse(query["limit"]); var offset = query.TryGetValue("seek", out var seek) ? int.Parse(seek) : 0;
                if (query.TryGetValue("cursor", out var cursor))
                {
                    var scope = cursor.Split(':');
                    if (scope[0] != query["library_id"] || scope[1] != query["limit"])
                    {
                        InvalidCursors++;
                        return Reply(new { status = 400, type = "https://fixture.invalid/problems/invalid_cursor", detail = "The cursor is malformed, tampered with, or belongs to a different query." }, HttpStatusCode.BadRequest);
                    }
                    if (!query.ContainsKey("seek")) offset = int.Parse(scope[2]);
                }
                var count = Math.Min(limit, 1000 - offset);
                return Reply(new { items = Enumerable.Range(offset, count).Select(index => new { content_id = "item-" + index, title = "Fixture " + index, type = "movie" }),
                    total = 1000, total_exact = !query.ContainsKey("skip_total"), window_cursor = query["library_id"] + ":" + limit + ":0",
                    page = new { has_more = offset + count < 1000, next_cursor = query["library_id"] + ":" + limit + ":" + (offset + count) } });
            }
            if (path.EndsWith("/layout") || path.EndsWith("/sections")) return Reply(new { sections = Array.Empty<object>() });
            if (path.EndsWith("/filters")) return Reply(new { genres = Array.Empty<string>() });
            return Reply(new { status = 404, detail = "No isolated fixture value" }, HttpStatusCode.NotFound);
        }
        private static Task<HttpResponseMessage> Reply(object body, HttpStatusCode status = HttpStatusCode.OK)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
    }
}
