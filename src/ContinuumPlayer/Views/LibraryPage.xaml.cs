using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
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

        BuildAlphabetStrip();

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
        var scrollViewer = (ScrollViewer)sender;
        var verticalOffset = scrollViewer.VerticalOffset;
        var maxOffset = scrollViewer.ScrollableHeight;

        // Load more when within 500px of bottom
        if (maxOffset > 0 && verticalOffset >= maxOffset - 500)
        {
            if (ViewModel.HasMore && !ViewModel.IsLoading)
            {
                await ViewModel.LoadMoreCommand.ExecuteAsync(null);
            }
        }
    }

    private void BuildAlphabetStrip()
    {
        AlphabetStripPanel.Children.Clear();
        var letters = new[] { "#" }.Concat(Enumerable.Range('A', 26).Select(c => ((char)c).ToString()));
        foreach (var letter in letters)
        {
            var tb = new TextBlock
            {
                Text = letter,
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(8, 2, 8, 2),
                TextAlignment = TextAlignment.Center
            };
            tb.Tapped += AlphabetLetter_Tapped;
            tb.PointerEntered += AlphabetLetter_PointerEntered;
            tb.PointerExited += AlphabetLetter_PointerExited;
            AlphabetStripPanel.Children.Add(tb);
        }
    }

    private async void AlphabetLetter_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is TextBlock tb)
        {
            await ViewModel.JumpToLetterCommand.ExecuteAsync(tb.Text);
            ContentScrollViewer.ChangeView(null, 0, null);
        }
    }

    private void AlphabetLetter_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is TextBlock tb)
            tb.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
    }

    private void AlphabetLetter_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is TextBlock tb)
            tb.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton || clickedButton.Tag is not string tag)
            return;

        // Reset all tabs
        var tabs = new[] { RecommendedTab, LibraryTab, CollectionsTab };
        foreach (var tab in tabs)
        {
            tab.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            tab.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
            tab.BorderThickness = new Thickness(0);
        }

        // Highlight selected tab
        clickedButton.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        clickedButton.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"];
        clickedButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
        clickedButton.BorderThickness = new Thickness(0, 0, 0, 2);

        // Show/hide content panels
        FilterBar.Visibility = tag == "Library" ? Visibility.Visible : Visibility.Collapsed;
        ContentScrollViewer.Visibility = tag == "Library" ? Visibility.Visible : Visibility.Collapsed;
        AlphabetStrip.Visibility = tag == "Library" ? Visibility.Visible : Visibility.Collapsed;
        RecommendedPanel.Visibility = tag == "Recommended" ? Visibility.Visible : Visibility.Collapsed;
        CollectionsPanel.Visibility = tag == "Collections" ? Visibility.Visible : Visibility.Collapsed;
    }
}
