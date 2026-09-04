using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class ServerSetupRequiredPage : Page
{
    private ServerEntry? _server;
    private string _serverUrl = "";

    public ServerSetupRequiredPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _server = e.Parameter as ServerEntry;
        _serverUrl = _server?.Url ?? e.Parameter as string ?? "";
        ServerAddressText.Text = _serverUrl;
    }

    private async void OpenWebUi_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(ServerWebUiUri.FromApiBase(_serverUrl));
        }
        catch (Exception ex)
        {
            ShowError($"Silo could not open the WebUI: {ex.Message}");
        }
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        RetryButton.IsEnabled = false;
        StatusText.Visibility = Visibility.Collapsed;
        try
        {
            var setup = await App.Services.GetRequiredService<AuthApi>().GetSetupStatusAsync();
            if (setup.NeedsSetup)
            {
                ShowError("Setup is not complete yet. Finish it in the WebUI, then retry.");
                return;
            }

            App.Services.GetRequiredService<NavigationService>()
                .Navigate<LoginPage>(_server ?? (object)_serverUrl);
        }
        catch (Exception ex)
        {
            ShowError($"Silo could not check setup status: {ex.Message}");
        }
        finally
        {
            RetryButton.IsEnabled = true;
        }
    }

    private void ChooseServer_Click(object sender, RoutedEventArgs e)
        => App.Services.GetRequiredService<NavigationService>().Navigate<ServerSelectPage>();

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }
}
