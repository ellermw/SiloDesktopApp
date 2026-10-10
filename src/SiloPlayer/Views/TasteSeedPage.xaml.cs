using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.ViewModels;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Controls;

namespace SiloPlayer.Views;

public sealed partial class TasteSeedPage : Page
{
    public TasteSeedViewModel ViewModel { get; } = App.Services.GetRequiredService<TasteSeedViewModel>();
    public IReadOnlyList<int> SkeletonItems { get; } = Enumerable.Range(0, 24).ToArray();
    private bool _returningFromSettings;

    public TasteSeedPage()
    {
        InitializeComponent();
        TasteTitleRow.Children.RemoveAt(0);
        TasteTitleRow.Children.Insert(0, WebUiIcon.Create("sparkles", 20, (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"]));
        SizeChanged += (_, _) => ReflowTastePicker();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _returningFromSettings = e.Parameter is true;
        SkipButton.Content = _returningFromSettings ? "Cancel" : "Skip";
        await ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void Item_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TasteSeedItemViewModel item })
            ViewModel.Toggle(item);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        RememberDismissed();
        ReturnFromPicker();
    }

    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        DoneButton.Content = "Saving…";
        if (await ViewModel.SubmitAsync())
        {
            RememberDismissed();
            ReturnFromPicker();
        }
        DoneButton.Content = "Done";
    }

    private void ItemsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (ItemsScrollViewer.ScrollableHeight - ItemsScrollViewer.VerticalOffset < 500 && ViewModel.HasMore)
            _ = ViewModel.LoadMoreAsync();
    }

    private void ReturnFromPicker()
    {
        if (_returningFromSettings)
        {
            Frame.Navigate(typeof(SettingsPage), "Playback");
        }
        else
        {
            App.Services.GetRequiredService<NavigationService>().Navigate<HomePage>();
        }
    }

    private double _tasteWidth = 147;
    private void TastePoster_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Image { Parent: Grid card } image) return;
        foreach (var fallback in card.Children.OfType<DefaultArtwork>())
        {
            fallback.Visibility = image.Source == null ? Visibility.Visible : Visibility.Collapsed;
            if (image.Source == null) _ = fallback.ShowThumbhashAsync();
            else _ = fallback.ObserveConvertedImageAsync(image);
        }
    }
    private void TastePoster_ImageOpened(object sender, RoutedEventArgs args)
    {
        if (sender is Image { Parent: Grid card })
            foreach (var fallback in card.Children.OfType<DefaultArtwork>()) fallback.Visibility = Visibility.Collapsed;
    }
    private void TastePoster_ImageFailed(object sender, ExceptionRoutedEventArgs args)
    {
        if (sender is not Image { Parent: Grid card } image) return;
        image.Source = null;
        foreach (var fallback in card.Children.OfType<DefaultArtwork>())
        { fallback.Visibility = Visibility.Visible; _ = fallback.ShowThumbhashAsync(); }
    }
    private void ReflowTastePicker()
    {
        var narrow = ActualWidth < 640;
        var horizontalPadding = narrow ? 16 : 24;
        TasteHeaderBorder.Padding = new Thickness(horizontalPadding, 16, horizontalPadding, 16);
        TasteBodyPanel.Padding = new Thickness(horizontalPadding, narrow ? 24 : 32, horizontalPadding, narrow ? 24 : 32);
        TasteTitleText.FontSize = narrow ? 20 : 24;
        TasteTitleText.LineHeight = narrow ? 28 : 32;
        var viewport = ItemsScrollViewer.ViewportWidth > 0 ? ItemsScrollViewer.ViewportWidth : ActualWidth;
        var width = Math.Min(1152, viewport) - horizontalPadding * 2;
        var columns = ActualWidth < 640 ? 3 : ActualWidth < 768 ? 4 : ActualWidth < 1024 ? 5 : ActualWidth < 1280 ? 6 : 7;
        _tasteWidth = Math.Max(40, (width - (columns - 1) * 12) / columns);
        foreach (var repeater in new[] { TasteItemsGrid, TasteSkeletonGrid })
        {
            if (repeater.Layout is UniformGridLayout layout) { layout.MaximumRowsOrColumns = columns; layout.MinItemWidth = _tasteWidth; layout.MinItemHeight = _tasteWidth * 1.5; }
            for (var index = 0; index < ViewModel.Items.Count; index++) if (repeater.TryGetElement(index) is FrameworkElement element) SizeTasteCard(element);
        }
        // Source flex items stay inline at460px; only very small windows need
        // wrapping, instead of forcing a second actions row at the640 breakpoint.
        var wrapActions = ActualWidth < 360;
        TasteHeaderGrid.ColumnDefinitions[1].Width = wrapActions ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(TasteHeaderActions, wrapActions ? 0 : 1); Grid.SetRow(TasteHeaderActions, wrapActions ? 1 : 0);
        TasteHeaderActions.Margin = wrapActions ? new Thickness(0, 12, 0, 0) : new Thickness(0);
    }
    private void TasteCard_Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        SizeTasteCard(args.Element);
        if (args.Element is Button { Content: Grid card })
            foreach (var selected in card.Children.OfType<Border>().Where(border => border.Child is FontIcon))
                selected.Child = WebUiIcon.Create("check", 16, (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentForegroundBrush"]);
    }
    private void SizeTasteCard(UIElement element)
    {
        if (element is Button { Content: FrameworkElement card }) { card.Width = _tasteWidth; card.Height = _tasteWidth * 1.5; }
        else if (element is FrameworkElement skeleton) { skeleton.Width = _tasteWidth; skeleton.Height = _tasteWidth * 1.5; }
    }

    private void RememberDismissed()
    {
        var auth = App.Services.GetRequiredService<AuthService>();
        if (string.IsNullOrWhiteSpace(auth.SelectedProfileId)) return;
        var service = App.Services.GetRequiredService<SettingsService>();
        var settings = service.Load();
        if (!settings.TasteSeedDismissedProfileIds.Contains(auth.SelectedProfileId, StringComparer.Ordinal))
            settings.TasteSeedDismissedProfileIds.Add(auth.SelectedProfileId);
        service.Save(settings);
    }
}
