using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Controls;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class RecommendationsPage : Page
{
    public RecommendationsViewModel ViewModel { get; }
    private bool _eventsAttached;

    public RecommendationsPage()
    {
        ViewModel = App.Services.GetRequiredService<RecommendationsViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_eventsAttached)
        {
            _eventsAttached = true;
            ViewModel.Rows.CollectionChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(BuildForYouRows);
            };
            ViewModel.HeroItems.CollectionChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(BuildHeroSection);
            };
            ViewModel.PopularItems.CollectionChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(BuildPopularSection);
            };
            ViewModel.RecentlyAddedItems.CollectionChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(BuildRecentlyAddedSection);
            };
        }

        await ViewModel.LoadCommand.ExecuteAsync(null);
        BuildAllSections();
    }

    private void BuildAllSections()
    {
        BuildHeroSection();
        BuildPopularSection();
        BuildForYouRows();
        BuildRecentlyAddedSection();
        UpdateEmptyState();
    }

    private void BuildHeroSection()
    {
        HeroItemsPanel.Children.Clear();

        if (ViewModel.HeroItems.Count == 0)
        {
            HeroSection.Visibility = Visibility.Collapsed;
            return;
        }

        HeroSection.Visibility = Visibility.Visible;

        foreach (var item in ViewModel.HeroItems)
        {
            var card = CreateHeroCard(item);
            HeroItemsPanel.Children.Add(card);
        }
    }

    private Border CreateHeroCard(MediaItem item)
    {
        // Large backdrop card with title overlay
        var backdropBorder = new Border
        {
            Width = 320,
            Height = 180,
            CornerRadius = new CornerRadius(12),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"]
        };

        var placeholder = new FontIcon
        {
            Glyph = "\uE8B9",
            FontSize = 32,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        backdropBorder.Child = placeholder;

        // Load backdrop image
        if (!string.IsNullOrEmpty(item.BackdropUrl))
        {
            _ = LoadHeroBackdropAsync(backdropBorder, item);
        }

        // Gradient overlay
        var gradient = new Border
        {
            Width = 320,
            Height = 180,
            CornerRadius = new CornerRadius(12),
            Background = new Microsoft.UI.Xaml.Media.LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(0, 1),
                GradientStops =
                {
                    new Microsoft.UI.Xaml.Media.GradientStop { Color = Windows.UI.Color.FromArgb(0, 0, 0, 0), Offset = 0.4 },
                    new Microsoft.UI.Xaml.Media.GradientStop { Color = Windows.UI.Color.FromArgb(200, 0, 0, 0), Offset = 1.0 }
                }
            }
        };

        // Title and year text at bottom
        var titlePanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12, 0, 12, 10),
            Spacing = 2
        };

        titlePanel.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2
        });

        var metaParts = new List<string>();
        if (item.Year > 0) metaParts.Add(item.Year.ToString());
        if (item.Genres?.Count > 0) metaParts.Add(string.Join(", ", item.Genres.Take(2)));

        if (metaParts.Count > 0)
        {
            titlePanel.Children.Add(new TextBlock
            {
                Text = string.Join(" \u00B7 ", metaParts),
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(180, 255, 255, 255)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
        }

        var grid = new Grid { Width = 320, Height = 180 };
        grid.Children.Add(backdropBorder);
        grid.Children.Add(gradient);
        grid.Children.Add(titlePanel);

        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Child = grid,
            Tag = item.ContentId
        };

        card.PointerEntered += (s, _) =>
        {
            if (s is Border b) b.Opacity = 0.85;
        };
        card.PointerExited += (s, _) =>
        {
            if (s is Border b) b.Opacity = 1.0;
        };
        card.Tapped += (s, _) =>
        {
            if (s is Border b && b.Tag is string contentId)
            {
                var nav = App.Services.GetRequiredService<NavigationService>();
                nav.Navigate<ItemDetailPage>(contentId);
            }
        };

        return card;
    }

    private async Task LoadHeroBackdropAsync(Border border, MediaItem item)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                item.ContentId, "backdrop", item.BackdropUrl!, httpClient, CancellationToken.None);

            if (bytes == null) return;

            var bitmapImage = new BitmapImage
            {
                DecodePixelWidth = 400,
                DecodePixelType = DecodePixelType.Logical
            };
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            border.Child = new Image
            {
                Source = bitmapImage,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };
        }
        catch { }
    }

    private void BuildPopularSection()
    {
        PopularSectionPanel.Children.Clear();

        if (ViewModel.PopularItems.Count == 0)
        {
            PopularSectionPanel.Visibility = Visibility.Collapsed;
            return;
        }

        PopularSectionPanel.Visibility = Visibility.Visible;

        var section = new HomeSectionWithItems
        {
            Title = "Popular This Month",
            Items = new List<MediaItem>(ViewModel.PopularItems)
        };

        PopularSectionPanel.Children.Add(new SectionRow { Section = section });
    }

    private void BuildForYouRows()
    {
        RowsPanel.Children.Clear();

        if (ViewModel.Rows.Count == 0 && !ViewModel.IsLoading)
        {
            UpdateEmptyState();
            return;
        }

        // Count total items across all rows
        int totalItems = 0;
        foreach (var row in ViewModel.Rows)
            totalItems += row.Items.Count;

        // Add hero + popular + recently added to count
        totalItems += ViewModel.HeroItems.Count;
        totalItems += ViewModel.PopularItems.Count;
        totalItems += ViewModel.RecentlyAddedItems.Count;

        if (totalItems > 0)
        {
            CountPanel.Visibility = Visibility.Visible;
            RowCountText.Text = totalItems.ToString();
            RowCountLabel.Text = totalItems == 1 ? "suggestion" : "suggestions";
        }
        else
        {
            CountPanel.Visibility = Visibility.Collapsed;
        }

        foreach (var row in ViewModel.Rows)
        {
            if (row.Items.Count == 0) continue;

            var section = new HomeSectionWithItems
            {
                Title = row.Label,
                Items = new List<MediaItem>(row.Items)
            };

            var sectionRow = new SectionRow
            {
                Section = section
            };
            RowsPanel.Children.Add(sectionRow);
        }

        UpdateEmptyState();
    }

    private void BuildRecentlyAddedSection()
    {
        RecentlyAddedSectionPanel.Children.Clear();

        if (ViewModel.RecentlyAddedItems.Count == 0)
        {
            RecentlyAddedSectionPanel.Visibility = Visibility.Collapsed;
            return;
        }

        RecentlyAddedSectionPanel.Visibility = Visibility.Visible;

        var section = new HomeSectionWithItems
        {
            Title = "Recently Added",
            Items = new List<MediaItem>(ViewModel.RecentlyAddedItems)
        };

        RecentlyAddedSectionPanel.Children.Add(new SectionRow { Section = section });
    }

    private void UpdateEmptyState()
    {
        bool hasContent = ViewModel.Rows.Count > 0
            || ViewModel.HeroItems.Count > 0
            || ViewModel.PopularItems.Count > 0
            || ViewModel.RecentlyAddedItems.Count > 0;

        EmptyState.Visibility = !hasContent && !ViewModel.IsLoading
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
