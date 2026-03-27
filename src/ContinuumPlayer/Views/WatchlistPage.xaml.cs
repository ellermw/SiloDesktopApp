using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class WatchlistPage : Page
{
    public WatchlistViewModel ViewModel { get; }

    public WatchlistPage()
    {
        ViewModel = App.Services.GetRequiredService<WatchlistViewModel>();
        this.InitializeComponent();

        PosterRepeater.ItemsSource = ViewModel.Items;

        ViewModel.Items.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                EmptyState.Visibility = ViewModel.Items.Count == 0 && !ViewModel.IsLoading
                    ? Visibility.Visible : Visibility.Collapsed;
            });
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);

        EmptyState.Visibility = ViewModel.Items.Count == 0 && !ViewModel.IsLoading
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
