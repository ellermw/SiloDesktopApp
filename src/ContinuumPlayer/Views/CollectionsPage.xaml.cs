using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using ContinuumPlayer.Core.Models.Collections;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class CollectionsPage : Page
{
    public CollectionsViewModel ViewModel { get; }

    public CollectionsPage()
    {
        ViewModel = App.Services.GetRequiredService<CollectionsViewModel>();
        this.InitializeComponent();

        ViewModel.Collections.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(BuildCollectionCards);

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.IsEmpty))
                DispatcherQueue.TryEnqueue(UpdateEmptyState);
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCollectionsCommand.ExecuteAsync(null);
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = ViewModel.IsEmpty && !ViewModel.IsLoading
            ? Visibility.Visible : Visibility.Collapsed;
        CreateButton.Visibility = ViewModel.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CreateCollection_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<CollectionEditorPage>();
    }

    private void BuildCollectionCards()
    {
        CollectionsGrid.Children.Clear();

        if (ViewModel.Collections.Count == 0)
        {
            UpdateEmptyState();
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        CreateButton.Visibility = Visibility.Visible;

        // Build a wrapped grid of collection cards
        var currentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        int cardsPerRow = 5;
        int count = 0;

        foreach (var collection in ViewModel.Collections)
        {
            currentRow.Children.Add(BuildCollectionCard(collection));
            count++;
            if (count % cardsPerRow == 0)
            {
                CollectionsGrid.Children.Add(currentRow);
                currentRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Margin = new Thickness(0, 16, 0, 0)
                };
            }
        }

        if (currentRow.Children.Count > 0)
            CollectionsGrid.Children.Add(currentRow);
    }

    private Border BuildCollectionCard(Collection collection)
    {
        // Poster/icon area
        var posterBorder = new Border
        {
            Width = 180,
            Height = 200,
            CornerRadius = new CornerRadius(12),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"]
        };

        var posterPlaceholder = new FontIcon
        {
            Glyph = "\uE8FD",
            FontSize = 32,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        posterBorder.Child = posterPlaceholder;

        // Type badge overlay
        string typeLabel = collection.CollectionType switch
        {
            "smart" => "SMART",
            _ => "MANUAL"
        };

        var typeBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["AccentBackgroundBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 8, 0, 0),
            Child = new TextBlock
            {
                Text = typeLabel,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["AccentBrush"]
            }
        };

        // Shared indicator
        var sharedIcon = new Border
        {
            Visibility = collection.IsShared ? Visibility.Visible : Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 0),
            Background = (Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = new FontIcon
            {
                Glyph = "\uE72D", // Share icon
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };

        var posterGrid = new Grid { Width = 180, Height = 200 };
        posterGrid.Children.Add(posterBorder);
        posterGrid.Children.Add(typeBadge);
        posterGrid.Children.Add(sharedIcon);

        // Title
        var titleText = new TextBlock
        {
            Text = collection.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(2, 8, 2, 0)
        };

        // Info row: item count
        var infoText = new TextBlock
        {
            Text = collection.CollectionType == "smart" ? "Smart collection" : "Manual collection",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TertiaryTextBrush"],
            Margin = new Thickness(2, 2, 2, 0)
        };

        var content = new StackPanel
        {
            Width = 180,
            Children = { posterGrid, titleText, infoText }
        };

        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4),
            Child = content,
            Tag = collection
        };

        // Hover effect
        card.PointerEntered += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Brush)Application.Current.Resources["SurfaceHoverBrush"];
        };
        card.PointerExited += (s, _) =>
        {
            if (s is Border b)
                b.Background = null;
        };

        // B40: card click opens the browse view (catalog grid of the
        // collection's items), NOT the editor. The editor is accessible via
        // right-click context menu (Edit) to match the webui dual-path.
        card.Tapped += (s, _) =>
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
            {
                CollectionId = collection.Id,
                Title = collection.Name,
                Subtitle = collection.IsShared ? "Shared collection" : "Personal collection",
                IsUserCollection = true,
            });
        };

        // Right-click context menu
        var menuFlyout = new MenuFlyout();

        var editItem = new MenuFlyoutItem
        {
            Text = "Edit",
            Icon = new FontIcon { Glyph = "\uE70F" }
        };
        editItem.Click += (_, _) =>
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<CollectionEditorPage>(collection.Id);
        };

        var deleteItem = new MenuFlyoutItem
        {
            Text = "Delete",
            Icon = new FontIcon { Glyph = "\uE74D" }
        };
        deleteItem.Click += async (_, _) =>
        {
            await ShowDeleteConfirmationAsync(collection);
        };

        menuFlyout.Items.Add(editItem);
        menuFlyout.Items.Add(deleteItem);
        card.ContextFlyout = menuFlyout;

        return card;
    }

    private async Task ShowDeleteConfirmationAsync(Collection collection)
    {
        var dialog = new ContentDialog
        {
            Title = "Delete Collection",
            Content = $"Delete \"{collection.Name}\"? This action cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteCollectionCommand.ExecuteAsync(collection.Id);
        }
    }
}
