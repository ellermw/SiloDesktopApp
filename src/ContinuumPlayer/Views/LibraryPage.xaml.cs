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
    private bool _orderAsc = true;
    private DispatcherTimer? _yearDebounceTimer;

    public LibraryPage()
    {
        ViewModel = App.Services.GetRequiredService<LibraryViewModel>();
        this.InitializeComponent();
        Helpers.SmoothScrollHelper.Attach(ContentScrollViewer);

        PosterRepeater.ItemsSource = ViewModel.Items;

        _suppressFilterEvents = true;
        SortComboBox.SelectedIndex = 0;
        _suppressFilterEvents = false;

        ViewModel.Genres.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateGenreCombo());

        ViewModel.ContentRatings.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateContentRatingCombo());

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.TotalCount))
            {
                DispatcherQueue.TryEnqueue(() => UpdateCountDisplay());
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
            GenreComboBox.SelectedIndex = -1;
            ContentRatingComboBox.SelectedIndex = -1;
            YearMinBox.Text = "";
            YearMaxBox.Text = "";
            _orderAsc = true;
            UpdateOrderButton();
            ViewModel.SelectedSort = "title";
            ViewModel.SelectedOrder = "asc";
            ViewModel.SelectedGenre = null;
            ViewModel.SelectedContentRating = null;
            ViewModel.SelectedYearMin = null;
            ViewModel.SelectedYearMax = null;
            _suppressFilterEvents = false;
            _recommendedLoaded = false;

            // Show Recommended panel by default
            ShowTab("Recommended");

            // Load recommendations first (default tab)
            if (!_recommendedLoaded)
                await LoadRecommendationsAsync();

            // Load library items in background for when user switches tabs
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private void ShowTab(string tag)
    {
        // Update tab button styles
        var tabs = new[] { RecommendedTab, LibraryTab, CollectionsTab };
        foreach (var tab in tabs)
        {
            tab.Style = (Style)Resources["PillTabButtonStyle"];
        }

        Button activeTab = tag switch
        {
            "Library" => LibraryTab,
            "Collections" => CollectionsTab,
            _ => RecommendedTab
        };
        activeTab.Style = (Style)Resources["PillTabButtonActiveStyle"];

        // Toggle panel visibility
        FilterBar.Visibility = tag == "Library" ? Visibility.Visible : Visibility.Collapsed;
        ContentScrollViewer.Visibility = tag == "Library" ? Visibility.Visible : Visibility.Collapsed;
        RecommendedPanel.Visibility = tag == "Recommended" ? Visibility.Visible : Visibility.Collapsed;
        CollectionsPanel.Visibility = tag == "Collections" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Keeps loading pages until the content exceeds the viewport height.
    /// </summary>
    private async Task FillViewportAsync()
    {
        await Task.Delay(200);

        while (ViewModel.HasMore && !ViewModel.IsLoading &&
               ContentScrollViewer.ScrollableHeight < 200 &&
               ContentScrollViewer.Visibility == Visibility.Visible)
        {
            await ViewModel.LoadMoreCommand.ExecuteAsync(null);
            await Task.Delay(100);
        }
    }

    private void UpdateCountDisplay()
    {
        if (ViewModel.TotalCount > 0)
        {
            CountText.Text = ViewModel.TotalCount.ToString("N0");
            CountLabel.Text = ViewModel.TotalCount == 1 ? "item" : "items";
        }
        else
        {
            CountText.Text = "0";
            CountLabel.Text = "items";
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

    private void UpdateContentRatingCombo()
    {
        _suppressFilterEvents = true;
        ContentRatingComboBox.Items.Clear();
        ContentRatingComboBox.Items.Add(new ComboBoxItem { Content = "All Ratings", Tag = "" });
        foreach (var rating in ViewModel.ContentRatings)
        {
            if (!string.IsNullOrEmpty(rating))
                ContentRatingComboBox.Items.Add(new ComboBoxItem { Content = rating, Tag = rating });
        }
        ContentRatingComboBox.SelectedIndex = 0;
        _suppressFilterEvents = false;
    }

    private void UpdateOrderButton()
    {
        OrderIcon.Glyph = _orderAsc ? "\uE74A" : "\uE74B"; // Up arrow : Down arrow
        ToolTipService.SetToolTip(OrderToggleButton, _orderAsc ? "Ascending" : "Descending");
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

    private async void OrderToggle_Click(object sender, RoutedEventArgs e)
    {
        _orderAsc = !_orderAsc;
        UpdateOrderButton();
        ViewModel.SelectedOrder = _orderAsc ? "asc" : "desc";
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
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

    private async void ContentRatingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (ContentRatingComboBox.SelectedItem is ComboBoxItem item && item.Tag is string rating)
        {
            ViewModel.SelectedContentRating = string.IsNullOrEmpty(rating) ? null : rating;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
        }
    }

    private void YearFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;

        // Debounce year filter changes
        _yearDebounceTimer?.Stop();
        _yearDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _yearDebounceTimer.Tick += async (s, args) =>
        {
            _yearDebounceTimer?.Stop();
            _yearDebounceTimer = null;

            ViewModel.SelectedYearMin = string.IsNullOrWhiteSpace(YearMinBox.Text) ? null : YearMinBox.Text;
            ViewModel.SelectedYearMax = string.IsNullOrWhiteSpace(YearMaxBox.Text) ? null : YearMaxBox.Text;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
        };
        _yearDebounceTimer.Start();
    }

    private async void ContentScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
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

        ShowTab(tag);

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
