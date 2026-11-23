using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Converters;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

/// <summary>
/// A collection's poster card in a row, as the WebUI's CollectionPosterCard draws it without the sidebar pin
/// (pinnable false): a 2:3 poster with the title count in its lower corner, the collection's name under it, and
/// "by Owner" under that for another profile's shared collection ("User collection" for the viewer's own). It
/// opens the collection's page like the cards on the Collections page.
/// </summary>
public sealed class CollectionPosterTile : Grid
{
    private readonly Border _poster;
    private readonly Image? _image;

    public CollectionPosterTile(ItemCollectionCard collection, string? ownerName = null)
    {
        Collection = collection;
        Brush Brush(string key) => (Brush)Application.Current.Resources[key];
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        var posterBorder = new SolidColorBrush(((SolidColorBrush)Brush("BorderBrush")).Color) { Opacity = .65 };
        _poster = new Border { CornerRadius = new CornerRadius(16), Background = Brush("SurfaceBrush"), BorderThickness = new(1), BorderBrush = posterBorder };
        var face = new Grid();
        if (!string.IsNullOrWhiteSpace(collection.PosterUrl))
        {
            _image = new Image { Stretch = Stretch.UniformToFill, FlowDirection = FlowDirection.LeftToRight,
                Source = (ImageSource)new UrlToImageSourceConverter().Convert(collection.PosterUrl, typeof(ImageSource), null!, ""),
                RenderTransformOrigin = new(.5, .5) };
            face.Children.Add(_image);
        }
        else
        {
            var placeholder = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new(12) };
            if (collection.IsPersonal)
            {
                var user = WebUiIcon.Create("user", 20, Brush("SecondaryTextBrush")); user.Opacity = .6; user.HorizontalAlignment = HorizontalAlignment.Center;
                placeholder.Children.Add(user);
            }
            placeholder.Children.Add(new TextBlock { Text = collection.Title, FontSize = 14, FontWeight = FontWeights.Medium, Foreground = Brush("SecondaryTextBrush"),
                MaxLines = 3, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            face.Children.Add(placeholder);
        }
        var count = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(8), IsHitTestVisible = false,
            Padding = new(8, 2, 8, 2), CornerRadius = new(6),
            Background = new SolidColorBrush(((SolidColorBrush)Brush("AppBackgroundBrush")).Color) { Opacity = .6 },
            Child = new TextBlock { Text = collection.ItemCount.ToString("N0", AppStrings.FormatCulture), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("PrimaryTextBrush") },
        };
        face.Children.Add(count);
        _poster.Child = face;
        void Clip()
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_poster);
            var geometry = visual.Compositor.CreateRoundedRectangleGeometry();
            geometry.Size = new((float)_poster.ActualWidth, (float)_poster.ActualHeight); geometry.CornerRadius = new(16);
            visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
        }
        _poster.Loaded += (_, _) => Clip();
        _poster.SizeChanged += (_, _) => { if (_poster.IsLoaded) Clip(); };
        Children.Add(_poster);

        var caption = new StackPanel { Margin = new(2, 10, 2, 0) };
        caption.Children.Add(new TextBlock { Text = collection.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string? subline = ownerName != null
            ? CollectionText.Get("web.collections.posterCard.byOwner", "by " + ownerName, ("owner", ownerName))
            : collection.IsPersonal ? CollectionText.Get("web.collections.posterCard.userCollection", "User collection") : null;
        if (subline != null)
            caption.Children.Add(new TextBlock { Text = subline, FontSize = 12, Foreground = Brush("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetRow(caption, 1); Children.Add(caption);

        var open = new Button { Style = (Style)Application.Current.Resources["PosterHitTargetButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(open, CollectionText.Open(collection.Title));
        Grid.SetRowSpan(open, 2); Children.Add(open);
        open.Click += (_, _) => Open(collection);
        if (_image != null)
        {
            open.PointerEntered += (_, _) => _image.RenderTransform = new ScaleTransform { ScaleX = 1.05, ScaleY = 1.05 };
            PointerExited += (_, _) => _image.RenderTransform = null;
        }
    }

    /// <summary>Opens the collection's page: a server collection in its library, a personal one as its owner's.</summary>
    public static void Open(ItemCollectionCard collection)
    {
        var me = App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>().SelectedProfileId;
        var shared = collection.IsPersonal && !string.IsNullOrEmpty(collection.CreatorProfileId) && collection.CreatorProfileId != me;
        App.Services.GetRequiredService<NavigationService>().Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
        {
            CollectionId = collection.Id, Title = collection.Title, IsUserCollection = collection.IsPersonal, LibraryId = collection.IsPersonal ? null : collection.Library,
            Subtitle = collection.IsPersonal ? (shared ? "Shared collection" : "Personal collection") : "Server collection",
        });
    }

    public ItemCollectionCard Collection { get; }

    public void SetLayout(double width)
    {
        Width = width; _poster.Width = width; _poster.Height = width * 1.5;
    }
}
