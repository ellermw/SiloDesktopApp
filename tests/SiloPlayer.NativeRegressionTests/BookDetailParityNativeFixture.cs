using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
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
/// Actual ItemDetailPage book pages against the current WebUI
/// (AudiobookContent.tsx / audiobooks.css / EbookContent.tsx, Silo-Server
/// 17d5376): an audiobook at lg, sm and base widths, then the same retained
/// page instance switched to an ebook and a comic and back again.
/// </summary>
internal static partial class BookDetailParityNativeFixture
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    internal static async Task RunAsync()
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)field.GetValue(null)!;
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://book-detail-fixture.invalid"); client.SetProfile("book-profile");
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "book-detail-image-cache"));
        field.SetValue(null, new LocalServices(original, client, http, images));
        var differences = new List<string>();
        var owner = new Grid(); var window = new Window { Content = owner };
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000)); window.AppWindow.Show(false); await Task.Delay(150);
            var scale = owner.XamlRoot.RasterizationScale;
            var page = new ItemDetailPage { Width = 1440, Height = 1000 }; owner.Children.Add(page); await Task.Delay(150);
            var audiobook = JsonSerializer.Deserialize<MediaItemDetail>(AudiobookJson(), Json)!;

            foreach (var (width, height) in new[] { (1440d, 2300d), (900d, 2500d), (500d, 2900d) })
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale)));
                page.Width = width; page.Height = height;
                await ShowAsync(page, audiobook, width);
                AssertAudiobook(page, width, differences);
                await MediaParityNativeFixture.CaptureAsync(owner, $"book-detail-audiobook-{width:0}.png");
            }

            // Show all expands the eight-row window to every chapter.
            var toggle = (Button)page.FindName("AudiobookChaptersToggleButton");
            ((IInvokeProvider)new ButtonAutomationPeer(toggle).GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => ((StackPanel)page.FindName("AudiobookChaptersPanel")).Children.Count == 14);
            if (((TextBlock)page.FindName("AudiobookChaptersToggleText")).Text != "Show fewer")
                differences.Add("Expanded chapters must offer Show fewer.");
            await MediaParityNativeFixture.CaptureAsync(owner, "book-detail-audiobook-500-all-chapters.png");

            // Viewport-height capture at desktop size: what the user first sees.
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(1440 * scale), (int)(900 * scale)));
            page.Width = 1440; page.Height = 900;
            await ShowAsync(page, audiobook, 1440);
            await MediaParityNativeFixture.CaptureAsync(owner, "book-detail-audiobook-1440-viewport.png");

            // The same retained page switches to the ebook layout: the audiobook's
            // borrowed controls go back, the ebook's metadata and ratings move in.
            await RunEbookAsync(window, owner, page, scale, differences);

            var comic = new MediaItemDetail
            {
                ContentId = "fixture-comic", Type = "manga", Title = "Fixture Comic", Year = 2019, PosterUrl = "https://book-detail-fixture.invalid/cover-comic",
                Overview = "A synthetic comic series used to check that the comic detail keeps DetailHero after an audiobook.",
                Manga = new() { Chapters =
                [
                    new() { ContentId = "fixture-comic-2", Title = "A long chapter title inside the first volume", ChapterIndex = 2, Volume = "v01" },
                    new() { ContentId = "fixture-comic-1", Title = "The opening loose chapter", ChapterIndex = 1 },
                    new() { ContentId = "fixture-comic-4", Title = "Another long chapter title inside the first volume", ChapterIndex = 4, Volume = "1" },
                    new() { ContentId = "fixture-comic-3", Title = "The middle chapter inside the first volume", ChapterIndex = 3, Volume = "01" },
                ] },
            };
            await ShowAsync(page, comic, 1440);
            if (((FrameworkElement)page.FindName("AudiobookSidePanel")).Visibility == Visibility.Visible || ((FrameworkElement)page.FindName("HeroInfoPanel")).Visibility != Visibility.Visible
                || ((FrameworkElement)page.FindName("MangaChaptersSection")).Visibility != Visibility.Visible)
                differences.Add("Comic detail after an audiobook must use DetailHero with its chapter list.");
            await MediaParityNativeFixture.CaptureAsync(owner, "book-detail-comic-1440.png");
            foreach (var width in new[] { 1440d, 900d, 500d })
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(1700 * scale)));
                page.Width = width;
                await Task.Delay(200);
                AssertComicChapters(page, width, differences);
            }

            // Re-entry borrows the controls again without duplicating them.
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(1440 * scale), (int)(2300 * scale)));
            page.Width = 1440; page.Height = 2300;
            // The chapter list was expanded earlier on this same book object; a fresh visit starts collapsed.
            Set(page, "_audiobookChaptersExpanded", false);
            await ShowAsync(page, audiobook, 1440);
            AssertAudiobook(page, 1440, differences);

            Program.Log($"TRACE: book detail fixture unexpected requests: {string.Join(", ", handler.Unexpected.Distinct())}.");
            if (differences.Count > 0) throw new InvalidOperationException(string.Join("\n", differences));
            Program.Log("PASS: BOOK_DETAIL_PARITY_COMPLETED actual audiobook book page at 1440/900/500 (tint, cover column, head, facts, listening progress, chapter window, square rails) and the ebook page (series with progress, bare, no related titles, loading placeholder at 1440/1024/800), retained switch to ebook/comic DetailHero and back; captures saved.");
        }
        finally
        {
            owner.Children.Clear();
            window.Close();
            field.SetValue(null, original);
        }
    }

    private static async Task ShowAsync(ItemDetailPage page, MediaItemDetail item, double width)
    {
        Set(page, "_navigationCts", new CancellationTokenSource());
        Set(page, "_currentContentId", item.ContentId);
        page.ViewModel.Item = item;
        Call(page, "UpdateUI");
        Call(page, "UpdateResponsiveLayout", width);
        page.UpdateLayout();
        var cover = (Image)page.FindName(item.Type == "audiobook" ? "AudiobookCoverImage" : "HeroPosterImage");
        for (var i = 0; i < 80 && cover.Source == null; i++) await Task.Delay(25);
        await Task.Delay(250);
        page.UpdateLayout();
    }

    private static void AssertAudiobook(ItemDetailPage page, double width, List<string> differences)
    {
        T Named<T>(string name) where T : class => (T)page.FindName(name);
        var at = $"audiobook/{width}";
        if (Named<FrameworkElement>("BackdropImage").Visibility == Visibility.Visible || Named<FrameworkElement>("AudiobookTint").Visibility != Visibility.Visible)
            differences.Add($"{at}: the book page must replace the backdrop with the cover tint.");
        if (Named<FrameworkElement>("HeroInfoPanel").Visibility == Visibility.Visible || Named<FrameworkElement>("DetailContentPanel").Visibility == Visibility.Visible)
            differences.Add($"{at}: DetailHero copy and the below-hero stack must not render on the book page.");
        var side = Named<StackPanel>("AudiobookSidePanel"); var head = Named<FrameworkElement>("AudiobookHeadPanel");
        var body = Named<FrameworkElement>("AudiobookBodyPanel"); var cover = Named<FrameworkElement>("AudiobookCover");
        var actions = Named<FrameworkElement>("AudiobookActionsPanel"); var grid = Named<Grid>("HeroContentGrid");
        var expectedCover = width >= 1024 ? 260 : width >= 640 ? 176 : 112;
        if (Math.Abs(cover.ActualWidth - expectedCover) > 1 || Math.Abs(cover.ActualHeight - cover.ActualWidth) > 1)
            differences.Add($"{at}: square cover must be {expectedCover}px (was {cover.ActualWidth}x{cover.ActualHeight}).");
        var sideBounds = Bounds(side, grid); var headBounds = Bounds(head, grid); var bodyBounds = Bounds(body, grid); var actionBounds = Bounds(actions, grid);
        Program.Log($"TRACE: {at} side={sideBounds} head={headBounds} body={bodyBounds} actions={actionBounds} grid={grid.ActualWidth}x{grid.ActualHeight}.");
        if (width >= 1024)
        {
            if (!side.Children.Contains(actions) || headBounds.X <= sideBounds.X + sideBounds.Width || bodyBounds.X <= sideBounds.X + sideBounds.Width || bodyBounds.Y <= headBounds.Y)
                differences.Add($"{at}: lg must place cover and actions in a side column beside the head and body.");
        }
        else if (side.Children.Contains(actions) || actionBounds.Y < sideBounds.Y + sideBounds.Height - 1 || Math.Abs(bodyBounds.X - sideBounds.X) > 1
                 || headBounds.X <= sideBounds.X + sideBounds.Width)
            differences.Add($"{at}: below lg the cover sits beside the head with actions and body across the page.");

        var series = Named<TextBlock>("AudiobookSeriesText").Text;
        if (series != "The Wayfarer Saga, book 2") differences.Add($"{at}: series line was '{series}'.");
        if (Named<TextBlock>("AudiobookTitleText").Text != "The Long Way Home") differences.Add($"{at}: title missing.");
        if (!Text(Named<TextBlock>("BookAuthorLine")).Equals("By Jane Writer", StringComparison.Ordinal))
            differences.Add($"{at}: author line was '{Text(Named<TextBlock>("BookAuthorLine"))}'.");
        if (Named<FrameworkElement>("NarrationPickerButton").Visibility != Visibility.Visible || Named<FrameworkElement>("BookNarratorLine").Visibility == Visibility.Visible)
            differences.Add($"{at}: the narrator name must become the narration picker.");
        if (Named<TextBlock>("AudiobookListenText").Text != "Resume · Part Five")
            differences.Add($"{at}: primary label was '{Named<TextBlock>("AudiobookListenText").Text}'.");
        if (Named<FrameworkElement>("AudiobookProgressPanel").Visibility != Visibility.Visible
            || Named<TextBlock>("AudiobookListenedText").Text != "1h 23m listened · 30%" || Named<TextBlock>("AudiobookTimeLeftText").Text != "3 hr 17 min left")
            differences.Add($"{at}: listened bar was '{Named<TextBlock>("AudiobookListenedText").Text}' / '{Named<TextBlock>("AudiobookTimeLeftText").Text}'.");
        if (Named<FrameworkElement>("AudiobookListenFromStartButton").Visibility != Visibility.Visible || Named<FrameworkElement>("AudiobookCollectionButton").Visibility != Visibility.Visible)
            differences.Add($"{at}: Listen from Start and Add to Collection must show for a started book.");
        // Their row is collapsed by the ebook page; a retained page must show it again.
        if (Named<FrameworkElement>("AudiobookSecondaryActions").Visibility != Visibility.Visible || Named<FrameworkElement>("AudiobookCollectionButton").ActualWidth <= 0)
            differences.Add($"{at}: the Listen from Start / Add to Collection row must be on screen ({Named<FrameworkElement>("AudiobookSecondaryActions").Visibility}, {Named<FrameworkElement>("AudiobookCollectionButton").ActualWidth}px).");
        var facts = Named<Panel>("AudiobookFactsPanel").Children.OfType<StackPanel>()
            .Select(fact => string.Join("=", fact.Children.OfType<TextBlock>().Select(text => text.Text))).ToArray();
        if (!facts.SequenceEqual(["Length=4 hr 40 min", "Chapters=14", "Released=2021", "Publisher=Fixture Press"]))
            differences.Add($"{at}: facts were {string.Join(" | ", facts)}.");
        if (!Named<Panel>("AudiobookAboutPanel").Children.Contains(Named<UIElement>("OverviewText")) || !Named<Panel>("AudiobookAboutPanel").Children.Contains(Named<UIElement>("GenresBadgesPanel")))
            differences.Add($"{at}: overview and genres belong to the book body.");
        var chapters = Named<StackPanel>("AudiobookChaptersPanel").Children.OfType<Button>().ToArray();
        var labels = chapters.Select(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)).ToArray();
        if (chapters.Length != 8 || labels.First() != "Part Two" || labels.Last() != "Part Nine" || ((TextBlock)page.FindName("AudiobookChaptersToggleText")).Text != "Show all 14")
            differences.Add($"{at}: chapter window was {string.Join(", ", labels)} / '{((TextBlock)page.FindName("AudiobookChaptersToggleText")).Text}'.");
        var current = chapters.Where(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetItemStatus(button) == "current").Select(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button)).ToArray();
        if (!current.SequenceEqual(["Part Five"])) differences.Add($"{at}: current chapter was {string.Join(",", current)}.");
        var left = Descendants(chapters.FirstOrDefault(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetItemStatus(button) == "current") ?? (DependencyObject)page)
            .OfType<TextBlock>().Select(text => text.Text).FirstOrDefault(text => text.EndsWith("left in this chapter", StringComparison.Ordinal));
        if (left != "16m 40s left in this chapter") differences.Add($"{at}: left-in-chapter was '{left}'.");
        var starts = Descendants(chapters[0]).OfType<TextBlock>().Any(text => text.Text == "0:20:00");
        if (starts != width >= 640) differences.Add($"{at}: chapter start column must show only from sm.");

        var rails = Named<StackPanel>("BookRelatedGroupsPanel").Children.OfType<StackPanel>().ToArray();
        var headings = rails.Select(rail => ((TextBlock)rail.Children[0]).Text).ToArray();
        if (!headings.SequenceEqual(["In The Wayfarer Saga", "Also by Jane Writer", "You might also like"]))
            differences.Add($"{at}: rails were {string.Join(" | ", headings)}.");
        if (rails.Length > 0)
        {
            var cards = ((StackPanel)((ScrollViewer)rails[0].Children[1]).Content).Children.OfType<Button>().ToArray();
            var covers = cards.Select(card => Descendants(card).OfType<Border>().First(border => border.Child is Grid)).ToArray();
            var expectedCard = width >= 1024 ? 160 : width >= 640 ? 140 : 120;
            if (cards.Length != 3 || covers.Any(cover => Math.Abs(cover.Width - expectedCard) > .5 || Math.Abs(cover.Height - cover.Width) > .5))
                differences.Add($"{at}: series rail must show three {expectedCard}px square covers.");
            if (cards.Length == 3 && (covers[1].BorderThickness.Left != 2 || covers[0].BorderThickness.Left != 0))
                differences.Add($"{at}: the current book must be ringed in its series rail.");
            var texts = cards.Select(card => string.Join("|", Descendants(card).OfType<TextBlock>().Select(text => text.Text).Where(text => text.Length > 0 && text.All(ch => ch < (char)0xE000)))).ToArray();
            if (texts.Length == 3 && (texts[0] != "Book 1|The First Road|Finished" || texts[2] != "Book 3|The Far Shore|5 hr 50 min left"))
                differences.Add($"{at}: series cards were {string.Join(" / ", texts)}.");
        }
    }

    private static void AssertComicChapters(ItemDetailPage page, double width, List<string> differences)
    {
        var target = typeof(ItemDetailPage).GetField("_readerTargetContentId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page) as string;
        if (target != "fixture-comic-1") differences.Add($"Comic Start at {width}px must select loose Chapter 1, got {target}.");
        var panel = (StackPanel)page.FindName("MangaChaptersPanel");
        var sections = panel.Children.OfType<Expander>().ToArray();
        if (sections.Length != 1) differences.Add($"Comic volume aliases must share one section, got {sections.Length} at {width}px.");
        foreach (var section in sections)
        {
            if (panel.ActualWidth <= 0 || section.ActualWidth < panel.ActualWidth - 1)
                differences.Add($"Comic section at {width}px is {section.ActualWidth}px inside a {panel.ActualWidth}px chapter panel.");
            if (section.Content is Panel rows && rows.Children.OfType<FrameworkElement>().Any(row => row.ActualWidth < panel.ActualWidth * .7))
                differences.Add($"Comic chapter rows do not fill the section at {width}px.");
        }
    }

    private static string AudiobookJson()
    {
        var chapterNames = new[] { "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen" };
        return JsonSerializer.Serialize(new
        {
            content_id = "fixture-audiobook", type = "audiobook", title = "The Long Way Home", year = 2021,
            overview = "A cartographer who has mapped every road in the kingdom sets out to find the one that leads home. Along the way she meets travellers whose stories redraw the map she thought she knew. This synthetic overview is long enough to wrap across several lines in the book body.",
            genres = new[] { "Fantasy", "Adventure" },
            poster_url = "https://book-detail-fixture.invalid/cover-main", poster_thumbhash = "1QcSHQRnh493V4dIh4eXh1h4kJUI",
            user_data = new { position_seconds = 5000, duration_seconds = 16800, is_in_progress = true, played = false },
            versions = new[]
            {
                new
                {
                    file_id = 51, duration = 16800, container = "m4b",
                    chapters = chapterNames.Select((name, index) => new { index, title = "Part " + name, start_seconds = index * 1200, end_seconds = (index + 1) * 1200 }).ToArray(),
                },
            },
            audiobook = new
            {
                authors = new[] { new { name = "Jane Writer" } },
                narrators = new[] { new { name = "Sam Reader" } },
                publisher = "Fixture Press", total_duration_seconds = 16800,
                series = new
                {
                    name = "The Wayfarer Saga",
                    entries = new object[]
                    {
                        new { content_id = "fixture-series-1", title = "The First Road", series_index = 1, duration_seconds = 20000, played = true, poster_url = "https://book-detail-fixture.invalid/cover-1" },
                        new { content_id = "fixture-audiobook", title = "The Long Way Home", series_index = 2, duration_seconds = 16800, progress_ratio = .3, poster_url = "https://book-detail-fixture.invalid/cover-main" },
                        new { content_id = "fixture-series-3", title = "The Far Shore", series_index = 3, duration_seconds = 30000, progress_ratio = .3, poster_url = "https://book-detail-fixture.invalid/cover-3" },
                    },
                },
                other_narrations = new[] { new { content_id = "fixture-alt", title = "The Long Way Home", narrators = new[] { "Alex Voice" }, year = 2023 } },
                related = new
                {
                    also_by_author = new[] { new { content_id = "fixture-also", title = "Letters from the Coast", duration_seconds = 36000, poster_url = "https://book-detail-fixture.invalid/cover-also" } },
                    similar = new[] { new { content_id = "fixture-similar", title = "North of Everything", year = 2018 } },
                },
            },
        });
    }
    private static string Text(TextBlock block) => block.Inlines.Count > 0
        ? string.Concat(block.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().Select(run => run.Text))
        : block.Text;

    private static Windows.Foundation.Rect Bounds(FrameworkElement element, UIElement relativeTo)
    {
        var origin = element.TransformToVisual(relativeTo).TransformPoint(new Windows.Foundation.Point());
        return new(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var next in Descendants(child)) yield return next;
        }
    }

    private static async Task UntilAsync(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Book detail fixture did not settle."); }
    private static void Set(object target, string name, object? value) => typeof(ItemDetailPage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    private sealed class LocalServices(IServiceProvider original, SiloApiClient client, HttpClient http, ImageService images) : IServiceProvider
    {
        public object? GetService(Type type) =>
            type == typeof(ItemDetailViewModel) ? new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((id, ct) => new CatalogApi(client).GetItemDetailAsync(id, ct)))
            : type == typeof(SiloApiClient) ? client
            : type == typeof(SettingsApi) ? new SettingsApi(client)
            : type == typeof(CatalogApi) ? new CatalogApi(client)
            : type == typeof(HttpClient) ? http
            : type == typeof(ImageService) ? images
            : type == typeof(WatchTogetherRoomViewModel) ? new WatchTogetherRoomViewModel(new PlaybackApi(client), client)
            : original.GetService(type);
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal readonly List<string> Unexpected = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/cover-", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "audiobook-cover.png"), ct)) };
            if (path == "/api/v2/settings/values/effective")
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}", Encoding.UTF8, "application/json") };
            lock (Unexpected) Unexpected.Add(path);
            return new(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"not_found\",\"message\":\"fixture\"}", Encoding.UTF8, "application/json") };
        }
    }
}
