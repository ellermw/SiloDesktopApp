using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class FavoritesPage : Page
{
    public FavoritesViewModel ViewModel { get; }

    public FavoritesPage()
    {
        ViewModel = App.Services.GetRequiredService<FavoritesViewModel>();
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
