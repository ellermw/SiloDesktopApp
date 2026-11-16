using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Home;
using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class BooksLibraryNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var surface = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Padding = new Thickness(12), Width = 780 };
        parent.Children.Add(surface);
        var item = JsonSerializer.Deserialize<MediaItem>("""
            {"content_id":"library-book","type":"audiobook","title":"An unfinished journey","year":2026,
             "status":"matched","runtime":240,"authors":["Avery Writer","Morgan Writer"],
             "position_seconds":3600,"duration_seconds":14400,"user_state":{"played":false}}
            """, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        var audio = new LibraryGridCard();
        audio.SetLayout(185, 185, 261);
        audio.Bind(item, "title");
        var ebook = new LibraryGridCard();
        ebook.Bind(new() { ContentId = "ebook", Type = "ebook", Title = "Portrait ebook", Year = 2026 }, "title");
        var comic = new LibraryGridCard();
        comic.Bind(new() { ContentId = "comic", Type = "manga", Title = "Portrait comic", ShowStatus = "Ongoing", MangaVolumeCount = 12, MangaChapterCount = 3, UserState = new() { Played = true } }, "title");
        surface.Children.Add(audio);
        surface.Children.Add(ebook);
        surface.Children.Add(comic);
        await Task.Delay(200);
        surface.UpdateLayout();
        await MediaParityNativeFixture.CaptureAsync(surface, "books-library-in-progress.png");
        Check(Text(audio, "_subtitleText") == "3h left", "in-progress audiobook resting caption names time left");
        Check(Text(audio, "_authorText") == "Avery Writer, Morgan Writer", "browse authors survive catalog deserialization and appear under the title");
        var track = Field<Border>(audio, "_listeningTrack");
        var fill = Field<Border>(audio, "_listeningFill");
        Check(track.Visibility == Visibility.Visible && Math.Abs(fill.Width / track.Width - .25) < .001, "audiobook has a quarter-filled progress track");
        Check(Math.Abs(Canvas.GetTop(Field<TextBlock>(audio, "_subtitleText")) - 241) < .1, "author and status occupy separate caption lines");
        Check(Field<Border>(ebook, "_posterBackground").Height == ebook.Width * 1.5 && Field<Border>(comic, "_posterBackground").Height == comic.Width * 1.5, "ebook and comic covers retain portrait geometry");
        Check(Field<Border>(ebook, "_listeningTrack").Visibility == Visibility.Collapsed && Field<Border>(comic, "_listeningTrack").Visibility == Visibility.Collapsed, "reading cards do not get audiobook progress");
        Check(Field<FrameworkElement>(comic, "_watchStatus").Visibility == Visibility.Collapsed, "comic status/count chips do not collide with a generic watch-status badge");
        item.UserState!.Played = true;
        audio.RefreshState();
        Check(track.Visibility == Visibility.Collapsed && Text(audio, "_subtitleText") == "4h", "completed refresh hides progress and restores total duration");
        audio.Bind(item, "author");
        Check(Field<TextBlock>(audio, "_authorText").Visibility == Visibility.Collapsed, "author sort does not repeat the author line");
        audio.BindPlaceholder();
        Check(track.Visibility == Visibility.Collapsed && Field<TextBlock>(audio, "_authorText").Visibility == Visibility.Collapsed, "recycled placeholders clear book-specific state");
        audio.Bind(item, "year");
        Check(Text(audio, "_subtitleText") == "2026", "explicit sort metadata still wins over resting duration");
        item.UserState.Played = false;
        item.PositionSeconds = 7200;
        audio.Bind(item, "title");
        audio.SetLayout(240, 240, 316);
        Check(Math.Abs(fill.Width / track.Width - .5) < .001, "resizing preserves the current progress ratio");
        await MediaParityNativeFixture.CaptureAsync(surface, "books-library-resized.png");
        audio.Reset();
        Check(track.Visibility == Visibility.Collapsed && Field<TextBlock>(audio, "_authorText").Visibility == Visibility.Collapsed, "recycled cards clear book-specific state");
        parent.Children.Remove(surface);
        await Task.Delay(150);
        Program.Log("PASS: books library audiobook captions/progress, reading-card geometry, sorting, refresh, resize and recycling.");
    }

    private static T Field<T>(object owner, string name) => (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner)
        ?? throw new InvalidOperationException("Missing book-library card field: " + name));
    private static string Text(object owner, string name) => Field<TextBlock>(owner, name).Text;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Program.Log("PASS: " + message);
    }

    internal static async Task RunPageBehaviorAsync()
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        using var wire = new PageWire();
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http);
        api.SetBaseUrl("https://books-page.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        using var events = new EventChannelClient(api, auth);
        var viewModel = new LibraryViewModel(new CatalogApi(api));
        servicesField.SetValue(null, new PageServices(previous, viewModel, events, new SettingsApi(api)));
        var failures = new List<string>();
        try
        {
            var page = new LibraryPage();
            foreach (var (type, expected) in new[] { ("audiobooks", "Listening:"), ("ebooks", "Read:"), ("manga", "Read:") })
            {
                viewModel.Library = new Library { Id = 17, Type = type };
                viewModel.UseAdvancedRules = false;
                viewModel.SelectedWatchStatus = "in_progress";
                Invoke(page, "UpdateActiveFilterBadges");
                var labels = string.Join(" ", Texts((Panel)page.FindName("FilterBadgesPanel")));
                if (!labels.Contains(expected)) failures.Add(type + " uses the wrong status badge: " + labels);
                else Program.Log("PASS: " + type + " uses book-specific status labels.");
            }
            foreach (var axis in new[] { "author", "narrator", "series" })
            {
                viewModel.Library = new Library { Id = 17, Type = "audiobooks" };
                viewModel.UseAdvancedRules = true;
                viewModel.AdvancedRulesMatch = "any";
                viewModel.AdvancedGroups.Clear();
                var stale = new EditableQueryGroup();
                stale.Rules.Add(new QueryRule { Field = "year", Op = "is", Value = 1999 });
                viewModel.AdvancedGroups.Add(stale);
                typeof(LibraryPage).GetField("_currentAudiobookAxis", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, axis);
                await (Task)Invoke(page, "SelectAudiobookGroupAsync", "Selected Group")!;
                var parameters = wire.LastCatalog!.Query.TrimStart('?').Split('&').Select(value => value.Split('=', 2))
                    .ToDictionary(value => value[0], value => Uri.UnescapeDataString(value[1]));
                using var groups = JsonDocument.Parse(parameters["groups"]);
                var rules = groups.RootElement.EnumerateArray().SelectMany(group => group.GetProperty("rules").EnumerateArray()).ToArray();
                if (rules.Length != 1 || rules[0].GetProperty("field").GetString() != axis || rules[0].GetProperty("value").GetString() != "Selected Group")
                    failures.Add(axis + " group selection did not replace the stale query: " + parameters["groups"]);
                else Program.Log("PASS: " + axis + " group click requests only its selected group.");
            }
            viewModel.ErrorMessage = "Synthetic catalog failure";
            typeof(LibraryPage).GetField("_libraryCatalogLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, true);
            Invoke(page, "RenderVirtualGrid", false);
            if (((TextBlock)page.FindName("LibraryEmptyText")).Visibility != Visibility.Collapsed)
                failures.Add("Failed book catalog must not claim the library is empty.");
            else Program.Log("PASS: failed book catalog does not display an empty-library claim.");
            if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        }
        finally { servicesField.SetValue(null, previous); }
    }

    private static object? Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);

    private static IEnumerable<string> Texts(UIElement element)
    {
        if (element is TextBlock text) yield return text.Text;
        var children = element is Panel panel ? panel.Children.Cast<UIElement>()
            : element is Border { Child: { } child } ? new[] { child } : [];
        foreach (var next in children)
        foreach (var value in Texts(next)) yield return value;
    }

    private sealed class PageServices(IServiceProvider fallback, LibraryViewModel viewModel, EventChannelClient events, SettingsApi settings) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(LibraryViewModel) ? viewModel : type == typeof(EventChannelClient) ? events
            : type == typeof(SettingsApi) ? settings : fallback.GetService(type);
    }

    private sealed class PageWire : HttpMessageHandler
    {
        internal Uri? LastCatalog;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/catalog") LastCatalog = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[],\"total\":0,\"page\":{\"has_more\":false}}") });
        }
    }
}
