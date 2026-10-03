using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.ViewModels;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SiloPlayer.Views;

public sealed partial class WatchlistPage : Page
{
    public WatchlistViewModel ViewModel { get; }
    private bool _ascending = true;
    private string _sortField = "title";

    public WatchlistPage()
    {
        ViewModel = App.Services.GetRequiredService<WatchlistViewModel>();
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        PosterRepeater.ItemsSource = ViewModel.Items;
        ExternalRepeater.ItemsSource = ViewModel.ExternalTitles;

        ViewModel.Items.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdateCounts);
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
        ApplySort();
        UpdateCounts();
        if (WatchlistScope.SelectedIndex == 1) await LoadExternalAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) { ViewModel.CancelExternalLoad(); base.OnNavigatedFrom(e); }

    private async void WatchlistScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExternalRepeater == null || PosterRepeater == null) return;
        var external = WatchlistScope.SelectedIndex == 1;
        PosterRepeater.Visibility = external ? Visibility.Collapsed : Visibility.Visible;
        ExternalRepeater.Visibility = external ? Visibility.Visible : Visibility.Collapsed;
        if (external) await LoadExternalAsync();
        else { ViewModel.CancelExternalLoad(); ExternalStatusText.Visibility = Visibility.Collapsed; UpdateCounts(); }
    }

    private async Task LoadExternalAsync()
    {
        ExternalStatusText.Text = "Loading titles…"; ExternalStatusText.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        await ViewModel.LoadExternalTitlesAsync();
        await App.Services.GetRequiredService<CardOverlayService>().EnsureLoadedAsync();
        if (WatchlistScope.SelectedIndex != 1) return;
        ExternalStatusText.Text = ViewModel.ExternalTitlesError ?? (!ViewModel.ExternalTitlesSupported ? "This server doesn't currently support watchlist titles outside your library." : ViewModel.ExternalTitles.Count == 0 ? "No titles outside your library yet. Add titles from Search or Discover." : "");
        ExternalStatusText.Visibility = string.IsNullOrEmpty(ExternalStatusText.Text) ? Visibility.Collapsed : Visibility.Visible;
        ApplySort(); UpdateCounts();
    }

    private void ExternalRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not StackPanel panel || args.Index >= ViewModel.ExternalTitles.Count) return;
        var title = ViewModel.ExternalTitles[args.Index]; panel.Children.Clear();
        var status = RequestViewerPolicy.WatchlistPresentation(title);
        var image = new Image { Width = 160, Height = 240, Stretch = Stretch.UniformToFill };
        if (title.PosterUrl is string url) image.Source = (ImageSource)new SiloPlayer.Converters.UrlToImageSourceConverter().Convert(url, typeof(ImageSource), null!, "");
        var contents = new StackPanel { Spacing = 6 };
        var poster = new Grid { Width = 160, Height = 240 };
        poster.Children.Add(new Border { CornerRadius = new CornerRadius(8), Child = image });
        var overlays = App.Services.GetRequiredService<CardOverlayService>();
        var prefs = overlays.GetPrefs();
        if (prefs?.TryGetValue("request_status", out var badgeConfig) == true && badgeConfig.Enabled)
        {
            var badge = SiloPlayer.Controls.PosterCard.BuildBadge(status.Badge, "request_status", badgeConfig, overlays.Preset);
            badge.Margin = new Thickness(7);
            badge.HorizontalAlignment = badgeConfig.Position is OverlayPosition.TopRight or OverlayPosition.BottomRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            badge.VerticalAlignment = badgeConfig.Position is OverlayPosition.BottomLeft or OverlayPosition.BottomRight ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            poster.Children.Add(badge);
            if (RequestViewerPolicy.DownloadPercent(title.Request.Download) is int percent)
                poster.Children.Add(new ProgressBar { Value = Math.Clamp(percent, 0, 100), Maximum = 100, Height = 3, VerticalAlignment = VerticalAlignment.Bottom });
        }
        contents.Children.Add(poster);
        contents.Children.Add(new TextBlock { Text = title.Title, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        contents.Children.Add(new TextBlock { Text = title.DisplayMeta, FontSize = 12, Opacity = 0.65 });
        contents.Children.Add(new TextBlock { Text = status.Caption, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var open = new Button { Content = contents, Padding = new Thickness(0), Style = (Style)Application.Current.Resources["GhostButtonStyle"], HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, $"Open {title.Title}");
        open.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(title.MediaType, title.TmdbId));
        panel.Children.Add(open);
        if (title.Status is "needs_review" or "removed")
        {
            var find = new Button { Content = "Find it", HorizontalAlignment = HorizontalAlignment.Stretch };
            find.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<SearchPage>(title.Title);
            panel.Children.Add(find);
        }
        var remove = new Button { Content = "Remove", HorizontalAlignment = HorizontalAlignment.Stretch };
        remove.Click += async (_, _) =>
        {
            remove.IsEnabled = false;
            try { await ViewModel.RemoveExternalTitleAsync(title); UpdateCounts(); }
            catch (Exception ex) { remove.IsEnabled = true; App.Services.GetRequiredService<ToastService>().Error(ex.Message); }
        };
        panel.Children.Add(remove);
    }

    private async void ContentScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (WatchlistScope.SelectedIndex == 1 || e.IsIntermediate || !ViewModel.HasMore || ViewModel.IsLoadingMore)
            return;

        if (ContentScrollViewer.VerticalOffset < ContentScrollViewer.ScrollableHeight - 640)
            return;

        var previousCount = ViewModel.Items.Count;
        await ViewModel.LoadMoreCommand.ExecuteAsync(null);
        if (ViewModel.Items.Count != previousCount)
            ApplySort();
    }

    private void UpdateCounts()
    {
        int count = WatchlistScope.SelectedIndex == 1 ? ViewModel.ExternalTitles.Count : ViewModel.Items.Count;
        bool hasItems = count > 0 && !ViewModel.IsLoading;

        EmptyState.Visibility = WatchlistScope.SelectedIndex != 1 && count == 0 && !ViewModel.IsLoading
            ? Visibility.Visible : Visibility.Collapsed;

        CountPanel.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        SortBar.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

        if (hasItems)
        {
            ItemCountText.Text = count.ToString();
            ItemCountLabel.Text = count == 1 ? "title" : "titles";
        }
    }

    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortComboBox.SelectedItem is ComboBoxItem item && item.Tag is string field)
        {
            _sortField = field;
            ApplySort();
        }
    }

    private void OrderButton_Click(object sender, RoutedEventArgs e)
    {
        _ascending = !_ascending;
        OrderIcon.Glyph = _ascending ? "\uE74A" : "\uE74B";
        ApplySort();
    }

    private void ApplySort()
    {
        if (WatchlistScope?.SelectedIndex == 1)
        {
            IEnumerable<WatchlistTitle> titles = _sortField switch
            {
                "soonest" => ViewModel.ExternalTitles.OrderBy(t => RequestViewerPolicy.WatchlistPresentation(t).Rank).ThenBy(t => DateOnly.TryParse(t.ReleaseDate, out var date) ? date : DateOnly.MaxValue).ThenByDescending(t => ParseAddedAt(t.AddedAt)),
                "year" => _ascending ? ViewModel.ExternalTitles.OrderBy(t => t.Year) : ViewModel.ExternalTitles.OrderByDescending(t => t.Year),
                "added_at" => _ascending ? ViewModel.ExternalTitles.OrderBy(t => ParseAddedAt(t.AddedAt)) : ViewModel.ExternalTitles.OrderByDescending(t => ParseAddedAt(t.AddedAt)),
                _ => _ascending ? ViewModel.ExternalTitles.OrderBy(t => t.Title) : ViewModel.ExternalTitles.OrderByDescending(t => t.Title)
            };
            var sortedTitles = titles.ToArray();
            ViewModel.ExternalTitles.Clear(); foreach (var title in sortedTitles) ViewModel.ExternalTitles.Add(title);
            return;
        }
        if (ViewModel.Items.Count == 0) return;

        var sorted = _sortField switch
        {
            "year" => _ascending
                ? ViewModel.Items.OrderBy(i => i.Year).ToList()
                : ViewModel.Items.OrderByDescending(i => i.Year).ToList(),
            "added_at" => _ascending
                ? ViewModel.Items.OrderBy(i => ParseAddedAt(i.AddedAt)).ToList()
                : ViewModel.Items.OrderByDescending(i => ParseAddedAt(i.AddedAt)).ToList(),
            _ => _ascending
                ? ViewModel.Items.OrderBy(i => i.Title).ToList()
                : ViewModel.Items.OrderByDescending(i => i.Title).ToList(),
        };

        ViewModel.Items.Clear();
        foreach (var item in sorted)
            ViewModel.Items.Add(item);
    }

    private static DateTimeOffset ParseAddedAt(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;
}
