using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Views;

public sealed partial class ItemDetailPage
{
    private long _extraCardsGeneration;
    private readonly List<Episode> _siblingEpisodes = [];

    private int BuildSiblingEpisodeCards()
    {
        SiblingEpisodesPanel.Children.Clear();
        var currentIndex = -1;
        for (var index = 0; index < _siblingEpisodes.Count; index++)
        {
            var episode = _siblingEpisodes[index];
            var current = episode.ContentId == ViewModel.Item?.ContentId;
            if (current)
            {
                currentIndex = index;
                if (ViewModel.Item?.UserData is { } data)
                    episode.UserData = new() { Played = data.Played, PositionSeconds = data.PositionSeconds,
                        DurationSeconds = data.DurationSeconds, IsInProgress = data.IsInProgress };
            }
            var card = CreateEpisodeCard(episode, current);
            card.Width = SiblingEpisodeCardWidth();
            SiblingEpisodesPanel.Children.Add(card);
        }
        return currentIndex;
    }

    private void UpdateSiblingEpisodeCardSizes()
    {
        foreach (var card in SiblingEpisodesPanel.Children.OfType<FrameworkElement>())
        {
            card.Width = SiblingEpisodeCardWidth();
            foreach (var caption in DescendantCaptions(card))
            {
                var compact = _tvViewport != null && ActualWidth < 1024;
                caption.MetadataRow.Visibility = compact || caption.Presentation.Metadata.Length == 0 &&
                    !caption.Presentation.ShowTranslation && !caption.Presentation.ShowUnreadable ? Visibility.Collapsed : Visibility.Visible;
                caption.OverviewText.Visibility = compact || caption.Presentation.Overview.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    private static IEnumerable<EpisodeCardCaption> DescendantCaptions(DependencyObject parent)
    {
        if (parent is EpisodeCardCaption caption) yield return caption;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (var nested in DescendantCaptions(VisualTreeHelper.GetChild(parent, index))) yield return nested;
    }

    private Button CreateExtraCard(ItemExtra extra)
    {
        var title = string.IsNullOrWhiteSpace(extra.Title) ? ExtraKindGroupLabel(extra.Kind) : extra.Title.Trim();
        var owner = ViewModel.Item;
        var navigationOwner = _navigationCts;
        var navigationToken = navigationOwner?.Token ?? CancellationToken.None;
        var generation = _extraCardsGeneration;
        var api = App.Services.GetRequiredService<SiloApiClient>();
        var context = api.CaptureContext();
        bool Current() => !navigationToken.IsCancellationRequested && ReferenceEquals(_navigationCts, navigationOwner) &&
            ReferenceEquals(ViewModel.Item, owner) && generation == _extraCardsGeneration && api.IsCurrentContext(context);

        var fallback = new DefaultArtwork { MediaType = "movie", Thumbhash = extra.StillThumbhash };
        var image = new Image { Name = "ExtraStillImage", Stretch = Stretch.UniformToFill };
        var placeholder = new FontIcon { Glyph = "\uE768", FontSize = 28, Opacity = .4,
            Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var hoverPlay = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x99, 0, 0, 0)), Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon { Glyph = "\uE768", FontSize = 16, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) } };
        var still = new Grid();
        still.Children.Add(fallback); still.Children.Add(image); still.Children.Add(placeholder); still.Children.Add(hoverPlay);
        var aspect = new EpisodeStillAspectPanel();
        aspect.Children.Add(new Border { CornerRadius = new CornerRadius(8), Child = still });
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(aspect);
        var text = new StackPanel { Margin = new Thickness(2, 0, 2, 0), Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"], TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        if (extra.DurationSeconds is > 0)
            text.Children.Add(new TextBlock { Text = FormatExtraDuration(extra.DurationSeconds.Value), FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
        content.Children.Add(text);
        var button = new Button { Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(0), Content = content, Tag = extra.ContentId };
        AutomationProperties.SetName(button, $"Play {title}" + (extra.DurationSeconds is > 0 ? $", {FormatExtraDuration(extra.DurationSeconds.Value)}" : ""));
        button.PointerEntered += (_, _) => hoverPlay.Opacity = 1;
        button.PointerExited += (_, _) => { if (button.FocusState == FocusState.Unfocused) hoverPlay.Opacity = 0; };
        button.GotFocus += (_, _) => hoverPlay.Opacity = 1;
        button.LostFocus += (_, _) => hoverPlay.Opacity = 0;
        button.Click += (_, _) => { if (Current()) _ = PlayExtraAsync(extra.ContentId); };

        CancellationTokenSource? imageLoad = null;
        long loadGeneration = 0;
        button.Loaded += (_, _) =>
        {
            imageLoad?.Cancel(); imageLoad?.Dispose();
            imageLoad = CancellationTokenSource.CreateLinkedTokenSource(navigationToken);
            var load = ++loadGeneration;
            bool CurrentLoad() => Current() && button.IsLoaded && load == loadGeneration;
            _ = fallback.ShowThumbhashAsync();
            _ = LoadExtraStillAsync(extra, image, fallback, placeholder, CurrentLoad, imageLoad.Token);
        };
        button.Unloaded += (_, _) => { ++loadGeneration; imageLoad?.Cancel(); imageLoad?.Dispose(); imageLoad = null; };
        return button;
    }

    private async Task LoadExtraStillAsync(ItemExtra extra, Image image, DefaultArtwork fallback,
        FrameworkElement placeholder, Func<bool> current, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(extra.StillUrl) || !current()) return;
        try
        {
            var api = App.Services.GetRequiredService<SiloApiClient>();
            var url = new Uri(new Uri(api.BaseUrl.TrimEnd('/') + "/"), extra.StillUrl);
            if (url.Scheme is not ("https" or "http")) return;
            // API v2 supplies an authorized short-lived URL; reuse the normal
            // image cache/client without copying account headers to image hosts.
            var bytes = await App.Services.GetRequiredService<ImageService>().GetImageAsync(extra.ContentId,
                "still", url.AbsoluteUri, App.Services.GetRequiredService<HttpClient>(), ct);
            if (!current() || ct.IsCancellationRequested || bytes is not { Length: > 0 }) return;
            var bitmap = new BitmapImage { DecodePixelWidth = 480, DecodePixelType = DecodePixelType.Logical };
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            if (!current() || ct.IsCancellationRequested) return;
            image.Source = bitmap; fallback.Visibility = Visibility.Collapsed; placeholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch { /* Keep the thumbhash/play placeholder on missing, expired or invalid artwork. */ }
    }
}
