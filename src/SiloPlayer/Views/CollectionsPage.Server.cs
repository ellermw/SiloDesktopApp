using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Converters;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class CollectionsPage
{
    /// <summary>The server's cards live in the one wall, so a change to them rebuilds the wall.</summary>
    private void BuildServerCollectionRows() => BuildWall();
    private static IEnumerable<Button> DescendantButtons(DependencyObject root)
    {
        if (root is Button button) yield return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in DescendantButtons(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private Border BuildServerCollectionCard(ServerCollectionsLibrary? library, ServerCollectionSummary collection)
    {
        var width = PosterWidth;
        var poster = new Border { Width = width, Height = width * 1.5, CornerRadius = new CornerRadius(16), Background = (Brush)Application.Current.Resources["SurfaceBrush"], BorderThickness = new(1), BorderBrush = CollectionPosterBorder() };
        if (!string.IsNullOrWhiteSpace(collection.PosterUrl))
            poster.Child = new Image { Stretch = Stretch.UniformToFill, Source = (ImageSource)new UrlToImageSourceConverter().Convert(collection.PosterUrl, typeof(ImageSource), width, "") };
        else poster.Child = new TextBlock { Text = collection.Title, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxLines = 3, Margin = new Thickness(12), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        poster.Loaded += (_, _) => ClipCollectionPoster(poster); poster.SizeChanged += (_, _) => ClipCollectionPoster(poster);
        var copy = new StackPanel(); copy.Children.Add(poster);
        copy.Children.Add(new TextBlock { Text = collection.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 10, 2, 0) });
        copy.Children.Add(new TextBlock { Text = $"{collection.ItemCount} title{(collection.ItemCount == 1 ? "" : "s")}", FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], Margin = new Thickness(2, 0, 2, 0) });
        var open = new Button { Content = copy, Style = (Style)Application.Current.Resources["PosterHitTargetButtonStyle"], Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(open, "Open " + collection.Title);
        open.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs { CollectionId = collection.Id, Title = collection.Title, Subtitle = library == null ? "Server collection" : library.LibraryName + " collection", IsUserCollection = false, LibraryId = library?.LibraryId });
        var layer = new Grid(); layer.Children.Add(open); var card = new Border { Child = layer, Tag = collection }; ConfigurePosterImageHover(card, poster);
        card.SizeChanged += (_, _) =>
        {
            if (card.ActualWidth <= 0 || Math.Abs(poster.Width - card.ActualWidth) <= .5) return;
            poster.Width = card.ActualWidth; poster.Height = card.ActualWidth * 1.5;
            if (poster.Child is Image { Source: BitmapImage bitmap } image && bitmap.DecodePixelWidth != (int)Math.Ceiling(card.ActualWidth))
                image.Source = (ImageSource)new UrlToImageSourceConverter().Convert(collection.PosterUrl!, typeof(ImageSource), card.ActualWidth, "");
        };
        if (library != null && App.MainWindowInstance is MainWindow window)
        {
            var pinned = window.IsSidebarPin(library.LibraryId, "collection", collection.Id);
            var pin = new Button { Width = 26, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new Thickness(6),
                CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8) };
            var over = false;
            void Visibility() { var visible = pinned || over || pin.FocusState != FocusState.Unfocused; pin.Opacity = visible ? 1 : 0; pin.IsHitTestVisible = visible; }
            void UpdatePin()
            {
                var source = (SolidColorBrush)Application.Current.Resources[pinned ? "AccentBrush" : "AppBackgroundBrush"];
                pin.Background = new SolidColorBrush(source.Color) { Opacity = pinned ? .9 : .5 };
                pin.Foreground = (Brush)Application.Current.Resources[pinned ? "AccentForegroundBrush" : "PrimaryTextBrush"];
                pin.Content = WebUiIcon.Navigation(pinned ? "pin-off" : "pin", 14, pin.Foreground);
                ToolTipService.SetToolTip(pin, pinned ? "Unpin from sidebar" : "Pin to sidebar");
                AutomationProperties.SetName(pin, $"{(pinned ? "Unpin" : "Pin")} {collection.Title} {(pinned ? "from" : "to")} sidebar"); Visibility();
            }
            UpdatePin();
            pin.Click += async (_, _) =>
            {
                var apiClient = App.Services.GetRequiredService<SiloApiClient>();
                var context = apiClient.CaptureContext();
                pin.IsEnabled = false;
                try
                {
                    pinned = await window.ToggleSidebarPinAsync(library.LibraryId, "collection", collection.Id, collection.Title);
                    UpdatePin();
                }
                catch (OperationCanceledException)
                {
                    // A retired action cannot change the retained card state.
                }
                catch (Exception ex)
                {
                    if (IsLoaded && pin.IsLoaded && apiClient.IsCurrentContext(context))
                        App.Services.GetRequiredService<ToastService>().Error(ex,
                            ex is InvalidDataException ? ex.Message : "Silo could not update sidebar pins.");
                }
                finally { pin.IsEnabled = true; }
            };
            layer.Children.Add(pin); card.PointerEntered += (_, _) => { over = true; Visibility(); }; card.PointerExited += (_, _) => { over = false; Visibility(); }; pin.GotFocus += (_, _) => Visibility(); pin.LostFocus += (_, _) => Visibility();
        }
        return card;
    }
}
