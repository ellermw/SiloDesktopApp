using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }
    private bool _filterInitializing = true;
    private bool _initialized;
    private double _catalogCardWidth = 178;

    public SearchPage()
    {
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
        this.InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        SearchLoadingRepeater.ItemsSource = Enumerable.Range(0, 24).ToArray();

        ResultsRepeater.ItemsSource = ViewModel.Results;
        PeopleRepeater.ItemsSource = ViewModel.PeopleResults;
        RequestResultsRepeater.ItemsSource = ViewModel.OutsideLibraryResults;

        ViewModel.Results.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdateResultsState);
        };

        ViewModel.PeopleResults.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdatePeopleSection);
        };
        ViewModel.OutsideLibraryResults.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateRequestResults);

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.TotalCount) ||
                args.PropertyName == nameof(ViewModel.IsLoading))
            {
                DispatcherQueue.TryEnqueue(UpdateResultsState);
            }
        };
        SizeChanged += SearchPage_SizeChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (!_initialized)
        {
            await Task.WhenAll(ViewModel.LoadMediaScopeAsync(), ViewModel.LoadFiltersAsync());
            PopulateResultFilters();
            _filterInitializing = false;
            _initialized = true;
        }

        if (e.NavigationMode == NavigationMode.Back && !string.IsNullOrWhiteSpace(ViewModel.Query))
        {
            SearchBox.Text = ViewModel.Query;
            ResultsSearchBox.Text = ViewModel.Query;
            UpdateScopeButtons();
            UpdateResultsState();
            return;
        }

        ViewModel.Query = "";
        ViewModel.Results.Clear();
        ViewModel.PeopleResults.Clear();
        ViewModel.OutsideLibraryResults.Clear();
        SearchBox.Text = "";
        ResultsSearchBox.Text = "";
        UpdateScopeButtons();
        EmptyState.Visibility = Visibility.Visible;
        ResultsState.Visibility = Visibility.Collapsed;

        SearchBox.Focus(FocusState.Programmatic);
    }

    private void UpdateResultsState()
    {
        bool hasQuery = !string.IsNullOrWhiteSpace(ViewModel.Query);

        EmptyState.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;
        ResultsState.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;

        if (hasQuery)
        {
            ResultsTitle.Text = $"Results for \"{ViewModel.Query}\"";
            var mediaCount = ViewModel.TotalCount;
            var peopleCount = ViewModel.PeopleResults.Count;
            var totalDisplay = mediaCount + peopleCount + ViewModel.OutsideLibraryResults.Count;
            ResultCountText.Text = $"{totalDisplay:N0} {(totalDisplay == 1 ? "result" : "results")}";

            NoResultsText.Visibility = mediaCount == 0 && peopleCount == 0 && ViewModel.OutsideLibraryResults.Count == 0 && !ViewModel.IsLoading
                ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdatePeopleSection();
    }

    private void UpdatePeopleSection()
    {
        // The current full catalog search surface only renders media results;
        // people remain available through the global command palette.
        PeopleSection.Visibility = Visibility.Collapsed;
    }

    private async void Scope_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string scope }) return;
        await ViewModel.SetMediaScopeAsync(scope);
        UpdateScopeButtons();
    }

    private void UpdateScopeButtons()
    {
        foreach (var button in new[] { EmptyVideoScope, EmptyAudiobookScope, EmptyAllScope, ResultsVideoScope, ResultsAudiobookScope, ResultsAllScope })
        {
            var active = string.Equals(button.Tag?.ToString(), ViewModel.MediaScope, StringComparison.Ordinal);
            button.Style = (Style)Application.Current.Resources[active ? "AccentButtonStyle" : "OutlineButtonStyle"];
        }
        var placeholder = ViewModel.MediaScope == "audiobook" ? "Search audiobooks..." : ViewModel.MediaScope == "all" ? "Search all media..." : "Search movies, series...";
        SearchBox.PlaceholderText = placeholder;
        ResultsSearchBox.PlaceholderText = placeholder;
        _filterInitializing = true;
        SelectComboTag(ResultTypeCombo, ViewModel.MediaType ?? "all");
        _filterInitializing = false;
    }

    private void UpdateRequestResults() => RequestResultsSection.Visibility = ViewModel.OutsideLibraryResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void RequestResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RequestMediaResult item })
            App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(item.MediaType, item.TmdbId));
    }

    private DispatcherTimer? _searchDebounce;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Sync query from whichever search box was used
        if (sender is TextBox textBox)
        {
            ViewModel.Query = textBox.Text;

            // Sync the other search box without retriggering
            if (textBox == SearchBox && ResultsSearchBox.Text != textBox.Text)
                ResultsSearchBox.Text = textBox.Text;
            else if (textBox == ResultsSearchBox && SearchBox.Text != textBox.Text)
                SearchBox.Text = textBox.Text;
        }

        // Toggle clear button visibility based on whether text is present
        var hasText = !string.IsNullOrEmpty(ViewModel.Query);
        SearchBoxClearButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        ResultsSearchBoxClearButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(ViewModel.Query))
        {
            _searchDebounce?.Stop();
            EmptyState.Visibility = Visibility.Visible;
            ResultsState.Visibility = Visibility.Collapsed;
            return;
        }

        // Match the current WebUI's 100ms live-search navigation debounce.
        _searchDebounce?.Stop();
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce?.Stop();

            EmptyState.Visibility = Visibility.Collapsed;
            ResultsState.Visibility = Visibility.Visible;
            ResultsTitle.Text = $"Results for \"{ViewModel.Query}\"";

            await ViewModel.SearchCommand.ExecuteAsync(null);
        };
        _searchDebounce.Start();
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // When Enter is pressed and we're in empty state, focus the results search box
        if (e.Key == Windows.System.VirtualKey.Enter && sender is TextBox textBox)
        {
            if (textBox == SearchBox && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                ResultsSearchBox.Focus(FocusState.Programmatic);
            }
        }
    }

    private void PersonCard_Click(object sender, RoutedEventArgs e)
    {
        // B33: Person.Id is a string end-to-end now.
        if (sender is Button btn && btn.Tag is string personId && !string.IsNullOrEmpty(personId))
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<PersonDetailPage>(personId);
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        // Clear both search boxes and reset to empty state
        SearchBox.Text = "";
        ResultsSearchBox.Text = "";
        ViewModel.Query = "";
        ViewModel.Results.Clear();
        ViewModel.PeopleResults.Clear();
        ViewModel.OutsideLibraryResults.Clear();
        SearchBoxClearButton.Visibility = Visibility.Collapsed;
        ResultsSearchBoxClearButton.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        ResultsState.Visibility = Visibility.Collapsed;
        _searchDebounce?.Stop();
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void ResultsScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (ResultsScroll.ScrollableHeight - ResultsScroll.VerticalOffset < 900)
            await ViewModel.LoadMoreAsync();
    }

    private void SearchPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var gutter = e.NewSize.Width < 640 ? 16 : e.NewSize.Width < 1024 ? 24 : 40;
        ResultsScroll.Padding = new Thickness(gutter, 0, gutter, 24);
        UpdateCatalogGridLayout(e.NewSize.Width, gutter);
    }

    private void UpdateCatalogGridLayout(double viewportWidth, double gutter)
    {
        var columns = viewportWidth >= 1280 ? 8
            : viewportWidth >= 1024 ? 7
            : viewportWidth >= 768 ? 5
            : viewportWidth >= 640 ? 4
            : 3;
        var contentWidth = Math.Max(320, Math.Min(1400, viewportWidth - (gutter * 2)));
        _catalogCardWidth = Math.Max(96, (contentWidth - (12 * (columns - 1))) / columns);
        ResultsGridLayout.MaximumRowsOrColumns = columns;
        ResultsGridLayout.MinItemWidth = _catalogCardWidth;
        ResultsGridLayout.MinItemHeight = (_catalogCardWidth * 1.5) + 56;
        for (var index = 0; index < ViewModel.Results.Count; index++)
            if (ResultsRepeater.TryGetElement(index) is SiloPlayer.Controls.PosterCard card)
                card.SetCatalogGridLayout(_catalogCardWidth);
    }

    private void ResultsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is SiloPlayer.Controls.PosterCard card)
            card.SetCatalogGridLayout(_catalogCardWidth);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _searchDebounce?.Stop();
        base.OnNavigatedFrom(e);
    }

    private void PopulateResultFilters()
    {
        FillResultCombo(ResultGenreCombo, "All Genres", ViewModel.AvailableFilters?.Genres ?? []);
        FillResultCombo(ResultRatingCombo, "All Ratings", ViewModel.AvailableFilters?.ContentRatings ?? []);
        FillResultCombo(ResultResolutionCombo, "All Resolutions", ViewModel.AvailableFilters?.Resolutions ?? []);
        FillResultCombo(ResultCountryCombo, "All Countries", ViewModel.AvailableFilters?.Countries ?? []);
    }

    private static void FillResultCombo(ComboBox combo, string allLabel, IEnumerable<string> values)
    {
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = allLabel, Tag = "" });
        foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
            combo.Items.Add(new ComboBoxItem { Content = value, Tag = value });
        combo.SelectedIndex = 0;
    }

    private async void ResultFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filterInitializing || string.IsNullOrWhiteSpace(ViewModel.Query)) return;
        if (ReferenceEquals(sender, ResultTypeCombo))
        {
            await ViewModel.SetMediaTypeAsync(SelectedTag(ResultTypeCombo));
            UpdateScopeButtons();
            return;
        }

        ViewModel.SortOrder = SelectedTag(ResultOrderCombo) ?? "desc";
        ViewModel.Genre = SelectedTag(ResultGenreCombo);
        ViewModel.ContentRating = SelectedTag(ResultRatingCombo);
        ViewModel.Resolution = SelectedTag(ResultResolutionCombo);
        ViewModel.Country = SelectedTag(ResultCountryCombo);
        await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private async void ResultSort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filterInitializing) return;
        ViewModel.SortField = SelectedTag(ResultSortCombo) ?? "added_at";
        var ascending = ViewModel.SortField is "title" or "content_rating" or "author" or "narrator" or "series";
        _filterInitializing = true;
        SelectComboTag(ResultOrderCombo, ascending ? "asc" : "desc");
        _filterInitializing = false;
        if (!string.IsNullOrWhiteSpace(ViewModel.Query)) await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private void OpenResultFilters_Click(object sender, RoutedEventArgs e) => ResultFiltersSheet.IsOpen = true;
    private void CloseResultFilters_Click(object sender, RoutedEventArgs e) => ResultFiltersSheet.IsOpen = false;

    private async void ClearResultFilters_Click(object sender, RoutedEventArgs e)
    {
        _filterInitializing = true;
        ResultGenreCombo.SelectedIndex = 0;
        ResultRatingCombo.SelectedIndex = 0;
        ResultResolutionCombo.SelectedIndex = 0;
        ResultCountryCombo.SelectedIndex = 0;
        _filterInitializing = false;
        ViewModel.Genre = null;
        ViewModel.ContentRating = null;
        ViewModel.Resolution = null;
        ViewModel.Country = null;
        if (!string.IsNullOrWhiteSpace(ViewModel.Query)) await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private static string? SelectedTag(ComboBox combo)
    {
        var value = (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void SelectComboTag(ComboBox combo, string value)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = index;
                return;
            }
        }
    }
}
