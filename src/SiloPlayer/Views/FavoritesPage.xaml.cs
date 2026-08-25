using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class FavoritesPage : Page
{
    public FavoritesViewModel ViewModel { get; }
    private bool _ascending = true;
    private string _sortField = "title";
    private readonly List<string> _serverOrder = [];

    public FavoritesPage()
    {
        ViewModel = App.Services.GetRequiredService<FavoritesViewModel>();
        this.InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        PosterRepeater.ItemsSource = ViewModel.Items;

        ViewModel.Items.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdateCounts);
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
        CaptureServerOrder(replace: true);
        ApplySort();
        UpdateCounts();
    }

    private async void ContentScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate || !ViewModel.HasMore || ViewModel.IsLoadingMore)
            return;

        if (ContentScrollViewer.VerticalOffset < ContentScrollViewer.ScrollableHeight - 640)
            return;

        var previousCount = ViewModel.Items.Count;
        await ViewModel.LoadMoreCommand.ExecuteAsync(null);
        if (ViewModel.Items.Count != previousCount)
        {
            CaptureServerOrder(replace: false);
            ApplySort();
        }
    }

    private void UpdateCounts()
    {
        int count = ViewModel.Items.Count;
        bool hasItems = count > 0 && !ViewModel.IsLoading;

        EmptyState.Visibility = count == 0 && !ViewModel.IsLoading
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
        OrderIcon.Glyph = _ascending ? "\uE74A" : "\uE74B"; // SortUp : SortDown
        ApplySort();
    }

    private void ApplySort()
    {
        if (ViewModel.Items.Count == 0) return;

        var sorted = _sortField switch
        {
            "year" => _ascending
                ? ViewModel.Items.OrderBy(i => i.Year).ToList()
                : ViewModel.Items.OrderByDescending(i => i.Year).ToList(),
            "added_at" => _ascending
                ? ViewModel.Items.OrderBy(ServerOrderIndex).ToList()
                : ViewModel.Items.OrderByDescending(ServerOrderIndex).ToList(),
            _ => _ascending
                ? ViewModel.Items.OrderBy(i => i.Title).ToList()
                : ViewModel.Items.OrderByDescending(i => i.Title).ToList(),
        };

        ViewModel.Items.Clear();
        foreach (var item in sorted)
            ViewModel.Items.Add(item);
    }

    private void CaptureServerOrder(bool replace)
    {
        if (replace)
            _serverOrder.Clear();

        var known = _serverOrder.ToHashSet(StringComparer.Ordinal);
        foreach (var item in ViewModel.Items)
        {
            if (known.Add(item.ContentId))
                _serverOrder.Add(item.ContentId);
        }
    }

    private int ServerOrderIndex(MediaItem item)
    {
        var index = _serverOrder.IndexOf(item.ContentId);
        return index >= 0 ? index : int.MaxValue;
    }
}
