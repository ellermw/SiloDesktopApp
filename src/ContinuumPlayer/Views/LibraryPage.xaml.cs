using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }
    private bool _suppressFilterEvents;

    public LibraryPage()
    {
        ViewModel = App.Services.GetRequiredService<LibraryViewModel>();
        this.InitializeComponent();

        PosterRepeater.ItemsSource = ViewModel.Items;

        // Set default combo selections
        _suppressFilterEvents = true;
        SortComboBox.SelectedIndex = 0;   // title
        OrderComboBox.SelectedIndex = 0;  // asc
        _suppressFilterEvents = false;

        // Watch genre list changes to populate combo
        ViewModel.Genres.CollectionChanged += (_, _) => UpdateGenreCombo();

        // Watch total count changes
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.TotalCount))
            {
                CountText.Text = ViewModel.TotalCount > 0
                    ? $"{ViewModel.TotalCount} items"
                    : "";
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

            await ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private void UpdateGenreCombo()
    {
        _suppressFilterEvents = true;
        GenreComboBox.Items.Clear();

        // Add "All Genres" as first option
        GenreComboBox.Items.Add(new ComboBoxItem { Content = "All Genres", Tag = "" });

        foreach (var genre in ViewModel.Genres)
        {
            if (!string.IsNullOrEmpty(genre))
            {
                GenreComboBox.Items.Add(new ComboBoxItem { Content = genre, Tag = genre });
            }
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
        }
    }

    private async void OrderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (OrderComboBox.SelectedItem is ComboBoxItem item && item.Tag is string order)
        {
            ViewModel.SelectedOrder = order;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        }
    }

    private async void GenreComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (GenreComboBox.SelectedItem is ComboBoxItem item && item.Tag is string genre)
        {
            ViewModel.SelectedGenre = string.IsNullOrEmpty(genre) ? null : genre;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        }
    }

    private async void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;

        var scrollViewer = (ScrollViewer)sender;
        var verticalOffset = scrollViewer.VerticalOffset;
        var maxOffset = scrollViewer.ScrollableHeight;

        // Load more when within 200px of bottom
        if (maxOffset > 0 && verticalOffset >= maxOffset - 200)
        {
            if (ViewModel.HasMore && !ViewModel.IsLoading)
            {
                await ViewModel.LoadMoreCommand.ExecuteAsync(null);
            }
        }
    }
}
