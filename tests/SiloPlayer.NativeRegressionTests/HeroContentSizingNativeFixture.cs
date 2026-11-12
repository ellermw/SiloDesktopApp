using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Home;
using Windows.Foundation;

/// <summary>Measures the published hero in a real scroll viewport, including content beyond 760px.</summary>
internal static class HeroContentSizingNativeFixture
{
    internal static async Task RunAsync(Window window)
    {
        var previous = window.Content;
        var failures = new List<string>();
        if (window.AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(false, false);
        var hero = new HeroCarousel();
        var stack = new StackPanel();
        stack.Children.Add(hero);
        var following = new Button { Content = "Following section", Height = 60 };
        stack.Children.Add(following);
        var scroll = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Disabled };
        var root = new Grid(); root.Children.Add(scroll); window.Content = root;
        try
        {
            foreach (var (width, height, largeText) in new[]
            {
                (640, 360, false), (460, 550, false), (320, 300, false),
                (1280, 720, false), (1280, 1080, false), (656, 360, false), (1296, 720, false),
                (460, 550, true), (640, 360, true), (320, 300, true), (800, 360, true),
            })
            foreach (var tall in new[] { false, true })
            {
                var label = $"hero-content-{(tall ? "library" : "home")}-{width}x{height}-large{largeText}";
                try
                {
                    window.AppWindow.Resize(new(width, height));
                    hero.IsTall = tall;
                    await Settle(root);
                    hero.ItemsSource = Slides("Short title", details: false);
                    await Settle(root);
                    // Keep screenshot/measurement timing independent of the
                    // eight-second production timer, through its real control.
                    var pause = (Button)hero.FindName("PauseCarouselButton");
                    if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(pause) == "Pause slideshow")
                        Invoke(pause);
                    var minimum = Math.Clamp(root.ActualHeight * (tall
                        ? root.ActualWidth >= 1024 ? .72 : .60
                        : root.ActualWidth >= 1024 ? .66 : .54), tall ? 420 : 380, 760);
                    var shortHeight = hero.ActualHeight;
                    Require(Math.Abs(shortHeight - minimum) <= 2, $"Short content should use viewport minimum {minimum:F1}, actual {shortHeight:F1}.");

                    hero.ItemsSource = Slides(string.Join(" ", Enumerable.Repeat("Die außergewöhnlich lange Reise durch die geheimnisvollen Welten", 7)));
                    await Settle(root);
                    var title = (TextBlock)hero.FindName("HeroTitle");
                    var shadow = (TextBlock)hero.FindName("HeroTitleShadow");
                    var overview = (TextBlock)hero.FindName("HeroOverview");
                    var play = (Button)hero.FindName("PlayButton");
                    var more = (Button)hero.FindName("MoreInfoButton");
                    var playText = (TextBlock)hero.FindName("PlayButtonText");
                    var moreText = Descendants<TextBlock>(more).Single(t => t.Text == "More Info");
                    var titleSize = title.FontSize; var lineHeight = title.LineHeight;
                    if (largeText)
                    {
                        title.FontSize *= 1.5; shadow.FontSize *= 1.5;
                        title.LineHeight *= 1.5; shadow.LineHeight *= 1.5;
                        overview.FontSize *= 1.5; overview.LineHeight *= 1.5;
                        playText.Text = "Lecture de ce livre audio"; moreText.Text = "Informations supplémentaires";
                        playText.FontSize *= 1.5; moreText.FontSize *= 1.5;
                        playText.LineHeight *= 1.5; moreText.LineHeight *= 1.5;
                    }
                    await Settle(root);
                    var content = (FrameworkElement)hero.FindName("HeroContent");
                    var controls = (FrameworkElement)hero.FindName("SlideControlsPanel");
                    var contentBounds = Bounds(content, hero); var controlBounds = Bounds(controls, hero);
                    Program.Log($"MEASURE {label}: viewport={root.ActualWidth:F1}x{root.ActualHeight:F1} min={minimum:F1} short={shortHeight:F1} hero={hero.ActualHeight:F1} title={Bounds(title, hero)} content={contentBounds} controls={controlBounds} play={Bounds(play, hero)} more={Bounds(more, hero)}");
                    await MediaParityNativeFixture.CaptureAsync(hero, label + ".png");
                    Require(hero.ActualHeight > shortHeight + 20, "Wrapped content must grow the hero beyond its minimum.");
                    // Home reserves 64px; Library also clears its marquee header. On
                    // narrow windows the counter gets its own space above the title.
                    var topClearance = tall ? root.ActualWidth >= 1024 ? 128 : root.ActualWidth >= 640 ? 112 : 96 : 64;
                    Require(contentBounds.Top >= topClearance - 1, $"Content escaped top/header clearance {topClearance}: {contentBounds.Top:F1}.");
                    foreach (var element in new FrameworkElement[] { title, overview, play, more, controls,
                        (Button)hero.FindName("PrevButton"), (Button)hero.FindName("NextButton") })
                        Inside(element, hero);
                    Contained(playText, play); Contained(moreText, more);
                    Require(!Intersects(controlBounds, Bounds(play, hero)) && !Intersects(controlBounds, Bounds(more, hero))
                        && !Intersects(controlBounds, Bounds(title, hero)), "Counter/pause must not cover title or actions.");
                    Require(hero.ActualHeight - contentBounds.Bottom >= (root.ActualWidth >= 1024 ? 64 : root.ActualWidth >= 640 ? 48 : 40) - 1,
                        "Content lost the bottom gradient clearance.");
                    Require(Bounds(following, stack).Top >= hero.ActualHeight - 1, "Growing hero overlaps the following section.");
                    Require(hero.FindName("HeroEyebrow") is not TextBlock eyebrow || eyebrow.Visibility == Visibility.Collapsed,
                        "Obsolete Featured eyebrow remains visible.");
                    Require(!((BackdropImage)hero.FindName("BackdropImageA")).EnableKenBurns
                        && !((BackdropImage)hero.FindName("BackdropImageB")).EnableKenBurns, "Featured backdrop must remain static.");
                    var measured = hero.ActualHeight;
                    await Settle(root);
                    Require(Math.Abs(hero.ActualHeight - measured) < .1, "Hero layout does not settle.");

                    scroll.ChangeView(null, scroll.ScrollableHeight, null, true); await Settle(root);
                    Require(play.Focus(FocusState.Keyboard) && more.Focus(FocusState.Keyboard), "Actions are not keyboard reachable.");
                    Invoke((Button)hero.FindName("NextButton")); await Settle(root);
                    Require(((TextBlock)hero.FindName("SlideCounterCurrent")).Text == "02", "Next slide/counter is broken.");
                    Invoke((Button)hero.FindName("PrevButton")); await Settle(root);
                    Require(((TextBlock)hero.FindName("SlideCounterCurrent")).Text == "01", "Previous slide/counter is broken.");
                    Invoke((Button)hero.FindName("PauseCarouselButton"));
                    title.FontSize = shadow.FontSize = titleSize;
                    title.LineHeight = shadow.LineHeight = lineHeight;
                    overview.ClearValue(TextBlock.FontSizeProperty); overview.LineHeight = 28;
                    playText.FontSize = moreText.FontSize = 13.44;
                    playText.LineHeight = moreText.LineHeight = 20.16;
                    moreText.Text = "More Info";
                    hero.ItemsSource = Slides("Short again", details: false); await Settle(root);
                    Require(Math.Abs(hero.ActualHeight - minimum) <= 2, "Hero retained a stale content height after data replacement.");
                    scroll.ChangeView(null, 0, null, true);
                    Program.Log("PASS: " + label);
                }
                catch (Exception ex) { failures.Add(label + ": " + ex.Message); Program.Log("FAIL: " + label + ": " + ex); }
                finally
                {
                    // A failed matrix row must not leave synthetic typography on
                    // the retained control and invalidate subsequent rows.
                    var title = (TextBlock)hero.FindName("HeroTitle");
                    var shadow = (TextBlock)hero.FindName("HeroTitleShadow");
                    title.FontSize = shadow.FontSize = root.ActualWidth >= 1280 ? 72 : root.ActualWidth >= 1024 ? 60 : root.ActualWidth >= 640 ? 48 : 36;
                    title.LineHeight = shadow.LineHeight = root.ActualWidth < 640 ? 40 : title.FontSize;
                    var overview = (TextBlock)hero.FindName("HeroOverview");
                    overview.FontSize = root.ActualWidth >= 640 ? 16 : 14; overview.LineHeight = 28;
                    var playText = (TextBlock)hero.FindName("PlayButtonText");
                    playText.FontSize = 13.44; playText.LineHeight = 20.16;
                    foreach (var text in Descendants<TextBlock>((Button)hero.FindName("MoreInfoButton")))
                        if (text.Text is "More Info" or "Informations supplémentaires")
                        { text.Text = "More Info"; text.FontSize = 13.44; text.LineHeight = 20.16; }
                    scroll.ChangeView(null, 0, null, true);
                }
            }
            if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
            Program.Log("PASS: measured Home/Library hero growth, clearance, data/resize/text changes, controls and static backdrop.");
        }
        finally { window.Content = previous; }
    }

    private static List<MediaItem> Slides(string title, bool details = true) => [new() { ContentId = "hero-content-first", Type = "movie", Title = title,
        Overview = details ? "Une longue histoire traduite qui garde les actions accessibles. Une histoire qui continue avec des amis et des aventures." : null,
        Year = details ? 2026 : 0, Genres = details ? ["Science Fiction", "Adventure"] : [], Runtime = details ? 123 : 0 },
        new() { ContentId = "hero-content-second", Type = "movie", Title = "Second title" }];
    private static async Task Settle(FrameworkElement root) { root.UpdateLayout(); await Task.Delay(100); root.UpdateLayout(); }
    private static Rect Bounds(FrameworkElement element, UIElement relative) => element.TransformToVisual(relative)
        .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    private static bool Intersects(Rect a, Rect b) => a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
    private static void Inside(FrameworkElement element, HeroCarousel hero)
    {
        var bounds = Bounds(element, hero);
        Require(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1
            && bounds.Right <= hero.ActualWidth + 1 && bounds.Bottom <= hero.ActualHeight + 1,
            $"{element.Name} is clipped: {bounds}, hero={hero.ActualWidth}x{hero.ActualHeight}.");
    }
    private static void Contained(FrameworkElement element, FrameworkElement owner)
    {
        var bounds = Bounds(element, owner);
        Require(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= owner.ActualWidth + 1
            && bounds.Bottom <= owner.ActualHeight + 1, $"Action label escaped its button: {bounds}, owner={owner.ActualWidth}x{owner.ActualHeight}.");
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
