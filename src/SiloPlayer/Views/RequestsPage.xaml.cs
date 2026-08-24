using SiloPlayer.Core.Models.Requests;
using SiloPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class RequestsPage : Page
{
    public RequestsViewModel ViewModel { get; }
    private string _activeTab = "discover";
    private int _renderedDataVersion = -1;
    private int _layoutBucket = -1;
    private double _requestCardWidth = 184;

    public RequestsPage()
    {
        ViewModel = App.Services.GetRequiredService<RequestsViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
        if (_renderedDataVersion != ViewModel.DataVersion)
        {
            Render();
            _renderedDataVersion = ViewModel.DataVersion;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.CancelSearch();
        base.OnNavigatedFrom(e);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.InvalidateCache();
        await ViewModel.LoadCommand.ExecuteAsync(null);
        Render();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        await RunSearchAsync();
    }

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        ViewModel.SearchQuery = SearchBox.Text;
        ViewModel.SelectedMediaType = SelectedTag(MediaTypeComboBox, "all");
        ViewModel.SearchPage = 1;
        var searchTask = ViewModel.SearchCommand.ExecuteAsync(null);
        _activeTab = "discover";
        Render();
        await searchTask;
        Render();
    }

    private async void MediaType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(ViewModel.SearchQuery)) return;
        ViewModel.SelectedMediaType = SelectedTag(MediaTypeComboBox, "all");
        ViewModel.SearchPage = 1;
        var searchTask = ViewModel.SearchCommand.ExecuteAsync(null);
        Render();
        await searchTask;
        Render();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        SearchButton.IsEnabled = SearchBox.Text.Trim().Length >= 2;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        ViewModel.ClearSearch();
        Render();
    }

    private async void PreviousSearch_Click(object sender, RoutedEventArgs e)
    {
        var searchTask = ViewModel.SetSearchPageAsync(ViewModel.SearchPage - 1);
        Render();
        await searchTask;
        Render();
        PageScrollViewer.ChangeView(null, 360, null);
    }

    private async void NextSearch_Click(object sender, RoutedEventArgs e)
    {
        var searchTask = ViewModel.SetSearchPageAsync(ViewModel.SearchPage + 1);
        Render();
        await searchTask;
        Render();
        PageScrollViewer.ChangeView(null, 360, null);
    }

    private void RequestTab_Click(object sender, RoutedEventArgs e)
    {
        var yours = (sender as FrameworkElement)?.Tag?.ToString() == "yours";
        _activeTab = yours ? "yours" : "discover";
        UpdateTabState();
    }

    private void UpdateTabState()
    {
        var yours = _activeTab == "yours";
        var busy = ViewModel.IsLoading || ViewModel.IsSearching;
        MyRequestsSection.Visibility = yours && !busy ? Visibility.Visible : Visibility.Collapsed;
        DiscoverySection.Visibility = !yours && !busy ? Visibility.Visible : Visibility.Collapsed;
        DiscoverTabButton.Opacity = yours ? 0.6 : 1;
        YoursTabButton.Opacity = yours ? 1 : 0.6;
        DiscoverTabUnderline.Visibility = yours ? Visibility.Collapsed : Visibility.Visible;
        YoursTabUnderline.Visibility = yours ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Render()
    {
        var busy = ViewModel.IsLoading || ViewModel.IsSearching;
        LoadingSkeleton.Visibility = busy
            ? Visibility.Visible : Visibility.Collapsed;
        var pageError = !ViewModel.RequestsEnabled ? "Requests are disabled on this server."
            : string.IsNullOrWhiteSpace(ViewModel.SearchQuery) ? ViewModel.ErrorMessage ?? "" : "";
        ErrorText.Text = pageError;
        ErrorText.Visibility = string.IsNullOrWhiteSpace(pageError) ? Visibility.Collapsed : Visibility.Visible;

        YoursCountText.Text = ViewModel.MyRequests.Count.ToString("N0");
        YoursCountBadge.Visibility = ViewModel.MyRequests.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Preserve the mounted cards while a request or search is in flight.
        // The skeleton covers the content state; rebuilding the hidden rows here
        // caused a visible hitch before and after every search.
        if (busy)
        {
            UpdateTabState();
            return;
        }

        BuildMyRequests();
        BuildSearchResults();
        BuildDiscovery();
        BuildBrands();
        UpdateTabState();
        _renderedDataVersion = ViewModel.DataVersion;
    }

    private void BuildMyRequests()
    {
        MyRequestsList.Children.Clear();
        MineSummaryPanel.Children.Clear();
        MineErrorPanel.Visibility = string.IsNullOrWhiteSpace(ViewModel.MineError) ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(ViewModel.MineError))
        {
            MineEmptyPanel.Visibility = Visibility.Collapsed;
            StatusGuide.Visibility = Visibility.Collapsed;
            return;
        }

        if (ViewModel.MyRequests.Count == 0)
        {
            MineEmptyPanel.Visibility = Visibility.Visible;
            StatusGuide.Visibility = Visibility.Collapsed;
            return;
        }

        MineEmptyPanel.Visibility = Visibility.Collapsed;
        StatusGuide.Visibility = Visibility.Visible;

        var pending = ViewModel.MyRequests.Count(r => r.Outcome == "active" && r.Status == "pending");
        var inFlight = ViewModel.MyRequests.Count(r => r.Outcome == "active" && r.Status is "approved" or "queued" or "downloading");
        var completed = ViewModel.MyRequests.Count(r => r.Status == "completed");
        var issues = ViewModel.MyRequests.Count(r => r.Outcome is "declined" or "cancelled" or "failed");
        AddSummaryChip("Pending review", pending, "#26F59E0B");
        AddSummaryChip("In motion", inFlight, "#2614A8E8");
        AddSummaryChip("Ready to watch", completed, "#2610B981");
        AddSummaryChip("Need attention", issues, "#26EF4444");

        var groups = new[]
        {
            ("In motion", ViewModel.MyRequests.Where(r => r.Outcome == "active" && r.Status != "completed")),
            ("Landed in your library", ViewModel.MyRequests.Where(r => r.Status == "completed")),
            ("Needs attention", ViewModel.MyRequests.Where(r => r.Outcome is "declined" or "cancelled" or "failed")),
        };
        foreach (var (title, items) in groups)
        {
            var values = items.ToList();
            if (values.Count == 0) continue;
            var eyebrow = title switch { "In motion" => "ON THEIR WAY", "Landed in your library" => "READY TO WATCH", _ => "HIT A SNAG" };
            MyRequestsList.Children.Add(new TextBlock { Text = eyebrow, FontSize = 10, CharacterSpacing = 220, FontWeight = FontWeights.SemiBold, Foreground = Brush("SecondaryTextBrush"), Margin = new Thickness(0, 10, 0, -4) });
            MyRequestsList.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
            var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            foreach (var request in values) cards.Children.Add(BuildRequestPosterCard(request));
            MyRequestsList.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = cards });
        }
    }

    private void AddSummaryChip(string label, int count, string background)
    {
        if (count <= 0) return;
        MineSummaryPanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(
                Convert.ToByte(background[1..3], 16), Convert.ToByte(background[3..5], 16),
                Convert.ToByte(background[5..7], 16), Convert.ToByte(background[7..9], 16))),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(11, 5, 11, 5),
            Child = new TextBlock { Text = $"{count}  {label}", FontSize = 12, FontWeight = FontWeights.SemiBold }
        });
    }

    private void BuildSearchResults()
    {
        SearchResultsList.Children.Clear();
        var hasSearch = !string.IsNullOrWhiteSpace(ViewModel.SearchQuery);
        SearchSection.Visibility = hasSearch ? Visibility.Visible : Visibility.Collapsed;
        DiscoveryContent.Visibility = hasSearch ? Visibility.Collapsed : Visibility.Visible;
        if (!hasSearch) return;

        var typeLabel = ViewModel.SelectedMediaType switch { "movie" => "MOVIES", "series" => "SERIES", _ => "ALL" };
        SearchEyebrowText.Text = $"SEARCH RESULTS · {typeLabel}";
        SearchTitleText.Text = $"“{ViewModel.SearchQuery}”";
        SearchCountText.Text = ViewModel.SearchResults.Count > 0
            ? $"{ViewModel.SearchTotalResults:N0} results" + (ViewModel.SearchTotalPages > 1 ? $" · Page {ViewModel.SearchPage} of {ViewModel.SearchTotalPages}" : "") : "";
        foreach (var item in ViewModel.SearchResults)
            SearchResultsList.Children.Add(BuildMediaPosterCard(item));

        var empty = !ViewModel.IsSearching && ViewModel.SearchResults.Count == 0;
        SearchEmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SearchEmptyTitle.Text = string.IsNullOrWhiteSpace(ViewModel.ErrorMessage) ? "Nothing found" : "Search failed";
        SearchEmptyDetail.Text = string.IsNullOrWhiteSpace(ViewModel.ErrorMessage)
            ? $"No titles matched “{ViewModel.SearchQuery}”. Try a different spelling or media type."
            : "TMDB search couldn’t be loaded. Try again in a moment.";
        SearchPager.Visibility = ViewModel.SearchTotalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
        SearchPageText.Text = $"Page {ViewModel.SearchPage} of {ViewModel.SearchTotalPages}";
    }

    private void BuildDiscovery()
    {
        DiscoveryList.Children.Clear();
        DiscoveryErrorPanel.Visibility = string.IsNullOrWhiteSpace(ViewModel.DiscoveryError)
            ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(ViewModel.DiscoveryError)) return;

        foreach (var section in ViewModel.DiscoverySections)
        {
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = section.Key.StartsWith("trending", StringComparison.OrdinalIgnoreCase) ? "TRENDING NOW" : section.Key.StartsWith("popular", StringComparison.OrdinalIgnoreCase) ? "CROWD FAVORITES" : "DISCOVER",
                FontSize = 10, CharacterSpacing = 220, FontWeight = FontWeights.SemiBold,
                Foreground = Brush("SecondaryTextBrush")
            });
            panel.Children.Add(new TextBlock
            {
                Text = section.Title,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("PrimaryTextBrush"),
            });

            var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            foreach (var item in section.Results.Take(16))
                cards.Children.Add(BuildMediaPosterCard(item));
            panel.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = cards,
            });

            DiscoveryList.Children.Add(panel);
        }
    }

    private FrameworkElement BuildMediaPosterCard(RequestMediaResult result)
    {
        var card = new Grid { Width = _requestCardWidth, RowSpacing = 6 };
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_requestCardWidth * 1.5) });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var image = new Image { Width = _requestCardWidth, Height = _requestCardWidth * 1.5, Stretch = Stretch.UniformToFill };
        if (!string.IsNullOrWhiteSpace(result.PosterUrl))
            image.Source = new BitmapImage(new Uri(result.PosterUrl));
        var poster = new Grid { Width = _requestCardWidth, Height = _requestCardWidth * 1.5 };
        poster.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = Brush("CardBackgroundBrush"), Child = image });
        var ribbonLabel = !string.IsNullOrWhiteSpace(result.Request.Status) ? FormatStatus(result.Request.Status)
            : result.Availability == "available" ? "In library"
            : !result.Request.Requestable ? FormatReason(result.Request.Reason) : "";
        if (!string.IsNullOrWhiteSpace(ribbonLabel))
        {
            var ribbon = BuildRequestRibbon(result, ribbonLabel);
            poster.Children.Add(ribbon);
        }
        Grid.SetRow(poster, 0);
        card.Children.Add(poster);

        var title = new TextBlock { Text = result.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(title, 1);
        card.Children.Add(title);
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.YearText)) meta.Add(result.YearText);
        meta.Add(result.MediaType == "series" ? "Series" : "Movie");
        if (result.VoteAverage is > 0) meta.Add($"★ {result.VoteAverage:0.0}");
        var metadata = new TextBlock { Text = string.Join(" · ", meta), FontSize = 11, Foreground = Brush("SecondaryTextBrush") };
        Grid.SetRow(metadata, 2);
        card.Children.Add(metadata);

        var open = new Button
        {
            Tag = new RequestDetailNavigation(result.MediaType, result.TmdbId),
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRowSpan(open, 3);
        AutomationProperties.SetName(open, $"Open {result.Title} request details");
        open.Click += RequestCard_Click;
        card.Children.Add(open);

        if (!string.IsNullOrWhiteSpace(result.LibraryContentId))
        {
            var library = new Button
            {
                Content = "▥  Library",
                Tag = result.LibraryContentId,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(7),
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(220, 20, 20, 22)),
            };
            AutomationProperties.SetName(library, $"Open {result.Title} in library");
            library.Tapped += (_, args) => args.Handled = true;
            library.Click += LibraryBadge_Click;
            Grid.SetRow(library, 0);
            card.Children.Add(library);
        }
        if (result.Request.Requestable)
        {
            var request = new Button { Content = "+  Request", Tag = result, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(13, 6, 13, 6), CornerRadius = new CornerRadius(16), Opacity = 0 };
            AutomationProperties.SetName(request, $"Request {result.Title}");
            request.Tapped += (_, args) => args.Handled = true;
            request.Click += SubmitRequest_Click;
            request.GotFocus += (_, _) => request.Opacity = 1;
            request.LostFocus += (_, _) => request.Opacity = 0;
            Grid.SetRow(request, 0);
            card.Children.Add(request);
            card.PointerEntered += (_, _) => request.Opacity = 1;
            card.PointerExited += (_, _) => request.Opacity = request.FocusState == FocusState.Unfocused ? 0 : 1;
        }
        return card;
    }

    private static Border BuildRequestRibbon(RequestMediaResult result, string label)
    {
        var tone = result.Request.Status switch
        {
            "pending" => "amber",
            "queued" or "downloading" => "sky",
            "approved" or "completed" => "emerald",
            _ when result.Availability == "available" => "emerald",
            _ => "zinc",
        };
        var (background, foreground, border, dotColor) = RequestStatusColors(tone);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(dotColor),
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 60,
            Foreground = new SolidColorBrush(foreground),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var ribbon = new Border
        {
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(7),
            Child = content,
        };
        AutomationProperties.SetName(ribbon, label);
        return ribbon;
    }

    private static (Windows.UI.Color Background, Windows.UI.Color Foreground, Windows.UI.Color Border, Windows.UI.Color Dot) RequestStatusColors(string tone) => tone switch
    {
        "amber" => (Microsoft.UI.ColorHelper.FromArgb(191, 69, 26, 3), Microsoft.UI.ColorHelper.FromArgb(255, 254, 243, 199), Microsoft.UI.ColorHelper.FromArgb(77, 251, 191, 36), Microsoft.UI.ColorHelper.FromArgb(255, 252, 211, 77)),
        "sky" => (Microsoft.UI.ColorHelper.FromArgb(191, 8, 47, 73), Microsoft.UI.ColorHelper.FromArgb(255, 224, 242, 254), Microsoft.UI.ColorHelper.FromArgb(89, 56, 189, 248), Microsoft.UI.ColorHelper.FromArgb(255, 125, 211, 252)),
        "emerald" => (Microsoft.UI.ColorHelper.FromArgb(204, 2, 44, 34), Microsoft.UI.ColorHelper.FromArgb(255, 209, 250, 229), Microsoft.UI.ColorHelper.FromArgb(77, 52, 211, 153), Microsoft.UI.ColorHelper.FromArgb(255, 110, 231, 183)),
        _ => (Microsoft.UI.ColorHelper.FromArgb(204, 24, 24, 27), Microsoft.UI.ColorHelper.FromArgb(255, 228, 228, 231), Microsoft.UI.ColorHelper.FromArgb(26, 255, 255, 255), Microsoft.UI.ColorHelper.FromArgb(255, 161, 161, 170)),
    };

    private void BuildBrands()
    {
        BuildBrandRow(StudiosPanel, "studio", ViewModel.Studios);
        BuildBrandRow(NetworksPanel, "network", ViewModel.Networks);
        BuildBrandRow(GenresPanel, "genre", ViewModel.Genres);
        BrandsSection.Visibility = ViewModel.Studios.Count + ViewModel.Networks.Count + ViewModel.Genres.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildBrandRow(StackPanel panel, string kind, IEnumerable<DiscoverBrandCard> cards)
    {
        panel.Children.Clear();
        foreach (var card in cards)
        {
            var button = new Button
            {
                Tag = new RequestBrowseNavigation(kind, card.Slug),
                Width = 154,
                Height = 82,
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                Background = Brush("CardBackgroundBrush")
            };
            if (!string.IsNullOrWhiteSpace(card.LogoUrl))
                button.Content = new Image { Source = new BitmapImage(new Uri(card.LogoUrl)), Stretch = Stretch.Uniform, MaxWidth = 128, MaxHeight = 54 };
            else
                button.Content = new TextBlock { Text = card.DisplayName, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            button.Click += Brand_Click;
            panel.Children.Add(button);
        }
    }

    private void Brand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RequestBrowseNavigation nav })
            App.Services.GetRequiredService<NavigationService>().Navigate<RequestBrowsePage>(nav);
    }

    private Border BuildRequestRow(MediaRequest request)
    {
        var card = BaseCard();
        card.Tag = new RequestDetailNavigation(request.MediaType, request.TmdbId);
        card.Tapped += RequestCard_Tapped;
        var grid = ThreeColumnGrid();

        var info = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(TitleText($"{request.Title}{YearSuffix(request.Year)}"));
        info.Children.Add(new TextBlock
        {
            Text = $"{FormatMediaType(request.MediaType)} - {FormatStatus(request.Status)} - {FormatOutcome(request.Outcome)}{DateSuffix(request.CreatedAt)}",
            FontSize = 12,
            Foreground = Brush("SecondaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(request.LastError))
        {
            info.Children.Add(new TextBlock
            {
                Text = request.LastError,
                FontSize = 12,
                Foreground = Brush("ErrorBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (string.Equals(request.Outcome, "active", StringComparison.OrdinalIgnoreCase))
        {
            var cancel = new Button { Content = "Cancel", Tag = request };
            cancel.Click += CancelRequest_Click;
            actions.Children.Add(cancel);
        }

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        card.Child = grid;
        return card;
    }

    private FrameworkElement BuildRequestPosterCard(MediaRequest request)
    {
        var panel = new Grid { Width = _requestCardWidth, RowSpacing = 6 };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_requestCardWidth * 1.5) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var image = new Image { Width = _requestCardWidth, Height = _requestCardWidth * 1.5, Stretch = Stretch.UniformToFill };
        if (!string.IsNullOrWhiteSpace(request.PosterPath)) image.Source = new BitmapImage(new Uri($"https://image.tmdb.org/t/p/w342{request.PosterPath}"));
        var poster = new Grid { Width = _requestCardWidth, Height = _requestCardWidth * 1.5 };
        poster.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = Brush("CardBackgroundBrush"), Child = image });
        var failed = request.Outcome is "declined" or "cancelled" or "failed";
        var label = failed ? FormatOutcome(request.Outcome) : FormatStatus(request.Status);
        var tone = failed ? "zinc" : request.Status switch
        {
            "pending" => "amber",
            "queued" or "downloading" => "sky",
            "approved" or "completed" => "emerald",
            _ => "zinc",
        };
        var (ribbonBackground, ribbonForeground, ribbonBorder, ribbonDot) = RequestStatusColors(tone);
        var ribbonContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        ribbonContent.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(ribbonDot),
            VerticalAlignment = VerticalAlignment.Center,
        });
        ribbonContent.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            CharacterSpacing = 60,
            Foreground = new SolidColorBrush(ribbonForeground),
        });
        var ribbon = new Border
        {
            Background = new SolidColorBrush(ribbonBackground),
            BorderBrush = new SolidColorBrush(ribbonBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8),
            Child = ribbonContent,
        };
        AutomationProperties.SetName(ribbon, label);
        poster.Children.Add(ribbon);
        if (request.Status == "completed")
        {
            poster.Children.Add(new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(220, 6, 78, 59)),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 4, 9, 4),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock { Text = "✓  Ready to watch", FontSize = 10, FontWeight = FontWeights.SemiBold }
            });
        }
        Grid.SetRow(poster, 0);
        panel.Children.Add(poster);
        var title = new TextBlock { Text = request.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(title, 1);
        panel.Children.Add(title);
        var metadata = new TextBlock { Text = $"{request.Year} · {(request.MediaType == "series" ? "Series" : "Movie")}", FontSize = 11, Foreground = Brush("SecondaryTextBrush") };
        Grid.SetRow(metadata, 2);
        panel.Children.Add(metadata);
        if (!string.IsNullOrWhiteSpace(request.LastError))
        {
            var error = new TextBlock { Text = request.LastError, FontSize = 10, Foreground = Brush("ErrorBrush"), TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
            Grid.SetRow(error, 3);
            panel.Children.Add(error);
        }

        var open = new Button
        {
            Tag = new RequestDetailNavigation(request.MediaType, request.TmdbId),
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRowSpan(open, 4);
        AutomationProperties.SetName(open, $"Open {request.Title} request details");
        open.Click += RequestCard_Click;
        panel.Children.Add(open);

        if (!string.IsNullOrWhiteSpace(request.LibraryContentId))
        {
            var library = new Button
            {
                Content = "▥  Library",
                Tag = request.LibraryContentId,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(220, 20, 20, 22)),
            };
            Grid.SetRow(library, 0);
            AutomationProperties.SetName(library, $"Open {request.Title} in library");
            library.Tapped += (_, args) => args.Handled = true;
            library.Click += LibraryBadge_Click;
            panel.Children.Add(library);
        }
        return panel;
    }

    private Border BuildMediaResultRow(RequestMediaResult result)
    {
        var card = BaseCard();
        card.Tag = new RequestDetailNavigation(result.MediaType, result.TmdbId);
        card.Tapped += RequestCard_Tapped;
        var grid = ThreeColumnGrid();

        var info = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(TitleText($"{result.Title}{YearSuffix(result.Year)}"));
        info.Children.Add(new TextBlock
        {
            Text = $"{FormatMediaType(result.MediaType)} - {FormatAvailability(result.Availability)} - {FormatVote(result.VoteAverage)}",
            FontSize = 12,
            Foreground = Brush("SecondaryTextBrush"),
        });
        if (!string.IsNullOrWhiteSpace(result.Overview))
        {
            info.Children.Add(new TextBlock
            {
                Text = result.Overview,
                FontSize = 12,
                Foreground = Brush("TertiaryTextBrush"),
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
            });
        }

        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var request = new Button
        {
            Content = result.Request.Requestable ? "Request" : RequestUnavailableLabel(result),
            Tag = result,
            IsEnabled = result.Request.Requestable,
            VerticalAlignment = VerticalAlignment.Center,
        };
        request.Click += SubmitRequest_Click;
        Grid.SetColumn(request, 2);
        grid.Children.Add(request);

        card.Child = grid;
        return card;
    }

    private async void SubmitRequest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RequestMediaResult result } button) return;
        button.IsEnabled = false;
        button.Content = "Sending…";
        await ViewModel.SubmitRequestCommand.ExecuteAsync(result);
        Render();
    }

    private async void CancelRequest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MediaRequest request }) return;
        await ViewModel.CancelRequestCommand.ExecuteAsync(request);
        Render();
    }

    private void RequestCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RequestDetailNavigation nav })
            App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(nav);
    }

    private void RequestCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RequestDetailNavigation nav })
            App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(nav);
    }

    private void LibraryBadge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string contentId } && !string.IsNullOrWhiteSpace(contentId))
            App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(contentId);
    }

    private static Grid ThreeColumnGrid()
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    private static Border BaseCard() => new()
    {
        Background = Brush("CardBackgroundBrush"),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16, 12, 16, 12),
    };

    private static TextBlock TitleText(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush("PrimaryTextBrush"),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static TextBlock MakeMutedText(string text) => new()
    {
        Text = text,
        Foreground = Brush("SecondaryTextBrush"),
        FontSize = 13,
    };

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private static string SelectedTag(ComboBox comboBox, string fallback)
        => comboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : fallback;

    private static string YearSuffix(int? year) => year.HasValue && year.Value > 0 ? $" ({year.Value})" : "";
    private static string DateSuffix(string date) => string.IsNullOrWhiteSpace(date) ? "" : $" - {FormatDate(date)}";
    private static string FormatMediaType(string value) => string.Equals(value, "series", StringComparison.OrdinalIgnoreCase) ? "Series" : "Movie";
    private static string FormatAvailability(string value) => string.Equals(value, "available", StringComparison.OrdinalIgnoreCase) ? "Already available" : "Missing";
    private static string FormatStatus(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : char.ToUpperInvariant(value[0]) + value[1..];
    private static string FormatOutcome(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : char.ToUpperInvariant(value[0]) + value[1..];
    private static string FormatVote(double? vote) => vote.HasValue && vote.Value > 0 ? $"{vote.Value:0.0}/10" : "Unrated";
    private static string RequestUnavailableLabel(RequestMediaResult result)
        => !string.IsNullOrWhiteSpace(result.Request.Reason) ? result.Request.Reason : "Unavailable";
    private static string FormatReason(string value) => value switch
    {
        "already_requested" => "Already requested",
        "already_available" => "Available",
        "requests_disabled" => "Requests disabled",
        "quota_exceeded" => "Limit reached",
        "blocked" => "Blocked",
        _ => "Unavailable"
    };

    private static string FormatDate(string value)
        => DateTime.TryParse(value, out var dt) ? DateTimeDisplay.FormatDate(new DateTimeOffset(dt), medium: true) : value;

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;

        var gutter = width < 640 ? 16d
            : width < 1024 ? 24d
            : width < 1280 ? 40d
            : 48d;
        PageContent.Padding = new Thickness(gutter, width < 640 ? 24 : 32, gutter, 48);
        PageTitleText.FontSize = width < 640 ? 29 : width < 1024 ? 36 : 42;

        var compact = width < 640;
        Grid.SetRow(MediaTypeComboBox, 0);
        Grid.SetColumn(MediaTypeComboBox, compact ? 0 : 0);
        Grid.SetColumnSpan(MediaTypeComboBox, compact ? 3 : 1);
        Grid.SetRow(SearchInputGrid, compact ? 1 : 0);
        Grid.SetColumn(SearchInputGrid, compact ? 0 : 1);
        Grid.SetColumnSpan(SearchInputGrid, compact ? 3 : 1);
        Grid.SetRow(SearchButton, compact ? 2 : 0);
        Grid.SetColumn(SearchButton, compact ? 0 : 2);
        Grid.SetColumnSpan(SearchButton, compact ? 3 : 1);
        SearchButton.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;

        var guideCompact = width < 900;
        for (var i = 0; i < StatusGuideGrid.Children.Count; i++)
        {
            if (StatusGuideGrid.Children[i] is not FrameworkElement child) continue;
            Grid.SetRow(child, guideCompact ? i : 0);
            Grid.SetColumn(child, guideCompact ? 0 : i);
            Grid.SetColumnSpan(child, guideCompact ? 3 : 1);
        }

        var bucket = width < 640 ? 0 : width < 1024 ? 1 : 2;
        _requestCardWidth = bucket switch { 0 => 148, 1 => 164, _ => 184 };
        if (_layoutBucket != bucket)
        {
            var hadLayout = _layoutBucket >= 0;
            _layoutBucket = bucket;
            if (hadLayout && IsLoaded && _renderedDataVersion >= 0)
                Render();
        }
    }
}
