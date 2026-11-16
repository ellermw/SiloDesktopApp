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
            SeriesCardCases(page, failures);
            await SeriesCoverLifetimeCasesAsync(page, servicesField);
            await SeriesPaginationCasesAsync(servicesField);
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

    /// <summary>
    /// Official #4397 series cards: one square cover (initials when no cover
    /// loads), a decorative bar with a segment per book up to 12 and one
    /// continuous bar past that, and listening counts that wrap.
    /// </summary>
    private static void SeriesCardCases(LibraryPage page, List<string> failures)
    {
        var group = new AudiobookGroup { Name = "Night Watch Saga", ItemCount = 5, FinishedCount = 2, InProgressCount = 1, TotalDurationSeconds = 36000 };
        var card = (Button)Invoke(page, "CreateAudiobookSeriesCard", group, 180d, CancellationToken.None)!;
        var content = (StackPanel)card.Content;
        var cover = content.Children[0] as Border;
        var bar = content.Children[1] as Grid;
        var listening = (TextBlock)content.Children[4];
        void Expect(bool condition, string message)
        {
            if (!condition) failures.Add(message);
            else Program.Log("PASS: " + message);
        }
        Expect(cover is { Width: 180d, Height: 180d, Child: TextBlock { Text: "NW" } },
            "series card shows one square cover and falls back to initials when no cover is left");
        Expect(content.Children.OfType<Border>().Count() == 1,
            "series card no longer fans several covers");
        Expect(bar != null && bar.ColumnDefinitions.Count == 5
            && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAccessibilityView(bar) == Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw,
            "series run bar has a segment per book and stays decorative for UI Automation");
        var segments = bar?.Children.OfType<Grid>().ToArray() ?? Array.Empty<Grid>();
        Expect(segments.Length == 5
            && segments[0].Children.Count == 2 && segments[0].ColumnDefinitions.Count == 0
            && segments[1].Children.Count == 2 && segments[1].ColumnDefinitions.Count == 0
            && segments[2].Children.Count == 2 && segments[2].ColumnDefinitions.Count == 2
            && segments[3].Children.Count == 1 && segments[4].Children.Count == 1,
            "finished books fill their segment and an in-progress book half-fills its own");
        Expect(listening.Text == "2 finished · 1 in progress" && listening.TextWrapping == TextWrapping.WrapWholeWords
            && listening.MaxLines == 0 && listening.Visibility == Visibility.Visible,
            "series listening lists finished and in-progress counts and wraps instead of clipping");

        var longRun = (Button)Invoke(page, "CreateAudiobookSeriesCard",
            new AudiobookGroup { Name = "Long", ItemCount = 20, FinishedCount = 20 }, 180d, CancellationToken.None)!;
        var longContent = (StackPanel)longRun.Content;
        Expect(longContent.Children[1] is Grid { ColumnDefinitions.Count: 1 }
            && ((TextBlock)longContent.Children[4]).Text == "all finished",
            "a series past 12 books draws one continuous bar and says all finished");

        var unstarted = (Button)Invoke(page, "CreateAudiobookSeriesCard",
            new AudiobookGroup { Name = "Unstarted", ItemCount = 3 }, 180d, CancellationToken.None)!;
        Expect(((TextBlock)((StackPanel)unstarted.Content).Children[4]).Visibility == Visibility.Collapsed,
            "an unstarted series shows no listening line");
    }

    private static async Task SeriesCoverLifetimeCasesAsync(LibraryPage page, FieldInfo servicesField)
    {
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        var cache = Path.Combine(Path.GetTempPath(), "silo-series-cover-" + Guid.NewGuid().ToString("N"));
        using var images = new ImageService(cache);
        using var wire = new CoverWire();
        using var http = new HttpClient(wire);
        using var departure = new CancellationTokenSource();
        var host = (Panel)page.FindName("AudiobookGroupsHost");
        var axis = typeof(LibraryPage).GetField("_currentAudiobookAxis", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var navigated = typeof(LibraryPage).GetField("_isNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var owner = typeof(LibraryPage).GetField("_audiobookGroupLoadCts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        servicesField.SetValue(null, new CoverServices(previous, images, http));
        try
        {
            // Both cards are waiting on downloads when departure cancels their
            // token. Returning to Home must not strand them on the Library tab.
            axis.SetValue(page, "series");
            for (var index = 0; index < 2; index++)
            {
                var group = new AudiobookGroup { Name = "Pending Series", PosterUrls = [$"https://covers.invalid/pending-{index}"] };
                host.Children.Add((Button)Invoke(page, "CreateAudiobookSeriesCard", group, 180d, departure.Token)!);
            }
            // These retained cards represent a completed group response; only
            // their artwork downloads are still pending at departure.
            typeof(LibraryPage).GetField("_audiobookGroupsKey", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(page, Invoke(page, "CurrentAudiobookGroupsKey"));
            typeof(LibraryPage).GetField("_audiobookGroupsLoadCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(page, true);
            await wire.PendingStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            departure.Cancel();
            Invoke(page, "ShowTab", "Recommended");
            Check(host.Children.OfType<Button>().All(card => ((Border)((StackPanel)card.Content).Children[0]).Child == null),
                "departure interrupts pending series covers before any image or initials is installed");
            wire.Release.TrySetResult(true);
            navigated.SetValue(page, true);
            var viewModel = page.ViewModel;
            var library = viewModel.Library;
            viewModel.Library = new Library { Id = library!.Id + 1, Type = "audiobooks" };
            Invoke(page, "ShowTab", "Library");
            Check(host.Children.OfType<Button>().All(card => ((Border)((StackPanel)card.Content).Children[0]).Child == null),
                "showing another library never resumes retained covers from the previous library");
            viewModel.Library = library;
            Invoke(page, "ShowTab", "Recommended");
            Invoke(page, "ShowTab", "Library");
            var covers = host.Children.OfType<Button>().Select(card => (Border)((StackPanel)card.Content).Children[0]).ToArray();
            for (var attempt = 0; attempt < 200 && covers.Any(cover => cover.Child == null); attempt++)
                await Task.Delay(10);
            Check(covers.Length == 2 && covers.All(cover => cover.Child is TextBlock { Text: "PS" }),
                "re-showing Library resumes every retained cancelled series cover to initials without rebuilding cards");

            var timeout = "https://covers.invalid/timeout";
            var valid = "https://covers.invalid/valid";
            var failed = new HashSet<string>(StringComparer.Ordinal);
            var recovered = new Border();
            await (Task)Invoke(page, "LoadAudiobookSeriesCoverAsync", recovered,
                new AudiobookGroup { Name = "Timeout Series", PosterUrls = [timeout, valid] }, 180d, failed, CancellationToken.None)!;
            Check(failed.Contains(timeout) && !failed.Contains(valid) && recovered.Child is Image,
                "download TaskCanceledException with an uncancelled caller token falls through to a valid cover");
            var fallback = new Border();
            await (Task)Invoke(page, "LoadAudiobookSeriesCoverAsync", fallback,
                new AudiobookGroup { Name = "Timeout Series", PosterUrls = [timeout] }, 180d,
                new HashSet<string>(StringComparer.Ordinal), CancellationToken.None)!;
            Check(fallback.Child is TextBlock { Text: "TS" }, "exhausted timed-out covers fall back to initials");
        }
        finally
        {
            wire.Release.TrySetResult(true);
            navigated.SetValue(page, false);
            axis.SetValue(page, "books");
            var resumed = (CancellationTokenSource?)owner.GetValue(page);
            resumed?.Cancel();
            resumed?.Dispose();
            owner.SetValue(page, null);
            Invoke(page, "ResetAudiobookGroups");
            servicesField.SetValue(null, previous);
            Directory.Delete(cache, recursive: true);
        }
    }

    private static async Task SeriesPaginationCasesAsync(FieldInfo servicesField)
    {
        var previous = (IServiceProvider)servicesField.GetValue(null)!;
        foreach (var resize in new[] { false, true })
        {
            using var wire = new SeriesGroupsWire();
            using var http = new HttpClient(wire);
            var api = new SiloApiClient(http);
            api.SetBaseUrl("https://series-pagination.invalid");
            servicesField.SetValue(null, new SeriesGroupsServices(previous, new CatalogApi(api)));
            var page = new LibraryPage();
            var panel = (ScrollViewer)page.FindName("AudiobookGroupsPanel");
            var host = (Panel)page.FindName("AudiobookGroupsHost");
            var window = new Window();
            void Set(string name, object value) => typeof(LibraryPage)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
            try
            {
                page.ViewModel.Library = new Library { Id = 17, Type = "audiobooks" };
                Set("_currentAudiobookAxis", "series");
                Set("_currentTab", "Library");
                Set("_isNavigated", true);
                Set("_posterWidthOverride", resize ? 320d : 140d);
                // Mount just the real groups viewport, avoiding page navigation.
                // Disable ViewChanged so only the new fill path can advance it.
                var viewChangedEvent = typeof(ScrollViewer).GetEvent(nameof(ScrollViewer.ViewChanged))!;
                var viewChanged = typeof(LibraryPage).GetMethod("AudiobookGroupsPanel_ViewChanged",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .CreateDelegate(viewChangedEvent.EventHandlerType!, page);
                viewChangedEvent.RemoveEventHandler(panel, viewChanged);
                ((Panel)panel.Parent).Children.Remove(panel);
                panel.Width = 2560;
                panel.Height = 1440;
                panel.Visibility = Visibility.Visible;
                window.Content = panel;
                window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                window.AppWindow.Show(false);
                await Task.Delay(150);
                panel.UpdateLayout();
                await (Task)Invoke(page, "LoadAudiobookGroupsAsync", true)!;

                if (resize)
                {
                    await Task.Delay(150);
                    panel.UpdateLayout();
                    Check(host.Children.Count == 60 && panel.ScrollableHeight >= 360 && wire.RequestCount == 1,
                        "large series cards stop loading when the first page fills the viewport");
                    Set("_posterWidthOverride", 140d);
                    Invoke(page, "ResizeAudiobookSeriesCards");
                }
                for (var attempt = 0; attempt < 200 && host.Children.Count != 61; attempt++)
                    await Task.Delay(10);
                panel.UpdateLayout();
                Check(host.Children.Count == 61 && wire.SecondPageRequests == 1 && panel.VerticalOffset == 0,
                    resize ? "shrinking retained series cards requests the next page without scrolling"
                        : "narrow series posters request the next page without scrolling an underfilled viewport");
                Check(panel.ScrollableHeight == 0,
                    "the dense series pagination case has no scrollable range");
                var requests = wire.RequestCount;
                Invoke(page, "ResizeAudiobookSeriesCards");
                await Task.Delay(150);
                Check(wire.RequestCount == requests, "series viewport fill stops when HasMore is false");
            }
            finally
            {
                Set("_isNavigated", false);
                Invoke(page, "ResetAudiobookGroups");
                window.Content = null;
                window.Close();
                servicesField.SetValue(null, previous);
            }
        }
    }

    private sealed class SeriesGroupsServices(IServiceProvider fallback, CatalogApi catalog) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(CatalogApi) ? catalog : fallback.GetService(type);
    }

    private sealed class SeriesGroupsWire : HttpMessageHandler
    {
        internal int RequestCount;
        internal int SecondPageRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var second = request.RequestUri!.Query.Contains("cursor=series-next", StringComparison.Ordinal);
            if (second) SecondPageRequests++;
            var items = Enumerable.Range(second ? 60 : 0, second ? 1 : 60)
                .Select(index => new AudiobookGroup { Name = $"Series {index:00}", ItemCount = 3 });
            var json = JsonSerializer.Serialize(new
            {
                items,
                total = 61,
                total_exact = true,
                page = new { has_more = !second, next_cursor = second ? null : "series-next" },
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class CoverServices(IServiceProvider fallback, ImageService images, HttpClient http) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(ImageService) ? images
            : type == typeof(HttpClient) ? http : fallback.GetService(type);
    }

    private sealed class CoverWire : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<bool> PendingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _pendingCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/pending-", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _pendingCount) == 2) PendingStarted.TrySetResult(true);
                await Release.Task;
            }
            if (path == "/timeout") throw new TaskCanceledException("Synthetic download timeout");
            return path == "/valid"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII=")) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
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
