using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Services;
using Windows.Foundation;

internal static class PosterCaptionNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, Window window)
    {
        if (Environment.GetEnvironmentVariable("SILO_POSTER_CAPTION_CASE") == "matrix")
        {
            await MatrixAsync(parent, window);
            return;
        }
        if (Environment.GetEnvironmentVariable("SILO_POSTER_CAPTION_CASE") == "library")
        {
            var ui = SiloPlayer.App.Services.GetRequiredService<UICustomizationService>();
            var libraryCard = new LibraryGridCard();
            libraryCard.SetLayout(185, 277.5, 277.5 + ui.CardCaptionHeight);
            libraryCard.Bind(Episode(), null);
            parent.Children.Add(libraryCard);
            try
            {
                await Task.Delay(100); libraryCard.UpdateLayout();
                await MediaParityNativeFixture.CaptureAsync(libraryCard, "library-episode-caption.png");
                var text = (TextBlock)typeof(LibraryGridCard).GetField("_subtitleText", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(libraryCard)!;
                var bottom = text.TransformToVisual(libraryCard).TransformPoint(new Point(0, text.ActualHeight)).Y;
                Program.Log($"Library episode-code bottom={bottom}, card={libraryCard.ActualHeight}.");
                if (bottom > libraryCard.ActualHeight + .5)
                    throw new InvalidOperationException("Library episode metadata exceeds its virtual row height.");
            }
            finally { parent.Children.Remove(libraryCard); await Task.Delay(40); }
            return;
        }
        var row = new SectionRow
        {
            Width = 1280,
            Section = new HomeSectionWithItems
            {
                Id = "episode-caption", Title = "Recently Added in TV Shows", SectionType = "recently_added", LoadCompleted = true,
                Items = [Episode(), new() { ContentId = "series", Title = "Fixture series", Type = "series", Year = 2026 }],
            },
        };
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 200 });
        content.Children.Add(row);
        content.Children.Add(new TextBlock { Text = "Next section", Height = 100 });
        var scroll = new ScrollViewer { Width = 1280, Height = 600, Content = content };
        parent.Children.Add(scroll);
        try
        {
            await Task.Delay(150); scroll.UpdateLayout();
            scroll.ChangeView(null, 160, null, true);
            await Task.Delay(120); scroll.UpdateLayout();
            var repeater = (ItemsRepeater)row.FindName("CardsRepeater");
            var card = (PosterCard)repeater.TryGetElement(0)!;
            if (card is null) throw new InvalidOperationException("Episode card was not realized.");
            await MediaParityNativeFixture.CaptureAsync(row, "episode-caption-row.png");
            AssertCaptionVisible(card, (ScrollViewer)row.FindName("CardsScrollViewer"));
            Program.Log("PASS: actual scrolled Home episode poster keeps its full title, episode name and code visible.");
        }
        finally { parent.Children.Remove(scroll); await Task.Delay(40); }
    }

    private static MediaItem Episode() => new()
    {
        ContentId = "episode", Title = "Original Parts", SeriesTitle = "Tracker (2024)", Type = "episode",
        SeasonNumber = 4, EpisodeNumber = 1, Year = 2026,
    };

    private static async Task MatrixAsync(StackPanel parent, Window window)
    {
        var ui = SiloPlayer.App.Services.GetRequiredService<UICustomizationService>();
        var presentation = ui.CardPresentation;
        var originalSize = presentation.PosterSize;
        var originalCaption = presentation.Caption;
        var cases = 0;
        try
        {
            foreach (var width in new[] { 500, 1080, 1920, 3440, 3840 })
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
                await Task.Delay(40);
                foreach (var size in new[] { "compact", "standard", "large" })
                foreach (var caption in new[] { "title_metadata", "title", "artwork" })
                {
                    presentation.PosterSize = size;
                    presentation.Caption = caption;
                    var row = new SectionRow
                    {
                        Width = width,
                        Section = new()
                        {
                            Id = $"matrix-{cases}", Title = size == "compact" ? "Recently Added in Anime" : "Recently Added in TV Shows",
                            SectionType = size == "large" ? "genre" : "recently_added", LoadCompleted = true,
                            Items = new(Enumerable.Range(0, 18).Select(index => index % 3 == 1
                                ? new MediaItem { ContentId = $"movie-{index}", Title = "A movie in the mixed row", Type = "movie", Year = 2026 }
                                : index % 3 == 2 ? new MediaItem { ContentId = $"series-{index}", Title = "A series", Type = "series", Year = 2026 }
                                : new MediaItem { ContentId = $"episode-{index}", Title = "An episode title that is deliberately much longer than its poster", SeriesTitle = "Fixture series", Type = "episode", SeasonNumber = 6, EpisodeNumber = index + 1 })),
                        },
                    };
                    var content = new StackPanel();
                    content.Children.Add(new Border { Height = 200 }); content.Children.Add(row);
                    content.Children.Add(new TextBlock { Text = "Next section", Height = 100 });
                    var scroll = new ScrollViewer { Width = width, Height = 600, Content = content };
                    parent.Children.Add(scroll);
                    try
                    {
                        await Task.Delay(60); scroll.UpdateLayout();
                        var rail = (ScrollViewer)row.FindName("CardsScrollViewer");
                        var repeater = (ItemsRepeater)row.FindName("CardsRepeater");
                        foreach (var atEnd in new[] { false, true, false })
                        {
                            scroll.ChangeView(null, atEnd ? 200 : 120, null, true);
                            rail.ChangeView(atEnd ? rail.ScrollableWidth : 0, null, null, true);
                            await Task.Delay(40); scroll.UpdateLayout();
                            var seen = 0;
                            for (var index = 0; index < 18; index++)
                                if (repeater.TryGetElement(index) is PosterCard card)
                                {
                                    seen++;
                                    AssertPresentation(card, rail, caption);
                                }
                            if (seen == 0) throw new InvalidOperationException("Scrolled caption matrix realized no cards.");
                        }
                        var first = (PosterCard)repeater.TryGetElement(0)!;
                        // Reuse the same control across two-line and three-line payloads.
                        first.MediaItem = new() { ContentId = "recycled-movie", Title = "Movie", Type = "movie", Year = 2026 };
                        first.MediaItem = Episode();
                        await Task.Delay(30); scroll.UpdateLayout(); AssertPresentation(first, rail, caption);
                        if (size == "standard" && (caption == "title_metadata" || width == 1080))
                            await MediaParityNativeFixture.CaptureAsync(row, $"caption-{width}-{caption}.png");
                        Program.Log($"PASS: scrolled mixed TV/Anime poster row width={width}, size={size}, caption={caption}, recycled episode and end/back paging.");
                        cases++;
                    }
                    finally { parent.Children.Remove(scroll); await Task.Delay(20); }
                }
            }
        }
        finally { presentation.PosterSize = originalSize; presentation.Caption = originalCaption; }
        Program.Log($"PASS: {cases} responsive poster caption configurations.");
    }

    private static void AssertPresentation(PosterCard card, ScrollViewer rail, string caption)
    {
        foreach (var name in new[] { "TitleText", "EpisodeTitleText", "SubtitleText" })
        {
            var text = (TextBlock)card.FindName(name);
            var expected = name == "TitleText" ? caption != "artwork"
                : caption == "title_metadata" && !string.IsNullOrWhiteSpace(text.Text);
            if ((text.Visibility == Visibility.Visible) != expected)
                throw new InvalidOperationException($"{name} visibility does not match {caption}.");
            if (!expected) continue;
            var bottom = text.TransformToVisual(card).TransformPoint(new Point(0, text.ActualHeight)).Y;
            var railBottom = text.TransformToVisual(rail).TransformPoint(new Point(0, text.ActualHeight)).Y;
            if (text.ActualHeight <= 0 || bottom > card.ActualHeight + .5 || railBottom > rail.ViewportHeight + .5)
                throw new InvalidOperationException($"{name} clipped at {bottom} in card {card.ActualHeight}, rail {railBottom}/{rail.ViewportHeight}.");
        }
    }

    private static void AssertCaptionVisible(PosterCard card, ScrollViewer rail)
    {
        foreach (var name in new[] { "TitleText", "EpisodeTitleText", "SubtitleText" })
        {
            var text = (TextBlock)card.FindName(name);
            if (text.Visibility != Visibility.Visible || string.IsNullOrWhiteSpace(text.Text))
                throw new InvalidOperationException($"Fixture caption {name} must be present.");
            var cardBottom = text.TransformToVisual(card).TransformPoint(new Point(0, text.ActualHeight)).Y;
            var railBottom = text.TransformToVisual(rail).TransformPoint(new Point(0, text.ActualHeight)).Y;
            Program.Log($"Caption {name}: text={text.Text}, height={text.ActualHeight}, bottom={cardBottom}, card={card.ActualHeight}, railBottom={railBottom}, viewport={rail.ViewportHeight}.");
            if (cardBottom > card.ActualHeight + .5 || railBottom > rail.ViewportHeight + .5)
                throw new InvalidOperationException($"{name} is clipped beneath the episode title: bottom {cardBottom:F2} exceeds card {card.ActualHeight:F2} or rail {rail.ViewportHeight:F2}.");
        }
    }
}
