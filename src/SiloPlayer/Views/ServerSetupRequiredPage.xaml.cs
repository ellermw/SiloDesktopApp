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
    private CancellationTokenSource? _setupStatusCts;

    public ServerSetupRequiredPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        CancelSetupStatusRequest();
        _server = e.Parameter as ServerEntry;
        _serverUrl = _server?.Url ?? e.Parameter as string ?? "";
        ServerAddressText.Text = _serverUrl;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        CancelSetupStatusRequest();
        base.OnNavigatedFrom(e);
    }

    private async void OpenWebUi_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var launched = await Windows.System.Launcher.LaunchUriAsync(
                ServerWebUiUri.FromApiBase(_serverUrl));
            if (!launched)
                ShowError("Silo could not open the WebUI in your default browser.");
        }
        catch (Exception ex)
        {
            ShowError($"Silo could not open the WebUI: {ex.Message}");
        }
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        var requestCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _setupStatusCts, requestCts);
        previous?.Cancel();
        previous?.Dispose();
        var expectedServer = _server;
        var expectedServerUrl = _serverUrl;
        RetryButton.IsEnabled = false;
        StatusText.Visibility = Visibility.Collapsed;
        try
        {
            var setup = await App.Services.GetRequiredService<AuthApi>()
                .GetSetupStatusAsync(requestCts.Token);
            if (requestCts.IsCancellationRequested ||
                !ReferenceEquals(Frame?.Content, this) ||
                !ReferenceEquals(_server, expectedServer) ||
                !string.Equals(_serverUrl, expectedServerUrl, StringComparison.Ordinal))
            {
                return;
            }

            if (setup.NeedsSetup)
            {
                ShowError("Setup is not complete yet. Finish it in the WebUI, then retry.");
                return;
            }

            App.Services.GetRequiredService<NavigationService>()
                .Navigate<LoginPage>(_server ?? (object)_serverUrl);
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!requestCts.IsCancellationRequested && ReferenceEquals(Frame?.Content, this))
                ShowError($"Silo could not check setup status: {ex.Message}");
        }
        finally
        {
            var ownsRequest = ReferenceEquals(
                    Interlocked.CompareExchange(ref _setupStatusCts, null, requestCts),
                    requestCts);
            if (ownsRequest)
            {
                requestCts.Dispose();
            }
            if (ownsRequest && ReferenceEquals(Frame?.Content, this))
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

    private void CancelSetupStatusRequest()
    {
        var pending = Interlocked.Exchange(ref _setupStatusCts, null);
        if (pending == null)
            return;

        pending.Cancel();
        pending.Dispose();
    }
}
