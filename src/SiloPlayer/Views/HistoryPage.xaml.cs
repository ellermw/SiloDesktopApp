using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }
    private readonly HashSet<string> _selectedIds = [];
    private bool _selectionMode;
    private bool _cardBuildQueued;

    public HistoryPage()
    {
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        ViewModel.Items.CollectionChanged += (_, _) =>
        {
            QueueCardBuild();
        };
    }

    private void QueueCardBuild()
    {
        if (_cardBuildQueued) return;
        _cardBuildQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _cardBuildQueued = false;
            BuildCards();
        });
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
        BuildCards();
    }

    private async void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tab) return;

        // Update tab styles
        InProgressTab.Style = tab == "in_progress"
            ? (Style)Resources["HistoryTabActiveStyle"]
            : (Style)Resources["HistoryTabStyle"];
        AllHistoryTab.Style = tab == "all"
            ? (Style)Resources["HistoryTabActiveStyle"]
            : (Style)Resources["HistoryTabStyle"];

        // Update subtitle
        SubtitleText.Text = tab == "in_progress"
            ? "Pick up where you left off."
            : "Everything you've watched.";

        await ViewModel.SwitchTabCommand.ExecuteAsync(tab);
        EndSelection();
        HistoryRemovalPanel.Visibility = tab == "all" ? Visibility.Visible : Visibility.Collapsed;
        BuildCards();
    }

    private async void HistoryScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (ViewModel.SelectedTab != "all") return;

        var offset = HistoryScrollViewer.VerticalOffset;
        var scrollable = HistoryScrollViewer.ScrollableHeight;

        if (scrollable > 0 && offset >= scrollable - 500 && ViewModel.HasMore && !ViewModel.IsLoading)
        {
            LoadMoreRing.IsActive = true;
            LoadMoreRing.Visibility = Visibility.Visible;

            await ViewModel.LoadMoreCommand.ExecuteAsync(null);
            BuildCards();

            LoadMoreRing.IsActive = false;
            LoadMoreRing.Visibility = Visibility.Collapsed;
        }
    }

    private void BuildCards()
    {
        HistoryCardsPanel.Children.Clear();

        int count = ViewModel.Items.Count;
        bool hasItems = count > 0 && !ViewModel.IsLoading;
        bool isInProgress = ViewModel.SelectedTab == "in_progress";

        EmptyState.Visibility = count == 0 && !ViewModel.IsLoading
            ? Visibility.Visible : Visibility.Collapsed;

        if (count == 0 && !ViewModel.IsLoading)
        {
            EmptyTitle.Text = isInProgress ? "Nothing in progress" : "No watch history";
            EmptySubtitle.Text = isInProgress
                ? "Start watching something and it will appear here so you can pick up where you left off."
                : "Your complete watch history will appear here after you finish watching something.";
        }

        CountPanel.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

        if (hasItems)
        {
            int displayCount = ViewModel.SelectedTab == "all" ? ViewModel.TotalCount : count;
            CountLabel.Text = isInProgress ? "IN PROGRESS" : "WATCHED";
            ItemCountText.Text = displayCount.ToString();
            ItemCountLabel.Text = displayCount == 1 ? "title" : "titles";
        }

        foreach (var item in ViewModel.Items)
        {
            HistoryCardsPanel.Children.Add(CreateHistoryCard(item));
        }
    }

    private Border CreateHistoryCard(HistoryDisplayItem item)
    {
        bool isHistoryTab = ViewModel.SelectedTab == "all";

        // Poster thumbnail (2:3 aspect, 80x120)
        var posterBorder = new Border
        {
            Width = 80,
            Height = 120,
            CornerRadius = new CornerRadius(6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"]
        };

        var posterPlaceholder = new FontIcon
        {
            Glyph = "\uE8B9",
            FontSize = 20,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        posterBorder.Child = posterPlaceholder;

        // Load poster image
        if (!string.IsNullOrEmpty(item.PosterUrl))
        {
            _ = LoadPosterAsync(posterBorder, item);
        }

        // Progress bar overlay at bottom of poster
        var posterGrid = new Grid { Width = 80, Height = 120 };
        posterGrid.Children.Add(posterBorder);

        if (item.ProgressPercent > 0 && !item.Completed)
        {
            var progressBar = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(0, 0, 6, 6),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Width = 80.0 * Math.Min(item.ProgressPercent / 100.0, 1.0)
            };
            posterGrid.Children.Add(progressBar);
        }

        // Completion badge overlay
        if (item.Completed)
        {
            var completedBadge = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 4, 0),
                Child = new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentForegroundBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            posterGrid.Children.Add(completedBadge);
        }

        // Play button overlay centered on poster
        var playOverlay = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(180, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            Child = new FontIcon
            {
                Glyph = "\uE768",
                FontSize = 14,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        posterGrid.Children.Add(playOverlay);

        // Title
        var titleText = new TextBlock
        {
            Text = item.Title,
            Style = (Style)Application.Current.Resources["SubtitleTextStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };

        // Year + type row
        var metaParts = new List<string>();
        if (item.Year > 0) metaParts.Add(item.Year.ToString());
        if (!string.IsNullOrEmpty(item.Type)) metaParts.Add(item.Type == "series" ? "Series" : "Movie");

        var yearText = new TextBlock
        {
            Text = string.Join(" \u00B7 ", metaParts),
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"]
        };

        // Status + timestamp row
        var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        // Status badge
        var statusBadge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Background = item.Completed
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBackgroundBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
            Child = new TextBlock
            {
                Text = item.StatusText,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = item.Completed
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"]
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };
        statusPanel.Children.Add(statusBadge);

        // Timestamp
        if (!string.IsNullOrEmpty(item.TimestampDisplay))
        {
            statusPanel.Children.Add(new TextBlock
            {
                Text = item.TimestampDisplay,
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"],
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // Progress text: "1:23:45 / 2:00:00"
        var progressText = new TextBlock
        {
            Text = item.ProgressText,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            Margin = new Thickness(0, 2, 0, 0)
        };

        // Wide progress bar below text (only for non-completed items)
        var textChildren = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { titleText, yearText, statusPanel, progressText }
        };

        if (!item.Completed && item.ProgressPercent > 0)
        {
            var wideProgressBar = new ProgressBar
            {
                Value = item.ProgressPercent,
                Maximum = 100,
                Height = 3,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
                Margin = new Thickness(0, 6, 0, 0)
            };
            textChildren.Children.Add(wideProgressBar);
        }

        // Resume/Play button on right
        var actionButton = new Button
        {
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(16, 8, 16, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.ContentId
        };

        var actionContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actionContent.Children.Add(new FontIcon
        {
            Glyph = "\uE768",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentForegroundBrush"]
        });
        actionContent.Children.Add(new TextBlock
        {
            Text = item.Completed ? "Watch Again" : "Resume",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentForegroundBrush"]
        });
        actionButton.Content = actionContent;
        actionButton.Click += ActionButton_Click;

        // Row grid: [poster 80] [text *] [button auto]
        var rowGrid = new Grid { ColumnSpacing = 16 };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(posterGrid, 0);
        Grid.SetColumn(textChildren, 1);
        Grid.SetColumn(actionButton, 2);

        rowGrid.Children.Add(posterGrid);
        rowGrid.Children.Add(textChildren);
        rowGrid.Children.Add(actionButton);

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(12),
            Child = rowGrid,
            Tag = item.ContentId
        };

        if (_selectionMode)
        {
            var selected = _selectedIds.Contains(item.ContentId);
            card.BorderThickness = new Thickness(selected ? 2 : 1);
            card.BorderBrush = (Brush)Application.Current.Resources[selected ? "AccentBrush" : "BorderBrush"];
            var selectionBadge = new CheckBox
            {
                IsChecked = selected,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 4, 0)
            };
            Grid.SetColumn(selectionBadge, 2);
            rowGrid.Children.Add(selectionBadge);
            actionButton.Visibility = Visibility.Collapsed;
        }

        card.PointerEntered += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceHoverBrush"];
            playOverlay.Opacity = 1;
        };

        card.PointerExited += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"];
            playOverlay.Opacity = 0;
        };

        card.Tapped += (s, _) =>
        {
            if (s is Border b && b.Tag is string contentId)
            {
                if (_selectionMode)
                    ToggleSelection(contentId);
                else
                    NavigateToDetail(contentId);
            }
        };

        return card;
    }

    private void BeginSelection_Click(object sender, RoutedEventArgs e)
    {
        _selectionMode = true;
        BeginSelectionButton.Visibility = Visibility.Collapsed;
        SelectionControls.Visibility = Visibility.Visible;
        UpdateSelectionState();
        BuildCards();
    }

    private void SelectLoaded_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in ViewModel.Items) _selectedIds.Add(item.ContentId);
        UpdateSelectionState();
        BuildCards();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        _selectedIds.Clear();
        UpdateSelectionState();
        BuildCards();
    }

    private void EndSelection_Click(object sender, RoutedEventArgs e) => EndSelection();

    private void EndSelection()
    {
        _selectionMode = false;
        _selectedIds.Clear();
        if (BeginSelectionButton is null) return;
        BeginSelectionButton.Visibility = Visibility.Visible;
        SelectionControls.Visibility = Visibility.Collapsed;
        UpdateSelectionState();
        BuildCards();
    }

    private void ToggleSelection(string contentId)
    {
        if (!_selectedIds.Add(contentId)) _selectedIds.Remove(contentId);
        UpdateSelectionState();
        BuildCards();
    }

    private void UpdateSelectionState()
    {
        if (SelectedCountText is null) return;
        SelectedCountText.Text = $"{_selectedIds.Count} selected";
        RemoveSelectedButton.IsEnabled = _selectedIds.Count > 0;
    }

    private async void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.Items.Where(item => _selectedIds.Contains(item.ContentId)).ToList();
        if (selected.Count == 0) return;

        var isShow = selected.Count == 1 && selected[0].Type is "series" or "season";
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = selected.Count > 1 ? "Remove selected watch data?" : isShow ? "Remove show watch data?" : "Remove watch data?",
            Content = selected.Count > 1
                ? $"{selected.Count} selected items will have their watch history, watched status, and resume progress cleared for this profile."
                : isShow
                    ? "This clears the show's watch history, watched episodes, and resume progress for this profile."
                    : "This clears the item's watch history, watched status, and resume progress for this profile.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        RemoveSelectedButton.IsEnabled = false;
        try
        {
            await ViewModel.RemoveHistoryAsync(selected);
            EndSelection();
        }
        catch (Exception ex)
        {
            var errorDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Could not remove watch data",
                Content = ex.Message,
                CloseButtonText = "Close"
            };
            await errorDialog.ShowAsync();
            UpdateSelectionState();
        }
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string contentId)
        {
            var playerService = App.Services.GetRequiredService<Services.PlayerService>();
            _ = playerService.PlayAsync(contentId);
        }
    }

    private void NavigateToDetail(string contentId)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<ItemDetailPage>(contentId);
    }

    private async Task LoadPosterAsync(Border posterBorder, HistoryDisplayItem item)
    {
        try
        {
            // Show thumbhash placeholder first
            if (!string.IsNullOrEmpty(item.PosterThumbhash))
            {
                try
                {
                    var decoded = ThumbhashDecoder.Decode(item.PosterThumbhash);
                    var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);

                    var bgra = new byte[decoded.Rgba.Length];
                    for (int i = 0; i < decoded.Rgba.Length; i += 4)
                    {
                        bgra[i] = decoded.Rgba[i + 2];
                        bgra[i + 1] = decoded.Rgba[i + 1];
                        bgra[i + 2] = decoded.Rgba[i];
                        bgra[i + 3] = decoded.Rgba[i + 3];
                    }

                    System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.CopyTo(bgra, bitmap.PixelBuffer);
                    bitmap.Invalidate();

                    posterBorder.Child = new Image
                    {
                        Source = bitmap,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
                    };
                }
                catch { }
            }

            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                item.ContentId, "poster", item.PosterUrl!, httpClient, CancellationToken.None);

            if (bytes == null) return;

            var bitmapImage = new BitmapImage
            {
                DecodePixelWidth = 100,
                DecodePixelType = DecodePixelType.Logical
            };
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            posterBorder.Child = new Image
            {
                Source = bitmapImage,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };
        }
        catch { }
    }
}
