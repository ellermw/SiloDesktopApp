using SiloPlayer.Core.Models.Requests;
using SiloPlayer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Converters;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class RequestsPage : Page
{
    private static readonly UrlToImageSourceConverter RemoteImageConverter = new();
    public RequestsViewModel ViewModel { get; }
    private string _activeTab = "discover";
    private int _renderedDataVersion = -1;
    private int _layoutBucket = -1;
    private double _requestCardWidth = 184;
    private readonly StackPanel _mineLoadingSkeleton = new() { Spacing = 12, Visibility = Visibility.Collapsed };
    private readonly Grid _mineHeading = new() { ColumnSpacing = 8, RowSpacing = 8 };
    private Button? _mineHelp;
    private readonly DispatcherTimer _downloadRefreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _downloadRefreshOwner;
    private bool _downloadTickInProgress;

    public RequestsPage()
    {
        ViewModel = App.Services.GetRequiredService<RequestsViewModel>();
        InitializeComponent();
        for (var i = 0; i < 4; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Padding = new Thickness(0, 12, 0, 12) };
            row.Children.Add(new SiloPlayer.Controls.SkeletonBox { Width = 48, Height = 72, CornerRadius = new CornerRadius(6) });
            var text = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var (width, height) in new[] { (192d, 16d), (128d, 12d), (160d, 12d) })
                text.Children.Add(new SiloPlayer.Controls.SkeletonBox { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4) });
            row.Children.Add(text); _mineLoadingSkeleton.Children.Add(row);
        }
        PageContent.Children.Insert(PageContent.Children.IndexOf(LoadingSkeleton), _mineLoadingSkeleton);
        MyRequestsSection.Children.Remove(StatusGuide);
        var help = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { SiloPlayer.Controls.WebUiIcon.Create("info", 16, Brush("SecondaryTextBrush")), new TextBlock { Text = "What the statuses mean", FontSize = 14, FontWeight = FontWeights.Normal, Foreground = Brush("SecondaryTextBrush") } } }, HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Application.Current.Resources["GhostButtonStyle"], FontSize = 14, MinHeight = 0, Height = 32, Padding = new Thickness(12,0,12,0) };
        StatusGuide.Width = 286; StatusGuide.MaxWidth = 286;
        StatusGuideGrid.ColumnDefinitions.Clear(); StatusGuideGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < StatusGuideGrid.Children.Count; i++)
        { var child = (FrameworkElement)StatusGuideGrid.Children[i]; Grid.SetRow(child, i); Grid.SetColumn(child, 0); Grid.SetColumnSpan(child, 1); }
        StatusGuideGrid.Children.Clear();
        StatusGuideGrid.RowDefinitions.Clear();
        StatusGuideGrid.ColumnDefinitions[0].Width = new GridLength(120);
        StatusGuideGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var descriptions = new[]
        {
            ("Pending", "Waiting for an admin. You can cancel it until it is sent to the download automation."),
            ("Approved", "Approved, not yet sent to the download automation. You can still cancel it."),
            ("Processing", "Sent to the download automation."),
            ("Partially available", "Some episodes of the requested seasons have arrived."),
            ("Available", "In your library and ready to watch."), ("Declined", "An admin declined it."),
            ("Cancelled", "Withdrawn before it was sent to the download automation."),
            ("Failed", "Silo or the download automation hit an error."),
        };
        for (var i = 0; i < descriptions.Length; i++)
        {
            StatusGuideGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var badge = SiloPlayer.Controls.ExternalTitleCard.StatusBadge(descriptions[i].Item1); badge.VerticalAlignment = VerticalAlignment.Top;
            var text = new TextBlock { Text = descriptions[i].Item2, FontSize = 12, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Foreground = Brush("SecondaryTextBrush") };
            Grid.SetRow(badge, i); Grid.SetRow(text, i); Grid.SetColumn(text, 1); StatusGuideGrid.Children.Add(badge); StatusGuideGrid.Children.Add(text);
        }
        StatusGuide.Padding = new Thickness(0); StatusGuide.BorderThickness = new Thickness(0);
        StatusGuide.Background = null; StatusGuideGrid.ColumnSpacing = 12; StatusGuideGrid.RowSpacing = 10;
        var statusPresenter = new Style { TargetType = typeof(FlyoutPresenter) };
        statusPresenter.Setters.Add(new Setter(FrameworkElement.WidthProperty, 320d));
        statusPresenter.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16)));
        statusPresenter.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        statusPresenter.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(12)));
        statusPresenter.Setters.Add(new Setter(Control.BackgroundProperty, Brush("PopoverBrush")));
        statusPresenter.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("BorderBrush")));
        help.Flyout = new Flyout { Content = StatusGuide, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterStyle = statusPresenter };
        MineSummaryPanel.Visibility = Visibility.Collapsed;
        _mineHeading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _mineHeading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _mineHeading.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _mineHeading.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _mineHeading.Children.Add(new TextBlock { Text = "Requests from your account, grouped by what happens next.", FontSize = 14, LineHeight = 20,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center });
        _mineHelp = help; Grid.SetColumn(help, 1); _mineHeading.Children.Add(help); MyRequestsSection.Children.Insert(0, _mineHeading);
        foreach (var brandRegion in BrandsSection.Children.OfType<StackPanel>())
        {
            if (brandRegion.Children.OfType<ScrollViewer>().FirstOrDefault() is not { } scroll) continue;
            brandRegion.Children.Remove(scroll); brandRegion.Children.Add(new SiloPlayer.Controls.CarouselRail(scroll));
        }
        var customization = App.Services.GetService<SiloPlayer.Services.UICustomizationService>();
        _downloadRefreshTimer.Tick += DownloadRefresh_Tick;
        Loaded += (_, _) => { if (customization != null) { customization.Changed -= CardPresentationChanged; customization.Changed += CardPresentationChanged; } };
        Unloaded += (_, _) => { StopDownloadRefresh(); if (customization != null) customization.Changed -= CardPresentationChanged; };
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    private void CardPresentationChanged(object? sender, EventArgs e)
    {
        UpdateCardWidth(ActualWidth); Render();
    }
    private async void RetryMine_Click(object sender, RoutedEventArgs e) { if (sender is Button button) button.IsEnabled = false; await ViewModel.RetryMineAsync(); Render(); if (sender is Button retry) retry.IsEnabled = true; }
    private async void RetryDiscovery_Click(object sender, RoutedEventArgs e) { if (sender is Button button) button.IsEnabled = false; await ViewModel.RetryDiscoveryAsync(); Render(); if (sender is Button retry) retry.IsEnabled = true; }
    private void BrowseDiscover_Click(object sender, RoutedEventArgs e) { _activeTab = "discover"; UpdateTabState(); }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        StopDownloadRefresh();
        ViewModel.PropertyChanged += RequestsStateChanged;
        _downloadRefreshOwner = new CancellationTokenSource();
        _downloadRefreshTimer.Start();
        await ViewModel.LoadCommand.ExecuteAsync(null);
        if (_renderedDataVersion != ViewModel.DataVersion)
        {
            Render();
            _renderedDataVersion = ViewModel.DataVersion;
        }
    }

    private void RequestsStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (IsLoaded && !_downloadTickInProgress && (e.PropertyName is nameof(RequestsViewModel.DataVersion) or nameof(RequestsViewModel.IsLoading))) Render();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        StopDownloadRefresh();
        ViewModel.CancelSearch();
        base.OnNavigatedFrom(e);
    }

    private void StopDownloadRefresh()
    {
        ViewModel.PropertyChanged -= RequestsStateChanged;
        _downloadRefreshTimer.Stop();
        _downloadRefreshOwner?.Cancel();
        _downloadRefreshOwner?.Dispose();
        _downloadRefreshOwner = null;
    }

    private async void DownloadRefresh_Tick(object? sender, object e)
    {
        if (_downloadTickInProgress || _downloadRefreshOwner is not { } owner) return;
        var previousVersion = ViewModel.DataVersion;
        _downloadTickInProgress = true;
        try { await ViewModel.RefreshDownloadsAsync(owner.Token); }
        finally { _downloadTickInProgress = false; }
        if (!ReferenceEquals(owner, _downloadRefreshOwner) || !IsLoaded || ViewModel.DataVersion == previousVersion) return;
        BuildMyRequests();
        _renderedDataVersion = ViewModel.DataVersion;
        UpdateTabState();
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
        if (SearchBox.Text.Trim().Length >= 2)
        {
            App.Services.GetRequiredService<NavigationService>().Navigate<SearchPage>(SearchBox.Text.Trim());
            return;
        }
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

    private void RequestTab_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right
            or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End)) return;
        var target = e.Key switch
        {
            Windows.System.VirtualKey.Home => DiscoverTabButton,
            Windows.System.VirtualKey.End => YoursTabButton,
            _ => ReferenceEquals(sender, DiscoverTabButton) ? YoursTabButton : DiscoverTabButton,
        };
        _activeTab = ReferenceEquals(target, YoursTabButton) ? "yours" : "discover";
        UpdateTabState();
        target.Focus(FocusState.Keyboard);
        e.Handled = true;
    }

    private void UpdateTabState()
    {
        var yours = _activeTab == "yours";
        var busy = ViewModel.IsLoading || ViewModel.IsSearching;
        LoadingSkeleton.Visibility = busy && !yours ? Visibility.Visible : Visibility.Collapsed;
        _mineLoadingSkeleton.Visibility = (busy || ViewModel.IsLoadingMine) && yours ? Visibility.Visible : Visibility.Collapsed;
        MyRequestsSection.Visibility = yours && !busy && !ViewModel.IsLoadingMine ? Visibility.Visible : Visibility.Collapsed;
        DiscoverySection.Visibility = !yours && !busy ? Visibility.Visible : Visibility.Collapsed;
        DiscoverTabButton.Foreground = Brush(yours ? "SecondaryTextBrush" : "PrimaryTextBrush");
        YoursTabButton.Foreground = Brush(yours ? "PrimaryTextBrush" : "SecondaryTextBrush");
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
        MyRequestsList.Children.Clear(); MineSummaryPanel.Children.Clear(); StatusGuide.Visibility = Visibility.Visible;
        MineErrorPanel.Visibility = string.IsNullOrWhiteSpace(ViewModel.MineError) ? Visibility.Collapsed : Visibility.Visible;
        MineEmptyPanel.Visibility = ViewModel.MyRequests.Count == 0 && string.IsNullOrWhiteSpace(ViewModel.MineError) ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(ViewModel.MineError)) return;
        var groups = new[]
        {
            ("Needs attention", ViewModel.MyRequests.Where(r => RequestDisplayLabel(r) is "Declined" or "Failed")),
            ("On the way", ViewModel.MyRequests.Where(r => RequestDisplayLabel(r) is "Pending" or "Approved" or "Processing" or "Partially available")),
            ("In your library", ViewModel.MyRequests.Where(r => RequestDisplayLabel(r) == "Available")),
            ("Cancelled", ViewModel.MyRequests.Where(r => RequestDisplayLabel(r) == "Cancelled")),
        };
        foreach (var (title, items) in groups)
        {
            var values = items.ToList(); if (values.Count == 0) continue;
            var region = new StackPanel { Spacing = 4 };
            AutomationProperties.SetName(region, title);
            var groupHeading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            groupHeading.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, LineHeight = 28,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
            groupHeading.Children.Add(new TextBlock { Text = values.Count.ToString(), FontSize = 14, Foreground = Brush("SecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center });
            region.Children.Add(groupHeading);
            var rows = new StackPanel();
            foreach (var request in values)
            {
                var row = BuildRequestRow(request);
                if (rows.Children.Count > 0 && Brush("BorderBrush") is SolidColorBrush divider)
                {
                    row.BorderBrush = new SolidColorBrush(divider.Color) { Opacity = .6 };
                    row.BorderThickness = new Thickness(0, 1, 0, 0);
                }
                rows.Children.Add(row);
            }
            region.Children.Add(rows);
            MyRequestsList.Children.Add(region);
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
        if (ViewModel.IsLoadingDiscovery)
        {
            for (var row = 0; row < 2; row++)
            {
                var section = new StackPanel { Spacing = 20 };
                section.Children.Add(new SiloPlayer.Controls.SkeletonText { Width = 150, Height = 20, HorizontalAlignment = HorizontalAlignment.Left });
                var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
                for (var i = 0; i < 6; i++) cards.Children.Add(new SiloPlayer.Controls.SkeletonPoster { Width = _requestCardWidth, Height = _requestCardWidth * 1.5 });
                section.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = cards });
                DiscoveryList.Children.Add(section);
            }
            DiscoveryErrorPanel.Visibility = Visibility.Collapsed;
            return;
        }
        DiscoveryErrorPanel.Visibility = string.IsNullOrWhiteSpace(ViewModel.DiscoveryError)
            ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(ViewModel.DiscoveryError)) return;

        foreach (var section in ViewModel.DiscoverySections)
        {
            if (section.Results.Count == 0) continue;
            var panel = new StackPanel { Spacing = 20 };
            var heading = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
            heading.Children.Add(new TextBlock
            {
                Text = section.Title,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("PrimaryTextBrush"),
            });
            var explore = new Button { Content = new TextBlock { Text = "EXPLORE ALL", FontSize = 11, FontWeight = FontWeights.SemiBold, CharacterSpacing = 140 },
                Height = 24, MinHeight = 0, Padding = new Thickness(0), Foreground = Brush("SecondaryTextBrush"),
                Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                Visibility = section.TotalPages > 1 ? Visibility.Visible : Visibility.Collapsed };
            explore.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<RequestBrowsePage>(new RequestBrowseNavigation("section", section.Key));
            AutomationProperties.SetName(explore, $"Explore {section.Title}");
            Grid.SetColumn(explore, 1); heading.Children.Add(explore); panel.Children.Add(heading);

            var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            foreach (var item in section.Results)
                cards.Children.Add(BuildMediaPosterCard(item));
            panel.Children.Add(new SiloPlayer.Controls.CarouselRail(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = cards,
            }));

            DiscoveryList.Children.Add(panel);
        }
    }

    private FrameworkElement BuildMediaPosterCard(RequestMediaResult result)
    {
        return SiloPlayer.Controls.ExternalTitleCard.Build(result, _requestCardWidth,
            request: result.Request.Requestable ? async () =>
            {
                await ViewModel.SubmitRequestCommand.ExecuteAsync(result);
                if (ViewModel.ErrorMessage is { Length: > 0 } error) throw new InvalidOperationException(error);
                Render();
            } : null,
            watchlist: ViewModel.Features.WatchlistTitlesSupported ? async () =>
            {
                var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.RequestsApi>();
                if (result.InWatchlist == true) await api.RemoveWatchlistTitleAsync(result.MediaType, result.TmdbId);
                else
                {
                    var entry = await api.AddWatchlistTitleAsync(result.MediaType, result.TmdbId);
                    if (ViewModel.Features.WatchlistRequests && !string.IsNullOrEmpty(entry.Request.Reason) && !entry.Request.Requestable && string.IsNullOrEmpty(entry.Request.Status))
                        App.Services.GetRequiredService<SiloPlayer.Services.ToastService>().Info("Added to watchlist. " + SiloPlayer.Core.Services.RequestViewerPolicy.Reason(entry.Request.Reason));
                }
                result.InWatchlist = result.InWatchlist != true;
                App.Services.GetRequiredService<WatchlistViewModel>().InvalidateExternalTitles();
            } : null);
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
        foreach (var region in BrandsSection.Children.OfType<StackPanel>()) region.Visibility = Visibility.Visible;
        BrandsSection.Visibility = Visibility.Visible;
    }

    private void BuildBrandRow(StackPanel panel, string kind, IEnumerable<DiscoverBrandCard> cards)
    {
        panel.Children.Clear();
        if (ViewModel.IsBrandLoading(kind))
        {
            for (var i = 0; i < 4; i++) panel.Children.Add(new SiloPlayer.Controls.SkeletonBox { Width = ActualWidth < 640 ? 208 : 256, Height = ActualWidth < 640 ? 112 : 128, CornerRadius = new CornerRadius(12) });
            return;
        }
        if (ViewModel.BrandErrors.TryGetValue(kind, out var error))
        {
            var recovery = new StackPanel { Spacing = 12, Padding = new Thickness(0, 24, 0, 24) };
            recovery.Children.Add(new TextBlock { Text = error, FontSize = 14, Foreground = Brush("SecondaryTextBrush") });
            var retry = new Button { Content = "Retry", HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
            retry.Click += async (_, _) => { retry.IsEnabled = false; await ViewModel.RetryBrandAsync(kind); BuildBrands(); };
            recovery.Children.Add(retry); panel.Children.Add(recovery); return;
        }
        foreach (var card in cards)
        {
            var button = new Button
            {
                Tag = new RequestBrowseNavigation(kind, card.Slug),
                Width = ActualWidth < 640 ? 208 : 256,
                Height = ActualWidth < 640 ? 112 : 128,
                Padding = new Thickness(ActualWidth < 640 ? 24 : 32),
                CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Brush("BorderBrush"),
                Background = kind == "genre" ? GenreGradient(card) : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 31, 41, 55)),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            if (!string.IsNullOrWhiteSpace(card.LogoUrl))
                button.Content = new Image
                {
                    Source = (ImageSource)RemoteImageConverter.Convert(
                        card.LogoUrl,
                        typeof(ImageSource),
                        null!,
                        string.Empty),
                    Stretch = Stretch.Uniform,
                };
            else
                button.Content = new TextBlock { Text = card.DisplayName, FontSize = 16, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            button.Click += Brand_Click;
            panel.Children.Add(button);
        }
    }

    private void Brand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RequestBrowseNavigation nav })
            App.Services.GetRequiredService<NavigationService>().Navigate<RequestBrowsePage>(nav);
    }

    private static Brush GenreGradient(DiscoverBrandCard card)
    {
        static Windows.UI.Color Parse(string? value, string fallback)
        {
            var hex = value?.Trim().TrimStart('#'); if (hex?.Length != 6) hex = fallback;
            try { return Microsoft.UI.ColorHelper.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)); }
            catch { return Microsoft.UI.ColorHelper.FromArgb(255, 71, 85, 105); }
        }
        return new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1), GradientStops = { new GradientStop { Color = Parse(card.GradientFrom, "475569"), Offset = 0 }, new GradientStop { Color = Parse(card.GradientTo, "0f172a"), Offset = 1 } } };
    }

    private Border BuildRequestRow(MediaRequest request)
    {
        var row = new Grid { Padding = new Thickness(0,12,0,12), ColumnSpacing = ActualWidth < 640 ? 12 : 16 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(48) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var poster = new Image { Width = 48, Height = 72, Stretch = Stretch.UniformToFill };
        if (!string.IsNullOrWhiteSpace(request.PosterPath)) poster.Source = (ImageSource)RemoteImageConverter.Convert($"https://image.tmdb.org/t/p/w92{request.PosterPath}", typeof(ImageSource), null!, "");
        UIElement posterContent = poster;
        if (string.IsNullOrWhiteSpace(request.PosterPath))
            posterContent = new Border { Width = 48, Height = 72, Background = Brush("CardBackgroundBrush"), CornerRadius = new CornerRadius(6),
                Child = SiloPlayer.Controls.WebUiIcon.Create(request.MediaType == "series" ? "tv" : "film", 16, Brush("SecondaryTextBrush")) };
        var openPoster = new Button { Content = posterContent, Padding = new Thickness(0), Width = 48, Height = 72, VerticalAlignment = VerticalAlignment.Top, Style = (Style)Application.Current.Resources["PosterHitTargetButtonStyle"] };
        openPoster.Click += (_,_) => App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(request.MediaType,request.TmdbId)); row.Children.Add(openPoster);
        var content = new StackPanel { Spacing = 4, VerticalAlignment = ActualWidth < 640 ? VerticalAlignment.Top : VerticalAlignment.Center };
        var titleRow = new SiloPlayer.Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 };
        var title = new HyperlinkButton { Content = new TextBlock { Text = request.Title, FontSize = 14, LineHeight = 20,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis }, Height = 20, MinHeight = 0,
            Padding = new Thickness(0), Foreground = Brush("PrimaryTextBrush") };
        title.Click += (_,_) => App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(request.MediaType,request.TmdbId));
        titleRow.Children.Add(title); titleRow.Children.Add(SiloPlayer.Controls.ExternalTitleCard.StatusBadge(RequestDisplayLabel(request)));
        content.Children.Add(titleRow);
        var details = new List<string> { FormatMediaType(request.MediaType) };
        if (request.Year is >0) details.Add(request.Year.ToString()!);
        if (request.Seasons?.Count >0) details.Add(FormatRequestedSeasons(request.Seasons));
        var progress = RequestDisplayLabel(request) == "Partially available" ? SiloPlayer.Core.Services.RequestViewerPolicy.SeasonProgressLabel(request.SeasonProgress) : "";
        if (progress.Length>0) details.Add(progress);
        content.Children.Add(new TextBlock { Text = string.Join(" · ", details), FontSize=12, LineHeight=16, LineStackingStrategy=LineStackingStrategy.BlockLineHeight, Foreground=Brush("SecondaryTextBrush"), TextWrapping=TextWrapping.Wrap });
        if (request.Download != null) content.Children.Add(SiloPlayer.Controls.RequestDownloadProgress.Build(request.Download, 256));
        var updated = LastChange(request);
        var dates = new TextBlock { Text = "Requested " + FormatDate(request.CreatedAt) + (updated == null ? "" : " · Updated " + updated), FontSize=12, LineHeight=16, LineStackingStrategy=LineStackingStrategy.BlockLineHeight, Foreground=Brush("SecondaryTextBrush"), TextWrapping=TextWrapping.Wrap };
        ToolTipService.SetToolTip(dates, "Requested " + FormatDate(request.CreatedAt) + (updated == null ? "" : " · Updated " + FormatDate(request.UpdatedAt)));
        content.Children.Add(dates);
        if (!string.IsNullOrEmpty(request.LastError) || !string.IsNullOrEmpty(request.OutcomeReason)) content.Children.Add(new TextBlock { Text = request.LastError.Length>0 ? request.LastError : "Reason: " + request.OutcomeReason, FontSize=12, LineHeight=16.5, LineStackingStrategy=LineStackingStrategy.BlockLineHeight, Foreground=Brush(request.LastError.Length>0 ? "ErrorBrush" : "SecondaryTextBrush"), TextWrapping=TextWrapping.Wrap });
        var actions = new SiloPlayer.Controls.WrapPanel { HorizontalSpacing=8, VerticalSpacing=8, Margin=new Thickness(0,8,0,0) };
        if (request.LibraryContentId is { Length:>0 } id)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            label.Children.Add(SiloPlayer.Controls.WebUiIcon.Create("library", 14));
            label.Children.Add(new TextBlock { Text = "Open in library", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var library = new Button { Content = label, FontSize = 14, Height = 32, MinHeight = 0,
                Padding = new Thickness(12, 0, 12, 0), Style = (Style)Application.Current.Resources["OutlineButtonStyle"] };
            AutomationProperties.SetName(library, "Open " + request.Title + " in library");
            library.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(id);
            actions.Children.Add(library);
        }
        if (SiloPlayer.Core.Services.RequestViewerPolicy.CanCancel(request))
        {
            var cancel = new Button { Content = "Cancel request", Tag = request, FontSize = 14, Height = 32, MinHeight = 0,
                Padding = new Thickness(12, 0, 12, 0), Style = (Style)Application.Current.Resources["OutlineButtonStyle"] };
            AutomationProperties.SetName(cancel, "Cancel request for " + request.Title);
            cancel.Click += CancelRequest_Click; actions.Children.Add(cancel);
        }
        if (actions.Children.Count > 0)
        {
            if (ActualWidth < 640) content.Children.Add(actions);
            else { actions.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(actions,2); row.Children.Add(actions); }
        }
        Grid.SetColumn(content,1); row.Children.Add(content);
        return new Border { Child=row };
    }
    private FrameworkElement BuildRequestPosterCard(MediaRequest request)
    {
        var panel = new Grid { Width = _requestCardWidth, RowSpacing = 6 };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_requestCardWidth * 1.5) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var image = new Image { Width = _requestCardWidth, Height = _requestCardWidth * 1.5, Stretch = Stretch.UniformToFill };
        if (!string.IsNullOrWhiteSpace(request.PosterPath))
            image.Source = (ImageSource)RemoteImageConverter.Convert(
                $"https://image.tmdb.org/t/p/w342{request.PosterPath}",
                typeof(ImageSource),
                null!,
                string.Empty);
        var poster = new Grid { Width = _requestCardWidth, Height = _requestCardWidth * 1.5 };
        poster.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = Brush("CardBackgroundBrush"), Child = image });
        var failed = request.Outcome is "declined" or "cancelled" or "failed";
        var label = RequestDisplayLabel(request);
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
        if (label == "Available")
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
        var details = new[] { request.OutcomeReason, SiloPlayer.Core.Services.RequestViewerPolicy.DownloadLabel(request.Download), SiloPlayer.Core.Services.RequestViewerPolicy.SeasonProgressLabel(request.SeasonProgress), request.Source == "watchlist" ? "From watchlist" : null, request.LastError }.Where(s => !string.IsNullOrWhiteSpace(s));
        var detailText = string.Join(" · ", details);
        if (!string.IsNullOrWhiteSpace(detailText))
        {
            var error = new TextBlock { Text = detailText, FontSize = 11, Foreground = Brush(failed ? "ErrorBrush" : "SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(error, 3);
            panel.Children.Add(error);
        }

        var open = new Button
        {
            Tag = new RequestDetailNavigation(request.MediaType, request.TmdbId),
            Style = (Style)Application.Current.Resources["PosterHitTargetButtonStyle"],
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

    private static string RequestDisplayLabel(MediaRequest request) => SiloPlayer.Core.Services.RequestViewerPolicy.Label(request.Status, request.Outcome, request.State);

    private static string FormatRequestedSeasons(IReadOnlyList<int> seasons)
    {
        var sorted = seasons.Distinct().Order().ToArray();
        var runs = new List<string>();
        for (var i = 0; i < sorted.Length; i++)
        {
            var first = sorted[i];
            while (i + 1 < sorted.Length && sorted[i + 1] == sorted[i] + 1) i++;
            runs.Add(first == sorted[i] ? first.ToString() : $"{first}–{sorted[i]}");
        }
        return $"{(sorted.Length == 1 ? "Season" : "Seasons")} {string.Join(", ", runs)}";
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
        var confirmation = new ContentDialog
        {
            Title = "Cancel this request?", Content = $"Your request for \"{request.Title}\" will be withdrawn before it is sent to the download automation.",
            PrimaryButtonText = "Cancel request", CloseButtonText = "Keep request", XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
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

    private static string? LastChange(MediaRequest request)
    {
        if (!DateTimeOffset.TryParse(request.CreatedAt, out var created) || !DateTimeOffset.TryParse(request.UpdatedAt, out var updated) || updated - created < TimeSpan.FromMinutes(1)) return null;
        var age = DateTimeOffset.UtcNow - updated;
        var minutes = Math.Floor(age.TotalMinutes + .5);
        if (minutes < 1) return "just now";
        if (minutes < 60) return $"{minutes:0}m ago";
        var hours = Math.Floor(age.TotalHours + .5);
        if (hours < 24) return $"{hours:0}h ago";
        var days = Math.Floor(age.TotalDays + .5);
        return days < 7 ? $"{days:0}d ago" : FormatDate(request.UpdatedAt);
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;

        var gutter = width < 640 ? 16d
            : width < 1024 ? 24d
            : width < 1280 ? 40d
            : 48d;
        PageContent.Padding = new Thickness(gutter, 24, gutter, 40);
        PageTitleText.FontSize = width < 640 ? 24 : 30;
        PageTitleText.LineHeight = width < 640 ? 32 : 36;
        PageTitleText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;

        var compact = width < 640;
        RequestsHeader.RowSpacing = compact ? 16 : 0;
        _mineHeading.RowSpacing = compact ? 8 : 0;
        if (_mineHelp != null)
        {
            Grid.SetRow(_mineHelp, compact ? 1 : 0); Grid.SetColumn(_mineHelp, compact ? 0 : 1);
            _mineHelp.Margin = compact ? new Thickness(-8, 0, 0, 0) : new Thickness(0, 0, -8, 0);
        }
        RequestsHeader.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(288);
        Grid.SetRow(SearchBarGrid, compact ? 1 : 0); Grid.SetColumn(SearchBarGrid, compact ? 0 : 1);
        Grid.SetColumnSpan(SearchBarGrid, compact ? 2 : 1);
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

        StatusGuide.Width = Math.Min(286, Math.Max(120, width - 66));
        MyRequestsSection.MaxWidth = Math.Max(280, 1024 - gutter * 2);
        MyRequestsSection.Width = Math.Min(width - gutter * 2, MyRequestsSection.MaxWidth);
        MyRequestsSection.HorizontalAlignment = HorizontalAlignment.Left;

        var bucket = width < 640 ? 0 : width < 1024 ? 1 : 2;
        UpdateCardWidth(width);
        if (_layoutBucket != bucket)
        {
            var hadLayout = _layoutBucket >= 0;
            _layoutBucket = bucket;
            if (hadLayout && IsLoaded && _renderedDataVersion >= 0)
                Render();
        }
    }
    private void UpdateCardWidth(double width)
    {
        var bucket = width < 640 ? 0 : width < 1024 ? 1 : 2;
        _requestCardWidth = App.Services.GetService<SiloPlayer.Services.UICustomizationService>()?.CardPresentation.PosterSize switch
        {
            "compact" => bucket switch { 0 => 120, 1 => 140, _ => 160 },
            "large" => bucket switch { 0 => 170, 1 => 195, _ => 220 },
            _ => bucket switch { 0 => 140, 1 => 160, _ => 185 },
        };
    }
}
