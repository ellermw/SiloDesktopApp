using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Controls;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class LibraryPage : Page
{
    // B42: Persist last-viewed tab + filters per library across navigations.
    // Web parses this from the URL (?tab=library|collections); we don't have
    // routing yet, so we mirror the page state in a per-library in-memory dict.
    private sealed class LibraryViewState
    {
        public string Tab { get; set; } = "Recommended";
        public string Sort { get; set; } = "sort_title";
        public string Order { get; set; } = "asc";
        public string? MediaType { get; set; }
        public string? Genre { get; set; }
        public string? ContentRating { get; set; }
        public string? Studio { get; set; }
        public string? Country { get; set; }
        public string? Resolution { get; set; }
        public string? AudioLanguage { get; set; }
        public string? YearMin { get; set; }
        public string? YearMax { get; set; }
    }

    private static readonly Dictionary<int, LibraryViewState> _viewStateByLibrary = new();

    public LibraryViewModel ViewModel { get; }
    private bool _suppressFilterEvents;
    private bool _recommendedLoaded;
    private bool _collectionsLoaded;
    private bool _orderAsc = true;
    private string _currentTab = "Recommended";
    private DispatcherTimer? _yearDebounceTimer;

    public LibraryPage()
    {
        ViewModel = App.Services.GetRequiredService<LibraryViewModel>();
        this.InitializeComponent();
        SmoothScrollHelper.Attach(ContentScrollViewer);

        PosterRepeater.ItemsSource = ViewModel.Items;

        _suppressFilterEvents = true;
        SortComboBox.SelectedIndex = 0;
        _suppressFilterEvents = false;

        ViewModel.Genres.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateGenreCombo());

        ViewModel.ContentRatings.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateContentRatingCombo());

        ViewModel.Studios.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateStudioCombo());

        ViewModel.Countries.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateCountryCombo());

        ViewModel.Resolutions.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateResolutionCombo());

        ViewModel.AudioLanguages.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateAudioLangCombo());

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

            // B42: Restore previously-viewed tab + filters for this library if any.
            // Falls back to fresh defaults on first visit.
            _viewStateByLibrary.TryGetValue(library.Id, out var state);
            state ??= new LibraryViewState();

            _suppressFilterEvents = true;
            SortComboBox.SelectedIndex = IndexOfSortTag(state.Sort);
            MediaTypeComboBox.SelectedIndex = IndexOfMediaType(state.MediaType);
            GenreComboBox.SelectedIndex = -1;
            ContentRatingComboBox.SelectedIndex = -1;
            StudioComboBox.SelectedIndex = -1;
            CountryComboBox.SelectedIndex = -1;
            ResolutionComboBox.SelectedIndex = -1;
            AudioLangComboBox.SelectedIndex = -1;
            YearMinBox.Text = state.YearMin ?? "";
            YearMaxBox.Text = state.YearMax ?? "";
            _orderAsc = state.Order != "desc";
            UpdateOrderButton();
            ViewModel.SelectedSort = state.Sort;
            ViewModel.SelectedOrder = state.Order;
            ViewModel.SelectedType = state.MediaType;
            ViewModel.SelectedGenre = state.Genre;
            ViewModel.SelectedContentRating = state.ContentRating;
            ViewModel.SelectedStudio = state.Studio;
            ViewModel.SelectedCountry = state.Country;
            ViewModel.SelectedResolution = state.Resolution;
            ViewModel.SelectedAudioLanguage = state.AudioLanguage;
            ViewModel.SelectedYearMin = state.YearMin;
            ViewModel.SelectedYearMax = state.YearMax;
            _suppressFilterEvents = false;
            _recommendedLoaded = false;
            _collectionsLoaded = false;
            ActiveFiltersBar.Visibility = Visibility.Collapsed;

            ShowTab(state.Tab);

            if (state.Tab == "Recommended" && !_recommendedLoaded)
                await LoadRecommendationsAsync();

            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private static int IndexOfSortTag(string tag) => tag switch
    {
        "sort_title" => 0,
        "recently_added" => 1,
        "year" => 2,
        "rating_imdb" => 3,
        _ => 0,
    };

    private static int IndexOfMediaType(string? type) => type switch
    {
        "movie" => 1,
        "series" => 2,
        _ => 0,
    };

    /// <summary>B42: Persist current page state for this library.</summary>
    private void SaveViewState(string tab)
    {
        if (ViewModel.Library == null) return;
        _viewStateByLibrary[ViewModel.Library.Id] = new LibraryViewState
        {
            Tab = tab,
            Sort = ViewModel.SelectedSort ?? "sort_title",
            Order = ViewModel.SelectedOrder ?? "asc",
            MediaType = ViewModel.SelectedType,
            Genre = ViewModel.SelectedGenre,
            ContentRating = ViewModel.SelectedContentRating,
            Studio = ViewModel.SelectedStudio,
            Country = ViewModel.SelectedCountry,
            Resolution = ViewModel.SelectedResolution,
            AudioLanguage = ViewModel.SelectedAudioLanguage,
            YearMin = ViewModel.SelectedYearMin,
            YearMax = ViewModel.SelectedYearMax,
        };
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
        LibraryContentArea.Visibility = tag == "Library" ? Visibility.Visible : Visibility.Collapsed;
        RecommendedPanel.Visibility = tag == "Recommended" ? Visibility.Visible : Visibility.Collapsed;
        CollectionsPanel.Visibility = tag == "Collections" ? Visibility.Visible : Visibility.Collapsed;

        _currentTab = tag;
        // B42: Persist tab selection so a return to this library lands on the same tab.
        SaveViewState(tag);
    }

    /// <summary>
    /// Keeps loading pages until the content exceeds the viewport height.
    /// </summary>
    private async Task FillViewportAsync()
    {
        await Task.Delay(200);

        while (ViewModel.HasMore && !ViewModel.IsLoading &&
               ContentScrollViewer.ScrollableHeight < 200 &&
               LibraryContentArea.Visibility == Visibility.Visible)
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
            SaveViewState(_currentTab); // B42
        }
    }

    private async void OrderToggle_Click(object sender, RoutedEventArgs e)
    {
        _orderAsc = !_orderAsc;
        UpdateOrderButton();
        ViewModel.SelectedOrder = _orderAsc ? "asc" : "desc";
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        SaveViewState(_currentTab); // B42
    }

    private async void MediaTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (MediaTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string type)
        {
            ViewModel.SelectedType = string.IsNullOrEmpty(type) ? null : type;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
            UpdateActiveFilterBadges();
            SaveViewState(_currentTab); // B42
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
            UpdateActiveFilterBadges();
            SaveViewState(_currentTab); // B42
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
            UpdateActiveFilterBadges();
            SaveViewState(_currentTab); // B42
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
            UpdateActiveFilterBadges();
            SaveViewState(_currentTab); // B42
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

        if (tag == "Collections" && !_collectionsLoaded)
            await LoadCollectionsAsync();
    }

    private async Task LoadRecommendationsAsync()
    {
        _recommendedLoaded = true;
        RecommendedLoading.IsActive = true;
        RecommendedLoading.Visibility = Visibility.Visible;
        RecommendedError.Visibility = Visibility.Collapsed;
        RecommendedHeroCarousel.Visibility = Visibility.Collapsed;

        // Clear any previous section rows (keep loading ring, error text, hero carousel)
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

            // Web parity (LibraryRecommended.tsx/splitLibrarySections): first featured
            // section becomes the HeroBanner, the rest render as SectionRows.
            HomeSectionWithItems? heroSection = null;
            var rowSections = new List<HomeSectionWithItems>();
            foreach (var section in response.Sections)
            {
                if (section.Items.Count == 0) continue;
                if (heroSection == null && section.Featured)
                    heroSection = section;
                else
                    rowSections.Add(section);
            }

            if (heroSection != null)
            {
                var limit = heroSection.ItemLimit > 0 ? heroSection.ItemLimit : heroSection.Items.Count;
                var heroItems = heroSection.Items.Take(limit).ToList();
                RecommendedHeroCarousel.ItemsSource = heroItems;
                RecommendedHeroCarousel.Visibility = Visibility.Visible;
            }

            foreach (var section in rowSections)
            {
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

    // ===== Collections Tab =====

    private async Task LoadCollectionsAsync()
    {
        _collectionsLoaded = true;
        CollectionsLoading.IsActive = true;
        CollectionsLoading.Visibility = Visibility.Visible;

        await ViewModel.LoadCollectionsCommand.ExecuteAsync(null);

        CollectionsLoading.IsActive = false;
        CollectionsLoading.Visibility = Visibility.Collapsed;

        BuildCollectionCards();
    }

    private void BuildCollectionCards()
    {
        var collections = ViewModel.Collections;

        if (collections.Count == 0)
        {
            CollectionsEmptyCard.Visibility = Visibility.Visible;
            CollectionsRepeater.Visibility = Visibility.Collapsed;
            return;
        }

        CollectionsEmptyCard.Visibility = Visibility.Collapsed;
        CollectionsRepeater.Visibility = Visibility.Visible;

        // Build a wrapped grid of collection cards
        CollectionsRepeater.ItemsSource = null;

        var cardElements = new List<FrameworkElement>();
        foreach (var c in collections)
        {
            cardElements.Add(CreateCollectionCard(c));
        }

        // Replace the repeater content with a simple panel
        var wrapPanel = new StackPanel();
        var currentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        int cardsPerRow = 5;
        int count = 0;

        foreach (var card in cardElements)
        {
            currentRow.Children.Add(card);
            count++;
            if (count % cardsPerRow == 0)
            {
                wrapPanel.Children.Add(currentRow);
                currentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 16, 0, 0) };
            }
        }

        if (currentRow.Children.Count > 0)
            wrapPanel.Children.Add(currentRow);

        // Replace the empty card with our content
        var parent = (StackPanel)CollectionsRepeater.Parent!;
        int idx = parent.Children.IndexOf(CollectionsRepeater);
        if (idx >= 0)
        {
            // Remove old dynamic panels if any
            for (int i = parent.Children.Count - 1; i >= 0; i--)
            {
                if (parent.Children[i] is StackPanel sp && sp.Name == null && sp != parent && sp.Tag is "CollectionGrid")
                    parent.Children.RemoveAt(i);
            }

            wrapPanel.Tag = "CollectionGrid";
            parent.Children.Insert(idx + 1, wrapPanel);
        }
    }

    private Border CreateCollectionCard(LibraryCollection collection)
    {
        // Poster area
        var posterBorder = new Border
        {
            Width = 180,
            Height = 200,
            CornerRadius = new CornerRadius(8),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"]
        };

        var posterPlaceholder = new FontIcon
        {
            Glyph = "\uE8F1",
            FontSize = 32,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        posterBorder.Child = posterPlaceholder;

        if (!string.IsNullOrEmpty(collection.PosterUrl))
        {
            _ = LoadCollectionPosterAsync(posterBorder, collection);
        }

        // Type badge overlay
        var typeBadge = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBackgroundBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 6, 0, 0),
            Child = new TextBlock
            {
                Text = collection.CollectionType.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"]
            }
        };

        var posterGrid = new Grid { Width = 180, Height = 200 };
        posterGrid.Children.Add(posterBorder);
        posterGrid.Children.Add(typeBadge);

        // Title
        var titleText = new TextBlock
        {
            Text = collection.Title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(2, 8, 2, 0)
        };

        // Item count
        var countText = new TextBlock
        {
            Text = $"{collection.ItemCount} {(collection.ItemCount == 1 ? "item" : "items")}",
            FontSize = 11,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"],
            Margin = new Thickness(2, 2, 2, 0)
        };

        var content = new StackPanel
        {
            Width = 180,
            Children = { posterGrid, titleText, countText }
        };

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = content,
            Tag = collection
        };

        card.PointerEntered += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceHoverBrush"];
        };
        card.PointerExited += (s, _) =>
        {
            if (s is Border b)
                b.Background = null;
        };

        // B41: navigate to a standalone collection browse page instead of
        // mutating the library tab items in place. Preserves back navigation
        // and mirrors the webui /catalog?source=library_collection route.
        card.Tapped += (s, _) =>
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
            {
                CollectionId = collection.Id,
                Title = collection.Title,
                Subtitle = ViewModel.Library?.Name,
                IsUserCollection = false,
            });
        };

        return card;
    }

    private async Task LoadCollectionPosterAsync(Border posterBorder, LibraryCollection collection)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                collection.Id, "collection_poster", collection.PosterUrl!, httpClient, CancellationToken.None);

            if (bytes == null) return;

            var bitmapImage = new BitmapImage
            {
                DecodePixelWidth = 200,
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

    // ===== Enhanced Filters =====

    private void UpdateStudioCombo()
    {
        _suppressFilterEvents = true;
        StudioComboBox.Items.Clear();
        StudioComboBox.Items.Add(new ComboBoxItem { Content = "All Studios", Tag = "" });
        foreach (var studio in ViewModel.Studios)
        {
            if (!string.IsNullOrEmpty(studio))
                StudioComboBox.Items.Add(new ComboBoxItem { Content = studio, Tag = studio });
        }
        StudioComboBox.SelectedIndex = 0;
        _suppressFilterEvents = false;
    }

    private void UpdateCountryCombo()
    {
        _suppressFilterEvents = true;
        CountryComboBox.Items.Clear();
        CountryComboBox.Items.Add(new ComboBoxItem { Content = "All Countries", Tag = "" });
        foreach (var country in ViewModel.Countries)
        {
            if (!string.IsNullOrEmpty(country))
                CountryComboBox.Items.Add(new ComboBoxItem { Content = country, Tag = country });
        }
        CountryComboBox.SelectedIndex = 0;
        _suppressFilterEvents = false;
    }

    private void UpdateResolutionCombo()
    {
        _suppressFilterEvents = true;
        ResolutionComboBox.Items.Clear();
        ResolutionComboBox.Items.Add(new ComboBoxItem { Content = "All Quality", Tag = "" });
        foreach (var res in ViewModel.Resolutions)
        {
            if (!string.IsNullOrEmpty(res))
                ResolutionComboBox.Items.Add(new ComboBoxItem { Content = res, Tag = res });
        }
        ResolutionComboBox.SelectedIndex = 0;
        _suppressFilterEvents = false;
    }

    private void UpdateAudioLangCombo()
    {
        _suppressFilterEvents = true;
        AudioLangComboBox.Items.Clear();
        AudioLangComboBox.Items.Add(new ComboBoxItem { Content = "All Audio", Tag = "" });
        foreach (var lang in ViewModel.AudioLanguages)
        {
            if (!string.IsNullOrEmpty(lang))
                AudioLangComboBox.Items.Add(new ComboBoxItem { Content = lang, Tag = lang });
        }
        AudioLangComboBox.SelectedIndex = 0;
        _suppressFilterEvents = false;
    }

    private async void StudioComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (StudioComboBox.SelectedItem is ComboBoxItem item && item.Tag is string studio)
        {
            ViewModel.SelectedStudio = string.IsNullOrEmpty(studio) ? null : studio;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
            UpdateActiveFilterBadges();
            SaveViewState(_currentTab); // B42
        }
    }

    private async void CountryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (CountryComboBox.SelectedItem is ComboBoxItem item && item.Tag is string country)
        {
            ViewModel.SelectedCountry = string.IsNullOrEmpty(country) ? null : country;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
            UpdateActiveFilterBadges();
            SaveViewState(_currentTab); // B42
        }
    }

    private async void ResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (ResolutionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string res)
        {
            ViewModel.SelectedResolution = string.IsNullOrEmpty(res) ? null : res;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
            UpdateActiveFilterBadges();
            SaveViewState(_currentTab); // B42
        }
    }

    private async void AudioLangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (AudioLangComboBox.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            ViewModel.SelectedAudioLanguage = string.IsNullOrEmpty(lang) ? null : lang;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
            UpdateActiveFilterBadges();
            SaveViewState(_currentTab); // B42
        }
    }

    private void UpdateActiveFilterBadges()
    {
        FilterBadgesPanel.Children.Clear();

        var filters = new List<(string Label, string Value, Action ClearAction)>();

        if (!string.IsNullOrEmpty(ViewModel.SelectedType))
        {
            var label = ViewModel.SelectedType == "movie" ? "Movies" : "Series";
            filters.Add(("Type", label, () => { ViewModel.SelectedType = null; MediaTypeComboBox.SelectedIndex = 0; }));
        }
        if (!string.IsNullOrEmpty(ViewModel.SelectedGenre))
            filters.Add(("Genre", ViewModel.SelectedGenre, () => { ViewModel.SelectedGenre = null; GenreComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedContentRating))
            filters.Add(("Rating", ViewModel.SelectedContentRating, () => { ViewModel.SelectedContentRating = null; ContentRatingComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedStudio))
            filters.Add(("Studio", ViewModel.SelectedStudio, () => { ViewModel.SelectedStudio = null; StudioComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedCountry))
            filters.Add(("Country", ViewModel.SelectedCountry, () => { ViewModel.SelectedCountry = null; CountryComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedResolution))
            filters.Add(("Quality", ViewModel.SelectedResolution, () => { ViewModel.SelectedResolution = null; ResolutionComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedAudioLanguage))
            filters.Add(("Audio", ViewModel.SelectedAudioLanguage, () => { ViewModel.SelectedAudioLanguage = null; AudioLangComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedYearMin))
            filters.Add(("Year From", ViewModel.SelectedYearMin, () => { ViewModel.SelectedYearMin = null; YearMinBox.Text = ""; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedYearMax))
            filters.Add(("Year To", ViewModel.SelectedYearMax, () => { ViewModel.SelectedYearMax = null; YearMaxBox.Text = ""; }));

        if (filters.Count == 0)
        {
            ActiveFiltersBar.Visibility = Visibility.Collapsed;
            return;
        }

        ActiveFiltersBar.Visibility = Visibility.Visible;
        ClearAllFiltersButton.Visibility = filters.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (label, value, clearAction) in filters)
        {
            var badge = CreateFilterBadge(label, value, clearAction);
            FilterBadgesPanel.Children.Add(badge);
        }
    }

    private Border CreateFilterBadge(string label, string value, Action clearAction)
    {
        var closeBtn = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(2),
            MinWidth = 0,
            MinHeight = 0,
            Content = new FontIcon
            {
                Glyph = "\uE711",
                FontSize = 10,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };
        closeBtn.Click += async (_, _) =>
        {
            _suppressFilterEvents = true;
            clearAction();
            _suppressFilterEvents = false;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
            UpdateActiveFilterBadges();
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = $"{label}: {value}",
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                },
                closeBtn
            }
        };

        return new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 6, 4),
            Child = content
        };
    }

    private async void ClearAllFilters_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilterEvents = true;
        ViewModel.SelectedType = null;
        ViewModel.SelectedGenre = null;
        ViewModel.SelectedContentRating = null;
        ViewModel.SelectedStudio = null;
        ViewModel.SelectedCountry = null;
        ViewModel.SelectedResolution = null;
        ViewModel.SelectedAudioLanguage = null;
        ViewModel.SelectedYearMin = null;
        ViewModel.SelectedYearMax = null;
        MediaTypeComboBox.SelectedIndex = 0;
        GenreComboBox.SelectedIndex = 0;
        ContentRatingComboBox.SelectedIndex = 0;
        StudioComboBox.SelectedIndex = 0;
        CountryComboBox.SelectedIndex = 0;
        ResolutionComboBox.SelectedIndex = 0;
        AudioLangComboBox.SelectedIndex = 0;
        YearMinBox.Text = "";
        YearMaxBox.Text = "";
        _suppressFilterEvents = false;

        ActiveFiltersBar.Visibility = Visibility.Collapsed;

        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
    }
}
