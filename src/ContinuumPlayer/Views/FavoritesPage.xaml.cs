using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class FavoritesPage : Page
{
    public FavoritesViewModel ViewModel { get; }
    private bool _ascending = true;
    private string _sortField = "title";

    public FavoritesPage()
    {
        ViewModel = App.Services.GetRequiredService<FavoritesViewModel>();
        this.InitializeComponent();

        PosterRepeater.ItemsSource = ViewModel.Items;

        ViewModel.Items.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdateCounts);
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
        UpdateCounts();
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
                ? ViewModel.Items.ToList()     // preserve server order (roughly added_at)
                : ViewModel.Items.Reverse().ToList(),
            _ => _ascending
                ? ViewModel.Items.OrderBy(i => i.Title).ToList()
                : ViewModel.Items.OrderByDescending(i => i.Title).ToList(),
        };

        ViewModel.Items.Clear();
        foreach (var item in sorted)
            ViewModel.Items.Add(item);
    }
}
