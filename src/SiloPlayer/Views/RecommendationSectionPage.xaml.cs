using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class RecommendationSectionPage : Page
{
    public RecommendationSectionViewModel ViewModel { get; }

    private RecommendationSectionNavigationArgs? _args;
    private bool _loaded;

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

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.CancelLoad();
        base.OnNavigatedFrom(e);
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
        var loadTask = ViewModel.LoadAsync(_args);
        UpdatePageState();
        await loadTask;
        Bindings.Update();
        UpdatePageState();
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        await LoadSectionAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        App.Services.GetRequiredService<NavigationService>().GoBack();
    }

    private void UpdatePageState()
    {
        var hasError = !string.IsNullOrWhiteSpace(ViewModel.ErrorMessage);
        var hasItems = !hasError && ViewModel.Items.Count > 0;

        LoadingSkeleton.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ErrorState.Visibility = !ViewModel.IsLoading && hasError ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = !ViewModel.IsLoading && !hasError && !hasItems ? Visibility.Visible : Visibility.Collapsed;
        ItemsGrid.Visibility = !ViewModel.IsLoading && hasItems ? Visibility.Visible : Visibility.Collapsed;
        ItemCountLabel.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        ItemCountLabel.Text = ViewModel.Items.Count == 1 ? "1 title" : $"{ViewModel.Items.Count:N0} titles";
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;

        var gutter = width < 640 ? 16d : width < 1024 ? 24d : width < 1280 ? 40d : 48d;
        PageContent.Padding = new Thickness(gutter, width < 640 ? 24 : 32, gutter, 48);
        var columns = width < 640 ? 3 : width < 768 ? 4 : width < 1024 ? 5 : width < 1280 ? 6 : 7;
        ItemsLayout.MinItemWidth = Math.Max(96, Math.Floor((width - gutter * 2 - (columns - 1) * 16) / columns));
    }
}
