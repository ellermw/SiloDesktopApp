using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Views;

internal static class MediaTvPopulatedNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var visualCase = Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "tv-visual";
        var landscapeCase = Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "tv-landscape";
        var copyCase = Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "tv-copy";
        var differences = new List<string>();
        foreach (var single in new[] { false, true })
        foreach (var (width, height) in new[] { (1440d, 900d), (1024d, 600d), (900d, 700d), (600d, 450d), (460d, 720d) })
        {
            if (landscapeCase && (width != 1024 || height != 600)) continue;
            var owner = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"], RequestedTheme = ElementTheme.Dark };
            var window = new Window { Content = owner };
            var page = new ItemDetailPage { Width = width, Height = height };
            try
            {
                window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)width, (int)height));
                window.AppWindow.Show(false); await Task.Delay(100);
                var scale = owner.XamlRoot.RasterizationScale;
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
                owner.Children.Add(page); await Task.Delay(100);
                page.ViewModel.Item = new MediaItemDetail
                {
                    Type = "series", ContentId = "fixture-series", Title = "The Fixture Series", Year = 2024,
                    Overview = "A fixture overview for populated seasons and episodes. A fixture overview for populated seasons and episodes.",
                    Extras = Enumerable.Range(1, 3).Select(index => new ItemExtra
                    { ContentId = $"fixture-extra-{index}", Kind = "behind_the_scenes", Title = $"Behind the scenes {index}", DurationSeconds = 90 + index * 30 }).ToList()
                };
                page.ViewModel.IsSeries = true;
                page.ViewModel.SelectedSeasonNumber = 1;
                for (var number = 1; number <= (single ? 1 : 5); number++)
                    page.ViewModel.Seasons.Add(new Season
                    {
                        ContentId = $"fixture-season-{number}", Title = $"Season {number}", SeasonNumber = number,
                        EpisodeCount = 3, UserData = new SeasonUserData
                        { Played = number == 1, WatchedCount = number == 1 ? 3 : number == 2 ? 1 : 0, InProgressCount = number == 2 ? 1 : 0 }
                    });
                if (single)
                    for (var number = 1; number <= 3; number++)
                        page.ViewModel.Episodes.Add(new Episode
                        {
                            ContentId = $"fixture-episode-{number}", Title = $"Episode {number}: A distinct fixture passage", SeasonNumber = 1,
                            EpisodeNumber = number, Runtime = 42, Overview = "The fixture episode overview.",
                            UserData = new EpisodeUserData { Played = number == 1, PositionSeconds = number == 2 ? 600 : 0, DurationSeconds = 2520 }
                        });
                page.Measure(new Windows.Foundation.Size(width, height)); page.Arrange(new Windows.Foundation.Rect(0, 0, width, height)); page.UpdateLayout();
                Call(page, "UpdateUI"); Call(page, "BuildSeasonCards"); Call(page, "BuildEpisodeRows");
                Call(page, "UpdateSeriesCountsFromLoadedSeasons"); Call(page, "ApplyAuthoritativeSeriesAction");
                ((FrameworkElement)page.FindName("SeasonsSection")).Visibility = single ? Visibility.Collapsed : Visibility.Visible;
                ((FrameworkElement)page.FindName("SeasonsLoadingSkeleton")).Visibility = Visibility.Collapsed;
                ((FrameworkElement)page.FindName("SeasonsScrollViewer")).Visibility = Visibility.Visible;
                ((FrameworkElement)page.FindName("EpisodesPanel")).Visibility = Visibility.Visible;
                Call(page, "ArrangeCurrentWebUiContentOrder", "series"); Call(page, "UpdateResponsiveLayout", width);
                page.UpdateLayout(); await Task.Delay(100);
                var seasons = (StackPanel)page.FindName("SeasonsPanel");
                var episodes = (Grid)page.FindName("EpisodesPanel");
                if (single ? episodes.Children.Count != 3 : seasons.Children.Count != 5)
                    throw new InvalidOperationException("Populated native TV did not render its actual VM seasons/episodes.");
                var navigation = Field<ScrollViewer>(page, "_tvNavigation");
                var viewport = Field<Grid>(page, "_tvViewport");
                Program.Log($"TRACE: populated TV {width}x{height} single={single}: client={owner.XamlRoot.Size.Width}x{owner.XamlRoot.Size.Height}, viewport={viewport.ActualWidth}x{viewport.ActualHeight}, declared={viewport.Height}, navigationRow={Grid.GetRow(navigation)}, column={Grid.GetColumn(navigation)}, cards={seasons.Children.Count}, episodes={episodes.Children.Count}, episodeColumns={episodes.ColumnDefinitions.Count}.");
                if (Math.Abs(owner.XamlRoot.Size.Width - width) > 2 || Math.Abs(owner.XamlRoot.Size.Height - height) > 2)
                    throw new InvalidOperationException("Populated TV capture does not have the requested actual native client viewport.");
                await MediaParityNativeFixture.CaptureAsync(owner, $"media-tv-populated-{(single ? "episodes" : "seasons")}-{width:0}x{height:0}.png");
                if (copyCase)
                {
                    var copy = Field<Grid>(page, "_tvCopyHost");
                    var copyScroll = Field<ScrollViewer>(page, "_tvCopyScroll");
                    var title = (TextBlock)page.FindName("TitleText");
                    var at = title.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
                    Program.Log($"TRACE: bounded TV copy {width}x{height}/single={single}: copy={copy.ActualWidth}x{copy.ActualHeight}, padding={copy.Padding}; scroll={copyScroll.ActualWidth}x{copyScroll.ActualHeight}, padding={copyScroll.Padding}; title={title.ActualWidth}x{title.ActualHeight}@{at.X},{at.Y}, font={title.FontSize}, spacing={title.CharacterSpacing}, lines={title.MaxLines}.");
                    var expectedFont = width < 1024 ? Math.Clamp(width * .06, 20, 30) : height > 800 ? 72 : 48;
                    if (Math.Abs(title.FontSize - expectedFont) > .1 || Math.Abs(title.LineHeight - expectedFont * 1.15) > .1)
                        differences.Add($"{width}x{height}/single={single}: actual title must match original responsive{expectedFont}px/1.15 line geometry.");
                    var poster = (Border)page.FindName("HeroPosterContainer");
                    var posterColor = ((SolidColorBrush)poster.Background).Color;
                    var surfaceColor = ((SolidColorBrush)Application.Current.Resources["SurfaceBrush"]).Color;
                    Program.Log($"TRACE: actual TV poster background {posterColor}; shared surface={surfaceColor}.");
                    if (!posterColor.Equals(surfaceColor)) differences.Add($"{width}x{height}: TV poster must paint original Surface role.");
                    if (!copy.Padding.Equals(new Thickness(4)) || title.CharacterSpacing != -25 || title.MaxLines != (width < 1024 ? 3 : 2))
                        differences.Add($"{width}x{height}/single={single}: bounded TV copy requires original4px inset, -.025em title spacing and3/2 mobile/desktop line budgets.");
                    if (width >= 1024 && copyScroll.Padding.Right < 12 || width < 1024 && copyScroll.Padding.Right != 0)
                        differences.Add($"{width}x{height}/single={single}: desktop information requires its stable scroll gutter; mobile visible-overflow copy does not reserve one.");
                    if (width == 1024 && title.ActualHeight < 80)
                        differences.Add($"1024x600/single={single}: the matched title must wrap into the two-line pinned composition.");
                }
                if (landscapeCase)
                {
                    var at = navigation.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
                    var hero = (FrameworkElement)page.FindName("BackdropContainer");
                    var content = (Grid)page.FindName("HeroContentGrid");
                    var title = (TextBlock)page.FindName("TitleText");
                    Program.Log($"TRACE: short landscape single={single}: navigation={navigation.ActualWidth}x{navigation.ActualHeight}@{at.X},{at.Y}; hero={hero.ActualWidth}x{hero.ActualHeight}; inset={content.Margin}; titleFont={title.FontSize}.");
                    // Exact pinned DOM: navigation y344/h256; hero h392 from
                    // the more-specific available-height cap. Shared font
                    // metrics can vary, but the CSS size/insets are explicit.
                    if (Math.Abs(navigation.ActualHeight - 256) > 2 || Math.Abs(at.Y - 344) > 2 || navigation.Padding.Left != 12 || navigation.Padding.Right != 12)
                        differences.Add($"1024x600/single={single}: short desktop navigation must be 256px high, bottom-aligned and inset12px.");
                    if (hero.ActualHeight > 394 || content.Margin.Left != 16 || content.Margin.Right != 16 || content.Margin.Top != 52 || content.Margin.Bottom != 8 || title.FontSize != 48)
                        differences.Add($"1024x600/single={single}: short desktop hero must retain the pinned available-height cap, 52/16/8 insets and48px title.");
                }
                var extras = ((StackPanel)page.FindName("ExtrasGroupsPanel")).Children.OfType<StackPanel>().SelectMany(group => group.Children.OfType<Grid>()).Single();
                if (extras.Children.Count != 3 || extras.ColumnDefinitions.Count != (width < 640 ? 1 : width < 1024 ? 2 : 3))
                    throw new InvalidOperationException("Mounted populated extras do not follow their real responsive grid.");
                if (copyCase)
                {
                    var surface = ((SolidColorBrush)Application.Current.Resources["SurfaceBrush"]).Color;
                    var muted = ((SolidColorBrush)Application.Current.Resources["MutedBrush"]).Color;
                    foreach (var card in single ? Array.Empty<Button>() : seasons.Children.OfType<Button>())
                    {
                        var art = Descendants(card).OfType<Border>().First(border => border.Width > 0 && Math.Abs(border.Height - border.Width * 1.5) < .1);
                        Program.Log($"TRACE: actual season artwork role {width}x{height}: corner={art.CornerRadius.TopLeft}; fill={((SolidColorBrush)art.Background).Color}.");
                        if (art.CornerRadius.TopLeft != 16 || !((SolidColorBrush)art.Background).Color.Equals(surface))
                            differences.Add($"{width}x{height}: populated season poster requires the original16px/Surface artwork role.");
                    }
                    foreach (var card in extras.Children.OfType<Button>())
                    {
                        var disc = Descendants(card).OfType<Border>().Single(border => border.Width == 36 && border.Height == 36);
                        var play = Descendants(disc).OfType<Microsoft.UI.Xaml.Shapes.Path>().FirstOrDefault();
                        var playBox = disc.Child as FrameworkElement;
                        Program.Log($"TRACE: actual extra roles {width}x{height}: corner={card.CornerRadius.TopLeft}; fill={((SolidColorBrush)card.Background).Color}; disc={((SolidColorBrush)disc.Background).Color}; icon={disc.Child.GetType().Name}.");
                        if (card.CornerRadius.TopLeft != 12 || !((SolidColorBrush)card.Background).Color.Equals(surface) || !((SolidColorBrush)disc.Background).Color.Equals(muted)
                            || play?.Fill == null || playBox?.Width != 16 || playBox.Height != 16 || playBox.Margin.Left != 2)
                            differences.Add($"{width}x{height}: real extras require source12px/Surface cards and muted36px discs with filled16px play inset2px.");
                    }
                }
                var scroll = (ScrollViewer)page.FindName("ContentScroll");
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true); await Task.Delay(100);
                await MediaParityNativeFixture.CaptureAsync(owner, $"media-tv-populated-extras-{(single ? "episodes" : "seasons")}-{width:0}x{height:0}.png");
                if (width < 1024 && (!double.IsNaN(viewport.Height) || Grid.GetColumn(navigation) != 0 || Grid.GetRow(navigation) != 1 ||
                    navigation.VerticalScrollMode != ScrollMode.Disabled || Field<ScrollViewer>(page, "_tvCopyScroll").VerticalScrollMode != ScrollMode.Disabled ||
                    ((FrameworkElement)page.FindName("HeroPosterContainer")).Visibility != Visibility.Collapsed))
                    throw new InvalidOperationException("Actual populated mobile TV remains a fixed device-height/side-navigation viewport instead of the current natural hero and following navigation.");
                if (visualCase)
                {
                    if (((FrameworkElement)page.FindName("SplitPlayButton")).Visibility != Visibility.Visible ||
                        ((Button)page.FindName("PrimaryPlayButton")).IsEnabled || ((TextBlock)page.FindName("PlayButtonText")).Text != "Browse Series")
                        differences.Add($"{width}x{height}/single={single}: an authoritative null target must retain the disabled Browse Series action.");
                    var heroPoster = (FrameworkElement)page.FindName("HeroPosterContainer");
                    if (width >= 1024 && (heroPoster.Visibility != Visibility.Visible || Math.Abs(heroPoster.ActualHeight - 330) > 2 || Math.Abs(heroPoster.ActualWidth - 220) > 2 ||
                        !Descendants(heroPoster).OfType<TextBlock>().Any(text => text.Text == "The Fixture Series")))
                        differences.Add($"{width}x{height}/single={single}: desktop series without poster artwork must retain the actual title fallback.");
                    if (Math.Abs(((FrameworkElement)page.FindName("SplitPlayButton")).Opacity - .5) > .01)
                        differences.Add($"{width}x{height}/single={single}: the disabled null-target primary pill must retain its accent paint at the measured 50% opacity.");
                    if (width < 1024)
                    {
                        var actions = (FrameworkElement)page.FindName("HeroActionsRow");
                        var watched = (FrameworkElement)page.FindName("WatchedButton");
                        var more = (FrameworkElement)page.FindName("MoreButton");
                        var watchedAt = watched.TransformToVisual(actions).TransformPoint(new Windows.Foundation.Point());
                        var moreAt = more.TransformToVisual(actions).TransformPoint(new Windows.Foundation.Point());
                        if (Math.Abs(moreAt.X - watchedAt.X - watched.ActualWidth - 8) > 2 || Math.Abs(moreAt.Y - watchedAt.Y) > 2)
                            differences.Add($"{width}x{height}/single={single}: collapsed compact actions must not leave phantom gaps; More must follow Watched with the measured 8 px gap.");
                    }
                    if (width < 1024 && (((FrameworkElement)page.FindName("FavoriteButton")).Visibility != Visibility.Collapsed ||
                        ((FrameworkElement)page.FindName("StarRatingContainer")).Visibility != Visibility.Collapsed ||
                        ((TextBlock)page.FindName("WatchedText")).Text != "Mark Watched"))
                        differences.Add($"{width}x{height}/single={single}: current mobile TV must hide inline Favorite/Rating and use the short Mark Watched label.");
                    if (width < 1024)
                    {
                        Call(page, "BuildMoreFlyout");
                        var menu = (MenuFlyout)page.FindName("MoreFlyout");
                        if (!menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Add to favorites") ||
                            !menu.Items.OfType<MenuFlyoutSubItem>().Any(item => item.Text == "Rate" && item.Items.Count == 5))
                            differences.Add($"{width}x{height}/single={single}: hidden mobile Favorite/Rating must remain available in the native overflow menu.");
                    }
                    if (!single)
                    {
                        var cardWidth = (double)Call(page, "SeasonWidth", width)!;
                        foreach (var (card, index) in seasons.Children.OfType<Button>().Select((card, index) => (card, index)))
                        {
                            var poster = Descendants(card).OfType<Border>().FirstOrDefault(border =>
                                Math.Abs(border.ActualWidth - cardWidth) < 2 && Math.Abs(border.ActualHeight - cardWidth * 1.5) < 2);
                            if (poster?.Child is not TextBlock title || title.Text != $"Season {index + 1}")
                                differences.Add($"{width}x{height}: season {index + 1} without artwork requires its actual title inside the poster fallback.");
                        }
                    }
                    if (single && ((TextBlock)page.FindName("EpisodesHeader")).Text != "Episodes")
                        differences.Add($"{width}x{height}: actual single-season series heading must be Episodes.");
                    if (single && width >= 1024 && episodes.ColumnDefinitions.Count != (height <= 650 && width > height ? 2 : 5))
                        differences.Add($"{width}x{height}: desktop single-season navigation requires two columns in short landscape, otherwise five.");
                    if (single && width < 1024)
                    {
                        var cards = episodes.Children.OfType<FrameworkElement>().ToArray();
                        var positions = cards.Select(card => card.TransformToVisual(episodes).TransformPoint(new Windows.Foundation.Point())).ToArray();
                        var rail = page.FindName("EpisodesScroll") as ScrollViewer;
                        if (cards.Any(card => Math.Abs(card.ActualWidth - 160) > 2) ||
                            positions.Any(position => Math.Abs(position.Y - positions[0].Y) > 2) ||
                            positions.Skip(1).Where((position, index) => Math.Abs(position.X - positions[index].X - 172) > 2).Any() ||
                            rail == null || rail.HorizontalScrollMode != ScrollMode.Enabled || rail.VerticalScrollMode != ScrollMode.Disabled)
                            differences.Add($"{width}x{height}: mobile single-season navigation requires a horizontally scrollable row of 160 px episode cards with 12 px gaps.");
                    }
                }
            }
            finally { owner.Children.Remove(page); window.Close(); }
        }
        if (differences.Count > 0)
            throw new InvalidOperationException("Populated TV paired visual differences:\n" + string.Join("\n", differences));
        Program.Log("PASS: MEDIA_TV_POPULATED_COMPLETED real VM seasons, episodes, progress states, responsive navigation and mounted extras.");
    }
    private static object? Call(object page, string method, params object?[] args)
        => page.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);
    private static T Field<T>(object page, string field)
        => (T)page.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(owner); index++)
        {
            var child = VisualTreeHelper.GetChild(owner, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
