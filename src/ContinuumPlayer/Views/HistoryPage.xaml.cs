using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        this.InitializeComponent();

        ViewModel.Items.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(BuildCards);
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
        BuildCards();
    }

    private void BuildCards()
    {
        HistoryCardsPanel.Children.Clear();

        int count = ViewModel.Items.Count;
        bool hasItems = count > 0 && !ViewModel.IsLoading;

        EmptyState.Visibility = count == 0 && !ViewModel.IsLoading
            ? Visibility.Visible : Visibility.Collapsed;

        CountPanel.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

        if (hasItems)
        {
            ItemCountText.Text = count.ToString();
            ItemCountLabel.Text = count == 1 ? "title" : "titles";
        }

        foreach (var item in ViewModel.Items)
        {
            HistoryCardsPanel.Children.Add(CreateHistoryCard(item));
        }
    }

    private Border CreateHistoryCard(HistoryDisplayItem item)
    {
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

        // Year
        var yearText = new TextBlock
        {
            Text = item.Year > 0 ? item.Year.ToString() : "",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"]
        };

        // Progress text: "1:23:45 / 2:00:00"
        var progressText = new TextBlock
        {
            Text = item.ProgressText,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            Margin = new Thickness(0, 2, 0, 0)
        };

        // Wide progress bar below text
        var wideProgressBar = new ProgressBar
        {
            Value = item.ProgressPercent,
            Maximum = 100,
            Height = 3,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"],
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
            Margin = new Thickness(0, 6, 0, 0)
        };

        var textContent = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { titleText, yearText, progressText, wideProgressBar }
        };

        // Resume button on right
        var resumeButton = new Button
        {
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(16, 8, 16, 8),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = item.ContentId
        };

        var resumeContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        resumeContent.Children.Add(new FontIcon
        {
            Glyph = "\uE768",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentForegroundBrush"]
        });
        resumeContent.Children.Add(new TextBlock
        {
            Text = "Resume",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentForegroundBrush"]
        });
        resumeButton.Content = resumeContent;
        resumeButton.Click += ResumeButton_Click;

        // Row grid: [poster 80] [text *] [resume auto]
        var rowGrid = new Grid { ColumnSpacing = 16 };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(posterGrid, 0);
        Grid.SetColumn(textContent, 1);
        Grid.SetColumn(resumeButton, 2);

        rowGrid.Children.Add(posterGrid);
        rowGrid.Children.Add(textContent);
        rowGrid.Children.Add(resumeButton);

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(12),
            Child = rowGrid,
            Tag = item.ContentId
        };

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
                NavigateToDetail(contentId);
        };

        return card;
    }

    private void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string contentId)
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<PlayerPage>(contentId);
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
