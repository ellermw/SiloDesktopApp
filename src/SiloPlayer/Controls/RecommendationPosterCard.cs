using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Converters;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

/// <summary>The title-only recommendation card, without catalog badges or menus.</summary>
public sealed class RecommendationPosterCard : Grid
{
    private readonly Grid _artwork;
    private readonly TextBlock _title;
    public RecommendationPosterCard(MediaItem item)
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        _artwork = new Grid { Background = (Brush)Application.Current.Resources["CardBackgroundBrush"] };
        var fallback = new DefaultArtwork { MediaType = item.Type, Thumbhash = item.PosterThumbhash };
        var zoom = new ScaleTransform();
        var image = new Image { Stretch = Stretch.UniformToFill, RenderTransform = zoom, RenderTransformOrigin = new(.5, .5) };
        if (!string.IsNullOrWhiteSpace(item.PosterUrl))
            image.Source = (ImageSource)new UrlToImageSourceConverter().Convert(item.PosterUrl, typeof(ImageSource), null!, "");
        fallback.Visibility = image.Source == null ? Visibility.Visible : Visibility.Collapsed;
        image.Loaded += (_, _) => { _ = fallback.ObserveConvertedImageAsync(image); };
        image.ImageOpened += (_, _) => fallback.Visibility = Visibility.Collapsed;
        image.ImageFailed += (_, _) => { image.Source = null; fallback.Visibility = Visibility.Visible; };
        _artwork.Children.Add(fallback); _artwork.Children.Add(image); Children.Add(_artwork);
        void ClipArtwork()
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_artwork);
            var geometry = visual.Compositor.CreateRoundedRectangleGeometry();
            geometry.Size = new((float)_artwork.ActualWidth, (float)_artwork.ActualHeight); geometry.CornerRadius = new(8);
            visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
        }
        _artwork.Loaded += (_, _) => ClipArtwork();
        _artwork.SizeChanged += (_, _) => { if (_artwork.IsLoaded) ClipArtwork(); };
        _title = new TextBlock { Text = item.Title, FontSize = 14, FontWeight = FontWeights.Medium,
            LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 6, 0, 0) };
        Grid.SetRow(_title, 1); Children.Add(_title);
        var open = new Button { Style = (Style)Application.Current.Resources["PosterHitTargetButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        AutomationProperties.SetName(open, item.Title);
        Grid.SetRowSpan(open, 2); Children.Add(open);
        open.Click += (_, _) => App.Services.GetRequiredService<NavigationService>()
            .Navigate<ItemDetailPage>(MediaNavigationContext.Detail(item.ContentId, MediaNavigationContext.LibraryId(this)));
        open.PointerEntered += (_, _) =>
        {
            zoom.ScaleX = zoom.ScaleY = 1.05;
            if (MediaNavigationContext.LibraryId(this) == null) App.Services.GetService<ItemDetailPrefetchCache>()?.Prefetch(item.ContentId);
        };
        PointerExited += (_, _) => zoom.ScaleX = zoom.ScaleY = 1;
        // v2 catalog cards omit play_content_id for a movie that plays itself.
        var playId = item.PlayContentId ?? (item.Type == "movie" ? item.ContentId : null);
        if (!string.IsNullOrWhiteSpace(playId))
        {
            var play = new Button { Content = WebUiIcon.Create("play", 15), Width = 36, Height = 36, MinWidth = 0, MinHeight = 0,
                Padding = new(0), CornerRadius = new(18), Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(204, 0, 0, 0)),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), BorderThickness = new(0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false };
            AutomationProperties.SetName(play, "Play " + item.Title); Children.Add(play);
            var hovered = false;
            void Reveal() { var visible = hovered || play.FocusState != FocusState.Unfocused; play.Opacity = visible ? 1 : 0; play.IsHitTestVisible = visible; }
            PointerEntered += (_, _) => { hovered = true; Reveal(); };
            PointerExited += (_, _) => { hovered = false; Reveal(); };
            play.GotFocus += (_, _) => Reveal(); play.LostFocus += (_, _) => Reveal();
            play.Click += async (_, _) =>
            {
                try { await App.Services.GetRequiredService<PlayerService>().PlayAsync(playId, libraryId: MediaNavigationContext.LibraryId(this)); }
                catch (Exception ex) { App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
            };
        }
    }
    public void SetLayout(double width, bool showCaption)
    {
        Width = width; _artwork.Width = width; _artwork.Height = width * 1.5;
        _title.Visibility = showCaption ? Visibility.Visible : Visibility.Collapsed;
    }
}
