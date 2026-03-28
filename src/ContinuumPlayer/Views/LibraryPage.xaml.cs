using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Controls;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }
    private bool _suppressFilterEvents;
    private bool _recommendedLoaded;

    public LibraryPage()
    {
        ViewModel = App.Services.GetRequiredService<LibraryViewModel>();
        this.InitializeComponent();
        Helpers.SmoothScrollHelper.Attach(ContentScrollViewer);

        PosterRepeater.ItemsSource = ViewModel.Items;

        _suppressFilterEvents = true;
        SortComboBox.SelectedIndex = 0;
        OrderComboBox.SelectedIndex = 0;
        _suppressFilterEvents = false;

        ViewModel.Genres.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateGenreCombo());

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.TotalCount))
            {
                DispatcherQueue.TryEnqueue(() =>
                    CountText.Text = ViewModel.TotalCount > 0 ? $"{ViewModel.TotalCount:N0} items" : "");
            }
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is Library library)
        {
            LibraryTitle.Text = library.Name;
            ViewModel.Library = library;

            _suppressFilterEvents = true;
            SortComboBox.SelectedIndex = 0;
            OrderComboBox.SelectedIndex = 0;
            GenreComboBox.SelectedIndex = -1;
            ViewModel.SelectedSort = "title";
            ViewModel.SelectedOrder = "asc";
            ViewModel.SelectedGenre = null;
            _suppressFilterEvents = false;
            _recommendedLoaded = false;

            // Ensure Recommended panel is visible before loading data (default tab)
            RecommendedPanel.Visibility = Visibility.Visible;
            FilterBar.Visibility = Visibility.Collapsed;
            ContentScrollViewer.Visibility = Visibility.Collapsed;
            CollectionsPanel.Visibility = Visibility.Collapsed;

            // Load recommendations first since Recommended is the default tab
            if (!_recommendedLoaded)
                await LoadRecommendationsAsync();

            // Load library items in background for when user switches tabs
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Keeps loading pages until the content exceeds the viewport height.
    /// Runs after initial load and after each subsequent load.
    /// </summary>
    private async Task FillViewportAsync()
    {
        // Give layout a moment to settle
        await Task.Delay(200);

        while (ViewModel.HasMore && !ViewModel.IsLoading &&
               ContentScrollViewer.ScrollableHeight < 200 &&
               ContentScrollViewer.Visibility == Visibility.Visible)
        {
            await ViewModel.LoadMoreCommand.ExecuteAsync(null);
            await Task.Delay(100); // Let layout recalculate
        }
    }

    private void UpdateGenreCombo()
    {
        _suppressFilterEvents = true;
        GenreComboBox.Items.Clear();
        GenreComboBox.Items.Add(new ComboBoxItem { Content = "All Genres", Tag = "" });
        foreach (var genre in ViewModel.Genres)
        {
            if (!string.IsNullOrEmpty(genre))
                GenreComboBox.Items.Add(new ComboBoxItem { Content = genre, Tag = genre });
        }
        GenreComboBox.SelectedIndex = 0;
        _suppressFilterEvents = false;
    }

    private async void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (SortComboBox.SelectedItem is ComboBoxItem item && item.Tag is string sort)
        {
            ViewModel.SelectedSort = sort;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
        }
    }

    private async void OrderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (OrderComboBox.SelectedItem is ComboBoxItem item && item.Tag is string order)
        {
            ViewModel.SelectedOrder = order;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
        }
    }

    private async void GenreComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (GenreComboBox.SelectedItem is ComboBoxItem item && item.Tag is string genre)
        {
            ViewModel.SelectedGenre = string.IsNullOrEmpty(genre) ? null : genre;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
        }
    }

    private async void ContentScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        // Load more when user scrolls near the bottom
        var offset = ContentScrollViewer.VerticalOffset;
        var scrollable = ContentScrollViewer.ScrollableHeight;

        if (scrollable > 0 && offset >= scrollable - 1000 && ViewModel.HasMore && !ViewModel.IsLoading)
        {
            await ViewModel.LoadMoreCommand.ExecuteAsync(null);
        }
    }

    private async void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton || clickedButton.Tag is not string tag)
            return;

        var tabs = new[] { RecommendedTab, LibraryTab, CollectionsTab };
        foreach (var tab in tabs)
        {
            tab.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            tab.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
            tab.BorderThickness = new Thickness(0);
        }

        clickedButton.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        clickedButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
        clickedButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
        clickedButton.BorderThickness = new Thickness(0, 0, 0, 2);

        FilterBar.Visibility = tag == "Library" ? Visibility.Visible : Visibility.Collapsed;
        ContentScrollViewer.Visibility = tag == "Library" ? Visibility.Visible : Visibility.Collapsed;
        RecommendedPanel.Visibility = tag == "Recommended" ? Visibility.Visible : Visibility.Collapsed;
        CollectionsPanel.Visibility = tag == "Collections" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "Recommended" && !_recommendedLoaded)
            await LoadRecommendationsAsync();
    }

    private async Task LoadRecommendationsAsync()
    {
        _recommendedLoaded = true;
        RecommendedLoading.IsActive = true;
        RecommendedLoading.Visibility = Visibility.Visible;
        RecommendedError.Visibility = Visibility.Collapsed;

        // Clear any previous section rows (keep loading ring and error text)
        for (int i = RecommendedSectionsPanel.Children.Count - 1; i >= 0; i--)
        {
            if (RecommendedSectionsPanel.Children[i] is SectionRow)
                RecommendedSectionsPanel.Children.RemoveAt(i);
        }

        try
        {
            var catalogApi = App.Services.GetRequiredService<CatalogApi>();
            var libraryId = ViewModel.Library?.Id ?? 0;
            var response = await catalogApi.GetLibrarySectionsAsync(libraryId);

            RecommendedLoading.IsActive = false;
            RecommendedLoading.Visibility = Visibility.Collapsed;

            if (response.Sections.Count == 0)
            {
                RecommendedError.Text = "No recommendations available yet.";
                RecommendedError.Visibility = Visibility.Visible;
                return;
            }

            foreach (var section in response.Sections)
            {
                if (section.Items.Count == 0) continue;
                RecommendedSectionsPanel.Children.Add(new SectionRow { Section = section });
            }
        }
        catch (Exception ex)
        {
            RecommendedLoading.IsActive = false;
            RecommendedLoading.Visibility = Visibility.Collapsed;
            RecommendedError.Text = $"Failed to load recommendations: {ex.Message}";
            RecommendedError.Visibility = Visibility.Visible;
        }
    }
}
