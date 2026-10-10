using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Converters;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class CollectionsPage
{
    private int PosterColumnCount => ActualWidth >= 1024 ? 7 : ActualWidth >= 640 ? 5 : 3;
    private double PosterWidth => Math.Max(60, (Math.Min(ActualWidth > 0 ? ActualWidth : Width, CollectionsPageShell.MaxWidth) - CollectionsPageShell.Padding.Left - CollectionsPageShell.Padding.Right - 14 * (PosterColumnCount - 1)) / PosterColumnCount);
    private void BuildCollectionSkeletons()
    {
        Grid Skeleton()
        {
            var cards = new List<FrameworkElement>();
            for (var index = 0; index < 7; index++)
            {
                var picture = new Border { Height = PosterWidth * 1.5, CornerRadius = new(16), Background = (Brush)Application.Current.Resources["SurfaceBrush"] };
                var title = new Border { Height = 16, Width = PosterWidth * .75, CornerRadius = new(6), Background = (Brush)Application.Current.Resources["SurfaceBrush"], HorizontalAlignment = HorizontalAlignment.Left };
                picture.SizeChanged += (_, _) => { if (picture.ActualWidth > 0) { picture.Height = picture.ActualWidth * 1.5; title.Width = picture.ActualWidth * .75; } };
                cards.Add(new StackPanel { Spacing = 10, Children = { picture, title } });
            }
            return BuildPosterBoard(cards);
        }
        CollectionsLoadingShell.Children.Clear(); CollectionsLoadingShell.Children.Add(Skeleton());
        ServerCollectionsLoadingRows.Children.Clear(); ServerCollectionsLoadingRows.Children.Add(Skeleton());
    }
    private Grid BuildPosterBoard(IReadOnlyList<FrameworkElement> cards)
    {
        var board = new Grid { ColumnSpacing = 14, RowSpacing = 20 };
        for (var column = 0; column < PosterColumnCount; column++) board.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < Math.Max(1, (cards.Count + PosterColumnCount - 1) / PosterColumnCount); row++) board.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < cards.Count; index++) { Grid.SetColumn(cards[index], index % PosterColumnCount); Grid.SetRow(cards[index], index / PosterColumnCount); board.Children.Add(cards[index]); }
        return board;
    }
    private FrameworkElement NewCollectionCard()
    {
        var body = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        body.Children.Add(WebUiIcon.Create("plus", 20, (Brush)Application.Current.Resources["SecondaryTextBrush"])); body.Children.Add(new TextBlock { Text = "New collection", FontSize = 13, FontWeight = FontWeights.Medium, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], TextAlignment = TextAlignment.Center });
        var content = new Grid(); content.Children.Add(body);
        var outline = new Microsoft.UI.Xaml.Shapes.Rectangle { RadiusX = 16, RadiusY = 16, Stroke = (Brush)Application.Current.Resources["BorderBrush"], StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false }; content.Children.Add(outline);
        var card = new Button { Content = content, Height = PosterWidth * 1.5, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Padding = new(0), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        card.Click += CreateCollection_Click; return card;
    }
    private Border BuildCollectionCard(Collection collection)
    {
        Brush Brush(string key) => (Brush)Application.Current.Resources[key];
        var width = PosterWidth;
        var poster = new Border { Width = width, Height = width * 1.5, CornerRadius = new CornerRadius(16), Background = Brush("SurfaceBrush"), BorderThickness = new(1), BorderBrush = CollectionPosterBorder() };
        if (!string.IsNullOrWhiteSpace(collection.PosterUrl))
        {
            var image = new Image { Stretch = Stretch.UniformToFill, Source = (ImageSource)new UrlToImageSourceConverter().Convert(collection.PosterUrl, typeof(ImageSource), null!, "") };
            poster.Child = image;
        }
        else
        {
            var placeholder = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new(12) };
            var user = WebUiIcon.Create("user", 20, Brush("SecondaryTextBrush")); user.Opacity = .6; user.HorizontalAlignment = HorizontalAlignment.Center; placeholder.Children.Add(user);
            placeholder.Children.Add(new TextBlock { Text = collection.Name, FontSize = 14, FontWeight = FontWeights.Medium, Foreground = Brush("SecondaryTextBrush"), MaxLines = 3,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }); poster.Child = placeholder;
        }
        poster.Loaded += (_, _) => ClipCollectionPoster(poster); poster.SizeChanged += (_, _) => ClipCollectionPoster(poster);
        var posterLayer = new Grid(); posterLayer.Children.Add(poster);
        var stack = new StackPanel(); stack.Children.Add(posterLayer);
        stack.Children.Add(new TextBlock { Text = collection.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 10, 2, 0) });
        var type = collection.CollectionType switch { "manual" => "Manual", "smart" => "Smart", _ => "Synced list" };
        var status = collection.LastSyncStatus switch { "running" => " · Syncing now", "failed" => " · Sync failed", _ => "" };
        var metadata = new TextBlock { FontSize = 12, Foreground = Brush("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 2, 0) };
        metadata.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = $"{type} · {collection.ItemCount} title{(collection.ItemCount == 1 ? "" : "s")}" });
        if (status.Length > 0)
        {
            metadata.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = status, Foreground = collection.LastSyncStatus == "failed" ? Brush("ErrorBrush") : Brush("SecondaryTextBrush"), FontWeight = collection.LastSyncStatus == "failed" ? FontWeights.Medium : FontWeights.Normal });
            if (!string.IsNullOrWhiteSpace(collection.LastSyncMessage)) ToolTipService.SetToolTip(metadata, collection.LastSyncMessage);
        }
        stack.Children.Add(metadata);
        var layer = new Grid();
        var open = new Button { Content = stack, Padding = new Thickness(0), Style = (Style)Application.Current.Resources["PosterHitTargetButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(open, "Open " + collection.Name);
        open.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
        { CollectionId = collection.Id, Title = collection.Name, Subtitle = collection.IsShared ? "Shared collection" : "Personal collection", IsUserCollection = true });
        layer.Children.Add(open); var card = new Border { Child = layer, Tag = collection }; ConfigurePosterImageHover(card, poster);
        if (collection.IsShared && PersonalCollectionOwnership.IsOwn(collection, App.Services.GetRequiredService<AuthService>().SelectedProfileId))
        {
            var shared = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            shared.Children.Add(WebUiIcon.Create("users", 12, new SolidColorBrush(Windows.UI.Color.FromArgb(255, 125, 211, 252))));
            shared.Children.Add(new TextBlock { Text = "Shared", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 125, 211, 252)) });
            stack.Children.Add(new Border { Child = shared, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(2, 6, 2, 0), Padding = new(8, 2, 8, 2), CornerRadius = new(999), BorderThickness = new(1), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(102, 14, 165, 233)), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(38, 14, 165, 233)) });
        }
        card.SizeChanged += (_, _) => { if (card.ActualWidth > 0 && Math.Abs(poster.Width - card.ActualWidth) > .5) { poster.Width = card.ActualWidth; poster.Height = card.ActualWidth * 1.5; } };
        var profile = App.Services.GetRequiredService<AuthService>().SelectedProfileId;
        if (!PersonalCollectionOwnership.IsOwn(collection, profile)) { card.ContextFlyout = new MenuFlyout(); return card; }
        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = "Edit collection" }; edit.Click += (_, _) => NavigateToCollectionEditor(collection); menu.Items.Add(edit);
        if (ViewModel.Capabilities?.Imports == true && CanSyncCollection(collection))
        { var sync = new MenuFlyoutItem { Text = "Sync now" }; sync.Click += async (_, _) => await ViewModel.SyncCollectionCommand.ExecuteAsync(collection.Id); menu.Items.Add(sync); menu.Opened += (_, _) => { sync.IsEnabled = !ViewModel.IsCollectionMutationPending; sync.Text = ViewModel.SyncCollectionCommand.IsRunning ? "Syncing…" : "Sync now"; }; }
        var home = new MenuFlyoutItem { Text = "Add to my Home…" };
        AutomationProperties.SetHelpText(home, "A row on your Home"); ToolTipService.SetToolTip(home, "A row on your Home");
        home.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<CustomizeHomePage>(new CustomizeHomeNavigationArgs(collection.Id, collection.Name)); menu.Items.Add(home);
        if (ViewModel.Profiles.Any(profile => profile.Id != collection.CreatorProfileId))
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var share = new ToggleMenuFlyoutItem { Text = "Show to other profiles", IsChecked = collection.IsShared, IsEnabled = !ViewModel.IsCollectionMutationPending };
            AutomationProperties.SetHelpText(share, "Every profile on this account sees it"); ToolTipService.SetToolTip(share, "Every profile on this account sees it");
            menu.Opened += (_, _) => share.IsEnabled = !ViewModel.IsCollectionMutationPending;
            share.Click += async (_, _) =>
            {
                var desired = share.IsChecked; share.IsChecked = collection.IsShared;
                if (!desired)
                {
                    var names = ViewModel.Profiles.Where(profile => profile.Id != collection.CreatorProfileId).Select(profile => profile.Name).ToArray();
                    var who = names.Length == 0 ? "Other profiles" : names.Length == 1 ? names[0] : string.Join(", ", names[..^1]) + " and " + names[^1];
                    var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = $"Stop sharing {collection.Name}?", Content = $"{who} {(names.Length == 1 ? "loses" : "lose")} it, including rows they made from it.", PrimaryButtonText = "Stop sharing", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                }
                share.IsEnabled = false;
                try { if (await ViewModel.SetSharedAsync(collection, desired)) share.IsChecked = desired; }
                finally { share.IsEnabled = true; }
            };
            menu.Items.Add(share);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var delete = new MenuFlyoutItem { Text = "Delete…" }; delete.Click += async (_, _) => await ShowDeleteConfirmationAsync(collection); menu.Items.Add(delete);
        CollectionActionMenuPresentation.Configure(menu);
        foreach (var entry in menu.Items)
        {
            if (entry is ToggleMenuFlyoutItem sharing)
            {
                sharing.Icon = WebUiIcon.Navigation("users-round", 16, (Brush)Application.Current.Resources["SecondaryTextBrush"]);
                CollectionActionMenuPresentation.Configure(sharing, "Every profile on this account sees it");
            }
            else if (entry is MenuFlyoutItem action)
            {
                var mark = action.Text switch { "Edit collection" => "pencil", "Sync now" => "refresh-cw", "Add to my Home…" => "house", "Delete…" => "trash-2", _ => "plus" };
                action.Icon = WebUiIcon.Navigation(mark, 16, (Brush)Application.Current.Resources[action == delete ? "ErrorBrush" : "SecondaryTextBrush"]);
                CollectionActionMenuPresentation.Configure(action, action == home ? "A row on your Home" : null, action == delete);
            }
        }
        card.ContextFlyout = menu;
        // A FlyoutBase has one owner. ContextFlyout owns this menu; invoking it
        // from the visible trigger must not attach it to a second control.
        var more = new Button { Content = WebUiIcon.Create("ellipsis", 16, new SolidColorBrush(Microsoft.UI.Colors.White)), Width = ActualWidth < 1024 ? 36 : 32, Height = ActualWidth < 1024 ? 36 : 32,
            MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8) };
        AutomationProperties.SetName(more, "More for " + collection.Name); layer.Children.Add(more);
        more.Click += (_, _) => menu.ShowAt(more);
        more.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(Windows.UI.Color.FromArgb(179, 0, 0, 0));
        menu.Opened += (_, _) => more.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(204, 0, 0, 0));
        menu.Closed += (_, _) => more.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0));
        if (ViewModel.Capabilities?.ItemReorder == true)
        {
            ConfigureCollectionDrag(card, collection);
            var handle = new Button { Content = WebUiIcon.Create("grip-vertical", 16), Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(0), CornerRadius = new(10),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new(8), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0)) };
            AutomationProperties.SetName(handle, "Move " + collection.Name);
            var picked = false; string? target = null;
            handle.KeyDown += async (_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Space)
                {
                    args.Handled = true;
                    if (!picked) { picked = true; target = collection.Id; await ViewModel.BeginCollectionDragAsync(); handle.Content = "✓"; }
                    else { picked = false; if (target != null && target != collection.Id) await ViewModel.DropCollectionAsync(collection.Id, target, null); else ViewModel.CancelCollectionDrag(); handle.Content = WebUiIcon.Create("grip-vertical", 16); }
                }
                else if (picked && args.Key == Windows.System.VirtualKey.Escape) { args.Handled = true; picked = false; ViewModel.CancelCollectionDrag(); handle.Content = WebUiIcon.Create("grip-vertical", 16); }
                else if (picked && args.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down)
                {
                    args.Handled = true;
                    var own = ViewModel.Collections.Where(item => PersonalCollectionOwnership.IsOwn(item, profile)).ToArray();
                    if (own.Length == 0) { picked = false; ViewModel.CancelCollectionDrag(); return; }
                    var offset = args.Key switch { Windows.System.VirtualKey.Left => -1, Windows.System.VirtualKey.Right => 1, Windows.System.VirtualKey.Up => -PosterColumnCount, _ => PosterColumnCount };
                    var index = Array.FindIndex(own, item => item.Id == target);
                    target = own[Math.Clamp(index + offset, 0, own.Length - 1)].Id;
                    AutomationProperties.SetHelpText(handle, $"Move to position {Array.FindIndex(own, item => item.Id == target) + 1} of {own.Length}. Space drops; Escape cancels.");
                }
            };
            layer.Children.Add(handle);
            ConfigurePosterHover(card, handle);
        }
        return card;
    }
    private static void ConfigurePosterImageHover(FrameworkElement card, Border poster)
    {
        if (poster.Child is not Image image) return;
        image.RenderTransformOrigin = new(.5, .5);
        card.PointerEntered += (_, _) => image.RenderTransform = new ScaleTransform { ScaleX = 1.05, ScaleY = 1.05 };
        card.PointerExited += (_, _) => image.RenderTransform = null;
    }
    private static Brush CollectionPosterBorder() => new SolidColorBrush(((SolidColorBrush)Application.Current.Resources["BorderBrush"]).Color) { Opacity = .65 };
    private static void ConfigurePosterHover(FrameworkElement card, Control action)
    {
        var over = false;
        void Show(bool visible) { action.Opacity = visible ? 1 : 0; action.IsHitTestVisible = visible; }
        Show(false);
        card.PointerEntered += (_, _) => { over = true; Show(true); };
        card.PointerExited += (_, _) => { over = false; if (action.FocusState == FocusState.Unfocused) Show(false); };
        card.GotFocus += (_, _) => Show(true);
        card.LostFocus += (_, _) => { if (!over) Show(false); };
    }
    private static void ClipCollectionPoster(Border frame)
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(frame);
        var geometry = visual.Compositor.CreateRoundedRectangleGeometry(); geometry.Size = new((float)frame.ActualWidth, (float)frame.ActualHeight); geometry.CornerRadius = new(16);
        visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
    }
}
