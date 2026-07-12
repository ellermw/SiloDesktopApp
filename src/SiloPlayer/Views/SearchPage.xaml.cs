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

    public SearchPage()
    {
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
        this.InitializeComponent();

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
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Reset to empty state
        ViewModel.Query = "";
        ViewModel.Results.Clear();
        ViewModel.PeopleResults.Clear();
        ViewModel.OutsideLibraryResults.Clear();
        await ViewModel.LoadMediaScopeAsync();
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
        PeopleSection.Visibility = ViewModel.PeopleResults.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
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

        // Debounce: wait 400ms after last keystroke before searching
        _searchDebounce?.Stop();
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
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
}
