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
}
