using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class RecommendationSectionPage : Page
{
    public RecommendationSectionViewModel ViewModel { get; }
    private readonly UICustomizationService _uiCustomizationService;

    private RecommendationSectionNavigationArgs? _args;
    private bool _loaded;
    private double _cardWidth = 165;

    public RecommendationSectionPage()
    {
        ViewModel = App.Services.GetRequiredService<RecommendationSectionViewModel>();
        _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
        this.InitializeComponent();
        LoadingSkeleton.ItemsSource = Enumerable.Range(0, 24).ToArray();
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
        _uiCustomizationService.Changed += UICustomization_Changed;
        _args = e.Parameter as RecommendationSectionNavigationArgs;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.CancelLoad();
        _uiCustomizationService.Changed -= UICustomization_Changed;
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
        ApplyCardLayout(e.NewSize.Width);
    }

    private void UICustomization_Changed(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => ApplyCardLayout(ActualWidth));

    private void ApplyCardLayout(double width)
    {
        if (width <= 0) return;

        var gutter = width < 640 ? 16d : width < 1024 ? 24d : width < 1280 ? 40d : 48d;
        PageContent.Padding = new Thickness(gutter, width < 640 ? 24 : 32, gutter, 48);
        var contentWidth = Math.Max(280, width - gutter * 2);
        var columns = _uiCustomizationService.GetPosterColumnCount(contentWidth);
        PageTitleText.FontSize = width < 640 ? 24 : 30;
        _cardWidth = Math.Max(96, Math.Floor((width - gutter * 2 - (columns - 1) * 16) / columns));
        ItemsLayout.MaximumRowsOrColumns = columns;
        ItemsLayout.MinItemWidth = _cardWidth;
        LoadingLayout.MaximumRowsOrColumns = columns;
        LoadingLayout.MinItemWidth = _cardWidth;

        for (var index = 0; index < ViewModel.Items.Count; index++)
            if (ItemsGrid.TryGetElement(index) is PosterCard card)
                card.SetCatalogGridLayout(_cardWidth);
        for (var index = 0; index < 24; index++)
            if (LoadingSkeleton.TryGetElement(index) is StackPanel skeleton)
                ResizeSkeleton(skeleton);
    }

    private void ItemsGrid_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is PosterCard card)
            card.SetCatalogGridLayout(_cardWidth);
    }

    private void LoadingSkeleton_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is StackPanel skeleton)
            ResizeSkeleton(skeleton);
    }

    private void ResizeSkeleton(StackPanel skeleton)
    {
        skeleton.Width = _cardWidth;
        if (skeleton.Children.FirstOrDefault() is FrameworkElement poster)
            poster.Height = _cardWidth * 1.5;
    }
}
