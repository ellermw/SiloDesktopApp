using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Views;

/// <summary>
/// The ebook page of the same fixture: EbookContent.tsx (Silo-Server 3b2b32168,
/// #4645) on the audiobook layout.
/// </summary>
internal static partial class BookDetailParityNativeFixture
{
    /// <summary>
    /// An ebook in a series with progress, one with no series and nothing read,
    /// one with no files and no related titles, and the loading placeholder, each
    /// at 1440, 1024 and 800 (and 500 for the series book).
    /// </summary>
    private static async Task RunEbookAsync(Window window, Grid owner, ItemDetailPage page, double scale, List<string> differences)
    {
        async Task ResizeAsync(double width, double height)
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale)));
            page.Width = width; page.Height = height;
            await Task.Delay(150);
        }
        void Progress(MediaItemDetail? item, double fraction)
        {
            Set(page, "_ebookProgressOwner", item);
            Set(page, "_ebookProgressFileId", item == null ? null : 61);
            Set(page, "_ebookProgressFraction", fraction);
        }

        var series = JsonSerializer.Deserialize<MediaItemDetail>(EbookJson(), Json)!;
        foreach (var width in new[] { 1440d, 1024d, 800d, 500d })
        {
            await ResizeAsync(width, width >= 800 ? 1750 : 2300);
            Progress(series, .42);
            await ShowAsync(page, series, width);
            AssertEbookSeries(page, width, differences);
            await MediaParityNativeFixture.CaptureAsync(owner, $"book-detail-ebook-series-{width:0}.png");
        }
        // A retained page that resizes rebuilds the rails at the new card width.
        foreach (var width in new[] { 500d, 1440d })
        {
            await ResizeAsync(width, 1750);
            await Task.Delay(100);
            AssertEbookRailWidth(page, width < 640 ? 140 : width < 1024 ? 160 : 185, differences);
        }

        var bare = JsonSerializer.Deserialize<MediaItemDetail>(EbookBareJson(), Json)!;
        foreach (var width in new[] { 1440d, 1024d, 800d })
        {
            await ResizeAsync(width, 1500);
            Progress(null, 0);
            await ShowAsync(page, bare, width);
            AssertEbookBare(page, width, differences);
            await MediaParityNativeFixture.CaptureAsync(owner, $"book-detail-ebook-bare-{width:0}.png");
        }

        var empty = JsonSerializer.Deserialize<MediaItemDetail>(EbookNoRelatedJson(), Json)!;
        foreach (var width in new[] { 1440d, 1024d, 800d })
        {
            await ResizeAsync(width, 1100);
            Progress(null, 0);
            await ShowAsync(page, empty, width);
            AssertEbookNoRelated(page, width, differences);
            await MediaParityNativeFixture.CaptureAsync(owner, $"book-detail-ebook-no-related-{width:0}.png");
        }

        // The loading placeholder takes the book layout, so nothing moves when the book arrives.
        page.ViewModel.IsLoading = true;
        foreach (var width in new[] { 1440d, 1024d, 800d })
        {
            await ResizeAsync(width, 1100);
            Call(page, "ShowBookSkeletonKind", "ebook");
            Call(page, "UpdateResponsiveLayout", width);
            page.UpdateLayout(); await Task.Delay(250);
            var host = (FrameworkElement?)typeof(ItemDetailPage).GetField("_bookSkeletonHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page);
            if (host == null || host.Visibility != Visibility.Visible || Descendants(host).OfType<BookDetailSkeleton>().SingleOrDefault() is not { } skeleton)
                differences.Add($"ebook skeleton/{width}: the book placeholder must show while loading.");
            else
            {
                var expectedCover = width >= 1024 ? 260 : width >= 768 ? 216 : 176;
                var cover = Descendants(skeleton).OfType<SkeletonBox>().First();
                if (Math.Abs(cover.ActualWidth - expectedCover) > 1 || Math.Abs(cover.ActualHeight - expectedCover * 1.5) > 1)
                    differences.Add($"ebook skeleton/{width}: cover must be {expectedCover}x{expectedCover * 1.5} (was {cover.ActualWidth}x{cover.ActualHeight}).");
            }
            await MediaParityNativeFixture.CaptureAsync(owner, $"book-detail-ebook-skeleton-{width:0}.png");
        }
        Call(page, "HideBookSkeleton");
        page.ViewModel.IsLoading = false;
        Progress(null, 0);
    }

    private static void AssertEbookCommon(ItemDetailPage page, double width, string at, List<string> differences)
    {
        T Named<T>(string name) where T : class => (T)page.FindName(name);
        if (Named<FrameworkElement>("BackdropImage").Visibility == Visibility.Visible || Named<FrameworkElement>("AudiobookTint").Visibility != Visibility.Visible)
            differences.Add($"{at}: the ebook page must replace the backdrop with the cover tint.");
        if (Named<FrameworkElement>("HeroInfoPanel").Visibility == Visibility.Visible || Named<FrameworkElement>("DetailContentPanel").Visibility == Visibility.Visible)
            differences.Add($"{at}: DetailHero copy and the below-hero stack must not render on the ebook page.");
        var side = Named<StackPanel>("AudiobookSidePanel"); var head = Named<FrameworkElement>("AudiobookHeadPanel");
        var body = Named<FrameworkElement>("AudiobookBodyPanel"); var cover = Named<FrameworkElement>("AudiobookCover");
        var actions = Named<FrameworkElement>("AudiobookActionsPanel"); var grid = Named<Grid>("HeroContentGrid");
        var expectedCover = width >= 1024 ? 260 : width >= 768 ? 216 : width >= 640 ? 176 : 112;
        if (Math.Abs(cover.ActualWidth - expectedCover) > 1 || Math.Abs(cover.ActualHeight - expectedCover * 1.5) > 1)
            differences.Add($"{at}: 2:3 cover must be {expectedCover}x{expectedCover * 1.5} (was {cover.ActualWidth}x{cover.ActualHeight}).");
        var sideBounds = Bounds(side, grid); var headBounds = Bounds(head, grid); var bodyBounds = Bounds(body, grid); var actionBounds = Bounds(actions, grid);
        Program.Log($"TRACE: {at} side={sideBounds} head={headBounds} body={bodyBounds} actions={actionBounds} grid={grid.ActualWidth}x{grid.ActualHeight}.");
        if (width >= 768)
        {
            if (!side.Children.Contains(actions) || headBounds.X <= sideBounds.X + sideBounds.Width || bodyBounds.X <= sideBounds.X + sideBounds.Width || bodyBounds.Y <= headBounds.Y)
                differences.Add($"{at}: from md the cover and actions form a side column beside the head and body.");
        }
        else if (side.Children.Contains(actions) || actionBounds.Y < sideBounds.Y + sideBounds.Height - 1 || Math.Abs(bodyBounds.X - sideBounds.X) > 1
                 || headBounds.X <= sideBounds.X + sideBounds.Width)
            differences.Add($"{at}: below md the cover sits beside the head with actions and body across the page.");
        if (actions.ActualWidth <= 0 || actionBounds.X + actions.ActualWidth > grid.ActualWidth + 1)
            differences.Add($"{at}: the actions must fit the page ({actionBounds}).");
        if (Named<FrameworkElement>("EbookReadButton").Visibility == Visibility.Visible && Math.Abs(Named<FrameworkElement>("EbookReadButton").ActualHeight - 44) > .5)
            differences.Add($"{at}: Read must be 44px tall.");
        if (Named<FrameworkElement>("AudiobookListenButton").Visibility == Visibility.Visible || Named<FrameworkElement>("BookNarratorLine").Visibility == Visibility.Visible)
            differences.Add($"{at}: the audiobook's listen button and narrator line must not show on an ebook.");
        // No collection button or curator menu on the ebook page.
        if (Named<FrameworkElement>("AudiobookMoreButton").Visibility == Visibility.Visible || Named<FrameworkElement>("AudiobookCollectionButton").Visibility == Visibility.Visible)
            differences.Add($"{at}: EbookContent has no collection or more menu.");
        var secondary = Named<Grid>("EbookSecondaryActions");
        if (secondary.Visibility != Visibility.Visible || Named<TextBlock>("EbookWatchedText").Text != "Mark Read")
            differences.Add($"{at}: Mark read was '{Named<TextBlock>("EbookWatchedText").Text}' ({secondary.Visibility}).");
        // The overview shows four lines before More.
        if (Named<ExpandableDescription>("OverviewText").CollapsedLines != 4)
            differences.Add($"{at}: the overview must clamp to 4 lines.");
        // Facts, the ratings row, the overview, then genres, in that order.
        var about = Named<Panel>("AudiobookAboutPanel").Children.ToList();
        var order = new UIElement[] { Named<UIElement>("AudiobookFactsBorder"), Named<UIElement>("EbookBadgesRow"), Named<UIElement>("OverviewText"), Named<UIElement>("GenresBadgesPanel") }
            .Select(element => about.IndexOf(element)).ToArray();
        if (order.Any(index => index < 0) || !order.SequenceEqual(order.Order()))
            differences.Add($"{at}: the body must run facts, ratings, overview, genres ({string.Join(",", order)}).");
        // Body order: about, rails, Files.
        var bodyChildren = Named<Panel>("AudiobookBodyPanel").Children.ToList();
        if (bodyChildren.IndexOf(Named<UIElement>("AudiobookAboutPanel")) != 0 || bodyChildren.IndexOf(Named<UIElement>("BookRelatedSection")) != 1
            || bodyChildren.IndexOf(Named<UIElement>("MediaLocationsSection")) != 2 || bodyChildren.Contains(Named<UIElement>("AudiobookChaptersSection")))
            differences.Add($"{at}: the body must run about, rails, Files ({string.Join(",", bodyChildren.Select(child => (child as FrameworkElement)?.Name))}).");
    }

    private static string[] EbookFacts(ItemDetailPage page) => ((Panel)page.FindName("AudiobookFactsPanel")).Children.OfType<StackPanel>()
        .Select(fact => string.Join("=", fact.Children.OfType<TextBlock>().Select(text => text.Text))).ToArray();

    private static string CardText(DependencyObject card)
        => string.Join("|", Descendants(card).OfType<TextBlock>().Select(text => text.Text).Where(text => text.Length > 0 && text.All(ch => ch < (char)0xE000)));

    private static void AssertEbookSeries(ItemDetailPage page, double width, List<string> differences)
    {
        T Named<T>(string name) where T : class => (T)page.FindName(name);
        var at = $"ebook-series/{width}";
        AssertEbookCommon(page, width, at, differences);
        if (Named<TextBlock>("AudiobookSeriesText").Text != "Ebook Series, book 2") differences.Add($"{at}: series line was '{Named<TextBlock>("AudiobookSeriesText").Text}'.");
        if (Named<TextBlock>("AudiobookTitleText").Text != "The Salt Road") differences.Add($"{at}: title missing.");
        if (Text(Named<TextBlock>("BookAuthorLine")) != "By Jane Writer, Sam Editor") differences.Add($"{at}: author line was '{Text(Named<TextBlock>("BookAuthorLine"))}'.");
        if (Named<TextBlock>("EbookReadText").Text != "Continue" || Named<FrameworkElement>("EbookReadButton").Visibility != Visibility.Visible)
            differences.Add($"{at}: the primary action was '{Named<TextBlock>("EbookReadText").Text}'.");
        if (Named<FrameworkElement>("EbookProgressPanel").Visibility != Visibility.Visible || Named<TextBlock>("EbookProgressLabel").Text != "Reading progress"
            || Named<TextBlock>("EbookProgressPercent").Text != "42%")
            differences.Add($"{at}: progress was '{Named<TextBlock>("EbookProgressLabel").Text}' / '{Named<TextBlock>("EbookProgressPercent").Text}'.");
        var facts = EbookFacts(page);
        if (!facts.SequenceEqual(["Format=EPUB, PDF", "File Size=2.4 MB", "Released=2020", "Publisher=Fixture Press"]))
            differences.Add($"{at}: facts were {string.Join(" | ", facts)}.");
        var badges = Named<Panel>("EbookBadgesRow");
        if (badges.Visibility != Visibility.Visible || !badges.Children.Contains(Named<UIElement>("ContentRatingBadge")) || !badges.Children.Contains(Named<UIElement>("ScoresPanel"))
            || Named<FrameworkElement>("ContentRatingBadge").Visibility != Visibility.Visible || Named<TextBlock>("ContentRatingText").Text != "PG-13")
            differences.Add($"{at}: the content rating and ratings must show between the facts and the overview.");
        var genres = ((Panel)page.FindName("GenresBadgesPanel")).Children.OfType<Button>().Select(button => button.Content?.ToString()).ToArray();
        if (!genres.SequenceEqual(["MYSTERY", "HISTORICAL"])) differences.Add($"{at}: genre pills were {string.Join(",", genres)}.");

        var rails = Named<StackPanel>("BookRelatedGroupsPanel").Children.OfType<StackPanel>().ToArray();
        var headings = rails.Select(rail => ((TextBlock)rail.Children[0]).Text).ToArray();
        if (!headings.SequenceEqual(["In Ebook Series", "Also by Jane Writer", "You might also like"]))
            differences.Add($"{at}: rails were {string.Join(" | ", headings)}.");
        if (rails.Length == 3)
        {
            var expectedCard = width >= 1024 ? 185 : width >= 640 ? 160 : 140;
            var cards = ((StackPanel)((ScrollViewer)rails[0].Children[1]).Content).Children.OfType<Button>().ToArray();
            var covers = cards.Select(card => Descendants(card).OfType<Border>().First(border => border.Child is Grid)).ToArray();
            var texts = cards.Select(CardText).ToArray();
            if (cards.Length != 3 || covers.Any(cover => Math.Abs(cover.Width - expectedCard) > .5 || Math.Abs(cover.Height - cover.Width * 1.5) > .5))
                differences.Add($"{at}: series rail must show three {expectedCard}px 2:3 covers.");
            if (cards.Length == 3 && (covers[1].BorderThickness.Left != 2 || covers[0].BorderThickness.Left != 0))
                differences.Add($"{at}: the current book must be ringed in its series rail.");
            if (texts.Length == 3 && (texts[0] != "Book 1|The First Road|Read" || texts[1] != "Book 2|The Salt Road|42% read" || texts[2] != "Book 3|The Far Shore|2022"))
                differences.Add($"{at}: series cards were {string.Join(" / ", texts)}.");
            var author = ((StackPanel)((ScrollViewer)rails[1].Children[1]).Content).Children.OfType<Button>().Select(CardText).ToArray();
            if (!author.SequenceEqual(["Letters from the Coast|2019", "The Quiet Harbour|7% read"]))
                differences.Add($"{at}: also-by cards were {string.Join(" / ", author)}.");
        }
        if (Named<FrameworkElement>("MediaLocationsSection").Visibility != Visibility.Visible)
            differences.Add($"{at}: the Files list must follow the rails.");
    }

    private static void AssertEbookBare(ItemDetailPage page, double width, List<string> differences)
    {
        T Named<T>(string name) where T : class => (T)page.FindName(name);
        var at = $"ebook-bare/{width}";
        AssertEbookCommon(page, width, at, differences);
        if (Named<FrameworkElement>("AudiobookSeriesText").Visibility == Visibility.Visible) differences.Add($"{at}: a book with no series shows no series line.");
        if (Named<TextBlock>("EbookReadText").Text != "Read" || Named<FrameworkElement>("EbookProgressPanel").Visibility == Visibility.Visible)
            differences.Add($"{at}: an unread book shows Read and no progress bar.");
        if (Named<FrameworkElement>("EbookBadgesRow").Visibility == Visibility.Visible) differences.Add($"{at}: no rating or content rating means no badges row.");
        if (!EbookFacts(page).SequenceEqual(["Format=EPUB", "File Size=1.0 MB", "Released=2018"]))
            differences.Add($"{at}: facts were {string.Join(" | ", EbookFacts(page))}.");
        var rails = Named<StackPanel>("BookRelatedGroupsPanel").Children.OfType<StackPanel>().Select(rail => ((TextBlock)rail.Children[0]).Text).ToArray();
        if (!rails.SequenceEqual(["Also by Pat Plain", "You might also like"])) differences.Add($"{at}: rails were {string.Join(" | ", rails)}.");
    }

    private static void AssertEbookNoRelated(ItemDetailPage page, double width, List<string> differences)
    {
        T Named<T>(string name) where T : class => (T)page.FindName(name);
        var at = $"ebook-no-related/{width}";
        AssertEbookCommon(page, width, at, differences);
        if (Named<FrameworkElement>("BookRelatedSection").Visibility == Visibility.Visible) differences.Add($"{at}: no related titles means no rails.");
        if (Named<FrameworkElement>("EbookReadButton").Visibility == Visibility.Visible || Named<FrameworkElement>("EbookPrimaryDownloadButton").Visibility == Visibility.Visible
            || Named<FrameworkElement>("EbookDownloadButton").Visibility == Visibility.Visible)
            differences.Add($"{at}: with no file there is nothing to read or download.");
        if (!EbookFacts(page).SequenceEqual(["Released=2023"])) differences.Add($"{at}: facts were {string.Join(" | ", EbookFacts(page))}.");
        if (Named<FrameworkElement>("MediaLocationsSection").Visibility != Visibility.Visible) differences.Add($"{at}: the Files list still shows its empty message.");
    }

    private static void AssertEbookRailWidth(ItemDetailPage page, double expected, List<string> differences)
    {
        var rails = ((StackPanel)page.FindName("BookRelatedGroupsPanel")).Children.OfType<StackPanel>().ToArray();
        if (rails.Length == 0) { differences.Add("Resized ebook rail is missing."); return; }
        var cards = ((StackPanel)((ScrollViewer)rails[0].Children[1]).Content).Children.OfType<Button>().ToArray();
        if (cards.Length == 0 || cards.Any(card => Math.Abs(card.ActualWidth - expected) > .5))
            differences.Add($"Retained ebook rail at {page.Width}px must resize to {expected}px cards, got {string.Join(", ", cards.Select(card => card.ActualWidth))}.");
    }

    /// <summary>An ebook in a series, partway through, with every optional part of the page.</summary>
    private static string EbookJson() => JsonSerializer.Serialize(new
    {
        content_id = "fixture-ebook-2", type = "ebook", title = "The Salt Road", year = 2020,
        overview = "A cartographer's apprentice inherits a map that no one else can read, and follows it along the salt roads to the coast. Every town on the way has rewritten the same legend a little differently, and she has to decide which version of the story to believe before the tide closes the last causeway. This synthetic overview is long enough to wrap past four lines in the book body, so the More control shows.",
        genres = new[] { "Mystery", "Historical" }, content_rating = "PG-13",
        ratings = new[] { new { source = "imdb", name = "IMDb", score = 7.8, display = "7.8" } },
        poster_url = "https://book-detail-fixture.invalid/cover-ebook", poster_thumbhash = "1QcSHQRnh493V4dIh4eXh1h4kJUI",
        versions = new object[]
        {
            new { file_id = 61, container = "epub", file_name = "the-salt-road.epub", file_size = 2500000, duration = 312 },
            new { file_id = 62, container = "pdf", file_name = "the-salt-road.pdf", file_size = 9000000, duration = 318 },
        },
        ebook = new
        {
            authors = new[] { new { name = "Jane Writer" }, new { name = "Sam Editor" } },
            publisher = "Fixture Press",
            series = new
            {
                name = "Ebook Series",
                entries = new object[]
                {
                    new { content_id = "fixture-ebook-1", title = "The First Road", series_index = 1, played = true, poster_url = "https://book-detail-fixture.invalid/cover-1" },
                    new { content_id = "fixture-ebook-2", title = "The Salt Road", series_index = 2, progress_ratio = .42, poster_url = "https://book-detail-fixture.invalid/cover-main" },
                    new { content_id = "fixture-ebook-3", title = "The Far Shore", series_index = 3, year = 2022, poster_url = "https://book-detail-fixture.invalid/cover-3" },
                },
            },
            related = new
            {
                also_by_author = new object[]
                {
                    new { content_id = "fixture-also", title = "Letters from the Coast", year = 2019, poster_url = "https://book-detail-fixture.invalid/cover-also" },
                    new { content_id = "fixture-also-2", title = "The Quiet Harbour", year = 2015, progress_ratio = .07, poster_url = "https://book-detail-fixture.invalid/cover-also" },
                },
                similar = new[] { new { content_id = "fixture-similar", title = "North of Everything", year = 2018 } },
            },
        },
    });

    /// <summary>An ebook with no series, nothing read, no rating and no genres.</summary>
    private static string EbookBareJson() => JsonSerializer.Serialize(new
    {
        content_id = "fixture-ebook-bare", type = "ebook", title = "A Plain Little Book", year = 2018,
        overview = "A short synthetic description of a book that has no series and has not been opened yet.",
        poster_url = "https://book-detail-fixture.invalid/cover-ebook",
        versions = new[] { new { file_id = 71, container = "epub", file_name = "plain.epub", file_size = 1048576 } },
        ebook = new
        {
            authors = new[] { new { name = "Pat Plain" } },
            related = new
            {
                also_by_author = new[] { new { content_id = "fixture-also", title = "Another Plain Book", year = 2016, poster_url = "https://book-detail-fixture.invalid/cover-also" } },
                similar = new[] { new { content_id = "fixture-similar", title = "North of Everything", year = 2018, poster_url = "https://book-detail-fixture.invalid/cover-3" } },
            },
        },
    });

    /// <summary>An ebook with no files and no related titles: the emptiest page.</summary>
    private static string EbookNoRelatedJson() => JsonSerializer.Serialize(new
    {
        content_id = "fixture-ebook-empty", type = "ebook", title = "The Book Without Files", year = 2023,
        poster_url = "https://book-detail-fixture.invalid/cover-ebook",
        versions = Array.Empty<object>(),
        ebook = new { authors = new[] { new { name = "Quinn Quiet" } } },
    });
}
