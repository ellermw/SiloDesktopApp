using Microsoft.Extensions.DependencyInjection;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private async void TitleArt_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanEditTitleArt && TitleArtToggle.IsOn != ViewModel.ShowTitleArt)
        {
            await ViewModel.SaveTitleArtAsync(TitleArtToggle.IsOn);
            TitleArtToggle.IsOn = ViewModel.ShowTitleArt;
            TitleArtAllDevicesToggle.IsOn = ViewModel.TitleArtAllDevices;
        }
    }
    private async void TitleArtAllDevices_Toggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanEditTitleArt && TitleArtAllDevicesToggle.IsOn != ViewModel.TitleArtAllDevices)
        {
            await ViewModel.SaveTitleArtAsync(TitleArtAllDevicesToggle.IsOn, changeAllDevices: true);
            TitleArtToggle.IsOn = ViewModel.ShowTitleArt;
            TitleArtAllDevicesToggle.IsOn = ViewModel.TitleArtAllDevices;
        }
    }
    private async void TitleArtRetry_Click(object sender, RoutedEventArgs e)
    { ViewModel.TitleArtErrorMessage = null; await ViewModel.LoadTitleArtAsync(); }

    public async Task RefreshInheritedPreferencesAsync(string key)
    {
        if (!IsLoaded) return;
        var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.SettingsApi>();
        var context = api.CaptureContext();
        if (key == "ui.title_art") await ViewModel.LoadTitleArtAsync();
        else if (key == "ui.next_up_new_releases_first") await ViewModel.ReloadNewEpisodesPreferenceCommand.ExecuteAsync(null);
        else if (key == "ui.next_up_mode")
        {
            await ViewModel.LoadNextUpPreferenceAsync();
            if (!IsLoaded || !api.IsCurrentContext(context)) return;
            var suppressed = _suppressEvents; _suppressEvents = true;
            try { SelectComboBoxByTag(NextUpModeComboBox, ViewModel.NextUpMode); }
            finally { _suppressEvents = suppressed; }
        }
    }
}
