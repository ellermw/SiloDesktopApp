using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
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

        ViewModel.Results.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                EmptyState.Visibility = ViewModel.Results.Count == 0 && string.IsNullOrWhiteSpace(ViewModel.Query)
                    ? Visibility.Visible : Visibility.Collapsed;
                ResultCountText.Text = ViewModel.Results.Count > 0
                    ? $"{ViewModel.TotalCount} results" : "";
            });
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SearchBox.Focus(FocusState.Programmatic);
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.Query = SearchBox.Text;
        await ViewModel.SearchCommand.ExecuteAsync(null);
    }
}
