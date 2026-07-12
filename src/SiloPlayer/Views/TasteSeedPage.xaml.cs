using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.ViewModels;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class TasteSeedPage : Page
{
    public TasteSeedViewModel ViewModel { get; } = App.Services.GetRequiredService<TasteSeedViewModel>();
    private bool _returningFromSettings;

    public TasteSeedPage()
    {
        InitializeComponent();
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
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(SettingsPage), "Personalize");
        }
        else
        {
            App.Services.GetRequiredService<NavigationService>().Navigate<HomePage>();
        }
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
