using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }

    public SearchPage()
    {
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
        this.InitializeComponent();

        ResultsRepeater.ItemsSource = ViewModel.Results;
        PeopleRepeater.ItemsSource = ViewModel.PeopleResults;

        ViewModel.Results.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdateResultsState);
        };

        ViewModel.PeopleResults.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdatePeopleSection);
        };

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.TotalCount) ||
                args.PropertyName == nameof(ViewModel.IsLoading))
            {
                DispatcherQueue.TryEnqueue(UpdateResultsState);
            }
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Reset to empty state
        ViewModel.Query = "";
        ViewModel.Results.Clear();
        ViewModel.PeopleResults.Clear();
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
            var totalDisplay = mediaCount + peopleCount;
            ResultCountText.Text = totalDisplay.ToString("N0");
            ResultCountLabel.Text = totalDisplay == 1 ? "result" : "results";

            NoResultsText.Visibility = mediaCount == 0 && peopleCount == 0 && !ViewModel.IsLoading
                ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdatePeopleSection();
    }

    private void UpdatePeopleSection()
    {
        PeopleSection.Visibility = ViewModel.PeopleResults.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Sync query from whichever search box was used
        if (sender is TextBox textBox)
        {
            ViewModel.Query = textBox.Text;

            // Sync the other search box
            if (textBox == SearchBox && ResultsSearchBox.Text != textBox.Text)
                ResultsSearchBox.Text = textBox.Text;
            else if (textBox == ResultsSearchBox && SearchBox.Text != textBox.Text)
                SearchBox.Text = textBox.Text;
        }

        // Transition to results state if text was entered
        if (!string.IsNullOrWhiteSpace(ViewModel.Query))
        {
            EmptyState.Visibility = Visibility.Collapsed;
            ResultsState.Visibility = Visibility.Visible;
            ResultsTitle.Text = $"Results for \"{ViewModel.Query}\"";
        }
        else
        {
            EmptyState.Visibility = Visibility.Visible;
            ResultsState.Visibility = Visibility.Collapsed;
            return;
        }

        await ViewModel.SearchCommand.ExecuteAsync(null);
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
        if (sender is Button btn && btn.Tag is int personId && personId > 0)
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<PersonDetailPage>(personId);
        }
    }
}
