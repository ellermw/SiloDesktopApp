using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class RecommendationSectionPage : Page
{
    public RecommendationSectionViewModel ViewModel { get; }

    private RecommendationSectionNavigationArgs? _args;
    private bool _loaded;

    public string ItemCountText
        => ViewModel.Items.Count == 1 ? "1 item" : $"{ViewModel.Items.Count:N0} items";

    public RecommendationSectionPage()
    {
        ViewModel = App.Services.GetRequiredService<RecommendationSectionViewModel>();
        this.InitializeComponent();
        ViewModel.Items.CollectionChanged += (_, _) => Bindings.Update();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.Title))
                Bindings.Update();
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _args = e.Parameter as RecommendationSectionNavigationArgs;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        await LoadSectionAsync();
    }

    private async Task LoadSectionAsync()
    {
        if (_args == null) return;
        await ViewModel.LoadAsync(_args);
        Bindings.Update();
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        await LoadSectionAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        App.Services.GetRequiredService<NavigationService>().GoBack();
    }
}
