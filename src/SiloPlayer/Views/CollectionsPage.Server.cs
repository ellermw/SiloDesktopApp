using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Converters;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class CollectionsPage
{
    private int? _selectedServerLibrary;
    private void BuildServerCollectionRows()
    {
        ServerCollectionsRows.Children.Clear();
        ServerCollectionsHeaderActions.Children.Clear();
        ServerCollectionsSection.Visibility = ViewModel.IsLoadingServerCollections || ViewModel.ServerLibraries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (ViewModel.ServerLibraries.Count == 0) return;
        var selected = ViewModel.ServerLibraries.Count == 1 ? ViewModel.ServerLibraries[0] : ViewModel.ServerLibraries.FirstOrDefault(library => library.LibraryId == _selectedServerLibrary);
        if (selected == null) _selectedServerLibrary = null;
        if (ViewModel.ServerLibraries.Count > 1)
        {
            var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            void Pill(int? id, string name)
            {
                var active = id == selected?.LibraryId;
                var button = new Button { Content = name, Height = 32, MinHeight = 0, CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 0, 12, 0), FontSize = 13,
                    Background = (Brush)Application.Current.Resources[active ? "PrimaryTextBrush" : "SurfaceBrush"], Foreground = (Brush)Application.Current.Resources[active ? "AppBackgroundBrush" : "SecondaryTextBrush"] };
                AutomationProperties.SetName(button, name); button.Click += (_, _) => { _selectedServerLibrary = id; BuildServerCollectionRows(); }; choices.Children.Add(button);
            }
            Pill(null, "All"); foreach (var library in ViewModel.ServerLibraries) Pill(library.LibraryId, library.LibraryName);
            ServerCollectionsRows.Children.Add(new ScrollViewer { Content = choices, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        }
        if (selected != null)
        {
            var seeAll = new Button { Content = $"See all {selected.TotalCount}", Style = (Style)Application.Current.Resources["GhostButtonStyle"], HorizontalAlignment = HorizontalAlignment.Right };
            AutomationProperties.SetName(seeAll, $"See all {selected.TotalCount} {selected.LibraryName} collections"); seeAll.Click += (_, _) => NavigateToLibraryCollections(selected); ServerCollectionsHeaderActions.Children.Add(seeAll);
        }
        var libraryCounts = ViewModel.ServerLibraries.SelectMany(library => library.Collections.Select(collection => collection.Id)).GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
        var seen = new HashSet<string>(StringComparer.Ordinal); var cards = new List<FrameworkElement>();
        foreach (var library in selected == null ? ViewModel.ServerLibraries.ToArray() : new[] { selected })
            foreach (var collection in library.Collections)
            {
                if (!seen.Add(collection.Id)) continue;
                if (cards.Count >= PosterColumnCount) break;
                cards.Add(BuildServerCollectionCard(selected == null && libraryCounts[collection.Id] > 1 ? null : library, collection));
            }
        ServerCollectionsRows.Children.Add(BuildPosterBoard(cards));
    }
    private Border BuildServerCollectionCard(ServerCollectionsLibrary? library, ServerCollectionSummary collection)
    {
        var width = PosterWidth;
        var poster = new Border { Width = width, Height = width * 1.5, CornerRadius = new CornerRadius(16), Background = (Brush)Application.Current.Resources["SurfaceBrush"], BorderThickness = new(1), BorderBrush = CollectionPosterBorder() };
        if (!string.IsNullOrWhiteSpace(collection.PosterUrl))
            poster.Child = new Image { Stretch = Stretch.UniformToFill, Source = (ImageSource)new UrlToImageSourceConverter().Convert(collection.PosterUrl, typeof(ImageSource), null!, "") };
        else poster.Child = new TextBlock { Text = collection.Title, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxLines = 3, Margin = new Thickness(12), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        poster.Loaded += (_, _) => ClipCollectionPoster(poster); poster.SizeChanged += (_, _) => ClipCollectionPoster(poster);
        var copy = new StackPanel(); copy.Children.Add(poster);
        copy.Children.Add(new TextBlock { Text = collection.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 10, 2, 0) });
        copy.Children.Add(new TextBlock { Text = $"{collection.ItemCount} title{(collection.ItemCount == 1 ? "" : "s")}", FontSize = 12, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], Margin = new Thickness(2, 0, 2, 0) });
        var open = new Button { Content = copy, Style = (Style)Application.Current.Resources["PosterHitTargetButtonStyle"], Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(open, "Open " + collection.Title);
        open.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs { CollectionId = collection.Id, Title = collection.Title, Subtitle = library == null ? "Server collection" : library.LibraryName + " collection", IsUserCollection = false, LibraryId = library?.LibraryId });
        var layer = new Grid(); layer.Children.Add(open); var card = new Border { Child = layer, Tag = collection }; ConfigurePosterImageHover(card, poster);
        card.SizeChanged += (_, _) => { if (card.ActualWidth > 0 && Math.Abs(poster.Width - card.ActualWidth) > .5) { poster.Width = card.ActualWidth; poster.Height = card.ActualWidth * 1.5; } };
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
            UpdatePin(); pin.Click += async (_, _) => { pin.IsEnabled = false; try { pinned = await window.ToggleSidebarPinAsync(library.LibraryId, "collection", collection.Id, collection.Title); UpdatePin(); } finally { pin.IsEnabled = true; } };
            layer.Children.Add(pin); card.PointerEntered += (_, _) => { over = true; Visibility(); }; card.PointerExited += (_, _) => { over = false; Visibility(); }; pin.GotFocus += (_, _) => Visibility(); pin.LostFocus += (_, _) => Visibility();
        }
        return card;
    }
}
