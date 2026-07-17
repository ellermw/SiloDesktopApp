using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.ViewModels.Admin;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminMarkerHistoryPage : Page
{
    public AdminMarkerHistoryViewModel ViewModel { get; } =
        App.Services.GetRequiredService<AdminMarkerHistoryViewModel>();

    public AdminMarkerHistoryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout(ActualWidth);
        await ViewModel.LoadAsync();
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        var compact = width < 760;
        var narrow = width < 600;
        var gutter = narrow ? 16 : compact ? 24 : 40;
        MarkerHistoryPageShell.Padding = new Thickness(gutter, compact ? 24 : 32, gutter, 40);
        MarkerHistoryTitle.FontSize = narrow ? 34 : compact ? 40 : 48;
        Grid.SetRow(MarkerHistoryHeaderActions, compact ? 1 : 0);
        Grid.SetColumn(MarkerHistoryHeaderActions, compact ? 0 : 1);
        Grid.SetColumnSpan(MarkerHistoryHeaderActions, compact ? 2 : 1);
        MarkerHistoryHeaderActions.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async void LimitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LimitComboBox.SelectedItem is not ComboBoxItem item ||
            !int.TryParse(item.Tag?.ToString(), out var limit))
            return;

        ViewModel.Limit = limit;
        await ViewModel.LoadAsync();
    }

    private void ItemLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string itemId } && !string.IsNullOrWhiteSpace(itemId))
            Frame.Navigate(typeof(ItemDetailPage), itemId);
    }
}
